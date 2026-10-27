namespace Roadhog.Core.Api;

/// <summary>Caller-owned scheduling budget; never substitutes a value for a publication.</summary>
public static class SnapshotReadBudget
{
    private static readonly AsyncLocal<TimeSpan?> Current = new();
    private static readonly AsyncLocal<CancellationToken> OperationCancellation = new();

    // Propagate an action's deadline below WaitAsync, so abandoned confirmations
    // cannot leave provider retries running after the action has ended.
    public static IDisposable EnterCancellation(CancellationToken token)
    {
        var previous = OperationCancellation.Value;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(previous, token);
        OperationCancellation.Value = linked.Token;
        return new Scope(() =>
        {
            // WaitAsync can resume before the parent token has invoked every
            // linked callback. Explicit cancellation prevents Dispose from
            // unregistering that propagation while a capture is still pending.
            try { linked.Cancel(); }
            finally { OperationCancellation.Value = previous; linked.Dispose(); }
        });
    }

    internal static bool OperationCancelled => OperationCancellation.Value.IsCancellationRequested;
    public static IDisposable Enter(TimeSpan limit)
    {
        var previous = Current.Value;
        Current.Value = limit;
        return new Scope(() => Current.Value = previous);
    }

    internal static CancellationTokenSource CreateCancellation(CancellationToken stop)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stop, OperationCancellation.Value);
        if (Current.Value is { } limit) source.CancelAfter(limit);
        return source;
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

public sealed class SnapshotReadBudgetExceededException(string? channel = null)
    : OperationCanceledException("Snapshot read scheduling budget expired." + (channel is null ? "" : " Channel: " + channel))
{
    public string? Channel { get; } = channel;
}
