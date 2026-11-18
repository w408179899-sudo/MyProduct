using Roadhog.Application.Travel;
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

public sealed partial class StationaryCombatController
{
    private async Task<TimeSpan> TickLegacyAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        using var returnFailureTick = state.ReturnNavigationFailure.TrackTick();
        context.StopToken.ThrowIfCancellationRequested();
        if (await TryTickTownReturnAsync(context, plan, semiAutoState, state).ConfigureAwait(false) is { } returnDelay)
            return returnDelay;

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
            semiAutoState.ResetAttackWeaveStates();
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

        if (!state.StartupTownReturnPending && !state.BagCleanup.Active)
        {
            await EnsureRightMouseDownAsync(context, state).ConfigureAwait(false);
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

        var postCombatReturnDelay = await TryTickPostCombatReturnAsync(context, plan, semiAutoState, state,
            player, home, radius, playerDistanceFromHome, radarSettings).ConfigureAwait(false);
        if (postCombatReturnDelay.HasValue) return postCombatReturnDelay.Value;

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
        var restGuardAllowsSit = true;
        var skipMaintenanceThisTick = false;
        if (context.Config.CombatFlowVersion == CombatFlowVersion.CoordinatedV2)
        {
            var recovery = await CoordinateRecoveryAsync(context, plan, semiAutoState, state, player,
                playerPosition, home, radius, combat, playerDistanceFromHome,
                temporaryTargetSwitchGuard, stationaryGatherWorkAvailable).ConfigureAwait(false);
            noTargetRestWakeTarget = recovery.WakeTarget;
            preMaintenanceDefenseTarget = recovery.DefenseTarget;
            restGuardAllowsSit = recovery.AllowsSit;
            if (recovery.Disposition == CombatFlow.RecoveryDisposition.Wait) return IdleDelay;
        }
        else
        {
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
                restGuardAllowsSit = restDecision.CanSit;
                preMaintenanceDefenseTarget = restDecision.DefenseTarget;
                noTargetRestWakeTarget ??= preMaintenanceDefenseTarget;
                if (preMaintenanceDefenseTarget is not null)
                {
                    skipMaintenanceThisTick = true;
                }
            }
            else if (!state.Fighting &&
                     !skipMaintenanceThisTick &&
                     !semiAutoState.IsMaintenanceResting &&
                     !temporaryTargetSwitchGuard &&
                     !IsGatherMaintenanceBlocked(state) &&
                     mayEnterMaintenanceRest)
            {
                // Approaching a defense target is not Fighting yet. Recheck the live threat
                // before every sit attempt, without suppressing ordinary recovery skills/items.
                preMaintenanceDefenseTarget = await SelectMaintenanceDefenseTargetAsync(
                        context, state, playerPosition)
                    .ConfigureAwait(false);
                noTargetRestWakeTarget ??= preMaintenanceDefenseTarget;
                restGuardAllowsSit = preMaintenanceDefenseTarget is null;
                if (preMaintenanceDefenseTarget is not null)
                {
                    LogActionThrottled(context, state, "stationary_combat.maintenance_rest.blocked", "defense", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["reason"] = "defense_target_detected",
                        ["targetEntityId"] = preMaintenanceDefenseTarget.EntityId,
                        ["targetServerObjectId"] = preMaintenanceDefenseTarget.ServerObjectId,
                        ["targetName"] = preMaintenanceDefenseTarget.Name
                    }, TimeSpan.FromSeconds(3));
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
                    allowSitMaintenance: !temporaryTargetSwitchGuard && restGuardAllowsSit,
                    clearSitWhenDisallowed: temporaryTargetSwitchGuard)
                .ConfigureAwait(false))
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return IdleDelay;
            }

            if (!state.Fighting &&
                !temporaryTargetSwitchGuard &&
                !stationaryGatherWorkAvailable &&
                restGuardAllowsSit &&
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
        else if (playerDistanceFromHome > radius && DateTimeOffset.UtcNow >= state.ReturnNavigationRetryAfter)
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
                if (await WaitForStandingBeforeReturnHomeAsync(context, semiAutoState, state, player).ConfigureAwait(false))
                    return IdleDelay;
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
                    await HandleUnreachableReturnAsync(context, state, navigation, "outside_radius").ConfigureAwait(false);
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
        target ??= await SelectNextStationaryTargetAsync(
            context, state, playerPosition, home, radius, combat.ContestMonster, gatherThreatTarget)
            .ConfigureAwait(false);

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
            if (combat.ReturnHomeWhenNoTarget && playerDistanceFromHome > ReturnStopDistance &&
                DateTimeOffset.UtcNow >= state.ReturnNavigationRetryAfter)
            {
                state.ClearNoTargetRest();
                if (await WaitForStandingBeforeReturnHomeAsync(context, semiAutoState, state, player).ConfigureAwait(false))
                    return IdleDelay;
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
                    await HandleUnreachableReturnAsync(context, state, navigation, "no_target").ConfigureAwait(false);
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
                                             restGuardAllowsSit &&
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
                        allowSitMaintenance: !temporaryTargetSwitchGuard && restGuardAllowsSit,
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
                ReadTargetAcquireDistance(context))
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
                                    playerDistanceToTarget > ReadTargetAcquireDistance(context);
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

        if (playerDistanceToTarget > ReadTargetAcquireDistance(context))
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await PathFollowStepAsync(context, state, player, targetPosition, ReadTargetAcquireDistance(context)).ConfigureAwait(false);
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

        await StopMovementAsync(context, state, releaseRightMouse: false).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target, home, radius).ConfigureAwait(false);
    }

}
