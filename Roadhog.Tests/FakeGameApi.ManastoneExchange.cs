using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

sealed partial class FakeGameApi : IManastoneExchangeGameApi
{
    public Func<ManastoneExchangeSnapshot>? ManastoneExchangeRead { get; set; }

    public Task<OperationResult<ManastoneExchangeSnapshot>> ReadManastoneExchangeAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<ManastoneExchangeSnapshot>.Ok(ManastoneExchangeRead!()));
    }
}
