using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Smart.Data")]
namespace Smart.Contracts;

public readonly record struct SnapshotStamp(long Generation, long Version);
public sealed record PublishedSnapshot<T>(T Value, SnapshotStamp Stamp, DateTimeOffset CapturedAt);
public readonly record struct NoPartition
{
    public static NoPartition Value => default;
}

// Only the catalog can construct tokens. Runtime registration identity is checked on every read.
public sealed class SnapshotChannel<T, TPartition> where TPartition : notnull
{
    internal SnapshotChannel(Guid catalog, string id) { Catalog = catalog; Id = id; }
    internal Guid Catalog { get; }
    public string Id { get; }
}

public interface ISnapshotReader
{
    ValueTask<PublishedSnapshot<T>> ReadAsync<T, TPartition>(
        SnapshotChannel<T, TPartition> channel, TPartition partition, CancellationToken cancellationToken = default)
        where TPartition : notnull;

    ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TPartition>(
        SnapshotChannel<T, TPartition> channel, TPartition partition, SnapshotStamp after,
        CancellationToken cancellationToken = default) where TPartition : notnull;
}
