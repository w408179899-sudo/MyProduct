namespace Roadhog.Core.Api;

/// <summary>Caller-owned scheduling budget; never substitutes a value for a publication.</summary>
public static class SnapshotReadBudget
{
    private static readonly AsyncLocal<TimeSpan?> Current = new();
    public static IDisposable Enter(TimeSpan limit)
    {
        var previous = Current.Value;
        Current.Value = limit;
        return new Scope(() => Current.Value = previous);
    }

    internal static CancellationTokenSource CreateCancellation(CancellationToken stop)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stop);
        if (Current.Value is { } limit) source.CancelAfter(limit);
        return source;
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

public sealed class SnapshotReadBudgetExceededException() : OperationCanceledException("Snapshot read scheduling budget expired.");
