using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface ILootInteractionGameApi
{
    Task<OperationResult<LootInteractionSnapshot>> ReadLootInteractionAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default);
}
