using Roadhog.Core.Common;

namespace Roadhog.Core.Profiles;

public interface IScriptProfileStore
{
    Task<OperationResult<IReadOnlyList<ScriptProfileSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<ScriptProfileDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default);

    // A successful null is an absent file; invalid or unreadable documents fail.
    Task<OperationResult<ScriptProfileDocument?>> LoadOptionalAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        ScriptProfileDocument profile,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default);
}
