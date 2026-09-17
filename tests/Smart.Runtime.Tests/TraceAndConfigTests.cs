using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class TraceAndConfigTests
{
    private sealed record Settings(int Value);
    private sealed class Source : IRawChannelReader<int, NoPartition>
    {
        public int Value;
        public bool Fail;
        public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token) =>
            ValueTask.FromResult(Fail ? RawRead<int>.Failed("injected") : RawRead<int>.Complete(Value));
    }
    [Fact] public async Task ActualPublicationsRecordAndReplayWithoutFabricatingFailedReads()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var time = new FakeTimeProvider(); var source = new Source { Value = 7 }; var catalog = new SnapshotCatalog();
            var channel = catalog.Register<int, NoPartition, int>("counter", source, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            catalog.Seal();
            await using (var logs = new JsonLineEventSink(root))
            {
                using var session = new SnapshotProvider(catalog, time, observer: new SnapshotTraceRecorder(logs))
                    .OpenSession(new("d", "c", "a", "worker", 1, "p", "m"));
                var first = await session.Reader.ReadAsync(channel, NoPartition.Value);
                source.Fail = true; time.Advance(TimeSpan.FromSeconds(1));
                Assert.Same(first, await session.Reader.ReadAsync(channel, NoPartition.Value));
                source.Fail = false; source.Value = 0; time.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(0, (await session.Reader.ReadAsync(channel, NoPartition.Value)).Value);
            }
            var samples = await SnapshotTraceReplay.LoadAsync<int, NoPartition>(Directory.GetFiles(root), "worker", 1, "counter", NoPartition.Value);
            Assert.Equal(new[] { 7, 0 }, samples.Select(x => x.Value));
            var clock = new FakeTimeProvider(); var replay = new ReplayReader<int>(samples, clock);
            var context = new CaptureContext(new("d", "c", "a", "w", 1, "p", "m"), 1);
            Assert.Equal(7, (await replay.CaptureAsync(context, NoPartition.Value, default)).Value);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(0, (await replay.CaptureAsync(context, NoPartition.Value, default)).Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task LogRetentionIncludesPreviousRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-retention-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            for (var i = 0; i < 5; i++)
            {
                var file = Path.Combine(root, "smart-old-" + i + ".jsonl");
                await File.WriteAllTextAsync(file, "old"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-1));
            }
            await using (var logs = new JsonLineEventSink(root, retainedFiles: 2)) logs.Write(new(DateTimeOffset.UtcNow, "test", "a", ""));
            Assert.Equal(2, Directory.GetFiles(root).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task MigrationTransformsOldSettingsWithoutOverwritingSourceAndRejectsFutureVersions()
    {
        var path = Path.Combine(Path.GetTempPath(), "smart-migration-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"SchemaVersion\":1,\"Settings\":{\"LegacyValue\":42}}");
            var store = new JsonConfigStore<Settings>(path, 2, _ => { }, new Dictionary<int, Func<JsonElement, JsonElement>>
            { [1] = json => JsonSerializer.SerializeToElement(new Settings(json.GetProperty("LegacyValue").GetInt32())) });
            Assert.Equal(42, (await store.LoadAsync()).Value);
            Assert.Contains("LegacyValue", await File.ReadAllTextAsync(path));
            await File.WriteAllTextAsync(path, "{\"SchemaVersion\":3,\"Settings\":{\"Value\":42}}");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { File.Delete(path); }
    }
}
