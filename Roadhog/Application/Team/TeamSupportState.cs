namespace Roadhog.Application.Team;

public sealed class TeamSupportState
{
    public DateTimeOffset LastSnapshotWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastCatalogWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastInputWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;
}

public sealed record TeamSupportTickResult(
    bool ShouldSkipNormalWork,
    TimeSpan Delay)
{
    public static TeamSupportTickResult Continue(TimeSpan delay)
    {
        return new TeamSupportTickResult(false, delay);
    }

    public static TeamSupportTickResult SkipNormalWork(TimeSpan delay)
    {
        return new TeamSupportTickResult(true, delay);
    }
}
