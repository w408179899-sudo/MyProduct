using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader : ISkillAvailabilitySnapshotReader
{
    public async Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
        long afterVersion = 0,
        CancellationToken cancellationToken = default)
    {
        // The new mode can cancel its cold-start wait without cancelling the
        // worker or changing any existing reader's cancellation behavior.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopToken, cancellationToken);
        var stop = linked.Token;
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            OperationResult<SkillAvailabilitySnapshot> result;
            try
            {
                result = _gameApi is ISkillAvailabilityGameApi api
                    ? await api.ReadSkillAvailabilityAsync(_readContext, stop).ConfigureAwait(false)
                    : OperationResult<SkillAvailabilitySnapshot>.Fail("Skill availability provider is not installed.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                LogReadFault("skill_availability", ex.Message);
                await Task.Delay(RetryDelay, stop).ConfigureAwait(false);
                continue;
            }
            stop.ThrowIfCancellationRequested();
            if (result.Success && result.Value is { } value)
            {
                var version = AdvanceVersion("skill_availability");
                if (version > afterVersion) return new(version, value);
            }
            else LogReadFault("skill_availability", result.Error);
            await Task.Delay(RetryDelay, stop).ConfigureAwait(false);
        }
    }
}
