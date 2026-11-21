using Roadhog.Core.Api;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Paths;
using Roadhog.Application.Trading;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Food;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop : IAccountWorkerLoop
{
    private const int StartupScrollCount = 10;
    private const int StartupScrollDelta = -1;
    private static readonly TimeSpan StartupScrollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IKeyboardInput _keyboard;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly StationaryCombatController _stationaryCombat;
    private readonly TeamSupportController? _teamSupport;
    private readonly TeamOutputController? _teamOutput;
    private readonly FixedChannelController? _fixedChannel;
    private readonly CleanupWorkflowRunner? _cleanupWorkflow;
    private readonly Func<AccountConfig, ISharedAccountConfiguration?>? _sharedConfigurationFactory;
    private readonly Func<AccountConfig, GameSessions.IGameExitSession>? _gameExitSessionFactory;
    private readonly Core.Profiles.IAdvancedSkillProfileStore? _advancedSkillProfiles;
    private readonly ISharedPathStore? _pathStore;

    public DefaultAccountWorkerLoop(
        IKeyboardInput keyboard,
        SemiAutoCombatController semiAuto,
        StationaryCombatController stationaryCombat,
        TeamSupportController? teamSupport = null,
        TeamOutputController? teamOutput = null,
        FixedChannelController? fixedChannel = null,
        CleanupWorkflowRunner? cleanupWorkflow = null,
        Func<AccountConfig, ISharedAccountConfiguration?>? sharedConfigurationFactory = null,
        Func<AccountConfig, GameSessions.IGameExitSession>? gameExitSessionFactory = null,
        Core.Profiles.IAdvancedSkillProfileStore? advancedSkillProfiles = null,
        ISharedPathStore? pathStore = null)
    {
        _keyboard = keyboard;
        _semiAuto = semiAuto;
        _stationaryCombat = stationaryCombat;
        _teamSupport = teamSupport;
        _teamOutput = teamOutput;
        _fixedChannel = fixedChannel;
        _cleanupWorkflow = cleanupWorkflow;
        _sharedConfigurationFactory = sharedConfigurationFactory;
        _gameExitSessionFactory = gameExitSessionFactory;
        _advancedSkillProfiles = advancedSkillProfiles;
        _pathStore = pathStore;
    }

    public async Task RunAsync(AccountWorkerContext context)
    {
        await using var exitSession = _gameExitSessionFactory?.Invoke(context.Config);
        try { await RunCoreAsync(context, exitSession).ConfigureAwait(false); }
        finally
        {
            if (context.StopToken.IsCancellationRequested && context.GameSessionRequests.Current is not null && _gameExitSessionFactory is not null)
            {
                // A cancelled read executor is retired. This new adapter can only cancel an existing countdown.
                using var cancelDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await using var cancelSession = _gameExitSessionFactory(context.Config);
                    await GameSessions.GameExitController.CancelAsync(cancelSession, cancelDeadline.Token);
                }
                catch (Exception error)
                {
                    context.Logger.Warn("game_exit.stop_cancel_failed", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["error"] = error.Message });
                }
            }
        }
    }

    private async Task RunCoreAsync(AccountWorkerContext context, GameSessions.IGameExitSession? exitSession)
    {
        // Offline requests must reach the manager without waiting for a nonexistent player.
        if (context.GameSessionRequests.Current is { } initialExit && exitSession is not null)
        {
            var initialScene = await exitSession.ReadAsync(context.StopToken);
            context.StopToken.ThrowIfCancellationRequested();
            if (!initialScene.ProcessPresent) context.GameSessionRequests.Complete(initialExit);
        }
        if (context.GameSessionRequests.Completed is not null)
        {
            while (true)
            {
                context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);
                await Task.Delay(250, context.StopToken);
            }
        }
        // Capture the session boundary before input setup or readiness waits can cross a due time.
        // Recovery uses the same context, so it retains the original boundary and pending task.
        _cleanupWorkflow?.InitializeGrocerySchedule(context);
        await AdvancedSkillProfileLoader.ApplyAsync(context.Config, _advancedSkillProfiles, context.Logger, context.StopToken).ConfigureAwait(false);
        context.Logger.Info("worker.loop.enter", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["mode"] = context.Config.MainMode.ToString(),
            ["combatFlowVersion"] = nameof(CombatFlowVersion.CoordinatedV2),
            ["hardwareKey"] = context.Config.HardwareKey,
            ["vmmDevice"] = context.Config.VmmDeviceName
        });
        var startupF1 = await _keyboard.PressKeyAsync("F1", TimeSpan.FromMilliseconds(35), context.StopToken).ConfigureAwait(false);
        if (!startupF1.Success) throw new InvalidOperationException("启动时按 F1 失败：" + startupF1.Error);
        context.Logger.Info("worker.startup.f1", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = "F1"
        });
        if (_keyboard is IInputStateReset inputReset)
        {
            var reset = await inputReset.ReleaseAllAsync(context.StopToken).ConfigureAwait(false);
            if (!reset.Success) throw new InvalidOperationException("运行前释放输入失败：" + reset.Error);
        }
        await ScrollStartupMouseAsync(context).ConfigureAwait(false);
        await ReleaseStartupMovementAsync(context).ConfigureAwait(false);
        await new StartupWindowCloser(_keyboard).RunAsync(context).ConfigureAwait(false);
        await context.PrepareSkillBindingsAsync().ConfigureAwait(false);

        var scriptSettings = context.Config.ScriptSettings ?? new ScriptSettings
        {
            ProfileName = context.Config.ProfileName,
            MainMode = context.Config.MainMode,
            CombatMode = context.Config.CombatMode
        };
        var semiAutoPlan = SemiAutoSkillPlan.FromSharedSettings(scriptSettings.Skills, context.SkillBindings, context.ReportMissingSkillBinding);
        var attackPlan = QuickbarSkillPlan.FromSettings(scriptSettings.QuickbarSkills, context.SkillBindings!, context.ReportMissingSkillBinding);
        var semiAutoState = new SemiAutoCombatState { QuickbarPlan = attackPlan };
        var stationaryCombatState = new StationaryCombatState();
        var teamSupportState = new TeamSupportState();
        var teamOutputState = new TeamOutputState();
        var fixedChannelState = new FixedChannelState();
        var foodMaintenance = new FoodMaintenanceController(_keyboard);
        context.WorkflowOwnsCleanup = _cleanupWorkflow != null;
        var cleanupProgress = new CleanupLoopProgress();
        var nextSharedRefresh = DateTimeOffset.MinValue;
        var sharedConfiguration = _sharedConfigurationFactory?.Invoke(context.Config);
        CombatJumpAssistSession? jumpAssist = null;
        if (scriptSettings.Combat.JumpAssistEnabled)
        {
            var teamFollower =
                IsTeamSupportEnabled(scriptSettings) ||
                IsTeamOutputEnabled(scriptSettings);
            jumpAssist = new CombatJumpAssistSession(context, _keyboard, teamFollower);
            stationaryCombatState.JumpAssist = jumpAssist;
        }
        context.Logger.Info("semi_auto.plan.loaded", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["mode"] = SkillTreeReleaseMode.QuickbarAvailability.ToString(),
            ["topLevelSkillCount"] = attackPlan.Roots.Count,
            ["chainRootCount"] = attackPlan.Roots.Count(root => root.Children.Count > 0),
            ["skillReadIdCount"] = attackPlan.SkillReadIds.Concat(semiAutoPlan.SharedSkillReadIds).Distinct().Count(),
            ["requiresFullSkillRead"] = semiAutoPlan.RequiresFullSharedSkillRead,
            ["hasExecutableSkills"] = attackPlan.HasCombatActions,
            ["spiritmasterAutoLogic"] = semiAutoPlan.UsesSpiritmasterAutoLogic,
            ["topLevelSkills"] = string.Join(" > ", attackPlan.Roots.Select(root => root.Name + "@" + root.Key)),
            ["chainRoots"] = string.Join(" > ", attackPlan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key))
        });

        var restartInputReleased = false;
        try
        {
            while (!context.StopToken.IsCancellationRequested)
            {
                using var returnFailureTick = stationaryCombatState.ReturnNavigationFailure.TrackTick();
                using var readBudget = SnapshotReadBudget.Enter(TimeSpan.FromSeconds(2));
                cleanupProgress.OperationActive = false;
                try
                {
                    context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);
                    // Exit owns input and process observation before any game-world read.
                    if (context.GameSessionRequests.Completed is not null)
                    {
                        await Task.Delay(250, context.StopToken);
                        continue;
                    }
                    if (context.GameSessionRequests.Current is { } exitRequest)
                    {
                        if (exitSession is null) throw new InvalidOperationException("未配置退出游戏适配器。");
                        var exitTick = await exitRequest.Exit.TickAsync(exitSession, async () =>
                        {
                            if (exitRequest.AfterWorldSnapshotStall)
                            {
                                _stationaryCombat.StopWorkerBackgroundWork(context, stationaryCombatState);
                                await ReleaseActiveInputAsync(context, stationaryCombatState).ConfigureAwait(false);
                            }
                            else
                            {
                                if (fixedChannelState.AwaitingConfirmation) return false;
                                if (!await _stationaryCombat.PrepareCleanupTickAsync(context, semiAutoPlan, semiAutoState, stationaryCombatState)) return false;
                            }
                            if (jumpAssist is not null) await jumpAssist.StopAsync("game_exit");
                            return true;
                        }, context.StopToken);
                        context.RuntimeStates.MarkCleanupProgress(context.Config.AccountName, exitTick.Message);
                        if (exitTick.Completed)
                        {
                            context.GameSessionRequests.Complete(exitRequest);
                            context.Logger.Info("game_exit.completed", new Dictionary<string, object?>
                                { ["account"] = context.Config.AccountName, ["requestId"] = exitRequest.Id,
                                    ["operation"] = exitRequest.Operation.ToString() });
                        }
                        if (exitRequest.AfterWorldSnapshotStall || !exitTick.YieldToCombat || !fixedChannelState.AwaitingConfirmation)
                        {
                            await Task.Delay(250, context.StopToken);
                            continue;
                        }
                    }
                    if (context.GameSessionRequests.ScriptRestartRequestId is { } restartRequestId)
                    {
                        if (!restartInputReleased)
                        {
                            await ReleaseActiveInputAsync(context, stationaryCombatState).ConfigureAwait(false);
                            restartInputReleased = true;
                            context.Logger.Info("account.restart.requested", new Dictionary<string, object?>
                                { ["account"] = context.Config.AccountName, ["requestId"] = restartRequestId });
                        }
                        await Task.Delay(context.Options.TickInterval, context.StopToken).ConfigureAwait(false);
                        continue;
                    }
                    if (context.GameSessionRequests.Current is null &&
                        !context.CleanupRequests.StandaloneShopRestartRequestId.HasValue &&
                        await _stationaryCombat.TryPrepareStartupResourceReturnAsync(
                            context, semiAutoState, stationaryCombatState).ConfigureAwait(false) is { } startupResourceDelay)
                    {
                        await Task.Delay(startupResourceDelay, context.StopToken).ConfigureAwait(false);
                        continue;
                    }
                    if (context.CleanupRequests.Current is { Failure: not null } failedShop)
                    {
                        cleanupProgress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                        await AbandonCleanupAsync(context, stationaryCombatState, failedShop,
                            new InvalidOperationException(failedShop.Failure));
                        cleanupProgress.CheckLifeAfterYield = true;
                    }
                    if (cleanupProgress.CheckLifeAfterYield)
                    {
                        var recoveryDelay = await _stationaryCombat.TickPlayerLifeGuardAsync(
                            context, semiAutoPlan, semiAutoState, stationaryCombatState,
                            scriptSettings.MainMode == AccountMainMode.CustomCombat).ConfigureAwait(false);
                        if (recoveryDelay.HasValue)
                        {
                            await Task.Delay(recoveryDelay.Value, context.StopToken);
                            continue;
                        }
                        cleanupProgress.CheckLifeAfterYield = false;
                    }

                    var foodCombatActive = FoodMaintenanceController.HasWork(stationaryCombatState);
                    if (scriptSettings.MainMode == AccountMainMode.SemiAuto &&
                        (scriptSettings.Maintenance.AutoDrinkEnabled || scriptSettings.Maintenance.AutoFoodEnabled))
                        foodCombatActive |= (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value.IsMonsterAlive;
                    foodMaintenance.ObserveCombat(scriptSettings.Maintenance, foodCombatActive);
                    if (context.CleanupRequests.StandaloneShopRestartRequestId.HasValue)
                    {
                        // Verified sellout keeps the existing manager-owned Stop/Start handoff.
                        await Task.Delay(context.Options.TickInterval, context.StopToken).ConfigureAwait(false);
                        continue;
                    }
                    if ((sharedConfiguration is not null || _pathStore is not null) && DateTimeOffset.UtcNow >= nextSharedRefresh)
                    {
                        nextSharedRefresh = DateTimeOffset.UtcNow.AddSeconds(2);
                        try
                        {
                            if (sharedConfiguration is not null)
                                await SharedConfigurationRefresh.RefreshAsync(sharedConfiguration, scriptSettings,
                                    includeCleanup: context.CleanupRequests.Current is null, context.StopToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            context.Logger.Warn("shared_configuration.refresh_failed", new Dictionary<string, object?>
                                { ["account"] = context.Config.AccountName, ["error"] = ex.Message });
                            context.RuntimeStates.MarkWarning(context.Config.AccountName, "共享配置刷新失败：" + ex.Message);
                        }
                        await RefreshRevivePathMonsterKillPrioritiesAsync(context, scriptSettings).ConfigureAwait(false);
                    }
                    if (await _stationaryCombat.TryTickTownReturnAsync(
                            context, semiAutoPlan, semiAutoState, stationaryCombatState).ConfigureAwait(false) is { } townReturnDelay)
                    {
                        await Task.Delay(townReturnDelay, context.StopToken).ConfigureAwait(false);
                        continue;
                    }

                    if (_cleanupWorkflow != null && !fixedChannelState.AwaitingConfirmation)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var maintenance = scriptSettings.Maintenance;
                        var followingRevivePath = StationaryCombatController.IsFollowingRevivePath(stationaryCombatState);
                        if (context.CleanupRequests.Current == null && now >= cleanupProgress.NextCheck && now >= context.CleanupRequests.AutomaticRetryAfter &&
                            !stationaryCombatState.Fighting && !stationaryCombatState.LootAfterKill.Active &&
                            (stationaryCombatState.TopLevelState == StationaryCombatTopLevelState.Normal ||
                             (followingRevivePath && maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop &&
                              maintenance.CleanupWorkflow.GroceryScheduleEnabled)))
                        {
                            cleanupProgress.NextCheck = now.AddSeconds(2);
                            if (followingRevivePath)
                            {
                                // Only the due schedule can interrupt a revive route. Bag capacity,
                                // arrow restocking and manual/exit preparation retain their admission rules.
                                if (await _cleanupWorkflow.GroceryScheduleDueAsync(context))
                                    context.CleanupRequests.Request(scriptSettings, manual: false, resetsCooldown: false, allowNpcSell: false,
                                        groceryTrigger: GroceryShopTrigger.Scheduled);
                            }
                            else if (await NpcArrowPurchaseSequence.NeededAsync(context, duringCleanup: false))
                            {
                                context.CleanupRequests.Request(scriptSettings, manual: false, arrowRestock: true);
                                context.Logger.Info("arrow_restock.requested", new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
                            }
                            else if (maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop)
                            {
                                var scheduled = await _cleanupWorkflow.GroceryScheduleDueAsync(context);
                                var bagFull = false;
                                if (!scheduled && maintenance.BagCleanupEnabled && maintenance.BagCleanupThreshold > 0 &&
                                    now >= context.CleanupRequests.BackpackRetryAfter)
                                {
                                    var bag = (await context.Snapshots.ReadInventoryAsync()).Value;
                                    var capacity = (await context.Snapshots.ReadInventoryCapacityAsync()).Value;
                                    bagFull = BagCleanupController.CountFreeSlots(bag, capacity) < maintenance.BagCleanupThreshold;
                                }
                                if (scheduled || bagFull)
                                    context.CleanupRequests.Request(scriptSettings, manual: false, resetsCooldown: false, allowNpcSell: false,
                                        groceryTrigger: scheduled ? GroceryShopTrigger.Scheduled : GroceryShopTrigger.Backpack);
                            }
                            else if (maintenance.BagCleanupEnabled && maintenance.BagCleanupThreshold > 0 && maintenance.CleanupWorkflow.NpcCleanup &&
                                now >= context.CleanupRequests.BackpackRetryAfter)
                            {
                                var bag = (await context.Snapshots.ReadInventoryAsync()).Value;
                                var capacity = (await context.Snapshots.ReadInventoryCapacityAsync()).Value;
                                if (BagCleanupController.CountFreeSlots(bag, capacity) < maintenance.BagCleanupThreshold)
                                {
                                    var automatic = scriptSettings.Clone();
                                    var fullCleanupAllowed = now - cleanupProgress.LastCleanup >= BagCleanupController.FullCleanupCooldown;
                                    if (!fullCleanupAllowed)
                                    {
                                        // Existing discard rules remain eligible during the full-cleanup cooldown.
                                        if (!automatic.Maintenance.CleanupWorkflow.NpcCleanup || BagCleanupItemMatcher.SelectDiscardItems(bag, automatic.Maintenance, includeGreenArmorFallback: true).Count == 0)
                                            automatic.Maintenance.CleanupWorkflow.NpcCleanup = false;
                                    }
                                    if (automatic.Maintenance.CleanupWorkflow.NpcCleanup)
                                        context.CleanupRequests.Request(automatic, manual: false, resetsCooldown: fullCleanupAllowed, allowNpcSell: fullCleanupAllowed);
                                }
                            }
                        }
                        if (context.CleanupRequests.Current is { } cleanup)
                        {
                            var cleanupResult = await CleanupRequestRunner.RunAsync(context, stationaryCombatState, cleanup, cleanupProgress,
                                new CleanupRequestActions(this, context, semiAutoPlan, semiAutoState, stationaryCombatState, jumpAssist));
                            if (cleanupResult == CleanupRequestTickResult.RestartPending) continue;
                            await Task.Delay(context.Options.TickInterval, context.StopToken);
                            continue;
                        }
                    }

                    var delay = context.Options.TickInterval;
                    var mainMode = scriptSettings.MainMode;
                    var combatMode = scriptSettings.CombatMode;
                    var isStationaryCombat =
                        mainMode == AccountMainMode.CustomCombat &&
                        combatMode == AccountCombatMode.Stationary;
                    var isPathCombat =
                        mainMode == AccountMainMode.CustomCombat &&
                        combatMode == AccountCombatMode.Path;
                    var followRevivePath = isStationaryCombat || isPathCombat;
                    var fixedChannelDelay = _fixedChannel is null
                        ? null
                        : await _fixedChannel
                            .TickAsync(
                                context,
                                scriptSettings,
                                fixedChannelState,
                                stationaryCombatState,
                                () => _stationaryCombat.PrepareForChannelSwitchAttemptAsync(
                                    context,
                                    semiAutoState,
                                    stationaryCombatState))
                            .ConfigureAwait(false);
                    await _stationaryCombat.SetChannelSwitchPendingAsync(
                        context, stationaryCombatState, fixedChannelState.WaitingForPeace).ConfigureAwait(false);
                    if (fixedChannelDelay.HasValue)
                    {
                        if (jumpAssist is not null)
                        {
                            await jumpAssist.StopAsync("fixed_channel_correction").ConfigureAwait(false);
                        }

                        delay = fixedChannelDelay.Value;
                    }
                    else
                    {
                        var lifeGuardDelay = await _stationaryCombat
                            .TickPlayerLifeGuardAsync(
                            context,
                            semiAutoPlan,
                            semiAutoState,
                            stationaryCombatState,
                            followRevivePath)
                            .ConfigureAwait(false);
                        if (lifeGuardDelay.HasValue)
                        {
                            if (jumpAssist is not null)
                            {
                                await jumpAssist.StopAsync("player_life_guard").ConfigureAwait(false);
                            }

                            delay = lifeGuardDelay.Value;
                            if (stationaryCombatState.DeathRecovery.RevivePathLeaderSiphonActive)
                            {
                                var normalWorkBlocked = true;
                                var teamResult = await TryTickTeamControllersAsync(
                                    context,
                                    scriptSettings,
                                    teamSupportState,
                                    teamOutputState,
                                    stationaryCombatState,
                                    semiAutoState)
                                    .ConfigureAwait(false);
                                if (teamResult.HasValue)
                                {
                                    delay = teamResult.Value.Delay;
                                    normalWorkBlocked = teamResult.Value.ShouldSkipNormalWork;
                                }
                                else if (stationaryCombatState.LootAfterKill.Active)
                                {
                                    normalWorkBlocked = false;
                                }

                                if (!normalWorkBlocked &&
                                    await _semiAuto
                                        .EnsureSpiritmasterPetAsync(
                                        context,
                                        semiAutoPlan,
                                        semiAutoState,
                                        beforeSummonKeyPress: () => ReleaseActiveInputAsync(context, stationaryCombatState))
                                        .ConfigureAwait(false))
                                {
                                    delay = context.Options.TickInterval;
                                }
                                else if (!normalWorkBlocked)
                                {
                                    delay = await TickNormalWorkAsync(
                                        context,
                                        semiAutoPlan,
                                        semiAutoState,
                                        stationaryCombatState,
                                        mainMode,
                                        isStationaryCombat,
                                        isPathCombat)
                                        .ConfigureAwait(false);
                                }
                            }
                        }
                        else if (await _stationaryCombat.TryTickChannelSwitchWaitAsync(
                                     context, semiAutoPlan, semiAutoState, stationaryCombatState).ConfigureAwait(false) is { } channelWaitDelay)
                        {
                            if (jumpAssist is not null && !stationaryCombatState.Fighting)
                                await jumpAssist.StopAsync("fixed_channel_wait").ConfigureAwait(false);
                            delay = channelWaitDelay;
                        }
                        else if (await foodMaintenance.TickAsync(context, stationaryCombatState,
                            () => _stationaryCombat.PrepareForChannelSwitchAttemptAsync(context, semiAutoState, stationaryCombatState, "food_maintenance"),
                            () => foodCombatActive || context.CleanupRequests.Current != null || fixedChannelState.WaitingForPeace ||
                                  fixedChannelState.AwaitingConfirmation).ConfigureAwait(false))
                        {
                            delay = context.Options.TickInterval;
                        }
                        else if (isStationaryCombat && semiAutoPlan.UsesSpiritmasterAutoLogic &&
                                 await _stationaryCombat.TryPrepareStartupReturnAsync(
                                     context, semiAutoPlan, semiAutoState, stationaryCombatState).ConfigureAwait(false) is { } startupReturnDelay)
                        {
                            delay = startupReturnDelay;
                        }
                        else if (await _semiAuto
                                     .EnsureSpiritmasterPetAsync(
                                     context,
                                     semiAutoPlan,
                                     semiAutoState,
                                     beforeSummonKeyPress: () => ReleaseActiveInputAsync(context, stationaryCombatState),
                                     handleSummonRetryDefense: () => _stationaryCombat.TryHandleCoordinatedSummonRetryDefenseAsync(
                                         context, semiAutoPlan, semiAutoState, stationaryCombatState))
                                     .ConfigureAwait(false))
                        {
                            delay = context.Options.TickInterval;
                        }
                        else
                        {
                            var normalWorkBlocked = false;
                            var teamResult = await TryTickTeamControllersAsync(
                                context,
                                scriptSettings,
                                teamSupportState,
                                teamOutputState,
                                stationaryCombatState,
                                semiAutoState)
                                .ConfigureAwait(false);
                            if (teamResult.HasValue)
                            {
                                delay = teamResult.Value.Delay;
                                normalWorkBlocked = teamResult.Value.ShouldSkipNormalWork;
                            }

                            if (!normalWorkBlocked)
                            {
                                delay = await TickNormalWorkAsync(
                                    context,
                                    semiAutoPlan,
                                    semiAutoState,
                                    stationaryCombatState,
                                    mainMode,
                                    isStationaryCombat,
                                    isPathCombat)
                                    .ConfigureAwait(false);
                            }
                        }
                    }

                    await Task.Delay(delay, context.StopToken).ConfigureAwait(false);
                }
                catch (SnapshotReadBudgetExceededException ex) when (!context.StopToken.IsCancellationRequested)
                {
                    cleanupProgress.CheckLifeAfterYield = true;
                    // A transaction may already have been submitted: never replay the workflow.
                    await ReleaseActiveInputAsync(context, stationaryCombatState).ConfigureAwait(false);
                    if (cleanupProgress.OperationActive && context.CleanupRequests.Current is { } interrupted)
                    {
                        context.Logger.Warn("cleanup_workflow.operation_timeout", new Dictionary<string, object?>
                            { ["account"] = context.Config.AccountName, ["channel"] = ex.Channel });
                        cleanupProgress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                        cleanupProgress.LastCleanup = DateTimeOffset.UtcNow;
                        await AbandonCleanupAsync(context, stationaryCombatState, interrupted, ex);
                    }
                    else if (ex.Channel == "world_objects" && ex.AwaitingFirstPublication &&
                             !fixedChannelState.AwaitingConfirmation &&
                             context.CleanupRequests.Current is null &&
                             !context.CleanupRequests.StandaloneShopRestartRequestId.HasValue)
                    {
                        context.StopToken.ThrowIfCancellationRequested();
                        if (context.GameSessionRequests.RequestWorldSnapshotRestart() is { } requestId)
                        {
                            context.RuntimeStates.MarkWarning(context.Config.AccountName, "周围对象数据尚未就绪，等待自动恢复。");
                            context.Logger.Warn("worker.world_snapshot.restart_requested", new Dictionary<string, object?>
                                { ["account"] = context.Config.AccountName, ["requestId"] = requestId });
                        }
                    }
                    context.Logger.Info("worker.read_budget.yield", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["waitMs"] = 2000 });
                    await Task.Delay(context.Options.TickInterval, context.StopToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
                {
                    // A failed tick must return to this loop, never terminate the account.
                    cleanupProgress.CheckLifeAfterYield = true;
                    if (context.CleanupRequests.Current is { } interrupted)
                    {
                        cleanupProgress.NextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                        await AbandonCleanupAsync(context, stationaryCombatState, interrupted, ex);
                    }
                    else
                    {
                        context.Logger.Error("worker.tick.failed_continuing", ex,
                            new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
                        context.RuntimeStates.MarkWarning(context.Config.AccountName, "本次操作失败，返回主流程：" + ex.Message);
                        await ReleaseFailedBranchInputAsync(context, stationaryCombatState);
                    }
                    await Task.Delay(context.Options.TickInterval, context.StopToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            semiAutoState.ResetCombatSkillActions();
            semiAutoState.ResetAttackKeyPressThrottle();
            try
            {
                try { _stationaryCombat.StopWorkerBackgroundWork(context, stationaryCombatState); }
                finally
                {
                    if (jumpAssist is not null)
                        await jumpAssist.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await ReleaseActiveInputAsync(context, stationaryCombatState).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTeamSupportEnabled(ScriptSettings scriptSettings)
    {
        return scriptSettings.Team.Role == TeamRole.Support &&
               (scriptSettings.Team.Support?.Enabled ?? false);
    }

    private static bool IsTeamOutputEnabled(ScriptSettings scriptSettings)
    {
        return scriptSettings.Team.Role == TeamRole.Output &&
               (scriptSettings.Team.Output?.Enabled ?? false);
    }

    private async Task<TeamWorkerTickResult?> TryTickTeamControllersAsync(
        AccountWorkerContext context,
        ScriptSettings scriptSettings,
        TeamSupportState teamSupportState,
        TeamOutputState teamOutputState,
        StationaryCombatState stationaryCombatState,
        SemiAutoCombatState semiAutoState)
    {
        if (stationaryCombatState.LootAfterKill.Active)
        {
            return null;
        }

        if (_teamSupport is not null &&
            IsTeamSupportEnabled(scriptSettings))
        {
            var supportResult = await _teamSupport
                .TickAsync(context, teamSupportState, stationaryCombatState, semiAutoState)
                .ConfigureAwait(false);
            return new TeamWorkerTickResult(
                supportResult.ShouldSkipNormalWork,
                supportResult.Delay);
        }

        if (_teamOutput is not null &&
            IsTeamOutputEnabled(scriptSettings))
        {
            var outputResult = await _teamOutput
                .TickAsync(context, teamOutputState, stationaryCombatState)
                .ConfigureAwait(false);
            return new TeamWorkerTickResult(
                outputResult.ShouldSkipNormalWork,
                outputResult.Delay);
        }

        return null;
    }

    private async Task<TimeSpan> TickNormalWorkAsync(
        AccountWorkerContext context,
        SemiAutoSkillPlan semiAutoPlan,
        SemiAutoCombatState semiAutoState,
        StationaryCombatState stationaryCombatState,
        AccountMainMode mainMode,
        bool isStationaryCombat,
        bool isPathCombat)
    {
        if (mainMode == AccountMainMode.SemiAuto)
        {
            return await _semiAuto.TickAsync(context, semiAutoPlan, semiAutoState).ConfigureAwait(false);
        }

        if (isStationaryCombat)
        {
            return await _stationaryCombat
                .TickAsync(context, semiAutoPlan, semiAutoState, stationaryCombatState)
                .ConfigureAwait(false);
        }

        if (isPathCombat)
        {
            return await _stationaryCombat
                .TickPathAsync(context, semiAutoPlan, semiAutoState, stationaryCombatState)
                .ConfigureAwait(false);
        }

        if (context.Options.PollPlayerSnapshot)
        {
            await context.Snapshots.ReadPlayerAsync().ConfigureAwait(false);
            context.RuntimeStates.ClearWarning(context.Config.AccountName);
        }

        return context.Options.TickInterval;
    }

    private readonly record struct TeamWorkerTickResult(bool ShouldSkipNormalWork, TimeSpan Delay);

    private async Task ScrollStartupMouseAsync(AccountWorkerContext context)
    {
        var sent = 0;
        for (var attempt = 1; attempt <= StartupScrollCount; attempt++)
        {
            var result = await _keyboard
                .ScrollMouseAsync(StartupScrollDelta, context.StopToken)
                .ConfigureAwait(false);
            if (result.Success)
            {
                sent++;
            }
            else
            {
                context.Logger.Warn("worker.startup.scroll_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["delta"] = StartupScrollDelta,
                    ["attempt"] = attempt,
                    ["targetCount"] = StartupScrollCount,
                    ["error"] = result.Error
                });
            }

            if (attempt < StartupScrollCount)
            {
                await Task.Delay(StartupScrollInterval, context.StopToken).ConfigureAwait(false);
            }
        }

        context.Logger.Info("worker.startup.scroll_complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["delta"] = StartupScrollDelta,
            ["count"] = StartupScrollCount,
            ["sent"] = sent,
            ["intervalMs"] = (long)StartupScrollInterval.TotalMilliseconds
        });
    }

    private async Task ReleaseStartupMovementAsync(AccountWorkerContext context)
    {
        var result = await _keyboard.KeyUpAsync("W", context.StopToken).ConfigureAwait(false);
        if (result.Success)
        {
            context.Logger.Info("worker.startup.key_up", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["key"] = "W"
            });
            return;
        }

        context.Logger.Warn("worker.startup.key_up_failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = "W",
            ["error"] = result.Error
        });
    }

    private async Task ReleaseActiveInputAsync(
        AccountWorkerContext context,
        StationaryCombatState state)
    {
        await state.ForwardInputSync.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (state.IsMovingForward)
            {
                var result = await _keyboard.KeyUpAsync("W", CancellationToken.None).ConfigureAwait(false);
                state.IsMovingForward = false;
                LogInputRelease(context, "W", "key_up", result);
            }
        }
        finally { state.ForwardInputSync.Release(); }

        if (state.IsRightMouseDown)
        {
            var result = await _keyboard.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
            state.IsRightMouseDown = false;
            LogInputRelease(context, "Right", "mouse_up", result);
        }
    }

    private static void LogInputRelease(
        AccountWorkerContext context,
        string input,
        string action,
        Core.Common.OperationResult result)
    {
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["input"] = input,
            ["action"] = action
        };

        if (result.Success)
        {
            context.Logger.Info("worker.input.release", fields);
            return;
        }

        fields["error"] = result.Error;
        context.Logger.Warn("worker.input.release_failed", fields);
    }
}
