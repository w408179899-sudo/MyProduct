using Roadhog.Core.Accounts;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoSkillPlan
{
    private SemiAutoSkillPlan(
        IReadOnlyList<SemiAutoSkillNode> roots,
        IReadOnlyList<SemiAutoSkillNode> triggerPrefixRoots)
    {
        Roots = roots;
        TriggerPrefixRoots = triggerPrefixRoots;
    }

    public IReadOnlyList<SemiAutoSkillNode> Roots { get; }

    public IReadOnlyList<SemiAutoSkillNode> TriggerPrefixRoots { get; }

    public bool HasExecutableSkills => Roots.Any(root => !root.IsTrigger);

    public static SemiAutoSkillPlan FromSettings(SkillScriptSettings settings)
    {
        var roots = settings.Mode == SkillConfigurationMode.Auto
            ? BuildAutoRoots(settings)
            : BuildManualRoots(settings);

        var triggerPrefix = roots
            .TakeWhile(root => root.IsTrigger)
            .ToArray();

        return new SemiAutoSkillPlan(roots, triggerPrefix);
    }

    private static IReadOnlyList<SemiAutoSkillNode> BuildAutoRoots(SkillScriptSettings settings)
    {
        var keyOrder = settings.KeyOrder.Count == 0
            ? SkillScriptSettings.DefaultKeyOrder()
            : settings.KeyOrder;
        var count = Math.Min(settings.ExecutionTree.Count, keyOrder.Count);
        var roots = new List<SemiAutoSkillNode>(count);

        for (var i = 0; i < count; i++)
        {
            roots.Add(SemiAutoSkillNode.FromConfigTree(settings.ExecutionTree[i], keyOrder[i]));
        }

        return roots;
    }

    private static IReadOnlyList<SemiAutoSkillNode> BuildManualRoots(SkillScriptSettings settings)
    {
        var roots = new List<SemiAutoSkillNode>();
        foreach (var mapping in settings.ManualMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.SkillName) ||
                string.IsNullOrWhiteSpace(mapping.Key))
            {
                continue;
            }

            roots.Add(new SemiAutoSkillNode(
                0,
                mapping.SkillName,
                mapping.SkillName,
                mapping.SkillType,
                null,
                mapping.Key));
        }

        return roots;
    }
}
