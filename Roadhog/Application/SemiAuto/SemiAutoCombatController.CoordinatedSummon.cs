using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    // V2 owns this ordering. Legacy keeps its existing summon-first path.
    // A finite summon confirmation owns input until it finishes; between attempts,
    // one configured HP-maintenance turn may precede the next summon attempt.
    private async Task<bool> EnsureCoordinatedSpiritmasterPetAsync(
        AccountWorkerContext context, SemiAutoSkillPlan plan, SemiAutoCombatState state,
        Func<Task>? beforeInput, Func<Task<bool>>? handleRetryDefense)
    {
        context.StopToken.ThrowIfCancellationRequested();
        var spirit = context.Config.ScriptSettings?.Skills?.Spiritmaster ?? new();
        if (!plan.UsesSpiritmasterAutoLogic ||
            !spirit.SummonSkills.Any(rule => !string.IsNullOrWhiteSpace(rule.Key)))
        {
            state.CoordinatedSummonHpTurnUsed = false;
            state.CoordinatedSummonDefenseAllowed = false;
            state.CoordinatedSummonDefenseReason = null;
            state.CoordinatedSummonCooldown = null;
            return false;
        }

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (player.IsDead || player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.ClearSpiritmasterSummonVerification();
            state.CoordinatedSummonHpTurnUsed = false;
            state.CoordinatedSummonDefenseAllowed = false;
            state.CoordinatedSummonDefenseReason = null;
            state.CoordinatedSummonCooldown = null;
            return false;
        }

        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (roster.LocalPlayerPet.Pet is { IsSummoned: true, IsAlive: true } pet)
        {
            if (state.HasPendingSpiritmasterSummonVerification)
                context.Logger.Info("semi_auto.spiritmaster.summon_verified", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["petServerObjectId"] = pet.ServerObjectId,
                    ["petEntityId"] = pet.EntityId, ["petName"] = pet.Name, ["petHpPercent"] = pet.HpPercent
                });
            state.ClearSpiritmasterSummonVerification();
            state.CoordinatedSummonHpTurnUsed = false;
            state.CoordinatedSummonDefenseAllowed = false;
            state.CoordinatedSummonDefenseReason = null;
            state.CoordinatedSummonCooldown = null;
            return false;
        }

        var now = DateTimeOffset.Now;
        if (state.IsAwaitingSpiritmasterSummonVerification(now))
        {
            if (ShouldLog(state.LastSpiritmasterSummonVerifyLogAt, now))
            {
                state.LastSpiritmasterSummonVerifyLogAt = now;
                context.Logger.Info("semi_auto.spiritmaster.summon_waiting",
                    new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
            }
            return true;
        }
        if (state.IsSpiritmasterSummonVerificationExpired(now))
        {
            context.Logger.Warn("semi_auto.spiritmaster.summon_verify_timeout",
                new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
            state.ClearSpiritmasterSummonVerification();
            state.CoordinatedSummonDefenseAllowed = true;
            state.CoordinatedSummonDefenseReason = "defense_after_summon_timeout";
        }

        // Waiting for the game's cooldown has sent no cast and owns no cast window.
        // HP recovery and real-attacker defense remain eligible during this wait.
        var summonCooldown = await GetCoordinatedSummonCooldownDecisionAsync(context, state, spirit).ConfigureAwait(false);
        if (!state.CoordinatedSummonHpTurnUsed &&
            await TryRecoverPlayerHpBeforeSummonAsync(context, plan, state, player, beforeInput).ConfigureAwait(false))
        {
            context.StopToken.ThrowIfCancellationRequested();
            state.CoordinatedSummonHpTurnUsed = true;
            state.QuickbarSkills.CancelInputAttemptsForHandoff();
            context.Logger.Info("combat_flow.summon.hp_turn", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["version"] = "CoordinatedV2",
                ["hpPercent"] = Math.Round(player.HpPercent, 1), ["reason"] = "hp_before_next_summon"
            });
            // Use the normal maintenance input spacing after its bounded confirmation.
            await WaitForMaintenanceGlobalKeyIntervalAsync(state, context.StopToken).ConfigureAwait(false);
            return true;
        }

        // Outside a protected cast, cooldown waits and expired attempts permit
        // the caller to handle an actual attacker after its HP opportunity.
        // The defense owner decides whether any work exists; absence resumes the
        // established retry path. No ordinary combat input enters a pending cast.
        if (state.CoordinatedSummonDefenseAllowed && handleRetryDefense is not null)
        {
            var handled = await handleRetryDefense().ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
            if (handled) return true;
        }

        context.StopToken.ThrowIfCancellationRequested();
        if (summonCooldown == SummonCooldownDecision.Wait) return true;
        now = DateTimeOffset.Now;
        if (!state.ShouldAttemptSpiritmasterSummon(now, SpiritmasterSummonAttemptInterval) ||
            state.CoordinatedSummonHpTurnUsed &&
            state.GetRemainingMaintenanceGlobalInterval(now, MaintenanceGlobalKeyInterval) > TimeSpan.Zero)
            return true;

        // Fair handoff: failed/unconfirmed HP recovery must not starve summoning.
        // Reset before the attempt so a failed posture/input can yield HP next time.
        state.CoordinatedSummonHpTurnUsed = false;
        if (beforeInput is not null) await beforeInput().ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        return await TryPressSpiritmasterSummonAsync(context, state,
            context.Config.ScriptSettings?.SemiAuto ?? new(), spirit).ConfigureAwait(false);
    }

    private async Task<bool> TryRecoverPlayerHpBeforeSummonAsync(
        AccountWorkerContext context, SemiAutoSkillPlan plan, SemiAutoCombatState state,
        PlayerSnapshot player, Func<Task>? beforeInput)
    {
        var rules = context.Config.ScriptSettings?.Maintenance?.HpMaintenanceRules;
        if (!player.IsAlive || rules is null ||
            !rules.Any(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                               player.HpPercent <= Math.Clamp(rule.BelowPercent, 0, 100)))
            return false;

        var target = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        var timing = player.IsCombatStance || target.IsMonsterAlive
            ? MaintenanceRuleRunTiming.InCombat : MaintenanceRuleRunTiming.Always;
        return await TryPressMaintenanceRuleAsync(context, state,
            context.Config.ScriptSettings?.SemiAuto ?? new(), rules, "hp",
            player.CurrentHp, player.MaxHp, player, beforeMaintenanceKeyPress: async () =>
            {
                if (beforeInput is not null) await beforeInput().ConfigureAwait(false);
                context.StopToken.ThrowIfCancellationRequested();
            }, plan: plan, requireCooldownCalibrationForMaintenance: true,
            runTiming: timing, includeAlwaysRules: true).ConfigureAwait(false);
    }
}
