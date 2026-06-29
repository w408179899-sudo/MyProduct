using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatController
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan TabInterval = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan MoveTickDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TargetTimeout = TimeSpan.FromMinutes(1);
    private const double ReturnStopDistance = 2.0D;
    private const double AcquireDistance = 25.0D;
    private const double DefaultLootReachDistance = 2.0D;
    private const double DefaultLootScanRadius = 120.0D;
    private const double LootPositionMatchDistance = 3.0D;
    private const double TargetLeashExtraDistance = 5.0D;
    private const double PreLockFaceYawToleranceDegrees = 20.0D;
    private const double StartupRecoveryReachDistance = 3.0D;
    private const int DefaultReviveClickX = 680;
    private const int DefaultReviveClickY = 460;
    private const int DefaultPostReviveScrollCount = 10;
    private const int DefaultPostReviveScrollDelta = -1;
    private const int AbsoluteMouseResetDelta = -32768;
    private const ushort NpcEntityType = 3;

    private readonly IKeyboardInput _input;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly ISharedPathStore? _pathStore;

    public StationaryCombatController(
        IKeyboardInput input,
        SemiAutoCombatController semiAuto,
        ISharedPathStore? pathStore = null)
    {
        _input = input;
        _semiAuto = semiAuto;
        _pathStore = pathStore;
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        if (!combat.HasStationaryCombatPosition)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogThrottled(context, state, "stationary_combat.position.missing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
            return IdleDelay;
        }

        var home = new Vector3Snapshot(
            (float)combat.StationaryCombatX,
            (float)combat.StationaryCombatY,
            (float)combat.StationaryCombatZ);
        var radius = Math.Max(1.0D, combat.StationaryCombatRadius);

        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogThrottled(context, state, "stationary_combat.player.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = playerResult.Error
            });
            return IdleDelay;
        }

        var player = playerResult.Value;
        var playerPosition = player.Position.Value;
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);

        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
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

        if (state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery)
        {
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

        if (state.LootAfterKill.Active)
        {
            return await TickLootAfterKillAsync(
                    context,
                    semiAutoState,
                    state,
                    player)
                .ConfigureAwait(false);
        }

        if (await _semiAuto
                .TryHandleMaintenanceAsync(
                    context,
                    semiAutoState,
                    player,
                    allowSitMaintenance: false,
                    clearSitWhenDisallowed: false,
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
            return IdleDelay;
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
                semiAutoState,
                state,
                player,
                playerPosition,
                home,
                playerDistanceFromHome)
            .ConfigureAwait(false);
        if (startupRecoveryDelay is not null)
        {
            return startupRecoveryDelay.Value;
        }

        if (playerDistanceFromHome > radius)
        {
            state.ReturningHome = true;
        }

        if (state.ReturningHome)
        {
            if (playerDistanceFromHome <= ReturnStopDistance)
            {
                state.ReturningHome = false;
                await StopMovementAsync(context, state).ConfigureAwait(false);
            }
            else
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, home, ReturnStopDistance).ConfigureAwait(false);
                return MoveTickDelay;
            }
        }

        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition,
                forceRefresh: semiAutoState.IsMaintenanceResting)
            .ConfigureAwait(false);
        if (target is not null)
        {
            if (semiAutoState.IsMaintenanceResting)
            {
                await _semiAuto
                    .CancelMaintenanceRestAsync(context, semiAutoState, "targeting_monster_detected")
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (await _semiAuto
                    .TryHandleMaintenanceAsync(
                        context,
                        semiAutoState,
                        player,
                        plan: plan,
                        requireCooldownCalibrationForMaintenance: true)
                    .ConfigureAwait(false))
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                return IdleDelay;
            }

            target = await SelectTargetAsync(context, state, playerPosition, home, radius).ConfigureAwait(false);
        }

        if (target?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            state.ClearTarget();
            return IdleDelay;
        }

        var previousCandidateEntityId = state.CandidateEntityId;
        state.MarkCandidate(target.EntityId, DateTimeOffset.Now);
        var targetPosition = target.Position.Value;
        var targetDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(targetPosition, home);
        var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (previousCandidateEntityId != target.EntityId)
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
                ["targetServerObjectId"] = target.TargetServerObjectId
            });
        }

        if (!target.IsTargetingLocalPlayer && targetDistanceFromHome > radius)
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
                    target.Name,
                    "not_locked")
                .ConfigureAwait(false);
        }

        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (state.IsPendingTabCandidate(target.EntityId))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                "pre_move")
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
            return MoveTickDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target).ConfigureAwait(false);
    }

    public async Task<TimeSpan?> TickPlayerLifeGuardAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        bool followRevivePath)
    {
        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value is null)
        {
            if (state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
            {
                return null;
            }

            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogThrottled(context, state, "player_life.guard.player_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = playerResult.Error
            });
            return IdleDelay;
        }

        var player = playerResult.Value;
        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
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

        var hasStationaryHome = TryGetStationaryHome(context, out var home);
        var shouldFollowRevivePath = followRevivePath && hasStationaryHome;
        var playerDistanceFromHome = player.Position is not null && hasStationaryHome
            ? StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, home)
            : 0.0D;
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
                StationaryCombatDeathRecoveryStep.PostReviveMaintenance => await TickDeathPostReviveMaintenanceNodeAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        player)
                    .ConfigureAwait(false),
                StationaryCombatDeathRecoveryStep.FollowRevivePath => followRevivePath
                    ? await TickDeathFollowRevivePathNodeAsync(
                            context,
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

        var x = ReadDeathReviveClickX();
        var y = ReadDeathReviveClickY();
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

        var x = ReadDeathReviveClickX();
        var y = ReadDeathReviveClickY();
        var result = await ClickAbsoluteScreenPointAsync(context, x, y).ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.death_recovery.revive_retry_click_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["x"] = x,
                ["y"] = y,
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

    private async Task<StationaryCombatBehaviorStatus> TickDeathPostReviveMaintenanceNodeAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
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

    private async Task<StationaryCombatBehaviorStatus> TickDeathFollowRevivePathNodeAsync(
        AccountWorkerContext context,
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

        if (player.Position is null)
        {
            LogActionThrottled(context, state, "stationary_combat.death_recovery.path_wait", "player_no_position", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "player_no_position"
            }, TimeSpan.FromSeconds(1));
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

        while (state.DeathRecovery.RevivePathPointIndex >= 0 &&
               state.DeathRecovery.RevivePathPointIndex < state.DeathRecovery.RevivePathPoints.Count)
        {
            var point = state.DeathRecovery.RevivePathPoints[state.DeathRecovery.RevivePathPointIndex];
            var distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, point);
            if (distance > StartupRecoveryReachDistance)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, point, StartupRecoveryReachDistance).ConfigureAwait(false);
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
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return StationaryCombatBehaviorStatus.Success;
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
        if (x < 0 || y < 0)
        {
            return OperationResult.Fail("Absolute mouse target must be non-negative.");
        }

        var resetCount = ReadDeathReviveMouseResetCount();
        var stepDelay = TimeSpan.FromMilliseconds(ReadDeathReviveMouseStepDelayMs());
        for (var i = 0; i < resetCount; i++)
        {
            var reset = await _input
                .MoveMouseRelativeAsync(AbsoluteMouseResetDelta, AbsoluteMouseResetDelta, context.StopToken)
                .ConfigureAwait(false);
            if (!reset.Success)
            {
                return OperationResult.Fail("Absolute mouse reset failed. " + reset.Error);
            }

            await DelayAsync(stepDelay, context).ConfigureAwait(false);
        }

        var move = await _input.MoveMouseRelativeAsync(x, y, context.StopToken).ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail("Absolute mouse target move failed. " + move.Error);
        }

        await DelayAsync(stepDelay, context).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    private async Task<TimeSpan?> TickStartupRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double playerDistanceFromHome)
    {
        if (state.StartupRecoveryActive)
        {
            return await ContinueStartupRecoveryAsync(
                    context,
                    semiAutoState,
                    state,
                    player,
                    playerPosition)
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
        var nearestPointIndex = FindNearestPathPointIndex(playerPosition, revivePoints, playerDistanceFromHome);
        if (nearestPointIndex < 0)
        {
            context.Logger.Info("stationary_combat.startup_recovery.home_nearest", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = revivePathName,
                ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
                ["pathPointCount"] = revivePoints.Length
            });
            return null;
        }

        var nearestDistance = StationaryCombatTargetSelector.HorizontalDistance(
            playerPosition,
            revivePoints[nearestPointIndex]);
        state.StartStartupRecovery(revivePathName, revivePoints, nearestPointIndex);
        state.ReturningHome = false;
        state.ClearTarget();
        context.Logger.Info("stationary_combat.startup_recovery.selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = revivePathName,
            ["startPointIndex"] = nearestPointIndex,
            ["startPointNumber"] = nearestPointIndex + 1,
            ["pathPointCount"] = revivePoints.Length,
            ["pathPointDistance"] = Math.Round(nearestDistance, 2),
            ["homeDistance"] = Math.Round(playerDistanceFromHome, 2)
        });

        return await ContinueStartupRecoveryAsync(
                context,
                semiAutoState,
                state,
                player,
                playerPosition)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan?> ContinueStartupRecoveryAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot playerPosition)
    {
        while (state.StartupRecoveryActive &&
               state.StartupRecoveryPointIndex >= 0 &&
               state.StartupRecoveryPointIndex < state.StartupRecoveryPoints.Count)
        {
            var point = state.StartupRecoveryPoints[state.StartupRecoveryPointIndex];
            var distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, point);
            if (distance > StartupRecoveryReachDistance)
            {
                semiAutoState.ResetAttackKeyPressThrottle();
                await PathFollowStepAsync(context, state, player, point, StartupRecoveryReachDistance).ConfigureAwait(false);
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
        var targetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!targetResult.Success || targetResult.Value is null)
        {
            if (IsTargetTimedOut(state, now))
            {
                return await IgnoreCurrentTargetAsync(
                        context,
                        semiAutoState,
                        state,
                        state.CurrentTargetEntityId,
                        string.Empty,
                        "not_locked")
                    .ConfigureAwait(false);
            }

            return await ReacquireCurrentFightTargetAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    home,
                    radius,
                    playerDistanceFromHome,
                    targetResult,
                    "target_read_failed")
                .ConfigureAwait(false);
        }

        var target = targetResult.Value;
        if (!target.IsMonsterAlive)
        {
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
                        semiAutoState,
                        state,
                        player)
                    .ConfigureAwait(false);
            }

            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        if (target.TargetEntityId != state.CurrentTargetEntityId)
        {
            if (IsTargetTimedOut(state, now))
            {
                return await IgnoreCurrentTargetAsync(
                        context,
                        semiAutoState,
                        state,
                        state.CurrentTargetEntityId,
                        string.Empty,
                        "not_locked")
                    .ConfigureAwait(false);
            }

            return await ReacquireCurrentFightTargetAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    home,
                    radius,
                    playerDistanceFromHome,
                    targetResult,
                    "target_mismatch")
                .ConfigureAwait(false);
        }

        if (IsTargetTimedOut(state, now))
        {
            return await IgnoreCurrentTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.TargetEntityId,
                    target.Name,
                    "not_dead")
                .ConfigureAwait(false);
        }

        if (!state.CurrentTargetIsMaintenanceDefense &&
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
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.TargetEntityId,
                    target.Name,
                    playerDistanceFromHome,
                    radius,
                    targetResult,
                    "target_outside_leash")
                .ConfigureAwait(false);
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await _semiAuto
            .TickAsync(context, plan, semiAutoState, requireCooldownCalibrationForMaintenance: true)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickLootAfterKillAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
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
                StationaryCombatLootAfterKillStep.ScanLootableCorpses => await TickLootScanCorpsesNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.MoveToCorpse => await TickLootMoveToCorpseNodeAsync(
                        context,
                        state,
                        player)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.PressF9 => await TickLootPressF9NodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.VerifyLockedCorpse => await TickLootVerifyLockedCorpseNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.PressLootKey => await TickLootPressLootKeyNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.WaitAfterLoot => TickLootWaitAfterLootNode(state),
                StationaryCombatLootAfterKillStep.PressStopKey => await TickLootPressStopKeyNodeAsync(
                        context,
                        state)
                    .ConfigureAwait(false),
                StationaryCombatLootAfterKillStep.IgnoreCorpse => TickLootIgnoreCorpseNode(
                    context,
                    state),
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

    private async Task<StationaryCombatBehaviorStatus> TickLootScanCorpsesNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var result = await ReadLootCorpsesAsync(context).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            context.Logger.Warn("stationary_combat.loot.scan_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = result.Error
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        var now = DateTimeOffset.Now;
        var scanRadius = ReadLootScanRadius();
        var corpse = result.Value
            .Where(corpse => corpse.IsLootable)
            .Where(corpse => corpse.IsMonsterCorpse)
            .Where(corpse => !state.IsLootCorpseIgnored(corpse, now))
            .Where(corpse => scanRadius <= 0.0D ||
                             !corpse.DistanceToLocalPlayer.HasValue ||
                             corpse.DistanceToLocalPlayer.Value <= scanRadius)
            .Select(corpse => new
            {
                Corpse = corpse,
                Score = GetLootCorpseMatchScore(state.LootAfterKill, corpse)
            })
            .Where(match => match.Score < int.MaxValue)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Corpse.DistanceToLocalPlayer ?? double.MaxValue)
            .ThenBy(match => match.Corpse.EntityId)
            .Select(match => match.Corpse)
            .FirstOrDefault();

        if (corpse is null)
        {
            context.Logger.Info("stationary_combat.loot.no_matching_corpse", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
                ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
                ["targetName"] = state.LootAfterKill.KilledTargetName,
                ["corpseCount"] = result.Value.Count
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        state.LootAfterKill.SetTargetCorpse(corpse);
        context.Logger.Info("stationary_combat.loot.corpse_selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["corpseEntityId"] = corpse.EntityId,
            ["corpseServerObjectId"] = corpse.ServerObjectId,
            ["corpseName"] = corpse.Name,
            ["distance"] = corpse.DistanceToLocalPlayer,
            ["lootableRaw"] = corpse.LootableRaw
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootMoveToCorpseNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        if (corpse?.Position is not { } corpsePosition || player.Position is null)
        {
            return StationaryCombatBehaviorStatus.Failure;
        }

        var reachDistance = ReadLootReachDistance();
        var distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, corpsePosition);
        if (distance <= reachDistance)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            return StationaryCombatBehaviorStatus.Success;
        }

        if (DateTimeOffset.Now - state.LootAfterKill.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootMoveTimeoutMs()))
        {
            context.Logger.Warn("stationary_combat.loot.move_timeout", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse.EntityId,
                ["corpseServerObjectId"] = corpse.ServerObjectId,
                ["distance"] = Math.Round(distance, 2),
                ["reachDistance"] = reachDistance
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        await PathFollowStepAsync(context, state, player, corpsePosition, reachDistance).ConfigureAwait(false);
        return StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPressF9NodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        if (corpse is null)
        {
            return StationaryCombatBehaviorStatus.Failure;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        var result = await _input
            .PressKeyAsync("F9", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.loot.f9_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse.EntityId,
                ["corpseServerObjectId"] = corpse.ServerObjectId,
                ["error"] = result.Error
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        context.Logger.Info("stationary_combat.loot.f9_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["corpseEntityId"] = corpse.EntityId,
            ["corpseServerObjectId"] = corpse.ServerObjectId,
            ["retry"] = state.LootAfterKill.SelectRetryCount
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootVerifyLockedCorpseNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        if (corpse is null)
        {
            return StationaryCombatBehaviorStatus.Failure;
        }

        var verified = await PollLootLockedCorpseVerifyAsync(context, corpse).ConfigureAwait(false);
        if (verified)
        {
            context.Logger.Info("stationary_combat.loot.verified", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse.EntityId,
                ["corpseServerObjectId"] = corpse.ServerObjectId,
                ["retry"] = state.LootAfterKill.SelectRetryCount
            });
            return StationaryCombatBehaviorStatus.Success;
        }

        if (state.LootAfterKill.SelectRetryCount < ReadLootMaxRetries() - 1)
        {
            state.LootAfterKill.RetrySelect(DateTimeOffset.Now);
            context.Logger.Warn("stationary_combat.loot.verify_retry", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse.EntityId,
                ["corpseServerObjectId"] = corpse.ServerObjectId,
                ["retry"] = state.LootAfterKill.SelectRetryCount
            });
            return StationaryCombatBehaviorStatus.Running;
        }

        context.Logger.Warn("stationary_combat.loot.verify_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["corpseEntityId"] = corpse.EntityId,
            ["corpseServerObjectId"] = corpse.ServerObjectId,
            ["maxRetries"] = ReadLootMaxRetries()
        });
        return StationaryCombatBehaviorStatus.Failure;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPressLootKeyNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        if (corpse is null)
        {
            return StationaryCombatBehaviorStatus.Failure;
        }

        var result = await _input
            .PressKeyAsync("NumPadDecimal", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.loot.pick_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse.EntityId,
                ["corpseServerObjectId"] = corpse.ServerObjectId,
                ["error"] = result.Error
            });
            return StationaryCombatBehaviorStatus.Failure;
        }

        state.LootAfterKill.MarkLootKeyPressed();
        context.Logger.Info("stationary_combat.loot.pick_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["corpseEntityId"] = corpse.EntityId,
            ["corpseServerObjectId"] = corpse.ServerObjectId
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private static StationaryCombatBehaviorStatus TickLootWaitAfterLootNode(StationaryCombatState state)
    {
        return DateTimeOffset.Now - state.LootAfterKill.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootAfterPickWaitMs())
            ? StationaryCombatBehaviorStatus.Success
            : StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPressStopKeyNodeAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        var result = await _input
            .PressKeyAsync("S", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.loot.stop_key_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["corpseEntityId"] = corpse?.EntityId ?? 0,
                ["corpseServerObjectId"] = corpse?.ServerObjectId ?? 0,
                ["error"] = result.Error
            });
        }

        return StationaryCombatBehaviorStatus.Success;
    }

    private static StationaryCombatBehaviorStatus TickLootIgnoreCorpseNode(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        if (corpse is null)
        {
            return StationaryCombatBehaviorStatus.Failure;
        }

        state.IgnoreLootCorpse(corpse, DateTimeOffset.Now, TimeSpan.FromMilliseconds(ReadLootIgnoredCorpseTtlMs()));
        context.Logger.Info("stationary_combat.loot.corpse_ignored", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["corpseEntityId"] = corpse.EntityId,
            ["corpseServerObjectId"] = corpse.ServerObjectId,
            ["ttlMs"] = ReadLootIgnoredCorpseTtlMs(),
            ["lootKeyPressed"] = state.LootAfterKill.LootKeyPressed
        });
        return StationaryCombatBehaviorStatus.Success;
    }

    private async Task<bool> PollLootLockedCorpseVerifyAsync(
        AccountWorkerContext context,
        LootCorpseSnapshot corpse)
    {
        var verifyMs = ReadLootVerifyMs();
        var pollMs = ReadLootVerifyPollMs();
        var elapsedMs = 0;

        while (elapsedMs <= verifyMs)
        {
            var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            if (IsLockedLootCorpseMatch(lockedResult, corpse))
            {
                return true;
            }

            if (elapsedMs >= verifyMs)
            {
                break;
            }

            var waitMs = Math.Min(pollMs, verifyMs - elapsedMs);
            await DelayAsync(TimeSpan.FromMilliseconds(waitMs), context).ConfigureAwait(false);
            elapsedMs += waitMs;
        }

        return false;
    }

    private static bool IsLockedLootCorpseMatch(
        OperationResult<LockedTargetSnapshot> lockedResult,
        LootCorpseSnapshot corpse)
    {
        if (!lockedResult.Success ||
            lockedResult.Value is not { HasTarget: true } locked ||
            locked.ObjectType != LockedTargetSnapshot.MonsterObjectType ||
            locked.IsAlive)
        {
            return false;
        }

        if (locked.ServerObjectId != 0 &&
            corpse.ServerObjectId != 0 &&
            locked.ServerObjectId == corpse.ServerObjectId)
        {
            return true;
        }

        if (locked.TargetEntityId != 0 &&
            corpse.EntityId != 0 &&
            locked.TargetEntityId == corpse.EntityId)
        {
            return true;
        }

        return string.Equals(locked.Name, corpse.Name, StringComparison.Ordinal) &&
               locked.Position is { } lockedPosition &&
               corpse.Position is { } corpsePosition &&
               StationaryCombatTargetSelector.HorizontalDistance(lockedPosition, corpsePosition) <= LootPositionMatchDistance;
    }

    private static int GetLootCorpseMatchScore(
        StationaryCombatLootAfterKillState loot,
        LootCorpseSnapshot corpse)
    {
        if (loot.KilledTargetServerObjectId != 0 &&
            corpse.ServerObjectId != 0 &&
            loot.KilledTargetServerObjectId == corpse.ServerObjectId)
        {
            return 0;
        }

        if (loot.KilledTargetEntityId != 0 &&
            corpse.EntityId != 0 &&
            loot.KilledTargetEntityId == corpse.EntityId)
        {
            return 1;
        }

        if (string.Equals(loot.KilledTargetName, corpse.Name, StringComparison.Ordinal) &&
            loot.KilledTargetPosition is { } targetPosition &&
            corpse.Position is { } corpsePosition &&
            StationaryCombatTargetSelector.HorizontalDistance(targetPosition, corpsePosition) <= LootPositionMatchDistance)
        {
            return 2;
        }

        return int.MaxValue;
    }

    private static bool ShouldStartLootAfterKill(
        AccountWorkerContext context,
        LockedTargetSnapshot target)
    {
        return (context.Config.ScriptSettings?.Combat?.EnableLoot ?? true) &&
               target.IsLockedMonster &&
               !target.IsAlive;
    }

    private static void FinishLootAfterKill(
        AccountWorkerContext context,
        StationaryCombatState state,
        string reason,
        bool success)
    {
        var corpse = state.LootAfterKill.TargetCorpse;
        context.Logger.Info("stationary_combat.loot.finished", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["success"] = success,
            ["reason"] = reason,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["corpseEntityId"] = corpse?.EntityId ?? 0,
            ["corpseServerObjectId"] = corpse?.ServerObjectId ?? 0
        });
        state.ClearLootAfterKill();
        state.ClearTarget();
    }

    private async Task<TimeSpan> ReacquireCurrentFightTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome,
        OperationResult<LockedTargetSnapshot> lockedResult,
        string reason)
    {
        var targetEntityId = state.CurrentTargetEntityId != 0
            ? state.CurrentTargetEntityId
            : state.CandidateEntityId;
        if (targetEntityId == 0)
        {
            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh: true).ConfigureAwait(false);
        var target = objects.FirstOrDefault(candidate => candidate.EntityId == targetEntityId);
        if (state.IsTargetIgnored(targetEntityId))
        {
            state.ClearTarget();
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        if (target is null)
        {
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    targetEntityId,
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
                DateTimeOffset.Now);
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

        if (!IsCurrentFightTargetStillSelectable(target, home, radius, state.CurrentTargetIsMaintenanceDefense))
        {
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    targetEntityId,
                    target.Name,
                    playerDistanceFromHome,
                    radius,
                    lockedResult,
                    reason + "_target_not_selectable")
                .ConfigureAwait(false);
        }

        state.MarkCandidate(targetEntityId, DateTimeOffset.Now);
        semiAutoState.ResetAttackKeyPressThrottle();
        LogActionThrottled(context, state, "stationary_combat.target.reacquire", reason + ":" + targetEntityId, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["targetEntityId"] = targetEntityId,
            ["targetName"] = target!.Name,
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
            ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
            ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
            ["error"] = lockedResult.Error
        }, TimeSpan.FromMilliseconds(500));

        return await TickAcquireAsync(context, plan, semiAutoState, state, target).ConfigureAwait(false);
    }

    private async Task<TimeSpan> WaitForCurrentFightTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        ushort targetEntityId,
        string targetName,
        double playerDistanceFromHome,
        double radius,
        OperationResult<LockedTargetSnapshot> lockedResult,
        string reason)
    {
        state.Fighting = true;
        state.CurrentTargetEntityId = targetEntityId;
        state.MarkCandidate(targetEntityId, DateTimeOffset.Now);
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        LogActionThrottled(context, state, "stationary_combat.target.reacquire_wait", reason + ":" + targetEntityId, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["targetEntityId"] = targetEntityId,
            ["targetName"] = targetName,
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
            ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
            ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
            ["error"] = lockedResult.Error
        }, TimeSpan.FromMilliseconds(500));
        return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
    }

    private async Task<TimeSpan> TickAcquireAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (state.IsPendingTabCandidate(target.EntityId))
        {
            return await TickPendingTabVerificationAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    target,
                    lockedResult)
                .ConfigureAwait(false);
        }

        var acquiredDelay = await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                "pre_tab")
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
                ["lockedReadSuccess"] = lockedResult.Success,
                ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
                ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
                ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
                ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
                ["error"] = lockedResult.Error
            }, TimeSpan.FromMilliseconds(500));

            return await PressTabAndVerifyAsync(context, plan, semiAutoState, state, target).ConfigureAwait(false);
        }

        return MoveTickDelay;
    }

    private async Task<TimeSpan> TickPendingTabVerificationAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        OperationResult<LockedTargetSnapshot> lockedResult)
    {
        var acquiredDelay = await VerifyPendingTabTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
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
            return await PressTabAndVerifyAsync(context, plan, semiAutoState, state, target).ConfigureAwait(false);
        }

        return MoveTickDelay;
    }

    private async Task<TimeSpan> PressTabAndVerifyAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
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
            target.EntityId,
            DateTimeOffset.Now + TimeSpan.FromMilliseconds(verifyWindowMs));
        context.Logger.Info("stationary_combat.tab.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateName"] = target.Name,
            ["verifyWindowMs"] = verifyWindowMs
        });

        return await PollTabVerifyAsync(
                context,
                plan,
                semiAutoState,
                state,
                target)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> PollTabVerifyAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target)
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
        OperationResult<LockedTargetSnapshot> lockedResult,
        int delayMs)
    {
        LogTabVerify(context, state, target, lockedResult, delayMs);
        return await TryAcquireLockedTargetAsync(
                context,
                plan,
                semiAutoState,
                state,
                target,
                lockedResult,
                "after_tab")
            .ConfigureAwait(false);
    }

    private static void LogTabVerify(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        OperationResult<LockedTargetSnapshot> lockedResult,
        int delayMs)
    {
        context.Logger.Info("stationary_combat.tab.verify", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateName"] = target.Name,
            ["delayMs"] = delayMs,
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
            ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
            ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
            ["matched"] = lockedResult.Success &&
                          lockedResult.Value is { IsMonsterAlive: true } lockedTarget &&
                          lockedTarget.TargetEntityId == target.EntityId,
            ["pendingUntil"] = state.PendingTabVerifyUntil,
            ["error"] = lockedResult.Error
        });
    }

    private async Task<TimeSpan?> TryAcquireLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        OperationResult<LockedTargetSnapshot> lockedResult,
        string phase)
    {
        if (!lockedResult.Success ||
            lockedResult.Value is not { IsMonsterAlive: true } lockedTarget ||
            lockedTarget.TargetEntityId != target.EntityId)
        {
            return null;
        }

        state.Fighting = true;
        state.CurrentTargetEntityId = target.EntityId;
        state.CurrentTargetIsMaintenanceDefense = target.IsTargetingLocalPlayer;
        state.MarkCandidate(target.EntityId, DateTimeOffset.Now);
        state.ClearPendingTabVerification();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        context.Logger.Info("stationary_combat.target.acquired", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.EntityId,
            ["targetName"] = target.Name,
            ["phase"] = phase,
            ["targetingMe"] = target.IsTargetingLocalPlayer,
            ["targetServerObjectId"] = target.TargetServerObjectId
        });
        return await _semiAuto
            .TickAsync(context, plan, semiAutoState, requireCooldownCalibrationForMaintenance: true)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> IgnoreCurrentTargetAsync(
        AccountWorkerContext context,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        ushort targetEntityId,
        string targetName,
        string reason)
    {
        var now = DateTimeOffset.Now;
        var elapsedMs = state.TargetStartedAt == DateTimeOffset.MinValue
            ? 0
            : (long)Math.Max(0.0D, (now - state.TargetStartedAt).TotalMilliseconds);
        state.IgnoreTarget(targetEntityId);
        context.Logger.Info("stationary_combat.target.ignored", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = targetEntityId,
            ["targetName"] = targetName,
            ["reason"] = reason,
            ["elapsedMs"] = elapsedMs,
            ["timeoutMs"] = (long)TargetTimeout.TotalMilliseconds
        });
        state.ClearTarget();
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);
        return IdleDelay;
    }

    private static bool IsTargetTimedOut(StationaryCombatState state, DateTimeOffset now)
    {
        return state.TargetStartedAt != DateTimeOffset.MinValue &&
               now - state.TargetStartedAt >= TargetTimeout;
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

    private static bool TryGetStationaryHome(AccountWorkerContext context, out Vector3Snapshot home)
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

    private async Task<WorldObjectSnapshot?> SelectTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius)
    {
        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);

        if (state.CandidateEntityId != 0)
        {
            var candidate = objects.FirstOrDefault(
                target => target.EntityId == state.CandidateEntityId);
            if (!state.IsTargetIgnored(state.CandidateEntityId) &&
                IsCandidateStillSelectable(candidate, home, radius))
            {
                return candidate;
            }
        }

        return StationaryCombatTargetSelector.SelectNearest(
            objects.Where(target => !state.IsTargetIgnored(target.EntityId)),
            playerPosition,
            home,
            radius);
    }

    private async Task<WorldObjectSnapshot?> SelectMaintenanceDefenseTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        bool forceRefresh)
    {
        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh).ConfigureAwait(false);
        return objects
            .Where(target => !state.IsTargetIgnored(target.EntityId))
            .Where(target => target.IsTargetingLocalPlayer)
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<WorldObjectSnapshot>> RefreshWorldObjectsAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        bool forceRefresh = false)
    {
        var now = DateTimeOffset.Now;
        if (forceRefresh || state.CachedWorldObjects.Count == 0 || now - state.LastWorldScanAt >= ScanInterval)
        {
            var objectsResult = await ReadWorldObjectsAsync(context).ConfigureAwait(false);
            if (!objectsResult.Success || objectsResult.Value is null)
            {
                LogThrottled(context, state, "stationary_combat.world_objects.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = objectsResult.Error
                });
                state.CachedWorldObjects = Array.Empty<WorldObjectSnapshot>();
            }
            else
            {
                state.CachedWorldObjects = objectsResult.Value;
            }

            state.LastWorldScanAt = now;
        }

        state.PruneIgnoredTargets(state.CachedWorldObjects);
        return state.CachedWorldObjects;
    }

    private static bool IsCandidateStillSelectable(
        WorldObjectSnapshot? candidate,
        Vector3Snapshot home,
        double radius)
    {
        return candidate is { Position: not null } target &&
               StationaryCombatTargetSelector.IsSelectableMonster(target) &&
               (target.IsTargetingLocalPlayer ||
                StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) <= radius);
    }

    private static bool IsCurrentFightTargetStillSelectable(
        WorldObjectSnapshot? candidate,
        Vector3Snapshot home,
        double radius,
        bool currentTargetIsMaintenanceDefense)
    {
        return candidate is { Position: not null } target &&
               StationaryCombatTargetSelector.IsSelectableMonster(target) &&
               (currentTargetIsMaintenanceDefense ||
                target.IsTargetingLocalPlayer ||
                StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) <= radius + TargetLeashExtraDistance);
    }

    private async Task PathFollowStepAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot target,
        double reachDistance)
    {
        if (player.Position is null)
        {
            return;
        }

        var options = ReadPathFollowTurnOptions();
        var poller = EnsurePathFollowPoller(context, state, player, options);
        SetPathFollowPollTarget(poller, targetIndex: 0, target, reachDistance, options);
        if (!TryGetPathFollowPollSnapshot(poller, out var snapshot, out _))
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
            var restartMoveForLargeYaw = ShouldRestartMoveForYaw(state.IsMovingForward, snapshot.YawError, options.RestartYawThresholdDegrees);
            if (restartMoveForLargeYaw)
            {
                await StopMovementAsync(context, state).ConfigureAwait(false);
                LogPathAction(context, state, "move_restart_yaw", snapshot, 0, 0);
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

    private async Task<bool> FaceTargetStepAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        PlayerSnapshot player,
        Vector3Snapshot targetPosition,
        WorldObjectSnapshot target)
    {
        if (player.Position is null)
        {
            return false;
        }

        var options = ReadPathFollowTurnOptions() with
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
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name
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
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name,
                ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
                ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
                ["yawError"] = Math.Round(snapshot.YawError, 2),
                ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
                ["targetPitch"] = Math.Round(options.TargetPitchDegrees, 2),
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
            ["targetEntityId"] = target.EntityId,
            ["targetName"] = target.Name,
            ["currentYaw"] = Math.Round(snapshot.CurrentYaw, 2),
            ["targetYaw"] = Math.Round(snapshot.TargetYaw, 2),
            ["yawError"] = Math.Round(snapshot.YawError, 2),
            ["currentPitch"] = Math.Round(snapshot.CurrentPitch, 2),
            ["targetPitch"] = Math.Round(options.TargetPitchDegrees, 2),
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
        StationaryCombatState state)
    {
        if (state.IsRightMouseDown)
        {
            return;
        }

        var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
        state.IsRightMouseDown = down.Success;
        if (!down.Success)
        {
            context.Logger.Warn("stationary_combat.mouse_down.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["button"] = "Right",
                ["error"] = down.Error
            });
        }
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

    private async Task<CombinedTurnResult> DragCameraCombinedTwoPassFixedYawPitchAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot target,
        PathFollowTurnOptions options,
        bool keepRightDown,
        bool useFaceTargetMouseMove,
        bool leaveRightDown)
    {
        var result = new CombinedTurnResult();
        var mouseDownStartedHere = false;
        try
        {
            if (!keepRightDown)
            {
                await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
                state.IsRightMouseDown = false;
                await DelayAsync(TimeSpan.FromMilliseconds(8), context).ConfigureAwait(false);
                var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
                state.IsRightMouseDown = down.Success;
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

                await DelayAsync(TimeSpan.FromMilliseconds(options.MouseDownWarmupMs), context).ConfigureAwait(false);
            }
            else
            {
                await EnsureRightMouseDownAsync(context, state).ConfigureAwait(false);
            }

            for (var pass = 1; pass <= options.TwoPassMaxPasses; pass++)
            {
                result.Passes = pass;
                var snapshot = await ReadStableTurnSnapshotAsync(context, target, options).ConfigureAwait(false);
                if (snapshot is null)
                {
                    break;
                }

                result.FinalYawError = snapshot.YawError;
                result.FinalPitchError = snapshot.PitchError;
                if (Math.Abs(snapshot.YawError) <= options.ToleranceDegrees &&
                    Math.Abs(snapshot.PitchError) <= options.ToleranceDegrees)
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
                    ["targetPitch"] = Math.Round(options.TargetPitchDegrees, 2),
                    ["pitchError"] = Math.Round(snapshot.PitchError, 2),
                    ["rawDx"] = Math.Round(rawDx, 2),
                    ["rawDy"] = Math.Round(rawDy, 2),
                    ["dx"] = dx,
                    ["dy"] = dy,
                    ["moveCommands"] = EstimateCombinedChunkDragMoveCommandCount(dx, dy, options),
                    ["maxChunkPx"] = options.DragStepPixels,
                    ["primeTail"] = options.DragPrimePixels + "/" + options.DragTailPixels,
                    ["moveLogic"] = useFaceTargetMouseMove ? "face_target" : "fixed",
                    ["minApplied"] = minXApplied || minYApplied
                });

                await DragCameraCombinedChunksAsync(context, dx, dy, options).ConfigureAwait(false);
                result.TotalDx += dx;
                result.TotalDy += dy;
                await DelayAsync(TimeSpan.FromMilliseconds(options.MouseHoldAfterMoveMs), context).ConfigureAwait(false);

                var afterSnapshot = await WaitForCameraAnglesChangeAsync(
                        context,
                        target,
                        snapshot.CurrentYaw,
                        snapshot.CurrentPitch,
                        options)
                    .ConfigureAwait(false);
                if (afterSnapshot is null)
                {
                    break;
                }

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
                    Math.Abs(afterSnapshot.PitchError) <= options.ToleranceDegrees)
                {
                    result.Success = true;
                    break;
                }

                if (!verification.AnyImproved)
                {
                    break;
                }
            }
        }
        finally
        {
            if (mouseDownStartedHere && !keepRightDown && !leaveRightDown)
            {
                await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
                state.IsRightMouseDown = false;
            }
            else if (mouseDownStartedHere && leaveRightDown)
            {
                state.IsRightMouseDown = true;
            }

            await DelayAsync(TimeSpan.FromMilliseconds(options.DurationMs), context).ConfigureAwait(false);
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
        if (!TryGetPathFollowPollSnapshot(poller, out var snapshot, out _))
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

            if (!TryGetPathFollowPollSnapshot(poller, out snapshot, out _))
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

            if (!TryGetPathFollowPollSnapshot(poller, out snapshot, out _))
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
        PathFollowTurnOptions options)
    {
        var xChunks = BuildSignedCameraChunks(dx, options);
        var yChunks = BuildSignedCameraChunks(dy, options);
        var count = Math.Max(xChunks.Length, yChunks.Length);
        for (var i = 0; i < count; i++)
        {
            var stepX = i < xChunks.Length ? xChunks[i] : 0;
            var stepY = i < yChunks.Length ? yChunks[i] : 0;
            await SendCameraCombinedMoveStepAsync(context, stepX, stepY, options).ConfigureAwait(false);
        }
    }

    private async Task SendCameraCombinedMoveStepAsync(
        AccountWorkerContext context,
        int dx,
        int dy,
        PathFollowTurnOptions options)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var move = await _input.MoveMouseRelativeAsync(dx, dy, context.StopToken).ConfigureAwait(false);
        if (!move.Success)
        {
            context.Logger.Warn("stationary_combat.mouse_move.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = move.Error
            });
            return;
        }

        await DelayAsync(TimeSpan.FromMilliseconds(options.DragStepDelayMs), context).ConfigureAwait(false);
    }

    private static Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadPlayerAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadPlayerAsync(context.StopToken);
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadWorldObjectsAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadWorldObjectsAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLootCorpsesAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadLootCorpsesAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
    }

    private async Task<CameraTurnSnapshot?> ReadTurnSnapshotAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        PathFollowTurnOptions options)
    {
        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        return playerResult.Success && playerResult.Value is not null
            ? BuildCameraTurnSnapshot(playerResult.Value, target, options)
            : null;
    }

    private async Task<CameraTurnSnapshot?> ReadStableTurnSnapshotAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        PathFollowTurnOptions options)
    {
        await DelayAsync(TimeSpan.FromMilliseconds(options.SettleMs), context).ConfigureAwait(false);
        return await ReadTurnSnapshotAsync(context, target, options).ConfigureAwait(false);
    }

    private async Task<CameraTurnSnapshot?> WaitForCameraAnglesChangeAsync(
        AccountWorkerContext context,
        Vector3Snapshot target,
        double previousYaw,
        double previousPitch,
        PathFollowTurnOptions options)
    {
        await DelayAsync(TimeSpan.FromMilliseconds(options.AdaptiveReadSettleMs), context).ConfigureAwait(false);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, options.AdaptiveReadTimeoutMs));
        if (timeout <= TimeSpan.Zero)
        {
            var immediate = await ReadChangedCameraAnglesAsync(context, target, previousYaw, previousPitch, options).ConfigureAwait(false);
            return immediate is null
                ? null
                : await WaitForCameraAnglesStableAsync(context, target, options, immediate).ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline && !context.StopToken.IsCancellationRequested)
        {
            var observed = await ReadChangedCameraAnglesAsync(context, target, previousYaw, previousPitch, options).ConfigureAwait(false);
            if (observed is not null)
            {
                return await WaitForCameraAnglesStableAsync(context, target, options, observed).ConfigureAwait(false);
            }

            await DelayAsync(TimeSpan.FromMilliseconds(10), context).ConfigureAwait(false);
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
        CameraTurnSnapshot stableSnapshot)
    {
        var stableMs = Math.Max(0, options.AdaptiveStableMs);
        var timeoutMs = Math.Max(stableMs, options.AdaptiveStableTimeoutMs);
        if (stableMs <= 0 || timeoutMs <= 0)
        {
            return stableSnapshot;
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        var stableSince = DateTimeOffset.UtcNow;
        var currentStable = stableSnapshot;
        while (DateTimeOffset.UtcNow <= deadline && !context.StopToken.IsCancellationRequested)
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

            await DelayAsync(TimeSpan.FromMilliseconds(10), context).ConfigureAwait(false);
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
                if (initialPlayer.Position is not null)
                {
                    existing.Local = initialPlayer;
                    existing.HasLocal = true;
                    existing.Error = null;
                    existing.LastReadTime = DateTimeOffset.Now;
                    existing.ReadCount++;
                    UpdatePathFollowPollMetricsLocked(existing);
                }
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
            poller.HasLocal = initialPlayer.Position is not null;
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
            var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
            lock (poller.SyncRoot)
            {
                if (poller.StopRequested)
                {
                    return;
                }

                if (playerResult.Success && playerResult.Value?.Position is not null)
                {
                    poller.Local = playerResult.Value;
                    poller.HasLocal = true;
                    poller.Error = null;
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
                else
                {
                    poller.Error = playerResult.Error ?? "local position unavailable";
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
        if (!poller.HasLocal || poller.TargetIndex < 0 || poller.Local?.Position is null)
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
        out CameraTurnSnapshot snapshot,
        out string? error)
    {
        lock (poller.SyncRoot)
        {
            error = poller.Error;
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

    private static async Task<bool> TryWaitForPathFollowPollSnapshotAsync(
        PathFollowPollState poller,
        long previousReadCount,
        TimeSpan timeout,
        AccountWorkerContext context)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (TryGetPathFollowPollSnapshot(poller, out var snapshot, out _) &&
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

        return TryGetPathFollowPollSnapshot(poller, out _, out _);
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
        if (player.Position is null)
        {
            return null;
        }

        var currentYaw = player.CameraYawDegrees ?? player.ActorYawDegrees;
        if (currentYaw is null)
        {
            return null;
        }

        var currentPitch = player.CameraPitchDegrees ?? options.TargetPitchDegrees;
        var targetYaw = CalculateTargetYawDegrees(player.Position.Value, target);
        var yawError = NormalizeSignedDegrees(targetYaw - currentYaw.Value);
        var pitchError = options.TargetPitchDegrees - currentPitch;
        return new CameraTurnSnapshot(
            player.Position.Value,
            target,
            StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, target),
            currentYaw.Value,
            currentPitch,
            targetYaw,
            yawError,
            pitchError,
            0,
            TimeSpan.Zero);
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
        var middleChunks = BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels));
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

    private static double ReadPathFollowRestartYawThreshold()
    {
        return Math.Max(0.1D, ReadDoubleFromEnv("AION_PATH_FOLLOW_RESTART_YAW_DEG", 15.0D));
    }

    private static double ReadPathFollowDisableMoveAdjustDistance()
    {
        return Math.Max(0.0D, ReadDoubleFromEnv("AION_PATH_FOLLOW_DISABLE_MOVE_ADJUST_DISTANCE", 15.0D));
    }

    private static PathFollowTurnOptions ReadPathFollowTurnOptions()
    {
        var pixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG_ABS", 0.0D));
        if (pixelsPerDegreeAbs < 0.0001D)
        {
            pixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG", 13.0D));
        }

        if (pixelsPerDegreeAbs < 0.0001D)
        {
            pixelsPerDegreeAbs = 13.0D;
        }

        var pitchPixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG_ABS", 0.0D));
        if (pitchPixelsPerDegreeAbs < 0.0001D)
        {
            pitchPixelsPerDegreeAbs = Math.Abs(ReadDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG", 13.0D));
        }

        if (pitchPixelsPerDegreeAbs < 0.0001D)
        {
            pitchPixelsPerDegreeAbs = 13.0D;
        }

        var yawTolerance = ReadPathFollowYawTolerance();
        var fixedTargetPitch = ReadDoubleFromEnv("AION_CAMERA_FIXED_PITCH_DEG", 20.0D);
        var targetPitch = ClampDouble(ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_DEG", fixedTargetPitch), -65.0D, 85.0D);
        return new PathFollowTurnOptions
        {
            DurationMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DURATION_MS", 0), 0, 3000),
            SettleMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_SETTLE_MS", 20), 0, 500),
            MouseDownWarmupMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_DOWN_WARMUP_MS", 0), 0, 1000),
            MouseHoldAfterMoveMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_HOLD_AFTER_MOVE_MS", 0), 0, 1000),
            MinCorrectionPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MIN_CORRECTION_PIXELS", 70), 0, 500),
            ToleranceDegrees = Math.Min(Math.Max(0.1D, ReadDoubleFromEnv("AION_FACE_TARGET_TOLERANCE_DEG", 2.5D)), yawTolerance),
            YawToleranceDegrees = yawTolerance,
            MicroYawToleranceDegrees = ReadPathFollowMicroYawTolerance(),
            RestartYawThresholdDegrees = ReadPathFollowRestartYawThreshold(),
            DisableMoveAdjustDistance = ReadPathFollowDisableMoveAdjustDistance(),
            PitchToleranceDegrees = Math.Max(0.5D, ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_TOLERANCE_DEG", 5.0D)),
            TargetPitchDegrees = targetPitch,
            PixelsPerDegreeAbs = pixelsPerDegreeAbs,
            PitchPixelsPerDegreeAbs = pitchPixelsPerDegreeAbs,
            DragPrimePixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_PRIME_PIXELS", 5), 0, 50),
            DragTailPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_TAIL_PIXELS", 5), 0, 50),
            DragStepPixels = ClampInt(Math.Abs(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_PX", 20)), 1, 500),
            DragStepDelayMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_DELAY_MS", 0), 0, 50),
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

    private static int ReadPathFollowTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("AION_PATH_FOLLOW_TICK_MS", 10), 1, 2000);
    }

    private static int ReadDeathRecoveryTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_DEATH_RECOVERY_TICK_MS", 200), 40, 2000);
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

    private static int ReadLootVerifyMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_VERIFY_MS", 100), 0, 2000);
    }

    private static int ReadLootVerifyPollMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_VERIFY_POLL_MS", 20), 1, 500);
    }

    private static int ReadLootAfterKillWaitMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", 100), 0, 10_000);
    }

    private static int ReadLootAfterPickWaitMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", 500), 0, 10_000);
    }

    private static int ReadLootIgnoredCorpseTtlMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_IGNORED_CORPSE_TTL_MS", 600_000), 1_000, 3_600_000);
    }

    private static int ReadLootMaxRetries()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_MAX_RETRIES", 3), 1, 10);
    }

    private static int ReadLootKeyHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_KEY_HOLD_MS", 25), 1, 1000);
    }

    private static int ReadLootMoveTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_MOVE_TIMEOUT_MS", 15_000), 1_000, 120_000);
    }

    private static double ReadLootReachDistance()
    {
        return Math.Max(0.2D, ReadDoubleFromEnv("ROADHOG_LOOT_REACH_DISTANCE", DefaultLootReachDistance));
    }

    private static double ReadLootScanRadius()
    {
        return Math.Max(0.0D, ReadDoubleFromEnv("ROADHOG_LOOT_SCAN_RADIUS", DefaultLootScanRadius));
    }

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
        public bool HasLocal { get; set; }
        public PlayerSnapshot? Local { get; set; }
        public string? Error { get; set; }
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
        public double PixelsPerDegreeAbs { get; init; }
        public double PitchPixelsPerDegreeAbs { get; init; }
        public int DragPrimePixels { get; init; }
        public int DragTailPixels { get; init; }
        public int DragStepPixels { get; init; }
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

    private sealed record CameraTurnSnapshot(
        Vector3Snapshot PlayerPosition,
        Vector3Snapshot TargetPosition,
        double DistanceToTarget,
        double CurrentYaw,
        double CurrentPitch,
        double TargetYaw,
        double YawError,
        double PitchError,
        long ReadCount,
        TimeSpan Age);

    private sealed class CombinedTurnResult
    {
        public bool Success { get; set; }
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
