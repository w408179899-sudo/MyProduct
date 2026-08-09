using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogChannelGameApi
{
    Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        CancellationToken cancellationToken = default);
}

public interface IRoadhogScopedChannelGameApi : IRoadhogChannelGameApi
{
    Task<OperationResult<ChannelSnapshot>> ReadChannelAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
