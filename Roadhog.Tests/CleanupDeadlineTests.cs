using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task ReturnDeadlineAsync()
    {
        foreach (var loading in new[] { true, false })
        {
            using var trip = new Trip();
            var clicked = false;
            DateTimeOffset? clickedAt = null;
            var deadlineObserved = false;
            var readAfterDeadline = false;
            trip.AfterScrollClick = () => { clicked = true; clickedAt = trip.Now; };
            if (!loading) trip.Input.AfterKeyUp = _ => { if (deadlineObserved) throw new IOException("injected release failure"); };
            trip.Api.TransitionRead = () =>
            {
                if (deadlineObserved) readAfterDeadline = true;
                if (clickedAt.HasValue && trip.Now - clickedAt.Value >= GroceryReturnSequence.ArrivalTimeout) deadlineObserved = true;
                return !clicked
                ? new(true, trip.Api.Player, trip.Api.Channel, trip.Now)
                : loading ? new(false, null, null, trip.Now)
                : new(true, trip.Api.Player with { Position = new(500, 0, 0) }, trip.Api.Channel with { MapId = 2 }, trip.Now);
            };
            try
            {
                await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now)
                    .RunAsync(trip.Context, trip.Route, trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, _ => { });
                throw new Exception("unconfirmed destination must time out");
            }
            catch (CleanupExecutionFailedException ex)
            { Check(ex.InnerException is TimeoutException && ex.Message.Contains("120"), "bounded destination deadline"); }
            Check(!trip.Stop.IsCancellationRequested && trip.ScrollClicks == 1 && trip.ShopStarts == 0,
                "loading or wrong landing terminates without recasting, selling, or external cancellation");
            Check(deadlineObserved && !readAfterDeadline, "terminal recall cannot start another scene read from its finally block");
            if (!loading) Check(trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.return.release_failed"),
                "failed input release is recorded without replacing the terminal timeout");
        }
    }
}

internal static partial class CleanupWorkflowTests
{
    public static async Task CleanupDeadlinesAsync()
    {
        var progress = new CleanupPathProgress();
        progress.Observe(0, 100, TimeSpan.Zero, "test");
        progress.Observe(0, 100, TimeSpan.FromSeconds(59), "test");
        AssertTimeout(() => progress.Observe(0, 100, TimeSpan.FromSeconds(60), "test"));
        progress = new();
        for (var seconds = 0; seconds < 300; seconds += 10)
            progress.Observe(0, 1000 - seconds, TimeSpan.FromSeconds(seconds), "test");
        AssertTimeout(() => progress.Observe(0, 700, TimeSpan.FromSeconds(300), "test"));
        progress = new();
        progress.Observe(0, 100, TimeSpan.Zero, "test");
        progress.Observe(0, 90, TimeSpan.FromSeconds(50), "test");
        progress.Observe(0, 90, TimeSpan.FromSeconds(100), "test");
        progress.Observe(1, 200, TimeSpan.FromSeconds(105), "test");
        progress.Observe(1, 200, TimeSpan.FromSeconds(160), "test");

        var api = new FakeGameApi { TargetEntityId = 0, PersonalShopRead = Shop };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = new AccountWorkerContext(new() { AccountName = "cleanup-deadlines", ScriptSettings = new() }, api, logger,
            new AccountRuntimeManager(logger), new(), stop.Token);
        var controller = new BagCleanupController(input, new InMemorySharedPathStore(), (_, _, _) => Task.FromResult(OperationResult.Ok()));
        api.TransitionRead = () => new(false, null, null, DateTimeOffset.UtcNow);
        foreach (var step in new[] { BagCleanupStep.PressTownReturn, BagCleanupStep.PressReturnToRevive })
        {
            var state = new BagCleanupState(); state.Start(0, 0); state.Advance(step);
            typeof(BagCleanupState).GetProperty(nameof(BagCleanupState.StepStartedAt))!.SetValue(state, DateTimeOffset.Now.AddSeconds(-121));
            try { await controller.TickAfterLootAsync(context, state); throw new Exception("pre-recall loading must time out"); }
            catch (CleanupExecutionFailedException) { }
        }
        Require(input.Keys.IsEmpty, "unready scene timeout cannot send a recall key");
        api.TransitionRead = null;
        foreach (var step in new[] { BagCleanupStep.WaitTownReturnSettle, BagCleanupStep.WaitReturnToReviveSettle })
        {
            var state = new BagCleanupState(); state.Start(0, 0); state.Advance(step);
            state.ReturnTransition = new();
            state.ReturnTransition.Start(new(true, api.Player, api.Channel, DateTimeOffset.UtcNow),
                new SharedPathDocument { Name = "unreachable", MapId = api.Channel.MapId, Points = new() { new() { X = 1000 } } },
                DateTimeOffset.UtcNow.AddSeconds(-121));
            try { await controller.TickAfterLootAsync(context, state); throw new Exception("recall must time out"); }
            catch (CleanupExecutionFailedException) { }
        }
        var now = DateTimeOffset.UtcNow;
        try
        {
            await new WarehousePurchaseSequence(input, (ms, token) => { token.ThrowIfCancellationRequested(); now = now.AddMinutes(1); return Task.CompletedTask; }, () => now)
                .RunAsync(context.Snapshots, new() { WarehouseName = "missing", WarehouseSelectionKey = "F7" }, _ => { }, stop.Token);
            throw new Exception("missing warehouse must time out");
        }
        catch (CleanupExecutionFailedException) { }
        var semi = new SemiAutoCombatController(input);
        var combat = new StationaryCombatController(input, semi);
        try
        {
            await combat.ReturnAfterCleanupAsync(context, SemiAutoSkillPlan.FromSettings(new()), new(), new(), timeout: TimeSpan.Zero);
            throw new Exception("return to combat must have a deadline");
        }
        catch (CleanupExecutionFailedException) { }
        Require(!stop.IsCancellationRequested, "all stalled operations exit without user stop");
    }

    private static void AssertTimeout(Action action)
    {
        try { action(); throw new Exception("expected no-progress timeout"); }
        catch (CleanupExecutionFailedException) { }
    }
}
