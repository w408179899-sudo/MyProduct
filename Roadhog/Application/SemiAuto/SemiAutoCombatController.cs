using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatController
{
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaintenanceConfirmWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaintenanceConfirmPollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MaintenanceKeyRetryInterval = TimeSpan.FromSeconds(3);
    private static readonly string AttackKey = "C";
    private static readonly string RestEnterKey = "OemComma";
    private static readonly string RestExitKey = "X";

    private readonly IKeyboardInput _keyboard;

    public SemiAutoCombatController(IKeyboardInput keyboard)
    {
        _keyboard = keyboard;
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var now = DateTimeOffset.Now;

        if (await TryHandleMaintenanceAsync(
                    context,
                    state,
                    allowSitMaintenance: false,
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance)
                .ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        if (!plan.HasExecutableSkills)
        {
            state.ResetOpeningAttackKey();
            state.ResetAttackKeyPressThrottle();
            if (ShouldLog(state.LastPlanWarningAt, now))
            {
                state.LastPlanWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.plan.empty",
                    new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        var targetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!targetResult.Success || targetResult.Value is null)
        {
            state.ResetOpeningAttackKey();
            state.ResetAttackKeyPressThrottle();
            if (ShouldLog(state.LastTargetWarningAt, now))
            {
                state.LastTargetWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.target.read_failed",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["error"] = targetResult.Error
                    });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        if (state.ObserveTarget(targetResult.Value, out var killedTargetEntityId))
        {
            context.RuntimeStates.MarkKill(context.Config.AccountName);
            context.Logger.Info("semi_auto.target.kill_counted", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = killedTargetEntityId,
                ["targetName"] = targetResult.Value.Name
            });
        }

        if (!targetResult.Value.IsMonsterAlive)
        {
            state.ResetOpeningAttackKey();
            state.ResetAttackKeyPressThrottle();
            if (ShouldLog(state.LastTargetStateLogAt, now))
            {
                state.LastTargetStateLogAt = now;
                context.Logger.Info(
                    "semi_auto.target.not_attackable",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["hasTarget"] = targetResult.Value.HasTarget,
                        ["targetEntityId"] = targetResult.Value.TargetEntityId,
                        ["targetName"] = targetResult.Value.Name,
                        ["objectType"] = targetResult.Value.ObjectType,
                        ["currentHp"] = targetResult.Value.CurrentHp,
                        ["maxHp"] = targetResult.Value.MaxHp,
                        ["isMonster"] = targetResult.Value.IsMonster,
                        ["isAlive"] = targetResult.Value.IsAlive
                    });
            }

            return Ms(settings.TargetIdleDelayMs, 200);
        }

        if (await PressOpeningAttackKeyIfNeededAsync(context, state, settings, targetResult.Value).ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        var skillsResult = await ReadSkillsAsync(context, plan).ConfigureAwait(false);
        if (!skillsResult.Success || skillsResult.Value is null)
        {
            if (ShouldLog(state.LastSkillWarningAt, now))
            {
                state.LastSkillWarningAt = now;
                context.Logger.Warn(
                    "semi_auto.skills.read_failed",
                    new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = skillsResult.Error
                });
            }

            await PressAttackKeyIfDueAsync(context, state, settings).ConfigureAwait(false);
            return Ms(settings.TickIntervalMs, 50);
        }

        if (skillsResult.Value.Count == 0)
        {
            if (ShouldLog(state.LastNoSkillLogAt, now))
            {
                state.LastNoSkillLogAt = now;
                context.Logger.Warn(
                    "semi_auto.skills.empty",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["targetEntityId"] = targetResult.Value.TargetEntityId,
                        ["targetName"] = targetResult.Value.Name
                    });
            }

            await PressAttackKeyIfDueAsync(context, state, settings).ConfigureAwait(false);
            return Ms(settings.TickIntervalMs, 50);
        }

        var configuredSkills = ResolveConfiguredSkills(plan, skillsResult.Value);
        var osTick = CurrentOsTick();
        if (state.TryUpdateCooldownTickCalibration(
                configuredSkills,
                osTick,
                DateTimeOffset.Now,
                out var calibration))
        {
            context.Logger.Info("semi_auto.cooldown.calibrated", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = calibration.SkillName,
                ["skillId"] = calibration.SkillId,
                ["durationMs"] = calibration.CooldownDuration,
                ["endTick"] = calibration.CooldownEndTime,
                ["startTick"] = calibration.CooldownStartTick,
                ["osTick"] = calibration.OsTick,
                ["offsetMs"] = calibration.OffsetMs
            });
        }

        var decision = SemiAutoSkillReleasePriority.SelectNext(plan, state, configuredSkills, settings, DateTimeOffset.Now);
        if (decision.Kind != SemiAutoSkillReleaseDecisionKind.None)
        {
            await ExecuteReleaseDecisionAsync(context, plan, state, decision, settings).ConfigureAwait(false);
            return state.HasChainWork
                ? Ms(settings.ChainTickIntervalMs, 40)
                : Ms(settings.TickIntervalMs, 40);
        }

        if (ShouldPressTriggerFallback(plan, state, configuredSkills))
        {
            await PressTriggerFallbackAsync(context, plan, state, settings).ConfigureAwait(false);
            return Ms(settings.TickIntervalMs, 40);
        }

        if (ShouldLog(state.LastNoSkillLogAt, now))
        {
            state.LastNoSkillLogAt = now;
            context.Logger.Info(
                "semi_auto.skill.none_ready",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = targetResult.Value.TargetEntityId,
                    ["targetName"] = targetResult.Value.Name,
                    ["skillCount"] = configuredSkills.Count,
                    ["rawSkillCount"] = skillsResult.Value.Count,
                    ["topLevelSkillCount"] = plan.Roots.Count,
                    ["chainRootCount"] = plan.Roots.Count(root => root.Children.Count > 0),
                    ["triggerPrefixCount"] = plan.TriggerPrefixRoots.Count,
                    ["topLevelSkills"] = string.Join(" > ", plan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key)),
                    ["chainRoots"] = string.Join(" > ", plan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key)),
                    ["configuredSkills"] = FormatConfiguredSkills(configuredSkills),
                    ["reasons"] = SemiAutoSkillReleasePriority.BuildNoReadyReasons(plan, state, configuredSkills, settings)
                });
        }

        await PressAttackKeyIfDueAsync(context, state, settings).ConfigureAwait(false);
        return Ms(settings.TickIntervalMs, 40);
    }

    public async Task<TimeSpan> TickOpeningAttackKeyLoopAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        LockedTargetSnapshot target)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        state.MarkOpeningAttackKeyAttempted(target);
        if (settings.AttackKeyLoopEnabled &&
            state.ShouldPressAttackKey(DateTimeOffset.Now, Ms(settings.AttackKeyLoopIntervalMs, 300)))
        {
            await PressAttackKeyAsync(context, state, settings).ConfigureAwait(false);
        }

        return Ms(settings.TickIntervalMs, 40);
    }

    public async Task<bool> TryHandleMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        bool allowSitMaintenance = true,
        bool clearSitWhenDisallowed = true,
        Func<Task>? beforeMaintenanceKeyPress = null,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var maintenance = context.Config.ScriptSettings?.Maintenance;
        return maintenance is not null &&
               await TryHandleMaintenanceAsync(
                       context,
                       state,
                       settings,
                       maintenance,
                       player,
                       allowSitMaintenance,
                        clearSitWhenDisallowed,
                        beforeMaintenanceKeyPress,
                        plan,
                        requireCooldownCalibrationForMaintenance)
                    .ConfigureAwait(false);
    }

    public async Task<bool> TryRecoverAfterReviveAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        SemiAutoSkillPlan? plan = null,
        Func<Task>? beforeMaintenanceKeyPress = null,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var maintenance = context.Config.ScriptSettings?.Maintenance;
        if (maintenance is null || !player.HasKnownHealth || player.IsDead)
        {
            state.ClearMaintenanceRest();
            return false;
        }

        var hpRecoverToPercent = Math.Clamp(maintenance.SitHpRecoverToPercent, 1, 100);
        var mpRecoverToPercent = Math.Clamp(maintenance.SitMpRecoverToPercent, 1, 100);
        if (state.IsMaintenanceResting)
        {
            return await ContinueSitMaintenanceAsync(
                    context,
                    state,
                    settings,
                    maintenance,
                    player,
                    hpRecoverToPercent)
                .ConfigureAwait(false);
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.HpMaintenanceRules,
                "hp",
                player.CurrentHp,
                player.MaxHp,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.MpMaintenanceRules,
                "mp",
                player.CurrentMp,
                player.MaxMp,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        var hpRecovered = IsPercentAtOrAbove(player.CurrentHp, player.MaxHp, hpRecoverToPercent);
        var mpRecovered = player.MaxMp == 0 ||
                          IsPercentAtOrAbove(player.CurrentMp, player.MaxMp, mpRecoverToPercent);
        if (hpRecovered && mpRecovered)
        {
            return false;
        }

        if (!maintenance.SitMaintenanceEnabled)
        {
            return false;
        }

        if (beforeMaintenanceKeyPress is not null)
        {
            await beforeMaintenanceKeyPress().ConfigureAwait(false);
        }

        var result = await _keyboard
            .PressKeyAsync(RestEnterKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestEnterKey, "revive_rest_enter", result.Error);
            return false;
        }

        state.StartMaintenanceRest(forHp: !hpRecovered, forMp: player.MaxMp > 0 && !mpRecovered);
        context.Logger.Info("semi_auto.maintenance.revive_rest_enter", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = RestEnterKey,
            ["forHp"] = !hpRecovered,
            ["forMp"] = player.MaxMp > 0 && !mpRecovered,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["hpPercent"] = Math.Round(player.HpPercent, 1),
            ["hpRecoverToPercent"] = hpRecoverToPercent,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp,
            ["mpPercent"] = Math.Round(player.MpPercent, 1),
            ["mpRecoverToPercent"] = mpRecoverToPercent
        });
        return true;
    }

    private async Task<bool> TryHandleMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        bool allowSitMaintenance,
        bool clearSitWhenDisallowed = true,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var maintenance = context.Config.ScriptSettings?.Maintenance;
        if (maintenance is null || !HasMaintenanceWork(maintenance, allowSitMaintenance))
        {
            if (clearSitWhenDisallowed)
            {
                state.ClearMaintenanceRest();
            }

            return false;
        }

        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value is null)
        {
            var now = DateTimeOffset.Now;
            if (ShouldLog(state.LastMaintenanceWarningAt, now))
            {
                state.LastMaintenanceWarningAt = now;
                context.Logger.Warn("semi_auto.maintenance.player_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = playerResult.Error
                });
            }

            return false;
        }

        return await TryHandleMaintenanceAsync(
                context,
                state,
                settings,
                maintenance,
                playerResult.Value,
                allowSitMaintenance,
                clearSitWhenDisallowed,
                plan: plan,
                requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryHandleMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceScriptSettings maintenance,
        PlayerSnapshot player,
        bool allowSitMaintenance,
        bool clearSitWhenDisallowed,
        Func<Task>? beforeMaintenanceKeyPress = null,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        if (!HasMaintenanceWork(maintenance, allowSitMaintenance))
        {
            if (clearSitWhenDisallowed)
            {
                state.ClearMaintenanceRest();
            }

            return false;
        }

        if (clearSitWhenDisallowed &&
            (!allowSitMaintenance || !maintenance.SitMaintenanceEnabled) &&
            state.IsMaintenanceResting)
        {
            state.ClearMaintenanceRest();
        }

        if (allowSitMaintenance && state.IsMaintenanceResting)
        {
            return await ContinueSitMaintenanceAsync(context, state, settings, maintenance, player).ConfigureAwait(false);
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.HpMaintenanceRules,
                "hp",
                player.CurrentHp,
                player.MaxHp,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.MpMaintenanceRules,
                "mp",
                player.CurrentMp,
                player.MaxMp,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (!allowSitMaintenance || !maintenance.SitMaintenanceEnabled)
        {
            return false;
        }

        var restForHp = IsPercentAtOrBelow(player.CurrentHp, player.MaxHp, maintenance.SitHpBelowPercent);
        var restForMp = IsPercentAtOrBelow(player.CurrentMp, player.MaxMp, maintenance.SitMpBelowPercent);
        if (!restForHp && !restForMp)
        {
            return false;
        }

        var result = await _keyboard
            .PressKeyAsync(RestEnterKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestEnterKey, "rest_enter", result.Error);
            return false;
        }

        state.StartMaintenanceRest(restForHp, restForMp);
        context.Logger.Info("semi_auto.maintenance.rest_enter", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = RestEnterKey,
            ["forHp"] = restForHp,
            ["forMp"] = restForMp,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp,
            ["hpBelowPercent"] = maintenance.SitHpBelowPercent,
            ["mpBelowPercent"] = maintenance.SitMpBelowPercent
        });
        return true;
    }

    public async Task<bool> CancelMaintenanceRestAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        string reason)
    {
        if (!state.IsMaintenanceResting)
        {
            return false;
        }

        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var result = await _keyboard
            .PressKeyAsync(RestExitKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestExitKey, "rest_interrupt", result.Error);
            state.ClearMaintenanceRest();
            return false;
        }

        context.Logger.Info("semi_auto.maintenance.rest_interrupt", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = RestExitKey,
            ["reason"] = reason
        });
        state.ClearMaintenanceRest();
        return true;
    }

    private async Task<bool> ContinueSitMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceScriptSettings maintenance,
        PlayerSnapshot player,
        int? hpRecoverToPercentOverride = null)
    {
        var hpRecoverToPercent = hpRecoverToPercentOverride ?? maintenance.SitHpRecoverToPercent;
        var hpRecovered = !state.MaintenanceRestingForHp ||
                          IsPercentAtOrAbove(player.CurrentHp, player.MaxHp, hpRecoverToPercent);
        var mpRecovered = !state.MaintenanceRestingForMp ||
                          IsPercentAtOrAbove(player.CurrentMp, player.MaxMp, maintenance.SitMpRecoverToPercent);
        if (!hpRecovered || !mpRecovered)
        {
            return true;
        }

        var result = await _keyboard
            .PressKeyAsync(RestExitKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestExitKey, "rest_exit", result.Error);
            return true;
        }

        context.Logger.Info("semi_auto.maintenance.rest_exit", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = RestExitKey,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp
        });
        state.ClearMaintenanceRest();
        return true;
    }

    private async Task<bool> TryPressMaintenanceRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IEnumerable<MaintenanceKeyRuleConfig>? rules,
        string resource,
        uint current,
        uint max,
        Func<Task>? beforeMaintenanceKeyPress = null,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false)
    {
        if (max == 0)
        {
            return false;
        }

        var percent = Percent(current, max);
        OperationResult<IReadOnlyList<SkillSnapshot>>? skillsResult = null;
        foreach (var rule in (rules ?? Array.Empty<MaintenanceKeyRuleConfig>())
                     .Where(rule => !string.IsNullOrWhiteSpace(rule.Key))
                     .OrderBy(rule => Math.Clamp(rule.BelowPercent, 0, 100)))
        {
            var threshold = Math.Clamp(rule.BelowPercent, 0, 100);
            if (percent > threshold)
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (requireCooldownCalibrationForMaintenance &&
                !state.HasCooldownTickCalibration)
            {
                continue;
            }

            var hasExplicitSkill = HasExplicitMaintenanceSkill(rule);
            SkillSnapshot? maintenanceSkill = null;
            if (hasExplicitSkill || plan is not null)
            {
                skillsResult ??= await ReadAllSkillsAsync(context).ConfigureAwait(false);
                if (skillsResult.Success && skillsResult.Value is not null)
                {
                    TryUpdateMaintenanceCooldownCalibration(context, state, skillsResult.Value);
                    maintenanceSkill = ResolveMaintenanceRuleSkill(rule, plan, skillsResult.Value);
                    if (maintenanceSkill is not null &&
                        GetMaintenanceCooldownReadiness(maintenanceSkill, state) == SemiAutoSkillCooldownReadiness.CoolingDown)
                    {
                        LogMaintenanceRuleSkippedCooling(context, state, rule, resource, maintenanceSkill);
                        continue;
                    }

                    if (hasExplicitSkill && maintenanceSkill is null)
                    {
                        LogMaintenanceRuleSkippedMissing(context, state, rule, resource);
                        continue;
                    }
                }
                else if (hasExplicitSkill)
                {
                    LogMaintenanceRuleSkillReadFailed(context, state, rule, resource, skillsResult.Error);
                    continue;
                }
            }

            if (maintenanceSkill is null)
            {
                if (!state.ShouldPressMaintenanceKey(rule.Key, now, MaintenanceKeyRetryInterval))
                {
                    continue;
                }
            }

            return await ExecuteMaintenanceKeyRuleAsync(
                    context,
                    state,
                    settings,
                    rule,
                    resource,
                    current,
                    max,
                    percent,
                    threshold,
                    beforeMaintenanceKeyPress,
                    maintenanceSkill)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> ExecuteMaintenanceKeyRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceKeyRuleConfig rule,
        string resource,
        uint current,
        uint max,
        double percent,
        int threshold,
        Func<Task>? beforeMaintenanceKeyPress,
        SkillSnapshot? maintenanceSkill)
    {
        if (beforeMaintenanceKeyPress is not null)
        {
            await beforeMaintenanceKeyPress().ConfigureAwait(false);
        }

        var baselineResult = await ReadAllSkillsAsync(context).ConfigureAwait(false);
        var baselineCooldowns = baselineResult.Success && baselineResult.Value is not null
            ? SnapshotCooldownEndTimes(baselineResult.Value)
            : null;
        if (baselineResult.Success && baselineResult.Value is not null)
        {
            TryUpdateMaintenanceCooldownCalibration(context, state, baselineResult.Value);
        }

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

            if (baselineCooldowns is not null)
            {
                var skillsResult = await ReadAllSkillsAsync(context).ConfigureAwait(false);
                if (skillsResult.Success &&
                    skillsResult.Value is not null)
                {
                    TryUpdateMaintenanceCooldownCalibration(context, state, skillsResult.Value);
                    if (TryFindAdvancedCooldown(baselineCooldowns, skillsResult.Value, maintenanceSkill, out confirmedSkill))
                    {
                        break;
                    }
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
                ["baselineReadSuccess"] = baselineResult.Success,
                ["baselineError"] = baselineResult.Error,
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

    private async Task ExecuteReleaseDecisionAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillReleaseDecision decision,
        SemiAutoScriptSettings settings)
    {
        var node = decision.Node;
        if (node is null)
        {
            return;
        }

        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.ClearPendingChain)
        {
            LogChainEnded(context, node, decision.Reason, decision.Skill);
            state.ClearPendingChainAdvance();
            return;
        }

        if (!decision.ShouldPress || decision.Skill is null)
        {
            return;
        }

        var pressed = await PressSkillAsync(
                context,
                plan,
                state,
                node,
                settings,
                includeTriggerPrefix: decision.Kind != SemiAutoSkillReleaseDecisionKind.PressChain)
            .ConfigureAwait(false);
        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.PressChain)
        {
            if (pressed)
            {
                state.MarkSkillPressed(
                    decision.Skill,
                    DateTimeOffset.Now + Ms(settings.ConfirmTimeoutMs, 1500));
                if (state.IsPendingChainNextNode(node))
                {
                    state.MarkPendingChainNextPressed(decision.Skill);
                }
                else
                {
                    StartPendingChainConfirmation(context, state, node, decision.Skill, settings);
                }
            }
            else
            {
                LogChainEnded(context, node, "press_failed", decision.Skill);
                state.ClearChain();
            }

            return;
        }

        if (pressed)
        {
            var confirmationExpiresAt = DateTimeOffset.Now + Ms(settings.ConfirmTimeoutMs, 1500);
            state.MarkSkillPressed(
                decision.Skill,
                confirmationExpiresAt);
            state.SuppressUncalibratedUnknownSkill(decision.Skill, confirmationExpiresAt);
            if (node.Children.Count > 0)
            {
                StartPendingChainAdvance(context, state, node, decision.Skill, settings);
            }
        }
    }

    private async Task<bool> PressSkillAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        bool includeTriggerPrefix)
    {
        return await PressSkillKeysAsync(
                context,
                plan,
                state,
                node,
                settings,
                includeTriggerPrefix)
            .ConfigureAwait(false);
    }

    private async Task<bool> PressSkillKeysAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        bool includeTriggerPrefix)
    {
        if (includeTriggerPrefix && !node.IsTrigger)
        {
            await PressTriggerPrefixAsync(context, plan, node, settings).ConfigureAwait(false);
        }

        return await PressNodeKeyAsync(
                context,
                node,
                settings,
                phase: "skill")
            .ConfigureAwait(false);
    }

    private async Task PressTriggerPrefixAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoSkillNode currentNode,
        SemiAutoScriptSettings settings)
    {
        foreach (var trigger in plan.TriggerPrefixRoots)
        {
            if (ReferenceEquals(trigger, currentNode))
            {
                continue;
            }

            await PressNodeKeyAsync(
                    context,
                    trigger,
                    settings,
                    phase: "trigger_prefix")
                .ConfigureAwait(false);
        }
    }

    private async Task PressTriggerFallbackAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings)
    {
        foreach (var trigger in plan.TriggerPrefixRoots)
        {
            await PressNodeKeyAsync(
                    context,
                    trigger,
                    settings,
                    phase: "trigger_fallback")
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> PressNodeKeyAsync(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        SemiAutoScriptSettings settings,
        string phase = "skill")
    {
        var result = await _keyboard
            .PressKeyAsync(node.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            context.Logger.Warn("semi_auto.key.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = node.Name,
                ["key"] = node.Key,
                ["phase"] = phase,
                ["error"] = result.Error
            });
            return false;
        }

        context.Logger.Info("semi_auto.key.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["type"] = node.Type,
            ["phase"] = phase
        });
        return true;
    }

    private static bool ShouldPressTriggerFallback(
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills)
    {
        if (state.HasChainWork || plan.TriggerPrefixRoots.Count == 0)
        {
            return false;
        }

        var hasExecutableRoot = false;
        foreach (var root in plan.Roots)
        {
            if (root.IsTrigger || root.IsDp)
            {
                continue;
            }

            hasExecutableRoot = true;
            var skill = root.ResolveSkill(skills);
            if (skill is null)
            {
                return false;
            }

            if (SemiAutoSkillReleasePriority.GetCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                return false;
            }
        }

        return hasExecutableRoot;
    }

    private async Task<bool> PressOpeningAttackKeyIfNeededAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        LockedTargetSnapshot target)
    {
        if (!settings.AttackKeyLoopEnabled || !state.ShouldPressOpeningAttackKey(target))
        {
            return false;
        }

        state.MarkOpeningAttackKeyAttempted(target);
        return await PressAttackKeyAsync(context, state, settings).ConfigureAwait(false);
    }

    private async Task PressAttackKeyIfDueAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings)
    {
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task<bool> PressAttackKeyAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings)
    {
        state.MarkAttackKeyAttempted(DateTimeOffset.Now);
        var result = await _keyboard
            .PressKeyAsync(AttackKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            var failedAt = DateTimeOffset.Now;
            if (ShouldLog(state.LastAttackKeyWarningAt, failedAt))
            {
                state.LastAttackKeyWarningAt = failedAt;
                context.Logger.Warn("semi_auto.attack_key.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = AttackKey,
                    ["error"] = result.Error
                });
            }
        }

        return result.Success;
    }

    private static IReadOnlyList<SkillSnapshot> ResolveConfiguredSkills(
        SemiAutoSkillPlan plan,
        IReadOnlyList<SkillSnapshot> learnedSkills)
    {
        var configuredSkills = new List<SkillSnapshot>();
        var seenSkillIds = new HashSet<uint>();
        foreach (var node in FlattenNodes(plan.Roots))
        {
            var skill = node.ResolveSkill(learnedSkills);
            if (skill is null || !seenSkillIds.Add(skill.SkillId))
            {
                continue;
            }

            configuredSkills.Add(skill);
        }

        return configuredSkills;
    }

    private static IEnumerable<SemiAutoSkillNode> FlattenNodes(IEnumerable<SemiAutoSkillNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenNodes(root.Children))
            {
                yield return child;
            }
        }
    }

    private static string FormatConfiguredSkills(IReadOnlyList<SkillSnapshot> configuredSkills)
    {
        return string.Join(
            " | ",
            configuredSkills.Select(skill =>
                skill.Name +
                "#" + skill.SkillId +
                ":cooldown=" + skill.CooldownDuration +
                "/" + skill.CooldownEndTime));
    }

    private static void StartPendingChainAdvance(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        SkillSnapshot sourceSkill,
        SemiAutoScriptSettings settings)
    {
        if (sourceNode.Children.Count == 0)
        {
            state.ClearChain();
            return;
        }

        StartPendingChainAdvance(context, state, sourceNode, sourceSkill, sourceNode.Children[0], settings);
    }

    private static void StartPendingChainAdvance(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        SkillSnapshot sourceSkill,
        SemiAutoSkillNode nextNode,
        SemiAutoScriptSettings settings)
    {
        var windowMs = nextNode.ChainTimeMs ??
                       sourceNode.ChainTimeMs ??
                       settings.DefaultChainTimeMs;
        state.StartPendingChainAdvance(
            sourceNode,
            nextNode,
            DateTimeOffset.Now + Ms(windowMs, 5000),
            sourceSkill.CooldownEndTime);
        context.Logger.Info("semi_auto.chain.pending", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["sourceSkill"] = sourceNode.Name,
            ["sourceKey"] = sourceNode.Key,
            ["sourceCooldownEndTime"] = sourceSkill.CooldownEndTime,
            ["nextSkill"] = nextNode.Name,
            ["nextKey"] = nextNode.Key,
            ["configuredChildCount"] = sourceNode.Children.Count,
            ["expiresInMs"] = windowMs
        });
    }

    private static void StartPendingChainConfirmation(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode chainNode,
        SkillSnapshot chainSkill,
        SemiAutoScriptSettings settings)
    {
        var sourceNode = chainNode.Parent ?? chainNode;
        var windowMs = chainNode.ChainTimeMs ??
                       sourceNode.ChainTimeMs ??
                       settings.DefaultChainTimeMs;
        state.StartPendingChainAdvance(
            sourceNode,
            chainNode,
            DateTimeOffset.Now + Ms(windowMs, 5000),
            chainSkill.CooldownEndTime);
        state.MarkPendingChainNextPressed(chainSkill);
        context.Logger.Info("semi_auto.chain.pending", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["sourceSkill"] = sourceNode.Name,
            ["sourceKey"] = sourceNode.Key,
            ["sourceCooldownEndTime"] = chainSkill.CooldownEndTime,
            ["nextSkill"] = chainNode.Name,
            ["nextKey"] = chainNode.Key,
            ["configuredChildCount"] = sourceNode.Children.Count,
            ["expiresInMs"] = windowMs
        });
    }

    private static void LogChainEnded(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        string reason,
        SkillSnapshot? skill = null)
    {
        context.Logger.Info("semi_auto.chain.ended", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["type"] = node.Type,
            ["reason"] = reason,
            ["cooldownDuration"] = skill?.CooldownDuration,
            ["cooldownEndTime"] = skill?.CooldownEndTime
        });
    }

    private static bool HasMaintenanceWork(MaintenanceScriptSettings maintenance, bool allowSitMaintenance)
    {
        return (allowSitMaintenance && maintenance.SitMaintenanceEnabled) ||
               HasMaintenanceRules(maintenance.HpMaintenanceRules) ||
               HasMaintenanceRules(maintenance.MpMaintenanceRules);
    }

    private static bool HasMaintenanceRules(IEnumerable<MaintenanceKeyRuleConfig>? rules)
    {
        return rules?.Any(rule => !string.IsNullOrWhiteSpace(rule.Key)) == true;
    }

    private static Dictionary<uint, uint> SnapshotCooldownEndTimes(IEnumerable<SkillSnapshot> skills)
    {
        var result = new Dictionary<uint, uint>();
        foreach (var skill in skills)
        {
            result[skill.SkillId] = skill.CooldownEndTime;
        }

        return result;
    }

    private static bool HasExplicitMaintenanceSkill(MaintenanceKeyRuleConfig rule)
    {
        return rule.SkillId != 0 || !string.IsNullOrWhiteSpace(rule.SkillName);
    }

    private static SkillSnapshot? ResolveMaintenanceRuleSkill(
        MaintenanceKeyRuleConfig rule,
        SemiAutoSkillPlan? plan,
        IReadOnlyList<SkillSnapshot> skills)
    {
        if (HasExplicitMaintenanceSkill(rule))
        {
            return skills.FirstOrDefault(skill => MatchesMaintenanceSkill(skill, rule.SkillId, rule.SkillName));
        }

        if (plan is null)
        {
            return null;
        }

        foreach (var node in FlattenNodes(plan.Roots)
                     .Where(node => string.Equals(node.Key, rule.Key, StringComparison.OrdinalIgnoreCase)))
        {
            var skill = node.ResolveSkill(skills);
            if (skill is not null)
            {
                return skill;
            }
        }

        return null;
    }

    private static bool MatchesMaintenanceSkill(SkillSnapshot skill, uint skillId, string? skillName)
    {
        if (skillId != 0)
        {
            return skill.SkillId == skillId;
        }

        return EqualsSkillName(skill.Name, skillName) ||
               EqualsSkillName(skill.DisplayBaseName, skillName);
    }

    private static bool EqualsSkillName(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    private static SemiAutoSkillCooldownReadiness GetMaintenanceCooldownReadiness(
        SkillSnapshot skill,
        SemiAutoCombatState state)
    {
        var readiness = SemiAutoSkillReleasePriority.GetCooldownReadiness(skill, state);
        if (readiness != SemiAutoSkillCooldownReadiness.Unknown ||
            state.HasCooldownTickCalibration ||
            skill.CooldownEndTime == 0)
        {
            return readiness;
        }

        var rawRemainingMs = unchecked((int)(skill.CooldownEndTime - CurrentOsTick()));
        if (rawRemainingMs <= SemiAutoSkillReleasePriority.CooldownReadyToleranceMs)
        {
            return SemiAutoSkillCooldownReadiness.Ready;
        }

        var maxExpectedRemainingMs = (long)skill.CooldownDuration + 60_000L;
        return skill.CooldownDuration > 0 && rawRemainingMs <= maxExpectedRemainingMs
            ? SemiAutoSkillCooldownReadiness.CoolingDown
            : SemiAutoSkillCooldownReadiness.Unknown;
    }

    private static void TryUpdateMaintenanceCooldownCalibration(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills)
    {
        var osTick = CurrentOsTick();
        if (!state.TryUpdateCooldownTickCalibration(
                skills,
                osTick,
                DateTimeOffset.Now,
                out var calibration))
        {
            return;
        }

        context.Logger.Info("semi_auto.cooldown.calibrated", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = calibration.SkillName,
            ["skillId"] = calibration.SkillId,
            ["durationMs"] = calibration.CooldownDuration,
            ["endTick"] = calibration.CooldownEndTime,
            ["startTick"] = calibration.CooldownStartTick,
            ["osTick"] = calibration.OsTick,
            ["offsetMs"] = calibration.OffsetMs,
            ["source"] = "maintenance"
        });
    }

    private static bool TryFindAdvancedCooldown(
        IReadOnlyDictionary<uint, uint> baseline,
        IEnumerable<SkillSnapshot> skills,
        SkillSnapshot? maintenanceSkill,
        out SkillSnapshot confirmedSkill)
    {
        foreach (var skill in skills)
        {
            if (maintenanceSkill is not null &&
                !MatchesMaintenanceSkill(skill, maintenanceSkill.SkillId, maintenanceSkill.Name))
            {
                continue;
            }

            if (skill.CooldownEndTime == 0)
            {
                continue;
            }

            if (!baseline.TryGetValue(skill.SkillId, out var previousEndTime) ||
                HasCooldownEndAdvanced(previousEndTime, skill.CooldownEndTime))
            {
                confirmedSkill = skill;
                return true;
            }
        }

        confirmedSkill = default!;
        return false;
    }

    private static bool HasCooldownEndAdvanced(uint previousEndTime, uint currentEndTime)
    {
        return currentEndTime != 0 &&
               (previousEndTime == 0 ||
                unchecked((int)(currentEndTime - previousEndTime)) > 0);
    }

    private static void LogMaintenanceRuleSkippedCooling(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        MaintenanceKeyRuleConfig rule,
        string resource,
        SkillSnapshot skill)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Info("semi_auto.maintenance.skill_cooling", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = skill.SkillId,
            ["skillName"] = skill.Name,
            ["cooldownDuration"] = skill.CooldownDuration,
            ["cooldownEndTime"] = skill.CooldownEndTime,
            ["cooldownOffsetMs"] = state.CooldownTickOffsetMs
        });
    }

    private static void LogMaintenanceRuleSkippedMissing(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        MaintenanceKeyRuleConfig rule,
        string resource)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.skill_missing", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName
        });
    }

    private static void LogMaintenanceRuleSkillReadFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        MaintenanceKeyRuleConfig rule,
        string resource,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.skill_read_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["error"] = error
        });
    }

    private static bool IsPercentAtOrBelow(uint current, uint max, int threshold)
    {
        return max > 0 && Percent(current, max) <= Math.Clamp(threshold, 0, 100);
    }

    private static bool IsPercentAtOrAbove(uint current, uint max, int threshold)
    {
        return max > 0 && Percent(current, max) >= Math.Clamp(threshold, 0, 100);
    }

    private static double Percent(uint current, uint max)
    {
        return max == 0
            ? 100.0D
            : Math.Clamp(current * 100.0D / max, 0.0D, 100.0D);
    }

    private static void LogMaintenanceKeyFailure(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        string key,
        string phase,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.key_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["phase"] = phase,
            ["error"] = error
        });
    }

    private static Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadPlayerAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadPlayerAsync(context.StopToken);
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            var readContext = CreateReadContext(context);
            if (!plan.RequiresFullSkillRead && plan.SkillReadIds.Count > 0)
            {
                return scopedApi.ReadSkillsAsync(readContext, plan.SkillReadIds, context.StopToken);
            }

            return scopedApi.ReadSkillsAsync(readContext, context.StopToken);
        }

        return context.GameApi.ReadSkillsAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadAllSkillsAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadSkillsAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadSkillsAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
    }

    private static TimeSpan Ms(int configuredMs, int fallbackMs)
    {
        var value = configuredMs > 0 ? configuredMs : fallbackMs;
        return TimeSpan.FromMilliseconds(Math.Max(1, value));
    }

    private static uint CurrentOsTick()
    {
        return unchecked((uint)Environment.TickCount64);
    }

    private static bool ShouldLog(DateTimeOffset lastLogAt, DateTimeOffset now)
    {
        return now - lastLogAt >= WarningLogInterval;
    }
}
