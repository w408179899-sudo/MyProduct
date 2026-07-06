using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatState
{
    private int? cooldownTickOffsetMs;
    private readonly Dictionary<uint, uint> observedCooldownEndTimes = new();
    private readonly Dictionary<uint, uint> knownCooldownEndTimes = new();
    private readonly Dictionary<uint, DateTimeOffset> uncalibratedUnknownSuppressUntil = new();
    private readonly Dictionary<string, DateTimeOffset> maintenanceKeyPressedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, uint> statusMaintenanceAbnormalIds = new();
    private readonly Dictionary<uint, uint> spiritmasterDotAbnormalIds = new();
    private readonly Dictionary<uint, uint> spiritmasterPetBuffAbnormalIds = new();
    private readonly Dictionary<uint, DateTimeOffset> spiritmasterPetHpCooldownUntil = new();
    private DateTimeOffset lastAttackKeyPressedAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastSpiritmasterSummonAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset spiritmasterSummonVerifyUntil = DateTimeOffset.MinValue;
    private uint? lastPressedSkillId;
    private uint lastPressedCooldownEndTime;
    private DateTimeOffset lastPressedCooldownExpiresAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastPressedCooldownRetryAt = DateTimeOffset.MinValue;
    private string lastPressedCooldownRetryKey = string.Empty;
    private string lastPressedCooldownRetrySkillName = string.Empty;
    private string lastPressedCooldownRetrySkillType = string.Empty;
    private string lastPressedCooldownRetryPhase = string.Empty;
    private uint openingAttackTargetIdentity;
    private uint spiritmasterOpeningAttackTargetIdentity;
    private uint openingSkillTargetIdentity;
    private ushort observedTargetEntityId;
    private bool observedTargetWasAliveMonster;
    private SpiritmasterAbnormalObservation? pendingSpiritmasterDotObservation;

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

    public DateTimeOffset LastSpiritmasterSummonVerifyLogAt { get; set; } = DateTimeOffset.MinValue;

    public bool HasPendingSpiritmasterSummonVerification =>
        spiritmasterSummonVerifyUntil != DateTimeOffset.MinValue;

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
            openingAttackTargetIdentity = 0;
            spiritmasterOpeningAttackTargetIdentity = 0;
            openingSkillTargetIdentity = 0;
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
            openingAttackTargetIdentity = 0;
            spiritmasterOpeningAttackTargetIdentity = 0;
            openingSkillTargetIdentity = 0;
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
        var targetIdentity = ResolveOpeningTargetIdentity(target);
        return target.IsMonsterAlive &&
               targetIdentity != 0 &&
               openingAttackTargetIdentity != targetIdentity;
    }

    public void MarkOpeningAttackKeyAttempted(LockedTargetSnapshot target)
    {
        openingAttackTargetIdentity = ResolveOpeningTargetIdentity(target);
    }

    public void ResetOpeningAttackKey()
    {
        openingAttackTargetIdentity = 0;
    }

    public bool ShouldPressSpiritmasterOpeningAttackKey(LockedTargetSnapshot target)
    {
        var targetIdentity = ResolveOpeningTargetIdentity(target);
        return target.IsMonsterAlive &&
               targetIdentity != 0 &&
               spiritmasterOpeningAttackTargetIdentity != targetIdentity;
    }

    public void MarkSpiritmasterOpeningAttackKeyAttempted(LockedTargetSnapshot target)
    {
        spiritmasterOpeningAttackTargetIdentity = ResolveOpeningTargetIdentity(target);
    }

    public void ResetSpiritmasterOpeningAttackKey()
    {
        spiritmasterOpeningAttackTargetIdentity = 0;
    }

    public bool ShouldHandleOpeningSkill(LockedTargetSnapshot target)
    {
        var targetIdentity = ResolveOpeningTargetIdentity(target);
        return target.IsMonsterAlive &&
               targetIdentity != 0 &&
               openingSkillTargetIdentity != targetIdentity;
    }

    public void MarkOpeningSkillHandled(LockedTargetSnapshot target)
    {
        openingSkillTargetIdentity = ResolveOpeningTargetIdentity(target);
    }

    public void ResetOpeningSkill()
    {
        openingSkillTargetIdentity = 0;
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

    public bool ShouldAttemptSpiritmasterSummon(DateTimeOffset now, TimeSpan interval)
    {
        return lastSpiritmasterSummonAttemptAt == DateTimeOffset.MinValue ||
               now - lastSpiritmasterSummonAttemptAt >= interval;
    }

    public void MarkSpiritmasterSummonAttempted(DateTimeOffset now)
    {
        lastSpiritmasterSummonAttemptAt = now;
    }

    public bool IsAwaitingSpiritmasterSummonVerification(DateTimeOffset now)
    {
        return HasPendingSpiritmasterSummonVerification && now < spiritmasterSummonVerifyUntil;
    }

    public bool IsSpiritmasterSummonVerificationExpired(DateTimeOffset now)
    {
        return HasPendingSpiritmasterSummonVerification && now >= spiritmasterSummonVerifyUntil;
    }

    public void BeginSpiritmasterSummonVerification(DateTimeOffset now, TimeSpan verifyWindow)
    {
        spiritmasterSummonVerifyUntil = now + verifyWindow;
        LastSpiritmasterSummonVerifyLogAt = DateTimeOffset.MinValue;
    }

    public void ClearSpiritmasterSummonVerification()
    {
        spiritmasterSummonVerifyUntil = DateTimeOffset.MinValue;
        LastSpiritmasterSummonVerifyLogAt = DateTimeOffset.MinValue;
    }

    public bool TryGetStatusMaintenanceAbnormalId(uint skillId, out uint abnormalId)
    {
        return statusMaintenanceAbnormalIds.TryGetValue(skillId, out abnormalId) && abnormalId != 0;
    }

    public void RememberStatusMaintenanceAbnormalId(uint skillId, uint abnormalId)
    {
        if (skillId != 0 && abnormalId != 0)
        {
            statusMaintenanceAbnormalIds[skillId] = abnormalId;
        }
    }

    public bool TryGetSpiritmasterDotAbnormalId(uint skillId, out uint abnormalId)
    {
        return spiritmasterDotAbnormalIds.TryGetValue(skillId, out abnormalId) && abnormalId != 0;
    }

    public void RememberSpiritmasterDotAbnormalId(uint skillId, uint abnormalId)
    {
        if (skillId != 0 && abnormalId != 0)
        {
            spiritmasterDotAbnormalIds[skillId] = abnormalId;
        }
    }

    public void BeginSpiritmasterDotObservation(
        uint skillId,
        uint targetServerObjectId,
        IEnumerable<AbnormalStatusEntrySnapshot> beforeEntries,
        DateTimeOffset expiresAt)
    {
        if (skillId == 0 || targetServerObjectId == 0)
        {
            pendingSpiritmasterDotObservation = null;
            return;
        }

        pendingSpiritmasterDotObservation = new SpiritmasterAbnormalObservation(
            skillId,
            targetServerObjectId,
            beforeEntries.Select(entry => entry.AbnormalId).Where(id => id != 0).ToHashSet(),
            expiresAt);
    }

    public bool TryCompleteSpiritmasterDotObservation(
        uint targetServerObjectId,
        IEnumerable<AbnormalStatusEntrySnapshot> afterEntries,
        DateTimeOffset now,
        out uint skillId,
        out uint abnormalId)
    {
        skillId = 0;
        abnormalId = 0;
        var observation = pendingSpiritmasterDotObservation;
        if (observation is null ||
            observation.TargetServerObjectId != targetServerObjectId ||
            now > observation.ExpiresAt)
        {
            pendingSpiritmasterDotObservation = null;
            return false;
        }

        var learned = afterEntries.Any(entry => entry.AbnormalId == observation.SkillId)
            ? observation.SkillId
            : 0;
        if (learned == 0)
        {
            learned = afterEntries
                .Where(entry => entry.AbnormalId != 0 && !observation.BeforeAbnormalIds.Contains(entry.AbnormalId))
                .OrderByDescending(entry => entry.IsPhysicalDebuffCategory)
                .Select(entry => entry.AbnormalId)
                .FirstOrDefault();
        }

        if (learned == 0)
        {
            return false;
        }

        pendingSpiritmasterDotObservation = null;
        skillId = observation.SkillId;
        abnormalId = learned;
        RememberSpiritmasterDotAbnormalId(observation.SkillId, learned);
        return true;
    }

    public bool TryGetSpiritmasterPetBuffAbnormalId(uint skillId, out uint abnormalId)
    {
        return spiritmasterPetBuffAbnormalIds.TryGetValue(skillId, out abnormalId) && abnormalId != 0;
    }

    public void RememberSpiritmasterPetBuffAbnormalId(uint skillId, uint abnormalId)
    {
        if (skillId != 0 && abnormalId != 0)
        {
            spiritmasterPetBuffAbnormalIds[skillId] = abnormalId;
        }
    }

    public bool ShouldPressSpiritmasterPetHpSkill(uint skillId, DateTimeOffset now)
    {
        if (skillId == 0 ||
            !spiritmasterPetHpCooldownUntil.TryGetValue(skillId, out var blockedUntil))
        {
            return true;
        }

        if (now < blockedUntil)
        {
            return false;
        }

        spiritmasterPetHpCooldownUntil.Remove(skillId);
        return true;
    }

    public void MarkSpiritmasterPetHpSkillPressed(uint skillId, DateTimeOffset now, TimeSpan cooldown)
    {
        if (skillId == 0 || cooldown <= TimeSpan.Zero)
        {
            return;
        }

        spiritmasterPetHpCooldownUntil[skillId] = now + cooldown;
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
        DateTimeOffset cooldownConfirmationExpiresAt,
        string? retryKey = null,
        string? retrySkillName = null,
        string? retrySkillType = null,
        string? retryPhase = null,
        DateTimeOffset? pressedAt = null)
    {
        if (skill.CooldownDuration == 0)
        {
            return;
        }

        observedCooldownEndTimes[skill.SkillId] = skill.CooldownEndTime;
        lastPressedSkillId = skill.SkillId;
        lastPressedCooldownEndTime = skill.CooldownEndTime;
        lastPressedCooldownExpiresAt = cooldownConfirmationExpiresAt;
        lastPressedCooldownRetryAt = pressedAt ?? DateTimeOffset.Now;
        lastPressedCooldownRetryKey = retryKey?.Trim() ?? string.Empty;
        lastPressedCooldownRetrySkillName = retrySkillName ?? skill.Name;
        lastPressedCooldownRetrySkillType = retrySkillType ?? string.Empty;
        lastPressedCooldownRetryPhase = retryPhase ?? "skill";
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

    public bool IsAwaitingPressedSkillCooldownConfirmation(
        IReadOnlyList<SkillSnapshot> skills,
        DateTimeOffset now,
        out SkillSnapshot? pendingSkill,
        out DateTimeOffset expiresAt)
    {
        pendingSkill = null;
        expiresAt = lastPressedCooldownExpiresAt;
        if (lastPressedSkillId is not uint skillId)
        {
            return false;
        }

        if (now > lastPressedCooldownExpiresAt)
        {
            ClearLastPressedSkill();
            return false;
        }

        pendingSkill = skills.FirstOrDefault(skill => skill.SkillId == skillId);
        if (pendingSkill is null)
        {
            return true;
        }

        if (pendingSkill.CooldownDuration == 0 ||
            DidCooldownEndAdvance(lastPressedCooldownEndTime, pendingSkill.CooldownEndTime))
        {
            ClearLastPressedSkill();
            return false;
        }

        return true;
    }

    public bool TryGetPressedSkillCooldownRetry(
        DateTimeOffset now,
        TimeSpan retryInterval,
        out SemiAutoPendingSkillCooldownRetry retry)
    {
        retry = default;
        if (lastPressedSkillId is null ||
            string.IsNullOrWhiteSpace(lastPressedCooldownRetryKey) ||
            now - lastPressedCooldownRetryAt < retryInterval)
        {
            return false;
        }

        retry = new SemiAutoPendingSkillCooldownRetry(
            lastPressedCooldownRetryKey,
            lastPressedCooldownRetrySkillName,
            lastPressedCooldownRetrySkillType,
            lastPressedCooldownRetryPhase);
        return true;
    }

    public bool HasPressedSkillCooldownRetryKey()
    {
        return lastPressedSkillId is not null &&
               !string.IsNullOrWhiteSpace(lastPressedCooldownRetryKey);
    }

    public void MarkPressedSkillCooldownRetried(DateTimeOffset now)
    {
        lastPressedCooldownRetryAt = now;
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
        lastPressedCooldownRetryAt = DateTimeOffset.MinValue;
        lastPressedCooldownRetryKey = string.Empty;
        lastPressedCooldownRetrySkillName = string.Empty;
        lastPressedCooldownRetrySkillType = string.Empty;
        lastPressedCooldownRetryPhase = string.Empty;
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

    private static uint ResolveOpeningTargetIdentity(LockedTargetSnapshot target)
    {
        return target.ServerObjectId != 0
            ? target.ServerObjectId
            : target.TargetEntityId;
    }

    private sealed record SpiritmasterAbnormalObservation(
        uint SkillId,
        uint TargetServerObjectId,
        HashSet<uint> BeforeAbnormalIds,
        DateTimeOffset ExpiresAt);
}

public readonly record struct SemiAutoCooldownTickCalibration(
    uint SkillId,
    string SkillName,
    uint OsTick,
    uint CooldownDuration,
    uint CooldownEndTime,
    uint CooldownStartTick,
    int OffsetMs);

public readonly record struct SemiAutoPendingSkillCooldownRetry(
    string Key,
    string SkillName,
    string SkillType,
    string Phase);
