using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IInventoryDiscardConfirmGameApi
{
    Task<OperationResult<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
