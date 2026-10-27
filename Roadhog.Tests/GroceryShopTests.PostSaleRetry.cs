using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

internal static partial class GroceryShopTests
{
    public static async Task PostSaleDiscardAbandonsRemainingAsync()
    {
        using var trip = new Trip { HoverFailuresRemaining = 2, HoverFailureAfterDiscards = 1 };
        trip.Stop.CancelAfter(TimeSpan.FromSeconds(30));
        trip.AddDiscardCandidates(2);
        var context = trip.Context;
        context.CleanupRequests.Request(trip.Config.ScriptSettings!, true, groceryTrigger: GroceryShopTrigger.Manual);
        await trip.Runner().RunAsync(context, context.CleanupRequests.Current!);

        Check(trip.HoverFailuresRemaining == 1 && trip.Discards == 1 && trip.Api.InventoryItems.Count == 2 && !trip.BagOpen,
            "hover mismatch after one discard leaves the unconfirmed remaining item and abandons the attempt");
        Check(trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.final_discard.skipped") == 1,
            "ordinary discard failure is reported once and does not hold the grocery workflow");
        Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1 && trip.Success.Saves == 1,
            "skipping final discard cannot repeat return travel selling or success persistence");
        Check(trip.Success.Last == trip.FirstDiscardAt && trip.Now > trip.Success.Last && context.CleanupRequests.Current!.RequestsRestart,
            "preserve actual sale timestamp and allow the normal post-sale continuation after explicitly skipping discard");
        Check(trip.Logger.Entries.Count(e => e.EventName == "bag_cleanup.discard.verified" && Equals(e.Fields["instanceId"], (ulong)100)) == 1,
            "the previously confirmed discarded item cannot be discarded again");
    }

    public static async Task PostSaleDiscardWorkerContinuesAsync()
    {
        using var trip = new Trip { FailDiscard = true };
        trip.Stop.CancelAfter(TimeSpan.FromSeconds(30));
        trip.AddDiscardCandidates();
        var settings = trip.Config.ScriptSettings!;
        settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "12:00" };
        var paths = new InMemorySharedPathStore(trip.Route);
        var semi = new SemiAutoCombatController(trip.Input);
        var loop = new DefaultAccountWorkerLoop(trip.Input, semi, new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
        var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop, new() { TickInterval = TimeSpan.FromMilliseconds(5) });
        async Task Until(Func<bool> ready)
        {
            while (!ready())
            { trip.Stop.Token.ThrowIfCancellationRequested(); Check(host.IsRunning, "worker survives final discard failure"); await Task.Delay(10, trip.Stop.Token); }
        }
        try
        {
            Check(host.Start(trip.Config).Success, "start scheduled grocery worker");
            await Until(() => host.StandaloneShopRestartRequestId.HasValue);
            Check(host.CleanupFailure == null && !trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.failed"),
                "ordinary discard failure must not stop the account");
            Check(trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.final_discard.skipped") == 1 && trip.FailDiscard,
                "normal continuation does not require the persistent discard fault to recover");
            Check(trip.Discards == 0 && !trip.BagOpen && trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1 && trip.Success.Saves == 1,
                "skip remaining discard and signal normal continuation without rerunning the grocery trip");
            Check(trip.Logger.Entries.Count(e => e.EventName == "standalone_shop.restart.requested") == 1,
                "post-sale continuation publishes exactly one restart signal");
            Check((await host.StopAsync()).Success, "worker remains stoppable after skipping discard");
        }
        finally { await host.StopAsync(); }
    }
}
