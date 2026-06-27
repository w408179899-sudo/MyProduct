using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatController
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan TabInterval = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan MoveTickDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);
    private const double ReturnStopDistance = 2.0D;
    private const double AcquireDistance = 25.0D;
    private const double TargetLeashExtraDistance = 5.0D;

    private readonly IKeyboardInput _input;
    private readonly SemiAutoCombatController _semiAuto;

    public StationaryCombatController(IKeyboardInput input, SemiAutoCombatController semiAuto)
    {
        _input = input;
        _semiAuto = semiAuto;
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
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
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
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
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

        if (state.Fighting)
        {
            return await TickFightAsync(
                context,
                plan,
                semiAutoState,
                state,
                home,
                radius,
                playerDistanceFromHome).ConfigureAwait(false);
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
                await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
                await PathFollowStepAsync(context, state, player, home, ReturnStopDistance).ConfigureAwait(false);
                return MoveTickDelay;
            }
        }

        var target = await SelectTargetAsync(context, state, playerPosition, home, radius).ConfigureAwait(false);
        if (target?.Position is null)
        {
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            await StopMovementAsync(context, state).ConfigureAwait(false);
            state.CandidateEntityId = 0;
            return IdleDelay;
        }

        var previousCandidateEntityId = state.CandidateEntityId;
        state.CandidateEntityId = target.EntityId;
        var targetPosition = target.Position.Value;
        var targetDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(targetPosition, home);
        var playerDistanceToTarget = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, targetPosition);
        if (previousCandidateEntityId != target.EntityId)
        {
            context.Logger.Info("stationary_combat.target.selected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.EntityId,
                ["targetName"] = target.Name,
                ["playerDistanceToTarget"] = Math.Round(playerDistanceToTarget, 2),
                ["targetDistanceFromHome"] = Math.Round(targetDistanceFromHome, 2),
                ["radius"] = Math.Round(radius, 2)
            });
        }

        if (targetDistanceFromHome > radius)
        {
            await StopMovementAsync(context, state).ConfigureAwait(false);
            state.CandidateEntityId = 0;
            return IdleDelay;
        }

        var isFacingTarget = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
        if (!isFacingTarget)
        {
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            return MoveTickDelay;
        }

        if (playerDistanceToTarget > AcquireDistance)
        {
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            await PathFollowStepAsync(context, state, player, targetPosition, AcquireDistance).ConfigureAwait(false);
            return MoveTickDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await TickAcquireAsync(context, plan, semiAutoState, state, target).ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickFightAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        Vector3Snapshot home,
        double radius,
        double playerDistanceFromHome)
    {
        var targetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!targetResult.Success || targetResult.Value is null)
        {
            state.ClearTarget();
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            return IdleDelay;
        }

        var target = targetResult.Value;
        if (!target.IsMonsterAlive || target.TargetEntityId != state.CurrentTargetEntityId)
        {
            state.ClearTarget();
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return playerDistanceFromHome > radius ? MoveTickDelay : IdleDelay;
        }

        if (target.Position is not null &&
            StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home) > radius + TargetLeashExtraDistance)
        {
            context.Logger.Info("stationary_combat.target.leash_drop", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.TargetEntityId,
                ["targetName"] = target.Name
            });
            state.ClearTarget();
            await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
            await StopMovementAsync(context, state).ConfigureAwait(false);
            return IdleDelay;
        }

        await StopMovementAsync(context, state).ConfigureAwait(false);
        return await _semiAuto.TickAsync(context, plan, semiAutoState).ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickAcquireAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
        var lockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
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

        await semiAutoState.StopAttackKeyLoopAsync().ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        if (now - state.LastTabAt >= TabInterval)
        {
            state.LastTabAt = now;
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
            }
            else
            {
                context.Logger.Info("stationary_combat.tab.pressed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["candidateEntityId"] = target.EntityId,
                    ["candidateName"] = target.Name
                });

                var verifyDelayMs = ReadTabVerifyDelayMs();
                await DelayAsync(TimeSpan.FromMilliseconds(verifyDelayMs), context).ConfigureAwait(false);
                var afterTabLockedResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
                context.Logger.Info("stationary_combat.tab.verify", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["candidateEntityId"] = target.EntityId,
                    ["candidateName"] = target.Name,
                    ["delayMs"] = verifyDelayMs,
                    ["lockedReadSuccess"] = afterTabLockedResult.Success,
                    ["lockedEntityId"] = afterTabLockedResult.Value?.TargetEntityId ?? 0,
                    ["lockedName"] = afterTabLockedResult.Value?.Name ?? string.Empty,
                    ["lockedAlive"] = afterTabLockedResult.Value?.IsMonsterAlive ?? false,
                    ["lockedHp"] = afterTabLockedResult.Value?.CurrentHp ?? 0,
                    ["matched"] = afterTabLockedResult.Success &&
                                  afterTabLockedResult.Value is { IsMonsterAlive: true } afterTabLockedTarget &&
                                  afterTabLockedTarget.TargetEntityId == target.EntityId,
                    ["error"] = afterTabLockedResult.Error
                });

                acquiredDelay = await TryAcquireLockedTargetAsync(
                        context,
                        plan,
                        semiAutoState,
                        state,
                        target,
                        afterTabLockedResult,
                        "after_tab")
                    .ConfigureAwait(false);
                if (acquiredDelay is not null)
                {
                    return acquiredDelay.Value;
                }
            }
        }

        return MoveTickDelay;
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
        state.CandidateEntityId = target.EntityId;
        await StopMovementAsync(context, state).ConfigureAwait(false);
        context.Logger.Info("stationary_combat.target.acquired", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.EntityId,
            ["targetName"] = target.Name,
            ["phase"] = phase
        });
        return await _semiAuto.TickAsync(context, plan, semiAutoState).ConfigureAwait(false);
    }

    private async Task<WorldObjectSnapshot?> SelectTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius)
    {
        var now = DateTimeOffset.Now;
        if (state.CachedWorldObjects.Count == 0 || now - state.LastWorldScanAt >= ScanInterval)
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

        return StationaryCombatTargetSelector.SelectNearest(
            state.CachedWorldObjects,
            playerPosition,
            home,
            radius);
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
        await EnsureMoveForwardAsync(context, state).ConfigureAwait(false);
        LogActionThrottled(context, state, "stationary_combat.path_follow", "move_forward", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = "move_forward",
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

        var options = ReadPathFollowTurnOptions();
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

    private async Task EnsureMoveForwardAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        if (state.IsMovingForward)
        {
            return;
        }

        await _input.KeyUpAsync("W", context.StopToken).ConfigureAwait(false);
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
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", 60), 0, 500);
    }

    private static int ReadPathFollowTickMs()
    {
        return ClampInt(ReadRawIntFromEnv("AION_PATH_FOLLOW_TICK_MS", 10), 1, 2000);
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

    private sealed class PathFollowTurnOptions
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
