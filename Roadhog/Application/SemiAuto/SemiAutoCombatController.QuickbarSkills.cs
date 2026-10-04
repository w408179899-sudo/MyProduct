using Roadhog.Application.JumpAssist;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private QuickbarSkillCombatController? quickbarSkillController;
    internal static readonly TimeSpan QuickbarCombatBurstBudget = TimeSpan.FromMilliseconds(320);

    // The legacy TickAsync body remains unchanged. This mode owns its attack
    // state and calls the existing maintenance/opening boundaries serially;
    // legacy trigger prefixes, retries and inferred chains never run here.
    // Optional weaving consumes this mode's own confirmed releases only.
    private async Task<TimeSpan> TickQuickbarSkillsAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan maintenancePlan,
        SemiAutoCombatState state,
        bool requireCooldownCalibrationForMaintenance,
        CombatJumpAssistSession? jumpAssist,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress,
        bool suppressSpiritmasterPetSummon)
    {
        var script = context.Config.ScriptSettings!;
        var settings = script.SemiAuto ?? new SemiAutoScriptSettings();
        if (!settings.AttackWeaveEnabled) state.QuickbarSkills.AttackWeave.Reset();
        var sharedSkillSettings = script.Skills ?? new SkillScriptSettings();
        var tick = Ms(settings.TickIntervalMs, 40);
        var includeAlwaysStatusMaintenance = !ShouldSuppressAlwaysSupportStatusMaintenanceDuringCustomCombat(context);
        var maintenance = script.Maintenance ?? new MaintenanceScriptSettings();
        var hasMaintenance = HasMaintenanceWork(maintenance, false, MaintenanceRuleRunTiming.Always, true) ||
            HasMaintenanceWork(maintenance, false, MaintenanceRuleRunTiming.InCombat, maintenancePlan.UsesSpiritmasterAutoLogic);
        var maintenancePlayer = hasMaintenance ? await ReadPlayerAsync(context).ConfigureAwait(false) : null;
        if (maintenancePlayer?.IsDead == true)
        {
            state.QuickbarSkills.Reset();
            return tick;
        }
        if (!maintenancePlan.UsesSpiritmasterAutoLogic &&
            await RunWithJumpPauseAsync(jumpAssist, "semi_auto_maintenance",
                () => TryHandleQuickbarMaintenanceAsync(context, state, settings, maintenance, maintenancePlayer,
                    plan: maintenancePlan, requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                    includeStatusMaintenance: includeAlwaysStatusMaintenance)).ConfigureAwait(false))
        {
            state.QuickbarSkills.SuspendInputAttempts();
            return tick;
        }

        if (context.SkillBindings is null)
            await context.PrepareSkillBindingsAsync().ConfigureAwait(false);
        var plan = state.QuickbarPlan ??= QuickbarSkillPlan.FromSettings(script.QuickbarSkills ?? new(),
            context.SkillBindings!, context.ReportMissingSkillBinding);
        if (!plan.HasCombatActions)
        {
            if (!settings.AttackWeaveEnabled) state.QuickbarSkills.Reset();
            if (ShouldLog(state.LastPlanWarningAt, DateTimeOffset.Now))
            {
                state.LastPlanWarningAt = DateTimeOffset.Now;
                context.Logger.Warn("quickbar_skill.plan.empty", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName });
            }
        }

        var target = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        var killed = state.ObserveTarget(target, out var killedId, out var targetChanged);
        if (targetChanged)
        {
            state.QuickbarSkills.Reset();
            state.CancelSpiritmasterDotObservation();
        }
        if (killed)
            context.RuntimeStates.MarkKill(context.Config.AccountName, killedId, target.ServerObjectId, target.CapturedAt);
        if (!target.IsMonsterAlive)
        {
            state.QuickbarSkills.Reset();
            state.CancelSpiritmasterDotObservation();
            state.ResetOpeningAttackKey();
            state.ResetSpiritmasterOpeningAttackKey();
            state.ResetOpeningSkill();
            state.ResetAttackKeyPressThrottle();
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            return Ms(settings.TargetIdleDelayMs, 200);
        }

        // The old attack tree is not an input to this mode. Only shared opening
        // and pet actions retain their old skill-read requirements.
        var sharedIds = maintenancePlan.UsesSpiritmasterAutoLogic
            ? maintenancePlan.SkillReadIds
            : maintenancePlan.OpeningSkills.Select(node => node.SkillId).Where(id => id != 0).ToArray();
        var exactIds = plan.SkillReadIds.Concat(sharedIds).Distinct().ToArray();
        var needsFullRead = maintenancePlan.OpeningSkills.Any(node => node.SkillId == 0) ||
            (maintenancePlan.UsesSpiritmasterAutoLogic && maintenancePlan.RequiresFullSkillRead);
        var skills = needsFullRead
            ? (await context.Snapshots.ReadSkillsAsync().ConfigureAwait(false)).Value
            : exactIds.Length > 0
                ? await ReadQuickbarSkillsAndCalibrateAsync(context, state, exactIds).ConfigureAwait(false)
                : Array.Empty<SkillSnapshot>();
        if (needsFullRead)
        {
            var missingIds = exactIds.Where(id => !skills.Any(skill => skill.SkillId == id)).ToArray();
            if (missingIds.Length > 0)
                skills = MergeSkillSnapshots(skills,
                    await ReadQuickbarSkillsAndCalibrateAsync(context, state, missingIds).ConfigureAwait(false));
        }
        // Ordinary skills and the unchanged maintenance rules share the existing
        // cooldown clock. Conditional skills still require the official bar signal.
        UpdateCooldownCalibration(context, state, skills, CurrentOsTick(), DateTimeOffset.Now);
        if (settings.AttackWeaveEnabled &&
            !await ObserveQuickbarOpeningWeaveAsync(context, state, settings, target, skills).ConfigureAwait(false))
            return tick;

        SpiritmasterCombatContext? spiritContext = null;
        if (!state.QuickbarSkills.AttackWeave.IsWaiting)
        {
            if (maintenancePlan.UsesSpiritmasterAutoLogic &&
                await PressSpiritmasterOpeningAttackKeyIfNeededAsync(context, state, settings,
                    sharedSkillSettings.Spiritmaster, target).ConfigureAwait(false))
            {
                state.QuickbarSkills.SuspendInputAttempts(preserveAttackWeave: settings.AttackWeaveEnabled);
                return tick;
            }
            if (await PressQuickbarOpeningSkillIfNeededAsync(context, state, settings, maintenancePlan, target, skills).ConfigureAwait(false))
            {
                state.QuickbarSkills.SuspendInputAttempts(preserveAttackWeave: settings.AttackWeaveEnabled);
                return tick;
            }
            if (await PressOpeningAttackKeyIfNeededAsync(context, state, settings, target).ConfigureAwait(false))
            {
                state.QuickbarSkills.SuspendInputAttempts(preserveAttackWeave: settings.AttackWeaveEnabled);
                return tick;
            }

            if (maintenancePlan.UsesSpiritmasterAutoLogic)
            {
                spiritContext = await ReadSpiritmasterCombatContextAsync(context, target).ConfigureAwait(false);
                if (spiritContext.CanUseSpiritmasterLogic &&
                    await TryHandleSpiritmasterSpecialAsync(context, state, settings, sharedSkillSettings.Spiritmaster,
                        skills, spiritContext, suppressSpiritmasterPetSummon).ConfigureAwait(false))
                {
                    state.QuickbarSkills.SuspendInputAttempts();
                    return tick;
                }
            }
        }

        if (await RunWithJumpPauseAsync(jumpAssist, "semi_auto_in_combat_maintenance",
                () => TryHandleQuickbarMaintenanceAsync(context, state, settings, maintenance, maintenancePlayer,
                    plan: maintenancePlan, requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                    runTiming: MaintenanceRuleRunTiming.InCombat,
                    includeAlwaysRules: maintenancePlan.UsesSpiritmasterAutoLogic,
                    ensureHpMaintenanceTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress)).ConfigureAwait(false))
        {
            state.QuickbarSkills.SuspendInputAttempts();
            return tick;
        }

        if (!plan.HasCombatActions && !state.QuickbarSkills.AttackWeave.IsWaiting)
            return Ms(settings.TargetIdleDelayMs, 200);

        jumpAssist?.ActivatePreparedTeamCombatJump(target.ServerObjectId);
        quickbarSkillController ??= new QuickbarSkillCombatController(_keyboard, _timeProvider);
        if (context.Snapshots is not ISkillAvailabilitySnapshotReader availabilityReader)
            throw new InvalidOperationException("技能栏可用模式需要技能可用状态读取接口。");

        // A bounded, serial fast segment avoids rerunning world/full-table and
        // maintenance work every 80ms. Configured DOT roots separately recheck
        // target statuses. It yields regularly to the existing worker lifecycle;
        // every poll and press still obtains an official local life/target guard.
        // Candidates already checked by maintenance may be cooling or otherwise
        // unavailable. Only newly due resource rules interrupt this segment, so
        // a cooling heal cannot starve all attacks while HP remains below its threshold.
        var checkedMaintenanceCandidates = maintenancePlayer is null ? new HashSet<string>(StringComparer.Ordinal) :
            DueQuickbarMaintenanceKeys(maintenance, state, maintenancePlayer.MaxHp, maintenancePlayer.HpPercent,
                maintenancePlayer.MaxMp, maintenancePlayer.MpPercent, maintenancePlayer.CurrentDp);
        var burstStart = _timeProvider.GetTimestamp();
        QuickbarSpiritmasterDotPolicy? dotPolicy = null;
        if (maintenancePlan.UsesSpiritmasterAutoLogic && sharedSkillSettings.Spiritmaster.DotSkills.Count > 0)
        {
            spiritContext ??= await ReadSpiritmasterCombatContextAsync(context, target).ConfigureAwait(false);
            if (spiritContext.CanUseSpiritmasterLogic)
                dotPolicy = new QuickbarSpiritmasterDotPolicy(state, plan, skills, sharedSkillSettings.Spiritmaster,
                    target, () => ReadLockedTargetAbnormalStatusesAsync(context), _timeProvider, context.Logger,
                    context.Config.AccountName);
        }
        Func<Task<IReadOnlySet<uint>>>? readSuppressedRootSkillIds = dotPolicy is { HasDotRoots: true }
            ? dotPolicy.ReadSuppressedRootSkillIdsAsync : null;
        Func<QuickbarSkillNode, Task>? onSkillPressed = readSuppressedRootSkillIds is not null
            ? dotPolicy!.OnSkillPressedAsync : null;
        while (true)
        {
            context.StopToken.ThrowIfCancellationRequested();
            var delay = await quickbarSkillController.TickAsync(plan, state.QuickbarSkills, target,
                availabilityReader,
                ids => ReadQuickbarSkillsAndCalibrateAsync(context, state, ids),
                settings, context.Logger, context.StopToken,
                readTargetBeforePress: () => ReadLockedTargetAsync(context),
                ordinaryReadiness: ordinarySkills => ordinarySkills.Select(skill => skill.SkillId).ToHashSet(),
                allowCombatSnapshot: combat => !DueQuickbarMaintenanceKeys(maintenance, state, combat.MaxHp,
                    combat.HpPercent, combat.MaxMp, combat.MpPercent, combat.CurrentDp)
                    .Any(key => !checkedMaintenanceCandidates.Contains(key)),
                availabilityCooldownReadiness: (skill, bar) => GetQuickbarCooldownReadiness(skill, state, bar, _timeProvider),
                isCooldownClockCalibrated: () => state.HasCooldownTickCalibration,
                readSuppressedRootSkillIds: readSuppressedRootSkillIds,
                onSkillPressed: onSkillPressed)
                .ConfigureAwait(false);
            if (state.QuickbarSkills.YieldToWorker || !state.QuickbarSkills.SupportsCombatState)
                return delay;
            var remaining = QuickbarCombatBurstBudget - _timeProvider.GetElapsedTime(burstStart);
            if (remaining <= delay || remaining <= TimeSpan.Zero)
                return delay;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, context.StopToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<SkillSnapshot>> ReadQuickbarSkillsAndCalibrateAsync(
        AccountWorkerContext context, SemiAutoCombatState state, IReadOnlyCollection<uint> ids)
    {
        var skills = (await context.Snapshots.ReadSkillsAsync(ids).ConfigureAwait(false)).Value;
        UpdateCooldownCalibration(context, state, skills, CurrentOsTick(), DateTimeOffset.Now);
        return skills;
    }

    internal static SemiAutoSkillCooldownReadiness GetQuickbarCooldownReadiness(
        SkillSnapshot skill, SemiAutoCombatState state, SkillAvailabilitySnapshot availability,
        TimeProvider? timeProvider = null)
    {
        if (state.HasCooldownTickCalibration)
            return SemiAutoSkillReleasePriority.GetCooldownReadiness(skill, state);
        if (skill.CooldownEndTime == 0 || skill.CooldownDuration == 0)
            return SemiAutoSkillCooldownReadiness.Ready;
        // Actual release time and CD end use the same client clock. A CD that
        // ended before an observed actual release is certainly over, even on
        // startup when the local OS clock differs from the game device's clock.
        // Advance this conservative bound with monotonic elapsed time using the
        // existing running-client clock-rate assumption. A single cooling skill
        // can therefore become ready without requiring an unrelated manual cast.
        // A real CD advance then supplies the usual complete clock calibration.
        var lowerBound = state.QuickbarSkills.ObserveReleaseClockLowerBound(
            availability.LastReleasedSkillTime, timeProvider ?? TimeProvider.System);
        return lowerBound.HasValue &&
               unchecked((int)(lowerBound.Value - skill.CooldownEndTime)) >= 0
            ? SemiAutoSkillCooldownReadiness.Ready
            : SemiAutoSkillCooldownReadiness.Unknown;
    }

    private Task<bool> TryHandleQuickbarMaintenanceAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SemiAutoScriptSettings settings, MaintenanceScriptSettings maintenance,
        PlayerSnapshot? player, SemiAutoSkillPlan plan, bool requireCooldownCalibrationForMaintenance,
        MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
        bool includeAlwaysRules = true, bool includeStatusMaintenance = true,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress = null)
    {
        if (player is null)
        {
            state.ClearMaintenanceRest();
            return Task.FromResult(false);
        }
        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        return TryHandleMaintenanceAsync(context, state, settings, maintenance, player,
            allowSitMaintenance: false, clearSitWhenDisallowed: true, plan: plan,
            requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
            runTiming: runTiming, includeAlwaysRules: includeAlwaysRules,
            includeStatusMaintenance: includeStatusMaintenance,
            ensureHpMaintenanceTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress);
    }

    private static HashSet<string> DueQuickbarMaintenanceKeys(MaintenanceScriptSettings maintenance,
        SemiAutoCombatState state, uint maxHp, double hpPercent, uint maxMp, double mpPercent, ushort currentDp)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTimeOffset.Now;
        bool Due(string key) => !string.IsNullOrWhiteSpace(key) &&
            state.ShouldPressMaintenanceKey(key, now, MaintenanceKeyRetryInterval, MaintenanceGlobalKeyInterval);
        bool RunsInCombat(MaintenanceRuleRunTiming timing) =>
            timing is MaintenanceRuleRunTiming.Always or MaintenanceRuleRunTiming.InCombat;
        foreach (var rule in maintenance.HpMaintenanceRules ?? Enumerable.Empty<MaintenanceKeyRuleConfig>())
            if (rule.ActionType == MaintenanceRuleActionType.Skill && RunsInCombat(rule.RunTiming) && Due(rule.Key) &&
                maxHp > 0 && hpPercent <= Math.Clamp(rule.BelowPercent, 0, 100)) candidates.Add(rule.Key);
        foreach (var rule in maintenance.MpMaintenanceRules ?? Enumerable.Empty<MaintenanceKeyRuleConfig>())
            if (RunsInCombat(rule.RunTiming) && Due(rule.Key) &&
                maxMp > 0 && mpPercent <= Math.Clamp(rule.BelowPercent, 0, 100)) candidates.Add(rule.Key);
        foreach (var rule in maintenance.DpMaintenanceRules ?? Enumerable.Empty<DpMaintenanceRuleConfig>())
            if (RunsInCombat(rule.RunTiming) && Due(rule.Key) &&
                currentDp >= NormalizeRequiredDp(rule.RequiredDp)) candidates.Add(rule.Key);
        return candidates;
    }
}
