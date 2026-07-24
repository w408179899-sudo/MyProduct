using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;

namespace Roadhog.Application.Workers;

public sealed class DefaultAccountWorkerLoop : IAccountWorkerLoop
{
    private readonly IKeyboardInput _keyboard;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly StationaryCombatController _stationaryCombat;
    private readonly TeamSupportController? _teamSupport;
    private readonly TeamOutputController? _teamOutput;

    public DefaultAccountWorkerLoop(
        IKeyboardInput keyboard,
        SemiAutoCombatController semiAuto,
        StationaryCombatController stationaryCombat,
        TeamSupportController? teamSupport = null,
        TeamOutputController? teamOutput = null)
    {
        _keyboard = keyboard;
        _semiAuto = semiAuto;
        _stationaryCombat = stationaryCombat;
        _teamSupport = teamSupport;
        _teamOutput = teamOutput;
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
                    delay = lifeGuardDelay.Value;
                    if (stationaryCombatState.DeathRecovery.RevivePathLeaderSiphonActive)
                    {
                        var normalWorkBlocked = true;
                        var teamResult = await TryTickTeamControllersAsync(
                                context,
                                scriptSettings,
                                teamSupportState,
                                teamOutputState,
                                stationaryCombatState)
                            .ConfigureAwait(false);
                        if (teamResult.HasValue)
                        {
                            delay = teamResult.Value.Delay;
                            normalWorkBlocked = teamResult.Value.ShouldSkipNormalWork;
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
                            stationaryCombatState)
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

                await Task.Delay(delay, context.StopToken).ConfigureAwait(false);
            }
        }
        finally
        {
            semiAutoState.ResetAttackKeyPressThrottle();
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
        StationaryCombatState stationaryCombatState)
    {
        if (stationaryCombatState.LootAfterKill.Active)
        {
            return null;
        }

        if (_teamSupport is not null &&
            IsTeamSupportEnabled(scriptSettings))
        {
            var supportResult = await _teamSupport
                .TickAsync(context, teamSupportState, stationaryCombatState)
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
