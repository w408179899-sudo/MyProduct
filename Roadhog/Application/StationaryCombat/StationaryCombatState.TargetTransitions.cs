using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatState
{
    // Optional roles deliberately preserve the current approach context. Acquiring a target
    // must not erase a gather/tactical/path role established earlier in the same flow.
    internal void BeginFight(WorldObjectSnapshot target, bool? maintenanceDefense = null,
        bool? revivePathClear = null, bool? bypassesHomeLeash = null) =>
        BeginFight(target.EntityId, target.ServerObjectId, maintenanceDefense, revivePathClear, bypassesHomeLeash);

    internal void BeginFight(LockedTargetSnapshot target, bool? maintenanceDefense = null,
        bool? revivePathClear = null, bool? bypassesHomeLeash = null) =>
        BeginFight(target.TargetEntityId, target.ServerObjectId, maintenanceDefense, revivePathClear, bypassesHomeLeash);

    internal void BeginFight(ushort entityId, uint serverObjectId, bool? maintenanceDefense = null,
        bool? revivePathClear = null, bool? bypassesHomeLeash = null)
    {
        Fighting = true;
        SetCurrentTarget(entityId, serverObjectId);
        if (maintenanceDefense.HasValue) CurrentTargetIsMaintenanceDefense = maintenanceDefense.Value;
        if (revivePathClear.HasValue) CurrentTargetIsRevivePathClear = revivePathClear.Value;
        if (bypassesHomeLeash.HasValue) CurrentTargetBypassesHomeLeash = bypassesHomeLeash.Value;
    }

    internal void PreparePathTargetRoles(bool revivePathClear, bool maintenanceDefense)
    {
        CurrentTargetIsRevivePathClear = revivePathClear;
        CurrentTargetBypassesHomeLeash = revivePathClear || maintenanceDefense;
        if (maintenanceDefense) CurrentTargetIsMaintenanceDefense = true;
    }

    internal void MarkMaintenanceDefenseApproach()
    {
        CurrentTargetIsMaintenanceDefense = true;
        CurrentTargetBypassesHomeLeash = true;
    }

    internal void MarkGatherSafetyApproach()
    {
        CurrentTargetIsGatherSafetyClear = true;
        CurrentTargetBypassesHomeLeash = true;
    }

    private void ClearTargetIdentityAndRoles()
    {
        Fighting = false;
        CurrentTargetEntityId = 0;
        CurrentTargetServerObjectId = 0;
        CurrentTargetIsMaintenanceDefense = false;
        CurrentTargetIsRevivePathClear = false;
        CurrentTargetIsGatherSafetyClear = false;
        CurrentTargetBypassesHomeLeash = false;
        CurrentTargetIsTacticalMark = false;
    }
}
