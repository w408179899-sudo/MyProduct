using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Smart.Runtime;

internal static class SoakRunner
{
    private sealed class Counters
    {
        public long Sessions, Decisions, Sends, SendFailures, Releases;
        public int HeldInputs;
    }
    private sealed class Transport : IMemoryTransport
    {
        public string DeviceId => "synthetic-shared-device";
        public string ConnectionId => "synthetic-connection";
        private int _calls;
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> reads)
        {
            var call = ++_calls;
            Thread.Sleep(call % 53 == 0 ? 25 : 2);
            if (call % 37 == 0) throw new IOException("Injected external read failure.");
            return reads.Select(read => new MemoryBlock(read.Address,
                BitConverter.GetBytes(call).ToImmutableArray(), call % 11 != 0)).ToImmutableArray();
        }
        public void Dispose() { }
    }
    private sealed class Input(string id, Counters counters) : IInputDevice
    {
        private int _held;
        public string DeviceId => "synthetic-input:" + id;
        public ValueTask SendAsync(InputCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref counters.Sends) % 997 == 0)
            { Interlocked.Increment(ref counters.SendFailures); throw new IOException("Injected input failure."); }
            if (command.Operation == InputOperation.KeyDown && Interlocked.Exchange(ref _held, 1) == 0)
                Interlocked.Increment(ref counters.HeldInputs);
            return ValueTask.CompletedTask;
        }
        public ValueTask ReleaseAllAsync(CancellationToken token)
        {
            if (Interlocked.Exchange(ref _held, 0) != 0) Interlocked.Decrement(ref counters.HeldInputs);
            Interlocked.Increment(ref counters.Releases); return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ReleaseAllAsync(default);
    }
    private sealed class Module(string id, int priority, SnapshotChannel<int, NoPartition> channel, Counters counters) : IAccountModule
    {
        private long _turn;
        public string Id => id; public int Priority => priority;
        public IReadOnlyList<string> RequiredChannels => [channel.Id];
        public async ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token)
        {
            await context.Snapshots.ReadAsync(channel, default, token);
            var delay = TimeSpan.FromMilliseconds(25 + priority * 3);
            if (!context.Budget.TrySpend(64)) return new(delay);
            Interlocked.Increment(ref counters.Decisions);
            return new(delay, new ActionPlan(Id + ":" + ++_turn, InputResource.Keyboard,
                [InputCommand.PressDown(4), InputCommand.Wait(TimeSpan.FromMilliseconds(2))], TimeSpan.FromMilliseconds(250)));
        }
    }
    private sealed class Factory(DmaDispatcher dispatcher, Counters counters, IEventSink events) : IRuntimeSessionFactory
    {
        private readonly InputLeaseRegistry _leases = new();
        public ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var run = Guid.NewGuid(); var composition = new SessionComposition(profile);
            var source = new DmaChannelReader<int, NoPartition>(dispatcher, (_, _) => [new(0x1000, 4)], blocks =>
                blocks[0].Complete ? RawRead<int>.Complete(BitConverter.ToInt32(blocks[0].Bytes.AsSpan())) : RawRead<int>.Failed("Injected short read."));
            var channel = composition.Channels.Register<int, NoPartition, int>("counter", source, new ReplaceMerger<int>(_ => true),
                SnapshotMergePolicy.Replace, TimeSpan.FromMilliseconds(10));
            for (var n = 0; n < 4; n++) { var index = n; composition.AddModule("module-" + index, [channel.Id], _ => new Module("module-" + index, index, channel, counters)); }
            composition.Seal();
            var recorder = new SnapshotTraceRecorder(events); recorder.RecordConfiguration(profile, run.ToString("N"));
            var snapshots = new SnapshotProvider(composition.Channels, events: events, observer: recorder).OpenSession(
                new(dispatcher.DeviceId, dispatcher.ConnectionId, profile.Id, run.ToString("N"), 1, "synthetic-process", "synthetic-module"));
            var executor = new ActionExecutor(new Input(profile.Id, counters), _leases);
            var worker = new AccountWorker(profile.Id, snapshots.Reader, executor, composition.Activate(snapshots.Reader, run),
                events: events, registeredChannels: [channel.Id], runId: run);
            var started = Stopwatch.GetTimestamp(); Interlocked.Increment(ref counters.Sessions);
            return ValueTask.FromResult<IRuntimeSession>(new RuntimeSession(worker, snapshots,
                _ => ValueTask.FromResult(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3))));
        }
    }
    public static async Task RunAsync(string[] arguments)
    {
        var seconds = arguments.Length > 1 ? int.Parse(arguments[1]) : 60;
        var count = arguments.Length > 2 ? int.Parse(arguments[2]) : 8;
        if (seconds is < 10 or > 86400 || count is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(arguments));
        var root = Path.GetFullPath(Path.Combine("artifacts", "soak-logs", Guid.NewGuid().ToString("N")));
        var counters = new Counters();
        await using var logs = new JsonLineEventSink(root, capacity: 4096, maximumFileBytes: 4 * 1024 * 1024, retainedFiles: 4);
        await using var dispatcher = new DmaDispatcher(new Transport(), 16);
        var factory = new Factory(dispatcher, counters, logs);
        var accounts = Enumerable.Range(0, count).Select(n => new ManagedAccount(new("soak-" + n, ProbeIntervalMs: 100, RetryDelayMs: 100), factory, logs)).ToArray();
        try
        {
            foreach (var account in accounts) account.Start();
            await Task.Delay(3000); // Warm JIT, logging and the first set of native/worker continuations.
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var cpu = process.TotalProcessorTime; var handles = process.HandleCount;
            var heap = GC.GetTotalMemory(true); var allocation = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                await Task.Delay(250);
                var faulted = accounts.FirstOrDefault(x => x.Status.State == SessionState.Faulted);
                if (faulted is not null) throw new InvalidOperationException(faulted.Profile.Id + ": " + faulted.Status.Error);
            }
            var stopping = Stopwatch.StartNew();
            await Task.WhenAll(accounts.Select(x => x.StopAsync(TimeSpan.FromSeconds(5))));
            await dispatcher.DisposeAsync();
            foreach (var account in accounts) await account.DisposeAsync();
            stopping.Stop(); await logs.DisposeAsync();
            var retainedHeap = GC.GetTotalMemory(true); process.Refresh();
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            var metrics = dispatcher.Metrics;
            var passed = !(counters.HeldInputs != 0 || counters.Sessions < count * 2 || counters.Decisions < count * seconds ||
                metrics.Failures == 0 || metrics.PeakQueued > 16 || metrics.Active != 0 || metrics.Queued != 0 || logs.DroppedEvents != 0 || logs.WriteError is not null ||
                retainedHeap - heap > 32 * 1024 * 1024 || process.HandleCount - handles > 64 || cpuMs > clock.Elapsed.TotalMilliseconds * 2);
            var report = JsonSerializer.Serialize(new { Passed = passed, Scenario = "shared_dma_fault_reconnect_input_trace", Accounts = count, ModulesPerAccount = 4,
                MeasurementSeconds = clock.Elapsed.TotalSeconds, WarmupSeconds = 3, CpuMilliseconds = cpuMs,
                CpuCoreEquivalents = cpuMs / clock.Elapsed.TotalMilliseconds, ManagedHeapGrowthBytes = retainedHeap - heap,
                AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocation, HandleGrowth = process.HandleCount - handles,
                StopMilliseconds = stopping.Elapsed.TotalMilliseconds, counters.Sessions, counters.Decisions, counters.Sends,
                counters.SendFailures, counters.Releases, counters.HeldInputs, Dma = metrics, logs.DroppedEvents, Logs = root,
                Note = "Synthetic transport and input only; physical DMA/KMBox and hours-long operation require separate acceptance." }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine("artifacts", "soak-attempt.json"), report);
            if (!passed) throw new InvalidOperationException("Soak acceptance failed: " + report);
            Console.WriteLine(report);
        }
        finally { foreach (var account in accounts) await account.DisposeAsync(); }
    }
}
