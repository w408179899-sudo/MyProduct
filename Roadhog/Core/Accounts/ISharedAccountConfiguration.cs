using Roadhog.Core.Common;

namespace Roadhog.Core.Accounts;

public interface ISharedAccountConfiguration : IBagCleanupNameListStore
{
    string Region { get; set; }
    Task<OperationResult<List<string>>> LoadMonsterFiltersAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveMonsterFiltersAsync(IReadOnlyList<string> before, IReadOnlyList<string> after,
        CancellationToken cancellationToken = default);
}
