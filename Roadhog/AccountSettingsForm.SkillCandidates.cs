using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private static IEnumerable<SkillSnapshot> SelectHighestSkillComboCandidates(IEnumerable<SkillSnapshot> skills)
    {
        // Unknown names must stay separate. Only the candidate view is filtered;
        // callers retain an explicitly saved selection, including an older tier.
        return skills.GroupBy(skill =>
            {
                var name = NormalizeSkillBaseName(GetSkillBaseName(skill));
                return (Name: name, Id: name.Length == 0 ? skill.SkillId : 0u);
            })
            .Select(group => group.OrderByDescending(GetSkillRank).First());
    }
}
