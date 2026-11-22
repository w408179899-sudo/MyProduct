namespace Roadhog.Core.Model;

public readonly record struct LockedCombatTargetSnapshot(ushort TargetEntityId, uint ServerObjectId, bool IsMonsterAlive)
{
    internal static LockedCombatTargetSnapshot FromLockedTarget(LockedTargetSnapshot target) =>
        new(target.TargetEntityId, target.ServerObjectId, target.IsMonsterAlive);
}
