using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IChannelSwitchUiGameApi
{
    Task<OperationResult<ChannelSwitchUiSnapshot>> ReadChannelSwitchUiAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default);
}
