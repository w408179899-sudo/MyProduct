using Roadhog.Core.Api;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Vmm;

internal sealed class DmaStableSnapshotStore
{
    private sealed record Entry(object Value, Type ValueType, DateTimeOffset CapturedAt);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _timeToLive;

    public DmaStableSnapshotStore(TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }

        _timeToLive = timeToLive;
    }

    public StableSnapshotResolution<T> Resolve<T>(
        string sessionKey,
        string dataKey,
        GameApiReadContext context,
        OperationResult<T> observed,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataKey);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(observed);

        var key = BuildKey(sessionKey, dataKey);
        if (observed.Success && observed.Value is not null)
        {
            lock (_syncRoot)
            {
                _entries[key] = new Entry(observed.Value, typeof(T), now);
            }

            return StableSnapshotResolution<T>.Fresh(observed);
        }

        if (context.RequireFresh)
        {
            return StableSnapshotResolution<T>.Failed(observed, StableSnapshotFailureReason.FreshRequired);
        }

        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(key, out var entry) ||
                entry.ValueType != typeof(T))
            {
                return StableSnapshotResolution<T>.Failed(observed, StableSnapshotFailureReason.Missing);
            }

            var age = now - entry.CapturedAt;
            if (age < TimeSpan.Zero || age > _timeToLive)
            {
                _entries.Remove(key);
                return StableSnapshotResolution<T>.Failed(observed, StableSnapshotFailureReason.Expired, age);
            }

            return StableSnapshotResolution<T>.Fallback(
                OperationResult<T>.Ok((T)entry.Value),
                age,
                observed.Error);
        }
    }

    public bool TryGetFresh<T>(
        string sessionKey,
        string dataKey,
        DateTimeOffset now,
        out T? value,
        out TimeSpan age)
    {
        var key = BuildKey(sessionKey, dataKey);
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out var entry) &&
                entry.ValueType == typeof(T))
            {
                age = now - entry.CapturedAt;
                if (age >= TimeSpan.Zero && age <= _timeToLive)
                {
                    value = (T)entry.Value;
                    return true;
                }

                _entries.Remove(key);
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
}

internal enum StableSnapshotFailureReason
{
    None = 0,
    Missing = 1,
    Expired = 2,
    FreshRequired = 3
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
