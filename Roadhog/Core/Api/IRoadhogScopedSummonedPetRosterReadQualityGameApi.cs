using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

/// <summary>
/// Optional capability for consumers that must distinguish an explicitly
/// absent summoned pet from a partial or failed external-memory capture.
/// </summary>
public interface IRoadhogScopedSummonedPetRosterReadQualityGameApi
{
    Task<SummonedPetRosterReadResult> ReadSummonedPetRosterWithQualityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
