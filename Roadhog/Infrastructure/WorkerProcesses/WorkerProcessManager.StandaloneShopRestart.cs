using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    // Shared stop/reconcile/start lifecycle for sellout and explicit script recovery.
    private async Task RestartRequestedWorkerAsync(Entry entry, Guid requestId)
    {
        Task<OperationResult> stopping;
        long generation;
        CancellationTokenSource operation;
        int? previousProcessId;
        string eventPrefix;
        lock (entry.Sync)
        {
            if (!entry.Desired || entry.StartingRequest || entry.StopTask is { IsCompleted: false } ||
                entry.PendingShopRestartId != requestId &&
                ((entry.Status?.ScriptRestartRequestId ?? entry.Status?.StandaloneShopRestartRequestId) != requestId || entry.Descriptor is null) ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0)
                return;
            previousProcessId = entry.Descriptor?.ProcessId;
            if (entry.PendingShopRestartId != requestId)
            {
                entry.PendingScriptRestart = entry.Status?.ScriptRestartRequestId == requestId;
                entry.PendingWorldSnapshotRestart = entry.PendingScriptRestart && entry.Status?.WorldSnapshotRestartRequested == true;
                if (!entry.PendingWorldSnapshotRestart) entry.WorldSnapshotRestarts = 0;
            }
            eventPrefix = entry.PendingScriptRestart ? "account.restart" : "standalone_shop.restart";
            entry.PendingShopRestartId = requestId;
            // Reap and reconcile the old worker before every attempt; keep the user's run intent.
            stopping = BeginStop(entry, forShopRestart: true);
            generation = entry.StopGeneration;
            operation = entry.Operation;
        }
        var fields = new Dictionary<string, object?>
            { ["account"] = entry.Config.AccountName, ["requestId"] = requestId, ["previousProcessId"] = previousProcessId };
        _logger.Info(eventPrefix + ".stopping", fields);
        var stopped = await stopping.ConfigureAwait(false);
        if (!stopped.Success)
        {
            fields["error"] = stopped.Error;
            _logger.Warn(eventPrefix + ".failed", fields);
            await DeferShopRestartAsync(entry, requestId, generation, stopped.Error).ConfigureAwait(false);
            return;
        }
        // Generation and operation checks occur inside start admission too, so a later Stop or Start wins.
        var started = await StartAccountAsync(entry.Config.InstanceId, false, _lifetime.Token, null, generation, operation).ConfigureAwait(false);
        if (!started.Success)
        {
            fields["error"] = started.Error;
            _logger.Warn(eventPrefix + ".failed", fields);
            await DeferShopRestartAsync(entry, requestId, generation, started.Error).ConfigureAwait(false);
            return;
        }
        lock (entry.Sync)
            if (entry.StopGeneration == generation && entry.PendingShopRestartId == requestId)
            {
                if (entry.PendingWorldSnapshotRestart) entry.WorldSnapshotRestarts++;
                entry.PendingShopRestartId = null;
                entry.PendingWorldSnapshotRestart = false;
            }
        fields["processId"] = entry.Descriptor?.ProcessId;
        _logger.Info(eventPrefix + ".complete", fields);
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
            Fail(entry, (entry.PendingScriptRestart ? "脚本重启" : "售罄后重启") + "暂未完成，将延迟重试：" + error);
            entry.State = "recovering";
        }
        var intentPersisted = false;
        try { await SaveIntentAsync().ConfigureAwait(false); intentPersisted = true; }
        catch (Exception ex) { _logger.Error("standalone_shop.restart.intent_failed", ex); }
        // Publish the handoff after the persistence attempt has finished. Observers
        // must not see "scheduled" and race the subsequent run-intent replacement.
        lock (entry.Sync)
        {
            if (!entry.Desired || entry.PendingShopRestartId != requestId || entry.StopGeneration != generation ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0) return;
            _logger.Warn((entry.PendingScriptRestart ? "account.restart" : "standalone_shop.restart") + ".retry_scheduled", new Dictionary<string, object?>
            { ["account"] = entry.Config.AccountName, ["requestId"] = requestId, ["retryAt"] = entry.RetryAt,
                ["error"] = error, ["intentPersisted"] = intentPersisted });
        }
    }
}
