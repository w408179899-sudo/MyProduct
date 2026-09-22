namespace Roadhog.Application.Shell;

/// <summary>Tracks UI work so closing cannot dispose its workspace underneath an unfinished operation.</summary>
internal sealed class MainWindowOperations : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _pending = new();
    private CancellationTokenSource _cancellation = new();
    private bool _closing;
    private bool _disposed;

    public CancellationToken Token { get { lock (_sync) return _cancellation.Token; } }
    public bool IsClosing { get { lock (_sync) return _closing; } }

    public Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_closing || _disposed) return Task.FromCanceled(new CancellationToken(true));
            _pending.Add(completion.Task);
        }
        _ = ExecuteAsync();
        return completion.Task;

        async Task ExecuteAsync()
        {
            try
            {
                await action().ConfigureAwait(true);
                lock (_sync) { _pending.Remove(completion.Task); completion.TrySetResult(); }
            }
            catch (OperationCanceledException)
            {
                lock (_sync) { _pending.Remove(completion.Task); completion.TrySetCanceled(); }
            }
            catch (Exception exception)
            {
                lock (_sync) { _pending.Remove(completion.Task); completion.TrySetException(exception); }
            }
        }
    }

    public void BeginShutdown()
    {
        CancellationTokenSource cancellation;
        lock (_sync) { if (_disposed) return; _closing = true; cancellation = _cancellation; }
        cancellation.Cancel();
    }

    public async Task DrainAsync()
    {
        Task[] pending;
        lock (_sync) pending = _pending.ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(true); }
        catch { /* Originating UI handlers report failures; closing still drains every operation. */ }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MainWindowOperations));
            if (_pending.Count != 0) throw new InvalidOperationException("界面操作尚未结束，不能恢复操作。");
            _cancellation.Dispose();
            _cancellation = new();
            _closing = false;
        }
    }

    public void Dispose()
    {
        BeginShutdown();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_pending.Count == 0) _cancellation.Dispose();
        }
    }
}
