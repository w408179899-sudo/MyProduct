using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatState
{
    private const uint ShortCooldownCalibrationCheckDurationMs = 15_000;
    private const int ShortCooldownImpossibleExtraRemainingMs = 60_000;
    private const int CooldownImpossibleExtraRemainingMs = 10_000;
    private const int CooldownCalibrationMaxOffsetJumpMs = 3_000;
    private static readonly TimeSpan CooldownCalibrationInvalidationThrottle = TimeSpan.FromSeconds(3);

    private int? cooldownTickOffsetMs;
    private readonly Dictionary<uint, uint> observedCooldownEndTimes = new();
    private readonly Dictionary<uint, uint> knownCooldownEndTimes = new();
    private readonly Dictionary<uint, DateTimeOffset> uncalibratedUnknownSuppressUntil = new();
    private readonly Dictionary<string, DateTimeOffset> maintenanceKeyPressedAt = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset lastMaintenanceKeyPressedAt = DateTimeOffset.MinValue;
    private readonly Dictionary<uint, uint> statusMaintenanceAbnormalIds = new();
    private readonly Dictionary<string, DateTimeOffset> statusMaintenanceActiveSeenAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> statusMaintenanceMissingReadCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> statusMaintenanceMissingReadStartedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, uint> spiritmasterDotAbnormalIds = new();
    private readonly Dictionary<uint, uint> spiritmasterPetBuffAbnormalIds = new();
    private readonly Dictionary<uint, DateTimeOffset> spiritmasterPetHpCooldownUntil = new();
    private DateTimeOffset lastAttackKeyPressedAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastSpiritmasterSummonAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset spiritmasterSummonVerifyUntil = DateTimeOffset.MinValue;
    private int consecutiveSpiritmasterPetMissingReads;
    private long lastSpiritmasterPetMissingCaptureSequence;
    private uint? lastPressedSkillId;
    private uint lastPressedCooldownEndTime;
    private DateTimeOffset lastPressedCooldownExpiresAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastPressedCooldownRetryAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastCooldownCalibrationInvalidatedAt = DateTimeOffset.MinValue;
    private string lastPressedCooldownRetryKey = string.Empty;
    private string lastPressedCooldownRetrySkillName = string.Empty;
    private string lastPressedCooldownRetrySkillType = string.Empty;
    private string lastPressedCooldownRetryPhase = string.Empty;
    private uint openingAttackTargetIdentity;
    private uint spiritmasterOpeningAttackTargetIdentity;
    private uint openingSkillTargetIdentity;
    private uint openingSkillAttemptTargetIdentity;
    private DateTimeOffset openingSkillAttemptStartedAt = DateTimeOffset.MinValue;
    private ushort observedTargetEntityId;
    private uint observedTargetIdentity;
    private bool observedTargetWasAliveMonster;
    private SpiritmasterAbnormalObservation? pendingSpiritmasterDotObservation;
    private SpiritmasterPetHpIncreaseConfirmation? pendingSpiritmasterPetHpIncreaseConfirmation;

    public SemiAutoSkillNode? PendingChainSourceNode { get; private set; }

    public SemiAutoSkillNode? PendingChainNextNode { get; private set; }

    public uint PendingChainSourceCooldownEndTime { get; private set; }

    public DateTimeOffset PendingChainExpiresAt { get; private set; }

    public int PendingChainWindowMs { get; private set; }

    public bool PendingChainNextPressStarted { get; private set; }

    public uint PendingChainNextCooldownEndTime { get; private set; }

    public bool HasChainWork => PendingChainSourceNode is not null;

    public bool HasPendingChainWindowStarted => PendingChainExpiresAt != DateTimeOffset.MinValue;

    public bool HasCooldownTickCalibration => cooldownTickOffsetMs.HasValue;

    public int? CooldownTickOffsetMs => cooldownTickOffsetMs;

    public DateTimeOffset LastTargetWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastPlanWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSkillWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastConditionSkillWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetStateLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastNoSkillLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastAttackKeyWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastMaintenanceWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSpiritmasterSummonVerifyLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSpiritmasterPetPresenceUnknownLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSpiritmasterPetHpConfirmationLogAt { get; set; } = DateTimeOffset.MinValue;

    public bool HasPendingSpiritmasterSummonVerification =>
        spiritmasterSummonVerifyUntil != DateTimeOffset.MinValue;

    public int ConsecutiveSpiritmasterPetMissingReads => consecutiveSpiritmasterPetMissingReads;

    public long LastSpiritmasterPetMissingCaptureSequence => lastSpiritmasterPetMissingCaptureSequence;

    public SpiritmasterPetHpIncreaseConfirmation? PendingSpiritmasterPetHpIncreaseConfirmation =>
        pendingSpiritmasterPetHpIncreaseConfirmation;

    public bool IsMaintenanceResting { get; private set; }

    public bool MaintenanceRestingForHp { get; private set; }

    public bool MaintenanceRestingForMp { get; private set; }

    public bool ObserveTarget(
        LockedTargetSnapshot target,
        out ushort killedTargetEntityId,
        out bool liveTargetChanged)
    {
        killedTargetEntityId = 0;
        liveTargetChanged = false;
        if (!target.HasTarget)
        {
            observedTargetEntityId = 0;
            observedTargetIdentity = 0;
            observedTargetWasAliveMonster = false;
            openingAttackTargetIdentity = 0;
            spiritmasterOpeningAttackTargetIdentity = 0;
            openingSkillTargetIdentity = 0;
            ClearOpeningSkillAttempt();
            return false;
        }

        var targetEntityId = target.TargetEntityId;
        var targetIdentity = ResolveOpeningTargetIdentity(target);
        var previousTargetIdentity = observedTargetIdentity != 0
            ? observedTargetIdentity
            : observedTargetEntityId;
        var killed = observedTargetEntityId == targetEntityId &&
                     observedTargetWasAliveMonster &&
                     target.IsMonster &&
                     !target.IsAlive;
        liveTargetChanged = target.IsMonsterAlive &&
                            observedTargetWasAliveMonster &&
                            previousTargetIdentity != 0 &&
                            targetIdentity != 0 &&
                            previousTargetIdentity != targetIdentity;

        observedTargetEntityId = targetEntityId;
        observedTargetIdentity = targetIdentity;
        observedTargetWasAliveMonster = target.IsMonsterAlive;
        if (!target.IsMonsterAlive)
        {
            openingAttackTargetIdentity = 0;
            spiritmasterOpeningAttackTargetIdentity = 0;
            openingSkillTargetIdentity = 0;
            ClearOpeningSkillAttempt();
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
        ClearOpeningSkillAttempt();
    }

    public void ResetOpeningSkill()
    {
        openingSkillTargetIdentity = 0;
        ClearOpeningSkillAttempt();
    }

    public DateTimeOffset GetOrStartOpeningSkillAttemptStartedAt(LockedTargetSnapshot target, DateTimeOffset now)
    {
        var targetIdentity = ResolveOpeningTargetIdentity(target);
        if (targetIdentity == 0)
        {
            return DateTimeOffset.MinValue;
        }

        if (openingSkillAttemptTargetIdentity != targetIdentity ||
            openingSkillAttemptStartedAt == DateTimeOffset.MinValue)
        {
            openingSkillAttemptTargetIdentity = targetIdentity;
            openingSkillAttemptStartedAt = now;
        }

        return openingSkillAttemptStartedAt;
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
        return ShouldPressMaintenanceKey(key, now, interval, TimeSpan.Zero);
    }

    public bool ShouldPressMaintenanceKey(
        string key,
        DateTimeOffset now,
        TimeSpan interval,
        TimeSpan globalInterval)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (globalInterval > TimeSpan.Zero &&
            lastMaintenanceKeyPressedAt != DateTimeOffset.MinValue &&
            now - lastMaintenanceKeyPressedAt < globalInterval)
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
            lastMaintenanceKeyPressedAt = now;
        }
    }

    public void ClearMaintenanceKeyAttempt(string key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !maintenanceKeyPressedAt.Remove(key.Trim()))
        {
            return;
        }

        lastMaintenanceKeyPressedAt = maintenanceKeyPressedAt.Count == 0
            ? DateTimeOffset.MinValue
            : maintenanceKeyPressedAt.Values.Max();
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

    public int RecordSpiritmasterPetMissingRead(long captureSequence)
    {
        if (captureSequence <= 0 || captureSequence == lastSpiritmasterPetMissingCaptureSequence)
        {
            return consecutiveSpiritmasterPetMissingReads;
        }

        lastSpiritmasterPetMissingCaptureSequence = captureSequence;
        if (consecutiveSpiritmasterPetMissingReads < int.MaxValue)
        {
            consecutiveSpiritmasterPetMissingReads++;
        }

        return consecutiveSpiritmasterPetMissingReads;
    }

    public void ResetSpiritmasterPetMissingReads()
    {
        consecutiveSpiritmasterPetMissingReads = 0;
    }

    public void ResetSpiritmasterPetLifecycle()
    {
        consecutiveSpiritmasterPetMissingReads = 0;
        lastSpiritmasterPetMissingCaptureSequence = 0;
        lastSpiritmasterSummonAttemptAt = DateTimeOffset.MinValue;
        LastSpiritmasterPetPresenceUnknownLogAt = DateTimeOffset.MinValue;
        ClearSpiritmasterSummonVerification();
        ClearSpiritmasterPetHpIncreaseConfirmation();
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

    public void MarkStatusMaintenanceActive(string ruleKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ruleKey))
        {
            return;
        }

        var key = ruleKey.Trim();
        statusMaintenanceActiveSeenAt[key] = now;
        ClearStatusMaintenanceMissingRead(key);
    }

    public bool TryGetStatusMaintenanceActiveSeenAt(string ruleKey, out DateTimeOffset activeSeenAt)
    {
        activeSeenAt = DateTimeOffset.MinValue;
        return !string.IsNullOrWhiteSpace(ruleKey) &&
               statusMaintenanceActiveSeenAt.TryGetValue(ruleKey.Trim(), out activeSeenAt);
    }

    public int MarkStatusMaintenanceMissingRead(
        string ruleKey,
        DateTimeOffset now,
        out DateTimeOffset firstMissingAt)
    {
        firstMissingAt = now;
        if (string.IsNullOrWhiteSpace(ruleKey))
        {
            return 0;
        }

        var key = ruleKey.Trim();
        if (!statusMaintenanceMissingReadStartedAt.TryGetValue(key, out firstMissingAt))
        {
            firstMissingAt = now;
            statusMaintenanceMissingReadStartedAt[key] = firstMissingAt;
        }

        statusMaintenanceMissingReadCounts.TryGetValue(key, out var count);
        count++;
        statusMaintenanceMissingReadCounts[key] = count;
        return count;
    }

    public void ClearStatusMaintenanceMissingRead(string ruleKey)
    {
        if (!string.IsNullOrWhiteSpace(ruleKey))
        {
            var key = ruleKey.Trim();
            statusMaintenanceMissingReadCounts.Remove(key);
            statusMaintenanceMissingReadStartedAt.Remove(key);
        }
    }

    public void ClearStatusMaintenanceStickyState()
    {
        statusMaintenanceActiveSeenAt.Clear();
    }

    public void ClearStatusMaintenanceTransientState()
    {
        ClearStatusMaintenanceStickyState();
        statusMaintenanceMissingReadCounts.Clear();
        statusMaintenanceMissingReadStartedAt.Clear();
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

    public void BeginSpiritmasterPetHpIncreaseConfirmation(
        uint skillId,
        string key,
        uint petServerObjectId,
        uint baselineCurrentHp,
        DateTimeOffset baselineCapturedAt,
        DateTimeOffset pressedAt,
        TimeSpan retryInterval,
        TimeSpan confirmationLifetime,
        TimeSpan localCooldown)
    {
        pendingSpiritmasterPetHpIncreaseConfirmation = new SpiritmasterPetHpIncreaseConfirmation(
            skillId,
            key,
            petServerObjectId,
            baselineCurrentHp,
            baselineCapturedAt,
            pressedAt,
            pressedAt + retryInterval,
            pressedAt + confirmationLifetime,
            1,
            (int)Math.Clamp(localCooldown.TotalMilliseconds, 1, int.MaxValue));
    }

    public void DeferSpiritmasterPetHpIncreaseConfirmation(
        DateTimeOffset now,
        TimeSpan retryInterval)
    {
        if (pendingSpiritmasterPetHpIncreaseConfirmation is not { } pending)
        {
            return;
        }

        pendingSpiritmasterPetHpIncreaseConfirmation = pending with
        {
            NextCheckAt = now + retryInterval
        };
    }

    public void RecordSpiritmasterPetHpIncreaseRetry(
        uint baselineCurrentHp,
        DateTimeOffset baselineCapturedAt,
        DateTimeOffset pressedAt,
        TimeSpan retryInterval)
    {
        if (pendingSpiritmasterPetHpIncreaseConfirmation is not { } pending)
        {
            return;
        }

        pendingSpiritmasterPetHpIncreaseConfirmation = pending with
        {
            BaselineCurrentHp = baselineCurrentHp,
            BaselineCapturedAt = baselineCapturedAt,
            LastPressedAt = pressedAt,
            NextCheckAt = pressedAt + retryInterval,
            AttemptCount = pending.AttemptCount + 1
        };
    }

    public void ClearSpiritmasterPetHpIncreaseConfirmation()
    {
        pendingSpiritmasterPetHpIncreaseConfirmation = null;
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
        StartPendingChainAdvance(sourceNode, nextNode, expiresAt, sourceCooldownEndTime, 0);
    }

    public void StartPendingChainAdvance(
        SemiAutoSkillNode sourceNode,
        SemiAutoSkillNode nextNode,
        DateTimeOffset expiresAt,
        uint sourceCooldownEndTime,
        int windowMs)
    {
        PendingChainSourceNode = sourceNode;
        PendingChainNextNode = nextNode;
        PendingChainSourceCooldownEndTime = sourceCooldownEndTime;
        PendingChainExpiresAt = expiresAt;
        PendingChainWindowMs = windowMs;
        PendingChainNextPressStarted = false;
        PendingChainNextCooldownEndTime = 0;
    }

    public void StartPendingChainWindow(DateTimeOffset expiresAt)
    {
        PendingChainExpiresAt = expiresAt;
    }

    public void ClearPendingChainAdvance()
    {
        PendingChainSourceNode = null;
        PendingChainNextNode = null;
        PendingChainSourceCooldownEndTime = 0;
        PendingChainExpiresAt = DateTimeOffset.MinValue;
        PendingChainWindowMs = 0;
        PendingChainNextPressStarted = false;
        PendingChainNextCooldownEndTime = 0;
    }

    public bool IsPendingChainExpired(DateTimeOffset now)
    {
        return PendingChainSourceNode is not null &&
               HasPendingChainWindowStarted &&
               now >= PendingChainExpiresAt;
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
        return TryUpdateCooldownTickCalibration(
            skills,
            osTick,
            now,
            out calibration,
            out _);
    }

    public bool TryUpdateCooldownTickCalibration(
        IReadOnlyList<SkillSnapshot> skills,
        uint osTick,
        DateTimeOffset now,
        out SemiAutoCooldownTickCalibration calibration,
        out SemiAutoCooldownTickCalibrationRejection? rejection)
    {
        calibration = default;
        rejection = null;
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

            var candidate = BuildCooldownTickCalibration(observedSkill, osTick);
            if (TryRejectCooldownTickCalibration(candidate, out var rejected))
            {
                rejection = rejected;
                if (lastPressedSkillId == observedSkill.SkillId)
                {
                    ClearLastPressedSkill();
                }

                continue;
            }

            updatedCalibration = candidate;
        }

        if (updatedCalibration.HasValue)
        {
            calibration = updatedCalibration.Value;
            ApplyCooldownTickCalibration(calibration);
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

        var pendingCandidate = BuildCooldownTickCalibration(skill, osTick);
        if (TryRejectCooldownTickCalibration(pendingCandidate, out var pendingRejection))
        {
            rejection = pendingRejection;
            ClearLastPressedSkill();
            return false;
        }

        calibration = pendingCandidate;
        ApplyCooldownTickCalibration(calibration);
        ClearLastPressedSkill();
        return true;
    }

    public bool TryInvalidateImplausibleCooldownTickCalibration(
        IReadOnlyList<SkillSnapshot> skills,
        uint osTick,
        DateTimeOffset now,
        int readyToleranceMs,
        out SemiAutoCooldownCalibrationInvalidation invalidation)
    {
        invalidation = default;
        if (cooldownTickOffsetMs is not int oldOffsetMs ||
            skills.Count == 0 ||
            lastCooldownCalibrationInvalidatedAt != DateTimeOffset.MinValue &&
            now - lastCooldownCalibrationInvalidatedAt < CooldownCalibrationInvalidationThrottle)
        {
            return false;
        }

        if (lastPressedSkillId is not null && now <= lastPressedCooldownExpiresAt)
        {
            return false;
        }

        var gameTick = EstimateGameTick(osTick);
        var suspiciousSkillCount = 0;
        SemiAutoCooldownCalibrationInvalidation? strongestInvalidation = null;

        foreach (var skill in skills)
        {
            if (skill.CooldownDuration == 0)
            {
                continue;
            }

            var effectiveEndTime = GetEffectiveCooldownEndTime(skill, gameTick, readyToleranceMs);
            if (effectiveEndTime == 0)
            {
                return false;
            }

            var remainingMs = unchecked((int)(effectiveEndTime - gameTick));
            if (remainingMs <= readyToleranceMs)
            {
                return false;
            }

            var extraRemainingMs = (long)remainingMs - skill.CooldownDuration;
            var shortCooldownImpossible =
                skill.CooldownDuration <= ShortCooldownCalibrationCheckDurationMs &&
                extraRemainingMs > ShortCooldownImpossibleExtraRemainingMs;
            var generallyImplausible = extraRemainingMs > CooldownImpossibleExtraRemainingMs;
            if (!shortCooldownImpossible && !generallyImplausible)
            {
                continue;
            }

            suspiciousSkillCount++;
            if (strongestInvalidation is null || shortCooldownImpossible)
            {
                strongestInvalidation = new SemiAutoCooldownCalibrationInvalidation(
                    oldOffsetMs,
                    osTick,
                    gameTick,
                    shortCooldownImpossible ? "short_cooldown_impossible" : "cooldown_implausible",
                    suspiciousSkillCount,
                    skill.SkillId,
                    skill.Name,
                    skill.CooldownDuration,
                    skill.CooldownEndTime,
                    effectiveEndTime,
                    remainingMs);
            }
        }

        if (strongestInvalidation is not { } candidate ||
            candidate.Reason != "short_cooldown_impossible" && suspiciousSkillCount < 2)
        {
            return false;
        }

        cooldownTickOffsetMs = null;
        observedCooldownEndTimes.Clear();
        knownCooldownEndTimes.Clear();
        uncalibratedUnknownSuppressUntil.Clear();
        lastCooldownCalibrationInvalidatedAt = now;
        invalidation = candidate with { SuspiciousSkillCount = suspiciousSkillCount };
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

    public void ClearPressedSkillCooldownConfirmation()
    {
        var skillId = lastPressedSkillId;
        ClearLastPressedSkill();
        if (skillId is not null && skillId != 0)
        {
            uncalibratedUnknownSuppressUntil.Remove(skillId.Value);
        }
    }

    public void ClearPressedSkillCooldownTracking()
    {
        ClearLastPressedSkill();
    }

    private void ClearOpeningSkillAttempt()
    {
        openingSkillAttemptTargetIdentity = 0;
        openingSkillAttemptStartedAt = DateTimeOffset.MinValue;
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

    private SemiAutoCooldownTickCalibration BuildCooldownTickCalibration(
        SkillSnapshot skill,
        uint osTick)
    {
        var startTick = unchecked(skill.CooldownEndTime - skill.CooldownDuration);
        var offsetMs = unchecked((int)(startTick - osTick));

        return new SemiAutoCooldownTickCalibration(
            skill.SkillId,
            skill.Name,
            osTick,
            skill.CooldownDuration,
            skill.CooldownEndTime,
            startTick,
            offsetMs);
    }

    private void ApplyCooldownTickCalibration(SemiAutoCooldownTickCalibration calibration)
    {
        cooldownTickOffsetMs = calibration.OffsetMs;
    }

    private bool TryRejectCooldownTickCalibration(
        SemiAutoCooldownTickCalibration calibration,
        out SemiAutoCooldownTickCalibrationRejection rejection)
    {
        rejection = default;
        if (cooldownTickOffsetMs is not int oldOffsetMs)
        {
            return false;
        }

        var deltaMs = BoundedAbsDiff(calibration.OffsetMs, oldOffsetMs);
        if (deltaMs <= CooldownCalibrationMaxOffsetJumpMs)
        {
            return false;
        }

        rejection = new SemiAutoCooldownTickCalibrationRejection(
            oldOffsetMs,
            calibration.OffsetMs,
            deltaMs,
            CooldownCalibrationMaxOffsetJumpMs,
            "offset_jump",
            calibration.SkillId,
            calibration.SkillName,
            calibration.OsTick,
            calibration.CooldownDuration,
            calibration.CooldownEndTime,
            calibration.CooldownStartTick);
        return true;
    }

    private static int BoundedAbsDiff(int first, int second)
    {
        var delta = Math.Abs((long)first - second);
        return delta > int.MaxValue ? int.MaxValue : (int)delta;
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

public readonly record struct SemiAutoCooldownTickCalibrationRejection(
    int OldOffsetMs,
    int NewOffsetMs,
    int DeltaMs,
    int MaxDeltaMs,
    string Reason,
    uint SkillId,
    string SkillName,
    uint OsTick,
    uint CooldownDuration,
    uint CooldownEndTime,
    uint CooldownStartTick);

public readonly record struct SemiAutoCooldownCalibrationInvalidation(
    int OldOffsetMs,
    uint OsTick,
    uint EstimatedGameTick,
    string Reason,
    int SuspiciousSkillCount,
    uint SkillId,
    string SkillName,
    uint CooldownDuration,
    uint CooldownEndTime,
    uint EffectiveCooldownEndTime,
    int RemainingMs);

public readonly record struct SemiAutoPendingSkillCooldownRetry(
    string Key,
    string SkillName,
    string SkillType,
    string Phase);

public sealed record SpiritmasterPetHpIncreaseConfirmation(
    uint SkillId,
    string Key,
    uint PetServerObjectId,
    uint BaselineCurrentHp,
    DateTimeOffset BaselineCapturedAt,
    DateTimeOffset LastPressedAt,
    DateTimeOffset NextCheckAt,
    DateTimeOffset ExpiresAt,
    int AttemptCount,
    int CooldownMs);
