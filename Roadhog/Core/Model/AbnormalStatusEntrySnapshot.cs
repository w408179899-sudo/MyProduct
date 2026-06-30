namespace Roadhog.Core.Model;

public sealed record AbnormalStatusEntrySnapshot(
    uint Field00,
    uint AbnormalId,
    uint Category,
    int TimeOrSource,
    ushort LevelOrStack,
    ulong EntryAddress)
{
    public bool IsBuffCategory => Category == PlayerAbnormalStatusSnapshot.BuffCategory;

    public bool IsPhysicalDebuffCategory => Category == PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory;

    public bool IsHarmfulForRest => AbnormalId != 0 && IsPhysicalDebuffCategory;
}
