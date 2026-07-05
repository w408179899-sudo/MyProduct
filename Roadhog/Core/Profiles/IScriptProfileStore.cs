using Roadhog.Core.Common;

namespace Roadhog.Core.Profiles;

public interface IScriptProfileStore
{
    Task<OperationResult<IReadOnlyList<ScriptProfileSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<ScriptProfileDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        ScriptProfileDocument profile,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default);
}
