using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Optional attack pair lifecycle; all release evidence belongs to the existing executors.</summary>
public sealed class QuickbarAttackWeaveState
{
    private TimeProvider? clock;
    private long? lastSkillKeyAt;
    private long? mainAttemptId;
    private long waitStartedAt;
    private TimeSpan waitDuration;
    private long? lastAttackAttemptAt;
    private long? postAttackStartedAt;
    private OpeningAttempt? openingAttempt;

    public static readonly TimeSpan PostAttackDelay = TimeSpan.FromMilliseconds(30);
    public int ConfirmedCount { get; private set; }
    public bool IsWaiting => ConfirmedCount == 2 ||
        (postAttackStartedAt is { } stamp && clock is { } timeProvider &&
         timeProvider.GetElapsedTime(stamp) < PostAttackDelay);
    public bool HasOpeningAttempt => openingAttempt is not null;

    public bool TryResetAfterIdle(TimeProvider timeProvider)
    {
        if (IsWaiting || lastSkillKeyAt is not { } stamp ||
            timeProvider.GetElapsedTime(stamp) <= AttackWeaveState.MaximumSkillKeyGap) return false;
        var hadPair = ConfirmedCount != 0 || mainAttemptId.HasValue || openingAttempt is not null;
        Reset();
        return hadPair;
    }

    public void MarkMainSkillKeyPressed(long attemptId, TimeProvider timeProvider)
    {
        MarkSkillKeyPressed(timeProvider);
        mainAttemptId = attemptId;
    }

    public bool TryConfirmMainRelease(long attemptId, TimeProvider timeProvider, int delayMs)
    {
        TryResetAfterIdle(timeProvider);
        if (mainAttemptId != attemptId) return false;
        mainAttemptId = null;
        return ConfirmRelease(timeProvider, delayMs);
    }

    public void TrackOpeningPress(SkillSnapshot skill, SkillAvailabilitySnapshot baseline,
        TimeProvider timeProvider, TimeSpan confirmationTimeout)
    {
        MarkSkillKeyPressed(timeProvider);
        if (openingAttempt?.SkillId == skill.SkillId) return;
        openingAttempt = new(skill.SkillId, skill.CooldownEndTime, baseline.LastReleasedSkillTime,
            timeProvider.GetTimestamp(), confirmationTimeout);
    }

    public uint? TryConfirmOpeningRelease(IReadOnlyList<SkillSnapshot> skills, SkillAvailabilitySnapshot availability,
        TimeProvider timeProvider, int delayMs)
    {
        TryResetAfterIdle(timeProvider);
        if (openingAttempt is not { } pending) return null;
        if (timeProvider.GetElapsedTime(pending.StartedAt) >= pending.Timeout)
        {
            openingAttempt = null;
            return null;
        }
        var preciseRelease = availability.LastReleasedSkillId == pending.SkillId &&
            availability.LastReleasedSkillTime != pending.PreviousReleaseTime;
        var differentRelease = availability.LastReleasedSkillTime != pending.PreviousReleaseTime &&
            availability.LastReleasedSkillId != 0 && availability.LastReleasedSkillId != pending.SkillId;
        var releaseClockAdvancedOrUnavailable = availability.LastReleasedSkillTime != pending.PreviousReleaseTime ||
            (availability.CombatState is null && availability.LastReleasedSkillTime == 0 &&
             pending.PreviousReleaseTime == 0 && availability.LastReleasedSkillId == 0);
        var skill = skills.FirstOrDefault(item => item.SkillId == pending.SkillId);
        var cooldownAdvanced = !differentRelease && releaseClockAdvancedOrUnavailable && skill is not null &&
            skill.CooldownEndTime != 0 && (pending.PreviousCooldownEndTime == 0 ||
            unchecked((int)(skill.CooldownEndTime - pending.PreviousCooldownEndTime)) > 0);
        if (!preciseRelease && !cooldownAdvanced) return null;
        openingAttempt = null;
        return ConfirmRelease(timeProvider, delayMs) ? pending.SkillId : null;
    }

    public bool ShouldPressAttack(TimeProvider timeProvider) => ConfirmedCount == 2 &&
        timeProvider.GetElapsedTime(waitStartedAt) >= waitDuration &&
        (lastAttackAttemptAt is not { } stamp || timeProvider.GetElapsedTime(stamp) >= TimeSpan.FromMilliseconds(100));

    public void MarkAttackAttempt(TimeProvider timeProvider) => lastAttackAttemptAt = timeProvider.GetTimestamp();

    public void MarkAttackSucceeded(TimeProvider timeProvider)
    {
        Reset();
        clock = timeProvider;
        postAttackStartedAt = timeProvider.GetTimestamp();
    }

    public void Reset()
    {
        ConfirmedCount = 0;
        lastSkillKeyAt = null;
        mainAttemptId = null;
        lastAttackAttemptAt = null;
        postAttackStartedAt = null;
        openingAttempt = null;
        clock = null;
    }

    private void MarkSkillKeyPressed(TimeProvider timeProvider)
    {
        if (clock is not null && !ReferenceEquals(clock, timeProvider)) Reset();
        clock = timeProvider;
        TryResetAfterIdle(timeProvider);
        clock = timeProvider;
        lastSkillKeyAt = timeProvider.GetTimestamp();
    }

    private bool ConfirmRelease(TimeProvider timeProvider, int delayMs)
    {
        if (IsWaiting) return false;
        ConfirmedCount++;
        if (IsWaiting)
        {
            waitStartedAt = timeProvider.GetTimestamp();
            waitDuration = TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 0, 10000));
            lastAttackAttemptAt = null;
        }
        return true;
    }

    private sealed record OpeningAttempt(uint SkillId, uint PreviousCooldownEndTime, uint PreviousReleaseTime,
        long StartedAt, TimeSpan Timeout);
}
