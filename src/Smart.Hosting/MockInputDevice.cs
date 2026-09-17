using System.Collections.Concurrent;
using Smart.Contracts;
namespace Smart.Hosting;

public sealed class MockInputDevice(string deviceId, Action<InputCommand>? onSend = null) : IInputDevice
{
    private readonly ConcurrentQueue<InputCommand> _history = new();
    private int _releases;
    public string DeviceId { get; } = deviceId;
    public IReadOnlyList<InputCommand> History => _history.ToArray();
    public int Releases => Volatile.Read(ref _releases);
    public ValueTask SendAsync(InputCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _history.Enqueue(command);
        // Bound diagnostic history; normal scripts must not retain unbounded action logs.
        while (_history.Count > 1024) _history.TryDequeue(out _);
        onSend?.Invoke(command);
        return ValueTask.CompletedTask;
    }
    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _releases);
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
