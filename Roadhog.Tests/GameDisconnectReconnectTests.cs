using Roadhog.Application.GameSessions;
using Roadhog.Infrastructure.GameSessions;

internal static class GameDisconnectReconnectTests
{
    private static readonly GameProcessIdentity Game = new(9044, "9044:original-process");

    public static async Task ReconnectThenExitAsync()
    {
        // Exercise both an existing connection and another reconnect at the real exit.
        foreach (var session in new[] { "reconnected", "reconnected-again" })
        foreach (var games in new GameProcessIdentity[][]
                 { [], [Game with { InstanceKey = "9044:replacement-process" }], [Game with { ProcessId = 9045 }] })
        {
            await using var test = new ReconnectScenario();
            await test.Warming.Task.WaitAsync(test.Timeout.Token);
            test.AllowRecovery = true;
            var nextRead = test.StillWatching.Task.WaitAsync(test.Timeout.Token);
            if (await Task.WhenAny(test.Monitor, nextRead) == test.Monitor)
            {
                await test.Monitor;
                throw new InvalidOperationException("A new reading connection must not disconnect the same game process.");
            }
            await nextRead; // The monitor processed the new session and requested another snapshot.
            test.Exit = new(session, games);
            await test.Monitor.WaitAsync(test.Timeout.Token);
        }
    }

    public static async Task StopWhileReconnectingAsync()
    {
        await using var test = new ReconnectScenario();
        await test.Warming.Task.WaitAsync(test.Timeout.Token);
        test.Timeout.Cancel();
        try
        {
            await test.Monitor.WaitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Manual stop while reconnecting must cancel, not report game exit.");
        }
        catch (OperationCanceledException) when (test.Monitor.IsCanceled) { }
    }

    private sealed class ReconnectScenario : IAsyncDisposable
    {
        private readonly GameProcessSnapshotPublisher _publisher = new(new("script4", 0, "Aion.bin", "device4", true));
        private readonly VmmGameProcessSnapshotSource _source;
        private int _captures;
        private int _recoveredCaptures;
        public volatile bool AllowRecovery;
        public volatile GameProcessSnapshot? Exit;
        public readonly TaskCompletionSource Warming = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource StillWatching = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Timeout = new(TimeSpan.FromSeconds(5));
        public Task Monitor { get; }

        public ReconnectScenario()
        {
            _source = new(_publisher, lifecycle =>
            {
                if (Interlocked.Increment(ref _captures) == 1)
                {
                    lifecycle("original-connection");
                    return new("original-connection", [Game]);
                }
                lifecycle("reconnected");
                if (!AllowRecovery)
                {
                    Warming.TrySetResult();
                    throw new IOException("New reading connection is warming up.");
                }
                if (Interlocked.Increment(ref _recoveredCaptures) >= 2) StillWatching.TrySetResult();
                var snapshot = Exit ?? new("reconnected", [Game]);
                lifecycle(snapshot.Session);
                return snapshot;
            });
            Monitor = new GameDisconnectMonitor(TimeSpan.FromMilliseconds(10))
                .WaitForExitAsync(_source, Game.ProcessId, Timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            Timeout.Cancel();
            try { await Monitor; }
            catch (OperationCanceledException) when (Timeout.IsCancellationRequested) { }
            finally { _source.Dispose(); _publisher.Dispose(); Timeout.Dispose(); }
        }
    }
}
