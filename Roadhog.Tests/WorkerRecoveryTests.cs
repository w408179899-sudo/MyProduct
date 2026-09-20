using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;

internal static class WorkerRecoveryTests
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private sealed class Factory : IRoadhogSnapshotReaderFactory
    {
        public int Creates;
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            if (++Creates == 1) throw new InvalidOperationException("initialization failed");
            return new FakeGameApi().Create(config, logger, cancellationToken);
        }
    }

    private sealed class FaultLoop : IAccountWorkerLoop
    {
        public int Calls;
        public bool AlwaysFail;
        public readonly TaskCompletionSource<AccountWorkerContext> Recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AccountWorkerContext? FirstContext;
        public async Task RunAsync(AccountWorkerContext context)
        {
            FirstContext ??= context;
            Require(ReferenceEquals(context, FirstContext), "recovery must retain official snapshot session");
            var call = Interlocked.Increment(ref Calls);
            if (AlwaysFail || call <= 3) throw new InvalidOperationException("tick failed");
            if (call == 4) throw new OperationCanceledException("local timeout, not user Stop");
            if (call == 5) return;
            Recovered.TrySetResult(context);
            await Task.Delay(Timeout.Infinite, context.StopToken);
        }
    }

    public static async Task HostRecoveryAsync()
    {
        var logger = new InMemoryRoadhogLogger();
        var states = new AccountRuntimeManager(logger);
        var factory = new Factory();
        var loop = new FaultLoop();
        var host = new AccountWorkerHost(factory, logger, states, loop, new()
        {
            RecoveryDelay = TimeSpan.FromMilliseconds(5), MaxRecoveryDelay = TimeSpan.FromMilliseconds(20)
        });
        try
        {
            Require(host.Start(new() { AccountName = "recovery" }).Success, "start accepted");
            await loop.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(host.IsRunning && states.Snapshot().Single().Status == "running", "faults and unexpected return must not stop account");
            Require(factory.Creates == 2 && loop.Calls == 6, "retry initialization, repeated failures, foreign cancellation and unexpected completion");
            var failures = logger.Entries.Where(e => e.EventName == "worker.loop.exception").ToArray();
            Require(failures.Length == 6 && failures.All(e => (double)e.Fields["retryAfterMs"]! <= 20), "retry delay is capped without retry count cutoff");
            Require(!logger.Entries.Any(e => e.EventName is "account.failed" or "account.stopped"), "no terminal state before manual stop");
        }
        finally { await host.StopAsync(); }
        Require(!host.IsRunning && states.Snapshot().Single().Status == "idle", "manual Stop ends recovery session");

        loop = new FaultLoop { AlwaysFail = true };
        host = new AccountWorkerHost(new FakeGameApi(), logger, states, loop, new() { RecoveryDelay = TimeSpan.FromSeconds(30) });
        try
        {
            Require(host.Start(new() { AccountName = "backoff-stop" }).Success, "second host starts");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!logger.Entries.Any(e => e.EventName == "worker.loop.exception" && Equals(e.Fields["account"], "backoff-stop")))
                await Task.Delay(5, timeout.Token);
            Require((await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(1))).Success, "Stop interrupts long recovery delay immediately");
            Require(loop.Calls == 1, "Stop must never restart the worker");
        }
        finally { await host.StopAsync(); }
    }
}
