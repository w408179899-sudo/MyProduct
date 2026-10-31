namespace Roadhog.Application.GameSessions;

public sealed record GameProcessIdentity(int ProcessId, string InstanceKey);
public sealed record GameProcessSnapshot(string Session, IReadOnlyList<GameProcessIdentity> Processes);

public interface IGameProcessSnapshotSource
{
    Task<GameProcessSnapshot> ReadAsync(CancellationToken token);
}

// Watches the game session independently of player reads and long-running gameplay actions.
// The provider owns completeness, retries and publication; absence here is official data.
public sealed class GameDisconnectMonitor(TimeSpan? interval = null)
{
    public async Task WaitForExitAsync(IGameProcessSnapshotSource source, int processId, CancellationToken token)
    {
        var initial = await source.ReadAsync(token).ConfigureAwait(false);
        var process = initial.Processes.SingleOrDefault(p => p.ProcessId == processId);
        if (process is null) return;
        while (true)
        {
            await Task.Delay(interval ?? TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            var current = await source.ReadAsync(token).ConfigureAwait(false);
            if (current.Session != initial.Session || !current.Processes.Contains(process)) return;
        }
    }
}
