using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.Workers;

public sealed class DefaultAccountWorkerLoop : IAccountWorkerLoop
{
    private readonly SemiAutoCombatController _semiAuto;

    public DefaultAccountWorkerLoop(SemiAutoCombatController semiAuto)
    {
        _semiAuto = semiAuto;
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

        var scriptSettings = context.Config.ScriptSettings ?? new ScriptSettings
        {
            ProfileName = context.Config.ProfileName,
            MainMode = context.Config.MainMode,
            CombatMode = context.Config.CombatMode
        };
        var semiAutoPlan = SemiAutoSkillPlan.FromSettings(scriptSettings.Skills);
        var semiAutoState = new SemiAutoCombatState();
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

        while (!context.StopToken.IsCancellationRequested)
        {
            context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);

            var delay = context.Options.TickInterval;
            if (context.Config.MainMode == AccountMainMode.SemiAuto)
            {
                delay = await _semiAuto.TickAsync(context, semiAutoPlan, semiAutoState).ConfigureAwait(false);
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
}
