using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader
{
    public Task<PublishedGameSnapshot<ManastoneExchangeSnapshot>> ReadManastoneExchangeAsync(long afterVersion = 0) =>
        ReadUntilPublishedAsync("manastone_exchange",
            stop => _gameApi is IManastoneExchangeGameApi api ? api.ReadManastoneExchangeAsync(_readContext, stop)
                : Missing<ManastoneExchangeSnapshot>("Manastone exchange channel is unavailable."), afterVersion);
}
