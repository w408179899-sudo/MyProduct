using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Application.Workers;

public sealed class AccountWorkerHost
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly AccountRuntimeManager _runtimeStates;
    private readonly IAccountWorkerLoop _workerLoop;
    private readonly AccountWorkerOptions _options;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _stopSource;
    private Task? _task;

    public AccountWorkerHost(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        IAccountWorkerLoop workerLoop,
        AccountWorkerOptions options)
    {
        _gameApi = gameApi;
        _logger = logger;
        _runtimeStates = runtimeStates;
        _workerLoop = workerLoop;
        _options = options;
    }

    public string? AccountName { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _task is { IsCompleted: false };
            }
        }
    }

    public OperationResult Start(AccountConfig config)
    {
        if (!config.Validate(out var error))
        {
            return OperationResult.Fail(error);
        }

        lock (_syncRoot)
        {
            if (_task is { IsCompleted: false })
            {
                return OperationResult.Fail("Account worker is already running: " + AccountName);
            }

            var workerConfig = config.Clone();
            AccountName = workerConfig.AccountName;
            _stopSource?.Dispose();
            _stopSource = new CancellationTokenSource();
            _runtimeStates.MarkStarting(workerConfig);

            var token = _stopSource.Token;
            _task = Task.Factory.StartNew(
                () => RunWorkerAsync(workerConfig, token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();

            return OperationResult.Ok();
        }
    }

    public async Task<OperationResult> StopAsync()
    {
        CancellationTokenSource? stopSource;
        Task? task;
        string? accountName;

        lock (_syncRoot)
        {
            stopSource = _stopSource;
            task = _task;
            accountName = AccountName;

            if (string.IsNullOrWhiteSpace(accountName) || task is null || task.IsCompleted)
            {
                return OperationResult.Ok();
            }

            _runtimeStates.RequestStop(accountName);
            stopSource?.Cancel();
        }

        var completed = await Task.WhenAny(task, Task.Delay(_options.StopTimeout)).ConfigureAwait(false);
        if (completed != task)
        {
            return OperationResult.Fail("Account worker did not stop before timeout: " + accountName);
        }

        await ObserveCompletionAsync(task).ConfigureAwait(false);

        lock (_syncRoot)
        {
            stopSource?.Dispose();
            if (ReferenceEquals(_stopSource, stopSource))
            {
                _stopSource = null;
            }
        }

        return OperationResult.Ok();
    }

    private async Task RunWorkerAsync(AccountConfig config, CancellationToken stopToken)
    {
        _runtimeStates.MarkRunning(config.AccountName, Environment.CurrentManagedThreadId);

        try
        {
            var context = new AccountWorkerContext(config, _gameApi, _logger, _runtimeStates, _options, stopToken);
            await _workerLoop.RunAsync(context).ConfigureAwait(false);
            _runtimeStates.MarkStopped(config.AccountName);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            _runtimeStates.MarkStopped(config.AccountName);
        }
        catch (Exception ex)
        {
            _logger.Error("worker.loop.exception", ex, new Dictionary<string, object?> { ["account"] = config.AccountName });
            _runtimeStates.MarkFailed(config.AccountName, ex.Message);
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Worker exceptions are already captured into AccountRuntimeState.
        }
    }
}
