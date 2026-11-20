using Roadhog.Core.Accounts;

namespace Roadhog.Application.Workers;

public sealed partial class DefaultAccountWorkerLoop
{
    private async Task RefreshRevivePathMonsterKillPrioritiesAsync(AccountWorkerContext context, ScriptSettings settings)
    {
        if (_pathStore is null || settings.MainMode != AccountMainMode.CustomCombat) return;

        var pathName = context.Config.ResolveRevivePathName();
        if (pathName.Length == 0)
        {
            settings.Combat.MonsterKillPriorities = new();
            return;
        }

        var loaded = await _pathStore.LoadMonsterKillPrioritiesAsync(pathName, context.StopToken).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (!loaded.Success && settings.Combat.MonsterKillPriorities.Count > 0)
        {
            context.Logger.Warn("stationary_combat.monster_kill_priorities.load_failed", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName, ["pathName"] = pathName, ["error"] = loaded.Error });
        }
        settings.Combat.MonsterKillPriorities = loaded.Value ?? new();
    }
}
