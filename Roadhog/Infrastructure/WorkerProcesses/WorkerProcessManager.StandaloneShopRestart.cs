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
                entry.Status?.StandaloneShopRestartRequestId != requestId || entry.Descriptor is null ||
                Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0)
                return;
            previousProcessId = entry.Descriptor.ProcessId;
            // Use the same stop path as the button: clear durable intent, release input and reap the worker.
            stopping = BeginStop(entry);
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
            return;
        }
        // Generation and operation checks occur inside start admission too, so a later Stop or Start wins.
        var started = await StartAccountAsync(entry.Config.InstanceId, false, _lifetime.Token, null, generation, operation).ConfigureAwait(false);
        if (!started.Success)
        {
            fields["error"] = started.Error;
            _logger.Warn("standalone_shop.restart.failed", fields);
            return;
        }
        fields["processId"] = entry.Descriptor?.ProcessId;
        _logger.Info("standalone_shop.restart.complete", fields);
    }
}
