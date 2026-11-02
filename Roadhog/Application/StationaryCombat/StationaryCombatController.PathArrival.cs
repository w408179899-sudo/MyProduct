using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private static void MarkPathFollowArrivalLocked(PathFollowPollState poller)
    {
        if (poller.StopRequested || poller.TargetIndex < 0 || poller.ArrivalObserved ||
            poller.Local.Position is not { } position) return;
        var distance = StationaryCombatTargetSelector.HorizontalDistance(position, poller.TargetPoint);
        if (!(distance <= poller.ReachDistance)) return;
        // Arrival needs coordinates only; a missing camera yaw must not delay W-up.
        poller.ArrivalObserved = true;
        poller.HasArrived = true;
        poller.ArrivedDistance = distance;
        poller.ArrivedTargetIndex = poller.TargetIndex;
        poller.ArrivedSnapshot = poller.MetricsSnapshot;
    }

    private async Task<long> SetPathFollowTargetAsync(AccountWorkerContext context, StationaryCombatState state,
        PathFollowPollState poller, Vector3Snapshot target, double reachDistance, PathFollowTurnOptions options)
    {
        // Serialize retargeting with W-up: an old arrival cannot release a new segment's W-down.
        await state.ForwardInputSync.WaitAsync(context.StopToken).ConfigureAwait(false);
        try
        {
            SetPathFollowPollTarget(poller, 0, target, reachDistance, options);
            lock (poller.SyncRoot) return poller.TargetVersion;
        }
        finally { state.ForwardInputSync.Release(); }
    }

    private static bool CanStartPathForward(StationaryCombatState state, PathFollowPollState? poller, long? version)
    {
        if (poller is null || !ReferenceEquals(state.PathFollowPoller, poller)) return false;
        lock (poller.SyncRoot)
            return !poller.StopRequested && !poller.Cancellation.IsCancellationRequested &&
                !poller.ArrivalObserved && (!version.HasValue || version.Value == poller.TargetVersion);
    }

    private async Task StopArrivedPathForwardAsync(AccountWorkerContext context, StationaryCombatState state,
        PathFollowPollState poller, long version)
    {
        await state.ForwardInputSync.WaitAsync(poller.Cancellation.Token).ConfigureAwait(false);
        try
        {
            double distance;
            double reachDistance;
            Vector3Snapshot target;
            lock (poller.SyncRoot)
            {
                if (!ReferenceEquals(state.PathFollowPoller, poller) || poller.StopRequested ||
                    poller.Cancellation.IsCancellationRequested || poller.TargetVersion != version ||
                    !poller.ArrivalObserved || !poller.IsMoving || !state.IsMovingForward) return;
                distance = poller.ArrivedDistance;
                reachDistance = poller.ReachDistance;
                target = poller.TargetPoint;
            }

            // No camera lock, combat scan or maintenance work in this input lane.
            var result = await _input.KeyUpAsync("W", poller.Cancellation.Token).ConfigureAwait(false);
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.input.w_up_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["source"] = "path_arrival",
                    ["error"] = result.Error
                });
                return; // Keep the arrival latched and retry on the next position poll.
            }

            state.IsMovingForward = false;
            lock (poller.SyncRoot)
            {
                poller.IsMoving = false;
                poller.ArrivalStoppedForward = true;
            }
            context.Logger.Info("stationary_combat.input.w_up", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["source"] = "path_arrival",
                ["distance"] = distance, ["reachDistance"] = reachDistance,
                ["targetX"] = target.X, ["targetY"] = target.Y, ["targetZ"] = target.Z,
                ["targetVersion"] = version
            });
        }
        finally { state.ForwardInputSync.Release(); }
    }

    private async Task StopForwardInputAsync(AccountWorkerContext context, StationaryCombatState state,
        string source, CancellationToken cancellationToken)
    {
        await state.ForwardInputSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!state.IsMovingForward) return;
            var result = await _input.KeyUpAsync("W", cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.input.w_up_failed", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["source"] = source, ["error"] = result.Error });
                return;
            }
            state.IsMovingForward = false;
            SetPathFollowMoving(state, false);
            state.ResetCombatApproachStuckTracking();
            context.Logger.Info("stationary_combat.input.w_up", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["source"] = source });
        }
        finally { state.ForwardInputSync.Release(); }
    }
}
