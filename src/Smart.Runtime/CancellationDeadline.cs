using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Smart.Runtime.Tests")]
namespace Smart.Runtime;

// Timer callbacks must never execute extension cancellation handlers synchronously: an exception
// on the timer thread would terminate the host before input and device cleanup can run.
internal sealed class CancellationDeadline : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _source = new();
    private readonly ITimer _timer;
    private Task? _cancellation, _disposal;
    private bool _closing;
    public CancellationToken Token { get; }

    public CancellationDeadline(TimeSpan timeout, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Token = _source.Token;
        _timer = time.CreateTimer(static state => ((CancellationDeadline)state!).Expire(), this,
            timeout, Timeout.InfiniteTimeSpan);
    }

    private void Expire()
    {
        lock (_sync)
            if (!_closing) _cancellation ??= _source.CancelAsync();
    }

    // Freeze the deadline before deciding whether input may remain held. This never waits for
    // extension callbacks; an expiry that won the lock has already signalled Token when we return.
    public void Disarm()
    {
        lock (_sync) _closing = true;
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closing = true;
            return new(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
            Task? cancellation;
            lock (_sync) cancellation = _cancellation;
            if (cancellation is not null) await cancellation.ConfigureAwait(false);
        }
        finally { _source.Dispose(); }
    }
}
