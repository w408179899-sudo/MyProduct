namespace Roadhog.Application.Licensing;

public enum LicenseRuntimeStateKind
{
    Uninitialized,
    Checking,
    ActivationRequired,
    Authorized,
    OfflineGrace,
    Denied,
    Unavailable
}

public sealed record LicenseRuntimeState(
    LicenseRuntimeStateKind Kind,
    string? ErrorCode = null,
    long? LicenseId = null,
    DateTimeOffset? LicenseExpiresAt = null,
    DateTimeOffset? LastVerifiedAt = null)
{
    public bool IsAuthorized => Kind == LicenseRuntimeStateKind.Authorized;

    public bool RequiresStop => Kind == LicenseRuntimeStateKind.Denied;
}

public sealed class LicenseStateChangedEventArgs : EventArgs
{
    public LicenseStateChangedEventArgs(LicenseRuntimeState state)
    {
        State = state;
    }

    public LicenseRuntimeState State { get; }
}
