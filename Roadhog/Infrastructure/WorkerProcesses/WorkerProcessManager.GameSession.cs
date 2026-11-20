using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    private const int MaximumWorldSnapshotRestarts = 3;

    // The worker is already parked at its restart request. Use the normal relogin RPC
    // so the backend validates the saved login before asking the game to exit.
    private async Task RequestParkedWorkerReloginAsync(Entry entry, Guid requestId, CancellationToken operation, bool scheduledGrocerySellout = false)
    {
        AccountConfig account;
        int restarts;
        lock (entry.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            var requestMatches = scheduledGrocerySellout
                ? entry.Status is { ScheduledGrocerySellout: true, ScriptRestartRequestId: null } status &&
                    status.StandaloneShopRestartRequestId == requestId && !entry.Config.DailySchedule.ReloginEnabled
                : entry.Status?.ScriptRestartRequestId == requestId;
            if (!entry.Desired || entry.StartingRequest || operation.IsCancellationRequested ||
                entry.StopTask is { IsCompleted: false } || !ScheduleAllowsStart(entry) ||
                !requestMatches || entry.ParkedReloginRetry is { } retry && retry.Id == requestId && now < retry.At) return;
            entry.ParkedReloginRetry = (requestId, now.AddSeconds(30));
            account = entry.Config.Clone();
            restarts = entry.WorldSnapshotRestarts;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, operation);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var fields = new Dictionary<string, object?>
            { ["account"] = account.AccountName, ["requestId"] = requestId };
        var eventPrefix = scheduledGrocerySellout ? "game_session.scheduled_grocery_relogin" : "game_session.world_snapshot_relogin";
        if (!scheduledGrocerySellout) fields["scriptRestarts"] = restarts;
        try
        {
            var result = await Client(entry).CallAsync<OperationResult>(
                WorkerCommands.Relogin, [account], deadline.Token).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.Info(eventPrefix + "_requested", fields);
                return;
            }
            fields["error"] = result.Error;
            _logger.Warn(eventPrefix + (scheduledGrocerySellout ? "_not_accepted" : "_retry"), fields);
        }
        catch (Exception error)
        {
            if (operation.IsCancellationRequested || _lifetime.IsCancellationRequested) return;
            fields["error"] = error.Message;
            _logger.Warn(eventPrefix + "_retry", fields);
        }
    }

    private async Task CompleteGameSessionAsync(Entry entry, Guid requestId)
    {
        Task<OperationResult> stopping;
        long generation;
        CancellationTokenSource operation;
        GameSessionOperation? requested;
        lock (entry.Sync)
        {
            if (!entry.Desired || entry.StartingRequest || entry.StopTask is { IsCompleted: false } ||
                entry.Status?.GameSessionCompletedRequestId != requestId || entry.Descriptor is null ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0) return;
            requested = entry.Status.CompletedGameSessionOperation;
            entry.WorldSnapshotRestarts = 0;
            entry.ParkedReloginRetry = null;
            if (requested == GameSessionOperation.Relogin)
            {
                // Exit has already completed. Retry only the stop/start continuation;
                // never replay the game-exit request after a cleanup or login fault.
                entry.PendingShopRestartId = requestId;
                entry.PendingScriptRestart = true;
                entry.PendingWorldSnapshotRestart = false;
            }
            stopping = BeginStop(entry, forShopRestart: requested == GameSessionOperation.Relogin);
            generation = entry.StopGeneration;
            operation = entry.Operation;
        }
        var fields = new Dictionary<string, object?>
            { ["account"] = entry.Config.AccountName, ["requestId"] = requestId, ["operation"] = requested.ToString() };
        var stopped = await stopping.ConfigureAwait(false);
        if (!stopped.Success)
        {
            fields["error"] = stopped.Error;
            _logger.Warn("game_session.stop_failed", fields);
            if (requested == GameSessionOperation.Relogin)
                await DeferShopRestartAsync(entry, requestId, generation, stopped.Error).ConfigureAwait(false);
            return;
        }
        if (requested != GameSessionOperation.Relogin)
        {
            _logger.Info("game_session.offline", fields);
            return;
        }
        // A later user Stop/Start wins, including the gap between exit and login.
        var started = await StartAccountAsync(entry.Config.InstanceId, false, _lifetime.Token, null, generation, operation).ConfigureAwait(false);
        if (!started.Success)
            await DeferShopRestartAsync(entry, requestId, generation, started.Error).ConfigureAwait(false);
        else
            lock (entry.Sync)
                if (entry.StopGeneration == generation && entry.PendingShopRestartId == requestId)
                    entry.PendingShopRestartId = null;
        fields["success"] = started.Success;
        fields["error"] = started.Error;
        _logger.Info("game_session.relogin_complete", fields);
    }
}
