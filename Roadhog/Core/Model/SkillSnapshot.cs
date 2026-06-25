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
    uint CooldownEndTime);
