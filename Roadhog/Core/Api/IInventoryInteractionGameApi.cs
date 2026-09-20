using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IInventoryInteractionGameApi
{
    Task<OperationResult<InventoryInteractionSnapshot>> ReadInventoryInteractionAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
    Task<OperationResult<PersonalShopSnapshot>> ReadPersonalShopAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
    Task<OperationResult<GameUiCursorSnapshot>> ReadUiCursorAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
