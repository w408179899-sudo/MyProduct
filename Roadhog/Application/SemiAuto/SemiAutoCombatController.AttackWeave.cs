using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private void ResetAttackWeaveAfterIdle(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings)
    {
        if (!settings.AttackWeaveEnabled)
        {
            return;
        }

        var weave = state.AttackWeave;
        var now = _timeProvider.GetUtcNow();
        var lastKeyAt = weave.LastSkillKeyPressedAt;
        var confirmedCount = weave.ConfirmedCount;
        var hadAttempts = weave.HasAttempts;
        if (weave.TryResetAfterIdle(now))
        {
            context.Logger.Info("semi_auto.attack_weave.idle_reset", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["idleMs"] = (long)(now - lastKeyAt!.Value).TotalMilliseconds,
                ["timeoutMs"] = (long)AttackWeaveState.MaximumSkillKeyGap.TotalMilliseconds,
                ["confirmedCount"] = confirmedCount,
                ["hadPendingAttempts"] = hadAttempts,
                ["chainPending"] = state.HasChainWork
            });
        }
    }

    private async Task<bool> HandleAttackWeaveAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IReadOnlyList<SkillSnapshot>? skills = null)
    {
        if (!settings.AttackWeaveEnabled)
        {
            state.FinishAttackWeavePause(_timeProvider.GetUtcNow());
            state.AttackWeave.Reset();
            return false;
        }

        var weave = state.AttackWeave;
        ResetAttackWeaveAfterIdle(context, state, settings);
        var delayMs = Math.Clamp(settings.AttackWeaveDelayMs, 0, SemiAutoScriptSettings.MaximumAttackWeaveDelayMs);
        if (weave.HasAttempts)
        {
            skills ??= await ReadSkillsAsync(context, plan).ConfigureAwait(false);
            // A snapshot read can itself cross the gap deadline. Discard the old pair before
            // accepting late evidence, without waiting for another key to get past CanPress.
            ResetAttackWeaveAfterIdle(context, state, settings);
            var chainActive = !state.IsPendingChainExpired(DateTimeOffset.Now);
            var confirmed = weave.Observe(skills, _timeProvider.GetUtcNow(), delayMs,
                pendingChainSource: chainActive ? state.PendingChainSourceNode : null,
                pendingChainNext: chainActive && state.PendingChainNextPressStarted ? state.PendingChainNextNode : null);
            var confirmedCount = weave.ConfirmedCount - confirmed.Count;
            foreach (var skillId in confirmed)
            {
                context.Logger.Info("semi_auto.attack_weave.skill_confirmed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["skillId"] = skillId,
                    ["confirmedCount"] = ++confirmedCount
                });
            }

            if (confirmed.Count > 0 && weave.WaitStartedAt.HasValue)
            {
                // Process observed cooldowns before yielding for the intentional pause. Otherwise
                // that pause would be misinterpreted as extra cooldown clock offset on the next tick.
                UpdateCooldownCalibration(context, state,
                    skills.Where(skill => confirmed.Contains(skill.SkillId)).ToArray(), CurrentOsTick(), DateTimeOffset.Now);
                context.Logger.Info("semi_auto.attack_weave.wait_started", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["delayMs"] = delayMs,
                    ["chainPending"] = state.HasChainWork
                });
            }
        }

        if (!weave.WaitStartedAt.HasValue)
        {
            return false;
        }

        context.StopToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        if (weave.ShouldPressAttack(now))
        {
            weave.MarkAttackAttempt(now);
            if (await PressAttackKeyAsync(context, state, settings).ConfigureAwait(false))
            {
                state.FinishAttackWeavePause(_timeProvider.GetUtcNow());
                context.Logger.Info("semi_auto.attack_weave.pressed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = AttackKey,
                    ["delayMs"] = delayMs,
                    ["chainPending"] = state.HasChainWork
                });
            }
        }

        // Yield the account worker so life guards and cancellation remain responsive.
        return true;
    }

    private void TrackAttackWeaveSkill(
        SemiAutoCombatState state, SemiAutoScriptSettings settings, SemiAutoSkillPlan plan, SkillSnapshot skill,
        SemiAutoSkillNode? chainNode = null)
    {
        if (settings.AttackWeaveEnabled)
        {
            state.AttackWeave.MarkSkillKeyPressed(_timeProvider.GetUtcNow());
            state.AttackWeave.TrackPress(
                skill, _timeProvider.GetUtcNow() + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic),
                chainNode);
        }
    }

    private async Task<bool> PressAttackWeavePrefixStepAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IReadOnlyList<SkillSnapshot> skills,
        SemiAutoSkillNode? root)
    {
        var owner = root?.NodeKey ?? "trigger_fallback";
        var index = state.AttackWeave.GetPrefixIndex(owner);
        while (index < plan.TriggerPrefixRoots.Count)
        {
            var trigger = plan.TriggerPrefixRoots[index];
            if (ReferenceEquals(trigger, root))
            {
                state.AttackWeave.AdvancePrefix();
                index++;
                continue;
            }

            var skill = trigger.ResolveSkill(skills);
            var canConfirm = skill is not null &&
                SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown;
            if (canConfirm && !state.AttackWeave.CanPress(skill!.SkillId))
            {
                return true;
            }

            if (await PressNodeKeyAsync(context, trigger, settings,
                    root is null ? "trigger_fallback" : "trigger_prefix").ConfigureAwait(false))
            {
                if (canConfirm)
                {
                    TrackAttackWeaveSkill(state, settings, plan, skill!);
                }
                else
                {
                    state.AttackWeave.MarkSkillKeyPressed(_timeProvider.GetUtcNow());
                }

                state.AttackWeave.AdvancePrefix();
            }

            // Observe each prefix skill before sending the next input in this batch.
            return true;
        }

        return false;
    }
}
