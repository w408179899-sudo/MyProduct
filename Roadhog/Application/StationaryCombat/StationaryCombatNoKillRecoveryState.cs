using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatNoKillRecoveryState
{
    public StationaryCombatNoKillRecoveryStep Step { get; private set; } =
        StationaryCombatNoKillRecoveryStep.Inactive;

    public DateTimeOffset WatchStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset RetryNotBefore { get; private set; } = DateTimeOffset.MinValue;

    public Vector3Snapshot? TownReturnStartPosition { get; private set; }

    public string RevivePathName { get; private set; } = string.Empty;

    public IReadOnlyList<Vector3Snapshot> RevivePathPoints { get; private set; } =
        Array.Empty<Vector3Snapshot>();

    public bool Active => Step != StationaryCombatNoKillRecoveryStep.Inactive;

    public void ObserveCombatActivity(DateTimeOffset? activityAt, DateTimeOffset now)
    {
        if (activityAt is { } reference)
        {
            if (WatchStartedAt == DateTimeOffset.MinValue || reference > WatchStartedAt)
            {
                WatchStartedAt = reference;
            }
            return;
        }

        if (WatchStartedAt == DateTimeOffset.MinValue)
        {
            WatchStartedAt = now;
        }
    }

    public bool IsDue(DateTimeOffset now, TimeSpan timeout)
    {
        return !Active &&
               now >= RetryNotBefore &&
               WatchStartedAt != DateTimeOffset.MinValue &&
               now - WatchStartedAt >= timeout;
    }

    public void StartTownReturn(
        Vector3Snapshot startPosition,
        string revivePathName,
        IReadOnlyList<Vector3Snapshot> revivePathPoints,
        DateTimeOffset now)
    {
        Step = StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle;
        StepStartedAt = now;
        RetryNotBefore = DateTimeOffset.MinValue;
        TownReturnStartPosition = startPosition;
        RevivePathName = revivePathName;
        RevivePathPoints = revivePathPoints.ToArray();
    }

    public void StartRevivePath(DateTimeOffset now)
    {
        Step = StationaryCombatNoKillRecoveryStep.FollowRevivePath;
        StepStartedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        ResetRecovery();
        WatchStartedAt = now;
    }

    public void Postpone(DateTimeOffset now, TimeSpan retryDelay)
    {
        ResetRecovery();
        RetryNotBefore = now + retryDelay;
    }

    public void ResetWatch(DateTimeOffset now)
    {
        ResetRecovery();
        WatchStartedAt = now;
        RetryNotBefore = DateTimeOffset.MinValue;
    }

    private void ResetRecovery()
    {
        Step = StationaryCombatNoKillRecoveryStep.Inactive;
        StepStartedAt = DateTimeOffset.MinValue;
        TownReturnStartPosition = null;
        RevivePathName = string.Empty;
        RevivePathPoints = Array.Empty<Vector3Snapshot>();
    }
}

public enum StationaryCombatNoKillRecoveryStep
{
    Inactive,
    WaitTownReturnSettle,
    FollowRevivePath
}
