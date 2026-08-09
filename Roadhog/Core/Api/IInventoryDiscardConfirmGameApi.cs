using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IInventoryDiscardConfirmGameApi
{
    Task<OperationResult<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
