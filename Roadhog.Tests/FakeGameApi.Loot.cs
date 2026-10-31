using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

sealed partial class FakeGameApi
{
    public Func<LootInteractionSnapshot>? LootInteractionRead { get; set; }
    public Func<CancellationToken, Task<OperationResult<LootInteractionSnapshot>>>? LootInteractionCapture { get; set; }
    private int _lootInteractionReads;
    public int LootInteractionReads => Volatile.Read(ref _lootInteractionReads);

    public Task<OperationResult<LootInteractionSnapshot>> ReadLootInteractionAsync(GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _lootInteractionReads);
        if (LootInteractionCapture is { } capture) return capture(cancellationToken);
        if (LootInteractionRead is { } read) return Task.FromResult(OperationResult<LootInteractionSnapshot>.Ok(read()));
        // Existing combat fixtures model a successful nearby pickup. New action
        // tests supply input-coupled acknowledgements (including swallowed keys).
        var nearby = TargetPosition is { } target && Player.Position is { } player &&
            Math.Sqrt(Math.Pow(target.X-player.X,2)+Math.Pow(target.Y-player.Y,2)) <= 3;
        return Task.FromResult(OperationResult<LootInteractionSnapshot>.Ok(
            count > 1 && TargetCurrentHp == 0 && TargetLootableRaw != 0 && nearby
                ? new(TargetOwnServerObjectId != 0 ? TargetOwnServerObjectId : TargetEntityId, false, false) : LootInteractionSnapshot.Closed));
    }
}
