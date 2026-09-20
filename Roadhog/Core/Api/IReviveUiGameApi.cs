using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IReviveUiGameApi
{
    Task<OperationResult<ReviveUiSnapshot>> ReadReviveUiAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
