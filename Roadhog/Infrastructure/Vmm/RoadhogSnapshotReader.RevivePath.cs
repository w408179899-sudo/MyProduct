using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader
{
    public async Task<PathFollowPose> ReadRevivePathPoseAsync(CancellationToken cancellationToken = default)
    {
        using var cancellation = SnapshotReadBudget.EnterCancellation(cancellationToken);
        if (_gameApi is not IRevivePathPoseGameApi api)
        {
            var player = await ReadPlayerAsync().ConfigureAwait(false);
            return PathFollowPose.FromPlayer(player.Value);
        }

        var published = await ReadUntilPublishedAsync(AionVmmSnapshotChannels.RevivePathPose.Name,
            stop => api.ReadRevivePathPoseAsync(_readContext, stop), 0).ConfigureAwait(false);
        return published.Value;
    }
}
