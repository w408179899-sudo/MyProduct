using Roadhog.Application.JumpAssist;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop
{
    private async Task ObserveScheduledCleanupAsync(AccountWorkerContext context, ScriptSettings settings, CleanupLoopProgress progress)
    {
        var flow = settings.Maintenance.CleanupWorkflow;
        if (_cleanupWorkflow is null || flow.Mode != CleanupMode.GroceryShop || !flow.GroceryScheduleEnabled) return;
        if (context.CleanupRequests.StandaloneShopRestartRequestId.HasValue ||
            context.CleanupRequests.Current?.GroceryTrigger == GroceryShopTrigger.Scheduled) return;

        var now = DateTimeOffset.UtcNow;
        if (now < progress.NextScheduleCheck) return;
        progress.NextScheduleCheck = now.AddSeconds(2);
        try
        {
            if (await _cleanupWorkflow.GroceryScheduleDueAsync(context).ConfigureAwait(false) && now >= progress.NextScheduledAttempt)
                context.CleanupRequests.Request(settings, manual: false, resetsCooldown: false, allowNpcSell: false,
                    groceryTrigger: GroceryShopTrigger.Scheduled);
        }
        catch (Exception error) when (!context.StopToken.IsCancellationRequested)
        {
            progress.NextScheduleCheck = now.AddSeconds(30);
            context.Logger.Warn("grocery_shop.schedule.check_failed", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["error"] = error.Message });
        }
    }

    private sealed class CleanupRequestActions(DefaultAccountWorkerLoop owner, AccountWorkerContext context,
        SemiAutoSkillPlan plan, SemiAutoCombatState semiAutoState, StationaryCombatState state,
        CombatJumpAssistSession? jumpAssist) : ICleanupRequestActions
    {
        public Task<bool> PrepareAsync(CleanupRequest request) => owner._stationaryCombat.PrepareCleanupTickAsync(
            context, plan, semiAutoState, state,
            pauseRevivePath: request.ItemTransfer || request.GroceryTrigger == GroceryShopTrigger.Scheduled,
            scheduled: request.GroceryTrigger == GroceryShopTrigger.Scheduled);
        public Task StopJumpAsync() => jumpAssist?.StopAsync("standalone_shop") ?? Task.CompletedTask;
        public Task RunWorkflowAsync(CleanupRequest request) => owner._cleanupWorkflow!.RunAsync(context, request,
            c => owner._stationaryCombat.ReturnAfterCleanupAsync(c, plan, semiAutoState, state));
        public Task FinishGroceryAfterFailureAsync(CleanupRequest request, Exception error) =>
            owner._cleanupWorkflow!.FinishGroceryAfterFailureAsync(context, request, error);
        public Task ClearChannelSwitchAsync() => owner._stationaryCombat.SetChannelSwitchPendingAsync(context, state, false);
        public Task AbandonAsync(CleanupRequest request, Exception error) => owner.AbandonCleanupAsync(context, state, request, error);
    }
}
