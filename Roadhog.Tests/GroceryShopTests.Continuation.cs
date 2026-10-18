using Roadhog.Application;
using Roadhog.Application.Trading;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

internal static partial class GroceryShopTests
{
    private static void NpcMode(Trip trip)
    {
        var settings = trip.Config.ScriptSettings!;
        settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = GroceryPostSaleAction.NpcSell;
        settings.Paths.BagCleanupReturnItemName = settings.Paths.GroceryReturnItemName;
        settings.Paths.MaintenancePathName = trip.NpcRoute.Name;
        trip.Stop.CancelAfter(TimeSpan.FromSeconds(40));
    }

    public static async Task NpcContinuationWorkerAsync()
    {
        foreach (var failure in new[] { "sale_submit", "save_record", "final_release" })
        {
            using var trip = new Trip(); NpcMode(trip); trip.AddDiscardCandidates(4);
            var settings = trip.Config.ScriptSettings!;
            settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
            settings.Maintenance.BagCleanupEnabled = false;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = false;
            trip.FailNpcSubmit = failure == "sale_submit";
            trip.Success.FailSave = failure == "save_record";
            var releaseFailures = 0;
            if (failure == "final_release") trip.Input.AfterKeyUp = key =>
            {
                if (key == "W" && trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.npc_sale.complete") && releaseFailures++ < 2)
                    throw new IOException("release failed after NPC completion");
            };
            var paths = new InMemorySharedPathStore(trip.Route, trip.NpcRoute);
            var semi = new SemiAutoCombatController(trip.Input);
            var loop = new DefaultAccountWorkerLoop(trip.Input, semi, new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
            var context = trip.Context;
            Check(context.CleanupRequests.Request(settings, true, groceryTrigger: GroceryShopTrigger.Manual).Success, "grocery request accepted");
            var task = loop.RunAsync(context);
            try
            {
                while (!context.CleanupRequests.StandaloneShopRestartRequestId.HasValue)
                {
                    if (task.IsCompleted) await task;
                    await Task.Delay(10, trip.Stop.Token);
                }
                Check(context.CleanupRequests.Current == null && trip.Input.Keys.Contains("Space") && !trip.BagOpen &&
                    trip.Logger.Entries.Count(e => e.EventName == "standalone_shop.restart.requested") == 1,
                    "stage failure reaches actual worker restart handoff: " + failure);
                Check(trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.stage_skipped") &&
                    !trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.failed"), "failure is recorded without parking the worker");
                Check(trip.NpcBatches.Count == (failure == "sale_submit" ? 1 : 2) && trip.Discards == 0,
                    "never replay a failed sale or discard its remaining items");
                var inputs = trip.Input.Keys.Count + trip.Input.MouseCommands.Count;
                await Task.Delay(150, trip.Stop.Token);
                Check(inputs == trip.Input.Keys.Count + trip.Input.MouseCommands.Count, "worker waits for manager restart without repeating actions");
            }
            finally
            {
                trip.Stop.Cancel();
                try { await task; } catch (OperationCanceledException) when (trip.Stop.IsCancellationRequested) { }
            }
        }
    }

    public static async Task NpcContinuationTravelAndStopAsync()
    {
        foreach (var failure in new[] { "missing_route", "route_failure", "return_failure", "stop" })
        {
            using var trip = new Trip(); NpcMode(trip); trip.AddDiscardCandidates(2);
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.BagCleanupStallItems.Clear();
            if (failure == "missing_route") settings.Paths.MaintenancePathName = "missing";
            if (failure == "return_failure") trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.InstanceId != 11).ToArray();
            var context = trip.Context;
            context.CleanupRequests.Request(settings, true, groceryTrigger: GroceryShopTrigger.Manual);
            var request = context.CleanupRequests.Current!;
            var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.Route, trip.NpcRoute), (_, _, _) =>
            {
                if (failure == "stop") { trip.Stop.Cancel(); trip.Stop.Token.ThrowIfCancellationRequested(); }
                throw new InvalidOperationException("path failed");
            }, new UnusedJournal(), grocerySuccessStore: trip.Success, groceryDelay: trip.Delay, groceryClock: () => trip.Now);
            try
            {
                await runner.RunAsync(context, request);
                Check(failure != "stop" && request.RequestsRestart && request.Failure == null && trip.Input.Keys.Contains("Space"),
                    "failed NPC trip still closes UI and requests restart: " + failure);
            }
            catch (OperationCanceledException) when (failure == "stop")
            { Check(!request.RequestsRestart && !trip.Input.Keys.Contains("Space"), "user Stop cancels without jump or restart"); }
            Check(trip.NpcBatches.Count == 0 && trip.Discards == 0 && trip.Success.Saves == 0,
                "failed travel never trades remotely, discards goods or invents sellout");
        }
    }
}
