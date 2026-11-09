using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Account-local optional plans. Unmatched targets receive the original plan instance.</summary>
internal sealed class AdvancedSkillCombatPlans(AdvancedSkillSettings settings, QuickbarSkillPlan ordinary,
    SkillKeyBindings bindings, Action<string>? missing)
{
    private readonly AdvancedSkillSettings settings = settings.Clone();
    private readonly Dictionary<AdvancedMonsterCategory, QuickbarSkillPlan> plans = new();

    public QuickbarSkillPlan Resolve(LockedTargetSnapshot target)
    {
        var category = target.IsMonsterAlive ? settings.Match(target.Name) : null;
        if (category is null) return ordinary;
        if (!plans.TryGetValue(category.Value, out var plan))
        {
            var advanced = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = settings.Group(category.Value).Skills }, bindings, missing);
            plan = QuickbarSkillPlan.WithAdvanced(ordinary, advanced, category.Value.ToString());
            plans.Add(category.Value, plan);
        }
        return plan;
    }
}
