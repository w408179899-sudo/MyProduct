using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed partial class GroceryNpcSaleSequence
{
    private async Task<NpcSaleOutcome?> RunInitialJunkAsync(AccountWorkerContext context, string npcName,
        BagCleanupState state, GroceryStageContinuation continuation, Action<string> report)
    {
        try
        {
            return await new NpcSaleSequence(input, delay).RunAsync(context, npcName, state,
                selectCandidates: static _ => Array.Empty<InventoryItemSnapshot>(), registerAllJunkFirst: true);
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested &&
            ex is ClickNotSentException or NpcJunkRegistrationNotConfirmedException)
        {
            // Both failures precede sale submission. Never replay an uncertain
            // sale, suppress protection failures, or restart the trip.
            var registrationUnconfirmed = ex is NpcJunkRegistrationNotConfirmedException;
            continuation.RecordFailure("automatic_junk", ex);
            var actions = new TradingActions(input, context.Snapshots, context.StopToken, delay);
            await actions.Alive();
            var target = (await context.Snapshots.ReadLockedTargetAsync().WaitAsync(context.StopToken)).Value;
            var ui = (await context.Snapshots.ReadNpcTradeAsync().WaitAsync(context.StopToken)).Value;
            Require(target.HasTarget && target.Name == npcName && target.ServerObjectId != 0,
                "杂货步骤恢复时 NPC 已改变。");
            if (registrationUnconfirmed || !ui.IsSelling || ui.NpcServerObjectId != target.ServerObjectId || ui.OtherModalOpen || ui.Basket.Count != 0)
            {
                // Also close on registration timeout with an empty publication:
                // the game may still be processing that click. Cancel before continuing.
                Require(await new TradingWindowRecovery(input, context.Snapshots, delay)
                    .TryCloseAsync(report, context.StopToken, includeShop: false), "杂货登记未取消，不能继续后续出售。");
                var npc = new BagCleanupNpcInteractor(input);
                Check(await npc.SelectConfiguredNpcAsync(context, npcName));
                Check(await npc.OpenDialogAsync(context));
            }
            report(registrationUnconfirmed
                ? "杂货登录未确认，取消本次登记，继续出售丢弃集合和出售集合"
                : "初始杂货点击未发出，跳过该步骤，继续出售丢弃集合和出售集合");
            context.Logger.Info("grocery_shop.automatic_junk.continuing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = registrationUnconfirmed ? "registration_not_confirmed" : "click_not_sent",
                ["registeredCount"] = ui.Basket.Count
            });
            return null;
        }
    }
}
