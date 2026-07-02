using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public static class SpiritmasterAutoSkillReleasePriority
{
    public static SemiAutoSkillReleaseDecision SelectNext(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        SpiritmasterCombatContext? spiritContext,
        DateTimeOffset now)
    {
        if (spiritContext?.CanUseSpiritmasterLogic == false)
        {
            return SemiAutoSkillReleasePriority.SelectNext(plan, state, skills, settings, now);
        }

        var pendingSource = state.PendingChainSourceNode;
        var pendingNext = state.PendingChainNextNode;
        if (pendingSource is not null && pendingNext is not null)
        {
            if (state.IsPendingChainExpired(now))
            {
                return SemiAutoSkillReleaseDecision.ClearPendingChain(pendingNext, "chain_not_confirmed");
            }

            var pendingNextSkill = pendingNext.ResolveSkill(skills);
            if (pendingNextSkill is null)
            {
                return SemiAutoSkillReleaseDecision.None;
            }

            if (RequiresPet(pendingNext, pendingNextSkill) &&
                spiritContext?.HasSummonedPet != true)
            {
                return SemiAutoSkillReleaseDecision.ClearPendingChain(
                    pendingNext,
                    "spiritmaster_pet_missing",
                    pendingNextSkill);
            }

            if (!state.HasPendingChainNextCooldownAdvanced(pendingNextSkill))
            {
                return SemiAutoSkillReleaseDecision.PressChain(pendingNext, pendingNextSkill);
            }

            if (pendingNext.Children.Count == 0)
            {
                return SemiAutoSkillReleaseDecision.ClearPendingChain(pendingNext, "chain_complete", pendingNextSkill);
            }

            foreach (var child in pendingNext.Children)
            {
                var childSkill = child.ResolveSkill(skills);
                if (childSkill is null)
                {
                    continue;
                }

                if (RequiresPet(child, childSkill) &&
                    spiritContext?.HasSummonedPet != true)
                {
                    return SemiAutoSkillReleaseDecision.ClearPendingChain(
                        child,
                        "spiritmaster_pet_missing",
                        childSkill);
                }

                return SemiAutoSkillReleaseDecision.PressChain(child, childSkill);
            }

            return SemiAutoSkillReleaseDecision.None;
        }

        foreach (var root in plan.Roots)
        {
            if (root.IsTrigger || root.IsDp)
            {
                continue;
            }

            var skill = root.ResolveSkill(skills);
            if (skill is null)
            {
                continue;
            }

            if (state.IsUncalibratedUnknownSuppressed(skill, now))
            {
                continue;
            }

            var readiness = SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state);
            if (readiness == SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                continue;
            }

            if (ShouldSkipRoot(root, skill, state, spiritSettings, spiritContext, now))
            {
                continue;
            }

            BeginDotObservationIfNeeded(root, skill, state, spiritSettings, spiritContext, now);
            return SemiAutoSkillReleaseDecision.PressRoot(root, skill);
        }

        return SemiAutoSkillReleaseDecision.None;
    }

    public static bool IsConfiguredDotSkill(
        SemiAutoSkillNode? node,
        SkillSnapshot? skill,
        SpiritmasterSkillSettings spiritSettings)
    {
        if (skill is null)
        {
            return false;
        }

        return spiritSettings.DotSkills.Any(rule =>
            (rule.SkillId != 0 && rule.SkillId == skill.SkillId) ||
            EqualsSkillName(rule.SkillName, skill.Name) ||
            EqualsSkillName(rule.SkillName, skill.DisplayBaseName) ||
            EqualsSkillName(rule.SkillName, node?.Name) ||
            EqualsSkillName(rule.SkillName, node?.BaseName));
    }

    private static bool ShouldSkipRoot(
        SemiAutoSkillNode node,
        SkillSnapshot skill,
        SemiAutoCombatState state,
        SpiritmasterSkillSettings spiritSettings,
        SpiritmasterCombatContext? spiritContext,
        DateTimeOffset now)
    {
        if (RequiresPet(node, skill) && spiritContext?.HasSummonedPet != true)
        {
            return true;
        }

        if (!IsConfiguredDotSkill(node, skill, spiritSettings))
        {
            return false;
        }

        return IsDotActiveOnTarget(skill, state, spiritContext, now);
    }

    private static bool IsDotActiveOnTarget(
        SkillSnapshot skill,
        SemiAutoCombatState state,
        SpiritmasterCombatContext? spiritContext,
        DateTimeOffset now)
    {
        var targetSnapshot = spiritContext?.LockedTargetAbnormalStatuses;
        var targetId = ResolveTargetServerObjectId(targetSnapshot);
        if (targetId == 0)
        {
            return false;
        }

        if (targetSnapshot?.HasAbnormalId(skill.SkillId) == true)
        {
            state.RememberSpiritmasterDotAbnormalId(skill.SkillId, skill.SkillId);
            return true;
        }

        if (state.TryGetSpiritmasterDotAbnormalId(skill.SkillId, out var learnedAbnormalId) &&
            learnedAbnormalId != skill.SkillId &&
            targetSnapshot?.HasAbnormalId(learnedAbnormalId) == true)
        {
            state.RememberSpiritmasterDotAbnormalId(skill.SkillId, learnedAbnormalId);
            return true;
        }

        return false;
    }

    private static void BeginDotObservationIfNeeded(
        SemiAutoSkillNode? node,
        SkillSnapshot? skill,
        SemiAutoCombatState state,
        SpiritmasterSkillSettings spiritSettings,
        SpiritmasterCombatContext? spiritContext,
        DateTimeOffset now)
    {
        if (!IsConfiguredDotSkill(node, skill, spiritSettings) || skill is null)
        {
            return;
        }

        var targetSnapshot = spiritContext?.LockedTargetAbnormalStatuses;
        var targetId = ResolveTargetServerObjectId(targetSnapshot);
        if (targetId == 0)
        {
            return;
        }

        state.RememberSpiritmasterDotAbnormalId(skill.SkillId, skill.SkillId);
        state.BeginSpiritmasterDotObservation(
            skill.SkillId,
            targetId,
            targetSnapshot?.Entries ?? Array.Empty<AbnormalStatusEntrySnapshot>(),
            now + TimeSpan.FromSeconds(3));
    }

    private static uint ResolveTargetServerObjectId(LockedTargetAbnormalStatusSnapshot? targetSnapshot)
    {
        if (targetSnapshot?.Target is not { HasTarget: true } target)
        {
            return 0;
        }

        return target.ServerObjectId != 0
            ? target.ServerObjectId
            : target.TargetEntityId;
    }

    private static bool RequiresPet(SemiAutoSkillNode node, SkillSnapshot skill)
    {
        return StartsWithCommandPrefix(node.Name) ||
               StartsWithCommandPrefix(node.BaseName) ||
               StartsWithCommandPrefix(skill.Name) ||
               StartsWithCommandPrefix(skill.DisplayBaseName);
    }

    private static bool StartsWithCommandPrefix(string? value)
    {
        var text = value?.TrimStart() ?? string.Empty;
        return text.StartsWith("\u547d\u4ee4:", StringComparison.Ordinal) ||
               text.StartsWith("\u547d\u4ee4\uff1a", StringComparison.Ordinal) ||
               text.StartsWith("Command:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqualsSkillName(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }
}
