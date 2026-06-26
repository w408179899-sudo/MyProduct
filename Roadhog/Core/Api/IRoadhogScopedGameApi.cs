using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogScopedGameApi : IRoadhogGameApi
{
    Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
