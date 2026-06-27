using Roadhog.Core.Common;

namespace Roadhog.Core.Paths;

public interface ISharedPathStore
{
    Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<SharedPathDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        SharedPathDocument path,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default);
}
