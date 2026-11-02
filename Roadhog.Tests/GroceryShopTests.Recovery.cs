using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task TradeFailureClosesWindowsAsync()
    {
        foreach (var phase in new[] { "npc_budget", "editor", "registered", "partial_sale" })
        {
            using var trip = new Trip(); AddNpcStages(trip);
            var injected = false;
            if (phase == "npc_budget")
            {
                var click = trip.Input.AfterMouseUp;
                trip.Input.AfterMouseUp = button =>
                {
                    if (!injected && button == RoadhogMouseButton.Left && trip.Api.InventoryUiCursor == new GameUiPoint(280, 400))
                    { injected = true; throw new SnapshotReadBudgetExceededException("npc-trade"); }
                    click?.Invoke(button);
                };
            }
            else
            {
                trip.RejectNpcBatch = 1;
                var click = trip.Input.AfterMouseUp;
                trip.Input.AfterMouseUp = button =>
                {
                    click?.Invoke(button);
                    var ui = trip.ShopState;
                    if (!injected && (phase == "editor" && ui.Editor != null ||
                        phase == "registered" && ui.Listings.Count > 0 || phase == "partial_sale" && ui.IsSelling))
                    {
                        injected = true;
                        if (phase == "partial_sale")
                        {
                            trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 12 ? i with { Count = 2 } : i).ToArray();
                            trip.Api.InventoryMoney += 500;
                        }
                        throw new SnapshotReadBudgetExceededException("personal-shop");
                    }
                };
            }
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            try { await trip.Runner().RunAsync(trip.Context, request); throw new Exception("injected trade fault disappeared: " + phase); }
            catch (SnapshotReadBudgetExceededException) { }
            Check(injected && !trip.BagOpen && !trip.ShopState.IsOpen && !trip.ShopState.IsSelling && trip.ShopState.Editor == null,
                "exception closes bag, editor and any active stall: " + phase);
            Check(!trip.Api.NpcTradeRead!().IsOpen && !trip.Api.NpcTradeRead!().DialogOpen,
                "NPC budget exception still executes Space closure");
            Check(!request.RequestsRestart && !request.SoldOutConfirmed && trip.Success.Saves == 0 && trip.Discards == 0,
                "UI recovery cannot manufacture completion or discard leftovers");
            Check(trip.ShopStarts == (phase == "partial_sale" ? 1 : 0) && trip.ShopStops == (phase == "partial_sale" ? 1 : 0),
                "active stall is stopped once; registration failures never start selling");
            if (phase == "partial_sale") Check(trip.Api.InventoryItems.Single(i => i.InstanceId == 12).Count == 2,
                "partial sale retains unsold stock without replay");
        }
    }

    public static async Task TradeRecoveryDeadlineAndStopAsync()
    {
        using var trip = new Trip();
        var ui = trip.ShopState with { IsOpen = true, Editor = new(12, true, 5, 15, 3, new(1, 1), new(2, 2)) };
        trip.Api.PersonalShopRead = () => ui; // Escape is accepted but the UI never acknowledges it.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var messages = new List<string>();
        var recovered = await new TradingWindowRecovery(trip.Input, trip.Context.Snapshots, trip.Delay)
            .TryCloseAsync(messages.Add, trip.Stop.Token, timeout: TimeSpan.FromMilliseconds(120));
        Check(!recovered && messages.Count == 1 && timer.Elapsed < TimeSpan.FromSeconds(2),
            "unresponsive editor has a total recovery deadline and reports failure");
        Check(trip.Input.Keys.SequenceEqual(new[] { "Escape" }) && trip.Confirms == 0 && trip.ShopStarts == 0,
            "recovery cannot confirm a pending edit or repeatedly toggle the shop");
        trip.Stop.Cancel();
        var count = trip.Input.Keys.Count;
        try
        {
            await new TradingWindowRecovery(trip.Input, trip.Context.Snapshots, trip.Delay).TryCloseAsync(messages.Add, trip.Stop.Token);
            throw new Exception("user Stop was swallowed by trade recovery");
        }
        catch (OperationCanceledException) { }
        Check(trip.Input.Keys.Count == count, "user Stop forbids new UI input");
    }

    public static async Task TradeInputReleaseDeadlineAsync()
    {
        using var trip = new Trip();
        var input = new UnresponsiveResetInput();
        var actions = new TradingActions(input, trip.Context.Snapshots, trip.Stop.Token);
        trip.Stop.Cancel(); // Releasing held input is still required after Stop.
        try
        {
            await actions.Reset().WaitAsync(TimeSpan.FromSeconds(5));
            throw new Exception("unresponsive release returned success");
        }
        catch (OperationCanceledException)
        {
            Check(input.Calls == 1 && input.Token.IsCancellationRequested, "finally release is attempted once and receives its own deadline");
        }
        finally { input.Completion.TrySetResult(OperationResult.Ok()); }
    }

    private sealed class UnresponsiveResetInput : IKeyboardInput, IInputStateReset
    {
        public readonly TaskCompletionSource<OperationResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public int Calls;
        public Task<OperationResult> ReleaseAllAsync(CancellationToken cancellationToken = default)
        { Calls++; Token = cancellationToken; return Completion.Task; }
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public static async Task EmptyStallReturnsBeforeCompletionAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip); trip.RejectNpcBatch = 1;
        var settings = trip.Config.ScriptSettings!;
        settings.Maintenance.BagCleanupStallItems.Clear();
        settings.Paths.RevivePathName = trip.NpcRoute.Name;
        var calls = 0;
        var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
        await trip.Runner().RunAsync(trip.Context, request, context =>
        {
            Check(!trip.BagOpen && !trip.Api.NpcTradeRead!().IsOpen && trip.NpcBatches.Count == 1,
                "return controller owns continuation only after NPC closure and stopped sale");
            calls++;
            return Task.CompletedTask;
        });
        Check(calls == 1 && !request.RequestsRestart && trip.ShopStarts == 0 && trip.Follows == 1,
            "worker return controller is invoked once instead of restarting or navigating twice");
    }

    public static async Task EmptyStallReturnsAcrossMapsAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip); trip.RejectNpcBatch = 1;
        var settings = trip.Config.ScriptSettings!;
        settings.Maintenance.BagCleanupStallItems.Clear();
        var home = new SharedPathDocument { Name = "grind", MapId = 3,
            Points = new() { new() { X = 4000 }, new() { X = 4100 } } };
        settings.Paths.RevivePathName = home.Name;
        settings.Paths.TownReturnKey = "F1";
        var press = trip.Input.AfterPress;
        trip.Input.AfterPress = key =>
        {
            press?.Invoke(key);
            if (key == "F1")
            {
                trip.Api.Channel = trip.Api.Channel with { MapId = 3 };
                trip.Api.Player = trip.Api.Player with { Position = new(4000, 0, 0) };
            }
        };
        var visited = new List<string>();
        var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.NpcRoute, home), (_, name, points) =>
        {
            Check(trip.Api.Channel.MapId == (name == home.Name ? 3 : 2), "arrival must be confirmed before walking each map");
            trip.Api.Player = trip.Api.Player with { Position = points[^1] }; visited.Add(name);
            return Task.FromResult(OperationResult.Ok());
        }, new UnusedJournal(), grocerySuccessStore: trip.Success, groceryDelay: trip.Delay, groceryClock: () => trip.Now);
        var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
        await runner.RunAsync(trip.Context, request);
        Check(visited.SequenceEqual(new[] { trip.NpcRoute.Name, home.Name }) && trip.Input.Keys.Count(k => k == "F1") == 1 &&
            trip.Api.Player.Position == new Vector3Snapshot(4100, 0, 0) && !request.RequestsRestart,
            "empty stall recalls to the correct map then actually reaches the configured grind destination");
    }

    public static async Task FailedNpcWorkerResumesMapRecoveryAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip); trip.FailNpcSubmit = true;
        trip.Api.TargetObjectType = 0; // A selected merchant is not a live combat monster.
        var mouseDown = trip.Input.AfterMouseDown;
        var mouseUp = trip.Input.AfterMouseUp;
        trip.Input.AfterMouseDown = button =>
        {
            if (button != RoadhogMouseButton.Right || trip.BagOpen) mouseDown?.Invoke(button);
        };
        trip.Input.AfterMouseUp = button =>
        {
            if (button != RoadhogMouseButton.Right || trip.BagOpen) mouseUp?.Invoke(button);
        };
        var settings = trip.Config.ScriptSettings!;
        settings.MainMode = trip.Config.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Maintenance.BagCleanupEnabled = false;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = false;
        settings.Paths.TownReturnKey = "F1";
        var home = new SharedPathDocument { Name = "home", MapId = 3,
            Points = new() { new() { X = 4000 }, new() { X = 4100 } } };
        settings.Paths.RevivePathName = home.Name;
        var recalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var press = trip.Input.AfterPress;
        trip.Input.AfterPress = key =>
        {
            press?.Invoke(key);
            if (key == "F1")
            {
                if (trip.NpcBatches.Count == 0)
                {
                    // Custom-combat startup performs its ordinary initial recall before queued cleanup.
                    trip.Api.Channel = trip.Api.Channel with { MapId = 3 };
                    trip.Api.Player = trip.Api.Player with { Position = new(4000, 0, 0) };
                    return;
                }
                Check(trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing") && !trip.BagOpen &&
                    !trip.Api.NpcTradeRead!().IsOpen, "normal map recovery follows abandonment and closed NPC UI");
                recalled.TrySetResult();
            }
        };
        var paths = new InMemorySharedPathStore(trip.Route, trip.NpcRoute, home);
        var semi = new SemiAutoCombatController(trip.Input);
        var loop = new DefaultAccountWorkerLoop(trip.Input, semi,
            new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
        var context = trip.Context;
        Check(context.CleanupRequests.Request(settings, true, groceryTrigger: GroceryShopTrigger.Manual).Success, "queue NPC failure scenario");
        var task = loop.RunAsync(context);
        try
        {
            await recalled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Check(!task.IsCompleted && !context.CleanupRequests.StandaloneShopRestartRequestId.HasValue && trip.NpcBatches.Count == 1,
                "failed NPC transaction resumes recall in the same worker without replay or restart");
        }
        catch (TimeoutException ex)
        {
            if (task.IsCompleted) await task;
            throw new Exception("NPC recovery did not recall; " + System.Text.Json.JsonSerializer.Serialize(
                trip.Logger.Entries.TakeLast(12)), ex);
        }
        finally
        {
            trip.Stop.Cancel();
            try { await task; } catch (OperationCanceledException) when (trip.Stop.IsCancellationRequested) { }
        }
    }
}
