using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

internal static partial class GroceryShopTests
{
    public static async Task ScheduleLateStartupAsync()
    {
        foreach (var last in new DateTimeOffset?[] { null, At(20).AddDays(-1), At(9) })
        {
            using var trip = new Trip();
            trip.Success.Last = last;
            var flow = trip.Config.ScriptSettings!.Maintenance.CleanupWorkflow;
            flow.GroceryScheduleEnabled = true;
            flow.GroceryScheduleTimes = new() { "10:00", "18:00" };
            var context = trip.Context;
            var runner = trip.Runner();
            Check(!await runner.GroceryScheduleDueAsync(context, At(12)), "12h startup must skip the elapsed 10h slot regardless of success history");
            Check(!await runner.GroceryScheduleDueAsync(context, At(18).AddTicks(-1)), "18h cannot trigger early after a late startup");
            Check(await runner.GroceryScheduleDueAsync(context, At(18)), "18h remains eligible in the running session");
            Check(await runner.GroceryScheduleDueAsync(context, At(18, 5)), "an interrupted 18h attempt remains pending");
            Check(await runner.GroceryScheduleDueAsync(context, At(0).AddDays(1)), "a running unfinished attempt can retry across midnight");
            Check(!await runner.GroceryScheduleDueAsync(trip.Context, At(20)), "manual restart discards the old session's elapsed pending slot");
            trip.Success.Last = At(20);
            Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(1)), "actual sale clears the running pending slot");
            Check(await runner.GroceryScheduleDueAsync(context, At(10).AddDays(1)), "tomorrow's 10h slot still triggers normally");
            Check(trip.Success.Saves == 0 && trip.ScrollClicks == 0, "session boundaries cannot write fake success or send input");
        }
    }

    public static async Task ScheduleSessionBoundaryAsync()
    {
        using var trip = new Trip { Now = At(9) };
        var flow = trip.Config.ScriptSettings!.Maintenance.CleanupWorkflow;
        flow.GroceryScheduleEnabled = true;
        flow.GroceryScheduleTimes = new() { "10:00", "18:00" };
        var runner = trip.Runner();
        var context = trip.Context;
        runner.InitializeGrocerySchedule(context);
        Check(await runner.GroceryScheduleDueAsync(context, At(12)), "startup readiness or combat delay cannot miss a slot crossed since 9h startup");
        runner.InitializeGrocerySchedule(context, At(13));
        Check(await runner.GroceryScheduleDueAsync(context, At(13)), "automatic worker recovery retains original session and pending 10h attempt");
        Check(Equals(trip.Logger.Entries.Single(e => e.EventName == "grocery_shop.schedule.due").Fields["sessionStartedAt"], At(9)),
            "diagnostics retain real session start rather than the delayed first observation");

        var restarted = trip.Context;
        runner.InitializeGrocerySchedule(restarted, At(12).ToOffset(TimeSpan.Zero));
        Check(!await runner.GroceryScheduleDueAsync(restarted, At(12)), "a restarted worker cannot inherit a pending slot from another context");
        Check(await runner.GroceryScheduleDueAsync(restarted, At(18, 2).ToOffset(TimeSpan.Zero)), "delayed polling after 18h still triggers using the same UTC-aware boundary");
        var evening = trip.Context;
        runner.InitializeGrocerySchedule(evening, At(19));
        Check(!await runner.GroceryScheduleDueAsync(evening, At(20)), "startup after both configured times skips both");
        Check(await runner.GroceryScheduleDueAsync(evening, At(10).AddDays(1)), "late startup does not suppress next day's first slot");
        trip.Success.Last = At(20).AddDays(1);
        Check(!await runner.GroceryScheduleDueAsync(evening, At(20).AddDays(1)), "actual 20h sale covers both that day's configured slots");
    }

    public static async Task ScheduleLateStartupWorkerAsync()
    {
        using var trip = new Trip { Now = At(12) };
        trip.Stop.CancelAfter(TimeSpan.FromSeconds(30));
        var settings = trip.Config.ScriptSettings!;
        settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.BagCleanupEnabled = false;
        trip.Api.InventoryCapacity = 100;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "10:00", "18:00" };
        var runner = trip.Runner();
        var paths = new InMemorySharedPathStore(trip.Route);
        var semi = new SemiAutoCombatController(trip.Input);
        var loop = new DefaultAccountWorkerLoop(trip.Input, semi, new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: runner);
        var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop, new() { TickInterval = TimeSpan.FromMilliseconds(5) });
        async Task Until(Func<bool> ready)
        {
            while (!ready())
            { trip.Stop.Token.ThrowIfCancellationRequested(); Check(host.IsRunning, "late-started worker remains alive"); await Task.Delay(10, trip.Stop.Token); }
        }
        try
        {
            Check(host.Start(trip.Config).Success, "start real worker at simulated 12h");
            await Until(() => trip.Success.Loads >= 2);
            Check(trip.ScrollClicks == 0 && trip.Follows == 0 && trip.Success.Saves == 0 &&
                host.StandaloneShopRestartRequestId == null && !trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.schedule.due"),
                "12h worker startup cannot return or sell for elapsed 10h slot");
            trip.Now = At(18);
            await Until(() => host.StandaloneShopRestartRequestId.HasValue);
            Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1 && trip.Success.Saves == 1,
                "18h triggers independently of bag capacity and completes exactly once");
            var due = trip.Logger.Entries.Single(e => e.EventName == "grocery_shop.schedule.due");
            Check(Equals(due.Fields["dueAt"], At(18)) && Equals(due.Fields["sessionStartedAt"], At(12)),
                "worker due event shows 18h task and the original 12h session start");
            Check((await host.StopAsync()).Success, "late-started worker remains stoppable");
        }
        finally { await host.StopAsync(); }
    }
}
