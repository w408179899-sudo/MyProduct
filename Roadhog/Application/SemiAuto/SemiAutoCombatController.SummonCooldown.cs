using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

internal sealed record SummonCooldownObservation(uint SkillId, uint EndTime, uint Duration,
    TimeProvider Clock, long StartedTimestamp);

public sealed partial class SemiAutoCombatController
{
    internal static SemiAutoSkillCooldownReadiness GetSummonCooldownReadiness(
        SkillSnapshot skill, SemiAutoCombatState state, uint osTick)
    {
        if (!state.HasCooldownTickCalibration)
            return skill.CooldownEndTime == 0 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        var gameTick = state.EstimateGameTick(osTick);
        // Summoning cannot use the ordinary attack path's early-ready tolerance:
        // pressing once just before expiry can lose the entire summon attempt.
        var end = state.GetEffectiveCooldownEndTime(skill, gameTick, readyToleranceMs: 0);
        return end != 0 && unchecked((int)(end - gameTick)) > 0
            ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready;
    }

    private async Task<SkillSnapshot?> ReadConfiguredSummonSkillAsync(
        AccountWorkerContext context, SpiritmasterSkillKeyRuleConfig rule)
    {
        var id = rule.SkillId;
        if (id == 0)
        {
            // A manually configured key can still point to an ordinary skill.
            // Read its current slot without changing the user's configuration.
            var quickbar = (await context.Snapshots.ReadQuickbarAsync().ConfigureAwait(false)).Value;
            context.StopToken.ThrowIfCancellationRequested();
            id = quickbar.Slots.FirstOrDefault(slot => slot.ContentType == 21 &&
                string.Equals(SkillKeyBindings.GetSlotKey(slot.Bar, slot.Slot), rule.Key?.Trim(),
                    StringComparison.OrdinalIgnoreCase))?.SkillId ?? 0;
        }
        if (id == 0) return null; // Unmapped manual keys retain their established bounded retry contract.
        var skills = (await context.Snapshots.ReadSkillsAsync(new[] { id }).ConfigureAwait(false)).Value;
        context.StopToken.ThrowIfCancellationRequested();
        return skills.FirstOrDefault(skill => skill.SkillId == id);
    }

    private async Task<bool> ShouldWaitForCoordinatedSummonCooldownAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SpiritmasterSkillSettings spirit)
    {
        var rule = spirit.SummonSkills.Take(2).LastOrDefault(rule => !string.IsNullOrWhiteSpace(rule.Key));
        if (rule is null) return false;
        var skill = await ReadConfiguredSummonSkillAsync(context, rule).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (skill is null) return false;

        // Unlike ordinary attack selection, do not compare two uncalibrated clocks.
        var readiness = GetSummonCooldownReadiness(skill, state, unchecked((uint)Environment.TickCount64));
        var wait = readiness == SemiAutoSkillCooldownReadiness.CoolingDown;
        var reason = "skill_cooldown";
        if (readiness == SemiAutoSkillCooldownReadiness.Unknown && skill.CooldownDuration > 0)
        {
            // A valid duration bounds a cooldown even before the account has calibrated
            // the game clock. A changed endpoint starts a new observation; repeated
            // reads of the same endpoint must not restart the wait forever.
            var observed = state.CoordinatedSummonUnknownCooldown;
            if (observed is null || observed.SkillId != skill.SkillId ||
                observed.EndTime != skill.CooldownEndTime || observed.Duration != skill.CooldownDuration)
                state.CoordinatedSummonUnknownCooldown = observed = new(skill.SkillId,
                    skill.CooldownEndTime, skill.CooldownDuration, _timeProvider, _timeProvider.GetTimestamp());
            wait = observed.Clock.GetElapsedTime(observed.StartedTimestamp) < TimeSpan.FromMilliseconds(skill.CooldownDuration);
            reason = "duration_wait_before_clock_calibration";
        }
        else state.CoordinatedSummonUnknownCooldown = null;

        if (!wait) return false;
        state.CoordinatedSummonDefenseAllowed = true;
        state.CoordinatedSummonDefenseReason = "summon_cooldown";
        var now = DateTimeOffset.Now;
        if (ShouldLog(state.LastCoordinatedSummonCooldownLogAt, now))
        {
            state.LastCoordinatedSummonCooldownLogAt = now;
            context.Logger.Info("combat_flow.summon.cooldown_wait", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId,
                ["skillName"] = skill.Name, ["key"] = rule.Key, ["reason"] = reason,
                ["cooldownDuration"] = skill.CooldownDuration, ["cooldownEndTime"] = skill.CooldownEndTime,
                ["cooldownReadiness"] = readiness.ToString(), ["hasClockCalibration"] = state.HasCooldownTickCalibration
            });
        }
        return true;
    }

    private async Task<bool> ShouldPressCoordinatedSummonSpeedAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SpiritmasterSkillSettings spirit)
    {
        var skill = await ReadConfiguredSummonSkillAsync(context, spirit.SummonSkills[0]).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (skill is null || GetSummonCooldownReadiness(skill, state, unchecked((uint)Environment.TickCount64)) == SemiAutoSkillCooldownReadiness.Ready)
            return true;
        context.Logger.Info("combat_flow.summon.speed_skipped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId,
            ["reason"] = "optional_speed_not_ready", ["cooldownDuration"] = skill.CooldownDuration,
            ["cooldownEndTime"] = skill.CooldownEndTime
        });
        return false;
    }
}
