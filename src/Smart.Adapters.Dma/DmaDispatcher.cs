using System.Collections.Immutable;
using System.Threading.Channels;
using Smart.Data;
namespace Smart.Adapters.Dma;

public sealed record DmaDispatcherMetrics(long Requests, long NativeCalls, long Failures, int Queued, int Active, int PeakQueued);

// One bounded native-I/O thread per physical connection, never Task.Run for each datum.
// Production uses IsolatedVmmTransport, which bounds worker calls and confirms child termination.
// A directly injected native transport still requires its driver to return before shutdown.
public sealed class DmaDispatcher : IAsyncDisposable
{
    private sealed record Request(int ProcessId, ImmutableArray<MemoryReadRequest> Reads,
        CancellationToken Cancellation, TaskCompletionSource<ImmutableArray<MemoryBlock>> Completion);
    private readonly IMemoryTransport _transport;
    private readonly Channel<Request> _queue;
    private readonly Task _pump;
    private readonly SemaphoreSlim _close = new(1, 1);
    private bool _transportClosed;
    private long _requests, _nativeCalls, _failures;
    private int _active, _peakQueued;
    private int _disposed;
    public string DeviceId => _transport.DeviceId;
    public string ConnectionId => _transport.ConnectionId;
    public DmaDispatcherMetrics Metrics => new(Interlocked.Read(ref _requests), Interlocked.Read(ref _nativeCalls),
        Interlocked.Read(ref _failures), _queue.Reader.Count, Volatile.Read(ref _active), Volatile.Read(ref _peakQueued));
    public DmaDispatcher(IMemoryTransport transport, int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _transport = transport;
        _queue = Channel.CreateBounded<Request>(new BoundedChannelOptions(capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        _pump = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public async ValueTask<ImmutableArray<MemoryBlock>> ReadAsync(CaptureContext context,
        IReadOnlyList<MemoryReadRequest> reads, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (context.Session.DeviceId != DeviceId || context.Session.ConnectionId != ConnectionId)
            throw new InvalidOperationException("Capture is bound to another DMA connection.");
        if (reads.Count is < 1 or > 256 || reads.Any(x => x.Length <= 0) || reads.Sum(x => (long)x.Length) > 1_048_576)
            throw new ArgumentException("Read batch budget exceeded.");
        var completion = new TaskCompletionSource<ImmutableArray<MemoryBlock>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref _requests);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await _queue.Writer.WriteAsync(new(context.Session.ProcessId, reads.ToImmutableArray(), cancellationToken, completion), cancellationToken).ConfigureAwait(false);
        var queued = _queue.Reader.Count;
        for (var peak = Volatile.Read(ref _peakQueued); queued > peak; peak = Volatile.Read(ref _peakQueued))
            if (Interlocked.CompareExchange(ref _peakQueued, queued, peak) == peak) break;
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private void Pump()
    {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var request))
                {
                    if (request.Cancellation.IsCancellationRequested)
                    { request.Completion.TrySetCanceled(request.Cancellation); continue; }
                    Interlocked.Increment(ref _nativeCalls); Interlocked.Increment(ref _active);
                    try { request.Completion.TrySetResult(_transport.ReadBatch(request.ProcessId, request.Reads)); }
                    catch (Exception ex) { Interlocked.Increment(ref _failures); request.Completion.TrySetException(ex); }
                    finally { Interlocked.Decrement(ref _active); }
                }
            }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _queue.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
        await _close.WaitAsync().ConfigureAwait(false);
        try { if (!_transportClosed) { _transport.Dispose(); _transportClosed = true; } }
        finally { _close.Release(); }
    }
}

public sealed class DmaChannelReader<T, TPartition>(
    DmaDispatcher dispatcher, Func<CaptureContext, TPartition, IReadOnlyList<MemoryReadRequest>> plan,
    Func<ImmutableArray<MemoryBlock>, RawRead<T>> decode) : IRawChannelReader<T, TPartition> where TPartition : notnull
{
    public async ValueTask<RawRead<T>> CaptureAsync(CaptureContext context, TPartition partition, CancellationToken cancellationToken) =>
        decode(await dispatcher.ReadAsync(context, plan(context, partition), cancellationToken).ConfigureAwait(false));
}
