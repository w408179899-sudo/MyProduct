using Roadhog.Core.Api;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Application.Trading;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Food;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop
{
    private async Task<CleanupRequestTickResult> RunLegacyCleanupRequestAsync(AccountWorkerContext context,
        SemiAutoSkillPlan semiAutoPlan, SemiAutoCombatState semiAutoState,
        StationaryCombatState stationaryCombatState, CombatJumpAssistSession? jumpAssist,
        CleanupRequest cleanup, CleanupLoopProgress progress)
    {
        try
        {
            if (await _stationaryCombat.PrepareCleanupTickAsync(context, semiAutoPlan, semiAutoState, stationaryCombatState,
                    pauseRevivePath: cleanup.GroceryTrigger == GroceryShopTrigger.Scheduled))
            {
                if ((cleanup.StandaloneShop || cleanup.GroceryShop) && jumpAssist is not null)
                    await jumpAssist.StopAsync("standalone_shop").ConfigureAwait(false);
                try
                {
                    progress.OperationActive = true;
                    await _cleanupWorkflow!.RunAsync(context, cleanup, c => _stationaryCombat.ReturnAfterCleanupAsync(
                        c, semiAutoPlan, semiAutoState, stationaryCombatState));
                }
                catch (SnapshotReadBudgetExceededException) { throw; }
                catch (CleanupExecutionFailedException) { throw; }
                catch (CleanupSessionInterruptionException) { throw; }
                catch (Exception ex) when (!context.StopToken.IsCancellationRequested && cleanup.GroceryShop &&
                    cleanup.Settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction == GroceryPostSaleAction.NpcSell &&
                    (cleanup.SoldOutConfirmed || cleanup.GroceryProcessingCompleted))
                {
                    await _cleanupWorkflow!.FinishGroceryAfterFailureAsync(context, cleanup, ex);
                }
                progress.OperationActive = false;
                context.StopToken.ThrowIfCancellationRequested();
                if (cleanup.RequestsRestart)
                {
                    context.CleanupRequests.CompleteStandaloneShopForRestart(cleanup);
                    context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, cleanup.GroceryShop
                        ? "杂货清包处理完成，等待停止并重新启动脚本" : "全部售罄，等待停止并重新启动脚本");
                    context.Logger.Info("standalone_shop.restart.requested", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId });
                    return CleanupRequestTickResult.RestartPending;
                }
                context.CleanupRequests.Complete();
                if (cleanup.GroceryShop) progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
                if (cleanup.ResetsCooldown && cleanup.FullCleanupStarted) progress.LastCleanup = DateTimeOffset.UtcNow;
                stationaryCombatState.PathCombat.Reset();
                await _stationaryCombat.SetChannelSwitchPendingAsync(context, stationaryCombatState, false);
            }
            else context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, "清包已排队，先处理当前战斗 / 复活");
        }
        catch (SnapshotReadBudgetExceededException) { throw; }
        catch (CleanupSessionInterruptionException) when (!context.StopToken.IsCancellationRequested)
        {
            progress.OperationActive = false;
            context.CleanupRequests.Complete();
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
            stationaryCombatState.PathCombat.Reset();
            await _stationaryCombat.SetChannelSwitchPendingAsync(context, stationaryCombatState, false);
            context.Logger.Info("cleanup_workflow.discard.session_interrupted", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["requestId"] = cleanup.RequestId });
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested && cleanup.GroceryShop && !cleanup.GroceryNpcStageStarted &&
            ex is GroceryTripInterruptedException or GroceryTripDepartureFailedException or CleanupCombatInterruptionException or CleanupDeathInterruptionException)
        {
            context.CleanupRequests.Complete();
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, string.Empty);
            context.RuntimeStates.MarkWarning(context.Config.AccountName, ex.Message);
            progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(2);
            stationaryCombatState.PathCombat.Reset();
            await _stationaryCombat.SetChannelSwitchPendingAsync(context, stationaryCombatState, false);
            context.Logger.Info("grocery_shop.attempt_ended", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["reason"] = ex.Message });
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested &&
            !cleanup.StandaloneShop && !cleanup.GroceryShop && (ex is CleanupCombatInterruptionException or CleanupDeathInterruptionException))
        {
            if (ex is CleanupDeathInterruptionException) cleanup.TownReturnCompleted = false;
            context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, ex.Message);
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested && !cleanup.StandaloneShop &&
            cleanup.PreparationStage == CleanupPreparationStage.Discarding)
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
            await _stationaryCombat.SetChannelSwitchPendingAsync(context, stationaryCombatState, false);
        }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        {
            progress.OperationActive = false;
            progress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
            progress.LastCleanup = DateTimeOffset.UtcNow;
            await AbandonCleanupAsync(context, stationaryCombatState, cleanup, ex);
            progress.CheckLifeAfterYield = true;
        }
        return CleanupRequestTickResult.Yield;
    }
}
