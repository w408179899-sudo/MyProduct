using Roadhog.Application.Input;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task MouseFailureResumesCurrentShopAsync()
    {
        foreach (var phase in new[] { "item", "item_uncertain", "price", "confirm", "start" })
        {
            using var trip = new Trip(); AddNpcStages(trip); trip.RejectNpcBatch = 1;
            trip.Stop.CancelAfter(TimeSpan.FromSeconds(30));
            var failed = false;
            var retries = 0;
            var stateUncertain = false;
            var read = trip.Api.PersonalShopRead!;
            trip.Api.PersonalShopRead = () => stateUncertain ? read() with { OtherModalOpen = true } : read();
            var move = trip.Input.AfterMove;
            trip.Input.AfterMove = (dx, dy) =>
            {
                var ui = trip.ShopState;
                var target = new GameUiPoint(trip.Api.InventoryUiCursor.X + dx, trip.Api.InventoryUiCursor.Y + dy);
                if (!failed && ui.IsOpen && ((phase is "item" or "item_uncertain") && ui.InventoryOpen && ui.Editor == null && trip.Confirms == 0 ||
                    phase == "price" && ui.Editor != null && target == new GameUiPoint(500, 100) ||
                    phase == "confirm" && target == new GameUiPoint(600, 100) ||
                    phase == "start" && target == new GameUiPoint(700, 100)))
                {
                    failed = true;
                    stateUncertain = phase == "item_uncertain";
                    throw new MouseMovementTimeoutException("injected pre-click positioning timeout");
                }
                move?.Invoke(dx, dy);
            };
            trip.DelayOverride = (ms, ct) =>
            {
                if (stateUncertain && ms == 30000)
                {
                    Check(trip.Confirms == 0 && trip.ShopStarts == 0, "uncertain UI remains observation-only before retry");
                    stateUncertain = false;
                }
                if (failed && ms == 1000)
                {
                    retries++;
                    // A late movement completes during backoff. The next attempt
                    // must read the new position instead of replaying its old delta.
                    trip.Api.InventoryUiCursor = new(682, 468);
                    trip.CursorVisits.Add(trip.Api.InventoryUiCursor);
                }
                return Task.CompletedTask;
            };
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await trip.Runner().RunAsync(trip.Context, request);
            Check(failed && retries == 1 && trip.Confirms == 1 && trip.ShopStarts == 1 && trip.Success.Saves == 1,
                "positioning recovers at the current operation with exactly one registration and start: " + phase);
            Check(trip.NpcBatches.Count == 1 && trip.ScrollClicks == 1 && trip.Follows == 2 && request.SoldOutConfirmed,
                "recovery never repeats NPC selling, recall or route travel: " + phase);
        }
    }

    public static async Task PersistentShopMouseFailureNeverReturnsToCombatAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip); trip.RejectNpcBatch = 1;
        trip.Stop.CancelAfter(TimeSpan.FromSeconds(40));
        trip.Api.TargetObjectType = 0;
        var settings = trip.Config.ScriptSettings!;
        settings.MainMode = trip.Config.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Maintenance.BagCleanupEnabled = false;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = false;
        settings.Paths.TownReturnKey = "F1";
        var home = new SharedPathDocument { Name = "home", MapId = 3,
            Points = new() { new() { X = 4000 }, new() { X = 4100 } } };
        settings.Paths.RevivePathName = home.Name;
        var press = trip.Input.AfterPress;
        var recallsAfterFailure = 0;
        var ignoredMoves = 0;
        trip.Input.AfterPress = key =>
        {
            press?.Invoke(key);
            if (key != "F1") return;
            if (ignoredMoves > 0) recallsAfterFailure++;
            trip.Api.Channel = trip.Api.Channel with { MapId = 3 };
            trip.Api.Player = trip.Api.Player with { Position = new(4000, 0, 0) };
        };
        var move = trip.Input.AfterMove;
        trip.Input.AfterMove = (dx, dy) =>
        {
            if (trip.ShopState is { IsOpen: true, InventoryOpen: true, Editor: null }) { ignoredMoves++; return; }
            move?.Invoke(dx, dy);
        };
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryDelays = new List<int>();
        trip.DelayOverride = (ms, ct) =>
        {
            if (ignoredMoves > 0 && ms is 1000 or 30000)
            {
                retryDelays.Add(ms);
                if (ms == 30000) { waiting.TrySetResult(); return Task.Delay(Timeout.Infinite, ct); }
            }
            return Task.CompletedTask;
        };
        var paths = new InMemorySharedPathStore(trip.Route, trip.NpcRoute, home);
        var semi = new SemiAutoCombatController(trip.Input);
        var loop = new DefaultAccountWorkerLoop(trip.Input, semi,
            new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
        var context = trip.Context;
        Check(context.CleanupRequests.Request(settings, true, groceryTrigger: GroceryShopTrigger.Manual).Success, "queue shop failure");
        var request = context.CleanupRequests.Current;
        var task = loop.RunAsync(context);
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Check(!task.IsCompleted && ReferenceEquals(request, context.CleanupRequests.Current), "worker retains the same shop task while waiting");
            Check(retryDelays.SequenceEqual(new[] { 1000, 1000, 1000, 30000 }) && ignoredMoves == 4,
                "three local retries then slow recovery; no command flood during unconfirmed movement");
            Check(recallsAfterFailure == 0 && trip.NpcBatches.Count == 1 && trip.ScrollClicks == 1 && trip.Follows == 2,
                "persistent positioning failure never recalls, repeats NPC sale or re-runs travel");
            Check(trip.Confirms == 0 && trip.ShopStarts == 0 && trip.Success.Saves == 0 &&
                !context.CleanupRequests.StandaloneShopRestartRequestId.HasValue &&
                !trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "failure cannot become success or return to combat");
        }
        finally
        {
            trip.Stop.Cancel();
            try { await task.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) when (trip.Stop.IsCancellationRequested) { }
        }
        Check(recallsAfterFailure == 0 && trip.ShopStarts == 0, "Stop cancels recovery without starting a sale or recall");
    }

    public static async Task DelayedShopConfirmationNeverReplaysAsync()
    {
        foreach (var phase in new[] { "editor", "registered", "selling", "stop" })
        {
            using var trip = new Trip();
            trip.Stop.CancelAfter(TimeSpan.FromSeconds(25));
            var read = trip.Api.PersonalShopRead!;
            var awaiting = false;
            var release = false;
            var rightClicks = 0;
            var mouseDown = trip.Input.AfterMouseDown;
            trip.Input.AfterMouseDown = button =>
            {
                if (button == RoadhogMouseButton.Right && trip.ShopState.IsOpen) rightClicks++;
                mouseDown?.Invoke(button);
            };
            PersonalShopSnapshot? before = null;
            var click = trip.Input.AfterMouseUp;
            trip.Input.AfterMouseUp = button =>
            {
                var old = trip.ShopState;
                click?.Invoke(button);
                var ui = trip.ShopState;
                if (!awaiting && (phase == "editor" && ui.Editor != null || (phase is "registered" or "stop") && ui.Listings.Count > 0 || phase == "selling" && ui.IsSelling))
                { awaiting = true; before = old; }
            };
            trip.Api.PersonalShopRead = () => awaiting && !release ? before! : read();
            var waits = 0;
            trip.DelayOverride = (ms, ct) =>
            {
                if (ms == 30000 && awaiting)
                {
                    waits++;
                    Check(trip.ShopStarts == (phase == "selling" ? 1 : 0), "unconfirmed submission must not advance to a new start");
                    if (phase == "stop") { trip.Stop.Cancel(); ct.ThrowIfCancellationRequested(); }
                    release = true;
                }
                return Task.CompletedTask;
            };
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            try { await trip.Runner().RunAsync(trip.Context, request); }
            catch (OperationCanceledException) when (phase == "stop" && trip.Stop.IsCancellationRequested) { }
            if (phase == "stop")
            {
                Check(waits == 1 && rightClicks == 1 && trip.Confirms == 1 && trip.ShopStarts == 0 &&
                    !request.SoldOutConfirmed && trip.Success.Saves == 0, "Stop interrupts uncertain registration without replay or false completion");
                continue;
            }
            Check(waits == 1 && rightClicks == 1 && trip.Confirms == 1 && trip.ShopStarts == 1 && request.SoldOutConfirmed,
                "late official confirmation resumes without a second click: " + phase);
        }
    }
}
