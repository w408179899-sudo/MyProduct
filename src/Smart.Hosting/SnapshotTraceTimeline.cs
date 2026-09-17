using System.Collections.Immutable;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
namespace Smart.Hosting;

public sealed record ReplayEvent(TimeSpan At, DiagnosticEvent Event);

// One recorded session timeline for configuration, every channel/partition, and action command/results.
// State playback samples this common clock; it does not claim to reproduce OS thread scheduling.
public sealed class SnapshotTraceTimeline
{
    private SnapshotTraceTimeline(string runId, AccountProfile profile, ImmutableArray<ReplayEvent> events)
    { RunId = runId; Profile = profile; Events = events; }
    public string RunId { get; }
    public AccountProfile Profile { get; }
    public ImmutableArray<ReplayEvent> Events { get; }
    public static async Task<SnapshotTraceTimeline> LoadAsync(IEnumerable<string> files, string runId,
        int maximumEvents = 200_000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEvents);
        var selected = files.ToArray();
        if (selected.Length > 256 || selected.Sum(x => new FileInfo(x).Length) > 256L * 1024 * 1024)
            throw new InvalidDataException("Trace input size budget exceeded.");
        var events = new List<DiagnosticEvent>();
        foreach (var file in selected)
        {
            using var reader = File.OpenText(file);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var entry = JsonSerializer.Deserialize<DiagnosticEvent>(line) ?? throw new InvalidDataException("Invalid trace event.");
                if (entry.RunId != runId) continue;
                if (events.Count >= maximumEvents) throw new InvalidDataException("Replay event budget exceeded.");
                events.Add(entry);
            }
        }
        var configurations = events.Where(x => x.Name == "session.configuration").ToArray();
        if (configurations.Length != 1) throw new InvalidDataException("Replay requires one recorded session configuration; the trace may be incomplete.");
        var profile = JsonSerializer.Deserialize<AccountProfile>(configurations[0].Detail) ?? throw new InvalidDataException("Missing session configuration.");
        if (events.Any(x => x.Scope != profile.Id)) throw new InvalidDataException("Trace mixes account identities.");
        if (events.All(x => x.Sequence > 0))
        {
            if (events.Select(x => x.Sequence).Distinct().Count() != events.Count) throw new InvalidDataException("Duplicate trace events.");
            events.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        }
        else events = events.OrderBy(x => x.At).ToList();
        var versions = new Dictionary<(long Generation, string Channel, string Partition), long>();
        var timeline = ImmutableArray.CreateBuilder<ReplayEvent>(events.Count);
        var elapsed = TimeSpan.Zero; var origin = configurations[0].At;
        foreach (var entry in events)
        {
            if (entry.Name == "snapshot.published")
            {
                using var json = JsonDocument.Parse(entry.Detail);
                var root = json.RootElement;
                var session = root.GetProperty("Session").Deserialize<SessionIdentity>()!;
                var stamp = root.GetProperty("Snapshot").GetProperty("Stamp").Deserialize<SnapshotStamp>();
                if (session.WorkerId != runId || session.AccountId != profile.Id || entry.Snapshot != stamp)
                    throw new InvalidDataException("Snapshot identity/stamp differs from its event.");
                var key = (stamp.Generation, root.GetProperty("Channel").GetString()!, root.GetProperty("Partition").GetRawText());
                versions.TryGetValue(key, out var previous);
                if (stamp.Version != previous + 1) throw new InvalidDataException("Trace has missing or duplicate publications.");
                versions[key] = stamp.Version;
            }
            var at = entry.At - origin;
            if (at > elapsed) elapsed = at;
            timeline.Add(new(elapsed, entry));
        }
        return new(runId, profile, timeline.MoveToImmutable());
    }
    public IReadOnlyList<ReplaySample<T>> Samples<T, TP>(long generation, string channel, TP partition) where TP : notnull
    {
        var result = new List<ReplaySample<T>>();
        foreach (var item in Events)
        {
            if (item.Event.Name != "snapshot.published" || item.Event.Snapshot?.Generation != generation) continue;
            using var json = JsonDocument.Parse(item.Event.Detail);
            if (json.RootElement.GetProperty("Channel").GetString() != channel) continue;
            var snapshot = JsonSerializer.Deserialize<SnapshotTrace<T, TP>>(item.Event.Detail)!;
            if (!EqualityComparer<TP>.Default.Equals(partition, snapshot.Partition)) continue;
            var at = result.Count > 0 && item.At <= result[^1].At ? result[^1].At + TimeSpan.FromTicks(1) : item.At;
            result.Add(new(at, snapshot.Snapshot.Value));
        }
        if (result.Count == 0) throw new InvalidDataException("No matching recorded publications.");
        return result;
    }
    public ReplayReader<T> CreateReader<T, TP>(long generation, string channel, TP partition, ReplayClock clock) where TP : notnull =>
        new(Samples<T, TP>(generation, channel, partition), clock);

    // Register this source against the original typed partition token. All partitions share one clock;
    // an unrecorded partition has no official value and follows the provider's normal cold-start path.
    public IRawChannelReader<T, TP> CreatePartitionedReader<T, TP>(long generation, string channel,
        ReplayClock clock, int maximumPartitions = 256) where TP : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPartitions);
        var groups = new Dictionary<TP, List<ReplaySample<T>>>();
        foreach (var item in Events)
        {
            if (item.Event.Name != "snapshot.published" || item.Event.Snapshot?.Generation != generation) continue;
            using var json = JsonDocument.Parse(item.Event.Detail);
            if (json.RootElement.GetProperty("Channel").GetString() != channel) continue;
            var snapshot = JsonSerializer.Deserialize<SnapshotTrace<T, TP>>(item.Event.Detail)!;
            if (snapshot.Partition is null) throw new InvalidDataException("A recorded partition cannot be null.");
            if (!groups.TryGetValue(snapshot.Partition, out var samples))
            {
                if (groups.Count >= maximumPartitions) throw new InvalidDataException("Replay partition budget exceeded.");
                groups.Add(snapshot.Partition, samples = []);
            }
            var at = samples.Count > 0 && item.At <= samples[^1].At ? samples[^1].At + TimeSpan.FromTicks(1) : item.At;
            samples.Add(new(at, snapshot.Snapshot.Value));
        }
        if (groups.Count == 0) throw new InvalidDataException("No matching recorded publications.");
        return new PartitionedReader<T, TP>(groups.ToDictionary(x => x.Key, x => new ReplayReader<T>(x.Value, clock)));
    }
    private sealed class PartitionedReader<T, TP>(IReadOnlyDictionary<TP, ReplayReader<T>> readers) : IRawChannelReader<T, TP> where TP : notnull
    {
        public ValueTask<RawRead<T>> CaptureAsync(CaptureContext context, TP partition, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (partition is null) throw new ArgumentNullException(nameof(partition));
            return readers.TryGetValue(partition, out var reader)
                ? reader.CaptureAsync(context, NoPartition.Value, token)
                : ValueTask.FromResult(RawRead<T>.Failed("No recorded publication for this partition."));
        }
    }
}
