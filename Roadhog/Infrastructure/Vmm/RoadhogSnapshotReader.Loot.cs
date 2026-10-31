using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader
{
    public async Task<PublishedGameSnapshot<LootInteractionSnapshot>> ReadLootInteractionAsync(
        CancellationToken cancellationToken = default)
    {
        // The short-lived action observer owns its deadline, independently of
        // the foreground tick budget. Cold-start retry remains in the provider.
        using var cancellation = SnapshotReadBudget.EnterCancellation(cancellationToken);
        using var budget = SnapshotReadBudget.Enter(Timeout.InfiniteTimeSpan);
        return await ReadUntilPublishedAsync("loot_interaction",
            stop => _gameApi is ILootInteractionGameApi api
                ? api.ReadLootInteractionAsync(_readContext, stop)
                : Missing<LootInteractionSnapshot>("Loot interaction provider is not installed."), 0).ConfigureAwait(false);
    }
}
