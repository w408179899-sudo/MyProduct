using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRoadhogTacticsSignGameApi
{
    Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        CancellationToken cancellationToken = default);
}

internal interface IRoadhogScopedTacticsSignGameApi : IRoadhogTacticsSignGameApi
{
    Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
