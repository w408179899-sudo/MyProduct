using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.Trading;

public sealed partial class CleanupWorkflowRunner
{
    private async Task<bool> PrepareAutomaticCleanupAsync(AccountWorkerContext context,
        CleanupRequest request, Action<string> report)
    {
        var settings = context.Config.ScriptSettings!.Maintenance;
        var cleanup = new BagCleanupController(input, paths, executePath);
        request.PreparationStage = CleanupPreparationStage.Discarding;
        while (true)
        {
            context.StopToken.ThrowIfCancellationRequested();
            await cleanup.RunDiscardRequestedAsync(context, report);
            var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
            var discardCount = BagCleanupItemMatcher.SelectDiscardItems(bag, settings).Count;
            // Items can arrive after closing the inventory. Finish these locally as well.
            if (discardCount > 0) continue;
            var capacity = (await context.Snapshots.ReadInventoryCapacityAsync().WaitAsync(context.StopToken)).Value;
            var freeSlots = BagCleanupController.CountFreeSlots(bag, capacity);
            var sellCount = BagCleanupItemMatcher.SelectSellRegistrationItems(bag, settings).Count;
            var needsTown = settings.BagCleanupThreshold > 0 && freeSlots < settings.BagCleanupThreshold &&
                request.AllowNpcSell && (settings.CleanupWorkflow.NpcCleanup && sellCount > 0 || settings.CleanupWorkflow.Auction);
            context.Logger.Info("cleanup_workflow.automatic.capacity_checked", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["freeSlots"] = freeSlots,
                ["threshold"] = settings.BagCleanupThreshold, ["remainingDiscardCandidateCount"] = discardCount,
                ["sellCandidateCount"] = sellCount, ["fullCleanupAllowed"] = request.AllowNpcSell,
                ["needsTownReturn"] = needsTown
            });
            request.PreparationStage = CleanupPreparationStage.None;
            return needsTown;
        }
    }
}
