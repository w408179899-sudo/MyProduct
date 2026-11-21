namespace SkillScheduling;

/// <summary>Retains a game's release evidence and the original window while input changes owners.</summary>
public sealed class SkillAttempt<T>(T evidence, SkillConfirmationWindow confirmation, DateTimeOffset lastInputAt, long lastInputTimestamp)
    where T : class
{
    public T Evidence { get; set; } = evidence;
    public SkillConfirmationWindow Confirmation { get; } = confirmation;
    public DateTimeOffset LastInputAt { get; private set; } = lastInputAt;
    private long _lastInputTimestamp = lastInputTimestamp;

    public bool IsRetryDue(DateTimeOffset now, TimeSpan interval) => Confirmation.Clock is { } clock
        ? clock.GetElapsedTime(_lastInputTimestamp) >= interval
        : now >= LastInputAt + interval;

    public void RecordInput(DateTimeOffset now, TimeProvider? clock, long? timestamp)
    {
        LastInputAt = now;
        if (Confirmation.Clock is { } attemptClock)
            _lastInputTimestamp = ReferenceEquals(attemptClock, clock)
                ? timestamp ?? attemptClock.GetTimestamp()
                : attemptClock.GetTimestamp();
    }
}

/// <summary>Per-session input attempts. Game adapters decide when an attempt may resume or has completed.</summary>
public sealed class SkillAttempts<T> where T : class
{
    public SkillAttempt<T>? Active { get; private set; }
    private readonly List<SkillAttempt<T>> _suspended = [];
    public bool HasSuspended => _suspended.Count != 0;

    public void Start(SkillAttempt<T> attempt, bool suspendActive)
    {
        if (suspendActive && Active is { } active) _suspended.Add(active);
        Active = attempt;
    }

    public void ClearActive() => Active = null;

    public void Clear()
    {
        ClearActive();
        _suspended.Clear();
    }

    public bool TryResume()
    {
        if (Active is not null || _suspended.Count == 0) return false;
        Active = _suspended[^1];
        _suspended.RemoveAt(_suspended.Count - 1);
        return true;
    }

    /// <summary>Removes a suspended attempt when the game observes its own late completion evidence.</summary>
    public T? FinishSuspended(Func<T, bool> isConfirmed)
    {
        for (var index = _suspended.Count - 1; index >= 0; index--)
        {
            var evidence = _suspended[index].Evidence;
            if (!isConfirmed(evidence)) continue;
            _suspended.RemoveAt(index);
            return evidence;
        }
        return null;
    }
}
