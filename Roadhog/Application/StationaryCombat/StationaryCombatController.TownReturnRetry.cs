using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private async Task<TownReturnPhase?> TickRetryableTownReturnAsync(
        AccountWorkerContext context, StationaryCombatState state, TownReturnTransition transition)
    {
        // Also bound loading, a wrong destination and an outstanding provider read. None of
        // those states permits blind retry input, but none may own the worker indefinitely.
        var remaining = TownReturnTransition.ConfirmationWait * TownReturnTransition.MaximumAttempts -
                        (_townReturnTime.GetUtcNow() - transition.StartedAt);
        if (remaining <= TimeSpan.Zero)
        {
            RestartAfterTimedOutTownReturn(context, state, transition);
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken);
        deadline.CancelAfter(remaining);
        using var reads = SnapshotReadBudget.EnterCancellation(deadline.Token);
        TownReturnPhase phase;
        try
        {
            phase = await transition.TickAsync(context, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!context.StopToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            RestartAfterTimedOutTownReturn(context, state, transition);
            return null;
        }

        context.StopToken.ThrowIfCancellationRequested();
        if (!transition.TryReserveRetry(_townReturnTime.GetUtcNow())) return phase;

        var key = context.Config.ScriptSettings?.Paths.TownReturnKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            AbandonReturnNavigation(context, state, "回城重试按键未配置");
            return null;
        }

        string? error = null;
        try
        {
            var result = await _input.PressKeyAsync(key, StartupTownReturnHoldDuration, context.StopToken).ConfigureAwait(false);
            if (!result.Success) error = result.Error ?? "回城按键发送失败";
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !context.StopToken.IsCancellationRequested)
        {
            error = ex.Message;
        }
        context.Logger.Warn("town_return.retry", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["pathName"] = transition.Destination?.Name,
            ["key"] = key, ["attempt"] = transition.AttemptCount,
            ["maximumAttempts"] = TownReturnTransition.MaximumAttempts,
            ["confirmationWaitMs"] = TownReturnTransition.ConfirmationWait.TotalMilliseconds,
            ["success"] = error == null, ["error"] = error
        });
        context.RuntimeStates.MarkWarning(context.Config.AccountName,
            $"回城尚未确认，已进行第 {transition.AttemptCount}/{TownReturnTransition.MaximumAttempts} 次尝试，等待 60 秒确认。" +
            (error == null ? string.Empty : "按键发送失败：" + error));
        return phase;
    }

    private static void RestartAfterTimedOutTownReturn(AccountWorkerContext context,
        StationaryCombatState state, TownReturnTransition transition)
    {
        context.StopToken.ThrowIfCancellationRequested();
        var requestId = context.GameSessionRequests.RequestScriptRestart();
        state.ClearStartupRecovery();
        state.ClearPostCombatReturn();
        state.CompleteCleanupReturnToCombat();
        state.NoKillRecovery.ResetWatch(DateTimeOffset.Now);
        state.ReturningHome = false;
        context.Logger.Warn("town_return.retry_exhausted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["pathName"] = transition.Destination?.Name,
            ["restartRequestId"] = requestId,
            ["attempts"] = transition.AttemptCount, ["maximumAttempts"] = TownReturnTransition.MaximumAttempts,
            ["phase"] = transition.Phase.ToString(), ["sawLoading"] = transition.SawLoading
        });
        context.RuntimeStates.MarkWarning(context.Config.AccountName,
            $"回城确认超时（已尝试 {transition.AttemptCount}/{TownReturnTransition.MaximumAttempts} 次），等待自动重启当前账号脚本。");
    }
}
