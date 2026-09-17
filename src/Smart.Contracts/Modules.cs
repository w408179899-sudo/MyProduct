namespace Smart.Contracts;

public sealed record TickContext(
    string AccountId, ISnapshotReader Snapshots, TimeSpan Elapsed, ActionFeedback? LastAction, WorkBudget Budget);
public sealed record ModuleResult(TimeSpan NextRunIn, ActionPlan? Action = null, bool ReleaseInput = false);
public sealed record ModuleContext(string AccountId, ISnapshotReader Snapshots, Guid RunId);
public sealed record ModuleActivationContext(string AccountId, string ModuleId, Guid RunId, ISnapshotReader Snapshots);
public enum ModuleStopReason { Stopped, Faulted }

public interface IAccountModule
{
    string Id { get; }
    int Priority { get; }
    IReadOnlyList<string> Dependencies => Array.Empty<string>();
    IReadOnlyList<string> RequiredChannels => Array.Empty<string>();
    ValueTask InitializeAsync(ModuleContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask StopAsync(ModuleStopReason reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken cancellationToken);
}

// Work count is deterministic; elapsed time also bounds cooperative algorithms.
public sealed class WorkBudget
{
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly TimeSpan _duration;
    private int _remaining;
    private readonly object _sync = new();
    private int _pauses;
    private long _pauseStarted;
    private TimeSpan _paused;
    public WorkBudget(int operations, TimeSpan duration, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operations);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _remaining = operations; _duration = duration; _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
    }
    public bool TrySpend(int operations = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operations);
        lock (_sync)
        {
            if (_remaining < operations || ElapsedCore() >= _duration) return false;
            _remaining -= operations;
            return true;
        }
    }
    public bool IsExpired { get { lock (_sync) return _remaining <= 0 || ElapsedCore() >= _duration; } }
    public TimeSpan ComputeElapsed
    {
        get { lock (_sync) return ElapsedCore(); }
    }
    private TimeSpan ElapsedCore() => _time.GetElapsedTime(_started) - _paused - (_pauses > 0 ? _time.GetElapsedTime(_pauseStarted) : TimeSpan.Zero);
    public IDisposable SuspendForRead()
    {
        lock (_sync) { if (_pauses++ == 0) _pauseStarted = _time.GetTimestamp(); }
        return new Suspension(this);
    }
    private sealed class Suspension(WorkBudget owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) { if (--owner._pauses == 0) owner._paused += owner._time.GetElapsedTime(owner._pauseStarted); }
        }
    }
}

public sealed record DiagnosticEvent(DateTimeOffset At, string Name, string Scope, string Detail,
    string? RunId = null, string? ModuleId = null, string? ActionId = null, SnapshotStamp? Snapshot = null, long Sequence = 0);
public interface IEventSink { void Write(DiagnosticEvent entry); }
public sealed class NullEventSink : IEventSink
{
    public static NullEventSink Instance { get; } = new();
    public void Write(DiagnosticEvent entry) { }
}
