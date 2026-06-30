using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatState
{
    private int? cooldownTickOffsetMs;
    private readonly Dictionary<uint, uint> observedCooldownEndTimes = new();
    private readonly Dictionary<uint, uint> knownCooldownEndTimes = new();
    private readonly Dictionary<uint, DateTimeOffset> uncalibratedUnknownSuppressUntil = new();
    private readonly Dictionary<string, DateTimeOffset> maintenanceKeyPressedAt = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset lastAttackKeyPressedAt = DateTimeOffset.MinValue;
    private uint? lastPressedSkillId;
    private uint lastPressedCooldownEndTime;
    private DateTimeOffset lastPressedCooldownExpiresAt = DateTimeOffset.MinValue;
    private ushort openingAttackTargetEntityId;
    private ushort openingSkillTargetEntityId;
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

    public DateTimeOffset LastMaintenanceWarningAt { get; set; } = DateTimeOffset.MinValue;

    public bool IsMaintenanceResting { get; private set; }

    public bool MaintenanceRestingForHp { get; private set; }

    public bool MaintenanceRestingForMp { get; private set; }

    public bool ObserveTarget(LockedTargetSnapshot target, out ushort killedTargetEntityId)
    {
        killedTargetEntityId = 0;
        if (!target.HasTarget)
        {
            observedTargetEntityId = 0;
            observedTargetWasAliveMonster = false;
            openingAttackTargetEntityId = 0;
            openingSkillTargetEntityId = 0;
            return false;
        }

        var targetEntityId = target.TargetEntityId;
        var killed = observedTargetEntityId == targetEntityId &&
                     observedTargetWasAliveMonster &&
                     target.IsMonster &&
                     !target.IsAlive;

        observedTargetEntityId = targetEntityId;
        observedTargetWasAliveMonster = target.IsMonsterAlive;
        if (!target.IsMonsterAlive)
        {
            openingAttackTargetEntityId = 0;
            openingSkillTargetEntityId = 0;
        }

        if (!killed)
        {
            return false;
        }

        killedTargetEntityId = targetEntityId;
        return true;
    }

    public bool ShouldPressOpeningAttackKey(LockedTargetSnapshot target)
    {
        return target.IsMonsterAlive &&
               target.TargetEntityId != 0 &&
               openingAttackTargetEntityId != target.TargetEntityId;
    }

    public void MarkOpeningAttackKeyAttempted(LockedTargetSnapshot target)
    {
        openingAttackTargetEntityId = target.TargetEntityId;
    }

    public void ResetOpeningAttackKey()
    {
        openingAttackTargetEntityId = 0;
    }

    public bool ShouldHandleOpeningSkill(LockedTargetSnapshot target)
    {
        return target.IsMonsterAlive &&
               target.TargetEntityId != 0 &&
               openingSkillTargetEntityId != target.TargetEntityId;
    }

    public void MarkOpeningSkillHandled(LockedTargetSnapshot target)
    {
        openingSkillTargetEntityId = target.TargetEntityId;
    }

    public void ResetOpeningSkill()
    {
        openingSkillTargetEntityId = 0;
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

    public bool ShouldPressMaintenanceKey(string key, DateTimeOffset now, TimeSpan interval)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return !maintenanceKeyPressedAt.TryGetValue(key.Trim(), out var lastPressedAt) ||
               now - lastPressedAt >= interval;
    }

    public void MarkMaintenanceKeyAttempted(string key, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            maintenanceKeyPressedAt[key.Trim()] = now;
        }
    }

    public void StartMaintenanceRest(bool forHp, bool forMp)
    {
        IsMaintenanceResting = true;
        MaintenanceRestingForHp = forHp;
        MaintenanceRestingForMp = forMp;
    }

    public void ClearMaintenanceRest()
    {
        IsMaintenanceResting = false;
        MaintenanceRestingForHp = false;
        MaintenanceRestingForMp = false;
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

    public bool IsUncalibratedUnknownSuppressed(SkillSnapshot skill, DateTimeOffset now)
    {
        if (HasCooldownTickCalibration ||
            !uncalibratedUnknownSuppressUntil.TryGetValue(skill.SkillId, out var suppressedUntil))
        {
            return false;
        }

        if (now < suppressedUntil)
        {
            return true;
        }

        uncalibratedUnknownSuppressUntil.Remove(skill.SkillId);
        return false;
    }

    public void SuppressUncalibratedUnknownSkill(SkillSnapshot skill, DateTimeOffset suppressedUntil)
    {
        if (HasCooldownTickCalibration ||
            skill.CooldownDuration == 0 ||
            skill.CooldownEndTime == 0)
        {
            return;
        }

        uncalibratedUnknownSuppressUntil[skill.SkillId] = suppressedUntil;
    }

    public bool TryUpdateCooldownTickCalibration(
        IReadOnlyList<SkillSnapshot> skills,
        uint osTick,
        DateTimeOffset now,
        out SemiAutoCooldownTickCalibration calibration)
    {
        calibration = default;
        ClearExpiredUncalibratedUnknownSuppressions(now);
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
            uncalibratedUnknownSuppressUntil.Clear();
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

    private void ClearExpiredUncalibratedUnknownSuppressions(DateTimeOffset now)
    {
        foreach (var pair in uncalibratedUnknownSuppressUntil.ToArray())
        {
            if (now >= pair.Value)
            {
                uncalibratedUnknownSuppressUntil.Remove(pair.Key);
            }
        }
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
