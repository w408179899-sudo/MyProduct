namespace Roadhog.Core.Model;

public sealed record GatherMaterialDefinition(
    string InternalName,
    int Rate,
    bool IsExtra);

public sealed record GatherSourceDefinition(
    uint GatherSourceId,
    string InternalName,
    string DescriptionKey,
    string Category,
    string SourceType,
    string Mesh,
    string SourceColor,
    double SourceUpper,
    string SourceFx,
    string MotionName,
    string HarvestSkill,
    int RequiredSkillLevel,
    int RequiredCharacterLevel,
    uint GatherDelayId,
    int GatherDelay,
    string RequiredItem,
    int CheckType,
    int EraseValue,
    int TheoreticalHarvestCount,
    int SuccessAdjustment,
    int FailureAdjustment,
    int AerialAdjustment,
    int CaptchaRate,
    IReadOnlyList<GatherMaterialDefinition> Materials)
{
    public bool RequiresItem => CheckType != 0 && !string.IsNullOrWhiteSpace(RequiredItem);

    public bool ConsumesRequiredItem => RequiresItem && EraseValue != 0;
}
