using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRoadhogPartyGameApi
{
    Task<OperationResult<PartySnapshot>> ReadPartyAsync(CancellationToken cancellationToken = default);
}

internal interface IRoadhogScopedPartyGameApi : IRoadhogPartyGameApi
{
    Task<OperationResult<PartySnapshot>> ReadPartyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
