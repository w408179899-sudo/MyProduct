using Roadhog.Application;
using Roadhog.Application.Channels;
using Roadhog.Application.Licensing;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Shell;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Input;
using Roadhog.Core.Processes;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Licensing;
using Roadhog.Infrastructure.Mock;
using Roadhog.Infrastructure.Offsets;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.Profiles;
using Roadhog.Infrastructure.Shell;
using Roadhog.Infrastructure.ToolBridge;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.Composition;

public sealed class RoadhogServices : IDisposable
{
    private RoadhogServices(
        IRoadhogLogger logger,
        IRoadhogGameApi gameApi,
        IHardwareDeviceResolver hardwareResolver,
        ITargetProcessResolver processResolver,
        IAccountConfigStore accountConfigStore,
        ISharedPathStore sharedPathStore,
        IScriptProfileStore scriptProfileStore,
        IFolderLauncher folderLauncher,
        AccountRuntimeManager accountRuntimeManager,
        AccountOrchestrator accountOrchestrator,
        RoadhogRuntime runtime,
        OffsetCatalogProvider offsets,
        LicenseCoordinator licenseCoordinator,
        IKeyboardInput keyboardInput,
        string keyboardDeviceText,
        string kmBoxNetConfigPath,
        string pathLibraryDirectory,
        KmBoxNetDeviceConfig kmBoxNetConfig)
    {
        Logger = logger;
        GameApi = gameApi;
        HardwareResolver = hardwareResolver;
        ProcessResolver = processResolver;
        AccountConfigStore = accountConfigStore;
        SharedPathStore = sharedPathStore;
        ScriptProfileStore = scriptProfileStore;
        FolderLauncher = folderLauncher;
        AccountRuntimeManager = accountRuntimeManager;
        AccountOrchestrator = accountOrchestrator;
        Runtime = runtime;
        Offsets = offsets;
        LicenseCoordinator = licenseCoordinator;
        KeyboardInput = keyboardInput;
        KeyboardDeviceText = keyboardDeviceText;
        KmBoxNetConfigPath = kmBoxNetConfigPath;
        PathLibraryDirectory = pathLibraryDirectory;
        KmBoxNetConfig = kmBoxNetConfig;
        LicenseCoordinator.StateChanged += LicenseCoordinator_StateChanged;
    }

    private bool _disposed;

    public IRoadhogLogger Logger { get; }

    public IRoadhogGameApi GameApi { get; }

    public IHardwareDeviceResolver HardwareResolver { get; }

    public ITargetProcessResolver ProcessResolver { get; }

    public IAccountConfigStore AccountConfigStore { get; }

    public ISharedPathStore SharedPathStore { get; }

    public IScriptProfileStore ScriptProfileStore { get; }

    public IFolderLauncher FolderLauncher { get; }

    public AccountRuntimeManager AccountRuntimeManager { get; }

    public AccountOrchestrator AccountOrchestrator { get; }

    public RoadhogRuntime Runtime { get; }

    public OffsetCatalogProvider Offsets { get; }

    public LicenseCoordinator LicenseCoordinator { get; }

    public IKeyboardInput KeyboardInput { get; }

    public string KeyboardDeviceText { get; }

    public string KmBoxNetConfigPath { get; }

    public string PathLibraryDirectory { get; }

    public KmBoxNetDeviceConfig KmBoxNetConfig { get; }

    public static RoadhogServices Create(RoadhogServiceOptions? options = null)
    {
        options ??= new RoadhogServiceOptions();
        var kmBoxConfigStore = new JsonKmBoxNetDeviceConfigStore(options.KmBoxNetConfigPath);
        var kmBoxLoadResult = kmBoxConfigStore.Load();
        if (kmBoxLoadResult.Success && kmBoxLoadResult.Value is { IsConfigured: true } savedKmBoxConfig)
        {
            savedKmBoxConfig.ApplyTo(options.KmBoxNetInput);
        }
        var effectiveKmBoxConfig = KmBoxNetDeviceConfig.FromOptions(options.KmBoxNetInput);

        IRoadhogLogger logger = options.EnableLogging
            ? new FileRoadhogLogger(options.LogDirectory)
            : NoOpRoadhogLogger.Instance;
        logger.Info("roadhog.services.created", new Dictionary<string, object?>
        {
            ["logDirectory"] = options.LogDirectory,
            ["accountConfigPath"] = options.AccountConfigPath,
            ["pathLibraryDirectory"] = options.PathLibraryDirectory,
            ["profileLibraryDirectory"] = options.ProfileLibraryDirectory,
            ["kmBoxNetConfigPath"] = options.KmBoxNetConfigPath,
            ["licenseCredentialPath"] = options.LicenseCredentialPath,
            ["ownerLicenseGrantPath"] = options.OwnerLicenseGrantPath,
            ["licenseServerUrl"] = options.LicenseServerUrl,
            ["inputBackend"] = "KmBoxNet",
            ["keyboardInput"] = "KMBox Net",
            ["keyboardEndpoint"] = options.KmBoxNetInput.EndpointText(),
            ["enableLogging"] = options.EnableLogging,
            ["useMockGameApi"] = options.UseMockGameApi,
            ["useToolTestBridge"] = options.UseToolTestBridge
        });
        if (!kmBoxLoadResult.Success)
        {
            logger.Warn("kmbox_net.config.load_failed", new Dictionary<string, object?>
            {
                ["path"] = options.KmBoxNetConfigPath,
                ["error"] = kmBoxLoadResult.Error
            });
        }
        IRoadhogGameApi gameApi = options.UseToolTestBridge
            ? new ToolProcessApiClient(options.ToolTestBridge, logger)
            : options.UseMockGameApi
                ? new MockRoadhogGameApi()
                : new AionVmmGameApi(options.AionVmm, logger);
        var hardwareResolver = new WindowsHardwareDeviceResolver(options.HardwareResolver);
        var processResolver = new AionProcessResolver(options.ProcessResolver);
        var accountConfigStore = new JsonAccountConfigStore(options.AccountConfigPath);
        var sharedPathStore = new JsonSharedPathStore(options.PathLibraryDirectory);
        var scriptProfileStore = new JsonScriptProfileStore(options.ProfileLibraryDirectory);
        var folderLauncher = new WindowsFolderLauncher();
        var accounts = new AccountRuntimeManager(logger);
        var deviceIdentityProvider = new WindowsDeviceIdentityProvider();
        var licenseServerUri = new Uri(options.LicenseServerUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var licenseApiClient = new CloudflareLicenseApiClient(new HttpClient
        {
            BaseAddress = licenseServerUri,
            Timeout = options.LicenseRequestTimeout
        });
        var licenseCoordinator = new LicenseCoordinator(
            licenseApiClient,
            new DpapiLicenseCredentialStore(options.LicenseCredentialPath),
            deviceIdentityProvider,
            logger,
            new LicenseCoordinatorOptions
            {
                HeartbeatInterval = options.LicenseHeartbeatInterval,
                HeartbeatRetryCount = options.LicenseHeartbeatRetryCount,
                HeartbeatRetryDelay = options.LicenseHeartbeatRetryDelay,
                ClientVersion = typeof(RoadhogServices).Assembly.GetName().Version?.ToString(3) ?? "unknown"
            },
            ownerLicenseGrantProvider: new SignedOwnerLicenseGrantProvider(
                options.OwnerLicenseGrantPath,
                deviceIdentityProvider));
        var keyboardInput = CreateKeyboardInput(options);
        var semiAutoController = new SemiAutoCombatController(keyboardInput);
        var stationaryCombatController = new StationaryCombatController(keyboardInput, semiAutoController, sharedPathStore);
        var fixedChannelSwitchExecutor = new FixedChannelMouseSwitchExecutor(keyboardInput, logger);
        var fixedChannelController = new FixedChannelController(
            keyboardInput,
            sharedPathStore,
            fixedChannelSwitchExecutor);
        var teamSupportController = new TeamSupportController(
            keyboardInput,
            tacticalTargetRangePolicy: stationaryCombatController);
        var teamOutputController = new TeamOutputController(
            keyboardInput,
            stationaryCombatController);
        var workerOptions = new AccountWorkerOptions
        {
            TickInterval = options.AccountWorkerTickInterval,
            StopTimeout = options.AccountWorkerStopTimeout,
            PollPlayerSnapshot = options.PollPlayerSnapshotInWorker
        };
        var accountOrchestrator = new AccountOrchestrator(
            gameApi,
            logger,
            accounts,
            hardwareResolver,
            processResolver,
            new DefaultAccountWorkerLoop(
                keyboardInput,
                semiAutoController,
                stationaryCombatController,
                teamSupportController,
                teamOutputController,
                fixedChannelController),
            workerOptions,
            licenseCoordinator);
        var runtime = new RoadhogRuntime(
            gameApi,
            logger,
            accounts,
            accountOrchestrator,
            accountConfigStore,
            hardwareResolver,
            keyboardInput,
            stationaryCombatController);
        var offsets = new OffsetCatalogProvider(new OffsetCatalogLoader(), logger);

        return new RoadhogServices(
            logger,
            gameApi,
            hardwareResolver,
            processResolver,
            accountConfigStore,
            sharedPathStore,
            scriptProfileStore,
            folderLauncher,
            accounts,
            accountOrchestrator,
            runtime,
            offsets,
            licenseCoordinator,
            keyboardInput,
            options.KmBoxNetInput.DeviceText(),
            options.KmBoxNetConfigPath,
            options.PathLibraryDirectory,
            effectiveKmBoxConfig);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LicenseCoordinator.StateChanged -= LicenseCoordinator_StateChanged;
        try
        {
            LicenseCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Error("license.dispose_exception", ex);
        }

        try
        {
            if (KeyboardInput is IInputStateReset reset)
            {
                var result = reset.ReleaseAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (!result.Success)
                {
                    Logger.Warn("input.release_all.dispose_failed", new Dictionary<string, object?>
                    {
                        ["error"] = result.Error
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("input.release_all.dispose_exception", ex);
        }

        if (KeyboardInput is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void LicenseCoordinator_StateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        if (!e.State.RequiresStop || _disposed)
        {
            return;
        }

        _ = StopWorkersForLicenseAsync(e.State.ErrorCode);
    }

    private async Task StopWorkersForLicenseAsync(string? errorCode)
    {
        try
        {
            Logger.Warn("license.runtime.stop_all", new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode
            });
            var results = await AccountOrchestrator.StopAllAsync().ConfigureAwait(false);
            foreach (var failure in results.Where(pair => !pair.Value.Success))
            {
                Logger.Warn("license.runtime.stop_failed", new Dictionary<string, object?>
                {
                    ["account"] = failure.Key,
                    ["error"] = failure.Value.Error
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("license.runtime.stop_exception", ex);
        }
    }

    private static IKeyboardInput CreateKeyboardInput(RoadhogServiceOptions options)
    {
        return new KmBoxNetKeyboardInput(options.KmBoxNetInput);
    }
}
