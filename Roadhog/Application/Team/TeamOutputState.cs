namespace Roadhog.Application.Team;

public sealed class TeamOutputState
{
    public DateTimeOffset LastSnapshotWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastInputWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetRejectLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;
}

public sealed record TeamOutputTickResult(
    bool ShouldSkipNormalWork,
    TimeSpan Delay)
{
    public static TeamOutputTickResult Continue(TimeSpan delay)
    {
        return new TeamOutputTickResult(false, delay);
    }

    public static TeamOutputTickResult SkipNormalWork(TimeSpan delay)
    {
        return new TeamOutputTickResult(true, delay);
    }
}
