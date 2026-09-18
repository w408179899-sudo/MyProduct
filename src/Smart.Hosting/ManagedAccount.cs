using Smart.Contracts;
using Smart.Runtime;
namespace Smart.Hosting;

public enum SessionState { Stopped, Connecting, Running, Reconnecting, Stopping, Paused, Faulted }
public sealed record SessionStatus(SessionState State, long Generation = 0, string? Error = null);
public interface IRuntimeSession : IAsyncDisposable
{
    AccountWorker Worker { get; }
    void Invalidate();
    ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken);
}
public interface IRuntimeSessionFactory : IAsyncDisposable
{
    ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken cancellationToken);
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
}

// Reconnection replaces the whole worker scope: snapshots, modules, actions and device ownership.
public sealed class ManagedAccount(AccountProfile profile, IRuntimeSessionFactory factory,
    IEventSink? events = null, TimeProvider? time = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private Task? _task;
    private Task? _stopOperation;
    private CancellationTokenSource? _stop;
    private IRuntimeSession? _session;
    private bool _disposed;
    private bool _disposeComplete;
    private bool _configurationHold;
    private SessionStatus _status = new(SessionState.Stopped);
    private long _generation;
    private long _startedUtcTicks;
    public DateTimeOffset? StartedAt
    {
        get { var ticks = Interlocked.Read(ref _startedUtcTicks); return ticks == 0 ? null : new(ticks, TimeSpan.Zero); }
    }
    public AccountProfile Profile => profile;
    public SessionStatus Status => Volatile.Read(ref _status);
    public WorkerMetrics? Metrics => Volatile.Read(ref _session)?.Worker.Metrics;
    public SessionTarget? Target => (Volatile.Read(ref _session) as IRuntimeSessionInfo)?.Target;
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_configurationHold) throw new InvalidOperationException("Account configuration is being replaced.");
            if (_stopOperation is { IsCompleted: false }) throw new InvalidOperationException("Previous stop is still completing.");
            if (_task is { IsCompleted: false }) return;
            if (_session is not null) throw new InvalidOperationException("Previous session cleanup must complete before restart.");
            profile.Validate();
            Interlocked.Exchange(ref _startedUtcTicks, _time.GetUtcNow().UtcTicks);
            _stop?.Dispose(); _stop = new();
            SetStatus(SessionState.Connecting);
            _task = Task.Run(() => RunAsync(_stop.Token));
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        var attempts = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { _session = await factory.OpenAsync(profile, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (IsRecoverableConnectionFailure(ex))
                {
                    SetStatus(SessionState.Reconnecting, ex.Message);
                    await DelayRetryAsync(++attempts, token).ConfigureAwait(false); continue;
                }
                Interlocked.Increment(ref _generation); attempts = 0;
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                var worker = _session.Worker.RunAsync(lifetime.Token);
                SetStatus(SessionState.Running);
                try
                {
                    var failures = 0;
                    while (!worker.IsCompleted)
                    {
                        var delay = Task.Delay(TimeSpan.FromMilliseconds(profile.ProbeIntervalMs), _time, lifetime.Token);
                        if (await Task.WhenAny(worker, delay).ConfigureAwait(false) == worker) break;
                        await delay.ConfigureAwait(false);
                        bool current;
                        try { current = await _session.IsCurrentAsync(lifetime.Token).ConfigureAwait(false); failures = 0; }
                        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                        catch (Exception ex)
                        {
                            if (++failures < 3) continue;
                            SetStatus(SessionState.Reconnecting, "Connection probe failed: " + ex.Message); current = false;
                        }
                        if (!current) break;
                    }
                    if (worker.IsCompleted) await worker.ConfigureAwait(false);
                }
                catch (InputExecutionException ex) { SetStatus(SessionState.Reconnecting, ex.Message); }
                finally
                {
                    await EndSessionAsync(worker, lifetime).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                SetStatus(SessionState.Reconnecting);
                await DelayRetryAsync(1, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetStatus(SessionState.Stopped); }
        catch (Exception ex) { SetStatus(SessionState.Faulted, ex.Message); }
    }
    private Task DelayRetryAsync(int attempt, CancellationToken token) => Task.Delay(
        TimeSpan.FromMilliseconds(Math.Min(30000, (long)profile.RetryDelayMs * (1 << Math.Min(5, attempt - 1)))), _time, token);
    private static bool IsRecoverableConnectionFailure(Exception error) => error is
        IOException or TimeoutException or System.Net.Sockets.SocketException;
    private void SetStatus(SessionState state, string? error = null)
    {
        var next = new SessionStatus(state, Interlocked.Read(ref _generation), error);
        if (Interlocked.Exchange(ref _status, next) == next) return;
        try { events?.Write(new(_time.GetUtcNow(), "session." + state.ToString().ToLowerInvariant(), profile.Id, error ?? "")); } catch { }
    }
    private async Task DisposeSessionAsync()
    {
        if (_session is null) return;
        await _session.DisposeAsync().ConfigureAwait(false);
        Volatile.Write(ref _session, null);
    }
    private async Task EndSessionAsync(Task worker, CancellationTokenSource lifetime)
    {
        List<Exception>? errors = null;
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { (errors ??= []).Add(ex); }
        try { _session?.Invalidate(); }
        catch (Exception ex) { (errors ??= []).Add(ex); }
        try { await worker.ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InputExecutionException) { }
        catch (Exception ex) { (errors ??= []).Add(ex); }
        try { await DisposeSessionAsync().ConfigureAwait(false); }
        catch (Exception ex) { (errors ??= []).Add(ex); }
        ThrowCleanupErrors(errors);
    }
    private static void ThrowCleanupErrors(List<Exception>? errors)
    {
        if (errors is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is { Count: > 1 }) throw new AggregateException(errors);
    }
    public async Task StopAsync(TimeSpan timeout, CancellationToken token = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Task pending;
        lock (_sync)
        {
            if (_stopOperation is not { IsCompleted: false })
            {
                if (_task is { IsCompleted: false } || _session is not null) SetStatus(SessionState.Stopping);
                _stopOperation = CompleteStopAsync(_task,
                    _stop is { IsCancellationRequested: false } source ? source.CancelAsync() : Task.CompletedTask);
            }
            pending = _stopOperation;
        }
        // The deadline covers all cleanup. Timeout only stops this caller's wait; ownership remains until cleanup finishes.
        await pending.WaitAsync(timeout, _time, token).ConfigureAwait(false);
    }
    private async Task CompleteStopAsync(Task? run, Task cancellation)
    {
        try
        {
            List<Exception>? errors = null;
            try { await cancellation.ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { if (run is not null) await run.ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { if (_session is not null) await DisposeSessionAsync().ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            ThrowCleanupErrors(errors);
            SetStatus(SessionState.Stopped);
        }
        catch (Exception ex) { SetStatus(SessionState.Faulted, ex.Message); throw; }
    }
    internal IDisposable HoldConfiguration()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_configurationHold || _task is { IsCompleted: false } || _stopOperation is { IsCompleted: false } ||
                Status.State is not (SessionState.Stopped or SessionState.Paused or SessionState.Faulted))
                throw new InvalidOperationException("Stop accounts before changing profiles.");
            _configurationHold = true;
            return new ConfigurationHold(this);
        }
    }
    private sealed class ConfigurationHold(ManagedAccount owner) : IDisposable
    {
        public void Dispose() { lock (owner._sync) owner._configurationHold = false; }
    }
    public async Task PauseAsync(TimeSpan timeout, CancellationToken token = default)
    {
        await StopAsync(timeout, token).ConfigureAwait(false);
        if (Status.State == SessionState.Stopped) SetStatus(SessionState.Paused);
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposeComplete) return;
            _disposed = true;
        }
        await StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        lock (_sync) { _stop?.Dispose(); _stop = null; _disposeComplete = true; }
    }
}
