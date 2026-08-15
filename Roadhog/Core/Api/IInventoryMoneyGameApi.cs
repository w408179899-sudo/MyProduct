using Roadhog.Core.Common;

namespace Roadhog.Core.Api;

internal interface IInventoryMoneyGameApi
{
    Task<OperationResult<ulong>> ReadInventoryMoneyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
