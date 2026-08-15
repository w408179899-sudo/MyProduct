using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

/// <summary>
/// Optional capability for callers that need structural traversal completeness
/// plus per-object field validity without changing the legacy API.
/// </summary>
internal interface IRoadhogScopedWorldObjectReadQualityGameApi
{
    Task<WorldObjectReadResult> ReadWorldObjectsWithQualityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
