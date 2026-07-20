namespace Roadhog.Core.Licensing;

public sealed record LicenseApiResult(
    bool Success,
    bool IsTransient,
    string? ErrorCode,
    long? LicenseId,
    long? ActivationGeneration,
    string? Token,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? LicenseExpiresAt,
    DateTimeOffset? ServerTime)
{
    public static LicenseApiResult Failure(string errorCode, bool isTransient = false)
    {
        return new LicenseApiResult(
            false,
            isTransient,
            errorCode,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
