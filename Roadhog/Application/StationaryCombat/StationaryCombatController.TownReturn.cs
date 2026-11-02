using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    // Decide whether startup needs a recall before pet maintenance can block normal work.
    // Pending transitions already own the worker; after landing, summon before route movement.
    public async Task<TimeSpan?> TryPrepareStartupReturnAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        if (state.StartupRecoveryChecked || state.StartupRecoveryActive || state.StartupTownReturnPending ||
            state.TopLevelState != StationaryCombatTopLevelState.Normal || state.Fighting ||
            state.LootAfterKill.Active || state.BagCleanup.Active || state.CleanupReturnToCombatActive)
            return null;

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!player.IsAlive || !player.IsSpiritmaster || player.Position is not { } position)
            return null;
        var homeResult = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        if (!homeResult.Success || homeResult.Value is null)
            return null;

        var home = homeResult.Value.Position;
        return await TickStartupRecoveryAsync(context, plan, semiAutoState, state, player, position, home,
            Math.Max(1.0D, context.Config.ScriptSettings?.Combat?.StationaryCombatRadius ?? 1.0D),
            StationaryCombatTargetSelector.HorizontalDistance(position, home),
            deferRouteMovement: true).ConfigureAwait(false);
    }

    // Run before cleanup scheduling, channel switching and pet maintenance.
    public async Task<TimeSpan?> TryTickTownReturnAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        if (context.GameSessionRequests.ScriptRestartRequestId.HasValue) return IdleDelay;
        if (state.StuckTownReturnPending)
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            return await TickStuckTownReturnAsync(context, state).ConfigureAwait(false);
        }
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
            AbandonReturnNavigation(context, state, "返回路线异常");
        }
        return null;
    }

    private async Task PrepareForTownReturnAsync(AccountWorkerContext context, SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        state.ClearPostCombatReturn();
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
        var phase = await TickRetryableTownReturnAsync(context, state, transition).ConfigureAwait(false);
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
        var phase = await TickRetryableTownReturnAsync(context, state, transition).ConfigureAwait(false);
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

    private static void AbandonReturnNavigation(AccountWorkerContext context, StationaryCombatState state, string reason)
    {
        state.ReturnNavigationBlocked = false;
        state.CompleteCleanupReturnToCombat();
        state.ClearStartupRecovery();
        state.ClearPostCombatReturn();
        state.ReturningHome = false;
        state.ResetReturnHomeStuckTracking();
        // Failure backoff, not a skill cooldown: normal combat/maintenance may continue.
        state.ReturnNavigationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        state.NoKillRecovery.Postpone(DateTimeOffset.Now, TimeSpan.FromSeconds(30));
        context.RuntimeStates.MarkWarning(context.Config.AccountName, reason + "，放弃本次返回分支，继续主流程。");
        context.Logger.Warn("town_return.branch_abandoned", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName, ["reason"] = reason });
    }
}
