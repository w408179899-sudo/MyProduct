using Roadhog.Core.Model;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<IReadOnlyList<SkillSnapshot>> RefreshSkillsByIdsAsync(
        IReadOnlyCollection<uint> skillIds,
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skillIds);
        cancellationToken.ThrowIfCancellationRequested();
        var ids = skillIds.Where(id => id != 0).Distinct().ToArray();
        // An empty current bar needs no hardware read and must not become the
        // existing highest-rank/full-list refresh operation.
        if (ids.Length == 0) return Array.Empty<SkillSnapshot>();
        var snapshot = (await CreateSnapshotReader(accountName, cancellationToken)
            .ReadSkillsAsync(ids).ConfigureAwait(false)).Value;
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Info("skills.refresh_ids.ok", new Dictionary<string, object?>
        {
            ["account"] = accountName,
            ["requestedCount"] = ids.Length,
            ["count"] = snapshot.Count
        });
        return snapshot;
    }
}
