using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class QuickbarSkillCombatController
{
    internal static void LogAttackWeaveConfirmation(QuickbarSkillCombatState state, uint skillId,
        SemiAutoScriptSettings settings, IRoadhogLogger? logger)
    {
        logger?.Info("quickbar_skill.attack_weave.skill_confirmed", new Dictionary<string, object?>
        {
            ["skillId"] = skillId, ["confirmedCount"] = state.AttackWeave.ConfirmedCount
        });
        if (state.AttackWeave.IsWaiting)
            logger?.Info("quickbar_skill.attack_weave.wait_started", new Dictionary<string, object?>
            {
                ["delayMs"] = Math.Clamp(settings.AttackWeaveDelayMs, 0, SemiAutoScriptSettings.MaximumAttackWeaveDelayMs)
            });
    }

    private async Task<bool> HandleAttackWeaveAsync(QuickbarSkillCombatState state, LockedTargetSnapshot target,
        SkillAvailabilitySnapshot availability, ISkillAvailabilitySnapshotReader availabilityReader,
        SemiAutoScriptSettings settings, Func<SkillAvailabilitySnapshot, bool> observe,
        Func<Task<LockedTargetSnapshot>>? readTargetBeforePress, IRoadhogLogger? logger,
        CancellationToken cancellationToken)
    {
        if (!settings.AttackWeaveEnabled || !state.AttackWeave.IsWaiting) return false;
        if (!state.AttackWeave.ShouldPressAttack(_timeProvider)) return true;

        if (availability.CombatState is null && readTargetBeforePress is not null)
        {
            var currentTarget = await readTargetBeforePress().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!SameTarget(target, currentTarget))
            {
                state.Reset();
                state.YieldToWorker = true;
                logger?.Info("quickbar_skill.target.changed_before_press");
                return true;
            }
        }
        var beforePress = await availabilityReader.ReadSkillAvailabilityAsync(
            state.LastAvailabilityVersion, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        state.LastAvailabilityVersion = beforePress.Version;
        if (!observe(beforePress.Value) || !state.AttackWeave.IsWaiting) return true;
        if (!settings.AttackWeaveEnabled)
        {
            state.AttackWeave.Reset();
            return true;
        }

        state.AttackWeave.MarkAttackAttempt(_timeProvider);
        var result = await _keyboard.PressKeyAsync("C",
            TimeSpan.FromMilliseconds(Math.Clamp(settings.KeyHoldMs, 1, 30)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Success)
        {
            state.AttackWeave.Reset();
            logger?.Info("quickbar_skill.attack_weave.pressed");
        }
        else logger?.Warn("quickbar_skill.attack_weave.press_failed", new Dictionary<string, object?> { ["error"] = result.Error });
        // Keep the executor serial. Even with zero delay, C consumes this tick's one input.
        return true;
    }
}
