using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    public static async Task JunkRegistrationConfirmationAsync()
    {
        foreach (var latencyMs in new[] { 0, 700, 1700 })
        {
            using var trip = new Trip(); AddNpcStages(trip); AddAutomaticJunk(trip, 200);
            DateTimeOffset? registeredAt = null;
            TimeSpan? firstObservation = null;
            TimeSpan? confirmedAt = null;
            var click = trip.Input.AfterMouseUp;
            trip.Input.AfterMouseUp = button =>
            {
                click?.Invoke(button);
                if (button == RoadhogMouseButton.Left && trip.Api.InventoryUiCursor == new GameUiPoint(160, 400))
                    registeredAt = trip.Now;
            };
            var read = trip.Api.NpcTradeRead!;
            trip.Api.NpcTradeRead = () =>
            {
                var ui = read();
                if (registeredAt.HasValue)
                {
                    var elapsed = trip.Now - registeredAt.Value;
                    firstObservation ??= elapsed;
                    if (elapsed < TimeSpan.FromMilliseconds(latencyMs)) return ui with { Basket = Array.Empty<NpcTradeItem>() };
                    confirmedAt ??= elapsed;
                }
                return ui;
            };
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await trip.Runner().RunAsync(trip.Context, request);
            Check(firstObservation == TimeSpan.Zero && confirmedAt == TimeSpan.FromMilliseconds(latencyMs),
                "advance on actual registration evidence, with no fixed 500ms sleep: " + latencyMs);
            Check(trip.NpcSubmittedItems.SelectMany(i => i).SequenceEqual(new ulong[] { 200, 100, 101, 102 }) &&
                !trip.Events.Contains("npc_junk_empty_submit") && request.RequestsRestart,
                "delayed junk is sold first and configured phases complete without an empty submission");
        }
    }

    public static async Task JunkRegistrationTimeoutAsync()
    {
        foreach (var lateBasket in new[] { false, true })
        {
            using var trip = new Trip(); AddNpcStages(trip);
            if (lateBasket) trip.BulkJunkIds.Add(100);
            var read = trip.Api.NpcTradeRead!;
            trip.Api.NpcTradeRead = () =>
            {
                var ui = read();
                // The pending registration becomes visible only after the action times out.
                if (trip.Events.Contains("npc_junk_register") &&
                    !trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.stage_skipped"))
                    return ui with { Basket = Array.Empty<NpcTradeItem>() };
                return ui;
            };
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await trip.Runner().RunAsync(trip.Context, request);
            Check(trip.Events.Count(e => e == "npc_junk_register") == 1 && !trip.Events.Contains("npc_junk_empty_submit") &&
                trip.Logger.Entries.All(e => e.EventName != "bag_cleanup.sell.junk_registered"),
                "timeout never claims a successful registration or repeats the initial action");
            Check(trip.NpcSubmittedItems.SelectMany(i => i).SequenceEqual(new ulong[] { 100, 101, 102 }) &&
                trip.Input.Keys.Count(k => k == "C") == 2 && request.RequestsRestart && trip.Follows == 1 && trip.ScrollClicks == 1,
                "cancel pending or late registration before all configured phases, without duplicate sale or return trip");
            Check(trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.automatic_junk.continuing" &&
                Equals(e.Fields["reason"], "registration_not_confirmed")), "log the action timeout distinctly from a click guard");
        }
    }

    private static Func<bool> FailInitialJunkClick(Trip trip, bool registration)
    {
        var read = trip.Api.NpcTradeRead!;
        var failed = false;
        trip.Api.NpcTradeRead = () =>
        {
            var ui = read();
            var point = registration ? new GameUiPoint(160, 400) : new GameUiPoint(280, 400);
            if (!failed && ui.IsSelling && trip.Api.InventoryUiCursor == point &&
                (registration || trip.Events.Contains("npc_junk_register")))
            {
                failed = true;
                // Mouse has arrived, but the official button position changed before mouse-down.
                return registration ? ui with { RegisterAllJunkButton = new(161, 400) }
                    : ui with { SellButton = new(281, 400) };
            }
            return ui;
        };
        return () => failed;
    }

    public static async Task JunkClickContinuationAsync()
    {
        foreach (var scenario in new[] { "register", "registered", "worker" })
        {
            using var trip = new Trip(); AddNpcStages(trip);
            var hasBasket = scenario is "registered" or "worker";
            if (hasBasket) trip.BulkJunkIds.Add(100); // Overlap: must cancel registration, then sell exactly once.
            var failed = FailInitialJunkClick(trip, scenario == "register");
            var settings = trip.Config.ScriptSettings!;
            settings.Paths.TownReturnKey = "F7";
            var context = trip.Context;
            if (scenario == "worker")
            {
                settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
                settings.Maintenance.BagCleanupEnabled = false;
                settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = false;
                var paths = new InMemorySharedPathStore(trip.Route, trip.NpcRoute);
                var semi = new SemiAutoCombatController(trip.Input);
                var loop = new DefaultAccountWorkerLoop(trip.Input, semi,
                    new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
                Check(context.CleanupRequests.Request(settings, true, groceryTrigger: GroceryShopTrigger.Manual).Success, "request accepted");
                var task = loop.RunAsync(context);
                try
                {
                    while (!context.CleanupRequests.StandaloneShopRestartRequestId.HasValue)
                    {
                        if (task.IsCompleted) await task;
                        Check(!trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "local failure must not abandon worker cleanup");
                        await Task.Delay(10, trip.Stop.Token);
                    }
                }
                finally
                {
                    trip.Stop.Cancel();
                    try { await task; } catch (OperationCanceledException) when (trip.Stop.IsCancellationRequested) { }
                }
            }
            else
            {
                var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
                await trip.Runner().RunAsync(context, request);
                Check(request.RequestsRestart, "all later configured sales complete normally: " + scenario);
            }
            Check(failed() && trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.automatic_junk.continuing"), "exercise pre-click recovery: " + scenario);
            Check(trip.NpcSubmittedItems.SelectMany(i => i).SequenceEqual(new ulong[] { 100, 101, 102 }),
                "continue all three configured phases without reselling registered goods: " + scenario);
            Check(trip.Events.Count(e => e == "npc_junk_register") == (scenario == "register" ? 0 : 1) &&
                !trip.Events.Contains("npc_junk_empty_submit"), "never replay failed initial step");
            Check(trip.Follows == 1 && trip.ScrollClicks == 1 && trip.ShopStarts == 0 && !trip.Input.Keys.Contains("F7") &&
                !trip.Logger.Entries.Any(e => e.EventName is "cleanup_workflow.failed_continuing" or "grocery_shop.return_to_combat.complete"),
                "no second trip, stall or grinding recovery after initial click failure");
            Check(trip.Input.Keys.Count(k => k == "C") == (hasBasket ? 2 : 1), "only a residual basket requires closing and reopening NPC");
        }
    }

    public static async Task JunkClickRecoveryGuardsAsync()
    {
        foreach (var scenario in new[] { "stop", "close_failure" })
        {
            using var trip = new Trip(); AddNpcStages(trip); trip.BulkJunkIds.Add(100);
            var failed = FailInitialJunkClick(trip, registration: false);
            trip.Input.PressResult = key =>
            {
                if (key == "Space" && failed())
                {
                    if (scenario == "stop") { trip.Stop.Cancel(); trip.Stop.Token.ThrowIfCancellationRequested(); }
                    return OperationResult.Fail("injected NPC closure failure");
                }
                return OperationResult.Ok();
            };
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            Exception? failure = null;
            try { await trip.Runner().RunAsync(trip.Context, request); } catch (Exception ex) { failure = ex; }
            Check(failed() && (scenario == "stop" ? failure is OperationCanceledException : failure is InvalidOperationException),
                "stop and failed basket cancellation cannot fall through to later sales");
            Check(trip.NpcBatches.Count == 0 && trip.ShopStarts == 0 && !request.RequestsRestart &&
                trip.Api.InventoryItems.Any(i => i.InstanceId == 100), "never submit a residual basket during failed recovery");
        }
    }
}
