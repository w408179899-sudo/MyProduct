using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Application.Workers;

public sealed class AccountWorkerContext
{
    public AccountWorkerContext(
        AccountConfig config,
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        AccountWorkerOptions options,
        CancellationToken stopToken)
    {
        Config = config;
        GameApi = gameApi;
        Logger = logger;
        RuntimeStates = runtimeStates;
        Options = options;
        StopToken = stopToken;
    }

    public AccountConfig Config { get; }

    public IRoadhogGameApi GameApi { get; }

    public IRoadhogLogger Logger { get; }

    public AccountRuntimeManager RuntimeStates { get; }

    public AccountWorkerOptions Options { get; }

    public CancellationToken StopToken { get; }
}
