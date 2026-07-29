using Roadhog.Core.Common;

namespace Roadhog.Core.Licensing;

public interface IOwnerLicenseGrantProvider
{
    Task<OperationResult<bool>> IsAuthorizedAsync(CancellationToken cancellationToken = default);
}
