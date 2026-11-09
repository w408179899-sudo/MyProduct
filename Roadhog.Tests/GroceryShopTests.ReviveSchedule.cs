using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task ScheduleInterruptsReviveTravelWorkerAsync()
    {
        foreach (var skipTrip in new[] { false, true })
        {
            using var trip = new Trip { Now = At(11, 59) };
            trip.Stop.CancelAfter(TimeSpan.FromSeconds(40));
            var settings = trip.Config.ScriptSettings!;
            settings.MainMode = trip.Config.MainMode = AccountMainMode.CustomCombat;
            settings.CombatMode = trip.Config.CombatMode = AccountCombatMode.Stationary;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.Maintenance.BagCleanupEnabled = true;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "12:00" };
            settings.Paths.RevivePathName = "revive";
            settings.Combat.PathFollowReachDistance = 1;
            settings.Combat.StationaryCombatRadius = 5;
            settings.Combat.ReturnHomeWhenNoTarget = false;
            trip.Api.Player = new(1, 100, "revive-schedule", 100, 100, 100, 100, 0,
                new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90);
            trip.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
            // The shop fixture models item clicks; revive navigation also holds RMB to turn.
            var itemDown = trip.Input.AfterMouseDown;
            var itemUp = trip.Input.AfterMouseUp;
            var cameraHeld = false;
            trip.Input.AfterMouseDown = button =>
            {
                if (button == RoadhogMouseButton.Right && !trip.BagOpen)
                { cameraHeld = true; return; }
                itemDown?.Invoke(button);
            };
            trip.Input.AfterMouseUp = button =>
            {
                if (button == RoadhogMouseButton.Right && cameraHeld)
                { cameraHeld = false; return; }
                itemUp?.Invoke(button);
            };
            if (skipTrip) settings.Maintenance.BagCleanupStallItems.Clear();
            var revive = new SharedPathDocument { Name = "revive", Points = new()
                { new() { X = 0 }, new() { X = 20 }, new() { X = 40 } } };
            var paths = new InMemorySharedPathStore(revive, trip.Route);
            var semi = new SemiAutoCombatController(trip.Input);
            var loop = new DefaultAccountWorkerLoop(trip.Input, semi,
                new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
            var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop,
                new() { TickInterval = TimeSpan.FromMilliseconds(5) });
            async Task Until(Func<bool> ready, string stage)
            {
                while (!ready())
                {
                    if (trip.Stop.IsCancellationRequested)
                        throw new Exception("Timed out waiting for " + stage + ": " + string.Join(" | ", trip.Logger.Entries.TakeLast(12)
                            .Select(e => e.EventName + " " + string.Join(",", e.Fields.Select(f => f.Key + "=" + f.Value)))));
                    Check(host.IsRunning, "worker remains active through revival and scheduled cleanup");
                    await Task.Delay(10);
                }
            }
            int Follows() => trip.Logger.Entries.Count(e => e.EventName == "stationary_combat.death_recovery.path_follow");
            try
            {
                Check(host.Start(trip.Config).Success, "start actual worker before scheduled time");
                await Until(() => trip.Success.Loads > 0, "initial schedule observation");
                trip.Api.Player = trip.Api.Player with { CurrentHp = 0 };
                await Until(() => trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.death_recovery.stop_input"), "death recovery entry");
                trip.Api.Player = trip.Api.Player with { CurrentHp = 100 };
                await Until(() => Follows() > 0, "revive route movement");
                var scheduleLoads = trip.Success.Loads;
                trip.Api.InventoryCapacity = 2;
                await Until(() => trip.Success.Loads >= scheduleLoads + 2, "pre-schedule checks during full-bag revive travel");
                Check(trip.ScrollClicks == 0 && !trip.Logger.Entries.Any(e =>
                        e.EventName is "grocery_shop.schedule.due" or "cleanup_workflow.revive_path.paused"),
                    "a full backpack cannot interrupt revive travel before the scheduled time");
                trip.Api.InventoryCapacity = 100;
                var follows = Follows();
                trip.Now = At(12);
                await Until(() => trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.revive_path.paused"), "scheduled revive route pause");
                Check(!trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.death_recovery.complete"),
                    "schedule does not require or falsely report completion of the revive route");
                if (skipTrip)
                {
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.skipped") && Follows() > follows, "skipped trip resume");
                    Check(trip.ScrollClicks == 0 && !host.StandaloneShopRestartRequestId.HasValue,
                        "no-goods skip resumes recovery travel in the same worker without recall or restart");
                }
                else
                {
                    await Until(() => host.StandaloneShopRestartRequestId.HasValue, "scheduled sellout");
                    Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1 && trip.Success.Saves == 1,
                        "scheduled recall and sellout run exactly once before the revive route endpoint");
                    Check(trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.schedule.due") == 1,
                        "due slot is not duplicated during recovery handoff");
                }
                Check((await host.StopAsync()).Success, "Stop still drains the scheduled worker");
            }
            finally { await host.StopAsync(); }
        }
    }
}
