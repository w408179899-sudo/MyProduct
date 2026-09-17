using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
namespace Smart.Hosting;

public sealed record SnapshotTrace<T, TP>(SessionIdentity Session, string Channel, TP Partition, PublishedSnapshot<T> Snapshot);
// Recording is opt-in because full values have a measurable serialization/storage cost.
public sealed class SnapshotTraceRecorder(IEventSink sink) : ISnapshotObserver
{
    public void Published<T, TP>(SessionIdentity session, string channel, TP partition, PublishedSnapshot<T> snapshot) where TP : notnull =>
        Write(() => new(snapshot.CapturedAt, "snapshot.published", session.AccountId,
            JsonSerializer.Serialize(new SnapshotTrace<T, TP>(session, channel, partition, snapshot)),
            session.WorkerId, Snapshot: snapshot.Stamp));
    public void RecordConfiguration(AccountProfile profile, string runId)
    {
        var at = DateTimeOffset.UtcNow;
        Write(() => new(at, "session.configuration", profile.Id, JsonSerializer.Serialize(profile), runId));
    }
    private void Write(Func<DiagnosticEvent> create)
    {
        if (sink is IDeferredEventSink deferred) deferred.WriteDeferred(create);
        else sink.Write(create());
    }
}

public static class SnapshotTraceReplay
{
    public static async Task<IReadOnlyList<ReplaySample<T>>> LoadAsync<T, TP>(IEnumerable<string> files,
        string workerId, long generation, string channel, TP partition, int maximumFrames = 100_000,
        CancellationToken cancellationToken = default) where TP : notnull
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        var frames = new SortedDictionary<long, PublishedSnapshot<T>>();
        foreach (var file in files)
        {
            using var reader = File.OpenText(file);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var entry = JsonSerializer.Deserialize<DiagnosticEvent>(line) ?? throw new InvalidDataException("Invalid trace event.");
                if (entry.Name != "snapshot.published" || entry.RunId != workerId || entry.Snapshot?.Generation != generation) continue;
                using var detail = JsonDocument.Parse(entry.Detail);
                if (detail.RootElement.GetProperty("Channel").GetString() != channel) continue;
                var record = JsonSerializer.Deserialize<SnapshotTrace<T, TP>>(entry.Detail)!;
                if (!EqualityComparer<TP>.Default.Equals(record.Partition, partition)) continue;
                if (!frames.TryAdd(record.Snapshot.Stamp.Version, record.Snapshot)) throw new InvalidDataException("Duplicate publication in trace.");
                if (frames.Count > maximumFrames) throw new InvalidDataException("Replay frame budget exceeded.");
            }
        }
        if (frames.Count == 0) throw new InvalidDataException("No matching official snapshots in trace.");
        var result = new List<ReplaySample<T>>(frames.Count);
        var origin = frames.First().Value.CapturedAt;
        var previous = TimeSpan.FromTicks(-1);
        long expectedVersion = frames.First().Key;
        foreach (var pair in frames)
        {
            if (pair.Key != expectedVersion++) throw new InvalidDataException("Trace has missing publications; exact replay is not possible.");
            var at = pair.Value.CapturedAt - origin;
            if (at <= previous) at = previous + TimeSpan.FromTicks(1);
            result.Add(new(at, pair.Value.Value)); previous = at;
        }
        return result;
    }
}
