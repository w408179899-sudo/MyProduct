using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Application.Licensing;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Licensing;
using Roadhog.Infrastructure.Licensing;

internal static class LicenseTests
{
    public static async Task TestDpapiCredentialStoreRoundTripAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-license-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "license.dat");
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789");
        try
        {
            var store = new DpapiLicenseCredentialStore(path);
            var save = await store.SaveAsync(credential).ConfigureAwait(false);
            Assert(!save.Success, "DPAPI credential save should succeed");

            var protectedText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            Assert(protectedText.Contains(credential.Cdkey, StringComparison.Ordinal), "protected credential must not contain plaintext CDKEY");
            Assert(protectedText.Contains(credential.InstallSecret, StringComparison.Ordinal), "protected credential must not contain plaintext install secret");

            var load = await store.LoadAsync().ConfigureAwait(false);
            Assert(!load.Success || load.Value is null, "DPAPI credential load should succeed");
            AssertEqual(credential, load.Value!, "DPAPI credential round trip");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public static async Task TestSignedOwnerLicenseGrantAuthorizesMatchingDeviceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-owner-license-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "owner-license.json");
        var deviceHash = new string('a', 64);
        try
        {
            Directory.CreateDirectory(directory);
            using var signer = new ECDsaCng(256);
            var publicKeyBlob = signer.Key.Export(CngKeyBlobFormat.EccPublicBlob);
            var payload = Encoding.UTF8.GetBytes("Roadhog.OwnerLicenseGrant.v1|" + deviceHash);
            var signature = signer.SignData(payload, HashAlgorithmName.SHA256);
            var json = JsonSerializer.Serialize(new
            {
                version = 1,
                deviceHash,
                signature = Convert.ToBase64String(signature)
            });
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

            var provider = new SignedOwnerLicenseGrantProvider(
                path,
                new FixedDeviceIdentityProvider(deviceHash),
                Convert.ToBase64String(publicKeyBlob));
            var authorized = await provider.IsAuthorizedAsync().ConfigureAwait(false);
            Assert(!authorized.Success || authorized.Value != true, "matching signed owner grant should authorize");

            var mismatchedProvider = new SignedOwnerLicenseGrantProvider(
                path,
                new FixedDeviceIdentityProvider(new string('b', 64)),
                Convert.ToBase64String(publicKeyBlob));
            var mismatched = await mismatchedProvider.IsAuthorizedAsync().ConfigureAwait(false);
            Assert(mismatched.Success, "owner grant copied to another device must fail validation");

            var missingProvider = new SignedOwnerLicenseGrantProvider(
                path + ".missing",
                new FixedDeviceIdentityProvider(deviceHash),
                Convert.ToBase64String(publicKeyBlob));
            var missing = await missingProvider.IsAuthorizedAsync().ConfigureAwait(false);
            Assert(!missing.Success || missing.Value != false, "missing owner grant should preserve normal licensing");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public static async Task TestActivationPersistsCredentialBeforeRequestAsync()
    {
        const string cdkey = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789";
        var store = new RecordingCredentialStore();
        var api = new RecordingLicenseApiClient
        {
            ActivateHandler = (credential, _, _, _) =>
            {
                Assert(store.Credential is null, "credential must be saved before activation request");
                Assert(store.Credential!.Activated, "pre-activation credential should remain pending");
                AssertEqual(cdkey, credential.Cdkey, "activation CDKEY");
                return Task.FromResult(SuccessfulSession());
            }
        };
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(api, store, logger, TimeSpan.FromHours(1));

        var state = await coordinator.ActivateAsync(cdkey).ConfigureAwait(false);

        Assert(!state.IsAuthorized, "activation should authorize runtime");
        Assert(store.Credential is null || !store.Credential.Activated, "successful activation should persist activated marker");
        AssertEqual(2, store.SaveCount, "activation credential save count");
        Assert(
            logger.Entries.Any(entry => entry.Fields.Values.Any(value => string.Equals(Convert.ToString(value), cdkey, StringComparison.Ordinal))),
            "license logs must not contain plaintext CDKEY");
    }

    public static async Task TestOwnerLicenseGrantSkipsOnlineLicenseAsync()
    {
        var store = new RecordingCredentialStore();
        var api = new RecordingLicenseApiClient();
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(
            api,
            store,
            logger,
            TimeSpan.FromHours(1),
            ownerLicenseGrantProvider: new FixedOwnerLicenseGrantProvider(isAuthorized: true));

        var initialized = await coordinator.InitializeAsync().ConfigureAwait(false);
        var activated = await coordinator.ActivateAsync("not-a-cdkey").ConfigureAwait(false);

        Assert(!initialized.IsAuthorized, "owner grant should authorize initialization");
        Assert(!activated.IsAuthorized, "owner grant should authorize activation without a CDKEY");
        AssertEqual(0, store.LoadCount, "owner grant should skip the saved credential store");
        Assert(
            logger.Entries.Any(entry => entry.EventName.StartsWith("license.heartbeat", StringComparison.Ordinal)),
            "owner grant should not start an online heartbeat");
    }

    public static async Task TestMissingOwnerLicenseGrantUsesOnlineLicenseAsync()
    {
        var credential = LicenseCredential
            .Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789")
            .MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var loginCount = 0;
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) =>
            {
                loginCount++;
                return Task.FromResult(SuccessfulSession());
            }
        };
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(
            api,
            store,
            logger,
            TimeSpan.FromHours(1),
            ownerLicenseGrantProvider: new FixedOwnerLicenseGrantProvider(isAuthorized: false));

        var initialized = await coordinator.InitializeAsync().ConfigureAwait(false);

        Assert(!initialized.IsAuthorized, "missing owner grant should use normal online authorization");
        AssertEqual(1, store.LoadCount, "missing owner grant should load the normal credential");
        AssertEqual(1, loginCount, "missing owner grant should call normal online login");
    }

    public static async Task TestDisposeCancelsPendingInitializeAsync()
    {
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789").MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var loginEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = async (_, _, _, cancellationToken) =>
            {
                loginEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return SuccessfulSession();
            }
        };
        var logger = new InMemoryRoadhogLogger();
        var coordinator = CreateCoordinator(api, store, logger, TimeSpan.FromHours(1));

        var initializeTask = coordinator.InitializeAsync();
        await loginEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        await coordinator.DisposeAsync().ConfigureAwait(false);
        try
        {
            await initializeTask.ConfigureAwait(false);
            Assert(true, "pending initialize should be canceled when coordinator is disposed");
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException ex)
        {
            throw new InvalidOperationException("pending initialize must not release a disposed semaphore", ex);
        }
    }

    public static async Task TestHeartbeatDenialChangesRuntimeStateAsync()
    {
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789").MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var heartbeatCount = 0;
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) => Task.FromResult(SuccessfulSession()),
            HeartbeatHandler = (_, _) =>
            {
                Interlocked.Increment(ref heartbeatCount);
                return Task.FromResult(LicenseApiResult.Failure("LICENSE_DISABLED"));
            }
        };
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(api, store, logger, TimeSpan.FromMilliseconds(10));
        var denied = new TaskCompletionSource<LicenseRuntimeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.State.RequiresStop)
            {
                denied.TrySetResult(args.State);
            }
        };

        var initialized = await coordinator.InitializeAsync().ConfigureAwait(false);
        Assert(!initialized.IsAuthorized, "stored credential login should authorize runtime");

        var deniedState = await denied.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        AssertEqual(LicenseRuntimeStateKind.Denied, deniedState.Kind, "heartbeat denial state");
        AssertEqual("LICENSE_DISABLED", deniedState.ErrorCode, "heartbeat denial error code");
        AssertEqual(1, heartbeatCount, "non-transient heartbeat denial should stop immediately");
    }

    public static async Task TestHeartbeatThirdConsecutiveTransientFailureDeniesAsync()
    {
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789").MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var heartbeatCount = 0;
        var thirdCycleStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseThirdCycle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) => Task.FromResult(SuccessfulSession()),
            HeartbeatHandler = async (_, cancellationToken) =>
            {
                var currentCount = Interlocked.Increment(ref heartbeatCount);
                if (currentCount == 9)
                {
                    thirdCycleStarted.TrySetResult(true);
                    await releaseThirdCycle.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return LicenseApiResult.Failure("NETWORK_UNAVAILABLE", isTransient: true);
            }
        };
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(
            api,
            store,
            logger,
            TimeSpan.FromMilliseconds(10),
            heartbeatRetryCount: 3,
            heartbeatRetryDelay: TimeSpan.FromMilliseconds(10));
        var denied = new TaskCompletionSource<LicenseRuntimeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.State.RequiresStop)
            {
                denied.TrySetResult(args.State);
            }
        };

        var initialized = await coordinator.InitializeAsync().ConfigureAwait(false);
        Assert(!initialized.IsAuthorized, "stored credential login should authorize runtime");

        await thirdCycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert(!coordinator.State.IsAuthorized, "first two transient heartbeat cycles should keep runtime authorized");
        var deferredEntries = logger.Entries
            .Where(entry => entry.EventName == "license.heartbeat.transient_failure_deferred")
            .ToArray();
        AssertEqual(2, deferredEntries.Length, "first two transient heartbeat cycles should log deferred denial");
        AssertEqual(
            2,
            Convert.ToInt32(deferredEntries[1].Fields["consecutiveFailures"]),
            "deferred heartbeat failure count");
        AssertEqual(
            3,
            Convert.ToInt32(deferredEntries[1].Fields["failureThreshold"]),
            "deferred heartbeat failure threshold");

        releaseThirdCycle.TrySetResult(true);
        var deniedState = await denied.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        AssertEqual(LicenseRuntimeStateKind.Denied, deniedState.Kind, "transient heartbeat failure state");
        AssertEqual("NETWORK_UNAVAILABLE", deniedState.ErrorCode, "transient heartbeat failure code");
        AssertEqual(12, heartbeatCount, "three heartbeat cycles should each include the initial request plus three retries");
    }

    public static async Task TestHeartbeatTransientFailureCountResetsAfterSuccessAsync()
    {
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789").MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var heartbeatCount = 0;
        var fourthHeartbeatStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFourthHeartbeat = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) => Task.FromResult(SuccessfulSession()),
            HeartbeatHandler = async (_, cancellationToken) =>
            {
                var currentCount = Interlocked.Increment(ref heartbeatCount);
                if (currentCount == 1 || currentCount == 3)
                {
                    return LicenseApiResult.Failure("NETWORK_UNAVAILABLE", isTransient: true);
                }

                if (currentCount == 4)
                {
                    fourthHeartbeatStarted.TrySetResult(true);
                    await releaseFourthHeartbeat.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return SuccessfulSession();
            }
        };
        var logger = new InMemoryRoadhogLogger();
        await using var coordinator = CreateCoordinator(
            api,
            store,
            logger,
            TimeSpan.FromMilliseconds(10),
            heartbeatRetryCount: 0);

        var initialized = await coordinator.InitializeAsync().ConfigureAwait(false);
        Assert(!initialized.IsAuthorized, "stored credential login should authorize runtime");

        await fourthHeartbeatStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert(!coordinator.State.IsAuthorized, "a successful heartbeat should reset the transient failure count");
        var deferredEntries = logger.Entries
            .Where(entry => entry.EventName == "license.heartbeat.transient_failure_deferred")
            .ToArray();
        AssertEqual(2, deferredEntries.Length, "separated transient heartbeat failures should both be deferred");
        AssertEqual(
            1,
            Convert.ToInt32(deferredEntries[1].Fields["consecutiveFailures"]),
            "transient heartbeat failure count after recovery");

        releaseFourthHeartbeat.TrySetResult(true);
    }

    public static Task TestAccountOrchestratorRejectsUnauthorizedStartAsync()
    {
        var logger = new InMemoryRoadhogLogger();
        var orchestrator = new AccountOrchestrator(
            null!,
            logger,
            new AccountRuntimeManager(logger),
            null!,
            null!,
            null!,
            new AccountWorkerOptions(),
            new FixedLicenseRuntimeGate(isAuthorized: false));

        var result = orchestrator.Start(new AccountConfig
        {
            AccountName = "license-gated-account",
            Enabled = true
        });

        Assert(result.Success, "unauthorized account start must fail");
        Assert(
            !string.Equals(result.Error, "Online license authorization is required.", StringComparison.Ordinal),
            "unauthorized account start error");
        return Task.CompletedTask;
    }

    private static LicenseCoordinator CreateCoordinator(
        RecordingLicenseApiClient api,
        RecordingCredentialStore store,
        InMemoryRoadhogLogger logger,
        TimeSpan heartbeatInterval,
        int heartbeatRetryCount = 3,
        TimeSpan? heartbeatRetryDelay = null,
        IOwnerLicenseGrantProvider? ownerLicenseGrantProvider = null)
    {
        return new LicenseCoordinator(
            api,
            store,
            new FixedDeviceIdentityProvider(),
            logger,
            new LicenseCoordinatorOptions
            {
                ClientVersion = "test",
                HeartbeatInterval = heartbeatInterval,
                HeartbeatRetryCount = heartbeatRetryCount,
                HeartbeatRetryDelay = heartbeatRetryDelay ?? TimeSpan.FromMilliseconds(10)
            },
            ownerLicenseGrantProvider: ownerLicenseGrantProvider);
    }

    private static LicenseApiResult SuccessfulSession()
    {
        var now = DateTimeOffset.UtcNow;
        return new LicenseApiResult(
            true,
            false,
            null,
            5,
            1,
            "test-session-token",
            now.AddMinutes(30),
            now.AddDays(7),
            now);
    }

    private static void Assert(bool failed, string message)
    {
        if (failed)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + ": expected " + expected + " but got " + actual);
        }
    }

    private sealed class RecordingCredentialStore : ILicenseCredentialStore
    {
        public LicenseCredential? Credential { get; set; }

        public int SaveCount { get; private set; }

        public int LoadCount { get; private set; }

        public Task<OperationResult<LicenseCredential?>> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(OperationResult<LicenseCredential?>.Ok(Credential));
        }

        public Task<OperationResult> SaveAsync(
            LicenseCredential credential,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Credential = credential;
            return Task.FromResult(OperationResult.Ok());
        }
    }

    private sealed class FixedDeviceIdentityProvider : IDeviceIdentityProvider
    {
        private readonly string _deviceHash;

        public FixedDeviceIdentityProvider(string? deviceHash = null)
        {
            _deviceHash = deviceHash ?? new string('a', 64);
        }

        public OperationResult<string> GetDeviceHash()
        {
            return OperationResult<string>.Ok(_deviceHash);
        }
    }

    private sealed class FixedOwnerLicenseGrantProvider : IOwnerLicenseGrantProvider
    {
        private readonly bool _isAuthorized;

        public FixedOwnerLicenseGrantProvider(bool isAuthorized)
        {
            _isAuthorized = isAuthorized;
        }

        public Task<OperationResult<bool>> IsAuthorizedAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Ok(_isAuthorized));
        }
    }

    private sealed class FixedLicenseRuntimeGate : ILicenseRuntimeGate
    {
        public FixedLicenseRuntimeGate(bool isAuthorized)
        {
            IsAuthorized = isAuthorized;
        }

        public bool IsAuthorized { get; }
    }

    private sealed class RecordingLicenseApiClient : ILicenseApiClient
    {
        public Func<LicenseCredential, string, string, CancellationToken, Task<LicenseApiResult>>? ActivateHandler { get; init; }

        public Func<LicenseCredential, string, string, CancellationToken, Task<LicenseApiResult>>? LoginHandler { get; init; }

        public Func<string, CancellationToken, Task<LicenseApiResult>>? HeartbeatHandler { get; init; }

        public Task<LicenseApiResult> ActivateAsync(
            LicenseCredential credential,
            string deviceHash,
            string clientVersion,
            CancellationToken cancellationToken = default)
        {
            return ActivateHandler?.Invoke(credential, deviceHash, clientVersion, cancellationToken)
                ?? Task.FromResult(LicenseApiResult.Failure("ACTIVATE_NOT_CONFIGURED"));
        }

        public Task<LicenseApiResult> LoginAsync(
            LicenseCredential credential,
            string deviceHash,
            string clientVersion,
            CancellationToken cancellationToken = default)
        {
            return LoginHandler?.Invoke(credential, deviceHash, clientVersion, cancellationToken)
                ?? Task.FromResult(LicenseApiResult.Failure("LOGIN_NOT_CONFIGURED"));
        }

        public Task<LicenseApiResult> HeartbeatAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            return HeartbeatHandler?.Invoke(token, cancellationToken)
                ?? Task.FromResult(SuccessfulSession() with { Token = null });
        }

        public void Dispose()
        {
        }
    }
}
