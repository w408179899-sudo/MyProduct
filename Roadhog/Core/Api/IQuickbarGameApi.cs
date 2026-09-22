using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IQuickbarGameApi
{
    Task<OperationResult<QuickbarSnapshot>> ReadQuickbarAsync(GameApiReadContext context, CancellationToken cancellationToken = default);
}
