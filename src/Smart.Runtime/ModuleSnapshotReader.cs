using Smart.Contracts;
namespace Smart.Runtime;

// A newly added module cannot silently consume an undeclared channel.
public sealed class ModuleSnapshotReader(ISnapshotReader inner, string moduleId, IEnumerable<string> channels) : ISnapshotReader
{
    public ModuleSnapshotReader(ISnapshotReader inner, IAccountModule module) : this(inner, module.Id, module.RequiredChannels) { }
    private readonly HashSet<string> _allowed = channels.ToHashSet(StringComparer.Ordinal);
    private void Check(string channel)
    {
        if (!_allowed.Contains(channel)) throw new InvalidOperationException($"Module {moduleId} did not declare channel {channel}.");
    }
    public ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition, CancellationToken cancellationToken = default) where TP : notnull
    { Check(channel.Id); return inner.ReadAsync(channel, partition, cancellationToken); }
    public ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition, SnapshotStamp after, CancellationToken cancellationToken = default) where TP : notnull
    { Check(channel.Id); return inner.WaitForChangeAsync(channel, partition, after, cancellationToken); }
}

internal sealed class BudgetedSnapshotReader(ISnapshotReader inner, WorkBudget budget) : ISnapshotReader
{
    public ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition,
        CancellationToken cancellationToken = default) where TP : notnull => Read(() => inner.ReadAsync(channel, partition, cancellationToken));
    public ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition,
        SnapshotStamp after, CancellationToken cancellationToken = default) where TP : notnull => Read(() => inner.WaitForChangeAsync(channel, partition, after, cancellationToken));
    private async ValueTask<T> Read<T>(Func<ValueTask<T>> read)
    {
        using var suspension = budget.SuspendForRead();
        return await read().ConfigureAwait(false);
    }
}
