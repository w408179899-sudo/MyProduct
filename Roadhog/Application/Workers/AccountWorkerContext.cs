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
        CancellationToken stopToken)
    {
        Config = config;
        Logger = logger;
        RuntimeStates = runtimeStates;
        Options = options;
        StopToken = stopToken;
        Snapshots = snapshotReaders.Create(config, logger, stopToken);
    }

    public AccountConfig Config { get; }

    public IRoadhogSnapshotReader Snapshots { get; }

    public IRoadhogLogger Logger { get; }

    public AccountRuntimeManager RuntimeStates { get; }

    public AccountWorkerOptions Options { get; }

    public CancellationToken StopToken { get; }
}
