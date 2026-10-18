using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Application.Travel;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

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

    public async Task RunAsync(AccountWorkerContext context, Action<string> report)
    {
        var settings = context.Config.ScriptSettings!;
        var token = context.StopToken;
        foreach (var name in new[] { settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName })
            if (!string.IsNullOrWhiteSpace(name) && !settings.Maintenance.BagCleanupExcludedItemNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(name.Trim());
        var actions = new TradingActions(input, context.Snapshots, token, delay);
        var continuation = new GroceryStageContinuation(context, input, delay);
        SharedPathDocument? route = null;
        var state = new BagCleanupState();
        state.Start(0, 0);
        var saleCompleted = false;
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
            if (arrived) saleCompleted = await continuation.TryAsync("npc_sale", async () =>
            {
                var npc = new BagCleanupNpcInteractor(input);
                Check(await npc.SelectConfiguredNpcAsync(context, route!.CleanupNpcName));
                Check(await npc.OpenDialogAsync(context));
                var beforeSale = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var skipped = BagCleanupItemMatcher.SelectDiscardItems(beforeSale, settings.Maintenance)
                    .Where(i => !i.CanSellToNpc).ToArray();
                if (skipped.Length > 0)
                {
                    report("保留不可出售物品：" + string.Join("、", skipped.Select(i => i.Name).Distinct()));
                    context.Logger.Info("grocery_shop.npc_sale.skipped", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["items"] = skipped.Select(i => new { i.Name, i.InstanceId, i.ItemType, i.CanSellToNpc }).ToArray() });
                }
                report("先登录全部杂货并出售，再按原丢弃规则每三条一批出售");
                var outcome = await new NpcSaleSequence(input, delay).RunAsync(context, route.CleanupNpcName, state,
                    bag => GroceryNpcSalePolicy.SelectCandidates(bag, settings.Maintenance), batchSize: 3, registerAllJunkFirst: true);
                Require(outcome == NpcSaleOutcome.AllConfiguredItemsSold,
                    "商人限卖或未成交，保留剩余物品并继续后续流程。");
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
            report(continuation.FailureCount == 0 ? "NPC 处理完成，准备停止并重新启动脚本" : "NPC 处理结束，失败步骤已跳过，准备重新启动脚本");
            context.Logger.Info("grocery_shop.npc_sale.complete", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["npc"] = route?.CleanupNpcName,
                    ["batches"] = state.SellBatchCount, ["moneyDelta"] = state.TotalMoneyDelta,
                    ["saleCompleted"] = saleCompleted, ["skippedStages"] = continuation.FailureCount });
        }
        finally { await continuation.ReleaseAsync(); }
    }
}
