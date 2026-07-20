using Roadhog.Core.Common;

namespace Roadhog.Core.Licensing;

public interface ILicenseCredentialStore
{
    Task<OperationResult<LicenseCredential?>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        LicenseCredential credential,
        CancellationToken cancellationToken = default);
}
