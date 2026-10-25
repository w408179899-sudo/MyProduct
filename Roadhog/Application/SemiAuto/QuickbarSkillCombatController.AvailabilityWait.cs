using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class QuickbarSkillCombatController
{
    public static readonly TimeSpan AvailabilityWaitLimit = TimeSpan.FromSeconds(2);

    private sealed class AvailabilityWaitExpiredException : Exception;

    // Bound each read, including final input guards and attack-weave reads.
    // Await cancellation completion so no abandoned capture overlaps the next tick.
    private sealed class BoundedAvailabilityReader(ISkillAvailabilitySnapshotReader inner)
        : ISkillAvailabilitySnapshotReader
    {
        public async Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(AvailabilityWaitLimit);
            try
            {
                var result = await inner.ReadSkillAvailabilityAsync(afterVersion, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new AvailabilityWaitExpiredException();
            }
        }
    }
}
