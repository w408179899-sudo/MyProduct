using Roadhog.Core.Diagnostics;
using Roadhog.Core.Licensing;

namespace Roadhog.Application.Licensing;

public sealed class LicenseCoordinator : ILicenseRuntimeGate, IAsyncDisposable
{
    private static readonly HashSet<string> ReplaceablePendingCredentialErrors = new(StringComparer.Ordinal)
    {
        "INVALID_CDKEY_FORMAT",
        "LICENSE_NOT_FOUND",
        "LICENSE_NOT_ACTIVATED"
    };

    private readonly ILicenseApiClient _apiClient;
    private readonly ILicenseCredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly IRoadhogLogger _logger;
    private readonly LicenseCoordinatorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateSyncRoot = new();

    private LicenseRuntimeState _state = new(LicenseRuntimeStateKind.Uninitialized);
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;
    private DateTimeOffset? _lastVerifiedAt;
    private bool _disposed;

    public LicenseCoordinator(
        ILicenseApiClient apiClient,
        ILicenseCredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        IRoadhogLogger logger,
        LicenseCoordinatorOptions options,
        TimeProvider? timeProvider = null)
    {
        _apiClient = apiClient;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;
        _logger = logger;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Heartbeat interval must be positive.");
        }

        if (_options.HeartbeatRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Heartbeat retry count cannot be negative.");
        }

        if (_options.HeartbeatRetryCount > 0 && _options.HeartbeatRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Heartbeat retry delay must be positive.");
        }
    }

    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    public LicenseRuntimeState State
    {
        get
        {
            lock (_stateSyncRoot)
            {
                return _state;
            }
        }
    }

    public bool IsAuthorized => State.IsAuthorized;

    public async Task<LicenseRuntimeState> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(new LicenseRuntimeState(LicenseRuntimeStateKind.Checking));

            var loadResult = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!loadResult.Success)
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "LOCAL_CREDENTIAL_READ_FAILED"));
            }

            var credential = loadResult.Value;
            if (credential is null)
            {
                return SetState(new LicenseRuntimeState(LicenseRuntimeStateKind.ActivationRequired));
            }

            if (!credential.Validate(out _))
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "LOCAL_CREDENTIAL_INVALID"));
            }

            var deviceResult = _deviceIdentityProvider.GetDeviceHash();
            if (!deviceResult.Success || string.IsNullOrWhiteSpace(deviceResult.Value))
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "DEVICE_IDENTITY_UNAVAILABLE"));
            }

            var result = await _apiClient.LoginAsync(
                    credential,
                    deviceResult.Value,
                    _options.ClientVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                if (!credential.Activated)
                {
                    var saveResult = await _credentialStore
                        .SaveAsync(credential.MarkActivated(), cancellationToken)
                        .ConfigureAwait(false);
                    if (!saveResult.Success)
                    {
                        _logger.Warn("license.credential.mark_activated_failed", new Dictionary<string, object?>
                        {
                            ["error"] = saveResult.Error
                        });
                    }
                }

                return AcceptSession(result, "login");
            }

            if (result.IsTransient)
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Unavailable,
                    result.ErrorCode ?? "LICENSE_SERVER_UNAVAILABLE"));
            }

            var kind = ReplaceablePendingCredentialErrors.Contains(result.ErrorCode ?? string.Empty)
                ? LicenseRuntimeStateKind.ActivationRequired
                : LicenseRuntimeStateKind.Denied;
            return SetState(new LicenseRuntimeState(kind, result.ErrorCode ?? "LOGIN_FAILED"));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<LicenseRuntimeState> ActivateAsync(
        string cdkey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedCdkey = LicenseCredential.NormalizeCdkey(cdkey);
            if (!LicenseCredential.IsValidCdkey(normalizedCdkey))
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.ActivationRequired,
                    "INVALID_CDKEY_FORMAT"));
            }

            var stateBeforeActivation = State;
            SetState(new LicenseRuntimeState(LicenseRuntimeStateKind.Checking));

            var loadResult = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!loadResult.Success)
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "LOCAL_CREDENTIAL_READ_FAILED"));
            }

            var credential = loadResult.Value;
            if (credential is not null &&
                !string.Equals(credential.Cdkey, normalizedCdkey, StringComparison.Ordinal))
            {
                var previousError = stateBeforeActivation.ErrorCode;
                if (credential.Activated ||
                    !ReplaceablePendingCredentialErrors.Contains(previousError ?? string.Empty))
                {
                    return SetState(new LicenseRuntimeState(
                        LicenseRuntimeStateKind.ActivationRequired,
                        "LOCAL_LICENSE_ALREADY_CONFIGURED"));
                }

                credential = null;
            }

            credential ??= LicenseCredential.Create(normalizedCdkey);
            if (!credential.Validate(out _))
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "LOCAL_CREDENTIAL_INVALID"));
            }

            var saveResult = await _credentialStore.SaveAsync(credential, cancellationToken).ConfigureAwait(false);
            if (!saveResult.Success)
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "LOCAL_CREDENTIAL_WRITE_FAILED"));
            }

            var deviceResult = _deviceIdentityProvider.GetDeviceHash();
            if (!deviceResult.Success || string.IsNullOrWhiteSpace(deviceResult.Value))
            {
                return SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    "DEVICE_IDENTITY_UNAVAILABLE"));
            }

            var result = await _apiClient.ActivateAsync(
                    credential,
                    deviceResult.Value,
                    _options.ClientVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                var kind = result.IsTransient
                    ? LicenseRuntimeStateKind.Unavailable
                    : LicenseRuntimeStateKind.ActivationRequired;
                return SetState(new LicenseRuntimeState(
                    kind,
                    result.ErrorCode ?? "ACTIVATION_FAILED"));
            }

            var activatedCredential = credential.MarkActivated();
            var activatedSaveResult = await _credentialStore
                .SaveAsync(activatedCredential, cancellationToken)
                .ConfigureAwait(false);
            if (!activatedSaveResult.Success)
            {
                _logger.Warn("license.credential.mark_activated_failed", new Dictionary<string, object?>
                {
                    ["error"] = activatedSaveResult.Error
                });
            }

            return AcceptSession(result, "activate");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _heartbeatCancellation?.Cancel();

        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _heartbeatCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        _operationGate.Dispose();
        _apiClient.Dispose();
    }

    private LicenseRuntimeState AcceptSession(LicenseApiResult result, string source)
    {
        if (string.IsNullOrWhiteSpace(result.Token) || result.LicenseId is null)
        {
            return SetState(new LicenseRuntimeState(
                LicenseRuntimeStateKind.Unavailable,
                "INVALID_SERVER_RESPONSE"));
        }

        var now = _timeProvider.GetUtcNow();
        _lastVerifiedAt = now;
        var state = SetState(new LicenseRuntimeState(
            LicenseRuntimeStateKind.Authorized,
            null,
            result.LicenseId,
            result.LicenseExpiresAt,
            now));

        _logger.Info("license.session.authorized", new Dictionary<string, object?>
        {
            ["source"] = source,
            ["licenseId"] = result.LicenseId,
            ["licenseExpiresAt"] = result.LicenseExpiresAt
        });

        StartHeartbeat(result.Token);
        return state;
    }

    private void StartHeartbeat(string token)
    {
        _heartbeatCancellation?.Cancel();
        _heartbeatCancellation?.Dispose();
        _heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _heartbeatTask = RunHeartbeatAsync(token, _heartbeatCancellation.Token);
    }

    private async Task RunHeartbeatAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                var result = await SendHeartbeatWithRetriesAsync(token, cancellationToken).ConfigureAwait(false);
                var now = _timeProvider.GetUtcNow();

                if (result.Success)
                {
                    _lastVerifiedAt = now;
                    var current = State;
                    SetState(new LicenseRuntimeState(
                        LicenseRuntimeStateKind.Authorized,
                        null,
                        result.LicenseId ?? current.LicenseId,
                        result.LicenseExpiresAt ?? current.LicenseExpiresAt,
                        now));
                    continue;
                }

                _logger.Warn("license.heartbeat.denied", new Dictionary<string, object?>
                {
                    ["errorCode"] = result.ErrorCode,
                    ["transient"] = result.IsTransient
                });
                SetState(new LicenseRuntimeState(
                    LicenseRuntimeStateKind.Denied,
                    result.ErrorCode ?? "HEARTBEAT_DENIED",
                    State.LicenseId,
                    State.LicenseExpiresAt,
                    _lastVerifiedAt));
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("license.heartbeat.exception", ex);
            SetState(new LicenseRuntimeState(
                LicenseRuntimeStateKind.Denied,
                "HEARTBEAT_INTERNAL_ERROR",
                State.LicenseId,
                State.LicenseExpiresAt,
                _lastVerifiedAt));
        }
    }

    private async Task<LicenseApiResult> SendHeartbeatWithRetriesAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var result = await _apiClient.HeartbeatAsync(token, cancellationToken).ConfigureAwait(false);
        for (var retry = 1;
             !result.Success && result.IsTransient && retry <= _options.HeartbeatRetryCount;
             retry++)
        {
            _logger.Warn("license.heartbeat.retry", new Dictionary<string, object?>
            {
                ["attempt"] = retry,
                ["maxAttempts"] = _options.HeartbeatRetryCount,
                ["delaySeconds"] = _options.HeartbeatRetryDelay.TotalSeconds,
                ["errorCode"] = result.ErrorCode
            });
            await Task.Delay(_options.HeartbeatRetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            result = await _apiClient.HeartbeatAsync(token, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private LicenseRuntimeState SetState(LicenseRuntimeState state)
    {
        EventHandler<LicenseStateChangedEventArgs>? handler;
        lock (_stateSyncRoot)
        {
            if (_state == state)
            {
                return _state;
            }

            _state = state;
            handler = StateChanged;
        }

        handler?.Invoke(this, new LicenseStateChangedEventArgs(state));
        return state;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
