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

    public IReadOnlyList<uint> SkillReadIds { get; private init; } = Array.Empty<uint>();

    public bool RequiresFullSkillRead { get; private init; }

    public bool HasExecutableSkills => Roots.Any(root => !root.IsTrigger && !root.IsDp);

    public static SemiAutoSkillPlan FromSettings(SkillScriptSettings settings)
    {
        var roots = settings.Mode == SkillConfigurationMode.Auto
            ? BuildAutoRoots(settings)
            : BuildManualRoots(settings);

        var triggerPrefix = BuildTriggerPrefixRoots(roots, settings.TriggerPrefixMode);

        return new SemiAutoSkillPlan(roots, triggerPrefix)
        {
            SkillReadIds = BuildSkillReadIds(roots, out var requiresFullSkillRead),
            RequiresFullSkillRead = requiresFullSkillRead
        };
    }

    private static IReadOnlyList<uint> BuildSkillReadIds(
        IReadOnlyList<SemiAutoSkillNode> roots,
        out bool requiresFullSkillRead)
    {
        requiresFullSkillRead = false;
        var ids = new HashSet<uint>();
        foreach (var node in FlattenNodes(roots))
        {
            if (node.IsTrigger || node.IsDp)
            {
                continue;
            }

            if (node.SkillId == 0)
            {
                requiresFullSkillRead = true;
                continue;
            }

            ids.Add(node.SkillId);
        }

        return ids.OrderBy(id => id).ToArray();
    }

    private static IEnumerable<SemiAutoSkillNode> FlattenNodes(IEnumerable<SemiAutoSkillNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenNodes(node.Children))
            {
                yield return child;
            }
        }
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

    private static IReadOnlyList<SemiAutoSkillNode> BuildTriggerPrefixRoots(
        IReadOnlyList<SemiAutoSkillNode> roots,
        string? mode)
    {
        if (string.Equals(mode, "AllTriggerSkills", StringComparison.OrdinalIgnoreCase))
        {
            return roots.Where(root => root.IsTrigger).ToArray();
        }

        var firstTriggerIndex = -1;
        for (var i = 0; i < roots.Count; i++)
        {
            if (roots[i].IsTrigger)
            {
                firstTriggerIndex = i;
                break;
            }
        }

        if (firstTriggerIndex < 0)
        {
            return Array.Empty<SemiAutoSkillNode>();
        }

        return roots
            .Skip(firstTriggerIndex)
            .TakeWhile(root => root.IsTrigger)
            .ToArray();
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
