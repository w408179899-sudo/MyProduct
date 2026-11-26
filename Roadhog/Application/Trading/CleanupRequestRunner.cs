using Roadhog.Application.Workers;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.BagCleanup;
using Roadhog.Core.Api;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.Trading;

internal enum CleanupRequestTickResult { Yield, RestartPending }

// Session-owned deadlines; OperationActive is reset by the worker at each tick boundary.
internal sealed class CleanupLoopProgress
{
    public DateTimeOffset NextCheck { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NextScheduleCheck { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NextScheduledAttempt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastCleanup { get; set; } = DateTimeOffset.MinValue;
    public bool CheckLifeAfterYield { get; set; }
    public bool OperationActive { get; set; }
}

internal interface ICleanupRequestActions
{
    Task<bool> PrepareAsync(CleanupRequest request);
    Task StopJumpAsync();
    Task RunWorkflowAsync(CleanupRequest request);
    Task FinishGroceryAfterFailureAsync(CleanupRequest request, Exception error);
    Task ClearChannelSwitchAsync();
    Task AbandonAsync(CleanupRequest request, Exception error);
}

internal static class CleanupRequestRunner
{
    public static async Task<CleanupRequestTickResult> RunAsync(AccountWorkerContext context,
        StationaryCombatState stationaryCombatState, CleanupRequest cleanup,
        CleanupLoopProgress progress, ICleanupRequestActions actions)
    {
        try
        {
            context.StopToken.ThrowIfCancellationRequested();
            var prepared = await actions.PrepareAsync(cleanup);
            context.StopToken.ThrowIfCancellationRequested();
            if (prepared)
            {
                if (cleanup.StandaloneShop || cleanup.GroceryShop || cleanup.ItemTransfer)
                    await actions.StopJumpAsync().ConfigureAwait(false);
                context.StopToken.ThrowIfCancellationRequested();
                try
                {
                    progress.OperationActive = true;
                    cleanup.WorkflowStarted = true;
                    await actions.RunWorkflowAsync(cleanup);
                }
                catch (SnapshotReadBudgetExceededException) { throw; }
                catch (CleanupExecutionFailedException) { throw; }
                catch (CleanupSessionInterruptionException) { throw; }
                catch (Exception ex) when (!context.StopToken.IsCancellationRequested && cleanup.GroceryShop &&
                    cleanup.Settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction == GroceryPostSaleAction.NpcSell &&
                    (cleanup.SoldOutConfirmed || cleanup.GroceryProcessingCompleted))
                {
                    await actions.FinishGroceryAfterFailureAsync(cleanup, ex);
                }
                progress.OperationActive = false;
                context.StopToken.ThrowIfCancellationRequested();
                if (cleanup.RequestsRestart)
                {
                    if (cleanup.GroceryTrigger == GroceryShopTrigger.Scheduled &&
                        StationaryCombatController.IsFollowingRevivePath(stationaryCombatState))
                    {
                        stationaryCombatState.ExitDeathRecovery();
                        stationaryCombatState.RetryStartupRecovery();
                        context.Logger.Info("grocery_shop.revive_path.discarded", new Dictionary<string, object?>
                        {
                            ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId,
                            ["pathName"] = cleanup.Settings.Paths.RevivePathName
                        });
                    }
                    context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, cleanup.GroceryShop
                        ? "杂货清包处理完成，等待后续操作" : "全部售罄，等待停止并重新启动脚本");
                    context.CleanupRequests.CompleteStandaloneShopForRestart(cleanup);
                    context.Logger.Info("standalone_shop.restart.requested", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId });
                    return CleanupRequestTickResult.RestartPending;
                }
                context.CleanupRequests.Complete();
                if (cleanup.GroceryShop) progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
                if (cleanup.ResetsCooldown && cleanup.FullCleanupStarted) progress.LastCleanup = DateTimeOffset.UtcNow;
                stationaryCombatState.PathCombat.Reset();
                await actions.ClearChannelSwitchAsync();
            }
            else context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName,
                (cleanup.ItemTransfer ? "倒货" : "清包") + "已排队，先处理当前战斗 / 复活");
        }
        catch (SnapshotReadBudgetExceededException) { throw; }
        catch (Exception ex) when (CleanupFailurePolicy.Classify(cleanup, ex, context.StopToken) == CleanupFailureDisposition.EndSession)
        {
            progress.OperationActive = false;
            context.CleanupRequests.Complete();
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
            stationaryCombatState.PathCombat.Reset();
            await actions.ClearChannelSwitchAsync();
            context.Logger.Info("cleanup_workflow.discard.session_interrupted", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId });
        }
        catch (Exception ex) when (CleanupFailurePolicy.Classify(cleanup, ex, context.StopToken) == CleanupFailureDisposition.EndGroceryTrip)
        {
            context.CleanupRequests.Complete();
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
            context.RuntimeStates.MarkWarning(context.Config.AccountName, ex.Message);
            progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(2);
            stationaryCombatState.PathCombat.Reset();
            await actions.ClearChannelSwitchAsync();
            context.Logger.Info("grocery_shop.attempt_ended", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["reason"] = ex.Message });
        }
        catch (Exception ex) when (CleanupFailurePolicy.Classify(cleanup, ex, context.StopToken) == CleanupFailureDisposition.ResumeAfterCombat)
        {
            progress.OperationActive = false;
            if (ex is CleanupDeathInterruptionException)
            {
                cleanup.TownReturnCompleted = false;
                cleanup.ScheduledGroceryReturn = null;
            }
            var continuation = cleanup.ItemTransfer ? "；脱战后继续倒货" :
                cleanup.GroceryTrigger == GroceryShopTrigger.Scheduled ? "；脱战后继续定时清包" : string.Empty;
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, ex.Message + continuation);
            if (cleanup.ItemTransfer)
                context.Logger.Info("item_transfer.return.interrupted", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId,
                    ["clicks"] = cleanup.ItemTransferReturn?.Pending?.Clicks ?? 0, ["reason"] = ex.Message
                });
            else if (cleanup.GroceryTrigger == GroceryShopTrigger.Scheduled)
                context.Logger.Info("grocery_shop.schedule.return_interrupted", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId, ["reason"] = ex.Message });
        }
        catch (Exception ex) when (CleanupFailurePolicy.Classify(cleanup, ex, context.StopToken) == CleanupFailureDisposition.AbandonDiscard)
        {
            // Ordinary action failures stay inside the discard state machine.
            // Keep this boundary for exceptions that cannot resume that state;
            // never replay the full workflow or stop the account.
            progress.OperationActive = false;
            context.CleanupRequests.Complete();
            progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
            context.Logger.Warn("cleanup_workflow.discard_abandoned", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["error"] = ex.Message,
                ["manual"] = cleanup.Manual, ["groceryTrigger"] = cleanup.GroceryTrigger.ToString(),
                ["automaticRetryAfterMs"] = 30000
            });
            context.RuntimeStates.MarkWarning(context.Config.AccountName, "本次丢弃失败，已放弃并继续挂机：" + ex.Message);
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
            stationaryCombatState.PathCombat.Reset();
            await actions.ClearChannelSwitchAsync();
        }
        catch (Exception ex) when (CleanupFailurePolicy.Classify(cleanup, ex, context.StopToken) == CleanupFailureDisposition.AbandonWorkflow)
        {
            progress.OperationActive = false;
            progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
            progress.LastCleanup = DateTimeOffset.UtcNow;
            await actions.AbandonAsync(cleanup, ex);
            progress.CheckLifeAfterYield = true;
        }
        finally
        {
            if (cleanup.GroceryTrigger == GroceryShopTrigger.Scheduled && !ReferenceEquals(context.CleanupRequests.Current, cleanup))
                progress.NextScheduledAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        return CleanupRequestTickResult.Yield;
    }
}
