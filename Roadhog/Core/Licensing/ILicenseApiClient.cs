namespace Roadhog.Core.Licensing;

public interface ILicenseApiClient : IDisposable
{
    Task<LicenseApiResult> ActivateAsync(
        LicenseCredential credential,
        string deviceHash,
        string clientVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseApiResult> LoginAsync(
        LicenseCredential credential,
        string deviceHash,
        string clientVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseApiResult> HeartbeatAsync(
        string token,
        CancellationToken cancellationToken = default);
}
