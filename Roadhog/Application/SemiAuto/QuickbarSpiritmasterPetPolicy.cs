using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Pet commands require the local player's living summoned pet at selection and input time.</summary>
internal sealed class QuickbarSpiritmasterPetPolicy
{
    private readonly IReadOnlySet<uint> commandSkillIds;
    private readonly Func<Task<SummonedPetRosterSnapshot>> readPetRoster;

    public QuickbarSpiritmasterPetPolicy(QuickbarSkillPlan plan, IReadOnlyList<SkillSnapshot> skills,
        Func<Task<SummonedPetRosterSnapshot>> readPetRoster)
    {
        this.readPetRoster = readPetRoster;
        commandSkillIds = Flatten(plan.Roots).Where(node =>
        {
            var skill = skills.FirstOrDefault(value => value.SkillId == node.SkillId);
            return IsCommand(node.Name) || IsCommand(skill?.Name) || IsCommand(skill?.DisplayBaseName);
        }).Select(node => node.SkillId).ToHashSet();
    }

    public bool HasCommands => commandSkillIds.Count > 0;

    public async Task<IReadOnlySet<uint>> ReadSuppressedSkillIdsAsync()
    {
        if (!HasCommands) return new HashSet<uint>();
        var roster = await readPetRoster().ConfigureAwait(false);
        return roster.LocalPlayerPet.Pet is { IsSummoned: true, IsAlive: true }
            ? new HashSet<uint>() : commandSkillIds;
    }

    private static bool IsCommand(string? value)
    {
        var name = value?.TrimStart() ?? string.Empty;
        return name.StartsWith("命令:", StringComparison.Ordinal) ||
            name.StartsWith("命令：", StringComparison.Ordinal) ||
            name.StartsWith("Command:", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<QuickbarSkillNode> Flatten(IEnumerable<QuickbarSkillNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
