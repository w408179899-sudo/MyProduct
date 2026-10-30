using Roadhog.Application.GameSessions;

internal static class GameExitControllerTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds);
    }

    private sealed class Session : IGameExitSession
    {
        internal GameExitSnapshot Scene = InWorld();
        internal List<GameExitAction> Actions { get; } = [];
        internal int Reads;
        internal bool AcceptAction = true;
        internal Action? OnRead;
        internal Action<GameExitAction>? OnExecute;
        public Task<GameExitSnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            OnRead?.Invoke();
            return Task.FromResult(Scene);
        }
        public Task<bool> ExecuteAsync(GameExitSnapshot expected, GameExitAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(expected == Scene, "An input must use the scene read in this tick.");
            Actions.Add(action);
            OnExecute?.Invoke(action);
            return Task.FromResult(AcceptAction);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static GameExitSnapshot InWorld() => new("account-one/process-123", 123, true, false, false, false, false, 100, false);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task SubmitExitAsync(GameExitController controller, Session session, Clock clock, Func<Task<bool>> prepare)
    {
        await controller.TickAsync(session, prepare, CancellationToken.None);
        await controller.TickAsync(session, prepare, CancellationToken.None);
        session.Scene = session.Scene with { MenuOpen = true, CanQuit = true };
        clock.Advance(0.7);
        await controller.TickAsync(session, prepare, CancellationToken.None);
        Require(session.Actions.SequenceEqual(new[] { GameExitAction.OpenMenu, GameExitAction.Quit }),
            "A normal exit first opens the menu, then selects its quit control.");
        session.Scene = session.Scene with { MenuOpen = false, CanQuit = false, CountdownOpen = true, CanCancel = true };
    }

    internal static async Task TwelveSecondsCannotProveExitAsync()
    {
        var clock = new Clock();
        var controller = new GameExitController(clock);
        await using var session = new Session();
        var preparations = 0;
        Task<bool> Prepare() { preparations++; return Task.FromResult(true); }
        await SubmitExitAsync(controller, session, clock, Prepare);
        clock.Advance(11.999);
        var waiting = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!waiting.Completed && !waiting.YieldToCombat && session.Actions.Count == 2,
            "Before the observation deadline, countdown input stays idle.");
        clock.Advance(0.001);
        var timeout = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!timeout.Completed && !timeout.YieldToCombat && session.Actions.Last() == GameExitAction.Cancel,
            "Twelve elapsed seconds with a live process must cancel/retry, never authorize login.");
        Require(preparations == 1, "Combat preparation cannot run over a visible countdown.");
        session.Scene = session.Scene with { CountdownOpen = false, CanCancel = false };
        var closed = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!closed.Completed && closed.YieldToCombat, "Only observed cancellation yields control back to combat.");
        session.Scene = session.Scene with { ProcessPresent = false, ProcessId = 0 };
        var complete = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(complete.Completed && !complete.YieldToCombat && preparations == 1,
            "Confirmed process absence completes without reading or preparing the old character.");
    }

    internal static async Task DamageCancelsBeforeCombatAndRetriesAsync()
    {
        var clock = new Clock();
        var controller = new GameExitController(clock);
        await using var session = new Session();
        var preparations = 0;
        var peaceful = true;
        Task<bool> Prepare() { preparations++; return Task.FromResult(peaceful); }
        await SubmitExitAsync(controller, session, clock, Prepare);
        clock.Advance(1);
        peaceful = false;
        session.Scene = session.Scene with { CurrentHp = 80 };
        var hit = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!hit.Completed && !hit.YieldToCombat && session.Actions.Last() == GameExitAction.Cancel,
            "Damage during countdown requests cancellation before any combat input.");
        var stillVisible = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!stillVisible.YieldToCombat && preparations == 1,
            "Sending cancel alone does not prove that the countdown is closed.");
        session.Scene = session.Scene with { CountdownOpen = false, CanCancel = false };
        Require((await controller.TickAsync(session, Prepare, CancellationToken.None)).YieldToCombat,
            "The closed countdown releases input ownership.");
        var fighting = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(fighting.YieldToCombat && preparations == 2 && session.Actions.Count == 3,
            "An unfinished fight retains the request without another menu input.");
        peaceful = true;
        clock.Advance(1);
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(preparations == 3 && session.Actions.Last() == GameExitAction.OpenMenu,
            "After combat preparation succeeds, the same request starts a fresh exit attempt.");
        session.Scene = session.Scene with { MenuOpen = true, CanQuit = true };
        clock.Advance(0.7);
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        session.Scene = session.Scene with { ProcessPresent = false, ProcessId = 0 };
        Require((await controller.TickAsync(session, Prepare, CancellationToken.None)).Completed,
            "The retried exit completes only after the process disappears.");
        Require(session.Actions.Count(action => action == GameExitAction.Quit) == 2,
            "The interrupted request performs exactly one fresh quit after combat.");
    }

    internal static async Task ClosedCountdownWaitsForProcessTeardownAsync()
    {
        foreach (var exitsBeforeDeadline in new[] { true, false })
        {
            var clock = new Clock();
            var controller = new GameExitController(clock);
            await using var session = new Session();
            var preparations = 0;
            Task<bool> Prepare() { preparations++; return Task.FromResult(true); }
            await SubmitExitAsync(controller, session, clock, Prepare);
            clock.Advance(11);
            session.Scene = session.Scene with { CountdownOpen = false, CanCancel = false };
            var closed = await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(!closed.Completed && !closed.YieldToCombat,
                "Closing the countdown is not proof of process exit and must not resume combat.");
            clock.Advance(1);
            var atTwelve = await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(!atTwelve.Completed && !atTwelve.YieldToCombat && preparations == 1 && session.Actions.Count == 2,
                "At twelve seconds, a closed countdown with a live process must not reopen O, cancel, or prepare combat.");
            clock.Advance(13);
            if (exitsBeforeDeadline) session.Scene = session.Scene with { ProcessPresent = false, ProcessId = 0 };
            var atTwentyFive = await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(atTwentyFive.Completed == exitsBeforeDeadline && !atTwentyFive.YieldToCombat &&
                preparations == 1 && session.Actions.Count == 2,
                "At twenty-five seconds, only confirmed process absence completes; teardown receives no extra input.");
            if (exitsBeforeDeadline) continue;
            clock.Advance(4.999);
            var beforeDeadline = await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(!beforeDeadline.Completed && !beforeDeadline.YieldToCombat && preparations == 1 && session.Actions.Count == 2,
                "A live process is observed without combat or input until the full thirty-second teardown deadline.");
            clock.Advance(0.001);
            var expired = await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(!expired.Completed && expired.YieldToCombat && preparations == 1 && session.Actions.Count == 2,
                "Only the thirty-second deadline releases an incomplete teardown for retry preparation.");
            await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(preparations == 2 && session.Actions.Count == 2,
                "A retry must prepare again before issuing more menu input.");
            await controller.TickAsync(session, Prepare, CancellationToken.None);
            Require(session.Actions.SequenceEqual(new[] { GameExitAction.OpenMenu, GameExitAction.Quit, GameExitAction.OpenMenu }),
                "An overdue live process starts one new menu attempt after preparation.");
        }
    }

    internal static async Task ExistingCountdownMustBeCancelledAsync()
    {
        var clock = new Clock();
        var controller = new GameExitController(clock);
        await using var session = new Session
        {
            Scene = InWorld() with { CountdownOpen = true, CanCancel = false }
        };
        var preparations = 0;
        Task<bool> Prepare() { preparations++; return Task.FromResult(true); }
        var blocked = await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(!blocked.YieldToCombat && preparations == 0 && session.Actions.Count == 0,
            "An existing countdown without a usable cancel control must not receive guessed input or combat input.");
        session.Scene = session.Scene with { CanCancel = true };
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(session.Actions.SequenceEqual(new[] { GameExitAction.Cancel }) && preparations == 0,
            "A preexisting countdown is cancelled before preparing a new exit.");
        session.Scene = session.Scene with { CountdownOpen = false, CanCancel = false };
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        await controller.TickAsync(session, Prepare, CancellationToken.None);
        Require(preparations == 1, "Preparation resumes after cancellation has been observed.");
    }

    internal static async Task RejectedQuitDoesNotStartCountdownAsync()
    {
        var clock = new Clock();
        var controller = new GameExitController(clock);
        await using var session = new Session { Scene = InWorld() with { MenuOpen = true, CanQuit = true }, AcceptAction = false };
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        clock.Advance(0.7);
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        Require(session.Actions.SequenceEqual(new[] { GameExitAction.Quit, GameExitAction.Quit }),
            "A rejected physical click is rebound and retried, without waiting twelve seconds for a nonexistent countdown.");
    }

    internal static async Task ObservationStartsAfterAcceptedInputAsync()
    {
        var clock = new Clock();
        var controller = new GameExitController(clock);
        await using var session = new Session
        {
            Scene = InWorld() with { MenuOpen = true, CanQuit = true },
            OnExecute = action => { if (action == GameExitAction.Quit) clock.Advance(5); }
        };
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        session.Scene = session.Scene with { MenuOpen = false, CanQuit = false, CountdownOpen = true, CanCancel = true };
        clock.Advance(11.999);
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        Require(session.Actions.SequenceEqual(new[] { GameExitAction.Quit }),
            "Window focus and mouse travel must not consume the twelve-second observation after accepted quit input.");
        clock.Advance(0.001);
        await controller.TickAsync(session, () => Task.FromResult(true), CancellationToken.None);
        Require(session.Actions.Last() == GameExitAction.Cancel,
            "The full observation window expires twelve seconds after the physical quit action completes.");
    }

    internal static async Task StopAtAwaitBoundariesPreventsContinuationAsync()
    {
        foreach (var phase in new[] { "before-read", "during-read", "during-prepare" })
        {
            using var stop = new CancellationTokenSource();
            await using var session = new Session();
            var prepared = false;
            if (phase == "before-read") stop.Cancel();
            if (phase == "during-read") session.OnRead = stop.Cancel;
            try
            {
                await new GameExitController().TickAsync(session, () =>
                {
                    prepared = true;
                    if (phase == "during-prepare") stop.Cancel();
                    return Task.FromResult(true);
                }, stop.Token);
                throw new InvalidOperationException("Stop must abort the tick at " + phase);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Require(session.Actions.Count == 0, "Cancelled exit cannot submit physical input.");
            if (phase != "during-prepare") Require(!prepared, "Cancelled read cannot enter combat preparation.");
            if (phase == "before-read") Require(session.Reads == 0, "Stop must reject a new read.");
        }
    }

    internal static Task MailboxIsolationAndCompletionAsync()
    {
        var first = new GameSessionRequestMailbox();
        var second = new GameSessionRequestMailbox();
        Require(first.Request(GameSessionOperation.Exit).Success && second.Request(GameSessionOperation.Relogin).Success,
            "Independent accounts can request different session operations.");
        var exiting = first.Current!;
        var relogging = second.Current!;
        Require(exiting.Operation == GameSessionOperation.Exit && relogging.Operation == GameSessionOperation.Relogin && exiting.Id != relogging.Id,
            "Completion identity and requested continuation are account-scoped.");
        Require(!first.Request(GameSessionOperation.Relogin).Success && first.Current == exiting,
            "Repeated clicks cannot replace a running exit with another intent.");
        var rejectedForeignCompletion = false;
        try
        {
            first.Complete(relogging);
        }
        catch (InvalidOperationException) { rejectedForeignCompletion = true; }
        Require(rejectedForeignCompletion && first.Current == exiting && first.Completed is null,
            "A foreign completion must be rejected without changing the pending request.");
        first.Complete(exiting);
        Require(first.Current is null && first.Completed == exiting && second.Current == relogging && second.Completed is null,
            "One account's completion must not consume another account's pending request.");
        Require(!first.Request(GameSessionOperation.Relogin).Success, "Completion awaiting manager handoff rejects duplicate requests.");
        second.Complete(relogging);
        Require(second.Completed?.Operation == GameSessionOperation.Relogin && first.Completed?.Operation == GameSessionOperation.Exit,
            "Exit-only and relogin preserve distinct manager handoffs.");
        return Task.CompletedTask;
    }
}
