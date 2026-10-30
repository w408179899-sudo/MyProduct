using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Application.StationaryCombat;

namespace Roadhog.Application.Workers;

public sealed class AccountWorkerHost
{
    private readonly IRoadhogSnapshotReaderFactory _snapshotReaders;
    private readonly IRoadhogLogger _logger;
    private readonly AccountRuntimeManager _runtimeStates;
    private readonly IAccountWorkerLoop _workerLoop;
    private readonly AccountWorkerOptions _options;
    private readonly ISharedPathStore? _pathStore;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _stopSource;
    private Task? _task;
    private CleanupRequestMailbox _cleanupRequests = new();
    private GameSessions.GameSessionRequestMailbox _gameSessionRequests = new();
    public GameSessions.GameSessionRequest? CompletedGameSession => _gameSessionRequests.Completed;
    public GameSessions.GameSessionRequest? PendingGameSession => _gameSessionRequests.Current;
    // Retained for IPC compatibility. Branch failures never put an account in a terminal state.
    public string? CleanupFailure => null;

    public AccountWorkerHost(
        IRoadhogSnapshotReaderFactory snapshotReaders,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        IAccountWorkerLoop workerLoop,
        AccountWorkerOptions options,
        ISharedPathStore? pathStore = null)
    {
        _snapshotReaders = snapshotReaders;
        _logger = logger;
        _runtimeStates = runtimeStates;
        _workerLoop = workerLoop;
        _options = options;
        _pathStore = pathStore;
    }

    public string? AccountName { get; private set; }

    public Guid? StandaloneShopRestartRequestId
    {
        get
        {
            lock (_syncRoot)
                return _task is { IsCompleted: false } && _stopSource?.IsCancellationRequested == false
                    ? _cleanupRequests.StandaloneShopRestartRequestId : null;
        }
    }

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

    public OperationResult Start(AccountConfig config, bool cleanupFirst = false, bool standaloneShop = false,
        GameSessions.GameSessionOperation? gameSessionOperation = null)
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
            _cleanupRequests = new();
            _gameSessionRequests = new();
            if (gameSessionOperation.HasValue) _gameSessionRequests.Request(gameSessionOperation.Value);
            if (cleanupFirst || standaloneShop)
            {
                var request = _cleanupRequests.Request(workerConfig.ScriptSettings ?? new(), true, standaloneShop: standaloneShop);
                if (!request.Success) return request;
            }
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

        var stopTimeout = _gameSessionRequests.Current is null ? _options.StopTimeout : _options.StopTimeout + TimeSpan.FromSeconds(5);
        var completed = await Task.WhenAny(task, Task.Delay(stopTimeout)).ConfigureAwait(false);
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
        AccountWorkerContext? context = null;
        var failures = 0;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _runtimeStates.MarkRunning(config.AccountName, Environment.CurrentManagedThreadId);
                    if (context is null)
                    {
                        if (_pathStore is not null)
                            await CombatPathRadiusBinding.ApplyAsync(config, _pathStore, _logger, stopToken).ConfigureAwait(false);
                        context = new AccountWorkerContext(config, _snapshotReaders, _logger, _runtimeStates, _options, stopToken, _cleanupRequests, _gameSessionRequests);
                    }

                    // Keep the same provider session. Only controller state is rebuilt after an exception.
                    await _workerLoop.RunAsync(context).ConfigureAwait(false);
                    stopToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("账号主循环意外返回，自动恢复运行。");
                }
                catch (Exception ex) when (!stopToken.IsCancellationRequested)
                {
                    // Last-resort recovery must abandon the failed branch before re-entering
                    // the main loop, so submitted trades are never replayed from its mailbox.
                    if (_cleanupRequests.Current != null || ex is CleanupExecutionFailedException)
                        _cleanupRequests.Abandon();
                    failures = started.Elapsed >= TimeSpan.FromMinutes(1) ? 1 : Math.Min(failures + 1, 16);
                    var initialMs = Math.Max(1, _options.RecoveryDelay.TotalMilliseconds);
                    var maxMs = Math.Max(initialMs, _options.MaxRecoveryDelay.TotalMilliseconds);
                    var delay = TimeSpan.FromMilliseconds(Math.Min(maxMs, initialMs * Math.Pow(2, failures - 1)));
                    _logger.Error("worker.loop.exception", ex, new Dictionary<string, object?>
                    {
                        ["account"] = config.AccountName, ["recovering"] = true,
                        ["attempt"] = failures, ["retryAfterMs"] = delay.TotalMilliseconds
                    });
                    _runtimeStates.MarkWarning(config.AccountName, "运行异常，自动恢复：" + ex.Message);
                    // No retry limit: a failed tick must not terminate an enabled account.
                    while (delay > TimeSpan.Zero)
                    {
                        _runtimeStates.MarkHeartbeat(config.AccountName);
                        var slice = delay > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                        await Task.Delay(slice, stopToken).ConfigureAwait(false);
                        delay -= slice;
                    }
                }
            }
        }
        catch (Exception) when (stopToken.IsCancellationRequested)
        {
            // Cancellation belongs to StopAsync, including errors from input release during shutdown.
        }
        finally
        {
            _runtimeStates.MarkStopped(config.AccountName);
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

    public OperationResult RequestCleanup(ScriptSettings settings, bool standaloneShop = false)
    {
        lock (_syncRoot)
        {
            if (_task is not { IsCompleted: false } || _stopSource?.IsCancellationRequested != false)
                return OperationResult.Fail("账号正在停止或未运行。");
            if (_gameSessionRequests.Current is not null || _gameSessionRequests.Completed is not null)
                return OperationResult.Fail("退出 / 重登任务正在执行，请等待完成。");
            return _cleanupRequests.Request(settings, true, standaloneShop: standaloneShop);
        }
    }

    public OperationResult RequestGameSession(GameSessions.GameSessionOperation operation)
    {
        lock (_syncRoot)
        {
            if (_task is not { IsCompleted: false } || _stopSource?.IsCancellationRequested != false)
                return OperationResult.Fail("账号正在停止或未运行。");
            if (_cleanupRequests.Current is not null || _cleanupRequests.StandaloneShopRestartRequestId.HasValue)
                return OperationResult.Fail("清包流程正在执行，请等待完成后再重新登录。");
            return _gameSessionRequests.Request(operation);
        }
    }
}
