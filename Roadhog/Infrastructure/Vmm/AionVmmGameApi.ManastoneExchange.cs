using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IManastoneExchangeGameApi
{
    public Task<OperationResult<ManastoneExchangeSnapshot>> ReadManastoneExchangeAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.ManastoneExchange,
            () => ReadInventoryInteractionCore(context, static (decoder, module) => decoder.ReadManastoneExchange(module))), cancellationToken);
}
