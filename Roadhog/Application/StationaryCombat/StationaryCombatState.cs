using Roadhog.Application.BagCleanup;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatState
{
    public StationaryCombatTopLevelState TopLevelState { get; private set; } = StationaryCombatTopLevelState.Normal;

    public StationaryCombatDeathRecoveryState DeathRecovery { get; } = new();

    public StationaryCombatLootAfterKillState LootAfterKill { get; } = new();

    public BagCleanupState BagCleanup { get; } = new();

    public StationaryCombatNoKillRecoveryState NoKillRecovery { get; } = new();

    public StationaryCombatPathCombatState PathCombat { get; } = new();

    public bool CleanupReturnToCombatActive { get; private set; }

    public bool ReturningHome { get; set; }

    public bool Fighting { get; set; }

    public bool IsMovingForward { get; set; }

    public bool IsRightMouseDown { get; set; }

    public int ConsecutiveCameraTurnNoChangeCount { get; private set; }

    public ushort CurrentTargetEntityId { get; set; }

    public uint CurrentTargetServerObjectId { get; set; }

    public bool CurrentTargetIsMaintenanceDefense { get; set; }

    public bool CurrentTargetIsRevivePathClear { get; set; }

    public bool CurrentTargetBypassesHomeLeash { get; set; }

    public ushort TeamLeaderProtectionTargetEntityId { get; private set; }

    public uint TeamLeaderProtectionTargetServerObjectId { get; private set; }

    public uint LocalCombatSideServerObjectId { get; set; }

    public uint LocalCombatSidePetServerObjectId { get; set; }

    public ushort CandidateEntityId { get; set; }

    public uint CandidateServerObjectId { get; set; }

    public ushort FacedCandidateEntityId { get; set; }

    public DateTimeOffset TargetStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort CombatApproachStuckEntityId { get; private set; }

    public uint CombatApproachStuckServerObjectId { get; private set; }

    public Vector3Snapshot? CombatApproachLastProgressPosition { get; private set; }

    public DateTimeOffset CombatApproachLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastCombatApproachJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int CombatApproachJumpCount { get; private set; }

    public Vector3Snapshot? ReturnHomeLastProgressPosition { get; private set; }

    public DateTimeOffset ReturnHomeLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastReturnHomeJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int ReturnHomeJumpCount { get; private set; }

    public ushort CurrentTargetDamageEntityId { get; private set; }

    public uint CurrentTargetDamageServerObjectId { get; private set; }

    public uint CurrentTargetDamageBaselineHp { get; private set; }

    public DateTimeOffset CurrentTargetDamageObservedAt { get; private set; } = DateTimeOffset.MinValue;

    public bool CurrentTargetDamageObserved { get; private set; }

    public ushort MissingCurrentTargetEntityId { get; private set; }

    public uint MissingCurrentTargetServerObjectId { get; private set; }

    public DateTimeOffset MissingCurrentTargetSince { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabCandidateEntityId { get; private set; }

    public uint PendingTabCandidateServerObjectId { get; private set; }

    public DateTimeOffset PendingTabVerifyUntil { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabPreviousLockedEntityId { get; private set; }

    public uint PendingTabPreviousLockedServerObjectId { get; private set; }

    public bool PendingTabCorpseNudged { get; private set; }

    public bool PendingTabWrongLockNudged { get; private set; }

    public ushort WrongLockNudgeCandidateEntityId { get; private set; }

    public uint WrongLockNudgeCandidateServerObjectId { get; private set; }

    public ushort WrongLockNudgeLockedEntityId { get; private set; }

    public uint WrongLockNudgeLockedServerObjectId { get; private set; }

    public DateTimeOffset LastTabAt { get; set; }

    public DateTimeOffset LastWorldScanAt { get; set; }

    public DateTimeOffset LastLogAt { get; set; }

    public Dictionary<string, DateTimeOffset> LastActionLogAtByKey { get; } = new();

    public IReadOnlyList<WorldObjectSnapshot> CachedWorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public HashSet<ushort> IgnoredTargetEntityIds { get; } = new();

    public HashSet<uint> IgnoredTargetServerObjectIds { get; } = new();

    private readonly Dictionary<LootCorpseKey, DateTimeOffset> _attemptedLootCorpses = new();

    public object? PathFollowPoller { get; set; }

    public string StationaryHomePathName { get; private set; } = string.Empty;

    public int StationaryHomePathPointCount { get; private set; }

    public Vector3Snapshot? StationaryHomeFromRevivePath { get; private set; }

    public bool StartupRecoveryChecked { get; private set; }

    public bool StartupRecoveryActive { get; private set; }

    public string StartupRecoveryPathName { get; private set; } = string.Empty;

    public int StartupRecoveryPointIndex { get; private set; } = -1;

    public IReadOnlyList<Vector3Snapshot> StartupRecoveryPoints { get; private set; } = Array.Empty<Vector3Snapshot>();

    public int StartupRecoveryStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? StartupRecoveryLastProgressPosition { get; private set; }

    public DateTimeOffset StartupRecoveryLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastStartupRecoveryJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int StartupRecoveryJumpCount { get; private set; }

    public void ClearTarget()
    {
        Fighting = false;
        CurrentTargetEntityId = 0;
        CurrentTargetServerObjectId = 0;
        CurrentTargetIsMaintenanceDefense = false;
        CurrentTargetIsRevivePathClear = false;
        CurrentTargetBypassesHomeLeash = false;
        ClearTeamLeaderProtectionTarget();
        CandidateEntityId = 0;
        CandidateServerObjectId = 0;
        FacedCandidateEntityId = 0;
        TargetStartedAt = DateTimeOffset.MinValue;
        PathCombat.ClearCurrentTargetAnchor();
        ResetCombatApproachStuckTracking();
        ResetCurrentTargetDamageObservation();
        ResetCurrentTargetMissing();
        ClearPendingTabVerification();
        ClearWrongLockNudge();
    }

    public void EnterDeathRecovery(DateTimeOffset now)
    {
        TopLevelState = StationaryCombatTopLevelState.DeathRecovery;
        ReturningHome = false;
        LootAfterKill.Reset();
        BagCleanup.Reset();
        NoKillRecovery.ResetWatch(now);
        CleanupReturnToCombatActive = false;
        PathCombat.Reset();
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
        return MarkCandidate(entityId, 0, now);
    }

    public bool MarkCandidate(WorldObjectSnapshot target, DateTimeOffset now)
    {
        return MarkCandidate(target.EntityId, target.ServerObjectId, now);
    }

    public bool MarkCandidate(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        var changed = !IsSameTarget(CandidateEntityId, CandidateServerObjectId, entityId, serverObjectId);
        CandidateEntityId = entityId;
        CandidateServerObjectId = serverObjectId;
        if (changed || TargetStartedAt == DateTimeOffset.MinValue)
        {
            TargetStartedAt = now;
        }

        if (changed)
        {
            ResetCombatApproachStuckTracking();
            ResetReturnHomeStuckTracking();
            ClearWrongLockNudge();
        }

        return changed;
    }

    public bool IsCombatApproachStuckTrackingTarget(ushort entityId, uint serverObjectId)
    {
        return IsSameTarget(CombatApproachStuckEntityId, CombatApproachStuckServerObjectId, entityId, serverObjectId);
    }

    public void MarkCombatApproachProgress(ushort entityId, uint serverObjectId, Vector3Snapshot position, DateTimeOffset now)
    {
        CombatApproachStuckEntityId = entityId;
        CombatApproachStuckServerObjectId = serverObjectId;
        CombatApproachLastProgressPosition = position;
        CombatApproachLastProgressAt = now;
    }

    public void MarkCombatApproachJump(DateTimeOffset now)
    {
        LastCombatApproachJumpAt = now;
        CombatApproachJumpCount++;
    }

    public void ResetCombatApproachStuckTracking()
    {
        CombatApproachStuckEntityId = 0;
        CombatApproachStuckServerObjectId = 0;
        CombatApproachLastProgressPosition = null;
        CombatApproachLastProgressAt = DateTimeOffset.MinValue;
        LastCombatApproachJumpAt = DateTimeOffset.MinValue;
        CombatApproachJumpCount = 0;
    }

    public void MarkReturnHomeProgress(Vector3Snapshot position, DateTimeOffset now)
    {
        ReturnHomeLastProgressPosition = position;
        ReturnHomeLastProgressAt = now;
    }

    public void MarkReturnHomeJump(DateTimeOffset now)
    {
        LastReturnHomeJumpAt = now;
        ReturnHomeJumpCount++;
    }

    public void ResetReturnHomeStuckTracking()
    {
        ReturnHomeLastProgressPosition = null;
        ReturnHomeLastProgressAt = DateTimeOffset.MinValue;
        LastReturnHomeJumpAt = DateTimeOffset.MinValue;
        ReturnHomeJumpCount = 0;
    }

    public int MarkCameraTurnNoChange()
    {
        ConsecutiveCameraTurnNoChangeCount++;
        return ConsecutiveCameraTurnNoChangeCount;
    }

    public void ResetCameraTurnNoChange()
    {
        ConsecutiveCameraTurnNoChangeCount = 0;
    }

    public void SetCurrentTarget(WorldObjectSnapshot target)
    {
        SetCurrentTarget(target.EntityId, target.ServerObjectId);
    }

    public void SetCurrentTarget(LockedTargetSnapshot target)
    {
        SetCurrentTarget(target.TargetEntityId, target.ServerObjectId);
    }

    public void SetCurrentTarget(ushort entityId, uint serverObjectId)
    {
        if (!IsSameTarget(CurrentTargetEntityId, CurrentTargetServerObjectId, entityId, serverObjectId))
        {
            ResetCurrentTargetDamageObservation();
        }

        CurrentTargetEntityId = entityId;
        CurrentTargetServerObjectId = serverObjectId;
    }

    public void MarkTeamLeaderProtectionTarget(WorldObjectSnapshot target)
    {
        TeamLeaderProtectionTargetEntityId = target.EntityId;
        TeamLeaderProtectionTargetServerObjectId = target.ServerObjectId;
    }

    public void ClearTeamLeaderProtectionTarget()
    {
        TeamLeaderProtectionTargetEntityId = 0;
        TeamLeaderProtectionTargetServerObjectId = 0;
    }

    public bool IsTeamLeaderProtectionTarget(WorldObjectSnapshot target)
    {
        return IsSameTarget(
            TeamLeaderProtectionTargetEntityId,
            TeamLeaderProtectionTargetServerObjectId,
            target.EntityId,
            target.ServerObjectId);
    }

    public bool IsTeamLeaderProtectionTarget(LockedTargetSnapshot target)
    {
        return IsSameTarget(
            TeamLeaderProtectionTargetEntityId,
            TeamLeaderProtectionTargetServerObjectId,
            target.TargetEntityId,
            target.ServerObjectId);
    }

    public void TrackCurrentTargetDamageObservation(LockedTargetSnapshot target, DateTimeOffset now)
    {
        if (!target.HasKnownHealth || target.CurrentHp == 0)
        {
            ResetCurrentTargetDamageObservation();
            return;
        }

        if (!IsSameTarget(
                CurrentTargetDamageEntityId,
                CurrentTargetDamageServerObjectId,
                target.TargetEntityId,
                target.ServerObjectId) ||
            CurrentTargetDamageObservedAt == DateTimeOffset.MinValue)
        {
            CurrentTargetDamageEntityId = target.TargetEntityId;
            CurrentTargetDamageServerObjectId = target.ServerObjectId;
            CurrentTargetDamageBaselineHp = target.CurrentHp;
            CurrentTargetDamageObservedAt = now;
            CurrentTargetDamageObserved = false;
            return;
        }

        if (target.CurrentHp < CurrentTargetDamageBaselineHp)
        {
            CurrentTargetDamageObserved = true;
            return;
        }

        if (!CurrentTargetDamageObserved && target.CurrentHp > CurrentTargetDamageBaselineHp)
        {
            CurrentTargetDamageBaselineHp = target.CurrentHp;
            CurrentTargetDamageObservedAt = now;
        }
    }

    public void ResetCurrentTargetDamageObservation()
    {
        CurrentTargetDamageEntityId = 0;
        CurrentTargetDamageServerObjectId = 0;
        CurrentTargetDamageBaselineHp = 0;
        CurrentTargetDamageObservedAt = DateTimeOffset.MinValue;
        CurrentTargetDamageObserved = false;
    }

    public DateTimeOffset MarkCurrentTargetMissing(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        if (!IsSameTarget(MissingCurrentTargetEntityId, MissingCurrentTargetServerObjectId, entityId, serverObjectId) ||
            MissingCurrentTargetSince == DateTimeOffset.MinValue)
        {
            MissingCurrentTargetEntityId = entityId;
            MissingCurrentTargetServerObjectId = serverObjectId;
            MissingCurrentTargetSince = now;
        }

        return MissingCurrentTargetSince;
    }

    public void ResetCurrentTargetMissing()
    {
        MissingCurrentTargetEntityId = 0;
        MissingCurrentTargetServerObjectId = 0;
        MissingCurrentTargetSince = DateTimeOffset.MinValue;
    }

    public bool IsCurrentTarget(LockedTargetSnapshot target)
    {
        return IsSameTarget(
            CurrentTargetEntityId,
            CurrentTargetServerObjectId,
            target.TargetEntityId,
            target.ServerObjectId);
    }

    public bool IsCandidate(WorldObjectSnapshot target)
    {
        return IsSameTarget(CandidateEntityId, CandidateServerObjectId, target.EntityId, target.ServerObjectId);
    }

    public WorldObjectSnapshot? FindCandidate(IEnumerable<WorldObjectSnapshot> objects)
    {
        return objects.FirstOrDefault(IsCandidate);
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
        BagCleanup.Reset();
        CleanupReturnToCombatActive = false;
    }

    public bool HasAttemptedLootCorpse(
        ushort entityId,
        uint serverObjectId,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        PruneAttemptedLootCorpses(now, ttl);
        return TryCreateLootCorpseKey(entityId, serverObjectId, out var key) &&
               _attemptedLootCorpses.ContainsKey(key);
    }

    public void MarkLootCorpseAttempted(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        if (TryCreateLootCorpseKey(entityId, serverObjectId, out var key))
        {
            _attemptedLootCorpses[key] = now;
        }
    }

    private void PruneAttemptedLootCorpses(DateTimeOffset now, TimeSpan ttl)
    {
        if (_attemptedLootCorpses.Count == 0)
        {
            return;
        }

        if (ttl <= TimeSpan.Zero)
        {
            _attemptedLootCorpses.Clear();
            return;
        }

        foreach (var entry in _attemptedLootCorpses.ToArray())
        {
            if (now - entry.Value >= ttl)
            {
                _attemptedLootCorpses.Remove(entry.Key);
            }
        }
    }

    private static bool TryCreateLootCorpseKey(
        ushort entityId,
        uint serverObjectId,
        out LootCorpseKey key)
    {
        if (serverObjectId != 0)
        {
            key = new LootCorpseKey(serverObjectId, 0);
            return true;
        }

        if (entityId != 0)
        {
            key = new LootCorpseKey(0, entityId);
            return true;
        }

        key = default;
        return false;
    }

    public bool IsTargetIgnored(ushort entityId)
    {
        return IsTargetIgnored(entityId, 0);
    }

    public bool IsTargetIgnored(WorldObjectSnapshot target)
    {
        return IsTargetIgnored(target.EntityId, target.ServerObjectId);
    }

    public bool IsTargetIgnored(ushort entityId, uint serverObjectId)
    {
        return (serverObjectId != 0 && IgnoredTargetServerObjectIds.Contains(serverObjectId)) ||
               (entityId != 0 && IgnoredTargetEntityIds.Contains(entityId));
    }

    public void IgnoreTarget(ushort entityId)
    {
        IgnoreTarget(entityId, 0);
    }

    public void IgnoreTarget(WorldObjectSnapshot target)
    {
        IgnoreTarget(target.EntityId, target.ServerObjectId);
    }

    public void IgnoreTarget(ushort entityId, uint serverObjectId)
    {
        if (entityId != 0)
        {
            IgnoredTargetEntityIds.Add(entityId);
        }

        if (serverObjectId != 0)
        {
            IgnoredTargetServerObjectIds.Add(serverObjectId);
        }
    }

    public void MarkStartupRecoveryChecked()
    {
        StartupRecoveryChecked = true;
    }

    public void StartCleanupReturnToCombat()
    {
        CleanupReturnToCombatActive = true;
        StartupRecoveryChecked = false;
        StartupRecoveryActive = false;
        StartupRecoveryPathName = string.Empty;
        StartupRecoveryPointIndex = -1;
        StartupRecoveryPoints = Array.Empty<Vector3Snapshot>();
        ResetStartupRecoveryStuckTracking();
        ReturningHome = false;
        ClearTarget();
    }

    public void CompleteCleanupReturnToCombat()
    {
        CleanupReturnToCombatActive = false;
    }

    public bool TryGetStationaryHomeFromRevivePath(
        string pathName,
        out Vector3Snapshot home,
        out int pointCount)
    {
        if (StationaryHomeFromRevivePath is { } cachedHome &&
            string.Equals(StationaryHomePathName, pathName, StringComparison.OrdinalIgnoreCase))
        {
            home = cachedHome;
            pointCount = StationaryHomePathPointCount;
            return true;
        }

        home = default;
        pointCount = 0;
        return false;
    }

    public void SetStationaryHomeFromRevivePath(
        string pathName,
        Vector3Snapshot home,
        int pointCount)
    {
        StationaryHomePathName = pathName;
        StationaryHomeFromRevivePath = home;
        StationaryHomePathPointCount = Math.Max(0, pointCount);
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
        ResetStartupRecoveryStuckTracking();
    }

    public void AdvanceStartupRecoveryPoint()
    {
        if (StartupRecoveryActive)
        {
            StartupRecoveryPointIndex++;
            ResetStartupRecoveryStuckTracking();
        }
    }

    public void MarkStartupRecoveryProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        StartupRecoveryStuckPointIndex = pointIndex;
        StartupRecoveryLastProgressPosition = position;
        StartupRecoveryLastProgressAt = now;
    }

    public void MarkStartupRecoveryJump(DateTimeOffset now)
    {
        LastStartupRecoveryJumpAt = now;
        StartupRecoveryJumpCount++;
    }

    public void ResetStartupRecoveryStuckTracking()
    {
        StartupRecoveryStuckPointIndex = -1;
        StartupRecoveryLastProgressPosition = null;
        StartupRecoveryLastProgressAt = DateTimeOffset.MinValue;
        LastStartupRecoveryJumpAt = DateTimeOffset.MinValue;
        StartupRecoveryJumpCount = 0;
    }

    public void ClearStartupRecovery()
    {
        StartupRecoveryChecked = true;
        StartupRecoveryActive = false;
        StartupRecoveryPathName = string.Empty;
        StartupRecoveryPointIndex = -1;
        StartupRecoveryPoints = Array.Empty<Vector3Snapshot>();
        ResetStartupRecoveryStuckTracking();
    }

    public void PruneIgnoredTargets(IEnumerable<WorldObjectSnapshot> objects)
    {
        var liveServerObjectIds = objects
            .Where(target => target.IsAlive && target.ServerObjectId != 0)
            .Select(target => target.ServerObjectId)
            .ToHashSet();
        var liveEntityIds = objects
            .Where(target => target.IsAlive)
            .Select(target => target.EntityId)
            .ToHashSet();
        IgnoredTargetServerObjectIds.RemoveWhere(serverObjectId => !liveServerObjectIds.Contains(serverObjectId));
        IgnoredTargetEntityIds.RemoveWhere(entityId => !liveEntityIds.Contains(entityId));
    }

    public bool IsPendingTabCandidate(ushort entityId)
    {
        return IsPendingTabCandidate(entityId, 0);
    }

    public bool IsPendingTabCandidate(WorldObjectSnapshot target)
    {
        return IsPendingTabCandidate(target.EntityId, target.ServerObjectId);
    }

    public bool IsPendingTabCandidate(ushort entityId, uint serverObjectId)
    {
        return PendingTabCandidateEntityId != 0 &&
               IsSameTarget(
                   PendingTabCandidateEntityId,
                   PendingTabCandidateServerObjectId,
                   entityId,
                   serverObjectId);
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
        StartPendingTabVerification(entityId, 0, verifyUntil, previousLockedEntityId, 0);
    }

    public void StartPendingTabVerification(
        WorldObjectSnapshot target,
        DateTimeOffset verifyUntil,
        LockedTargetSnapshot? previousLockedTarget)
    {
        StartPendingTabVerification(
            target.EntityId,
            target.ServerObjectId,
            verifyUntil,
            previousLockedTarget?.TargetEntityId ?? 0,
            previousLockedTarget?.ServerObjectId ?? 0);
    }

    public void StartPendingTabVerification(
        ushort entityId,
        uint serverObjectId,
        DateTimeOffset verifyUntil,
        ushort previousLockedEntityId,
        uint previousLockedServerObjectId)
    {
        PendingTabCandidateEntityId = entityId;
        PendingTabCandidateServerObjectId = serverObjectId;
        PendingTabVerifyUntil = verifyUntil;
        PendingTabPreviousLockedEntityId = previousLockedEntityId;
        PendingTabPreviousLockedServerObjectId = previousLockedServerObjectId;
        PendingTabCorpseNudged = false;
        PendingTabWrongLockNudged = false;
    }

    public void ClearPendingTabVerification()
    {
        PendingTabCandidateEntityId = 0;
        PendingTabCandidateServerObjectId = 0;
        PendingTabVerifyUntil = DateTimeOffset.MinValue;
        PendingTabPreviousLockedEntityId = 0;
        PendingTabPreviousLockedServerObjectId = 0;
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
        return HasWrongLockNudge(candidateEntityId, 0, lockedEntityId, 0);
    }

    public bool HasWrongLockNudge(
        ushort candidateEntityId,
        uint candidateServerObjectId,
        ushort lockedEntityId,
        uint lockedServerObjectId)
    {
        return candidateEntityId != 0 &&
               lockedEntityId != 0 &&
               IsSameTarget(
                   WrongLockNudgeCandidateEntityId,
                   WrongLockNudgeCandidateServerObjectId,
                   candidateEntityId,
                   candidateServerObjectId) &&
               IsSameTarget(
                   WrongLockNudgeLockedEntityId,
                   WrongLockNudgeLockedServerObjectId,
                   lockedEntityId,
                   lockedServerObjectId);
    }

    public void MarkWrongLockNudged(ushort candidateEntityId, ushort lockedEntityId)
    {
        MarkWrongLockNudged(candidateEntityId, 0, lockedEntityId, 0);
    }

    public void MarkWrongLockNudged(
        ushort candidateEntityId,
        uint candidateServerObjectId,
        ushort lockedEntityId,
        uint lockedServerObjectId)
    {
        WrongLockNudgeCandidateEntityId = candidateEntityId;
        WrongLockNudgeCandidateServerObjectId = candidateServerObjectId;
        WrongLockNudgeLockedEntityId = lockedEntityId;
        WrongLockNudgeLockedServerObjectId = lockedServerObjectId;
    }

    public void ClearWrongLockNudge()
    {
        WrongLockNudgeCandidateEntityId = 0;
        WrongLockNudgeCandidateServerObjectId = 0;
        WrongLockNudgeLockedEntityId = 0;
        WrongLockNudgeLockedServerObjectId = 0;
    }

    public static bool IsSameTarget(
        ushort leftEntityId,
        uint leftServerObjectId,
        ushort rightEntityId,
        uint rightServerObjectId)
    {
        if (leftServerObjectId != 0 && rightServerObjectId != 0)
        {
            return leftServerObjectId == rightServerObjectId;
        }

        return leftEntityId != 0 &&
               rightEntityId != 0 &&
               leftEntityId == rightEntityId;
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

    public int RevivePathStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? RevivePathLastProgressPosition { get; private set; }

    public DateTimeOffset RevivePathLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastRevivePathJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int RevivePathJumpCount { get; private set; }

    public bool RevivePathLeaderSiphonActive { get; private set; }

    public uint RevivePathLeaderSiphonServerObjectId { get; private set; }

    public string RevivePathLeaderSiphonName { get; private set; } = string.Empty;

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
        ClearRevivePathLeaderSiphon();
        ResetRevivePathStuckTracking();
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

    public void MarkRevivePathProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        RevivePathStuckPointIndex = pointIndex;
        RevivePathLastProgressPosition = position;
        RevivePathLastProgressAt = now;
    }

    public void MarkRevivePathJump(DateTimeOffset now)
    {
        LastRevivePathJumpAt = now;
        RevivePathJumpCount++;
    }

    public void ResetRevivePathStuckTracking()
    {
        RevivePathStuckPointIndex = -1;
        RevivePathLastProgressPosition = null;
        RevivePathLastProgressAt = DateTimeOffset.MinValue;
        LastRevivePathJumpAt = DateTimeOffset.MinValue;
        RevivePathJumpCount = 0;
    }

    public bool ActivateRevivePathLeaderSiphon(uint leaderServerObjectId, string leaderName)
    {
        var changed =
            !RevivePathLeaderSiphonActive ||
            RevivePathLeaderSiphonServerObjectId != leaderServerObjectId;
        RevivePathLeaderSiphonActive = true;
        RevivePathLeaderSiphonServerObjectId = leaderServerObjectId;
        RevivePathLeaderSiphonName = leaderName ?? string.Empty;
        return changed;
    }

    public bool ClearRevivePathLeaderSiphon()
    {
        var wasActive = RevivePathLeaderSiphonActive;
        RevivePathLeaderSiphonActive = false;
        RevivePathLeaderSiphonServerObjectId = 0;
        RevivePathLeaderSiphonName = string.Empty;
        return wasActive;
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
        ClearRevivePathLeaderSiphon();
        ResetRevivePathStuckTracking();
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

public sealed class StationaryCombatPathCombatState
{
    public bool Active { get; private set; }

    public bool Completed { get; private set; }

    public string CompletedPathName { get; private set; } = string.Empty;

    public string PathName { get; private set; } = string.Empty;

    public int PointIndex { get; private set; } = -1;

    public int Direction { get; private set; } = 1;

    public IReadOnlyList<Vector3Snapshot> Points { get; private set; } = Array.Empty<Vector3Snapshot>();

    public Vector3Snapshot? CurrentTargetAnchor { get; private set; }

    public int PathStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? PathLastProgressPosition { get; private set; }

    public DateTimeOffset PathLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastPathJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int PathJumpCount { get; private set; }

    public void Start(string pathName, IReadOnlyList<Vector3Snapshot> points, int pointIndex)
    {
        Active = true;
        Completed = false;
        CompletedPathName = string.Empty;
        PathName = pathName;
        Points = points;
        PointIndex = Math.Clamp(pointIndex, 0, Math.Max(0, points.Count - 1));
        Direction = 1;
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }

    public void AdvancePoint(bool loopPath, bool reverseAtEnd)
    {
        if (!Active || Points.Count == 0)
        {
            Reset();
            return;
        }

        var nextIndex = PointIndex + Direction;
        if (nextIndex >= 0 && nextIndex < Points.Count)
        {
            PointIndex = nextIndex;
            ResetPathStuckTracking();
            return;
        }

        if (loopPath && reverseAtEnd && Points.Count > 1)
        {
            Direction = -Direction;
            PointIndex = Math.Clamp(PointIndex + Direction, 0, Points.Count - 1);
            ResetPathStuckTracking();
            return;
        }

        if (loopPath)
        {
            PointIndex = Direction >= 0 ? 0 : Points.Count - 1;
            ResetPathStuckTracking();
            return;
        }

        Complete();
    }

    public void MarkCurrentTargetAnchor(Vector3Snapshot position)
    {
        CurrentTargetAnchor = position;
    }

    public void ClearCurrentTargetAnchor()
    {
        CurrentTargetAnchor = null;
    }

    public void MarkPathProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        PathStuckPointIndex = pointIndex;
        PathLastProgressPosition = position;
        PathLastProgressAt = now;
    }

    public void MarkPathJump(DateTimeOffset now)
    {
        LastPathJumpAt = now;
        PathJumpCount++;
    }

    public void ResetPathStuckTracking()
    {
        PathStuckPointIndex = -1;
        PathLastProgressPosition = null;
        PathLastProgressAt = DateTimeOffset.MinValue;
        LastPathJumpAt = DateTimeOffset.MinValue;
        PathJumpCount = 0;
    }

    public void Reset()
    {
        Active = false;
        Completed = false;
        CompletedPathName = string.Empty;
        PathName = string.Empty;
        PointIndex = -1;
        Direction = 1;
        Points = Array.Empty<Vector3Snapshot>();
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }

    private void Complete()
    {
        Completed = true;
        CompletedPathName = PathName;
        Active = false;
        PathName = string.Empty;
        PointIndex = -1;
        Direction = 1;
        Points = Array.Empty<Vector3Snapshot>();
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }
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

    public uint KilledTargetLootableRaw { get; private set; }

    public uint KilledTargetInteractionState { get; private set; }

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
        KilledTargetLootableRaw = killedTarget.LootableRaw;
        KilledTargetInteractionState = killedTarget.InteractionState;
        LootKeyPressed = false;
    }

    public void MarkLootKeyPressed()
    {
        LootKeyPressed = true;
    }

    public void MoveToPostCombatMaintenance(DateTimeOffset now)
    {
        Step = StationaryCombatLootAfterKillStep.PostCombatMaintenance;
        StepStartedAt = now;
    }

    public void Advance(DateTimeOffset now)
    {
        Step = Step switch
        {
            StationaryCombatLootAfterKillStep.StopInput => StationaryCombatLootAfterKillStep.WaitAfterKill,
            StationaryCombatLootAfterKillStep.WaitAfterKill => StationaryCombatLootAfterKillStep.PressLootKey,
            StationaryCombatLootAfterKillStep.PressLootKey => StationaryCombatLootAfterKillStep.WaitNearCorpse,
            StationaryCombatLootAfterKillStep.WaitNearCorpse => StationaryCombatLootAfterKillStep.WaitAfterNear,
            StationaryCombatLootAfterKillStep.WaitAfterNear => StationaryCombatLootAfterKillStep.PostCombatMaintenance,
            StationaryCombatLootAfterKillStep.PostCombatMaintenance => StationaryCombatLootAfterKillStep.Complete,
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
        KilledTargetLootableRaw = 0;
        KilledTargetInteractionState = 0;
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
    PostCombatMaintenance,
    Complete
}

internal readonly record struct LootCorpseKey(uint ServerObjectId, ushort EntityId);

internal enum StationaryCombatBehaviorStatus
{
    Running,
    Success,
    Failure
}
