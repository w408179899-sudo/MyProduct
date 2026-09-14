using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IChannelTransitionGameApi
{
    Task<OperationResult<ChannelTransitionSnapshot>> ReadChannelTransitionAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default);
}
