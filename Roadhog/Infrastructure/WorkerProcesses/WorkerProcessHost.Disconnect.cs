using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.GameSessions;

namespace Roadhog.Infrastructure.WorkerProcesses;

internal sealed partial class RoadhogWorkerProcessBackend
{
    private readonly object _disconnectSync = new();
    private CancellationTokenSource? _disconnectCancellation;
    private readonly List<Task> _disconnectTasks = new();
    private Guid? _disconnectRequestId;
    private volatile bool _loginRetryPending;

    private Guid? DisconnectRequestId { get { lock (_disconnectSync) return _disconnectRequestId; } }

    private void StartDisconnectWatch(AccountConfig account)
    {
        lock (_disconnectSync)
        {
            _disconnectCancellation?.Cancel();
            _disconnectRequestId = null;
            if (!account.AutoRecover || _services.LoginGameApi == null) return;
            var cancellation = new CancellationTokenSource();
            _disconnectCancellation = cancellation;
            _disconnectTasks.RemoveAll(t => t.IsCompleted);
            _disconnectTasks.Add(Task.Run(() => WatchGameAsync(account.Clone(), cancellation)));
        }
    }

    private async Task WatchGameAsync(AccountConfig account, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await WaitForDisconnectWithRecoveryAsync(async attemptToken =>
            {
                using var source = new VmmGameProcessSnapshotSource(_services.LoginGameApi!, account, _services.Logger);
                await new GameDisconnectMonitor().WaitForExitAsync(source, checked((int)account.ProcessId), attemptToken).ConfigureAwait(false);
            }, _services.Logger, account.AccountName, token).ConfigureAwait(false);
            lock (_disconnectSync)
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_disconnectCancellation, cancellation)) return;
                _disconnectRequestId = Guid.NewGuid();
                _loginCompleted = false;
                _services.Logger.Info("game_disconnect.detected", new Dictionary<string, object?>
                { ["account"] = account.AccountName, ["gamePid"] = account.ProcessId, ["requestId"] = _disconnectRequestId });
                // Stop stale gameplay immediately. The manager owns input release, old-worker
                // reconciliation and login in a new worker; no login is run alongside combat.
                _ = BeginStop();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { _services.Logger.Error("game_disconnect.monitor_failed", error); }
        finally
        {
            lock (_disconnectSync)
            {
                if (ReferenceEquals(_disconnectCancellation, cancellation)) _disconnectCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    internal static async Task WaitForDisconnectWithRecoveryAsync(Func<CancellationToken, Task> observeExit,
        IRoadhogLogger logger, string account, CancellationToken token, TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? TimeSpan.FromSeconds(3);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await observeExit(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return; // Only the monitor's normal completion confirms game exit.
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                logger.Error("game_disconnect.monitor_retry", error, new Dictionary<string, object?>
                    { ["account"] = account, ["retryAfterMs"] = delay.TotalMilliseconds });
            }
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }

    private void CancelDisconnectWatch()
    {
        lock (_disconnectSync) _disconnectCancellation?.Cancel();
    }

    private async Task DisposeDisconnectWatchAsync()
    {
        Task[] tasks;
        lock (_disconnectSync) { _disconnectCancellation?.Cancel(); tasks = _disconnectTasks.ToArray(); }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
