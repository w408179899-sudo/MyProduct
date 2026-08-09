using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamCombatTargetAdopter
{
    public static bool IsCurrentTacticalMarkedTarget(
        StationaryCombatState? combatState,
        LockedTargetSnapshot? target)
    {
        return combatState is { CurrentTargetIsTacticalMark: true } &&
               target is not null &&
               StationaryCombatState.IsSameTarget(
                   combatState.CurrentTargetEntityId,
                   combatState.CurrentTargetServerObjectId,
                   target.TargetEntityId,
                   target.ServerObjectId);
    }

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
        combatState.CurrentTargetIsTacticalMark = false;
        combatState.FacedCandidateEntityId = 0;
        combatState.ClearPendingTabVerification();
        combatState.PathCombat.ClearCurrentTargetAnchor();
        return true;
    }

    public static bool TryAdoptSelfDefenseTarget(
        StationaryCombatState? combatState,
        WorldObjectSnapshot? target)
    {
        if (combatState is null ||
            target is null ||
            !StationaryCombatTargetSelector.IsSelectableMonster(target))
        {
            return false;
        }

        combatState.ReturningHome = false;
        combatState.Fighting = true;
        combatState.SetCurrentTarget(target);
        combatState.MarkCandidate(target, DateTimeOffset.Now);
        combatState.CurrentTargetIsMaintenanceDefense = true;
        combatState.CurrentTargetIsRevivePathClear = false;
        combatState.CurrentTargetBypassesHomeLeash = true;
        combatState.CurrentTargetIsTacticalMark = false;
        combatState.FacedCandidateEntityId = 0;
        combatState.ClearPendingTabVerification();
        combatState.PathCombat.ClearCurrentTargetAnchor();
        return true;
    }

    public static bool TryAdoptTacticalMarkedTarget(
        StationaryCombatState? combatState,
        LockedTargetSnapshot? target)
    {
        if (combatState is null || !TacticalMarkCoordinator.IsStrictlyLivingMonster(target))
        {
            return false;
        }

        combatState.ReturningHome = false;
        combatState.Fighting = true;
        combatState.SetCurrentTarget(target!);
        combatState.MarkCandidate(target!.TargetEntityId, target.ServerObjectId, DateTimeOffset.Now);
        combatState.CurrentTargetIsMaintenanceDefense = true;
        combatState.CurrentTargetIsRevivePathClear = false;
        combatState.CurrentTargetBypassesHomeLeash = true;
        combatState.CurrentTargetIsTacticalMark = true;
        combatState.FacedCandidateEntityId = 0;
        combatState.ClearPendingTabVerification();
        combatState.PathCombat.ClearCurrentTargetAnchor();
        return true;
    }
}
