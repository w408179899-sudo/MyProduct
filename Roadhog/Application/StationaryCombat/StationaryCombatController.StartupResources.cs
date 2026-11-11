using Roadhog.Application.SemiAuto;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public async Task<TimeSpan?> TryPrepareStartupResourceReturnAsync(AccountWorkerContext context,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        var startup = context.StartupResourceReturn;
        if (startup.Handled)
            return state.StartupTownReturnPending && ReferenceEquals(state.ReturnTransition, startup.Transition)
                ? await TickStartupReturnTransitionAsync(context, state).ConfigureAwait(false)
                : null;

        if (startup.Player is null)
        {
            var scene = (await context.Snapshots.ReadChannelTransitionAsync()
                .WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            if (!scene.IsReady) return IdleDelay;
            startup.Observe(scene.Player!);
        }
        if (!startup.RequiresReturn)
        {
            startup.Handled = true;
            return null;
        }

        try
        {
            var key = context.Config.ScriptSettings?.Paths.TownReturnKey?.Trim();
            var pathName = GetRevivePathName(context);
            if (string.IsNullOrWhiteSpace(key) || _pathStore is null || string.IsNullOrWhiteSpace(pathName))
                return AbandonStartupResourceReturn(context, state, "上线低血蓝回城缺少回城键或返回路线");

            var pathResult = await _pathStore.LoadAsync(pathName, context.StopToken).ConfigureAwait(false);
            if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
                return AbandonStartupResourceReturn(context, state, "上线低血蓝回城的返回路线不可用");

            await PrepareForTownReturnAsync(context, semiAutoState, state).ConfigureAwait(false);
            var departure = (await context.Snapshots.ReadChannelTransitionAsync()
                .WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            if (!departure.IsReady) return IdleDelay;
            if (!departure.Player!.IsAlive)
            {
                startup.Handled = true;
                return null;
            }

            var revivePoints = points.Select(point => point.ToVector3()).ToArray();
            var transition = new TownReturnTransition();
            transition.Start(departure, pathResult.Value, _townReturnTime.GetUtcNow());
            context.StopToken.ThrowIfCancellationRequested();
            // Reserve the one-shot action and transition before submitting input.
            startup.Handled = true;
            state.SetStationaryHomeFromRevivePath(pathName, revivePoints[^1], revivePoints.Length);
            state.StartStartupTownReturn(pathName, revivePoints, departure.Player.Position!.Value, DateTimeOffset.Now);
            state.ReturnTransition = transition;
            startup.Transition = transition;
            state.ReturningHome = false;
            state.ClearTarget();
            var press = await _input.PressKeyAsync(key, StartupTownReturnHoldDuration, context.StopToken)
                .ConfigureAwait(false);
            if (!press.Success)
                return AbandonStartupResourceReturn(context, state, "上线低血蓝回城按键失败：" + press.Error);

            var player = startup.Player!;
            context.Logger.Warn("worker.startup.low_resources.return", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["key"] = key, ["pathName"] = pathName,
                ["hp"] = player.CurrentHp, ["maxHp"] = player.MaxHp,
                ["mp"] = player.CurrentMp, ["maxMp"] = player.MaxMp,
                ["hpPercent"] = player.HpPercent, ["mpPercent"] = player.MpPercent
            });
            return IdleDelay;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AbandonStartupResourceReturn(context, state, "上线低血蓝回城失败：" + ex.Message);
        }
    }

    private static TimeSpan AbandonStartupResourceReturn(AccountWorkerContext context,
        StationaryCombatState state, string reason)
    {
        context.StartupResourceReturn.Handled = true;
        AbandonReturnNavigation(context, state, reason);
        return IdleDelay;
    }
}
