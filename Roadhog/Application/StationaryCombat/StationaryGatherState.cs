using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public enum StationaryGatherPhase
{
    Idle,
    Ready,
    Approaching,
    WaitingForStart,
    Gathering
}

public sealed class StationaryGatherState
{
    public StationaryGatherPhase Phase { get; private set; }

    public uint ServerObjectId { get; private set; }

    public uint GatherSourceId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string GatherKey { get; private set; } = string.Empty;

    public Vector3Snapshot? Position { get; private set; }

    public float InteractionRadius { get; private set; }

    public DateTimeOffset NodeStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset PhaseStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastAttemptFinishedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StartWaitStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public int ConsecutiveMissingReads { get; private set; }

    public int ConsecutiveUnavailableSnapshotReads { get; private set; }

    public DateTimeOffset LastMissingSnapshotAt { get; private set; } = DateTimeOffset.MinValue;

    public int AttemptStartFailureCount { get; private set; }

    public Vector3Snapshot? LastApproachProgressPosition { get; private set; }

    public DateTimeOffset LastApproachProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastApproachJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int ApproachJumpCount { get; private set; }

    public uint SuppressedServerObjectId { get; private set; }

    public DateTimeOffset SuppressedUntil { get; private set; } = DateTimeOffset.MinValue;

    public bool Active => ServerObjectId != 0;

    public bool IsSuppressed(uint serverObjectId, DateTimeOffset now)
    {
        if (SuppressedServerObjectId == 0 || now >= SuppressedUntil)
        {
            SuppressedServerObjectId = 0;
            SuppressedUntil = DateTimeOffset.MinValue;
            return false;
        }

        return serverObjectId == SuppressedServerObjectId;
    }

    public void Track(
        GatherObjectSnapshot target,
        GatherFilterRuleSettings rule,
        DateTimeOffset now)
    {
        ServerObjectId = target.ServerObjectId;
        GatherSourceId = target.GatherSourceId;
        Name = string.IsNullOrWhiteSpace(target.Name) ? rule.Name ?? string.Empty : target.Name;
        GatherKey = rule.GatherKey;
        Position = target.Position ?? target.SpawnPosition;
        InteractionRadius = target.InteractionRadius;
        NodeStartedAt = now;
        Phase = StationaryGatherPhase.Ready;
        PhaseStartedAt = now;
        ConsecutiveMissingReads = 0;
        ConsecutiveUnavailableSnapshotReads = 0;
        LastMissingSnapshotAt = DateTimeOffset.MinValue;
        AttemptStartFailureCount = 0;
        ResetApproachProgress();
    }

    public void Refresh(GatherObjectSnapshot target, GatherFilterRuleSettings rule)
    {
        GatherSourceId = target.GatherSourceId;
        Name = string.IsNullOrWhiteSpace(target.Name) ? rule.Name ?? string.Empty : target.Name;
        GatherKey = rule.GatherKey;
        Position = target.Position ?? target.SpawnPosition;
        InteractionRadius = target.InteractionRadius;
        ConsecutiveMissingReads = 0;
        LastMissingSnapshotAt = DateTimeOffset.MinValue;
    }

    public int MarkMissing(DateTimeOffset capturedAt)
    {
        if (capturedAt == LastMissingSnapshotAt)
        {
            return ConsecutiveMissingReads;
        }

        LastMissingSnapshotAt = capturedAt;
        ConsecutiveMissingReads++;
        return ConsecutiveMissingReads;
    }

    public int MarkSnapshotUnavailable()
    {
        if (ConsecutiveUnavailableSnapshotReads < int.MaxValue)
        {
            ConsecutiveUnavailableSnapshotReads++;
        }

        return ConsecutiveUnavailableSnapshotReads;
    }

    public void MarkSnapshotAvailable()
    {
        ConsecutiveUnavailableSnapshotReads = 0;
    }

    public void MarkReady(DateTimeOffset now)
    {
        Phase = StationaryGatherPhase.Ready;
        PhaseStartedAt = now;
        ResetApproachProgress();
    }

    public void MarkApproaching(Vector3Snapshot playerPosition, DateTimeOffset now)
    {
        Phase = StationaryGatherPhase.Approaching;
        PhaseStartedAt = now;
        if (LastApproachProgressPosition is null)
        {
            LastApproachProgressPosition = playerPosition;
            LastApproachProgressAt = now;
        }
    }

    public void ObserveApproachProgress(Vector3Snapshot playerPosition, DateTimeOffset now, double minimumDistance)
    {
        if (LastApproachProgressPosition is not { } previous ||
            StationaryCombatTargetSelector.HorizontalDistance(previous, playerPosition) >= minimumDistance)
        {
            LastApproachProgressPosition = playerPosition;
            LastApproachProgressAt = now;
            ApproachJumpCount = 0;
        }
    }

    public bool ShouldJumpApproach(DateTimeOffset now, TimeSpan stuckDelay, TimeSpan retryDelay, int maximumJumps)
    {
        return LastApproachProgressAt != DateTimeOffset.MinValue &&
               now - LastApproachProgressAt >= stuckDelay &&
               (LastApproachJumpAt == DateTimeOffset.MinValue || now - LastApproachJumpAt >= retryDelay) &&
               ApproachJumpCount < maximumJumps;
    }

    public void MarkApproachJump(DateTimeOffset now)
    {
        LastApproachJumpAt = now;
        ApproachJumpCount++;
    }

    public bool IsApproachTimedOut(DateTimeOffset now, TimeSpan timeout)
    {
        return LastApproachProgressAt != DateTimeOffset.MinValue &&
               now - LastApproachProgressAt >= timeout;
    }

    public void MarkKeyPressed(DateTimeOffset now)
    {
        if (StartWaitStartedAt == DateTimeOffset.MinValue)
        {
            StartWaitStartedAt = now;
        }

        Phase = StationaryGatherPhase.WaitingForStart;
        PhaseStartedAt = now;
    }

    public bool IsStartWaitTimedOut(DateTimeOffset now, TimeSpan timeout)
    {
        return StartWaitStartedAt != DateTimeOffset.MinValue &&
               now - StartWaitStartedAt >= timeout;
    }

    public void MarkGathering(DateTimeOffset now)
    {
        if (Phase != StationaryGatherPhase.Gathering)
        {
            Phase = StationaryGatherPhase.Gathering;
            PhaseStartedAt = now;
            AttemptStartFailureCount = 0;
            StartWaitStartedAt = DateTimeOffset.MinValue;
        }
    }

    public int MarkAttemptStartFailed(DateTimeOffset now)
    {
        AttemptStartFailureCount++;
        MarkReady(now);
        return AttemptStartFailureCount;
    }

    public void MarkAttemptFinished(DateTimeOffset now)
    {
        LastAttemptFinishedAt = now;
        StartWaitStartedAt = DateTimeOffset.MinValue;
        MarkReady(now);
    }

    public bool CanPressAgain(DateTimeOffset now, TimeSpan delay)
    {
        return LastAttemptFinishedAt == DateTimeOffset.MinValue ||
               now - LastAttemptFinishedAt >= delay;
    }

    public void SuppressCurrent(DateTimeOffset now, TimeSpan duration)
    {
        SuppressedServerObjectId = ServerObjectId;
        SuppressedUntil = now + duration;
        ClearActive();
    }

    public void CompleteCurrent()
    {
        ClearActive();
    }

    public void Reset()
    {
        ClearActive();
        SuppressedServerObjectId = 0;
        SuppressedUntil = DateTimeOffset.MinValue;
    }

    private void ClearActive()
    {
        Phase = StationaryGatherPhase.Idle;
        ServerObjectId = 0;
        GatherSourceId = 0;
        Name = string.Empty;
        GatherKey = string.Empty;
        Position = null;
        InteractionRadius = 0;
        NodeStartedAt = DateTimeOffset.MinValue;
        PhaseStartedAt = DateTimeOffset.MinValue;
        LastAttemptFinishedAt = DateTimeOffset.MinValue;
        StartWaitStartedAt = DateTimeOffset.MinValue;
        ConsecutiveMissingReads = 0;
        ConsecutiveUnavailableSnapshotReads = 0;
        LastMissingSnapshotAt = DateTimeOffset.MinValue;
        AttemptStartFailureCount = 0;
        ResetApproachProgress();
    }

    private void ResetApproachProgress()
    {
        LastApproachProgressPosition = null;
        LastApproachProgressAt = DateTimeOffset.MinValue;
        LastApproachJumpAt = DateTimeOffset.MinValue;
        ApproachJumpCount = 0;
    }
}
