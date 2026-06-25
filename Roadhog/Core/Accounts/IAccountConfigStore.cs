using Roadhog.Core.Common;

namespace Roadhog.Core.Accounts;

public interface IAccountConfigStore
{
    Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default);

    Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default);
}
