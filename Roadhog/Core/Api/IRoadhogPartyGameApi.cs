using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogPartyGameApi
{
    Task<OperationResult<PartySnapshot>> ReadPartyAsync(CancellationToken cancellationToken = default);
}

public interface IRoadhogScopedPartyGameApi : IRoadhogPartyGameApi
{
    Task<OperationResult<PartySnapshot>> ReadPartyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
