using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRoadhogChannelGameApi
{
    Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        CancellationToken cancellationToken = default);
}

internal interface IRoadhogScopedChannelGameApi : IRoadhogChannelGameApi
{
    Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
