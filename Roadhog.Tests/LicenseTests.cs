using System.Text;
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
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) => Task.FromResult(SuccessfulSession()),
            HeartbeatHandler = (_, _) => Task.FromResult(LicenseApiResult.Failure("LICENSE_DISABLED"))
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
    }

    public static async Task TestHeartbeatTransientFailureRetriesThenDeniesAsync()
    {
        var credential = LicenseCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789").MarkActivated();
        var store = new RecordingCredentialStore { Credential = credential };
        var heartbeatCount = 0;
        var api = new RecordingLicenseApiClient
        {
            LoginHandler = (_, _, _, _) => Task.FromResult(SuccessfulSession()),
            HeartbeatHandler = (_, _) =>
            {
                heartbeatCount++;
                return Task.FromResult(LicenseApiResult.Failure("NETWORK_UNAVAILABLE", isTransient: true));
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

        var deniedState = await denied.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        AssertEqual(LicenseRuntimeStateKind.Denied, deniedState.Kind, "transient heartbeat failure state");
        AssertEqual("NETWORK_UNAVAILABLE", deniedState.ErrorCode, "transient heartbeat failure code");
        AssertEqual(4, heartbeatCount, "initial heartbeat plus three retries");
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
        TimeSpan? heartbeatRetryDelay = null)
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
            });
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

        public Task<OperationResult<LicenseCredential?>> LoadAsync(CancellationToken cancellationToken = default)
        {
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
        public OperationResult<string> GetDeviceHash()
        {
            return OperationResult<string>.Ok(new string('a', 64));
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
