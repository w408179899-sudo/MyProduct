using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public enum QuickbarSkillDecisionKind { None, WaitForChain, PressRoot, PressChain, PressClockBootstrap }

public sealed record QuickbarSkillReleaseDecision(QuickbarSkillDecisionKind Kind, QuickbarSkillNode? Node = null)
{
    public static QuickbarSkillReleaseDecision None { get; } = new(QuickbarSkillDecisionKind.None);
}

/// <summary>
/// Pure candidate selection. Special opportunities require the client-provided
/// CanUse; ordinary roots may receive externally calibrated cooldown readiness.
/// </summary>
public static class QuickbarSkillReleasePriority
{
    public static QuickbarSkillReleaseDecision SelectNext(
        QuickbarSkillPlan plan,
        QuickbarSkillCombatState state,
        SkillAvailabilitySnapshot availability,
        DateTimeOffset now,
        IReadOnlySet<uint>? ordinaryReadyIds = null,
        IReadOnlySet<uint>? coolingSkillIds = null,
        IReadOnlySet<uint>? suppressedRootSkillIds = null,
        IReadOnlySet<uint>? suppressedSkillIds = null)
    {
        if (availability.Page != plan.Page)
            return QuickbarSkillReleaseDecision.None;

        bool IsLit(QuickbarSkillNode node) => GetMatchingSlot(node, availability) is { CanUse: true } &&
            suppressedSkillIds?.Contains(node.SkillId) != true &&
            coolingSkillIds?.Contains(node.SkillId) != true && !state.IsRepeatBlocked(node, now);

        // CD-first handoffs carry their own current stage while the precise
        // actor record may still describe the previously confirmed predecessor.
        if ((state.ChainTransition?.Source ?? state.ActiveChainSource) is { } source)
        {
            foreach (var child in source.Children)
                if (IsLit(child))
                    return new(QuickbarSkillDecisionKind.PressChain, child);
        }

        // The client may replace the displayed id before the prior key is confirmed.
        // Only configured, exactly bound continuations can take over that key.
        foreach (var child in Descendants(plan.Roots))
            if (IsLit(child)) return new(QuickbarSkillDecisionKind.PressChain, child);

        // A successful predecessor can enter CD before the client substitutes
        // and lights its next stage. Keep that bounded gap free of other attacks.
        if (state.IsChainTransitionWaiting(now) && state.ChainTransition is { } transition &&
            !transition.Source.Children.All(child => coolingSkillIds?.Contains(child.SkillId) == true ||
                suppressedSkillIds?.Contains(child.SkillId) == true))
            return new(QuickbarSkillDecisionKind.WaitForChain);

        // Once an action is chosen, retry that still-open action until it is
        // accepted, closes, enters CD, or reaches its finite failure budget. A
        // retry that is not due must not hand its baseline to a later root.
        if (state.PendingAction is { RetryStopped: false } pending && coolingSkillIds?.Contains(pending.Node.SkillId) != true &&
            suppressedSkillIds?.Contains(pending.Node.SkillId) != true &&
            (pending.Node.NodeKey.Contains('/') || suppressedRootSkillIds?.Contains(pending.Node.SkillId) != true) &&
            (GetMatchingSlot(pending.Node, availability) is { CanUse: true } ||
             (!pending.Node.NodeKey.Contains('/') && IsOrdinaryRootReady(pending.Node, availability, ordinaryReadyIds))))
        {
            if (state.IsRepeatBlocked(pending.Node, now)) return QuickbarSkillReleaseDecision.None;
            return new(pending.Node.NodeKey.Contains('/') ? QuickbarSkillDecisionKind.PressChain : QuickbarSkillDecisionKind.PressRoot, pending.Node);
        }

        // Confirmed zero-CD roots yield one round to other eligible roots. A new
        // round starts only when all currently eligible roots have had their turn.
        foreach (var includeYielded in new[] { false, true })
            foreach (var root in plan.Roots)
                if ((includeYielded || !state.HasYieldedRoot(root)) &&
                    suppressedRootSkillIds?.Contains(root.SkillId) != true &&
                    suppressedSkillIds?.Contains(root.SkillId) != true &&
                    coolingSkillIds?.Contains(root.SkillId) != true &&
                    (GetMatchingSlot(root, availability) is { CanUse: true } || IsOrdinaryRootReady(root, availability, ordinaryReadyIds)) &&
                    !state.IsRepeatBlocked(root, now))
                    return new(QuickbarSkillDecisionKind.PressRoot, root);
        return QuickbarSkillReleaseDecision.None;
    }

    public static SkillAvailabilitySlotSnapshot? GetMatchingSlot(QuickbarSkillNode node, SkillAvailabilitySnapshot availability) =>
        availability.Slots.FirstOrDefault(slot => slot.Bar == node.Bar && slot.Slot == node.Slot &&
            slot.ContentType == 21 && slot.BaseSkillId == node.BaseSkillId && slot.EffectiveSkillId == node.SkillId);

    private static bool IsOrdinaryRootReady(QuickbarSkillNode node, SkillAvailabilitySnapshot availability, IReadOnlySet<uint>? ordinaryReadyIds) =>
        ordinaryReadyIds?.Contains(node.SkillId) == true && availability.UnsupportedSkillIds?.Contains(node.SkillId) == true &&
        availability.BindingSlots?.Any(slot => slot.Bar == node.Bar && slot.Slot == node.Slot &&
            slot.ContentType == 21 && slot.BaseSkillId == node.BaseSkillId && slot.EffectiveSkillId == node.SkillId) == true;

    private static IEnumerable<QuickbarSkillNode> Descendants(IEnumerable<QuickbarSkillNode> roots)
    {
        foreach (var root in roots)
            foreach (var child in root.Children)
            {
                yield return child;
                foreach (var next in Descendants(new[] { child })) yield return next;
            }
    }
}
