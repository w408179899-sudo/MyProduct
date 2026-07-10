using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IInventoryWindowGameApi
{
    Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken = default)
    {
        return ReadInventoryWindowAsync(context, cancellationToken);
    }
}
