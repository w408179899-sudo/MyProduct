using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Smart.Runtime;

if (args.FirstOrDefault() == "--soak") { await SoakRunner.RunAsync(args); return; }

var results = new List<object>();
foreach (var interval in new[] { TimeSpan.FromHours(1), TimeSpan.Zero })
{
    var source = new CounterReader(); var catalog = new SnapshotCatalog();
    var token = catalog.Register<int, NoPartition, int>("counter", source, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, interval);
    catalog.Seal(); using var session = new SnapshotProvider(catalog).OpenSession(new("mock", "c", "a", "w", 1, "p", "m"));
    await session.Reader.ReadAsync(token, NoPartition.Value);
    var iterations = interval == TimeSpan.Zero ? 10000 : 100000;
    var samples = new double[iterations];
    var allocated = GC.GetTotalAllocatedBytes(true); var total = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        await session.Reader.ReadAsync(token, NoPartition.Value);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
    }
    total.Stop(); Array.Sort(samples);
    results.Add(new { Scenario = interval == TimeSpan.Zero ? "capture_merge_publish" : "cached_read", Iterations = iterations,
        total.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
        source.Captures, P95Microseconds = samples[(int)(iterations * .95)], P99Microseconds = samples[(int)(iterations * .99)] });
}
var merger = new CollectionMerger<int, Item, int>((value, old) => MergeDecision<Item>.Accept(new(value)));
var observed = Enumerable.Range(0, 2000).Select(x => new EntityObservation<int, int>(x, x, x % 17 != 0)).ToImmutableArray();
PublishedSnapshot<ImmutableDictionary<int, Item>>? previous = null;
var collectionWatch = Stopwatch.StartNew();
for (var i = 0; i < 1000; i++)
{
    var merged = merger.Merge(ReadCompleteness.Partial, observed, previous);
    previous = new(merged.Value, new(1, i + 1), DateTimeOffset.UtcNow);
}
results.Add(new { Scenario = "partial_collection_2000x1000", collectionWatch.Elapsed.TotalMilliseconds, Objects = previous!.Value.Count });
foreach (var count in new[] { 1, 4, 8 })
{
    var leases = new InputLeaseRegistry();
    var accounts = Enumerable.Range(0, count).Select(i => new ManagedAccount(new("bench-" + i), new MockSessionFactory(leases,
        configure: composition => { for (var n = 0; n < 4; n++) { var id = "module-" + n; composition.AddModule(id, [], _ => new WorkModule(id)); } }))).ToArray();
    using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
    foreach (var account in accounts) account.Start();
    await Task.Delay(3000);
    var ticks = accounts.Sum(x => x.Metrics?.Ticks ?? 0);
    await Task.WhenAll(accounts.Select(x => x.StopAsync(TimeSpan.FromSeconds(3))));
    process.Refresh();
    results.Add(new { Scenario = "bounded_work_modules", Accounts = count, ModulesPerAccount = 4, DurationMs = 3000,
        ProcessCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, Ticks = ticks });
    foreach (var account in accounts) await account.DisposeAsync();
}
Console.WriteLine(JsonSerializer.Serialize(new { Note = "Synthetic CPU/allocation/latency measurements; no hardware, business or real-time guarantee.", Results = results }, new JsonSerializerOptions { WriteIndented = true }));
sealed record Item(int Value);
sealed class CounterReader : IRawChannelReader<int, NoPartition>
{
    public int Captures;
    public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token) => ValueTask.FromResult(RawRead<int>.Complete(++Captures));
}
sealed class WorkModule(string id) : IAccountModule
{
    public string Id => id; public int Priority => 1;
    public ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token)
    {
        for (var i = 0; i < 1000 && context.Budget.TrySpend(); i++) token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ModuleResult(TimeSpan.FromMilliseconds(25)));
    }
}
