using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

internal sealed record SummonCooldownObservation(uint SkillId, uint EndTime, uint Duration,
    TimeProvider Clock, long StartedTimestamp, bool TimeoutLogged = false);

public sealed partial class SemiAutoCombatController
{
    private const int SummonCooldownTimeoutGraceMs = 5_000;

    private enum SummonCooldownDecision { Ready, Wait, TimedOut }

    internal static SemiAutoSkillCooldownReadiness GetSummonCooldownReadiness(
        SkillSnapshot skill, SemiAutoCombatState state, uint osTick) =>
        GetSummonCooldownReadiness(skill, state, osTick, out _);

    private static SemiAutoSkillCooldownReadiness GetSummonCooldownReadiness(
        SkillSnapshot skill, SemiAutoCombatState state, uint osTick, out uint end)
    {
        end = skill.CooldownEndTime;
        if (!state.HasCooldownTickCalibration)
            return end == 0 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        var gameTick = state.EstimateGameTick(osTick);
        // Summoning cannot use the ordinary attack path's early-ready tolerance:
        // pressing once just before expiry can lose the entire summon attempt.
        end = state.GetEffectiveCooldownEndTime(skill, gameTick, readyToleranceMs: 0);
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

    private async Task<SummonCooldownDecision> GetCoordinatedSummonCooldownDecisionAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SpiritmasterSkillSettings spirit)
    {
        var rule = spirit.SummonSkills.Take(2).LastOrDefault(rule => !string.IsNullOrWhiteSpace(rule.Key));
        var skill = rule is null ? null : await ReadConfiguredSummonSkillAsync(context, rule).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (rule is null || skill is null)
        {
            state.CoordinatedSummonCooldown = null;
            return SummonCooldownDecision.Ready;
        }

        var readiness = GetSummonCooldownReadiness(skill, state, CurrentOsTick(), out var effectiveEnd);
        var clockUnknown = readiness == SemiAutoSkillCooldownReadiness.Unknown;
        if (readiness == SemiAutoSkillCooldownReadiness.Ready || (clockUnknown && skill.CooldownDuration == 0))
        {
            state.CoordinatedSummonCooldown = null;
            return SummonCooldownDecision.Ready;
        }

        if (skill.CooldownDuration > 0)
        {
            // The same endpoint cannot keep a summon waiting past its duration and grace,
            // even if an old clock calibration still reports time remaining.
            var observed = state.CoordinatedSummonCooldown;
            if (observed is null || observed.SkillId != skill.SkillId ||
                observed.EndTime != effectiveEnd || observed.Duration != skill.CooldownDuration)
                state.CoordinatedSummonCooldown = observed = new(skill.SkillId,
                    effectiveEnd, skill.CooldownDuration, _timeProvider, _timeProvider.GetTimestamp());

            var limitMs = (long)skill.CooldownDuration + (clockUnknown ? 0 : SummonCooldownTimeoutGraceMs);
            var elapsed = observed.Clock.GetElapsedTime(observed.StartedTimestamp);
            if (elapsed >= TimeSpan.FromMilliseconds(limitMs))
            {
                if (clockUnknown) return SummonCooldownDecision.Ready;
                if (!observed.TimeoutLogged)
                {
                    state.CoordinatedSummonCooldown = observed with { TimeoutLogged = true };
                    context.Logger.Warn("combat_flow.summon.cooldown_timeout", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId,
                        ["skillName"] = skill.Name, ["key"] = rule.Key,
                        ["cooldownDuration"] = skill.CooldownDuration, ["cooldownEndTime"] = effectiveEnd,
                        ["elapsedMs"] = (long)elapsed.TotalMilliseconds, ["limitMs"] = limitMs
                    });
                }
                return SummonCooldownDecision.TimedOut;
            }
        }
        else state.CoordinatedSummonCooldown = null;

        state.CoordinatedSummonDefenseAllowed = true;
        state.CoordinatedSummonDefenseReason = "summon_cooldown";
        var now = DateTimeOffset.Now;
        if (ShouldLog(state.LastCoordinatedSummonCooldownLogAt, now))
        {
            state.LastCoordinatedSummonCooldownLogAt = now;
            context.Logger.Info("combat_flow.summon.cooldown_wait", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId,
                ["skillName"] = skill.Name, ["key"] = rule.Key,
                ["reason"] = clockUnknown ? "duration_wait_before_clock_calibration" : "skill_cooldown",
                ["cooldownDuration"] = skill.CooldownDuration, ["cooldownEndTime"] = skill.CooldownEndTime,
                ["cooldownReadiness"] = readiness.ToString(), ["hasClockCalibration"] = state.HasCooldownTickCalibration
            });
        }
        return SummonCooldownDecision.Wait;
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
