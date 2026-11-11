using Roadhog.Application.Radar;
using Roadhog.Application.Workers;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private static readonly TimeSpan ReturnNavigationFailureTimeout = TimeSpan.FromSeconds(15);

    private async Task HandleUnreachableReturnAsync(AccountWorkerContext context, StationaryCombatState state,
        RadarNavigationDecision navigation, string phase)
    {
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        LogRadarNavigation(context, state, navigation, phase);
        context.StopToken.ThrowIfCancellationRequested();
        if (state.Fighting || state.ChannelSwitchPending || state.StartupTownReturnPending ||
            state.TopLevelState != StationaryCombatTopLevelState.Normal ||
            DateTimeOffset.UtcNow < state.ReturnNavigationRetryAfter)
        {
            state.ReturnNavigationFailure.Reset();
            return;
        }

        var elapsed = state.ReturnNavigationFailure.Observe(
            navigation.MapId, navigation.Destination, phase, _townReturnTime);
        if (elapsed < ReturnNavigationFailureTimeout) return;

        state.ReturnNavigationFailure.Reset();
        context.Logger.Warn("stationary_combat.return_home.navigation_failed_recovery", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["phase"] = phase,
            ["mapId"] = navigation.MapId, ["reason"] = navigation.Reason,
            ["homeX"] = navigation.Destination.X, ["homeY"] = navigation.Destination.Y,
            ["failedForMs"] = elapsed.TotalMilliseconds,
            ["thresholdMs"] = ReturnNavigationFailureTimeout.TotalMilliseconds
        });
        await RecoverBlockedReturnRouteCoreAsync(context, state, navigation.MapId).ConfigureAwait(false);
    }
}
