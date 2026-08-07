using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogTacticsSignGameApi
{
    Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        CancellationToken cancellationToken = default);
}

public interface IRoadhogScopedTacticsSignGameApi : IRoadhogTacticsSignGameApi
{
    Task<OperationResult<TacticsSignSnapshot>> ReadTacticsSignsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
