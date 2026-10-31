using System.Drawing;
using System.Text.Json;
using Roadhog.Application.Login;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class UuLaunchFallbackTests
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class Clock : TimeProvider
    {
        internal double Seconds;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => TimeSpan.FromSeconds(Seconds).Ticks;
    }

    private static JsonElement Window(int x = 12, int y = 20, int width = 1000, int height = 688,
        bool visible = true, bool disabled = false, string style = "0x94000000", uint thread = 7, string hwnd = "0x9033C") =>
        LoginNativeReader.Json(new { Hwnd = hwnd, OwnerThread = thread, Visible = visible, Disabled = disabled,
            Style = style, ClientRect = new[] { x, y, x + width, y + height } });

    private static JsonElement Foreground(string name = "uu.exe", uint thread = 7) =>
        LoginNativeReader.Json(new { Pid = 10u, Name = name, Tid = thread });

    private static LoginObservation Target(string session = "one") =>
        UuLaunchFallbackWindow.Project(session, 1, ["uu.exe"], Foreground(), [Window()])!;

    private static LoginObservation Fault() => throw new UuWindowReadException(
        new InvalidOperationException("Incomplete UU UI field: address=0x143CFE4, expected=4, actual=0."));

    internal static Task DeadlineAndRecoveryAsync()
    {
        var clock = new Clock(); var fallback = new UuLaunchFallback(clock); int windowReads = 0;
        LoginObservation Read() => fallback.Capture("one", Fault, () => { windowReads++; return Target(); }, CancellationToken.None);
        Reject<UuWindowReadException>(() => Read(), "A cold failure must not immediately click.");
        clock.Seconds = 29.999;
        Reject<UuWindowReadException>(() => Read(), "Thirty seconds must elapse.");
        Require(windowReads == 0, "No window-only recovery before the threshold.");
        clock.Seconds = 30;
        Require(UuLaunchFallback.IsFallback(Read().Snapshot), "The exact threshold permits the independent window path.");

        var normal = Target() with { Snapshot = Target().Snapshot with { ActionIdentity = "normal-launch" } };
        Require(fallback.Capture("one", () => normal, () => throw new Exception("Normal recognition must win"), CancellationToken.None) == normal,
            "Recovered control recognition takes priority.");
        clock.Seconds = 31; Reject<UuWindowReadException>(() => Read(), "Success resets the consecutive failure period.");
        clock.Seconds = 60.999; Reject<UuWindowReadException>(() => Read(), "Separate failures cannot accumulate.");
        clock.Seconds = 61; Require(Read().Point == new Point(107, 366), "A new continuous failure period may recover.");
        clock.Seconds = 62;
        Reject<UuWindowReadException>(() => fallback.Capture("two", Fault, () => Target("two"), CancellationToken.None),
            "Process/module session replacement starts a fresh timer.");
        clock.Seconds = 92;
        Require(fallback.Capture("two", Fault, () => Target("two"), CancellationToken.None).Snapshot.Session == "two", "New session cannot borrow old identity.");
        return Task.CompletedTask;
    }

    internal static Task ArchivedGeometryAndWindowGuardsAsync()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures, "11-uu-ui-after-acc.json")));
        var launch = fixture.RootElement.EnumerateArray().SelectMany(p => p.GetProperty("Controls").EnumerateArray())
            .Single(c => LoginNativeReader.Text(c, "Name") == "launch_game" && LoginNativeReader.Flag(c, "Visible") && LoginNativeReader.Flag(c, "AncestorsVisible"));
        var rect = launch.GetProperty("Rect");
        Require(UuLaunchFallbackWindow.LaunchPoint == new Point((rect.GetProperty("Left").GetInt32() + rect.GetProperty("Right").GetInt32()) / 2,
            (rect.GetProperty("Top").GetInt32() + rect.GetProperty("Bottom").GetInt32()) / 2), "Fixed point must match the archived real launch control.");
        LoginObservation? Project(params JsonElement[] windows) => UuLaunchFallbackWindow.Project("one", 1, ["uu.exe", "uu.exe"], Foreground(), windows);
        Require(Project(Window(200, 100))!.Point == new Point(295, 446), "A moved window uses client-relative coordinates, including with extra UU processes.");
        foreach (var window in new[] { Window(width: 1250, height: 860), Window(height: 650), Window(x: -1), Window(y: 1900),
            Window(visible: false), Window(disabled: true), Window(style: "0xB4000000"), Window(style: "0x54000000"), Window(thread: 8), Window(hwnd: "0x0") })
            Require(Project(window) is null, "Unsupported geometry, hidden/minimized/disabled/child windows and foreign threads cannot authorize a click.");
        Require(Project(Window(), Window(width: 360, height: 416, hwnd: "0x200")) is null, "A popup on the same UI thread blocks fixed input.");
        Require(Project(Window(), Window(hwnd: "0x200")) is null, "Ambiguous main windows block fixed input.");
        Require(Project(Window(), Window(visible: false, hwnd: "0x200")) is not null, "Hidden helper windows are not a modal obstruction.");
        Require(UuLaunchFallbackWindow.Project("one", 1, ["uu.exe"], Foreground("explorer.exe"), [Window()]) is null, "A background UU cannot receive fixed input.");
        foreach (var name in new[] { "Aion.bin", "Launcher.dat", "sdologin.exe", "AION.BIN" })
            Require(UuLaunchFallbackWindow.Project("one", 1, ["uu.exe", name], Foreground(), [Window()]) is null, "An existing client blocks the fallback: " + name);
        return Task.CompletedTask;
    }

    internal static Task FaultIsolationAndCancellationAsync()
    {
        var clock = new Clock(); var fallback = new UuLaunchFallback(clock);
        Reject<UuWindowReadException>(() => fallback.Capture("one", Fault, () => Target(), CancellationToken.None), "Initial read fails.");
        clock.Seconds = 31;
        Reject<UuWindowReadException>(() => fallback.Capture("one", Fault, () => null, CancellationToken.None), "A missing/unsafe window cannot fabricate a target.");
        Reject<IOException>(() => fallback.Capture("one", Fault, () => throw new IOException("window table"), CancellationToken.None), "Failed window evidence must not authorize input.");
        Reject<IOException>(() => fallback.Capture("one", () => throw new IOException("unrelated read"), () => throw new Exception(), CancellationToken.None),
            "Only UU-specific failures enable recovery.");
        Reject<UuWindowReadException>(() => fallback.Capture("one", Fault, () => Target(), CancellationToken.None), "An unrelated failure interrupts the consecutive UU failure period.");
        clock.Seconds = 62;
        using var stop = new CancellationTokenSource();
        Reject<OperationCanceledException>(() => fallback.Capture("one", Fault, () => { stop.Cancel(); return Target(); }, stop.Token),
            "Cancellation during the window read discards even a complete target.");
        Reject<OperationCanceledException>(() => fallback.TryReserveAttempt(stop.Token), "A stopped session cannot reserve/send input.");
        Require(fallback.TryReserveAttempt(CancellationToken.None), "Cancellation before reservation does not consume the one attempt.");
        Require(!fallback.TryReserveAttempt(CancellationToken.None), "Reservation is one-shot.");
        return Task.CompletedTask;
    }

    internal static async Task SingleAttemptAndLostAcknowledgementAsync()
    {
        var clock = new Clock(); var fallback = new UuLaunchFallback(clock);
        Reject<UuWindowReadException>(() => fallback.Capture("one", Fault, () => Target(), CancellationToken.None), "Initial read fails.");
        clock.Seconds = 31; fallback.Capture("one", Fault, () => Target(), CancellationToken.None);
        int clicks = 0; bool awaiting = false;
        var recovery = new LoginInputRecovery("test");
        bool accepted = await recovery.ExecuteAsync(() =>
        {
            Require(fallback.TryReserveAttempt(CancellationToken.None), "Reserve before the uncertain send.");
            awaiting = true; clicks++; throw new IOException("mouse-down acknowledgement lost");
        }, _ => Task.FromResult(OperationResult.Ok()), CancellationToken.None, () => awaiting);
        Require(accepted && clicks == 1, "An uncertain send waits for outcome after releasing input.");
        clock.Seconds = 100;
        Reject<UuWindowReadException>(() => fallback.Capture("one", Fault, () => throw new Exception("No second window attempt"), CancellationToken.None), "Cannot click twice.");
        Reject<UuWindowReadException>(() => fallback.Capture("two", Fault, () => Target("two"), CancellationToken.None), "Replacement cannot restore the attempt.");
        clock.Seconds = 200;
        Reject<UuWindowReadException>(() => fallback.Capture("two", Fault, () => throw new Exception("No replay"), CancellationToken.None), "One attempt per login run, not per PID.");
        var normal = new UuLaunchFallback(clock); normal.SuppressAfterNormalLaunch();
        Reject<UuWindowReadException>(() => normal.Capture("one", Fault, () => Target(), CancellationToken.None), "Start normal failure timer.");
        clock.Seconds = 240;
        Reject<UuWindowReadException>(() => normal.Capture("one", Fault, () => throw new Exception("No fallback after normal launch"), CancellationToken.None), "Normal launch also suppresses blind replay.");
    }

    private sealed class Session(bool appearAfterClick) : IAutoLoginSession
    {
        private readonly Clock _clock = new();
        private readonly LoginSnapshotPublisher _publisher = new(new("test", 0, "", "device"));
        private int _reads;
        internal int Clicks, Releases;
        internal Action? BeforeRebind;
        internal Func<LoginObservation?> Window = () => Target();
        internal Session() : this(true) { }

        // Field initializer cannot refer to the instance clock.
        private UuLaunchFallback Fallback => _fallbackLazy ??= new(_clock);
        private UuLaunchFallback? _fallbackLazy;

        internal LoginObservation? Capture(Action<LoginObservation>? bind = null) => _publisher.Capture(lifecycle =>
        {
            lifecycle("one");
            return Fallback.Capture("one", Fault, Window, CancellationToken.None);
        }, bind);

        public Task<LoginSnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Clicks > 0 && appearAfterClick)
                return Task.FromResult(new LoginSnapshot("game", LoginStage.InWorld, LoginAction.None, "world", "online", "target", 99, 1));
            if (_reads++ == 0) { Require(Capture() is null, "Cold read remains unpublished."); _clock.Seconds = 30; }
            return Task.FromResult(Capture()!.Snapshot);
        }

        public Task<bool> ExecuteAsync(LoginSnapshot expected, LoginAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LoginObservation? bound = null; Capture(value => bound = value);
            if (bound is null || bound.Snapshot != expected) return Task.FromResult(false);
            BeforeRebind?.Invoke();
            LoginObservation? after = null; Capture(value => after = value);
            if (after is null) return Task.FromResult(false);
            try { LoginInputTarget.Validate(bound, after, compareAction: true); }
            catch (InvalidOperationException) { return Task.FromResult(false); }
            if (!Fallback.TryReserveAttempt(token)) return Task.FromResult(false);
            Clicks++;
            return Task.FromResult(true);
        }
        public Task ReleaseInputAsync() { Releases++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { _publisher.Dispose(); return ValueTask.CompletedTask; }
    }

    private static AutoLoginOptions Options => new()
    { PollInterval = TimeSpan.FromMilliseconds(1), TransitionTimeout = TimeSpan.FromMilliseconds(120), Timeout = TimeSpan.FromSeconds(3) };

    internal static async Task PublicationAndLoginFlowAsync()
    {
        await using var success = new Session();
        var result = await new AutoLoginRunner(Options).RunAsync(success, "target", null, CancellationToken.None);
        Require(result.Success && result.Value == 99 && success.Clicks == 1 && success.Releases == 1, "Fallback completes through the existing runner and releases input.");
        await using var timeout = new Session(false);
        result = await new AutoLoginRunner(Options).RunAsync(timeout, "target", null, CancellationToken.None);
        Require(!result.Success && result.Error!.Contains("超时") && timeout.Clicks == 1 && timeout.Releases == 1, "No client after click obeys the bounded transition deadline without replay.");
    }

    internal static async Task RebindingRejectsChangedWindowAsync()
    {
        foreach (var mode in new[] { "moved", "focus_lost", "replaced", "normal_recovered" })
        {
            await using var session = new Session();
            session.BeforeRebind = () => session.Window = mode switch
            {
                "moved" => () => Target() with { Bounds = new(13, 20, 1000, 688) },
                "focus_lost" => () => null,
                "replaced" => () => Target() with { Window = "0x200" },
                _ => () => Target() with { Snapshot = Target().Snapshot with { ActionIdentity = "recognized-button" } }
            };
            // Keep changing on every rebind, so no stable old target can be sent.
            var initial = await session.ReadAsync(CancellationToken.None);
            Require(!await session.ExecuteAsync(initial, LoginAction.LaunchGame, CancellationToken.None) && session.Clicks == 0,
                "Changed geometry/focus/identity or restored recognition rejects stale fixed input: " + mode);
        }
    }
}
