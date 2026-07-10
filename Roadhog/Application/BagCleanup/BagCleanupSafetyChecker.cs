using Roadhog.Application.Workers;
using Roadhog.Core.Common;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupSafetyChecker
{
    public async Task<OperationResult> CheckSafeToReturnAsync(
        AccountWorkerContext context)
    {
        var lockedResult = await BagCleanupGameApi.ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!lockedResult.Success)
        {
            return OperationResult.Fail("Locked target read failed: " + lockedResult.Error);
        }

        if (lockedResult.Value is { HasTarget: true, IsMonsterAlive: true } target &&
            (target.IsTargetingLocalPlayer || target.TargetServerObjectIdMatchesLocal))
        {
            return OperationResult.Fail("Locked monster is still targeting local side: " + target.Name);
        }

        var worldResult = await BagCleanupGameApi.ReadWorldObjectsAsync(context).ConfigureAwait(false);
        if (!worldResult.Success)
        {
            return OperationResult.Fail("World object read failed: " + worldResult.Error);
        }

        var attacker = worldResult.Value?
            .FirstOrDefault(target => target.IsAlive && target.IsTargetingLocalPlayer);
        return attacker is null
            ? OperationResult.Ok()
            : OperationResult.Fail("Nearby target is still attacking local side: " + attacker.Name);
    }
}
