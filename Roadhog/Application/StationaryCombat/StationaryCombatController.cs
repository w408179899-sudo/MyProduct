using Roadhog.Application.Input;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Radar;
using Roadhog.Application.Team;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Radar;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatController : ITeamTacticalTargetRangePolicy
{
    private static readonly TimeSpan TabInterval = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan MoveTickDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DeathRevivePreClickPause = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TargetTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NoTargetRestKeyRetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PostLootNoTargetActionDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GatherPollDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GatherAttemptRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GatherFailureSuppression = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GatherApproachJumpRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NoKillTownReturnHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan DefaultNoKillTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultNoKillTownReturnSettleDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DefaultNoKillRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupTownReturnHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan DefaultStartupTownReturnSettleDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FightSoftRestartApproachTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TemporaryTargetSwitchGuardGrace = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultSmartPreAimResultTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SmartPreAimTeamSnapshotRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SmartPreAimTeamSnapshotRetention = TimeSpan.FromSeconds(5);
    private const double ReturnStopDistance = 2.0D;
    private const double AcquireDistance = 25.0D;
    private const double FightSoftRestartApproachDistance = 5.0D;
    private const double GatherKeyActivationDistance = 20.0D;
    private const double TargetLeashExtraDistance = 5.0D;
    private const double PreLockFaceYawToleranceDegrees = 30.0D;
    private const double SmartPreAimFaceYawToleranceDegrees = 10.0D;
    private const int SmartPreAimSwitchConfirmationThreshold = 3;
    private const int SmartPreAimCandidateDiagnosticSampleCount = 8;
    private const double DefaultPathFollowReachDistance = 5.0D;
    private const double DefaultStartupTownReturnDistance = 500.0D;
    private const double DefaultStartupTownReturnMinDistance = 5.0D;
    private const double DefaultYawPixelsPerDegree = 11.0D;
    private const double DefaultPitchPixelsPerDegree = 13.0D;
    private const double DefaultSmartPreAimSwitchDistanceMargin = 2.0D;
    private const int DefaultReviveClickX = PathScriptSettings.DefaultDeathReviveClickX;
    private const int DefaultReviveClickY = PathScriptSettings.DefaultDeathReviveClickY;
    private const int DefaultReviveFallbackClickX = 550;
    private const int DefaultReviveFallbackClickY = 375;
    private const int DefaultReviveThirdClickX = 690;
    private const int DefaultReviveThirdClickY = 468;
    private const int DefaultPostReviveScrollCount = 30;
    private const int DefaultPostReviveScrollDelta = -1;
    private const int DefaultPostCombatMaintenanceRoundLimit = 8;
    private const int CameraTurnRecoveryFailureThreshold = 2;
    private const int CameraTurnRecoveryReleaseMs = 80;
    private const int CameraTurnRecoveryWarmupMs = 80;
    private const string NoTargetRestEnterKey = "OemComma";
    private const string NoTargetRestExitKey = "X";
    private const ushort NpcEntityType = 3;

    private readonly IKeyboardInput _input;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly ISharedPathStore? _pathStore;
    private readonly StationaryObstacleNavigator? _obstacleNavigator;
    private readonly RadarLiveSnapshotRegistry? _radarSnapshots;
    private readonly BagCleanupController? _bagCleanup;
    private readonly TacticalMarkCoordinator _tacticalMark;
    private readonly SemaphoreSlim _cameraTurnInputSync = new(1, 1);

    public StationaryCombatController(
        IKeyboardInput input,
        SemiAutoCombatController semiAuto,
        ISharedPathStore? pathStore = null,
        StationaryObstacleNavigator? obstacleNavigator = null,
        RadarLiveSnapshotRegistry? radarSnapshots = null)
    {
        _input = input;
        _semiAuto = semiAuto;
        _tacticalMark = new TacticalMarkCoordinator(input);
        _pathStore = pathStore;
        _obstacleNavigator = obstacleNavigator;
        _radarSnapshots = radarSnapshots;
        _bagCleanup = pathStore is null
            ? null
            : new BagCleanupController(input, pathStore, ExecutePathOnceAsync);
    }

    public async Task SuspendForFixedChannelCorrectionAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopSoloJumpAsync(state, "fixed_channel_correction").ConfigureAwait(false);
        StopNextTargetPreAim(context, state, "fixed_channel_correction", clearCandidate: true);
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        state.ObstacleNavigation.Reset();
        await AbortDiscardForExternalInterruptionIfActiveAsync(
                context,
                state,
                "fixed_channel_correction")
            .ConfigureAwait(false);
        if (state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
            state.PrepareForFixedChannelCorrection(DateTimeOffset.Now);
        }
    }

    public async Task<TeamTacticalTargetRangeDecision> EvaluateNewTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState combatState,
        LockedTargetSnapshot target)
    {
        var scriptSettings = context.Config.ScriptSettings;
        var mainMode = scriptSettings?.MainMode ?? context.Config.MainMode;
        var combatMode = scriptSettings?.CombatMode ?? context.Config.CombatMode;
        if (mainMode != AccountMainMode.CustomCombat ||
            combatMode != AccountCombatMode.Stationary)
        {
            return TeamTacticalTargetRangeDecision.NotApplicable();
        }

        var radius = Math.Max(1.0D, scriptSettings?.Combat?.StationaryCombatRadius ?? 1.0D);
        if (target.Position is null)
        {
            return TeamTacticalTargetRangeDecision.Rejected(
                "target_position_unavailable",
                null,
                radius);
        }

        var homeResult = await TryResolveStationaryHomeAsync(context, combatState).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            return TeamTacticalTargetRangeDecision.Rejected(
                "stationary_home_unavailable",
                null,
                radius,
                homeResult.Error);
        }

        var distanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(
            target.Position.Value,
            homeResult.Value.Position);
        return distanceFromHome <= radius
            ? TeamTacticalTargetRangeDecision.Inside(distanceFromHome, radius)
            : TeamTacticalTargetRangeDecision.Rejected(
                "outside_stationary_radius",
                distanceFromHome,
                radius);
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        UpdateMaintenanceRestJumpPause(state, semiAutoState);
        if (!state.Fighting && state.CandidateEntityId == 0)
        {
            await StopSoloJumpAsync(state, "stationary_no_active_target").ConfigureAwait(false);
        }

        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var radarSettings = _obstacleNavigator?.ResolveSettings(
                                context.Config.AccountName,
                                combat.RadarObstacleAvoidance)
                            ?? new RadarObstacleScriptSettings();
        if (!radarSettings.Enabled)
        {
            state.ObstacleNavigation.Reset();
        }
        if (!IsSmartPreAimEnabled(context))
        {
            StopNextTargetPreAim(context, state, "disabled", clearCandidate: true);
            state.ClearSmartPreAimHandoff(clearDisplacedTargetGuard: true);
        }

        var gatherSettings = context.Config.ScriptSettings?.Gather ?? new GatherScriptSettings();
        if (!gatherSettings.StationaryPriorityEnabled)
        {
            if (state.Gather.Active)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
            }

            state.Gather.Reset();
            state.CachedGatherSnapshot = null;
        }

        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            StopNextTargetPreAim(context, state, "stationary_home_missing", clearCandidate: true);
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogThrottled(context, state, "stationary_combat.position.missing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["revivePathName"] = GetRevivePathName(context),
                ["reason"] = homeResult.Error
            });
            return IdleDelay;
        }

        var homeResolution = homeResult.Value;
        var home = homeResolution.Position;
        var radius = Math.Max(1.0D, combat.StationaryCombatRadius);

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        _radarSnapshots?.PublishPlayer(context.Config.AccountName, player);
        var playerPosition = player.Position!.Value;
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        await RefreshLocalCombatSideAsync(context, plan, state, player).ConfigureAwait(false);

        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
            StopNextTargetPreAim(context, state, "player_dead", clearCandidate: true);
            await AbortDiscardForExternalInterruptionIfActiveAsync(context, state, "death_recovery")
                .ConfigureAwait(false);
            state.EnterDeathRecovery(DateTimeOffset.Now);
            semiAutoState.ResetAttackKeyPressThrottle();
            context.Logger.Warn("stationary_combat.death.detected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["x"] = Math.Round(playerPosition.X, 2),
                ["y"] = Math.Round(playerPosition.Y, 2),
                ["z"] = Math.Round(playerPosition.Z, 2)
            });
        }

        if (state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
            (!state.DeathRecovery.RevivePathLeaderSiphonActive || player.IsDead))
        {
            await StopSoloJumpAsync(state, "stationary_death_recovery").ConfigureAwait(false);
            if (!ShouldPreserveDeathRecoverySmartPreAim(state, player))
            {
                StopNextTargetPreAim(context, state, "death_recovery", clearCandidate: true);
            }

            return await TickDeathRecoveryAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    home,
                    playerDistanceFromHome,
                    followRevivePath: true)
                .ConfigureAwait(false);
        }

        if (state.StartupTownReturnPending)
        {
            StopNextTargetPreAim(context, state, "startup_town_return", clearCandidate: true);
            return await TickStartupTownReturnAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    radius)
                .ConfigureAwait(false);
        }

        if (state.LootAfterKill.Active)
        {
            await StopSoloJumpAsync(state, "stationary_loot").ConfigureAwait(false);
            return await TickLootAfterKillAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player)
                .ConfigureAwait(false);
        }

        var noKillRecoveryDelay = await TickNoKillRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player)
            .ConfigureAwait(false);
        if (noKillRecoveryDelay is not null)
        {
            await StopSoloJumpAsync(state, "stationary_no_kill_recovery").ConfigureAwait(false);
            StopNextTargetPreAim(context, state, "no_kill_recovery", clearCandidate: true);
            return noKillRecoveryDelay.Value;
        }

        var temporaryTargetSwitchGuard = !state.Fighting &&
                                         state.ShouldGuardTemporaryTargetSwitch(DateTimeOffset.Now);
        var stationaryGatherWorkAvailable = false;
        if (!state.Fighting &&
            !temporaryTargetSwitchGuard &&
            gatherSettings.StationaryPriorityEnabled)
        {
            stationaryGatherWorkAvailable = await HasStationaryGatherWorkAsync(
                    context,
                    state,
                    home,
                    gatherSettings,
                    radius)
                .ConfigureAwait(false);
            if (state.NoTargetRestActive && stationaryGatherWorkAvailable)
            {
                await CancelNoTargetRestAtHomeAsync(
                        context,
                        semiAutoState,
                        state,
                        player,
                        "gather_available")
                    .ConfigureAwait(false);
                if (state.NoTargetRestActive)
                {
                    return IdleDelay;
                }
            }
        }

        WorldObjectSnapshot? noTargetRestWakeTarget = null;
        WorldObjectSnapshot? preMaintenanceDefenseTarget = null;
        var leaderRestGuardAllowsSit = true;
        var skipMaintenanceThisTick = false;
        if (temporaryTargetSwitchGuard)
        {
            noTargetRestWakeTarget = await SelectMaintenanceDefenseTargetAsync(
                    context,
                    state,
                    playerPosition)
                .ConfigureAwait(false);
            noTargetRestWakeTarget ??= await SelectTargetAsync(
                    context,
                    state,
                    playerPosition,
                    home,
                    radius,
                    combat.ContestMonster)
                .ConfigureAwait(false);
        }

        if (state.NoTargetRestActive)
        {
            noTargetRestWakeTarget = await TickNoTargetRestAtHomeAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    home,
                    radius,
                    combat,
                    playerDistanceFromHome)
                .ConfigureAwait(false);
            if (state.NoTargetRestActive && noTargetRestWakeTarget is null)
            {
                return IdleDelay;
            }
        }
        else
        {
            if (semiAutoState.IsMaintenanceResting)
            {
                if (state.Fighting && state.CurrentTargetIsMaintenanceDefense)
                {
                    if (!await TryInterruptMaintenanceRestForDefenseAsync(
                            context,
                            semiAutoState,
                            state,
                            player,
                            "active_defense_target")
                        .ConfigureAwait(false))
                    {
                        return IdleDelay;
                    }

                    skipMaintenanceThisTick = true;
                }
                else
                {
                    noTargetRestWakeTarget = await SelectMaintenanceDefenseTargetAsync(
                            context,
                            state,
                            playerPosition)
                        .ConfigureAwait(false);
                    if (noTargetRestWakeTarget?.Position is not null)
                    {
                        if (!await TryInterruptMaintenanceRestForDefenseAsync(
                                context,
                                semiAutoState,
                                state,
                                player,
                                "defense_target_detected")
                            .ConfigureAwait(false))
                        {
                            return IdleDelay;
                        }

                        skipMaintenanceThisTick = true;
                    }
                }
            }

            var mayEnterMaintenanceRest = ShouldAttemptSitMaintenance(context, player);
            var mayEnterNoTargetRest = !temporaryTargetSwitchGuard &&
                                       !stationaryGatherWorkAvailable &&
                                       CanUseNoTargetRestAtHome(combat) &&
                                       playerDistanceFromHome <= ReturnStopDistance &&
                                       !ShouldDelayNoTargetActionAfterLoot(state, DateTimeOffset.Now);
            if (!state.Fighting &&
                !skipMaintenanceThisTick &&
                (mayEnterMaintenanceRest || mayEnterNoTargetRest) &&
                IsEnabledTeamLeader(context))
            {
                var restDecision = await EvaluateLeaderRestGuardAsync(
                        context,
                        state,
                        playerPosition)
                    .ConfigureAwait(false);
                leaderRestGuardAllowsSit = restDecision.CanSit;
                preMaintenanceDefenseTarget = restDecision.DefenseTarget;
                noTargetRestWakeTarget ??= preMaintenanceDefenseTarget;
                if (preMaintenanceDefenseTarget is not null)
                {
                    skipMaintenanceThisTick = true;
                }
            }

            if (!state.Fighting &&
                !IsGatherMaintenanceBlocked(state) &&
                !skipMaintenanceThisTick &&
                await TryHandleStationaryMaintenanceAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    allowSitMaintenance: !temporaryTargetSwitchGuard && leaderRestGuardAllowsSit,
                    clearSitWhenDisallowed: temporaryTargetSwitchGuard)
                .ConfigureAwait(false))
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return IdleDelay;
            }

            if (!state.Fighting &&
                !temporaryTargetSwitchGuard &&
                !stationaryGatherWorkAvailable &&
                leaderRestGuardAllowsSit &&
                CanUseNoTargetRestAtHome(combat) &&
                playerDistanceFromHome <= ReturnStopDistance &&
                !ShouldDelayNoTargetActionAfterLoot(state, DateTimeOffset.Now))
            {
                noTargetRestWakeTarget = await SelectMaintenanceDefenseTargetAsync(
                        context,
                        state,
                        playerPosition)
                    .ConfigureAwait(false);
                noTargetRestWakeTarget ??= await SelectTargetAsync(
                        context,
                        state,
                        playerPosition,
                        home,
                        radius,
                        combat.ContestMonster)
                    .ConfigureAwait(false);
                if (noTargetRestWakeTarget?.Position is null &&
                    await TryEnterNoTargetRestAtHomeAsync(context, semiAutoState, state, player, playerDistanceFromHome)
                        .ConfigureAwait(false))
                {
                    return IdleDelay;
                }
            }
        }

        if (!state.Fighting && noTargetRestWakeTarget is null)
        {
            var postLootDelayResult = await TryDelayNoTargetActionAfterLootAsync(
                    context,
                    state,
                    playerPosition,
                    home,
                    radius,
                    combat)
                .ConfigureAwait(false);
            if (postLootDelayResult.Delayed)
            {
                return IdleDelay;
            }

            noTargetRestWakeTarget = postLootDelayResult.Target;
        }

        if (state.Fighting)
        {
            return await TickFightAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                home,
                radius,
                playerDistanceFromHome).ConfigureAwait(false);
        }

        var startupRecoveryDelay = await TickStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false);
        if (startupRecoveryDelay is not null)
        {
            StopNextTargetPreAim(context, state, "startup_recovery", clearCandidate: true);
            return startupRecoveryDelay.Value;
        }

        if (stationaryGatherWorkAvailable)
        {
            state.ReturningHome = false;
            state.ResetReturnHomeStuckTracking();
        }
        else if (playerDistanceFromHome > radius)
        {
            state.ReturningHome = true;
        }

        if (state.ReturningHome)
        {
            await StopSoloJumpAsync(state, "stationary_returning_home").ConfigureAwait(false);
            StopNextTargetPreAim(context, state, "returning_home", clearCandidate: true);
            if (playerDistanceFromHome <= ReturnStopDistance)
            {
                state.ReturningHome = false;
                state.ResetReturnHomeStuckTracking();
                await StopMovementAsync(context, state).ConfigureAwait(false);
            }
            else
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                var navigation = await ResolveObstacleNavigationAsync(
                        context,
                        state,
                        playerPosition,
                        home,
                        RadarNavigationPurpose.ReturnHome,
                        targetServerObjectId: 0,
                        radarSettings,
                        ReturnStopDistance)
                    .ConfigureAwait(false);
                if (navigation?.Action == RadarNavigationAction.Unreachable)
                {
                    await StopMovementAsync(context, state).ConfigureAwait(false);
                    LogRadarNavigation(context, state, navigation, "outside_radius");
                    return IdleDelay;
                }

                var destination = navigation is null
                    ? home
                    : ToVector3(navigation.Destination, playerPosition.Z);
                var reachDistance = navigation?.ReachDistanceMeters ?? ReturnStopDistance;
                await PathFollowStepAsync(context, state, player, destination, reachDistance).ConfigureAwait(false);
                if (navigation is not null)
                {
                    LogRadarNavigation(context, state, navigation, "outside_radius");
                }
                await TryJumpReturnHomeIfStuckAsync(
                        context,
                        state,
                        playerPosition,
                        home,
                        playerDistanceFromHome,
                        "outside_radius")
                    .ConfigureAwait(false);
                return MoveTickDelay;
            }
        }

        WorldObjectSnapshot? gatherThreatTarget = null;
        if (!temporaryTargetSwitchGuard &&
            gatherSettings.StationaryPriorityEnabled)
        {
            await StopSoloJumpAsync(state, "stationary_gather").ConfigureAwait(false);
            StopNextTargetPreAim(context, state, "stationary_gather", clearCandidate: true);
            var gatherTick = await TickStationaryGatherAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    home,
                    gatherSettings,
                    radius)
                .ConfigureAwait(false);
            if (gatherTick.Handled)
            {
                return gatherTick.Delay;
            }

            gatherThreatTarget = gatherTick.ThreatTarget;
        }

        var target = noTargetRestWakeTarget ?? preMaintenanceDefenseTarget;
        if (target is null)
        {
            target = await SelectMaintenanceDefenseTargetAsync(
                    context,
                    state,
                    playerPosition)
                .ConfigureAwait(false);
        }

        if (target is null)
        {
            target = gatherThreatTarget;
        }

        if (target is null)
        {
            target = await SelectTargetAsync(
                    context,
                    state,
                    playerPosition,
                    home,
                    radius,
                    combat.ContestMonster)
                .ConfigureAwait(false);
        }

        if (target is not null &&
            state.HasSmartPreAimHandoff &&
            !state.IsSmartPreAimHandoffTarget(target.EntityId, target.ServerObjectId))
        {
            ReleaseSmartPreAimHandoff(context, state, "target_override", clearPreAimCandidate: true);
        }

        if (target?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopSoloJumpAsync(state, "stationary_target_unavailable").ConfigureAwait(false);
            if (state.HasSmartPreAimHandoff)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return MoveTickDelay;
            }

            StopNextTargetPreAim(context, state, "target_unavailable", clearCandidate: true);
            state.ClearTarget();
            if (combat.ReturnHomeWhenNoTarget && playerDistanceFromHome > ReturnStopDistance)
            {
                state.ClearNoTargetRest();
                LogActionThrottled(context, state, "stationary_combat.no_target.return_home", "home", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
                    ["stopDistance"] = Math.Round(ReturnStopDistance, 2),
                    ["homeX"] = Math.Round(home.X, 2),
                    ["homeY"] = Math.Round(home.Y, 2)
                }, TimeSpan.FromMilliseconds(500));
                var navigation = await ResolveObstacleNavigationAsync(
                        context,
                        state,
                        playerPosition,
                        home,
                        RadarNavigationPurpose.ReturnHome,
                        targetServerObjectId: 0,
                        radarSettings,
                        ReturnStopDistance)
                    .ConfigureAwait(false);
                if (navigation?.Action == RadarNavigationAction.Unreachable)
                {
                    await StopMovementAsync(context, state).ConfigureAwait(false);
                    LogRadarNavigation(context, state, navigation, "no_target");
                    return IdleDelay;
                }

                var destination = navigation is null
                    ? home
                    : ToVector3(navigation.Destination, playerPosition.Z);
                var reachDistance = navigation?.ReachDistanceMeters ?? ReturnStopDistance;
                await PathFollowStepAsync(context, state, player, destination, reachDistance).ConfigureAwait(false);
                if (navigation is not null)
                {
                    LogRadarNavigation(context, state, navigation, "no_target");
                }
                await TryJumpReturnHomeIfStuckAsync(
                        context,
                        state,
                        playerPosition,
                        home,
                        playerDistanceFromHome,
                        "no_target")
                    .ConfigureAwait(false);
                return MoveTickDelay;
            }

            var canEnterNoTargetRestAtHome = !temporaryTargetSwitchGuard &&
                                             leaderRestGuardAllowsSit &&
                                             CanUseNoTargetRestAtHome(combat) &&
                                             playerDistanceFromHome <= ReturnStopDistance;
            if (!canEnterNoTargetRestAtHome &&
                !IsGatherMaintenanceBlocked(state) &&
                await TryHandleStationaryMaintenanceAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player,
                        allowSitMaintenance: !temporaryTargetSwitchGuard && leaderRestGuardAllowsSit,
                        clearSitWhenDisallowed: temporaryTargetSwitchGuard)
                    .ConfigureAwait(false))
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return IdleDelay;
            }

            if (canEnterNoTargetRestAtHome &&
                await TryEnterNoTargetRestAtHomeAsync(context, semiAutoState, state, player, playerDistanceFromHome)
                    .ConfigureAwait(false))
            {
                return IdleDelay;
            }

            state.ClearNoTargetRest();
            state.ResetReturnHomeStuckTracking();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return IdleDelay;
        }

        var candidateChanged = state.MarkCandidate(target, DateTimeOffset.Now);
        if (IsMaintenanceDefenseTarget(target, state))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
            state.CurrentTargetBypassesHomeLeash = true;
        }
        else if (IsSameGatherThreat(target, gatherThreatTarget))
        {
            state.CurrentTargetIsGatherSafetyClear = true;
            state.CurrentTargetBypassesHomeLeash = true;
        }

        var targetPosition = target.Position.Value;
        var targetDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(targetPosition, home);
        var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (candidateChanged)
        {
            state.FacedCandidateEntityId = 0;
            state.ClearPendingTabVerification();
            context.Logger.Info("stationary_combat.target.selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name,
                ["playerDistanceToTarget"] = Math.Round(playerDistanceToTarget, 2),
                ["targetDistanceFromHome"] = Math.Round(targetDistanceFromHome, 2),
                ["radius"] = Math.Round(radius, 2),
                ["targetingMe"] = target.IsTargetingLocalPlayer,
                ["serverObjectId"] = target.ServerObjectId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetingServerObjectId"] = target.TargetServerObjectId,
                ["aggressiveKnown"] = target.AggressiveKnown,
                ["aggressiveToPlayer"] = target.IsAggressiveToPlayer,
                ["passiveToPlayer"] = target.IsPassiveToPlayer,
                ["aggressiveSource"] = target.AggressiveSource
            });
        }

        if (!IsTargetingLocalSide(target, state) &&
            !state.IsTeamLeaderProtectionTarget(target) &&
            !state.CurrentTargetBypassesHomeLeash &&
            targetDistanceFromHome > radius)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopNextTargetPreAim(context, state, "candidate_outside_radius", clearCandidate: true);
            state.ClearTarget();
            return IdleDelay;
        }

        if (IsTargetTimedOut(state, DateTimeOffset.Now))
        {
            return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.EntityId,
                    target.ServerObjectId,
                    target.Name,
                    "not_locked")
                .ConfigureAwait(false);
        }

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        var lockedDefenseDelay = await TryAcquireLockedLocalSideDefenseTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius)
            .ConfigureAwait(false);
        if (lockedDefenseDelay is not null)
        {
            return lockedDefenseDelay.Value;
        }

        var radarNavigation = await ResolveObstacleNavigationAsync(
                context,
                state,
                playerPosition,
                targetPosition,
                RadarNavigationPurpose.ApproachTarget,
                target.ServerObjectId,
                radarSettings,
                AcquireDistance)
            .ConfigureAwait(false);
        if (radarNavigation?.Action == RadarNavigationAction.Unreachable)
        {
            return await TemporarilyExcludeRadarUnreachableTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target,
                    radarNavigation)
                .ConfigureAwait(false);
        }

        var radarRequiresMovement = radarNavigation?.Action == RadarNavigationAction.MoveToWaypoint ||
                                    radarNavigation?.Action == RadarNavigationAction.Direct &&
                                    playerDistanceToTarget > AcquireDistance;
        if (radarRequiresMovement && radarNavigation is not null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            var destination = ToVector3(radarNavigation.Destination, playerPosition.Z);
            Func<PathFollowStopContext, Task<bool>>? afterWaypointStopAsync =
                radarNavigation.Action == RadarNavigationAction.MoveToWaypoint
                    ? stop => TryCommitRadarDirectAfterWaypointStopAsync(
                        context,
                        state,
                        target.ServerObjectId,
                        radarSettings,
                        RadarDirectTargetSource.WorldObjects,
                        "target_approach",
                        stop)
                    : null;
            await PathFollowStepAsync(
                    context,
                    state,
                    player,
                    destination,
                    radarNavigation.ReachDistanceMeters,
                    afterWaypointStopAsync)
                .ConfigureAwait(false);
            LogRadarNavigation(context, state, radarNavigation, "target_approach");
            await TryJumpCombatApproachIfStuckAsync(
                    context,
                    state,
                    target,
                    playerPosition,
                    playerDistanceToTarget,
                    "radar_approach")
                .ConfigureAwait(false);
            return MoveTickDelay;
        }

        if (state.IsPendingTabCandidate(target))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    home,
                    radius)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                allowLockedFallback: false,
                phase: "pre_move")
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        if (state.FacedCandidateEntityId != target.EntityId)
        {
            if (TryConsumeNextTargetPreAim(context, state, player, targetPosition, target))
            {
                state.FacedCandidateEntityId = target.EntityId;
            }
        }

        if (state.FacedCandidateEntityId != target.EntityId)
        {
            var isFacingTarget = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
            if (!isFacingTarget)
            {
                var faceTargetTimeoutMs = ReadFaceTargetTimeoutMs();
                if (state.TargetStartedAt != DateTimeOffset.MinValue &&
                    DateTimeOffset.Now - state.TargetStartedAt >= TimeSpan.FromMilliseconds(faceTargetTimeoutMs))
                {
                    return await IgnoreCurrentTargetAsync(
                            context,
                            semiAutoState,
                            state,
                            target.EntityId,
                            target.ServerObjectId,
                            target.Name,
                            "face_target_failed",
                            timeoutMs: faceTargetTimeoutMs,
                            extraFields: new Dictionary<string, object?>
                            {
                                ["phase"] = "pre_lock",
                                ["playerDistanceToTarget"] = Math.Round(playerDistanceToTarget, 2),
                                ["targetDistanceFromHome"] = Math.Round(targetDistanceFromHome, 2)
                            })
                        .ConfigureAwait(false);
                }

                semiAutoState.ResetAttackKeyPressThrottle();
                return MoveTickDelay;
            }

            state.FacedCandidateEntityId = target.EntityId;
            if (state.JumpAssist is not null)
            {
                await state.JumpAssist
                    .StartSoloTargetAsync(
                        target.EntityId,
                        target.ServerObjectId,
                        target.Name,
                        target.CurrentHp)
                    .ConfigureAwait(false);
            }
        }

        if (playerDistanceToTarget > AcquireDistance)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await PathFollowStepAsync(context, state, player, targetPosition, AcquireDistance).ConfigureAwait(false);
            await TryJumpCombatApproachIfStuckAsync(
                    context,
                    state,
                    target,
                    playerPosition,
                    playerDistanceToTarget,
                    "pre_move")
                .ConfigureAwait(false);
            return MoveTickDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target, home, radius).ConfigureAwait(false);
    }

    public async Task<TimeSpan> TickPathAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        StopNextTargetPreAim(context, state, "path_combat", clearCandidate: true);
        UpdateMaintenanceRestJumpPause(state, semiAutoState);
        if (!state.Fighting && state.CandidateEntityId == 0)
        {
            await StopSoloJumpAsync(state, "path_combat_no_active_target").ConfigureAwait(false);
        }

        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var radius = ResolvePathCombatRadius(combat);

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        var playerPosition = player.Position!.Value;
        await RefreshLocalCombatSideAsync(context, plan, state, player).ConfigureAwait(false);

        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
            await AbortDiscardForExternalInterruptionIfActiveAsync(context, state, "death_recovery")
                .ConfigureAwait(false);
            state.EnterDeathRecovery(DateTimeOffset.Now);
            semiAutoState.ResetAttackKeyPressThrottle();
            context.Logger.Warn("stationary_combat.path_combat.death.detected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["x"] = Math.Round(playerPosition.X, 2),
                ["y"] = Math.Round(playerPosition.Y, 2),
                ["z"] = Math.Round(playerPosition.Z, 2)
            });
        }

        if (state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
            (!state.DeathRecovery.RevivePathLeaderSiphonActive || player.IsDead))
        {
            await StopSoloJumpAsync(state, "path_combat_death_recovery").ConfigureAwait(false);
            return await TickPlayerLifeGuardAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    followRevivePath: true)
                .ConfigureAwait(false) ?? IdleDelay;
        }

        if (state.StartupTownReturnPending)
        {
            return await TickStartupTownReturnAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    radius)
                .ConfigureAwait(false);
        }

        if (state.LootAfterKill.Active)
        {
            await StopSoloJumpAsync(state, "path_combat_loot").ConfigureAwait(false);
            return await TickLootAfterKillAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player)
                .ConfigureAwait(false);
        }

        var noKillRecoveryDelay = await TickNoKillRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player)
            .ConfigureAwait(false);
        if (noKillRecoveryDelay is not null)
        {
            await StopSoloJumpAsync(state, "path_combat_no_kill_recovery").ConfigureAwait(false);
            return noKillRecoveryDelay.Value;
        }

        if (!state.Fighting &&
            await TryHandleStationaryMaintenanceAsync(context, plan, semiAutoState, state, player).ConfigureAwait(false))
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return IdleDelay;
        }

        if (state.Fighting)
        {
            var anchor = state.PathCombat.CurrentTargetAnchor ?? playerPosition;
            var playerDistanceFromAnchor = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, anchor);
            return await TickFightAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    anchor,
                    radius,
                    playerDistanceFromAnchor)
                .ConfigureAwait(false);
        }

        var accessPathDelay = await TickPathCombatAccessPathAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                radius)
            .ConfigureAwait(false);
        if (accessPathDelay is not null)
        {
            return accessPathDelay.Value;
        }

        if (!state.PathCombat.Active &&
            !await TryStartPathCombatAsync(context, state, playerPosition).ConfigureAwait(false))
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            return IdleDelay;
        }

        var targetDelay = await TryHandlePathCombatTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                radius,
                combat.ContestMonster)
            .ConfigureAwait(false);
        if (targetDelay is not null)
        {
            return targetDelay.Value;
        }

        return await ContinuePathCombatAsync(
                context,
                semiAutoState,
                state,
                player,
                playerPosition)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> ExecutePathOnceAsync(
        AccountWorkerContext context,
        string pathName,
        IReadOnlyList<Vector3Snapshot> points)
    {
        if (points.Count == 0)
        {
            return OperationResult.Fail("Path has no points.");
        }

        var state = new StationaryCombatState();
        var pathPoints = points.ToArray();
        var displayName = string.IsNullOrWhiteSpace(pathName) ? "manual_path" : pathName.Trim();
        var reachDistance = ResolvePathFollowReachDistance(context.Config.ScriptSettings?.Combat);

        try
        {
            var initialPlayer = await ReadManualPathPlayerAsync(
                    context)
                .ConfigureAwait(false);
            var initialPosition = initialPlayer.Position!.Value;
            var nearestPointIndex = FindNearestPathPointIndex(initialPosition, pathPoints, double.MaxValue);
            if (nearestPointIndex < 0)
            {
                return OperationResult.Fail("No nearest path point was found.");
            }

            state.PathCombat.Start(displayName, pathPoints, nearestPointIndex);
            context.Logger.Info("manual_path.execute.start", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = displayName,
                ["startPointIndex"] = nearestPointIndex,
                ["startPointNumber"] = nearestPointIndex + 1,
                ["pathPointCount"] = pathPoints.Length,
                ["reachDistance"] = Math.Round(reachDistance, 2)
            });

            while (state.PathCombat.Active &&
                   state.PathCombat.PointIndex >= 0 &&
                   state.PathCombat.PointIndex < state.PathCombat.Points.Count)
            {
                context.StopToken.ThrowIfCancellationRequested();

                var player = await ReadManualPathPlayerAsync(
                        context)
                    .ConfigureAwait(false);
                var playerPosition = player.Position!.Value;
                var pointIndex = state.PathCombat.PointIndex;
                var point = state.PathCombat.Points[pointIndex];
                var distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, point);
                if (distance > reachDistance)
                {
                    await PathFollowStepAsync(context, state, player, point, reachDistance).ConfigureAwait(false);
                    await TryJumpPathCombatIfStuckAsync(context, state, playerPosition, pointIndex, distance)
                        .ConfigureAwait(false);
                    await Task.Delay(MoveTickDelay, context.StopToken).ConfigureAwait(false);
                    continue;
                }

                context.Logger.Info("manual_path.execute.point_reached", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pathName"] = displayName,
                    ["pointIndex"] = pointIndex,
                    ["pointNumber"] = pointIndex + 1,
                    ["pointCount"] = state.PathCombat.Points.Count,
                    ["distance"] = Math.Round(distance, 2)
                });
                state.PathCombat.AdvancePoint(loopPath: false, reverseAtEnd: false);
            }

            context.Logger.Info("manual_path.execute.complete", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = displayName,
                ["pointCount"] = pathPoints.Length
            });
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Path execution was canceled.");
        }
        finally
        {
            await StopMovementBestEffortAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
        }
    }

    private static Task<PlayerSnapshot> ReadManualPathPlayerAsync(AccountWorkerContext context) =>
        ReadPlayerAsync(context);

    public async Task<TimeSpan?> TickPlayerLifeGuardAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        bool followRevivePath)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
            await AbortDiscardForExternalInterruptionIfActiveAsync(context, state, "death_recovery")
                .ConfigureAwait(false);
            state.EnterDeathRecovery(DateTimeOffset.Now);
            semiAutoState.ResetAttackKeyPressThrottle();
            context.Logger.Warn("player_life.death.detected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["x"] = player.Position?.X,
                ["y"] = player.Position?.Y,
                ["z"] = player.Position?.Z
            });
        }

        if (state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
            return null;
        }

        var home = default(Vector3Snapshot);
        var shouldFollowRevivePath = false;
        var playerDistanceFromHome = 0.0D;
        if (followRevivePath)
        {
            var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
            if (homeResult.Success && homeResult.Value is not null)
            {
                home = homeResult.Value.Position;
                shouldFollowRevivePath = true;
                playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(player.Position!.Value, home);
            }
            else
            {
                LogThrottled(context, state, "player_life.guard.home_missing", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["revivePathName"] = GetRevivePathName(context),
                    ["reason"] = homeResult.Error
                });
            }
        }

        return await TickDeathRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                home,
                playerDistanceFromHome,
                shouldFollowRevivePath)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickDeathRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double playerDistanceFromHome,
        bool followRevivePath)
    {
        while (!context.StopToken.IsCancellationRequested)
        {
            if (state.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.Complete)
            {
                state.ExitDeathRecovery();
                context.Logger.Info("stationary_combat.death_recovery.complete", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["hp"] = player.CurrentHp,
                    ["maxHp"] = player.MaxHp,
                    ["hpPercent"] = Math.Round(player.HpPercent, 1)
                });
                return IdleDelay;
            }

            var status = state.DeathRecovery.Step switch
            {
                StationaryCombatDeathRecoveryStep.StopInput => await TickDeathStopInputNodeAsync(
                        context,
                        semiAutoState,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick => TickDeathWaitBeforeReviveClickNode(
                    context,
                    state,
                    player),
                StationaryCombatDeathRecoveryStep.ClickRevive => await TickDeathClickReviveNodeAsync(
                        context,
                        state,
                        player)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.WaitAlive => await TickDeathWaitAliveNodeAsync(
                        context,
                        state,
                        player)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.PostReviveScroll => await TickDeathPostReviveScrollNodeAsync(
                        context,
                        state,
                        player)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.PostReviveSpiritmasterPet => await TickDeathPostReviveSpiritmasterPetNodeAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player,
                        home,
                        playerDistanceFromHome)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.PostReviveMaintenance => await TickDeathPostReviveMaintenanceNodeAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player,
                        home,
                        playerDistanceFromHome)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.FollowRevivePath => followRevivePath
                    ? await TickDeathFollowRevivePathNodeAsync(
                            context,
                            plan,
                            semiAutoState,
                            state,
                            player,
                            home,
                            playerDistanceFromHome)
                        .ConfigureAwait(false)
                    : StationaryCombatBehaviorStatus.Success,
                _ => StationaryCombatBehaviorStatus.Success
            };

            if (status == StationaryCombatBehaviorStatus.Running)
            {
                return TimeSpan.FromMilliseconds(ReadDeathRecoveryTickMs());
            }

            if (status == StationaryCombatBehaviorStatus.Failure)
            {
                return IdleDelay;
            }

            state.DeathRecovery.Advance(DateTimeOffset.Now);
        }

        return IdleDelay;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathStopInputNodeAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        state.ReturningHome = false;
        state.ClearTarget();
        semiAutoState.ClearMaintenanceRest();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        context.Logger.Info("stationary_combat.death_recovery.stop_input", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["deathStopPath"] = context.Config.ScriptSettings?.Paths?.DeathStopPath ?? true
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private static StationaryCombatBehaviorStatus TickDeathWaitBeforeReviveClickNode(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (player.IsAlive)
        {
            context.Logger.Info("stationary_combat.death_recovery.already_alive", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        var delay = TimeSpan.FromMilliseconds(ReadDeathReviveClickDelayMs());
        var elapsed = DateTimeOffset.Now - state.DeathRecovery.StepStartedAt;
        if (elapsed < delay)
        {
            LogActionThrottled(context, state, "stationary_combat.death_recovery.wait_before_click", "wait_before_click", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["elapsedMs"] = (long)Math.Max(0.0D, elapsed.TotalMilliseconds),
                ["waitMs"] = (long)delay.TotalMilliseconds
            }, TimeSpan.FromSeconds(1));
            return StationaryCombatBehaviorStatus.Running;
        }

        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathClickReviveNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (player.IsAlive || state.DeathRecovery.ReviveClicked)
        {
            return StationaryCombatBehaviorStatus.Success;
        }

        var (x, y) = ReadDeathReviveClickPoint(context, state.DeathRecovery.ReviveClickCount);
        var result = await ClickAbsoluteScreenPointAsync(context, x, y).ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.death_recovery.revive_click_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["x"] = x,
                ["y"] = y,
                ["error"] = result.Error
            });
            return StationaryCombatBehaviorStatus.Running;
        }

        state.DeathRecovery.MarkReviveClicked(DateTimeOffset.Now);
        context.Logger.Info("stationary_combat.death_recovery.revive_clicked", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["x"] = x,
            ["y"] = y,
            ["clickCount"] = state.DeathRecovery.ReviveClickCount
        });
        return StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathWaitAliveNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (player.IsAlive)
        {
            context.Logger.Info("stationary_combat.death_recovery.revive_confirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["hpPercent"] = Math.Round(player.HpPercent, 1)
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        LogActionThrottled(context, state, "stationary_combat.death_recovery.wait_alive", "wait_alive", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["reviveClicked"] = state.DeathRecovery.ReviveClicked,
            ["clickCount"] = state.DeathRecovery.ReviveClickCount
        }, TimeSpan.FromSeconds(1));

        var retryDelay = TimeSpan.FromMilliseconds(ReadDeathReviveRetryMs());
        var lastClickAt = state.DeathRecovery.LastReviveClickAt == DateTimeOffset.MinValue
            ? state.DeathRecovery.StepStartedAt
            : state.DeathRecovery.LastReviveClickAt;
        var elapsed = DateTimeOffset.Now - lastClickAt;
        if (elapsed < retryDelay)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        var (x, y) = ReadDeathReviveClickPoint(context, state.DeathRecovery.ReviveClickCount);
        var result = await ClickAbsoluteScreenPointAsync(context, x, y).ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.death_recovery.revive_retry_click_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["x"] = x,
                ["y"] = y,
                ["fallback"] = true,
                ["clickCount"] = state.DeathRecovery.ReviveClickCount,
                ["retryWaitMs"] = (long)retryDelay.TotalMilliseconds,
                ["error"] = result.Error
            });
            return StationaryCombatBehaviorStatus.Running;
        }

        state.DeathRecovery.MarkReviveClicked(DateTimeOffset.Now);
        context.Logger.Info("stationary_combat.death_recovery.revive_retry_clicked", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["x"] = x,
            ["y"] = y,
            ["fallback"] = true,
            ["clickCount"] = state.DeathRecovery.ReviveClickCount,
            ["retryWaitMs"] = (long)retryDelay.TotalMilliseconds
        });
        return StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathPostReviveScrollNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (player.IsDead)
        {
            state.EnterDeathRecovery(DateTimeOffset.Now);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (!player.IsAlive)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        var count = ReadDeathPostReviveScrollCount();
        var delta = ReadDeathPostReviveScrollDelta();
        var interval = TimeSpan.FromMilliseconds(ReadDeathPostReviveScrollIntervalMs());
        while (state.DeathRecovery.PostReviveScrollsSent < count)
        {
            var result = await _input.ScrollMouseAsync(delta, context.StopToken).ConfigureAwait(false);
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.death_recovery.post_revive_scroll_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["delta"] = delta,
                    ["sent"] = state.DeathRecovery.PostReviveScrollsSent,
                    ["targetCount"] = count,
                    ["error"] = result.Error
                });
                return StationaryCombatBehaviorStatus.Running;
            }

            state.DeathRecovery.PostReviveScrollsSent++;
            context.Logger.Info("stationary_combat.death_recovery.post_revive_scroll_sent", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["input"] = _input.GetType().Name,
                ["delta"] = delta,
                ["sent"] = state.DeathRecovery.PostReviveScrollsSent,
                ["targetCount"] = count
            });

            if (state.DeathRecovery.PostReviveScrollsSent < count && interval > TimeSpan.Zero)
            {
                await DelayAsync(interval, context).ConfigureAwait(false);
            }
        }

        context.Logger.Info("stationary_combat.death_recovery.post_revive_scroll_complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["input"] = _input.GetType().Name,
            ["delta"] = delta,
            ["count"] = count,
            ["intervalMs"] = (long)interval.TotalMilliseconds
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathPostReviveSpiritmasterPetNodeAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        if (player.IsDead)
        {
            state.EnterDeathRecovery(DateTimeOffset.Now);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (!player.IsAlive)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (await TryHandleDeathRecoveryLocalDefenseBeforeRecoveryWorkAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                home,
                playerDistanceFromHome)
            .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        var handled = await _semiAuto
            .EnsureSpiritmasterPetAsync(
                context,
                plan,
                semiAutoState,
                beforeSummonKeyPress: async () =>
                {
                    semiAutoState.ResetAttackKeyPressThrottle();
                    await StopMovementAsync(context, state).ConfigureAwait(false);
                    StopPathFollowPoller(state);
                })
            .ConfigureAwait(false);

        return handled
            ? StationaryCombatBehaviorStatus.Running
            : StationaryCombatBehaviorStatus.Success;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathPostReviveMaintenanceNodeAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        if (player.IsDead)
        {
            state.EnterDeathRecovery(DateTimeOffset.Now);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (!player.IsAlive)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (await TryHandleDeathRecoveryLocalDefenseBeforeRecoveryWorkAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                home,
                playerDistanceFromHome)
            .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        var hpRecoverToPercent = Math.Clamp(
            context.Config.ScriptSettings?.Maintenance?.SitHpRecoverToPercent ?? 75,
            1,
            100);
        var mpRecoverToPercent = Math.Clamp(
            context.Config.ScriptSettings?.Maintenance?.SitMpRecoverToPercent ?? 90,
            1,
            100);
        var hpRecovered = player.HpPercent >= hpRecoverToPercent;
        var mpRecovered = player.MaxMp == 0 || player.MpPercent >= mpRecoverToPercent;
        if (!semiAutoState.IsMaintenanceResting && hpRecovered && mpRecovered)
        {
            context.Logger.Info("stationary_combat.death_recovery.maintenance_complete", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["hpPercent"] = Math.Round(player.HpPercent, 1),
                ["hpRecoverToPercent"] = hpRecoverToPercent,
                ["mp"] = player.CurrentMp,
                ["maxMp"] = player.MaxMp,
                ["mpPercent"] = Math.Round(player.MpPercent, 1),
                ["mpRecoverToPercent"] = mpRecoverToPercent
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        var handled = await _semiAuto
            .TryRecoverAfterReviveAsync(
                context,
                semiAutoState,
                player,
                plan,
                beforeMaintenanceKeyPress: async () =>
                {
                    semiAutoState.ResetAttackKeyPressThrottle();
                    await StopMovementAsync(context, state).ConfigureAwait(false);
                    StopPathFollowPoller(state);
                })
            .ConfigureAwait(false);

        if (handled)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (!hpRecovered || !mpRecovered)
        {
            context.Logger.Warn("stationary_combat.death_recovery.maintenance_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["hpPercent"] = Math.Round(player.HpPercent, 1),
                ["hpRecoverToPercent"] = hpRecoverToPercent,
                ["mp"] = player.CurrentMp,
                ["maxMp"] = player.MaxMp,
                ["mpPercent"] = Math.Round(player.MpPercent, 1),
                ["mpRecoverToPercent"] = mpRecoverToPercent
            });
        }

        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<bool> TryHandleDeathRecoveryLocalDefenseBeforeRecoveryWorkAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        if (player.Position is null)
        {
            return false;
        }

        var radius = Math.Max(1.0D, context.Config.ScriptSettings?.Combat?.StationaryCombatRadius ?? 1.0D);
        var defenseDelay = await TryHandleRecoveryDefenseTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                player.Position.Value,
                home,
                radius,
                playerDistanceFromHome,
                "death_recovery",
                allowRevivePathClear: false)
            .ConfigureAwait(false);
        return defenseDelay is not null;
    }

    private async Task<StationaryCombatBehaviorStatus> TickDeathFollowRevivePathNodeAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        if (player.IsDead)
        {
            state.EnterDeathRecovery(DateTimeOffset.Now);
            return StationaryCombatBehaviorStatus.Running;
        }

        var leaderSiphon = await TryUpdateDeathRecoveryRevivePathLeaderSiphonAsync(
                context,
                state)
            .ConfigureAwait(false);
        if (leaderSiphon.Active)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (leaderSiphon.Released &&
            !await TryRetargetDeathRevivePathAfterLeaderSiphonAsync(
                    context,
                    state,
                    player.Position!.Value,
                    home,
                    playerDistanceFromHome)
                .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Success;
        }
        else if (leaderSiphon.Released)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
        }

        if (state.LootAfterKill.Active)
        {
            await TickLootAfterKillAsync(context, plan, semiAutoState, state, player).ConfigureAwait(false);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (await TryHandleDeathRecoveryLocalDefenseBeforeRecoveryWorkAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                home,
                playerDistanceFromHome)
            .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (await _semiAuto
                .EnsureSpiritmasterPetAsync(
                    context,
                    plan,
                    semiAutoState,
                    beforeSummonKeyPress: async () =>
                    {
                        semiAutoState.ResetAttackKeyPressThrottle();
                        await StopMovementAsync(context, state).ConfigureAwait(false);
                        StopPathFollowPoller(state);
                    })
                .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        var radius = Math.Max(1.0D, context.Config.ScriptSettings?.Combat?.StationaryCombatRadius ?? 1.0D);
        var defenseDelay = await TryHandleRecoveryDefenseTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                player.Position!.Value,
                home,
                radius,
                playerDistanceFromHome,
                "death_recovery")
            .ConfigureAwait(false);
        if (defenseDelay is not null)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (await _semiAuto
                .TryHandleMaintenanceAsync(
                    context,
                    semiAutoState,
                    player,
                    beforeMaintenanceKeyPress: async () =>
                    {
                        semiAutoState.ResetAttackKeyPressThrottle();
                        await StopMovementAsync(context, state).ConfigureAwait(false);
                        StopPathFollowPoller(state);
                    },
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: true)
                .ConfigureAwait(false))
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return StationaryCombatBehaviorStatus.Running;
        }

        if (state.DeathRecovery.RevivePathPoints.Count == 0)
        {
            var loaded = await TryStartDeathRevivePathAsync(
                    context,
                    state,
                    player.Position.Value,
                    home,
                    playerDistanceFromHome)
                .ConfigureAwait(false);
            if (!loaded)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                return StationaryCombatBehaviorStatus.Success;
            }
        }

        var pathFollowReachDistance = ResolvePathFollowReachDistance(context.Config.ScriptSettings?.Combat);
        while (state.DeathRecovery.RevivePathPointIndex >= 0 &&
               state.DeathRecovery.RevivePathPointIndex < state.DeathRecovery.RevivePathPoints.Count)
        {
            var point = state.DeathRecovery.RevivePathPoints[state.DeathRecovery.RevivePathPointIndex];
            var distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, point);
            if (distance > pathFollowReachDistance)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, point, pathFollowReachDistance).ConfigureAwait(false);
                await TryJumpDeathRevivePathIfStuckAsync(
                        context,
                        state,
                        player.Position.Value,
                        state.DeathRecovery.RevivePathPointIndex,
                        distance)
                    .ConfigureAwait(false);
                LogActionThrottled(context, state, "stationary_combat.death_recovery.path_follow", "move:" + state.DeathRecovery.RevivePathPointIndex, new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pathName"] = state.DeathRecovery.RevivePathName,
                    ["pointIndex"] = state.DeathRecovery.RevivePathPointIndex,
                    ["pointNumber"] = state.DeathRecovery.RevivePathPointIndex + 1,
                    ["pointCount"] = state.DeathRecovery.RevivePathPoints.Count,
                    ["distance"] = Math.Round(distance, 2)
                }, TimeSpan.FromMilliseconds(500));
                return StationaryCombatBehaviorStatus.Running;
            }

            context.Logger.Info("stationary_combat.death_recovery.path_point_reached", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.DeathRecovery.RevivePathName,
                ["pointIndex"] = state.DeathRecovery.RevivePathPointIndex,
                ["pointNumber"] = state.DeathRecovery.RevivePathPointIndex + 1,
                ["pointCount"] = state.DeathRecovery.RevivePathPoints.Count,
                ["distance"] = Math.Round(distance, 2)
            });
            state.DeathRecovery.RevivePathPointIndex++;
            state.DeathRecovery.ResetRevivePathStuckTracking();
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<RevivePathLeaderSiphonTick> TryUpdateDeathRecoveryRevivePathLeaderSiphonAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        if (!ShouldAllowRevivePathLeaderSiphon(team))
        {
            return ClearDeathRecoveryRevivePathLeaderSiphon(
                context,
                state,
                "team_role_disabled",
                null,
                team.GroupDistanceMeters);
        }

        var monitor = new TeamMonitor(context.Snapshots, context.Logger);
        var snapshot = await monitor.ReadSnapshotAsync().ConfigureAwait(false);
        var leader = snapshot.LeaderMember;
        if (!TeamLeaderRuntimePolicy.IsLeaderInGroupRange(leader, team.GroupDistanceMeters))
        {
            return ClearDeathRecoveryRevivePathLeaderSiphon(
                context,
                state,
                ResolveRevivePathLeaderSiphonInactiveReason(leader),
                leader,
                team.GroupDistanceMeters);
        }

        var activeLeader = leader!;
        var changed = state.DeathRecovery.ActivateRevivePathLeaderSiphon(
            activeLeader.ServerObjectId,
            activeLeader.Name);
        if (changed)
        {
            context.Logger.Info("stationary_combat.death_recovery.leader_siphon.enter", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.DeathRecovery.RevivePathName,
                ["pointIndex"] = state.DeathRecovery.RevivePathPointIndex,
                ["leader"] = activeLeader.Name,
                ["leaderServerObjectId"] = activeLeader.ServerObjectId,
                ["leaderDistanceToLocal"] = activeLeader.PartyMember.DistanceToLocalPlayer,
                ["groupDistanceMeters"] = team.GroupDistanceMeters
            });
        }

        return new RevivePathLeaderSiphonTick(Active: true, Released: false);
    }

    private RevivePathLeaderSiphonTick ClearDeathRecoveryRevivePathLeaderSiphon(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        TeamMemberSnapshot? leader,
        double groupDistanceMeters,
        string? error = null)
    {
        var previousLeaderName = state.DeathRecovery.RevivePathLeaderSiphonName;
        var previousLeaderServerObjectId = state.DeathRecovery.RevivePathLeaderSiphonServerObjectId;
        if (!state.DeathRecovery.ClearRevivePathLeaderSiphon())
        {
            return new RevivePathLeaderSiphonTick(Active: false, Released: false);
        }

        context.Logger.Info("stationary_combat.death_recovery.leader_siphon.exit", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["pathName"] = state.DeathRecovery.RevivePathName,
            ["pointIndex"] = state.DeathRecovery.RevivePathPointIndex,
            ["leader"] = leader?.Name ?? previousLeaderName,
            ["leaderServerObjectId"] = leader?.ServerObjectId ?? previousLeaderServerObjectId,
            ["leaderDistanceToLocal"] = leader?.PartyMember.DistanceToLocalPlayer,
            ["groupDistanceMeters"] = groupDistanceMeters,
            ["error"] = error
        });
        return new RevivePathLeaderSiphonTick(Active: false, Released: true);
    }

    private async Task<bool> TryRetargetDeathRevivePathAfterLeaderSiphonAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        state.ClearTarget();
        if (state.DeathRecovery.RevivePathPoints.Count == 0)
        {
            return true;
        }

        var oldPointIndex = state.DeathRecovery.RevivePathPointIndex;
        var nearestPointIndex = FindNearestPathPointIndex(
            playerPosition,
            state.DeathRecovery.RevivePathPoints,
            playerDistanceFromHome);
        if (nearestPointIndex < 0)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            context.Logger.Info("stationary_combat.death_recovery.leader_siphon.path_complete", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.DeathRecovery.RevivePathName,
                ["homeDistance"] = Math.Round(StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home), 2),
                ["pathPointCount"] = state.DeathRecovery.RevivePathPoints.Count
            });
            return false;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            state.DeathRecovery.RevivePathPoints[nearestPointIndex]);
        state.DeathRecovery.RevivePathPointIndex = nearestPointIndex;
        state.DeathRecovery.ResetRevivePathStuckTracking();
        context.Logger.Info("stationary_combat.death_recovery.leader_siphon.path_resumed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.DeathRecovery.RevivePathName,
            ["oldPointIndex"] = oldPointIndex,
            ["newPointIndex"] = nearestPointIndex,
            ["newPointNumber"] = nearestPointIndex + 1,
            ["pointCount"] = state.DeathRecovery.RevivePathPoints.Count,
            ["distance"] = Math.Round(nearestDistance, 2)
        });
        return true;
    }

    private static bool ShouldAllowRevivePathLeaderSiphon(TeamScriptSettings team)
    {
        return team.Role switch
        {
            TeamRole.Output => (team.Output?.Enabled ?? false) &&
                               (team.Output?.FollowLeader ?? true),
            TeamRole.Support => team.Support?.Enabled ?? false,
            _ => false
        };
    }

    private static string ResolveRevivePathLeaderSiphonInactiveReason(TeamMemberSnapshot? leader)
    {
        if (leader is null)
        {
            return "leader_missing";
        }

        if (leader.IsSelf)
        {
            return "leader_is_self";
        }

        if (leader.PartyMember.IsDead)
        {
            return "leader_dead";
        }

        return leader.PartyMember.DistanceToLocalPlayer is null
            ? "leader_distance_unknown"
            : "leader_out_of_range";
    }

    private readonly record struct RevivePathLeaderSiphonTick(bool Active, bool Released);

    private async Task TryJumpDeathRevivePathIfStuckAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        int pointIndex,
        double distanceToPoint)
    {
        var now = DateTimeOffset.Now;
        var deathRecovery = state.DeathRecovery;
        var minProgressDistance = ReadDeathRevivePathStuckDistance();
        if (deathRecovery.RevivePathStuckPointIndex != pointIndex ||
            deathRecovery.RevivePathLastProgressPosition is null ||
            deathRecovery.RevivePathLastProgressAt == DateTimeOffset.MinValue)
        {
            deathRecovery.MarkRevivePathProgress(pointIndex, playerPosition, now);
            return;
        }

        var moved = StationaryCombatTargetSelector.HorizontalDistance(
            deathRecovery.RevivePathLastProgressPosition.Value,
            playerPosition);
        if (moved >= minProgressDistance)
        {
            deathRecovery.MarkRevivePathProgress(pointIndex, playerPosition, now);
            return;
        }

        var stuckMs = ReadDeathRevivePathStuckMs();
        var stuckFor = now - deathRecovery.RevivePathLastProgressAt;
        if (stuckFor.TotalMilliseconds < stuckMs)
        {
            return;
        }

        if (deathRecovery.LastRevivePathJumpAt != DateTimeOffset.MinValue &&
            (now - deathRecovery.LastRevivePathJumpAt).TotalMilliseconds < stuckMs)
        {
            return;
        }

        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        var jumpHold = TimeSpan.FromMilliseconds(ReadDeathRevivePathJumpHoldMs());
        var result = await _input.PressKeyAsync("Space", jumpHold, context.StopToken).ConfigureAwait(false);
        deathRecovery.MarkRevivePathJump(now);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.death_recovery.path_stuck_jump_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = deathRecovery.RevivePathName,
                ["pointIndex"] = pointIndex,
                ["pointNumber"] = pointIndex + 1,
                ["distance"] = Math.Round(distanceToPoint, 2),
                ["moved"] = Math.Round(moved, 2),
                ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
                ["thresholdMs"] = stuckMs,
                ["progressDistance"] = Math.Round(minProgressDistance, 2),
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.death_recovery.path_stuck_jump", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = deathRecovery.RevivePathName,
            ["pointIndex"] = pointIndex,
            ["pointNumber"] = pointIndex + 1,
            ["distance"] = Math.Round(distanceToPoint, 2),
            ["moved"] = Math.Round(moved, 2),
            ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
            ["thresholdMs"] = stuckMs,
            ["progressDistance"] = Math.Round(minProgressDistance, 2),
            ["jumpCount"] = deathRecovery.RevivePathJumpCount,
            ["movingForward"] = state.IsMovingForward
        });
    }

    private async Task<bool> TryStartDeathRevivePathAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        var revivePathName = GetRevivePathName(context);
        if (_pathStore is null || string.IsNullOrWhiteSpace(revivePathName))
        {
            context.Logger.Warn("stationary_combat.death_recovery.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["reason"] = _pathStore is null ? "path_store_missing" : "path_name_missing"
            });
            return false;
        }

        var pathResult = await _pathStore.LoadAsync(revivePathName, context.StopToken).ConfigureAwait(false);
        if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
        {
            context.Logger.Warn("stationary_combat.death_recovery.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["error"] = pathResult.Error,
                ["pointCount"] = pathResult.Value?.PointCount ?? 0
            });
            return false;
        }

        var revivePoints = points
            .Select(point => point.ToVector3())
            .ToArray();
        state.SetStationaryHomeFromRevivePath(revivePathName, revivePoints[^1], revivePoints.Length);
        var nearestPointIndex = FindNearestPathPointIndex(playerPosition, revivePoints, playerDistanceFromHome);
        if (nearestPointIndex < 0)
        {
            context.Logger.Info("stationary_combat.death_recovery.path_home_nearest", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["homeDistance"] = Math.Round(StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home), 2),
                ["pathPointCount"] = revivePoints.Length
            });
            return false;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            revivePoints[nearestPointIndex]);
        state.DeathRecovery.RevivePathName = revivePathName;
        state.DeathRecovery.RevivePathPoints = revivePoints;
        state.DeathRecovery.RevivePathPointIndex = nearestPointIndex;
        state.DeathRecovery.ResetRevivePathStuckTracking();
        state.ReturningHome = false;
        state.ClearTarget();
        context.Logger.Info("stationary_combat.death_recovery.path_selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = revivePathName,
            ["startPointIndex"] = nearestPointIndex,
            ["startPointNumber"] = nearestPointIndex + 1,
            ["pathPointCount"] = revivePoints.Length,
            ["pathPointDistance"] = Math.Round(nearestDistance, 2),
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2)
        });
        return true;
    }

    private async Task<OperationResult> ClickAbsoluteScreenPointAsync(
        AccountWorkerContext context,
        int x,
        int y)
    {
        var move = await MoveMouseToAbsoluteScreenPointAsync(context, x, y).ConfigureAwait(false);
        if (!move.Success)
        {
            return move;
        }

        await DelayAsync(DeathRevivePreClickPause, context).ConfigureAwait(false);
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        if (!down.Success)
        {
            return OperationResult.Fail("Revive left mouse down failed. " + down.Error);
        }

        await DelayAsync(TimeSpan.FromMilliseconds(ReadDeathReviveClickHoldMs()), context).ConfigureAwait(false);
        var up = await _input.MouseUpAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        return up.Success
            ? OperationResult.Ok()
            : OperationResult.Fail("Revive left mouse up failed. " + up.Error);
    }

    private async Task<OperationResult> MoveMouseToAbsoluteScreenPointAsync(
        AccountWorkerContext context,
        int x,
        int y)
    {
        return await ScreenPointMouseMover
            .MoveToAsync(
                _input,
                x,
                y,
                ReadDeathReviveMouseResetCount(),
                TimeSpan.FromMilliseconds(ReadDeathReviveMouseStepDelayMs()),
                context.StopToken)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TickStartupRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome)
    {
        if (state.StartupTownReturnPending)
        {
            return await TickStartupTownReturnAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    radius)
                .ConfigureAwait(false);
        }

        if (state.StartupRecoveryActive)
        {
            return await ContinueStartupRecoveryAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    home,
                    radius,
                    playerDistanceFromHome)
                .ConfigureAwait(false);
        }

        if (state.StartupRecoveryChecked)
        {
            return null;
        }

        state.MarkStartupRecoveryChecked();
        var revivePathName = GetRevivePathName(context);
        if (_pathStore is null || string.IsNullOrWhiteSpace(revivePathName))
        {
            return null;
        }

        var pathResult = await _pathStore.LoadAsync(revivePathName, context.StopToken).ConfigureAwait(false);
        if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
        {
            context.Logger.Warn("stationary_combat.startup_recovery.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["error"] = pathResult.Error,
                ["pointCount"] = pathResult.Value?.PointCount ?? 0
            });
            return null;
        }

        var revivePoints = points
            .Select(point => point.ToVector3())
            .ToArray();
        state.SetStationaryHomeFromRevivePath(revivePathName, revivePoints[^1], revivePoints.Length);

        var nearestPathPointIndex = FindNearestPathPointIndex(playerPosition, revivePoints, double.MaxValue);
        if (nearestPathPointIndex < 0)
        {
            context.Logger.Warn("stationary_combat.startup_recovery.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["reason"] = "nearest_point_missing",
                ["pathPointCount"] = revivePoints.Length
            });
            return null;
        }

        var nearestPathPointDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            revivePoints[nearestPathPointIndex]);
        var startupTownReturnDistance = ReadStartupTownReturnDistance();
        if (context.Config.ScriptSettings?.CombatMode == AccountCombatMode.Stationary &&
            !state.CleanupReturnToCombatActive &&
            nearestPathPointDistance > startupTownReturnDistance)
        {
            var key = context.Config.ScriptSettings?.Paths?.TownReturnKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                context.Logger.Warn("stationary_combat.startup_recovery.return.skipped", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "town_return_key_missing",
                    ["pathName"] = revivePathName,
                    ["nearestPathPointDistance"] = Math.Round(nearestPathPointDistance, 2),
                    ["distanceThreshold"] = Math.Round(startupTownReturnDistance, 2)
                });
            }
            else
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                state.ReturningHome = false;
                state.ClearTarget();

                var press = await _input
                    .PressKeyAsync(key, StartupTownReturnHoldDuration, context.StopToken)
                    .ConfigureAwait(false);
                if (press.Success)
                {
                    state.StartStartupTownReturn(
                        revivePathName,
                        revivePoints,
                        playerPosition,
                        DateTimeOffset.Now);
                    context.Logger.Warn("stationary_combat.startup_recovery.return.press", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["key"] = key,
                        ["pathName"] = revivePathName,
                        ["nearestPathPointIndex"] = nearestPathPointIndex,
                        ["nearestPathPointDistance"] = Math.Round(nearestPathPointDistance, 2),
                        ["startPointDistance"] = Math.Round(
                            StationaryCombatTargetSelector.HorizontalDistance(playerPosition, revivePoints[0]),
                            2),
                        ["endPointDistance"] = Math.Round(playerDistanceFromHome, 2),
                        ["distanceThreshold"] = Math.Round(startupTownReturnDistance, 2)
                    });
                    return IdleDelay;
                }

                context.Logger.Warn("stationary_combat.startup_recovery.return.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "town_return_press_failed",
                    ["key"] = key,
                    ["pathName"] = revivePathName,
                    ["nearestPathPointDistance"] = Math.Round(nearestPathPointDistance, 2),
                    ["distanceThreshold"] = Math.Round(startupTownReturnDistance, 2),
                    ["error"] = press.Error
                });
            }
        }

        if (!TryStartStartupRecoveryFromNearestPoint(
                context,
                state,
                playerPosition,
                revivePathName,
                revivePoints,
                playerDistanceFromHome))
        {
            return null;
        }

        return await ContinueStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> ContinueStartupRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome)
    {
        var defenseDelay = await TryHandleRecoveryDefenseTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome,
                "startup_recovery")
            .ConfigureAwait(false);
        if (defenseDelay is not null)
        {
            return defenseDelay.Value;
        }

        var pathFollowReachDistance = ResolvePathFollowReachDistance(context.Config.ScriptSettings?.Combat);
        while (state.StartupRecoveryActive &&
               state.StartupRecoveryPointIndex >= 0 &&
               state.StartupRecoveryPointIndex < state.StartupRecoveryPoints.Count)
        {
            var point = state.StartupRecoveryPoints[state.StartupRecoveryPointIndex];
            var distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, point);
            if (distance > pathFollowReachDistance)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, point, pathFollowReachDistance).ConfigureAwait(false);
                await TryJumpStartupRecoveryIfStuckAsync(
                        context,
                        state,
                        playerPosition,
                        state.StartupRecoveryPointIndex,
                        distance)
                    .ConfigureAwait(false);
                LogActionThrottled(context, state, "stationary_combat.startup_recovery", "move:" + state.StartupRecoveryPointIndex, new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["action"] = "move",
                    ["pathName"] = state.StartupRecoveryPathName,
                    ["pointIndex"] = state.StartupRecoveryPointIndex,
                    ["pointNumber"] = state.StartupRecoveryPointIndex + 1,
                    ["pointCount"] = state.StartupRecoveryPoints.Count,
                    ["distance"] = Math.Round(distance, 2)
                }, TimeSpan.FromMilliseconds(500));
                return MoveTickDelay;
            }

            context.Logger.Info("stationary_combat.startup_recovery.point_reached", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.StartupRecoveryPathName,
                ["pointIndex"] = state.StartupRecoveryPointIndex,
                ["pointNumber"] = state.StartupRecoveryPointIndex + 1,
                ["pointCount"] = state.StartupRecoveryPoints.Count,
                ["distance"] = Math.Round(distance, 2)
            });
            state.AdvanceStartupRecoveryPoint();
        }

        if (state.StartupRecoveryActive)
        {
            var pathName = state.StartupRecoveryPathName;
            state.ClearStartupRecovery();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            context.Logger.Info("stationary_combat.startup_recovery.complete", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = pathName
            });
        }

        return null;
    }

    private async Task<TimeSpan?> TickPathCombatAccessPathAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        double radius)
    {
        if (state.PathCombat.Active)
        {
            return null;
        }

        var combatPathName = GetCombatPathName(context);
        var paths = context.Config.ScriptSettings?.Paths;
        if (state.PathCombat.Completed &&
            (paths?.LoopPath ?? true) == false &&
            string.Equals(state.PathCombat.CompletedPathName, combatPathName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (state.StartupRecoveryActive)
        {
            return await ContinuePathCombatAccessPathAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    radius)
                .ConfigureAwait(false);
        }

        if (state.StartupRecoveryChecked)
        {
            return null;
        }

        var startProbe = await TryProbePathCombatStartAsync(context, playerPosition).ConfigureAwait(false);
        if (startProbe is null)
        {
            return null;
        }

        var accessPathDistance = ReadPathCombatAccessPathDistance();
        if (startProbe.Distance <= accessPathDistance)
        {
            state.MarkStartupRecoveryChecked();
            return null;
        }

        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            state.MarkStartupRecoveryChecked();
            LogThrottled(context, state, "stationary_combat.path_combat.access_path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["combatPathName"] = startProbe.PathName,
                ["combatPathDistance"] = Math.Round(startProbe.Distance, 2),
                ["accessPathDistance"] = Math.Round(accessPathDistance, 2),
                ["revivePathName"] = GetRevivePathName(context),
                ["reason"] = homeResult.Error
            });
            return null;
        }

        var home = homeResult.Value.Position;
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        context.Logger.Info("stationary_combat.path_combat.access_path_needed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["combatPathName"] = startProbe.PathName,
            ["combatStartPointIndex"] = startProbe.PointIndex,
            ["combatStartPointNumber"] = startProbe.PointIndex + 1,
            ["combatPathPointCount"] = startProbe.PointCount,
            ["combatPathDistance"] = Math.Round(startProbe.Distance, 2),
            ["accessPathDistance"] = Math.Round(accessPathDistance, 2),
            ["revivePathName"] = GetRevivePathName(context),
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2)
        });

        return await TickStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> ContinuePathCombatAccessPathAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        double radius)
    {
        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            state.ClearStartupRecovery();
            LogThrottled(context, state, "stationary_combat.path_combat.access_path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["combatPathName"] = GetCombatPathName(context),
                ["revivePathName"] = GetRevivePathName(context),
                ["reason"] = homeResult.Error
            });
            return null;
        }

        var home = homeResult.Value.Position;
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        return await ContinueStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryHandlePathCombatTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        double radius,
        bool allowClaimedByOther)
    {
        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition)
            .ConfigureAwait(false);
        if (target is not null && semiAutoState.IsMaintenanceResting)
        {
            if (!await TryInterruptMaintenanceRestForDefenseAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    "path_combat_defense_target_detected")
                .ConfigureAwait(false))
            {
                return IdleDelay;
            }
        }
        if (target is null)
        {
            if (await TryHandleStationaryMaintenanceAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player,
                        allowSitMaintenance: true)
                    .ConfigureAwait(false))
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return IdleDelay;
            }

            target = await SelectTargetAsync(
                    context,
                    state,
                    playerPosition,
                    playerPosition,
                    radius,
                    allowClaimedByOther)
                .ConfigureAwait(false);
        }

        if (target?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            return null;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        var candidateChanged = state.MarkCandidate(target, DateTimeOffset.Now);
        state.PathCombat.MarkCurrentTargetAnchor(playerPosition);
        state.CurrentTargetIsRevivePathClear = false;
        state.CurrentTargetBypassesHomeLeash = IsMaintenanceDefenseTarget(target, state);
        if (IsMaintenanceDefenseTarget(target, state))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
        }

        var targetPosition = target.Position.Value;
        var targetDistanceFromAnchor = StationaryCombatTargetSelector.HorizontalDistance(targetPosition, playerPosition);
        var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (candidateChanged)
        {
            state.FacedCandidateEntityId = 0;
            state.ClearPendingTabVerification();
            context.Logger.Info("stationary_combat.path_combat.target_selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.PathCombat.PathName,
                ["pointIndex"] = state.PathCombat.PointIndex,
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name,
                ["playerDistanceToTarget"] = Math.Round(playerDistanceToTarget, 2),
                ["targetDistanceFromAnchor"] = Math.Round(targetDistanceFromAnchor, 2),
                ["radius"] = Math.Round(radius, 2),
                ["targetingMe"] = target.IsTargetingLocalPlayer,
                ["serverObjectId"] = target.ServerObjectId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetingServerObjectId"] = target.TargetServerObjectId,
                ["aggressiveKnown"] = target.AggressiveKnown,
                ["aggressiveToPlayer"] = target.IsAggressiveToPlayer,
                ["passiveToPlayer"] = target.IsPassiveToPlayer,
                ["aggressiveSource"] = target.AggressiveSource
            });
        }

        if (!IsTargetingLocalSide(target, state) &&
            !state.IsTeamLeaderProtectionTarget(target) &&
            targetDistanceFromAnchor > radius)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            state.ClearTarget();
            return IdleDelay;
        }

        if (IsTargetTimedOut(state, DateTimeOffset.Now))
        {
            return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.EntityId,
                    target.ServerObjectId,
                    target.Name,
                    "path_combat_not_locked")
                .ConfigureAwait(false);
        }

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (state.IsPendingTabCandidate(target))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    playerPosition,
                    radius)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                playerPosition,
                radius,
                allowLockedFallback: false,
                phase: "path_combat")
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        if (state.FacedCandidateEntityId != target.EntityId)
        {
            var isFacingTarget = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
            if (!isFacingTarget)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                return MoveTickDelay;
            }

            state.FacedCandidateEntityId = target.EntityId;
            if (state.JumpAssist is not null)
            {
                await state.JumpAssist
                    .StartSoloTargetAsync(
                        target.EntityId,
                        target.ServerObjectId,
                        target.Name,
                        target.CurrentHp)
                    .ConfigureAwait(false);
            }
        }

        if (playerDistanceToTarget > AcquireDistance)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await PathFollowStepAsync(context, state, player, targetPosition, AcquireDistance).ConfigureAwait(false);
            await TryJumpCombatApproachIfStuckAsync(
                    context,
                    state,
                    target,
                    playerPosition,
                    playerDistanceToTarget,
                    "path_combat")
                .ConfigureAwait(false);
            return MoveTickDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target, playerPosition, radius).ConfigureAwait(false);
    }

    private async Task<bool> TryStartPathCombatAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition)
    {
        var combatPathName = GetCombatPathName(context);
        var paths = context.Config.ScriptSettings?.Paths;
        if (state.PathCombat.Completed &&
            (paths?.LoopPath ?? true) == false &&
            string.Equals(state.PathCombat.CompletedPathName, combatPathName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_pathStore is null || string.IsNullOrWhiteSpace(combatPathName))
        {
            LogThrottled(context, state, "stationary_combat.path_combat.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = combatPathName,
                ["reason"] = _pathStore is null ? "path_store_missing" : "path_name_missing"
            });
            return false;
        }

        var pathResult = await _pathStore.LoadAsync(combatPathName, context.StopToken).ConfigureAwait(false);
        if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
        {
            LogThrottled(context, state, "stationary_combat.path_combat.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = combatPathName,
                ["error"] = pathResult.Error,
                ["pointCount"] = pathResult.Value?.PointCount ?? 0
            });
            return false;
        }

        var combatPoints = points
            .Select(point => point.ToVector3())
            .ToArray();
        var nearestPointIndex = FindNearestPathPointIndex(playerPosition, combatPoints, double.MaxValue);
        if (nearestPointIndex < 0)
        {
            LogThrottled(context, state, "stationary_combat.path_combat.path_unavailable", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = combatPathName,
                ["reason"] = "nearest_point_missing",
                ["pathPointCount"] = combatPoints.Length
            });
            return false;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            combatPoints[nearestPointIndex]);
        state.PathCombat.Start(combatPathName, combatPoints, nearestPointIndex);
        state.ReturningHome = false;
        state.ClearTarget();
        context.Logger.Info("stationary_combat.path_combat.path_selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = combatPathName,
            ["startPointIndex"] = nearestPointIndex,
            ["startPointNumber"] = nearestPointIndex + 1,
            ["pathPointCount"] = combatPoints.Length,
            ["pathPointDistance"] = Math.Round(nearestDistance, 2)
        });
        return true;
    }

    private async Task<PathCombatStartProbe?> TryProbePathCombatStartAsync(
        AccountWorkerContext context,
        Vector3Snapshot playerPosition)
    {
        var combatPathName = GetCombatPathName(context);
        if (_pathStore is null || string.IsNullOrWhiteSpace(combatPathName))
        {
            return null;
        }

        var pathResult = await _pathStore.LoadAsync(combatPathName, context.StopToken).ConfigureAwait(false);
        if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
        {
            return null;
        }

        var combatPoints = points
            .Select(point => point.ToVector3())
            .ToArray();
        var nearestPointIndex = FindNearestPathPointIndex(playerPosition, combatPoints, double.MaxValue);
        if (nearestPointIndex < 0)
        {
            return null;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            combatPoints[nearestPointIndex]);
        return new PathCombatStartProbe(
            combatPathName,
            nearestPointIndex,
            combatPoints.Length,
            nearestDistance);
    }

    private async Task<TimeSpan> ContinuePathCombatAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition)
    {
        var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
        var advancedPointCount = 0;
        var maxPointAdvances = Math.Max(1, state.PathCombat.Points.Count);
        var pathFollowReachDistance = ResolvePathFollowReachDistance(context.Config.ScriptSettings?.Combat);
        while (state.PathCombat.Active &&
               state.PathCombat.PointIndex >= 0 &&
               state.PathCombat.PointIndex < state.PathCombat.Points.Count)
        {
            var point = state.PathCombat.Points[state.PathCombat.PointIndex];
            var distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, point);
            if (distance > pathFollowReachDistance)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, point, pathFollowReachDistance).ConfigureAwait(false);
                await TryJumpPathCombatIfStuckAsync(
                        context,
                        state,
                        playerPosition,
                        state.PathCombat.PointIndex,
                        distance)
                    .ConfigureAwait(false);
                LogActionThrottled(context, state, "stationary_combat.path_combat.path_follow", "move:" + state.PathCombat.PointIndex, new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["action"] = "move",
                    ["pathName"] = state.PathCombat.PathName,
                    ["pointIndex"] = state.PathCombat.PointIndex,
                    ["pointNumber"] = state.PathCombat.PointIndex + 1,
                    ["pointCount"] = state.PathCombat.Points.Count,
                    ["distance"] = Math.Round(distance, 2)
                }, TimeSpan.FromMilliseconds(500));
                return MoveTickDelay;
            }

            context.Logger.Info("stationary_combat.path_combat.point_reached", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.PathCombat.PathName,
                ["pointIndex"] = state.PathCombat.PointIndex,
                ["pointNumber"] = state.PathCombat.PointIndex + 1,
                ["pointCount"] = state.PathCombat.Points.Count,
                ["distance"] = Math.Round(distance, 2)
            });
            state.PathCombat.AdvancePoint(paths.LoopPath, paths.ReverseAtEnd);
            advancedPointCount++;
            if (advancedPointCount >= maxPointAdvances)
            {
                return IdleDelay;
            }
        }

        if (!state.PathCombat.Active)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            if (state.PathCombat.Completed)
            {
                context.Logger.Info("stationary_combat.path_combat.path_complete", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pathName"] = state.PathCombat.CompletedPathName
                });
            }
        }

        return IdleDelay;
    }

    private async Task TryJumpPathCombatIfStuckAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        int pointIndex,
        double distanceToPoint)
    {
        var now = DateTimeOffset.Now;
        var pathCombat = state.PathCombat;
        var minProgressDistance = ReadDeathRevivePathStuckDistance();
        if (pathCombat.PathStuckPointIndex != pointIndex ||
            pathCombat.PathLastProgressPosition is null ||
            pathCombat.PathLastProgressAt == DateTimeOffset.MinValue)
        {
            pathCombat.MarkPathProgress(pointIndex, playerPosition, now);
            return;
        }

        var moved = StationaryCombatTargetSelector.HorizontalDistance(
            pathCombat.PathLastProgressPosition.Value,
            playerPosition);
        if (moved >= minProgressDistance)
        {
            pathCombat.MarkPathProgress(pointIndex, playerPosition, now);
            return;
        }

        var stuckMs = ReadDeathRevivePathStuckMs();
        var stuckFor = now - pathCombat.PathLastProgressAt;
        if (stuckFor.TotalMilliseconds < stuckMs)
        {
            return;
        }

        if (pathCombat.LastPathJumpAt != DateTimeOffset.MinValue &&
            (now - pathCombat.LastPathJumpAt).TotalMilliseconds < stuckMs)
        {
            return;
        }

        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        var jumpHold = TimeSpan.FromMilliseconds(ReadDeathRevivePathJumpHoldMs());
        var result = await _input.PressKeyAsync("Space", jumpHold, context.StopToken).ConfigureAwait(false);
        pathCombat.MarkPathJump(now);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.path_combat.path_stuck_jump_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = pathCombat.PathName,
                ["pointIndex"] = pointIndex,
                ["pointNumber"] = pointIndex + 1,
                ["distance"] = Math.Round(distanceToPoint, 2),
                ["moved"] = Math.Round(moved, 2),
                ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
                ["thresholdMs"] = stuckMs,
                ["progressDistance"] = Math.Round(minProgressDistance, 2),
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.path_combat.path_stuck_jump", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = pathCombat.PathName,
            ["pointIndex"] = pointIndex,
            ["pointNumber"] = pointIndex + 1,
            ["distance"] = Math.Round(distanceToPoint, 2),
            ["moved"] = Math.Round(moved, 2),
            ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
            ["thresholdMs"] = stuckMs,
            ["progressDistance"] = Math.Round(minProgressDistance, 2),
            ["jumpCount"] = pathCombat.PathJumpCount,
            ["movingForward"] = state.IsMovingForward
        });
    }

    private async Task TryJumpReturnHomeIfStuckAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double distanceToHome,
        string phase)
    {
        if (!state.IsMovingForward)
        {
            state.ResetReturnHomeStuckTracking();
            return;
        }

        var now = DateTimeOffset.Now;
        var minProgressDistance = ReadDeathRevivePathStuckDistance();
        if (state.ReturnHomeLastProgressPosition is null ||
            state.ReturnHomeLastProgressAt == DateTimeOffset.MinValue)
        {
            state.MarkReturnHomeProgress(playerPosition, now);
            return;
        }

        var moved = StationaryCombatTargetSelector.HorizontalDistance(
            state.ReturnHomeLastProgressPosition.Value,
            playerPosition);
        if (moved >= minProgressDistance)
        {
            state.MarkReturnHomeProgress(playerPosition, now);
            return;
        }

        var stuckMs = ReadDeathRevivePathStuckMs();
        var stuckFor = now - state.ReturnHomeLastProgressAt;
        if (stuckFor.TotalMilliseconds < stuckMs)
        {
            return;
        }

        if (state.LastReturnHomeJumpAt != DateTimeOffset.MinValue &&
            (now - state.LastReturnHomeJumpAt).TotalMilliseconds < stuckMs)
        {
            return;
        }

        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        var jumpHold = TimeSpan.FromMilliseconds(ReadDeathRevivePathJumpHoldMs());
        var result = await _input.PressKeyAsync("Space", jumpHold, context.StopToken).ConfigureAwait(false);
        state.MarkReturnHomeJump(now);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.return_home.path_stuck_jump_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["homeX"] = Math.Round(home.X, 2),
                ["homeY"] = Math.Round(home.Y, 2),
                ["distance"] = Math.Round(distanceToHome, 2),
                ["moved"] = Math.Round(moved, 2),
                ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
                ["thresholdMs"] = stuckMs,
                ["progressDistance"] = Math.Round(minProgressDistance, 2),
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.return_home.path_stuck_jump", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["phase"] = phase,
            ["homeX"] = Math.Round(home.X, 2),
            ["homeY"] = Math.Round(home.Y, 2),
            ["distance"] = Math.Round(distanceToHome, 2),
            ["moved"] = Math.Round(moved, 2),
            ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
            ["thresholdMs"] = stuckMs,
            ["progressDistance"] = Math.Round(minProgressDistance, 2),
            ["jumpCount"] = state.ReturnHomeJumpCount,
            ["movingForward"] = state.IsMovingForward
        });
    }

    private async Task TryJumpStartupRecoveryIfStuckAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        int pointIndex,
        double distanceToPoint)
    {
        var now = DateTimeOffset.Now;
        var minProgressDistance = ReadDeathRevivePathStuckDistance();
        if (state.StartupRecoveryStuckPointIndex != pointIndex ||
            state.StartupRecoveryLastProgressPosition is null ||
            state.StartupRecoveryLastProgressAt == DateTimeOffset.MinValue)
        {
            state.MarkStartupRecoveryProgress(pointIndex, playerPosition, now);
            return;
        }

        var moved = StationaryCombatTargetSelector.HorizontalDistance(
            state.StartupRecoveryLastProgressPosition.Value,
            playerPosition);
        if (moved >= minProgressDistance)
        {
            state.MarkStartupRecoveryProgress(pointIndex, playerPosition, now);
            return;
        }

        var stuckMs = ReadDeathRevivePathStuckMs();
        var stuckFor = now - state.StartupRecoveryLastProgressAt;
        if (stuckFor.TotalMilliseconds < stuckMs)
        {
            return;
        }

        if (state.LastStartupRecoveryJumpAt != DateTimeOffset.MinValue &&
            (now - state.LastStartupRecoveryJumpAt).TotalMilliseconds < stuckMs)
        {
            return;
        }

        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        var jumpHold = TimeSpan.FromMilliseconds(ReadDeathRevivePathJumpHoldMs());
        var result = await _input.PressKeyAsync("Space", jumpHold, context.StopToken).ConfigureAwait(false);
        state.MarkStartupRecoveryJump(now);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.startup_recovery.path_stuck_jump_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = state.StartupRecoveryPathName,
                ["pointIndex"] = pointIndex,
                ["pointNumber"] = pointIndex + 1,
                ["distance"] = Math.Round(distanceToPoint, 2),
                ["moved"] = Math.Round(moved, 2),
                ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
                ["thresholdMs"] = stuckMs,
                ["progressDistance"] = Math.Round(minProgressDistance, 2),
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.startup_recovery.path_stuck_jump", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.StartupRecoveryPathName,
            ["pointIndex"] = pointIndex,
            ["pointNumber"] = pointIndex + 1,
            ["distance"] = Math.Round(distanceToPoint, 2),
            ["moved"] = Math.Round(moved, 2),
            ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
            ["thresholdMs"] = stuckMs,
            ["progressDistance"] = Math.Round(minProgressDistance, 2),
            ["jumpCount"] = state.StartupRecoveryJumpCount,
            ["movingForward"] = state.IsMovingForward
        });
    }

    private async Task<RecoveryDefenseSelection> SelectRecoveryDefenseTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        bool allowRevivePathClear)
    {
        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition,
                preloadedObjects: objects)
            .ConfigureAwait(false);
        if (target?.Position is not null)
        {
            if (state.HasSmartPreAimHandoff &&
                !state.IsSmartPreAimHandoffTarget(target.EntityId, target.ServerObjectId))
            {
                ReleaseSmartPreAimHandoff(context, state, "target_override", clearPreAimCandidate: true);
            }

            return new RecoveryDefenseSelection(target, false, false, false);
        }

        if (!allowRevivePathClear)
        {
            return RecoveryDefenseSelection.None;
        }

        var clearRadius = ResolveRevivePathAggressiveClearRadius(context.Config.ScriptSettings?.Paths);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        if (IsSmartPreAimEnabled(context) &&
            TryResolveSmartPreAimHandoffTarget(
                context,
                state,
                objects,
                DateTimeOffset.Now,
                playerPosition,
                clearRadius,
                allowClaimedByOther: false,
                activeMonsterNameFilters,
                out var handoffTarget,
                additionalEligibility: candidate =>
                    IsRevivePathSmartPreAimHandoffEligible(
                        candidate,
                        state,
                        playerPosition,
                        clearRadius)))
        {
            if (handoffTarget?.Position is null)
            {
                return new RecoveryDefenseSelection(null, false, true, true);
            }

            return new RecoveryDefenseSelection(
                handoffTarget,
                !IsMaintenanceDefenseTarget(handoffTarget, state),
                false,
                true);
        }

        target = await SelectRevivePathAggressiveClearTargetAsync(
                context,
                state,
                playerPosition,
                preloadedObjects: objects)
            .ConfigureAwait(false);
        return target?.Position is null
            ? RecoveryDefenseSelection.None
            : new RecoveryDefenseSelection(target, true, false, false);
    }

    private static bool IsRevivePathSmartPreAimHandoffEligible(
        WorldObjectSnapshot target,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        double clearRadius)
    {
        if (IsTargetingLocalSide(target, state) || WasSmartPreAimTargetingLocalSide(target, state))
        {
            return true;
        }

        return target.IsAggressiveToPlayer &&
               target.Position is { } position &&
               StationaryCombatTargetSelector.HorizontalDistance(position, playerPosition) <= clearRadius;
    }

    private static bool WasSmartPreAimTargetingLocalSide(
        WorldObjectSnapshot target,
        StationaryCombatState state)
    {
        lock (state.NextTargetPreAim.SyncRoot)
        {
            return state.NextTargetPreAim.TargetingLocalSide &&
                   StationaryCombatState.IsSameTarget(
                       state.NextTargetPreAim.TargetEntityId,
                       state.NextTargetPreAim.TargetServerObjectId,
                       target.EntityId,
                       target.ServerObjectId);
        }
    }

    private async Task<TimeSpan?> TryHandleRecoveryDefenseTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome,
        string recoveryPhase,
        bool? allowRevivePathClear = null)
    {
        if (state.Fighting)
        {
            return await TickFightAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    home,
                    radius,
                    playerDistanceFromHome)
                .ConfigureAwait(false);
        }

        var selection = await SelectRecoveryDefenseTargetAsync(
                context,
                state,
                playerPosition,
                allowRevivePathClear: allowRevivePathClear ?? IsRevivePathRecoveryPhase(recoveryPhase))
            .ConfigureAwait(false);
        if (selection.HoldForSmartPreAimHandoff)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            return IdleDelay;
        }

        var target = selection.Target;
        var isRevivePathClearTarget = selection.IsRevivePathClearTarget;
        if (target?.Position is null)
        {
            return null;
        }

        if (semiAutoState.IsMaintenanceResting)
        {
            if (isRevivePathClearTarget &&
                !IsMaintenanceDefenseTarget(target, state) &&
                !IsDeathRecoveryRevivePathActive(state))
            {
                return IdleDelay;
            }

            if (!await TryInterruptMaintenanceRestForDefenseAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    recoveryPhase + "_defense_target_detected")
                .ConfigureAwait(false))
            {
                return IdleDelay;
            }
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        var candidateChanged = state.MarkCandidate(target, DateTimeOffset.Now);
        state.CurrentTargetIsRevivePathClear = isRevivePathClearTarget;
        state.CurrentTargetBypassesHomeLeash = isRevivePathClearTarget || IsMaintenanceDefenseTarget(target, state);
        if (IsMaintenanceDefenseTarget(target, state))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
        }

        var targetPosition = target.Position.Value;
        var targetDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(targetPosition, home);
        var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (candidateChanged)
        {
            state.FacedCandidateEntityId = 0;
            state.ClearPendingTabVerification();
            context.Logger.Info("stationary_combat.recovery_defense.target_selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = recoveryPhase,
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name,
                ["playerDistanceToTarget"] = Math.Round(playerDistanceToTarget, 2),
                ["targetDistanceFromHome"] = Math.Round(targetDistanceFromHome, 2),
                ["radius"] = Math.Round(radius, 2),
                ["targetingMe"] = target.IsTargetingLocalPlayer,
                ["serverObjectId"] = target.ServerObjectId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetingServerObjectId"] = target.TargetServerObjectId,
                ["aggressiveKnown"] = target.AggressiveKnown,
                ["aggressiveToPlayer"] = target.IsAggressiveToPlayer,
                ["passiveToPlayer"] = target.IsPassiveToPlayer,
                ["aggressiveSource"] = target.AggressiveSource,
                ["revivePathClear"] = isRevivePathClearTarget,
                ["smartPreAimHandoff"] = selection.IsSmartPreAimHandoffTarget
            });
        }

        if (IsTargetTimedOut(state, DateTimeOffset.Now))
        {
            return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.EntityId,
                    target.ServerObjectId,
                    target.Name,
                    recoveryPhase + "_not_locked")
                .ConfigureAwait(false);
        }

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (state.IsPendingTabCandidate(target))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    home,
                    radius)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                allowLockedFallback: false,
                phase: recoveryPhase)
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        if (state.FacedCandidateEntityId != target.EntityId)
        {
            var isFacingTarget = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
            if (!isFacingTarget)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                return MoveTickDelay;
            }

            state.FacedCandidateEntityId = target.EntityId;
        }

        if (playerDistanceToTarget > AcquireDistance)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await PathFollowStepAsync(context, state, player, targetPosition, AcquireDistance).ConfigureAwait(false);
            await TryJumpCombatApproachIfStuckAsync(
                    context,
                    state,
                    target,
                    playerPosition,
                    playerDistanceToTarget,
                    recoveryPhase)
                .ConfigureAwait(false);
            return MoveTickDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target, home, radius).ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickFightAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome)
    {
        var now = DateTimeOffset.Now;
        var target = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!state.IsCurrentTarget(target))
        {
            StopNextTargetPreAim(context, state, "current_target_mismatch", clearCandidate: true);
            if (IsTargetTimedOut(state, now))
            {
                return await IgnoreCurrentTargetAsync(
                        context,
                        semiAutoState,
                        state,
                        state.CurrentTargetEntityId,
                        state.CurrentTargetServerObjectId,
                        string.Empty,
                        "not_locked")
                    .ConfigureAwait(false);
            }

            return await ReacquireCurrentFightTargetAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    home,
                    radius,
                    playerDistanceFromHome,
                    target,
                    "target_mismatch")
                .ConfigureAwait(false);
        }

        if (state.JumpAssist is not null)
        {
            await state.JumpAssist
                .ObserveSoloTargetHealthAsync(
                    target.TargetEntityId,
                    target.ServerObjectId,
                    target.CurrentHp)
                .ConfigureAwait(false);
        }

        if (!target.IsMonsterAlive)
        {
            MarkStationaryKillIfNeeded(context, target, "locked_target_dead");
            if (ShouldStartLootAfterKill(context, target))
            {
                state.StartLootAfterKill(target, now);
                semiAutoState.ResetAttackKeyPressThrottle();
                context.Logger.Info("stationary_combat.loot.started", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = target.TargetEntityId,
                    ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name,
                    ["currentHp"] = target.CurrentHp,
                    ["maxHp"] = target.MaxHp
                });
                return await TickLootAfterKillAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player)
                    .ConfigureAwait(false);
            }

            StopNextTargetPreAim(context, state, "current_target_dead", clearCandidate: false);
            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            await RunPostCombatMaintenanceRoundAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    killedTarget: target,
                    logEventPrefix: "stationary_combat")
                .ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        state.SetCurrentTarget(target);
        if (IsTargetingLocalSide(target, state) ||
            state.IsTeamLeaderProtectionTarget(target))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
        }

        var claimedDelay = await TryIgnoreClaimedLockedTargetAsync(
                context,
                semiAutoState,
                state,
                target)
            .ConfigureAwait(false);
        if (claimedDelay is not null)
        {
            return claimedDelay.Value;
        }

        var radarApproachDelay = await TryHandleLockedRadarApproachAsync(
                context,
                semiAutoState,
                state,
                player,
                target,
                home,
                radius)
            .ConfigureAwait(false);
        if (radarApproachDelay is not null)
        {
            return radarApproachDelay.Value;
        }

        var softRestartDelay = await TryRecoverStalledLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                target,
                now)
            .ConfigureAwait(false);
        if (softRestartDelay is not null)
        {
            return softRestartDelay.Value;
        }

        var noDamageDelay = await TryIgnoreNoDamageNoTargetingLockedTargetAsync(
                context,
                semiAutoState,
                state,
                target,
                now,
                "fight")
            .ConfigureAwait(false);
        if (noDamageDelay is not null)
        {
            return noDamageDelay.Value;
        }

        if (IsTargetTimedOut(state, now))
        {
            if (!TryKeepTimedOutMaintenanceDefenseTarget(context, state, target, now, "not_dead"))
            {
                return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.TargetEntityId,
                    target.ServerObjectId,
                    target.Name,
                    "not_dead")
                .ConfigureAwait(false);
            }
        }

        if (!state.CurrentTargetIsMaintenanceDefense &&
            !state.CurrentTargetIsRevivePathClear &&
            !state.CurrentTargetBypassesHomeLeash &&
            target.Position is not null &&
            StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) > radius + TargetLeashExtraDistance)
        {
            LogActionThrottled(context, state, "stationary_combat.target.leash_wait", "target:" + target.TargetEntityId, new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.TargetEntityId,
                ["targetName"] = target.Name,
                ["distanceFromHome"] = Math.Round(
                    StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home),
                    2),
                ["allowedDistance"] = Math.Round(radius + TargetLeashExtraDistance, 2)
            }, TimeSpan.FromMilliseconds(500));
            StopNextTargetPreAim(context, state, "current_target_outside_leash", clearCandidate: true);
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.TargetEntityId,
                    target.ServerObjectId,
                    target.Name,
                    playerDistanceFromHome,
                    radius,
                    target,
                    "target_outside_leash")
                .ConfigureAwait(false);
        }

        await MaintainLeaderTacticalMarkAsync(context, state, target).ConfigureAwait(false);
        var openingDelay = await TryWaitForLockedTargetToTargetPlayerAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                "fight")
            .ConfigureAwait(false);
        if (openingDelay is not null)
        {
            return openingDelay.Value;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        EnsureNextTargetPreAimRunning(context, state, target, player, home, radius);
        return await _semiAuto
            .TickAsync(
                context,
                plan,
                semiAutoState,
                requireCooldownCalibrationForMaintenance: true,
                jumpAssist: state.JumpAssist,
                ensureHpMaintenanceTargetBeforeKeyPress: () =>
                    EnsureOrdinaryFightTargetAfterOwnPetSelectionAsync(context, state),
                suppressSpiritmasterPetSummon: ShouldSuppressSpiritmasterPetSummonForDeathRecoveryDefense(state))
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryHandleLockedRadarApproachAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        LockedTargetSnapshot target,
        Vector3Snapshot home,
        double radius)
    {
        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var radarSettings = _obstacleNavigator?.ResolveSettings(
                                context.Config.AccountName,
                                combat.RadarObstacleAvoidance)
                            ?? new RadarObstacleScriptSettings();
        if (!radarSettings.Enabled || target.Position is not { } targetPosition)
        {
            return null;
        }

        var playerPosition = player.Position!.Value;

        var outsideLeash = !state.CurrentTargetIsMaintenanceDefense &&
                           !state.CurrentTargetIsRevivePathClear &&
                           !state.CurrentTargetBypassesHomeLeash &&
                           StationaryCombatTargetSelector.HorizontalDistance(targetPosition, home) >
                           radius + TargetLeashExtraDistance;
        if (outsideLeash)
        {
            return null;
        }

        var distanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        var navigation = await ResolveObstacleNavigationAsync(
                context,
                state,
                playerPosition,
                targetPosition,
                RadarNavigationPurpose.ApproachTarget,
                target.ServerObjectId,
                radarSettings,
                AcquireDistance)
            .ConfigureAwait(false);
        if (navigation?.Action == RadarNavigationAction.Unreachable)
        {
            return await TemporarilyExcludeRadarUnreachableLockedTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target,
                    navigation)
                .ConfigureAwait(false);
        }

        var requiresMovement = navigation?.Action == RadarNavigationAction.MoveToWaypoint ||
                               navigation?.Action == RadarNavigationAction.Direct &&
                               distanceToTarget > AcquireDistance;
        if (!requiresMovement || navigation is null)
        {
            return null;
        }

        state.ResetCurrentTargetStallObservation();
        state.ResetCurrentTargetDamageObservation();
        semiAutoState.ResetAttackKeyPressThrottle();
        Func<PathFollowStopContext, Task<bool>>? afterWaypointStopAsync =
            navigation.Action == RadarNavigationAction.MoveToWaypoint
                ? stop => TryCommitRadarDirectAfterWaypointStopAsync(
                    context,
                    state,
                    target.ServerObjectId,
                    radarSettings,
                    RadarDirectTargetSource.LockedTarget,
                    "locked_target_approach",
                    stop)
                : null;
        await PathFollowStepAsync(
                context,
                state,
                player,
                ToVector3(navigation.Destination, playerPosition.Z),
                navigation.ReachDistanceMeters,
                afterWaypointStopAsync)
            .ConfigureAwait(false);
        LogRadarNavigation(context, state, navigation, "locked_target_approach");
        var worldTarget = new WorldObjectSnapshot(
            target.TargetEntityId,
            target.ServerObjectId,
            target.Name,
            "monster",
            target.Position,
            target.DistanceToLocalPlayer,
            target.CurrentHp,
            target.MaxHp,
            target.TargetServerObjectId,
            target.IsTargetingLocalPlayer);
        await TryJumpCombatApproachIfStuckAsync(
                context,
                state,
                worldTarget,
                playerPosition,
                distanceToTarget,
                "radar_locked_target")
            .ConfigureAwait(false);
        return MoveTickDelay;
    }

    private async Task<bool> EnsureOrdinaryFightTargetAfterOwnPetSelectionAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var scriptSettings = context.Config.ScriptSettings;
        var mainMode = scriptSettings?.MainMode ?? context.Config.MainMode;
        var combatMode = scriptSettings?.CombatMode ?? context.Config.CombatMode;
        var expectedEntityId = state.CurrentTargetEntityId;
        var expectedServerObjectId = state.CurrentTargetServerObjectId;
        var localPetServerObjectId = state.LocalCombatSidePetServerObjectId;
        if (mainMode != AccountMainMode.CustomCombat ||
            combatMode != AccountCombatMode.Stationary ||
            !state.Fighting ||
            state.CurrentTargetIsRevivePathClear ||
            state.CurrentTargetIsGatherSafetyClear ||
            state.CurrentTargetIsTacticalMark ||
            expectedEntityId == 0 ||
            expectedServerObjectId == 0 ||
            localPetServerObjectId == 0)
        {
            return true;
        }

        var lockedResult = await ReadLockedTargetForActionAsync(context).ConfigureAwait(false);
        if (lockedResult.ServerObjectId != localPetServerObjectId)
        {
            return true;
        }

        var petTarget = lockedResult;
        context.Logger.Warn("stationary_combat.own_pet_target_recovery.detected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["expectedEntityId"] = expectedEntityId,
            ["expectedServerObjectId"] = expectedServerObjectId,
            ["lockedEntityId"] = petTarget.TargetEntityId,
            ["lockedServerObjectId"] = petTarget.ServerObjectId,
            ["localPetServerObjectId"] = localPetServerObjectId,
            ["lockedName"] = petTarget.Name
        });

        var tabResult = await _input
            .PressKeyAsync("Tab", TimeSpan.FromMilliseconds(25), context.StopToken)
            .ConfigureAwait(false);
        if (!tabResult.Success)
        {
            context.Logger.Warn("stationary_combat.own_pet_target_recovery.tab_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["expectedEntityId"] = expectedEntityId,
                ["expectedServerObjectId"] = expectedServerObjectId,
                ["localPetServerObjectId"] = localPetServerObjectId,
                ["error"] = tabResult.Error
            });
            return false;
        }

        context.Logger.Info("stationary_combat.own_pet_target_recovery.tab_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["expectedEntityId"] = expectedEntityId,
            ["expectedServerObjectId"] = expectedServerObjectId,
            ["localPetServerObjectId"] = localPetServerObjectId
        });

        var verifyDelayMs = ReadTabVerifyDelayMs();
        var pollMs = ReadTabVerifyPollMs();
        var elapsedMs = 0;
        do
        {
            if (verifyDelayMs > 0)
            {
                var waitMs = Math.Min(pollMs, verifyDelayMs - elapsedMs);
                await DelayAsync(TimeSpan.FromMilliseconds(waitMs), context).ConfigureAwait(false);
                elapsedMs += waitMs;
            }

            var verifyResult = await ReadLockedTargetForActionAsync(context).ConfigureAwait(false);
            var matched = verifyResult is { IsMonsterAlive: true } verifiedTarget &&
                          StationaryCombatState.IsSameTarget(
                              expectedEntityId,
                              expectedServerObjectId,
                              verifiedTarget.TargetEntityId,
                              verifiedTarget.ServerObjectId);
            context.Logger.Info("stationary_combat.own_pet_target_recovery.verify", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["expectedEntityId"] = expectedEntityId,
                ["expectedServerObjectId"] = expectedServerObjectId,
                ["lockedReadSuccess"] = true,
                ["lockedEntityId"] = verifyResult.TargetEntityId,
                ["lockedServerObjectId"] = verifyResult.ServerObjectId,
                ["lockedName"] = verifyResult.Name,
                ["lockedAlive"] = verifyResult.IsMonsterAlive,
                ["matched"] = matched,
                ["elapsedMs"] = elapsedMs,
                ["error"] = null
            });
            if (matched)
            {
                return true;
            }
        }
        while (elapsedMs < verifyDelayMs);

        return false;
    }

    private async Task<TimeSpan> TickLootAfterKillAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        await StopSoloJumpAsync(state, "loot_after_kill").ConfigureAwait(false);
        while (!context.StopToken.IsCancellationRequested)
        {
            if (state.LootAfterKill.Step == StationaryCombatLootAfterKillStep.Complete)
            {
                FinishLootAfterKill(context, state, "complete", success: true);
                return IdleDelay;
            }

            var status = state.LootAfterKill.Step switch
            {
                StationaryCombatLootAfterKillStep.StopInput => await TickLootStopInputNodeAsync(
                        context,
                        semiAutoState,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.WaitAfterKill => TickLootWaitAfterKillNode(state),
                StationaryCombatLootAfterKillStep.PressLootKey => await TickLootPressLootKeyNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.WaitNearCorpse => await TickLootWaitNearCorpseNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.WaitAfterNear => TickLootWaitAfterNearNode(state),
                StationaryCombatLootAfterKillStep.PostCombatMaintenance => await TickLootPostCombatMaintenanceNodeAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player)
                    .ConfigureAwait(false),
                _ => StationaryCombatBehaviorStatus.Success
            };

            if (status == StationaryCombatBehaviorStatus.Running)
            {
                return TimeSpan.FromMilliseconds(ReadLootTickMs());
            }

            if (status == StationaryCombatBehaviorStatus.Failure)
            {
                FinishLootAfterKill(context, state, "failed", success: false);
                return IdleDelay;
            }

            state.LootAfterKill.Advance(DateTimeOffset.Now);
        }

        return IdleDelay;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootStopInputNodeAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        state.ReturningHome = false;
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        context.Logger.Info("stationary_combat.loot.stop_input", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["targetName"] = state.LootAfterKill.KilledTargetName
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private static StationaryCombatBehaviorStatus TickLootWaitAfterKillNode(StationaryCombatState state)
    {
        return DateTimeOffset.Now - state.LootAfterKill.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootAfterKillWaitMs())
            ? StationaryCombatBehaviorStatus.Success
            : StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPressLootKeyNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var now = DateTimeOffset.Now;
        var attemptTtl = TimeSpan.FromMilliseconds(ReadLootAttemptCacheTtlMs());
        if (state.HasAttemptedLootCorpse(
                state.LootAfterKill.KilledTargetEntityId,
                state.LootAfterKill.KilledTargetServerObjectId,
                now,
                attemptTtl))
        {
            LogLootSkipped(
                context,
                state,
                "already_attempted",
                "attempt_cache",
                state.LootAfterKill.KilledTargetLootableRaw,
                state.LootAfterKill.KilledTargetInteractionState);
            state.LootAfterKill.MoveToPostCombatMaintenance(now);
            return StationaryCombatBehaviorStatus.Running;
        }

        var eligibility = await ReadLootAttemptEligibilityAsync(context, state).ConfigureAwait(false);
        if (!eligibility.HasLoot)
        {
            LogLootSkipped(
                context,
                state,
                eligibility.Reason,
                eligibility.Source,
                eligibility.LootableRaw,
                eligibility.InteractionState,
                eligibility.Error);
            state.LootAfterKill.MoveToPostCombatMaintenance(DateTimeOffset.Now);
            return StationaryCombatBehaviorStatus.Running;
        }

        var pressCount = ReadLootPressCount();
        var pressIntervalMs = ReadLootPressIntervalMs();
        for (var pressIndex = 0; pressIndex < pressCount; pressIndex++)
        {
            if (pressIndex > 0 && pressIntervalMs > 0)
            {
                await DelayAsync(TimeSpan.FromMilliseconds(pressIntervalMs), context).ConfigureAwait(false);
            }

            var result = await _input
                .PressKeyAsync("NumPadDecimal", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.loot.pick_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                    ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                    ["targetName"] = state.LootAfterKill.KilledTargetName,
                    ["pressIndex"] = pressIndex + 1,
                    ["error"] = result.Error
                });
                return StationaryCombatBehaviorStatus.Failure;
            }
        }

        state.MarkLootCorpseAttempted(
            state.LootAfterKill.KilledTargetEntityId,
            state.LootAfterKill.KilledTargetServerObjectId,
            DateTimeOffset.Now);
        state.LootAfterKill.MarkLootKeyPressed();
        context.Logger.Info("stationary_combat.loot.pick_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["targetName"] = state.LootAfterKill.KilledTargetName,
            ["lootRaw"] = eligibility.LootableRaw,
            ["interactionState"] = eligibility.InteractionState,
            ["lootabilitySource"] = eligibility.Source,
            ["pressCount"] = pressCount,
            ["pressIntervalMs"] = pressIntervalMs
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<LootAttemptEligibility> ReadLootAttemptEligibilityAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (lockedResult is { HasTarget: true } locked)
        {
            if (IsSameLootTarget(state.LootAfterKill, locked))
            {
                return new LootAttemptEligibility(
                    locked.HasLoot,
                    "locked_target",
                    locked.HasLoot ? "lootable" : "not_lootable",
                    locked.LootableRaw,
                    locked.InteractionState);
            }
        }
        var corpses = await ReadLootCorpsesAsync(context).ConfigureAwait(false);
        var corpse = corpses.FirstOrDefault(corpse => IsSameLootTarget(state.LootAfterKill, corpse));
        if (corpse is null)
        {
            return new LootAttemptEligibility(
                false,
                "loot_corpses",
                "corpse_not_found",
                state.LootAfterKill.KilledTargetLootableRaw,
                state.LootAfterKill.KilledTargetInteractionState);
        }

        return new LootAttemptEligibility(
            corpse.HasLoot,
            "loot_corpses",
            corpse.HasLoot ? "lootable" : "not_lootable",
            corpse.LootableRaw,
            corpse.InteractionState);
    }

    private static void LogLootSkipped(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        string source,
        uint lootableRaw,
        uint interactionState,
        string? error = null)
    {
        context.Logger.Info("stationary_combat.loot.skipped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["source"] = source,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["targetName"] = state.LootAfterKill.KilledTargetName,
            ["lootRaw"] = lootableRaw,
            ["interactionState"] = interactionState,
            ["error"] = error
        });
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootWaitNearCorpseNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var targetPosition = state.LootAfterKill.KilledTargetPosition;
        double? distance = null;

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (lockedResult is { HasTarget: true } locked)
        {
            if (!IsSameLootTarget(state.LootAfterKill, locked))
            {
                context.Logger.Info("stationary_combat.loot.target_changed_after_pick", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                    ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                    ["lockedEntityId"] = locked.TargetEntityId,
                    ["lockedServerObjectId"] = locked.ServerObjectId,
                    ["lockedName"] = locked.Name
                });
                return StationaryCombatBehaviorStatus.Success;
            }

            targetPosition = locked.Position ?? targetPosition;
            distance = locked.DistanceToLocalPlayer;
        }
        else
        {
            context.Logger.Info("stationary_combat.loot.target_missing_after_pick", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                ["targetName"] = state.LootAfterKill.KilledTargetName
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        if (!distance.HasValue && targetPosition is { } position)
        {
            var player = await ReadPlayerAsync(context).ConfigureAwait(false);
            distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position!.Value, position);
        }

        if (distance.HasValue && distance.Value <= ReadLootApproachDistance())
        {
            context.Logger.Info("stationary_combat.loot.near_corpse", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                ["targetName"] = state.LootAfterKill.KilledTargetName,
                ["distance"] = Math.Round(distance.Value, 2)
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        if (DateTimeOffset.Now - state.LootAfterKill.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootApproachTimeoutMs()))
        {
            context.Logger.Warn("stationary_combat.loot.approach_timeout", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                ["targetName"] = state.LootAfterKill.KilledTargetName,
                ["distance"] = distance.HasValue ? Math.Round(distance.Value, 2) : null,
                ["timeoutMs"] = ReadLootApproachTimeoutMs()
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        return StationaryCombatBehaviorStatus.Running;
    }

    private static StationaryCombatBehaviorStatus TickLootWaitAfterNearNode(StationaryCombatState state)
    {
        return DateTimeOffset.Now - state.LootAfterKill.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootAfterPickWaitMs())
            ? StationaryCombatBehaviorStatus.Success
            : StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPostCombatMaintenanceNodeAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (state.CleanupReturnToCombatActive)
        {
            await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
            var returnStatus = await TickCleanupReturnToCombatAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player)
                .ConfigureAwait(false);
            if (returnStatus == StationaryCombatBehaviorStatus.Running)
            {
                return StationaryCombatBehaviorStatus.Running;
            }
        }

        if (await TryPostponePostCombatMaintenanceForDefenseTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    player)
                .ConfigureAwait(false))
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        if (_bagCleanup is not null)
        {
            if (state.BagCleanup.Active)
            {
                await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
            }

            var cleanupResult = await _bagCleanup
                .TickAfterLootAsync(context, state.BagCleanup)
                .ConfigureAwait(false);
            if (cleanupResult.Status == BagCleanupTickStatus.Running)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                return StationaryCombatBehaviorStatus.Running;
            }

            if (cleanupResult.Status is BagCleanupTickStatus.Completed or BagCleanupTickStatus.RecoverableFailure)
            {
                state.StartCleanupReturnToCombat();
                context.Logger.Info("stationary_combat.bag_cleanup.return_to_combat.start", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["cleanupStatus"] = cleanupResult.Status.ToString(),
                    ["cleanupReason"] = cleanupResult.Reason,
                    ["revivePathName"] = GetRevivePathName(context)
                });

                return StationaryCombatBehaviorStatus.Running;
            }

            if (cleanupResult.Status == BagCleanupTickStatus.FatalFailure)
            {
                context.Logger.Warn("stationary_combat.bag_cleanup.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = cleanupResult.Reason,
                    ["error"] = cleanupResult.Error
                });
            }
        }

        await RunPostCombatMaintenanceRoundAsync(
                context,
                plan,
                semiAutoState,
                state,
                player)
            .ConfigureAwait(false);

        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task AbortDiscardForExternalInterruptionIfActiveAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason)
    {
        if (_bagCleanup is null || !state.BagCleanup.DiscardActive)
        {
            return;
        }

        await _bagCleanup
            .AbortDiscardForExternalInterruptionAsync(context, state.BagCleanup, reason)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryHandleStationaryMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true,
        bool allowSitMaintenance = true,
        bool clearSitWhenDisallowed = false)
    {
        const string pauseReason = "stationary_maintenance";
        state.JumpAssist?.Pause(pauseReason);
        try
        {
            return await _semiAuto
                .TryHandleMaintenanceAsync(
                    context,
                    semiAutoState,
                    player,
                    allowSitMaintenance: allowSitMaintenance,
                    clearSitWhenDisallowed: clearSitWhenDisallowed,
                    beforeMaintenanceKeyPress: async () =>
                    {
                        semiAutoState.ResetAttackKeyPressThrottle();
                        await StopMovementAsync(context, state).ConfigureAwait(false);
                        StopPathFollowPoller(state);
                    },
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: true,
                    runTiming: runTiming,
                    includeAlwaysRules: includeAlwaysRules)
                .ConfigureAwait(false);
        }
        finally
        {
            if (state.JumpAssist is not null)
            {
                await state.JumpAssist.WaitForTeamCooldownObservationAsync().ConfigureAwait(false);
            }

            state.JumpAssist?.Resume(pauseReason);
            UpdateMaintenanceRestJumpPause(state, semiAutoState);
        }
    }

    private async Task<bool> RunPostCombatMaintenanceRoundAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        LockedTargetSnapshot? killedTarget = null,
        string logEventPrefix = "stationary_combat.loot")
    {
        var handledAny = false;
        var currentPlayer = player;
        var targetEntityId = killedTarget?.TargetEntityId ?? state.LootAfterKill.KilledTargetEntityId;
        var targetServerObjectId = killedTarget?.ServerObjectId ?? state.LootAfterKill.KilledTargetServerObjectId;
        var targetName = killedTarget?.Name ?? state.LootAfterKill.KilledTargetName;
        for (var iteration = 1; iteration <= DefaultPostCombatMaintenanceRoundLimit; iteration++)
        {
            var handled = await TryHandleStationaryMaintenanceAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    currentPlayer,
                    MaintenanceRuleRunTiming.AfterCombat,
                    includeAlwaysRules: true,
                    allowSitMaintenance: true)
                .ConfigureAwait(false);
            if (!handled)
            {
                break;
            }

            handledAny = true;
            context.Logger.Info(logEventPrefix + ".post_combat_maintenance", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = targetEntityId,
                ["targetServerObjectId"] = targetServerObjectId,
                ["targetName"] = targetName,
                ["iteration"] = iteration
            });

            if (semiAutoState.IsMaintenanceResting)
            {
                break;
            }

            if (iteration == DefaultPostCombatMaintenanceRoundLimit)
            {
                context.Logger.Warn(logEventPrefix + ".post_combat_maintenance_limit", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetEntityId,
                    ["targetServerObjectId"] = targetServerObjectId,
                    ["targetName"] = targetName,
                    ["limit"] = DefaultPostCombatMaintenanceRoundLimit
                });
                break;
            }

            await Task.Delay(SemiAutoCombatController.MaintenanceGlobalKeyInterval, context.StopToken)
                .ConfigureAwait(false);

            currentPlayer = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (currentPlayer.IsDead)
            {
                context.Logger.Warn(logEventPrefix + ".post_combat_maintenance_player_dead", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetEntityId,
                    ["targetServerObjectId"] = targetServerObjectId,
                    ["targetName"] = targetName,
                    ["iteration"] = iteration
                });
                break;
            }
        }

        return handledAny;
    }

    private async Task<StationaryCombatBehaviorStatus> TickCleanupReturnToCombatAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        var playerPosition = player.Position!.Value;

        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            state.CompleteCleanupReturnToCombat();
            context.Logger.Warn("stationary_combat.bag_cleanup.return_to_combat.skipped", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = homeResult.Error,
                ["revivePathName"] = GetRevivePathName(context)
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var home = homeResult.Value.Position;
        var radius = Math.Max(1.0D, combat.StationaryCombatRadius);
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        var delay = state.StartupRecoveryActive
            ? await ContinueStartupRecoveryAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    home,
                    radius,
                    playerDistanceFromHome)
                .ConfigureAwait(false)
            : await TickStartupRecoveryAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    player,
                    playerPosition,
                    home,
                    radius,
                    playerDistanceFromHome)
                .ConfigureAwait(false);

        if (delay is not null || state.StartupRecoveryActive)
        {
            return StationaryCombatBehaviorStatus.Running;
        }

        state.CompleteCleanupReturnToCombat();
        context.Logger.Info("stationary_combat.bag_cleanup.return_to_combat.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["revivePathName"] = GetRevivePathName(context),
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2)
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<TimeSpan?> TickNoKillRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        var now = DateTimeOffset.Now;
        var runtime = context.RuntimeStates
            .Snapshot()
            .FirstOrDefault(item => string.Equals(
                item.AccountName,
                context.Config.AccountName,
                StringComparison.OrdinalIgnoreCase));
        state.NoKillRecovery.ObserveCombatActivity(runtime?.LastKillAt ?? runtime?.StartedAt, now);

        if (!state.NoKillRecovery.Active)
        {
            if (!state.NoKillRecovery.IsDue(now, ReadNoKillTimeout()))
            {
                return null;
            }

            var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
            var key = paths.TownReturnKey?.Trim() ?? string.Empty;
            var revivePathName = GetRevivePathName(context);
            if (string.IsNullOrWhiteSpace(key))
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "town_return_key_missing",
                    "Town return key is not configured.");
            }

            if (_pathStore is null || string.IsNullOrWhiteSpace(revivePathName))
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "revive_path_missing",
                    "Revive path is not configured.");
            }

            var pathResult = await _pathStore.LoadAsync(revivePathName, context.StopToken).ConfigureAwait(false);
            if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } pathPoints)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "revive_path_unavailable",
                    pathResult.Error ?? "Revive path has fewer than two points.");
            }

            var startPosition = player.Position!.Value;

            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            state.PathCombat.Reset();
            state.ClearStartupRecovery();
            state.ClearTarget();

            var press = await _input
                .PressKeyAsync(key, NoKillTownReturnHoldDuration, context.StopToken)
                .ConfigureAwait(false);
            if (!press.Success)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "town_return_press_failed",
                    press.Error ?? "Town return key press failed.");
            }

            var revivePoints = pathPoints
                .Select(point => point.ToVector3())
                .ToArray();
            state.NoKillRecovery.StartTownReturn(startPosition, revivePathName, revivePoints, now);
            context.Logger.Warn("stationary_combat.no_kill.return.press", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = key,
                ["timeoutMs"] = (long)ReadNoKillTimeout().TotalMilliseconds,
                ["lastKillAt"] = runtime?.LastKillAt,
                ["watchStartedAt"] = state.NoKillRecovery.WatchStartedAt,
                ["revivePathName"] = revivePathName,
                ["startX"] = startPosition.X,
                ["startY"] = startPosition.Y,
                ["startZ"] = startPosition.Z
            });
            return IdleDelay;
        }

        if (state.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle)
        {
            if (now - state.NoKillRecovery.StepStartedAt < ReadNoKillTownReturnSettleDelay())
            {
                return IdleDelay;
            }

            if (state.NoKillRecovery.TownReturnStartPosition is not { } startPosition)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "town_return_start_position_missing",
                    "Town return start position was not recorded.");
            }

            var afterPlayer = await ReadPlayerAsync(context).ConfigureAwait(false);
            var endPosition = afterPlayer.Position!.Value;

            var distance = StationaryCombatTargetSelector.HorizontalDistance(startPosition, endPosition);
            if (distance < ReadNoKillTownReturnMinDistance())
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "town_return_position_unchanged",
                    "Town return did not move the character enough. distance=" +
                    distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            var revivePoints = state.NoKillRecovery.RevivePathPoints;
            if (revivePoints.Count < 2)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "revive_path_unavailable",
                    "Revive path points were not retained after town return.");
            }

            var revivePathName = state.NoKillRecovery.RevivePathName;
            state.SetStationaryHomeFromRevivePath(revivePathName, revivePoints[^1], revivePoints.Count);
            state.StartStartupRecovery(revivePathName, revivePoints, 0);
            state.NoKillRecovery.StartRevivePath(now);
            context.Logger.Info("stationary_combat.no_kill.return.verify.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["distance"] = Math.Round(distance, 2),
                ["revivePathName"] = revivePathName,
                ["startPointIndex"] = 0,
                ["pathPointCount"] = revivePoints.Count
            });
            player = afterPlayer;
        }

        if (state.NoKillRecovery.Step != StationaryCombatNoKillRecoveryStep.FollowRevivePath ||
            state.NoKillRecovery.RevivePathPoints.Count < 2)
        {
            return IdleDelay;
        }

        var playerPosition = player.Position!.Value;
        var home = state.NoKillRecovery.RevivePathPoints[^1];
        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var radius = Math.Max(1.0D, combat.StationaryCombatRadius);
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        var recoveryDelay = await ContinueStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false);
        if (recoveryDelay is not null || state.StartupRecoveryActive)
        {
            return recoveryDelay ?? MoveTickDelay;
        }

        var completedPathName = state.NoKillRecovery.RevivePathName;
        state.NoKillRecovery.Complete(DateTimeOffset.Now);
        context.Logger.Info("stationary_combat.no_kill.recovery.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["revivePathName"] = completedPathName,
            ["nextTimeoutMs"] = (long)ReadNoKillTimeout().TotalMilliseconds
        });
        return IdleDelay;
    }

    private static TimeSpan PostponeNoKillRecovery(
        AccountWorkerContext context,
        StationaryCombatState state,
        DateTimeOffset now,
        string reason,
        string error)
    {
        var retryDelay = ReadNoKillRetryDelay();
        state.NoKillRecovery.Postpone(now, retryDelay);
        context.Logger.Warn("stationary_combat.no_kill.recovery.postponed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["retryDelayMs"] = (long)retryDelay.TotalMilliseconds
        });
        return IdleDelay;
    }

    private async Task<bool> TryPostponePostCombatMaintenanceForDefenseTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        var playerPosition = player.Position!.Value;

        var selection = await SelectRecoveryDefenseTargetAsync(
                context,
                state,
                playerPosition,
                allowRevivePathClear: IsDeathRecoveryRevivePathActive(state))
            .ConfigureAwait(false);
        if (selection.HoldForSmartPreAimHandoff)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            return true;
        }

        var target = selection.Target;
        var isRevivePathClearTarget = selection.IsRevivePathClearTarget;
        if (target?.Position is null)
        {
            return false;
        }

        var isMaintenanceDefenseTarget = IsMaintenanceDefenseTarget(target, state);

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        if (_bagCleanup is not null && state.BagCleanup.DiscardActive)
        {
            await _bagCleanup
                .AbortDiscardForAttackAsync(context, state.BagCleanup, target.Name)
                .ConfigureAwait(false);
        }

        FinishLootAfterKill(
            context,
            state,
            "defense_target_before_post_combat_maintenance",
            success: true);
        state.Fighting = true;
        state.SetCurrentTarget(target);
        state.CurrentTargetIsMaintenanceDefense = isMaintenanceDefenseTarget;
        state.CurrentTargetIsRevivePathClear = isRevivePathClearTarget;
        state.CurrentTargetBypassesHomeLeash = isRevivePathClearTarget || isMaintenanceDefenseTarget;
        state.MarkCandidate(target, DateTimeOffset.Now);
        state.FacedCandidateEntityId = 0;
        state.ClearPendingTabVerification();
        if (selection.IsSmartPreAimHandoffTarget)
        {
            CompleteSmartPreAimHandoff(
                context,
                state,
                target.EntityId,
                target.ServerObjectId);
        }

        context.Logger.Info("stationary_combat.loot.post_combat_maintenance_postponed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.EntityId,
            ["serverObjectId"] = target.ServerObjectId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["targetingServerObjectId"] = target.TargetServerObjectId,
            ["targetingMe"] = IsTargetingLocalSide(target, state),
            ["revivePathClear"] = isRevivePathClearTarget,
            ["smartPreAimHandoff"] = selection.IsSmartPreAimHandoffTarget
        });
        return true;
    }

    private async Task<bool> TryInterruptMaintenanceRestForDefenseAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        string reason)
    {
        if (!semiAutoState.IsMaintenanceResting)
        {
            return true;
        }

        if (player.HasRestState && !player.IsResting)
        {
            semiAutoState.ClearMaintenanceRest();
            context.Logger.Info("stationary_combat.maintenance_rest.defense_clear_without_x", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = reason,
                ["stanceFlags"] = player.StanceFlags,
                ["stanceLow"] = player.StanceLowNibble,
                ["motionMode"] = player.MotionMode
            });
            return true;
        }

        var canceled = await _semiAuto
            .CancelMaintenanceRestAsync(context, semiAutoState, reason)
            .ConfigureAwait(false);
        if (!canceled)
        {
            return false;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return true;
    }

    private static bool IsSameLootTarget(
        StationaryCombatLootAfterKillState loot,
        LockedTargetSnapshot locked)
    {
        if (loot.KilledTargetServerObjectId != 0 &&
            locked.ServerObjectId != 0 &&
            loot.KilledTargetServerObjectId == locked.ServerObjectId)
        {
            return true;
        }

        if (loot.KilledTargetEntityId != 0 &&
            locked.TargetEntityId != 0 &&
            loot.KilledTargetEntityId == locked.TargetEntityId)
        {
            return true;
        }

        return string.Equals(loot.KilledTargetName, locked.Name, StringComparison.Ordinal);
    }

    private static bool IsSameLootTarget(
        StationaryCombatLootAfterKillState loot,
        LootCorpseSnapshot corpse)
    {
        if (loot.KilledTargetServerObjectId != 0 &&
            corpse.ServerObjectId != 0 &&
            loot.KilledTargetServerObjectId == corpse.ServerObjectId)
        {
            return true;
        }

        if (loot.KilledTargetEntityId != 0 &&
            corpse.EntityId != 0 &&
            loot.KilledTargetEntityId == corpse.EntityId)
        {
            return true;
        }

        return string.Equals(loot.KilledTargetName, corpse.Name, StringComparison.Ordinal);
    }

    private static bool IsLockedTargetEmpty(LockedTargetSnapshot lockedResult)
    {
        return !lockedResult.HasTarget;
    }

    private static bool ShouldStartLootAfterKill(
        AccountWorkerContext context,
        LockedTargetSnapshot target)
    {
        return (context.Config.ScriptSettings?.Combat?.EnableLoot ?? true) &&
               target.IsLockedMonster &&
               !target.IsAlive;
    }

    private static void MarkStationaryKillIfNeeded(
        AccountWorkerContext context,
        LockedTargetSnapshot target,
        string source)
    {
        if (!target.IsLockedMonster || target.IsAlive)
        {
            return;
        }

        var counted = context.RuntimeStates.MarkKill(
            context.Config.AccountName,
            target.TargetEntityId,
            target.ServerObjectId,
            target.CapturedAt);
        if (!counted)
        {
            return;
        }

        context.Logger.Info("stationary_combat.target.kill_counted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["source"] = source,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name
        });
    }

    private static void FinishLootAfterKill(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        bool success)
    {
        var finishedAt = DateTimeOffset.Now;
        var lootKeyPressed = state.LootAfterKill.LootKeyPressed;
        context.Logger.Info("stationary_combat.loot.finished", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["success"] = success,
            ["reason"] = reason,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["targetName"] = state.LootAfterKill.KilledTargetName
        });
        StopNextTargetPreAim(context, state, "loot_after_kill_finished", clearCandidate: false);
        state.MarkLootAfterKillFinished(finishedAt, lootKeyPressed);
        state.ClearLootAfterKill();
        state.ClearTarget();
    }

    private async Task<TimeSpan> ReacquireCurrentFightTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome,
        LockedTargetSnapshot lockedResult,
        string reason)
    {
        var targetEntityId = state.CurrentTargetEntityId != 0
            ? state.CurrentTargetEntityId
            : state.CandidateEntityId;
        var targetServerObjectId = state.CurrentTargetServerObjectId != 0
            ? state.CurrentTargetServerObjectId
            : state.CandidateServerObjectId;
        if (targetEntityId == 0 && targetServerObjectId == 0)
        {
            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var target = objects.FirstOrDefault(candidate =>
            StationaryCombatState.IsSameTarget(
                targetEntityId,
                targetServerObjectId,
                candidate.EntityId,
                candidate.ServerObjectId));
        if (state.IsTargetIgnored(targetEntityId, targetServerObjectId) ||
            state.IsTargetTemporarilyExcluded(targetEntityId, targetServerObjectId, DateTimeOffset.Now))
        {
            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        if (target is null)
        {
            var missingDelay = await TryClearMissingCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    targetEntityId,
                    targetServerObjectId,
                    playerDistanceFromHome,
                    radius,
                    lockedResult,
                    reason)
                .ConfigureAwait(false);
            if (missingDelay is not null)
            {
                return missingDelay.Value;
            }

            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    targetEntityId,
                    targetServerObjectId,
                    string.Empty,
                    playerDistanceFromHome,
                    radius,
                    lockedResult,
                    reason + "_target_missing")
                .ConfigureAwait(false);
        }

        if (!target.IsAlive)
        {
            var killedSnapshot = new LockedTargetSnapshot(
                target.EntityId,
                target.ServerObjectId,
                NpcEntityType,
                LockedTargetSnapshot.MonsterObjectType,
                target.Name,
                target.CurrentHp,
                target.MaxHp,
                target.Position,
                target.DistanceToLocalPlayer,
                DateTimeOffset.Now,
                target.TargetServerObjectId,
                target.IsTargetingLocalPlayer);
            MarkStationaryKillIfNeeded(context, killedSnapshot, "world_object_reacquire");
            if (ShouldStartLootAfterKill(context, killedSnapshot))
            {
                state.StartLootAfterKill(killedSnapshot, DateTimeOffset.Now);
                context.Logger.Info("stationary_combat.loot.started", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = target.EntityId,
                    ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name,
                    ["source"] = "world_object_reacquire"
                });
                return IdleDelay;
            }

            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        if (IsTargetingLocalSide(target, state) ||
            state.IsTeamLeaderProtectionTarget(target))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
        }

        if (!state.CurrentTargetIsMaintenanceDefense &&
            !state.CurrentTargetIsRevivePathClear &&
            !AllowsClaimedTargets(context) &&
            IsClaimedByOther(target, state) &&
            !TryKeepClaimedTargetForVerifiedLeaderTacticalMark(
                context,
                state,
                target.EntityId,
                target.ServerObjectId,
                target.Name,
                target.TargetServerObjectId))
        {
            return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.EntityId,
                    target.ServerObjectId,
                    target.Name,
                    "target_owned_by_other",
                    target.TargetServerObjectId)
                .ConfigureAwait(false);
        }

        if (!IsCurrentFightTargetStillSelectable(target, home, radius, state.CurrentTargetIsMaintenanceDefense, state))
        {
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    targetEntityId,
                    targetServerObjectId,
                    target.Name,
                    playerDistanceFromHome,
                    radius,
                    lockedResult,
                    reason + "_target_not_selectable")
                .ConfigureAwait(false);
        }

        state.SetCurrentTarget(target);
        state.MarkCandidate(target, DateTimeOffset.Now);
        semiAutoState.ResetAttackKeyPressThrottle();
        LogActionThrottled(context, state, "stationary_combat.target.reacquire", reason + ":" + TargetActionKey(target.EntityId, target.ServerObjectId), new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["targetEntityId"] = target.EntityId,
            ["serverObjectId"] = target.ServerObjectId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetingServerObjectId"] = target.TargetServerObjectId,
            ["targetName"] = target.Name,
            ["lockedReadSuccess"] = true,
            ["lockedEntityId"] = lockedResult.TargetEntityId,
            ["lockedServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedResult.TargetServerObjectId,
            ["lockedLocalServerObjectId"] = lockedResult.LocalServerObjectId,
            ["lockedTargetingMe"] = IsTargetingLocalPlayerByServerObjectId(lockedResult),
            ["lockedName"] = lockedResult.Name,
            ["lockedAlive"] = lockedResult.IsMonsterAlive,
            ["lockedHp"] = lockedResult.CurrentHp,
            ["error"] = null
        }, TimeSpan.FromMilliseconds(500));

        if (state.CurrentTargetIsMaintenanceDefense &&
            target.Position is { } targetPosition)
        {
            var playerPosition = player.Position!.Value;
            var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
            if (state.FacedCandidateEntityId != target.EntityId)
            {
                var isFacingTarget = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
                if (!isFacingTarget)
                {
                    semiAutoState.ResetAttackKeyPressThrottle();
                    return MoveTickDelay;
                }

                state.FacedCandidateEntityId = target.EntityId;
            }

            var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
            var radarSettings = _obstacleNavigator?.ResolveSettings(
                                    context.Config.AccountName,
                                    combat.RadarObstacleAvoidance)
                                ?? new RadarObstacleScriptSettings();
            var navigation = await ResolveObstacleNavigationAsync(
                    context,
                    state,
                    playerPosition,
                    targetPosition,
                    RadarNavigationPurpose.ApproachTarget,
                    target.ServerObjectId,
                    radarSettings,
                    AcquireDistance)
                .ConfigureAwait(false);
            if (navigation?.Action == RadarNavigationAction.Unreachable)
            {
                return await TemporarilyExcludeRadarUnreachableTargetAsync(
                        context,
                        semiAutoState,
                        state,
                        target,
                        navigation)
                    .ConfigureAwait(false);
            }

            var requiresMovement = navigation?.Action == RadarNavigationAction.MoveToWaypoint ||
                                   navigation?.Action == RadarNavigationAction.Direct &&
                                   playerDistanceToTarget > AcquireDistance ||
                                   navigation is null && playerDistanceToTarget > AcquireDistance;
            if (requiresMovement)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                var destination = navigation is null
                    ? targetPosition
                    : ToVector3(navigation.Destination, playerPosition.Z);
                var reachDistance = navigation?.ReachDistanceMeters ?? AcquireDistance;
                await PathFollowStepAsync(context, state, player, destination, reachDistance).ConfigureAwait(false);
                if (navigation is not null)
                {
                    LogRadarNavigation(context, state, navigation, "reacquire");
                }
                await TryJumpCombatApproachIfStuckAsync(
                        context,
                        state,
                        target,
                        playerPosition,
                        playerDistanceToTarget,
                        "reacquire")
                    .ConfigureAwait(false);
                return MoveTickDelay;
            }
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target, home, radius).ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryClearMissingCurrentFightTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId,
        double playerDistanceFromHome,
        double radius,
        LockedTargetSnapshot lockedResult,
        string reason)
    {
        if (!IsLockedTargetEmpty(lockedResult))
        {
            return null;
        }

        context.Logger.Info("stationary_combat.target.lost", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason + "_target_missing",
            ["targetEntityId"] = targetEntityId,
            ["serverObjectId"] = targetServerObjectId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["lockedReadSuccess"] = true,
            ["lockedEntityId"] = lockedResult.TargetEntityId,
            ["lockedServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedResult.TargetServerObjectId,
            ["error"] = null
        });

        state.ClearTarget();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
    }

    private async Task<TimeSpan> WaitForCurrentFightTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        double playerDistanceFromHome,
        double radius,
        LockedTargetSnapshot lockedResult,
        string reason)
    {
        state.Fighting = true;
        state.SetCurrentTarget(targetEntityId, targetServerObjectId);
        state.MarkCandidate(targetEntityId, targetServerObjectId, DateTimeOffset.Now);
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        LogActionThrottled(context, state, "stationary_combat.target.reacquire_wait", reason + ":" + TargetActionKey(targetEntityId, targetServerObjectId), new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["targetEntityId"] = targetEntityId,
            ["serverObjectId"] = targetServerObjectId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["targetName"] = targetName,
            ["lockedReadSuccess"] = true,
            ["lockedEntityId"] = lockedResult.TargetEntityId,
            ["lockedServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedResult.TargetServerObjectId,
            ["lockedName"] = lockedResult.Name,
            ["lockedAlive"] = lockedResult.IsMonsterAlive,
            ["lockedHp"] = lockedResult.CurrentHp,
            ["error"] = null
        }, TimeSpan.FromMilliseconds(500));
        return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
    }

    private async Task<TimeSpan> TickAcquireAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        Vector3Snapshot home,
        double radius)
    {
        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (state.IsPendingTabCandidate(target))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    home,
                    radius)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                allowLockedFallback: false,
                phase: "pre_tab")
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        var now = DateTimeOffset.Now;
        if (now - state.LastTabAt >= TabInterval)
        {
            LogActionThrottled(context, state, "stationary_combat.target.verify_failed", "verify:" + target.EntityId, new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["candidateEntityId"] = target.EntityId,
                ["candidateName"] = target.Name,
                ["lockedReadSuccess"] = true,
                ["lockedEntityId"] = lockedResult.TargetEntityId,
                ["lockedName"] = lockedResult.Name,
                ["lockedAlive"] = lockedResult.IsMonsterAlive,
                ["lockedHp"] = lockedResult.CurrentHp,
                ["error"] = null
            }, TimeSpan.FromMilliseconds(500));

            return await PressTabAndVerifyAsync(context, plan, semiAutoState, state, target, lockedResult, home, radius).ConfigureAwait(false);
        }

        return MoveTickDelay;
    }

    private async Task<TimeSpan> TickPendingTabVerificationAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        Vector3Snapshot home,
        double radius)
    {
        var acquiredDelay = await VerifyPendingTabTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                delayMs: 0)
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        var now = DateTimeOffset.Now;
        if (!state.IsPendingTabVerifyExpired(now))
        {
            return MoveTickDelay;
        }

        if (now - state.LastTabAt >= TabInterval)
        {
            return await PressTabAndVerifyAsync(context, plan, semiAutoState, state, target, lockedResult, home, radius).ConfigureAwait(false);
        }

        return MoveTickDelay;
    }

    private async Task<TimeSpan> PressTabAndVerifyAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedBeforeResult,
        Vector3Snapshot home,
        double radius)
    {
        if (!await PrepareOrdinaryStationaryFightReacquireTabAsync(context, state, target).ConfigureAwait(false))
        {
            return MoveTickDelay;
        }

        var now = DateTimeOffset.Now;
        state.LastTabAt = now;
        var verifyWindowMs = ReadTabVerifyWindowMs();

        var tabResult = await _input
            .PressKeyAsync("Tab", TimeSpan.FromMilliseconds(25), context.StopToken)
            .ConfigureAwait(false);
        if (!tabResult.Success)
        {
            context.Logger.Warn("stationary_combat.tab.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.EntityId,
                ["error"] = tabResult.Error
            });
            return MoveTickDelay;
        }

        state.StartPendingTabVerification(
            target,
            DateTimeOffset.Now + TimeSpan.FromMilliseconds(verifyWindowMs),
            lockedBeforeResult);
        context.Logger.Info("stationary_combat.tab.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
            ["candidateName"] = target.Name,
            ["previousLockedEntityId"] = lockedBeforeResult.TargetEntityId,
            ["previousLockedServerObjectId"] = lockedBeforeResult.ServerObjectId,
            ["previousLockedTargetServerObjectId"] = lockedBeforeResult.ServerObjectId,
            ["previousLockedTargetingServerObjectId"] = lockedBeforeResult.TargetServerObjectId,
            ["verifyWindowMs"] = verifyWindowMs
        });

        return await PollTabVerifyAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                home,
                radius)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> PollTabVerifyAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        Vector3Snapshot home,
        double radius)
    {
        var verifyDelayMs = ReadTabVerifyDelayMs();
        var pollMs = ReadTabVerifyPollMs();
        if (verifyDelayMs <= 0)
        {
            var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            var acquiredDelay = await VerifyPendingTabTargetAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    home,
                    radius,
                    delayMs: 0)
                .ConfigureAwait(false);
            return acquiredDelay ?? MoveTickDelay;
        }

        var elapsedMs = 0;
        while (elapsedMs < verifyDelayMs)
        {
            var waitMs = Math.Min(pollMs, verifyDelayMs - elapsedMs);
            await DelayAsync(TimeSpan.FromMilliseconds(waitMs), context).ConfigureAwait(false);
            elapsedMs += waitMs;

            var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            var acquiredDelay = await VerifyPendingTabTargetAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult,
                    home,
                    radius,
                    elapsedMs)
                .ConfigureAwait(false);
            if (acquiredDelay is not null)
            {
                return acquiredDelay.Value;
            }
        }

        return MoveTickDelay;
    }

    private async Task<TimeSpan?> VerifyPendingTabTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        Vector3Snapshot home,
        double radius,
        int delayMs)
    {
        LogTabVerify(context, state, target, lockedResult, delayMs);
        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                allowLockedFallback: false,
                phase: "after_tab")
            .ConfigureAwait(false);
        if (acquiredDelay is not null)
        {
            return acquiredDelay.Value;
        }

        var nearbyAggressiveDelay = await TryAcceptNearbyAggressiveLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                delayMs)
            .ConfigureAwait(false);
        if (nearbyAggressiveDelay is not null)
        {
            return nearbyAggressiveDelay.Value;
        }

        await PressForwardIfTabLockMissAsync(
                context,
                state,
                target,
                lockedResult,
                delayMs)
            .ConfigureAwait(false);
        return null;
    }

    private async Task<TimeSpan?> TryAcceptNearbyAggressiveLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        Vector3Snapshot home,
        double radius,
        int delayMs)
    {
        if (lockedResult is not { IsMonsterAlive: true } lockedTarget ||
            StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId))
        {
            return null;
        }

        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var lockedWorldTarget = objects.FirstOrDefault(candidate =>
            StationaryCombatState.IsSameTarget(
                candidate.EntityId,
                candidate.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId));
        if (lockedWorldTarget is null)
        {
            return null;
        }

        var protectedSideDelay = await TryAcceptSmartPreAimProtectedSideLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedTarget,
                lockedWorldTarget,
                home,
                radius)
            .ConfigureAwait(false);
        if (protectedSideDelay is not null)
        {
            return protectedSideDelay.Value;
        }

        if (state.IsSmartPreAimHandoffTarget(target.EntityId, target.ServerObjectId) &&
            !IsTargetingLocalSide(lockedWorldTarget, state) &&
            !state.IsTeamLeaderProtectionTarget(lockedWorldTarget))
        {
            LogActionThrottled(
                context,
                state,
                "stationary_combat.smart_preaim.handoff_wrong_lock_rejected",
                "locked:" + TargetActionKey(lockedWorldTarget.EntityId, lockedWorldTarget.ServerObjectId),
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["candidateEntityId"] = target.EntityId,
                    ["candidateServerObjectId"] = target.ServerObjectId,
                    ["lockedEntityId"] = lockedWorldTarget.EntityId,
                    ["lockedServerObjectId"] = lockedWorldTarget.ServerObjectId,
                    ["lockedTargetingServerObjectId"] = lockedWorldTarget.TargetServerObjectId
                },
                TimeSpan.FromMilliseconds(500));
            return null;
        }

        var refreshedCandidate = objects.FirstOrDefault(candidate =>
            StationaryCombatState.IsSameTarget(
                candidate.EntityId,
                candidate.ServerObjectId,
                target.EntityId,
                target.ServerObjectId)) ?? target;
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        if (state.IsTargetIgnored(lockedWorldTarget) ||
            state.IsTargetTemporarilyExcluded(lockedWorldTarget, DateTimeOffset.Now) ||
            IsActiveMonsterFiltered(lockedWorldTarget, activeMonsterNameFilters) ||
            !StationaryCombatTargetSelector.IsSelectableMonster(lockedWorldTarget) ||
            !IsCandidateStillSelectable(
                lockedWorldTarget,
                home,
                radius,
                allowClaimedByOther: AllowsClaimedTargets(context),
                state) ||
            (!lockedWorldTarget.IsAggressiveToPlayer && !IsTargetingLocalSide(lockedWorldTarget, state)) ||
            !TryGetDistanceToLocalPlayer(refreshedCandidate, out var candidateDistance) ||
            !TryGetDistanceToLocalPlayer(lockedWorldTarget, out var lockedDistance) ||
            lockedDistance >= candidateDistance)
        {
            return null;
        }

        context.Logger.Info("stationary_combat.target.accept_nearby_aggressive_lock", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateName"] = target.Name,
            ["candidateDistance"] = Math.Round(candidateDistance, 2),
            ["lockedEntityId"] = lockedTarget.TargetEntityId,
            ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
            ["lockedName"] = lockedTarget.Name,
            ["lockedDistance"] = Math.Round(lockedDistance, 2),
            ["lockedAggressiveKnown"] = lockedWorldTarget.AggressiveKnown,
            ["lockedAggressiveToPlayer"] = lockedWorldTarget.IsAggressiveToPlayer,
            ["lockedTargetingMe"] = IsTargetingLocalSide(lockedWorldTarget, state),
            ["delayMs"] = delayMs
        });

        return await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                home,
                radius,
                allowLockedFallback: true,
                phase: "after_tab_aggressive")
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryAcceptSmartPreAimProtectedSideLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot candidate,
        LockedTargetSnapshot lockedTarget,
        WorldObjectSnapshot lockedWorldTarget,
        Vector3Snapshot home,
        double radius)
    {
        if (!state.IsSmartPreAimHandoffTarget(candidate.EntityId, candidate.ServerObjectId) ||
            lockedTarget.ServerObjectId == 0 ||
            lockedWorldTarget.ServerObjectId == 0 ||
            !lockedTarget.HasKnownHealth ||
            !lockedWorldTarget.HasKnownHealth ||
            lockedWorldTarget.Position is null ||
            !StationaryCombatTargetSelector.IsSelectableMonster(lockedWorldTarget) ||
            state.IsTargetTemporarilyExcluded(lockedWorldTarget, DateTimeOffset.Now))
        {
            return null;
        }

        var targetingLocalSide = IsTargetingLocalSide(lockedTarget, state) ||
                                 IsTargetingLocalSide(lockedWorldTarget, state);
        var targetingRecordedTeamProtection = state.IsTeamLeaderProtectionTarget(lockedWorldTarget);
        var targetingTeamSide = false;
        if (!targetingLocalSide && !targetingRecordedTeamProtection && lockedTarget.TargetServerObjectId != 0)
        {
            var teamSideServerObjectIds = await ResolveSmartPreAimTeamSideServerObjectIdsAsync(
                    context,
                    state,
                    DateTimeOffset.Now)
                .ConfigureAwait(false);
            targetingTeamSide = teamSideServerObjectIds.Contains(lockedTarget.TargetServerObjectId);
        }

        if (!targetingLocalSide && !targetingTeamSide && !targetingRecordedTeamProtection)
        {
            return null;
        }

        if (targetingTeamSide)
        {
            state.MarkTeamLeaderProtectionTarget(lockedWorldTarget);
        }

        context.Logger.Info("stationary_combat.smart_preaim.handoff_defense_lock_accepted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = candidate.EntityId,
            ["candidateServerObjectId"] = candidate.ServerObjectId,
            ["lockedEntityId"] = lockedWorldTarget.EntityId,
            ["lockedServerObjectId"] = lockedWorldTarget.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
            ["worldTargetingServerObjectId"] = lockedWorldTarget.TargetServerObjectId,
            ["targetingLocalSide"] = targetingLocalSide,
            ["targetingTeamSide"] = targetingTeamSide,
            ["recordedTeamProtection"] = targetingRecordedTeamProtection
        });

        return await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                lockedWorldTarget,
                lockedTarget,
                home,
                radius,
                allowLockedFallback: false,
                phase: "after_tab_smart_preaim_defense")
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickStartupTownReturnAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        double radius)
    {
        if (DateTimeOffset.Now - state.StartupTownReturnStartedAt < ReadStartupTownReturnSettleDelay())
        {
            return IdleDelay;
        }

        var startPosition = state.StartupTownReturnStartPosition;
        var revivePathName = state.StartupRecoveryPathName;
        var revivePoints = state.StartupRecoveryPoints;
        state.CompleteStartupTownReturn();

        if (startPosition is null || revivePoints.Count < 2)
        {
            state.ClearStartupRecovery();
            context.Logger.Warn("stationary_combat.startup_recovery.return.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = startPosition is null ? "start_position_missing" : "revive_path_unavailable",
                ["pathName"] = revivePathName,
                ["pathPointCount"] = revivePoints.Count
            });
            return IdleDelay;
        }

        var movedDistance = StationaryCombatTargetSelector.HorizontalDistance(
            startPosition.Value,
            playerPosition);
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            revivePoints[^1]);
        var nearestReturnedPointIndex = FindNearestPathPointIndex(
            playerPosition,
            revivePoints,
            double.MaxValue);
        var nearestReturnedPointDistance = nearestReturnedPointIndex >= 0
            ? StationaryCombatTargetSelector.HorizontalDistance(
                playerPosition,
                revivePoints[nearestReturnedPointIndex])
            : double.MaxValue;
        var minimumMovedDistance = ReadStartupTownReturnMinDistance();
        var startupTownReturnDistance = ReadStartupTownReturnDistance();
        var returnFailureReason = movedDistance < minimumMovedDistance
            ? "town_return_position_unchanged"
            : nearestReturnedPointDistance > startupTownReturnDistance
                ? "town_return_destination_still_distant"
                : string.Empty;
        if (!string.IsNullOrEmpty(returnFailureReason))
        {
            context.Logger.Warn("stationary_combat.startup_recovery.return.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = returnFailureReason,
                ["pathName"] = revivePathName,
                ["movedDistance"] = Math.Round(movedDistance, 2),
                ["minimumMovedDistance"] = Math.Round(minimumMovedDistance, 2),
                ["nearestPathPointDistance"] = nearestReturnedPointIndex >= 0
                    ? Math.Round(nearestReturnedPointDistance, 2)
                    : null,
                ["distanceThreshold"] = Math.Round(startupTownReturnDistance, 2)
            });

            if (!TryStartStartupRecoveryFromNearestPoint(
                    context,
                    state,
                    playerPosition,
                    revivePathName,
                    revivePoints,
                    playerDistanceFromHome))
            {
                return IdleDelay;
            }
        }
        else
        {
            state.StartStartupRecovery(revivePathName, revivePoints, 0);
            state.ReturningHome = false;
            state.ClearTarget();
            context.Logger.Info("stationary_combat.startup_recovery.return.verify.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["movedDistance"] = Math.Round(movedDistance, 2),
                ["nearestPathPointDistance"] = Math.Round(nearestReturnedPointDistance, 2),
                ["startPointIndex"] = 0,
                ["pathPointCount"] = revivePoints.Count
            });
            LogStartupRecoverySelected(
                context,
                revivePathName,
                revivePoints,
                0,
                StationaryCombatTargetSelector.HorizontalDistance(playerPosition, revivePoints[0]),
                playerDistanceFromHome,
                selectionReason: "town_return_verified");
        }

        return await ContinueStartupRecoveryAsync(
                context,
                plan,
                semiAutoState,
                state,
                player,
                playerPosition,
                revivePoints[^1],
                radius,
                playerDistanceFromHome)
            .ConfigureAwait(false) ?? IdleDelay;
    }

    private static bool TryStartStartupRecoveryFromNearestPoint(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        string revivePathName,
        IReadOnlyList<Vector3Snapshot> revivePoints,
        double playerDistanceFromHome)
    {
        var nearestPointIndex = FindNearestPathPointIndex(
            playerPosition,
            revivePoints,
            playerDistanceFromHome);
        if (nearestPointIndex < 0)
        {
            context.Logger.Info("stationary_combat.startup_recovery.home_nearest", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
                ["pathPointCount"] = revivePoints.Count
            });
            return false;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            revivePoints[nearestPointIndex]);
        state.StartStartupRecovery(revivePathName, revivePoints, nearestPointIndex);
        state.ReturningHome = false;
        state.ClearTarget();
        LogStartupRecoverySelected(
            context,
            revivePathName,
            revivePoints,
            nearestPointIndex,
            nearestDistance,
            playerDistanceFromHome,
            selectionReason: "nearest_path_point");
        return true;
    }

    private static void LogStartupRecoverySelected(
        AccountWorkerContext context,
        string revivePathName,
        IReadOnlyList<Vector3Snapshot> revivePoints,
        int pointIndex,
        double pointDistance,
        double playerDistanceFromHome,
        string selectionReason)
    {
        context.Logger.Info("stationary_combat.startup_recovery.selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = revivePathName,
            ["startPointIndex"] = pointIndex,
            ["startPointNumber"] = pointIndex + 1,
            ["pathPointCount"] = revivePoints.Count,
            ["pathPointDistance"] = Math.Round(pointDistance, 2),
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
            ["selectionReason"] = selectionReason
        });
    }

    private async Task<bool> PrepareOrdinaryStationaryFightReacquireTabAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
        if (!state.Fighting ||
            context.Config.ScriptSettings?.CombatMode != AccountCombatMode.Stationary ||
            state.CurrentTargetIsMaintenanceDefense ||
            state.CurrentTargetIsRevivePathClear ||
            state.CurrentTargetIsGatherSafetyClear)
        {
            return true;
        }

        if (target.Position is not { } targetPosition)
        {
            LogActionThrottled(context, state, "stationary_combat.target.reacquire_face_wait", "target_position_missing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "target_position_missing",
                ["targetEntityId"] = target.EntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name
            }, TimeSpan.FromMilliseconds(500));
            return false;
        }

        var player = await ReadPlayerForActionAsync(context).ConfigureAwait(false);
        var faced = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
        if (!faced)
        {
            LogActionThrottled(context, state, "stationary_combat.target.reacquire_face_wait", "face_incomplete", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "face_incomplete",
                ["targetEntityId"] = target.EntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name
            }, TimeSpan.FromMilliseconds(500));
        }

        return faced;
    }

    private static bool TryGetDistanceToLocalPlayer(WorldObjectSnapshot target, out double distance)
    {
        if (target.DistanceToLocalPlayer is { } value &&
            !double.IsNaN(value) &&
            !double.IsInfinity(value))
        {
            distance = Math.Max(0.0D, value);
            return true;
        }

        distance = 0.0D;
        return false;
    }

    private async Task PressForwardIfTabLockMissAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        int delayMs)
    {
        if (lockedResult is { IsMonsterAlive: true })
        {
            return;
        }

        if (lockedResult is { IsLockedMonster: true, HasKnownHealth: true, CurrentHp: 0 } lockedTarget)
        {
            var reason = ResolveTabCorpseNudgeReason(state, lockedTarget);
            await PressForwardForTabCorpseAsync(context, state, target, lockedTarget, delayMs, reason)
                .ConfigureAwait(false);
            return;
        }

        if (lockedResult is { HasTarget: true })
        {
            return;
        }

        await PressForwardForTabLockMissAsync(
                context,
                state,
                target,
                delayMs)
            .ConfigureAwait(false);
    }

    private static string ResolveTabCorpseNudgeReason(
        StationaryCombatState state,
        LockedTargetSnapshot lockedTarget)
    {
        if (!lockedTarget.HasLoot)
        {
            return "not_lootable";
        }

        return state.HasAttemptedLootCorpse(
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId,
                DateTimeOffset.Now,
                TimeSpan.FromMilliseconds(ReadLootAttemptCacheTtlMs()))
            ? "already_attempted"
            : "lootable";
    }

    private async Task PressForwardForTabCorpseAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedTarget,
        int delayMs,
        string reason)
    {
        if (!state.TryMarkPendingTabCorpseNudged())
        {
            return;
        }

        var holdMs = ReadTabCorpseNudgeKeyHoldMs();
        var result = await _input
            .PressKeyAsync("W", TimeSpan.FromMilliseconds(holdMs), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.tab.corpse_nudge_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["candidateEntityId"] = target.EntityId,
                ["candidateName"] = target.Name,
                ["lockedEntityId"] = lockedTarget.TargetEntityId,
                ["lockedName"] = lockedTarget.Name,
                ["lockedHp"] = lockedTarget.CurrentHp,
                ["reason"] = reason,
                ["delayMs"] = delayMs,
                ["holdMs"] = holdMs,
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.tab.corpse_nudge_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateName"] = target.Name,
            ["lockedEntityId"] = lockedTarget.TargetEntityId,
            ["lockedName"] = lockedTarget.Name,
            ["lockedHp"] = lockedTarget.CurrentHp,
            ["reason"] = reason,
            ["lootRaw"] = lockedTarget.LootableRaw,
            ["interactionState"] = lockedTarget.InteractionState,
            ["delayMs"] = delayMs,
            ["holdMs"] = holdMs
        });
    }

    private async Task PressForwardForTabLockMissAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        int delayMs)
    {
        if (!state.TryMarkPendingTabCorpseNudged())
        {
            return;
        }

        var holdMs = ReadTabCorpseNudgeKeyHoldMs();
        var result = await _input
            .PressKeyAsync("W", TimeSpan.FromMilliseconds(holdMs), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.tab.lock_miss_nudge_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "empty_lock",
                ["candidateEntityId"] = target.EntityId,
                ["candidateServerObjectId"] = target.ServerObjectId,
                ["candidateName"] = target.Name,
                ["lockedEntityId"] = 0,
                ["lockedServerObjectId"] = 0,
                ["lockedName"] = string.Empty,
                ["lockedHp"] = 0,
                ["delayMs"] = delayMs,
                ["holdMs"] = holdMs,
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("stationary_combat.tab.lock_miss_nudge_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = "empty_lock",
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateName"] = target.Name,
            ["lockedEntityId"] = 0,
            ["lockedServerObjectId"] = 0,
            ["lockedName"] = string.Empty,
            ["lockedHp"] = 0,
            ["lockedObjectType"] = 0,
            ["delayMs"] = delayMs,
            ["holdMs"] = holdMs
        });
    }

    private static void LogTabVerify(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        int delayMs)
    {
        context.Logger.Info("stationary_combat.tab.verify", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
            ["candidateName"] = target.Name,
            ["delayMs"] = delayMs,
            ["lockedReadSuccess"] = true,
            ["lockedEntityId"] = lockedResult.TargetEntityId,
            ["lockedServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedResult.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedResult.TargetServerObjectId,
            ["lockedName"] = lockedResult.Name,
            ["lockedAlive"] = lockedResult.IsMonsterAlive,
            ["lockedHp"] = lockedResult.CurrentHp,
            ["matched"] = lockedResult is { IsMonsterAlive: true } lockedTarget &&
                          StationaryCombatState.IsSameTarget(
                              target.EntityId,
                              target.ServerObjectId,
                              lockedTarget.TargetEntityId,
                              lockedTarget.ServerObjectId),
            ["previousLockedEntityId"] = state.PendingTabPreviousLockedEntityId,
            ["previousLockedServerObjectId"] = state.PendingTabPreviousLockedServerObjectId,
            ["previousLockedTargetServerObjectId"] = state.PendingTabPreviousLockedServerObjectId,
            ["pendingUntil"] = state.PendingTabVerifyUntil,
            ["error"] = null
        });
    }

    private async Task<TimeSpan?> TryAcquireLockedLocalSideDefenseTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot candidate,
        LockedTargetSnapshot lockedResult,
        Vector3Snapshot home,
        double radius)
    {
        if (state.Fighting ||
            state.CurrentTargetIsMaintenanceDefense ||
            state.CurrentTargetIsRevivePathClear ||
            state.CurrentTargetIsGatherSafetyClear ||
            state.CurrentTargetBypassesHomeLeash ||
            state.CurrentTargetIsTacticalMark ||
            lockedResult is not { IsMonsterAlive: true } lockedTarget ||
            StationaryCombatState.IsSameTarget(
                candidate.EntityId,
                candidate.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId) ||
            !IsTargetingLocalSide(lockedTarget, state))
        {
            return null;
        }

        return await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                candidate,
                lockedResult,
                home,
                radius,
                allowLockedFallback: true,
                phase: "pre_move_defense")
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryAcquireLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        LockedTargetSnapshot lockedResult,
        Vector3Snapshot home,
        double radius,
        bool allowLockedFallback,
        string phase)
    {
        if (lockedResult is not { IsMonsterAlive: true } lockedTarget)
        {
            return null;
        }

        var acquiredTarget = target;
        if (!StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId))
        {
            if (state.Fighting)
            {
                return null;
            }

            if (!allowLockedFallback)
            {
                return null;
            }

            var switchedTarget = await TrySwitchCandidateToLockedTargetAsync(
                    context,
                    state,
                    target,
                    lockedTarget,
                    home,
                    radius,
                    phase)
                .ConfigureAwait(false);
            if (switchedTarget is null)
            {
                return null;
            }

            acquiredTarget = switchedTarget;
        }

        var acquiredServerObjectId = acquiredTarget.ServerObjectId != 0
            ? acquiredTarget.ServerObjectId
            : lockedTarget.ServerObjectId;
        var acquiredEntityId = lockedTarget.TargetEntityId != 0
            ? lockedTarget.TargetEntityId
            : acquiredTarget.EntityId;
        if (state.HasSmartPreAimHandoff)
        {
            if (state.IsSmartPreAimHandoffTarget(acquiredEntityId, acquiredServerObjectId))
            {
                CompleteSmartPreAimHandoff(
                    context,
                    state,
                    acquiredEntityId,
                    acquiredServerObjectId);
            }
            else
            {
                ReleaseSmartPreAimHandoff(
                    context,
                    state,
                    "fight_target_override",
                    clearPreAimCandidate: true);
            }
        }

        state.Fighting = true;
        state.SetCurrentTarget(acquiredEntityId, acquiredServerObjectId);
        var isTeamLeaderProtectionTarget = state.IsTeamLeaderProtectionTarget(acquiredTarget) ||
                                           state.IsTeamLeaderProtectionTarget(lockedTarget);
        state.CurrentTargetIsMaintenanceDefense = IsTargetingLocalSide(acquiredTarget, state) ||
                                                  isTeamLeaderProtectionTarget;
        state.MarkCandidate(acquiredTarget, DateTimeOffset.Now);
        state.ClearPendingTabVerification();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        var effectiveTargetingServerObjectId = lockedTarget.TargetServerObjectId != 0
            ? lockedTarget.TargetServerObjectId
            : acquiredTarget.TargetServerObjectId;
        var effectiveTargetingMe = IsTargetingLocalSide(lockedTarget, state) ||
                                   IsTargetingLocalSide(acquiredTarget, state) ||
                                   isTeamLeaderProtectionTarget;
        var effectiveLockedTarget = lockedTarget with
        {
            ServerObjectId = lockedTarget.ServerObjectId != 0 ? lockedTarget.ServerObjectId : acquiredServerObjectId,
            TargetServerObjectId = effectiveTargetingServerObjectId,
            IsTargetingLocalPlayer = effectiveTargetingMe
        };
        if (state.JumpAssist is not null)
        {
            await state.JumpAssist
                .ObserveSoloTargetHealthAsync(
                    effectiveLockedTarget.TargetEntityId,
                    effectiveLockedTarget.ServerObjectId,
                    effectiveLockedTarget.CurrentHp)
                .ConfigureAwait(false);
        }

        state.CurrentTargetIsMaintenanceDefense = effectiveTargetingMe;
        context.Logger.Info("stationary_combat.target.acquired", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = acquiredTarget.EntityId,
            ["targetName"] = acquiredTarget.Name,
            ["phase"] = phase,
            ["targetingMe"] = effectiveTargetingMe,
            ["serverObjectId"] = acquiredServerObjectId,
            ["targetServerObjectId"] = acquiredServerObjectId,
            ["targetingServerObjectId"] = effectiveTargetingServerObjectId,
            ["localServerObjectId"] = lockedTarget.LocalServerObjectId,
            ["lockedEntityId"] = lockedTarget.TargetEntityId,
            ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
            ["aggressiveKnown"] = acquiredTarget.AggressiveKnown,
            ["aggressiveToPlayer"] = acquiredTarget.IsAggressiveToPlayer,
            ["passiveToPlayer"] = acquiredTarget.IsPassiveToPlayer,
            ["aggressiveSource"] = acquiredTarget.AggressiveSource
        });
        var claimedDelay = await TryIgnoreClaimedLockedTargetAsync(
                context,
                semiAutoState,
                state,
                effectiveLockedTarget)
            .ConfigureAwait(false);
        if (claimedDelay is not null)
        {
            return claimedDelay.Value;
        }

        var noDamageDelay = await TryIgnoreNoDamageNoTargetingLockedTargetAsync(
                context,
                semiAutoState,
                state,
                effectiveLockedTarget,
                DateTimeOffset.Now,
                phase)
            .ConfigureAwait(false);
        if (noDamageDelay is not null)
        {
            return noDamageDelay.Value;
        }

        await MaintainLeaderTacticalMarkAsync(context, state, effectiveLockedTarget).ConfigureAwait(false);
        var openingDelay = await TryWaitForLockedTargetToTargetPlayerAsync(
                context,
                plan,
                semiAutoState,
                state,
                effectiveLockedTarget,
                phase)
            .ConfigureAwait(false);
        if (openingDelay is not null)
        {
            return openingDelay.Value;
        }

        return await _semiAuto
            .TickAsync(
                context,
                plan,
                semiAutoState,
                requireCooldownCalibrationForMaintenance: true,
                jumpAssist: state.JumpAssist,
                suppressSpiritmasterPetSummon: ShouldSuppressSpiritmasterPetSummonForDeathRecoveryDefense(state))
            .ConfigureAwait(false);
    }

    private async Task<WorldObjectSnapshot?> TrySwitchCandidateToLockedTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot candidate,
        LockedTargetSnapshot lockedTarget,
        Vector3Snapshot home,
        double radius,
        string phase)
    {
        var lockedWorldTarget = await FindSelectableLockedWorldTargetAsync(
                context,
                state,
                lockedTarget,
                home,
                radius)
            .ConfigureAwait(false);
        if (lockedWorldTarget is null)
        {
            LogActionThrottled(context, state, "stationary_combat.target.locked_switch_rejected", "locked_switch:" + candidate.EntityId + ":" + lockedTarget.TargetEntityId, new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["candidateEntityId"] = candidate.EntityId,
                ["candidateServerObjectId"] = candidate.ServerObjectId,
                ["candidateTargetServerObjectId"] = candidate.ServerObjectId,
                ["candidateTargetingServerObjectId"] = candidate.TargetServerObjectId,
                ["candidateName"] = candidate.Name,
                ["lockedEntityId"] = lockedTarget.TargetEntityId,
                ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
                ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
                ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
                ["lockedName"] = lockedTarget.Name,
                ["lockedAlive"] = lockedTarget.IsMonsterAlive,
                ["lockedHp"] = lockedTarget.CurrentHp,
                ["lockedInWorldObjects"] = false
            }, TimeSpan.FromMilliseconds(500));
            return null;
        }

        context.Logger.Info("stationary_combat.target.switched_to_locked", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["phase"] = phase,
            ["candidateEntityId"] = candidate.EntityId,
            ["candidateServerObjectId"] = candidate.ServerObjectId,
            ["candidateTargetServerObjectId"] = candidate.ServerObjectId,
            ["candidateTargetingServerObjectId"] = candidate.TargetServerObjectId,
            ["candidateName"] = candidate.Name,
            ["lockedEntityId"] = lockedTarget.TargetEntityId,
            ["lockedName"] = lockedTarget.Name,
            ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
            ["lockedTargetingMe"] = IsTargetingLocalPlayerByServerObjectId(lockedTarget),
            ["worldServerObjectId"] = lockedWorldTarget.ServerObjectId,
            ["worldTargetServerObjectId"] = lockedWorldTarget.ServerObjectId,
            ["worldTargetingServerObjectId"] = lockedWorldTarget.TargetServerObjectId,
            ["worldTargetingMe"] = lockedWorldTarget.IsTargetingLocalPlayer,
            ["worldAggressiveKnown"] = lockedWorldTarget.AggressiveKnown,
            ["worldAggressiveToPlayer"] = lockedWorldTarget.IsAggressiveToPlayer,
            ["worldPassiveToPlayer"] = lockedWorldTarget.IsPassiveToPlayer,
            ["worldAggressiveSource"] = lockedWorldTarget.AggressiveSource
        });

        return lockedWorldTarget;
    }

    private async Task<WorldObjectSnapshot?> FindSelectableLockedWorldTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        LockedTargetSnapshot lockedTarget,
        Vector3Snapshot home,
        double radius)
    {
        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var lockedWorldTarget = objects.FirstOrDefault(target =>
            StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId));
        if (lockedWorldTarget is not null &&
            state.IsTargetTemporarilyExcluded(lockedWorldTarget, DateTimeOffset.Now))
        {
            return null;
        }

        return IsCandidateStillSelectable(
                lockedWorldTarget,
                home,
                radius,
                allowClaimedByOther: true,
                state: state)
            ? lockedWorldTarget
            : null;
    }

    private async Task<TimeSpan?> TryIgnoreClaimedLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        LockedTargetSnapshot target)
    {
        if (state.CurrentTargetIsMaintenanceDefense ||
            state.CurrentTargetIsRevivePathClear ||
            AllowsClaimedTargets(context) ||
            !IsClaimedByOther(target, state))
        {
            return null;
        }

        if (TryKeepClaimedTargetForVerifiedLeaderTacticalMark(
                context,
                state,
                target.TargetEntityId,
                target.ServerObjectId,
                target.Name,
                target.TargetServerObjectId))
        {
            return null;
        }

        return await IgnoreCurrentTargetAsync(
                context,
                semiAutoState,
                state,
                target.TargetEntityId,
                target.ServerObjectId,
                target.Name,
                "target_owned_by_other",
                target.TargetServerObjectId)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryIgnoreNoDamageNoTargetingLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        LockedTargetSnapshot target,
        DateTimeOffset now,
        string phase)
    {
        if (context.Config.ScriptSettings?.CombatMode == AccountCombatMode.Stationary)
        {
            state.TrackCurrentTargetStallObservation(target, now);
        }
        if (state.CurrentTargetIsMaintenanceDefense ||
            IsTargetingLocalSide(target, state))
        {
            state.ResetCurrentTargetDamageObservation();
            return null;
        }

        state.TrackCurrentTargetDamageObservation(target, now);
        if (state.CurrentTargetDamageObserved ||
            state.CurrentTargetDamageObservedAt == DateTimeOffset.MinValue)
        {
            return null;
        }

        var timeoutMs = ReadNoDamageNoTargetingTimeoutMs();
        var observedFor = now - state.CurrentTargetDamageObservedAt;
        if (observedFor.TotalMilliseconds < timeoutMs)
        {
            return null;
        }

        return await IgnoreCurrentTargetAsync(
                context,
                semiAutoState,
                state,
                target.TargetEntityId,
                target.ServerObjectId,
                target.Name,
                "no_damage_no_targeting",
                target.TargetServerObjectId,
                timeoutMs,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["currentHp"] = target.CurrentHp,
                    ["maxHp"] = target.MaxHp,
                    ["baselineHp"] = state.CurrentTargetDamageBaselineHp,
                    ["observedMs"] = (long)Math.Max(0.0D, observedFor.TotalMilliseconds)
                })
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryRecoverStalledLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        LockedTargetSnapshot target,
        DateTimeOffset now)
    {
        if (context.Config.ScriptSettings?.CombatMode != AccountCombatMode.Stationary)
        {
            state.ResetCurrentTargetStallObservation();
            return null;
        }

        state.TrackCurrentTargetStallObservation(target, now);
        var timeout = TimeSpan.FromMilliseconds(ReadFightSoftRestartTimeoutMs());
        if (state.IsCurrentTargetSoftRestartFallbackDue(now, timeout))
        {
            StopNextTargetPreAim(context, state, "soft_restart_fallback", clearCandidate: true);
            return await TemporarilyExcludeStalledTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target,
                    now,
                    timeout)
                .ConfigureAwait(false);
        }

        var wasPending = state.CurrentTargetSoftRestartPending;
        if (!state.TryStartCurrentTargetSoftRestart(now, timeout))
        {
            return null;
        }

        StopNextTargetPreAim(context, state, "soft_restart", clearCandidate: true);
        if (!wasPending)
        {
            semiAutoState.ClearChain();
            semiAutoState.ClearPressedSkillCooldownTracking();
            semiAutoState.ResetOpeningSkill();
            semiAutoState.ResetAttackKeyPressThrottle();
            context.Logger.Info("stationary_combat.target.soft_restart.started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.TargetEntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name,
                ["currentHp"] = target.CurrentHp,
                ["maxHp"] = target.MaxHp,
                ["stalledMs"] = (long)Math.Max(0.0D, (now - state.CurrentTargetStallLastProgressAt).TotalMilliseconds),
                ["timeoutMs"] = (long)timeout.TotalMilliseconds
            });
        }

        var distance = double.NaN;
        if (target.Position is not null)
        {
            distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position!.Value, target.Position.Value);
            if (!state.CurrentTargetSoftRestartFaced)
            {
                var faced = await FaceTargetStepAsync(
                        context,
                        state,
                        player,
                        target.Position.Value,
                        target.TargetEntityId,
                        target.Name)
                    .ConfigureAwait(false);
                state.MarkCurrentTargetSoftRestartFaced();
                context.Logger.Info("stationary_combat.target.soft_restart.faced", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = target.TargetEntityId,
                    ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name,
                    ["success"] = faced,
                    ["distance"] = Math.Round(distance, 2)
                });
            }

            var approachTimedOut = state.CurrentTargetSoftRestartStartedAt != DateTimeOffset.MinValue &&
                                   now - state.CurrentTargetSoftRestartStartedAt >= FightSoftRestartApproachTimeout;
            if (distance > FightSoftRestartApproachDistance && !approachTimedOut)
            {
                await PathFollowStepAsync(
                        context,
                        state,
                        player,
                        target.Position.Value,
                        FightSoftRestartApproachDistance)
                    .ConfigureAwait(false);
                return MoveTickDelay;
            }
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        state.CompleteCurrentTargetSoftRestart(target, now);
        state.ResetCurrentTargetDamageObservation();
        context.Logger.Info("stationary_combat.target.soft_restart.completed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["currentHp"] = target.CurrentHp,
            ["maxHp"] = target.MaxHp,
            ["distance"] = double.IsNaN(distance) ? null : Math.Round(distance, 2)
        });

        return await _semiAuto
            .TickAsync(
                context,
                plan,
                semiAutoState,
                requireCooldownCalibrationForMaintenance: true,
                jumpAssist: state.JumpAssist,
                suppressSpiritmasterPetSummon: ShouldSuppressSpiritmasterPetSummonForDeathRecoveryDefense(state))
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> TemporarilyExcludeStalledTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        LockedTargetSnapshot target,
        DateTimeOffset now,
        TimeSpan timeout)
    {
        var exclusion = TimeSpan.FromMilliseconds(
            ReadStalledTargetExclusionMs(context.Config.ScriptSettings?.Combat));
        var expiresAt = now + exclusion;
        state.TemporarilyExcludeTarget(
            target.TargetEntityId,
            target.ServerObjectId,
            expiresAt,
            expiresAt + TemporaryTargetSwitchGuardGrace);
        context.Logger.Info("stationary_combat.target.stall_temporarily_excluded", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["currentHp"] = target.CurrentHp,
            ["maxHp"] = target.MaxHp,
            ["stalledMs"] = (long)Math.Max(0.0D, (now - state.CurrentTargetStallLastProgressAt).TotalMilliseconds),
            ["softRestartAttempted"] = state.CurrentTargetSoftRestartAttempted,
            ["timeoutMs"] = (long)timeout.TotalMilliseconds,
            ["exclusionMs"] = (long)exclusion.TotalMilliseconds,
            ["expiresAt"] = expiresAt
        });

        state.ClearNoTargetRest();
        state.ClearTarget();
        semiAutoState.ClearChain();
        semiAutoState.ClearPressedSkillCooldownTracking();
        semiAutoState.ResetOpeningSkill();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return IdleDelay;
    }

    private async Task<TimeSpan> TemporarilyExcludeRadarUnreachableTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        RadarNavigationDecision navigation)
    {
        var now = DateTimeOffset.Now;
        var exclusion = TimeSpan.FromMilliseconds(
            ReadStalledTargetExclusionMs(context.Config.ScriptSettings?.Combat));
        var expiresAt = now + exclusion;
        state.TemporarilyExcludeTarget(
            target.EntityId,
            target.ServerObjectId,
            expiresAt,
            expiresAt + TemporaryTargetSwitchGuardGrace);
        context.Logger.Warn("stationary_combat.radar.target_unreachable", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["mapId"] = navigation.MapId,
            ["targetEntityId"] = target.EntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["reason"] = navigation.Reason,
            ["relevantObstacleCount"] = navigation.RelevantObstacleCount,
            ["exclusionMs"] = (long)exclusion.TotalMilliseconds,
            ["expiresAt"] = expiresAt
        });

        StopNextTargetPreAim(context, state, "radar_target_unreachable", clearCandidate: true);
        await StopSoloJumpAsync(state, "radar_target_unreachable").ConfigureAwait(false);
        state.ClearNoTargetRest();
        state.ClearTarget();
        semiAutoState.ClearChain();
        semiAutoState.ClearPressedSkillCooldownTracking();
        semiAutoState.ResetOpeningSkill();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return IdleDelay;
    }

    private async Task<TimeSpan> TemporarilyExcludeRadarUnreachableLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        LockedTargetSnapshot target,
        RadarNavigationDecision navigation)
    {
        var worldTarget = new WorldObjectSnapshot(
            target.TargetEntityId,
            target.ServerObjectId,
            target.Name,
            "monster",
            target.Position,
            target.DistanceToLocalPlayer,
            target.CurrentHp,
            target.MaxHp,
            target.TargetServerObjectId,
            target.IsTargetingLocalPlayer);
        return await TemporarilyExcludeRadarUnreachableTargetAsync(
                context,
                semiAutoState,
                state,
                worldTarget,
                navigation)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TryWaitForLockedTargetToTargetPlayerAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        LockedTargetSnapshot target,
        string phase)
    {
        if (state.CurrentTargetIsMaintenanceDefense ||
            IsTargetingLocalSide(target, state) ||
            IsTargetingSelf(target) ||
            (IsClaimedByOther(target, state) &&
             IsCurrentVerifiedLeaderTacticalMarkedTarget(
                 context,
                 state,
                 target.TargetEntityId,
                 target.ServerObjectId)))
        {
            semiAutoState.MarkOpeningAttackKeyAttempted(target);
            return null;
        }

        if (AllowsClaimedTargets(context) || !IsAttackKeyLoopEnabled(context))
        {
            semiAutoState.MarkOpeningAttackKeyAttempted(target);
            return null;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        LogActionThrottled(context, state, "stationary_combat.opening_attack.wait_targeting", "target:" + target.TargetEntityId, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetName"] = target.Name,
            ["phase"] = phase,
            ["serverObjectId"] = target.ServerObjectId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetingServerObjectId"] = target.TargetServerObjectId,
            ["localServerObjectId"] = target.LocalServerObjectId,
            ["targetingMe"] = IsTargetingLocalSide(target, state),
            ["targetingSelf"] = IsTargetingSelf(target)
        }, TimeSpan.FromMilliseconds(500));

        return await _semiAuto
            .TickOpeningAttackKeyLoopAsync(context, plan, semiAutoState, target)
            .ConfigureAwait(false);
    }

    private static bool TryKeepTimedOutMaintenanceDefenseTarget(
        AccountWorkerContext context,
        StationaryCombatState state,
        LockedTargetSnapshot target,
        DateTimeOffset now,
        string reason)
    {
        var targetingLocalSide = IsTargetingLocalSide(target, state);
        var teamLeaderProtection = state.IsTeamLeaderProtectionTarget(target);
        if (!state.CurrentTargetIsMaintenanceDefense &&
            !targetingLocalSide &&
            !teamLeaderProtection)
        {
            return false;
        }

        var elapsedMs = state.TargetStartedAt == DateTimeOffset.MinValue
            ? 0
            : (long)Math.Max(0.0D, (now - state.TargetStartedAt).TotalMilliseconds);

        state.Fighting = true;
        state.SetCurrentTarget(target);
        state.CurrentTargetIsMaintenanceDefense = true;
        state.RefreshCurrentTargetTimeout(now);

        context.Logger.Info("stationary_combat.target.timeout_kept_for_defense", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["serverObjectId"] = target.ServerObjectId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetingServerObjectId"] = target.TargetServerObjectId,
            ["localServerObjectId"] = state.LocalCombatSideServerObjectId,
            ["localPetServerObjectId"] = state.LocalCombatSidePetServerObjectId,
            ["targetName"] = target.Name,
            ["reason"] = reason,
            ["currentHp"] = target.CurrentHp,
            ["maxHp"] = target.MaxHp,
            ["elapsedMs"] = elapsedMs,
            ["timeoutMs"] = (long)TargetTimeout.TotalMilliseconds,
            ["targetingMe"] = targetingLocalSide,
            ["teamLeaderProtection"] = teamLeaderProtection
        });

        return true;
    }

    private async Task<TimeSpan> IgnoreCurrentTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        string reason,
        uint targetingServerObjectId = 0,
        long? timeoutMs = null,
        IReadOnlyDictionary<string, object?>? extraFields = null)
    {
        var now = DateTimeOffset.Now;
        var elapsedMs = state.TargetStartedAt == DateTimeOffset.MinValue
            ? 0
            : (long)Math.Max(0.0D, (now - state.TargetStartedAt).TotalMilliseconds);
        state.IgnoreTarget(targetEntityId, targetServerObjectId);
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = targetEntityId,
            ["serverObjectId"] = targetServerObjectId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["targetingServerObjectId"] = targetingServerObjectId,
            ["localServerObjectId"] = state.LocalCombatSideServerObjectId,
            ["localPetServerObjectId"] = state.LocalCombatSidePetServerObjectId,
            ["currentTargetIsMaintenanceDefense"] = state.CurrentTargetIsMaintenanceDefense,
            ["currentTargetIsRevivePathClear"] = state.CurrentTargetIsRevivePathClear,
            ["currentTargetBypassesHomeLeash"] = state.CurrentTargetBypassesHomeLeash,
            ["targetName"] = targetName,
            ["reason"] = reason,
            ["elapsedMs"] = elapsedMs,
            ["timeoutMs"] = timeoutMs ?? (long)TargetTimeout.TotalMilliseconds
        };
        if (extraFields is not null)
        {
            foreach (var field in extraFields)
            {
                fields[field.Key] = field.Value;
            }
        }

        context.Logger.Info("stationary_combat.target.ignored", fields);
        StopNextTargetPreAim(context, state, "target_ignored_" + reason, clearCandidate: true);
        await StopSoloJumpAsync(state, "target_ignored_" + reason).ConfigureAwait(false);
        state.ClearTarget();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return IdleDelay;
    }

    private static Task StopSoloJumpAsync(StationaryCombatState state, string reason)
    {
        return state.JumpAssist is null
            ? Task.CompletedTask
            : state.JumpAssist.StopSoloTargetAsync(reason);
    }

    private static void UpdateMaintenanceRestJumpPause(
        StationaryCombatState state,
        SemiAutoCombatState semiAutoState)
    {
        const string pauseReason = "stationary_maintenance_rest";
        if (semiAutoState.IsMaintenanceResting)
        {
            state.JumpAssist?.Pause(pauseReason);
        }
        else
        {
            state.JumpAssist?.Resume(pauseReason);
        }
    }

    private static bool IsTargetTimedOut(StationaryCombatState state, DateTimeOffset now)
    {
        return state.TargetStartedAt != DateTimeOffset.MinValue &&
               now - state.TargetStartedAt >= TargetTimeout;
    }

    private static string TargetActionKey(ushort entityId, uint serverObjectId)
    {
        return serverObjectId != 0
            ? "server:" + serverObjectId.ToString()
            : "entity:" + entityId.ToString();
    }

    private static string GetRevivePathName(AccountWorkerContext context)
    {
        var pathName = context.Config.ScriptSettings?.Paths?.RevivePathName;
        if (!string.IsNullOrWhiteSpace(pathName))
        {
            return pathName.Trim();
        }

        return context.Config.RevivePathName?.Trim() ?? string.Empty;
    }

    private static string GetCombatPathName(AccountWorkerContext context)
    {
        var pathName = context.Config.ScriptSettings?.Paths?.CombatPathName;
        if (!string.IsNullOrWhiteSpace(pathName))
        {
            return pathName.Trim();
        }

        return context.Config.CombatPathName?.Trim() ?? string.Empty;
    }

    private async Task<OperationResult<StationaryHomeResolution>> TryResolveStationaryHomeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var revivePathName = GetRevivePathName(context);
        if (_pathStore is not null && !string.IsNullOrWhiteSpace(revivePathName))
        {
            if (state.TryGetStationaryHomeFromRevivePath(
                    revivePathName,
                    out var cachedHome,
                    out var cachedPointCount))
            {
                return OperationResult<StationaryHomeResolution>.Ok(
                    new StationaryHomeResolution(cachedHome, "revive_path", revivePathName, cachedPointCount));
            }

            var pathResult = await _pathStore.LoadAsync(revivePathName, context.StopToken).ConfigureAwait(false);
            if (pathResult.Success && pathResult.Value?.Points is { Count: > 0 } points)
            {
                var home = points[^1].ToVector3();
                state.SetStationaryHomeFromRevivePath(revivePathName, home, points.Count);
                return OperationResult<StationaryHomeResolution>.Ok(
                    new StationaryHomeResolution(home, "revive_path", revivePathName, points.Count));
            }

            if (TryGetLegacyStationaryHome(context, out var legacyHome))
            {
                return OperationResult<StationaryHomeResolution>.Ok(
                    new StationaryHomeResolution(legacyHome, "legacy_config", revivePathName, 0));
            }

            var reason = pathResult.Success
                ? "revive_path_empty"
                : pathResult.Error ?? "revive_path_load_failed";
            return OperationResult<StationaryHomeResolution>.Fail(reason);
        }

        if (TryGetLegacyStationaryHome(context, out var homeFromLegacyConfig))
        {
            return OperationResult<StationaryHomeResolution>.Ok(
                new StationaryHomeResolution(homeFromLegacyConfig, "legacy_config", revivePathName, 0));
        }

        return OperationResult<StationaryHomeResolution>.Fail(
            _pathStore is null ? "path_store_missing" : "revive_path_name_missing");
    }

    private static bool TryGetLegacyStationaryHome(AccountWorkerContext context, out Vector3Snapshot home)
    {
        var combat = context.Config.ScriptSettings?.Combat;
        if (combat?.HasStationaryCombatPosition == true)
        {
            home = new Vector3Snapshot(
                (float)combat.StationaryCombatX,
                (float)combat.StationaryCombatY,
                (float)combat.StationaryCombatZ);
            return true;
        }

        home = default;
        return false;
    }

    private async Task<WorldObjectSnapshot?> TickNoTargetRestAtHomeAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        CombatScriptSettings combat,
        double playerDistanceFromHome)
    {
        if (!CanUseNoTargetRestAtHome(combat))
        {
            await CancelNoTargetRestAtHomeAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    "disabled")
                .ConfigureAwait(false);
            return null;
        }

        if (playerDistanceFromHome > ReturnStopDistance)
        {
            await CancelNoTargetRestAtHomeAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    "left_home")
                .ConfigureAwait(false);
            return null;
        }

        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition)
            .ConfigureAwait(false);
        target ??= await SelectTargetAsync(
                context,
                state,
                playerPosition,
                home,
                radius,
                combat.ContestMonster)
            .ConfigureAwait(false);

        if (target?.Position is not null)
        {
            await CancelNoTargetRestAtHomeAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    "target_available")
                .ConfigureAwait(false);
            return state.NoTargetRestActive ? null : target;
        }

        if (HasFullHealthAndMana(player))
        {
            await CancelNoTargetRestAtHomeAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    "resources_full")
                .ConfigureAwait(false);
            return null;
        }

        await TryEnterNoTargetRestAtHomeAsync(
                context,
                semiAutoState,
                state,
                player,
                playerDistanceFromHome)
            .ConfigureAwait(false);
        return null;
    }

    private async Task<bool> TryEnterNoTargetRestAtHomeAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        double playerDistanceFromHome)
    {
        if (HasFullHealthAndMana(player))
        {
            return false;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        if (player.IsResting)
        {
            var wasActive = state.NoTargetRestActive;
            state.MarkNoTargetRestActive();
            if (!wasActive)
            {
                context.Logger.Info("stationary_combat.no_target.rest_enter", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = NoTargetRestEnterKey,
                    ["alreadyResting"] = true,
                    ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
                    ["hp"] = player.CurrentHp,
                    ["maxHp"] = player.MaxHp,
                    ["mp"] = player.CurrentMp,
                    ["maxMp"] = player.MaxMp,
                    ["stanceFlags"] = player.StanceFlags,
                    ["stanceLow"] = player.StanceLowNibble,
                    ["motionMode"] = player.MotionMode
                });
            }

            return true;
        }

        var now = DateTimeOffset.Now;
        if (!state.ShouldPressNoTargetRestKey(now, NoTargetRestKeyRetryInterval))
        {
            state.MarkNoTargetRestActive();
            return true;
        }

        var phase = state.NoTargetRestActive ? "rest_reenter" : "rest_enter";
        var result = await _input
            .PressKeyAsync(NoTargetRestEnterKey, ReadKeyHoldDuration(context), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.no_target." + phase + ".failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = NoTargetRestEnterKey,
                ["error"] = result.Error
            });
            return false;
        }

        state.MarkNoTargetRestKey(now);
        context.Logger.Info("stationary_combat.no_target." + phase, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = NoTargetRestEnterKey,
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp,
            ["stanceFlags"] = player.StanceFlags,
            ["stanceLow"] = player.StanceLowNibble,
            ["motionMode"] = player.MotionMode
        });
        return true;
    }

    private async Task<bool> CancelNoTargetRestAtHomeAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        string reason)
    {
        if (!state.NoTargetRestActive)
        {
            return false;
        }

        if (state.NoTargetRestExitPending && !player.IsResting)
        {
            context.Logger.Info("stationary_combat.no_target.rest_exit_confirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = NoTargetRestExitKey,
                ["reason"] = reason,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["mp"] = player.CurrentMp,
                ["maxMp"] = player.MaxMp,
                ["stanceFlags"] = player.StanceFlags,
                ["stanceLow"] = player.StanceLowNibble,
                ["motionMode"] = player.MotionMode
            });
            state.ClearNoTargetRest();
            return true;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        var result = await _input
            .PressKeyAsync(NoTargetRestExitKey, ReadKeyHoldDuration(context), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.no_target.rest_exit.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = NoTargetRestExitKey,
                ["reason"] = reason,
                ["error"] = result.Error
            });
            state.MarkNoTargetRestExitPending();
            return false;
        }

        context.Logger.Info("stationary_combat.no_target.rest_exit", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = NoTargetRestExitKey,
            ["reason"] = reason,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp,
            ["stanceFlags"] = player.StanceFlags,
            ["stanceLow"] = player.StanceLowNibble,
            ["motionMode"] = player.MotionMode,
            ["waitingForStand"] = true
        });
        state.MarkNoTargetRestExitPending();
        return true;
    }

    private static bool CanUseNoTargetRestAtHome(CombatScriptSettings combat)
    {
        return combat.ReturnHomeWhenNoTarget && combat.SitWhenNoTargetAtHome;
    }

    private static bool HasFullHealthAndMana(PlayerSnapshot player)
    {
        return player.MaxHp > 0 &&
               player.CurrentHp >= player.MaxHp &&
               player.MaxMp > 0 &&
               player.CurrentMp >= player.MaxMp;
    }

    private async Task<PostLootNoTargetDelayResult> TryDelayNoTargetActionAfterLootAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        CombatScriptSettings combat)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldDelayNoTargetActionAfterLoot(state, now))
        {
            return new PostLootNoTargetDelayResult(false, null);
        }

        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition)
            .ConfigureAwait(false);
        target ??= await SelectTargetAsync(
                context,
                state,
                playerPosition,
                home,
                radius,
                combat.ContestMonster)
            .ConfigureAwait(false);
        if (target?.Position is not null)
        {
            return new PostLootNoTargetDelayResult(false, target);
        }

        state.ClearNoTargetRest();
        state.ResetReturnHomeStuckTracking();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        var elapsed = now - state.LastLootAfterKillFinishedAt;
        var elapsedMs = Math.Max(0, (int)Math.Round(elapsed.TotalMilliseconds));
        var remainingMs = Math.Max(0, (int)Math.Ceiling((PostLootNoTargetActionDelay - elapsed).TotalMilliseconds));
        LogActionThrottled(context, state, "stationary_combat.no_target.post_loot_delay", "post_loot_delay", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["delayMs"] = (int)PostLootNoTargetActionDelay.TotalMilliseconds,
            ["elapsedMs"] = elapsedMs,
            ["remainingMs"] = remainingMs
        }, TimeSpan.FromMilliseconds(200));
        return new PostLootNoTargetDelayResult(true, null);
    }

    private static bool ShouldDelayNoTargetActionAfterLoot(StationaryCombatState state, DateTimeOffset now)
    {
        var finishedAt = state.LastLootAfterKillFinishedAt;
        return finishedAt != DateTimeOffset.MinValue &&
               now >= finishedAt &&
               now - finishedAt < PostLootNoTargetActionDelay;
    }

    private static TimeSpan ReadKeyHoldDuration(AccountWorkerContext context)
    {
        var configuredMs = context.Config.ScriptSettings?.SemiAuto?.KeyHoldMs ?? 25;
        var value = configuredMs > 0 ? configuredMs : 25;
        return TimeSpan.FromMilliseconds(Math.Max(1, value));
    }

    private static double ResolvePathCombatRadius(CombatScriptSettings combat)
    {
        return Math.Max(1.0D, combat.PathCombatRadius);
    }

    private static double ResolveRevivePathAggressiveClearRadius(PathScriptSettings? paths)
    {
        return ClampDouble(
            paths?.RevivePathAggressiveClearRadius ?? PathScriptSettings.DefaultRevivePathAggressiveClearRadius,
            1.0D,
            500.0D);
    }

    private static double ResolvePathFollowReachDistance(CombatScriptSettings? combat)
    {
        return ClampDouble(combat?.PathFollowReachDistance ?? DefaultPathFollowReachDistance, 0.5D, 50.0D);
    }

    private static int FindNearestPathPointIndex(
        Vector3Snapshot playerPosition,
        IReadOnlyList<Vector3Snapshot> points,
        double homeDistance)
    {
        var nearestIndex = -1;
        var nearestDistance = homeDistance;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, points[i]);
            if (distance < nearestDistance)
            {
                nearestIndex = i;
                nearestDistance = distance;
            }
        }

        return nearestIndex;
    }

    private bool TryResolveSmartPreAimHandoffTarget(
        AccountWorkerContext context,
        StationaryCombatState state,
        IReadOnlyList<WorldObjectSnapshot> objects,
        DateTimeOffset now,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther,
        IReadOnlyList<string> activeMonsterNameFilters,
        out WorldObjectSnapshot? target,
        Func<WorldObjectSnapshot, bool>? additionalEligibility = null)
    {
        target = null;
        if (!state.HasSmartPreAimHandoff)
        {
            ushort preAimEntityId;
            uint preAimServerObjectId;
            DateTimeOffset alignedAt;
            lock (state.NextTargetPreAim.SyncRoot)
            {
                if (!state.NextTargetPreAim.HasAlignedCandidate)
                {
                    return false;
                }

                preAimEntityId = state.NextTargetPreAim.TargetEntityId;
                preAimServerObjectId = state.NextTargetPreAim.TargetServerObjectId;
                alignedAt = state.NextTargetPreAim.LastAlignedAt;
            }

            if (alignedAt == DateTimeOffset.MinValue ||
                now - alignedAt > ReadSmartPreAimResultTtl() ||
                ((state.CandidateEntityId != 0 || state.CandidateServerObjectId != 0) &&
                 !StationaryCombatState.IsSameTarget(
                     state.CandidateEntityId,
                     state.CandidateServerObjectId,
                     preAimEntityId,
                     preAimServerObjectId)))
            {
                return false;
            }

            state.StartSmartPreAimHandoff(preAimEntityId, preAimServerObjectId);
            bool displacedTargetGuardActivated;
            lock (state.NextTargetPreAim.SyncRoot)
            {
                displacedTargetGuardActivated = state.NextTargetPreAim.ActivateDisplacedTargetGuard(
                    preAimEntityId,
                    preAimServerObjectId);
            }

            context.Logger.Info("stationary_combat.smart_preaim.handoff_started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = preAimEntityId,
                ["targetServerObjectId"] = preAimServerObjectId,
                ["alignedAgeMs"] = (long)Math.Max(0.0D, (now - alignedAt).TotalMilliseconds),
                ["displacedTargetGuardActivated"] = displacedTargetGuardActivated
            });
        }

        var handoffTarget = objects.FirstOrDefault(candidate =>
            state.IsSmartPreAimHandoffTarget(candidate.EntityId, candidate.ServerObjectId));
        if (handoffTarget is null)
        {
            ReleaseSmartPreAimHandoff(
                context,
                state,
                "missing",
                clearPreAimCandidate: true);
            return false;
        }

        if (!allowClaimedByOther && IsClaimedByOther(handoffTarget, state))
        {
            state.IgnoreTarget(handoffTarget);
            ReleaseSmartPreAimHandoff(
                context,
                state,
                "claimed_by_other",
                clearPreAimCandidate: true);
            return false;
        }

        var targetSelectable = additionalEligibility is null
            ? IsCandidateStillSelectable(
                handoffTarget,
                home,
                radius,
                allowClaimedByOther,
                state)
            : handoffTarget.Position is not null &&
              StationaryCombatTargetSelector.IsSelectableMonster(handoffTarget) &&
              additionalEligibility(handoffTarget);
        if (state.IsTargetIgnored(handoffTarget) ||
            state.IsTargetTemporarilyExcluded(handoffTarget, now) ||
            IsActiveMonsterFiltered(handoffTarget, activeMonsterNameFilters) ||
            !targetSelectable)
        {
            ReleaseSmartPreAimHandoff(
                context,
                state,
                "target_invalid",
                clearPreAimCandidate: true);
            return false;
        }

        target = handoffTarget;
        return true;
    }

    private static void CompleteSmartPreAimHandoff(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId)
    {
        StopNextTargetPreAim(context, state, "handoff_fight_started", clearCandidate: true);
        state.ClearSmartPreAimHandoff(clearDisplacedTargetGuard: false);
        context.Logger.Info("stationary_combat.smart_preaim.handoff_completed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = targetServerObjectId
        });
    }

    private static void ReleaseSmartPreAimHandoff(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        bool clearPreAimCandidate)
    {
        var targetEntityId = state.SmartPreAimHandoffEntityId;
        var targetServerObjectId = state.SmartPreAimHandoffServerObjectId;
        if (clearPreAimCandidate)
        {
            StopNextTargetPreAim(context, state, "handoff_" + reason, clearCandidate: true);
        }

        state.ClearSmartPreAimHandoff(clearDisplacedTargetGuard: true);
        context.Logger.Info("stationary_combat.smart_preaim.handoff_released", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["reason"] = reason
        });
    }

    private async Task<WorldObjectSnapshot?> SelectTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther)
    {
        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        var preferAggressiveMonsters = PrefersAggressiveMonsters(context);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        var selectionOrigin = ResolvePendingNextTargetSelectionOrigin(context, state, playerPosition);

        if (TryResolveSmartPreAimHandoffTarget(
                context,
                state,
                objects,
                now,
                home,
                radius,
                allowClaimedByOther,
                activeMonsterNameFilters,
                out var handoffTarget))
        {
            return handoffTarget;
        }

        if (state.CandidateEntityId != 0 || state.CandidateServerObjectId != 0)
        {
            var candidate = state.FindCandidate(objects);
            if (!allowClaimedByOther && candidate is not null && IsClaimedByOther(candidate, state))
            {
                state.IgnoreTarget(candidate);
                LogActionThrottled(context, state, "stationary_combat.target.claimed_by_other", "candidate:" + TargetActionKey(candidate.EntityId, candidate.ServerObjectId), new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = candidate.EntityId,
                    ["serverObjectId"] = candidate.ServerObjectId,
                    ["targetServerObjectId"] = candidate.ServerObjectId,
                    ["targetingServerObjectId"] = candidate.TargetServerObjectId,
                    ["targetName"] = candidate.Name,
                }, TimeSpan.FromMilliseconds(500));
            }

            if (candidate is not null && IsActiveMonsterFiltered(candidate, activeMonsterNameFilters))
            {
                LogActionThrottled(context, state, "stationary_combat.target.filtered", "candidate:" + TargetActionKey(candidate.EntityId, candidate.ServerObjectId), new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = candidate.EntityId,
                    ["serverObjectId"] = candidate.ServerObjectId,
                    ["targetServerObjectId"] = candidate.ServerObjectId,
                    ["targetName"] = candidate.Name
                }, TimeSpan.FromMilliseconds(500));
            }
            else if (candidate is not null &&
                !state.IsTargetIgnored(candidate) &&
                !state.IsTargetTemporarilyExcluded(candidate, now) &&
                IsCandidateStillSelectable(candidate, home, radius, allowClaimedByOther, state))
            {
                if (!ShouldReplaceCandidateWithAggressiveTarget(
                    preferAggressiveMonsters,
                    candidate,
                    objects,
                    state,
                    home,
                    radius,
                    allowClaimedByOther))
                {
                    return candidate;
                }
            }
        }

        var candidates = objects
            .Where(target => !state.IsTargetIgnored(target))
            .Where(target => !state.IsTargetTemporarilyExcluded(target, now))
            .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters));
        if (!allowClaimedByOther)
        {
            candidates = candidates.Where(target => !IsClaimedByOther(target, state));
        }

        var selected = StationaryCombatTargetSelector.SelectNearest(
            candidates,
            selectionOrigin,
            home,
            radius,
            preferAggressiveMonsters);
        if (selected is null)
        {
            LogNoTargetScan(
                context,
                state,
                objects,
                playerPosition,
                home,
                radius,
                allowClaimedByOther,
                activeMonsterNameFilters,
                now);
        }

        return selected;
    }

    private static void LogNoTargetScan(
        AccountWorkerContext context,
        StationaryCombatState state,
        IReadOnlyList<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther,
        IReadOnlyList<string> activeMonsterNameFilters,
        DateTimeOffset now)
    {
        var monsterObjects = objects
            .Where(target => string.Equals(target.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var aliveMonsters = monsterObjects
            .Where(target => target.IsAlive)
            .ToArray();
        var selectableMonsters = aliveMonsters
            .Where(target => target.Position is not null)
            .ToArray();
        var monstersWithPosition = selectableMonsters
            .Where(target => target.Position is not null)
            .ToArray();
        var monstersWithoutPosition = aliveMonsters
            .Where(target => target.Position is null)
            .ToArray();
        var ignored = monstersWithPosition
            .Where(state.IsTargetIgnored)
            .ToArray();
        var temporarilyExcluded = monstersWithPosition
            .Where(target => state.IsTargetTemporarilyExcluded(target, now))
            .ToArray();
        var activeFiltered = monstersWithPosition
            .Where(target => IsActiveMonsterFiltered(target, activeMonsterNameFilters))
            .ToArray();
        var claimedByOther = allowClaimedByOther
            ? Array.Empty<WorldObjectSnapshot>()
            : monstersWithPosition
                .Where(target => !state.IsTargetIgnored(target))
                .Where(target => !state.IsTargetTemporarilyExcluded(target, now))
                .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters))
                .Where(target => IsClaimedByOther(target, state))
                .ToArray();
        var finalCandidates = monstersWithPosition
            .Where(target => !state.IsTargetIgnored(target))
            .Where(target => !state.IsTargetTemporarilyExcluded(target, now))
            .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters))
            .Where(target => allowClaimedByOther || !IsClaimedByOther(target, state))
            .Where(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, home) <= radius)
            .ToArray();
        var insideHomeRadius = monstersWithPosition
            .Where(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, home) <= radius)
            .ToArray();
        var insidePlayerRadius = monstersWithPosition
            .Where(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition) <= radius)
            .ToArray();
        var nearestSamples = monstersWithPosition
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .Take(5)
            .Select(target => FormatTargetScanSample(target, playerPosition, home, radius, state, activeMonsterNameFilters, allowClaimedByOther, now))
            .ToArray();

        LogActionThrottled(context, state, "stationary_combat.target.scan_none", "normal", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["worldObjectCount"] = objects.Count,
            ["monsterObjectCount"] = monsterObjects.Length,
            ["aliveMonsterCount"] = aliveMonsters.Length,
            ["selectableMonsterCount"] = selectableMonsters.Length,
            ["monsterWithPositionCount"] = monstersWithPosition.Length,
            ["monsterWithoutPositionCount"] = monstersWithoutPosition.Length,
            ["insideHomeRadiusCount"] = insideHomeRadius.Length,
            ["insidePlayerRadiusCount"] = insidePlayerRadius.Length,
            ["ignoredCount"] = ignored.Length,
            ["temporarilyExcludedCount"] = temporarilyExcluded.Length,
            ["activeFilteredCount"] = activeFiltered.Length,
            ["claimedByOtherCount"] = claimedByOther.Length,
            ["finalCandidateCount"] = finalCandidates.Length,
            ["radius"] = Math.Round(radius, 2),
            ["homeX"] = Math.Round(home.X, 2),
            ["homeY"] = Math.Round(home.Y, 2),
            ["playerX"] = Math.Round(playerPosition.X, 2),
            ["playerY"] = Math.Round(playerPosition.Y, 2),
            ["allowClaimedByOther"] = allowClaimedByOther,
            ["activeMonsterFilters"] = string.Join(",", activeMonsterNameFilters),
            ["nearestSamples"] = string.Join(" | ", nearestSamples)
        }, TimeSpan.FromSeconds(1));
    }

    private static string FormatTargetScanSample(
        WorldObjectSnapshot target,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        StationaryCombatState state,
        IReadOnlyList<string> activeMonsterNameFilters,
        bool allowClaimedByOther,
        DateTimeOffset now)
    {
        var playerDistance = target.Position is null
            ? double.NaN
            : StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, playerPosition);
        var homeDistance = target.Position is null
            ? double.NaN
            : StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home);
        var reasons = new List<string>();
        if (state.IsTargetIgnored(target))
        {
            reasons.Add("ignored");
        }

        if (state.IsTargetTemporarilyExcluded(target, now))
        {
            reasons.Add("temporarily_excluded");
        }

        if (IsActiveMonsterFiltered(target, activeMonsterNameFilters))
        {
            reasons.Add("name_filtered");
        }

        if (!allowClaimedByOther && IsClaimedByOther(target, state))
        {
            reasons.Add("claimed");
        }

        if (!double.IsNaN(homeDistance) && homeDistance > radius)
        {
            reasons.Add("outside_home");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("candidate");
        }

        return string.Join(
            ",",
            target.Name,
            "entity=" + target.EntityId,
            "server=" + target.ServerObjectId,
            "playerDist=" + Math.Round(playerDistance, 2),
            "homeDist=" + Math.Round(homeDistance, 2),
            "targetServer=" + target.TargetServerObjectId,
            "hp=" + target.CurrentHp + "/" + target.MaxHp,
            "reason=" + string.Join("+", reasons));
    }

    private async Task MaintainLeaderTacticalMarkAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        LockedTargetSnapshot target)
    {
        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        var leader = team.Leader ?? new TeamLeaderScriptSettings();
        if (team.Role != TeamRole.Leader ||
            !leader.Enabled ||
            !leader.TacticalMarkEnabled)
        {
            state.LeaderTacticalMark.Reset();
            return;
        }

        var markKey = string.IsNullOrWhiteSpace(leader.TacticalMarkKey)
            ? TeamLeaderScriptSettings.DefaultTacticalMarkKey
            : leader.TacticalMarkKey.Trim();
        await _tacticalMark
            .MaintainLeaderTargetMarkAsync(
                context,
                state.LeaderTacticalMark,
                target,
                markKey,
                ReadKeyHoldDuration(context))
            .ConfigureAwait(false);
    }

    private async Task<WorldObjectSnapshot?> SelectMaintenanceDefenseTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        IReadOnlyList<WorldObjectSnapshot>? preloadedObjects = null)
    {
        var objects = preloadedObjects ??
                      await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        var selectionOrigin = ResolvePendingNextTargetSelectionOrigin(context, state, playerPosition);
        var availableObjects = objects
            .Where(target => !state.IsTargetTemporarilyExcluded(target, now))
            .ToArray();
        var teamThreat = await SelectTeamLeaderProtectionTargetAsync(
                context,
                state,
                availableObjects,
                playerPosition)
            .ConfigureAwait(false);
        if (teamThreat is not null)
        {
            state.MarkTeamLeaderProtectionTarget(teamThreat.Target);
            LogActionThrottled(context, state, "stationary_combat.team_leader.protection_target_selected", "target:" + TargetActionKey(teamThreat.Target.EntityId, teamThreat.Target.ServerObjectId), new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = teamThreat.Target.EntityId,
                ["targetServerObjectId"] = teamThreat.Target.ServerObjectId,
                ["targetName"] = teamThreat.Target.Name,
                ["targetingServerObjectId"] = teamThreat.Target.TargetServerObjectId,
                ["protectedMember"] = teamThreat.ProtectedMember.Name,
                ["protectedMemberServerObjectId"] = teamThreat.ProtectedMember.ServerObjectId,
                ["protectedServerObjectId"] = teamThreat.ProtectedServerObjectId,
                ["protectedObjectIsPet"] = teamThreat.ProtectedObjectIsPet,
                ["protectedClass"] = teamThreat.ProtectedMember.PartyMember.ClassName,
                ["priority"] = teamThreat.Priority
            }, TimeSpan.FromMilliseconds(500));
            return teamThreat.Target;
        }

        var localSideThreat = availableObjects
            .Where(target => IsTargetingLocalSide(target, state))
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, selectionOrigin))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
        if (localSideThreat is not null)
        {
            state.ClearTeamLeaderProtectionTarget();
        }

        return localSideThreat;
    }

    private async Task<(bool CanSit, WorldObjectSnapshot? DefenseTarget)> EvaluateLeaderRestGuardAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition)
    {
        var objects = await ReadWorldObjectsAsync(context).ConfigureAwait(false);

        var monitor = new TeamMonitor(context.Snapshots, context.Logger);
        var snapshot = await monitor.ReadSnapshotAsync().ConfigureAwait(false);

        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        var teamThreat = TeamLeaderProtectionSelector.SelectThreat(
            snapshot,
            objects,
            playerPosition,
            team.GroupDistanceMeters);
        var localThreat = objects
            .Where(target => IsTargetingLocalSide(target, state))
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
        var defenseTarget = teamThreat?.Target ?? localThreat;
        if (defenseTarget is null)
        {
            return (true, null);
        }

        var protectedServerObjectIds = TeamLeaderProtectionSelector.CreateProtectedServerObjectIds(
            snapshot,
            team.GroupDistanceMeters);
        var threatCount = objects.Count(target =>
            StationaryCombatTargetSelector.IsSelectableMonster(target) &&
            (IsTargetingLocalSide(target, state) ||
             protectedServerObjectIds.Contains(target.TargetServerObjectId)));
        if (teamThreat is not null)
        {
            state.MarkTeamLeaderProtectionTarget(teamThreat.Target);
        }
        else
        {
            state.ClearTeamLeaderProtectionTarget();
        }

        LogLeaderRestBlocked(
            context,
            state,
            "team_combat_active",
            defenseTarget,
            threatCount,
            teamThreat);
        return (false, defenseTarget);
    }

    private static bool IsEnabledTeamLeader(AccountWorkerContext context)
    {
        var team = context.Config.ScriptSettings?.Team;
        return team?.Role == TeamRole.Leader && team.Leader?.Enabled == true;
    }

    private static bool ShouldAttemptSitMaintenance(
        AccountWorkerContext context,
        PlayerSnapshot player)
    {
        var maintenance = context.Config.ScriptSettings?.Maintenance;
        return maintenance?.SitMaintenanceEnabled == true &&
               (IsPercentAtOrBelow(player.CurrentHp, player.MaxHp, maintenance.SitHpBelowPercent) ||
                IsPercentAtOrBelow(player.CurrentMp, player.MaxMp, maintenance.SitMpBelowPercent));
    }

    private static bool IsPercentAtOrBelow(uint current, uint max, int threshold)
    {
        return max > 0 && current * 100.0D / max <= Math.Clamp(threshold, 0, 100);
    }

    private static void LogLeaderRestBlocked(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        WorldObjectSnapshot? defenseTarget = null,
        int threatCount = 0,
        TeamLeaderProtectionThreat? teamThreat = null,
        string? error = null)
    {
        LogActionThrottled(
            context,
            state,
            "stationary_combat.team_leader.maintenance_rest_blocked",
            "leader_rest:" + reason,
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = reason,
                ["threatCount"] = threatCount,
                ["targetEntityId"] = defenseTarget?.EntityId ?? 0,
                ["targetServerObjectId"] = defenseTarget?.ServerObjectId ?? 0,
                ["targetName"] = defenseTarget?.Name ?? string.Empty,
                ["targetingServerObjectId"] = defenseTarget?.TargetServerObjectId ?? 0,
                ["protectedMember"] = teamThreat?.ProtectedMember.Name ?? string.Empty,
                ["protectedMemberServerObjectId"] = teamThreat?.ProtectedMember.ServerObjectId ?? 0,
                ["protectedObjectIsPet"] = teamThreat?.ProtectedObjectIsPet ?? false,
                ["error"] = error
            },
            TimeSpan.FromMilliseconds(500));
    }

    private async Task<TeamLeaderProtectionThreat?> SelectTeamLeaderProtectionTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        IReadOnlyList<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition)
    {
        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        var leader = team.Leader ?? new TeamLeaderScriptSettings();
        if (team.Role != TeamRole.Leader || !leader.Enabled)
        {
            return null;
        }

        var monitor = new TeamMonitor(context.Snapshots, context.Logger);
        var snapshot = await monitor.ReadSnapshotAsync().ConfigureAwait(false);

        return TeamLeaderProtectionSelector.SelectThreat(
            snapshot,
            objects,
            playerPosition,
            team.GroupDistanceMeters);
    }

    private async Task<WorldObjectSnapshot?> SelectRevivePathAggressiveClearTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        IReadOnlyList<WorldObjectSnapshot>? preloadedObjects = null)
    {
        var objects = preloadedObjects ??
                      await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        var clearRadius = ResolveRevivePathAggressiveClearRadius(context.Config.ScriptSettings?.Paths);
        return objects
            .Where(target => !state.IsTargetIgnored(target))
            .Where(target => !state.IsTargetTemporarilyExcluded(target, DateTimeOffset.Now))
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => target.IsAggressiveToPlayer)
            .Where(target => !IsClaimedByOther(target, state))
            .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters))
            .Where(target => StationaryCombatTargetSelector.HorizontalDistance(
                target.Position!.Value,
                playerPosition) <= clearRadius)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
    }

    private async Task<bool> HasStationaryGatherWorkAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot home,
        GatherScriptSettings settings,
        double searchRadiusMeters)
    {
        if (state.Gather.Active)
        {
            return true;
        }

        var snapshot = await RefreshGatherSnapshotAsync(context, state).ConfigureAwait(false);
        if (snapshot.LocalGathering.IsDialogVisible)
        {
            return true;
        }

        return StationaryGatherSelector.SelectCandidate(
            snapshot,
            settings,
            home,
            searchRadiusMeters,
            DateTimeOffset.Now,
            state.Gather.IsSuppressed) is not null;
    }

    private async Task<StationaryGatherTickResult> TickStationaryGatherAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        GatherScriptSettings settings,
        double searchRadiusMeters)
    {
        var now = DateTimeOffset.Now;
        var snapshot = await RefreshGatherSnapshotAsync(context, state).ConfigureAwait(false);
        var localGathering = snapshot.LocalGathering;
        var gatherCommitted =
            state.Gather.Phase is StationaryGatherPhase.WaitingForStart or StationaryGatherPhase.Gathering ||
            localGathering.IsDialogVisible;
        if (gatherCommitted)
        {
            var localAttacker = SelectGatherInterruptingAttacker(
                snapshot.NearbyMonsters,
                playerPosition,
                state);
            if (localAttacker is not null)
            {
                var interruptedPhase = state.Gather.Phase;
                if (state.Gather.Active)
                {
                    state.Gather.MarkReady(now);
                }

                semiAutoState.ResetAttackKeyPressThrottle();
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                context.Logger.Warn("stationary_gather.interrupted_by_attacker", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["gatherServerObjectId"] = state.Gather.ServerObjectId,
                    ["gatherSourceId"] = state.Gather.GatherSourceId,
                    ["targetEntityId"] = localAttacker.EntityId,
                    ["targetServerObjectId"] = localAttacker.ServerObjectId,
                    ["targetName"] = localAttacker.Name,
                    ["targetingServerObjectId"] = localAttacker.TargetServerObjectId,
                    ["dialogVisible"] = localGathering.IsDialogVisible,
                    ["gatherPhase"] = interruptedPhase.ToString()
                });
                return StationaryGatherTickResult.ForThreat(localAttacker);
            }
        }

        var currentGatheringStarted =
            localGathering.IsDialogVisible &&
            state.Gather.Active &&
            localGathering.IsActive &&
            localGathering.GatherSourceId == state.Gather.GatherSourceId;
        if (currentGatheringStarted)
        {
            state.Gather.MarkGathering(now);
        }
        else if (state.Gather.IsStartWaitTimedOut(now, ReadGatherAttemptStartTimeout()))
        {
            context.Logger.Warn("stationary_gather.attempt.start_timeout", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["serverObjectId"] = state.Gather.ServerObjectId,
                ["gatherSourceId"] = state.Gather.GatherSourceId,
                ["name"] = state.Gather.Name,
                ["waitedMs"] = (long)(now - state.Gather.StartWaitStartedAt).TotalMilliseconds,
                ["maximumWaitMs"] = (long)ReadGatherAttemptStartTimeout().TotalMilliseconds
            });
            await StopAndSuppressGatherAsync(
                    context,
                    state,
                    now,
                    "start_timeout",
                    GatherFailureSuppression)
                .ConfigureAwait(false);
            return StationaryGatherTickResult.NotHandled;
        }

        if (localGathering.IsDialogVisible)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            if (currentGatheringStarted)
            {
                LogActionThrottled(
                    context,
                    state,
                    "stationary_gather.progress.active",
                    "node:" + state.Gather.ServerObjectId,
                    CreateGatherLogFields(context, state, localGathering),
                    TimeSpan.FromMilliseconds(500));
            }

            return StationaryGatherTickResult.HandledWith(GatherPollDelay);
        }

        if (state.Gather.Phase == StationaryGatherPhase.Gathering)
        {
            state.Gather.MarkAttemptFinished(now);
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            context.Logger.Info("stationary_gather.attempt.finished", CreateGatherLogFields(context, state, localGathering));
            return StationaryGatherTickResult.HandledWith(GatherPollDelay);
        }

        GatherObjectSnapshot? target;
        GatherFilterRuleSettings? rule;
        if (state.Gather.Active)
        {
            target = snapshot.FindObject(state.Gather.ServerObjectId);
            if (target is null)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                StopPathFollowPoller(state);
                var completedServerObjectId = state.Gather.ServerObjectId;
                var completedSourceId = state.Gather.GatherSourceId;
                var completedName = state.Gather.Name;
                state.Gather.CompleteCurrent();
                context.Logger.Info("stationary_gather.node.completed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["serverObjectId"] = completedServerObjectId,
                    ["gatherSourceId"] = completedSourceId,
                    ["name"] = completedName
                });

                return StationaryGatherTickResult.HandledWith(GatherPollDelay);
            }

            rule = FindGatherRule(settings, target.GatherSourceId);
            if (rule is null)
            {
                await StopAndSuppressGatherAsync(
                        context,
                        state,
                        now,
                        "rule_unavailable",
                        GatherFailureSuppression)
                    .ConfigureAwait(false);
                return StationaryGatherTickResult.NotHandled;
            }

            state.Gather.Refresh(target, rule);
        }
        else
        {
            var selected = StationaryGatherSelector.SelectCandidate(
                snapshot,
                settings,
                home,
                searchRadiusMeters,
                now,
                state.Gather.IsSuppressed);
            if (selected is null)
            {
                return StationaryGatherTickResult.NotHandled;
            }

            target = selected.Target;
            rule = selected.Rule;
            state.Gather.Track(target, rule, now);
            context.Logger.Info("stationary_gather.node.selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["serverObjectId"] = target.ServerObjectId,
                ["gatherSourceId"] = target.GatherSourceId,
                ["name"] = state.Gather.Name,
                ["key"] = rule.GatherKey,
                ["distanceToPlayer"] = target.DistanceToLocalPlayer,
                ["searchRadiusMeters"] = searchRadiusMeters,
                ["occupiedCheckRadiusMeters"] = settings.OccupiedCheckRadiusMeters
            });
        }

        if (target is null || rule is null || state.Gather.Position is not { } targetPosition)
        {
            await StopAndSuppressGatherAsync(
                    context,
                    state,
                    now,
                    "position_unavailable",
                    GatherFailureSuppression)
                .ConfigureAwait(false);
            return StationaryGatherTickResult.NotHandled;
        }

        if (now - state.Gather.NodeStartedAt >= ReadGatherNodeTimeout())
        {
            await StopAndSuppressGatherAsync(
                    context,
                    state,
                    now,
                    "node_timeout",
                    ReadGatherNodeTimeout())
                .ConfigureAwait(false);
            return StationaryGatherTickResult.NotHandled;
        }

        if (target.RuntimeAvailabilityRaw == 0 ||
            target.InteractionAvailability != GatherInteractionAvailability.Allowed)
        {
            await StopAndSuppressGatherAsync(
                    context,
                    state,
                    now,
                    "not_allowed",
                    GatherFailureSuppression)
                .ConfigureAwait(false);
            return StationaryGatherTickResult.NotHandled;
        }

        if (snapshot.IsLikelyOccupied(
                target,
                Math.Clamp(settings.OccupiedCheckRadiusMeters, 0.5D, 20.0D)))
        {
            await StopAndSuppressGatherAsync(
                    context,
                    state,
                    now,
                    "occupied",
                    GatherFailureSuppression)
                .ConfigureAwait(false);
            return StationaryGatherTickResult.NotHandled;
        }

        var threat = gatherCommitted
            ? null
            : SelectGatherSafetyThreat(
                snapshot.NearbyMonsters,
                targetPosition,
                Math.Clamp(settings.ThreatClearRadiusMeters, 0.5D, 50.0D),
                state);
        if (threat is not null)
        {
            state.Gather.MarkReady(now);
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogActionThrottled(
                context,
                state,
                "stationary_gather.threat.selected",
                "target:" + TargetActionKey(threat.EntityId, threat.ServerObjectId),
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["gatherServerObjectId"] = target.ServerObjectId,
                    ["gatherSourceId"] = target.GatherSourceId,
                    ["targetEntityId"] = threat.EntityId,
                    ["targetServerObjectId"] = threat.ServerObjectId,
                    ["targetName"] = threat.Name,
                    ["targetingServerObjectId"] = threat.TargetServerObjectId,
                    ["targetingLocalSide"] = IsTargetingLocalSide(threat, state),
                    ["aggressiveKnown"] = threat.AggressiveKnown,
                    ["aggressiveToPlayer"] = threat.IsAggressiveToPlayer,
                    ["threatRadiusMeters"] = settings.ThreatClearRadiusMeters
                },
                TimeSpan.FromMilliseconds(500));
            return StationaryGatherTickResult.ForThreat(threat);
        }

        var distanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (distanceToTarget > GatherKeyActivationDistance)
        {
            state.Gather.MarkApproaching(playerPosition, now);
            state.Gather.ObserveApproachProgress(playerPosition, now, ReadGatherApproachProgressDistance());
            if (state.Gather.IsApproachTimedOut(now, ReadGatherApproachTimeout()))
            {
                await StopAndSuppressGatherAsync(
                        context,
                        state,
                        now,
                        "approach_timeout",
                        ReadGatherNodeTimeout())
                    .ConfigureAwait(false);
                return StationaryGatherTickResult.NotHandled;
            }

            if (state.Gather.ShouldJumpApproach(
                    now,
                    ReadGatherApproachStuckDelay(),
                    GatherApproachJumpRetryDelay,
                    maximumJumps: 2))
            {
                var jump = await _input
                    .PressKeyAsync("Space", ReadKeyHoldDuration(context), context.StopToken)
                    .ConfigureAwait(false);
                if (jump.Success)
                {
                    state.Gather.MarkApproachJump(now);
                    context.Logger.Info("stationary_gather.approach.jump", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["serverObjectId"] = target.ServerObjectId,
                        ["gatherSourceId"] = target.GatherSourceId,
                        ["distanceMeters"] = Math.Round(distanceToTarget, 2)
                    });
                }
            }

            semiAutoState.ResetAttackKeyPressThrottle();
            await PathFollowStepAsync(
                    context,
                    state,
                    player,
                    targetPosition,
                    GatherKeyActivationDistance)
                .ConfigureAwait(false);
            return StationaryGatherTickResult.HandledWith(MoveTickDelay);
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        if (state.Gather.Phase == StationaryGatherPhase.WaitingForStart)
        {
            return StationaryGatherTickResult.HandledWith(GatherPollDelay);
        }

        if (!state.Gather.CanPressAgain(now, GatherAttemptRetryDelay))
        {
            return StationaryGatherTickResult.HandledWith(GatherPollDelay);
        }

        var press = await _input
            .PressKeyAsync(rule.GatherKey, ReadKeyHoldDuration(context), context.StopToken)
            .ConfigureAwait(false);
        if (!press.Success)
        {
            var failures = state.Gather.MarkAttemptStartFailed(now);
            context.Logger.Warn("stationary_gather.key.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["serverObjectId"] = target.ServerObjectId,
                ["gatherSourceId"] = target.GatherSourceId,
                ["key"] = rule.GatherKey,
                ["failureCount"] = failures,
                ["error"] = press.Error
            });
            if (failures >= 3)
            {
                state.Gather.SuppressCurrent(now, GatherFailureSuppression);
                return StationaryGatherTickResult.NotHandled;
            }

            return StationaryGatherTickResult.HandledWith(GatherPollDelay);
        }

        state.Gather.MarkKeyPressed(now);
        context.Logger.Info("stationary_gather.key.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["serverObjectId"] = target.ServerObjectId,
            ["gatherSourceId"] = target.GatherSourceId,
            ["name"] = state.Gather.Name,
            ["key"] = rule.GatherKey,
            ["distanceMeters"] = Math.Round(distanceToTarget, 2),
            ["activationDistanceMeters"] = GatherKeyActivationDistance,
            ["startTimeoutMs"] = (long)ReadGatherAttemptStartTimeout().TotalMilliseconds
        });
        return StationaryGatherTickResult.HandledWith(GatherPollDelay);
    }

    private async Task StopAndSuppressGatherAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        DateTimeOffset now,
        string reason,
        TimeSpan suppression)
    {
        var serverObjectId = state.Gather.ServerObjectId;
        var gatherSourceId = state.Gather.GatherSourceId;
        var name = state.Gather.Name;
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        state.Gather.SuppressCurrent(now, suppression);
        context.Logger.Warn("stationary_gather.node.suppressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["serverObjectId"] = serverObjectId,
            ["gatherSourceId"] = gatherSourceId,
            ["name"] = name,
            ["reason"] = reason,
            ["suppressionMs"] = (long)suppression.TotalMilliseconds
        });
    }

    private static GatherFilterRuleSettings? FindGatherRule(
        GatherScriptSettings settings,
        uint gatherSourceId)
    {
        return (settings.Rules ?? new List<GatherFilterRuleSettings>())
            .FirstOrDefault(rule =>
                rule.Enabled &&
                rule.GatherSourceId == gatherSourceId &&
                !string.IsNullOrWhiteSpace(rule.GatherKey));
    }

    private static WorldObjectSnapshot? SelectGatherSafetyThreat(
        IReadOnlyList<WorldObjectSnapshot> monsters,
        Vector3Snapshot gatherPosition,
        double threatRadius,
        StationaryCombatState state)
    {
        return monsters
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target =>
                (!state.IsTargetTemporarilyExcluded(target, DateTimeOffset.Now) &&
                 IsTargetingLocalSide(target, state)) ||
                (!state.IsTargetIgnored(target) &&
                 !state.IsTargetTemporarilyExcluded(target, DateTimeOffset.Now) &&
                 !IsClaimedByOther(target, state) &&
                 target.AggressiveKnown &&
                 target.IsAggressiveToPlayer &&
                 StationaryCombatTargetSelector.HorizontalDistance(
                     target.Position!.Value,
                     gatherPosition) <= threatRadius))
            .OrderByDescending(target => IsTargetingLocalSide(target, state))
            .ThenBy(target => StationaryCombatTargetSelector.HorizontalDistance(
                target.Position!.Value,
                gatherPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
    }

    private static WorldObjectSnapshot? SelectGatherInterruptingAttacker(
        IReadOnlyList<WorldObjectSnapshot> monsters,
        Vector3Snapshot playerPosition,
        StationaryCombatState state)
    {
        return monsters
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => IsTargetingLocalSide(target, state))
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(
                target.Position!.Value,
                playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
    }

    private static bool IsSameGatherThreat(
        WorldObjectSnapshot target,
        WorldObjectSnapshot? gatherThreat)
    {
        return gatherThreat is not null &&
               StationaryCombatState.IsSameTarget(
                   target.EntityId,
                   target.ServerObjectId,
                   gatherThreat.EntityId,
                   gatherThreat.ServerObjectId);
    }

    private static Dictionary<string, object?> CreateGatherLogFields(
        AccountWorkerContext context,
        StationaryCombatState state,
        LocalGatheringSnapshot progress)
    {
        return new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["serverObjectId"] = state.Gather.ServerObjectId,
            ["gatherSourceId"] = state.Gather.GatherSourceId,
            ["name"] = state.Gather.Name,
            ["dialogVisible"] = progress.IsDialogVisible,
            ["currentGatherSourceId"] = progress.GatherSourceId,
            ["hasTargetEntity"] = progress.HasTargetEntity,
            ["skillId"] = progress.SkillId,
            ["successMaximum"] = progress.SuccessGauge?.Maximum,
            ["successDisplayed"] = progress.SuccessGauge?.Displayed,
            ["successTarget"] = progress.SuccessGauge?.Target,
            ["failureMaximum"] = progress.FailureGauge?.Maximum,
            ["failureDisplayed"] = progress.FailureGauge?.Displayed,
            ["failureTarget"] = progress.FailureGauge?.Target
        };
    }

    private async Task<GatherSnapshot> RefreshGatherSnapshotAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var now = DateTimeOffset.Now;
        var snapshot = await ReadGatherSnapshotAsync(context).ConfigureAwait(false);
        state.CachedGatherSnapshot = snapshot;
        state.CachedWorldObjects = snapshot.NearbyMonsters;
        _radarSnapshots?.PublishWorldObjects(
            context.Config.AccountName,
            state.CachedWorldObjects,
            now);
        return snapshot;
    }

    private async Task<IReadOnlyList<WorldObjectSnapshot>> RefreshWorldObjectsAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var gather = context.Config.ScriptSettings?.Gather;
        if (gather?.StationaryPriorityEnabled == true)
        {
            await RefreshGatherSnapshotAsync(context, state).ConfigureAwait(false);
            state.PruneIgnoredTargets(state.CachedWorldObjects);
            state.PruneTemporaryTargetExclusions(state.CachedWorldObjects, DateTimeOffset.Now);
            return state.CachedWorldObjects;
        }

        var now = DateTimeOffset.Now;
        state.CachedWorldObjects = await ReadWorldObjectsAsync(context).ConfigureAwait(false);
        _radarSnapshots?.PublishWorldObjects(
            context.Config.AccountName,
            state.CachedWorldObjects,
            now);

        state.PruneIgnoredTargets(state.CachedWorldObjects);
        state.PruneTemporaryTargetExclusions(state.CachedWorldObjects, now);
        return state.CachedWorldObjects;
    }

    private async Task<RadarNavigationDecision?> ResolveObstacleNavigationAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot start,
        Vector3Snapshot goal,
        RadarNavigationPurpose purpose,
        uint targetServerObjectId,
        RadarObstacleScriptSettings settings,
        double finalReachDistanceMeters)
    {
        if (!settings.Enabled || _obstacleNavigator is null)
        {
            return null;
        }

        var mapId = await ReadRadarMapIdAsync(context, state).ConfigureAwait(false);
        if (mapId == 0)
        {
            LogActionThrottled(
                context,
                state,
                "stationary_combat.radar.fallback",
                "map_unavailable",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "map_id_unavailable"
                },
                TimeSpan.FromSeconds(3));
            return null;
        }

        return await _obstacleNavigator.ResolveAsync(
                state.ObstacleNavigation,
                mapId,
                ToRadarPoint(start),
                ToRadarPoint(goal),
                purpose,
                targetServerObjectId,
                settings,
                finalReachDistanceMeters,
                context.StopToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryCommitRadarDirectAfterWaypointStopAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        uint targetServerObjectId,
        RadarObstacleScriptSettings settings,
        RadarDirectTargetSource targetSource,
        string phase,
        PathFollowStopContext stop)
    {
        if (stop.Arrived)
        {
            state.ObstacleNavigation.LatchCurrentWaypointArrival();
        }

        if (!stop.StoppedForward || targetServerObjectId == 0)
        {
            return false;
        }

        var playerSnapshot = await context.Snapshots.ReadPlayerAsync().ConfigureAwait(false);
        if (playerSnapshot.Value.Position is not { } playerPosition)
        {
            LogRadarDirectConfirmation(
                context,
                state,
                phase,
                targetServerObjectId,
                stop,
                committed: false,
                "player_position_unavailable");
            return false;
        }

        Vector3Snapshot? targetPosition = null;
        if (targetSource == RadarDirectTargetSource.LockedTarget)
        {
            var targetSnapshot = await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false);
            if (targetSnapshot.Value is { } target &&
                target.ServerObjectId == targetServerObjectId &&
                target.IsMonsterAlive &&
                target.Position is { } position)
            {
                targetPosition = position;
            }
        }
        else
        {
            var worldSnapshot = await context.Snapshots.ReadWorldObjectsAsync().ConfigureAwait(false);
            targetPosition = worldSnapshot.Value
                .FirstOrDefault(candidate =>
                    candidate.ServerObjectId == targetServerObjectId &&
                    candidate.IsAlive &&
                    candidate.Position is not null)
                ?.Position;
        }

        if (targetPosition is not { } confirmedTargetPosition)
        {
            LogRadarDirectConfirmation(
                context,
                state,
                phase,
                targetServerObjectId,
                stop,
                committed: false,
                "same_target_missing");
            return false;
        }

        var confirmation = await ResolveObstacleNavigationAsync(
                context,
                state,
                playerPosition,
                confirmedTargetPosition,
                RadarNavigationPurpose.ApproachTarget,
                targetServerObjectId,
                settings,
                AcquireDistance)
            .ConfigureAwait(false);
        var committed = confirmation?.Action is RadarNavigationAction.Direct or RadarNavigationAction.Ready;
        if (committed)
        {
            state.ObstacleNavigation.CommitDirectApproach(targetServerObjectId);
        }

        LogRadarDirectConfirmation(
            context,
            state,
            phase,
            targetServerObjectId,
            stop,
            committed,
            confirmation?.Reason ?? "map_unavailable");
        return committed;
    }

    private static void LogRadarDirectConfirmation(
        AccountWorkerContext context,
        StationaryCombatState state,
        string phase,
        uint targetServerObjectId,
        PathFollowStopContext stop,
        bool committed,
        string reason)
    {
        LogActionThrottled(
            context,
            state,
            "stationary_combat.radar.direct_confirmation",
            phase + ":" + (committed ? "committed" : "blocked"),
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["targetServerObjectId"] = targetServerObjectId,
                ["stopReason"] = stop.Reason,
                ["arrived"] = stop.Arrived,
                ["committed"] = committed,
                ["reason"] = reason
            },
            TimeSpan.FromMilliseconds(500));
    }

    private async Task<uint> ReadRadarMapIdAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var navigation = state.ObstacleNavigation;
        var now = DateTimeOffset.Now;
        if (navigation.LastObservedMapReadAt != DateTimeOffset.MinValue &&
            now - navigation.LastObservedMapReadAt < TimeSpan.FromSeconds(1))
        {
            return navigation.ObservedMapId;
        }

        var channel = (await context.Snapshots.ReadChannelAsync().ConfigureAwait(false)).Value;
        navigation.LastObservedMapReadAt = now;
        if (channel.MapId == 0)
        {
            navigation.ObservedMapId = 0;
            return 0;
        }

        navigation.ObservedMapId = channel.MapId;
        _radarSnapshots?.PublishMapId(context.Config.AccountName, channel.MapId, now);
        return channel.MapId;
    }

    private static RadarPoint ToRadarPoint(Vector3Snapshot point)
    {
        return new RadarPoint(point.X, point.Y);
    }

    private static Vector3Snapshot ToVector3(RadarPoint point, float z)
    {
        return new Vector3Snapshot((float)point.X, (float)point.Y, z);
    }

    private static void LogRadarNavigation(
        AccountWorkerContext context,
        StationaryCombatState state,
        RadarNavigationDecision navigation,
        string phase)
    {
        LogActionThrottled(
            context,
            state,
            "stationary_combat.radar.navigation",
            phase + ":" + navigation.Action + ":" + navigation.Reason,
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["mapId"] = navigation.MapId,
                ["phase"] = phase,
                ["action"] = navigation.Action.ToString(),
                ["reason"] = navigation.Reason,
                ["destinationX"] = Math.Round(navigation.Destination.X, 2),
                ["destinationY"] = Math.Round(navigation.Destination.Y, 2),
                ["reachDistance"] = Math.Round(navigation.ReachDistanceMeters, 2),
                ["waypointCount"] = navigation.Plan?.WaypointCount ?? 0,
                ["relevantObstacleCount"] = navigation.RelevantObstacleCount,
                ["routeDistance"] = navigation.Plan is null
                    ? null
                    : Math.Round(navigation.Plan.RouteDistance, 2),
                ["elapsedMs"] = navigation.Plan is null
                    ? null
                    : Math.Round(navigation.Plan.Elapsed.TotalMilliseconds, 2)
            },
            TimeSpan.FromMilliseconds(500));
    }

    private static bool IsCandidateStillSelectable(
        WorldObjectSnapshot? candidate,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther,
        StationaryCombatState state)
    {
        return candidate is { Position: not null } target &&
               StationaryCombatTargetSelector.IsSelectableMonster(target) &&
               (allowClaimedByOther || !IsClaimedByOther(target, state)) &&
               (IsTargetingLocalSide(target, state) ||
                StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) <= radius);
    }

    private static bool IsCurrentFightTargetStillSelectable(
        WorldObjectSnapshot? candidate,
        Vector3Snapshot home,
        double radius,
        bool currentTargetIsMaintenanceDefense,
        StationaryCombatState state)
    {
        return candidate is { Position: not null } target &&
               StationaryCombatTargetSelector.IsSelectableMonster(target) &&
               (currentTargetIsMaintenanceDefense ||
                state.CurrentTargetIsRevivePathClear ||
                state.CurrentTargetBypassesHomeLeash ||
                IsTargetingLocalSide(target, state) ||
                StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) <= radius + TargetLeashExtraDistance);
    }

    private static bool AllowsClaimedTargets(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.Combat?.ContestMonster == true;
    }

    private static bool TryKeepClaimedTargetForVerifiedLeaderTacticalMark(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        uint targetingServerObjectId)
    {
        if (!IsCurrentVerifiedLeaderTacticalMarkedTarget(context, state, targetEntityId, targetServerObjectId))
        {
            return false;
        }

        LogActionThrottled(
            context,
            state,
            "stationary_combat.target.claimed_kept_for_verified_tactical_mark",
            "target:" + TargetActionKey(targetEntityId, targetServerObjectId),
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = targetEntityId,
                ["targetServerObjectId"] = targetServerObjectId,
                ["targetingServerObjectId"] = targetingServerObjectId,
                ["targetName"] = targetName
            },
            TimeSpan.FromMilliseconds(500));
        return true;
    }

    private static bool IsCurrentVerifiedLeaderTacticalMarkedTarget(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId)
    {
        var team = context.Config.ScriptSettings?.Team;
        return team?.Role == TeamRole.Leader &&
               team.Leader is { Enabled: true, TacticalMarkEnabled: true } &&
               state.LeaderTacticalMark.Verified &&
               targetServerObjectId != 0 &&
               state.LeaderTacticalMark.TargetServerObjectId == targetServerObjectId &&
               StationaryCombatState.IsSameTarget(
                   state.CurrentTargetEntityId,
                   state.CurrentTargetServerObjectId,
                   targetEntityId,
                   targetServerObjectId);
    }

    private static bool PrefersAggressiveMonsters(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.Combat?.PreferAggressiveMonsters == true;
    }

    private static bool IsRevivePathRecoveryPhase(string recoveryPhase)
    {
        return string.Equals(recoveryPhase, "death_recovery", StringComparison.Ordinal) ||
               string.Equals(recoveryPhase, "startup_recovery", StringComparison.Ordinal);
    }

    private static bool IsDeathRecoveryRevivePathActive(StationaryCombatState state)
    {
        return state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
               state.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.FollowRevivePath;
    }

    private static bool ShouldSuppressSpiritmasterPetSummonForDeathRecoveryDefense(StationaryCombatState state)
    {
        return state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
               state.CurrentTargetIsMaintenanceDefense;
    }

    private static IReadOnlyList<string> GetActiveMonsterNameFilters(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.Combat?.ActiveMonsterNameFilters?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? Array.Empty<string>();
    }

    private static bool IsActiveMonsterFiltered(WorldObjectSnapshot target, IReadOnlyList<string> filters)
    {
        if (filters.Count == 0 || string.IsNullOrWhiteSpace(target.Name))
        {
            return false;
        }

        var targetName = target.Name.Trim();
        return filters.Any(filter => string.Equals(targetName, filter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldReplaceCandidateWithAggressiveTarget(
        bool preferAggressiveMonsters,
        WorldObjectSnapshot? candidate,
        IEnumerable<WorldObjectSnapshot> objects,
        StationaryCombatState state,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther)
    {
        if (!preferAggressiveMonsters || candidate is null || candidate.IsAggressiveToPlayer)
        {
            return false;
        }

        return objects.Any(target =>
            target.IsAggressiveToPlayer &&
            !state.IsTargetIgnored(target) &&
            !state.IsTargetTemporarilyExcluded(target, DateTimeOffset.Now) &&
            !StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                candidate.EntityId,
                candidate.ServerObjectId) &&
            IsCandidateStillSelectable(target, home, radius, allowClaimedByOther, state));
    }

    private static bool IsAttackKeyLoopEnabled(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.SemiAuto?.AttackKeyLoopEnabled == true;
    }

    private static bool IsGatherMaintenanceBlocked(StationaryCombatState state)
    {
        return state.Gather.Active ||
               state.CachedGatherSnapshot?.LocalGathering.IsDialogVisible == true;
    }

    private static bool IsClaimedByOther(WorldObjectSnapshot target, StationaryCombatState state)
    {
        return target.TargetServerObjectId != 0 &&
               target.TargetServerObjectId != target.ServerObjectId &&
               !IsTargetingLocalSide(target, state);
    }

    private static bool IsClaimedByOther(LockedTargetSnapshot target, StationaryCombatState state)
    {
        return target.TargetServerObjectId != 0 &&
               target.TargetServerObjectId != target.ServerObjectId &&
               !IsTargetingLocalSide(target, state);
    }

    private static bool IsTargetingSelf(LockedTargetSnapshot target)
    {
        return target.TargetServerObjectId != 0 &&
               target.ServerObjectId != 0 &&
               target.TargetServerObjectId == target.ServerObjectId;
    }

    private static bool IsTargetingLocalSide(WorldObjectSnapshot target, StationaryCombatState state)
    {
        if (target.IsTargetingLocalPlayer)
        {
            return true;
        }

        return IsLocalSideServerObjectId(target.TargetServerObjectId, state);
    }

    private static bool IsMaintenanceDefenseTarget(WorldObjectSnapshot target, StationaryCombatState state)
    {
        return IsTargetingLocalSide(target, state) ||
               state.IsTeamLeaderProtectionTarget(target);
    }

    private static bool IsTargetingLocalSide(LockedTargetSnapshot target, StationaryCombatState state)
    {
        RememberLocalCombatSide(target, state);
        return IsTargetingLocalPlayerByServerObjectId(target) ||
               IsLocalSideServerObjectId(target.TargetServerObjectId, state);
    }

    private static void RememberLocalCombatSide(LockedTargetSnapshot target, StationaryCombatState state)
    {
        if (target.LocalServerObjectId != 0)
        {
            state.LocalCombatSideServerObjectId = target.LocalServerObjectId;
        }
    }

    private static bool IsLocalSideServerObjectId(uint serverObjectId, StationaryCombatState state)
    {
        return serverObjectId != 0 &&
               ((state.LocalCombatSideServerObjectId != 0 &&
                 serverObjectId == state.LocalCombatSideServerObjectId) ||
                (state.LocalCombatSidePetServerObjectId != 0 &&
                 serverObjectId == state.LocalCombatSidePetServerObjectId));
    }

    private static bool IsTargetingLocalPlayerByServerObjectId(LockedTargetSnapshot target)
    {
        if (target.LocalServerObjectId != 0)
        {
            return target.TargetServerObjectIdMatchesLocal;
        }

        return target.IsTargetingLocalPlayer;
    }

    private async Task PathFollowStepAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot target,
        double reachDistance,
        Func<PathFollowStopContext, Task<bool>>? afterWaypointStopAsync = null)
    {
        var actionPlayer = await ReadPlayerForActionAsync(context).ConfigureAwait(false);
        player = actionPlayer;

        var options = ReadPathFollowTurnOptions(context.Config.ScriptSettings?.Combat);
        var poller = EnsurePathFollowPoller(context, state, player, options);
        SetPathFollowPollTarget(poller, targetIndex: 0, target, reachDistance, options);
        if (TryConsumePathFollowArrival(poller, out var arrivedSnapshot))
        {
            var stoppedForward = state.IsMovingForward;
            await StopMovementAsync(context, state).ConfigureAwait(false);
            LogPathAction(context, state, "arrived_latched", arrivedSnapshot, 0, 0);
            if (afterWaypointStopAsync is not null)
            {
                await afterWaypointStopAsync(
                        new PathFollowStopContext(
                            Arrived: true,
                            StoppedForward: stoppedForward,
                            Reason: "arrived_latched"))
                    .ConfigureAwait(false);
            }

            StopPathFollowPoller(state);
            return;
        }

        if (!TryGetPathFollowPollSnapshot(poller, out var snapshot))
        {
            LogActionThrottled(context, state, "stationary_combat.path_follow", "move_no_yaw", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["action"] = "move_no_yaw",
                ["targetX"] = Math.Round(target.X, 2),
                ["targetY"] = Math.Round(target.Y, 2),
                ["targetZ"] = Math.Round(target.Z, 2),
                ["moving"] = state.IsMovingForward
            }, TimeSpan.FromMilliseconds(500));
        }
        else
        {
            if (snapshot.DistanceToTarget <= reachDistance)
            {
                TryMarkPathFollowArrivedNow(poller, out _, out _);
                var stoppedForward = state.IsMovingForward;
                await StopMovementAsync(context, state).ConfigureAwait(false);
                LogPathAction(context, state, "arrived", snapshot, 0, 0);
                if (afterWaypointStopAsync is not null)
                {
                    await afterWaypointStopAsync(
                            new PathFollowStopContext(
                                Arrived: true,
                                StoppedForward: stoppedForward,
                                Reason: "arrived"))
                        .ConfigureAwait(false);
                }

                StopPathFollowPoller(state);
                return;
            }

            var restartMoveForLargeYaw = ShouldRestartMoveForYaw(state.IsMovingForward, snapshot.YawError, options.RestartYawThresholdDegrees);
            if (restartMoveForLargeYaw)
            {
                var stoppedForward = state.IsMovingForward;
                await StopMovementAsync(context, state).ConfigureAwait(false);
                LogPathAction(context, state, "move_restart_yaw", snapshot, 0, 0);
                if (afterWaypointStopAsync is not null &&
                    await afterWaypointStopAsync(
                            new PathFollowStopContext(
                                Arrived: false,
                                StoppedForward: stoppedForward,
                                Reason: "move_restart_yaw"))
                        .ConfigureAwait(false))
                {
                    StopPathFollowPoller(state);
                    return;
                }
            }

            var activeYawTolerance = state.IsMovingForward
                ? options.MicroYawToleranceDegrees
                : options.YawToleranceDegrees;
            var moveAdjustDisabledByDistance = ShouldDisableMoveAdjustByDistance(
                state.IsMovingForward,
                snapshot.DistanceToTarget,
                options.DisableMoveAdjustDistance);
            var needsTurn = ShouldTurn(
                restartMoveForLargeYaw,
                moveAdjustDisabledByDistance,
                snapshot.YawError,
                snapshot.PitchError,
                activeYawTolerance,
                options.PitchToleranceDegrees);

            LogPathTick(context, state, snapshot, activeYawTolerance, moveAdjustDisabledByDistance, needsTurn);

            if (needsTurn)
            {
                if (restartMoveForLargeYaw)
                {
                    var turn = await DragCameraCombinedTwoPassFixedYawPitchAsync(
                            context,
                            state,
                            target,
                            options,
                            keepRightDown: false,
                            useFaceTargetMouseMove: false,
                            leaveRightDown: true)
                        .ConfigureAwait(false);
                    if (!turn.Success)
                    {
                        return;
                    }
                }
                else if (state.IsMovingForward)
                {
                    await EnsureRightMouseDownAsync(context, state).ConfigureAwait(false);
                    var adjust = await DragPathFollowAngleAdjustAsync(
                            context,
                            state,
                            poller,
                            target,
                            options,
                            activeYawTolerance)
                        .ConfigureAwait(false);
                    LogPathAction(context, state, "move_angle_adjust", adjust.Snapshot ?? snapshot, adjust.MovedDx, adjust.MovedDy);
                    if (adjust.RestartMove)
                    {
                        var turn = await DragCameraCombinedTwoPassFixedYawPitchAsync(
                                context,
                                state,
                                target,
                                options,
                                keepRightDown: false,
                                useFaceTargetMouseMove: false,
                                leaveRightDown: true)
                            .ConfigureAwait(false);
                        if (!turn.Success)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    var turn = await DragCameraCombinedTwoPassFixedYawPitchAsync(
                            context,
                            state,
                            target,
                            options,
                            keepRightDown: false,
                            useFaceTargetMouseMove: true,
                            leaveRightDown: true)
                        .ConfigureAwait(false);
                    if (!turn.Success)
                    {
                        return;
                    }
                }
            }
        }

        await EnsureRightMouseDownAsync(context, state).ConfigureAwait(false);
        var moveStarted = await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        LogActionThrottled(context, state, "stationary_combat.path_follow", "move_forward", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = moveStarted ? "move_start" : "move_hold",
            ["targetX"] = Math.Round(target.X, 2),
            ["targetY"] = Math.Round(target.Y, 2),
            ["targetZ"] = Math.Round(target.Z, 2),
            ["moving"] = state.IsMovingForward
        }, TimeSpan.FromMilliseconds(500));
    }

    private async Task TryJumpCombatApproachIfStuckAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        Vector3Snapshot playerPosition,
        double distanceToTarget,
        string phase)
    {
        if (!state.IsMovingForward)
        {
            state.ResetCombatApproachStuckTracking();
            return;
        }

        var now = DateTimeOffset.Now;
        var minProgressDistance = ReadCombatApproachStuckDistance();
        if (!state.IsCombatApproachStuckTrackingTarget(target.EntityId, target.ServerObjectId) ||
            state.CombatApproachLastProgressPosition is null ||
            state.CombatApproachLastProgressAt == DateTimeOffset.MinValue)
        {
            state.MarkCombatApproachProgress(target.EntityId, target.ServerObjectId, playerPosition, now);
            return;
        }

        var moved = StationaryCombatTargetSelector.HorizontalDistance(
            state.CombatApproachLastProgressPosition.Value,
            playerPosition);
        if (moved >= minProgressDistance)
        {
            state.MarkCombatApproachProgress(target.EntityId, target.ServerObjectId, playerPosition, now);
            return;
        }

        var stuckMs = ReadCombatApproachStuckMs();
        var stuckFor = now - state.CombatApproachLastProgressAt;
        if (stuckFor.TotalMilliseconds < stuckMs)
        {
            return;
        }

        if (state.LastCombatApproachJumpAt != DateTimeOffset.MinValue &&
            (now - state.LastCombatApproachJumpAt).TotalMilliseconds < stuckMs)
        {
            return;
        }

        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);

        var jumpHold = TimeSpan.FromMilliseconds(ReadCombatApproachJumpHoldMs());
        var jumpInterval = TimeSpan.FromMilliseconds(ReadCombatApproachJumpIntervalMs());
        var jumpPressCount = ReadCombatApproachJumpPressCount();
        state.MarkCombatApproachJump(now);
        for (var pressIndex = 0; pressIndex < jumpPressCount; pressIndex++)
        {
            var result = await _input.PressKeyAsync("Space", jumpHold, context.StopToken).ConfigureAwait(false);
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.combat_approach.stuck_jump_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["phase"] = phase,
                    ["targetEntityId"] = target.EntityId,
                    ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name,
                    ["distance"] = Math.Round(distanceToTarget, 2),
                    ["moved"] = Math.Round(moved, 2),
                    ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
                    ["thresholdMs"] = stuckMs,
                    ["progressDistance"] = Math.Round(minProgressDistance, 2),
                    ["pressIndex"] = pressIndex + 1,
                    ["pressCount"] = jumpPressCount,
                    ["error"] = result.Error
                });
                return;
            }

            if (pressIndex + 1 < jumpPressCount)
            {
                await Task.Delay(jumpInterval, context.StopToken).ConfigureAwait(false);
            }
        }

        context.Logger.Info("stationary_combat.combat_approach.stuck_jump", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["phase"] = phase,
            ["targetEntityId"] = target.EntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["distance"] = Math.Round(distanceToTarget, 2),
            ["moved"] = Math.Round(moved, 2),
            ["stuckMs"] = (long)Math.Max(0.0D, stuckFor.TotalMilliseconds),
            ["thresholdMs"] = stuckMs,
            ["progressDistance"] = Math.Round(minProgressDistance, 2),
            ["pressCount"] = jumpPressCount,
            ["intervalMs"] = (long)jumpInterval.TotalMilliseconds,
            ["jumpCount"] = state.CombatApproachJumpCount,
            ["movingForward"] = state.IsMovingForward
        });
    }

    private static bool IsSmartPreAimEnabled(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.CombatMode == AccountCombatMode.Stationary &&
               context.Config.ScriptSettings?.Combat?.SmartPreAimEnabled == true;
    }

    private static bool ShouldPreserveDeathRecoverySmartPreAim(
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (player.IsDead ||
            state.DeathRecovery.Step != StationaryCombatDeathRecoveryStep.FollowRevivePath)
        {
            return false;
        }

        if (state.Fighting || state.LootAfterKill.Active || state.HasSmartPreAimHandoff)
        {
            return true;
        }

        lock (state.NextTargetPreAim.SyncRoot)
        {
            return state.NextTargetPreAim.HasAlignedCandidate;
        }
    }

    private static bool UsesFightTargetPositionForSmartPreAim(AccountWorkerContext context)
    {
        return IsSmartPreAimEnabled(context) &&
               context.Config.ScriptSettings?.Combat?.SmartPreAimUseFightTargetPosition == true;
    }

    private static Vector3Snapshot ResolvePendingNextTargetSelectionOrigin(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition)
    {
        if (!UsesFightTargetPositionForSmartPreAim(context))
        {
            return playerPosition;
        }

        lock (state.NextTargetPreAim.SyncRoot)
        {
            return state.NextTargetPreAim.HasCandidate &&
                   state.NextTargetPreAim.FightTargetPosition is { } fightTargetPosition
                ? fightTargetPosition
                : playerPosition;
        }
    }

    private void EnsureNextTargetPreAimRunning(
        AccountWorkerContext context,
        StationaryCombatState state,
        LockedTargetSnapshot currentTarget,
        PlayerSnapshot player,
        Vector3Snapshot home,
        double radius)
    {
        if (!IsSmartPreAimEnabled(context) ||
            !state.Fighting ||
            !currentTarget.IsMonsterAlive)
        {
            return;
        }

        var preAim = state.NextTargetPreAim;
        CancellationTokenSource linkedCancellation;
        long sessionId;
        lock (preAim.SyncRoot)
        {
            if (preAim.IsWorkerRunning)
            {
                if (StationaryCombatState.IsSameTarget(
                    preAim.FightTargetEntityId,
                    preAim.FightTargetServerObjectId,
                    currentTarget.TargetEntityId,
                    currentTarget.ServerObjectId))
                {
                    return;
                }

                if (!preAim.StopRequested)
                {
                    preAim.StopRequested = true;
                    preAim.LastStopReason = "fight_target_changed";
                    preAim.Cancellation?.Cancel();
                }

                return;
            }

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken);
            sessionId = ++preAim.SessionId;
            preAim.Cancellation = linkedCancellation;
            preAim.StopRequested = false;
            preAim.FightTargetEntityId = currentTarget.TargetEntityId;
            preAim.FightTargetServerObjectId = currentTarget.ServerObjectId;
            preAim.FightTargetPosition = currentTarget.Position;
            preAim.LastStopReason = string.Empty;
        }

        var worker = Task.Run(
            () => RunNextTargetPreAimAsync(
                context,
                state,
                currentTarget.TargetEntityId,
                currentTarget.ServerObjectId,
                home,
                radius,
                sessionId,
                linkedCancellation),
            CancellationToken.None);
        lock (preAim.SyncRoot)
        {
            if (ReferenceEquals(preAim.Cancellation, linkedCancellation))
            {
                preAim.Worker = worker;
            }
        }

        context.Logger.Info("stationary_combat.smart_preaim.started", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["fightTargetEntityId"] = currentTarget.TargetEntityId,
            ["fightTargetServerObjectId"] = currentTarget.ServerObjectId,
            ["fightTargetName"] = currentTarget.Name,
            ["useFightTargetPosition"] = UsesFightTargetPositionForSmartPreAim(context),
            ["radius"] = Math.Round(radius, 2)
        });
    }

    private static void StopNextTargetPreAim(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        bool clearCandidate)
    {
        var preAim = state.NextTargetPreAim;
        bool shouldLog;
        ushort targetEntityId;
        uint targetServerObjectId;
        string targetName;
        bool hadCandidate;
        lock (preAim.SyncRoot)
        {
            preAim.SessionId++;
            hadCandidate = preAim.HasCandidate;
            shouldLog = (preAim.IsWorkerRunning && !preAim.StopRequested) ||
                        (clearCandidate && hadCandidate);
            targetEntityId = preAim.TargetEntityId;
            targetServerObjectId = preAim.TargetServerObjectId;
            targetName = preAim.TargetName;
            if (preAim.Cancellation is not null && !preAim.StopRequested)
            {
                preAim.StopRequested = true;
                preAim.Cancellation.Cancel();
            }

            preAim.FightTargetEntityId = 0;
            preAim.FightTargetServerObjectId = 0;
            preAim.LastStoppedAt = DateTimeOffset.Now;
            preAim.LastStopReason = reason;
            if (clearCandidate)
            {
                preAim.ClearCandidate();
                preAim.FightTargetPosition = null;
            }
        }

        if (!shouldLog)
        {
            return;
        }

        context.Logger.Info("stationary_combat.smart_preaim.stopped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["clearCandidate"] = clearCandidate,
            ["hadCandidate"] = hadCandidate,
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["targetName"] = targetName
        });
    }

    private async Task RunNextTargetPreAimAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        Vector3Snapshot home,
        double radius,
        long sessionId,
        CancellationTokenSource linkedCancellation)
    {
        var token = linkedCancellation.Token;
        var scanInterval = TimeSpan.FromMilliseconds(ReadSmartPreAimWorldScanMs());
        var adjustInterval = TimeSpan.FromMilliseconds(ReadSmartPreAimAdjustMs());
        var nextScanAt = DateTimeOffset.MinValue;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsSmartPreAimLoopStillValid(
                        context,
                        state,
                        fightTargetEntityId,
                        fightTargetServerObjectId,
                        sessionId))
                {
                    break;
                }

                var player = await ReadPlayerAsync(context).ConfigureAwait(false);
                var now = DateTimeOffset.Now;
                if (now >= nextScanAt)
                {
                    await RefreshNextTargetPreAimSelectionAsync(
                            context,
                            state,
                            player.Position!.Value,
                            home,
                            radius,
                            fightTargetEntityId,
                            fightTargetServerObjectId,
                            sessionId)
                        .ConfigureAwait(false);
                    nextScanAt = DateTimeOffset.Now + scanInterval;
                }

                if (TryGetNextTargetPreAimCandidate(
                    state,
                    out var targetEntityId,
                    out var targetServerObjectId,
                    out var targetName,
                    out var targetPosition))
                {
                    await AdjustNextTargetPreAimAsync(
                            context,
                            state,
                            player,
                            targetEntityId,
                            targetServerObjectId,
                            targetName,
                            targetPosition,
                            token)
                        .ConfigureAwait(false);
                }

                await DelayAsync(adjustInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            context.Logger.Warn("stationary_combat.smart_preaim.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["fightTargetEntityId"] = fightTargetEntityId,
                ["fightTargetServerObjectId"] = fightTargetServerObjectId,
                ["error"] = ex.Message
            });
        }
        finally
        {
            await ReleaseNextTargetPreAimRightMouseAsync(context, state).ConfigureAwait(false);
            lock (state.NextTargetPreAim.SyncRoot)
            {
                if (ReferenceEquals(state.NextTargetPreAim.Cancellation, linkedCancellation))
                {
                    state.NextTargetPreAim.Cancellation = null;
                    state.NextTargetPreAim.Worker = null;
                    state.NextTargetPreAim.StopRequested = false;
                    state.NextTargetPreAim.FightTargetEntityId = 0;
                    state.NextTargetPreAim.FightTargetServerObjectId = 0;
                    state.NextTargetPreAim.LastStoppedAt = DateTimeOffset.Now;
                    if (string.IsNullOrWhiteSpace(state.NextTargetPreAim.LastStopReason))
                    {
                        state.NextTargetPreAim.LastStopReason = "completed";
                    }
                }
            }

            linkedCancellation.Dispose();
        }
    }

    private static bool IsSmartPreAimLoopStillValid(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        long? sessionId = null)
    {
        if (!IsSmartPreAimEnabled(context))
        {
            return false;
        }

        var currentFightTargetMatches = state.Fighting &&
            StationaryCombatState.IsSameTarget(
                state.CurrentTargetEntityId,
                state.CurrentTargetServerObjectId,
                fightTargetEntityId,
                fightTargetServerObjectId);
        var lootTargetMatches = state.LootAfterKill.Active &&
            StationaryCombatState.IsSameTarget(
                state.LootAfterKill.KilledTargetEntityId,
                state.LootAfterKill.KilledTargetServerObjectId,
                fightTargetEntityId,
                fightTargetServerObjectId);
        if (!currentFightTargetMatches && !lootTargetMatches)
        {
            return false;
        }

        lock (state.NextTargetPreAim.SyncRoot)
        {
            return !state.NextTargetPreAim.StopRequested &&
                   (!sessionId.HasValue || state.NextTargetPreAim.SessionId == sessionId.Value) &&
                   StationaryCombatState.IsSameTarget(
                       state.NextTargetPreAim.FightTargetEntityId,
                       state.NextTargetPreAim.FightTargetServerObjectId,
                       fightTargetEntityId,
                       fightTargetServerObjectId);
        }
    }

    private async Task RefreshNextTargetPreAimSelectionAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        long sessionId)
    {
        var objects = await ReadWorldObjectsAsync(context).ConfigureAwait(false);

        if (!IsSmartPreAimLoopStillValid(
                context,
                state,
                fightTargetEntityId,
                fightTargetServerObjectId,
                sessionId))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var exclusions = state.CreateNextTargetPreAimExclusionSnapshot(objects, now);
        lock (state.NextTargetPreAim.SyncRoot)
        {
            if (state.NextTargetPreAim.TryGetActiveDisplacedTargetForFightTarget(
                    fightTargetEntityId,
                    fightTargetServerObjectId,
                    out var displacedEntityId,
                    out var displacedServerObjectId))
            {
                exclusions = exclusions.WithIgnoredTarget(
                    displacedEntityId,
                    displacedServerObjectId);
            }
        }

        IReadOnlySet<uint> teamSideServerObjectIds = new HashSet<uint>();
        if (ShouldResolveSmartPreAimTeamSideServerObjectIds(objects, state))
        {
            teamSideServerObjectIds = await ResolveSmartPreAimTeamSideServerObjectIdsAsync(
                    context,
                    state,
                    now)
                .ConfigureAwait(false);
        }

        if (!IsSmartPreAimLoopStillValid(
                context,
                state,
                fightTargetEntityId,
                fightTargetServerObjectId,
                sessionId))
        {
            return;
        }

        NextTargetPreAimSelection? currentSelection;
        bool hasCommittedCandidate;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            currentSelection = state.NextTargetPreAim.CreateCurrentSelection();
            hasCommittedCandidate = state.NextTargetPreAim.HasCameraCommittedCandidate;
        }

        var distanceOrigin = ResolveSmartPreAimDistanceOrigin(
            context,
            state,
            objects,
            playerPosition,
            fightTargetEntityId,
            fightTargetServerObjectId);
        var switchDistanceMargin = ReadSmartPreAimSwitchDistanceMargin();
        var allowClaimedByOther = AllowsClaimedTargets(context);
        var preferAggressiveMonsters = PrefersAggressiveMonsters(context);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        var selection = NextTargetPreAimSelector.Select(
            objects,
            distanceOrigin,
            home,
            radius,
            fightTargetEntityId,
            fightTargetServerObjectId,
            state.LocalCombatSideServerObjectId,
            state.LocalCombatSidePetServerObjectId,
            allowClaimedByOther,
            preferAggressiveMonsters,
            activeMonsterNameFilters,
            currentSelection,
            now,
            TimeSpan.FromMilliseconds(ReadSmartPreAimMinimumHoldMs()),
            switchDistanceMargin,
            exclusions,
            teamSideServerObjectIds);
        selection = KeepCommittedNextTargetPreAimSelectionStable(
            state,
            currentSelection,
            selection,
            hasCommittedCandidate,
            objects,
            playerPosition,
            distanceOrigin,
            switchDistanceMargin);

        var selectionChanged = StoreNextTargetPreAimSelection(
            context,
            state,
            selection,
            objects.Count,
            distanceOrigin,
            now,
            fightTargetEntityId,
            fightTargetServerObjectId,
            sessionId);
        var distanceOriginSource = ResolveSmartPreAimDistanceOriginSource(context, state);
        LogSmartPreAimCandidateDiagnostics(
            context,
            state,
            objects,
            playerPosition,
            home,
            radius,
            distanceOrigin,
            distanceOriginSource,
            fightTargetEntityId,
            fightTargetServerObjectId,
            state.LocalCombatSideServerObjectId,
            state.LocalCombatSidePetServerObjectId,
            allowClaimedByOther,
            preferAggressiveMonsters,
            activeMonsterNameFilters,
            exclusions,
            teamSideServerObjectIds,
            currentSelection,
            selection,
            selectionChanged);
    }

    private static NextTargetPreAimSelection? KeepCommittedNextTargetPreAimSelectionStable(
        StationaryCombatState state,
        NextTargetPreAimSelection? currentSelection,
        NextTargetPreAimSelection? proposedSelection,
        bool hasCommittedCandidate,
        IReadOnlyList<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        Vector3Snapshot distanceOrigin,
        double switchDistanceMargin)
    {
        var preAim = state.NextTargetPreAim;
        lock (preAim.SyncRoot)
        {
            if (!hasCommittedCandidate ||
                currentSelection is null ||
                !preAim.HasCameraCommittedCandidate ||
                !StationaryCombatState.IsSameTarget(
                    preAim.TargetEntityId,
                    preAim.TargetServerObjectId,
                    currentSelection.Target.EntityId,
                    currentSelection.Target.ServerObjectId))
            {
                preAim.ResetPendingSwitchConfirmation();
                return proposedSelection;
            }

            var currentVisibleTarget = objects.FirstOrDefault(target =>
                StationaryCombatState.IsSameTarget(
                    target.EntityId,
                    target.ServerObjectId,
                    currentSelection.Target.EntityId,
                    currentSelection.Target.ServerObjectId) &&
                StationaryCombatTargetSelector.IsSelectableMonster(target));
            if (proposedSelection is not null &&
                StationaryCombatState.IsSameTarget(
                    currentSelection.Target.EntityId,
                    currentSelection.Target.ServerObjectId,
                    proposedSelection.Target.EntityId,
                    proposedSelection.Target.ServerObjectId))
            {
                preAim.ResetPendingSwitchConfirmation();
                return proposedSelection;
            }

            if (proposedSelection is not null &&
                proposedSelection.IsTargetingProtectedSide &&
                (string.Equals(proposedSelection.DecisionReason, "higher_priority", StringComparison.Ordinal) ||
                 string.Equals(proposedSelection.DecisionReason, "current_invalid", StringComparison.Ordinal)))
            {
                preAim.ResetPendingSwitchConfirmation();
                return proposedSelection;
            }

            if (currentVisibleTarget is not null)
            {
                if (proposedSelection is null ||
                    string.Equals(proposedSelection.DecisionReason, "current_invalid", StringComparison.Ordinal))
                {
                    preAim.ResetPendingSwitchConfirmation();
                    return proposedSelection;
                }

                return KeepCommittedNextTargetPreAimSelectionForBetterCandidate(
                    preAim,
                    currentSelection,
                    proposedSelection,
                    currentVisibleTarget,
                    playerPosition,
                    distanceOrigin,
                    switchDistanceMargin);
            }

            preAim.ResetPendingSwitchConfirmation();
            return proposedSelection;
        }
    }

    private static NextTargetPreAimSelection KeepCommittedNextTargetPreAimSelectionForBetterCandidate(
        NextTargetPreAimState preAim,
        NextTargetPreAimSelection currentSelection,
        NextTargetPreAimSelection proposedSelection,
        WorldObjectSnapshot currentVisibleTarget,
        Vector3Snapshot playerPosition,
        Vector3Snapshot distanceOrigin,
        double switchDistanceMargin)
    {
        var currentPosition = currentVisibleTarget.Position;
        var proposedPosition = proposedSelection.Target.Position;
        if (currentPosition is null || proposedPosition is null)
        {
            preAim.ResetPendingSwitchConfirmation();
            return currentSelection with
            {
                Target = currentVisibleTarget,
                DecisionReason = "kept_missing_switch_distance"
            };
        }

        var currentDistanceToPlayer = StationaryCombatTargetSelector.HorizontalDistance(
            currentPosition.Value,
            playerPosition);
        var proposedDistanceToPlayer = StationaryCombatTargetSelector.HorizontalDistance(
            proposedPosition.Value,
            playerPosition);
        var currentDistanceToOrigin = StationaryCombatTargetSelector.HorizontalDistance(
            currentPosition.Value,
            distanceOrigin);
        if (currentDistanceToPlayer - proposedDistanceToPlayer < Math.Max(0.0D, switchDistanceMargin))
        {
            preAim.ResetPendingSwitchConfirmation();
            return currentSelection with
            {
                Target = currentVisibleTarget,
                DistanceToOrigin = currentDistanceToOrigin,
                DecisionReason = "kept_player_distance_stability"
            };
        }

        if (!StationaryCombatState.IsSameTarget(
                preAim.PendingSwitchTargetEntityId,
                preAim.PendingSwitchTargetServerObjectId,
                proposedSelection.Target.EntityId,
                proposedSelection.Target.ServerObjectId))
        {
            preAim.PendingSwitchTargetEntityId = proposedSelection.Target.EntityId;
            preAim.PendingSwitchTargetServerObjectId = proposedSelection.Target.ServerObjectId;
            preAim.ConsecutiveBetterTargetSnapshots = 1;
        }
        else
        {
            preAim.ConsecutiveBetterTargetSnapshots++;
        }

        if (preAim.ConsecutiveBetterTargetSnapshots >= SmartPreAimSwitchConfirmationThreshold)
        {
            var confirmedSnapshots = preAim.ConsecutiveBetterTargetSnapshots;
            preAim.ResetPendingSwitchConfirmation();
            return proposedSelection with
            {
                DecisionReason = $"{proposedSelection.DecisionReason}_confirmed_{confirmedSnapshots}"
            };
        }

        return currentSelection with
        {
            Target = currentVisibleTarget,
            DistanceToOrigin = currentDistanceToOrigin,
            DecisionReason = $"kept_better_candidate_{preAim.ConsecutiveBetterTargetSnapshots}"
        };
    }

    private static async Task<IReadOnlySet<uint>> ResolveSmartPreAimTeamSideServerObjectIdsAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        DateTimeOffset now)
    {
        var preAim = state.NextTargetPreAim;
        lock (preAim.SyncRoot)
        {
            if (preAim.LastTeamSideSnapshotAttemptAt != DateTimeOffset.MinValue &&
                now - preAim.LastTeamSideSnapshotAttemptAt < SmartPreAimTeamSnapshotRefreshInterval)
            {
                return preAim.TeamSideServerObjectIds.ToHashSet();
            }

            preAim.LastTeamSideSnapshotAttemptAt = now;
        }

        var monitor = new TeamMonitor(context.Snapshots);
        var snapshot = await monitor.ReadSnapshotAsync().ConfigureAwait(false);
        lock (preAim.SyncRoot)
        {
            var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
            var protectedIds = TeamLeaderProtectionSelector.CreateProtectedServerObjectIds(
                snapshot,
                team.GroupDistanceMeters);
            preAim.TeamSideServerObjectIds.Clear();
            foreach (var protectedId in protectedIds)
            {
                preAim.TeamSideServerObjectIds.Add(protectedId);
            }

            preAim.TeamSideSnapshotCapturedAt = now;
            return preAim.TeamSideServerObjectIds.ToHashSet();
        }
    }

    private static bool ShouldResolveSmartPreAimTeamSideServerObjectIds(
        IReadOnlyList<WorldObjectSnapshot> objects,
        StationaryCombatState state)
    {
        return objects.Any(target =>
            StationaryCombatTargetSelector.IsSelectableMonster(target) &&
            target.TargetServerObjectId != 0 &&
            target.TargetServerObjectId != target.ServerObjectId &&
            target.TargetServerObjectId != state.LocalCombatSideServerObjectId &&
            target.TargetServerObjectId != state.LocalCombatSidePetServerObjectId);
    }

    private static Vector3Snapshot ResolveSmartPreAimDistanceOrigin(
        AccountWorkerContext context,
        StationaryCombatState state,
        IReadOnlyList<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId)
    {
        if (!UsesFightTargetPositionForSmartPreAim(context))
        {
            return playerPosition;
        }

        var latestFightTargetPosition = objects
            .FirstOrDefault(target => StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                fightTargetEntityId,
                fightTargetServerObjectId))
            ?.Position;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            if (latestFightTargetPosition is not null)
            {
                state.NextTargetPreAim.FightTargetPosition = latestFightTargetPosition;
            }

            return state.NextTargetPreAim.FightTargetPosition ?? playerPosition;
        }
    }

    private static string ResolveSmartPreAimDistanceOriginSource(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (!UsesFightTargetPositionForSmartPreAim(context))
        {
            return "player";
        }

        lock (state.NextTargetPreAim.SyncRoot)
        {
            return state.NextTargetPreAim.FightTargetPosition is null
                ? "player_fallback"
                : "fight_target";
        }
    }

    private static bool StoreNextTargetPreAimSelection(
        AccountWorkerContext context,
        StationaryCombatState state,
        NextTargetPreAimSelection? selection,
        int worldObjectCount,
        Vector3Snapshot distanceOrigin,
        DateTimeOffset now,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        long sessionId)
    {
        var preAim = state.NextTargetPreAim;
        ushort previousEntityId;
        uint previousServerObjectId;
        string previousName;
        bool changed;
        lock (preAim.SyncRoot)
        {
            if (preAim.StopRequested ||
                preAim.SessionId != sessionId ||
                !StationaryCombatState.IsSameTarget(
                    preAim.FightTargetEntityId,
                    preAim.FightTargetServerObjectId,
                    fightTargetEntityId,
                    fightTargetServerObjectId))
            {
                return false;
            }

            previousEntityId = preAim.TargetEntityId;
            previousServerObjectId = preAim.TargetServerObjectId;
            previousName = preAim.TargetName;
            if (selection is null)
            {
                changed = preAim.HasCandidate;
                preAim.ClearCandidate();
                preAim.LastSnapshotAt = now;
            }
            else
            {
                changed = !StationaryCombatState.IsSameTarget(
                    preAim.TargetEntityId,
                    preAim.TargetServerObjectId,
                    selection.Target.EntityId,
                    selection.Target.ServerObjectId);
                if (changed &&
                    preAim.HasCameraCommittedCandidate &&
                    (previousEntityId != 0 || previousServerObjectId != 0))
                {
                    preAim.RecordDisplacedTargetGuard(
                        previousEntityId,
                        previousServerObjectId,
                        selection.Target.EntityId,
                        selection.Target.ServerObjectId);
                }

                preAim.TargetEntityId = selection.Target.EntityId;
                preAim.TargetServerObjectId = selection.Target.ServerObjectId;
                preAim.TargetName = selection.Target.Name;
                preAim.TargetPosition = selection.Target.Position;
                preAim.TargetPriorityTier = selection.PriorityTier;
                preAim.TargetDistanceToOrigin = selection.DistanceToOrigin;
                preAim.TargetingLocalSide = selection.IsTargetingLocalSide;
                preAim.TargetingTeamSide = selection.IsTargetingTeamSide;
                preAim.AggressivePriority = selection.IsAggressivePriority;
                preAim.TargetSelectedAt = changed || preAim.TargetSelectedAt == DateTimeOffset.MinValue
                    ? now
                    : selection.SelectedAt;
                preAim.LastSnapshotAt = now;
                if (changed)
                {
                    preAim.LastAlignedAt = DateTimeOffset.MinValue;
                    preAim.LastAdjustedAt = DateTimeOffset.MinValue;
                    preAim.ResetPendingSwitchConfirmation();
                }
            }
        }

        if (selection is null)
        {
            if (changed)
            {
                context.Logger.Info("stationary_combat.smart_preaim.discarded", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "no_candidate",
                    ["previousTargetEntityId"] = previousEntityId,
                    ["previousTargetServerObjectId"] = previousServerObjectId,
                    ["previousTargetName"] = previousName,
                    ["worldObjectCount"] = worldObjectCount
                });
            }

            return changed;
        }

        if (!changed)
        {
            return false;
        }

        context.Logger.Info(
            previousEntityId == 0 && previousServerObjectId == 0
                ? "stationary_combat.smart_preaim.target_selected"
                : "stationary_combat.smart_preaim.target_switched",
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = selection.Target.EntityId,
                ["targetServerObjectId"] = selection.Target.ServerObjectId,
                ["targetName"] = selection.Target.Name,
                ["previousTargetEntityId"] = previousEntityId,
                ["previousTargetServerObjectId"] = previousServerObjectId,
                ["previousTargetName"] = previousName,
                ["priorityTier"] = selection.PriorityTier,
                ["targetingLocalSide"] = selection.IsTargetingLocalSide,
                ["targetingTeamSide"] = selection.IsTargetingTeamSide,
                ["aggressivePriority"] = selection.IsAggressivePriority,
                ["distanceToOrigin"] = Math.Round(selection.DistanceToOrigin, 2),
                ["distanceOriginX"] = Math.Round(distanceOrigin.X, 2),
                ["distanceOriginY"] = Math.Round(distanceOrigin.Y, 2),
                ["distanceOriginSource"] = ResolveSmartPreAimDistanceOriginSource(context, state),
                ["decisionReason"] = selection.DecisionReason,
                ["worldObjectCount"] = worldObjectCount
            });
        return true;
    }

    private static void LogSmartPreAimCandidateDiagnostics(
        AccountWorkerContext context,
        StationaryCombatState state,
        IReadOnlyList<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        Vector3Snapshot distanceOrigin,
        string distanceOriginSource,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        bool allowClaimedByOther,
        bool preferAggressiveMonsters,
        IReadOnlyList<string> activeMonsterNameFilters,
        NextTargetPreAimExclusionSnapshot exclusions,
        IReadOnlySet<uint> teamSideServerObjectIds,
        NextTargetPreAimSelection? previousSelection,
        NextTargetPreAimSelection? selection,
        bool selectionChanged)
    {
        var diagnostics = objects
            .Select(target => CreateSmartPreAimCandidateDiagnostic(
                target,
                playerPosition,
                home,
                radius,
                distanceOrigin,
                fightTargetEntityId,
                fightTargetServerObjectId,
                localSideServerObjectId,
                localSidePetServerObjectId,
                allowClaimedByOther,
                preferAggressiveMonsters,
                activeMonsterNameFilters,
                exclusions,
                teamSideServerObjectIds))
            .ToArray();
        var eligible = diagnostics
            .Where(candidate => candidate.Eligible)
            .OrderByDescending(candidate => candidate.PriorityTier)
            .ThenBy(candidate => candidate.DistanceToOrigin)
            .ThenBy(candidate => candidate.Target.ServerObjectId)
            .ThenBy(candidate => candidate.Target.EntityId)
            .ToArray();
        var monstersWithPosition = diagnostics
            .Where(candidate => candidate.IsMonster && candidate.HasPosition)
            .ToArray();

        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["selectionChanged"] = selectionChanged,
            ["selectedTargetEntityId"] = selection?.Target.EntityId ?? 0,
            ["selectedTargetServerObjectId"] = selection?.Target.ServerObjectId ?? 0,
            ["selectedTargetName"] = selection?.Target.Name ?? string.Empty,
            ["selectedDecisionReason"] = selection?.DecisionReason ?? "no_candidate",
            ["previousTargetEntityId"] = previousSelection?.Target.EntityId ?? 0,
            ["previousTargetServerObjectId"] = previousSelection?.Target.ServerObjectId ?? 0,
            ["fightTargetEntityId"] = fightTargetEntityId,
            ["fightTargetServerObjectId"] = fightTargetServerObjectId,
            ["distanceOriginX"] = Math.Round(distanceOrigin.X, 2),
            ["distanceOriginY"] = Math.Round(distanceOrigin.Y, 2),
            ["distanceOriginSource"] = distanceOriginSource,
            ["playerX"] = Math.Round(playerPosition.X, 2),
            ["playerY"] = Math.Round(playerPosition.Y, 2),
            ["homeX"] = Math.Round(home.X, 2),
            ["homeY"] = Math.Round(home.Y, 2),
            ["radius"] = Math.Round(radius, 2),
            ["worldObjectCount"] = objects.Count,
            ["monsterWithPositionCount"] = monstersWithPosition.Length,
            ["eligibleCandidateCount"] = eligible.Length,
            ["allowClaimedByOther"] = allowClaimedByOther,
            ["preferAggressiveMonsters"] = preferAggressiveMonsters,
            ["activeMonsterFilters"] = string.Join(",", activeMonsterNameFilters),
            ["reasonCounts"] = FormatSmartPreAimCandidateReasonCounts(diagnostics),
            ["eligibleRanked"] = FormatSmartPreAimCandidateDiagnostics(eligible),
            ["nearestToPlayer"] = FormatSmartPreAimCandidateDiagnostics(
                monstersWithPosition
                    .OrderBy(candidate => candidate.DistanceToPlayer)
                    .ThenBy(candidate => candidate.Target.ServerObjectId)
                    .ThenBy(candidate => candidate.Target.EntityId)),
            ["nearestToOrigin"] = FormatSmartPreAimCandidateDiagnostics(
                monstersWithPosition
                    .OrderBy(candidate => candidate.DistanceToOrigin)
                    .ThenBy(candidate => candidate.Target.ServerObjectId)
                    .ThenBy(candidate => candidate.Target.EntityId))
        };

        if (selectionChanged)
        {
            context.Logger.Info("stationary_combat.smart_preaim.candidate_diagnostics", fields);
            return;
        }

        var selectedActionKey = selection is null
            ? "none"
            : TargetActionKey(selection.Target.EntityId, selection.Target.ServerObjectId);
        LogActionThrottled(
            context,
            state,
            "stationary_combat.smart_preaim.candidate_diagnostics",
            "fight:" + TargetActionKey(fightTargetEntityId, fightTargetServerObjectId) + ":selected:" + selectedActionKey,
            fields,
            TimeSpan.FromSeconds(2));
    }

    private static SmartPreAimCandidateDiagnostic CreateSmartPreAimCandidateDiagnostic(
        WorldObjectSnapshot target,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        Vector3Snapshot distanceOrigin,
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        bool allowClaimedByOther,
        bool preferAggressiveMonsters,
        IReadOnlyList<string> activeMonsterNameFilters,
        NextTargetPreAimExclusionSnapshot exclusions,
        IReadOnlySet<uint> teamSideServerObjectIds)
    {
        var isMonster = string.Equals(target.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase);
        var hasPosition = target.Position is not null;
        var isAlive = target.IsAlive;
        var selectableMonster = isMonster && isAlive && hasPosition;
        var sameFightTarget = StationaryCombatState.IsSameTarget(
            target.EntityId,
            target.ServerObjectId,
            fightTargetEntityId,
            fightTargetServerObjectId);
        var temporaryExcluded = exclusions.IsTemporarilyExcluded(target);
        var targetingLocalSide = IsSmartPreAimDiagnosticTargetingLocalSide(
            target,
            localSideServerObjectId,
            localSidePetServerObjectId);
        var targetingTeamSide = !targetingLocalSide &&
                                IsSmartPreAimDiagnosticTargetingTeamSide(target, teamSideServerObjectIds);
        var ignored = exclusions.IsIgnored(target);
        var nameFiltered = IsActiveMonsterFiltered(target, activeMonsterNameFilters);
        var claimedByOther = IsSmartPreAimDiagnosticClaimedByOther(
            target,
            targetingLocalSide,
            targetingTeamSide);
        var homeDistance = hasPosition
            ? StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, home)
            : double.NaN;
        var playerDistance = hasPosition
            ? StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition)
            : double.NaN;
        var originDistance = hasPosition
            ? StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, distanceOrigin)
            : double.NaN;
        var ordinaryOutsideHome = !targetingLocalSide &&
                                  !targetingTeamSide &&
                                  hasPosition &&
                                  homeDistance > Math.Max(0.0D, radius);
        var aggressivePriority = preferAggressiveMonsters && target.IsAggressiveToPlayer;
        var priorityTier = targetingLocalSide
            ? 4
            : targetingTeamSide
                ? 3
                : aggressivePriority
                    ? 2
                    : 1;

        var reasons = new List<string>();
        if (!isMonster)
        {
            reasons.Add("not_monster");
        }
        else if (!isAlive)
        {
            reasons.Add("dead");
        }

        if (!hasPosition)
        {
            reasons.Add("missing_position");
        }

        if (sameFightTarget)
        {
            reasons.Add("current_target");
        }

        if (temporaryExcluded)
        {
            reasons.Add("temporary_excluded");
        }

        if (!targetingLocalSide && !targetingTeamSide)
        {
            if (ignored)
            {
                reasons.Add("ignored");
            }

            if (nameFiltered)
            {
                reasons.Add("name_filtered");
            }

            if (!allowClaimedByOther && claimedByOther)
            {
                reasons.Add("claimed");
            }

            if (ordinaryOutsideHome)
            {
                reasons.Add("outside_home");
            }
        }

        var eligible = selectableMonster &&
                       !sameFightTarget &&
                       !temporaryExcluded &&
                       (targetingLocalSide ||
                        targetingTeamSide ||
                        (!ignored &&
                         !nameFiltered &&
                         (allowClaimedByOther || !claimedByOther) &&
                         !ordinaryOutsideHome));
        if (eligible)
        {
            reasons.Add("eligible");
        }

        return new SmartPreAimCandidateDiagnostic(
            target,
            isMonster,
            hasPosition,
            eligible,
            priorityTier,
            playerDistance,
            originDistance,
            homeDistance,
            targetingLocalSide,
            targetingTeamSide,
            aggressivePriority,
            string.Join("+", reasons));
    }

    private static bool IsSmartPreAimDiagnosticTargetingLocalSide(
        WorldObjectSnapshot target,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId)
    {
        if (target.IsTargetingLocalPlayer)
        {
            return true;
        }

        return target.TargetServerObjectId != 0 &&
               ((localSideServerObjectId != 0 && target.TargetServerObjectId == localSideServerObjectId) ||
                (localSidePetServerObjectId != 0 && target.TargetServerObjectId == localSidePetServerObjectId));
    }

    private static bool IsSmartPreAimDiagnosticTargetingTeamSide(
        WorldObjectSnapshot target,
        IReadOnlySet<uint> teamSideServerObjectIds)
    {
        return target.TargetServerObjectId != 0 &&
               teamSideServerObjectIds.Contains(target.TargetServerObjectId);
    }

    private static bool IsSmartPreAimDiagnosticClaimedByOther(
        WorldObjectSnapshot target,
        bool targetingLocalSide,
        bool targetingTeamSide)
    {
        return target.TargetServerObjectId != 0 &&
               target.TargetServerObjectId != target.ServerObjectId &&
               !targetingLocalSide &&
               !targetingTeamSide;
    }

    private static string FormatSmartPreAimCandidateDiagnostics(
        IEnumerable<SmartPreAimCandidateDiagnostic> diagnostics)
    {
        return string.Join(
            " | ",
            diagnostics
                .Take(SmartPreAimCandidateDiagnosticSampleCount)
                .Select(FormatSmartPreAimCandidateDiagnostic));
    }

    private static string FormatSmartPreAimCandidateDiagnostic(SmartPreAimCandidateDiagnostic diagnostic)
    {
        var target = diagnostic.Target;
        return string.Join(
            ",",
            target.Name,
            "entity=" + target.EntityId,
            "server=" + target.ServerObjectId,
            "tier=" + diagnostic.PriorityTier,
            "originDist=" + FormatSmartPreAimDistance(diagnostic.DistanceToOrigin),
            "playerDist=" + FormatSmartPreAimDistance(diagnostic.DistanceToPlayer),
            "homeDist=" + FormatSmartPreAimDistance(diagnostic.DistanceToHome),
            "targetServer=" + target.TargetServerObjectId,
            "hp=" + target.CurrentHp + "/" + target.MaxHp,
            "local=" + diagnostic.TargetingLocalSide,
            "team=" + diagnostic.TargetingTeamSide,
            "aggressive=" + diagnostic.AggressivePriority,
            "reason=" + diagnostic.Reasons);
    }

    private static string FormatSmartPreAimCandidateReasonCounts(
        IEnumerable<SmartPreAimCandidateDiagnostic> diagnostics)
    {
        return string.Join(
            ",",
            diagnostics
                .SelectMany(candidate => candidate.Reasons.Split('+', StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key + "=" + group.Count()));
    }

    private static string FormatSmartPreAimDistance(double distance)
    {
        return double.IsNaN(distance)
            ? "na"
            : Math.Round(distance, 2).ToString("0.##");
    }

    private sealed record SmartPreAimCandidateDiagnostic(
        WorldObjectSnapshot Target,
        bool IsMonster,
        bool HasPosition,
        bool Eligible,
        int PriorityTier,
        double DistanceToPlayer,
        double DistanceToOrigin,
        double DistanceToHome,
        bool TargetingLocalSide,
        bool TargetingTeamSide,
        bool AggressivePriority,
        string Reasons);

    private async Task AdjustNextTargetPreAimAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        Vector3Snapshot targetPosition,
        CancellationToken cancellationToken)
    {
        if (state.IsMovingForward ||
            state.PathFollowPoller is not null ||
            ShouldPauseNextTargetPreAimCameraAdjustment(state))
        {
            return;
        }

        var options = ReadPathFollowTurnOptions(context.Config.ScriptSettings?.Combat) with
        {
            ToleranceDegrees = SmartPreAimFaceYawToleranceDegrees,
            YawToleranceDegrees = SmartPreAimFaceYawToleranceDegrees
        };
        var snapshot = BuildCameraTurnSnapshot(player, targetPosition, options);
        if (snapshot is null)
        {
            return;
        }

        var needsTurn = ShouldTurn(
            restartMoveForLargeYaw: false,
            moveAdjustDisabledByDistance: false,
            snapshot.YawError,
            snapshot.PitchError,
            options.YawToleranceDegrees,
            options.PitchToleranceDegrees);
        if (!needsTurn)
        {
            MarkNextTargetPreAimAligned(context, state, targetEntityId, targetServerObjectId, targetName, snapshot);
            return;
        }

        var shouldLog = false;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            shouldLog = state.NextTargetPreAim.LastAdjustedAt == DateTimeOffset.MinValue ||
                        DateTimeOffset.Now - state.NextTargetPreAim.LastAdjustedAt >= TimeSpan.FromSeconds(1);
            state.NextTargetPreAim.LastAdjustedAt = DateTimeOffset.Now;
        }

        var turn = await DragCameraCombinedTwoPassFixedYawPitchAsync(
                context,
                state,
                targetPosition,
                options,
                keepRightDown: false,
                useFaceTargetMouseMove: true,
                leaveRightDown: false,
                cancellationToken: cancellationToken,
                publishRightMouseState: false)
            .ConfigureAwait(false);
        if (turn.Success)
        {
            lock (state.NextTargetPreAim.SyncRoot)
            {
                state.NextTargetPreAim.LastAlignedAt = DateTimeOffset.Now;
            }
        }

        if (shouldLog)
        {
            context.Logger.Info("stationary_combat.smart_preaim.adjusted", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = targetEntityId,
                ["targetServerObjectId"] = targetServerObjectId,
                ["targetName"] = targetName,
                ["yawError"] = Math.Round(snapshot.YawError, 2),
                ["pitchError"] = Math.Round(snapshot.PitchError, 2),
                ["success"] = turn.Success,
                ["finalYawError"] = Math.Round(turn.FinalYawError, 2),
                ["finalPitchError"] = Math.Round(turn.FinalPitchError, 2),
                ["mouseDx"] = turn.TotalDx,
                ["mouseDy"] = turn.TotalDy,
                ["passes"] = turn.Passes,
                ["angleChangeObserved"] = turn.AngleChangeObserved
            });
        }
    }

    private static void MarkNextTargetPreAimAligned(
        AccountWorkerContext context,
        StationaryCombatState state,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        CameraTurnSnapshot snapshot)
    {
        var shouldLog = false;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            shouldLog = state.NextTargetPreAim.LastAlignedAt == DateTimeOffset.MinValue ||
                        DateTimeOffset.Now - state.NextTargetPreAim.LastAlignedAt >= TimeSpan.FromSeconds(2);
            state.NextTargetPreAim.LastAlignedAt = DateTimeOffset.Now;
        }

        if (!shouldLog)
        {
            return;
        }

        context.Logger.Info("stationary_combat.smart_preaim.aligned", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["targetName"] = targetName,
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["pitchError"] = Math.Round(snapshot.PitchError, 2)
        });
    }

    private static bool ShouldPauseNextTargetPreAimCameraAdjustment(StationaryCombatState state)
    {
        return state.BagCleanup.Active ||
               state.CleanupReturnToCombatActive;
    }

    private async Task WaitForNextTargetPreAimCameraIdleAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (!IsSmartPreAimEnabled(context))
        {
            return;
        }

        lock (state.NextTargetPreAim.SyncRoot)
        {
            if (!state.NextTargetPreAim.IsWorkerRunning)
            {
                return;
            }
        }

        await _cameraTurnInputSync.WaitAsync(context.StopToken).ConfigureAwait(false);
        _cameraTurnInputSync.Release();
    }

    private async Task ReleaseNextTargetPreAimRightMouseAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (!state.IsRightMouseDown)
        {
            return;
        }

        var result = await _input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
        state.IsRightMouseDown = false;
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.smart_preaim.mouse_up_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = result.Error
            });
        }
    }

    private static bool TryGetNextTargetPreAimCandidate(
        StationaryCombatState state,
        out ushort targetEntityId,
        out uint targetServerObjectId,
        out string targetName,
        out Vector3Snapshot targetPosition)
    {
        lock (state.NextTargetPreAim.SyncRoot)
        {
            if (!state.NextTargetPreAim.HasCandidate ||
                state.NextTargetPreAim.TargetPosition is not { } position)
            {
                targetEntityId = 0;
                targetServerObjectId = 0;
                targetName = string.Empty;
                targetPosition = default;
                return false;
            }

            targetEntityId = state.NextTargetPreAim.TargetEntityId;
            targetServerObjectId = state.NextTargetPreAim.TargetServerObjectId;
            targetName = state.NextTargetPreAim.TargetName;
            targetPosition = position;
            return true;
        }
    }

    private bool TryConsumeNextTargetPreAim(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot targetPosition,
        WorldObjectSnapshot target)
    {
        if (!IsSmartPreAimEnabled(context))
        {
            StopNextTargetPreAim(context, state, "consume_disabled", clearCandidate: true);
            return false;
        }

        ushort preAimEntityId;
        uint preAimServerObjectId;
        string preAimName;
        DateTimeOffset alignedAt;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            if (!state.NextTargetPreAim.HasAlignedCandidate)
            {
                return false;
            }

            preAimEntityId = state.NextTargetPreAim.TargetEntityId;
            preAimServerObjectId = state.NextTargetPreAim.TargetServerObjectId;
            preAimName = state.NextTargetPreAim.TargetName;
            alignedAt = state.NextTargetPreAim.LastAlignedAt;
        }

        if (!StationaryCombatState.IsSameTarget(
            preAimEntityId,
            preAimServerObjectId,
            target.EntityId,
            target.ServerObjectId))
        {
            StopNextTargetPreAim(context, state, "selected_target_mismatch", clearCandidate: true);
            return false;
        }

        var now = DateTimeOffset.Now;
        if (alignedAt == DateTimeOffset.MinValue ||
            now - alignedAt > ReadSmartPreAimResultTtl())
        {
            StopNextTargetPreAim(context, state, "alignment_stale", clearCandidate: true);
            return false;
        }

        var options = ReadPathFollowTurnOptions(context.Config.ScriptSettings?.Combat) with
        {
            ToleranceDegrees = SmartPreAimFaceYawToleranceDegrees,
            YawToleranceDegrees = SmartPreAimFaceYawToleranceDegrees
        };
        var snapshot = BuildCameraTurnSnapshot(player, targetPosition, options);
        if (snapshot is null ||
            ShouldTurn(
                restartMoveForLargeYaw: false,
                moveAdjustDisabledByDistance: false,
                snapshot.YawError,
                snapshot.PitchError,
                options.YawToleranceDegrees,
                options.PitchToleranceDegrees))
        {
            StopNextTargetPreAim(context, state, "alignment_verify_failed", clearCandidate: true);
            return false;
        }

        bool displacedTargetGuardActivated;
        ushort displacedTargetEntityId;
        uint displacedTargetServerObjectId;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            displacedTargetGuardActivated = state.NextTargetPreAim.ActivateDisplacedTargetGuard(
                target.EntityId,
                target.ServerObjectId);
            displacedTargetEntityId = state.NextTargetPreAim.DisplacedTargetEntityId;
            displacedTargetServerObjectId = state.NextTargetPreAim.DisplacedTargetServerObjectId;
        }

        context.Logger.Info("stationary_combat.smart_preaim.consumed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.EntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["preAimName"] = preAimName,
            ["ageMs"] = (long)Math.Max(0.0D, (now - alignedAt).TotalMilliseconds),
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["pitchError"] = Math.Round(snapshot.PitchError, 2),
            ["displacedTargetGuardActivated"] = displacedTargetGuardActivated,
            ["displacedTargetEntityId"] = displacedTargetEntityId,
            ["displacedTargetServerObjectId"] = displacedTargetServerObjectId
        });
        StopNextTargetPreAim(context, state, "consumed", clearCandidate: true);
        return true;
    }

    private async Task<bool> FaceTargetStepAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot targetPosition,
        WorldObjectSnapshot target)
    {
        return await FaceTargetStepAsync(
                context,
                state,
                player,
                targetPosition,
                target.EntityId,
                target.Name)
            .ConfigureAwait(false);
    }

    private async Task<bool> FaceTargetStepAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot targetPosition,
        ushort targetEntityId,
        string targetName)
    {
        var options = ReadPathFollowTurnOptions(context.Config.ScriptSettings?.Combat) with
        {
            ToleranceDegrees = PreLockFaceYawToleranceDegrees,
            YawToleranceDegrees = PreLockFaceYawToleranceDegrees
        };
        var snapshot = BuildCameraTurnSnapshot(player, targetPosition, options);
        if (snapshot is null)
        {
            await StopMoveForwardAsync(context, state).ConfigureAwait(false);
            LogActionThrottled(context, state, "stationary_combat.face_target", "face_no_yaw", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["action"] = "face_no_yaw",
                ["targetEntityId"] = targetEntityId,
                ["targetName"] = targetName
            }, TimeSpan.FromMilliseconds(500));
            return false;
        }

        await StopMoveForwardAsync(context, state).ConfigureAwait(false);
        var needsTurn = ShouldTurn(
            restartMoveForLargeYaw: false,
            moveAdjustDisabledByDistance: false,
            snapshot.YawError,
            snapshot.PitchError,
            options.YawToleranceDegrees,
            options.PitchToleranceDegrees);
        if (!needsTurn)
        {
            LogActionThrottled(context, state, "stationary_combat.face_target", "face_aligned", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["action"] = "face_aligned",
                ["targetEntityId"] = targetEntityId,
                ["targetName"] = targetName,
                ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
                ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
                ["yawError"] = Math.Round(snapshot.YawError, 2),
                ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
                ["worldPitch"] = Math.Round(snapshot.WorldPitch, 2),
                ["targetPitch"] = Math.Round(snapshot.TargetPitch, 2),
                ["pitchError"] = Math.Round(snapshot.PitchError, 2),
                ["yawTolerance"] = Math.Round(options.YawToleranceDegrees, 2),
                ["pitchTolerance"] = Math.Round(options.PitchToleranceDegrees, 2)
            }, TimeSpan.FromMilliseconds(500));
            return true;
        }

        var turn = await DragCameraCombinedTwoPassFixedYawPitchAsync(
                context,
                state,
                targetPosition,
                options,
                keepRightDown: false,
                useFaceTargetMouseMove: true,
                leaveRightDown: true)
            .ConfigureAwait(false);
        LogActionThrottled(context, state, "stationary_combat.face_target", "face_turn", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = "face_turn",
            ["targetEntityId"] = targetEntityId,
            ["targetName"] = targetName,
            ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
            ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
            ["worldPitch"] = Math.Round(snapshot.WorldPitch, 2),
            ["targetPitch"] = Math.Round(snapshot.TargetPitch, 2),
            ["pitchError"] = Math.Round(snapshot.PitchError, 2),
            ["yawTolerance"] = Math.Round(options.YawToleranceDegrees, 2),
            ["pitchTolerance"] = Math.Round(options.PitchToleranceDegrees, 2),
            ["success"] = turn.Success,
            ["finalYawError"] = Math.Round(turn.FinalYawError, 2),
            ["finalPitchError"] = Math.Round(turn.FinalPitchError, 2),
            ["mouseDx"] = turn.TotalDx,
            ["mouseDy"] = turn.TotalDy,
            ["passes"] = turn.Passes
        }, TimeSpan.FromMilliseconds(500));
        return turn.Success;
    }

    private async Task<bool> EnsureMoveForwardAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (state.IsMovingForward)
        {
            return false;
        }

        var down = await _input.KeyDownAsync("W", context.StopToken).ConfigureAwait(false);
        state.IsMovingForward = down.Success;
        SetPathFollowMoving(state, state.IsMovingForward);
        if (!down.Success)
        {
            context.Logger.Warn("stationary_combat.move_key.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = "W",
                ["error"] = down.Error
            });
        }

        if (down.Success)
        {
            context.Logger.Info("stationary_combat.input.w_down", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
        }

        return down.Success;
    }

    private async Task EnsureRightMouseDownAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        bool cameraInputLockHeld = false)
    {
        if (state.IsRightMouseDown)
        {
            return;
        }

        if (cameraInputLockHeld)
        {
            await EnsureRightMouseDownCoreAsync(context, state).ConfigureAwait(false);
            return;
        }

        await _cameraTurnInputSync.WaitAsync(context.StopToken).ConfigureAwait(false);
        try
        {
            if (!state.IsRightMouseDown)
            {
                await EnsureRightMouseDownCoreAsync(context, state).ConfigureAwait(false);
            }
        }
        finally
        {
            _cameraTurnInputSync.Release();
        }
    }

    private async Task EnsureRightMouseDownCoreAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
        state.IsRightMouseDown = down.Success;
        if (down.Success)
        {
            return;
        }

        context.Logger.Warn("stationary_combat.mouse_down.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["button"] = "Right",
            ["error"] = down.Error
        });
    }

    private async Task StopMoveForwardAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (!state.IsMovingForward)
        {
            return;
        }

        await _input.KeyUpAsync("W", context.StopToken).ConfigureAwait(false);
        state.IsMovingForward = false;
        SetPathFollowMoving(state, false);
        state.ResetCombatApproachStuckTracking();
        context.Logger.Info("stationary_combat.input.w_up", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["source"] = "stop_move_forward"
        });
    }

    private async Task StopMovementAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (state.IsMovingForward)
        {
            await _input.KeyUpAsync("W", context.StopToken).ConfigureAwait(false);
            state.IsMovingForward = false;
            SetPathFollowMoving(state, false);
            state.ResetCombatApproachStuckTracking();
            context.Logger.Info("stationary_combat.input.w_up", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["source"] = "stop_movement"
            });
        }

        if (state.IsRightMouseDown)
        {
            await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
            state.IsRightMouseDown = false;
        }
    }

    private async Task<bool> RecoverRightMouseAtRevivePointAfterTurnFailuresAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        int failureCount)
    {
        var up = await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
        state.IsRightMouseDown = false;
        await DelayAsync(TimeSpan.FromMilliseconds(CameraTurnRecoveryReleaseMs), context).ConfigureAwait(false);

        var (cursorResetX, cursorResetY) = ReadDeathReviveClickPoint(context, reviveClickCount: 0);
        var cursorReset = up.Success
            ? await MoveMouseToAbsoluteScreenPointAsync(context, cursorResetX, cursorResetY).ConfigureAwait(false)
            : OperationResult.Fail("Right mouse release failed; cursor reset skipped.");
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
        state.IsRightMouseDown = down.Success;
        if (down.Success)
        {
            await DelayAsync(TimeSpan.FromMilliseconds(CameraTurnRecoveryWarmupMs), context).ConfigureAwait(false);
        }

        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["consecutiveFailures"] = failureCount,
            ["releaseMs"] = CameraTurnRecoveryReleaseMs,
            ["warmupMs"] = CameraTurnRecoveryWarmupMs,
            ["mouseUpSuccess"] = up.Success,
            ["cursorResetSuccess"] = cursorReset.Success,
            ["cursorResetX"] = cursorResetX,
            ["cursorResetY"] = cursorResetY,
            ["mouseDownSuccess"] = down.Success
        };
        if (up.Success && cursorReset.Success && down.Success)
        {
            context.Logger.Info("stationary_combat.right_mouse.recovered", fields);
        }
        else
        {
            fields["error"] = !up.Success
                ? up.Error
                : !cursorReset.Success
                    ? cursorReset.Error
                    : down.Error;
            context.Logger.Warn("stationary_combat.right_mouse.recovery_failed", fields);
        }

        return down.Success;
    }

    private async Task StopMovementBestEffortAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (state.IsMovingForward)
        {
            var result = await _input.KeyUpAsync("W", CancellationToken.None).ConfigureAwait(false);
            state.IsMovingForward = false;
            SetPathFollowMoving(state, false);
            state.ResetCombatApproachStuckTracking();
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.input.w_up_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["source"] = "stop_movement_best_effort",
                    ["error"] = result.Error
                });
            }
        }

        if (state.IsRightMouseDown)
        {
            var result = await _input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
            state.IsRightMouseDown = false;
            if (!result.Success)
            {
                context.Logger.Warn("stationary_combat.input.right_mouse_up_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["source"] = "stop_movement_best_effort",
                    ["error"] = result.Error
                });
            }
        }
    }

    private async Task<CombinedTurnResult> DragCameraCombinedTwoPassFixedYawPitchAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot target,
        PathFollowTurnOptions options,
        bool keepRightDown,
        bool useFaceTargetMouseMove,
        bool leaveRightDown,
        CancellationToken cancellationToken = default,
        bool publishRightMouseState = true)
    {
        var operationToken = ResolveOperationToken(context, cancellationToken);
        var result = new CombinedTurnResult();
        var lockTaken = false;
        var mouseDownStartedHere = false;
        try
        {
            await _cameraTurnInputSync.WaitAsync(operationToken).ConfigureAwait(false);
            lockTaken = true;

            if (!keepRightDown)
            {
                await _input.MouseUpAsync(RoadhogMouseButton.Right, operationToken).ConfigureAwait(false);
                if (publishRightMouseState)
                {
                    state.IsRightMouseDown = false;
                }

                await DelayAsync(TimeSpan.FromMilliseconds(8), operationToken).ConfigureAwait(false);
                var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, operationToken).ConfigureAwait(false);
                if (publishRightMouseState)
                {
                    state.IsRightMouseDown = down.Success;
                }

                mouseDownStartedHere = down.Success;
                if (!down.Success)
                {
                    context.Logger.Warn("stationary_combat.mouse_down.failed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["button"] = "Right",
                        ["error"] = down.Error
                    });
                    return result;
                }

                await DelayAsync(TimeSpan.FromMilliseconds(options.MouseDownWarmupMs), operationToken).ConfigureAwait(false);
            }
            else
            {
                await EnsureRightMouseDownAsync(context, state, cameraInputLockHeld: true).ConfigureAwait(false);
            }

            for (var pass = 1; pass <= options.TwoPassMaxPasses; pass++)
            {
                result.Passes = pass;
                var snapshot = await ReadStableTurnSnapshotAsync(context, target, options, operationToken).ConfigureAwait(false);
                if (snapshot is null)
                {
                    break;
                }

                result.FinalYawError = snapshot.YawError;
                result.FinalPitchError = snapshot.PitchError;
                if (Math.Abs(snapshot.YawError) <= options.ToleranceDegrees &&
                    Math.Abs(snapshot.PitchError) <= options.PitchToleranceDegrees)
                {
                    result.Success = true;
                    break;
                }

                var beforeYawError = snapshot.YawError;
                var beforePitchError = snapshot.PitchError;
                var dx = 0;
                var dy = 0;
                double rawDx = 0.0D;
                double rawDy = 0.0D;
                var minXApplied = false;
                var minYApplied = false;
                if (useFaceTargetMouseMove)
                {
                    dx = CalculateCameraDragDx(
                        snapshot.YawError,
                        options.PixelsPerDegreeAbs,
                        options,
                        applyMinCorrection: false,
                        out rawDx,
                        out minXApplied);
                    dy = CalculateCameraDragDy(
                        snapshot.PitchError,
                        options.PitchPixelsPerDegreeAbs,
                        options,
                        applyMinCorrection: false,
                        out rawDy,
                        out minYApplied);
                }
                else
                {
                    if (Math.Abs(snapshot.YawError) > options.ToleranceDegrees)
                    {
                        dx = CalculateCameraDragDx(
                            snapshot.YawError,
                            options.PixelsPerDegreeAbs,
                            options,
                            applyMinCorrection: true,
                            out rawDx,
                            out minXApplied);
                    }

                    if (Math.Abs(snapshot.PitchError) > options.ToleranceDegrees)
                    {
                        dy = CalculateCameraDragDy(
                            snapshot.PitchError,
                            options.PitchPixelsPerDegreeAbs,
                            options,
                            applyMinCorrection: true,
                            out rawDy,
                            out minYApplied);
                    }
                }

                context.Logger.Info("stationary_combat.path_follow.two_pass", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pass"] = pass,
                    ["cameraYaw"] = Math.Round(snapshot.CurrentYaw, 2),
                    ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
                    ["yawError"] = Math.Round(snapshot.YawError, 2),
                    ["cameraPitch"] = Math.Round(snapshot.CurrentPitch, 2),
                    ["worldPitch"] = Math.Round(snapshot.WorldPitch, 2),
                    ["targetPitch"] = Math.Round(snapshot.TargetPitch, 2),
                    ["pitchError"] = Math.Round(snapshot.PitchError, 2),
                    ["rawDx"] = Math.Round(rawDx, 2),
                    ["rawDy"] = Math.Round(rawDy, 2),
                    ["dx"] = dx,
                    ["dy"] = dy,
                    ["moveCommands"] = EstimateCombinedChunkDragMoveCommandCount(dx, dy, options),
                    ["maxChunkPx"] = options.DragStepPixels,
                    ["primeTail"] = options.DragPrimePixels + "/" + options.DragTailPixels,
                    ["chunkMode"] = FormatCameraDragChunkMode(options.DragChunkMode),
                    ["moveLogic"] = useFaceTargetMouseMove ? "face_target" : "fixed",
                    ["minApplied"] = minXApplied || minYApplied
                });

                await DragCameraCombinedChunksAsync(
                        context,
                        dx,
                        dy,
                        options,
                        operationToken,
                        cameraInputLockHeld: true)
                    .ConfigureAwait(false);
                result.MouseMoveAttempted |= dx != 0 || dy != 0;
                result.TotalDx += dx;
                result.TotalDy += dy;
                await DelayAsync(TimeSpan.FromMilliseconds(options.MouseHoldAfterMoveMs), operationToken).ConfigureAwait(false);

                var afterSnapshot = await WaitForCameraAnglesChangeAsync(
                        context,
                        target,
                        snapshot.CurrentYaw,
                        snapshot.CurrentPitch,
                        options,
                        operationToken)
                    .ConfigureAwait(false);
                if (afterSnapshot is null)
                {
                    break;
                }

                result.AngleChangeObserved = true;
                result.FinalYawError = afterSnapshot.YawError;
                result.FinalPitchError = afterSnapshot.PitchError;
                var verification = VerifyCameraTurn(
                    beforeYawError,
                    beforePitchError,
                    afterSnapshot.YawError,
                    afterSnapshot.PitchError);
                context.Logger.Info("stationary_combat.path_follow.two_pass_result", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pass"] = pass,
                    ["cameraYaw"] = Math.Round(afterSnapshot.CurrentYaw, 2),
                    ["cameraPitch"] = Math.Round(afterSnapshot.CurrentPitch, 2),
                    ["yawError"] = Math.Round(afterSnapshot.YawError, 2),
                    ["pitchError"] = Math.Round(afterSnapshot.PitchError, 2),
                    ["yawImproved"] = verification.YawImproved,
                    ["pitchImproved"] = verification.PitchImproved,
                    ["yawOvershot"] = verification.YawOvershot,
                    ["pitchOvershot"] = verification.PitchOvershot,
                    ["anyImproved"] = verification.AnyImproved
                });

                if (Math.Abs(afterSnapshot.YawError) <= options.ToleranceDegrees &&
                    Math.Abs(afterSnapshot.PitchError) <= options.PitchToleranceDegrees)
                {
                    result.Success = true;
                    break;
                }

                if (!verification.AnyImproved)
                {
                    break;
                }
            }

            if (publishRightMouseState)
            {
                if (result.Success || result.AngleChangeObserved)
                {
                    state.ResetCameraTurnNoChange();
                }
                else if (result.MouseMoveAttempted)
                {
                    var failureCount = state.MarkCameraTurnNoChange();
                    if (failureCount >= CameraTurnRecoveryFailureThreshold)
                    {
                        mouseDownStartedHere = await RecoverRightMouseAtRevivePointAfterTurnFailuresAsync(
                                context,
                                state,
                                failureCount)
                            .ConfigureAwait(false);
                        state.ResetCameraTurnNoChange();
                    }
                }
            }
        }
        finally
        {
            if (lockTaken)
            {
                try
                {
                    if (mouseDownStartedHere && !keepRightDown && !leaveRightDown)
                    {
                        await _input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
                        if (publishRightMouseState)
                        {
                            state.IsRightMouseDown = false;
                        }
                    }
                    else if (mouseDownStartedHere && leaveRightDown)
                    {
                        if (publishRightMouseState)
                        {
                            state.IsRightMouseDown = true;
                        }
                    }

                    await DelayAsync(TimeSpan.FromMilliseconds(options.DurationMs), operationToken).ConfigureAwait(false);
                }
                finally
                {
                    _cameraTurnInputSync.Release();
                }
            }
        }

        return result;
    }

    private async Task<PathFollowAdjustResult> DragPathFollowAngleAdjustAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PathFollowPollState poller,
        Vector3Snapshot target,
        PathFollowTurnOptions options,
        double yawTolerance)
    {
        var result = new PathFollowAdjustResult();
        if (!TryGetPathFollowPollSnapshot(poller, out var snapshot))
        {
            return result;
        }

        result.Snapshot = snapshot;
        if (ShouldRestartMoveForYaw(true, snapshot.YawError, options.RestartYawThresholdDegrees))
        {
            result.RestartMove = true;
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return result;
        }

        if (Math.Abs(snapshot.YawError) <= yawTolerance &&
            Math.Abs(snapshot.PitchError) <= options.PitchToleranceDegrees)
        {
            result.Aligned = true;
            return result;
        }

        var correctionYawError = Math.Abs(snapshot.YawError) > yawTolerance ? snapshot.YawError / 2.0D : 0.0D;
        var correctionPitchError = Math.Abs(snapshot.PitchError) > options.PitchToleranceDegrees ? snapshot.PitchError / 2.0D : 0.0D;
        var plannedDx = correctionYawError == 0.0D
            ? 0
            : CalculateCameraDragDx(
                correctionYawError,
                options.PixelsPerDegreeAbs,
                options,
                applyMinCorrection: false,
                out _,
                out _);
        var plannedDy = correctionPitchError == 0.0D
            ? 0
            : CalculateCameraDragDy(
                correctionPitchError,
                options.PitchPixelsPerDegreeAbs,
                options,
                applyMinCorrection: false,
                out _,
                out _);

        var remainingX = Math.Abs(plannedDx);
        var remainingY = Math.Abs(plannedDy);
        var pollWait = TimeSpan.FromMilliseconds(options.AngleAdjustPollWaitMs);
        while (remainingX > 0 || remainingY > 0)
        {
            if (IsPathFollowStopPending(poller, out _))
            {
                break;
            }

            if (!TryGetPathFollowPollSnapshot(poller, out snapshot))
            {
                break;
            }

            result.Snapshot = snapshot;
            if (ShouldRestartMoveForYaw(true, snapshot.YawError, options.RestartYawThresholdDegrees))
            {
                result.RestartMove = true;
                await StopMovementAsync(context, state).ConfigureAwait(false);
                break;
            }

            if (Math.Abs(snapshot.YawError) <= yawTolerance &&
                Math.Abs(snapshot.PitchError) <= options.PitchToleranceDegrees)
            {
                result.Aligned = true;
                break;
            }

            var movedOnePixel = false;
            if (remainingX > 0 && Math.Abs(snapshot.YawError) > yawTolerance)
            {
                var currentDx = CalculateCameraDragDx(
                    snapshot.YawError,
                    options.PixelsPerDegreeAbs,
                    options,
                    applyMinCorrection: false,
                    out _,
                    out _);
                var stepX = currentDx < 0 ? -1 : 1;
                var previousReadCount = snapshot.ReadCount;
                await SendCameraCombinedMoveStepAsync(context, stepX, 0, options).ConfigureAwait(false);
                result.MovedDx += stepX;
                remainingX--;
                movedOnePixel = true;
                TryMarkPathFollowArrivedNow(poller, out _, out _);
                await TryWaitForPathFollowPollSnapshotAsync(poller, previousReadCount, pollWait, context).ConfigureAwait(false);
            }

            if (!TryGetPathFollowPollSnapshot(poller, out snapshot))
            {
                break;
            }

            result.Snapshot = snapshot;
            if (ShouldRestartMoveForYaw(true, snapshot.YawError, options.RestartYawThresholdDegrees))
            {
                result.RestartMove = true;
                await StopMovementAsync(context, state).ConfigureAwait(false);
                break;
            }

            if (Math.Abs(snapshot.YawError) <= yawTolerance &&
                Math.Abs(snapshot.PitchError) <= options.PitchToleranceDegrees)
            {
                result.Aligned = true;
                break;
            }

            if (remainingY > 0 && Math.Abs(snapshot.PitchError) > options.PitchToleranceDegrees)
            {
                var currentDy = CalculateCameraDragDy(
                    snapshot.PitchError,
                    options.PitchPixelsPerDegreeAbs,
                    options,
                    applyMinCorrection: false,
                    out _,
                    out _);
                var stepY = currentDy < 0 ? -1 : 1;
                var previousReadCount = snapshot.ReadCount;
                await SendCameraCombinedMoveStepAsync(context, 0, stepY, options).ConfigureAwait(false);
                result.MovedDy += stepY;
                remainingY--;
                movedOnePixel = true;
                TryMarkPathFollowArrivedNow(poller, out _, out _);
                await TryWaitForPathFollowPollSnapshotAsync(poller, previousReadCount, pollWait, context).ConfigureAwait(false);
            }

            if (!movedOnePixel)
            {
                break;
            }
        }

        if (!result.Aligned && result.Snapshot is not null)
        {
            result.Aligned =
                Math.Abs(result.Snapshot.YawError) <= yawTolerance &&
                Math.Abs(result.Snapshot.PitchError) <= options.PitchToleranceDegrees;
        }

        return result;
    }

    private async Task DragCameraCombinedChunksAsync(
        AccountWorkerContext context,
        int dx,
        int dy,
        PathFollowTurnOptions options,
        CancellationToken cancellationToken = default,
        bool cameraInputLockHeld = false)
    {
        var operationToken = ResolveOperationToken(context, cancellationToken);
        var xChunks = BuildSignedCameraChunks(dx, options);
        var yChunks = BuildSignedCameraChunks(dy, options);
        var count = Math.Max(xChunks.Length, yChunks.Length);
        var primeMoveCommands = EstimateCombinedPrimeMoveCommandCount(dx, dy, options);
        var stepDelay = TimeSpan.FromMilliseconds(options.DragStepDelayMs);
        for (var i = 0; i < count; i++)
        {
            var stepX = i < xChunks.Length ? xChunks[i] : 0;
            var stepY = i < yChunks.Length ? yChunks[i] : 0;
            await SendCameraCombinedMoveStepAsync(
                    context,
                    stepX,
                    stepY,
                    options,
                    operationToken,
                    cameraInputLockHeld)
                .ConfigureAwait(false);
            if (i >= primeMoveCommands)
            {
                await DelayAsync(stepDelay, operationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendCameraCombinedMoveStepAsync(
        AccountWorkerContext context,
        int dx,
        int dy,
        PathFollowTurnOptions options,
        CancellationToken cancellationToken = default,
        bool cameraInputLockHeld = false)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var operationToken = ResolveOperationToken(context, cancellationToken);
        if (cameraInputLockHeld)
        {
            await SendCameraCombinedMoveStepCoreAsync(context, dx, dy, operationToken).ConfigureAwait(false);
            return;
        }

        await _cameraTurnInputSync.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            await SendCameraCombinedMoveStepCoreAsync(context, dx, dy, operationToken).ConfigureAwait(false);
        }
        finally
        {
            _cameraTurnInputSync.Release();
        }
    }

    private async Task SendCameraCombinedMoveStepCoreAsync(
        AccountWorkerContext context,
        int dx,
        int dy,
        CancellationToken cancellationToken)
    {
        var move = await _input.MoveMouseRelativeAsync(dx, dy, cancellationToken).ConfigureAwait(false);
        if (!move.Success)
        {
            context.Logger.Warn("stationary_combat.mouse_move.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = move.Error
            });
            return;
        }
    }

    private static async Task<PlayerSnapshot> ReadPlayerAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadPlayerAsync().ConfigureAwait(false)).Value;

    private static async Task<PlayerSnapshot> ReadPlayerForActionAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadPlayerAsync().ConfigureAwait(false)).Value;

    private static async Task RefreshLocalCombatSideAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        if (!plan.UsesSpiritmasterAutoLogic ||
            player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.LocalCombatSidePetServerObjectId = 0;
            return;
        }

        state.LocalCombatSideServerObjectId = 0;
        state.LocalCombatSidePetServerObjectId = 0;

        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        state.LocalCombatSideServerObjectId = roster.LocalServerObjectId;
        var localPet = roster.LocalPlayerPet;
        state.LocalCombatSidePetServerObjectId = localPet?.Pet is { IsSummoned: true, IsAlive: true } pet
            ? pet.ServerObjectId
            : 0;
    }

    private static async Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadSummonedPetRosterAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetSnapshot> ReadLockedTargetAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetSnapshot> ReadLockedTargetForActionAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;

    private static async Task<IReadOnlyList<WorldObjectSnapshot>> ReadWorldObjectsAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadWorldObjectsAsync().ConfigureAwait(false)).Value;

    private static async Task<GatherSnapshot> ReadGatherSnapshotAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadGatherSnapshotAsync().ConfigureAwait(false)).Value;

    private static async Task<IReadOnlyList<LootCorpseSnapshot>> ReadLootCorpsesAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLootCorpsesAsync().ConfigureAwait(false)).Value;

    private async Task<CameraTurnSnapshot?> ReadTurnSnapshotAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        PathFollowTurnOptions options)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        return BuildCameraTurnSnapshot(player, target, options);
    }

    private async Task<CameraTurnSnapshot?> ReadStableTurnSnapshotAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        PathFollowTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        var operationToken = ResolveOperationToken(context, cancellationToken);
        await DelayAsync(TimeSpan.FromMilliseconds(options.SettleMs), operationToken).ConfigureAwait(false);
        return await ReadTurnSnapshotAsync(context, target, options).ConfigureAwait(false);
    }

    private async Task<CameraTurnSnapshot?> WaitForCameraAnglesChangeAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        double previousYaw,
        double previousPitch,
        PathFollowTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        var operationToken = ResolveOperationToken(context, cancellationToken);
        await DelayAsync(TimeSpan.FromMilliseconds(options.AdaptiveReadSettleMs), operationToken).ConfigureAwait(false);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, options.AdaptiveReadTimeoutMs));
        if (timeout <= TimeSpan.Zero)
        {
            var immediate = await ReadChangedCameraAnglesAsync(context, target, previousYaw, previousPitch, options).ConfigureAwait(false);
            return immediate is null
                ? null
                : await WaitForCameraAnglesStableAsync(context, target, options, immediate, operationToken).ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline && !operationToken.IsCancellationRequested)
        {
            var observed = await ReadChangedCameraAnglesAsync(context, target, previousYaw, previousPitch, options).ConfigureAwait(false);
            if (observed is not null)
            {
                return await WaitForCameraAnglesStableAsync(context, target, options, observed, operationToken).ConfigureAwait(false);
            }

            await DelayAsync(TimeSpan.FromMilliseconds(10), operationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<CameraTurnSnapshot?> ReadChangedCameraAnglesAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        double previousYaw,
        double previousPitch,
        PathFollowTurnOptions options)
    {
        var snapshot = await ReadTurnSnapshotAsync(context, target, options).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var yawDelta = Math.Abs(NormalizeSignedDegrees(snapshot.CurrentYaw - previousYaw));
        var pitchDelta = Math.Abs(snapshot.CurrentPitch - previousPitch);
        return yawDelta >= options.AdaptiveMinYawDeltaDegrees ||
               pitchDelta >= options.AdaptiveMinYawDeltaDegrees
            ? snapshot
            : null;
    }

    private async Task<CameraTurnSnapshot> WaitForCameraAnglesStableAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        PathFollowTurnOptions options,
        CameraTurnSnapshot stableSnapshot,
        CancellationToken cancellationToken = default)
    {
        var operationToken = ResolveOperationToken(context, cancellationToken);
        var stableMs = Math.Max(0, options.AdaptiveStableMs);
        var timeoutMs = Math.Max(stableMs, options.AdaptiveStableTimeoutMs);
        if (stableMs <= 0 || timeoutMs <= 0)
        {
            return stableSnapshot;
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        var stableSince = DateTimeOffset.UtcNow;
        var currentStable = stableSnapshot;
        while (DateTimeOffset.UtcNow <= deadline && !operationToken.IsCancellationRequested)
        {
            var snapshot = await ReadTurnSnapshotAsync(context, target, options).ConfigureAwait(false);
            if (snapshot is not null)
            {
                var yawDelta = Math.Abs(NormalizeSignedDegrees(snapshot.CurrentYaw - currentStable.CurrentYaw));
                var pitchDelta = Math.Abs(snapshot.CurrentPitch - currentStable.CurrentPitch);
                if (yawDelta >= options.AdaptiveMinYawDeltaDegrees ||
                    pitchDelta >= options.AdaptiveMinYawDeltaDegrees)
                {
                    currentStable = snapshot;
                    stableSince = DateTimeOffset.UtcNow;
                }
                else if ((DateTimeOffset.UtcNow - stableSince).TotalMilliseconds >= stableMs)
                {
                    return currentStable;
                }
            }

            await DelayAsync(TimeSpan.FromMilliseconds(10), operationToken).ConfigureAwait(false);
        }

        return currentStable;
    }

    private PathFollowPollState EnsurePathFollowPoller(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot initialPlayer,
        PathFollowTurnOptions options)
    {
        if (state.PathFollowPoller is PathFollowPollState existing &&
            !existing.StopRequested &&
            !existing.Cancellation.IsCancellationRequested &&
            string.Equals(existing.AccountName, context.Config.AccountName, StringComparison.Ordinal) &&
            existing.ProcessId == context.Config.ProcessId)
        {
            lock (existing.SyncRoot)
            {
                existing.Options = options;
                existing.TargetPitch = options.TargetPitchDegrees;
                existing.Local = initialPlayer;
                existing.LastReadTime = DateTimeOffset.Now;
                existing.ReadCount++;
                UpdatePathFollowPollMetricsLocked(existing);
            }

            return existing;
        }

        if (state.PathFollowPoller is PathFollowPollState old)
        {
            StopPathFollowPoller(old);
        }

        var poller = new PathFollowPollState
        {
            AccountName = context.Config.AccountName,
            ProcessId = context.Config.ProcessId,
            Options = options,
            TargetPitch = options.TargetPitchDegrees,
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken)
        };
        lock (poller.SyncRoot)
        {
            poller.Local = initialPlayer;
            poller.LastReadTime = DateTimeOffset.Now;
            UpdatePathFollowPollMetricsLocked(poller);
        }

        poller.Task = Task.Run(() => PathFollowPollLoopAsync(context, poller), CancellationToken.None);
        state.PathFollowPoller = poller;
        return poller;
    }

    private async Task PathFollowPollLoopAsync(AccountWorkerContext context, PathFollowPollState poller)
    {
        var interval = TimeSpan.FromMilliseconds(ReadPathFollowTickMs());
        while (!poller.Cancellation.IsCancellationRequested)
        {
            var player = await ReadPlayerForActionAsync(context).ConfigureAwait(false);
            lock (poller.SyncRoot)
            {
                if (poller.StopRequested)
                {
                    return;
                }

                poller.Local = player;
                poller.LastReadTime = DateTimeOffset.Now;
                poller.ReadCount++;
                UpdatePathFollowPollMetricsLocked(poller);
                if (poller.TargetIndex >= 0 &&
                    !poller.HasArrived &&
                    poller.HasMetrics &&
                    poller.MetricsSnapshot is not null &&
                    poller.MetricsSnapshot.DistanceToTarget <= poller.ReachDistance)
                {
                    poller.HasArrived = true;
                    poller.ArrivedTargetIndex = poller.TargetIndex;
                    poller.ArrivedSnapshot = poller.MetricsSnapshot;
                }
            }

            try
            {
                await Task.Delay(interval, poller.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static void SetPathFollowPollTarget(
        PathFollowPollState poller,
        int targetIndex,
        Vector3Snapshot target,
        double reachDistance,
        PathFollowTurnOptions options)
    {
        lock (poller.SyncRoot)
        {
            if (poller.TargetIndex != targetIndex ||
                !SamePoint(poller.TargetPoint, target))
            {
                poller.HasArrived = false;
                poller.ArrivedTargetIndex = -1;
                poller.ArrivedSnapshot = null;
                poller.HasMetrics = false;
            }

            poller.TargetIndex = targetIndex;
            poller.TargetPoint = target;
            poller.ReachDistance = Math.Max(0.0D, reachDistance);
            poller.Options = options;
            poller.TargetPitch = options.TargetPitchDegrees;
            UpdatePathFollowPollMetricsLocked(poller);
        }
    }

    private static void UpdatePathFollowPollMetricsLocked(PathFollowPollState poller)
    {
        if (poller.TargetIndex < 0)
        {
            poller.HasMetrics = false;
            poller.MetricsSnapshot = null;
            return;
        }

        var snapshot = BuildCameraTurnSnapshot(poller.Local, poller.TargetPoint, poller.Options);
        if (snapshot is null)
        {
            poller.HasMetrics = false;
            poller.MetricsSnapshot = null;
            return;
        }

        poller.MetricsSnapshot = snapshot with
        {
            ReadCount = poller.ReadCount,
            Age = TimeSpan.Zero
        };
        poller.HasMetrics = true;
    }

    private static bool TryGetPathFollowPollSnapshot(
        PathFollowPollState poller,
        out CameraTurnSnapshot snapshot)
    {
        lock (poller.SyncRoot)
        {
            if (!poller.HasMetrics || poller.MetricsSnapshot is null)
            {
                snapshot = default!;
                return false;
            }

            snapshot = poller.MetricsSnapshot with
            {
                Age = DateTimeOffset.Now - poller.LastReadTime
            };
            return true;
        }
    }

    private static bool TryConsumePathFollowArrival(
        PathFollowPollState poller,
        out CameraTurnSnapshot snapshot)
    {
        lock (poller.SyncRoot)
        {
            if (!poller.HasArrived ||
                poller.ArrivedTargetIndex != poller.TargetIndex ||
                poller.ArrivedSnapshot is null)
            {
                snapshot = default!;
                return false;
            }

            snapshot = poller.ArrivedSnapshot with
            {
                Age = DateTimeOffset.Now - poller.LastReadTime
            };
            poller.HasArrived = false;
            poller.ArrivedTargetIndex = -1;
            poller.ArrivedSnapshot = null;
            return true;
        }
    }

    private static async Task<bool> TryWaitForPathFollowPollSnapshotAsync(
        PathFollowPollState poller,
        long previousReadCount,
        TimeSpan timeout,
        AccountWorkerContext context)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (TryGetPathFollowPollSnapshot(poller, out var snapshot) &&
                (snapshot.ReadCount != previousReadCount || timeout <= TimeSpan.Zero))
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await DelayAsync(TimeSpan.FromMilliseconds(1), context).ConfigureAwait(false);
        }
        while (!context.StopToken.IsCancellationRequested);

        return TryGetPathFollowPollSnapshot(poller, out _);
    }

    private static bool TryMarkPathFollowArrivedNow(
        PathFollowPollState poller,
        out int arrivedTargetIndex,
        out double arrivedDistance)
    {
        lock (poller.SyncRoot)
        {
            if (poller.TargetIndex >= 0 &&
                poller.HasMetrics &&
                poller.MetricsSnapshot is not null &&
                poller.MetricsSnapshot.DistanceToTarget <= poller.ReachDistance)
            {
                poller.HasArrived = true;
                poller.ArrivedTargetIndex = poller.TargetIndex;
                poller.ArrivedSnapshot = poller.MetricsSnapshot;
                arrivedTargetIndex = poller.TargetIndex;
                arrivedDistance = poller.MetricsSnapshot.DistanceToTarget;
                return true;
            }
        }

        arrivedTargetIndex = -1;
        arrivedDistance = 0.0D;
        return false;
    }

    private static bool IsPathFollowStopPending(PathFollowPollState poller, out string? reason)
    {
        lock (poller.SyncRoot)
        {
            if (poller.StopRequested)
            {
                reason = "poller_stop_requested";
                return true;
            }
        }

        reason = null;
        return false;
    }

    private static void SetPathFollowMoving(StationaryCombatState state, bool moving)
    {
        if (state.PathFollowPoller is not PathFollowPollState poller)
        {
            return;
        }

        lock (poller.SyncRoot)
        {
            poller.IsMoving = moving;
        }
    }

    private static void StopPathFollowPoller(PathFollowPollState poller)
    {
        lock (poller.SyncRoot)
        {
            poller.StopRequested = true;
        }

        poller.Cancellation.Cancel();
    }

    private static void StopPathFollowPoller(StationaryCombatState state)
    {
        if (state.PathFollowPoller is PathFollowPollState poller)
        {
            StopPathFollowPoller(poller);
            state.PathFollowPoller = null;
        }
    }

    private static bool SamePoint(Vector3Snapshot left, Vector3Snapshot right)
    {
        return Math.Abs(left.X - right.X) < 0.001D &&
               Math.Abs(left.Y - right.Y) < 0.001D &&
               Math.Abs(left.Z - right.Z) < 0.001D;
    }

    private static CameraTurnSnapshot? BuildCameraTurnSnapshot(
        PlayerSnapshot player,
        Vector3Snapshot target,
        PathFollowTurnOptions options)
    {
        var currentYaw = player.CameraYawDegrees ?? player.ActorYawDegrees;
        if (currentYaw is null)
        {
            return null;
        }

        var worldPitch = CalculateWorldPitchDegrees(player.Position!.Value, target);
        var targetPitch = ResolveTargetPitchDegrees(worldPitch, options);
        var currentPitch = player.CameraPitchDegrees ?? targetPitch;
        var targetYaw = CalculateTargetYawDegrees(player.Position.Value, target);
        var yawError = NormalizeSignedDegrees(targetYaw - currentYaw.Value);
        var pitchError = targetPitch - currentPitch;
        return new CameraTurnSnapshot(
            player.Position.Value,
            target,
            StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, target),
            currentYaw.Value,
            currentPitch,
            targetYaw,
            worldPitch,
            targetPitch,
            yawError,
            pitchError,
            0,
            TimeSpan.Zero);
    }

    private static double CalculateWorldPitchDegrees(Vector3Snapshot source, Vector3Snapshot target)
    {
        var horizontalDistance = StationaryCombatTargetSelector.HorizontalDistance(source, target);
        var dz = source.Z - target.Z;
        return Math.Atan2(dz, Math.Max(0.001D, horizontalDistance)) * 180.0D / Math.PI;
    }

    private static double ResolveTargetPitchDegrees(double worldPitchDegrees, PathFollowTurnOptions options)
    {
        if (!options.UseWorldTargetPitch)
        {
            return options.TargetPitchDegrees;
        }

        return ClampDouble(
            worldPitchDegrees + 10.0D,
            options.MinTargetPitchDegrees,
            options.MaxTargetPitchDegrees);
    }

    private static double CalculateTargetYawDegrees(Vector3Snapshot source, Vector3Snapshot target)
    {
        var dx = target.X - source.X;
        var dy = target.Y - source.Y;
        var mode = (Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE") ?? "y-x").Trim().ToLowerInvariant();
        double angleRadians = mode switch
        {
            "xy" => Math.Atan2(dy, dx),
            "negxy" or "-xy" => Math.Atan2(-dy, dx),
            "xnegy" or "x-y" => Math.Atan2(dy, -dx),
            "negyx" or "-yx" => Math.Atan2(-dx, dy),
            "ynegx" or "y-x" => Math.Atan2(dx, -dy),
            _ => Math.Atan2(dx, dy)
        };
        return NormalizeSignedDegrees(angleRadians * 180.0D / Math.PI + ReadDoubleFromEnv("AION_FACE_TARGET_YAW_OFFSET_DEG", 0.0D));
    }

    private static int CalculateCameraDragDx(
        double errorDegrees,
        double pixelsPerDegreeAbs,
        PathFollowTurnOptions options,
        bool applyMinCorrection,
        out double rawDx,
        out bool minApplied)
    {
        rawDx = -errorDegrees * pixelsPerDegreeAbs;
        minApplied = false;

        var dx = (int)Math.Round(rawDx, MidpointRounding.AwayFromZero);
        if (dx == 0)
        {
            dx = errorDegrees > 0.0D ? -1 : 1;
        }

        var sign = dx < 0 ? -1 : 1;
        var absDx = Math.Abs(dx);
        if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDx < options.MinCorrectionPixels)
        {
            dx = sign * options.MinCorrectionPixels;
            minApplied = true;
        }

        return dx;
    }

    private static int CalculateCameraDragDy(
        double errorDegrees,
        double pixelsPerDegreeAbs,
        PathFollowTurnOptions options,
        bool applyMinCorrection,
        out double rawDy,
        out bool minApplied)
    {
        rawDy = errorDegrees * pixelsPerDegreeAbs;
        if (options.PitchInvertMouse)
        {
            rawDy = -rawDy;
        }

        minApplied = false;
        var dy = (int)Math.Round(rawDy, MidpointRounding.AwayFromZero);
        if (dy == 0)
        {
            dy = errorDegrees > 0.0D ? 1 : -1;
            if (options.PitchInvertMouse)
            {
                dy = -dy;
            }
        }

        var sign = dy < 0 ? -1 : 1;
        var absDy = Math.Abs(dy);
        if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDy < options.MinCorrectionPixels)
        {
            dy = sign * options.MinCorrectionPixels;
            minApplied = true;
        }

        return dy;
    }

    private static int[] BuildSignedCameraChunks(int pixels, PathFollowTurnOptions options)
    {
        if (pixels == 0)
        {
            return Array.Empty<int>();
        }

        var sign = pixels < 0 ? -1 : 1;
        var remaining = Math.Abs(pixels);
        var chunks = new List<int>();
        var prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);
        for (var i = 0; i < prime; i++)
        {
            chunks.Add(sign);
            remaining--;
        }

        var tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
        var chunkRemaining = remaining - tail;
        var middleChunks = options.DragChunkMode == CameraDragChunkMode.Gradient
            ? BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels))
            : BuildTenStepMiddleChunks(chunkRemaining);
        for (var i = 0; i < middleChunks.Length; i++)
        {
            chunks.Add(sign * middleChunks[i]);
        }

        for (var i = 0; i < tail; i++)
        {
            chunks.Add(sign);
        }

        return chunks.ToArray();
    }

    private static int[] BuildTenStepMiddleChunks(int totalPixels)
    {
        if (totalPixels <= 0)
        {
            return Array.Empty<int>();
        }

        var chunkCount = Math.Min(10, totalPixels);
        var chunks = new int[chunkCount];
        var basePixels = totalPixels / chunkCount;
        var remainder = totalPixels % chunkCount;
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i] = basePixels + (i < remainder ? 1 : 0);
        }

        return chunks;
    }

    private static int[] BuildGradientChunks(int totalPixels, int maxStep)
    {
        if (totalPixels <= 0)
        {
            return Array.Empty<int>();
        }

        maxStep = Math.Max(1, maxStep);
        var length = 1;
        while (GetMaxGradientSum(length, maxStep) < totalPixels)
        {
            length++;
        }

        var chunks = new int[length];
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i] = 1;
        }

        var remaining = totalPixels - chunks.Length;
        var centerOutOrder = BuildCenterOutIndexOrder(chunks.Length);
        while (remaining > 0)
        {
            var raised = false;
            for (var i = 0; i < centerOutOrder.Length && remaining > 0; i++)
            {
                var index = centerOutOrder[i];
                if (!CanRaiseGradientChunk(chunks, index, maxStep))
                {
                    continue;
                }

                chunks[index]++;
                remaining--;
                raised = true;
            }

            if (!raised)
            {
                break;
            }
        }

        return chunks;
    }

    private static int GetMaxGradientSum(int length, int maxStep)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            var distanceToEdge = Math.Min(i, length - 1 - i);
            sum += Math.Min(maxStep, distanceToEdge + 1);
        }

        return sum;
    }

    private static int[] BuildCenterOutIndexOrder(int length)
    {
        var order = new List<int>();
        var leftCenter = (length - 1) / 2;
        var rightCenter = length / 2;
        for (var offset = 0; order.Count < length; offset++)
        {
            var left = leftCenter - offset;
            if (left >= 0)
            {
                order.Add(left);
            }

            var right = rightCenter + offset;
            if (right != left && right < length)
            {
                order.Add(right);
            }
        }

        return order.ToArray();
    }

    private static bool CanRaiseGradientChunk(int[] chunks, int index, int maxStep)
    {
        if (chunks[index] >= maxStep)
        {
            return false;
        }

        if (chunks.Length > 1 && (index == 0 || index == chunks.Length - 1))
        {
            return false;
        }

        var nextValue = chunks[index] + 1;
        if (index > 0 && Math.Abs(nextValue - chunks[index - 1]) > 1)
        {
            return false;
        }

        if (index + 1 < chunks.Length && Math.Abs(nextValue - chunks[index + 1]) > 1)
        {
            return false;
        }

        return true;
    }

    private static int EstimateCombinedChunkDragMoveCommandCount(
        int dx,
        int dy,
        PathFollowTurnOptions options)
    {
        return Math.Max(
            BuildSignedCameraChunks(dx, options).Length,
            BuildSignedCameraChunks(dy, options).Length);
    }

    private static int EstimateCombinedPrimeMoveCommandCount(
        int dx,
        int dy,
        PathFollowTurnOptions options)
    {
        return Math.Max(
            EstimatePrimeMoveCommandCount(dx, options),
            EstimatePrimeMoveCommandCount(dy, options));
    }

    private static int EstimatePrimeMoveCommandCount(int pixels, PathFollowTurnOptions options)
    {
        return Math.Min(Math.Max(0, options.DragPrimePixels), Math.Abs(pixels));
    }

    private static bool ShouldRestartMoveForYaw(bool isMoving, double yawErrorDegrees, double restartYawThresholdDegrees)
    {
        return isMoving && Math.Abs(yawErrorDegrees) > restartYawThresholdDegrees;
    }

    private static bool ShouldDisableMoveAdjustByDistance(bool isMoving, double distanceToTarget, double disableMoveAdjustDistance)
    {
        return isMoving && distanceToTarget <= disableMoveAdjustDistance;
    }

    private static bool ShouldTurn(
        bool restartMoveForLargeYaw,
        bool moveAdjustDisabledByDistance,
        double yawErrorDegrees,
        double pitchErrorDegrees,
        double yawToleranceDegrees,
        double pitchToleranceDegrees)
    {
        return restartMoveForLargeYaw ||
               (!moveAdjustDisabledByDistance &&
                (Math.Abs(yawErrorDegrees) > yawToleranceDegrees ||
                 Math.Abs(pitchErrorDegrees) > pitchToleranceDegrees));
    }

    private static CameraTurnVerificationResult VerifyCameraTurn(
        double beforeYawError,
        double beforePitchError,
        double afterYawError,
        double afterPitchError)
    {
        var beforeYawAbs = Math.Abs(beforeYawError);
        var afterYawAbs = Math.Abs(afterYawError);
        var beforePitchAbs = Math.Abs(beforePitchError);
        var afterPitchAbs = Math.Abs(afterPitchError);
        var yawWasMeaningful = beforeYawAbs > 0.0001D;
        var pitchWasMeaningful = beforePitchAbs > 0.0001D;
        var yawImproved = !yawWasMeaningful || afterYawAbs < beforeYawAbs;
        var pitchImproved = !pitchWasMeaningful || afterPitchAbs < beforePitchAbs;
        return new CameraTurnVerificationResult(
            yawImproved,
            pitchImproved,
            (yawWasMeaningful && yawImproved) || (pitchWasMeaningful && pitchImproved),
            yawWasMeaningful && beforeYawError * afterYawError < 0.0D,
            pitchWasMeaningful && beforePitchError * afterPitchError < 0.0D);
    }

    private static double ReadPathFollowYawTolerance()
    {
        return Math.Max(0.1D, ReadDoubleFromEnv("AION_PATH_FOLLOW_YAW_TOLERANCE_DEG", 10.0D));
    }

    private static double ReadPathFollowMicroYawTolerance()
    {
        return Math.Max(0.1D, ReadDoubleFromEnv("AION_PATH_FOLLOW_MICRO_YAW_TOLERANCE_DEG", 1.5D));
    }

    private static int ReadSmartPreAimWorldScanMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_SMART_PREAIM_WORLD_SCAN_MS", 900), 250, 5000);
    }

    private static int ReadSmartPreAimAdjustMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_SMART_PREAIM_ADJUST_MS", 220), 80, 2000);
    }

    private static int ReadSmartPreAimMinimumHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_SMART_PREAIM_MIN_HOLD_MS", 1000), 0, 10000);
    }

    private static double ReadSmartPreAimSwitchDistanceMargin()
    {
        return ClampDouble(
            ReadDoubleFromEnv("ROADHOG_SMART_PREAIM_SWITCH_DISTANCE_MARGIN", DefaultSmartPreAimSwitchDistanceMargin),
            0.0D,
            30.0D);
    }

    private static TimeSpan ReadSmartPreAimResultTtl()
    {
        var milliseconds = ClampInt(
            ReadRawIntFromEnv("ROADHOG_SMART_PREAIM_RESULT_TTL_MS", (int)DefaultSmartPreAimResultTtl.TotalMilliseconds),
            500,
            120_000);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static double ReadPathFollowRestartYawThreshold()
    {
        return Math.Max(0.1D, ReadDoubleFromEnv("AION_PATH_FOLLOW_RESTART_YAW_DEG", 15.0D));
    }

    private static double ReadPathFollowDisableMoveAdjustDistance()
    {
        return Math.Max(0.0D, ReadDoubleFromEnv("AION_PATH_FOLLOW_DISABLE_MOVE_ADJUST_DISTANCE", 15.0D));
    }

    private static PathFollowTurnOptions ReadPathFollowTurnOptions(CombatScriptSettings? combat)
    {
        var pixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG_ABS", 0.0D));
        if (pixelsPerDegreeAbs < 0.0001D)
        {
            pixelsPerDegreeAbs = ResolveConfiguredPixelsPerDegree(
                combat?.CameraYawPixelsPerDegree,
                "AION_FACE_TARGET_PIXELS_PER_DEG",
                DefaultYawPixelsPerDegree);
        }

        if (pixelsPerDegreeAbs < 0.0001D)
        {
            pixelsPerDegreeAbs = DefaultYawPixelsPerDegree;
        }

        var pitchPixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG_ABS", 0.0D));
        if (pitchPixelsPerDegreeAbs < 0.0001D)
        {
            pitchPixelsPerDegreeAbs = ResolveConfiguredPixelsPerDegree(
                combat?.CameraPitchPixelsPerDegree,
                "AION_CAMERA_PITCH_PIXELS_PER_DEG",
                DefaultPitchPixelsPerDegree);
        }

        if (pitchPixelsPerDegreeAbs < 0.0001D)
        {
            pitchPixelsPerDegreeAbs = DefaultPitchPixelsPerDegree;
        }

        var yawTolerance = ReadPathFollowYawTolerance();
        var fixedTargetPitch = ReadDoubleFromEnv("AION_CAMERA_FIXED_PITCH_DEG", 20.0D);
        var targetPitch = ClampDouble(ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_DEG", fixedTargetPitch), -65.0D, 85.0D);
        var minTargetPitch = ClampDouble(ReadDoubleFromEnv("AION_CAMERA_TARGET_PITCH_MIN_DEG", -65.0D), -89.0D, 89.0D);
        var maxTargetPitch = ClampDouble(ReadDoubleFromEnv("AION_CAMERA_TARGET_PITCH_MAX_DEG", 85.0D), -89.0D, 89.0D);
        if (minTargetPitch > maxTargetPitch)
        {
            (minTargetPitch, maxTargetPitch) = (maxTargetPitch, minTargetPitch);
        }

        return new PathFollowTurnOptions
        {
            DurationMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DURATION_MS", 0), 0, 3000),
            SettleMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_SETTLE_MS", 20), 0, 500),
            MouseDownWarmupMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_DOWN_WARMUP_MS", 30), 0, 1000),
            MouseHoldAfterMoveMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_HOLD_AFTER_MOVE_MS", 0), 0, 1000),
            MinCorrectionPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MIN_CORRECTION_PIXELS", 20), 0, 500),
            ToleranceDegrees = Math.Min(Math.Max(0.1D, ReadDoubleFromEnv("AION_FACE_TARGET_TOLERANCE_DEG", 2.5D)), yawTolerance),
            YawToleranceDegrees = yawTolerance,
            MicroYawToleranceDegrees = ReadPathFollowMicroYawTolerance(),
            RestartYawThresholdDegrees = ReadPathFollowRestartYawThreshold(),
            DisableMoveAdjustDistance = ReadPathFollowDisableMoveAdjustDistance(),
            PitchToleranceDegrees = Math.Max(0.5D, ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_TOLERANCE_DEG", 5.0D)),
            TargetPitchDegrees = targetPitch,
            UseWorldTargetPitch = ReadBoolFromEnv("AION_CAMERA_USE_WORLD_TARGET_PITCH", true),
            MinTargetPitchDegrees = minTargetPitch,
            MaxTargetPitchDegrees = maxTargetPitch,
            PixelsPerDegreeAbs = pixelsPerDegreeAbs,
            PitchPixelsPerDegreeAbs = pitchPixelsPerDegreeAbs,
            DragPrimePixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_PRIME_PIXELS", 3), 0, 50),
            DragTailPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_TAIL_PIXELS", 0), 0, 50),
            DragStepPixels = ClampInt(Math.Abs(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_PX", 20)), 1, 500),
            DragChunkMode = ReadCameraDragChunkMode(),
            DragStepDelayMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_DELAY_MS", 3), 0, 50),
            TwoPassMaxPasses = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_TWO_PASS_MAX_PASSES", 2), 1, 4),
            AngleAdjustPollWaitMs = ClampInt(ReadRawIntFromEnv("AION_PATH_FOLLOW_ANGLE_ADJUST_POLL_WAIT_MS", 20), 0, 200),
            AdaptiveReadSettleMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_ADAPTIVE_READ_SETTLE_MS", 20), 0, 200),
            AdaptiveReadTimeoutMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_ADAPTIVE_READ_TIMEOUT_MS", 900), 0, 2000),
            AdaptiveStableMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_ADAPTIVE_STABLE_MS", 160), 0, 1000),
            AdaptiveStableTimeoutMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_ADAPTIVE_STABLE_TIMEOUT_MS", 1500), 0, 5000),
            AdaptiveMinYawDeltaDegrees = Math.Max(0.0D, ReadDoubleFromEnv("AION_FACE_TARGET_ADAPTIVE_MIN_YAW_DELTA_DEG", 0.25D)),
            PitchInvertMouse = ReadBoolFromEnv("AION_CAMERA_PITCH_INVERT_MOUSE", false)
        };
    }

    private static double ResolveConfiguredPixelsPerDegree(double? configuredValue, string legacyEnvName, double fallback)
    {
        var configuredAbs = Math.Abs(configuredValue ?? 0.0D);
        if (configuredAbs >= 0.0001D)
        {
            return configuredAbs;
        }

        var legacyEnvValue = Math.Abs(ReadDoubleFromEnv(legacyEnvName, fallback));
        return legacyEnvValue >= 0.0001D ? legacyEnvValue : fallback;
    }

    private static CameraDragChunkMode ReadCameraDragChunkMode()
    {
        var value = (Environment.GetEnvironmentVariable("AION_FACE_TARGET_DRAG_CHUNK_MODE") ?? "ten_step_middle")
            .Trim()
            .ToLowerInvariant();
        return value switch
        {
            "gradient" or "legacy" => CameraDragChunkMode.Gradient,
            _ => CameraDragChunkMode.TenStepMiddle
        };
    }

    private static string FormatCameraDragChunkMode(CameraDragChunkMode mode)
    {
        return mode == CameraDragChunkMode.Gradient ? "gradient" : "ten_step_middle";
    }

    private static int ReadTabVerifyDelayMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", 500), 0, 1000);
    }

    private static int ReadTabVerifyPollMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_VERIFY_POLL_MS", 20), 1, 500);
    }

    private static int ReadTabVerifyWindowMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_VERIFY_WINDOW_MS", 300), 1, 2000);
    }

    private static int ReadTabCorpseNudgeKeyHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_CORPSE_NUDGE_HOLD_MS", 25), 1, 1000);
    }

    private static int ReadPathFollowTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("AION_PATH_FOLLOW_TICK_MS", 50), 1, 2000);
    }

    private static int ReadDeathRecoveryTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_RECOVERY_TICK_MS", 200), 40, 2000);
    }

    private static int ReadDeathRevivePathStuckMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", 2_500), 1, 30_000);
    }

    private static double ReadDeathRevivePathStuckDistance()
    {
        return ClampDouble(ReadDoubleFromEnv("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", 0.5D), 0.05D, 5.0D);
    }

    private static int ReadDeathRevivePathJumpHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", 50), 1, 1000);
    }

    private static int ReadCombatApproachStuckMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_COMBAT_APPROACH_STUCK_MS", 2_500), 1, 30_000);
    }

    private static double ReadCombatApproachStuckDistance()
    {
        return ClampDouble(ReadDoubleFromEnv("ROADHOG_COMBAT_APPROACH_STUCK_DISTANCE", 0.5D), 0.05D, 5.0D);
    }

    private static int ReadCombatApproachJumpHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_COMBAT_APPROACH_JUMP_HOLD_MS", 50), 1, 1000);
    }

    private static int ReadCombatApproachJumpPressCount()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_COMBAT_APPROACH_JUMP_COUNT", 3), 1, 10);
    }

    private static int ReadCombatApproachJumpIntervalMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_COMBAT_APPROACH_JUMP_INTERVAL_MS", 60), 0, 1000);
    }

    private static int ReadNoDamageNoTargetingTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS", 10_000), 1, 60_000);
    }

    private static int ReadFightSoftRestartTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_FIGHT_SOFT_RESTART_TIMEOUT_MS", 8_000), 1, 60_000);
    }

    private static int ReadStalledTargetExclusionMs(CombatScriptSettings? combat)
    {
        var configuredSeconds = combat?.StalledTargetExclusionSeconds ??
                                CombatScriptSettings.DefaultStalledTargetExclusionSeconds;
        if (configuredSeconds <= 0)
        {
            configuredSeconds = CombatScriptSettings.DefaultStalledTargetExclusionSeconds;
        }

        configuredSeconds = Math.Clamp(
            configuredSeconds,
            CombatScriptSettings.MinimumStalledTargetExclusionSeconds,
            CombatScriptSettings.MaximumStalledTargetExclusionSeconds);
        var configuredMilliseconds = (int)(configuredSeconds * 1000L);
        return ClampInt(
            ReadRawIntFromEnv("ROADHOG_STALLED_TARGET_EXCLUSION_MS", configuredMilliseconds),
            1,
            CombatScriptSettings.MaximumStalledTargetExclusionSeconds * 1000);
    }

    private static int ReadFaceTargetTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_FACE_TARGET_TIMEOUT_MS", 10_000), 1, 60_000);
    }

    private static TimeSpan ReadNoKillTimeout()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", (int)DefaultNoKillTimeout.TotalMilliseconds),
            0,
            24 * 60 * 60 * 1000));
    }

    private static TimeSpan ReadNoKillTownReturnSettleDelay()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv(
                "ROADHOG_NO_KILL_RETURN_SETTLE_MS",
                (int)DefaultNoKillTownReturnSettleDelay.TotalMilliseconds),
            0,
            60_000));
    }

    private static TimeSpan ReadNoKillRetryDelay()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv("ROADHOG_NO_KILL_RETURN_RETRY_MS", (int)DefaultNoKillRetryDelay.TotalMilliseconds),
            0,
            60 * 60 * 1000));
    }

    private static double ReadNoKillTownReturnMinDistance()
    {
        return ClampDouble(ReadDoubleFromEnv("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE", 5.0D), 0.0D, 10_000.0D);
    }

    private static double ReadStartupTownReturnDistance()
    {
        return ClampDouble(
            ReadDoubleFromEnv("ROADHOG_STARTUP_RETURN_DISTANCE", DefaultStartupTownReturnDistance),
            1.0D,
            10_000.0D);
    }

    private static TimeSpan ReadStartupTownReturnSettleDelay()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv(
                "ROADHOG_STARTUP_RETURN_SETTLE_MS",
                (int)DefaultStartupTownReturnSettleDelay.TotalMilliseconds),
            0,
            60_000));
    }

    private static double ReadStartupTownReturnMinDistance()
    {
        return ClampDouble(
            ReadDoubleFromEnv("ROADHOG_STARTUP_RETURN_MIN_DISTANCE", DefaultStartupTownReturnMinDistance),
            0.0D,
            10_000.0D);
    }

    private static double ReadPathCombatAccessPathDistance()
    {
        return ClampDouble(ReadDoubleFromEnv("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE", 120.0D), 0.0D, 10_000.0D);
    }

    private static int ReadDeathReviveClickDelayMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", 10_000), 0, 60_000);
    }

    private static int ReadDeathReviveClickX()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_CLICK_X", DefaultReviveClickX), 0, 32767);
    }

    private static int ReadDeathReviveClickY()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_CLICK_Y", DefaultReviveClickY), 0, 32767);
    }

    private static int ReadDeathReviveFallbackClickX()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_FALLBACK_CLICK_X", DefaultReviveFallbackClickX), 0, 32767);
    }

    private static int ReadDeathReviveFallbackClickY()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_FALLBACK_CLICK_Y", DefaultReviveFallbackClickY), 0, 32767);
    }

    private static int ReadDeathReviveThirdClickX()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_THIRD_CLICK_X", DefaultReviveThirdClickX), 0, 32767);
    }

    private static int ReadDeathReviveThirdClickY()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_THIRD_CLICK_Y", DefaultReviveThirdClickY), 0, 32767);
    }

    private static (int X, int Y) ReadDeathReviveClickPoint(AccountWorkerContext context, int reviveClickCount)
    {
        if (context.Config.ScriptSettings?.Paths is { } paths)
        {
            return (
                ClampInt(paths.DeathReviveClickX, 0, 32767),
                ClampInt(paths.DeathReviveClickY, 0, 32767));
        }

        return ReadLegacyDeathReviveClickPoint(reviveClickCount);
    }

    private static (int X, int Y) ReadLegacyDeathReviveClickPoint(int reviveClickCount)
    {
        var clickIndex = Math.Max(0, reviveClickCount) % 3;
        if (clickIndex == 0)
        {
            return (ReadDeathReviveClickX(), ReadDeathReviveClickY());
        }

        if (clickIndex == 1)
        {
            return (ReadDeathReviveFallbackClickX(), ReadDeathReviveFallbackClickY());
        }

        return (ReadDeathReviveThirdClickX(), ReadDeathReviveThirdClickY());
    }

    private static int ReadDeathReviveClickHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", 30), 1, 1000);
    }

    private static int ReadDeathReviveMouseResetCount()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", 2), 1, 10);
    }

    private static int ReadDeathReviveMouseStepDelayMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", 10), 0, 1000);
    }

    private static int ReadDeathReviveRetryMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_REVIVE_RETRY_MS", 500), 0, 60_000);
    }

    private static int ReadDeathPostReviveScrollCount()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT", DefaultPostReviveScrollCount), 0, 100);
    }

    private static int ReadDeathPostReviveScrollDelta()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_POST_REVIVE_SCROLL_DELTA", DefaultPostReviveScrollDelta), -120, 120);
    }

    private static int ReadDeathPostReviveScrollIntervalMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", 100), 0, 10_000);
    }

    private static int ReadLootTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_TICK_MS", 80), 20, 2000);
    }

    private static int ReadLootAfterKillWaitMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", 1_200), 0, 10_000);
    }

    private static int ReadLootAfterPickWaitMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", 200), 0, 10_000);
    }

    private static int ReadLootAttemptCacheTtlMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_ATTEMPT_CACHE_TTL_MS", 300_000), 30_000, 600_000);
    }

    private static int ReadLootPressCount()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_PRESS_COUNT", 1), 1, 5);
    }

    private static int ReadLootPressIntervalMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_PRESS_INTERVAL_MS", 30), 0, 1000);
    }

    private static int ReadLootKeyHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_KEY_HOLD_MS", 25), 1, 1000);
    }

    private static double ReadLootApproachDistance()
    {
        return Math.Max(0.2D, ReadDoubleFromEnv("ROADHOG_LOOT_APPROACH_DISTANCE", 3.0D));
    }

    private static int ReadLootApproachTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_APPROACH_TIMEOUT_MS", 5_000), 0, 5_000);
    }

    private readonly record struct PostLootNoTargetDelayResult(bool Delayed, WorldObjectSnapshot? Target);

    private readonly record struct RecoveryDefenseSelection(
        WorldObjectSnapshot? Target,
        bool IsRevivePathClearTarget,
        bool HoldForSmartPreAimHandoff,
        bool IsSmartPreAimHandoffTarget)
    {
        public static RecoveryDefenseSelection None { get; } =
            new(null, false, false, false);
    }

    private readonly record struct StationaryGatherTickResult(
        bool Handled,
        TimeSpan Delay,
        WorldObjectSnapshot? ThreatTarget)
    {
        public static StationaryGatherTickResult NotHandled { get; } =
            new(false, TimeSpan.Zero, null);

        public static StationaryGatherTickResult HandledWith(TimeSpan delay)
        {
            return new StationaryGatherTickResult(true, delay, null);
        }

        public static StationaryGatherTickResult ForThreat(WorldObjectSnapshot target)
        {
            return new StationaryGatherTickResult(false, TimeSpan.Zero, target);
        }
    }

    private readonly record struct LootAttemptEligibility(
        bool HasLoot,
        string Source,
        string Reason,
        uint LootableRaw,
        uint InteractionState,
        string? Error = null);

    private static double NormalizeSignedDegrees(double angle)
    {
        angle %= 360.0D;
        if (angle > 180.0D)
        {
            angle -= 360.0D;
        }
        else if (angle <= -180.0D)
        {
            angle += 360.0D;
        }

        return angle;
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double ClampDouble(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double ReadDoubleFromEnv(string name, double defaultValue)
    {
        return double.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : defaultValue;
    }

    private static TimeSpan ReadGatherAttemptStartTimeout()
    {
        return TimeSpan.FromMilliseconds(
            ClampInt(ReadRawIntFromEnv("ROADHOG_GATHER_START_TIMEOUT_MS", 5_000), 1, 30_000));
    }

    private static TimeSpan ReadGatherNodeTimeout()
    {
        return TimeSpan.FromMilliseconds(
            ClampInt(ReadRawIntFromEnv("ROADHOG_GATHER_NODE_TIMEOUT_MS", 30_000), 1_000, 600_000));
    }

    private static TimeSpan ReadGatherApproachStuckDelay()
    {
        return TimeSpan.FromMilliseconds(
            ClampInt(ReadRawIntFromEnv("ROADHOG_GATHER_APPROACH_STUCK_MS", 3000), 1, 60_000));
    }

    private static TimeSpan ReadGatherApproachTimeout()
    {
        return TimeSpan.FromMilliseconds(
            ClampInt(ReadRawIntFromEnv("ROADHOG_GATHER_APPROACH_TIMEOUT_MS", 5_000), 1, 120_000));
    }

    private static double ReadGatherApproachProgressDistance()
    {
        return ClampDouble(
            ReadDoubleFromEnv("ROADHOG_GATHER_APPROACH_PROGRESS_DISTANCE", 0.5D),
            0.05D,
            5.0D);
    }

    private static int ReadRawIntFromEnv(string name, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : defaultValue;
    }

    private static bool ReadBoolFromEnv(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               raw.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static Task DelayAsync(TimeSpan delay, AccountWorkerContext context)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, context.StopToken);
    }

    private static CancellationToken ResolveOperationToken(
        AccountWorkerContext context,
        CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? cancellationToken
            : context.StopToken;
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private static void LogThrottled(
        AccountWorkerContext context,
        StationaryCombatState state,
        string eventName,
        Dictionary<string, object?> fields)
    {
        var now = DateTimeOffset.Now;
        if (now - state.LastLogAt < TimeSpan.FromSeconds(3))
        {
            return;
        }

        state.LastLogAt = now;
        context.Logger.Warn(eventName, fields);
    }

    private static void LogPathTick(
        AccountWorkerContext context,
        StationaryCombatState state,
        CameraTurnSnapshot snapshot,
        double activeYawTolerance,
        bool moveAdjustDisabledByDistance,
        bool needsTurn)
    {
        LogActionThrottled(context, state, "stationary_combat.path_follow", "tick", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = "tick",
            ["distance"] = Math.Round(snapshot.DistanceToTarget, 2),
            ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
            ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["activeYawTolerance"] = Math.Round(activeYawTolerance, 2),
            ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
            ["worldPitch"] = Math.Round(snapshot.WorldPitch, 2),
            ["targetPitch"] = Math.Round(snapshot.TargetPitch, 2),
            ["pitchError"] = Math.Round(snapshot.PitchError, 2),
            ["moveAdjustDisabledByDistance"] = moveAdjustDisabledByDistance,
            ["needsTurn"] = needsTurn,
            ["moving"] = state.IsMovingForward
        }, TimeSpan.FromMilliseconds(500));
    }

    private static void LogPathAction(
        AccountWorkerContext context,
        StationaryCombatState state,
        string action,
        CameraTurnSnapshot snapshot,
        int mouseDx,
        int mouseDy)
    {
        LogActionThrottled(context, state, "stationary_combat.path_follow", "path:" + action, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = action,
            ["distance"] = Math.Round(snapshot.DistanceToTarget, 2),
            ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
            ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
            ["worldPitch"] = Math.Round(snapshot.WorldPitch, 2),
            ["targetPitch"] = Math.Round(snapshot.TargetPitch, 2),
            ["pitchError"] = Math.Round(snapshot.PitchError, 2),
            ["mouseDx"] = mouseDx,
            ["mouseDy"] = mouseDy,
            ["moving"] = state.IsMovingForward
        }, TimeSpan.FromMilliseconds(500));
    }

    private static void LogActionThrottled(
        AccountWorkerContext context,
        StationaryCombatState state,
        string eventName,
        string actionKey,
        Dictionary<string, object?> fields,
        TimeSpan interval)
    {
        var now = DateTimeOffset.Now;
        var key = eventName + ":" + actionKey;
        if (state.LastActionLogAtByKey.TryGetValue(key, out var lastLogAt) &&
            now - lastLogAt < interval)
        {
            return;
        }

        state.LastActionLogAtByKey[key] = now;
        context.Logger.Info(eventName, fields);
    }

    private sealed class PathFollowPollState
    {
        public readonly object SyncRoot = new();
        public string AccountName { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public CancellationTokenSource Cancellation { get; init; } = new();
        public Task? Task { get; set; }
        public bool StopRequested { get; set; }
        public PlayerSnapshot Local { get; set; } = null!;
        public DateTimeOffset LastReadTime { get; set; }
        public long ReadCount { get; set; }
        public int TargetIndex { get; set; } = -1;
        public Vector3Snapshot TargetPoint { get; set; }
        public double ReachDistance { get; set; }
        public PathFollowTurnOptions Options { get; set; } = new();
        public double TargetPitch { get; set; }
        public bool HasMetrics { get; set; }
        public CameraTurnSnapshot? MetricsSnapshot { get; set; }
        public bool IsMoving { get; set; }
        public bool HasArrived { get; set; }
        public int ArrivedTargetIndex { get; set; }
        public CameraTurnSnapshot? ArrivedSnapshot { get; set; }
    }

    private sealed record PathFollowStopContext(
        bool Arrived,
        bool StoppedForward,
        string Reason);

    private enum RadarDirectTargetSource
    {
        WorldObjects,
        LockedTarget
    }

    private sealed record StationaryHomeResolution(
        Vector3Snapshot Position,
        string Source,
        string PathName,
        int PathPointCount);

    private sealed record PathCombatStartProbe(
        string PathName,
        int PointIndex,
        int PointCount,
        double Distance);

    private sealed record PathFollowTurnOptions
    {
        public int DurationMs { get; init; }
        public int SettleMs { get; init; }
        public int MouseDownWarmupMs { get; init; }
        public int MouseHoldAfterMoveMs { get; init; }
        public int MinCorrectionPixels { get; init; }
        public double ToleranceDegrees { get; init; }
        public double YawToleranceDegrees { get; init; }
        public double MicroYawToleranceDegrees { get; init; }
        public double RestartYawThresholdDegrees { get; init; }
        public double DisableMoveAdjustDistance { get; init; }
        public double PitchToleranceDegrees { get; init; }
        public double TargetPitchDegrees { get; init; }
        public bool UseWorldTargetPitch { get; init; }
        public double MinTargetPitchDegrees { get; init; } = -65.0D;
        public double MaxTargetPitchDegrees { get; init; } = 85.0D;
        public double PixelsPerDegreeAbs { get; init; }
        public double PitchPixelsPerDegreeAbs { get; init; }
        public int DragPrimePixels { get; init; }
        public int DragTailPixels { get; init; }
        public int DragStepPixels { get; init; }
        public CameraDragChunkMode DragChunkMode { get; init; } = CameraDragChunkMode.TenStepMiddle;
        public int DragStepDelayMs { get; init; }
        public int TwoPassMaxPasses { get; init; }
        public int AngleAdjustPollWaitMs { get; init; }
        public int AdaptiveReadSettleMs { get; init; }
        public int AdaptiveReadTimeoutMs { get; init; }
        public int AdaptiveStableMs { get; init; }
        public int AdaptiveStableTimeoutMs { get; init; }
        public double AdaptiveMinYawDeltaDegrees { get; init; }
        public bool PitchInvertMouse { get; init; }
    }

    private enum CameraDragChunkMode
    {
        TenStepMiddle,
        Gradient
    }

    private sealed record CameraTurnSnapshot(
        Vector3Snapshot PlayerPosition,
        Vector3Snapshot TargetPosition,
        double DistanceToTarget,
        double CurrentYaw,
        double CurrentPitch,
        double TargetYaw,
        double WorldPitch,
        double TargetPitch,
        double YawError,
        double PitchError,
        long ReadCount,
        TimeSpan Age);

    private sealed class CombinedTurnResult
    {
        public bool Success { get; set; }

        public bool MouseMoveAttempted { get; set; }

        public bool AngleChangeObserved { get; set; }
        public double FinalYawError { get; set; }
        public double FinalPitchError { get; set; }
        public int TotalDx { get; set; }
        public int TotalDy { get; set; }
        public int Passes { get; set; }
    }

    private sealed class PathFollowAdjustResult
    {
        public bool Aligned { get; set; }
        public bool RestartMove { get; set; }
        public int MovedDx { get; set; }
        public int MovedDy { get; set; }
        public CameraTurnSnapshot? Snapshot { get; set; }
    }

    private sealed record CameraTurnVerificationResult(
        bool YawImproved,
        bool PitchImproved,
        bool AnyImproved,
        bool YawOvershot,
        bool PitchOvershot);
}
