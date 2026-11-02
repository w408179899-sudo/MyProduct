using Roadhog.Application.Workers;

namespace Roadhog.Application.BagCleanup;

public sealed partial class BagCleanupController
{
    private async Task<BagCleanupTickResult> TickWaitDiscardRetryAsync(AccountWorkerContext context, BagCleanupState state)
    {
        var interrupted = await TryAbortDiscardIfUnsafeAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null) return interrupted;
        if (DateTimeOffset.Now - state.StepStartedAt < TimeSpan.FromSeconds(2))
            return BagCleanupTickResult.Running("discard_retry_pending");

        var cancel = await _discarder.CancelPendingDiscardAsync(context).ConfigureAwait(false);
        var close = await _discarder.CloseInventoryWindowIfOpenAsync(context).ConfigureAwait(false);
        if (!cancel.Success || !close.Success)
            return await FailDiscardLocallyAsync(context, state, "discard_retry_ui_busy", cancel.Error ?? close.Error ?? "丢弃窗口尚未恢复。").ConfigureAwait(false);

        var ui = (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(context.StopToken)).Value;
        InventoryDiscardActions.RequireIdle(ui);
        await ReconcileCancelledDiscardAsync(context, state).ConfigureAwait(false);
        state.Advance(state.DiscardLimitReached ? BagCleanupStep.CloseDiscardInventory : BagCleanupStep.PrepareDiscardInventory);
        context.Logger.Info("bag_cleanup.discard.retry", new Dictionary<string, object?>
        { ["account"] = context.Config.AccountName, ["discardedCount"] = state.DiscardedItemCount });
        return BagCleanupTickResult.Running("discard_retry_started");
    }

    private static async Task ReconcileCancelledDiscardAsync(AccountWorkerContext context, BagCleanupState state)
    {
        // A confirm input may succeed in the game and then report failure. Count the removed
        // target before forgetting it, so recovery cannot grant an extra item beyond the limit.
        if (state.DiscardTarget is { } target)
        {
            var bag = (await context.Snapshots.ReadInventoryAsync(state.DiscardInventoryVersion)
                .WaitAsync(context.StopToken)).Value;
            if (!bag.Any(item => item.InstanceId == target.InstanceId))
            {
                state.MarkDiscardVerified();
                context.Logger.Info("bag_cleanup.discard.retry_verified", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["instanceId"] = target.InstanceId, ["discardedCount"] = state.DiscardedItemCount });
            }
        }
        state.ClearDiscardTarget();
    }
}
