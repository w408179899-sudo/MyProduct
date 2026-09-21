using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : INpcTradeGameApi
{
    public Task<OperationResult<NpcTradeSnapshot>> ReadNpcTradeAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.NpcTrade,
            () => ReadInventoryInteractionCore(context, static (decoder, module) => decoder.ReadNpcTrade(module))), cancellationToken);
}
