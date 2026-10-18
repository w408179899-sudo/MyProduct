using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
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
    public async Task<SharedPathDocument> ValidateAsync(AccountWorkerContext context, int requiredScrollCount = 1)
    {
        var settings = context.Config.ScriptSettings!;
        var name = settings.Paths.BagCleanupReturnItemName;
        Require(!string.IsNullOrWhiteSpace(name), "请选择清包路径的回程物品。");
        var loaded = await paths.LoadAsync(settings.Paths.MaintenancePathName, context.StopToken);
        Require(loaded.Success && loaded.Value is { MapId: > 0, PointCount: > 0 }, "NPC 出售需要带地图和入口坐标的清包路径。");
        Require(!string.IsNullOrWhiteSpace(loaded.Value!.CleanupNpcName), "清包路径缺少 NPC 名称。");
        var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
        var quantity = bag.Where(i => !i.IsEquipped && i.Slot >= 0 && string.Equals(i.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Sum(i => (long)i.Count);
        Require(quantity >= requiredScrollCount, $"清包回程物品不足，需要 {requiredScrollCount} 个：{name}");
        return loaded.Value.Clone();
    }

    public async Task RunAsync(AccountWorkerContext context, Action<string> report)
    {
        var route = await ValidateAsync(context);
        var settings = context.Config.ScriptSettings!;
        var token = context.StopToken;
        foreach (var name in new[] { settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName })
            if (!string.IsNullOrWhiteSpace(name) && !settings.Maintenance.BagCleanupExcludedItemNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(name.Trim());
        var actions = new TradingActions(input, context.Snapshots, token, delay);
        try
        {
            await new GroceryReturnSequence(input, delay, clock).RunAsync(context, route,
                settings.Paths.BagCleanupReturnItemName, report, "清包回程", "grocery_shop.npc_return.retry");
            report("清包回程已确认，沿路径前往商人：" + route.CleanupNpcName);
            Check(await executePath(context, route.Name, route.Points.Select(p => p.ToVector3()).ToArray()));
            var npc = new BagCleanupNpcInteractor(input);
            Check(await npc.SelectConfiguredNpcAsync(context, route.CleanupNpcName));
            Check(await npc.OpenDialogAsync(context));
            var state = new BagCleanupState();
            state.Start(0, 0);
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
            await actions.Key("Space");
            await actions.Wait(async () => (await context.Snapshots.ReadNpcTradeAsync().WaitAsync(token)).Value,
                ui => !ui.IsOpen && !ui.DialogOpen && !ui.OtherModalOpen);
            Require(outcome == NpcSaleOutcome.AllConfiguredItemsSold,
                "商人未成交，剩余物品已保留；请检查售卖限额或物品限制后再启动。");
            report("NPC 出售完成，准备停止并重新启动脚本");
            context.Logger.Info("grocery_shop.npc_sale.complete", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["npc"] = route.CleanupNpcName,
                    ["batches"] = state.SellBatchCount, ["moneyDelta"] = state.TotalMoneyDelta });
        }
        finally { await actions.Reset(); }
    }
}
