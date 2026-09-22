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
    Func<AccountConfig, ISharedAccountConfiguration?>? sharedConfigurationFactory = null)
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
        // Automatic requests first exhaust local discard work. A full bag at enqueue time
        // does not authorize a town trip after that work has recovered capacity.
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
        if (flow.NpcCleanup && request.AllowNpcSell && BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, settings.Maintenance).Count > 0)
            Require(!string.IsNullOrWhiteSpace((await Load(settings.Paths.MaintenancePathName)).CleanupNpcName), "清包路径缺少 NPC 名字。");
        if (flow.Auction) await Load(settings.Paths.AuctionPathName);
        if (flow.TransferGold || flow.PersonalShop)
        {
            await Load(settings.Paths.StallPathName); await Load(settings.Paths.RevivePathName);
            Require(!string.IsNullOrWhiteSpace(settings.Paths.TownReturnKey), "摆摊区域结束后需要回城按键和复活路径。");
        }
        if (flow.TransferGold) Require(!string.IsNullOrWhiteSpace(flow.WarehouseName) && !string.IsNullOrWhiteSpace(flow.WarehouseSelectionKey), "请填写仓库角色名和选仓库号按键。");
        if (!request.TownReturnCompleted)
        {
            Require(!string.IsNullOrWhiteSpace(settings.Paths.BagCleanupTownReturnKey) || !string.IsNullOrWhiteSpace(settings.Paths.TownReturnKey), "清包前需要配置清包回城按键。");
            await Load(settings.Paths.RevivePathName);
        }
        context.Logger.Info("cleanup_workflow.start", new Dictionary<string, object?> { ["account"] = context.Config.AccountName, ["manual"] = request.Manual, ["stages"] = flow.Describe() });
        try
        {
            var cleanup = new BagCleanupController(input, paths, executePath);
            request.PreparationStage = !request.TownReturnCompleted
                ? CleanupPreparationStage.ReturningToTown : flow.NpcCleanup ? CleanupPreparationStage.Discarding : CleanupPreparationStage.None;
            await actions.Reset();
            if (!request.TownReturnCompleted)
            {
                var entryPath = flow.NpcCleanup && request.AllowNpcSell &&
                    BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, settings.Maintenance).Count > 0
                    ? settings.Paths.MaintenancePathName : flow.Auction ? settings.Paths.AuctionPathName
                    : flow.TransferGold || flow.PersonalShop ? settings.Paths.StallPathName : settings.Paths.RevivePathName;
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
                await Follow(settings.Paths.AuctionPathName);
                var auctionPath = await Load(settings.Paths.AuctionPathName);
                await new AuctionTradingSequence(input, journal).RunAsync(context.Snapshots, context.Config.AccountName, settings.Maintenance, Report, token, auctionPath.AuctionNpcName);
                await Follow(settings.Paths.AuctionPathName, reverse: true);
            }
            if (flow.TransferGold || flow.PersonalShop)
            {
                // Freeze quantities before buying, so purchased warehouse goods cannot enter this run's stall.
                inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var stallPlan = inventory.Select(i => (Item: i, Rule: CleanupTradePolicy.Rule(i, settings.Maintenance, false)))
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
