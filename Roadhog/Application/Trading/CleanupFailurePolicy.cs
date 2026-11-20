using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;

namespace Roadhog.Application.Trading;

internal enum CleanupFailureDisposition
{
    Propagate, EndSession, EndGroceryTrip, ResumeAfterCombat, AbandonDiscard, AbandonWorkflow
}

internal static class CleanupFailurePolicy
{
    // Order matters: a session exit or combat interruption must not become a transaction retry.
    public static CleanupFailureDisposition Classify(CleanupRequest request, Exception error, CancellationToken stop)
    {
        if (stop.IsCancellationRequested || error is SnapshotReadBudgetExceededException)
            return CleanupFailureDisposition.Propagate;
        if (error is CleanupSessionInterruptionException) return CleanupFailureDisposition.EndSession;
        if (request.ItemTransfer && request.ItemTransferReturn is not null && error is GroceryTripInterruptedException)
            return CleanupFailureDisposition.ResumeAfterCombat;
        if (request.GroceryShop && !request.GroceryNpcStageStarted && error is
            GroceryTripInterruptedException or GroceryTripDepartureFailedException or
            CleanupCombatInterruptionException or CleanupDeathInterruptionException)
            return CleanupFailureDisposition.EndGroceryTrip;
        if (!request.StandaloneShop && !request.GroceryShop && !request.ItemTransfer && error is
            CleanupCombatInterruptionException or CleanupDeathInterruptionException)
            return CleanupFailureDisposition.ResumeAfterCombat;
        if (!request.StandaloneShop && request.PreparationStage == CleanupPreparationStage.Discarding)
            return CleanupFailureDisposition.AbandonDiscard;
        return CleanupFailureDisposition.AbandonWorkflow;
    }
}
