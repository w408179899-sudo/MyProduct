using System.Runtime.CompilerServices;
using Smart.Adapters.Dma;
using Smart.Data;

[assembly: InternalsVisibleTo("SampleProject.Tests")]
namespace Smart.Hosting.Windows;

// A native worker exit is a lifecycle boundary. Ordinary read failures do not trigger this path.
internal sealed class ConnectionSnapshotLifetime : IAsyncDisposable
{
    private readonly IAsyncDisposable _connection;
    private readonly IMemoryConnectionLifecycle? _lifecycle;
    private readonly Action _invalidate;
    internal ConnectionSnapshotLifetime(IProcessMemoryTransport transport, IAsyncDisposable connection, SnapshotSession snapshots)
    {
        _connection = connection;
        _lifecycle = transport as IMemoryConnectionLifecycle;
        _invalidate = snapshots.Dispose;
        if (_lifecycle is null) return;
        _lifecycle.Disconnected += _invalidate;
        // Covers an exit between opening the snapshot session and subscribing to the event.
        if (!_lifecycle.IsConnected) _invalidate();
    }
    public async ValueTask DisposeAsync()
    {
        _invalidate();
        if (_lifecycle is not null) _lifecycle.Disconnected -= _invalidate;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
