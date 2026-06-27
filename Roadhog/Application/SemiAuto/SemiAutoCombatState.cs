using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatState
{
    private int? cooldownTickOffsetMs;
    private readonly Dictionary<uint, uint> observedCooldownEndTimes = new();
    private readonly Dictionary<uint, uint> knownCooldownEndTimes = new();
    private DateTimeOffset lastAttackKeyPressedAt = DateTimeOffset.MinValue;
    private uint? lastPressedSkillId;
    private uint lastPressedCooldownEndTime;
    private DateTimeOffset lastPressedCooldownExpiresAt = DateTimeOffset.MinValue;
    private ushort observedTargetEntityId;
    private bool observedTargetWasAliveMonster;

    public SemiAutoSkillNode? PendingChainSourceNode { get; private set; }

    public SemiAutoSkillNode? PendingChainNextNode { get; private set; }

    public uint PendingChainSourceCooldownEndTime { get; private set; }

    public DateTimeOffset PendingChainExpiresAt { get; private set; }

    public bool PendingChainNextPressStarted { get; private set; }

    public uint PendingChainNextCooldownEndTime { get; private set; }

    public bool HasChainWork => PendingChainSourceNode is not null;

    public bool HasCooldownTickCalibration => cooldownTickOffsetMs.HasValue;

    public int? CooldownTickOffsetMs => cooldownTickOffsetMs;

    public DateTimeOffset LastTargetWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastPlanWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSkillWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetStateLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastNoSkillLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastAttackKeyWarningAt { get; set; } = DateTimeOffset.MinValue;

    public bool ObserveTarget(LockedTargetSnapshot target, out ushort killedTargetEntityId)
    {
        killedTargetEntityId = 0;
        if (!target.HasTarget)
        {
            observedTargetEntityId = 0;
            observedTargetWasAliveMonster = false;
            return false;
        }

        var targetEntityId = target.TargetEntityId;
        var killed = observedTargetEntityId == targetEntityId &&
                     observedTargetWasAliveMonster &&
                     target.IsMonster &&
                     !target.IsAlive;

        observedTargetEntityId = targetEntityId;
        observedTargetWasAliveMonster = target.IsMonsterAlive;

        if (!killed)
        {
            return false;
        }

        killedTargetEntityId = targetEntityId;
        return true;
    }

    public bool ShouldPressAttackKey(DateTimeOffset now, TimeSpan interval)
    {
        return lastAttackKeyPressedAt == DateTimeOffset.MinValue ||
               now - lastAttackKeyPressedAt >= interval;
    }

    public void MarkAttackKeyAttempted(DateTimeOffset now)
    {
        lastAttackKeyPressedAt = now;
    }

    public void ResetAttackKeyPressThrottle()
    {
        lastAttackKeyPressedAt = DateTimeOffset.MinValue;
    }

    public void ClearChain()
    {
        ClearPendingChainAdvance();
    }

    public void StartPendingChainAdvance(
        SemiAutoSkillNode sourceNode,
        SemiAutoSkillNode nextNode,
        DateTimeOffset expiresAt,
        uint sourceCooldownEndTime)
    {
        PendingChainSourceNode = sourceNode;
        PendingChainNextNode = nextNode;
        PendingChainSourceCooldownEndTime = sourceCooldownEndTime;
        PendingChainExpiresAt = expiresAt;
        PendingChainNextPressStarted = false;
        PendingChainNextCooldownEndTime = 0;
    }

    public void ClearPendingChainAdvance()
    {
        PendingChainSourceNode = null;
        PendingChainNextNode = null;
        PendingChainSourceCooldownEndTime = 0;
        PendingChainExpiresAt = DateTimeOffset.MinValue;
        PendingChainNextPressStarted = false;
        PendingChainNextCooldownEndTime = 0;
    }

    public bool IsPendingChainExpired(DateTimeOffset now)
    {
        return PendingChainSourceNode is not null && now >= PendingChainExpiresAt;
    }

    public bool HasPendingChainSourceCooldownAdvanced(SkillSnapshot sourceSkill)
    {
        return DidCooldownEndAdvance(PendingChainSourceCooldownEndTime, sourceSkill.CooldownEndTime);
    }

    public bool IsPendingChainNextNode(SemiAutoSkillNode node)
    {
        return PendingChainNextNode is not null &&
               string.Equals(PendingChainNextNode.NodeKey, node.NodeKey, StringComparison.Ordinal);
    }

    public void MarkPendingChainNextPressed(SkillSnapshot skill)
    {
        if (PendingChainNextPressStarted)
        {
            return;
        }

        PendingChainNextPressStarted = true;
        PendingChainNextCooldownEndTime = skill.CooldownEndTime;
    }

    public bool HasPendingChainNextCooldownAdvanced(SkillSnapshot nextSkill)
    {
        return PendingChainNextPressStarted &&
               DidCooldownEndAdvance(PendingChainNextCooldownEndTime, nextSkill.CooldownEndTime);
    }

    public void MarkSkillPressed(
        SkillSnapshot skill,
        DateTimeOffset cooldownConfirmationExpiresAt)
    {
        if (skill.CooldownDuration == 0)
        {
            return;
        }

        observedCooldownEndTimes[skill.SkillId] = skill.CooldownEndTime;
        lastPressedSkillId = skill.SkillId;
        lastPressedCooldownEndTime = skill.CooldownEndTime;
        lastPressedCooldownExpiresAt = cooldownConfirmationExpiresAt;
    }

    public uint GetEffectiveCooldownEndTime(
        SkillSnapshot skill,
        uint gameTick,
        int readyToleranceMs)
    {
        var currentEndTime = skill.CooldownEndTime;
        if (skill.CooldownDuration == 0 ||
            !knownCooldownEndTimes.TryGetValue(skill.SkillId, out var knownEndTime) ||
            knownEndTime == 0)
        {
            return currentEndTime;
        }

        var knownRemainingMs = unchecked((int)(knownEndTime - gameTick));
        if (knownRemainingMs <= readyToleranceMs)
        {
            return currentEndTime;
        }

        if (currentEndTime == 0)
        {
            return knownEndTime;
        }

        var currentRemainingMs = unchecked((int)(currentEndTime - gameTick));
        return knownRemainingMs > currentRemainingMs
            ? knownEndTime
            : currentEndTime;
    }

    public bool TryUpdateCooldownTickCalibration(
        IReadOnlyList<SkillSnapshot> skills,
        uint osTick,
        DateTimeOffset now,
        out SemiAutoCooldownTickCalibration calibration)
    {
        calibration = default;
        SemiAutoCooldownTickCalibration? updatedCalibration = null;

        foreach (var observedSkill in skills)
        {
            if (observedSkill.CooldownDuration == 0)
            {
                continue;
            }

            var hasPrevious = observedCooldownEndTimes.TryGetValue(observedSkill.SkillId, out var previousEndTime);
            observedCooldownEndTimes[observedSkill.SkillId] = observedSkill.CooldownEndTime;
            RememberKnownCooldownEndTime(observedSkill);

            if (!hasPrevious ||
                observedSkill.CooldownEndTime == 0 ||
                !DidCooldownEndAdvance(previousEndTime, observedSkill.CooldownEndTime))
            {
                continue;
            }

            updatedCalibration = ApplyCooldownTickCalibration(observedSkill, osTick);
        }

        if (updatedCalibration.HasValue)
        {
            calibration = updatedCalibration.Value;
            ClearLastPressedSkill();
            return true;
        }

        if (lastPressedSkillId is not uint skillId)
        {
            return false;
        }

        if (now > lastPressedCooldownExpiresAt)
        {
            ClearLastPressedSkill();
            return false;
        }

        var skill = skills.FirstOrDefault(item => item.SkillId == skillId);
        if (skill is null ||
            skill.CooldownDuration == 0 ||
            skill.CooldownEndTime == 0 ||
            !DidCooldownEndAdvance(lastPressedCooldownEndTime, skill.CooldownEndTime))
        {
            return false;
        }

        calibration = ApplyCooldownTickCalibration(skill, osTick);
        ClearLastPressedSkill();
        return true;
    }

    public uint EstimateGameTick(uint osTick)
    {
        return unchecked(osTick + (uint)(cooldownTickOffsetMs ?? 0));
    }

    private void ClearLastPressedSkill()
    {
        lastPressedSkillId = null;
        lastPressedCooldownEndTime = 0;
        lastPressedCooldownExpiresAt = DateTimeOffset.MinValue;
    }

    private void RememberKnownCooldownEndTime(SkillSnapshot skill)
    {
        if (skill.CooldownDuration == 0 || skill.CooldownEndTime == 0)
        {
            return;
        }

        if (!knownCooldownEndTimes.TryGetValue(skill.SkillId, out var knownEndTime) ||
            knownEndTime == 0 ||
            DidCooldownEndAdvance(knownEndTime, skill.CooldownEndTime))
        {
            knownCooldownEndTimes[skill.SkillId] = skill.CooldownEndTime;
        }
    }

    private SemiAutoCooldownTickCalibration ApplyCooldownTickCalibration(
        SkillSnapshot skill,
        uint osTick)
    {
        var startTick = unchecked(skill.CooldownEndTime - skill.CooldownDuration);
        var offsetMs = unchecked((int)(startTick - osTick));
        cooldownTickOffsetMs = offsetMs;

        return new SemiAutoCooldownTickCalibration(
            skill.SkillId,
            skill.Name,
            osTick,
            skill.CooldownDuration,
            skill.CooldownEndTime,
            startTick,
            offsetMs);
    }

    private static bool DidCooldownEndAdvance(uint previousEndTime, uint currentEndTime)
    {
        return currentEndTime != 0 &&
               currentEndTime != previousEndTime &&
               unchecked((int)(currentEndTime - previousEndTime)) > 0;
    }
}

public readonly record struct SemiAutoCooldownTickCalibration(
    uint SkillId,
    string SkillName,
    uint OsTick,
    uint CooldownDuration,
    uint CooldownEndTime,
    uint CooldownStartTick,
    int OffsetMs);
