using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class DisconnectWatchRecoveryTests
{
    public static async Task FaultsNeverConfirmExitAndStopCancelsRetryAsync()
    {
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var watch = RoadhogWorkerProcessBackend.WaitForDisconnectWithRecoveryAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref calls);
            if (attempt <= 2) throw new IOException("simulated monitor failure");
            if (attempt == 3) throw new OperationCanceledException("local observation timeout");
            observing.TrySetResult();
            await exited.Task.WaitAsync(token);
        }, logger, "watch-test", stop.Token, TimeSpan.FromMilliseconds(5));
        await observing.Task.WaitAsync(stop.Token);
        if (watch.IsCompleted || calls != 4 || logger.Entries.Count(e => e.EventName == "game_disconnect.monitor_retry") != 3)
            throw new InvalidOperationException("Monitor faults must retry without reporting game exit.");
        exited.TrySetResult();
        await watch.WaitAsync(stop.Token);

        using var manualStop = new CancellationTokenSource();
        var retrying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        calls = 0;
        watch = RoadhogWorkerProcessBackend.WaitForDisconnectWithRecoveryAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            retrying.TrySetResult();
            throw new IOException("persistent monitor failure");
        }, logger, "watch-stop", manualStop.Token, TimeSpan.FromSeconds(30));
        await retrying.Task.WaitAsync(stop.Token);
        manualStop.Cancel();
        try { await watch.WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException("Stop must cancel the monitor."); }
        catch (OperationCanceledException) when (manualStop.IsCancellationRequested) { }
        if (calls != 1) throw new InvalidOperationException("Manual Stop cannot restart a failed monitor.");
    }
}
