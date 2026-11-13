using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.GameSessions;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;

internal static partial class CleanupWorkflowTests
{
    public static async Task WorkflowDiscardYieldsSessionCommandAsync()
    {
        foreach (var operation in new[] { GameSessionOperation.Exit, GameSessionOperation.Relogin })
        {
            var game = new InventoryDiscardTests.Simulation(1) { WrongHover = true };
            game.Api.TargetEntityId = 0;
            game.Api.InventoryCapacity = 1;
            game.Settings.BagCleanupEnabled = true;
            game.Settings.BagCleanupThreshold = 2;
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var settings = new ScriptSettings { MainMode = AccountMainMode.SemiAuto, Maintenance = game.Settings };
            var context = new AccountWorkerContext(new AccountConfig
            { AccountName = "discard-session", MainMode = settings.MainMode, ScriptSettings = settings },
                game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(5) }, stop.Token);
            var paths = new InMemorySharedPathStore();
            var semi = new SemiAutoCombatController(game.Input);
            var session = new DiscardExitSession();
            var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("unexpected travel"), new Journal());
            var work = new DefaultAccountWorkerLoop(game.Input, semi, new StationaryCombatController(game.Input, semi, paths),
                cleanupWorkflow: runner, gameExitSessionFactory: _ => session).RunAsync(context);
            async Task Until(Func<bool> ready)
            {
                while (!ready())
                {
                    if (work.IsCompleted) { await work; throw new Exception("worker ended before session handoff"); }
                    await Task.Delay(5, stop.Token);
                }
            }
            try
            {
                await Until(() => logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.failed"));
                Require(context.GameSessionRequests.Request(operation).Success, "queue lifecycle command during retry");
                await Until(() => context.GameSessionRequests.Completed != null);
                Require(context.CleanupRequests.Current == null && context.GameSessionRequests.Completed!.Operation == operation && session.Reads > 0,
                    "worker receives the queued command after discard releases ownership");
                Require(game.Removed.Count == 0 && !game.Open && !game.LeftHeld &&
                    logger.Entries.Count(e => e.EventName == "cleanup_workflow.discard.session_interrupted") == 1 &&
                    !logger.Entries.Any(e => e.EventName is "cleanup_workflow.complete" or "cleanup_workflow.failed_continuing"),
                    "session handoff is neither discard completion nor failure and sends no further discard");
            }
            finally
            {
                stop.Cancel();
                try { await work.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
    }

    private sealed class DiscardExitSession : IGameExitSession
    {
        public int Reads;
        public Task<GameExitSnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Reads++;
            return Task.FromResult(new GameExitSnapshot("discard-test", 123, false, false, false, false, false, 100, false));
        }
        public Task<bool> ExecuteAsync(GameExitSnapshot expected, GameExitAction action, CancellationToken token) =>
            throw new Exception("absent game process requires no exit input");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public static async Task WorkflowDiscardRetrySafetyAsync()
    {
        foreach (var mode in new[] { "attack", "death", "stop", "exit", "relogin", "close", "empty_close", "cleanup_failure" })
        {
            var game = new InventoryDiscardTests.Simulation(mode == "empty_close" ? 0 : 1);
            game.Api.TargetEntityId = 0;
            game.Open = mode == "empty_close";
            game.WrongHover = mode is "attack" or "death" or "stop" or "exit" or "relogin" or "cleanup_failure";
            var closeBlocked = mode is "close" or "empty_close";
            var inputBlocked = mode == "cleanup_failure";
            var press = game.Input.AfterPress;
            game.Input.AfterPress = key =>
            {
                if (closeBlocked && key == "I" && game.Open && game.Api.InventoryItems.Count == 0) return;
                press?.Invoke(key);
            };
            var up = game.Input.AfterMouseUp;
            game.Input.AfterMouseUp = button =>
            {
                up?.Invoke(button);
                if (inputBlocked) throw new InvalidOperationException("temporary input release failure");
            };
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var context = new AccountWorkerContext(new AccountConfig
            { AccountName = "workflow-retry-" + mode, ScriptSettings = new() { Maintenance = game.Settings } },
                game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token) { WorkflowOwnsCleanup = true };

            var controller = new BagCleanupController(game.Input, new InMemorySharedPathStore(),
                (_, _, _) => throw new Exception("discard retry must not enter sale or travel"),
                discarder: new BagCleanupDiscarder(game.Input, (_, ct) => Task.Delay(1, ct)));
            var work = controller.RunDiscardRequestedAsync(context, _ => { });
            try
            {
                var failureEvent = inputBlocked ? "bag_cleanup.discard.retry_cleanup_failed" : "bag_cleanup.discard.failed";
                while (!logger.Entries.Any(e => e.EventName == failureEvent))
                {
                    if (work.IsCompleted) { await work; throw new Exception(mode + " unexpectedly completed"); }
                    await Task.Delay(5, stop.Token);
                }
                Require(!work.IsCompleted, mode + " must retain its batch after failure");
                if (mode == "attack") game.Api.TargetEntityId = 100;
                else if (mode == "death") game.Api.Player = game.Api.Player with { CurrentHp = 0 };
                else if (mode == "stop") stop.Cancel();
                else if (mode is "exit" or "relogin")
                    context.GameSessionRequests.Request(mode == "exit"
                        ? Roadhog.Application.GameSessions.GameSessionOperation.Exit : Roadhog.Application.GameSessions.GameSessionOperation.Relogin);
                else { closeBlocked = inputBlocked = game.WrongHover = false; }

                try
                {
                    await work.WaitAsync(TimeSpan.FromSeconds(8));
                    Require(mode is "close" or "empty_close" or "cleanup_failure", "interruption must propagate: " + mode);
                    Require(!game.Open && !game.LeftHeld && game.Api.InventoryItems.Count == 0,
                        "recovery completes and closes the same batch: " + mode);
                    Require(game.Removed.Count == (mode == "empty_close" ? 0 : 1) && game.Removed.Distinct().Count() == game.Removed.Count,
                        "closure/input retry cannot discard a confirmed item twice");
                    Require(logger.Entries.Count(e => e.EventName == "bag_cleanup.discard.complete") == 1,
                        "only verified exhaustion and closure report completion");
                }
                catch (CleanupCombatInterruptionException) when (mode == "attack") { }
                catch (CleanupDeathInterruptionException) when (mode == "death") { }
                catch (CleanupSessionInterruptionException) when (mode is "exit" or "relogin")
                { Require(context.GameSessionRequests.Current != null, "discard cannot consume the worker session command"); }
                catch (OperationCanceledException) when (mode == "stop" && stop.IsCancellationRequested) { }
                if (mode is "attack" or "death" or "stop" or "exit" or "relogin")
                    Require(game.Removed.Count == 0 && !game.Open && !game.LeftHeld &&
                        !logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.complete"),
                        "interruption during backoff cannot send another discard or falsely complete");
            }
            finally
            {
                stop.Cancel();
                try { await work.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception) when (work.IsCompleted) { }
            }
        }
    }
}
