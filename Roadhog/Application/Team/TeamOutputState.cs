namespace Roadhog.Application.Team;

public sealed class TeamOutputState
{
    public DateTimeOffset LastSnapshotWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastInputWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetRejectLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSelfDefenseWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSelfDefenseLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastFollowDecisionLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTacticalMarkLogAt { get; set; } = DateTimeOffset.MinValue;

    public TacticalMarkRangeRetryState TacticalMarkRangeRetry { get; } = new();

    public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;

    public int ConsecutiveLeaderUnavailableTicks { get; set; }

    public bool LeaderGroupActive { get; set; }

    public TeamLeaderRestSyncState LeaderRestSync { get; } = new();
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
