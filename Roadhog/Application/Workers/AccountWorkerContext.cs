using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Application.Workers;

public sealed class AccountWorkerContext
{
    public AccountWorkerContext(
        AccountConfig config,
        IRoadhogSnapshotReaderFactory snapshotReaders,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        AccountWorkerOptions options,
        CancellationToken stopToken,
        CleanupRequestMailbox? cleanupRequests = null)
    {
        Config = config;
        Logger = logger;
        RuntimeStates = runtimeStates;
        Options = options;
        StopToken = stopToken;
        Snapshots = snapshotReaders.Create(config, logger, stopToken);
        CleanupRequests = cleanupRequests ?? new();
    }

    public AccountConfig Config { get; }

    public IRoadhogSnapshotReader Snapshots { get; }

    public IRoadhogLogger Logger { get; }

    public AccountRuntimeManager RuntimeStates { get; }

    public AccountWorkerOptions Options { get; }

    public CancellationToken StopToken { get; }

    public CleanupRequestMailbox CleanupRequests { get; }
    public bool WorkflowOwnsCleanup { get; set; }

    private AccountWorkerContext(AccountWorkerContext source, AccountConfig config)
    {
        Config = config; Snapshots = source.Snapshots; Logger = source.Logger; RuntimeStates = source.RuntimeStates;
        Options = source.Options; StopToken = source.StopToken; CleanupRequests = source.CleanupRequests;
        WorkflowOwnsCleanup = source.WorkflowOwnsCleanup;
    }
    public AccountWorkerContext ForCleanup(ScriptSettings settings)
    {
        var config = Config.Clone();
        config.ScriptSettings ??= new();
        config.ScriptSettings.Maintenance = settings.Maintenance.Clone();
        config.ScriptSettings.Paths = settings.Paths.Clone();
        config.ScriptSettings.Paths.BagCleanupReturnByReversePath = true;
        return new(this, config);
    }
}
