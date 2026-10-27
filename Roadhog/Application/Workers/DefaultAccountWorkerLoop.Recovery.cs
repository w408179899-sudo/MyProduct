using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Input;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop
{
    private async Task AbandonCleanupAsync(AccountWorkerContext context, StationaryCombatState state,
        CleanupRequest request, Exception error)
    {
        // Remove ownership before recovery input: even recovery failure must not
        // retain an idle request or replay an unconfirmed transaction.
        request.Failure = error.Message;
        context.CleanupRequests.Abandon();
        context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
        context.RuntimeStates.MarkWarning(context.Config.AccountName, "本次清包失败，返回挂机主流程：" + error.Message);
        context.Logger.Warn("cleanup_workflow.failed_continuing", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["error"] = error.Message,
            ["requestId"] = request.RequestId, ["stage"] = request.PreparationStage.ToString(),
            ["standaloneShop"] = request.StandaloneShop, ["groceryShop"] = request.GroceryShop,
            ["arrowRestock"] = request.ArrowRestock, ["automaticRetryAfterMs"] = 30000
        });
        state.PathCombat.Reset();
        state.BagCleanup.Reset();
        state.CompleteCleanupReturnToCombat();
        state.ClearStartupRecovery();
        state.ClearPostCombatReturn();
        state.ReturnNavigationBlocked = false;
        state.ReturningHome = false;
        await _stationaryCombat.SetChannelSwitchPendingAsync(context, state, false);
        await ReleaseFailedBranchInputAsync(context, state);
    }

    private async Task ReleaseFailedBranchInputAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (_keyboard is IInputStateReset reset)
                await reset.ReleaseAllAsync(deadline.Token).WaitAsync(deadline.Token);
            else
            {
                foreach (var key in new[] { "W", "A", "S", "D", "ShiftKey", "ControlKey" })
                    await _keyboard.KeyUpAsync(key, deadline.Token).WaitAsync(deadline.Token);
                foreach (var button in new[] { RoadhogMouseButton.Left, RoadhogMouseButton.Right })
                    await _keyboard.MouseUpAsync(button, deadline.Token).WaitAsync(deadline.Token);
            }
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        {
            context.Logger.Warn("worker.branch.release_failed", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["error"] = ex.Message });
        }
        finally
        {
            deadline.Cancel();
            state.IsMovingForward = false;
            state.IsRightMouseDown = false;
        }
    }
}
