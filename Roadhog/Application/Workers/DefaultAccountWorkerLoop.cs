using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;

namespace Roadhog.Application.Workers;

public sealed class DefaultAccountWorkerLoop : IAccountWorkerLoop
{
    private readonly IKeyboardInput _keyboard;
    private readonly SemiAutoCombatController _semiAuto;
    private readonly StationaryCombatController _stationaryCombat;

    public DefaultAccountWorkerLoop(
        IKeyboardInput keyboard,
        SemiAutoCombatController semiAuto,
        StationaryCombatController stationaryCombat)
    {
        _keyboard = keyboard;
        _semiAuto = semiAuto;
        _stationaryCombat = stationaryCombat;
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
            ["topLevelSkills"] = string.Join(" > ", semiAutoPlan.Roots.Select(root => root.Name + "[" + root.Type + "]@" + root.Key)),
            ["chainRoots"] = string.Join(" > ", semiAutoPlan.Roots.Where(root => root.Children.Count > 0).Select(root => root.Name + "@" + root.Key))
        });

        try
        {
            while (!context.StopToken.IsCancellationRequested)
            {
                context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);

                var delay = context.Options.TickInterval;
                if (context.Config.MainMode == AccountMainMode.SemiAuto)
                {
                    delay = await _semiAuto.TickAsync(context, semiAutoPlan, semiAutoState).ConfigureAwait(false);
                }
                else if (context.Config.MainMode == AccountMainMode.CustomCombat &&
                         context.Config.CombatMode == AccountCombatMode.Stationary)
                {
                    delay = await _stationaryCombat
                        .TickAsync(context, semiAutoPlan, semiAutoState, stationaryCombatState)
                        .ConfigureAwait(false);
                }
                else if (context.Options.PollPlayerSnapshot)
                {
                    var result = await context.GameApi.ReadPlayerAsync(context.StopToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        context.Logger.Warn("worker.player_poll.failed", new Dictionary<string, object?>
                        {
                            ["account"] = context.Config.AccountName,
                            ["error"] = result.Error
                        });
                    }
                }

                await Task.Delay(delay, context.StopToken).ConfigureAwait(false);
            }
        }
        finally
        {
            semiAutoState.ResetAttackKeyPressThrottle();
        }
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
}
