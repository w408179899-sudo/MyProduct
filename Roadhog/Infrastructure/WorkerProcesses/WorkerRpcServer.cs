using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed class WorkerRpcServer
{
    private readonly string _pipeName;
    private readonly byte[] _tokenHash;
    private readonly Func<string, JsonElement[], IProgress<string>, CancellationToken, Task<object?>> _handler;

    public WorkerRpcServer(string pipeName, string token,
        Func<string, JsonElement[], IProgress<string>, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(handler);
        _pipeName = pipeName;
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        _handler = handler;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var capacity = new SemaphoreSlim(64);
        var sessions = new ConcurrentDictionary<long, Task>();
        long sequence = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 64,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var connected = pipe;
                    pipe = null;
                    var id = Interlocked.Increment(ref sequence);
                    var session = Task.Run(async () =>
                    {
                        try { await HandleAsync(connected, lifetime.Token).ConfigureAwait(false); }
                        finally { capacity.Release(); }
                    }, CancellationToken.None);
                    sessions[id] = session;
                    _ = session.ContinueWith(completed => sessions.TryRemove(id, out _),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch
                {
                    pipe?.Dispose();
                    capacity.Release();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            lifetime.Cancel();
            await Task.WhenAll(sessions.Values).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken shutdown)
    {
        using (pipe)
        using (var operation = CancellationTokenSource.CreateLinkedTokenSource(shutdown))
        {
            try
            {
                WorkerRpcFrame? request;
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    request = await WorkerRpcProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
                }
                if (request is null) return;
                if (request.Kind != "request" || string.IsNullOrWhiteSpace(request.Method)
                    || !CryptographicOperations.FixedTimeEquals(_tokenHash,
                        SHA256.HashData(Encoding.UTF8.GetBytes(request.Token ?? string.Empty))))
                {
                    await WorkerRpcProtocol.WriteAsync(pipe,
                        new WorkerRpcFrame { Kind = "error", Message = "Worker RPC authentication failed." }, shutdown).ConfigureAwait(false);
                    return;
                }

                var frames = Channel.CreateBounded<WorkerRpcFrame>(new BoundedChannelOptions(256)
                {
                    SingleReader = true, AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
                var monitor = MonitorDisconnectAsync(pipe, operation);
                var sender = SendFramesAsync(pipe, frames.Reader, operation);
                try
                {
                    var progress = new RpcProgress(message => frames.Writer.TryWrite(
                        new WorkerRpcFrame { Kind = "progress", Message = message }));
                    var invocation = Task.Run(() => _handler(request.Method, request.Arguments, progress, operation.Token),
                        CancellationToken.None);
                    // Native calls may finish after their disconnected caller. Observe later faults as well.
                    _ = invocation.ContinueWith(failed => _ = failed.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    var result = await invocation.WaitAsync(operation.Token).ConfigureAwait(false);
                    frames.Writer.TryWrite(new WorkerRpcFrame
                    {
                        Kind = "result", Value = JsonSerializer.SerializeToElement(result,
                            result?.GetType() ?? typeof(object), WorkerRpcProtocol.Json)
                    });
                }
                catch (OperationCanceledException)
                {
                    frames.Writer.TryWrite(new WorkerRpcFrame { Kind = "cancelled", Message = "The account operation was cancelled." });
                }
                catch (Exception exception)
                {
                    frames.Writer.TryWrite(new WorkerRpcFrame { Kind = "error", Message = exception.Message });
                }
                finally
                {
                    frames.Writer.TryComplete();
                    try { await sender.ConfigureAwait(false); }
                    finally { operation.Cancel(); }
                    await monitor.ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or JsonException)
            {
                // Malformed frames and abandoned callers affect only their own request.
                operation.Cancel();
            }
        }
    }

    private static async Task MonitorDisconnectAsync(Stream pipe, CancellationTokenSource operation)
    {
        try
        {
            // No further request payload is expected. EOF, cancellation, or an extra frame cancels this operation.
            await WorkerRpcProtocol.ReadAsync(pipe, operation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or JsonException) { }
        finally { operation.Cancel(); }
    }

    private static async Task SendFramesAsync(Stream pipe, ChannelReader<WorkerRpcFrame> frames,
        CancellationTokenSource operation)
    {
        try
        {
            await foreach (var frame in frames.ReadAllAsync(operation.Token).ConfigureAwait(false))
                await WorkerRpcProtocol.WriteAsync(pipe, frame, operation.Token).ConfigureAwait(false);
        }
        catch
        {
            operation.Cancel();
            throw;
        }
    }

    private sealed class RpcProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
