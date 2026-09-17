namespace Smart.Runtime;

public enum AccountState { Stopped, Running, Stopping, Faulted, Paused }
public sealed record AccountStatus(AccountState State, string? Error = null);
public sealed class AccountRunner(Func<AccountWorker> createWorker, Action? onStopped = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _stop;
    private Task? _task;
    private Task? _stopOperation;
    private AccountWorker? _worker;
    private bool _disposed, _disposeComplete;
    private AccountStatus _status = new(AccountState.Stopped);
    public AccountStatus Status => Volatile.Read(ref _status);
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_task is { IsCompleted: false } || _stopOperation is { IsCompleted: false }) return;
            if (_worker is { InputCleanupComplete: false })
                throw new InvalidOperationException("Previous worker input cleanup must complete before restarting.");
            // Keep the old scope until creation succeeds; a failed factory must not discard its diagnostics.
            var worker = createWorker();
            _stop?.Dispose(); _stop = new();
            _worker = worker; _stopOperation = null;
            _status = new(AccountState.Running);
            var token = _stop.Token;
            _task = Task.Run(() => RunAsync(worker, token));
        }
    }
    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task operation;
        lock (_sync)
        {
            if (_task is null && _worker is null) return;
            if (_stopOperation is null || _stopOperation.IsCompleted)
            {
                if (_task is { IsCompleted: false }) _status = new(AccountState.Stopping);
                var cancellation = _stop is { IsCancellationRequested: false } source
                    ? source.CancelAsync() : Task.CompletedTask;
                _stopOperation = CompleteStopAsync(_task, _worker, cancellation);
            }
            operation = _stopOperation;
        }
        // Caller timeout never releases a device still owned by a running or uncleaned worker.
        await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
    private async Task CompleteStopAsync(Task? run, AccountWorker? worker, Task cancellation)
    {
        List<Exception>? errors = null;
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception ex) { (errors ??= []).Add(ex); }
        if (run is not null)
        {
            try { await run.ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (worker is not null)
        {
            try { await worker.CleanupInputAsync().ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        lock (_sync)
        {
            if (worker is null || worker.InputCleanupComplete)
            {
                _worker = null; _task = null;
                _stop?.Dispose(); _stop = null;
            }
            if (errors is not null) _status = new(AccountState.Faulted, new AggregateException(errors).Message);
        }
        if (errors is not null) throw new AggregateException("Account stop failed.", errors);
    }
    public async Task PauseAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await StopAsync(timeout, cancellationToken).ConfigureAwait(false);
        lock (_sync) if (_status.State == AccountState.Stopped) _status = new(AccountState.Paused);
    }
    private async Task RunAsync(AccountWorker worker, CancellationToken token)
    {
        try
        {
            await worker.RunAsync(token).ConfigureAwait(false);
            Volatile.Write(ref _status, new(AccountState.Stopped));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Volatile.Write(ref _status, new(AccountState.Stopped)); }
        catch (Exception ex) { Volatile.Write(ref _status, new(AccountState.Faulted, ex.Message)); }
        finally { onStopped?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposeComplete) return;
            _disposed = true;
        }
        await StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        lock (_sync)
        {
            _stop?.Dispose(); _stop = null;
            _disposeComplete = true;
        }
    }
}
