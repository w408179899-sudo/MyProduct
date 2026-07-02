namespace Roadhog.Core.Model;

public sealed record LockedTargetAbnormalStatusSnapshot(
    LockedTargetSnapshot Target,
    uint AbnormalCategory2Count,
    IReadOnlyList<AbnormalStatusEntrySnapshot> Entries,
    DateTimeOffset CapturedAt)
{
    public bool HasTarget => Target.HasTarget;

    public bool IsMonster => Target.IsMonster;

    public bool IsMonsterAlive => Target.IsMonsterAlive;

    public int AbnormalStatusCount => Entries.Count;

    public int PhysicalDebuffCount => Entries.Count(entry => entry.IsPhysicalDebuffCategory);

    public bool HasAbnormalId(uint abnormalId)
    {
        return Entries.Any(entry => entry.AbnormalId == abnormalId);
    }

    public bool HasAbnormal(uint abnormalId, uint category)
    {
        return Entries.Any(entry => entry.AbnormalId == abnormalId && entry.Category == category);
    }

    public static LockedTargetAbnormalStatusSnapshot Empty(DateTimeOffset capturedAt)
    {
        return new LockedTargetAbnormalStatusSnapshot(
            LockedTargetSnapshot.Empty(capturedAt),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            capturedAt);
    }
}
