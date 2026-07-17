using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamCombatTargetAdopter
{
    public static bool TryAdoptLeaderAttackTarget(
        StationaryCombatState? combatState,
        LockedTargetSnapshot? target)
    {
        if (combatState is null || target is null || !target.IsMonsterAlive)
        {
            return false;
        }

        combatState.ReturningHome = false;
        combatState.Fighting = true;
        combatState.SetCurrentTarget(target);
        combatState.MarkCandidate(target.TargetEntityId, target.ServerObjectId, DateTimeOffset.Now);
        combatState.CurrentTargetIsMaintenanceDefense = true;
        combatState.CurrentTargetIsRevivePathClear = false;
        combatState.CurrentTargetBypassesHomeLeash = true;
        combatState.FacedCandidateEntityId = 0;
        combatState.ClearPendingTabVerification();
        combatState.PathCombat.ClearCurrentTargetAnchor();
        return true;
    }
}
