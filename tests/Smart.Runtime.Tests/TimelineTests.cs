using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class TimelineTests
{
    private sealed class DeferredSink : IDeferredEventSink
    {
        public Func<DiagnosticEvent>? Pending;
        public void Write(DiagnosticEvent entry) => throw new InvalidOperationException("Serialization must be deferred.");
        public void WriteDeferred(Func<DiagnosticEvent> create) => Pending = create;
    }
    private sealed class Value
    {
        public static int Reads;
        public int Number { get { Reads++; return 42; } }
    }
    [Fact] public void SnapshotSerializationDoesNotRunOnTheCaptureThreadWithDeferredSink()
    {
        Value.Reads = 0; var sink = new DeferredSink();
        new SnapshotTraceRecorder(sink).Published(new("d", "c", "a", "w", 1, "p", "m"), "value", NoPartition.Value,
            new PublishedSnapshot<Value>(new(), new(1, 1), DateTimeOffset.UtcNow));
        Assert.Equal(0, Value.Reads); Assert.NotNull(sink.Pending);
        Assert.Contains("42", sink.Pending!().Detail); Assert.Equal(1, Value.Reads);
    }
    [Fact] public async Task MultiChannelPlaybackPreservesSharedOriginAndRepeatedFrameDoesNotAdvanceVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-timeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = DateTimeOffset.UtcNow; var identity = new SessionIdentity("d", "c", "a", "run", 1, "p", "m");
            await using (var logs = new JsonLineEventSink(root))
            {
                logs.Write(new(start, "session.configuration", "a", JsonSerializer.Serialize(new AccountProfile("a")), "run"));
                var recorder = new SnapshotTraceRecorder(logs);
                recorder.Published(identity, "first", NoPartition.Value, new PublishedSnapshot<int>(10, new(1, 1), start));
                recorder.Published(identity, "later", NoPartition.Value, new PublishedSnapshot<int>(20, new(1, 1), start + TimeSpan.FromSeconds(2)));
                logs.Write(new(start + TimeSpan.FromSeconds(3), "action.completed", "a", JsonSerializer.Serialize(new ActionFeedback("test", ActionState.Succeeded)), "run", "module", "test"));
            }
            var timeline = await SnapshotTraceTimeline.LoadAsync(Directory.GetFiles(root), "run");
            Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(timeline.Samples<int, NoPartition>(1, "later", default)).At);
            Assert.Contains(timeline.Events, x => x.Event.ActionId == "test");
            var time = new FakeTimeProvider(); var clock = new ReplayClock(time); var catalog = new SnapshotCatalog();
            var first = catalog.Register<int, NoPartition, int>("first", timeline.CreateReader<int, NoPartition>(1, "first", default, clock), new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            var later = catalog.Register<int, NoPartition, int>("later", timeline.CreateReader<int, NoPartition>(1, "later", default, clock), new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            catalog.Seal(); using var session = new SnapshotProvider(catalog, time).OpenSession(identity);
            var published = await session.Reader.ReadAsync(first, default);
            Assert.Same(published, await session.Reader.ReadAsync(first, default));
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var pending = session.Reader.ReadAsync(later, default, stop.Token).AsTask();
            Assert.False(pending.IsCompleted); time.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(20, (await pending.WaitAsync(stop.Token)).Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
