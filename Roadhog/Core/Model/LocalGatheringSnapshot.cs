namespace Roadhog.Core.Model;

public sealed record GatherGaugeSnapshot(
    double Maximum,
    double Displayed,
    double Target);

public sealed record LocalGatheringSnapshot(
    bool DataAvailable,
    bool IsDialogVisible,
    uint GatherSourceId,
    bool HasTargetEntity,
    uint SkillId,
    GatherGaugeSnapshot? SuccessGauge,
    GatherGaugeSnapshot? FailureGauge)
{
    public bool IsActive =>
        DataAvailable &&
        IsDialogVisible &&
        GatherSourceId != 0 &&
        HasTargetEntity;

    public static LocalGatheringSnapshot Unavailable { get; } =
        new(false, false, 0, false, 0, null, null);
}
