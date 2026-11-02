using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    private async Task RestartAfterStandaloneShopAsync(Entry entry, Guid requestId)
    {
        Task<OperationResult> stopping;
        long generation;
        CancellationTokenSource operation;
        int? previousProcessId;
        lock (entry.Sync)
        {
            if (!entry.Desired || entry.StartingRequest || entry.StopTask is { IsCompleted: false } ||
                entry.PendingShopRestartId != requestId && (entry.Status?.StandaloneShopRestartRequestId != requestId || entry.Descriptor is null) ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0)
                return;
            previousProcessId = entry.Descriptor?.ProcessId;
            entry.PendingShopRestartId = requestId;
            // Reap and reconcile the old worker before every attempt; keep the user's run intent.
            stopping = BeginStop(entry, forShopRestart: true);
            generation = entry.StopGeneration;
            operation = entry.Operation;
        }
        var fields = new Dictionary<string, object?>
            { ["account"] = entry.Config.AccountName, ["requestId"] = requestId, ["previousProcessId"] = previousProcessId };
        _logger.Info("standalone_shop.restart.stopping", fields);
        var stopped = await stopping.ConfigureAwait(false);
        if (!stopped.Success)
        {
            fields["error"] = stopped.Error;
            _logger.Warn("standalone_shop.restart.failed", fields);
            await DeferShopRestartAsync(entry, requestId, generation, stopped.Error).ConfigureAwait(false);
            return;
        }
        // Generation and operation checks occur inside start admission too, so a later Stop or Start wins.
        var started = await StartAccountAsync(entry.Config.InstanceId, false, _lifetime.Token, null, generation, operation).ConfigureAwait(false);
        if (!started.Success)
        {
            fields["error"] = started.Error;
            _logger.Warn("standalone_shop.restart.failed", fields);
            await DeferShopRestartAsync(entry, requestId, generation, started.Error).ConfigureAwait(false);
            return;
        }
        lock (entry.Sync)
            if (entry.StopGeneration == generation && entry.PendingShopRestartId == requestId)
                entry.PendingShopRestartId = null;
        fields["processId"] = entry.Descriptor?.ProcessId;
        _logger.Info("standalone_shop.restart.complete", fields);
    }

    private async Task DeferShopRestartAsync(Entry entry, Guid requestId, long generation, string? error)
    {
        lock (entry.Sync)
        {
            if (!entry.Desired || entry.PendingShopRestartId != requestId || entry.StopGeneration != generation ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0) return;
            if (entry.Operation.IsCancellationRequested)
            {
                entry.RetiredOperations.Add(entry.Operation);
                entry.Operation = new();
            }
            Fail(entry, "售罄后重启暂未完成，将延迟重试：" + error);
            entry.State = "recovering";
            _logger.Warn("standalone_shop.restart.retry_scheduled", new Dictionary<string, object?>
            { ["account"] = entry.Config.AccountName, ["requestId"] = requestId, ["retryAt"] = entry.RetryAt, ["error"] = error });
        }
        try { await SaveIntentAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.Error("standalone_shop.restart.intent_failed", ex); }
    }
}
