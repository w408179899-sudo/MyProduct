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
    private static readonly TimeSpan MaintenanceRestExitBeforeKeyDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan PostCombatPotionPressInterval = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SpiritmasterCooldownConfirmRetryInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SpiritmasterSummonKeyInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SpiritmasterSummonAttemptInterval = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SpiritmasterSummonVerifyWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpiritmasterOpeningAttackKeyInterval = TimeSpan.FromMilliseconds(50);
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

        if (!plan.UsesSpiritmasterAutoLogic &&
            await TryHandleMaintenanceAsync(
                    context,
                    state,
                    allowSitMaintenance: false,
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance)
                .ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        if (!plan.HasCombatActions)
        {
            state.ResetOpeningAttackKey();
            state.ResetSpiritmasterOpeningAttackKey();
            state.ResetOpeningSkill();
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
            state.ResetSpiritmasterOpeningAttackKey();
            state.ResetOpeningSkill();
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
            var counted = context.RuntimeStates.MarkKill(
                context.Config.AccountName,
                killedTargetEntityId,
                targetResult.Value.ServerObjectId,
                targetResult.Value.CapturedAt);
            if (counted)
            {
                context.Logger.Info("semi_auto.target.kill_counted", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetEntityId"] = killedTargetEntityId,
                    ["targetServerObjectId"] = targetResult.Value.ServerObjectId,
                    ["targetName"] = targetResult.Value.Name
                });
            }
        }

        if (!targetResult.Value.IsMonsterAlive)
        {
            state.ResetOpeningAttackKey();
            state.ResetSpiritmasterOpeningAttackKey();
            state.ResetOpeningSkill();
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

        var skillSettings = context.Config.ScriptSettings?.Skills ?? new SkillScriptSettings();
        if (plan.UsesSpiritmasterAutoLogic &&
            await PressSpiritmasterOpeningAttackKeyIfNeededAsync(
                    context,
                    state,
                    settings,
                    skillSettings.Spiritmaster,
                    targetResult.Value)
                .ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        if (await PressOpeningSkillIfNeededAsync(context, state, settings, plan, targetResult.Value).ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        if (await ConfirmRetryablePressedSkillCooldownIfNeededAsync(context, state, settings, plan).ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
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
        var cooldownObservedSkills = configuredSkills;
        if (plan.UsesSpiritmasterAutoLogic)
        {
            cooldownObservedSkills = MergeSkillSnapshots(
                configuredSkills,
                ResolveSpiritmasterConfiguredSkills(skillSettings.Spiritmaster, skillsResult.Value));
        }

        var osTick = CurrentOsTick();
        if (state.TryUpdateCooldownTickCalibration(
                cooldownObservedSkills,
                osTick,
                now,
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

        var cooldownInvalidationSkills = ResolveCooldownInvalidationSkills(plan, configuredSkills);
        if (state.TryInvalidateImplausibleCooldownTickCalibration(
                cooldownInvalidationSkills,
                osTick,
                now,
                SemiAutoSkillReleasePriority.CooldownReadyToleranceMs,
                out var invalidation))
        {
            context.Logger.Warn("semi_auto.cooldown.calibration_invalidated", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = invalidation.SkillName,
                ["skillId"] = invalidation.SkillId,
                ["durationMs"] = invalidation.CooldownDuration,
                ["endTick"] = invalidation.CooldownEndTime,
                ["effectiveEndTick"] = invalidation.EffectiveCooldownEndTime,
                ["osTick"] = invalidation.OsTick,
                ["estimatedGameTick"] = invalidation.EstimatedGameTick,
                ["oldOffsetMs"] = invalidation.OldOffsetMs,
                ["remainingMs"] = invalidation.RemainingMs,
                ["suspiciousSkillCount"] = invalidation.SuspiciousSkillCount,
                ["reason"] = invalidation.Reason
            });
        }

        TryStartPendingChainWindowFromRootCooldown(context, state, configuredSkills, now, settings);

        var spiritContext = plan.UsesSpiritmasterAutoLogic
            ? await ReadSpiritmasterCombatContextAsync(context, targetResult.Value).ConfigureAwait(false)
            : null;
        var useSpiritmasterLogic =
            plan.UsesSpiritmasterAutoLogic &&
            spiritContext?.CanUseSpiritmasterLogic != false;
        if ((useSpiritmasterLogic || state.HasPressedSkillCooldownRetryKey()) &&
            state.IsAwaitingPressedSkillCooldownConfirmation(
                cooldownObservedSkills,
                DateTimeOffset.Now,
                out _,
                out _))
        {
            await PressPendingSkillCooldownRetryIfDueAsync(context, state, settings).ConfigureAwait(false);
            return Ms(settings.TickIntervalMs, 40);
        }

        if (useSpiritmasterLogic &&
            spiritContext is not null &&
            await TryHandleSpiritmasterSpecialAsync(
                    context,
                    state,
                    settings,
                    skillSettings.Spiritmaster,
                    skillsResult.Value,
                    spiritContext)
                .ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        if (await TryHandleMaintenanceAsync(
                    context,
                    state,
                    allowSitMaintenance: false,
                    plan: plan,
                    requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                    runTiming: MaintenanceRuleRunTiming.InCombat,
                    includeAlwaysRules: plan.UsesSpiritmasterAutoLogic)
                .ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

        var conditionTargetAbnormalStatuses = spiritContext?.LockedTargetAbnormalStatuses;
        if (conditionTargetAbnormalStatuses is null &&
            ShouldReadTargetConditionAbnormalStatuses(settings, plan, state, configuredSkills))
        {
            conditionTargetAbnormalStatuses = await ReadTargetConditionAbnormalStatusesAsync(context, state)
                .ConfigureAwait(false);
        }

        var decision = useSpiritmasterLogic
            ? SpiritmasterAutoSkillReleasePriority.SelectNext(
                plan,
                state,
                configuredSkills,
                settings,
                skillSettings.Spiritmaster,
                spiritContext,
                DateTimeOffset.Now)
            : SemiAutoSkillReleasePriority.SelectNext(
                plan,
                state,
                configuredSkills,
                settings,
                DateTimeOffset.Now,
                conditionTargetAbnormalStatuses);
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
                    ["spiritmasterAutoLogic"] = plan.UsesSpiritmasterAutoLogic,
                    ["topLevelSkills"] = string.Join(" > ", plan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key)),
                    ["chainRoots"] = string.Join(" > ", plan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key)),
                    ["configuredSkills"] = FormatConfiguredSkills(configuredSkills),
                    ["reasons"] = SemiAutoSkillReleasePriority.BuildNoReadyReasons(plan, state, configuredSkills, settings)
                });
        }

        await PressAttackKeyIfDueAsync(context, state, settings).ConfigureAwait(false);
        return Ms(settings.TickIntervalMs, 40);
    }

    public async Task<bool> EnsureSpiritmasterPetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        Func<Task>? beforeSummonKeyPress = null)
    {
        if (!plan.UsesSpiritmasterAutoLogic)
        {
            return false;
        }

        var skillSettings = context.Config.ScriptSettings?.Skills ?? new SkillScriptSettings();
        var spiritSettings = skillSettings.Spiritmaster ?? new SpiritmasterSkillSettings();
        if (!spiritSettings.SummonSkills.Any(rule => !string.IsNullOrWhiteSpace(rule.Key)))
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (!playerResult.Success || playerResult.Value is null || playerResult.Value.IsDead)
        {
            state.ClearSpiritmasterSummonVerification();
            return false;
        }

        var player = playerResult.Value;
        if (player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.ClearSpiritmasterSummonVerification();
            return false;
        }

        var rosterResult = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        if (!rosterResult.Success || rosterResult.Value is null)
        {
            if (state.HasPendingSpiritmasterSummonVerification)
            {
                if (ShouldLog(state.LastSpiritmasterSummonVerifyLogAt, now))
                {
                    state.LastSpiritmasterSummonVerifyLogAt = now;
                    context.Logger.Warn("semi_auto.spiritmaster.summon_verify_roster_failed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["error"] = rosterResult.Error
                    });
                }

                return true;
            }

            return false;
        }

        var pet = rosterResult.Value.LocalPlayerPet.Pet;
        if (pet.IsSummoned && pet.IsAlive)
        {
            if (state.HasPendingSpiritmasterSummonVerification)
            {
                context.Logger.Info("semi_auto.spiritmaster.summon_verified", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["petServerObjectId"] = pet.ServerObjectId,
                    ["petEntityId"] = pet.EntityId,
                    ["petName"] = pet.Name,
                    ["petHpPercent"] = pet.HpPercent
                });
            }

            state.ClearSpiritmasterSummonVerification();
            return false;
        }

        if (state.IsAwaitingSpiritmasterSummonVerification(now))
        {
            if (ShouldLog(state.LastSpiritmasterSummonVerifyLogAt, now))
            {
                state.LastSpiritmasterSummonVerifyLogAt = now;
                context.Logger.Info("semi_auto.spiritmaster.summon_waiting", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName
                });
            }

            return true;
        }

        if (state.IsSpiritmasterSummonVerificationExpired(now))
        {
            context.Logger.Warn("semi_auto.spiritmaster.summon_verify_timeout", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
            state.ClearSpiritmasterSummonVerification();
        }

        if (!state.ShouldAttemptSpiritmasterSummon(now, SpiritmasterSummonAttemptInterval))
        {
            return true;
        }

        if (beforeSummonKeyPress is not null)
        {
            await beforeSummonKeyPress().ConfigureAwait(false);
        }

        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        return await TryPressSpiritmasterSummonAsync(context, state, settings, spiritSettings).ConfigureAwait(false);
    }

    public async Task<TimeSpan> TickOpeningAttackKeyLoopAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        LockedTargetSnapshot target)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        if (await PressOpeningSkillIfNeededAsync(context, state, settings, plan, target).ConfigureAwait(false))
        {
            return Ms(settings.TickIntervalMs, 40);
        }

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
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
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
                        requireCooldownCalibrationForMaintenance,
                        runTiming,
                        includeAlwaysRules)
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

        var hpRecovered = IsPercentAtOrAbove(player.CurrentHp, player.MaxHp, hpRecoverToPercent);
        var mpRecovered = player.MaxMp == 0 ||
                          IsPercentAtOrAbove(player.CurrentMp, player.MaxMp, mpRecoverToPercent);
        if (hpRecovered && mpRecovered)
        {
            return false;
        }

        if (maintenance.SitMaintenanceEnabled)
        {
            if (await ShouldWaitForHarmfulAbnormalBeforeSitAsync(context, state, player, "revive_rest_enter").ConfigureAwait(false))
            {
                return true;
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

            var enteredAt = DateTimeOffset.Now;
            state.MarkMaintenanceKeyAttempted(RestEnterKey, enteredAt);
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

        if (await TryPressStatusMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.StatusMaintenanceRules,
                "status",
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressDpMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.DpMaintenanceRules,
                "dp",
                player,
                beforeMaintenanceKeyPress,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.HpMaintenanceRules,
                "hp",
                player.CurrentHp,
                player.MaxHp,
                player,
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
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance)
            .ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> TryHandleMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        bool allowSitMaintenance,
        bool clearSitWhenDisallowed = true,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
    {
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        var maintenance = context.Config.ScriptSettings?.Maintenance;
        if (maintenance is null ||
            !HasMaintenanceWork(maintenance, allowSitMaintenance, runTiming, includeAlwaysRules))
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
                requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                runTiming: runTiming,
                includeAlwaysRules: includeAlwaysRules)
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
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
    {
        if (!HasMaintenanceWork(maintenance, allowSitMaintenance, runTiming, includeAlwaysRules))
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

        if (await TryPressStatusMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.StatusMaintenanceRules,
                "status",
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance,
                runTiming,
                includeAlwaysRules)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressDpMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.DpMaintenanceRules,
                "dp",
                player,
                beforeMaintenanceKeyPress,
                requireCooldownCalibrationForMaintenance,
                runTiming,
                includeAlwaysRules)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressMaintenanceRuleAsync(
                context,
                state,
                settings,
                maintenance.HpMaintenanceRules,
                "hp",
                player.CurrentHp,
                player.MaxHp,
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance,
                runTiming,
                includeAlwaysRules)
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
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance,
                runTiming,
                includeAlwaysRules)
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

        if (await ShouldWaitForHarmfulAbnormalBeforeSitAsync(context, state, player, "rest_enter").ConfigureAwait(false))
        {
            return true;
        }

        var result = await _keyboard
            .PressKeyAsync(RestEnterKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestEnterKey, "rest_enter", result.Error);
            return false;
        }

        var enteredAt = DateTimeOffset.Now;
        state.MarkMaintenanceKeyAttempted(RestEnterKey, enteredAt);
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

    private static async Task<bool> ShouldWaitForHarmfulAbnormalBeforeSitAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        string phase)
    {
        var abnormalResult = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (!abnormalResult.Success || abnormalResult.Value is null)
        {
            var warningAt = DateTimeOffset.Now;
            if (ShouldLog(state.LastMaintenanceWarningAt, warningAt))
            {
                state.LastMaintenanceWarningAt = warningAt;
                context.Logger.Warn("semi_auto.maintenance.rest_wait_abnormal_read_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["phase"] = phase,
                    ["error"] = abnormalResult.Error,
                    ["hp"] = player.CurrentHp,
                    ["maxHp"] = player.MaxHp,
                    ["mp"] = player.CurrentMp,
                    ["maxMp"] = player.MaxMp
                });
            }

            return true;
        }

        var abnormal = abnormalResult.Value;
        if (!abnormal.HasHarmfulAbnormalForRest)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            state.LastMaintenanceWarningAt = now;
            context.Logger.Info("semi_auto.maintenance.rest_wait_harmful_abnormal", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["abnormalCategory2Count"] = abnormal.AbnormalCategory2Count,
                ["abnormalEntryCount"] = abnormal.Entries.Count,
                ["harmfulAbnormalCount"] = abnormal.HarmfulAbnormalCount,
                ["harmfulAbnormalSummary"] = abnormal.HarmfulAbnormalSummary,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["mp"] = player.CurrentMp,
                ["maxMp"] = player.MaxMp
            });
        }

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
            if (player.HasRestState && !player.IsResting)
            {
                var now = DateTimeOffset.Now;
                if (!state.ShouldPressMaintenanceKey(RestEnterKey, now, MaintenanceKeyRetryInterval))
                {
                    return true;
                }

                var reenterResult = await _keyboard
                    .PressKeyAsync(RestEnterKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
                    .ConfigureAwait(false);
                if (!reenterResult.Success)
                {
                    LogMaintenanceKeyFailure(context, state, RestEnterKey, "rest_reenter", reenterResult.Error);
                    return true;
                }

                state.MarkMaintenanceKeyAttempted(RestEnterKey, DateTimeOffset.Now);
                context.Logger.Info("semi_auto.maintenance.rest_reenter", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = RestEnterKey,
                    ["forHp"] = state.MaintenanceRestingForHp,
                    ["forMp"] = state.MaintenanceRestingForMp,
                    ["hp"] = player.CurrentHp,
                    ["maxHp"] = player.MaxHp,
                    ["mp"] = player.CurrentMp,
                    ["maxMp"] = player.MaxMp,
                    ["stanceFlags"] = player.StanceFlags,
                    ["stanceLow"] = player.StanceLowNibble,
                    ["motionMode"] = player.MotionMode
                });
            }

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
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress = null,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
    {
        if (max == 0)
        {
            return false;
        }

        var percent = Percent(current, max);
        OperationResult<IReadOnlyList<SkillSnapshot>>? skillsResult = null;
        OperationResult<IReadOnlyList<InventoryItemSnapshot>>? inventoryResult = null;
        foreach (var rule in (rules ?? Array.Empty<MaintenanceKeyRuleConfig>())
                     .Where(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                                     IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules))
                     .OrderBy(rule => GetMaintenanceRuleActionPriority(resource, rule))
                     .ThenBy(rule => Math.Clamp(rule.BelowPercent, 0, 100)))
        {
            var threshold = Math.Clamp(rule.BelowPercent, 0, 100);
            if (percent > threshold)
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (rule.ActionType == MaintenanceRuleActionType.Potion)
            {
                if (!string.Equals(resource, "mp", StringComparison.OrdinalIgnoreCase) ||
                    !state.ShouldPressMaintenanceKey(rule.Key, now, MaintenanceKeyRetryInterval))
                {
                    continue;
                }

                if (runTiming == MaintenanceRuleRunTiming.AfterCombat)
                {
                    return await ExecuteMaintenancePotionRuleAsync(
                            context,
                            state,
                            settings,
                            rule,
                            resource,
                            current,
                            max,
                            percent,
                            threshold,
                            player,
                            beforeMaintenanceKeyPress,
                            pressCount: 2,
                            pressInterval: PostCombatPotionPressInterval)
                        .ConfigureAwait(false);
                }

                inventoryResult ??= await ReadInventoryAsync(context).ConfigureAwait(false);
                if (!inventoryResult.Success || inventoryResult.Value is null)
                {
                    LogMaintenancePotionInventoryReadFailed(context, state, rule, inventoryResult.Error);
                    continue;
                }

                if (!inventoryResult.Value.Any(IsSpiritMpPotion))
                {
                    continue;
                }

                return await ExecuteMaintenancePotionRuleAsync(
                        context,
                        state,
                        settings,
                        rule,
                        resource,
                        current,
                        max,
                        percent,
                        threshold,
                        player,
                        beforeMaintenanceKeyPress)
                    .ConfigureAwait(false);
            }

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
                    player,
                    beforeMaintenanceKeyPress,
                    maintenanceSkill)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> TryPressStatusMaintenanceRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IEnumerable<StatusMaintenanceRuleConfig>? rules,
        string resource,
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress = null,
        SemiAutoSkillPlan? plan = null,
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
    {
        _ = plan;
        var configuredRules = (rules ?? Array.Empty<StatusMaintenanceRuleConfig>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                           HasStatusMaintenanceRuleSelection(rule) &&
                           IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules))
            .ToArray();
        if (configuredRules.Length == 0)
        {
            return false;
        }

        var abnormalResult = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (!abnormalResult.Success || abnormalResult.Value is null)
        {
            LogStatusMaintenanceAbnormalReadFailed(context, state, resource, abnormalResult.Error);
            return false;
        }

        OperationResult<IReadOnlyList<SkillSnapshot>>? skillsResult = null;
        foreach (var rule in configuredRules)
        {
            var configuredSkillId = rule.SkillId;
            if (IsStatusMaintenanceActive(rule, configuredSkillId, state, abnormalResult.Value.Entries))
            {
                continue;
            }

            SkillSnapshot? maintenanceSkill = null;
            if (HasExplicitStatusMaintenanceSkill(rule))
            {
                skillsResult ??= await ReadAllSkillsAsync(context).ConfigureAwait(false);
                if (skillsResult.Success && skillsResult.Value is not null)
                {
                    maintenanceSkill = ResolveStatusMaintenanceRuleSkill(rule, skillsResult.Value);
                    if (maintenanceSkill is not null &&
                        requireCooldownCalibrationForMaintenance &&
                        !state.HasCooldownTickCalibration &&
                        maintenanceSkill.CooldownEndTime != 0)
                    {
                        continue;
                    }

                    if (maintenanceSkill is not null &&
                        GetMaintenanceCooldownReadiness(maintenanceSkill, state) == SemiAutoSkillCooldownReadiness.CoolingDown)
                    {
                        LogStatusMaintenanceRuleSkippedCooling(context, state, rule, resource, maintenanceSkill);
                        continue;
                    }

                    if (maintenanceSkill is null)
                    {
                        LogStatusMaintenanceRuleSkippedMissing(context, state, rule, resource);
                        continue;
                    }
                }
                else
                {
                    LogStatusMaintenanceRuleSkillReadFailed(context, state, rule, resource, skillsResult.Error);
                    continue;
                }
            }

            var skillId = ResolveStatusMaintenanceSkillId(rule, maintenanceSkill);
            if (skillId != configuredSkillId &&
                IsStatusMaintenanceActive(rule, skillId, state, abnormalResult.Value.Entries))
            {
                continue;
            }

            if (IsStatusMaintenanceActive(rule, skillId, state, abnormalResult.Value.Entries))
            {
                continue;
            }

            var isOneShotStatusMaintenance = IsOneShotStatusMaintenanceRule(rule, maintenanceSkill);
            var oneShotSkillName = maintenanceSkill?.Name ?? maintenanceSkill?.DisplayBaseName ?? rule.SkillName;
            if (isOneShotStatusMaintenance &&
                state.WasOneShotStatusMaintenancePressed(skillId, oneShotSkillName, rule.Key))
            {
                LogStatusMaintenanceOneShotSkipped(
                    context,
                    state,
                    rule,
                    resource,
                    skillId,
                    oneShotSkillName);
                continue;
            }

            var now = DateTimeOffset.Now;
            if (!state.ShouldPressMaintenanceKey(rule.Key, now, MaintenanceKeyRetryInterval))
            {
                continue;
            }

            return await ExecuteStatusMaintenanceKeyRuleAsync(
                    context,
                    state,
                    settings,
                    rule,
                    resource,
                    player,
                    abnormalResult.Value,
                    beforeMaintenanceKeyPress,
                    maintenanceSkill,
                    isOneShotStatusMaintenance)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> TryPressDpMaintenanceRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IEnumerable<DpMaintenanceRuleConfig>? rules,
        string resource,
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress = null,
        bool requireCooldownCalibrationForMaintenance = false,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true)
    {
        var configuredRules = (rules ?? Array.Empty<DpMaintenanceRuleConfig>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                           IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules))
            .OrderByDescending(rule => NormalizeRequiredDp(rule.RequiredDp))
            .ToArray();
        if (configuredRules.Length == 0)
        {
            return false;
        }

        OperationResult<IReadOnlyList<SkillSnapshot>>? skillsResult = null;
        foreach (var rule in configuredRules)
        {
            var requiredDp = NormalizeRequiredDp(rule.RequiredDp);
            if (player.CurrentDp < requiredDp)
            {
                continue;
            }

            if (requireCooldownCalibrationForMaintenance &&
                !state.HasCooldownTickCalibration)
            {
                continue;
            }

            var hasExplicitSkill = HasExplicitDpMaintenanceSkill(rule);
            SkillSnapshot? maintenanceSkill = null;
            if (hasExplicitSkill)
            {
                skillsResult ??= await ReadAllSkillsAsync(context).ConfigureAwait(false);
                if (skillsResult.Success && skillsResult.Value is not null)
                {
                    maintenanceSkill = ResolveDpMaintenanceRuleSkill(rule, skillsResult.Value);
                    if (maintenanceSkill is not null &&
                        GetMaintenanceCooldownReadiness(maintenanceSkill, state) == SemiAutoSkillCooldownReadiness.CoolingDown)
                    {
                        LogDpMaintenanceRuleSkippedCooling(context, state, rule, resource, maintenanceSkill);
                        continue;
                    }

                    if (maintenanceSkill is null)
                    {
                        LogDpMaintenanceRuleSkippedMissing(context, state, rule, resource);
                        continue;
                    }
                }
                else
                {
                    LogDpMaintenanceRuleSkillReadFailed(context, state, rule, resource, skillsResult.Error);
                    continue;
                }
            }
            else
            {
                var now = DateTimeOffset.Now;
                if (!state.ShouldPressMaintenanceKey(rule.Key, now, MaintenanceKeyRetryInterval))
                {
                    continue;
                }
            }

            return await ExecuteDpMaintenanceKeyRuleAsync(
                    context,
                    state,
                    settings,
                    rule,
                    resource,
                    player,
                    requiredDp,
                    beforeMaintenanceKeyPress,
                    maintenanceSkill)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> ExecuteDpMaintenanceKeyRuleAsync(
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

        var baselineResult = await ReadAllSkillsAsync(context).ConfigureAwait(false);
        var baselineCooldowns = baselineResult.Success && baselineResult.Value is not null
            ? SnapshotCooldownEndTimes(baselineResult.Value)
            : null;
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
                    if (TryFindAdvancedCooldown(baselineCooldowns, skillsResult.Value, maintenanceSkill, out confirmedSkill))
                    {
                        TryUpdateMaintenanceCooldownCalibration(context, state, confirmedSkill);
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
                ["baselineReadSuccess"] = baselineResult.Success,
                ["baselineError"] = baselineResult.Error,
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

    private async Task<bool> ExecuteStatusMaintenanceKeyRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        StatusMaintenanceRuleConfig rule,
        string resource,
        PlayerSnapshot player,
        PlayerAbnormalStatusSnapshot beforeStatuses,
        Func<Task>? beforeMaintenanceKeyPress,
        SkillSnapshot? maintenanceSkill,
        bool isOneShotStatusMaintenance)
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

        var beforeIds = beforeStatuses.Entries
            .Select(entry => entry.AbnormalId)
            .Where(id => id != 0)
            .ToHashSet();
        var startedAt = DateTimeOffset.Now;
        var result = await _keyboard
            .PressKeyAsync(rule.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, rule.Key, resource, result.Error);
            return false;
        }

        state.MarkMaintenanceKeyAttempted(rule.Key, startedAt);
        var skillId = ResolveStatusMaintenanceSkillId(rule, maintenanceSkill);
        var skillName = maintenanceSkill?.Name ?? maintenanceSkill?.DisplayBaseName ?? rule.SkillName;
        if (isOneShotStatusMaintenance)
        {
            state.MarkOneShotStatusMaintenancePressed(skillId, skillName, rule.Key);
        }

        var deadline = startedAt + MaintenanceConfirmWindow;
        var polls = 0;

        while (DateTimeOffset.Now <= deadline)
        {
            polls++;
            var abnormalResult = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
            if (abnormalResult.Success && abnormalResult.Value is not null)
            {
                var confirmedAbnormalId = ResolveConfirmedStatusMaintenanceAbnormalId(
                    rule,
                    skillId,
                    state,
                    beforeIds,
                    abnormalResult.Value.Entries);
                if (confirmedAbnormalId != 0)
                {
                    if (skillId != 0)
                    {
                        state.RememberStatusMaintenanceAbnormalId(skillId, confirmedAbnormalId);
                    }

                    var completedAt = DateTimeOffset.Now;
                    context.Logger.Info("semi_auto.maintenance.status_key_pressed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["resource"] = resource,
                        ["key"] = rule.Key,
                        ["skillId"] = skillId,
                        ["skillName"] = skillName,
                        ["abnormalStatusId"] = confirmedAbnormalId,
                        ["oneShot"] = isOneShotStatusMaintenance,
                        ["polls"] = polls,
                        ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
                        ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds)
                    });
                    return true;
                }
            }
            else
            {
                LogStatusMaintenanceAbnormalReadFailed(context, state, resource, abnormalResult.Error);
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

        var completedAtUnconfirmed = DateTimeOffset.Now;
        context.Logger.Warn("semi_auto.maintenance.status_unconfirmed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = skillId,
            ["skillName"] = skillName,
            ["configuredAbnormalStatusId"] = rule.AbnormalStatusId,
            ["oneShot"] = isOneShotStatusMaintenance,
            ["polls"] = polls,
            ["confirmWindowMs"] = (long)MaintenanceConfirmWindow.TotalMilliseconds,
            ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAtUnconfirmed - startedAt).TotalMilliseconds)
        });
        return true;
    }

    private async Task<bool> ExecuteMaintenancePotionRuleAsync(
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
        int pressCount = 1,
        TimeSpan? pressInterval = null)
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

        var repeats = Math.Max(1, pressCount);
        var interval = pressInterval.GetValueOrDefault();
        for (var pressIndex = 1; pressIndex <= repeats; pressIndex++)
        {
            var result = await _keyboard
                .PressKeyAsync(rule.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                LogMaintenanceKeyFailure(context, state, rule.Key, "mp_potion", result.Error);
                return false;
            }

            if (pressIndex < repeats && interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, context.StopToken).ConfigureAwait(false);
            }
        }

        var completedAt = DateTimeOffset.Now;
        state.MarkMaintenanceKeyAttempted(rule.Key, completedAt);
        context.Logger.Info("semi_auto.maintenance.potion_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["current"] = current,
            ["max"] = max,
            ["percent"] = Math.Round(percent, 1),
            ["belowPercent"] = threshold,
            ["pressCount"] = repeats,
            ["pressIntervalMs"] = (long)interval.TotalMilliseconds
        });
        return true;
    }

    private static bool IsSpiritMpPotion(InventoryItemSnapshot item)
    {
        if (item.Count == 0 || item.Slot < 0 || item.IsEquipped || item.ItemType != 17)
        {
            return false;
        }

        var name = item.Name ?? string.Empty;
        return name.Contains("精神", StringComparison.Ordinal) &&
               (name.Contains("药水", StringComparison.Ordinal) ||
                name.Contains("仙药", StringComparison.Ordinal) ||
                name.Contains("灵药", StringComparison.Ordinal) ||
                name.Contains("恢复", StringComparison.Ordinal) ||
                name.Contains("秘药", StringComparison.Ordinal));
    }

    private static void LogMaintenancePotionInventoryReadFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        MaintenanceKeyRuleConfig rule,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.potion_inventory_read_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = rule.Key,
            ["error"] = error
        });
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
        PlayerSnapshot player,
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

        var baselineResult = await ReadAllSkillsAsync(context).ConfigureAwait(false);
        var baselineCooldowns = baselineResult.Success && baselineResult.Value is not null
            ? SnapshotCooldownEndTimes(baselineResult.Value)
            : null;
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
                    if (TryFindAdvancedCooldown(baselineCooldowns, skillsResult.Value, maintenanceSkill, out confirmedSkill))
                    {
                        TryUpdateMaintenanceCooldownCalibration(context, state, confirmedSkill);
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

    private async Task<bool> EnsureStandingBeforeMaintenanceKeyAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        PlayerSnapshot player,
        string resource,
        string maintenanceKey)
    {
        if (!player.IsResting)
        {
            return true;
        }

        var result = await _keyboard
            .PressKeyAsync(RestExitKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestExitKey, "rest_exit_before_key", result.Error);
            return false;
        }

        state.ClearMaintenanceRest();
        context.Logger.Info("semi_auto.maintenance.rest_exit_before_key", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = RestExitKey,
            ["resource"] = resource,
            ["maintenanceKey"] = maintenanceKey,
            ["hp"] = player.CurrentHp,
            ["maxHp"] = player.MaxHp,
            ["mp"] = player.CurrentMp,
            ["maxMp"] = player.MaxMp,
            ["stanceFlags"] = player.StanceFlags,
            ["stanceLow"] = player.StanceLowNibble,
            ["motionMode"] = player.MotionMode
        });

        await Task.Delay(MaintenanceRestExitBeforeKeyDelay, context.StopToken).ConfigureAwait(false);
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
                includeTriggerPrefix: decision.Kind == SemiAutoSkillReleaseDecisionKind.PressRoot)
            .ConfigureAwait(false);
        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.PressCondition)
        {
            if (pressed)
            {
                var confirmationExpiresAt = DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic);
                state.MarkSkillPressed(
                    decision.Skill,
                    confirmationExpiresAt,
                    retryKey: plan.UsesSpiritmasterAutoLogic ? node.Key : null,
                    retrySkillName: node.Name,
                    retrySkillType: node.Type,
                    retryPhase: "condition");
                state.SuppressUncalibratedUnknownSkill(decision.Skill, confirmationExpiresAt);
                var preemptedChain = state.HasChainWork;
                if (preemptedChain)
                {
                    var pendingNode = state.PendingChainNextNode ?? node;
                    LogChainEnded(context, pendingNode, "condition_preempted", decision.Skill);
                    state.ClearChain();
                }

                LogConditionSkillPressed(context, node, decision, preemptedChain);
            }

            return;
        }

        if (decision.Kind == SemiAutoSkillReleaseDecisionKind.PressChain)
        {
            if (pressed)
            {
                state.MarkSkillPressed(
                    decision.Skill,
                    DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic),
                    retryKey: plan.UsesSpiritmasterAutoLogic ? node.Key : null,
                    retrySkillName: node.Name,
                    retrySkillType: node.Type,
                    retryPhase: "skill");
                if (state.IsPendingChainNextNode(node))
                {
                    state.MarkPendingChainNextPressed(decision.Skill);
                }
                else
                {
                    StartPendingChainConfirmation(context, state, node, decision.Skill, settings);
                }

                if (ShouldLearnSpiritmasterDotAfterPress(context, plan, node, decision.Skill))
                {
                    await TryLearnSpiritmasterDotAfterPressAsync(context, state, decision.Skill).ConfigureAwait(false);
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
            var confirmationExpiresAt = DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic);
            state.MarkSkillPressed(
                decision.Skill,
                confirmationExpiresAt,
                retryKey: plan.UsesSpiritmasterAutoLogic ? node.Key : null,
                retrySkillName: node.Name,
                retrySkillType: node.Type,
                retryPhase: "skill");
            state.SuppressUncalibratedUnknownSkill(decision.Skill, confirmationExpiresAt);
            if (node.Children.Count > 0)
            {
                StartPendingChainAdvance(context, state, node, decision.Skill, settings);
            }

            if (ShouldLearnSpiritmasterDotAfterPress(context, plan, node, decision.Skill))
            {
                await TryLearnSpiritmasterDotAfterPressAsync(context, state, decision.Skill).ConfigureAwait(false);
            }
        }
    }

    private async Task<SpiritmasterCombatContext> ReadSpiritmasterCombatContextAsync(
        AccountWorkerContext context,
        LockedTargetSnapshot target)
    {
        PlayerSnapshot? player = null;
        SummonedPetRosterSnapshot? petRoster = null;
        LockedTargetAbnormalStatusSnapshot? lockedTargetAbnormalStatuses = null;

        var playerResult = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (playerResult.Success)
        {
            player = playerResult.Value;
        }

        if (player?.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            return new SpiritmasterCombatContext(player, null, null);
        }

        var rosterResult = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        if (rosterResult.Success)
        {
            petRoster = rosterResult.Value;
        }

        var abnormalResult = await ReadLockedTargetAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (abnormalResult.Success)
        {
            lockedTargetAbnormalStatuses = abnormalResult.Value;
        }

        lockedTargetAbnormalStatuses ??= new LockedTargetAbnormalStatusSnapshot(
            target,
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            DateTimeOffset.Now);

        return new SpiritmasterCombatContext(player, petRoster, lockedTargetAbnormalStatuses);
    }

    private async Task<bool> TryHandleSpiritmasterSpecialAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterCombatContext spiritContext)
    {
        if (!spiritContext.CanUseSpiritmasterLogic)
        {
            return false;
        }

        var pet = spiritContext.LocalPet?.Pet;
        if (pet is not { IsSummoned: true, IsAlive: true })
        {
            return await TryPressSpiritmasterSummonAsync(context, state, settings, spiritSettings).ConfigureAwait(false);
        }

        if (await TryPressSpiritmasterPetHpRuleAsync(
                context,
                state,
                settings,
                spiritSettings,
                skills,
                pet)
            .ConfigureAwait(false))
        {
            return true;
        }

        return await TryPressSpiritmasterPetBuffRuleAsync(
                context,
                state,
                settings,
                spiritSettings,
                skills,
                spiritContext.LocalPet!,
                spiritContext.Player)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryPressSpiritmasterSummonAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings)
    {
        var keys = spiritSettings.SummonSkills
            .Select(rule => rule.Key?.Trim() ?? string.Empty)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Take(2)
            .ToArray();
        if (keys.Length == 0)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (!state.ShouldAttemptSpiritmasterSummon(now, SpiritmasterSummonAttemptInterval))
        {
            return true;
        }

        state.MarkSpiritmasterSummonAttempted(now);
        if (!await PressSpiritmasterRawKeyAsync(context, settings, keys[0], "summon_speed").ConfigureAwait(false))
        {
            state.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, SpiritmasterSummonVerifyWindow);
            return true;
        }

        if (keys.Length > 1)
        {
            await Task.Delay(SpiritmasterSummonKeyInterval, context.StopToken).ConfigureAwait(false);
            await PressSpiritmasterRawKeyAsync(context, settings, keys[1], "summon_pet").ConfigureAwait(false);
        }

        state.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, SpiritmasterSummonVerifyWindow);
        return true;
    }

    private async Task<bool> PressSpiritmasterOpeningAttackKeyIfNeededAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        LockedTargetSnapshot target)
    {
        var key = spiritSettings.OpeningAttackKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key) ||
            !state.ShouldPressSpiritmasterOpeningAttackKey(target))
        {
            return false;
        }

        state.MarkSpiritmasterOpeningAttackKeyAttempted(target);
        context.Logger.Info("semi_auto.spiritmaster.opening_attack_key.started", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["pressCount"] = 2,
            ["intervalMs"] = (long)SpiritmasterOpeningAttackKeyInterval.TotalMilliseconds,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name
        });

        if (!await PressSpiritmasterRawKeyAsync(context, settings, key, "opening_attack").ConfigureAwait(false))
        {
            return true;
        }

        await Task.Delay(SpiritmasterOpeningAttackKeyInterval, context.StopToken).ConfigureAwait(false);
        await PressSpiritmasterRawKeyAsync(context, settings, key, "opening_attack").ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryPressSpiritmasterPetHpRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills,
        SummonedPetSnapshot pet)
    {
        if (!pet.HasKnownHealth)
        {
            return false;
        }

        foreach (var rule in spiritSettings.PetHpMaintenanceRules
                     .Where(rule => !string.IsNullOrWhiteSpace(rule.Key))
                     .OrderBy(rule => Math.Clamp(rule.BelowPercent, 0, 100)))
        {
            if (pet.HpPercent > Math.Clamp(rule.BelowPercent, 0, 100))
            {
                continue;
            }

            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            if (skill is null)
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (!state.ShouldPressSpiritmasterPetHpSkill(skill.SkillId, now) ||
                ShouldSkipSpiritmasterSpecialSkillForCooldown(skill, state, now))
            {
                continue;
            }

            if (!await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_hp").ConfigureAwait(false))
            {
                return true;
            }

            MarkSpiritmasterSkillPressed(state, settings, skill);
            var cooldown = ResolveSpiritmasterPetHpCooldown(rule);
            state.MarkSpiritmasterPetHpSkillPressed(skill.SkillId, DateTimeOffset.Now, cooldown);
            context.Logger.Info("semi_auto.spiritmaster.pet_hp_key_pressed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = rule.Key,
                ["skillId"] = skill.SkillId,
                ["skillName"] = skill.Name,
                ["petHpPercent"] = pet.HpPercent,
                ["belowPercent"] = rule.BelowPercent,
                ["cooldownMs"] = (int)cooldown.TotalMilliseconds
            });
            return true;
        }

        return false;
    }

    private async Task<bool> TryPressSpiritmasterPetBuffRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills,
        OwnedSummonedPetSnapshot localPet,
        PlayerSnapshot? player)
    {
        foreach (var rule in spiritSettings.PetBuffRules.Where(rule => !string.IsNullOrWhiteSpace(rule.Key)))
        {
            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            if (skill is null)
            {
                continue;
            }

            if (ShouldSkipSpiritmasterSpecialSkillForCooldown(skill, state, DateTimeOffset.Now))
            {
                continue;
            }

            var requiredDp = ResolveRequiredDp(skill);
            if (requiredDp > 0)
            {
                var currentDp = player?.CurrentDp ?? 0;
                if (currentDp < requiredDp)
                {
                    context.Logger.Info("semi_auto.spiritmaster.pet_buff_dp_skip", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["key"] = rule.Key,
                        ["skillId"] = skill.SkillId,
                        ["skillName"] = skill.Name,
                        ["currentDp"] = currentDp,
                        ["requiredDp"] = requiredDp
                    });
                    continue;
                }
            }

            if (HasSpiritmasterPetBuff(state, rule, skill, localPet.AbnormalStatuses))
            {
                continue;
            }

            var beforeIds = localPet.AbnormalStatuses
                .Select(entry => entry.AbnormalId)
                .Where(id => id != 0)
                .ToHashSet();

            if (!await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_buff").ConfigureAwait(false))
            {
                return true;
            }

            MarkSpiritmasterSkillPressed(state, settings, skill);
            await TryLearnSpiritmasterPetBuffAfterPressAsync(
                    context,
                    state,
                    skill,
                    beforeIds)
                .ConfigureAwait(false);
            context.Logger.Info("semi_auto.spiritmaster.pet_buff_key_pressed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = rule.Key,
                ["skillId"] = skill.SkillId,
                ["skillName"] = skill.Name,
                ["requiredDp"] = requiredDp
            });
            return true;
        }

        return false;
    }

    private async Task TryLearnSpiritmasterPetBuffAfterPressAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SkillSnapshot skill,
        HashSet<uint> beforeIds)
    {
        var rosterResult = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        if (!rosterResult.Success || rosterResult.Value is null)
        {
            return;
        }

        var afterEntries = rosterResult.Value.LocalPlayerPet.AbnormalStatuses;
        var learned = afterEntries
            .Where(entry => entry.AbnormalId != 0 && !beforeIds.Contains(entry.AbnormalId))
            .OrderByDescending(entry => entry.IsBuffCategory)
            .Select(entry => entry.AbnormalId)
            .FirstOrDefault();
        if (learned == 0 && afterEntries.Any(entry => entry.AbnormalId == skill.SkillId))
        {
            learned = skill.SkillId;
        }

        if (learned == 0)
        {
            return;
        }

        state.RememberSpiritmasterPetBuffAbnormalId(skill.SkillId, learned);
        context.Logger.Info("semi_auto.spiritmaster.pet_buff_learned", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skillId"] = skill.SkillId,
            ["skillName"] = skill.Name,
            ["abnormalId"] = learned
        });
    }

    private async Task TryLearnSpiritmasterDotAfterPressAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SkillSnapshot skill)
    {
        var abnormalResult = await ReadLockedTargetAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (!abnormalResult.Success || abnormalResult.Value is null)
        {
            return;
        }

        var target = abnormalResult.Value.Target;
        var targetId = target.ServerObjectId != 0 ? target.ServerObjectId : target.TargetEntityId;
        if (targetId == 0)
        {
            return;
        }

        if (!state.TryCompleteSpiritmasterDotObservation(
                targetId,
                abnormalResult.Value.Entries,
                DateTimeOffset.Now,
                out var learnedSkillId,
                out var abnormalId))
        {
            return;
        }

        context.Logger.Info("semi_auto.spiritmaster.dot_learned", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skillId"] = learnedSkillId,
            ["skillName"] = skill.Name,
            ["abnormalId"] = abnormalId,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId
        });
    }

    private async Task<bool> PressSpiritmasterRawKeyAsync(
        AccountWorkerContext context,
        SemiAutoScriptSettings settings,
        string key,
        string phase)
    {
        var normalizedKey = key?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var result = await _keyboard
            .PressKeyAsync(normalizedKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("semi_auto.spiritmaster.key_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = normalizedKey,
                ["phase"] = phase,
                ["error"] = result.Error
            });
            return false;
        }

        context.Logger.Info("semi_auto.spiritmaster.key_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = normalizedKey,
            ["phase"] = phase
        });
        return true;
    }

    private static void MarkSpiritmasterSkillPressed(
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SkillSnapshot skill)
    {
        var confirmationExpiresAt = DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, useSpiritmasterMinimum: true);
        state.MarkSkillPressed(skill, confirmationExpiresAt);
        state.SuppressUncalibratedUnknownSkill(skill, confirmationExpiresAt);
    }

    private static bool ShouldSkipSpiritmasterSpecialSkillForCooldown(
        SkillSnapshot skill,
        SemiAutoCombatState state,
        DateTimeOffset now)
    {
        if (state.IsUncalibratedUnknownSuppressed(skill, now))
        {
            return true;
        }

        var readiness = GetMaintenanceCooldownReadiness(skill, state);
        return readiness == SemiAutoSkillCooldownReadiness.CoolingDown;
    }

    private static TimeSpan ResolveSpiritmasterPetHpCooldown(SpiritmasterPetHpRuleConfig rule)
    {
        var configuredCooldownMs = rule.CooldownMs <= 0
            ? SpiritmasterPetHpRuleConfig.DefaultCooldownMs
            : rule.CooldownMs;
        var cooldownMs = Math.Clamp(
            configuredCooldownMs,
            SpiritmasterPetHpRuleConfig.MinCooldownMs,
            SpiritmasterPetHpRuleConfig.MaxCooldownMs);
        return TimeSpan.FromMilliseconds(cooldownMs);
    }

    private static bool ShouldLearnSpiritmasterDotAfterPress(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoSkillNode node,
        SkillSnapshot skill)
    {
        if (!plan.UsesSpiritmasterAutoLogic)
        {
            return false;
        }

        var spiritSettings = context.Config.ScriptSettings?.Skills.Spiritmaster ?? new SpiritmasterSkillSettings();
        return SpiritmasterAutoSkillReleasePriority.IsConfiguredDotSkill(node, skill, spiritSettings);
    }

    private static bool HasSpiritmasterPetBuff(
        SemiAutoCombatState state,
        SpiritmasterPetBuffRuleConfig rule,
        SkillSnapshot skill,
        IReadOnlyList<AbnormalStatusEntrySnapshot> abnormalStatuses)
    {
        if (rule.AbnormalStatusId != 0 &&
            abnormalStatuses.Any(entry => entry.AbnormalId == rule.AbnormalStatusId))
        {
            state.RememberSpiritmasterPetBuffAbnormalId(skill.SkillId, rule.AbnormalStatusId);
            return true;
        }

        if (state.TryGetSpiritmasterPetBuffAbnormalId(skill.SkillId, out var learnedAbnormalId) &&
            abnormalStatuses.Any(entry => entry.AbnormalId == learnedAbnormalId))
        {
            return true;
        }

        if (abnormalStatuses.Any(entry => entry.AbnormalId == skill.SkillId))
        {
            state.RememberSpiritmasterPetBuffAbnormalId(skill.SkillId, skill.SkillId);
            return true;
        }

        return false;
    }

    private static int ResolveRequiredDp(SkillSnapshot skill)
    {
        if (!string.IsNullOrWhiteSpace(skill.XmlCostDp))
        {
            var digits = new string(skill.XmlCostDp.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed) && parsed > 0)
            {
                return parsed;
            }
        }

        return skill.SkillId == 1787 ? 2000 : 0;
    }

    private static SkillSnapshot? ResolveSpiritmasterConfiguredSkill(
        uint skillId,
        string? skillName,
        IReadOnlyList<SkillSnapshot> skills)
    {
        return skills.FirstOrDefault(skill => MatchesMaintenanceSkill(skill, skillId, skillName));
    }

    private static IReadOnlyList<SkillSnapshot> ResolveSpiritmasterConfiguredSkills(
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills)
    {
        var result = new List<SkillSnapshot>();
        foreach (var rule in spiritSettings.DotSkills)
        {
            AddResolved(rule.SkillId, rule.SkillName);
        }

        foreach (var rule in spiritSettings.PetHpMaintenanceRules)
        {
            AddResolved(rule.SkillId, rule.SkillName);
        }

        foreach (var rule in spiritSettings.PetBuffRules)
        {
            AddResolved(rule.SkillId, rule.SkillName);
        }

        return result;

        void AddResolved(uint skillId, string skillName)
        {
            var skill = ResolveSpiritmasterConfiguredSkill(skillId, skillName, skills);
            if (skill is not null && result.All(item => item.SkillId != skill.SkillId))
            {
                result.Add(skill);
            }
        }
    }

    private static IReadOnlyList<SkillSnapshot> MergeSkillSnapshots(
        IReadOnlyList<SkillSnapshot> first,
        IReadOnlyList<SkillSnapshot> second)
    {
        if (second.Count == 0)
        {
            return first;
        }

        var result = new List<SkillSnapshot>(first.Count + second.Count);
        var seen = new HashSet<uint>();
        foreach (var skill in first.Concat(second))
        {
            if (seen.Add(skill.SkillId))
            {
                result.Add(skill);
            }
        }

        return result;
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

    private async Task<bool> ConfirmRetryablePressedSkillCooldownIfNeededAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SemiAutoSkillPlan plan)
    {
        if (!state.HasPressedSkillCooldownRetryKey())
        {
            return false;
        }

        var skillsResult = await ReadSkillsAsync(context, plan).ConfigureAwait(false);
        var observedSkills = skillsResult.Success && skillsResult.Value is not null
            ? ResolveConfiguredSkills(plan, skillsResult.Value)
            : Array.Empty<SkillSnapshot>();

        if (!state.IsAwaitingPressedSkillCooldownConfirmation(
                observedSkills,
                DateTimeOffset.Now,
                out _,
                out _))
        {
            return false;
        }

        await PressPendingSkillCooldownRetryIfDueAsync(context, state, settings).ConfigureAwait(false);
        return true;
    }

    private async Task PressPendingSkillCooldownRetryIfDueAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings)
    {
        var now = DateTimeOffset.Now;
        if (!state.TryGetPressedSkillCooldownRetry(
                now,
                SpiritmasterCooldownConfirmRetryInterval,
                out var retry))
        {
            return;
        }

        var result = await _keyboard
            .PressKeyAsync(retry.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        state.MarkPressedSkillCooldownRetried(DateTimeOffset.Now);
        if (!result.Success)
        {
            context.Logger.Warn("semi_auto.key.retry_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skill"] = retry.SkillName,
                ["key"] = retry.Key,
                ["phase"] = retry.Phase,
                ["error"] = result.Error
            });
            return;
        }

        context.Logger.Info("semi_auto.key.retry_until_cooldown", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = retry.SkillName,
            ["key"] = retry.Key,
            ["type"] = retry.SkillType,
            ["phase"] = retry.Phase,
            ["retryIntervalMs"] = (long)SpiritmasterCooldownConfirmRetryInterval.TotalMilliseconds
        });
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

            if (SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.CoolingDown)
            {
                return false;
            }
        }

        return hasExecutableRoot;
    }

    private async Task<bool> PressOpeningSkillIfNeededAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SemiAutoSkillPlan plan,
        LockedTargetSnapshot target)
    {
        var openingSkill = plan.OpeningSkill;
        if (openingSkill is null || !state.ShouldHandleOpeningSkill(target))
        {
            return false;
        }

        var skillsResult = await ReadOpeningSkillAsync(context, openingSkill).ConfigureAwait(false);
        if (!skillsResult.Success || skillsResult.Value is null)
        {
            state.MarkOpeningSkillHandled(target);
            context.Logger.Warn("semi_auto.opening_skill.read_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.TargetEntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name,
                ["skill"] = openingSkill.Name,
                ["skillId"] = openingSkill.SkillId,
                ["key"] = openingSkill.Key,
                ["error"] = skillsResult.Error
            });
            return false;
        }

        var skill = openingSkill.ResolveSkill(skillsResult.Value);
        if (skill is null)
        {
            state.MarkOpeningSkillHandled(target);
            LogOpeningSkillSkipped(context, target, openingSkill, null, "missing");
            return false;
        }

        var readiness = SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state);
        if (readiness != SemiAutoSkillCooldownReadiness.Ready)
        {
            state.MarkOpeningSkillHandled(target);
            LogOpeningSkillSkipped(
                context,
                target,
                openingSkill,
                skill,
                SemiAutoSkillReleasePriority.FormatCooldownReason(skill, state));
            return false;
        }

        var pressed = await PressNodeKeyAsync(context, openingSkill, settings, "opening_skill").ConfigureAwait(false);
        if (!pressed)
        {
            return false;
        }

        state.MarkOpeningSkillHandled(target);
        var confirmationExpiresAt = DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic);
        state.MarkSkillPressed(
            skill,
            confirmationExpiresAt,
            retryKey: openingSkill.Key,
            retrySkillName: openingSkill.Name,
            retrySkillType: openingSkill.Type,
            retryPhase: "opening_skill");
        state.SuppressUncalibratedUnknownSkill(skill, confirmationExpiresAt);
        return true;
    }

    private static void LogOpeningSkillSkipped(
        AccountWorkerContext context,
        LockedTargetSnapshot target,
        SemiAutoSkillNode node,
        SkillSnapshot? skill,
        string reason)
    {
        context.Logger.Info("semi_auto.opening_skill.skipped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["skill"] = node.Name,
            ["skillId"] = node.SkillId,
            ["matchedSkillId"] = skill?.SkillId,
            ["cooldownDuration"] = skill?.CooldownDuration,
            ["cooldownEndTime"] = skill?.CooldownEndTime,
            ["key"] = node.Key,
            ["reason"] = reason
        });
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

        void AddResolvedSkill(SemiAutoSkillNode node)
        {
            var skill = node.ResolveSkill(learnedSkills);
            if (skill is null || !seenSkillIds.Add(skill.SkillId))
            {
                return;
            }

            configuredSkills.Add(skill);
        }

        if (plan.OpeningSkill is not null)
        {
            AddResolvedSkill(plan.OpeningSkill);
        }

        foreach (var node in FlattenNodes(plan.Roots))
        {
            AddResolvedSkill(node);
        }

        return configuredSkills;
    }

    private static IReadOnlyList<SkillSnapshot> ResolveCooldownInvalidationSkills(
        SemiAutoSkillPlan plan,
        IReadOnlyList<SkillSnapshot> configuredSkills)
    {
        var skills = new List<SkillSnapshot>();
        var seenSkillIds = new HashSet<uint>();
        foreach (var root in plan.Roots)
        {
            if (root.IsTrigger || root.IsDp)
            {
                continue;
            }

            var skill = root.ResolveSkill(configuredSkills);
            if (skill is not null && seenSkillIds.Add(skill.SkillId))
            {
                skills.Add(skill);
            }
        }

        return skills;
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
        var windowMs = ResolveChainWindowMs(sourceNode, settings);
        state.StartPendingChainAdvance(
            sourceNode,
            nextNode,
            DateTimeOffset.MinValue,
            sourceSkill.CooldownEndTime,
            windowMs);
        LogPendingChain(context, state, sourceNode, sourceSkill.CooldownEndTime, nextNode, windowMs);
    }

    private static void TryStartPendingChainWindowFromRootCooldown(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> configuredSkills,
        DateTimeOffset now,
        SemiAutoScriptSettings settings)
    {
        var sourceNode = state.PendingChainSourceNode;
        if (sourceNode is null || state.HasPendingChainWindowStarted)
        {
            return;
        }

        var sourceSkill = sourceNode.ResolveSkill(configuredSkills);
        if (sourceSkill is null || !state.HasPendingChainSourceCooldownAdvanced(sourceSkill))
        {
            return;
        }

        var windowMs = state.PendingChainWindowMs > 0
            ? state.PendingChainWindowMs
            : ResolveChainWindowMs(sourceNode, settings);
        var expiresAt = now + TimeSpan.FromMilliseconds(Math.Max(1, windowMs));
        state.StartPendingChainWindow(expiresAt);
        context.Logger.Info("semi_auto.chain.window_started", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["sourceSkill"] = sourceNode.Name,
            ["sourceKey"] = sourceNode.Key,
            ["sourceCooldownEndTime"] = sourceSkill.CooldownEndTime,
            ["windowMs"] = windowMs,
            ["expiresInMs"] = windowMs
        });
    }

    private static void LogPendingChain(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode sourceNode,
        uint sourceCooldownEndTime,
        SemiAutoSkillNode nextNode,
        int windowMs)
    {
        var expiresInMs = state.HasPendingChainWindowStarted
            ? Math.Max(0, (int)Math.Ceiling((state.PendingChainExpiresAt - DateTimeOffset.Now).TotalMilliseconds))
            : (int?)null;
        context.Logger.Info("semi_auto.chain.pending", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["sourceSkill"] = sourceNode.Name,
            ["sourceKey"] = sourceNode.Key,
            ["sourceCooldownEndTime"] = sourceCooldownEndTime,
            ["nextSkill"] = nextNode.Name,
            ["nextKey"] = nextNode.Key,
            ["configuredChildCount"] = sourceNode.Children.Count,
            ["windowMs"] = windowMs,
            ["windowStarted"] = state.HasPendingChainWindowStarted,
            ["expiresInMs"] = expiresInMs
        });
    }

    private static void StartPendingChainConfirmation(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoSkillNode chainNode,
        SkillSnapshot chainSkill,
        SemiAutoScriptSettings settings)
    {
        var sourceNode = state.PendingChainSourceNode ?? ResolveChainRoot(chainNode);
        var windowMs = state.PendingChainWindowMs > 0
            ? state.PendingChainWindowMs
            : ResolveChainWindowMs(sourceNode, settings);
        var sourceCooldownEndTime = state.PendingChainSourceNode is not null
            ? state.PendingChainSourceCooldownEndTime
            : chainSkill.CooldownEndTime;
        state.StartPendingChainAdvance(
            sourceNode,
            chainNode,
            state.PendingChainExpiresAt,
            sourceCooldownEndTime,
            windowMs);
        state.MarkPendingChainNextPressed(chainSkill);
        LogPendingChain(context, state, sourceNode, sourceCooldownEndTime, chainNode, windowMs);
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

    private static void LogConditionSkillPressed(
        AccountWorkerContext context,
        SemiAutoSkillNode node,
        SemiAutoSkillReleaseDecision decision,
        bool preemptedChain)
    {
        context.Logger.Info("semi_auto.condition_skill.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = node.Name,
            ["key"] = node.Key,
            ["conditionStatus"] = decision.ConditionStatus,
            ["conditionAbnormalId"] = decision.ConditionAbnormalId,
            ["preemptedChain"] = preemptedChain
        });
    }

    private static int ResolveChainWindowMs(SemiAutoSkillNode sourceNode, SemiAutoScriptSettings settings)
    {
        var perLinkMs = ResolveChainWindowPerLinkMs(settings);
        return Math.Max(1, ResolveMaxChainDepth(sourceNode) - 1) * perLinkMs;
    }

    private static int ResolveChainWindowPerLinkMs(SemiAutoScriptSettings settings)
    {
        var configured = settings.ChainWindowPerLinkMs;
        if (configured <= 0)
        {
            configured = SemiAutoScriptSettings.DefaultChainWindowPerLinkMs;
        }

        return Math.Clamp(
            configured,
            SemiAutoScriptSettings.MinimumChainWindowPerLinkMs,
            SemiAutoScriptSettings.MaximumChainWindowPerLinkMs);
    }

    private static int ResolveMaxChainDepth(SemiAutoSkillNode node)
    {
        if (node.Children.Count == 0)
        {
            return 1;
        }

        var maxChildDepth = 0;
        foreach (var child in node.Children)
        {
            maxChildDepth = Math.Max(maxChildDepth, ResolveMaxChainDepth(child));
        }

        return 1 + maxChildDepth;
    }

    private static SemiAutoSkillNode ResolveChainRoot(SemiAutoSkillNode node)
    {
        var current = node;
        while (current.Parent is not null)
        {
            current = current.Parent;
        }

        return current;
    }

    private static bool HasMaintenanceWork(
        MaintenanceScriptSettings maintenance,
        bool allowSitMaintenance,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return (allowSitMaintenance && maintenance.SitMaintenanceEnabled) ||
               HasMaintenanceRules(maintenance.HpMaintenanceRules, runTiming, includeAlwaysRules) ||
               HasMaintenanceRules(maintenance.MpMaintenanceRules, runTiming, includeAlwaysRules) ||
               HasStatusMaintenanceRules(maintenance.StatusMaintenanceRules, runTiming, includeAlwaysRules) ||
               HasDpMaintenanceRules(maintenance.DpMaintenanceRules, runTiming, includeAlwaysRules);
    }

    private static bool HasMaintenanceRules(
        IEnumerable<MaintenanceKeyRuleConfig>? rules,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rules?.Any(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                                  IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules)) == true;
    }

    private static bool HasStatusMaintenanceRules(
        IEnumerable<StatusMaintenanceRuleConfig>? rules,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rules?.Any(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                                   HasStatusMaintenanceRuleSelection(rule) &&
                                   IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules)) == true;
    }

    private static bool HasDpMaintenanceRules(
        IEnumerable<DpMaintenanceRuleConfig>? rules,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rules?.Any(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
                                  IsMaintenanceRuleAllowed(rule, runTiming, includeAlwaysRules)) == true;
    }

    private static bool IsMaintenanceRuleAllowed(
        MaintenanceKeyRuleConfig rule,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rule.RunTiming == runTiming ||
               (includeAlwaysRules && rule.RunTiming == MaintenanceRuleRunTiming.Always);
    }

    private static bool IsMaintenanceRuleAllowed(
        DpMaintenanceRuleConfig rule,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rule.RunTiming == runTiming ||
               (includeAlwaysRules && rule.RunTiming == MaintenanceRuleRunTiming.Always);
    }

    private static bool IsMaintenanceRuleAllowed(
        StatusMaintenanceRuleConfig rule,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules)
    {
        return rule.RunTiming == runTiming ||
               (includeAlwaysRules && rule.RunTiming == MaintenanceRuleRunTiming.Always);
    }

    private static bool IsOneShotStatusMaintenanceRule(
        StatusMaintenanceRuleConfig rule,
        SkillSnapshot? skill)
    {
        return ContainsChantToken(rule.SkillName) ||
               ContainsChantToken(skill?.Name) ||
               ContainsChantToken(skill?.DisplayBaseName) ||
               ContainsChantToken(skill?.XmlSkillCategory) ||
               ContainsChantToken(skill?.XmlSkillType) ||
               ContainsChantToken(skill?.XmlSubType) ||
               ContainsChantToken(skill?.XmlTags);
    }

    private static bool ContainsChantToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (value.Contains("\u771F\u8A00", StringComparison.Ordinal) ||
                value.Contains("Chant", StringComparison.OrdinalIgnoreCase));
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

    private static int GetMaintenanceRuleActionPriority(string resource, MaintenanceKeyRuleConfig rule)
    {
        return string.Equals(resource, "mp", StringComparison.OrdinalIgnoreCase) &&
               rule.ActionType == MaintenanceRuleActionType.Potion
            ? 0
            : 1;
    }

    private static bool HasExplicitStatusMaintenanceSkill(StatusMaintenanceRuleConfig rule)
    {
        return rule.SkillId != 0 || !string.IsNullOrWhiteSpace(rule.SkillName);
    }

    private static bool HasExplicitDpMaintenanceSkill(DpMaintenanceRuleConfig rule)
    {
        return rule.SkillId != 0 || !string.IsNullOrWhiteSpace(rule.SkillName);
    }

    private static bool HasStatusMaintenanceRuleSelection(StatusMaintenanceRuleConfig rule)
    {
        return HasExplicitStatusMaintenanceSkill(rule) || rule.AbnormalStatusId != 0;
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

    private static SkillSnapshot? ResolveStatusMaintenanceRuleSkill(
        StatusMaintenanceRuleConfig rule,
        IReadOnlyList<SkillSnapshot> skills)
    {
        return skills.FirstOrDefault(skill => MatchesMaintenanceSkill(skill, rule.SkillId, rule.SkillName));
    }

    private static SkillSnapshot? ResolveDpMaintenanceRuleSkill(
        DpMaintenanceRuleConfig rule,
        IReadOnlyList<SkillSnapshot> skills)
    {
        return skills.FirstOrDefault(skill => MatchesMaintenanceSkill(skill, rule.SkillId, rule.SkillName));
    }

    private static int NormalizeRequiredDp(int requiredDp)
    {
        return Math.Clamp(requiredDp, 1, 4000);
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
        return SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state);
    }

    private static uint ResolveStatusMaintenanceSkillId(
        StatusMaintenanceRuleConfig rule,
        SkillSnapshot? skill)
    {
        if (skill is not null)
        {
            return skill.SkillId;
        }

        return rule.SkillId;
    }

    private static bool IsStatusMaintenanceActive(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        SemiAutoCombatState state,
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries)
    {
        return ResolveKnownStatusMaintenanceAbnormalId(rule, skillId, state, entries) != 0;
    }

    private static uint ResolveConfirmedStatusMaintenanceAbnormalId(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        SemiAutoCombatState state,
        HashSet<uint> beforeIds,
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries)
    {
        var known = ResolveKnownStatusMaintenanceAbnormalId(rule, skillId, state, entries);
        if (known != 0)
        {
            return known;
        }

        return entries
            .Where(entry => entry.AbnormalId != 0 && !beforeIds.Contains(entry.AbnormalId))
            .OrderByDescending(entry => skillId != 0 && entry.AbnormalId == skillId)
            .ThenByDescending(entry => entry.Category == 0)
            .ThenByDescending(entry => entry.IsBuffCategory)
            .Select(entry => entry.AbnormalId)
            .FirstOrDefault();
    }

    private static uint ResolveKnownStatusMaintenanceAbnormalId(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        SemiAutoCombatState state,
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries)
    {
        if (rule.AbnormalStatusId != 0 &&
            entries.Any(entry => entry.AbnormalId == rule.AbnormalStatusId))
        {
            return rule.AbnormalStatusId;
        }

        if (skillId != 0 &&
            state.TryGetStatusMaintenanceAbnormalId(skillId, out var learnedAbnormalId) &&
            entries.Any(entry => entry.AbnormalId == learnedAbnormalId))
        {
            return learnedAbnormalId;
        }

        if (skillId != 0 &&
            entries.Any(entry => entry.AbnormalId == skillId))
        {
            return skillId;
        }

        return 0;
    }

    private static void TryUpdateMaintenanceCooldownCalibration(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SkillSnapshot? skill)
    {
        if (skill is null)
        {
            return;
        }

        TryUpdateMaintenanceCooldownCalibration(context, state, new[] { skill });
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

    private static void LogStatusMaintenanceRuleSkippedCooling(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        StatusMaintenanceRuleConfig rule,
        string resource,
        SkillSnapshot skill)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Info("semi_auto.maintenance.status_skill_cooling", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = skill.SkillId,
            ["skillName"] = skill.Name,
            ["abnormalStatusId"] = rule.AbnormalStatusId,
            ["cooldownDuration"] = skill.CooldownDuration,
            ["cooldownEndTime"] = skill.CooldownEndTime,
            ["cooldownOffsetMs"] = state.CooldownTickOffsetMs
        });
    }

    private static void LogStatusMaintenanceRuleSkippedMissing(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        StatusMaintenanceRuleConfig rule,
        string resource)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.status_skill_missing", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["abnormalStatusId"] = rule.AbnormalStatusId
        });
    }

    private static void LogStatusMaintenanceRuleSkillReadFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        StatusMaintenanceRuleConfig rule,
        string resource,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.status_skill_read_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["abnormalStatusId"] = rule.AbnormalStatusId,
            ["error"] = error
        });
    }

    private static void LogStatusMaintenanceOneShotSkipped(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        StatusMaintenanceRuleConfig rule,
        string resource,
        uint skillId,
        string? skillName)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Info("semi_auto.maintenance.status_one_shot_skipped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["skillId"] = skillId,
            ["skillName"] = skillName,
            ["configuredSkillName"] = rule.SkillName,
            ["abnormalStatusId"] = rule.AbnormalStatusId
        });
    }

    private static void LogDpMaintenanceRuleSkippedCooling(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        DpMaintenanceRuleConfig rule,
        string resource,
        SkillSnapshot skill)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Info("semi_auto.maintenance.dp_skill_cooling", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["requiredDp"] = NormalizeRequiredDp(rule.RequiredDp),
            ["skillId"] = skill.SkillId,
            ["skillName"] = skill.Name,
            ["cooldownDuration"] = skill.CooldownDuration,
            ["cooldownEndTime"] = skill.CooldownEndTime,
            ["cooldownOffsetMs"] = state.CooldownTickOffsetMs
        });
    }

    private static void LogDpMaintenanceRuleSkippedMissing(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        DpMaintenanceRuleConfig rule,
        string resource)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.dp_skill_missing", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["requiredDp"] = NormalizeRequiredDp(rule.RequiredDp),
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName
        });
    }

    private static void LogDpMaintenanceRuleSkillReadFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        DpMaintenanceRuleConfig rule,
        string resource,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.dp_skill_read_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
            ["key"] = rule.Key,
            ["requiredDp"] = NormalizeRequiredDp(rule.RequiredDp),
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["error"] = error
        });
    }

    private static void LogStatusMaintenanceAbnormalReadFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        string resource,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.status_abnormal_read_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["resource"] = resource,
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

    private static Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadInventoryAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadInventoryAsync(context.StopToken);
    }

    private static bool ShouldReadTargetConditionAbnormalStatuses(
        SemiAutoScriptSettings settings,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> configuredSkills)
    {
        if (state.HasChainWork && !settings.ConditionSkillPreemptsChain)
        {
            return false;
        }

        return SemiAutoSkillReleasePriority.HasRunnableTargetConditionSkill(plan, state, configuredSkills);
    }

    private static async Task<LockedTargetAbnormalStatusSnapshot?> ReadTargetConditionAbnormalStatusesAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state)
    {
        var result = await ReadLockedTargetAbnormalStatusesAsync(context).ConfigureAwait(false);
        if (result.Success && result.Value is not null)
        {
            return result.Value;
        }

        var now = DateTimeOffset.Now;
        if (ShouldLog(state.LastConditionSkillWarningAt, now))
        {
            state.LastConditionSkillWarningAt = now;
            context.Logger.Warn("semi_auto.condition_skill.target_abnormal_read_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = result.Error
            });
        }

        return null;
    }

    private static Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadPlayerAbnormalStatusesAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadPlayerAbnormalStatusesAsync(context.StopToken);
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadLockedTargetAbnormalStatusesAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadLockedTargetAbnormalStatusesAsync(context.StopToken);
    }

    private static Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi)
        {
            return scopedApi.ReadSummonedPetRosterAsync(CreateReadContext(context), context.StopToken);
        }

        return context.GameApi.ReadSummonedPetRosterAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadOpeningSkillAsync(
        AccountWorkerContext context,
        SemiAutoSkillNode openingSkill)
    {
        if (context.GameApi is IRoadhogScopedGameApi scopedApi && openingSkill.SkillId != 0)
        {
            return scopedApi.ReadSkillsAsync(
                CreateReadContext(context),
                new[] { openingSkill.SkillId },
                context.StopToken);
        }

        return ReadAllSkillsAsync(context);
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

    private static TimeSpan ResolveCooldownConfirmationWindow(
        SemiAutoScriptSettings settings,
        bool useSpiritmasterMinimum)
    {
        var window = Ms(settings.ConfirmTimeoutMs, 1500);
        if (!useSpiritmasterMinimum)
        {
            return window;
        }

        window = Max(window, Ms(settings.PostPressSuppressMs, 650));
        return Max(window, TimeSpan.FromMilliseconds(1500));
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second)
    {
        return first >= second ? first : second;
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
