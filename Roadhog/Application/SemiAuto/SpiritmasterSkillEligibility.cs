using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

internal enum SpiritmasterPetRequirement { None, LivingPet, MissingPet }

/// <summary>Pet prerequisites only; cooldown, range, resources and target checks still apply.</summary>
internal static class SpiritmasterSkillEligibility
{
    public static SpiritmasterPetRequirement Classify(SkillSnapshot? skill, uint id = 0, params string?[] names)
    {
        if (string.Equals(skill?.XmlFirstTarget?.Trim(), "MyPet", StringComparison.OrdinalIgnoreCase) ||
            (skill?.SkillId ?? id) == 1771 ||
            names.Append(skill?.Name).Append(skill?.DisplayBaseName).Any(IsPetActionName))
            return SpiritmasterPetRequirement.LivingPet;

        // SummonHoming (energy attacks) and summon-speed buffs are not persistent pets.
        if ((skill?.XmlEffects ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(effect => string.Equals(effect, "Summon", StringComparison.OrdinalIgnoreCase)))
            return SpiritmasterPetRequirement.MissingPet;
        return SpiritmasterPetRequirement.None;
    }

    public static bool IsCommand(string? value)
    {
        var name = value?.TrimStart() ?? string.Empty;
        return name.StartsWith("命令:", StringComparison.Ordinal) || name.StartsWith("命令：", StringComparison.Ordinal) ||
            name.StartsWith("Command:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPetActionName(string? value) => IsCommand(value) ||
        (value?.TrimStart().StartsWith("精灵吸收", StringComparison.Ordinal) == true);

    public static string? BlockReason(SpiritmasterPetRequirement requirement, SummonedPetSnapshot? pet,
        uint? expectedPetId = null)
    {
        var alive = pet is { IsSummoned: true, IsAlive: true };
        if (requirement == SpiritmasterPetRequirement.MissingPet) return alive ? "pet_already_present" : null;
        if (requirement != SpiritmasterPetRequirement.LivingPet) return null;
        if (!alive) return "pet_missing";
        return expectedPetId is { } expected && expected != pet!.ServerObjectId ? "pet_changed" : null;
    }
}
