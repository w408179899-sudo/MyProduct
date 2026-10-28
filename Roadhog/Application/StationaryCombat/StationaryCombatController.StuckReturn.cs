using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private static readonly TimeSpan StuckTownReturnTimeout = TimeSpan.FromSeconds(90);

    private async Task RecoverBlockedReturnRouteAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        if (state.StartupTownReturnPending || DateTimeOffset.UtcNow < state.ReturnNavigationRetryAfter) return;

        await StopSoloJumpAsync(state, "return_route_stuck").ConfigureAwait(false);
        StopNextTargetPreAim(context, state, "return_route_stuck", clearCandidate: true);
        await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
        await StopMovementAsync(context, state, releaseRightMouse: true).ConfigureAwait(false);
        StopPathFollowPoller(state);
        try
        {
            var key = context.Config.ScriptSettings?.Paths?.TownReturnKey?.Trim();
            if (string.IsNullOrWhiteSpace(key) || _pathStore is null)
            {
                AbandonReturnNavigation(context, state, "卡住回城缺少按键或路线配置");
                return;
            }

            var path = await _pathStore.LoadAsync(GetRevivePathName(context), context.StopToken).ConfigureAwait(false);
            if (!path.Success || path.Value is not { Points.Count: >= 2 } route)
            {
                AbandonReturnNavigation(context, state, "卡住回城路线不可用");
                return;
            }

            var scene = (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            if (scene.IsReady && scene.Player!.IsDead)
            {
                state.EnterDeathRecovery(DateTimeOffset.Now);
                return;
            }
            if (!scene.IsReady)
            {
                AbandonReturnNavigation(context, state, "卡住回城时场景正在切换");
                return;
            }

            var transition = new TownReturnTransition();
            transition.Start(scene, route, DateTimeOffset.UtcNow);
            var points = route.Points.Select(point => point.ToVector3()).ToArray();
            state.NoKillRecovery.Postpone(DateTimeOffset.Now, TimeSpan.FromSeconds(30));
            state.ClearPostCombatReturn();
            state.ReturningHome = false;
            state.ClearTarget();
            state.StartStartupTownReturn(route.Name, points, scene.Player!.Position!.Value, DateTimeOffset.Now);
            state.StuckTownReturnPending = true;
            state.ReturnTransition = transition;
            var press = await _input.PressKeyAsync(key, StartupTownReturnHoldDuration, context.StopToken).ConfigureAwait(false);
            if (!press.Success)
            {
                AbandonReturnNavigation(context, state, "卡住回城按键失败：" + press.Error);
                return;
            }
            context.Logger.Warn("town_return.stuck_recovery.started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["pathName"] = route.Name,
                ["key"] = key, ["position"] = scene.Player.Position, ["timeoutMs"] = StuckTownReturnTimeout.TotalMilliseconds
            });
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        {
            AbandonReturnNavigation(context, state, "卡住回城失败：" + ex.Message);
        }
    }

    private async Task<TimeSpan> TickStuckTownReturnAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var transition = state.ReturnTransition!;
        var remaining = StuckTownReturnTimeout - (DateTimeOffset.UtcNow - transition.StartedAt);
        if (remaining <= TimeSpan.Zero)
        {
            AbandonReturnNavigation(context, state, "卡住回城确认超时");
            return IdleDelay;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken);
        deadline.CancelAfter(remaining);
        using var reads = SnapshotReadBudget.EnterCancellation(deadline.Token);
        try
        {
            var phase = await transition.TickAsync(context, deadline.Token).ConfigureAwait(false);
            if (phase == TownReturnPhase.Dead) state.EnterDeathRecovery(DateTimeOffset.Now);
            else if (phase == TownReturnPhase.Arrived)
            {
                var name = state.StartupRecoveryPathName;
                var points = state.StartupRecoveryPoints;
                state.NoKillRecovery.ResetWatch(DateTimeOffset.Now);
                state.SetStationaryHomeFromRevivePath(name, points[^1], points.Count);
                state.StartStartupRecovery(name, points, 0);
                state.ReturnNavigationRetryAfter = DateTimeOffset.MinValue;
                state.ReturningHome = false;
                state.ClearTarget();
                context.Logger.Info("town_return.stuck_recovery.arrived", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["pathName"] = name,
                    ["startPointIndex"] = 0, ["mapId"] = transition.Arrival!.Channel!.MapId
                });
            }
        }
        catch (SnapshotReadBudgetExceededException) when (!deadline.IsCancellationRequested)
        {
            // A single foreground read yield must not abandon an already submitted recall.
            throw;
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        {
            AbandonReturnNavigation(context, state, deadline.IsCancellationRequested
                ? "卡住回城确认超时" : "卡住回城确认失败：" + ex.Message);
        }
        return IdleDelay;
    }
}
