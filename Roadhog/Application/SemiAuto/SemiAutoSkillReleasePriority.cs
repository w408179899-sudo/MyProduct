using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public static class SemiAutoSkillReleasePriority
{
    public const int CooldownReadyToleranceMs = 80;
    private static readonly IReadOnlyDictionary<string, uint[]> TargetConditionAbnormalIds =
        new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stumble"] = new uint[] { 8218, 8635, 8636, 8637, 8676 },
            ["OpenAerial"] = new uint[] { 8224, 8678, 19552 },
            ["Stun"] = new uint[]
            {
                1499, 1500, 1501, 1521, 1939, 1940, 8225, 8255, 8315, 8361, 8383, 8411,
                8483, 8535, 8648, 11904, 18691, 19221, 19246, 19250, 19260, 19781, 19792,
                19808, 19870
            },
            ["Bind"] = new uint[] { 1077, 1119, 1277, 1343, 1746, 17479 },
            ["Blind"] = new uint[]
            {
                802, 863, 1056, 8259, 8539, 8575, 8652, 8714, 11514, 11572, 11574, 16564,
                16633, 16789, 16914, 17008, 17123, 17124, 17173, 17356, 17357, 17395,
                17410, 17479, 17576, 17684, 17697, 18625, 18698, 18773, 18977, 19058,
                19169
            },
            ["Spin"] = new uint[] { 8223, 8677 },
            ["Stagger"] = new uint[] { 8217, 8632, 8633, 8634, 8675 }
        };

    public static SemiAutoSkillReleaseDecision SelectNext(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoScriptSettings settings,
        DateTimeOffset now,
        LockedTargetAbnormalStatusSnapshot? targetAbnormalStatuses = null)
    {
        var conditionDecision = SelectTargetConditionSkill(
            plan,
            state,
            skills,
            targetAbnormalStatuses,
            now,
            settings.ConditionSkillPreemptsChain || !state.HasChainWork);
        if (conditionDecision.Kind != SemiAutoSkillReleaseDecisionKind.None)
        {
            return conditionDecision;
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

            if (IsTargetConditionSkill(root, skill))
            {
                continue;
            }

            if (state.IsUncalibratedUnknownSuppressed(skill, now))
            {
                continue;
            }

            var readiness = GetActionCooldownReadiness(skill, state);
            if (readiness == SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                continue;
            }

            return SemiAutoSkillReleaseDecision.PressRoot(root, skill);
        }

        return SemiAutoSkillReleaseDecision.None;
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
                if (state.IsUncalibratedUnknownSuppressed(skill, DateTimeOffset.Now))
                {
                    reason = "unverified_suppressed_cooldown_end=" + skill.CooldownEndTime +
                             "/duration=" + skill.CooldownDuration;
                    reasons.Add(root.Name + "[" + root.Type + "]@" + root.Key + ":" + reason);
                    continue;
                }

                var readiness = GetActionCooldownReadiness(skill, state);
                if (readiness == SemiAutoSkillCooldownReadiness.CoolingDown)
                {
                    reason = FormatCooldownReason(skill, state);
                }
                else if (IsTargetConditionSkill(root, skill))
                {
                    reason = "condition-waiting_status=" + skill.XmlTargetValidStatuses;
                }
                else if (readiness == SemiAutoSkillCooldownReadiness.Unknown)
                {
                    reason = "ready_unverified_cooldown_end=" + skill.CooldownEndTime +
                             "/duration=" + skill.CooldownDuration;
                }
            }

            reasons.Add(root.Name + "[" + root.Type + "]@" + root.Key + ":" + reason);
        }

        return string.Join(" | ", reasons);
    }

    public static bool HasRunnableTargetConditionSkill(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills)
    {
        var now = DateTimeOffset.Now;
        foreach (var root in plan.Roots)
        {
            if (root.IsDp)
            {
                continue;
            }

            var skill = root.ResolveSkill(skills);
            if (skill is null ||
                !IsTargetConditionSkill(root, skill) ||
                state.IsUncalibratedUnknownSuppressed(skill, now))
            {
                continue;
            }

            if (GetActionCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTargetConditionSkill(SkillSnapshot skill)
    {
        return !string.IsNullOrWhiteSpace(skill.XmlTargetValidStatuses);
    }

    public static bool IsTargetConditionSkill(SemiAutoSkillNode node, SkillSnapshot skill)
    {
        return node.IsCondition || IsTargetConditionSkill(skill);
    }

    private static SemiAutoSkillReleaseDecision SelectTargetConditionSkill(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        LockedTargetAbnormalStatusSnapshot? targetAbnormalStatuses,
        DateTimeOffset now,
        bool allowWhileChainPending)
    {
        if (!allowWhileChainPending ||
            targetAbnormalStatuses is not { IsMonsterAlive: true } ||
            targetAbnormalStatuses.Entries.Count == 0)
        {
            return SemiAutoSkillReleaseDecision.None;
        }

        var targetAbnormalIds = targetAbnormalStatuses.Entries
            .Select(entry => entry.AbnormalId)
            .Where(id => id != 0)
            .ToHashSet();
        if (targetAbnormalIds.Count == 0)
        {
            return SemiAutoSkillReleaseDecision.None;
        }

        foreach (var root in plan.Roots)
        {
            if (root.IsDp)
            {
                continue;
            }

            var skill = root.ResolveSkill(skills);
            if (skill is null || !IsTargetConditionSkill(root, skill))
            {
                continue;
            }

            if (state.IsUncalibratedUnknownSuppressed(skill, now))
            {
                continue;
            }

            var readiness = GetActionCooldownReadiness(skill, state);
            if (readiness == SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                continue;
            }

            if (TryMatchTargetCondition(skill, targetAbnormalIds, out var status, out var abnormalId))
            {
                return SemiAutoSkillReleaseDecision.PressCondition(root, skill, status, abnormalId);
            }
        }

        return SemiAutoSkillReleaseDecision.None;
    }

    private static bool TryMatchTargetCondition(
        SkillSnapshot skill,
        HashSet<uint> targetAbnormalIds,
        out string status,
        out uint abnormalId)
    {
        foreach (var candidateStatus in SplitTargetValidStatuses(skill.XmlTargetValidStatuses))
        {
            if (!TargetConditionAbnormalIds.TryGetValue(candidateStatus, out var mappedAbnormalIds))
            {
                continue;
            }

            foreach (var mappedAbnormalId in mappedAbnormalIds)
            {
                if (targetAbnormalIds.Contains(mappedAbnormalId))
                {
                    status = candidateStatus;
                    abnormalId = mappedAbnormalId;
                    return true;
                }
            }
        }

        status = string.Empty;
        abnormalId = 0;
        return false;
    }

    private static IEnumerable<string> SplitTargetValidStatuses(string? statuses)
    {
        if (string.IsNullOrWhiteSpace(statuses))
        {
            yield break;
        }

        foreach (var status in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(status))
            {
                yield return status;
            }
        }
    }

    public static bool IsSkillReady(SkillSnapshot skill, SemiAutoCombatState state)
    {
        return GetActionCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown;
    }

    public static SemiAutoSkillCooldownReadiness GetActionCooldownReadiness(
        SkillSnapshot skill,
        SemiAutoCombatState state)
    {
        var readiness = GetCooldownReadiness(skill, state);
        if (readiness != SemiAutoSkillCooldownReadiness.Unknown ||
            state.HasCooldownTickCalibration ||
            skill.CooldownEndTime == 0)
        {
            return readiness;
        }

        var rawRemainingMs = unchecked((int)(skill.CooldownEndTime - CurrentOsTick()));
        if (rawRemainingMs <= CooldownReadyToleranceMs)
        {
            return SemiAutoSkillCooldownReadiness.Ready;
        }

        var maxExpectedRemainingMs = (long)skill.CooldownDuration + 60_000L;
        return skill.CooldownDuration > 0 && rawRemainingMs <= maxExpectedRemainingMs
            ? SemiAutoSkillCooldownReadiness.CoolingDown
            : SemiAutoSkillCooldownReadiness.Unknown;
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
    PressCondition,
    PressRoot
}

public sealed record SemiAutoSkillReleaseDecision(
    SemiAutoSkillReleaseDecisionKind Kind,
    SemiAutoSkillNode? Node,
    SkillSnapshot? Skill,
    string Reason,
    string? ConditionStatus = null,
    uint ConditionAbnormalId = 0)
{
    public static readonly SemiAutoSkillReleaseDecision None = new(
        SemiAutoSkillReleaseDecisionKind.None,
        null,
        null,
        "none");

    public bool ShouldPress => Kind is SemiAutoSkillReleaseDecisionKind.PressChain
        or SemiAutoSkillReleaseDecisionKind.PressCondition
        or SemiAutoSkillReleaseDecisionKind.PressRoot;

    public bool BlocksFallbackThisTick => Kind is SemiAutoSkillReleaseDecisionKind.ClearPendingChain
        or SemiAutoSkillReleaseDecisionKind.PressChain
        or SemiAutoSkillReleaseDecisionKind.PressCondition;

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

    public static SemiAutoSkillReleaseDecision PressCondition(
        SemiAutoSkillNode node,
        SkillSnapshot skill,
        string conditionStatus,
        uint conditionAbnormalId)
    {
        return new SemiAutoSkillReleaseDecision(
            SemiAutoSkillReleaseDecisionKind.PressCondition,
            node,
            skill,
            "condition",
            conditionStatus,
            conditionAbnormalId);
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
