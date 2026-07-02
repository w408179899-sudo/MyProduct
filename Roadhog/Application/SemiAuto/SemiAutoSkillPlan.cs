using Roadhog.Core.Accounts;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoSkillPlan
{
    private SemiAutoSkillPlan(
        IReadOnlyList<SemiAutoSkillNode> roots,
        IReadOnlyList<SemiAutoSkillNode> triggerPrefixRoots,
        SemiAutoSkillNode? openingSkill,
        bool usesSpiritmasterAutoLogic)
    {
        Roots = roots;
        TriggerPrefixRoots = triggerPrefixRoots;
        OpeningSkill = openingSkill;
        UsesSpiritmasterAutoLogic = usesSpiritmasterAutoLogic;
    }

    public IReadOnlyList<SemiAutoSkillNode> Roots { get; }

    public IReadOnlyList<SemiAutoSkillNode> TriggerPrefixRoots { get; }

    public SemiAutoSkillNode? OpeningSkill { get; }

    public bool UsesSpiritmasterAutoLogic { get; }

    public IReadOnlyList<uint> SkillReadIds { get; private init; } = Array.Empty<uint>();

    public bool RequiresFullSkillRead { get; private init; }

    public bool HasExecutableSkills => Roots.Any(root => !root.IsTrigger && !root.IsDp);

    public bool HasOpeningSkill => OpeningSkill is not null;

    public bool HasCombatActions => HasExecutableSkills || HasOpeningSkill;

    public static SemiAutoSkillPlan FromSettings(SkillScriptSettings settings)
    {
        var usesSpiritmasterAutoLogic =
            settings.Mode == SkillConfigurationMode.Auto &&
            settings.SpiritmasterAutoSkillLogicEnabled;

        var roots = settings.Mode switch
        {
            SkillConfigurationMode.ManualMapping => BuildManualRoots(settings),
            SkillConfigurationMode.SystemClassification => BuildSystemRoots(settings),
            _ => usesSpiritmasterAutoLogic
                ? BuildSpiritmasterAutoRoots(settings)
                : BuildAutoRoots(settings)
        };

        var triggerPrefix = BuildTriggerPrefixRoots(roots, settings.TriggerPrefixMode);
        var openingSkill = BuildOpeningSkill(settings);

        return new SemiAutoSkillPlan(roots, triggerPrefix, openingSkill, usesSpiritmasterAutoLogic)
        {
            SkillReadIds = BuildSkillReadIds(
                roots,
                openingSkill,
                settings,
                usesSpiritmasterAutoLogic,
                out var requiresFullSkillRead),
            RequiresFullSkillRead = requiresFullSkillRead
        };
    }

    private static IReadOnlyList<uint> BuildSkillReadIds(
        IReadOnlyList<SemiAutoSkillNode> roots,
        SemiAutoSkillNode? openingSkill,
        SkillScriptSettings settings,
        bool usesSpiritmasterAutoLogic,
        out bool requiresFullSkillRead)
    {
        var needsFullSkillRead = false;
        var ids = new HashSet<uint>();
        foreach (var node in FlattenNodes(roots))
        {
            if (node.IsTrigger || node.IsDp)
            {
                continue;
            }

            if (node.SkillId == 0)
            {
                needsFullSkillRead = true;
                continue;
            }

            ids.Add(node.SkillId);
        }

        if (openingSkill is not null)
        {
            if (openingSkill.SkillId == 0)
            {
                needsFullSkillRead = true;
            }
            else
            {
                ids.Add(openingSkill.SkillId);
            }
        }

        if (usesSpiritmasterAutoLogic)
        {
            foreach (var item in settings.Spiritmaster.DotSkills)
            {
                if (item.SkillId != 0)
                {
                    ids.Add(item.SkillId);
                }
                else if (!string.IsNullOrWhiteSpace(item.SkillName))
                {
                    needsFullSkillRead = true;
                }
            }

            foreach (var item in settings.Spiritmaster.PetHpMaintenanceRules)
            {
                if (item.SkillId != 0)
                {
                    ids.Add(item.SkillId);
                }
                else if (!string.IsNullOrWhiteSpace(item.SkillName))
                {
                    needsFullSkillRead = true;
                }
            }

            foreach (var item in settings.Spiritmaster.PetBuffRules)
            {
                if (item.SkillId != 0)
                {
                    ids.Add(item.SkillId);
                }
                else if (!string.IsNullOrWhiteSpace(item.SkillName))
                {
                    needsFullSkillRead = true;
                }
            }
        }

        requiresFullSkillRead = needsFullSkillRead;
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
        return BuildConfiguredRoots(settings.ExecutionTree, settings.KeyOrder);
    }

    private static IReadOnlyList<SemiAutoSkillNode> BuildSpiritmasterAutoRoots(SkillScriptSettings settings)
    {
        return BuildConfiguredRoots(settings.ExecutionTree, settings.KeyOrder);
    }

    private static IReadOnlyList<SemiAutoSkillNode> BuildSystemRoots(SkillScriptSettings settings)
    {
        return BuildConfiguredRoots(settings.SystemExecutionTree, settings.KeyOrder);
    }

    private static IReadOnlyList<SemiAutoSkillNode> BuildConfiguredRoots(
        IReadOnlyList<SkillConfigNode> executionTree,
        IReadOnlyList<string> configuredKeyOrder)
    {
        var keyOrder = configuredKeyOrder.Count == 0
            ? SkillScriptSettings.DefaultKeyOrder()
            : configuredKeyOrder;
        var count = Math.Min(executionTree.Count, keyOrder.Count);
        var roots = new List<SemiAutoSkillNode>(count);

        for (var i = 0; i < count; i++)
        {
            roots.Add(SemiAutoSkillNode.FromConfigTree(executionTree[i], keyOrder[i]));
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

    private static SemiAutoSkillNode? BuildOpeningSkill(SkillScriptSettings settings)
    {
        var config = settings.OpeningSkill;
        if (config is null ||
            !config.Enabled ||
            string.IsNullOrWhiteSpace(config.Key) ||
            (config.SkillId == 0 && string.IsNullOrWhiteSpace(config.SkillName)))
        {
            return null;
        }

        var name = config.SkillName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Skill " + config.SkillId;
        }

        return new SemiAutoSkillNode(
            config.SkillId,
            name,
            name,
            "主动技能",
            null,
            config.Key.Trim());
    }
}
