using Roadhog.Core.Api;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Vmm;

internal sealed class DmaStableSnapshotStore
{
    private sealed record Entry(object Value, Type ValueType, DateTimeOffset CapturedAt);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly DmaSnapshotChannelRegistry _channels;

    public DmaStableSnapshotStore(DmaSnapshotChannelRegistry channels)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        if (!_channels.IsSealed)
        {
            throw new InvalidOperationException(
                "DMA snapshot channel registry must be fully registered and sealed before use.");
        }
    }

    public StableSnapshotResolution<T> Resolve<T>(
        string sessionKey,
        DmaSnapshotChannel<T> channel,
        GameApiReadContext context,
        OperationResult<T> observed,
        DateTimeOffset now,
        string? partitionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(observed);

        var dataKey = _channels.ResolveDataKey(channel, partitionKey);
        var key = BuildKey(sessionKey, dataKey);
        if (observed.Success && observed.Value is not null)
        {
            lock (_syncRoot)
            {
                _entries[key] = new Entry(observed.Value, typeof(T), now);
            }

            return StableSnapshotResolution<T>.Fresh(observed);
        }

        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(key, out var entry) ||
                entry.ValueType != typeof(T))
            {
                return StableSnapshotResolution<T>.Failed(observed, StableSnapshotFailureReason.Missing);
            }

            var age = GetNonNegativeAge(entry.CapturedAt, now);

            return StableSnapshotResolution<T>.Fallback(
                OperationResult<T>.Ok((T)entry.Value),
                age,
                observed.Error);
        }
    }

    public bool TryUpdate<T>(
        string sessionKey,
        DmaSnapshotChannel<T> channel,
        DateTimeOffset now,
        Func<T, T> update,
        out T? value,
        out TimeSpan age,
        string? partitionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(update);

        var dataKey = _channels.ResolveDataKey(channel, partitionKey);
        if (channel.MergePolicy != DmaSnapshotMergePolicy.FieldAware)
        {
            throw new InvalidOperationException(
                "DMA snapshot channel does not allow field-aware updates: " + channel.Name);
        }

        var key = BuildKey(sessionKey, dataKey);
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out var entry) &&
                entry.ValueType == typeof(T))
            {
                age = GetNonNegativeAge(entry.CapturedAt, now);
                var updated = update((T)entry.Value);
                if (updated is null)
                {
                    value = default;
                    return false;
                }

                value = updated;
                _entries[key] = new Entry(updated, typeof(T), now);
                return true;
            }
        }

        value = default;
        age = TimeSpan.Zero;
        return false;
    }

    public void ClearSession(string sessionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        var prefix = sessionKey + "\u001f";
        lock (_syncRoot)
        {
            foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                _entries.Remove(key);
            }
        }
    }

    public void ClearConnection(string connectionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);
        var prefix = connectionKey + "\u001e";
        lock (_syncRoot)
        {
            foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                _entries.Remove(key);
            }
        }
    }

    private static string BuildKey(string sessionKey, string dataKey)
    {
        return sessionKey + "\u001f" + dataKey;
    }

    private static TimeSpan GetNonNegativeAge(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        return now > capturedAt ? now - capturedAt : TimeSpan.Zero;
    }
}

internal enum StableSnapshotFailureReason
{
    None = 0,
    Missing = 1
}

internal sealed record StableSnapshotResolution<T>(
    OperationResult<T> Result,
    bool UsedFallback,
    TimeSpan FallbackAge,
    string? ObservedError,
    StableSnapshotFailureReason FailureReason)
{
    public static StableSnapshotResolution<T> Fresh(OperationResult<T> result)
    {
        return new StableSnapshotResolution<T>(
            result,
            false,
            TimeSpan.Zero,
            null,
            StableSnapshotFailureReason.None);
    }

    public static StableSnapshotResolution<T> Fallback(
        OperationResult<T> result,
        TimeSpan age,
        string? observedError)
    {
        return new StableSnapshotResolution<T>(
            result,
            true,
            age,
            observedError,
            StableSnapshotFailureReason.None);
    }

    public static StableSnapshotResolution<T> Failed(
        OperationResult<T> result,
        StableSnapshotFailureReason reason,
        TimeSpan age = default)
    {
        return new StableSnapshotResolution<T>(result, false, age, result.Error, reason);
    }
}
