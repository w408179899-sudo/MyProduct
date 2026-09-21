using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface INpcTradeGameApi
{
    Task<OperationResult<NpcTradeSnapshot>> ReadNpcTradeAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
