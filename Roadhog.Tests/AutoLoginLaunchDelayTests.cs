using Roadhog.Application.Login;

internal static class AutoLoginLaunchDelayTests
{
    // Keep the production 20-second delay; only wall-clock polling is shortened.
    private static readonly AutoLoginOptions Options = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(1),
        TransitionTimeout = TimeSpan.FromSeconds(2),
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static LoginSnapshot Scene(LoginStage stage, LoginAction action = LoginAction.None, string session = "one", int processCount = 0) =>
        new(session, stage, action, action.ToString(), stage.ToString(), stage == LoginStage.InWorld ? "HiApple" : "",
            stage == LoginStage.InWorld ? 123 : 0, processCount);

    private sealed class Clock : TimeProvider
    {
        internal double Seconds;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => TimeSpan.FromSeconds(Seconds).Ticks;
    }

    private sealed class Session(Clock clock, params (double Time, LoginSnapshot Snapshot)[] frames) : IAutoLoginSession
    {
        private int _index;
        internal readonly List<(LoginAction Action, double Time)> Inputs = [];
        internal int Releases;
        internal Action<LoginSnapshot>? OnRead;
        internal Func<LoginAction, bool>? OnExecute;

        public Task<LoginSnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var frame = frames[Math.Min(_index++, frames.Length - 1)];
            clock.Seconds = frame.Time;
            OnRead?.Invoke(frame.Snapshot);
            return Task.FromResult(frame.Snapshot);
        }

        public Task<bool> ExecuteAsync(LoginSnapshot snapshot, LoginAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Inputs.Add((action, clock.Seconds));
            return Task.FromResult(OnExecute?.Invoke(action) ?? true);
        }

        public Task ReleaseInputAsync() { Releases++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Messages : IProgress<string>
    {
        internal readonly List<string> Values = [];
        public void Report(string value) => Values.Add(value);
    }

    internal static async Task WaitsTwentySecondsAcrossWindowsAsync()
    {
        Require(Options.LauncherReadyDelay == TimeSpan.FromSeconds(20), "The default launch delay must be 20 seconds.");
        var clock = new Clock();
        var messages = new Messages();
        await using var session = new Session(clock,
            (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)),
            (6, Scene(LoginStage.Desktop, LoginAction.OpenAccelerator, "uu-minimized")),
            (7, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "launcher-created")),
            (12, Scene(LoginStage.Loading, session: "launcher-loading")),
            (24.999, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "launcher-ready")),
            (25, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "launcher-ready")),
            (26, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "launcher-ready")),
            (27, Scene(LoginStage.InWorld)))
        {
            // Count from completion of accepted launch input, not the preceding capture.
            OnExecute = action => { if (action == LoginAction.LaunchGame) clock.Seconds = 5; return true; }
        };
        var result = await new AutoLoginRunner(Options, clock).RunAsync(session, "HiApple", messages, CancellationToken.None);
        Require(result.Success && session.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d), (LoginAction.StartLauncher, 25d) }),
            "An early window, changing process identity or loading page must neither bypass nor restart the 20-second delay; no duplicate clicks.");
        Require(messages.Values.Any(value => value.Contains("20 秒")) && session.Releases == 1,
            "The warm-up remains visible and completion releases input.");
    }

    internal static async Task LateWindowAndExistingLauncherAsync()
    {
        var clock = new Clock();
        await using (var late = new Session(clock,
            (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)),
            (10, Scene(LoginStage.Loading)),
            (25, Scene(LoginStage.Launcher, LoginAction.StartLauncher)),
            (26, Scene(LoginStage.InWorld))))
        {
            Require((await new AutoLoginRunner(Options, clock).RunAsync(late, "HiApple", null, CancellationToken.None)).Success
                && late.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d), (LoginAction.StartLauncher, 25d) }),
                "A launcher appearing after the delay must not wait another 20 seconds.");
        }
        await using var existing = new Session(clock,
            (0, Scene(LoginStage.Launcher, LoginAction.StartLauncher)), (1, Scene(LoginStage.InWorld)));
        Require((await new AutoLoginRunner(Options, clock).RunAsync(existing, "HiApple", null, CancellationToken.None)).Success
            && existing.Inputs.SequenceEqual(new[] { (LoginAction.StartLauncher, 0d) }),
            "Resuming an existing launcher without this run's UU launch keeps the existing flow.");
    }

    internal static async Task RejectedLaunchAndOnlineBypassAsync()
    {
        var clock = new Clock();
        await using (var rejected = new Session(clock,
            (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)),
            (1, Scene(LoginStage.Launcher, LoginAction.StartLauncher)),
            (2, Scene(LoginStage.InWorld))) { OnExecute = action => action != LoginAction.LaunchGame })
        {
            Require((await new AutoLoginRunner(Options, clock).RunAsync(rejected, "HiApple", null, CancellationToken.None)).Success
                && rejected.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d), (LoginAction.StartLauncher, 1d) }),
                "A launch rejected before input must not start a warm-up timer.");
        }
        await using var online = new Session(clock,
            (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)), (2, Scene(LoginStage.InWorld)));
        Require((await new AutoLoginRunner(Options, clock).RunAsync(online, "HiApple", null, CancellationToken.None)).Success
            && online.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d) }),
            "Verified online state finishes immediately even during the launcher warm-up.");
    }

    internal static async Task ReconnectAndExitStartFreshDelayAsync()
    {
        foreach (bool disconnect in new[] { false, true })
        {
            var clock = new Clock();
            var interruption = disconnect ? LoginAction.ConfirmGameExit : LoginAction.Reaccelerate;
            await using var session = new Session(clock,
                (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)),
                (3, Scene(LoginStage.Launcher, LoginAction.StartLauncher)),
                (6, Scene(disconnect ? LoginStage.Disconnected : LoginStage.AcceleratorReconnect, interruption, processCount: disconnect ? 1 : 0)),
                (7, disconnect ? Scene(LoginStage.Desktop, LoginAction.OpenAccelerator, processCount: 1) : Scene(LoginStage.Accelerating)),
                (8, Scene(LoginStage.Accelerating, session: "clients-exited")),
                (18, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame, "new-launch")),
                (20, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "new-launcher")),
                (37.999, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "new-launcher")),
                (38, Scene(LoginStage.Launcher, LoginAction.StartLauncher, "new-launcher")),
                (39, Scene(LoginStage.InWorld)));
            Require((await new AutoLoginRunner(Options, clock).RunAsync(session, "HiApple", null, CancellationToken.None)).Success
                && session.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d), (interruption, 6d), (LoginAction.LaunchGame, 18d), (LoginAction.StartLauncher, 38d) }),
                "Exit/reconnect interrupts warm-up; only the new accepted launch starts its fresh 20-second delay.");
            Require(session.Releases == 1, "Recovery completion releases input.");
        }
    }

    internal static async Task StopDuringDelayAsync()
    {
        var clock = new Clock();
        using var cancel = new CancellationTokenSource();
        await using var session = new Session(clock,
            (0, Scene(LoginStage.AcceleratorReady, LoginAction.LaunchGame)),
            (5, Scene(LoginStage.Launcher, LoginAction.StartLauncher)))
        {
            OnRead = snapshot => { if (snapshot.Stage == LoginStage.Launcher) cancel.Cancel(); }
        };
        bool canceled = false;
        try { await new AutoLoginRunner(Options, clock).RunAsync(session, "HiApple", null, cancel.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Require(canceled && session.Inputs.SequenceEqual(new[] { (LoginAction.LaunchGame, 0d) }) && session.Releases == 1,
            "Stopping during warm-up cancels immediately, releases input and never presses Start.");
    }
}
