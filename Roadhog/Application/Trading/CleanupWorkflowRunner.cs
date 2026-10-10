using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed partial class CleanupWorkflowRunner(IKeyboardInput input, ISharedPathStore paths,
    BagCleanupPathExecutor executePath, IAuctionListingJournal journal,
    Func<AccountConfig, ISharedAccountConfiguration?>? sharedConfigurationFactory = null,
    IGroceryShopSuccessStore? grocerySuccessStore = null,
    Func<int, CancellationToken, Task>? groceryDelay = null, Func<DateTimeOffset>? groceryClock = null)
{
    public async Task RunAsync(AccountWorkerContext source, CleanupRequest request, Func<AccountWorkerContext, Task>? returnToCombat = null)
    {
        if (sharedConfigurationFactory?.Invoke(source.Config) is { } shared)
            await SharedConfigurationRefresh.CaptureCleanupAsync(shared, request, source.StopToken).ConfigureAwait(false);
        var context = source.ForCleanup(request.Settings); var token = context.StopToken;
        context.WorkflowOwnsCleanup = true;
        var settings = context.Config.ScriptSettings!; var flow = settings.Maintenance.CleanupWorkflow;
        var actions = new TradingActions(input, context.Snapshots, token);
        var documents = new Dictionary<string, SharedPathDocument>();
        async Task<SharedPathDocument> Load(string name)
        {
            Require(!string.IsNullOrWhiteSpace(name), "请配置所选流程的路径。");
            if (documents.TryGetValue(name, out var cached)) return cached;
            var result = await paths.LoadAsync(name, token);
            Require(result.Success && result.Value is { PointCount: > 0 }, "路径无法加载：" + name + "，" + result.Error);
            return documents[name] = result.Value!.Clone();
        }
        async Task Follow(string name, bool reverse = false)
        {
            var document = await Load(name);
            var points = document.Points.Select(p => p.ToVector3()).ToArray();
            if (reverse) Array.Reverse(points);
            Report((reverse ? "原路返回：" : "前往：") + name);
            Check(await executePath(context, name, points)); token.ThrowIfCancellationRequested();
        }
        void Report(string text)
        {
            context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, text);
        }
        // Backpack grocery requests first exhaust local discard; scheduled/manual departures
        // bypass that gate. Every completed grocery sale retains final discard before restart.
        if (request.GroceryShop)
        {
            await RunGroceryShopAsync(context, request, Report);
            return;
        }
        if (request.StandaloneShop)
        {
            try
            {
                await DiscountedPersonalShopWorkflow.RunAsync(context.Snapshots, settings.Maintenance,
                    plan => new ConfiguredPersonalShopSequence(input).RunAsync(context.Snapshots, plan, Report, token, flow.StandaloneShopDiscount),
                    () =>
                    {
                        Report("全部售罄，准备停止并重新启动脚本");
                        return Task.CompletedTask;
                    }, Report, token);
            }
            finally { await actions.Reset(); }
            return;
        }
        var usesAuctionScroll = flow.Auction && !string.IsNullOrWhiteSpace(settings.Paths.AuctionReturnItemName);
        settings.Paths.AuctionReturnItemName = (settings.Paths.AuctionReturnItemName ?? string.Empty).Trim();
        settings.Paths.StallReturnItemName = (settings.Paths.StallReturnItemName ?? string.Empty).Trim();
        var preservedScrollNames = new List<string>();
        if (usesAuctionScroll) preservedScrollNames.Add(settings.Paths.AuctionReturnItemName);
        if ((flow.TransferGold || flow.PersonalShop) && !string.IsNullOrWhiteSpace(settings.Paths.StallReturnItemName))
            preservedScrollNames.Add(settings.Paths.StallReturnItemName);
        // Preserve the selected scrolls before automatic cleanup can discard locally.
        foreach (var name in preservedScrollNames)
        {
            if (!settings.Maintenance.BagCleanupExcludedItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(name);
        }
        if (!request.Manual && !request.TownReturnCompleted)
        {
            bool needsTown;
            try
            {
                await actions.Reset();
                needsTown = await PrepareAutomaticCleanupAsync(context, request, Report);
            }
            finally { await actions.Reset(); }
            if (!needsTown)
            {
                Report("原地清包结束，继续挂机");
                context.Logger.Info("cleanup_workflow.complete", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["fullCleanup"] = false });
                return;
            }
        }
        // Validate all town stages before recalling. Manual requests also validate before discarding.
        var inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        var hasNpcSale = flow.NpcCleanup && request.AllowNpcSell &&
            BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, settings.Maintenance).Count > 0;
        var hasWarehouseStage = flow.TransferGold || flow.PersonalShop;
        var needsInitialTownReturn = !request.TownReturnCompleted &&
            (hasNpcSale || flow.Auction && !usesAuctionScroll || !hasWarehouseStage && !usesAuctionScroll);
        if (hasNpcSale)
            Require(!string.IsNullOrWhiteSpace((await Load(settings.Paths.MaintenancePathName)).CleanupNpcName), "清包路径缺少 NPC 名字。");
        if (flow.Auction)
        {
            var destination = await Load(settings.Paths.AuctionPathName);
            if (usesAuctionScroll)
            {
                Require(destination.MapId is > 0, "拍卖行路径需要录制地图及卷轴落点入口。");
                Require(GroceryReturnSequence.FindScroll(inventory, settings.Paths.AuctionReturnItemName) is not null,
                    "背包没有配置的拍卖行回程卷轴：" + settings.Paths.AuctionReturnItemName);
                await Load(settings.Paths.RevivePathName);
                Require(!string.IsNullOrWhiteSpace(settings.Paths.TownReturnKey), "拍卖行卷轴回程结束后需要回城按键和复活路径。");
            }
        }
        if (flow.TransferGold || flow.PersonalShop)
        {
            var destination = await Load(settings.Paths.StallPathName);
            Require(destination.MapId is > 0, "转移到仓库号路径需要录制地图及卷轴落点入口。");
            Require(!string.IsNullOrWhiteSpace(settings.Paths.StallReturnItemName), "请在转移到仓库号路径配置回程卷轴。");
            Require(GroceryReturnSequence.FindScroll(inventory, settings.Paths.StallReturnItemName) is not null,
                "背包没有配置的仓库回程卷轴：" + settings.Paths.StallReturnItemName);
            await Load(settings.Paths.RevivePathName);
            Require(!string.IsNullOrWhiteSpace(settings.Paths.TownReturnKey), "摆摊区域结束后需要回城按键和复活路径。");
        }
        if (flow.TransferGold) Require(!string.IsNullOrWhiteSpace(flow.WarehouseName) && !string.IsNullOrWhiteSpace(flow.WarehouseSelectionKey), "请填写仓库角色名和选仓库号按键。");
        if (needsInitialTownReturn)
        {
            Require(!string.IsNullOrWhiteSpace(settings.Paths.BagCleanupTownReturnKey) || !string.IsNullOrWhiteSpace(settings.Paths.TownReturnKey), "清包前需要配置清包回城按键。");
            await Load(settings.Paths.RevivePathName);
        }
        context.Logger.Info("cleanup_workflow.start", new Dictionary<string, object?> { ["account"] = context.Config.AccountName, ["manual"] = request.Manual, ["stages"] = flow.Describe() });
        try
        {
            var cleanup = new BagCleanupController(input, paths, executePath);
            request.PreparationStage = needsInitialTownReturn
                ? CleanupPreparationStage.ReturningToTown : flow.NpcCleanup ? CleanupPreparationStage.Discarding : CleanupPreparationStage.None;
            await actions.Reset();
            if (needsInitialTownReturn)
            {
                var entryPath = hasNpcSale
                    ? settings.Paths.MaintenancePathName : flow.Auction ? settings.Paths.AuctionPathName
                    : settings.Paths.RevivePathName;
                await cleanup.ReturnToTownRequestedAsync(context, Report, entryPath);
                request.TownReturnCompleted = true;
            }
            request.FullCleanupStarted = true;
            if (request.Manual && flow.NpcCleanup)
            {
                request.PreparationStage = CleanupPreparationStage.Discarding;
                await cleanup.RunDiscardRequestedAsync(context, Report);
            }
            request.PreparationStage = CleanupPreparationStage.None;
            if (flow.NpcCleanup && request.AllowNpcSell) await cleanup.RunSellRequestedAsync(context, Report);
            if (flow.Auction)
            {
                if (usesAuctionScroll)
                {
                    await new GroceryReturnSequence(input, groceryDelay, groceryClock).RunAsync(context,
                        await Load(settings.Paths.AuctionPathName), settings.Paths.AuctionReturnItemName, Report,
                        "拍卖行回程", "cleanup_workflow.auction_return.retry");
                    request.TownReturnCompleted = true;
                }
                await Follow(settings.Paths.AuctionPathName);
                var auctionPath = await Load(settings.Paths.AuctionPathName);
                await new AuctionTradingSequence(input, journal).RunAsync(context.Snapshots, context.Config.AccountName,
                    settings.Maintenance, Report, token, auctionPath.AuctionNpcName, preservedScrollNames);
                await Follow(settings.Paths.AuctionPathName, reverse: true);
                if (usesAuctionScroll && !hasWarehouseStage)
                    await cleanup.ReturnToReviveRequestedAsync(context, Report);
            }
            if (flow.TransferGold || flow.PersonalShop)
            {
                await new GroceryReturnSequence(input, groceryDelay, groceryClock).RunAsync(context,
                    await Load(settings.Paths.StallPathName), settings.Paths.StallReturnItemName, Report,
                    "仓库回程", "cleanup_workflow.warehouse_return.retry");
                request.TownReturnCompleted = true;
                // Freeze quantities before buying, so purchased warehouse goods cannot enter this run's stall.
                inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var stallPlan = inventory.Where(i => !preservedScrollNames.Contains(i.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(i => (Item: i, Rule: CleanupTradePolicy.Rule(i, settings.Maintenance, false)))
                    .Where(p => p.Rule?.EffectiveUnitPrice is > 0).Select(p => new PlannedShopItem(p.Item, checked((ulong)p.Rule!.EffectiveUnitPrice!.Value))).ToArray();
                await Follow(settings.Paths.StallPathName);
                if (flow.TransferGold) await new WarehousePurchaseSequence(input).RunAsync(context.Snapshots, flow, Report, token);
                if (flow.PersonalShop) await new ConfiguredPersonalShopSequence(input).RunAsync(context.Snapshots, stallPlan, Report, token);
                Report("交易完成，回城后走复活路径返回挂机点");
                await cleanup.ReturnToReviveRequestedAsync(context, Report);
            }
            if (request.TownReturnCompleted || flow.TransferGold || flow.PersonalShop)
            {
                Report("清包结束，沿复活路径返回挂机点");
                if (returnToCombat != null) await returnToCombat(context);
                else await Follow(settings.Paths.RevivePathName);
            }
            token.ThrowIfCancellationRequested();
            Report("清包流程完成，继续挂机");
            context.Logger.Info("cleanup_workflow.complete", new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
        }
        finally { await actions.Reset(); }
    }
    private static double Distance(Vector3Snapshot a, Vector3Snapshot b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
