using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatState
{
    public StationaryCombatTopLevelState TopLevelState { get; private set; } = StationaryCombatTopLevelState.Normal;

    public StationaryCombatDeathRecoveryState DeathRecovery { get; } = new();

    public StationaryCombatLootAfterKillState LootAfterKill { get; } = new();

    public bool ReturningHome { get; set; }

    public bool Fighting { get; set; }

    public bool IsMovingForward { get; set; }

    public bool IsRightMouseDown { get; set; }

    public ushort CurrentTargetEntityId { get; set; }

    public bool CurrentTargetIsMaintenanceDefense { get; set; }

    public ushort CandidateEntityId { get; set; }

    public ushort FacedCandidateEntityId { get; set; }

    public DateTimeOffset TargetStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabCandidateEntityId { get; private set; }

    public DateTimeOffset PendingTabVerifyUntil { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabPreviousLockedEntityId { get; private set; }

    public bool PendingTabCorpseNudged { get; private set; }

    public bool PendingTabWrongLockNudged { get; private set; }

    public ushort WrongLockNudgeCandidateEntityId { get; private set; }

    public ushort WrongLockNudgeLockedEntityId { get; private set; }

    public DateTimeOffset LastTabAt { get; set; }

    public DateTimeOffset LastWorldScanAt { get; set; }

    public DateTimeOffset LastLogAt { get; set; }

    public Dictionary<string, DateTimeOffset> LastActionLogAtByKey { get; } = new();

    public IReadOnlyList<WorldObjectSnapshot> CachedWorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public HashSet<ushort> IgnoredTargetEntityIds { get; } = new();

    public object? PathFollowPoller { get; set; }

    public bool StartupRecoveryChecked { get; private set; }

    public bool StartupRecoveryActive { get; private set; }

    public string StartupRecoveryPathName { get; private set; } = string.Empty;

    public int StartupRecoveryPointIndex { get; private set; } = -1;

    public IReadOnlyList<Vector3Snapshot> StartupRecoveryPoints { get; private set; } = Array.Empty<Vector3Snapshot>();

    public void ClearTarget()
    {
        Fighting = false;
        CurrentTargetEntityId = 0;
        CurrentTargetIsMaintenanceDefense = false;
        CandidateEntityId = 0;
        FacedCandidateEntityId = 0;
        TargetStartedAt = DateTimeOffset.MinValue;
        ClearPendingTabVerification();
        ClearWrongLockNudge();
    }

    public void EnterDeathRecovery(DateTimeOffset now)
    {
        TopLevelState = StationaryCombatTopLevelState.DeathRecovery;
        ReturningHome = false;
        LootAfterKill.Reset();
        ClearStartupRecovery();
        ClearTarget();
        DeathRecovery.Start(now);
    }

    public void ExitDeathRecovery()
    {
        TopLevelState = StationaryCombatTopLevelState.Normal;
        DeathRecovery.Reset();
    }

    public bool MarkCandidate(ushort entityId, DateTimeOffset now)
    {
        var changed = CandidateEntityId != entityId;
        CandidateEntityId = entityId;
        if (changed || TargetStartedAt == DateTimeOffset.MinValue)
        {
            TargetStartedAt = now;
        }

        if (changed)
        {
            ClearWrongLockNudge();
        }

        return changed;
    }

    public void StartLootAfterKill(LockedTargetSnapshot killedTarget, DateTimeOffset now)
    {
        LootAfterKill.Start(killedTarget, now);
        ReturningHome = false;
        ClearTarget();
    }

    public void ClearLootAfterKill()
    {
        LootAfterKill.Reset();
    }

    public bool IsTargetIgnored(ushort entityId)
    {
        return entityId != 0 && IgnoredTargetEntityIds.Contains(entityId);
    }

    public void IgnoreTarget(ushort entityId)
    {
        if (entityId != 0)
        {
            IgnoredTargetEntityIds.Add(entityId);
        }
    }

    public void MarkStartupRecoveryChecked()
    {
        StartupRecoveryChecked = true;
    }

    public void StartStartupRecovery(
        string pathName,
        IReadOnlyList<Vector3Snapshot> points,
        int pointIndex)
    {
        StartupRecoveryChecked = true;
        StartupRecoveryActive = true;
        StartupRecoveryPathName = pathName;
        StartupRecoveryPoints = points;
        StartupRecoveryPointIndex = Math.Max(0, pointIndex);
    }

    public void AdvanceStartupRecoveryPoint()
    {
        if (StartupRecoveryActive)
        {
            StartupRecoveryPointIndex++;
        }
    }

    public void ClearStartupRecovery()
    {
        StartupRecoveryChecked = true;
        StartupRecoveryActive = false;
        StartupRecoveryPathName = string.Empty;
        StartupRecoveryPointIndex = -1;
        StartupRecoveryPoints = Array.Empty<Vector3Snapshot>();
    }

    public void PruneIgnoredTargets(IEnumerable<WorldObjectSnapshot> objects)
    {
        var liveEntityIds = objects
            .Where(target => target.IsAlive)
            .Select(target => target.EntityId)
            .ToHashSet();
        IgnoredTargetEntityIds.RemoveWhere(entityId => !liveEntityIds.Contains(entityId));
    }

    public bool IsPendingTabCandidate(ushort entityId)
    {
        return PendingTabCandidateEntityId != 0 &&
               PendingTabCandidateEntityId == entityId;
    }

    public bool IsPendingTabVerifyExpired(DateTimeOffset now)
    {
        return PendingTabCandidateEntityId != 0 &&
               now >= PendingTabVerifyUntil;
    }

    public void StartPendingTabVerification(
        ushort entityId,
        DateTimeOffset verifyUntil,
        ushort previousLockedEntityId)
    {
        PendingTabCandidateEntityId = entityId;
        PendingTabVerifyUntil = verifyUntil;
        PendingTabPreviousLockedEntityId = previousLockedEntityId;
        PendingTabCorpseNudged = false;
        PendingTabWrongLockNudged = false;
    }

    public void ClearPendingTabVerification()
    {
        PendingTabCandidateEntityId = 0;
        PendingTabVerifyUntil = DateTimeOffset.MinValue;
        PendingTabPreviousLockedEntityId = 0;
        PendingTabCorpseNudged = false;
        PendingTabWrongLockNudged = false;
    }

    public bool TryMarkPendingTabCorpseNudged()
    {
        if (PendingTabCorpseNudged)
        {
            return false;
        }

        PendingTabCorpseNudged = true;
        return true;
    }

    public bool TryMarkPendingTabWrongLockNudged()
    {
        if (PendingTabWrongLockNudged)
        {
            return false;
        }

        PendingTabWrongLockNudged = true;
        return true;
    }

    public bool HasWrongLockNudge(ushort candidateEntityId, ushort lockedEntityId)
    {
        return candidateEntityId != 0 &&
               lockedEntityId != 0 &&
               WrongLockNudgeCandidateEntityId == candidateEntityId &&
               WrongLockNudgeLockedEntityId == lockedEntityId;
    }

    public void MarkWrongLockNudged(ushort candidateEntityId, ushort lockedEntityId)
    {
        WrongLockNudgeCandidateEntityId = candidateEntityId;
        WrongLockNudgeLockedEntityId = lockedEntityId;
    }

    public void ClearWrongLockNudge()
    {
        WrongLockNudgeCandidateEntityId = 0;
        WrongLockNudgeLockedEntityId = 0;
    }
}

public enum StationaryCombatTopLevelState
{
    Normal,
    DeathRecovery
}

public sealed class StationaryCombatDeathRecoveryState
{
    public StationaryCombatDeathRecoveryStep Step { get; private set; } = StationaryCombatDeathRecoveryStep.StopInput;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public bool ReviveClicked { get; set; }

    public DateTimeOffset LastReviveClickAt { get; private set; } = DateTimeOffset.MinValue;

    public int ReviveClickCount { get; private set; }

    public int PostReviveScrollsSent { get; set; }

    public string RevivePathName { get; set; } = string.Empty;

    public int RevivePathPointIndex { get; set; } = -1;

    public IReadOnlyList<Vector3Snapshot> RevivePathPoints { get; set; } = Array.Empty<Vector3Snapshot>();

    public void Start(DateTimeOffset now)
    {
        Step = StationaryCombatDeathRecoveryStep.StopInput;
        StartedAt = now;
        StepStartedAt = now;
        ReviveClicked = false;
        LastReviveClickAt = DateTimeOffset.MinValue;
        ReviveClickCount = 0;
        PostReviveScrollsSent = 0;
        RevivePathName = string.Empty;
        RevivePathPointIndex = -1;
        RevivePathPoints = Array.Empty<Vector3Snapshot>();
    }

    public void MarkReviveClicked(DateTimeOffset now)
    {
        ReviveClicked = true;
        LastReviveClickAt = now;
        ReviveClickCount++;
    }

    public void Advance(DateTimeOffset now)
    {
        Step = Step switch
        {
            StationaryCombatDeathRecoveryStep.StopInput => StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick,
            StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick => StationaryCombatDeathRecoveryStep.ClickRevive,
            StationaryCombatDeathRecoveryStep.ClickRevive => StationaryCombatDeathRecoveryStep.WaitAlive,
            StationaryCombatDeathRecoveryStep.WaitAlive => StationaryCombatDeathRecoveryStep.PostReviveScroll,
            StationaryCombatDeathRecoveryStep.PostReviveScroll => StationaryCombatDeathRecoveryStep.PostReviveMaintenance,
            StationaryCombatDeathRecoveryStep.PostReviveMaintenance => StationaryCombatDeathRecoveryStep.FollowRevivePath,
            StationaryCombatDeathRecoveryStep.FollowRevivePath => StationaryCombatDeathRecoveryStep.Complete,
            _ => StationaryCombatDeathRecoveryStep.Complete
        };
        StepStartedAt = now;
    }

    public void Reset()
    {
        Step = StationaryCombatDeathRecoveryStep.StopInput;
        StartedAt = DateTimeOffset.MinValue;
        StepStartedAt = DateTimeOffset.MinValue;
        ReviveClicked = false;
        LastReviveClickAt = DateTimeOffset.MinValue;
        ReviveClickCount = 0;
        PostReviveScrollsSent = 0;
        RevivePathName = string.Empty;
        RevivePathPointIndex = -1;
        RevivePathPoints = Array.Empty<Vector3Snapshot>();
    }
}

public enum StationaryCombatDeathRecoveryStep
{
    StopInput,
    WaitBeforeReviveClick,
    ClickRevive,
    WaitAlive,
    PostReviveScroll,
    PostReviveMaintenance,
    FollowRevivePath,
    Complete
}

public sealed class StationaryCombatLootAfterKillState
{
    public StationaryCombatLootAfterKillStep Step { get; private set; } = StationaryCombatLootAfterKillStep.Inactive;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort KilledTargetEntityId { get; private set; }

    public uint KilledTargetServerObjectId { get; private set; }

    public string KilledTargetName { get; private set; } = string.Empty;

    public Vector3Snapshot? KilledTargetPosition { get; private set; }

    public bool LootKeyPressed { get; private set; }

    public bool Active => Step != StationaryCombatLootAfterKillStep.Inactive &&
                          Step != StationaryCombatLootAfterKillStep.Complete;

    public void Start(LockedTargetSnapshot killedTarget, DateTimeOffset now)
    {
        Step = StationaryCombatLootAfterKillStep.StopInput;
        StartedAt = now;
        StepStartedAt = now;
        KilledTargetEntityId = killedTarget.TargetEntityId;
        KilledTargetServerObjectId = killedTarget.ServerObjectId;
        KilledTargetName = killedTarget.Name ?? string.Empty;
        KilledTargetPosition = killedTarget.Position;
        LootKeyPressed = false;
    }

    public void MarkLootKeyPressed()
    {
        LootKeyPressed = true;
    }

    public void Advance(DateTimeOffset now)
    {
        Step = Step switch
        {
            StationaryCombatLootAfterKillStep.StopInput => StationaryCombatLootAfterKillStep.WaitAfterKill,
            StationaryCombatLootAfterKillStep.WaitAfterKill => StationaryCombatLootAfterKillStep.PressLootKey,
            StationaryCombatLootAfterKillStep.PressLootKey => StationaryCombatLootAfterKillStep.WaitNearCorpse,
            StationaryCombatLootAfterKillStep.WaitNearCorpse => StationaryCombatLootAfterKillStep.WaitAfterNear,
            StationaryCombatLootAfterKillStep.WaitAfterNear => StationaryCombatLootAfterKillStep.Complete,
            _ => StationaryCombatLootAfterKillStep.Complete
        };
        StepStartedAt = now;
    }

    public void Reset()
    {
        Step = StationaryCombatLootAfterKillStep.Inactive;
        StartedAt = DateTimeOffset.MinValue;
        StepStartedAt = DateTimeOffset.MinValue;
        KilledTargetEntityId = 0;
        KilledTargetServerObjectId = 0;
        KilledTargetName = string.Empty;
        KilledTargetPosition = null;
        LootKeyPressed = false;
    }
}

public enum StationaryCombatLootAfterKillStep
{
    Inactive,
    StopInput,
    WaitAfterKill,
    PressLootKey,
    WaitNearCorpse,
    WaitAfterNear,
    Complete
}

internal enum StationaryCombatBehaviorStatus
{
    Running,
    Success,
    Failure
}
