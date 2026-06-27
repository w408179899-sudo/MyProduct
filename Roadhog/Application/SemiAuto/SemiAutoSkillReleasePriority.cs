using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public static class SemiAutoSkillReleasePriority
{
    public const int CooldownReadyToleranceMs = 80;

    public static SemiAutoSkillReleaseDecision SelectNext(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings,
        DateTimeOffset now)
    {
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

                return SemiAutoSkillReleaseDecision.PressChain(child, childSkill);
            }

            return SemiAutoSkillReleaseDecision.None;
        }

        SemiAutoSkillReleaseDecision? firstUnknownRoot = null;
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

            var readiness = GetCooldownReadiness(skill, state);
            if (readiness == SemiAutoSkillCooldownReadiness.Ready)
            {
                return SemiAutoSkillReleaseDecision.PressRoot(root, skill);
            }

            if (readiness == SemiAutoSkillCooldownReadiness.Unknown)
            {
                if (!state.IsUncalibratedUnknownSuppressed(skill, now))
                {
                    firstUnknownRoot ??= SemiAutoSkillReleaseDecision.PressRoot(root, skill);
                }
            }
        }

        return firstUnknownRoot ?? SemiAutoSkillReleaseDecision.None;
    }

    public static string BuildNoReadyReasons(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings)
    {
        var reasons = new List<string>();
        var triggerPrefixNodeKeys = plan.TriggerPrefixRoots
            .Select(root => root.NodeKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var root in plan.Roots)
        {
            var skill = root.ResolveSkill(skills);
            var reason = "ready";
            if (root.IsTrigger)
            {
                reason = triggerPrefixNodeKeys.Contains(root.NodeKey)
                    ? "trigger-prefix"
                    : "trigger-skip";
            }
            else if (root.IsDp)
            {
                reason = "dp-skip";
            }
            else if (skill is null)
            {
                reason = "missing";
            }
            else
            {
                var readiness = GetCooldownReadiness(skill, state);
                if (readiness == SemiAutoSkillCooldownReadiness.CoolingDown)
                {
                    reason = FormatCooldownReason(skill, state);
                }
                else if (readiness == SemiAutoSkillCooldownReadiness.Unknown)
                {
                    reason = state.IsUncalibratedUnknownSuppressed(skill, DateTimeOffset.Now)
                        ? "unverified_suppressed_cooldown_end=" + skill.CooldownEndTime +
                          "/duration=" + skill.CooldownDuration
                        : "ready_unverified_cooldown_end=" + skill.CooldownEndTime +
                          "/duration=" + skill.CooldownDuration;
                }
            }

            reasons.Add(root.Name + "[" + root.Type + "]@" + root.Key + ":" + reason);
        }

        return string.Join(" | ", reasons);
    }

    public static bool IsSkillReady(SkillSnapshot skill, SemiAutoCombatState state)
    {
        return GetCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown;
    }

    public static SemiAutoSkillCooldownReadiness GetCooldownReadiness(
        SkillSnapshot skill,
        SemiAutoCombatState state)
    {
        if (!state.HasCooldownTickCalibration)
        {
            if (skill.CooldownEndTime == 0)
            {
                return SemiAutoSkillCooldownReadiness.Ready;
            }

            return SemiAutoSkillCooldownReadiness.Unknown;
        }

        var gameTick = state.EstimateGameTick(CurrentOsTick());
        var cooldownEndTime = state.GetEffectiveCooldownEndTime(skill, gameTick, CooldownReadyToleranceMs);
        if (cooldownEndTime == 0)
        {
            return SemiAutoSkillCooldownReadiness.Ready;
        }

        var remainingMs = unchecked((int)(cooldownEndTime - gameTick));
        return remainingMs > CooldownReadyToleranceMs
            ? SemiAutoSkillCooldownReadiness.CoolingDown
            : SemiAutoSkillCooldownReadiness.Ready;
    }

    public static string FormatCooldownReason(SkillSnapshot skill, SemiAutoCombatState state)
    {
        if (!state.HasCooldownTickCalibration)
        {
            return "cooldown_unknown_end=" + skill.CooldownEndTime +
                   "/duration=" + skill.CooldownDuration;
        }

        var osTick = CurrentOsTick();
        var gameTick = state.EstimateGameTick(osTick);
        var cooldownEndTime = state.GetEffectiveCooldownEndTime(skill, gameTick, CooldownReadyToleranceMs);
        var remainingMs = Math.Max(0, unchecked((int)(cooldownEndTime - gameTick)));
        return "cooldown_end=" + cooldownEndTime +
               "/raw_end=" + skill.CooldownEndTime +
               "/duration=" + skill.CooldownDuration +
               "/game_tick=" + gameTick +
               "/os_tick=" + osTick +
               "/offset_ms=" + state.CooldownTickOffsetMs +
               "/ready_tolerance_ms=" + CooldownReadyToleranceMs +
               "/remaining_ms=" + remainingMs;
    }

    private static uint CurrentOsTick()
    {
        return unchecked((uint)Environment.TickCount64);
    }
}

public enum SemiAutoSkillCooldownReadiness
{
    Ready,
    Unknown,
    CoolingDown
}

public enum SemiAutoSkillReleaseDecisionKind
{
    None,
    ClearPendingChain,
    PressChain,
    PressRoot
}

public sealed record SemiAutoSkillReleaseDecision(
    SemiAutoSkillReleaseDecisionKind Kind,
    SemiAutoSkillNode? Node,
    SkillSnapshot? Skill,
    string Reason)
{
    public static readonly SemiAutoSkillReleaseDecision None = new(
        SemiAutoSkillReleaseDecisionKind.None,
        null,
        null,
        "none");

    public bool ShouldPress => Kind is SemiAutoSkillReleaseDecisionKind.PressChain or SemiAutoSkillReleaseDecisionKind.PressRoot;

    public bool BlocksFallbackThisTick => Kind is SemiAutoSkillReleaseDecisionKind.ClearPendingChain or SemiAutoSkillReleaseDecisionKind.PressChain;

    public static SemiAutoSkillReleaseDecision ClearPendingChain(
        SemiAutoSkillNode node,
        string reason,
        SkillSnapshot? skill = null)
    {
        return new SemiAutoSkillReleaseDecision(
            SemiAutoSkillReleaseDecisionKind.ClearPendingChain,
            node,
            skill,
            reason);
    }

    public static SemiAutoSkillReleaseDecision PressChain(SemiAutoSkillNode node, SkillSnapshot skill)
    {
        return new SemiAutoSkillReleaseDecision(
            SemiAutoSkillReleaseDecisionKind.PressChain,
            node,
            skill,
            "chain");
    }

    public static SemiAutoSkillReleaseDecision PressRoot(SemiAutoSkillNode node, SkillSnapshot skill)
    {
        return new SemiAutoSkillReleaseDecision(
            SemiAutoSkillReleaseDecisionKind.PressRoot,
            node,
            skill,
            "root");
    }
}
