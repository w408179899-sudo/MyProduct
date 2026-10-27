using System.Collections.Concurrent;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class RoadhogSnapshotReader
{
    // A native read may already be running when its cancellation is requested.
    // Keep ownership until it finishes; subsequent callers wait cancellably rather
    // than queuing more captures. Late results never reach publication here.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> pendingReads = new(StringComparer.Ordinal);

    private async Task<OperationResult<T>> ReadBoundedAsync<T>(string channel,
        Func<CancellationToken, Task<OperationResult<T>>> read, CancellationToken token)
    {
        var gate = pendingReads.GetOrAdd(channel, _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        async Task<OperationResult<T>> CaptureAsync()
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return await read(token).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }
        var pending = CaptureAsync();
        _ = pending.ContinueWith(failed => _ = failed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Give cooperative captures a brief drain window, never an unbounded wait.
            try { await pending.WaitAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false); }
            catch (Exception) { }
            throw;
        }
    }
}
