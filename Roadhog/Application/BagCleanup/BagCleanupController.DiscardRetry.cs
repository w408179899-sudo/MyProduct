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
        state.ClearDiscardTarget();
        state.Advance(BagCleanupStep.PrepareDiscardInventory);
        context.Logger.Info("bag_cleanup.discard.retry", new Dictionary<string, object?>
        { ["account"] = context.Config.AccountName, ["discardedCount"] = state.DiscardedItemCount });
        return BagCleanupTickResult.Running("discard_retry_started");
    }
}
