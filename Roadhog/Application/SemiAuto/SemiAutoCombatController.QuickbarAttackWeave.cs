using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private async Task<bool> ObserveQuickbarOpeningWeaveAsync(AccountWorkerContext context, SemiAutoCombatState state,
        SemiAutoScriptSettings settings, LockedTargetSnapshot target, IReadOnlyList<SkillSnapshot> skills)
    {
        var weave = state.QuickbarSkills.AttackWeave;
        if (weave.TryResetAfterIdle(_timeProvider)) context.Logger.Info("quickbar_skill.attack_weave.idle_reset");
        if (!weave.HasOpeningAttempt) return true;
        var availability = await ReadQuickbarWeaveAvailabilityAsync(context, state, target).ConfigureAwait(false);
        if (!QuickbarWeaveCombatMatches(target, availability, context.SkillBindings?.Page)) return false;
        if (weave.TryConfirmOpeningRelease(skills, availability, _timeProvider, settings.AttackWeaveDelayMs) is { } skillId)
            QuickbarSkillCombatController.LogAttackWeaveConfirmation(state.QuickbarSkills, skillId, settings, context.Logger);
        return true;
    }

    private async Task<bool> PressQuickbarOpeningSkillIfNeededAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SemiAutoScriptSettings settings, SemiAutoSkillPlan plan,
        LockedTargetSnapshot target, IReadOnlyList<SkillSnapshot>? skills = null)
    {
        if (!settings.AttackWeaveEnabled || !plan.HasOpeningSkill || !state.ShouldHandleOpeningSkill(target))
            return await PressOpeningSkillIfNeededAsync(context, state, settings, plan, target, skills,
                useAttackWeave: false).ConfigureAwait(false);
        var baseline = await ReadQuickbarWeaveAvailabilityAsync(context, state, target).ConfigureAwait(false);
        if (!QuickbarWeaveCombatMatches(target, baseline, context.SkillBindings?.Page)) return true;
        return await PressOpeningSkillIfNeededAsync(context, state, settings, plan, target, skills, useAttackWeave: false,
            onSkillPressed: skill => state.QuickbarSkills.AttackWeave.TrackOpeningPress(skill, baseline,
                _timeProvider, ResolveOpeningSkillConfirmationTimeout())).ConfigureAwait(false);
    }

    private static async Task<SkillAvailabilitySnapshot> ReadQuickbarWeaveAvailabilityAsync(AccountWorkerContext context,
        SemiAutoCombatState state, LockedTargetSnapshot target)
    {
        var reader = (ISkillAvailabilitySnapshotReader)context.Snapshots;
        var published = await reader.ReadSkillAvailabilityAsync(state.QuickbarSkills.LastAvailabilityVersion,
            context.StopToken).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        state.QuickbarSkills.LastAvailabilityVersion = published.Version;
        state.QuickbarSkills.ObserveScope(target, published.Value);
        if (!QuickbarWeaveCombatMatches(target, published.Value, context.SkillBindings?.Page)) state.QuickbarSkills.Reset();
        return published.Value;
    }

    private static bool QuickbarWeaveCombatMatches(LockedTargetSnapshot target, SkillAvailabilitySnapshot availability,
        int? expectedPage = null) => (expectedPage is null || availability.Page == expectedPage) &&
        target.IsMonsterAlive && (availability.CombatState is not { } combat ||
        (combat.IsAlive && combat.TargetEntityId == target.TargetEntityId &&
         (combat.TargetServerObjectId == 0 || target.ServerObjectId == 0 || combat.TargetServerObjectId == target.ServerObjectId)));

    private async Task<TimeSpan> TickQuickbarOpeningAttackKeyLoopAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState state, LockedTargetSnapshot target, SemiAutoScriptSettings settings)
    {
        var tick = Ms(settings.TickIntervalMs, 40);
        context.StopToken.ThrowIfCancellationRequested();
        if (!target.IsMonsterAlive)
        {
            state.QuickbarSkills.AttackWeave.Reset();
            return Ms(settings.TargetIdleDelayMs, 200);
        }
        var openingIds = plan.OpeningSkills.Any(node => node.SkillId == 0) ? null :
            plan.OpeningSkills.Select(node => node.SkillId).Distinct().ToArray();
        var skills = plan.OpeningSkills.Count == 0 ? Array.Empty<SkillSnapshot>() :
            (await context.Snapshots.ReadSkillsAsync(openingIds).ConfigureAwait(false)).Value;
        if (!await ObserveQuickbarOpeningWeaveAsync(context, state, settings, target, skills).ConfigureAwait(false)) return tick;
        if (state.QuickbarSkills.AttackWeave.IsWaiting)
        {
            quickbarSkillController ??= new QuickbarSkillCombatController(_keyboard, _timeProvider);
            if (context.SkillBindings is null) await context.PrepareSkillBindingsAsync().ConfigureAwait(false);
            var emptyPlan = QuickbarSkillPlan.FromSettings(new(), context.SkillBindings!);
            return await quickbarSkillController.TickAsync(emptyPlan, state.QuickbarSkills, target,
                (ISkillAvailabilitySnapshotReader)context.Snapshots,
                ids => ReadQuickbarSkillsAndCalibrateAsync(context, state, ids), settings, context.Logger, context.StopToken,
                readTargetBeforePress: () => ReadLockedTargetAsync(context)).ConfigureAwait(false);
        }
        if (await PressQuickbarOpeningSkillIfNeededAsync(context, state, settings, plan, target, skills).ConfigureAwait(false)) return tick;
        state.MarkOpeningAttackKeyAttempted(target);
        if (settings.AttackKeyLoopEnabled && state.ShouldPressAttackKey(DateTimeOffset.Now, Ms(settings.AttackKeyLoopIntervalMs, 300)))
            await PressAttackKeyAsync(context, state, settings).ConfigureAwait(false);
        return tick;
    }
}
