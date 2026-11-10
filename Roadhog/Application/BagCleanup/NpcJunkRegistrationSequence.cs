using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

internal sealed class NpcJunkRegistrationNotConfirmedException(Exception inner)
    : TimeoutException("全部杂货登录未确认，尚未提交出售。", inner);

internal static class NpcJunkRegistrationSequence
{
    public static async Task<InventoryItemSnapshot[]> RunAsync(AccountWorkerContext context, TradingActions actions,
        Func<Task<NpcTradeSnapshot>> readUi, Func<NpcTradeSnapshot, bool> selling, int confirmationTimeoutMs = 8000)
    {
        var inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
        await actions.Click(readUi, s => s.RegisterAllJunkButton, s => selling(s) && s.InventoryOpen && s.Basket.Count == 0);
        // Confirm the action from the official basket; an empty publication does
        // not acknowledge registration. Never submit an empty basket or repeat the click.
        context.Logger.Info("bag_cleanup.sell.junk_registration.started", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName, ["timeoutMs"] = confirmationTimeoutMs });
        async Task<NpcTradeSnapshot> ReadRegistration()
        {
            var observed = await readUi();
            TradingActions.Require(selling(observed) && observed.InventoryOpen, "登录全部杂货后商人页面变化。");
            return observed;
        }
        NpcTradeSnapshot ui;
        try { ui = await actions.Wait(ReadRegistration, s => s.Basket.Count > 0, confirmationTimeoutMs); }
        catch (TradingActions.ConfirmationTimeoutException ex)
        { throw new NpcJunkRegistrationNotConfirmedException(ex); }
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
