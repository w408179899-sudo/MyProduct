using Roadhog.Core.Model;
using Roadhog.Core.Common;

namespace Roadhog.Core.Api;

/// <summary>
/// Optional independent reader used only by the skill-bar release mode. The
/// existing snapshot-reader contract and quickbar binding reads do not request
/// this channel. The provider owns validation, partial merges, holding the last
/// official value, and cold-start retries.
/// </summary>
public interface ISkillAvailabilitySnapshotReader
{
    Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
        long afterVersion = 0,
        CancellationToken cancellationToken = default);
}

internal interface ISkillAvailabilityGameApi
{
    Task<OperationResult<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
