using Roadhog.Application.JumpAssist;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop
{
    private sealed class CleanupRequestActions(DefaultAccountWorkerLoop owner, AccountWorkerContext context,
        SemiAutoSkillPlan plan, SemiAutoCombatState semiAutoState, StationaryCombatState state,
        CombatJumpAssistSession? jumpAssist) : ICleanupRequestActions
    {
        public Task<bool> PrepareAsync(CleanupRequest request) => owner._stationaryCombat.PrepareCleanupTickAsync(
            context, plan, semiAutoState, state,
            pauseRevivePath: request.ItemTransfer || request.GroceryTrigger == GroceryShopTrigger.Scheduled);
        public Task StopJumpAsync() => jumpAssist?.StopAsync("standalone_shop") ?? Task.CompletedTask;
        public Task RunWorkflowAsync(CleanupRequest request) => owner._cleanupWorkflow!.RunAsync(context, request,
            c => owner._stationaryCombat.ReturnAfterCleanupAsync(c, plan, semiAutoState, state));
        public Task FinishGroceryAfterFailureAsync(CleanupRequest request, Exception error) =>
            owner._cleanupWorkflow!.FinishGroceryAfterFailureAsync(context, request, error);
        public Task ClearChannelSwitchAsync() => owner._stationaryCombat.SetChannelSwitchPendingAsync(context, state, false);
        public Task AbandonAsync(CleanupRequest request, Exception error) => owner.AbandonCleanupAsync(context, state, request, error);
    }
}
