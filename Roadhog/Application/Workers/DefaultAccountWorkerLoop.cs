using Roadhog.Application.JumpAssist;
using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;

namespace Roadhog.Application.Workers;

public sealed class DefaultAccountWorkerLoop : IAccountWorkerLoop
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

    public DefaultAccountWorkerLoop(
        IKeyboardInput keyboard,
        SemiAutoCombatController semiAuto,
        StationaryCombatController stationaryCombat,
        TeamSupportController? teamSupport = null,
        TeamOutputController? teamOutput = null,
        FixedChannelController? fixedChannel = null)
    {
        _keyboard = keyboard;
        _semiAuto = semiAuto;
        _stationaryCombat = stationaryCombat;
        _teamSupport = teamSupport;
        _teamOutput = teamOutput;
        _fixedChannel = fixedChannel;
    }

    public async Task RunAsync(AccountWorkerContext context)
    {
        context.Logger.Info("worker.loop.enter", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["mode"] = context.Config.MainMode.ToString(),
            ["hardwareKey"] = context.Config.HardwareKey,
            ["vmmDevice"] = context.Config.VmmDeviceName
        });
        await ScrollStartupMouseAsync(context).ConfigureAwait(false);
        await ReleaseStartupMovementAsync(context).ConfigureAwait(false);

        var scriptSettings = context.Config.ScriptSettings ?? new ScriptSettings
        {
            ProfileName = context.Config.ProfileName,
            MainMode = context.Config.MainMode,
            CombatMode = context.Config.CombatMode
        };
        var semiAutoPlan = SemiAutoSkillPlan.FromSettings(scriptSettings.Skills);
        var semiAutoState = new SemiAutoCombatState();
        var stationaryCombatState = new StationaryCombatState();
        var teamSupportState = new TeamSupportState();
        var teamOutputState = new TeamOutputState();
        var fixedChannelState = new FixedChannelState();
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
            ["mode"] = scriptSettings.Skills.Mode.ToString(),
            ["topLevelSkillCount"] = semiAutoPlan.Roots.Count,
            ["chainRootCount"] = semiAutoPlan.Roots.Count(root => root.Children.Count > 0),
            ["triggerPrefixCount"] = semiAutoPlan.TriggerPrefixRoots.Count,
            ["skillReadIdCount"] = semiAutoPlan.SkillReadIds.Count,
            ["requiresFullSkillRead"] = semiAutoPlan.RequiresFullSkillRead,
            ["hasExecutableSkills"] = semiAutoPlan.HasExecutableSkills,
            ["spiritmasterAutoLogic"] = semiAutoPlan.UsesSpiritmasterAutoLogic,
            ["topLevelSkills"] = string.Join(" > ", semiAutoPlan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key)),
            ["chainRoots"] = string.Join(" > ", semiAutoPlan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key))
        });

        try
        {
            while (!context.StopToken.IsCancellationRequested)
            {
                context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);

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
                            () => _stationaryCombat.SuspendForFixedChannelCorrectionAsync(
                                context,
                                semiAutoState,
                                stationaryCombatState))
                        .ConfigureAwait(false);
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
                    else if (await _semiAuto
                                 .EnsureSpiritmasterPetAsync(
                                 context,
                                 semiAutoPlan,
                                 semiAutoState,
                                 beforeSummonKeyPress: () => ReleaseActiveInputAsync(context, stationaryCombatState))
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
        }
        finally
        {
            semiAutoState.ResetAttackKeyPressThrottle();
            if (jumpAssist is not null)
            {
                await jumpAssist.DisposeAsync().ConfigureAwait(false);
            }

            await ReleaseActiveInputAsync(context, stationaryCombatState).ConfigureAwait(false);
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
            var result = await context.GameApi.ReadPlayerAsync(context.StopToken).ConfigureAwait(false);
            if (!result.Success)
            {
                context.RuntimeStates.MarkWarning(
                    context.Config.AccountName,
                    Roadhog.Application.RuntimeWarningText.FromPlayerReadFailure(result.Error));
                context.Logger.Warn("worker.player_poll.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = result.Error
                });
            }
            else
            {
                context.RuntimeStates.ClearWarning(context.Config.AccountName);
            }
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
        if (state.IsMovingForward)
        {
            var result = await _keyboard.KeyUpAsync("W", CancellationToken.None).ConfigureAwait(false);
            state.IsMovingForward = false;
            LogInputRelease(context, "W", "key_up", result);
        }

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
