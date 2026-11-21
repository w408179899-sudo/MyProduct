namespace SkillScheduling;

public enum SkillConfirmation
{
    Pending,
    Confirmed,
    TimedOut
}

/// <summary>Game evidence decides success. The shared window handles timing and deadline precedence.</summary>
public readonly record struct SkillConfirmationWindow(
    DateTimeOffset StartedAt, TimeSpan Duration, TimeProvider? Clock, long StartedTimestamp)
{
    public SkillConfirmationWindow(DateTimeOffset startedAt, TimeSpan duration) : this(startedAt, duration, null, 0) { }

    public DateTimeOffset Deadline => StartedAt + Duration;

    public TimeSpan Remaining(DateTimeOffset now) => Clock is { } clock
        ? Duration - clock.GetElapsedTime(StartedTimestamp)
        : Deadline - now;

    public SkillConfirmation Evaluate(bool confirmed, DateTimeOffset now)
    {
        if (confirmed) return SkillConfirmation.Confirmed;
        return Remaining(now) <= TimeSpan.Zero ? SkillConfirmation.TimedOut : SkillConfirmation.Pending;
    }
}
