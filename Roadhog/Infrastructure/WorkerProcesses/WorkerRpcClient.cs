using System.IO.Pipes;
using System.Text.Json;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed class WorkerRpcClient
{
    private readonly string _pipeName;
    private readonly string _token;
    private readonly Func<CancellationToken, Task<WorkerRpcClient>>? _resolve;

    public WorkerRpcClient(string pipeName, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _pipeName = pipeName;
        _token = token;
    }

    public WorkerRpcClient(Func<CancellationToken, Task<WorkerRpcClient>> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
        _pipeName = string.Empty;
        _token = string.Empty;
    }

    public async Task<T> CallAsync<T>(string method, object?[] arguments,
        CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        if (_resolve is not null)
        {
            var client = await _resolve(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(client, this)) throw new InvalidOperationException("Worker RPC resolver returned itself.");
            return await client.CallAsync<T>(method, arguments, cancellationToken, progress).ConfigureAwait(false);
        }
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var disconnectedOnCancellation = cancellationToken.Register(() => pipe.Dispose());
        try
        {
            await pipe.ConnectAsync(10_000, cancellationToken).ConfigureAwait(false);
            await WorkerRpcProtocol.WriteAsync(pipe, new WorkerRpcFrame
            {
                Kind = "request", Token = _token, Method = method,
                Arguments = arguments.Select(value => JsonSerializer.SerializeToElement(value,
                    value?.GetType() ?? typeof(object), WorkerRpcProtocol.Json)).ToArray()
            }, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var frame = await WorkerRpcProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("The account worker disconnected before returning a result.");
                switch (frame.Kind)
                {
                    case "progress":
                        progress?.Report(frame.Message ?? string.Empty);
                        break;
                    case "result":
                        return frame.Value is { } value && value.ValueKind != JsonValueKind.Null
                            ? value.Deserialize<T>(WorkerRpcProtocol.Json)!
                            : default!;
                    case "cancelled":
                        throw new OperationCanceledException(frame.Message ?? "The account operation was cancelled.", cancellationToken);
                    case "error":
                        throw new WorkerRpcException(frame.Message ?? "The account operation failed.");
                    default:
                        throw new InvalidDataException("Unexpected worker RPC response.");
                }
            }
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested
            && exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            throw new OperationCanceledException("The account operation was cancelled.", exception, cancellationToken);
        }
    }
}

public sealed class WorkerRpcException : Exception
{
    public WorkerRpcException(string message) : base(message) { }
}
