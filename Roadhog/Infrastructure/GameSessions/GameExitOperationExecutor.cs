using Roadhog.Infrastructure.Login;

namespace Roadhog.Infrastructure.GameSessions;

// Exit retries are long-lived. Retire a timed-out operation, then replace its
// executor only after the original native/device task has actually completed.
internal sealed class GameExitOperationExecutor(TimeSpan? timeout = null, Func<Task>? afterDrain = null)
{
    private readonly object _sync = new();
    private LoginOperationExecutor _executor = new(timeout);
    private Task? _retirement;
    private bool _closed;

    internal Task DrainCompletion { get { lock (_sync) return _retirement ?? Task.CompletedTask; } }

    internal Task<T> RunAsync<T>(Func<CancellationToken, T> read, string operation, CancellationToken token) =>
        RunTaskAsync(t => Task.FromResult(read(t)), operation, token);

    internal async Task<T> RunTaskAsync<T>(Func<CancellationToken, Task<T>> action, string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LoginOperationExecutor executor;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_retirement != null)
            {
                if (!_retirement.IsCompleted)
                    throw new TimeoutException("等待上一次退出操作结束，尚未发送新操作。");
                _retirement.GetAwaiter().GetResult();
                _executor = new(timeout);
                _retirement = null;
            }
            executor = _executor;
        }
        try { return await executor.RunTaskAsync(action, operation, token).ConfigureAwait(false); }
        catch (TimeoutException) { Retire(executor); throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Retire(executor); throw; }
    }

    private void Retire(LoginOperationExecutor executor)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_executor, executor) || _retirement != null) return;
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = drained.Task;
            executor.Close(() => _ = FinishRetirementAsync(drained));
        }
    }

    private async Task FinishRetirementAsync(TaskCompletionSource drained)
    {
        try
        {
            if (afterDrain != null) await afterDrain().ConfigureAwait(false);
            drained.TrySetResult();
        }
        catch (Exception error) { drained.TrySetException(error); }
    }

    internal void Close(Action cleanup)
    {
        Task? retirement;
        LoginOperationExecutor executor;
        lock (_sync) { _closed = true; retirement = _retirement; executor = _executor; }
        if (retirement == null) executor.Close(cleanup);
        else _ = CleanupAsync(retirement, cleanup);
    }

    private static async Task CleanupAsync(Task retirement, Action cleanup)
    {
        try { await retirement.ConfigureAwait(false); } catch (Exception) { }
        cleanup();
    }
}
