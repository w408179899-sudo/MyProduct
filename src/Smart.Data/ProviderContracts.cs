using Smart.Contracts;
namespace Smart.Data;

// Provider-only contracts. Domain/Application projects must never reference this assembly.
public enum ReadCompleteness { Complete, Partial, Failed }
public enum SnapshotReadPolicy { Stable }
public enum SnapshotMergePolicy { Replace, FieldAware }
public readonly record struct Field<T>(bool IsValid, T Value)
{
    public static Field<T> Valid(T value) => new(true, value);
    public static Field<T> Missing => new(false, default!);
}
public sealed record CaptureDiagnostics(string CaptureId, string TraversalTermination = "",
    int InvalidFields = 0, string? Error = null);
public sealed record RawRead<T>(ReadCompleteness Completeness, T Value, CaptureDiagnostics Diagnostics, string? ObservationId = null)
{
    public static RawRead<T> Complete(T value) => new(ReadCompleteness.Complete, value, new(Guid.NewGuid().ToString("N")));
    public static RawRead<T> Partial(T value) => new(ReadCompleteness.Partial, value, new(Guid.NewGuid().ToString("N")));
    public static RawRead<T> Failed(string error) => new(ReadCompleteness.Failed, default!, new(Guid.NewGuid().ToString("N"), Error: error));
}
public readonly record struct MergeDecision<T>(bool Publish, T Value)
{
    public static MergeDecision<T> Accept(T value) => new(true, value);
    public static MergeDecision<T> Hold => new(false, default!);
}
public sealed record SessionIdentity(string DeviceId, string ConnectionId, string AccountId,
    string WorkerId, int ProcessId, string ProcessStartId, string ModuleId)
{
    internal void Validate()
    {
        foreach (var value in new[] { DeviceId, ConnectionId, AccountId, WorkerId, ProcessStartId, ModuleId })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ProcessId);
    }
}
public sealed record CaptureContext(SessionIdentity Session, long Generation);
public interface IRawChannelReader<T, in TPartition> where TPartition : notnull
{
    ValueTask<RawRead<T>> CaptureAsync(CaptureContext context, TPartition partition, CancellationToken cancellationToken);
}
public interface ISnapshotMerger<T, in TRaw>
{
    MergeDecision<T> Merge(ReadCompleteness completeness, TRaw observed, PublishedSnapshot<T>? previous);
}
public sealed class ReplaceMerger<T>(Func<T, bool> validate) : ISnapshotMerger<T, T>
{
    public MergeDecision<T> Merge(ReadCompleteness completeness, T observed, PublishedSnapshot<T>? previous) =>
        completeness == ReadCompleteness.Complete && validate(observed)
            ? MergeDecision<T>.Accept(observed) : MergeDecision<T>.Hold;
}
public sealed record ChannelMetadata(string Id, Type ValueType, Type PartitionType,
    SnapshotReadPolicy Policy, SnapshotMergePolicy MergePolicy, TimeSpan MinimumCaptureInterval);

// Optional infrastructure observer; receives only committed official snapshots, outside capture locks.
public interface ISnapshotObserver
{
    void Published<T, TP>(SessionIdentity session, string channel, TP partition, PublishedSnapshot<T> snapshot) where TP : notnull;
}
