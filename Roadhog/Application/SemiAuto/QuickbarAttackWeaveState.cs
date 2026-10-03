using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Counts cooldown transitions after available skills were pressed, independently of actor release confirmation.</summary>
public sealed class QuickbarAttackWeaveState
{
    private TimeProvider? clock;
    private long? idleAnchorAt;
    private long waitStartedAt;
    private TimeSpan waitDuration;
    private long? lastAttackAttemptAt;
    private long? postAttackStartedAt;
    private readonly Dictionary<uint, CooldownAttempt> attempts = new();
    private readonly Queue<CooldownConfirmation> confirmations = new();

    public static readonly TimeSpan PostAttackDelay = TimeSpan.FromMilliseconds(30);
    public int ConfirmedCount { get; private set; }
    public bool IsWaiting => ConfirmedCount == 2 ||
        (postAttackStartedAt is { } stamp && clock is { } timeProvider &&
         timeProvider.GetElapsedTime(stamp) < PostAttackDelay);
    public bool HasOpeningAttempt => attempts.Values.Any(attempt => attempt.IsOpening) ||
        confirmations.Any(confirmation => confirmation.IsOpening);
    public bool HasPendingAttempts => attempts.Count != 0 || confirmations.Count != 0;
    public IReadOnlyCollection<uint> PendingSkillIds => attempts.Keys.ToArray();

    public bool TryResetAfterIdle(TimeProvider timeProvider)
    {
        if (IsWaiting || idleAnchorAt is not { } stamp ||
            timeProvider.GetElapsedTime(stamp) <= AttackWeaveState.MaximumSkillKeyGap) return false;
        var hadPair = ConfirmedCount != 0 || HasPendingAttempts;
        Reset();
        return hadPair;
    }

    public void TrackMainPress(SkillSnapshot? skill, bool wasAvailable, TimeProvider timeProvider,
        TimeSpan confirmationTimeout)
    {
        MarkSkillKeyPressed(timeProvider);
        if (wasAvailable && skill is not null) TrackPress(skill, timeProvider, confirmationTimeout, isOpening: false);
    }

    public void TrackOpeningPress(SkillSnapshot skill, TimeProvider timeProvider, TimeSpan confirmationTimeout)
    {
        MarkSkillKeyPressed(timeProvider);
        TrackPress(skill, timeProvider, confirmationTimeout, isOpening: true);
    }

    private void TrackPress(SkillSnapshot skill, TimeProvider timeProvider, TimeSpan confirmationTimeout, bool isOpening)
    {
        if (IsWaiting || skill.CooldownDuration == 0) return;
        if (attempts.TryGetValue(skill.SkillId, out var pending) &&
            timeProvider.GetElapsedTime(pending.StartedAt) < pending.Timeout) return;
        attempts[skill.SkillId] = new(skill.CooldownEndTime, timeProvider.GetTimestamp(), confirmationTimeout, isOpening);
    }

    public IReadOnlyList<uint> ObserveCooldowns(IReadOnlyList<SkillSnapshot> skills,
        TimeProvider timeProvider, int delayMs)
    {
        TryResetAfterIdle(timeProvider);
        foreach (var (skillId, pending) in attempts.ToArray())
        {
            if (timeProvider.GetElapsedTime(pending.StartedAt) >= pending.Timeout)
            {
                attempts.Remove(skillId);
                continue;
            }
            var skill = skills.FirstOrDefault(item => item.SkillId == skillId);
            if (skill is null || skill.CooldownDuration == 0 || skill.CooldownEndTime == 0 ||
                (pending.PreviousCooldownEndTime != 0 &&
                 unchecked((int)(skill.CooldownEndTime - pending.PreviousCooldownEndTime)) <= 0)) continue;
            attempts.Remove(skillId);
            confirmations.Enqueue(new(skillId, pending.IsOpening));
        }
        var counted = new List<uint>();
        while (!IsWaiting && confirmations.TryDequeue(out var confirmation))
        {
            ConfirmRelease(timeProvider, delayMs);
            counted.Add(confirmation.SkillId);
        }
        return counted;
    }

    public bool ShouldPressAttack(TimeProvider timeProvider) => ConfirmedCount == 2 &&
        timeProvider.GetElapsedTime(waitStartedAt) >= waitDuration &&
        (lastAttackAttemptAt is not { } stamp || timeProvider.GetElapsedTime(stamp) >= TimeSpan.FromMilliseconds(100));

    public void MarkAttackAttempt(TimeProvider timeProvider) => lastAttackAttemptAt = timeProvider.GetTimestamp();

    public void MarkAttackSucceeded(TimeProvider timeProvider)
    {
        // Later stages may already have entered CD before the first pair was observed.
        // Their unconsumed evidence belongs to the next pair, even across a long C wait.
        ConfirmedCount = 0;
        lastAttackAttemptAt = null;
        clock = timeProvider;
        postAttackStartedAt = timeProvider.GetTimestamp();
        idleAnchorAt = HasPendingAttempts
            ? postAttackStartedAt.Value + (long)Math.Ceiling(PostAttackDelay.TotalSeconds * timeProvider.TimestampFrequency)
            : null;
    }

    public void Reset()
    {
        ConfirmedCount = 0;
        idleAnchorAt = null;
        lastAttackAttemptAt = null;
        postAttackStartedAt = null;
        attempts.Clear();
        confirmations.Clear();
        clock = null;
    }

    private void MarkSkillKeyPressed(TimeProvider timeProvider)
    {
        if (clock is not null && !ReferenceEquals(clock, timeProvider)) Reset();
        clock = timeProvider;
        TryResetAfterIdle(timeProvider);
        clock = timeProvider;
        idleAnchorAt = timeProvider.GetTimestamp();
    }

    private bool ConfirmRelease(TimeProvider timeProvider, int delayMs)
    {
        if (IsWaiting) return false;
        // Carried cooldown evidence starts a new incomplete pair after the C pause.
        // Give that pair an idle deadline without extending the completed pair's key gap.
        idleAnchorAt ??= timeProvider.GetTimestamp();
        ConfirmedCount++;
        if (IsWaiting)
        {
            waitStartedAt = timeProvider.GetTimestamp();
            waitDuration = TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 0, 10000));
            lastAttackAttemptAt = null;
        }
        return true;
    }

    private sealed record CooldownAttempt(uint PreviousCooldownEndTime, long StartedAt, TimeSpan Timeout, bool IsOpening);
    private sealed record CooldownConfirmation(uint SkillId, bool IsOpening);
}
