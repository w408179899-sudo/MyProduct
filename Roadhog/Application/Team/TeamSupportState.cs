namespace Roadhog.Application.Team;

public sealed class TeamSupportState
{
    private readonly Dictionary<string, DateTimeOffset> teamBuffPressedAt = new(StringComparer.Ordinal);

    public DateTimeOffset LastSnapshotWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastCatalogWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastInputWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetRejectLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSelfDefenseWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSelfDefenseLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastFollowDecisionLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;

    public bool LeaderGroupActive { get; set; }

    public bool ShouldPressTeamBuff(
        uint memberServerObjectId,
        uint abnormalStatusId,
        string key,
        DateTimeOffset now,
        TimeSpan retryInterval)
    {
        if (memberServerObjectId == 0 ||
            abnormalStatusId == 0 ||
            string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var stateKey = FormatTeamBuffStateKey(memberServerObjectId, abnormalStatusId, key);
        return !teamBuffPressedAt.TryGetValue(stateKey, out var lastPressedAt) ||
               now - lastPressedAt >= retryInterval;
    }

    public void RememberTeamBuffPress(
        uint memberServerObjectId,
        uint abnormalStatusId,
        string key,
        DateTimeOffset pressedAt)
    {
        if (memberServerObjectId == 0 ||
            abnormalStatusId == 0 ||
            string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        teamBuffPressedAt[FormatTeamBuffStateKey(memberServerObjectId, abnormalStatusId, key)] = pressedAt;
    }

    private static string FormatTeamBuffStateKey(uint memberServerObjectId, uint abnormalStatusId, string key)
    {
        return memberServerObjectId.ToString() + ":" + abnormalStatusId.ToString() + ":" + key.Trim();
    }
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
