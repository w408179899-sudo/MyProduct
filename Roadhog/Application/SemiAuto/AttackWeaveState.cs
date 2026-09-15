using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Counts confirmed combat casts, independently of key retries and cooldown calibration.</summary>
public sealed class AttackWeaveState
{
    public static readonly TimeSpan MaximumSkillKeyGap = TimeSpan.FromMilliseconds(1500);
    private sealed record Attempt(uint BaselineEndTime, DateTimeOffset ExpiresAt, SemiAutoSkillNode? ChainNode);
    private readonly Dictionary<uint, Attempt> attempts = new();
    private string? prefixOwner;
    private int prefixIndex;
    private uint targetIdentity;

    public int ConfirmedCount { get; private set; }
    public bool HasAttempts => attempts.Count != 0;
    public DateTimeOffset? WaitStartedAt { get; private set; }
    public DateTimeOffset? AttackDueAt { get; private set; }
    public DateTimeOffset? LastAttackAttemptAt { get; private set; }
    public DateTimeOffset? LastSkillKeyPressedAt { get; private set; }

    public void MarkSkillKeyPressed(DateTimeOffset now) => LastSkillKeyPressedAt = now;

    public bool TryResetAfterIdle(DateTimeOffset now)
    {
        if (WaitStartedAt.HasValue || !LastSkillKeyPressedAt.HasValue ||
            now - LastSkillKeyPressedAt.Value <= MaximumSkillKeyGap)
        {
            return false;
        }

        var hadUnfinishedPair = ConfirmedCount != 0 || attempts.Count != 0;
        attempts.Clear();
        ConfirmedCount = 0;
        LastSkillKeyPressedAt = null;
        // Preserve target identity, prefix position and the controller's chain position.
        return hadUnfinishedPair;
    }

    public void ObserveTarget(LockedTargetSnapshot target)
    {
        var identity = target.ServerObjectId != 0 ? target.ServerObjectId : target.TargetEntityId;
        if (!target.IsMonsterAlive || (targetIdentity != 0 && targetIdentity != identity))
        {
            Reset();
        }

        targetIdentity = target.IsMonsterAlive ? identity : 0;
    }

    // Reserve at most the remaining places in this pair. A retry keeps its original baseline.
    public bool CanPress(uint skillId) =>
        !WaitStartedAt.HasValue && (attempts.ContainsKey(skillId) || ConfirmedCount + attempts.Count < 2);

    public void TrackPress(SkillSnapshot skill, DateTimeOffset expiresAt, SemiAutoSkillNode? chainNode = null)
    {
        if (CanPress(skill.SkillId))
        {
            if (attempts.TryGetValue(skill.SkillId, out var attempt))
            {
                // A skill first sent by the opening/prefix path can subsequently enter a chain.
                // Keep the original confirmation baseline and deadline when it is retried.
                if (attempt.ChainNode is null && chainNode is not null)
                {
                    attempts[skill.SkillId] = attempt with { ChainNode = chainNode };
                }
            }
            else
            {
                attempts.Add(skill.SkillId, new Attempt(skill.CooldownEndTime, expiresAt, chainNode));
            }
        }
    }

    public IReadOnlyList<uint> Observe(
        IReadOnlyList<SkillSnapshot> skills,
        DateTimeOffset now,
        int delayMs,
        SemiAutoSkillNode? pendingChainSource = null,
        SemiAutoSkillNode? pendingChainNext = null)
    {
        var confirmed = new List<uint>();
        foreach (var (id, attempt) in attempts.ToArray())
        {
            // Chain advancement still accepts these nodes' cooldown changes after the short
            // key-confirmation timeout. Retain the same attempts until the chain releases them,
            // including across polls with unchanged or missing snapshots, so it cannot advance
            // on a success that weaving has already forgotten.
            var pendingChainAttempt = attempt.ChainNode is not null &&
                (ReferenceEquals(attempt.ChainNode, pendingChainSource) ||
                 ReferenceEquals(attempt.ChainNode, pendingChainNext));
            if (now > attempt.ExpiresAt && !pendingChainAttempt)
            {
                attempts.Remove(id);
                continue;
            }

            var skill = skills.FirstOrDefault(item => item.SkillId == id);
            if (skill is null || skill.CooldownEndTime == 0 ||
                unchecked((int)(skill.CooldownEndTime - attempt.BaselineEndTime)) <= 0)
            {
                continue;
            }

            attempts.Remove(id);
            confirmed.Add(id);
            ConfirmedCount++;
            if (ConfirmedCount == 2)
            {
                WaitStartedAt = now;
                AttackDueAt = now.AddMilliseconds(delayMs);
            }
        }

        return confirmed;
    }

    public bool ShouldPressAttack(DateTimeOffset now) =>
        AttackDueAt.HasValue && now >= AttackDueAt.Value &&
        (!LastAttackAttemptAt.HasValue || now - LastAttackAttemptAt.Value >= TimeSpan.FromMilliseconds(100));

    public void MarkAttackAttempt(DateTimeOffset now) => LastAttackAttemptAt = now;

    public TimeSpan FinishPair(DateTimeOffset now)
    {
        var pause = WaitStartedAt.HasValue ? now - WaitStartedAt.Value : TimeSpan.Zero;
        ConfirmedCount = 0;
        WaitStartedAt = null;
        AttackDueAt = null;
        LastAttackAttemptAt = null;
        LastSkillKeyPressedAt = null;
        return pause > TimeSpan.Zero ? pause : TimeSpan.Zero;
    }

    public int GetPrefixIndex(string owner)
    {
        if (!string.Equals(prefixOwner, owner, StringComparison.Ordinal))
        {
            prefixOwner = owner;
            prefixIndex = 0;
        }

        return prefixIndex;
    }

    public void AdvancePrefix() => prefixIndex++;

    public void ResetPrefix()
    {
        prefixOwner = null;
        prefixIndex = 0;
    }

    public void Reset()
    {
        targetIdentity = 0;
        attempts.Clear();
        ConfirmedCount = 0;
        WaitStartedAt = null;
        AttackDueAt = null;
        LastAttackAttemptAt = null;
        LastSkillKeyPressedAt = null;
        ResetPrefix();
    }
}
