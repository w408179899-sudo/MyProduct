using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

/// <summary>Remembers an action acknowledgement even if its UI closes between worker ticks.</summary>
internal sealed class LootTouchObservation : IDisposable
{
    private readonly IRoadhogSnapshotReader _snapshots;
    private readonly uint _corpseId;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _token;
    private readonly SemaphoreSlim _capture = new(1, 1);
    private LootInteractionSnapshot _latest;
    private int _armed, _touched, _disposed;
    private readonly Task _loop;
    public LootInteractionSnapshot Latest => Volatile.Read(ref _latest);
    public bool Touched => Volatile.Read(ref _touched) != 0;
    internal Task Completion => _loop;

    private LootTouchObservation(IRoadhogSnapshotReader snapshots, uint corpseId, LootInteractionSnapshot baseline,
        CancellationTokenSource stop, IRoadhogLogger logger, string account)
    {
        _snapshots = snapshots; _corpseId = corpseId; _latest = baseline; _stop = stop; _token = stop.Token;
        // A foreground tick's read budget/cancellation scope must not escape
        // into this separately owned, bounded action observation.
        using (ExecutionContext.SuppressFlow())
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(15, _token).ConfigureAwait(false);
                        await CaptureAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    logger.Warn("stationary_combat.loot.observer_stopped", new Dictionary<string, object?>
                    { ["account"] = account, ["targetServerObjectId"] = corpseId, ["error"] = ex.Message });
                }
            });
    }

    public static async Task<LootTouchObservation> StartAsync(IRoadhogSnapshotReader snapshots, uint corpseId,
        TimeSpan timeout, CancellationToken stopToken, IRoadhogLogger logger, string account)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        stop.CancelAfter(timeout);
        try
        {
            var baseline = await snapshots.ReadLootInteractionAsync(stop.Token).ConfigureAwait(false);
            return new(snapshots, corpseId, baseline.Value, stop, logger, account);
        }
        catch { stop.Dispose(); throw; }
    }

    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public async Task CaptureAsync()
    {
        await _capture.WaitAsync(_token).ConfigureAwait(false);
        try
        {
            var current = (await _snapshots.ReadLootInteractionAsync(_token).ConfigureAwait(false)).Value;
            _token.ThrowIfCancellationRequested();
            Volatile.Write(ref _latest, current);
            // The server-accepted corpse ID is sufficient: the window may have
            // already auto-closed by the time its independently read field arrives.
            if (Volatile.Read(ref _armed) != 0 && _corpseId != 0 && current.CorpseServerObjectId == _corpseId)
                Interlocked.Exchange(ref _touched, 1);
        }
        finally { _capture.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        // Do not block the worker on an in-flight native read. It is cancelled
        // below the snapshot boundary; observe completion and retire ownership.
        _ = _loop.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
