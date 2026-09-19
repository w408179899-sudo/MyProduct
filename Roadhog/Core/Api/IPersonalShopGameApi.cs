using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IPersonalShopGameApi
{
    Task<OperationResult<PersonalShopSnapshot>> ReadPersonalShopAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
    Task<OperationResult<PersonalShopCursorSnapshot>> ReadPersonalShopCursorAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
