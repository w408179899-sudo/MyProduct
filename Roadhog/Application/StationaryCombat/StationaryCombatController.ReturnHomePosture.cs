using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private async Task<bool> WaitForStandingBeforeReturnHomeAsync(AccountWorkerContext context,
        SemiAutoCombatState semiAutoState, StationaryCombatState state, PlayerSnapshot player)
    {
        // The maintenance flag can already be cleared while the stand animation is still pending.
        // Only the published posture permits movement, never an input acknowledgement.
        if (!player.IsResting) return false;

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        state.ResetReturnHomeStuckTracking();
        semiAutoState.ResetAttackKeyPressThrottle();

        var now = DateTimeOffset.Now;
        if (!semiAutoState.ShouldPressMaintenanceKey(NoTargetRestExitKey, now, TimeSpan.FromSeconds(1)))
            return true;

        context.StopToken.ThrowIfCancellationRequested();
        semiAutoState.MarkMaintenanceKeyAttempted(NoTargetRestExitKey, now);
        var result = await _input.PressKeyAsync(NoTargetRestExitKey, ReadKeyHoldDuration(context), context.StopToken)
            .ConfigureAwait(false);
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["key"] = NoTargetRestExitKey,
            ["stanceFlags"] = player.StanceFlags, ["motionMode"] = player.MotionMode,
            ["waitingForStand"] = true
        };
        if (result.Success)
            context.Logger.Info("stationary_combat.return_home.stand_pressed", fields);
        else
        {
            fields["error"] = result.Error;
            context.Logger.Warn("stationary_combat.return_home.stand_failed", fields);
        }
        return true;
    }
}
