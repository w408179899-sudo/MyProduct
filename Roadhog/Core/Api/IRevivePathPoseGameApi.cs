using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRevivePathPoseGameApi
{
    Task<OperationResult<PathFollowPose>> ReadRevivePathPoseAsync(GameApiReadContext context, CancellationToken cancellationToken);
}
