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
    private enum SitMaintenanceContinuation
    {
        BlockTick,
        AllowMaintenanceRules
    }

    private readonly record struct StatusMaintenanceAbnormalMatch(uint AbnormalId, string Source)
    {
        public bool Found => AbnormalId != 0;

        public static StatusMaintenanceAbnormalMatch None { get; } = new(0, string.Empty);
    }

    private sealed record SpiritmasterElementalReplenishmentSafetySnapshot(
        PlayerSnapshot Player,
        SummonedPetSnapshot Pet);

    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaintenanceConfirmWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan NormalStatusMaintenanceConfirmWindow = TimeSpan.FromMilliseconds(1300);
    private static readonly TimeSpan ChantStatusMaintenanceConfirmWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ChantStatusMaintenanceMissingMinimumDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StatusMaintenancePressBurstInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaintenanceConfirmPollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MaintenanceKeyRetryInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan MaintenanceGlobalKeyInterval = TimeSpan.FromMilliseconds(600);
    private const int StatusMaintenancePressBurstCount = 3;
    private const int ChantStatusMaintenanceMissingRequiredCount = 3;
    private static readonly TimeSpan MaintenanceRestExitBeforeKeyDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SupportSelfSelectConfirmDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan SpiritmasterSummonKeyInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SpiritmasterSummonAttemptInterval = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SpiritmasterSummonVerifyWindow = TimeSpan.FromSeconds(5);
    // client_skills.xml: EL_Pet_RecoverHealth_G1..G4 share the HP-cost family.
    private static readonly IReadOnlySet<uint> SpiritmasterElementalReplenishmentSkillIds = new HashSet<uint> { 1676, 1677, 1678, 1705 };
    private const double SpiritmasterElementalReplenishmentMinPlayerHpPercent = 65.0D;
    private static readonly TimeSpan SpiritmasterElementalReplenishmentMinimumRetryInterval =
        TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SpiritmasterElementalReplenishmentMinimumConfirmationLifetime =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SpiritmasterOpeningAttackKeyInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan OpeningSkillConfirmationTimeout = TimeSpan.FromMilliseconds(2300);
    private static readonly string AttackKey = "C";
    private static readonly string SupportSelfSelectKey = "F1";
    private static readonly string RestEnterKey = "OemComma";
    private static readonly string RestExitKey = "X";
    private const string OpeningSkillConfirmationTimeoutEnvVar = "ROADHOG_OPENING_SKILL_CONFIRM_TIMEOUT_MS";

    private readonly IKeyboardInput _keyboard;
    private readonly TimeProvider _timeProvider;
    private AbnormalStatusCatalog? _abnormalStatusCatalog;

    public SemiAutoCombatController(
        IKeyboardInput keyboard,
        AbnormalStatusCatalog? abnormalStatusCatalog = null,
        TimeProvider? timeProvider = null)
    {
        _keyboard = keyboard;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _abnormalStatusCatalog = abnormalStatusCatalog;
    }

    private AbnormalStatusCatalog StatusCatalog =>
        _abnormalStatusCatalog ??= AbnormalStatusCatalog.Default;

    internal Task<bool> WaitForMaintenanceGlobalKeyIntervalAsync(
        SemiAutoCombatState state, CancellationToken cancellationToken) =>
        WaitForMaintenanceGlobalIntervalAsync(state, MaintenanceGlobalKeyInterval,
            static () => DateTimeOffset.Now,
            static (delay, token) => Task.Delay(delay, token), cancellationToken);

    internal static async Task<bool> WaitForMaintenanceGlobalIntervalAsync(
        SemiAutoCombatState state,
        TimeSpan interval,
        Func<DateTimeOffset> now,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(delay);
        cancellationToken.ThrowIfCancellationRequested();
        if (interval <= TimeSpan.Zero) return true;

        // Keep the existing full pause even if confirmation already consumed
        // part of the interval. Timer completion alone does not open the gate.
        await delay(interval, cancellationToken).ConfigureAwait(false);
        var supplementalBudget = interval;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = state.GetRemainingMaintenanceGlobalInterval(now(), interval);
            if (remaining <= TimeSpan.Zero) return true;
            if (remaining > supplementalBudget) return false;

            // A sub-millisecond delay can complete synchronously. Round upward
            // and bound the total extra wait so clock changes cannot own the worker.
            var supplementalDelay = TimeSpan.FromMilliseconds(Math.Max(1,
                Math.Ceiling(remaining.TotalMilliseconds)));
            if (supplementalDelay > supplementalBudget) return false;
            supplementalBudget -= supplementalDelay;
            await delay(supplementalDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TimeSpan> TickAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        bool requireCooldownCalibrationForMaintenance = false,
        CombatJumpAssistSession? jumpAssist = null,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress = null,
        bool suppressSpiritmasterPetSummon = false)
    {
        NormalizeCombatSettings(context);
        plan = plan.ToSharedPlan(context.Config.ScriptSettings!.Skills);
        return await TickQuickbarSkillsAsync(context, plan, state, requireCooldownCalibrationForMaintenance,
            jumpAssist, ensureHpMaintenanceTargetBeforeKeyPress, suppressSpiritmasterPetSummon).ConfigureAwait(false);
    }

    private static void NormalizeCombatSettings(AccountWorkerContext context)
    {
        var settings = context.Config.ScriptSettings;
        if (settings?.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability) return;
        var normalized = settings?.Clone() ?? new ScriptSettings();
        SkillTreeReleaseMigration.Migrate(normalized);
        context.Config.ScriptSettings = normalized;
    }

    private static async Task<bool> RunWithJumpPauseAsync(
        CombatJumpAssistSession? jumpAssist,
        string reason,
        Func<Task<bool>> action)
    {
        jumpAssist?.Pause(reason);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (jumpAssist is not null)
            {
                await jumpAssist.WaitForTeamCooldownObservationAsync().ConfigureAwait(false);
            }

            jumpAssist?.Resume(reason);
        }
    }

    public async Task<bool> EnsureSpiritmasterPetAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan plan,
        SemiAutoCombatState state,
        Func<Task>? beforeSummonKeyPress = null)
    {
        plan = plan.ToSharedPlan(context.Config.ScriptSettings?.Skills ?? new SkillScriptSettings());
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
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead)
        {
            state.ClearSpiritmasterSummonVerification();
            return false;
        }

        if (player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.ClearSpiritmasterSummonVerification();
            return false;
        }

        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        var localPet = roster.LocalPlayerPet;
        var pet = localPet.Pet;
        if (pet is { IsSummoned: true, IsAlive: true })
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
        NormalizeCombatSettings(context);
        plan = plan.ToSharedPlan(context.Config.ScriptSettings!.Skills);
        var settings = context.Config.ScriptSettings?.SemiAuto ?? new SemiAutoScriptSettings();
        return await TickQuickbarOpeningAttackKeyLoopAsync(context, plan, state, target, settings).ConfigureAwait(false);
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
            var continuation = await ContinueSitMaintenanceAsync(
                    context,
                    state,
                    settings,
                    maintenance,
                    player,
                    hpRecoverToPercent)
                .ConfigureAwait(false);
            if (continuation == SitMaintenanceContinuation.BlockTick)
            {
                return true;
            }

            if (await TryPressMaintenanceRulesAsync(
                    context,
                    state,
                    settings,
                    maintenance,
                    player,
                    beforeMaintenanceKeyPress,
                    plan,
                    requireCooldownCalibrationForMaintenance,
                    MaintenanceRuleRunTiming.Always,
                    includeAlwaysRules: true,
                    allowPotionWhileResting: true)
                .ConfigureAwait(false))
            {
                return true;
            }

            return true;
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
        bool includeAlwaysRules = true,
        bool includeStatusMaintenance = true,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress = null)
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

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.RuntimeStates.ClearWarning(context.Config.AccountName);
        return await TryHandleMaintenanceAsync(
                context,
                state,
                settings,
                maintenance,
                player,
                allowSitMaintenance,
                clearSitWhenDisallowed,
                plan: plan,
                requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                runTiming: runTiming,
                includeAlwaysRules: includeAlwaysRules,
                includeStatusMaintenance: includeStatusMaintenance,
                ensureHpMaintenanceTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress)
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
        bool includeAlwaysRules = true,
        bool includeStatusMaintenance = true,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress = null)
    {
        if (player.HasKnownHealth && player.IsDead)
        {
            state.ClearMaintenanceRest();
            return false;
        }

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
            var continuation = await ContinueSitMaintenanceAsync(context, state, settings, maintenance, player).ConfigureAwait(false);
            if (continuation == SitMaintenanceContinuation.BlockTick)
            {
                return true;
            }

            if (await TryPressMaintenanceRulesAsync(
                    context,
                    state,
                    settings,
                    maintenance,
                    player,
                    beforeMaintenanceKeyPress,
                    plan,
                    requireCooldownCalibrationForMaintenance,
                    runTiming,
                    includeAlwaysRules,
                    allowPotionWhileResting: true,
                    includeStatusMaintenance: includeStatusMaintenance,
                    ensureHpMaintenanceTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress)
                .ConfigureAwait(false))
            {
                return true;
            }

            return true;
        }

        if (await TryPressMaintenanceRulesAsync(
                context,
                state,
                settings,
                maintenance,
                player,
                beforeMaintenanceKeyPress,
                plan,
                requireCooldownCalibrationForMaintenance,
                runTiming,
                includeAlwaysRules,
                allowPotionWhileResting: false,
                includeStatusMaintenance: includeStatusMaintenance,
                ensureHpMaintenanceTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (allowSitMaintenance &&
            await TryEnterSitMaintenanceAsync(
                    context,
                    state,
                    settings,
                    maintenance,
                    player,
                    beforeMaintenanceKeyPress)
                .ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> TryPressMaintenanceRulesAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceScriptSettings maintenance,
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress,
        SemiAutoSkillPlan? plan,
        bool requireCooldownCalibrationForMaintenance,
        MaintenanceRuleRunTiming runTiming,
        bool includeAlwaysRules,
        bool allowPotionWhileResting,
        bool includeStatusMaintenance = true,
        Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress = null)
    {
        if (includeStatusMaintenance &&
            await TryPressStatusMaintenanceRuleAsync(
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
                includeAlwaysRules,
                allowPotionWhileResting,
                ensureHpMaintenanceTargetBeforeKeyPress)
            .ConfigureAwait(false))
        {
            return true;
        }

        return await TryPressMaintenanceRuleAsync(
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
                includeAlwaysRules,
                allowPotionWhileResting)
            .ConfigureAwait(false);
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
        bool includeAlwaysRules = true,
        bool allowPotionWhileResting = false,
        Func<Task<bool>>? ensureTargetBeforeKeyPress = null)
    {
        if (max == 0)
        {
            return false;
        }

        var percent = Percent(current, max);
        IReadOnlyList<SkillSnapshot>? skills = null;
        IReadOnlyList<InventoryItemSnapshot>? inventory = null;
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
                    !state.ShouldPressMaintenanceKey(
                        rule.Key,
                        now,
                        MaintenanceKeyRetryInterval,
                        MaintenanceGlobalKeyInterval))
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
                            allowWhileMaintenanceResting: allowPotionWhileResting)
                        .ConfigureAwait(false);
                }

                inventory ??= await ReadInventoryAsync(context).ConfigureAwait(false);
                if (!inventory.Any(IsSpiritMpPotion))
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
                        beforeMaintenanceKeyPress,
                        allowWhileMaintenanceResting: allowPotionWhileResting)
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
                skills ??= await ReadMaintenanceSkillsAsync(context,
                    (rules ?? Array.Empty<MaintenanceKeyRuleConfig>()).Where(item => item.ActionType == MaintenanceRuleActionType.Skill)
                        .Select(item => (item.SkillId, item.SkillName))).ConfigureAwait(false);
                maintenanceSkill = ResolveMaintenanceRuleSkill(rule, plan, skills);
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

            now = DateTimeOffset.Now;
            if (!state.ShouldPressMaintenanceKey(
                    rule.Key,
                    now,
                    MaintenanceKeyRetryInterval,
                    MaintenanceGlobalKeyInterval))
            {
                continue;
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
                    maintenanceSkill,
                    rule.RunTiming == MaintenanceRuleRunTiming.InCombat
                        ? ensureTargetBeforeKeyPress
                        : null)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> TryEnterSitMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        MaintenanceScriptSettings maintenance,
        PlayerSnapshot player,
        Func<Task>? beforeMaintenanceKeyPress = null)
    {
        if (!maintenance.SitMaintenanceEnabled)
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

        if (beforeMaintenanceKeyPress is not null)
        {
            await beforeMaintenanceKeyPress().ConfigureAwait(false);
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

    private async Task<bool> ShouldWaitForHarmfulAbnormalBeforeSitAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        string phase)
    {
        var abnormal = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        var harmfulEntries = abnormal.Entries
            .Where(StatusCatalog.IsHarmfulForRest)
            .ToArray();
        if (harmfulEntries.Length == 0)
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
                ["harmfulAbnormalCount"] = harmfulEntries.Length,
                ["harmfulAbnormalSummary"] = FormatAbnormalStatusSummary(harmfulEntries),
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["mp"] = player.CurrentMp,
                ["maxMp"] = player.MaxMp
            });
        }

        return true;
    }

    private static string FormatAbnormalStatusSummary(
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries)
    {
        var samples = entries
            .Take(8)
            .Select(entry => entry.AbnormalId.ToString() + ":" + entry.Category.ToString())
            .ToList();
        if (entries.Count > samples.Count)
        {
            samples.Add("+" + (entries.Count - samples.Count).ToString());
        }

        return string.Join(",", samples);
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

    private async Task<SitMaintenanceContinuation> ContinueSitMaintenanceAsync(
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
                if (!state.ShouldPressMaintenanceKey(
                        RestEnterKey,
                        now,
                        MaintenanceKeyRetryInterval,
                        MaintenanceGlobalKeyInterval))
                {
                    return SitMaintenanceContinuation.BlockTick;
                }

                var reenterResult = await _keyboard
                    .PressKeyAsync(RestEnterKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
                    .ConfigureAwait(false);
                if (!reenterResult.Success)
                {
                    LogMaintenanceKeyFailure(context, state, RestEnterKey, "rest_reenter", reenterResult.Error);
                    return SitMaintenanceContinuation.BlockTick;
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
                return SitMaintenanceContinuation.BlockTick;
            }

            return SitMaintenanceContinuation.AllowMaintenanceRules;
        }

        var result = await _keyboard
            .PressKeyAsync(RestExitKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogMaintenanceKeyFailure(context, state, RestExitKey, "rest_exit", result.Error);
            return SitMaintenanceContinuation.BlockTick;
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
        return SitMaintenanceContinuation.BlockTick;
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

        var abnormal = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        IReadOnlyList<SkillSnapshot>? skills = null;
        foreach (var rule in configuredRules)
        {
            var configuredSkillId = rule.SkillId;
            var now = DateTimeOffset.Now;
            var configuredActiveStatus = ResolveKnownStatusMaintenanceAbnormalId(
                    rule,
                    configuredSkillId,
                    state,
                    abnormal.Entries);
            if (configuredActiveStatus.Found)
            {
                if (IsChantStatusMaintenanceRule(rule, null, configuredSkillId))
                {
                    state.MarkChantStatusMaintenanceObserved(configuredSkillId, now);
                }

                continue;
            }

            SkillSnapshot? maintenanceSkill = null;
            if (HasExplicitStatusMaintenanceSkill(rule))
            {
                skills ??= await ReadMaintenanceSkillsAsync(context,
                    configuredRules.Select(item => (item.SkillId, item.SkillName))).ConfigureAwait(false);
                maintenanceSkill = ResolveStatusMaintenanceRuleSkill(rule, skills);
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

            var skillId = ResolveStatusMaintenanceSkillId(rule, maintenanceSkill);
            var isChantStatusMaintenance = IsChantStatusMaintenanceRule(rule, maintenanceSkill, skillId);
            var activeStatus = skillId != configuredSkillId
                ? ResolveKnownStatusMaintenanceAbnormalId(
                        rule,
                        skillId,
                        state,
                        abnormal.Entries)
                : configuredActiveStatus;
            if (activeStatus.Found)
            {
                if (isChantStatusMaintenance)
                {
                    state.MarkChantStatusMaintenanceObserved(skillId, now);
                }

                continue;
            }

            if (isChantStatusMaintenance &&
                state.ShouldDeferChantStatusMaintenanceMissing(
                    skillId,
                    now,
                    ChantStatusMaintenanceMissingRequiredCount,
                    ChantStatusMaintenanceMissingMinimumDuration,
                    out _,
                    out _))
            {
                continue;
            }

            if (!state.ShouldPressMaintenanceKey(
                    rule.Key,
                    now,
                    MaintenanceKeyRetryInterval,
                    MaintenanceGlobalKeyInterval))
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
                    abnormal,
                    beforeMaintenanceKeyPress,
                    maintenanceSkill,
                    isChantStatusMaintenance)
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

        IReadOnlyList<SkillSnapshot>? skills = null;
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
                skills ??= await ReadMaintenanceSkillsAsync(context,
                    configuredRules.Select(item => (item.SkillId, item.SkillName))).ConfigureAwait(false);
                maintenanceSkill = ResolveDpMaintenanceRuleSkill(rule, skills);
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
            var now = DateTimeOffset.Now;
            if (!state.ShouldPressMaintenanceKey(
                    rule.Key,
                    now,
                    MaintenanceKeyRetryInterval,
                    MaintenanceGlobalKeyInterval))
            {
                continue;
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
        bool isChantStatusMaintenance)
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

        if (!await EnsureSupportSelfSelectedBeforeStatusMaintenanceAsync(
                    context,
                    state,
                    settings,
                    rule,
                    player)
                .ConfigureAwait(false))
        {
            return false;
        }

        var beforeIds = beforeStatuses.Entries
            .Select(entry => entry.AbnormalId)
            .Where(id => id != 0)
            .ToHashSet();
        var startedAt = DateTimeOffset.Now;
        if (!await PressStatusMaintenanceKeyBurstAsync(context, state, settings, rule, resource).ConfigureAwait(false))
        {
            return false;
        }

        state.MarkMaintenanceKeyAttempted(rule.Key, startedAt);
        var skillId = ResolveStatusMaintenanceSkillId(rule, maintenanceSkill);
        var skillName = maintenanceSkill?.Name ?? maintenanceSkill?.DisplayBaseName ?? rule.SkillName;

        var confirmWindow = ResolveStatusMaintenanceConfirmWindow(isChantStatusMaintenance);
        var deadline = DateTimeOffset.Now + confirmWindow;
        var polls = 0;
        var rejectedCandidateAbnormalIds = new HashSet<uint>();

        while (DateTimeOffset.Now <= deadline)
        {
            polls++;
            var abnormal = await ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
            var confirmedAbnormal = ResolveConfirmedStatusMaintenanceAbnormalId(
                rule,
                skillId,
                state,
                beforeIds,
                abnormal.Entries,
                rejectedCandidateAbnormalIds);
            if (confirmedAbnormal.Found)
            {
                var completedAt = DateTimeOffset.Now;
                if (skillId != 0)
                {
                    state.RememberStatusMaintenanceAbnormalId(skillId, confirmedAbnormal.AbnormalId);
                    if (isChantStatusMaintenance)
                    {
                        state.MarkChantStatusMaintenanceObserved(skillId, completedAt);
                    }
                }

                context.Logger.Info("semi_auto.maintenance.status_key_pressed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["resource"] = resource,
                    ["key"] = rule.Key,
                    ["skillId"] = skillId,
                    ["skillName"] = skillName,
                    ["abnormalStatusId"] = confirmedAbnormal.AbnormalId,
                    ["abnormalStatusSource"] = confirmedAbnormal.Source,
                    ["oneShot"] = isChantStatusMaintenance,
                    ["chant"] = isChantStatusMaintenance,
                    ["pressCount"] = StatusMaintenancePressBurstCount,
                    ["pressIntervalMs"] = (long)StatusMaintenancePressBurstInterval.TotalMilliseconds,
                    ["polls"] = polls,
                    ["confirmWindowMs"] = (long)confirmWindow.TotalMilliseconds,
                    ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAt - startedAt).TotalMilliseconds)
                });
                return true;
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
            ["oneShot"] = isChantStatusMaintenance,
            ["chant"] = isChantStatusMaintenance,
            ["pressCount"] = StatusMaintenancePressBurstCount,
            ["pressIntervalMs"] = (long)StatusMaintenancePressBurstInterval.TotalMilliseconds,
            ["rejectedCandidateAbnormalIds"] = FormatAbnormalIdList(rejectedCandidateAbnormalIds),
            ["rejectedCandidateCount"] = rejectedCandidateAbnormalIds.Count,
            ["polls"] = polls,
            ["confirmWindowMs"] = (long)confirmWindow.TotalMilliseconds,
            ["confirmElapsedMs"] = (long)Math.Max(0.0D, (completedAtUnconfirmed - startedAt).TotalMilliseconds)
        });
        return true;
    }

    private async Task<bool> PressStatusMaintenanceKeyBurstAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        StatusMaintenanceRuleConfig rule,
        string resource)
    {
        for (var pressIndex = 0; pressIndex < StatusMaintenancePressBurstCount; pressIndex++)
        {
            var result = await _keyboard
                .PressKeyAsync(rule.Key, Ms(settings.KeyHoldMs, 25), context.StopToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                LogMaintenanceKeyFailure(context, state, rule.Key, resource, result.Error);
                return false;
            }

            if (pressIndex < StatusMaintenancePressBurstCount - 1)
            {
                await Task.Delay(StatusMaintenancePressBurstInterval, context.StopToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    private async Task<bool> EnsureSupportSelfSelectedBeforeStatusMaintenanceAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        StatusMaintenanceRuleConfig rule,
        PlayerSnapshot player)
    {
        if (!ShouldSelectSelfBeforeStatusMaintenance(context))
        {
            return true;
        }

        var current = await ReadLockedTargetForConfirmationAsync(context).ConfigureAwait(false);
        if (IsSelectedLocalPlayer(current, player))
        {
            LogStatusMaintenanceSelfTargetSelected(context, rule, current, alreadySelected: true);
            return true;
        }

        var pressResult = await _keyboard
            .PressKeyAsync(SupportSelfSelectKey, Ms(settings.KeyHoldMs, 25), context.StopToken)
            .ConfigureAwait(false);
        if (!pressResult.Success)
        {
            LogStatusMaintenanceSelfTargetSelectionFailed(
                context,
                state,
                rule,
                current,
                "press_failed",
                pressResult.Error);
            return false;
        }

        await Task.Delay(SupportSelfSelectConfirmDelay, context.StopToken).ConfigureAwait(false);
        var confirmed = await ReadLockedTargetForConfirmationAsync(context).ConfigureAwait(false);
        if (!IsSelectedLocalPlayer(confirmed, player))
        {
            LogStatusMaintenanceSelfTargetSelectionFailed(
                context,
                state,
                rule,
                confirmed,
                "target_mismatch",
                null);
            return false;
        }

        LogStatusMaintenanceSelfTargetSelected(context, rule, confirmed, alreadySelected: false);
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
        TimeSpan? pressInterval = null,
        bool allowWhileMaintenanceResting = false)
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
                    rule.Key,
                    allowWhileMaintenanceResting)
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

    private async Task<bool> EnsureStandingBeforeMaintenanceKeyAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        PlayerSnapshot player,
        string resource,
        string maintenanceKey,
        bool allowWhileMaintenanceResting = false)
    {
        if (allowWhileMaintenanceResting &&
            state.IsMaintenanceResting &&
            player.IsResting)
        {
            return true;
        }

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

        var keepMaintenanceRestLock = state.IsMaintenanceResting;
        if (!keepMaintenanceRestLock)
        {
            state.ClearMaintenanceRest();
        }

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

    private async Task<SpiritmasterCombatContext> ReadSpiritmasterCombatContextAsync(
        AccountWorkerContext context,
        LockedTargetSnapshot target)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);

        if (player?.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            return new SpiritmasterCombatContext(player, null, null);
        }

        var petRoster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        var lockedTargetAbnormalStatuses = await ReadLockedTargetAbnormalStatusesAsync(context).ConfigureAwait(false);

        return new SpiritmasterCombatContext(player, petRoster, lockedTargetAbnormalStatuses);
    }

    private async Task<bool> TryHandleSpiritmasterSpecialAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterCombatContext spiritContext,
        bool suppressPetSummon)
    {
        if (!spiritContext.CanUseSpiritmasterLogic || spiritContext.Player?.IsDead == true)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            state.ClearSpiritmasterPetBuffAttempts();
            return false;
        }

        if (spiritContext.PetRoster is null)
        {
            return false;
        }

        if (!spiritContext.HasSummonedPet)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            state.ClearSpiritmasterPetBuffAttempts();
            if (suppressPetSummon)
            {
                return false;
            }

            return await TryPressSpiritmasterSummonAsync(context, state, settings, spiritSettings).ConfigureAwait(false);
        }

        var confirmedLocalPet = spiritContext.LocalPet!;
        var pet = confirmedLocalPet.Pet;
        if (await TryContinueSpiritmasterElementalReplenishmentConfirmationAsync(
                context,
                state,
                settings,
                skills,
                pet)
            .ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPressSpiritmasterPetHpRuleAsync(
                context,
                state,
                settings,
                spiritSettings,
                skills,
                pet,
                spiritContext.Player)
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
                confirmedLocalPet,
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
            .Take(2)
            .ToArray();
        if (keys.Length == 0 || keys.All(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (!state.ShouldAttemptSpiritmasterSummon(now, SpiritmasterSummonAttemptInterval))
        {
            return true;
        }

        state.MarkSpiritmasterSummonAttempted(now);
        if (!string.IsNullOrWhiteSpace(keys[0]) && !await PressSpiritmasterRawKeyAsync(context, settings, keys[0], "summon_speed").ConfigureAwait(false))
        {
            state.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, SpiritmasterSummonVerifyWindow);
            return true;
        }

        if (keys.Length > 1 && !string.IsNullOrWhiteSpace(keys[1]))
        {
            if (!string.IsNullOrWhiteSpace(keys[0])) await Task.Delay(SpiritmasterSummonKeyInterval, context.StopToken).ConfigureAwait(false);
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
        if (await PressSpiritmasterRawKeyAsync(context, settings, key, "opening_attack").ConfigureAwait(false) &&
            spiritSettings.OpeningAttackDelayMs > 0)
        {
            context.Logger.Info("semi_auto.spiritmaster.opening_attack_key.delay", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["delayMs"] = spiritSettings.OpeningAttackDelayMs,
                ["targetServerObjectId"] = target.ServerObjectId
            });
            await Task.Delay(spiritSettings.OpeningAttackDelayMs, context.StopToken).ConfigureAwait(false);
        }
        return true;
    }

    private async Task<bool> TryPressSpiritmasterPetHpRuleAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings,
        IReadOnlyList<SkillSnapshot> skills,
        SummonedPetSnapshot pet,
        PlayerSnapshot? player)
    {
        foreach (var rule in spiritSettings.PetHpMaintenanceRules
                     .Where(rule => !string.IsNullOrWhiteSpace(rule.Key))
                     .OrderBy(rule => Math.Clamp(rule.BelowPercent, 0, 100)))
        {
            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            if (skill is null)
            {
                continue;
            }

            var isElementalReplenishment = IsSpiritmasterElementalReplenishment(skill);
            if (isElementalReplenishment &&
                state.PendingSpiritmasterPetHpIncreaseConfirmation is not null)
            {
                continue;
            }

            var petHpPercent = pet.HpPercent;

            if (petHpPercent > Math.Clamp(rule.BelowPercent, 0, 100))
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (!state.ShouldPressSpiritmasterPetHpSkill(skill.SkillId, now))
            {
                continue;
            }

            if (isElementalReplenishment)
            {
                if (GetMaintenanceCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready)
                {
                    continue;
                }

                if (player is not null &&
                    player.HpPercent < SpiritmasterElementalReplenishmentMinPlayerHpPercent)
                {
                    LogSpiritmasterElementalReplenishmentPlayerHpBlocked(context, state, player, "initial", skill.SkillId);
                    continue;
                }

                var safety = await ReadSpiritmasterElementalReplenishmentSafetyAsync(context)
                    .ConfigureAwait(false);
                if (safety.Pet.ServerObjectId != pet.ServerObjectId)
                {
                    continue;
                }

                if (safety.Player.HpPercent < SpiritmasterElementalReplenishmentMinPlayerHpPercent)
                {
                    LogSpiritmasterElementalReplenishmentPlayerHpBlocked(
                        context,
                        state,
                        safety.Player,
                        "initial_snapshot", skill.SkillId);
                    continue;
                }

                player = safety.Player;
                pet = safety.Pet;
                petHpPercent = pet.HpPercent;
                if (petHpPercent > Math.Clamp(rule.BelowPercent, 0, 100))
                {
                    continue;
                }
            }
            else if (ShouldSkipSpiritmasterSpecialSkillForCooldown(skill, state, now))
            {
                continue;
            }

            if (!await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_hp").ConfigureAwait(false))
            {
                return true;
            }

            var cooldown = ResolveSpiritmasterPetHpCooldown(rule);
            var pressedAt = DateTimeOffset.Now;
            state.MarkSpiritmasterPetHpSkillPressed(skill.SkillId, pressedAt, cooldown);
            if (isElementalReplenishment)
            {
                var retryInterval = ResolveSpiritmasterElementalReplenishmentRetryInterval(settings);
                state.BeginSpiritmasterPetHpIncreaseConfirmation(
                    skill.SkillId,
                    rule.Key,
                    pet.ServerObjectId,
                    pet.CurrentHp,
                    pet.CapturedAt,
                    pressedAt,
                    retryInterval,
                    ResolveSpiritmasterElementalReplenishmentConfirmationLifetime(cooldown),
                    cooldown);
                context.Logger.Info(
                    "semi_auto.spiritmaster.elemental_replenishment.confirmation_started",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["skillId"] = skill.SkillId,
                        ["skillName"] = skill.Name,
                        ["key"] = rule.Key,
                        ["petServerObjectId"] = pet.ServerObjectId,
                        ["baselinePetHp"] = pet.CurrentHp,
                        ["baselinePetMaxHp"] = pet.MaxHp,
                        ["playerHpPercent"] = player?.HpPercent,
                        ["retryIntervalMs"] = (int)retryInterval.TotalMilliseconds
                    });
            }
            else
            {
                MarkSpiritmasterSkillPressed(state, settings, skill);
            }

            context.Logger.Info("semi_auto.spiritmaster.pet_hp_key_pressed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = rule.Key,
                ["skillId"] = skill.SkillId,
                ["skillName"] = skill.Name,
                ["petHpPercent"] = petHpPercent,
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

            if (HasSpiritmasterPetBuff(state, rule, skill, localPet.AbnormalStatuses))
            {
                state.ConfirmSpiritmasterPetBuff(skill.SkillId);
                continue;
            }

            if (!state.ShouldAttemptSpiritmasterPetBuff(skill, localPet.Pet.ServerObjectId) ||
                ShouldSkipSpiritmasterSpecialSkillForCooldown(skill, state, DateTimeOffset.Now))
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

            var currentRoster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
            // Pet/status reads can yield; local life and DP are the last official
            // guard before this finite input, including death during that read.
            var currentPlayer = await ReadPlayerAsync(context).ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
            if (currentPlayer.IsDead || currentRoster.LocalPlayerPet.Pet is not { IsSummoned: true, IsAlive: true } currentPet ||
                currentPet.ServerObjectId != localPet.Pet.ServerObjectId)
            {
                state.ClearSpiritmasterPetBuffAttempts();
                return false;
            }
            if (requiredDp > currentPlayer.CurrentDp) continue;
            if (HasSpiritmasterPetBuff(state, rule, skill, currentRoster.LocalPlayerPet.AbnormalStatuses))
            {
                state.ConfirmSpiritmasterPetBuff(skill.SkillId);
                continue;
            }

            var beforeIds = currentRoster.LocalPlayerPet.AbnormalStatuses.Select(entry => entry.AbnormalId)
                .Where(id => id != 0).ToHashSet();

            var pressed = await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_buff").ConfigureAwait(false);
            state.MarkSpiritmasterPetBuffAttempt(skill, currentPet.ServerObjectId, _timeProvider,
                ResolveCooldownConfirmationWindow(settings, useSpiritmasterMinimum: true), MaintenanceKeyRetryInterval);
            if (!pressed) continue;

            MarkSpiritmasterSkillPressed(state, settings, skill);
            await TryLearnSpiritmasterPetBuffAfterPressAsync(
                    context,
                    state,
                    rule,
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
        SpiritmasterPetBuffRuleConfig rule,
        SkillSnapshot skill,
        HashSet<uint> beforeIds)
    {
        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        var afterEntries = roster.LocalPlayerPet.AbnormalStatuses;
        var rejectedCandidateAbnormalIds = new HashSet<uint>();
        var learned = 0u;
        foreach (var entry in afterEntries
                     .Where(entry => entry.AbnormalId != 0 && !beforeIds.Contains(entry.AbnormalId))
                     .OrderByDescending(entry => entry.AbnormalId == skill.SkillId)
                     .ThenByDescending(entry => entry.IsBuffCategory))
        {
            if (TryResolveTrustedSpiritmasterPetBuffAbnormalId(rule, skill, entry.AbnormalId))
            {
                learned = entry.AbnormalId;
                break;
            }

            rejectedCandidateAbnormalIds.Add(entry.AbnormalId);
        }

        if (learned == 0)
        {
            if (rejectedCandidateAbnormalIds.Count > 0)
            {
                context.Logger.Info("semi_auto.spiritmaster.pet_buff_learn_rejected", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = rule.Key,
                    ["skillId"] = skill.SkillId,
                    ["skillName"] = skill.Name,
                    ["configuredAbnormalStatusId"] = rule.AbnormalStatusId,
                    ["rejectedCandidateAbnormalIds"] = FormatAbnormalIdList(rejectedCandidateAbnormalIds),
                    ["rejectedCandidateCount"] = rejectedCandidateAbnormalIds.Count
                });
            }

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
            TryResolveTrustedSpiritmasterPetBuffAbnormalId(rule, skill, learnedAbnormalId) &&
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

    private static bool TryResolveTrustedSpiritmasterPetBuffAbnormalId(
        SpiritmasterPetBuffRuleConfig rule,
        SkillSnapshot skill,
        uint abnormalId)
    {
        if (abnormalId == 0)
        {
            return false;
        }

        if (rule.AbnormalStatusId != 0 && abnormalId == rule.AbnormalStatusId)
        {
            return true;
        }

        return abnormalId == skill.SkillId;
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

    private async Task<bool> PressOpeningSkillIfNeededAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        SemiAutoSkillPlan plan,
        LockedTargetSnapshot target,
        IReadOnlyList<SkillSnapshot>? observedSnapshot = null,
        Action<SkillSnapshot>? onSkillPressed = null)
    {
        state.ObserveOpeningSkillTarget(target);
        if (!plan.HasOpeningSkill || !state.ShouldHandleOpeningSkill(target))
            return false;

        while (state.ShouldHandleOpeningSkill(target))
        {
            context.StopToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.Now;
            // This also resets the cursor when the target identity changes.
            var confirmationStartedAt = state.GetOrStartOpeningSkillAttemptStartedAt(target, now);
            if (state.OpeningSkillIndex >= plan.OpeningSkills.Count)
            {
                state.MarkOpeningSkillHandled(target);
                return false;
            }

            var openingSkill = plan.OpeningSkills[state.OpeningSkillIndex];
            var confirmationTimeout = ResolveOpeningSkillConfirmationTimeout();
            if (now - confirmationStartedAt >= confirmationTimeout)
            {
                LogOpeningSkillConfirmTimeout(context, target, openingSkill, now - confirmationStartedAt, confirmationTimeout);
                FinishOpeningSkillEntry(state, plan, target);
                continue;
            }

            var skills = observedSnapshot ?? await ReadOpeningSkillAsync(context, openingSkill).ConfigureAwait(false);
            var skill = openingSkill.ResolveSkill(skills);
            if (skill is null)
            {
                LogOpeningSkillSkipped(context, target, openingSkill, null, "missing");
                FinishOpeningSkillEntry(state, plan, target);
                continue;
            }

            var readiness = SemiAutoSkillReleasePriority.GetActionCooldownReadiness(skill, state);
            if (readiness != SemiAutoSkillCooldownReadiness.Ready)
            {
                LogOpeningSkillSkipped(context, target, openingSkill, skill,
                    SemiAutoSkillReleasePriority.FormatCooldownReason(skill, state));
                FinishOpeningSkillEntry(state, plan, target);
                continue;
            }

            if (!await PressNodeKeyAsync(context, openingSkill, settings, "opening_skill").ConfigureAwait(false))
            {
                FinishOpeningSkillEntry(state, plan, target);
                continue;
            }

            state.MarkOpeningSkillPressed();
            onSkillPressed?.Invoke(skill);
            if (skill.CooldownDuration == 0)
            {
                FinishOpeningSkillEntry(state, plan, target);
                return true;
            }

            var confirmationExpiresAt = DateTimeOffset.Now + ResolveCooldownConfirmationWindow(settings, plan.UsesSpiritmasterAutoLogic);
            state.MarkSkillPressed(skill, confirmationExpiresAt,
                retryKey: openingSkill.Key, retrySkillName: openingSkill.Name,
                retrySkillType: openingSkill.Type, retryPhase: "opening_skill");
            state.SuppressUncalibratedUnknownSkill(skill, confirmationExpiresAt);
            return true;
        }

        return false;
    }

    private static void FinishOpeningSkillEntry(
        SemiAutoCombatState state, SemiAutoSkillPlan plan, LockedTargetSnapshot target)
    {
        var wasPressed = state.OpeningSkillWasPressed;
        if (wasPressed) state.ClearPressedSkillCooldownConfirmation();
        if (wasPressed && !plan.ReleaseAllOpeningSkills)
            state.MarkOpeningSkillHandled(target);
        else
            state.AdvanceOpeningSkill();
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

    private static void LogOpeningSkillConfirmTimeout(
        AccountWorkerContext context,
        LockedTargetSnapshot target,
        SemiAutoSkillNode node,
        TimeSpan elapsed,
        TimeSpan timeout)
    {
        context.Logger.Warn("semi_auto.opening_skill.confirm_timeout", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["skill"] = node.Name,
            ["skillId"] = node.SkillId,
            ["key"] = node.Key,
            ["elapsedMs"] = (long)elapsed.TotalMilliseconds,
            ["timeoutMs"] = (long)timeout.TotalMilliseconds
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

    private static bool IsChantStatusMaintenanceRule(
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

    private bool IsChantStatusMaintenanceRule(
        StatusMaintenanceRuleConfig rule,
        SkillSnapshot? skill,
        uint skillId)
    {
        return IsChantStatusMaintenanceRule(rule, skill) ||
               (skillId != 0 && StatusCatalog.GetChantAuraEffectAbnormalIds(skillId).Any());
    }

    private static bool ContainsChantToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (value.Contains("\u771F\u8A00", StringComparison.Ordinal) ||
                value.Contains("Chant", StringComparison.OrdinalIgnoreCase));
    }

    private static TimeSpan ResolveStatusMaintenanceConfirmWindow(bool isChantStatusMaintenance)
    {
        return isChantStatusMaintenance
            ? ChantStatusMaintenanceConfirmWindow
            : NormalStatusMaintenanceConfirmWindow;
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

    private StatusMaintenanceAbnormalMatch ResolveConfirmedStatusMaintenanceAbnormalId(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        SemiAutoCombatState state,
        HashSet<uint> beforeIds,
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries,
        ISet<uint> rejectedCandidateAbnormalIds)
    {
        var known = ResolveKnownStatusMaintenanceAbnormalId(rule, skillId, state, entries);
        if (known.Found)
        {
            return known;
        }

        foreach (var entry in entries
                     .Where(entry => entry.AbnormalId != 0 && !beforeIds.Contains(entry.AbnormalId))
                     .OrderByDescending(entry => skillId != 0 && entry.AbnormalId == skillId)
                     .ThenByDescending(entry => entry.Category == 0)
                     .ThenByDescending(entry => entry.IsBuffCategory))
        {
            if (TryResolveTrustedStatusMaintenanceAbnormalId(
                    rule,
                    skillId,
                    entry.AbnormalId,
                    out var source))
            {
                return new StatusMaintenanceAbnormalMatch(entry.AbnormalId, source);
            }

            rejectedCandidateAbnormalIds.Add(entry.AbnormalId);
        }

        return StatusMaintenanceAbnormalMatch.None;
    }

    private StatusMaintenanceAbnormalMatch ResolveKnownStatusMaintenanceAbnormalId(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        SemiAutoCombatState state,
        IReadOnlyList<AbnormalStatusEntrySnapshot> entries)
    {
        if (rule.AbnormalStatusId != 0 &&
            entries.Any(entry => entry.AbnormalId == rule.AbnormalStatusId))
        {
            return new StatusMaintenanceAbnormalMatch(rule.AbnormalStatusId, "configured");
        }

        if (skillId != 0)
        {
            foreach (var abnormalId in StatusCatalog.GetChantAuraEffectAbnormalIds(skillId))
            {
                if (entries.Any(entry => entry.AbnormalId == abnormalId))
                {
                    return new StatusMaintenanceAbnormalMatch(abnormalId, "xml_mapped");
                }
            }
        }

        if (skillId != 0 &&
            state.TryGetStatusMaintenanceAbnormalId(skillId, out var learnedAbnormalId) &&
            entries.Any(entry => entry.AbnormalId == learnedAbnormalId) &&
            TryResolveTrustedStatusMaintenanceAbnormalId(rule, skillId, learnedAbnormalId, out var learnedSource))
        {
            return new StatusMaintenanceAbnormalMatch(learnedAbnormalId, $"learned_{learnedSource}");
        }

        if (skillId != 0 &&
            entries.Any(entry => entry.AbnormalId == skillId))
        {
            return new StatusMaintenanceAbnormalMatch(skillId, "skill_id");
        }

        return StatusMaintenanceAbnormalMatch.None;
    }

    private bool TryResolveTrustedStatusMaintenanceAbnormalId(
        StatusMaintenanceRuleConfig rule,
        uint skillId,
        uint abnormalId,
        out string source)
    {
        if (abnormalId == 0)
        {
            source = string.Empty;
            return false;
        }

        if (rule.AbnormalStatusId != 0 && abnormalId == rule.AbnormalStatusId)
        {
            source = "configured";
            return true;
        }

        if (skillId != 0)
        {
            foreach (var mappedAbnormalId in StatusCatalog.GetChantAuraEffectAbnormalIds(skillId))
            {
                if (abnormalId == mappedAbnormalId)
                {
                    source = "xml_mapped";
                    return true;
                }
            }

            if (abnormalId == skillId)
            {
                source = "skill_id";
                return true;
            }
        }

        source = string.Empty;
        return false;
    }

    private static string FormatAbnormalIdList(IEnumerable<uint> abnormalIds)
    {
        return string.Join(",", abnormalIds.Where(id => id != 0).Distinct().OrderBy(id => id));
    }

    private async Task<bool> TryContinueSpiritmasterElementalReplenishmentConfirmationAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IReadOnlyList<SkillSnapshot> skills,
        SummonedPetSnapshot currentPet)
    {
        var pending = state.PendingSpiritmasterPetHpIncreaseConfirmation;
        if (pending is null)
        {
            return false;
        }

        if (!IsSpiritmasterElementalReplenishment(pending.SkillId))
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            return false;
        }

        if (currentPet.ServerObjectId != pending.PetServerObjectId)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.elemental_replenishment.confirmation_cancelled",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "pet_identity_changed",
                    ["expectedPetServerObjectId"] = pending.PetServerObjectId,
                    ["currentPetServerObjectId"] = currentPet.ServerObjectId,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        var now = DateTimeOffset.Now;
        if (now >= pending.ExpiresAt)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Warn(
                "semi_auto.spiritmaster.elemental_replenishment.confirmation_expired",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["petServerObjectId"] = pending.PetServerObjectId,
                    ["baselinePetHp"] = pending.BaselineCurrentHp,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        if (now < pending.NextCheckAt)
        {
            return false;
        }

        var retryInterval = ResolveSpiritmasterElementalReplenishmentRetryInterval(settings);
        var safety = await ReadSpiritmasterElementalReplenishmentSafetyAsync(context)
            .ConfigureAwait(false);
        if (safety.Pet.ServerObjectId != pending.PetServerObjectId)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.elemental_replenishment.confirmation_cancelled",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "pet_identity_changed",
                    ["expectedPetServerObjectId"] = pending.PetServerObjectId,
                    ["currentPetServerObjectId"] = safety.Pet.ServerObjectId,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        if (safety.Pet.CapturedAt <= pending.BaselineCapturedAt)
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return false;
        }

        if (safety.Pet.CurrentHp > pending.BaselineCurrentHp)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.elemental_replenishment.pet_hp_confirmed",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["skillId"] = pending.SkillId,
                    ["petServerObjectId"] = pending.PetServerObjectId,
                    ["baselinePetHp"] = pending.BaselineCurrentHp,
                    ["currentPetHp"] = safety.Pet.CurrentHp,
                    ["petMaxHp"] = safety.Pet.MaxHp,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        if (safety.Player.HpPercent < SpiritmasterElementalReplenishmentMinPlayerHpPercent)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            LogSpiritmasterElementalReplenishmentPlayerHpBlocked(context, state, safety.Player, "retry", pending.SkillId);
            return false;
        }

        var skill = skills.FirstOrDefault(item => item.SkillId == pending.SkillId);
        if (skill is null)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.elemental_replenishment.confirmation_cancelled",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "retry_skill_missing",
                    ["petServerObjectId"] = safety.Pet.ServerObjectId
                });
            return false;
        }

        if (GetMaintenanceCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready)
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return false;
        }

        if (!await PressSpiritmasterRawKeyAsync(context, settings, pending.Key, "pet_hp_retry")
                .ConfigureAwait(false))
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return true;
        }

        var pressedAt = DateTimeOffset.Now;
        var cooldown = TimeSpan.FromMilliseconds(Math.Max(1, pending.CooldownMs));
        state.MarkSpiritmasterPetHpSkillPressed(pending.SkillId, pressedAt, cooldown);
        state.RecordSpiritmasterPetHpIncreaseRetry(
            safety.Pet.CurrentHp,
            safety.Pet.CapturedAt,
            pressedAt,
            retryInterval);
        context.Logger.Warn(
            "semi_auto.spiritmaster.elemental_replenishment.pet_hp_retry",
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skillId"] = pending.SkillId,
                ["key"] = pending.Key,
                ["petServerObjectId"] = pending.PetServerObjectId,
                ["previousBaselinePetHp"] = pending.BaselineCurrentHp,
                ["retryBaselinePetHp"] = safety.Pet.CurrentHp,
                ["petMaxHp"] = safety.Pet.MaxHp,
                ["playerHpPercent"] = safety.Player.HpPercent,
                ["attemptCount"] = pending.AttemptCount + 1
            });
        return true;
    }

    private async Task<SpiritmasterElementalReplenishmentSafetySnapshot>
        ReadSpiritmasterElementalReplenishmentSafetyAsync(
            AccountWorkerContext context)
    {
        var pet = await ReadSummonedPetAsync(context).ConfigureAwait(false);
        // Recheck the player's HP cost boundary after the pet read has yielded.
        var player = await ReadPlayerAsync(context)
            .ConfigureAwait(false);
        return new SpiritmasterElementalReplenishmentSafetySnapshot(player, pet);
    }

    private static bool IsSpiritmasterElementalReplenishment(SkillSnapshot skill)
    {
        return IsSpiritmasterElementalReplenishment(skill.SkillId);
    }

    private static bool IsSpiritmasterElementalReplenishment(uint skillId) =>
        SpiritmasterElementalReplenishmentSkillIds.Contains(skillId);

    private static TimeSpan ResolveSpiritmasterElementalReplenishmentRetryInterval(
        SemiAutoScriptSettings settings)
    {
        return Max(
            Ms(settings.PostPressSuppressMs, 650),
            SpiritmasterElementalReplenishmentMinimumRetryInterval);
    }

    private static TimeSpan ResolveSpiritmasterElementalReplenishmentConfirmationLifetime(TimeSpan localCooldown)
    {
        var scaledMs = Math.Clamp(
            (long)Math.Ceiling(localCooldown.TotalMilliseconds) * 3L,
            (long)SpiritmasterElementalReplenishmentMinimumConfirmationLifetime.TotalMilliseconds,
            600_000L);
        return TimeSpan.FromMilliseconds(scaledMs);
    }

    private static void LogSpiritmasterElementalReplenishmentPlayerHpBlocked(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        string phase,
        uint skillId)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastSpiritmasterPetHpConfirmationLogAt, now))
        {
            return;
        }

        state.LastSpiritmasterPetHpConfirmationLogAt = now;
        context.Logger.Info(
            "semi_auto.spiritmaster.elemental_replenishment.player_hp_blocked",
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["skillId"] = skillId,
                ["playerCurrentHp"] = player.CurrentHp,
                ["playerMaxHp"] = player.MaxHp,
                ["playerHpPercent"] = player.HpPercent,
                ["minimumPlayerHpPercent"] = SpiritmasterElementalReplenishmentMinPlayerHpPercent
            });
    }

    private static bool ShouldSelectSelfBeforeStatusMaintenance(AccountWorkerContext context)
    {
        var team = context.Config.ScriptSettings?.Team;
        return team?.Role == TeamRole.Support &&
               (team.Support?.Enabled ?? false);
    }

    private static bool ShouldSuppressAlwaysSupportStatusMaintenanceDuringCustomCombat(
        AccountWorkerContext context)
    {
        var scriptSettings = context.Config.ScriptSettings;
        return scriptSettings?.MainMode == AccountMainMode.CustomCombat &&
               scriptSettings.CombatMode is AccountCombatMode.Stationary or AccountCombatMode.Path &&
               ShouldSelectSelfBeforeStatusMaintenance(context);
    }

    private static bool IsSelectedLocalPlayer(
        LockedTargetSnapshot target,
        PlayerSnapshot player)
    {
        if (target.ObjectType != LockedTargetSnapshot.PlayerObjectType ||
            target.ServerObjectId == 0)
        {
            return false;
        }

        if (target.LocalServerObjectId != 0)
        {
            return target.ServerObjectId == target.LocalServerObjectId;
        }

        return player.EntityId != 0 &&
               target.TargetEntityId != 0 &&
               target.TargetEntityId == player.EntityId;
    }

    private static void LogStatusMaintenanceSelfTargetSelected(
        AccountWorkerContext context,
        StatusMaintenanceRuleConfig rule,
        LockedTargetSnapshot target,
        bool alreadySelected)
    {
        context.Logger.Info("semi_auto.maintenance.self_target_selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = SupportSelfSelectKey,
            ["statusKey"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["alreadySelected"] = alreadySelected,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["localServerObjectId"] = target.LocalServerObjectId
        });
    }

    private static void LogStatusMaintenanceSelfTargetSelectionFailed(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        StatusMaintenanceRuleConfig rule,
        LockedTargetSnapshot target,
        string reason,
        string? error)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastMaintenanceWarningAt, now))
        {
            return;
        }

        state.LastMaintenanceWarningAt = now;
        context.Logger.Warn("semi_auto.maintenance.self_target_select.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["key"] = SupportSelfSelectKey,
            ["statusKey"] = rule.Key,
            ["skillId"] = rule.SkillId,
            ["skillName"] = rule.SkillName,
            ["targetReadSuccess"] = true,
            ["targetEntityId"] = target.TargetEntityId,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetObjectType"] = target.ObjectType,
            ["localServerObjectId"] = target.LocalServerObjectId
        });
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
                out var calibration,
                out var calibrationRejection))
        {
            if (calibrationRejection is not null)
            {
                LogCooldownCalibrationRejected(context, calibrationRejection.Value, "maintenance");
            }

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

    private static void LogCooldownCalibrationRejected(
        AccountWorkerContext context,
        SemiAutoCooldownTickCalibrationRejection rejection,
        string? source = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["skill"] = rejection.SkillName,
            ["skillId"] = rejection.SkillId,
            ["durationMs"] = rejection.CooldownDuration,
            ["endTick"] = rejection.CooldownEndTime,
            ["startTick"] = rejection.CooldownStartTick,
            ["osTick"] = rejection.OsTick,
            ["oldOffsetMs"] = rejection.OldOffsetMs,
            ["newOffsetMs"] = rejection.NewOffsetMs,
            ["deltaMs"] = rejection.DeltaMs,
            ["maxDeltaMs"] = rejection.MaxDeltaMs,
            ["reason"] = rejection.Reason
        };

        if (!string.IsNullOrWhiteSpace(source))
        {
            fields["source"] = source;
        }

        context.Logger.Warn("semi_auto.cooldown.calibration_rejected", fields);
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

    private static async Task<PlayerSnapshot> ReadPlayerAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadPlayerAsync().ConfigureAwait(false)).Value;

    private static async Task<IReadOnlyList<InventoryItemSnapshot>> ReadInventoryAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadInventoryAsync().ConfigureAwait(false)).Value;

    private static async Task<PlayerAbnormalStatusSnapshot> ReadPlayerAbnormalStatusesAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadPlayerAbnormalStatusesAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetSnapshot> ReadLockedTargetAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetSnapshot> ReadLockedTargetForConfirmationAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetAbnormalStatusSnapshot> ReadLockedTargetAbnormalStatusesAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadLockedTargetAbnormalStatusesAsync().ConfigureAwait(false)).Value;

    private static async Task<SummonedPetRosterSnapshot> ReadSummonedPetRosterAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadSummonedPetRosterAsync().ConfigureAwait(false)).Value;

    private static async Task<SummonedPetSnapshot> ReadSummonedPetAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadSummonedPetAsync().ConfigureAwait(false)).Value;

    private static void UpdateCooldownCalibration(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        IReadOnlyList<SkillSnapshot> skills,
        uint osTick,
        DateTimeOffset now)
    {
        if (state.TryUpdateCooldownTickCalibration(skills, osTick, now, out var calibration, out var rejection))
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
        else if (rejection is not null)
        {
            LogCooldownCalibrationRejected(context, rejection.Value);
        }
    }

    private static async Task<IReadOnlyList<SkillSnapshot>> ReadOpeningSkillAsync(
        AccountWorkerContext context,
        SemiAutoSkillNode openingSkill)
    {
        var ids = openingSkill.SkillId == 0 ? null : new[] { openingSkill.SkillId };
        return (await context.Snapshots.ReadSkillsAsync(ids).ConfigureAwait(false)).Value;
    }

    private static async Task<IReadOnlyList<SkillSnapshot>> ReadMaintenanceSkillsAsync(AccountWorkerContext context,
        IEnumerable<(uint SkillId, string SkillName)> references)
    {
        var selected = references.ToArray();
        var ids = selected.Select(item => item.SkillId).Where(id => id != 0).Distinct().ToArray();
        // The full editor list groups learned ranks. Explicit maintenance IDs
        // must use their exact official channel, including confirmation polls.
        var exact = ids.Length == 0 ? Array.Empty<SkillSnapshot>() :
            (await context.Snapshots.ReadSkillsAsync(ids).ConfigureAwait(false)).Value;
        if (selected.Length > 0 && selected.All(item => item.SkillId != 0)) return exact;
        var full = (await context.Snapshots.ReadSkillsAsync().ConfigureAwait(false)).Value;
        var missingIds = (context.SkillBindings?.SkillIds ?? Array.Empty<uint>())
            .Where(id => !full.Any(skill => skill.SkillId == id) && !exact.Any(skill => skill.SkillId == id)).ToArray();
        var placed = missingIds.Length == 0 ? Array.Empty<SkillSnapshot>() :
            (await context.Snapshots.ReadSkillsAsync(missingIds).ConfigureAwait(false)).Value;
        return exact.Concat(full).Concat(placed).DistinctBy(skill => skill.SkillId).ToArray();
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

    private static TimeSpan ResolveOpeningSkillConfirmationTimeout()
    {
        var configured = Environment.GetEnvironmentVariable(OpeningSkillConfirmationTimeoutEnvVar);
        if (int.TryParse(configured, out var configuredMs) && configuredMs > 0)
        {
            return TimeSpan.FromMilliseconds(Math.Clamp(configuredMs, 1, 60_000));
        }

        return OpeningSkillConfirmationTimeout;
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
