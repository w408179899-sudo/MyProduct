using Roadhog.Application.Workers;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupSafetyChecker
{
    public async Task<bool> CheckSafeToReturnAsync(
        AccountWorkerContext context)
    {
        var attack = await FindAttackingTargetNameAsync(context).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(attack);
    }

    public async Task<string?> FindAttackingTargetNameAsync(
        AccountWorkerContext context)
    {
        var lockedResult = await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false);

        if (lockedResult.Value is { HasTarget: true, IsMonsterAlive: true } target &&
            (target.IsTargetingLocalPlayer || target.TargetServerObjectIdMatchesLocal))
        {
            return target.Name;
        }

        var worldResult = await context.Snapshots.ReadWorldObjectsAsync().ConfigureAwait(false);

        var attacker = worldResult.Value
            .FirstOrDefault(target => target.IsAlive && target.IsTargetingLocalPlayer);
        return attacker?.Name;
    }
}
