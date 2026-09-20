using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IAuctionHouseGameApi
{
    Task<OperationResult<AuctionHouseSnapshot>> ReadAuctionHouseAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
