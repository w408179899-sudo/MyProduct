using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Shared Spiritmaster DOT rules for quickbar roots; chain stages retain their existing semantics.</summary>
internal sealed class QuickbarSpiritmasterDotPolicy
{
    private readonly SemiAutoCombatState state;
    private readonly QuickbarSkillNode[] dotRoots;
    private readonly LockedTargetSnapshot target;
    private readonly Func<Task<LockedTargetAbnormalStatusSnapshot>> readAbnormalStatuses;
    private readonly TimeProvider timeProvider;
    private readonly IRoadhogLogger? logger;
    private readonly string? accountName;
    private LockedTargetAbnormalStatusSnapshot? beforePress;

    public QuickbarSpiritmasterDotPolicy(
        SemiAutoCombatState state, QuickbarSkillPlan plan, IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterSkillSettings settings, LockedTargetSnapshot target,
        Func<Task<LockedTargetAbnormalStatusSnapshot>> readAbnormalStatuses,
        TimeProvider? timeProvider = null, IRoadhogLogger? logger = null, string? accountName = null)
    {
        this.state = state;
        this.target = target;
        this.readAbnormalStatuses = readAbnormalStatuses;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.logger = logger;
        this.accountName = accountName;
        dotRoots = plan.Roots.Where(node =>
        {
            var skill = skills.FirstOrDefault(value => value.SkillId == node.SkillId);
            return SpiritmasterAutoSkillReleasePriority.IsConfiguredDotSkill(node.SkillId, settings,
                node.Name, skill?.Name, skill?.DisplayBaseName);
        }).ToArray();
    }

    public bool HasDotRoots => dotRoots.Length > 0;

    public async Task<IReadOnlySet<uint>> ReadSuppressedRootSkillIdsAsync()
    {
        if (!HasDotRoots) return new HashSet<uint>();
        var abnormal = await readAbnormalStatuses().ConfigureAwait(false);
        beforePress = SameTarget(abnormal.Target) ? abnormal : null;
        if (beforePress is null)
        {
            state.CancelSpiritmasterDotObservation();
            // Statuses belonging to another object cannot authorize a DOT
            // refresh or learning. The executor independently rechecks target
            // identity before every key, including other attack roots.
            return dotRoots.Select(node => node.SkillId).ToHashSet();
        }
        if (state.TryCompleteSpiritmasterDotObservation(TargetId(abnormal.Target), abnormal.Entries,
                timeProvider.GetUtcNow(), out var learnedSkillId, out var abnormalId))
        {
            logger?.Info("semi_auto.spiritmaster.dot_learned", new Dictionary<string, object?>
            {
                ["account"] = accountName, ["skillId"] = learnedSkillId,
                ["skillName"] = dotRoots.FirstOrDefault(node => node.SkillId == learnedSkillId)?.Name,
                ["abnormalId"] = abnormalId, ["targetEntityId"] = abnormal.Target.TargetEntityId,
                ["targetServerObjectId"] = abnormal.Target.ServerObjectId
            });
        }
        return dotRoots.Where(node => SpiritmasterAutoSkillReleasePriority.IsDotActiveOnTarget(
            node.SkillId, state, abnormal)).Select(node => node.SkillId).ToHashSet();
    }

    public async Task OnSkillPressedAsync(QuickbarSkillNode node)
    {
        if (node.NodeKey.Contains('/') || !dotRoots.Any(root => root.NodeKey == node.NodeKey))
        {
            // A later attack can add its own debuff. It must not be mistaken
            // for the previously pressed DOT's delayed application.
            state.CancelSpiritmasterDotObservation();
            return;
        }
        if (beforePress is null)
            return;
        if (state.QuickbarSkills.PendingAction is { AttemptCount: 1 } pending)
        {
            // Preserve the first pre-press baseline throughout finite retries.
            // The quickbar confirmation budget can exceed the legacy 3s window.
            state.BeginSpiritmasterDotObservation(node.SkillId, TargetId(beforePress.Target), beforePress.Entries,
                pending.Deadline + TimeSpan.FromSeconds(3));
        }
        await ReadSuppressedRootSkillIdsAsync().ConfigureAwait(false);
    }

    private bool SameTarget(LockedTargetSnapshot current) => current.IsMonsterAlive &&
        current.TargetEntityId == target.TargetEntityId &&
        (current.ServerObjectId == 0 || target.ServerObjectId == 0 || current.ServerObjectId == target.ServerObjectId);

    private static uint TargetId(LockedTargetSnapshot value) =>
        value.ServerObjectId != 0 ? value.ServerObjectId : value.TargetEntityId;
}
