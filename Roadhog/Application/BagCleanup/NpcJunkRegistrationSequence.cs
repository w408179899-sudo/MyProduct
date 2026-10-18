using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

internal static class NpcJunkRegistrationSequence
{
    public static async Task<InventoryItemSnapshot[]> RunAsync(AccountWorkerContext context, TradingActions actions,
        Func<Task<NpcTradeSnapshot>> readUi, Func<NpcTradeSnapshot, bool> selling)
    {
        var inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
        await actions.Click(readUi, s => s.RegisterAllJunkButton, s => selling(s) && s.InventoryOpen && s.Basket.Count == 0);
        // sell_junk fills the local basket in its click handler. Allow the input event to be processed.
        await actions.Pause(200);
        var ui = await readUi();
        TradingActions.Require(selling(ui) && ui.InventoryOpen, "登录全部杂货后商人页面变化。");
        var settings = context.Config.ScriptSettings!;
        var protectedNames = settings.Maintenance.BagCleanupExcludedItemNames.Concat(new[]
        {
            settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName
        });
        var batch = ui.Basket.Select(entry =>
        {
            var item = inventory.SingleOrDefault(i => i.InstanceId == entry.InstanceId && i.TemplateId == entry.TemplateId && i.Count == entry.Quantity);
            TradingActions.Require(item is { IsEquipped: false, Slot: >= 0, CanSellToNpc: true }, "全部杂货登录了无法核对的背包物品，停止出售。");
            TradingActions.Require(!protectedNames.Any(name => CleanupTradePolicy.Matches(item!.Name, name)),
                "全部杂货包含白名单或回程物品，停止出售：" + item!.Name);
            return item!;
        }).ToArray();
        context.Logger.Info("bag_cleanup.sell.junk_registered", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["count"] = batch.Length,
            ["items"] = batch.Select(i => new { i.Name, i.InstanceId, i.TemplateId, i.Count }).ToArray()
        });
        return batch;
    }
}
