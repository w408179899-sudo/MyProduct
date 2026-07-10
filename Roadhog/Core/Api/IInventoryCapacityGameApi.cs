using Roadhog.Core.Common;

namespace Roadhog.Core.Api;

public interface IInventoryCapacityGameApi
{
    Task<OperationResult<int>> ReadInventoryCapacityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
