using Roadhog.Application.AbnormalStatuses;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private async Task<bool> ExecuteLegacyDpMaintenanceKeyRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        DpMaintenanceRuleConfig rule,
        string resource,
        PlayerSnapshot player,
        int requiredDp,
        Func<Task>? beforeMaintenanceKeyPress,
        SkillSnapshot? maintenanceSkill)
    {
        if (beforeMaintenanceKeyPress is not null)
        {
            await beforeMaintenanceKeyPress().ConfigureAwait(false);
        }

        if (!await EnsureStandingBeforeMaintenanceKeyAsync(
                    context,
                    state,
                    settings,
                    player,
                    resource,
                    rule.Key)
                .ConfigureAwait(false))
        {
            return false;
        }

        var selectedSkill = (maintenanceSkill?.SkillId ?? rule.SkillId, maintenanceSkill?.Name ?? rule.SkillName);
        var baselineSkills = await ReadMaintenanceSkillsAsync(context, new[] { selectedSkill }).ConfigureAwait(false);
        var baselineCooldowns = SnapshotCooldownEndTimes(baselineSkills);
        var startedAt = DateTimeOffset.Now;
        var deadline = startedAt + MaintenanceConfirmWindow;
        var attempts = 0;
        SkillSnapshot? confirmedSkill = null;

        while (DateTimeOffset.Now <= deadline)
        {
            attempts++;
            var result = await _keyboard
                .PressKeyAsync(rule.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                LogMaintenanceKeyFailure(context, state, rule.Key, resource, result.Error);
                return false;
            }

            state.MarkMaintenanceKeyAttempted(rule.Key, DateTimeOffset.Now);
            if (baselineCooldowns.Count > 0)
            {
                var skills = await ReadMaintenanceSkillsAsync(context, new[] { selectedSkill }).ConfigureAwait(false);
                if (TryFindAdvancedCooldown(baselineCooldowns, skills, maintenanceSkill, out confirmedSkill))
                {
                    TryUpdateMaintenanceCooldownCalibration(context, state, confirmedSkill);
                    break;
                }
            }

            var remaining = deadline - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < MaintenanceConfirmPollInterval
                ? remaining
                : MaintenanceConfirmPollInterval;
            await Task.Delay(delay, context.StopToken).ConfigureAwait(false);
        }

        var completedAt = DateTimeOffset.Now;
        if (confirmedSkill is null)
        {
            context.Logger.Warn("semi_auto.maintenance.dp_unconfirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["resource"] = resource,
                ["key"] = rule.Key,
                ["currentDp"] = player.CurrentDp,
                ["requiredDp"] = requiredDp,
                ["attempts"] = attempts,
                ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
                ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds),
                ["baselineReadSuccess"] = true,
                ["baselineError"] = null,
                ["maintenanceSkillId"] = maintenanceSkill?.SkillId,
                ["maintenanceSkillName"] = maintenanceSkill?.Name
            });
            return true;
        }

        state.MarkMaintenanceKeyAttempted(rule.Key, completedAt);
        context.Logger.Info("semi_auto.maintenance.dp_key_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["currentDp"] = player.CurrentDp,
            ["requiredDp"] = requiredDp,
            ["attempts"] = attempts,
            ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
            ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds),
            ["confirmed"] = true,
            ["confirmedSkillId"] = confirmedSkill.SkillId,
            ["confirmedSkillName"] = confirmedSkill.Name,
            ["confirmedCooldownEndTime"] = confirmedSkill.CooldownEndTime
        });
        return true;
    }

    private async Task<bool> ExecuteLegacyMaintenanceKeyRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceKeyRuleConfig rule,
        string resource,
        uint current,
        uint max,
        double percent,
        int threshold,
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress,
        SkillSnapshot? maintenanceSkill,
        Func<Task<bool>>? ensureTargetBeforeKeyPress)
    {
        if (beforeMaintenanceKeyPress is not null)
        {
            await beforeMaintenanceKeyPress().ConfigureAwait(false);
        }

        if (!await EnsureStandingBeforeMaintenanceKeyAsync(
                    context,
                    state,
                    settings,
                    player,
                    resource,
                    rule.Key)
                .ConfigureAwait(false))
        {
            return false;
        }

        var selectedSkill = (maintenanceSkill?.SkillId ?? rule.SkillId, maintenanceSkill?.Name ?? rule.SkillName);
        var baselineSkills = await ReadMaintenanceSkillsAsync(context, new[] { selectedSkill }).ConfigureAwait(false);
        var baselineCooldowns = SnapshotCooldownEndTimes(baselineSkills);
        var startedAt = DateTimeOffset.Now;
        var deadline = startedAt + MaintenanceConfirmWindow;
        var attempts = 0;
        SkillSnapshot? confirmedSkill = null;
        var requiresLifeDrainTarget = RequiresLifeDrainTarget(resource, maintenanceSkill, rule.SkillId);
        LockedTargetSnapshot? lifeDrainTarget = null;

        while (DateTimeOffset.Now <= deadline)
        {
            if (ensureTargetBeforeKeyPress is not null &&
                !await ensureTargetBeforeKeyPress().ConfigureAwait(false))
            {
                state.ClearMaintenanceKeyAttempt(rule.Key);
                context.Logger.Info("semi_auto.maintenance.target_guard_blocked", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["resource"] = resource,
                    ["key"] = rule.Key,
                    ["maintenanceSkillId"] = maintenanceSkill?.SkillId,
                    ["maintenanceSkillName"] = maintenanceSkill?.Name
                });
                return true;
            }

            // Pet selection recovery runs first; then validate the official target
            // on every attempt, including the first one after baseline reads.
            if (requiresLifeDrainTarget)
            {
                var target = await ReadLockedTargetForConfirmationAsync(context).ConfigureAwait(false);
                var reason = GetLifeDrainTargetBlockReason(target, lifeDrainTarget);
                if (reason is not null)
                {
                    state.ClearMaintenanceKeyAttempt(rule.Key);
                    LogLifeDrainTargetBlocked(context, rule, maintenanceSkill, target, lifeDrainTarget,
                        reason, attempts, startedAt);
                    // An Always/AfterCombat rule without a live enemy must not
                    // reserve idle maintenance and prevent acquiring a new target.
                    return attempts > 0 || rule.RunTiming == MaintenanceRuleRunTiming.InCombat;
                }

                lifeDrainTarget ??= target;
            }

            attempts++;
            var result = await _keyboard
                .PressKeyAsync(rule.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                LogMaintenanceKeyFailure(context, state, rule.Key, resource, result.Error);
                return false;
            }

            state.MarkMaintenanceKeyAttempted(rule.Key, DateTimeOffset.Now);
            if (baselineCooldowns.Count > 0)
            {
                var skills = await ReadMaintenanceSkillsAsync(context, new[] { selectedSkill }).ConfigureAwait(false);
                if (TryFindAdvancedCooldown(baselineCooldowns, skills, maintenanceSkill, out confirmedSkill))
                {
                    TryUpdateMaintenanceCooldownCalibration(context, state, confirmedSkill);
                    break;
                }
            }

            var remaining = deadline - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < MaintenanceConfirmPollInterval
                ? remaining
                : MaintenanceConfirmPollInterval;
            await Task.Delay(delay, context.StopToken).ConfigureAwait(false);
        }

        var completedAt = DateTimeOffset.Now;
        if (confirmedSkill is null)
        {
            context.Logger.Warn("semi_auto.maintenance.unconfirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["resource"] = resource,
                ["key"] = rule.Key,
                ["current"] = current,
                ["max"] = max,
                ["percent"] = Math.Round(percent, 1),
                ["belowPercent"] = threshold,
                ["attempts"] = attempts,
                ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
                ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds),
                ["baselineReadSuccess"] = true,
                ["baselineError"] = null,
                ["maintenanceSkillId"] = maintenanceSkill?.SkillId,
                ["maintenanceSkillName"] = maintenanceSkill?.Name
            });
            return true;
        }

        state.MarkMaintenanceKeyAttempted(rule.Key, completedAt);
        context.Logger.Info("semi_auto.maintenance.key_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["current"] = current,
            ["max"] = max,
            ["percent"] = Math.Round(percent, 1),
            ["belowPercent"] = threshold,
            ["attempts"] = attempts,
            ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
            ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds),
            ["confirmed"] = true,
            ["confirmedSkillId"] = confirmedSkill.SkillId,
            ["confirmedSkillName"] = confirmedSkill.Name,
            ["confirmedCooldownEndTime"] = confirmedSkill.CooldownEndTime
        });
        return true;
    }

}
