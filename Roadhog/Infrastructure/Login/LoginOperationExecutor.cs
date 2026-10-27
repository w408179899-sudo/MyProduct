namespace Roadhog.Infrastructure.Login;

// Owns native reads/inference and individual cancellable device operations. A timed-out
// session is retired; its outstanding operation can finish, but cannot start more work.
// Never wrap a whole login workflow here: later input must stay behind each await.
internal sealed class LoginOperationExecutor(TimeSpan? timeout = null)
{
    private readonly object _sync = new();
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);
    private Task? _pending;
    private bool _closed;

    internal async Task<T> RunAsync<T>(Func<CancellationToken, T> read, string operation, CancellationToken token)
        => await RunTaskAsync(t => Task.FromResult(read(t)), operation, token).ConfigureAwait(false);

    internal async Task<T> RunTaskAsync<T>(Func<CancellationToken, Task<T>> read, string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Task<T> pending;
        CancellationTokenSource deadline;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_pending is { IsCompleted: false })
                throw new InvalidOperationException("登录操作尚未结束，不能重叠执行。");
            deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(_timeout);
            pending = Task.Run(() =>
            {
                deadline.Token.ThrowIfCancellationRequested();
                return read(deadline.Token);
            });
            _pending = pending;
        }
        try
        {
            var value = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return value;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            lock (_sync) _closed = true;
            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"登录{operation}等待超过 {_timeout.TotalSeconds:0} 秒，已停止本次登录输入。");
        }
        finally
        {
            // Keep the token source alive until the native operation actually returns.
            _ = pending.ContinueWith(completed => { _ = completed.Exception; deadline.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal void Close(Action cleanup)
    {
        Task? pending;
        lock (_sync) { _closed = true; pending = _pending; }
        // Never dispose an inference session underneath a pending native call, and
        // never wait for a native destructor on the login/stop path.
        var completion = Task.Run(async () =>
        {
            if (pending is not null)
                try { await pending.ConfigureAwait(false); } catch (Exception) { }
            cleanup();
        });
        _ = completion.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
