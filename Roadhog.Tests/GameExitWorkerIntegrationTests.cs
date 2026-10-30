using Roadhog.Application;
using Roadhog.Application.GameSessions;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;

internal static class GameExitWorkerIntegrationTests
{
    private sealed class ExitSession : IGameExitSession
    {
        internal GameExitSnapshot Scene = new("worker-test/game-123", 123, true, false, false, false, false, 100, false);
        internal int Reads;
        internal int Disposals;
        internal readonly List<GameExitAction> Actions = [];
        internal Action? BeforeRead;
        internal Action<GameExitAction>? OnAction;
        public Task<GameExitSnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            BeforeRead?.Invoke();
            return Task.FromResult(Scene);
        }
        public Task<bool> ExecuteAsync(GameExitSnapshot expected, GameExitAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Actions.Add(action);
            OnAction?.Invoke(action);
            return Task.FromResult(true);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private static (DefaultAccountWorkerLoop Loop, AccountWorkerContext Context) Create(
        FakeGameApi api, RecordingKeyboardInput input, InMemoryRoadhogLogger logger,
        ExitSession session, CancellationToken token, GameSessionRequestMailbox mailbox,
        Func<IGameExitSession>? exitFactory = null)
    {
        var settings = new ScriptSettings { MainMode = AccountMainMode.Gather };
        var config = new AccountConfig { AccountName = "exit-worker-test", MainMode = settings.MainMode, ScriptSettings = settings };
        var states = new AccountRuntimeManager(logger);
        states.MarkStarting(config);
        states.MarkRunning(config.AccountName, Environment.CurrentManagedThreadId);
        var context = new AccountWorkerContext(config, api, logger, states,
            new() { TickInterval = TimeSpan.FromMilliseconds(10), PollPlayerSnapshot = true }, token,
            gameSessionRequests: mailbox);
        var semi = new SemiAutoCombatController(input);
        var loop = new DefaultAccountWorkerLoop(input, semi, new StationaryCombatController(input, semi),
            gameExitSessionFactory: _ => exitFactory?.Invoke() ?? session);
        return (loop, context);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task UntilAsync(Func<bool> predicate, Task work, CancellationToken token)
    {
        while (!predicate())
        {
            if (work.IsCompleted) { await work; throw new InvalidOperationException("Worker ended before the expected state."); }
            await Task.Delay(10, token);
        }
    }
    private static async Task StopAsync(CancellationTokenSource stop, Task work)
    {
        stop.Cancel();
        try { await work.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    internal static async Task NoRequestLeavesNormalWorkUntouchedAsync()
    {
        var api = new FakeGameApi();
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var session = new ExitSession { BeforeRead = () => throw new InvalidOperationException("Unexpected exit read without a request.") };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var (loop, context) = Create(api, input, logger, session, stop.Token, new());
        var work = loop.RunAsync(context);
        try
        {
            await UntilAsync(() => api.PlayerReadCount >= 3, work, stop.Token);
            Require(input.Keys.FirstOrDefault() == "F1" && input.MouseCommands.Count(command => command == "wheel:-1") == 10,
                "Ordinary startup still performs F1 and its ten existing camera scrolls.");
            Require(session.Reads == 0 && session.Actions.Count == 0,
                "An unrequested exit module must not poll DMA or send game input during normal work.");
            Require(!logger.Entries.Any(entry => entry.EventName == "worker.tick.failed_continuing"),
                "Normal work must not encounter any exit-module branch failure.");
        }
        finally { await StopAsync(stop, work); }
        Require(session.Disposals == 1, "The unused session is released once at worker shutdown.");
    }

    internal static async Task OfflineAndCompletedRequestsSkipGameplayStartupAsync()
    {
        foreach (var alreadyCompleted in new[] { false, true })
        {
            var api = new FakeGameApi();
            var input = new RecordingKeyboardInput();
            var logger = new InMemoryRoadhogLogger();
            var session = new ExitSession { Scene = new("worker-test/offline", 0, false, false, false, false, false, 0, false) };
            var mailbox = new GameSessionRequestMailbox();
            Require(mailbox.Request(GameSessionOperation.Relogin).Success, "queue offline relogin");
            if (alreadyCompleted) mailbox.Complete(mailbox.Current!);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var (loop, context) = Create(api, input, logger, session, stop.Token, mailbox);
            var work = loop.RunAsync(context);
            try
            {
                await UntilAsync(() => mailbox.Completed is not null, work, stop.Token);
                await Task.Delay(350, stop.Token);
                Require(!work.IsCompleted && mailbox.Completed!.Operation == GameSessionOperation.Relogin,
                    "The live worker holds the completed request for manager handoff.");
                Require(api.PlayerReadCount == 0 && api.QuickbarReadCount == 0 && api.TransitionReadCount == 0 &&
                    input.Keys.IsEmpty && input.MouseCommands.Count == 0,
                    "An absent game must not receive startup input or block on old player/readiness/quickbar data.");
                Require(session.Reads == (alreadyCompleted ? 0 : 1), "Completed handoff never re-observes the old game.");
            }
            finally { await StopAsync(stop, work); }
            Require(session.Disposals == 1, "Offline Stop disposes its session.");
        }
    }

    internal static async Task ProcessDisappearanceStopsOldWorldReadsAsync()
    {
        var api = new FakeGameApi();
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var session = new ExitSession { Scene = new("worker-test/offline", 0, false, false, false, false, false, 0, false) };
        var mailbox = new GameSessionRequestMailbox();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var (loop, context) = Create(api, input, logger, session, stop.Token, mailbox);
        var worldReadsAtExit = -1;
        session.BeforeRead = () => worldReadsAtExit = api.PlayerReadCount;
        var work = loop.RunAsync(context);
        try
        {
            await UntilAsync(() => api.PlayerReadCount >= 3, work, stop.Token);
            Require(mailbox.Request(GameSessionOperation.Relogin).Success, "enqueue relogin into active ordinary work");
            await UntilAsync(() => mailbox.Completed is not null, work, stop.Token);
            await Task.Delay(600, stop.Token);
            Require(worldReadsAtExit >= 3 && api.PlayerReadCount == worldReadsAtExit && session.Reads == 1,
                "Once absence is confirmed, successive ticks perform no old-player or exit-scene reads.");
            Require(logger.Entries.Count(entry => entry.EventName == "game_exit.completed") == 1 && !work.IsCompleted,
                "Completion is handed off once while the worker remains controllable.");
        }
        finally { await StopAsync(stop, work); }
    }

    internal static async Task DeathDuringCountdownCancelsThenRevivesAndRetriesAsync()
    {
        var previous = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
        var api = new FakeGameApi();
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var session = new ExitSession();
        var mailbox = new GameSessionRequestMailbox();
        Require(mailbox.Request(GameSessionOperation.Relogin).Success, "queue relogin before countdown interruption");
        var events = new List<string>();
        var quitCount = 0;
        session.BeforeRead = () => session.Scene = session.Scene with { CurrentHp = api.Player.CurrentHp, IsDead = api.Player.IsDead };
        session.OnAction = action =>
        {
            events.Add(action.ToString());
            if (action == GameExitAction.OpenMenu)
                session.Scene = session.Scene with { MenuOpen = true, CanQuit = true };
            if (action == GameExitAction.Quit)
            {
                if (++quitCount == 1)
                {
                    session.Scene = session.Scene with { MenuOpen = false, CanQuit = false, CountdownOpen = true, CanCancel = true };
                    api.Player = api.Player with { CurrentHp = 0 };
                }
                else session.Scene = session.Scene with { ProcessPresent = false, ProcessId = 0 };
            }
            if (action == GameExitAction.Cancel)
                session.Scene = session.Scene with { CountdownOpen = false, CanCancel = false };
        };
        input.AfterMouseUp = button =>
        {
            if (button == RoadhogMouseButton.Left && api.Player.IsDead && logger.Entries.Any(entry => entry.EventName == "player_life.death.detected"))
            {
                events.Add("Revive");
                api.Player = api.Player with { CurrentHp = 100 };
            }
        };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (loop, context) = Create(api, input, logger, session, stop.Token, mailbox);
        var work = loop.RunAsync(context);
        try
        {
            await UntilAsync(() => mailbox.Completed is not null, work, stop.Token);
            Require(api.Player.IsAlive && quitCount == 2, "A death interruption retains the request and retries exit after resurrection.");
            var cancel = events.IndexOf("Cancel");
            var revive = events.IndexOf("Revive");
            var lastQuit = events.LastIndexOf("Quit");
            Require(cancel >= 0 && revive > cancel && lastQuit > revive,
                "The countdown is cancelled before revive input, and a new quit occurs only after revival.");
            Require(!logger.Entries.Any(entry => entry.EventName == "worker.tick.failed_continuing"),
                "Death interruption uses the normal life guard rather than exception recovery.");
        }
        finally
        {
            try { await StopAsync(stop, work); }
            finally { Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previous); }
        }
    }

    internal static async Task StopCancelsCountdownAcrossWorkerPhasesAsync()
    {
        foreach (var duringStartup in new[] { false, true })
        {
            var api = new FakeGameApi();
            var input = new RecordingKeyboardInput();
            var logger = new InMemoryRoadhogLogger();
            var active = new ExitSession();
            var cleanup = new ExitSession();
            var mailbox = new GameSessionRequestMailbox();
            Require(mailbox.Request(GameSessionOperation.Relogin).Success, "queue exit before Stop");
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            if (duringStartup)
            {
                active.Scene = active.Scene with { CountdownOpen = true, CanCancel = true };
                input.AfterScroll = _ => stop.Cancel();
            }
            active.OnAction = action =>
            {
                if (action == GameExitAction.OpenMenu) active.Scene = active.Scene with { MenuOpen = true, CanQuit = true };
                if (action == GameExitAction.Quit)
                {
                    active.Scene = active.Scene with { MenuOpen = false, CanQuit = false, CountdownOpen = true, CanCancel = true };
                    stop.Cancel();
                }
            };
            cleanup.BeforeRead = () => cleanup.Scene = active.Scene;
            cleanup.OnAction = action =>
            {
                Require(action == GameExitAction.Cancel, "Stop cleanup must never open a menu or submit a new quit.");
                active.Scene = active.Scene with { CountdownOpen = false, CanCancel = false };
            };
            var created = 0;
            var (loop, context) = Create(api, input, logger, active, stop.Token, mailbox,
                () => ++created == 1 ? active : cleanup);
            var work = loop.RunAsync(context);
            try
            {
                try { await work.WaitAsync(TimeSpan.FromSeconds(8)); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                Require(stop.IsCancellationRequested && mailbox.Completed is null,
                    "Stop never publishes a relogin completion.");
                Require(created == 2 && cleanup.Actions.SequenceEqual(new[] { GameExitAction.Cancel }) && cleanup.Reads >= 2,
                    "Both startup and active-countdown Stop use a fresh adapter and confirm cancellation after clicking.");
                Require(!active.Scene.CountdownOpen && active.Disposals == 1 && cleanup.Disposals == 1,
                    "A stopped worker leaves the game online with no countdown and disposes both adapters.");
            }
            finally { await StopAsync(stop, work); }
        }
    }
}
