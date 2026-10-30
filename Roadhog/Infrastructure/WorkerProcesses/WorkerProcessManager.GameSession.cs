using Roadhog.Application.GameSessions;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
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
            stopping = BeginStop(entry);
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
            return;
        }
        if (requested != GameSessionOperation.Relogin)
        {
            _logger.Info("game_session.offline", fields);
            return;
        }
        // A later user Stop/Start wins, including the gap between exit and login.
        var started = await StartAccountAsync(entry.Config.InstanceId, false, _lifetime.Token, null, generation, operation).ConfigureAwait(false);
        fields["success"] = started.Success;
        fields["error"] = started.Error;
        _logger.Info("game_session.relogin_complete", fields);
    }
}
