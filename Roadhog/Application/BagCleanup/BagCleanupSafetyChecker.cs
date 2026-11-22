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
        AccountWorkerContext context, uint? merchantServerObjectId = null)
    {
        var locked = (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;
        var world = (await context.Snapshots.ReadWorldObjectsAsync().ConfigureAwait(false)).Value;

        // Merchants can share the raw lock type with monsters. Use the confirmed
        // trade identity and the world snapshot's NPC classification.
        if (locked.IsMonsterAlive && locked.ServerObjectId != merchantServerObjectId &&
            (locked.IsTargetingLocalPlayer || locked.TargetServerObjectIdMatchesLocal) &&
            !world.Any(target => target.ServerObjectId != 0 && target.ServerObjectId == locked.ServerObjectId &&
                string.Equals(target.ObjectKind, "npc", StringComparison.OrdinalIgnoreCase)))
        {
            return locked.Name;
        }

        var attacker = world.FirstOrDefault(target => target.IsAlive && target.IsTargetingLocalPlayer &&
            target.ServerObjectId != merchantServerObjectId &&
            string.Equals(target.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase));
        return attacker?.Name;
    }
}
