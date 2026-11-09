using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Per-target diagnostic history only. Never used to select, confirm or retry an action.</summary>
internal sealed class OpeningSkillDiagnostics
{
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(3);
    private sealed class Entry(SemiAutoSkillNode node, DateTimeOffset startedAt)
    {
        public SemiAutoSkillNode Node { get; } = node;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public string? Decision { get; set; }
        public SkillSnapshot? BeforePress { get; set; }
        public SkillSnapshot? LastObserved { get; set; }
        public int PressCount { get; set; }
        public DateTimeOffset LastPressedAt { get; set; }
    }

    private readonly Dictionary<int, Entry> entries = new();
    private uint targetIdentity;
    private (uint Id, uint Time, DateTimeOffset ObservedAt)? lastRelease;
    private bool releaseLoggedAfterPress;

    public void Reset()
    {
        entries.Clear();
        targetIdentity = 0;
        lastRelease = null;
        releaseLoggedAfterPress = false;
    }

    private void ObserveTarget(LockedTargetSnapshot target)
    {
        var identity = !target.IsMonsterAlive ? 0 : target.ServerObjectId != 0
            ? target.ServerObjectId : target.TargetEntityId;
        if (targetIdentity == identity) return;
        Reset();
        targetIdentity = identity;
    }

    public void Decision(AccountWorkerContext context, SemiAutoCombatState state, SemiAutoSkillPlan plan,
        LockedTargetSnapshot target, SemiAutoSkillNode node, DateTimeOffset startedAt,
        SkillSnapshot? skill, string decision)
    {
        ObserveTarget(target);
        var index = state.OpeningSkillIndex;
        if (!entries.TryGetValue(index, out var entry) || entry.StartedAt != startedAt)
            entries[index] = entry = new(node, startedAt);
        entry.LastObserved = skill;
        if (entry.Decision == decision) return;
        entry.Decision = decision;
        var fields = Fields(context, target, index, entry);
        fields["decision"] = decision;
        fields["clockCalibrated"] = state.HasCooldownTickCalibration;
        fields["entryCount"] = plan.OpeningSkills.Count;
        fields["releaseAll"] = plan.ReleaseAllOpeningSkills;
        context.Logger.Info("semi_auto.opening_skill.decision", fields);
    }

    public void Pressed(AccountWorkerContext context, LockedTargetSnapshot target, int index, SkillSnapshot skill)
    {
        if (!entries.TryGetValue(index, out var entry)) return;
        entry.BeforePress ??= skill;
        entry.PressCount++;
        entry.LastPressedAt = DateTimeOffset.Now;
        releaseLoggedAfterPress = false;
        var fields = Fields(context, target, index, entry);
        fields["inputSucceeded"] = true;
        fields["releaseBaselineObserved"] = lastRelease.HasValue;
        fields["lastObservedReleasedSkillId"] = lastRelease?.Id;
        fields["lastObservedReleasedSkillTime"] = lastRelease?.Time;
        fields["releaseBaselineAgeMs"] = lastRelease.HasValue
            ? (long)(DateTimeOffset.Now - lastRelease.Value.ObservedAt).TotalMilliseconds : null;
        fields["confirmationMode"] = skill.CooldownDuration == 0 ? "none_zero_duration" : "cooldown";
        context.Logger.Info("semi_auto.opening_skill.input_sent", fields);
    }

    public void Finished(AccountWorkerContext context, SemiAutoCombatState state, SemiAutoSkillPlan plan,
        LockedTargetSnapshot target, string reason)
    {
        if (!entries.TryGetValue(state.OpeningSkillIndex, out var entry)) return;
        var fields = Fields(context, target, state.OpeningSkillIndex, entry);
        fields["reason"] = reason;
        fields["wasPressed"] = state.OpeningSkillWasPressed;
        fields["clockCalibrated"] = state.HasCooldownTickCalibration;
        fields["releaseAll"] = plan.ReleaseAllOpeningSkills;
        fields["nextAction"] = state.OpeningSkillWasPressed && !plan.ReleaseAllOpeningSkills
            ? "mark_target_handled" : "advance_entry";
        fields["elapsedMs"] = (long)(DateTimeOffset.Now - entry.StartedAt).TotalMilliseconds;
        fields["releaseConfirmationPerformedByDiagnostics"] = false;
        context.Logger.Info("semi_auto.opening_skill.entry_finished", fields);
    }

    public void ObserveSkills(AccountWorkerContext context, LockedTargetSnapshot target, IReadOnlyList<SkillSnapshot> skills)
    {
        ObserveTarget(target);
        foreach (var (index, entry) in entries)
        {
            if (!IsObserving(entry)) continue;
            var skill = entry.Node.ResolveSkill(skills);
            if (skill is null || (skill.CooldownDuration == entry.LastObserved?.CooldownDuration &&
                skill.CooldownEndTime == entry.LastObserved?.CooldownEndTime)) continue;
            entry.LastObserved = skill;
            var fields = Fields(context, target, index, entry);
            fields["baselineCooldownEndTime"] = entry.BeforePress?.CooldownEndTime;
            fields["cooldownAdvancedSincePress"] = entry.BeforePress is { } before &&
                unchecked((int)(skill.CooldownEndTime - before.CooldownEndTime)) > 0;
            fields["evidence"] = "sample_only_not_attempt_confirmation";
            context.Logger.Info("semi_auto.opening_skill.cooldown_observed", fields);
        }
    }

    public void ObserveAvailability(AccountWorkerContext context, LockedTargetSnapshot target,
        PublishedGameSnapshot<SkillAvailabilitySnapshot> published)
    {
        ObserveTarget(target);
        var value = published.Value;
        if (!target.IsMonsterAlive || (context.SkillBindings is { } bindings && bindings.Page != value.Page) ||
            (value.CombatState is { } combat && (!combat.IsAlive || combat.TargetEntityId != target.TargetEntityId ||
                (combat.TargetServerObjectId != 0 && target.ServerObjectId != 0 && combat.TargetServerObjectId != target.ServerObjectId))))
            return;
        var previous = lastRelease;
        lastRelease = (value.LastReleasedSkillId, value.LastReleasedSkillTime, DateTimeOffset.Now);
        if (!entries.Values.Any(IsObserving) ||
            (releaseLoggedAfterPress && previous?.Id == value.LastReleasedSkillId && previous?.Time == value.LastReleasedSkillTime)) return;
        releaseLoggedAfterPress = true;
        // A last-release snapshot may be late, stale or superseded. Report the raw
        // observation, never infer that a particular opening attempt succeeded/failed.
        context.Logger.Info("semi_auto.opening_skill.release_observed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetEntityId"] = target.TargetEntityId,
            ["version"] = published.Version,
            ["page"] = value.Page,
            ["lastReleasedSkillId"] = value.LastReleasedSkillId,
            ["lastReleasedSkillTime"] = value.LastReleasedSkillTime,
            ["previousObservedReleasedSkillId"] = previous?.Id,
            ["previousObservedReleasedSkillTime"] = previous?.Time,
            ["matchingPressedEntryIndexes"] = string.Join(",", entries.Where(pair => IsObserving(pair.Value) &&
                pair.Value.BeforePress?.SkillId == value.LastReleasedSkillId).Select(pair => pair.Key)),
            ["evidence"] = "sample_only_not_attempt_confirmation"
        });
    }

    public ISkillAvailabilitySnapshotReader ObserveReads(ISkillAvailabilitySnapshotReader reader,
        AccountWorkerContext context, LockedTargetSnapshot target) => new ObservingReader(reader, this, context, target);

    private static bool IsObserving(Entry entry) => entry.PressCount > 0 &&
        DateTimeOffset.Now - entry.LastPressedAt <= ObservationWindow;

    private sealed class ObservingReader(ISkillAvailabilitySnapshotReader reader, OpeningSkillDiagnostics diagnostics,
        AccountWorkerContext context, LockedTargetSnapshot target) : ISkillAvailabilitySnapshotReader
    {
        public async Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            var published = await reader.ReadSkillAvailabilityAsync(afterVersion, cancellationToken).ConfigureAwait(false);
            diagnostics.ObserveAvailability(context, target, published);
            return published;
        }
    }

    private static Dictionary<string, object?> Fields(AccountWorkerContext context, LockedTargetSnapshot target,
        int index, Entry entry) => new()
    {
        ["account"] = context.Config.AccountName,
        ["targetServerObjectId"] = target.ServerObjectId,
        ["targetEntityId"] = target.TargetEntityId,
        ["entryIndex"] = index,
        ["entryStartedAt"] = entry.StartedAt,
        ["skillId"] = entry.Node.SkillId,
        ["skill"] = entry.Node.Name,
        ["key"] = entry.Node.Key,
        ["pressCount"] = entry.PressCount,
        ["msSinceLastPress"] = entry.PressCount > 0 ? (long)(DateTimeOffset.Now - entry.LastPressedAt).TotalMilliseconds : null,
        ["matchedSkillId"] = entry.LastObserved?.SkillId,
        ["cooldownDuration"] = entry.LastObserved?.CooldownDuration,
        ["cooldownEndTime"] = entry.LastObserved?.CooldownEndTime
    };
}
