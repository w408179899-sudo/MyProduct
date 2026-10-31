using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Immutable, explicitly configured skill tree and startup key bindings for the new release mode.</summary>
public sealed class QuickbarSkillPlan
{

    private QuickbarSkillPlan(int page, IReadOnlyList<QuickbarSkillNode> roots, bool triggerConditionSkillsPreemptChain)
    {
        Page = page;
        Roots = roots;
        TriggerConditionSkillsPreemptChain = triggerConditionSkillsPreemptChain;
        SkillReadIds = Flatten(roots).Select(node => node.SkillId).Distinct().ToArray();
    }

    public int Page { get; }
    public IReadOnlyList<QuickbarSkillNode> Roots { get; }
    public bool TriggerConditionSkillsPreemptChain { get; }
    public IReadOnlyList<uint> SkillReadIds { get; }
    public bool HasCombatActions => Roots.Count > 0;

    public static QuickbarSkillPlan FromSettings(
        QuickbarSkillScriptSettings settings,
        SkillKeyBindings bindings,
        Action<string>? missing = null)
    {
        QuickbarSkillNode? Build(SkillConfigNode config, QuickbarSkillNode? parent, SemiAutoSkillNode? bindingParent, string path)
        {
            var bound = bindings.ResolveChain(config.SkillId, config.Name, bindingParent);
            if (bound is null || !bindings.TryLocateKey(bound.Key, out var bar, out var slot))
            {
                missing?.Invoke(config.Name + "：未放入当前模式支持的栏位，也没有可确认的连续技栏位");
                return null;
            }

            // A dynamically substituted child keeps its source slot's base identity. A child
            // with its own shortcut uses its own base identity and can never press another skill.
            var sameSlot = parent is not null && parent.Bar == bar && parent.Slot == slot;
            var anchor = sameSlot ? parent!.BaseSkillId : bound.SkillId;
            var children = new List<QuickbarSkillNode>();
            var bindingNode = new SemiAutoSkillNode(bound.SkillId, bound.Name, config.BaseName, config.Type, config.ChainTimeMs, bound.Key, bindingParent);
            var node = new QuickbarSkillNode(bound.SkillId, bound.Name, bound.Key, bar, slot, anchor, path, children.AsReadOnly(),
                bindings.GetXmlChainTimeMs(bound.SkillId), bindingNode.IsTrigger || bindingNode.IsCondition ||
                bindings.IsTriggerOrConditionSkill(bound.SkillId));
            for (var index = 0; index < (config.Children?.Count ?? 0); index++)
                if (config.Children![index] is { } child && Build(child, node, bindingNode, path + "/" + index) is { } next)
                    children.Add(next);
            return node;
        }

        var roots = new List<QuickbarSkillNode>();
        for (var index = 0; index < (settings.ExecutionTree?.Count ?? 0); index++)
            if (settings.ExecutionTree![index] is { } config && Build(config, null, null, index.ToString()) is { } node)
                roots.Add(node);
        return new QuickbarSkillPlan(bindings.Page, roots.AsReadOnly(), settings.TriggerConditionSkillsPreemptChain);
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

public sealed record QuickbarSkillNode(
    uint SkillId,
    string Name,
    string Key,
    SkillQuickbar Bar,
    int Slot,
    uint BaseSkillId,
    string NodeKey,
    IReadOnlyList<QuickbarSkillNode> Children,
    int? ChainTimeMs = null,
    bool IsTriggerOrCondition = false);
