using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IReviveUiGameApi
{
    public Task<OperationResult<ReviveUiSnapshot>> ReadReviveUiAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.ReviveUi,
            () => ReadInventoryInteractionCore(context, static (decoder, gameBase) => decoder.ReadRevive(gameBase))), cancellationToken);
}
