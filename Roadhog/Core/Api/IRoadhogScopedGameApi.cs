using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogScopedGameApi : IRoadhogGameApi
{
    Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
