using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private async Task<bool> TryRetryOpeningSkillAsync(AccountWorkerContext context, SemiAutoCombatState state,
        SemiAutoScriptSettings settings, SemiAutoSkillPlan plan, LockedTargetSnapshot target,
        IReadOnlyList<SkillSnapshot> skills)
    {
        state.ObserveOpeningSkillTarget(target);
        if (!plan.HasOpeningSkill)
        {
            state.OpeningSkillRetries.Reset();
            return false;
        }
        if (!target.IsMonsterAlive || state.ShouldHandleOpeningSkill(target)) return false;

        var retries = state.OpeningSkillRetries;
        while (retries.Current is { } attempt)
        {
            context.StopToken.ThrowIfCancellationRequested();
            var node = plan.OpeningSkills.ElementAtOrDefault(attempt.Index);
            var skill = node?.ResolveSkill(skills);
            var reason = retries.IsExpired(_timeProvider) ? "expired"
                : node?.SkillId != attempt.Node.SkillId || node?.Key != attempt.Node.Key ? "plan_changed"
                : skill is null || skill.SkillId != attempt.Baseline.SkillId ? "missing"
                : skill.CooldownEndTime != 0 && unchecked((int)(skill.CooldownEndTime - attempt.Baseline.CooldownEndTime)) > 0
                    ? "cooldown_advanced"
                : SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready
                    ? "not_ready" : null;
            if (reason is null && attempt.StartedAt is { } started &&
                _timeProvider.GetElapsedTime(started) >= ResolveOpeningSkillConfirmationTimeout())
                reason = "confirmation_timeout";
            if (reason is not null)
            {
                LogOpeningRetry(context, target, attempt, "finished", reason);
                retries.Finish();
                continue;
            }

            // Never take input from a pending ordinary action, chain continuation or attack weave.
            if (state.QuickbarSkills.PendingAction is not null || state.QuickbarSkills.ChainTransition is not null ||
                state.QuickbarSkills.AttackWeave.IsWaiting) return false;
            if (attempt.StartedAt is not null) return true;
            if (!retries.IsReady(_timeProvider)) return false;

            var availability = await ReadQuickbarWeaveAvailabilityAsync(context, state, target).ConfigureAwait(false);
            if (availability.CombatState is { IsDead: true }) retries.Reset();
            if (!QuickbarWeaveCombatMatches(target, availability, context.SkillBindings?.Page)) return true;
            var locked = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            if (!locked.IsMonsterAlive || (target.ServerObjectId != 0 && locked.ServerObjectId != 0
                    ? target.ServerObjectId != locked.ServerObjectId : target.TargetEntityId != locked.TargetEntityId))
                return true;

            // Exactly one extra key press. Reserve its confirmation window, then discard it;
            // a failed retry must never enqueue itself again or restart the opening list.
            if (!await PressNodeKeyAsync(context, node!, settings, "opening_skill_retry").ConfigureAwait(false))
            {
                LogOpeningRetry(context, target, attempt, "finished", "input_failed");
                retries.Finish();
                return false;
            }
            retries.Start(_timeProvider);
            LogOpeningRetry(context, target, attempt, "started", "retry_once");
            if (settings.AttackWeaveEnabled)
                state.QuickbarSkills.AttackWeave.TrackOpeningPress(skill!, _timeProvider, ResolveOpeningSkillConfirmationTimeout());
            return true;
        }
        return false;
    }

    private static void LogOpeningRetry(AccountWorkerContext context, LockedTargetSnapshot target,
        OpeningSkillRetryState.Attempt attempt, string phase, string reason) =>
        context.Logger.Info("semi_auto.opening_skill.retry_" + phase, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["targetServerObjectId"] = target.ServerObjectId,
            ["entryIndex"] = attempt.Index, ["skillId"] = attempt.Baseline.SkillId,
            ["skill"] = attempt.Node.Name, ["reason"] = reason
        });
}
