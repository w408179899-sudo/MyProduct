using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatState
{
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
    }

    public bool MarkCandidate(ushort entityId, DateTimeOffset now)
    {
        var changed = CandidateEntityId != entityId;
        CandidateEntityId = entityId;
        if (changed || TargetStartedAt == DateTimeOffset.MinValue)
        {
            TargetStartedAt = now;
        }

        return changed;
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

    public void StartPendingTabVerification(ushort entityId, DateTimeOffset verifyUntil)
    {
        PendingTabCandidateEntityId = entityId;
        PendingTabVerifyUntil = verifyUntil;
    }

    public void ClearPendingTabVerification()
    {
        PendingTabCandidateEntityId = 0;
        PendingTabVerifyUntil = DateTimeOffset.MinValue;
    }
}
