using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Processes;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Mock;
using Roadhog.Infrastructure.Offsets;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.ToolBridge;

namespace Roadhog.Infrastructure.Composition;

public sealed class RoadhogServices
{
    private RoadhogServices(
        IRoadhogLogger logger,
        IRoadhogGameApi gameApi,
        IHardwareDeviceResolver hardwareResolver,
        ITargetProcessResolver processResolver,
        IAccountConfigStore accountConfigStore,
        AccountRuntimeManager accountRuntimeManager,
        AccountOrchestrator accountOrchestrator,
        RoadhogRuntime runtime,
        OffsetCatalogProvider offsets)
    {
        Logger = logger;
        GameApi = gameApi;
        HardwareResolver = hardwareResolver;
        ProcessResolver = processResolver;
        AccountConfigStore = accountConfigStore;
        AccountRuntimeManager = accountRuntimeManager;
        AccountOrchestrator = accountOrchestrator;
        Runtime = runtime;
        Offsets = offsets;
    }

    public IRoadhogLogger Logger { get; }

    public IRoadhogGameApi GameApi { get; }

    public IHardwareDeviceResolver HardwareResolver { get; }

    public ITargetProcessResolver ProcessResolver { get; }

    public IAccountConfigStore AccountConfigStore { get; }

    public AccountRuntimeManager AccountRuntimeManager { get; }

    public AccountOrchestrator AccountOrchestrator { get; }

    public RoadhogRuntime Runtime { get; }

    public OffsetCatalogProvider Offsets { get; }

    public static RoadhogServices Create(RoadhogServiceOptions? options = null)
    {
        options ??= new RoadhogServiceOptions();

        var logger = new InMemoryRoadhogLogger();
        IRoadhogGameApi gameApi = options.UseToolTestBridge
            ? new ToolProcessApiClient(options.ToolTestBridge, logger)
            : new MockRoadhogGameApi();
        var hardwareResolver = new WindowsHardwareDeviceResolver(options.HardwareResolver);
        var processResolver = new AionProcessResolver(options.ProcessResolver);
        var accountConfigStore = new JsonAccountConfigStore(options.AccountConfigPath);
        var accounts = new AccountRuntimeManager(logger);
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
            new DefaultAccountWorkerLoop(),
            workerOptions);
        var runtime = new RoadhogRuntime(gameApi, logger, accounts, accountOrchestrator);
        var offsets = new OffsetCatalogProvider(new OffsetCatalogLoader(), logger);

        return new RoadhogServices(logger, gameApi, hardwareResolver, processResolver, accountConfigStore, accounts, accountOrchestrator, runtime, offsets);
    }
}
