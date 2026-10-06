using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private static readonly HashSet<string> ManualAttackEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkillATK_Instant", "SkillATK", "SkillATKDrain_Instant",
        "SpellATK_Instant", "SpellATK", "SpellATKDrain_Instant", "SpellATKDrain",
        "DelayedSpellATK_Instant", "DelayedSkillATK_Instant", "NoReduceSpellATK_Instant",
        "FPATK_Instant", "FPATK", "DelayedFPATK_Instant", "ProcATK_Instant",
        "MPAttack_Instant", "MPAttack", "DashATK", "BackDashATK", "MoveBehindATK", "DeathBlow",
        "Poison", "Bleed"
    };

    private static bool HasManualSkillAttackEffect(SkillSnapshot skill)
    {
        // These effects deal damage as part of the skill. State slots and visual
        // effects may describe an attached debuff, rather than the skill's purpose.
        return skill.XmlEffects?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(ManualAttackEffects.Contains) ?? false;
    }

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
