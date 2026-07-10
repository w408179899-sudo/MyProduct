using Roadhog.Application.Workers;
using Roadhog.Core.Common;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupSafetyChecker
{
    public async Task<OperationResult> CheckSafeToReturnAsync(
        AccountWorkerContext context)
    {
        var attack = await FindAttackingTargetNameAsync(context).ConfigureAwait(false);
        if (!attack.Success)
        {
            return OperationResult.Fail(attack.Error ?? "Attack status read failed.");
        }

        return string.IsNullOrWhiteSpace(attack.Value)
            ? OperationResult.Ok()
            : OperationResult.Fail("Nearby target is still attacking local side: " + attack.Value);
    }

    public async Task<OperationResult<string?>> FindAttackingTargetNameAsync(
        AccountWorkerContext context)
    {
        var lockedResult = await BagCleanupGameApi.ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!lockedResult.Success)
        {
            return OperationResult<string?>.Fail("Locked target read failed: " + lockedResult.Error);
        }

        if (lockedResult.Value is { HasTarget: true, IsMonsterAlive: true } target &&
            (target.IsTargetingLocalPlayer || target.TargetServerObjectIdMatchesLocal))
        {
            return OperationResult<string?>.Ok(target.Name);
        }

        var worldResult = await BagCleanupGameApi.ReadWorldObjectsAsync(context).ConfigureAwait(false);
        if (!worldResult.Success)
        {
            return OperationResult<string?>.Fail("World object read failed: " + worldResult.Error);
        }

        var attacker = worldResult.Value?
            .FirstOrDefault(target => target.IsAlive && target.IsTargetingLocalPlayer);
        return OperationResult<string?>.Ok(attacker?.Name);
    }
}
