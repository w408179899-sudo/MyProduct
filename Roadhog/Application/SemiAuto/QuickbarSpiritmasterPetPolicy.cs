using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Pet commands require the local player's living summoned pet at selection and input time.</summary>
internal sealed class QuickbarSpiritmasterPetPolicy
{
    private readonly IReadOnlySet<uint> commandSkillIds;
    private readonly IReadOnlyDictionary<uint, SpiritmasterPetRequirement> requirements;
    private readonly Func<Task<SummonedPetRosterSnapshot>> readPetRoster;

    public QuickbarSpiritmasterPetPolicy(QuickbarSkillPlan plan, IReadOnlyList<SkillSnapshot> skills,
        Func<Task<SummonedPetRosterSnapshot>> readPetRoster, bool includePetPrerequisites = false)
    {
        this.readPetRoster = readPetRoster;
        commandSkillIds = Flatten(plan.Roots).Where(node =>
        {
            var skill = skills.FirstOrDefault(value => value.SkillId == node.SkillId);
            return IsCommand(node.Name) || IsCommand(skill?.Name) || IsCommand(skill?.DisplayBaseName);
        }).Select(node => node.SkillId).ToHashSet();
        requirements = Flatten(plan.Roots).DistinctBy(node => node.SkillId).ToDictionary(node => node.SkillId,
            node => includePetPrerequisites
                ? SpiritmasterSkillEligibility.Classify(skills.FirstOrDefault(skill => skill.SkillId == node.SkillId), node.SkillId, node.Name)
                : commandSkillIds.Contains(node.SkillId) ? SpiritmasterPetRequirement.LivingPet : SpiritmasterPetRequirement.None);
    }

    public bool HasCommands => commandSkillIds.Count > 0;
    public bool HasPetRules => requirements.Values.Any(value => value != SpiritmasterPetRequirement.None);

    public async Task<IReadOnlySet<uint>> ReadSuppressedSkillIdsAsync()
    {
        if (!HasPetRules) return new HashSet<uint>();
        var roster = await readPetRoster().ConfigureAwait(false);
        return requirements.Where(pair => SpiritmasterSkillEligibility.BlockReason(pair.Value, roster.LocalPlayerPet.Pet) is not null)
            .Select(pair => pair.Key).ToHashSet();
    }

    private static bool IsCommand(string? value) => SpiritmasterSkillEligibility.IsCommand(value);

    private static IEnumerable<QuickbarSkillNode> Flatten(IEnumerable<QuickbarSkillNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
