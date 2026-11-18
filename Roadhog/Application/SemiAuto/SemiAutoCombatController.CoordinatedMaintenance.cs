using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    // Keep the existing entry points and bool contract for callers and reflection-based tests.
    private async Task<bool> ExecuteDpMaintenanceKeyRuleAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SemiAutoScriptSettings settings, DpMaintenanceRuleConfig rule,
        string resource, PlayerSnapshot player, int requiredDp, Func<Task>? beforeMaintenanceKeyPress,
        SkillSnapshot? maintenanceSkill)
    {
        if (context.Config.CombatFlowVersion != CombatFlowVersion.CoordinatedV2)
            return await ExecuteLegacyDpMaintenanceKeyRuleAsync(context, state, settings, rule, resource,
                player, requiredDp, beforeMaintenanceKeyPress, maintenanceSkill).ConfigureAwait(false);

        var petGuard = await CreatePetSkillGuardAsync(context, state, maintenanceSkill, rule.SkillId, rule.SkillName, rule.Key).ConfigureAwait(false);
        if (petGuard is not null && !await petGuard.CheckAsync().ConfigureAwait(false)) return false;
        async Task<MaintenanceAttemptPermission> Guard(int attempts, DateTimeOffset _) =>
            petGuard is not null && !await petGuard.CheckAsync().ConfigureAwait(false)
                ? new(false, attempts > 0) : MaintenanceAttemptPermission.Allow;
        var result = await ConfirmMaintenanceKeyAsync(context, state, settings, rule.Key, resource, player,
            beforeMaintenanceKeyPress, maintenanceSkill, rule.SkillId, rule.SkillName, Guard).ConfigureAwait(false);
        LogMaintenanceConfirmation(context, state, rule.Key, resource, maintenanceSkill, result,
            "semi_auto.maintenance.dp_", new() { ["currentDp"] = player.CurrentDp, ["requiredDp"] = requiredDp });
        return result.ConsumesTick;
    }

    private async Task<bool> ExecuteMaintenanceKeyRuleAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SemiAutoScriptSettings settings, MaintenanceKeyRuleConfig rule,
        string resource, uint current, uint max, double percent, int threshold, PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress, SkillSnapshot? maintenanceSkill,
        Func<Task<bool>>? ensureTargetBeforeKeyPress)
    {
        if (context.Config.CombatFlowVersion != CombatFlowVersion.CoordinatedV2)
            return await ExecuteLegacyMaintenanceKeyRuleAsync(context, state, settings, rule, resource,
                current, max, percent, threshold, player, beforeMaintenanceKeyPress, maintenanceSkill,
                ensureTargetBeforeKeyPress).ConfigureAwait(false);

        var requiresTarget = RequiresLifeDrainTarget(resource, maintenanceSkill, rule.SkillId);
        var petGuard = await CreatePetSkillGuardAsync(context, state, maintenanceSkill, rule.SkillId, rule.SkillName, rule.Key).ConfigureAwait(false);
        if (petGuard is not null && !await petGuard.CheckAsync().ConfigureAwait(false)) return false;
        LockedTargetSnapshot? baselineTarget = null;
        async Task<MaintenanceAttemptPermission> Guard(int attempts, DateTimeOffset startedAt)
        {
            if (ensureTargetBeforeKeyPress is not null && !await ensureTargetBeforeKeyPress().ConfigureAwait(false))
            {
                state.ClearMaintenanceKeyAttempt(rule.Key);
                context.Logger.Info("semi_auto.maintenance.target_guard_blocked", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["resource"] = resource, ["key"] = rule.Key,
                    ["maintenanceSkillId"] = maintenanceSkill?.SkillId, ["maintenanceSkillName"] = maintenanceSkill?.Name
                });
                return new(false, true);
            }
            if (requiresTarget)
            {
                var target = await ReadLockedTargetForConfirmationAsync(context).ConfigureAwait(false);
                var reason = GetLifeDrainTargetBlockReason(target, baselineTarget);
                if (reason is not null)
                {
                    state.ClearMaintenanceKeyAttempt(rule.Key);
                    LogLifeDrainTargetBlocked(context, rule, maintenanceSkill, target, baselineTarget, reason, attempts, startedAt);
                    return new(false, attempts > 0 || rule.RunTiming == MaintenanceRuleRunTiming.InCombat);
                }
                baselineTarget ??= target;
            }
            if (string.Equals(resource, "mp", StringComparison.OrdinalIgnoreCase) &&
                await TryYieldMpToPetHpAsync(context, state, settings, rule.Key, attempts).ConfigureAwait(false))
                return new(false, true);
            if (petGuard is not null && !await petGuard.CheckAsync().ConfigureAwait(false))
            {
                state.ClearMaintenanceKeyAttempt(rule.Key);
                return new(false, attempts > 0);
            }
            return MaintenanceAttemptPermission.Allow;
        }

        var result = await ConfirmMaintenanceKeyAsync(context, state, settings, rule.Key, resource, player,
            beforeMaintenanceKeyPress, maintenanceSkill, rule.SkillId, rule.SkillName, Guard).ConfigureAwait(false);
        LogMaintenanceConfirmation(context, state, rule.Key, resource, maintenanceSkill, result,
            "semi_auto.maintenance.", new()
            { ["current"] = current, ["max"] = max, ["percent"] = Math.Round(percent, 1), ["belowPercent"] = threshold });
        return result.ConsumesTick;
    }

    private async Task<MaintenanceActionResult> ConfirmMaintenanceKeyAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SemiAutoScriptSettings settings, string key, string resource,
        PlayerSnapshot player, Func<Task>? beforeInput, SkillSnapshot? skill, uint configuredId,
        string configuredName, Func<int, DateTimeOffset, Task<MaintenanceAttemptPermission>>? guard)
    {
        if (beforeInput is not null) await beforeInput().ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (!await EnsureStandingBeforeMaintenanceKeyAsync(context, state, settings, player, resource, key).ConfigureAwait(false))
            return new(MaintenanceActionOutcome.PosturePending, false, 0, DateTimeOffset.Now, DateTimeOffset.Now);

        var selected = (skill?.SkillId ?? configuredId, skill?.Name ?? configuredName);
        var baseline = SnapshotCooldownEndTimes(await ReadMaintenanceSkillsAsync(context, new[] { selected }).ConfigureAwait(false));
        var actions = new MaintenanceConfirmationActions(this, context, state, settings, key, resource, skill, selected, baseline, guard);
        return await MaintenanceConfirmation.RunAsync(actions, MaintenanceConfirmWindow, MaintenanceConfirmPollInterval).ConfigureAwait(false);
    }

    private static void LogMaintenanceConfirmation(AccountWorkerContext context, SemiAutoCombatState state,
        string key, string resource, SkillSnapshot? skill, MaintenanceActionResult result,
        string eventPrefix, Dictionary<string, object?> fields)
    {
        if (result.Outcome is not (MaintenanceActionOutcome.Confirmed or MaintenanceActionOutcome.Unconfirmed)) return;
        fields["account"] = context.Config.AccountName;
        fields["resource"] = resource;
        fields["key"] = key;
        fields["attempts"] = result.Attempts;
        fields["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds;
        fields["confirmElapsedMs"] = (long)Math.Max(0, (result.CompletedAt - result.StartedAt).TotalMilliseconds);
        if (result.ConfirmedSkill is not { } confirmed)
        {
            fields["baselineReadSuccess"] = true;
            fields["baselineError"] = null;
            fields["maintenanceSkillId"] = skill?.SkillId;
            fields["maintenanceSkillName"] = skill?.Name;
            context.Logger.Warn(eventPrefix + "unconfirmed", fields);
            return;
        }
        state.MarkMaintenanceKeyAttempted(key, result.CompletedAt);
        fields["confirmed"] = true;
        fields["confirmedSkillId"] = confirmed.SkillId;
        fields["confirmedSkillName"] = confirmed.Name;
        fields["confirmedCooldownEndTime"] = confirmed.CooldownEndTime;
        context.Logger.Info(eventPrefix + "key_pressed", fields);
    }

    private sealed class MaintenanceConfirmationActions(SemiAutoCombatController owner,
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        string key, string resource, SkillSnapshot? skill, (uint, string) selected,
        IReadOnlyDictionary<uint, uint> baseline,
        Func<int, DateTimeOffset, Task<MaintenanceAttemptPermission>>? guard) : IMaintenanceConfirmationActions
    {
        public CancellationToken StopToken => context.StopToken;
        public DateTimeOffset Now => DateTimeOffset.Now;
        public Task<MaintenanceAttemptPermission> BeforeAttemptAsync(int attempts, DateTimeOffset startedAt) =>
            guard?.Invoke(attempts, startedAt) ?? Task.FromResult(MaintenanceAttemptPermission.Allow);
        public async Task<bool> PressAsync()
        {
            var result = await owner._keyboard.PressKeyAsync(key, Ms(settings.KeyHoldMs, 25), StopToken).ConfigureAwait(false);
            if (!result.Success) { LogMaintenanceKeyFailure(context, state, key, resource, result.Error); return false; }
            state.MarkMaintenanceKeyAttempted(key, Now);
            return true;
        }
        public async Task<SkillSnapshot?> ConfirmAsync()
        {
            if (baseline.Count == 0) return null;
            var skills = await ReadMaintenanceSkillsAsync(context, new[] { selected }).ConfigureAwait(false);
            if (!TryFindAdvancedCooldown(baseline, skills, skill, out var confirmed)) return null;
            TryUpdateMaintenanceCooldownCalibration(context, state, confirmed);
            return confirmed;
        }
        public Task DelayAsync(TimeSpan delay) => Task.Delay(delay, StopToken);
    }
}
