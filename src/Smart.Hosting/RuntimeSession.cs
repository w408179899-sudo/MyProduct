using Smart.Data;
using Smart.Runtime;
namespace Smart.Hosting;

public sealed class RuntimeSession(AccountWorker worker, SnapshotSession snapshots,
    Func<CancellationToken, ValueTask<bool>>? probe = null, IAsyncDisposable? connection = null, IDisposable? deviceLease = null) : IRuntimeSession
{
    private readonly SemaphoreSlim _cleanup = new(1, 1);
    private bool _disposed;
    public AccountWorker Worker => worker;
    public void Invalidate() => snapshots.Dispose();
    public ValueTask<bool> IsCurrentAsync(CancellationToken token) => probe?.Invoke(token) ?? ValueTask.FromResult(true);
    public async ValueTask DisposeAsync()
    {
        await _cleanup.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            Invalidate();
            await worker.CleanupInputAsync().ConfigureAwait(false);
            await snapshots.DisposeAsync().ConfigureAwait(false);
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            deviceLease?.Dispose(); _disposed = true;
        }
        finally { _cleanup.Release(); }
    }
}
