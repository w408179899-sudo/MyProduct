using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
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
using Roadhog.Infrastructure.Mock;
using Roadhog.Infrastructure.Offsets;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.Profiles;
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
        AccountRuntimeManager accountRuntimeManager,
        AccountOrchestrator accountOrchestrator,
        RoadhogRuntime runtime,
        OffsetCatalogProvider offsets,
        IKeyboardInput keyboardInput,
        string keyboardDeviceText,
        string kmBoxNetConfigPath,
        KmBoxNetDeviceConfig kmBoxNetConfig)
    {
        Logger = logger;
        GameApi = gameApi;
        HardwareResolver = hardwareResolver;
        ProcessResolver = processResolver;
        AccountConfigStore = accountConfigStore;
        SharedPathStore = sharedPathStore;
        ScriptProfileStore = scriptProfileStore;
        AccountRuntimeManager = accountRuntimeManager;
        AccountOrchestrator = accountOrchestrator;
        Runtime = runtime;
        Offsets = offsets;
        KeyboardInput = keyboardInput;
        KeyboardDeviceText = keyboardDeviceText;
        KmBoxNetConfigPath = kmBoxNetConfigPath;
        KmBoxNetConfig = kmBoxNetConfig;
    }

    private bool _disposed;

    public IRoadhogLogger Logger { get; }

    public IRoadhogGameApi GameApi { get; }

    public IHardwareDeviceResolver HardwareResolver { get; }

    public ITargetProcessResolver ProcessResolver { get; }

    public IAccountConfigStore AccountConfigStore { get; }

    public ISharedPathStore SharedPathStore { get; }

    public IScriptProfileStore ScriptProfileStore { get; }

    public AccountRuntimeManager AccountRuntimeManager { get; }

    public AccountOrchestrator AccountOrchestrator { get; }

    public RoadhogRuntime Runtime { get; }

    public OffsetCatalogProvider Offsets { get; }

    public IKeyboardInput KeyboardInput { get; }

    public string KeyboardDeviceText { get; }

    public string KmBoxNetConfigPath { get; }

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

        var memoryLogger = new InMemoryRoadhogLogger();
        var logger = new CompositeRoadhogLogger(
            memoryLogger,
            new FileRoadhogLogger(options.LogDirectory));
        logger.Info("roadhog.services.created", new Dictionary<string, object?>
        {
            ["logDirectory"] = options.LogDirectory,
            ["accountConfigPath"] = options.AccountConfigPath,
            ["pathLibraryDirectory"] = options.PathLibraryDirectory,
            ["profileLibraryDirectory"] = options.ProfileLibraryDirectory,
            ["kmBoxNetConfigPath"] = options.KmBoxNetConfigPath,
            ["inputBackend"] = "KmBoxNet",
            ["keyboardInput"] = "KMBox Net",
            ["keyboardEndpoint"] = options.KmBoxNetInput.EndpointText(),
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
        var accounts = new AccountRuntimeManager(logger);
        var keyboardInput = CreateKeyboardInput(options);
        var semiAutoController = new SemiAutoCombatController(keyboardInput);
        var stationaryCombatController = new StationaryCombatController(keyboardInput, semiAutoController, sharedPathStore);
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
            new DefaultAccountWorkerLoop(keyboardInput, semiAutoController, stationaryCombatController),
            workerOptions);
        var runtime = new RoadhogRuntime(gameApi, logger, accounts, accountOrchestrator, accountConfigStore, hardwareResolver);
        var offsets = new OffsetCatalogProvider(new OffsetCatalogLoader(), logger);

        return new RoadhogServices(
            logger,
            gameApi,
            hardwareResolver,
            processResolver,
            accountConfigStore,
            sharedPathStore,
            scriptProfileStore,
            accounts,
            accountOrchestrator,
            runtime,
            offsets,
            keyboardInput,
            options.KmBoxNetInput.DeviceText(),
            options.KmBoxNetConfigPath,
            effectiveKmBoxConfig);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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

    private static IKeyboardInput CreateKeyboardInput(RoadhogServiceOptions options)
    {
        return new KmBoxNetKeyboardInput(options.KmBoxNetInput);
    }
}
