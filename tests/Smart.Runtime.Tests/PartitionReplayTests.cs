using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Xunit;

namespace Smart.Runtime.Tests;

public sealed class PartitionReplayTests
{
    [Fact]
    public async Task TypedPartitionsReplayOnOneClockWithoutLeakingValuesAcrossKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-partition-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = DateTimeOffset.UtcNow;
            var identity = new SessionIdentity("d", "c", "a", "run", 1, "p", "m");
            await using (var logs = new JsonLineEventSink(root))
            {
                logs.Write(new(start, "session.configuration", "a", JsonSerializer.Serialize(new AccountProfile("a")), "run"));
                var recorder = new SnapshotTraceRecorder(logs);
                recorder.Published(identity, "values", "left", new PublishedSnapshot<int>(10, new(1, 1), start));
                recorder.Published(identity, "values", "right", new PublishedSnapshot<int>(30, new(1, 1), start + TimeSpan.FromSeconds(2)));
                recorder.Published(identity, "values", "left", new PublishedSnapshot<int>(20, new(1, 2), start + TimeSpan.FromSeconds(3)));
            }
            var timeline = await SnapshotTraceTimeline.LoadAsync(Directory.GetFiles(root), "run");
            var time = new FakeTimeProvider(); var clock = new ReplayClock(time);
            Assert.Throws<InvalidDataException>(() => timeline.CreatePartitionedReader<int, string>(1, "values", clock, maximumPartitions: 1));
            var catalog = new SnapshotCatalog();
            var channel = catalog.Register<int, string, int>("values", timeline.CreatePartitionedReader<int, string>(1, "values", clock),
                new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            catalog.Seal();
            await using var session = new SnapshotProvider(catalog, time).OpenSession(identity);
            var left = await session.Reader.ReadAsync(channel, "left");
            Assert.Equal(10, left.Value);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var right = session.Reader.ReadAsync(channel, "right", deadline.Token).AsTask();
            Assert.False(right.IsCompleted);
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(30, (await right.WaitAsync(deadline.Token)).Value);
            Assert.Same(left, await session.Reader.ReadAsync(channel, "left"));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(20, (await session.Reader.ReadAsync(channel, "left")).Value);
            Assert.Equal(30, (await session.Reader.ReadAsync(channel, "right")).Value);
            using var missingCancellation = new CancellationTokenSource();
            var missing = session.Reader.ReadAsync(channel, "unrecorded", missingCancellation.Token).AsTask();
            Assert.False(missing.IsCompleted);
            missingCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => missing);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
