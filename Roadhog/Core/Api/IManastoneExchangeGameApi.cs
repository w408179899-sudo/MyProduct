using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IManastoneExchangeGameApi
{
    Task<OperationResult<ManastoneExchangeSnapshot>> ReadManastoneExchangeAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default);
}
