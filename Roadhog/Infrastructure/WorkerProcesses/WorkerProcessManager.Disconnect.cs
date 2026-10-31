namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    // Poll holds the account gate. Reuse ordinary startup only after the old worker,
    // its input and its native handles have been reconciled; keep the user's run intent.
    private async Task RecoverDisconnectedGameAsync(Entry entry, Guid requestId, Func<bool> superseded, CancellationToken operation)
    {
        lock (entry.Sync)
        {
            if (superseded() || !entry.Desired || !entry.Config.AutoRecover || entry.LoginOnly) return;
            entry.RecoveringGameDisconnect = true;
            entry.State = "recovering";
        }
        _logger.Info("game_disconnect.recovering", new Dictionary<string, object?>
        { ["account"] = entry.Config.AccountName, ["requestId"] = requestId });
        using var recovery = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, operation);
        try
        {
            await ShutdownCoreAsync(entry).ConfigureAwait(false);
            lock (entry.Sync)
                if (superseded() || !entry.Desired || !entry.Config.AutoRecover || !ScheduleAllowsStart(entry)) return;
            await StartCoreAsync(entry, false, recovery.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (entry.Sync) if (!superseded() && !recovery.IsCancellationRequested) Fail(entry, error.Message);
        }
    }
}
