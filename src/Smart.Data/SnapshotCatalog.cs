using Smart.Contracts;
namespace Smart.Data;

public sealed class SnapshotCatalog
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly Dictionary<string, IRegistration> _channels = new(StringComparer.Ordinal);
    private bool _sealed;
    public bool IsSealed => _sealed;
    public IReadOnlyList<ChannelMetadata> Channels => _channels.Values.Select(x => x.Metadata).ToArray();

    public SnapshotChannel<T, TPartition> Register<T, TPartition, TRaw>(
        string id, IRawChannelReader<TRaw, TPartition> reader, ISnapshotMerger<T, TRaw> merger,
        SnapshotMergePolicy mergePolicy, TimeSpan minimumCaptureInterval) where TPartition : notnull
    {
        if (_sealed) throw new InvalidOperationException("Catalog is sealed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(merger);
        if (minimumCaptureInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumCaptureInterval));
        if (_channels.ContainsKey(id)) throw new InvalidOperationException("Duplicate channel: " + id);
        var token = new SnapshotChannel<T, TPartition>(_id, id);
        _channels.Add(id, new Registration<T, TPartition, TRaw>(token, reader, merger,
            new(id, typeof(T), typeof(TPartition), SnapshotReadPolicy.Stable, mergePolicy, minimumCaptureInterval)));
        return token;
    }

    public void Seal()
    {
        _sealed = true;
    }

    internal Registration<T, TPartition> Resolve<T, TPartition>(SnapshotChannel<T, TPartition> token)
        where TPartition : notnull
    {
        if (!_sealed) throw new InvalidOperationException("Seal catalog before creating workers.");
        if (token.Catalog != _id || !_channels.TryGetValue(token.Id, out var registration) ||
            registration is not Registration<T, TPartition> typed || !ReferenceEquals(typed.Token, token))
            throw new InvalidOperationException("Unregistered or foreign channel token.");
        return typed;
    }

    private interface IRegistration { ChannelMetadata Metadata { get; } }
    internal abstract class Registration<T, TPartition>(
        SnapshotChannel<T, TPartition> token, ChannelMetadata metadata) : IRegistration where TPartition : notnull
    {
        public SnapshotChannel<T, TPartition> Token { get; } = token;
        public ChannelMetadata Metadata { get; } = metadata;
        public abstract ValueTask<CaptureResult<T>> CaptureAsync(CaptureContext context,
            TPartition partition, PublishedSnapshot<T>? previous, CancellationToken cancellationToken);
    }
    internal sealed record CaptureResult<T>(MergeDecision<T> Decision, CaptureDiagnostics Diagnostics, ReadCompleteness Completeness, string? ObservationId = null);
    private sealed class Registration<T, TPartition, TRaw>(
        SnapshotChannel<T, TPartition> token, IRawChannelReader<TRaw, TPartition> reader,
        ISnapshotMerger<T, TRaw> merger, ChannelMetadata metadata) : Registration<T, TPartition>(token, metadata)
        where TPartition : notnull
    {
        public override async ValueTask<CaptureResult<T>> CaptureAsync(CaptureContext context, TPartition partition,
            PublishedSnapshot<T>? previous, CancellationToken cancellationToken)
        {
            var raw = await reader.CaptureAsync(context, partition, cancellationToken).ConfigureAwait(false);
            var decision = raw.Completeness == ReadCompleteness.Failed ? MergeDecision<T>.Hold :
                merger.Merge(raw.Completeness, raw.Value, previous);
            return new(decision, raw.Diagnostics, raw.Completeness, raw.ObservationId);
        }
    }
}
