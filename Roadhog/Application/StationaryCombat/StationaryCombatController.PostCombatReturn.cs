using System.Collections.Concurrent;
using Roadhog.Application.Radar;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private readonly ConcurrentDictionary<string, (bool Enabled, double Distance)> _postCombatReturnSettings =
        new(StringComparer.OrdinalIgnoreCase);

    public void SetPostCombatReturnSettings(string accountName, bool enabled, double distance) =>
        _postCombatReturnSettings[accountName] = (enabled, NormalizePostCombatReturnDistance(distance));

    private (bool Enabled, double Distance) ResolvePostCombatReturnSettings(AccountWorkerContext context)
    {
        if (_postCombatReturnSettings.TryGetValue(context.Config.AccountName, out var settings)) return settings;
        var combat = context.Config.ScriptSettings?.Combat;
        return (combat?.ReturnHomeAfterCombat == true, NormalizePostCombatReturnDistance(
            combat?.ReturnHomeAfterCombatDistance ?? CombatScriptSettings.DefaultReturnHomeAfterCombatDistance));
    }

    private static double NormalizePostCombatReturnDistance(double distance) =>
        double.IsFinite(distance) && distance >= 0 ? distance : CombatScriptSettings.DefaultReturnHomeAfterCombatDistance;

    private static bool IsPostCombatReturnMode(AccountWorkerContext context, StationaryCombatState state) =>
        context.Config.ScriptSettings is { MainMode: AccountMainMode.CustomCombat, CombatMode: AccountCombatMode.Stationary } &&
        state.TopLevelState == StationaryCombatTopLevelState.Normal && !state.StartupRecoveryActive &&
        !state.StartupTownReturnPending && !state.CleanupReturnToCombatActive && !state.ChannelSwitchPending;

    private void RequestPostCombatReturn(AccountWorkerContext context, StationaryCombatState state, LockedTargetSnapshot killedTarget)
    {
        if (killedTarget.IsLockedMonster && !killedTarget.IsAlive &&
            IsPostCombatReturnMode(context, state) && ResolvePostCombatReturnSettings(context).Enabled)
            state.PostCombatReturnPending = true;
    }

    private async Task<TimeSpan?> TryTickPostCombatReturnAsync(AccountWorkerContext context,
        SemiAutoSkillPlan plan, SemiAutoCombatState semiAutoState, StationaryCombatState state,
        PlayerSnapshot player, Vector3Snapshot home, double radius, double distanceFromHome,
        RadarObstacleScriptSettings radarSettings)
    {
        var settings = ResolvePostCombatReturnSettings(context);
        if (!settings.Enabled || !IsPostCombatReturnMode(context, state))
        {
            if (state.PostCombatReturningHome && !state.Fighting)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                state.ResetReturnHomeStuckTracking();
            }
            state.ClearPostCombatReturn();
            return null;
        }

        // Loot and maintenance have finished before this boundary. Use this tick's player position.
        if (!state.PostCombatReturningHome)
        {
            if (!state.PostCombatReturnPending || state.Fighting) return null;
            state.PostCombatReturnPending = false;
            if (distanceFromHome <= settings.Distance || distanceFromHome <= ReturnStopDistance) return null;
            state.PostCombatReturningHome = true;
            state.ReturningHome = false;
            state.ClearTarget();
            state.ClearNoTargetRest();
            state.ResetReturnHomeStuckTracking();
            context.Logger.Info("stationary_combat.post_combat_return.started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["homeDistance"] = distanceFromHome,
                ["triggerDistance"] = settings.Distance, ["stopDistance"] = ReturnStopDistance
            });
        }

        context.StopToken.ThrowIfCancellationRequested();
        await StopSoloJumpAsync(state, "post_combat_return").ConfigureAwait(false);
        StopNextTargetPreAim(context, state, "post_combat_return", clearCandidate: true);
        await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
        if (!state.Fighting && distanceFromHome <= ReturnStopDistance)
        {
            state.ClearPostCombatReturn();
            state.ClearTarget();
            state.ResetReturnHomeStuckTracking();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            context.Logger.Info("stationary_combat.post_combat_return.completed", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["homeDistance"] = distanceFromHome });
            return IdleDelay;
        }

        var position = player.Position!.Value;
        var defense = await TryHandleRecoveryDefenseTargetAsync(context, plan, semiAutoState, state, player,
            position, home, radius, distanceFromHome, "post_combat_return", allowRevivePathClear: false).ConfigureAwait(false);
        if (defense.HasValue) return defense.Value;

        // An attacker may disappear before it can be locked. Discard only that candidate and resume the return.
        if (state.CandidateEntityId != 0 || state.CurrentTargetEntityId != 0) state.ClearTarget();
        if (await TryHandleStationaryMaintenanceAsync(context, plan, semiAutoState, state, player).ConfigureAwait(false))
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return IdleDelay;
        }
        semiAutoState.ResetAttackKeyPressThrottle();
        var navigation = await ResolveObstacleNavigationAsync(context, state, position, home,
            RadarNavigationPurpose.ReturnHome, 0, radarSettings, ReturnStopDistance).ConfigureAwait(false);
        if (navigation?.Action == RadarNavigationAction.Unreachable)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            LogRadarNavigation(context, state, navigation, "post_combat_return");
            return IdleDelay;
        }
        var destination = navigation is null ? home : ToVector3(navigation.Destination, position.Z);
        await PathFollowStepAsync(context, state, player, destination,
            navigation?.ReachDistanceMeters ?? ReturnStopDistance).ConfigureAwait(false);
        if (navigation is not null) LogRadarNavigation(context, state, navigation, "post_combat_return");
        await TryJumpReturnHomeIfStuckAsync(context, state, position, home, distanceFromHome,
            "post_combat_return").ConfigureAwait(false);
        return MoveTickDelay;
    }
}
