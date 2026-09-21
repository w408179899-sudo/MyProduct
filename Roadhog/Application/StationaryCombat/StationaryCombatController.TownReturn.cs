using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    // Run before cleanup scheduling, channel switching and pet maintenance.
    public async Task<TimeSpan?> TryTickTownReturnAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        if (state.StartupTownReturnPending)
            return await TickStartupReturnTransitionAsync(context, state).ConfigureAwait(false);
        if (state.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle)
            return await TickNoKillReturnTransitionAsync(context, state).ConfigureAwait(false);
        if (_bagCleanup != null && state.BagCleanup.Step is
            (BagCleanupStep.WaitTownReturnSettle or BagCleanupStep.WaitReturnToReviveSettle))
        {
            try
            {
                var result = await _bagCleanup.TickAfterLootAsync(context, state.BagCleanup).ConfigureAwait(false);
                if (result.Status == BagCleanupTickStatus.Completed) state.StartCleanupReturnToCombat();
            }
            catch (CleanupDeathInterruptionException) { state.EnterDeathRecovery(DateTimeOffset.Now); }
            return IdleDelay;
        }
        if (state.ReturnNavigationBlocked)
        {
            var scene = (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            if (scene.IsReady && scene.Player!.IsDead) state.EnterDeathRecovery(DateTimeOffset.Now);
            else context.RuntimeStates.MarkWarning(context.Config.AccountName, "返回路线持续卡住，已停止移动，请检查路线或重新启动任务。");
            return IdleDelay;
        }
        return null;
    }

    private async Task PrepareForTownReturnAsync(AccountWorkerContext context, SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        semiAutoState.ResetAttackKeyPressThrottle();
        await StopSoloJumpAsync(state, "town_return").ConfigureAwait(false);
        StopNextTargetPreAim(context, state, "town_return", clearCandidate: true);
        await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
        await StopMovementAsync(context, state, releaseRightMouse: true).ConfigureAwait(false);
        StopPathFollowPoller(state);
    }

    private async Task<TimeSpan> TickStartupReturnTransitionAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var transition = state.ReturnTransition ?? throw new InvalidOperationException("回城确认状态缺失。");
        var phase = await transition.TickAsync(context).ConfigureAwait(false);
        if (phase == TownReturnPhase.Dead) state.EnterDeathRecovery(DateTimeOffset.Now);
        else if (phase == TownReturnPhase.Arrived)
        {
            var name = state.StartupRecoveryPathName;
            var points = state.StartupRecoveryPoints;
            state.StartStartupRecovery(name, points, 0);
            state.ReturningHome = false;
            state.ClearTarget();
            context.Logger.Info("stationary_combat.startup_recovery.return.verify.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["pathName"] = name,
                ["startPointIndex"] = 0, ["pathPointCount"] = points.Count,
                ["mapId"] = transition.Arrival!.Channel!.MapId, ["sawLoading"] = transition.SawLoading
            });
        }
        return IdleDelay;
    }

    private async Task<TimeSpan> TickNoKillReturnTransitionAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var recovery = state.NoKillRecovery;
        var transition = recovery.ReturnTransition ?? throw new InvalidOperationException("无击杀回城确认状态缺失。");
        var phase = await transition.TickAsync(context).ConfigureAwait(false);
        if (phase == TownReturnPhase.Dead) state.EnterDeathRecovery(DateTimeOffset.Now);
        else if (phase == TownReturnPhase.Arrived)
        {
            var points = recovery.RevivePathPoints;
            state.SetStationaryHomeFromRevivePath(recovery.RevivePathName, points[^1], points.Count);
            state.StartStartupRecovery(recovery.RevivePathName, points, 0);
            recovery.StartRevivePath(DateTimeOffset.Now);
            context.Logger.Info("stationary_combat.no_kill.return.verify.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["revivePathName"] = recovery.RevivePathName,
                ["startPointIndex"] = 0, ["pathPointCount"] = points.Count,
                ["mapId"] = transition.Arrival!.Channel!.MapId
            });
        }
        return IdleDelay;
    }

    private async Task RecoverBlockedReturnRouteAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        await StopSoloJumpAsync(state, "return_route_stuck").ConfigureAwait(false);
        StopNextTargetPreAim(context, state, "return_route_stuck", clearCandidate: true);
        await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
        await StopMovementAsync(context, state, releaseRightMouse: true).ConfigureAwait(false);
        StopPathFollowPoller(state);
        if (!state.ReturnRouteRejoinAttempted && _pathStore != null)
        {
            state.ReturnRouteRejoinAttempted = true;
            var path = await _pathStore.LoadAsync(GetRevivePathName(context), context.StopToken).ConfigureAwait(false);
            var scene = (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            if (path.Value is { Points.Count: >= 2 } route && scene.IsReady && !scene.Player!.IsDead &&
                (route.MapId is not > 0 || route.MapId == scene.Channel!.MapId))
            {
                var points = route.Points.Select(p => p.ToVector3()).ToArray();
                var position = scene.Player!.Position!.Value;
                var index = FindNearestPathPointIndex(position, points, 15);
                if (index >= 0 && Math.Abs(position.Z - points[index].Z) <= 10)
                {
                    state.StartStartupRecovery(route.Name, points, index);
                    state.ReturningHome = false;
                    state.ResetReturnHomeStuckTracking();
                    context.Logger.Warn("town_return.route_rejoined", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["pathName"] = route.Name, ["pointIndex"] = index });
                    return;
                }
            }
        }
        state.ReturnNavigationBlocked = true;
        context.RuntimeStates.MarkWarning(context.Config.AccountName, "返回路线持续卡住，已停止移动，请检查路线或重新启动任务。");
    }
}
