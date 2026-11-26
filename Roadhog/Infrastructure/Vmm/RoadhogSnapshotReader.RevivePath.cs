using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader
{
    public async Task<PublishedGameSnapshot<PathFollowPose>> ReadRevivePathPoseAsync(CancellationToken cancellationToken = default)
    {
        using var cancellation = SnapshotReadBudget.EnterCancellation(cancellationToken);
        if (_gameApi is not IRevivePathPoseGameApi api)
        {
            var player = await ReadPlayerAsync().ConfigureAwait(false);
            return new(player.Version, PathFollowPose.FromPlayer(player.Value));
        }

        return await ReadUntilPublishedAsync(AionVmmSnapshotChannels.RevivePathPose.Name,
            stop => api.ReadRevivePathPoseAsync(_readContext, stop), 0).ConfigureAwait(false);
    }
}
