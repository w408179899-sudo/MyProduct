using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatState
{
    public bool ReturningHome { get; set; }

    public bool Fighting { get; set; }

    public bool IsMovingForward { get; set; }

    public bool IsRightMouseDown { get; set; }

    public ushort CurrentTargetEntityId { get; set; }

    public ushort CandidateEntityId { get; set; }

    public ushort FacedCandidateEntityId { get; set; }

    public ushort PendingTabCandidateEntityId { get; private set; }

    public DateTimeOffset PendingTabVerifyUntil { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTabAt { get; set; }

    public DateTimeOffset LastWorldScanAt { get; set; }

    public DateTimeOffset LastLogAt { get; set; }

    public Dictionary<string, DateTimeOffset> LastActionLogAtByKey { get; } = new();

    public IReadOnlyList<WorldObjectSnapshot> CachedWorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public object? PathFollowPoller { get; set; }

    public void ClearTarget()
    {
        Fighting = false;
        CurrentTargetEntityId = 0;
        CandidateEntityId = 0;
        FacedCandidateEntityId = 0;
        ClearPendingTabVerification();
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
