namespace Roadhog.Core.Model;

public sealed record SkillSnapshot(
    uint SkillId,
    string Name,
    int HighestLevel,
    int ItemLevel,
    string? DisplayBaseName,
    int? DisplayTier,
    bool IsToggle,
    uint CooldownDuration,
    uint CooldownEndTime,
    string? XmlActivation = null,
    string? XmlTags = null,
    string? XmlTargetSlot = null,
    string? XmlChainCategory = null,
    string? XmlPrechainCategory = null,
    string? XmlChainTime = null,
    string? XmlCounterSkill = null,
    string? XmlCostDp = null);
