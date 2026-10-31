using Roadhog.Application.GameSessions;
using Roadhog.Infrastructure.GameSessions;
using Roadhog.Infrastructure.Vmm;

internal static class GameDisconnectTests
{
    private static readonly GameProcessIdentity Game = new(8780, "game-session-1");
    private static GameProcessSnapshot Snapshot(params GameProcessIdentity[] games) => new("device-account-connection", games);
    private static GameProcessSnapshotPublisher Publisher() => new(new("script1", 0, "Aion.bin", "device1", true));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static async Task ColdStartAndFailureAsync()
    {
        using var publisher = Publisher();
        var failures = true;
        var reads = 0;
        using var source = new VmmGameProcessSnapshotSource(publisher, lifecycle =>
        {
            lifecycle("device-account-connection");
            Interlocked.Increment(ref reads);
            if (Volatile.Read(ref failures)) throw new IOException("partial process enumeration");
            return Snapshot(Game);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var first = source.ReadAsync(timeout.Token);
        await Task.Delay(300);
        Require(!first.IsCompleted && reads >= 1, "cold failed inventory must wait, never publish empty");
        Volatile.Write(ref failures, false);
        var official = await first;
        Volatile.Write(ref failures, true);
        var held = await source.ReadAsync(timeout.Token);
        Require(ReferenceEquals(official, held) && held.Processes.Single() == Game, "failed reads keep the exact official publication");
    }

    public static Task PublicationAndLifecycleAsync()
    {
        using var publisher = Publisher();
        GameProcessSnapshot? Capture(GameProcessSnapshot value) => publisher.Capture(l => { l(value.Session); return value; }, default);
        var first = Capture(Snapshot(Game));
        var partial = publisher.Capture(l => { l("device-account-connection");
            return VmmGameProcessSnapshotSource.ValidateCapture("device-account-connection", [Game], []); }, default);
        Require(ReferenceEquals(first, partial), "mixed inventory captures cannot prove exit");
        var malformed = Capture(Snapshot(Game, Game));
        Require(ReferenceEquals(first, malformed), "duplicate process identity cannot replace publication");
        var empty = Capture(Snapshot());
        Require(empty!.Processes.Count == 0, "valid empty inventory publishes immediately");
        var newGame = Game with { InstanceKey = "reused-pid-new-session" };
        Require(Capture(Snapshot(newGame))!.Processes.Single() == newGame, "same PID new process replaces old identity");
        var replaced = publisher.Capture(l => { l("new-connection"); throw new IOException("connection warming"); }, default);
        Require(replaced == null, "connection replacement invalidates old official data before failure");
        using var second = Publisher();
        Require(second.ReadPublished(default) == null, "different readers cannot share a publication");
        var values = new[] { Game };
        var frozen = Capture(new("new-connection", values));
        values[0] = newGame;
        Require(frozen!.Processes[0] == Game, "published process values are immutable");
        Require(AionVmmSnapshotChannels.GameProcesses.ReadPolicy == DmaSnapshotReadPolicy.Stable, "process channel is Stable");
        return Task.CompletedTask;
    }

    public static async Task LateCaptureIsRejectedAsync()
    {
        using var publisher = Publisher();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var capture = Task.Run(() => publisher.Capture(l =>
        {
            l("device-account-connection"); entered.Set();
            release.Wait(TimeSpan.FromSeconds(3)); return Snapshot(Game);
        }, default));
        Require(entered.Wait(TimeSpan.FromSeconds(2)), "capture entered");
        publisher.Dispose(); release.Set();
        try { await capture; throw new Exception("late publication accepted after disposal"); }
        catch (ObjectDisposedException) { }
    }

    public static async Task MonitorProcessReplacementAsync()
    {
        foreach (var replacement in new[] { Snapshot(), Snapshot(Game with { InstanceKey = "new-instance" }), new("new-connection", new[] { Game }) })
        {
            var source = new Source(Snapshot(Game));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var monitor = new GameDisconnectMonitor(TimeSpan.FromMilliseconds(10)).WaitForExitAsync(source, Game.ProcessId, timeout.Token);
            await Task.Delay(60);
            Require(!monitor.IsCompleted, "present game never triggers recovery even if player reads or UU fail independently");
            source.Value = replacement;
            await monitor;
        }
        await new GameDisconnectMonitor().WaitForExitAsync(new Source(Snapshot()), Game.ProcessId, default);
        using var stopped = new CancellationTokenSource();
        var pending = new GameDisconnectMonitor().WaitForExitAsync(new Source(Snapshot(Game)), Game.ProcessId, stopped.Token);
        stopped.Cancel();
        try { await pending; throw new Exception("stop should cancel monitoring"); }
        catch (OperationCanceledException) { }
    }

    public static async Task NativeWaitCancellationAsync()
    {
        using var publisher = Publisher();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var source = new VmmGameProcessSnapshotSource(publisher, l =>
        {
            l("device-account-connection"); entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); return Snapshot();
        });
        using var cancellation = new CancellationTokenSource();
        var reading = source.ReadAsync(cancellation.Token);
        Require(entered.Wait(TimeSpan.FromSeconds(2)), "native process read entered");
        cancellation.Cancel();
        try { await reading.WaitAsync(TimeSpan.FromSeconds(1)); throw new Exception("native wait ignored stop"); }
        catch (OperationCanceledException) { }
        source.Dispose(); release.Set();
    }

    public static Task BusinessBoundaryAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Roadhog", "Application"))) directory = directory.Parent;
        Require(directory != null, "repository root located for architecture test");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "Roadhog", "Application", "GameSessions", "GameDisconnectMonitor.cs"));
        foreach (var forbidden in new[] { "Infrastructure.", "LoginNativeReader", "OperationResult", "catch (", "CapturedAt", "ReadPublished", "UsedFallback", "RequireFresh" })
            Require(!source.Contains(forbidden, StringComparison.Ordinal), "disconnect business monitor bypasses trusted data boundary: " + forbidden);
        return Task.CompletedTask;
    }

    private sealed class Source(GameProcessSnapshot value) : IGameProcessSnapshotSource
    {
        public GameProcessSnapshot Value = value;
        public Task<GameProcessSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(Volatile.Read(ref Value));
    }
}
