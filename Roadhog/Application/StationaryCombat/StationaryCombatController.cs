using Roadhog.Application.Input;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Team;
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
    private static readonly TimeSpan DeathRevivePreClickPause = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TargetTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultMissingFightTargetTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NoKillTownReturnHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan DefaultNoKillTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultNoKillTownReturnSettleDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DefaultNoKillRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultManualPathPlayerReadRetryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultManualPathPlayerReadRetryInterval = TimeSpan.FromMilliseconds(200);
    private const double ReturnStopDistance = 2.0D;
    private const double AcquireDistance = 25.0D;
    private const double TargetLeashExtraDistance = 5.0D;
    private const double PreLockFaceYawToleranceDegrees = 25.0D;
    private const double DefaultPathFollowReachDistance = 5.0D;
    private const double RevivePathAggressiveClearRadius = 10.0D;
    private const double DefaultYawPixelsPerDegree = 11.0D;
    private const double DefaultPitchPixelsPerDegree = 13.0D;
    private const int DefaultReviveClickX = PathScriptSettings.DefaultDeathReviveClickX;
    private const int DefaultReviveClickY = PathScriptSettings.DefaultDeathReviveClickY;
    private const int DefaultReviveFallbackClickX = 550;
    private const int DefaultReviveFallbackClickY = 375;
    private const int DefaultReviveThirdClickX = 690;
    private const int DefaultReviveThirdClickY = 468;
    private const int DefaultPostReviveScrollCount = 30;
    private const int DefaultPostReviveScrollDelta = -1;
    private const int DefaultPostCombatMaintenanceRoundLimit = 8;
    private const int CameraTurnRecoveryFailureThreshold = 3;
    private const int CameraTurnRecoveryReleaseMs = 80;
    private const int CameraTurnRecoveryWarmupMs = 80;
    private const ushort NpcEntityType = 3;

    private readonly IKeyboardInput _input;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly ISharedPathStore? _pathStore;
    private readonly BagCleanupController? _bagCleanup;

    public StationaryCombatController(
        IKeyboardInput input,
        SemiAutoCombatController semiAuto,
        ISharedPathStore? pathStore = null)
    {
        _input = input;
        _semiAuto = semiAuto;
        _pathStore = pathStore;
        _bagCleanup = pathStore is null
            ? null
            : new BagCleanupController(input, pathStore, ExecutePathOnceAsync);
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
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

        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            context.RuntimeStates.MarkWarning(
                context.Config.AccountName,
                Roadhog.Application.RuntimeWarningText.FromPlayerReadFailure(playerResult.Error));
            LogThrottled(context, state, "stationary_combat.player.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = playerResult.Error
            });
            return IdleDelay;
        }

        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        var player = playerResult.Value;
        var playerPosition = player.Position.Value;
        var playerDistanceFromHome = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, home);
        await RefreshLocalCombatSideAsync(context, plan, state, player).ConfigureAwait(false);

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

        if (state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
            (!state.DeathRecovery.RevivePathLeaderSiphonActive || player.IsDead))
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
            return noKillRecoveryDelay.Value;
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

            target = await SelectTargetAsync(
                    context,
                    state,
                    playerPosition,
                    home,
                    radius,
                    combat.ContestMonster)
                .ConfigureAwait(false);
        }

        if (target?.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            state.ClearTarget();
            if (combat.ReturnHomeWhenNoTarget && playerDistanceFromHome > ReturnStopDistance)
            {
                LogActionThrottled(context, state, "stationary_combat.no_target.return_home", "home", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["homeDistance"] = Math.Round(playerDistanceFromHome, 2),
                    ["stopDistance"] = Math.Round(ReturnStopDistance, 2),
                    ["homeX"] = Math.Round(home.X, 2),
                    ["homeY"] = Math.Round(home.Y, 2)
                }, TimeSpan.FromMilliseconds(500));
                await PathFollowStepAsync(context, state, player, home, ReturnStopDistance).ConfigureAwait(false);
                return MoveTickDelay;
            }

            await StopMovementAsync(context, state).ConfigureAwait(false);
            return IdleDelay;
        }

        var candidateChanged = state.MarkCandidate(target, DateTimeOffset.Now);
        if (state.IsTeamLeaderProtectionTarget(target))
        {
            state.CurrentTargetIsMaintenanceDefense = true;
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
            targetDistanceFromHome > radius)
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
                    "not_locked")
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
                phase: "pre_move")
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
        var combat = context.Config.ScriptSettings?.Combat ?? new CombatScriptSettings();
        var radius = ResolvePathCombatRadius(combat);

        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            context.RuntimeStates.MarkWarning(
                context.Config.AccountName,
                Roadhog.Application.RuntimeWarningText.FromPlayerReadFailure(playerResult.Error));
            LogThrottled(context, state, "stationary_combat.path_combat.player.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = playerResult.Error
            });
            return IdleDelay;
        }

        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        var player = playerResult.Value;
        if (player.Position is null)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);
            LogThrottled(context, state, "stationary_combat.path_combat.position.missing", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["combatPathName"] = GetCombatPathName(context),
                ["reason"] = "player_position_missing"
            });
            return IdleDelay;
        }

        var playerPosition = player.Position.Value;
        await RefreshLocalCombatSideAsync(context, plan, state, player).ConfigureAwait(false);

        if (player.IsDead && state.TopLevelState != StationaryCombatTopLevelState.DeathRecovery)
        {
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
            return await TickPlayerLifeGuardAsync(
                    context,
                    plan,
                    semiAutoState,
                    state,
                    followRevivePath: true)
                .ConfigureAwait(false) ?? IdleDelay;
        }

        if (state.LootAfterKill.Active)
        {
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
            return noKillRecoveryDelay.Value;
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
            var initialPlayerResult = await ReadManualPathPlayerWithRetryAsync(
                    context,
                    state,
                    displayName,
                    pointIndex: -1)
                .ConfigureAwait(false);
            if (!initialPlayerResult.Success || initialPlayerResult.Value?.Position is null)
            {
                return OperationResult.Fail(initialPlayerResult.Error ?? "Player position is missing.");
            }

            var initialPosition = initialPlayerResult.Value.Position.Value;
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

                var playerResult = await ReadManualPathPlayerWithRetryAsync(
                        context,
                        state,
                        displayName,
                        state.PathCombat.PointIndex)
                    .ConfigureAwait(false);
                if (!playerResult.Success || playerResult.Value?.Position is null)
                {
                    return OperationResult.Fail(playerResult.Error ?? "Player position is missing.");
                }

                var player = playerResult.Value;
                var playerPosition = player.Position.Value;
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

    private async Task<OperationResult<PlayerSnapshot>> ReadManualPathPlayerWithRetryAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        string pathName,
        int pointIndex)
    {
        var failedAt = DateTimeOffset.MinValue;
        var attempt = 0;
        var lastError = "Player position is missing.";
        while (!context.StopToken.IsCancellationRequested)
        {
            var result = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (result.Success && result.Value?.Position is not null)
            {
                if (attempt > 0)
                {
                    context.Logger.Info("manual_path.player_read.recovered", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["pathName"] = pathName,
                        ["pointIndex"] = pointIndex,
                        ["attempts"] = attempt,
                        ["failedMs"] = (long)Math.Max(0.0D, (DateTimeOffset.Now - failedAt).TotalMilliseconds)
                    });
                }

                return result;
            }

            attempt++;
            failedAt = failedAt == DateTimeOffset.MinValue ? DateTimeOffset.Now : failedAt;
            lastError = result.Error ?? "Player position is missing.";
            await StopMovementAsync(context, state).ConfigureAwait(false);
            StopPathFollowPoller(state);

            var failedFor = DateTimeOffset.Now - failedAt;
            var timeout = ReadManualPathPlayerReadRetryTimeout();
            context.Logger.Warn("manual_path.player_read.retry", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["pathName"] = pathName,
                ["pointIndex"] = pointIndex,
                ["attempt"] = attempt,
                ["failedMs"] = (long)Math.Max(0.0D, failedFor.TotalMilliseconds),
                ["timeoutMs"] = (long)timeout.TotalMilliseconds,
                ["error"] = lastError
            });
            if (failedFor >= timeout)
            {
                return OperationResult<PlayerSnapshot>.Fail(
                    "Player read failed continuously during path execution. attempts=" + attempt +
                    ", timeoutMs=" + (long)timeout.TotalMilliseconds +
                    ", error=" + lastError);
            }

            await DelayAsync(ReadManualPathPlayerReadRetryInterval(), context).ConfigureAwait(false);
        }

        context.StopToken.ThrowIfCancellationRequested();
        return OperationResult<PlayerSnapshot>.Fail(lastError);
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
            context.RuntimeStates.MarkWarning(
                context.Config.AccountName,
                Roadhog.Application.RuntimeWarningText.FromPlayerReadFailure(playerResult.Error));
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

        context.RuntimeStates.ClearWarning(context.Config.AccountName);
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
                playerDistanceFromHome = player.Position is not null
                    ? StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, home)
                    : 0.0D;
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

        if (player.Position is null)
        {
            LogActionThrottled(context, state, "stationary_combat.death_recovery.path_wait", "player_no_position", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "player_no_position"
            }, TimeSpan.FromSeconds(1));
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
                    player.Position.Value,
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

        if (state.LootAfterKill.Active)
        {
            await TickLootAfterKillAsync(context, plan, semiAutoState, state, player).ConfigureAwait(false);
            return StationaryCombatBehaviorStatus.Running;
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

        var monitor = new TeamMonitor(context.GameApi, context.Logger);
        var snapshotResult = await monitor
            .ReadSnapshotAsync(CreateReadContext(context), context.StopToken)
            .ConfigureAwait(false);
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            return ClearDeathRecoveryRevivePathLeaderSiphon(
                context,
                state,
                "snapshot_failed",
                null,
                team.GroupDistanceMeters,
                snapshotResult.Error);
        }

        var leader = snapshotResult.Value.LeaderMember;
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
                playerPosition,
                forceRefresh: semiAutoState.IsMaintenanceResting)
            .ConfigureAwait(false);
        if (target is not null)
        {
            if (semiAutoState.IsMaintenanceResting)
            {
                await _semiAuto
                    .CancelMaintenanceRestAsync(context, semiAutoState, "path_combat_targeting_monster_detected")
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

            target = await SelectTargetAsync(
                    context,
                    state,
                    playerPosition,
                    playerPosition,
                    radius,
                    allowClaimedByOther,
                    forceRefresh: true)
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
        state.CurrentTargetBypassesHomeLeash = state.IsTeamLeaderProtectionTarget(target);
        if (state.IsTeamLeaderProtectionTarget(target))
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
        string recoveryPhase)
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

        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition,
                forceRefresh: true)
            .ConfigureAwait(false);
        var isRevivePathClearTarget = false;
        if (target?.Position is null && IsRevivePathRecoveryPhase(recoveryPhase))
        {
            target = await SelectRevivePathAggressiveClearTargetAsync(
                    context,
                    state,
                    playerPosition,
                    forceRefresh: true)
                .ConfigureAwait(false);
            isRevivePathClearTarget = target?.Position is not null;
        }

        if (target?.Position is null)
        {
            return null;
        }

        if (semiAutoState.IsMaintenanceResting)
        {
            await _semiAuto
                .CancelMaintenanceRestAsync(context, semiAutoState, recoveryPhase + "_targeting_monster_detected")
                .ConfigureAwait(false);
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        var candidateChanged = state.MarkCandidate(target, DateTimeOffset.Now);
        state.CurrentTargetIsRevivePathClear = isRevivePathClearTarget;
        state.CurrentTargetBypassesHomeLeash = isRevivePathClearTarget || state.IsTeamLeaderProtectionTarget(target);
        if (state.IsTeamLeaderProtectionTarget(target))
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
                ["revivePathClear"] = isRevivePathClearTarget
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
                    targetResult,
                    "target_read_failed")
                .ConfigureAwait(false);
        }

        var target = targetResult.Value;
        if (!state.IsCurrentTarget(target))
        {
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
                    targetResult,
                    "target_mismatch")
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
        state.ResetCurrentTargetMissing();
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
            return await WaitForCurrentFightTargetAsync(
                    context,
                    semiAutoState,
                    state,
                    target.TargetEntityId,
                    target.ServerObjectId,
                    target.Name,
                    playerDistanceFromHome,
                    radius,
                    targetResult,
                    "target_outside_leash")
                .ConfigureAwait(false);
        }

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
        return await _semiAuto
            .TickAsync(context, plan, semiAutoState, requireCooldownCalibrationForMaintenance: true)
            .ConfigureAwait(false);
    }

    private async Task<TimeSpan> TickLootAfterKillAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
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
        if (lockedResult.Success && lockedResult.Value is { HasTarget: true } locked)
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
        else if (!lockedResult.Success)
        {
            return new LootAttemptEligibility(
                false,
                "locked_target",
                "lootability_read_failed",
                state.LootAfterKill.KilledTargetLootableRaw,
                state.LootAfterKill.KilledTargetInteractionState,
                lockedResult.Error);
        }

        var corpsesResult = await ReadLootCorpsesAsync(context).ConfigureAwait(false);
        if (!corpsesResult.Success)
        {
            return new LootAttemptEligibility(
                false,
                "loot_corpses",
                "lootability_read_failed",
                state.LootAfterKill.KilledTargetLootableRaw,
                state.LootAfterKill.KilledTargetInteractionState,
                corpsesResult.Error);
        }

        var corpse = corpsesResult.Value?.FirstOrDefault(corpse => IsSameLootTarget(state.LootAfterKill, corpse));
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
        if (lockedResult.Success && lockedResult.Value is { HasTarget: true } locked)
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
        else if (lockedResult.Success)
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
            var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (playerResult.Success && playerResult.Value?.Position is { } playerPosition)
            {
                distance = StationaryCombatTargetSelector.HorizontalDistance(playerPosition, position);
            }
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
            var handled = await _semiAuto
                .TryHandleMaintenanceAsync(
                    context,
                    semiAutoState,
                    currentPlayer,
                    allowSitMaintenance: false,
                    clearSitWhenDisallowed: false,
                    beforeMaintenanceKeyPress: async () =>
                    {
                        semiAutoState.ResetAttackKeyPressThrottle();
                        await StopMovementAsync(context, state).ConfigureAwait(false);
                        StopPathFollowPoller(state);
                    },
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: true,
                    runTiming: MaintenanceRuleRunTiming.AfterCombat,
                    includeAlwaysRules: true)
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

            var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (!playerResult.Success || playerResult.Value is null)
            {
                context.Logger.Warn(logEventPrefix + ".post_combat_maintenance_player_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetEntityId,
                    ["targetServerObjectId"] = targetServerObjectId,
                    ["targetName"] = targetName,
                    ["iteration"] = iteration,
                    ["error"] = playerResult.Error
                });
                break;
            }

            currentPlayer = playerResult.Value;
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
        if (player.Position is not { } playerPosition)
        {
            state.CompleteCleanupReturnToCombat();
            context.Logger.Warn("stationary_combat.bag_cleanup.return_to_combat.skipped", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "player_position_missing",
                ["revivePathName"] = GetRevivePathName(context)
            });
            return StationaryCombatBehaviorStatus.Success;
        }

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

            if (player.Position is not { } startPosition)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "player_position_missing",
                    "Player position before town return is not available.");
            }

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

            var afterResult = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (!afterResult.Success || afterResult.Value?.Position is not { } endPosition)
            {
                return PostponeNoKillRecovery(
                    context,
                    state,
                    now,
                    "town_return_end_position_missing",
                    afterResult.Error ?? "Player position after town return is not available.");
            }

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
            player = afterResult.Value;
        }

        if (state.NoKillRecovery.Step != StationaryCombatNoKillRecoveryStep.FollowRevivePath ||
            player.Position is not { } playerPosition ||
            state.NoKillRecovery.RevivePathPoints.Count < 2)
        {
            return IdleDelay;
        }

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
        if (player.Position is not { } playerPosition)
        {
            return false;
        }

        var target = await SelectMaintenanceDefenseTargetAsync(
                context,
                state,
                playerPosition,
                forceRefresh: true)
            .ConfigureAwait(false);
        if (target?.Position is null)
        {
            return false;
        }

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopMovementAsync(context, state).ConfigureAwait(false);
        StopPathFollowPoller(state);

        FinishLootAfterKill(
            context,
            state,
            "defense_target_before_post_combat_maintenance",
            success: true);
        state.Fighting = true;
        state.SetCurrentTarget(target);
        state.CurrentTargetIsMaintenanceDefense = true;
        state.MarkCandidate(target, DateTimeOffset.Now);
        state.FacedCandidateEntityId = 0;
        state.ClearPendingTabVerification();

        context.Logger.Info("stationary_combat.loot.post_combat_maintenance_postponed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.EntityId,
            ["serverObjectId"] = target.ServerObjectId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["targetingServerObjectId"] = target.TargetServerObjectId,
            ["targetingMe"] = IsTargetingLocalSide(target, state)
        });
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

    private static bool IsLockedTargetEmpty(OperationResult<LockedTargetSnapshot> lockedResult)
    {
        return lockedResult.Success &&
               (lockedResult.Value is null || !lockedResult.Value.HasTarget);
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
        context.Logger.Info("stationary_combat.loot.finished", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["success"] = success,
            ["reason"] = reason,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["targetName"] = state.LootAfterKill.KilledTargetName
        });
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
        OperationResult<LockedTargetSnapshot> lockedResult,
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

        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh: true).ConfigureAwait(false);
        var target = objects.FirstOrDefault(candidate =>
            StationaryCombatState.IsSameTarget(
                targetEntityId,
                targetServerObjectId,
                candidate.EntityId,
                candidate.ServerObjectId));
        if (state.IsTargetIgnored(targetEntityId, targetServerObjectId))
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

        state.ResetCurrentTargetMissing();
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
            IsClaimedByOther(target, state))
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
            state.ResetCurrentTargetMissing();
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
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetingServerObjectId"] = lockedResult.Value?.TargetServerObjectId ?? 0,
            ["lockedLocalServerObjectId"] = lockedResult.Value?.LocalServerObjectId ?? 0,
            ["lockedTargetingMe"] = lockedResult.Value is null
                ? false
                : IsTargetingLocalPlayerByServerObjectId(lockedResult.Value),
            ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
            ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
            ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
            ["error"] = lockedResult.Error
        }, TimeSpan.FromMilliseconds(500));

        if (state.CurrentTargetIsMaintenanceDefense &&
            target.Position is { } targetPosition &&
            player.Position is { } playerPosition)
        {
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
        OperationResult<LockedTargetSnapshot> lockedResult,
        string reason)
    {
        if (!IsLockedTargetEmpty(lockedResult))
        {
            state.ResetCurrentTargetMissing();
            return null;
        }

        var now = DateTimeOffset.Now;
        var missingSince = state.MarkCurrentTargetMissing(targetEntityId, targetServerObjectId, now);
        var missingFor = now - missingSince;
        var timeoutMs = ReadMissingFightTargetTimeoutMs();
        if (missingFor < TimeSpan.FromMilliseconds(timeoutMs))
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
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetingServerObjectId"] = lockedResult.Value?.TargetServerObjectId ?? 0,
            ["elapsedMs"] = (long)Math.Max(0.0D, missingFor.TotalMilliseconds),
            ["timeoutMs"] = timeoutMs,
            ["error"] = lockedResult.Error
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
        OperationResult<LockedTargetSnapshot> lockedResult,
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
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetingServerObjectId"] = lockedResult.Value?.TargetServerObjectId ?? 0,
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
                ["lockedReadSuccess"] = lockedResult.Success,
                ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
                ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
                ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
                ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
                ["error"] = lockedResult.Error
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
        OperationResult<LockedTargetSnapshot> lockedResult,
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
        OperationResult<LockedTargetSnapshot> lockedBeforeResult,
        Vector3Snapshot home,
        double radius)
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
            target,
            DateTimeOffset.Now + TimeSpan.FromMilliseconds(verifyWindowMs),
            lockedBeforeResult.Value);
        context.Logger.Info("stationary_combat.tab.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
            ["candidateName"] = target.Name,
            ["previousLockedEntityId"] = lockedBeforeResult.Value?.TargetEntityId ?? 0,
            ["previousLockedServerObjectId"] = lockedBeforeResult.Value?.ServerObjectId ?? 0,
            ["previousLockedTargetServerObjectId"] = lockedBeforeResult.Value?.ServerObjectId ?? 0,
            ["previousLockedTargetingServerObjectId"] = lockedBeforeResult.Value?.TargetServerObjectId ?? 0,
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
        OperationResult<LockedTargetSnapshot> lockedResult,
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

        var wrongLockDelay = await TryHandleUnchangedWrongLockedTargetAsync(
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
        if (wrongLockDelay is not null)
        {
            return wrongLockDelay.Value;
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

    private async Task PressForwardIfTabLockMissAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        OperationResult<LockedTargetSnapshot> lockedResult,
        int delayMs)
    {
        if (!lockedResult.Success)
        {
            return;
        }

        if (lockedResult.Value is { IsMonsterAlive: true })
        {
            return;
        }

        if (lockedResult.Value is { IsLockedMonster: true, HasKnownHealth: true, CurrentHp: 0 } lockedTarget)
        {
            var reason = ResolveTabCorpseNudgeReason(state, lockedTarget);
            await PressForwardForTabCorpseAsync(context, state, target, lockedTarget, delayMs, reason)
                .ConfigureAwait(false);
            return;
        }

        if (lockedResult.Value is { HasTarget: true })
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

    private async Task<TimeSpan?> TryHandleUnchangedWrongLockedTargetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState state,
        WorldObjectSnapshot target,
        OperationResult<LockedTargetSnapshot> lockedResult,
        Vector3Snapshot home,
        double radius,
        int delayMs)
    {
        if (!lockedResult.Success ||
            lockedResult.Value is not { IsMonsterAlive: true } lockedTarget ||
            lockedTarget.TargetEntityId == 0 ||
            StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId) ||
            state.PendingTabPreviousLockedEntityId == 0 ||
            !StationaryCombatState.IsSameTarget(
                state.PendingTabPreviousLockedEntityId,
                state.PendingTabPreviousLockedServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId))
        {
            return null;
        }

        var lockedWorldTarget = await FindSelectableLockedWorldTargetAsync(
                context,
                state,
                lockedTarget,
                home,
                radius)
            .ConfigureAwait(false);
        if (lockedWorldTarget is null)
        {
            return null;
        }

        if (state.HasWrongLockNudge(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId))
        {
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
                    phase: "after_tab_fallback")
                .ConfigureAwait(false);
        }

        if (!state.TryMarkPendingTabWrongLockNudged())
        {
            return null;
        }

        var holdMs = ReadTabWrongLockNudgeKeyHoldMs();
        var result = await _input
            .PressKeyAsync("W", TimeSpan.FromMilliseconds(holdMs), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("stationary_combat.tab.wrong_lock_nudge_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["candidateEntityId"] = target.EntityId,
                ["candidateServerObjectId"] = target.ServerObjectId,
                ["candidateTargetServerObjectId"] = target.ServerObjectId,
                ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
                ["candidateName"] = target.Name,
                ["lockedEntityId"] = lockedTarget.TargetEntityId,
                ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
                ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
                ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
                ["lockedName"] = lockedTarget.Name,
                ["delayMs"] = delayMs,
                ["holdMs"] = holdMs,
                ["error"] = result.Error
            });
            return MoveTickDelay;
        }

        state.MarkWrongLockNudged(
            target.EntityId,
            target.ServerObjectId,
            lockedTarget.TargetEntityId,
            lockedTarget.ServerObjectId);
        state.ClearPendingTabVerification();
        state.LastTabAt = DateTimeOffset.MinValue;
        context.Logger.Info("stationary_combat.tab.wrong_lock_nudge_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["candidateEntityId"] = target.EntityId,
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
            ["candidateName"] = target.Name,
            ["lockedEntityId"] = lockedTarget.TargetEntityId,
            ["lockedServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetServerObjectId"] = lockedTarget.ServerObjectId,
            ["lockedTargetingServerObjectId"] = lockedTarget.TargetServerObjectId,
            ["lockedName"] = lockedTarget.Name,
            ["lockedWorldServerObjectId"] = lockedWorldTarget.ServerObjectId,
            ["lockedWorldTargetServerObjectId"] = lockedWorldTarget.ServerObjectId,
            ["lockedWorldTargetingServerObjectId"] = lockedWorldTarget.TargetServerObjectId,
            ["lockedWorldName"] = lockedWorldTarget.Name,
            ["delayMs"] = delayMs,
            ["holdMs"] = holdMs
        });

        return MoveTickDelay;
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
            ["candidateServerObjectId"] = target.ServerObjectId,
            ["candidateTargetServerObjectId"] = target.ServerObjectId,
            ["candidateTargetingServerObjectId"] = target.TargetServerObjectId,
            ["candidateName"] = target.Name,
            ["delayMs"] = delayMs,
            ["lockedReadSuccess"] = lockedResult.Success,
            ["lockedEntityId"] = lockedResult.Value?.TargetEntityId ?? 0,
            ["lockedServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetServerObjectId"] = lockedResult.Value?.ServerObjectId ?? 0,
            ["lockedTargetingServerObjectId"] = lockedResult.Value?.TargetServerObjectId ?? 0,
            ["lockedName"] = lockedResult.Value?.Name ?? string.Empty,
            ["lockedAlive"] = lockedResult.Value?.IsMonsterAlive ?? false,
            ["lockedHp"] = lockedResult.Value?.CurrentHp ?? 0,
            ["matched"] = lockedResult.Success &&
                          lockedResult.Value is { IsMonsterAlive: true } lockedTarget &&
                          StationaryCombatState.IsSameTarget(
                              target.EntityId,
                              target.ServerObjectId,
                              lockedTarget.TargetEntityId,
                              lockedTarget.ServerObjectId),
            ["previousLockedEntityId"] = state.PendingTabPreviousLockedEntityId,
            ["previousLockedServerObjectId"] = state.PendingTabPreviousLockedServerObjectId,
            ["previousLockedTargetServerObjectId"] = state.PendingTabPreviousLockedServerObjectId,
            ["wrongLockNudged"] = lockedResult.Value is null
                ? false
                : state.HasWrongLockNudge(
                    target.EntityId,
                    target.ServerObjectId,
                    lockedResult.Value.TargetEntityId,
                    lockedResult.Value.ServerObjectId),
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
        Vector3Snapshot home,
        double radius,
        bool allowLockedFallback,
        string phase)
    {
        if (!lockedResult.Success ||
            lockedResult.Value is not { IsMonsterAlive: true } lockedTarget)
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

        state.Fighting = true;
        var acquiredServerObjectId = acquiredTarget.ServerObjectId != 0
            ? acquiredTarget.ServerObjectId
            : lockedTarget.ServerObjectId;
        var acquiredEntityId = lockedTarget.TargetEntityId != 0
            ? lockedTarget.TargetEntityId
            : acquiredTarget.EntityId;
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
            .TickAsync(context, plan, semiAutoState, requireCooldownCalibrationForMaintenance: true)
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
        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh: true).ConfigureAwait(false);
        var lockedWorldTarget = objects.FirstOrDefault(target =>
            StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                lockedTarget.TargetEntityId,
                lockedTarget.ServerObjectId));
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
            IsTargetingSelf(target))
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

    private static double ResolvePathCombatRadius(CombatScriptSettings combat)
    {
        return Math.Max(1.0D, combat.PathCombatRadius);
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

    private async Task<WorldObjectSnapshot?> SelectTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        Vector3Snapshot home,
        double radius,
        bool allowClaimedByOther,
        bool forceRefresh = false)
    {
        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh).ConfigureAwait(false);
        var preferAggressiveMonsters = PrefersAggressiveMonsters(context);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);

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
            .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters));
        if (!allowClaimedByOther)
        {
            candidates = candidates.Where(target => !IsClaimedByOther(target, state));
        }

        var selected = StationaryCombatTargetSelector.SelectNearest(
            candidates,
            playerPosition,
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
                activeMonsterNameFilters);
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
        IReadOnlyList<string> activeMonsterNameFilters)
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
        var activeFiltered = monstersWithPosition
            .Where(target => IsActiveMonsterFiltered(target, activeMonsterNameFilters))
            .ToArray();
        var claimedByOther = allowClaimedByOther
            ? Array.Empty<WorldObjectSnapshot>()
            : monstersWithPosition
                .Where(target => !state.IsTargetIgnored(target))
                .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters))
                .Where(target => IsClaimedByOther(target, state))
                .ToArray();
        var finalCandidates = monstersWithPosition
            .Where(target => !state.IsTargetIgnored(target))
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
            .Select(target => FormatTargetScanSample(target, playerPosition, home, radius, state, activeMonsterNameFilters, allowClaimedByOther))
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
        bool allowClaimedByOther)
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

    private async Task<WorldObjectSnapshot?> SelectMaintenanceDefenseTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        bool forceRefresh)
    {
        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh).ConfigureAwait(false);
        var teamThreat = await SelectTeamLeaderProtectionTargetAsync(
                context,
                state,
                objects,
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

        var localSideThreat = objects
            .Where(target => IsTargetingLocalSide(target, state))
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
        if (localSideThreat is not null)
        {
            state.ClearTeamLeaderProtectionTarget();
        }

        return localSideThreat;
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

        var monitor = new TeamMonitor(context.GameApi, context.Logger);
        var snapshotResult = await monitor
            .ReadSnapshotAsync(CreateReadContext(context), context.StopToken)
            .ConfigureAwait(false);
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            LogActionThrottled(context, state, "stationary_combat.team_leader.snapshot.failed", "snapshot", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = snapshotResult.Error
            }, TimeSpan.FromSeconds(3));
            return null;
        }

        return TeamLeaderProtectionSelector.SelectThreat(
            snapshotResult.Value,
            objects,
            playerPosition,
            team.GroupDistanceMeters);
    }

    private async Task<WorldObjectSnapshot?> SelectRevivePathAggressiveClearTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        Vector3Snapshot playerPosition,
        bool forceRefresh)
    {
        var objects = await RefreshWorldObjectsAsync(context, state, forceRefresh).ConfigureAwait(false);
        var activeMonsterNameFilters = GetActiveMonsterNameFilters(context);
        return objects
            .Where(target => !state.IsTargetIgnored(target))
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => target.IsAggressiveToPlayer)
            .Where(target => !IsClaimedByOther(target, state))
            .Where(target => !IsActiveMonsterFiltered(target, activeMonsterNameFilters))
            .Where(target => StationaryCombatTargetSelector.HorizontalDistance(
                target.Position!.Value,
                playerPosition) <= RevivePathAggressiveClearRadius)
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
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

    private static bool PrefersAggressiveMonsters(AccountWorkerContext context)
    {
        return context.Config.ScriptSettings?.Combat?.PreferAggressiveMonsters == true;
    }

    private static bool IsRevivePathRecoveryPhase(string recoveryPhase)
    {
        return string.Equals(recoveryPhase, "death_recovery", StringComparison.Ordinal) ||
               string.Equals(recoveryPhase, "startup_recovery", StringComparison.Ordinal);
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
        double reachDistance)
    {
        var freshPlayerResult = await ReadPathFollowPlayerAsync(context).ConfigureAwait(false);
        if (freshPlayerResult.Success && freshPlayerResult.Value?.Position is not null)
        {
            player = freshPlayerResult.Value;
        }

        if (player.Position is null)
        {
            return;
        }

        var options = ReadPathFollowTurnOptions(context.Config.ScriptSettings?.Combat);
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
            if (snapshot.DistanceToTarget <= reachDistance)
            {
                TryMarkPathFollowArrivedNow(poller, out _, out _);
                await StopMovementAsync(context, state).ConfigureAwait(false);
                LogPathAction(context, state, "arrived", snapshot, 0, 0);
                StopPathFollowPoller(state);
                return;
            }

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
            ["targetEntityId"] = target.EntityId,
            ["targetName"] = target.Name,
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

    private async Task<bool> ForceResetRightMouseAfterTurnFailuresAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        int failureCount)
    {
        var up = await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
        state.IsRightMouseDown = false;
        await DelayAsync(TimeSpan.FromMilliseconds(CameraTurnRecoveryReleaseMs), context).ConfigureAwait(false);

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
            ["mouseDownSuccess"] = down.Success
        };
        if (up.Success && down.Success)
        {
            context.Logger.Info("stationary_combat.right_mouse.recovered", fields);
        }
        else
        {
            fields["error"] = down.Success ? up.Error : down.Error;
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

                await DragCameraCombinedChunksAsync(context, dx, dy, options).ConfigureAwait(false);
                result.MouseMoveAttempted |= dx != 0 || dy != 0;
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

            if (result.Success || result.AngleChangeObserved)
            {
                state.ResetCameraTurnNoChange();
            }
            else if (result.MouseMoveAttempted)
            {
                var failureCount = state.MarkCameraTurnNoChange();
                if (failureCount >= CameraTurnRecoveryFailureThreshold)
                {
                    mouseDownStartedHere = await ForceResetRightMouseAfterTurnFailuresAsync(
                            context,
                            state,
                            failureCount)
                        .ConfigureAwait(false);
                    state.ResetCameraTurnNoChange();
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
        var primeMoveCommands = EstimateCombinedPrimeMoveCommandCount(dx, dy, options);
        var stepDelay = TimeSpan.FromMilliseconds(options.DragStepDelayMs);
        for (var i = 0; i < count; i++)
        {
            var stepX = i < xChunks.Length ? xChunks[i] : 0;
            var stepY = i < yChunks.Length ? yChunks[i] : 0;
            await SendCameraCombinedMoveStepAsync(context, stepX, stepY, options).ConfigureAwait(false);
            if (i >= primeMoveCommands)
            {
                await DelayAsync(stepDelay, context).ConfigureAwait(false);
            }
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
    }

    private static Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadPlayerAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadPlayerAsync(context.StopToken);
    }

    private static Task<OperationResult<PlayerSnapshot>> ReadPathFollowPlayerAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadPlayerAsync(CreateReadContext(context, bypassMemoryCache: true), context.StopToken)
            : context.GameApi.ReadPlayerAsync(context.StopToken);
    }

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

        var rosterResult = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        if (!rosterResult.Success || rosterResult.Value is null)
        {
            return;
        }

        var roster = rosterResult.Value;
        state.LocalCombatSideServerObjectId = roster.LocalServerObjectId;
        var localPet = roster.LocalPlayerPet;
        state.LocalCombatSidePetServerObjectId = SpiritmasterCombatContext.IsConfirmedLocalSummonedPet(localPet)
            ? localPet.Pet.ServerObjectId
            : 0;
    }

    private static Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadSummonedPetRosterAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadSummonedPetRosterAsync(context.StopToken);
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

    private static GameApiReadContext CreateReadContext(
        AccountWorkerContext context,
        bool bypassMemoryCache = false)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName,
            bypassMemoryCache);
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
            var playerResult = await ReadPathFollowPlayerAsync(context).ConfigureAwait(false);
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

        var worldPitch = CalculateWorldPitchDegrees(player.Position.Value, target);
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

    private static int ReadTabWrongLockNudgeKeyHoldMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_STATIONARY_TAB_WRONG_LOCK_NUDGE_HOLD_MS", 1000), 1, 3000);
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

    private static int ReadFaceTargetTimeoutMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_FACE_TARGET_TIMEOUT_MS", 10_000), 1, 60_000);
    }

    private static int ReadMissingFightTargetTimeoutMs()
    {
        return ClampInt(
            ReadRawIntFromEnv("ROADHOG_MISSING_FIGHT_TARGET_TIMEOUT_MS", (int)DefaultMissingFightTargetTimeout.TotalMilliseconds),
            0,
            60_000);
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

    private static TimeSpan ReadManualPathPlayerReadRetryTimeout()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv(
                "ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS",
                (int)DefaultManualPathPlayerReadRetryTimeout.TotalMilliseconds),
            0,
            60_000));
    }

    private static TimeSpan ReadManualPathPlayerReadRetryInterval()
    {
        return TimeSpan.FromMilliseconds(ClampInt(
            ReadRawIntFromEnv(
                "ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS",
                (int)DefaultManualPathPlayerReadRetryInterval.TotalMilliseconds),
            0,
            5_000));
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
