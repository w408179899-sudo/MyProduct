using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Application.Travel;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public enum GroceryNpcSaleOutcome { AllItemsSold, SaleRejected, ItemsRemain, Failed }

/// <summary>The grocery-only route and NPC stage. Ordinary cleanup owns its existing return policy.</summary>
public sealed class GroceryNpcSaleSequence(IKeyboardInput input, ISharedPathStore paths,
    BagCleanupPathExecutor executePath, Func<int, CancellationToken, Task>? delay = null,
    Func<DateTimeOffset>? clock = null)
{
    public async Task<SharedPathDocument> LoadRouteAsync(AccountWorkerContext context)
    {
        var settings = context.Config.ScriptSettings!;
        var loaded = await paths.LoadAsync(settings.Paths.MaintenancePathName, context.StopToken);
        Require(loaded.Success && loaded.Value is { MapId: > 0, PointCount: > 0 }, "NPC 出售需要带地图和入口坐标的清包路径。");
        Require(!string.IsNullOrWhiteSpace(loaded.Value!.CleanupNpcName), "清包路径缺少 NPC 名称。");
        return loaded.Value.Clone();
    }

    public async Task<GroceryNpcSaleOutcome> RunAsync(AccountWorkerContext context, Action<string> report)
    {
        var settings = context.Config.ScriptSettings!;
        var token = context.StopToken;
        foreach (var name in new[] { settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName })
            if (!string.IsNullOrWhiteSpace(name) && !settings.Maintenance.BagCleanupExcludedItemNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(name.Trim());
        InventoryItemSnapshot[] SaleInventory(IEnumerable<InventoryItemSnapshot> bag) => bag.Where(i =>
            !string.Equals(i.Name, settings.Paths.BagCleanupReturnItemName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(i.Name, settings.Paths.GroceryReturnItemName.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var actions = new TradingActions(input, context.Snapshots, token, delay);
        var continuation = new GroceryStageContinuation(context, input, delay);
        SharedPathDocument? route = null;
        var state = new BagCleanupState();
        state.Start(0, 0);
        var outcome = GroceryNpcSaleOutcome.Failed;
        try
        {
            var arrived = await continuation.TryAsync("npc_travel", async () =>
            {
                route = await LoadRouteAsync(context);
                await new GroceryReturnSequence(input, delay, clock).RunAsync(context, route,
                    settings.Paths.BagCleanupReturnItemName, report, "清包回程", "grocery_shop.npc_return.retry");
                report("清包回程已确认，沿路径前往商人：" + route.CleanupNpcName);
                Check(await CleanupPathTravel.FollowAsync(context, route, executePath));
            });
            if (arrived) await continuation.TryAsync("npc_sale", async () =>
            {
                void Reject(string stage)
                {
                    outcome = GroceryNpcSaleOutcome.SaleRejected;
                    report("商人未成交，保留剩余物品，结束 NPC 出售并转入摆摊");
                    context.Logger.Info("grocery_shop.npc_sale.rejected", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["stage"] = stage });
                }
                var npc = new BagCleanupNpcInteractor(input);
                Check(await npc.SelectConfiguredNpcAsync(context, route!.CleanupNpcName));
                Check(await npc.OpenDialogAsync(context));
                // Keep the game's initial junk registration/sale even when both configured
                // sets are empty. Re-read the bag afterwards so overlapping goods are not sold twice.
                report("先执行杂货全部登录并出售，再处理丢弃集合和出售集合");
                var junk = await new NpcSaleSequence(input, delay).RunAsync(context, route.CleanupNpcName, state,
                    selectCandidates: static _ => Array.Empty<InventoryItemSnapshot>(), registerAllJunkFirst: true);
                if (junk == NpcSaleOutcome.NoSale)
                {
                    Reject("AutomaticJunk");
                    return;
                }
                var beforeSale = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var skipped = GroceryNpcSalePolicy.SelectPendingItems(SaleInventory(beforeSale), settings.Maintenance)
                    .Where(i => !i.CanSellToNpc).ToArray();
                if (skipped.Length > 0)
                {
                    report("保留不可出售物品：" + string.Join("、", skipped.Select(i => i.Name).Distinct()));
                    context.Logger.Info("grocery_shop.npc_sale.skipped", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["items"] = skipped.Select(i => new { i.Name, i.InstanceId, i.ItemType, i.CanSellToNpc }).ToArray() });
                }
                foreach (var stage in new[] { GroceryNpcSaleStage.DiscardEquipment, GroceryNpcSaleStage.DiscardOther, GroceryNpcSaleStage.Sell })
                {
                    IReadOnlyList<InventoryItemSnapshot> Candidates(IReadOnlyList<InventoryItemSnapshot> bag) =>
                        GroceryNpcSalePolicy.SelectCandidates(SaleInventory(bag), settings.Maintenance, stage);
                    var candidates = Candidates((await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value);
                    if (candidates.Count == 0) continue;
                    var batchSize = stage == GroceryNpcSaleStage.Sell ? 1 : 3;
                    report(stage switch
                    {
                        GroceryNpcSaleStage.DiscardEquipment => "先出售丢弃集合中的装备，每三条一批",
                        GroceryNpcSaleStage.DiscardOther => "出售丢弃集合中的其他物品，每三条一批",
                        _ => "按正常顺序出售标记为出售的物品"
                    });
                    context.Logger.Info("grocery_shop.npc_sale.stage", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["stage"] = stage.ToString(), ["count"] = candidates.Count });
                    // Separate stages prevent the normal price sort from mixing the two discard groups.
                    var result = await new NpcSaleSequence(input, delay).RunAsync(context, route.CleanupNpcName, state, Candidates, batchSize);
                    if (result == NpcSaleOutcome.NoSale)
                    {
                        Reject(stage.ToString());
                        return;
                    }
                }
                var remaining = GroceryNpcSalePolicy.SelectPendingItems(SaleInventory(
                    (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value), settings.Maintenance);
                outcome = remaining.Count == 0 ? GroceryNpcSaleOutcome.AllItemsSold : GroceryNpcSaleOutcome.ItemsRemain;
            });
            await continuation.CloseWindowsAsync();
            if (arrived)
            {
                var purchased = await continuation.TryAsync("arrow_purchase", async () =>
                {
                    await actions.Pause(1000);
                    await new NpcArrowPurchaseSequence(input, delay).RunAsync(context, route!.CleanupNpcName, report);
                });
                if (!purchased) await continuation.CloseWindowsAsync();
            }
            report(outcome switch
            {
                GroceryNpcSaleOutcome.AllItemsSold => "NPC 两组物品已售空，准备重新启动脚本",
                GroceryNpcSaleOutcome.SaleRejected => "NPC 未成交，准备按现有名单摆摊",
                GroceryNpcSaleOutcome.ItemsRemain => "NPC 出售后仍有待处理物品，准备按现有名单摆摊",
                _ => "NPC 出售未完成，放弃本次清包"
            });
            context.Logger.Info("grocery_shop.npc_sale.complete", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["npc"] = route?.CleanupNpcName,
                    ["batches"] = state.SellBatchCount, ["moneyDelta"] = state.TotalMoneyDelta,
                    ["saleCompleted"] = outcome == GroceryNpcSaleOutcome.AllItemsSold,
                    ["outcome"] = outcome.ToString(), ["skippedStages"] = continuation.FailureCount });
            return outcome;
        }
        finally { await continuation.ReleaseAsync(); }
    }
}
