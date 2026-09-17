using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Smart.Adapters.Dma;

public sealed record VmmWorkerOptions
{
    public string? WorkerPath { get; init; }
    public int StartupTimeoutMs { get; init; } = 30000;
    public int OperationTimeoutMs { get; init; } = 5000;
    public int ShutdownTimeoutMs { get; init; } = 3000;
    public string? LeaseDirectory { get; init; }
    public string? ProfileJson { get; init; }
    internal void Validate()
    {
        if (StartupTimeoutMs is < 100 or > 120000 || OperationTimeoutMs is < 100 or > 120000 || ShutdownTimeoutMs is < 100 or > 120000)
            throw new ArgumentException("Worker deadlines must be between 100 and 120000 milliseconds.");
        if (WorkerPath is not null && !Path.IsPathFullyQualified(WorkerPath)) throw new ArgumentException("Worker executable path must be absolute.");
        if (LeaseDirectory is not null && !Path.IsPathFullyQualified(LeaseDirectory)) throw new ArgumentException("Worker lease directory must be absolute.");
        if (ProfileJson?.Length > 131072) throw new ArgumentException("Worker profile exceeds its startup budget.");
    }
}
public interface IMemoryConnectionLifecycle
{
    bool IsConnected { get; }
    event Action? Disconnected;
}
public sealed class MemoryConnectionLostException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class MemoryWorkerConfigurationException(string message) : ArgumentException(message);

// One process per physical connection, with one bounded binary RPC per batch, never one per field.
// The worker owns the physical file leases. Its lifetime is enclosed by a private Windows job.
public sealed class IsolatedVmmTransport : IProcessMemoryTransport, IMemoryConnectionLifecycle
{
    private readonly object _gate = new();
    private readonly VmmWorkerOptions _options;
    private readonly NamedPipeServerStream _pipe;
    private readonly WorkerJob _job;
    private readonly Process _process;
    private readonly Task _stdout, _stderr;
    private readonly StringBuilder _diagnostic = new();
    private int _sequence, _disconnected;
    private bool _disposed, _closed, _assigned;
    public string DeviceId { get; }
    public string ConnectionId { get; private set; } = "";
    public bool UsesScatter { get; private set; }
    public int WorkerProcessId => _process.Id;
    public bool IsConnected => Volatile.Read(ref _disconnected) == 0 && !_process.HasExited;
    public event Action? Disconnected;
    public string LastWorkerOutput { get { lock (_diagnostic) return _diagnostic.ToString(); } }

    public IsolatedVmmTransport(string libraryPath, string deviceId, IReadOnlyList<string> arguments,
        VmmWorkerOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Native process isolation requires Windows x64.");
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!Path.IsPathFullyQualified(libraryPath)) throw new ArgumentException("Use an absolute native library path.");
        ArgumentNullException.ThrowIfNull(arguments);
        VmmTransport.ValidateWorkerArguments(arguments, deviceId.Trim());
        _options = options ?? new(); _options.Validate(); DeviceId = deviceId.Trim();
        var executable = _options.WorkerPath ?? Path.Combine(AppContext.BaseDirectory, "native-worker", "Smart.Dma.Worker.exe");
        if (!File.Exists(executable)) throw new MemoryWorkerConfigurationException("Native worker is missing: " + executable + ". Build/publish the complete host payload; no in-process fallback is performed.");
        var pipeName = "Smart.Dma." + Guid.NewGuid().ToString("N");
        _pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        try { _job = new(); }
        catch { _pipe.Dispose(); throw; }
        _process = new() { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)! }, EnableRaisingEvents = true };
        _process.StartInfo.ArgumentList.Add("--pipe"); _process.StartInfo.ArgumentList.Add(pipeName);
        _process.StartInfo.ArgumentList.Add("--parent-pid"); _process.StartInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Framework-dependent worker apphosts use the same .NET installation as a dotnet-launched host.
        var host = Environment.ProcessPath;
        if (host is not null && Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            _process.StartInfo.Environment["DOTNET_ROOT_X64"] = Path.GetDirectoryName(host)!;
        _stdout = _stderr = Task.CompletedTask;
        var started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_process.Start()) throw new IOException("Cannot start native worker.");
            started = true;
            _process.Exited += (_, _) => NotifyDisconnected();
            _stdout = DrainOutputAsync(_process.StandardOutput); _stderr = DrainOutputAsync(_process.StandardError);
            _job.Assign(_process); // Worker waits for Initialize; no native device can open before this succeeds.
            _assigned = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_options.StartupTimeoutMs); // Only private pipe I/O callbacks are registered here.
            _pipe.WaitForConnectionAsync(deadline.Token).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out var client) || client != (uint)_process.Id)
                throw new IOException("Unexpected native worker pipe peer.");
            var leaseDirectory = _options.LeaseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smart", "device-leases");
            var startup = new WorkerStartup(libraryPath, DeviceId, arguments.ToArray(), leaseDirectory, _options.ProfileJson);
            using var response = ExchangeAsync(WorkerOperation.Initialize,
                writer => WorkerProtocol.WriteString(writer, JsonSerializer.Serialize(startup), 262144), deadline.Token).GetAwaiter().GetResult();
            using var reader = new BinaryReader(response, WorkerProtocol.Utf8);
            CheckStatus(reader);
            ConnectionId = WorkerProtocol.ReadString(reader); UsesScatter = reader.ReadBoolean(); WorkerProtocol.End(reader);
            if (string.IsNullOrWhiteSpace(ConnectionId)) throw new InvalidDataException("Worker did not return a connection identity.");
        }
        catch (Exception error)
        {
            NotifyDisconnected();
            // Closing the private job is the last-resort kernel cleanup even if assignment/handshake failed.
            try { if (started) TerminateAndWait(); }
            finally { _job.Dispose(); _pipe.Dispose(); _process.Dispose(); }
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException("Native worker initialization exceeded its startup deadline.", error);
            throw;
        }
    }
    public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null) => Call(WorkerOperation.ListProcesses,
        writer => { writer.Write(requiredModule is not null); if (requiredModule is not null) WorkerProtocol.WriteString(writer, requiredModule); },
        reader =>
        {
            var count = reader.ReadInt32();
            if (count is < 0 or > WorkerProtocol.MaximumProcesses) throw new InvalidDataException("Worker process count exceeds its budget.");
            var result = new List<ProcessBinding>(count);
            for (var i = 0; i < count; i++) result.Add(WorkerProtocol.ReadBinding(reader));
            return result;
        });
    public ProcessBinding GetProcess(int pid, string module)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pid); ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return Call(WorkerOperation.GetProcess, writer => { writer.Write(pid); WorkerProtocol.WriteString(writer, module); }, WorkerProtocol.ReadBinding);
    }
    public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
    {
        WorkerProtocol.ValidateReads(processId, requests);
        return Call(WorkerOperation.ReadBatch, writer =>
        {
            writer.Write(processId); writer.Write(requests.Count);
            foreach (var request in requests) { writer.Write(request.Address); writer.Write(request.Length); }
        }, reader => WorkerProtocol.ReadBlocks(reader, requests));
    }
    private T Call<T>(WorkerOperation operation, Action<BinaryWriter>? write, Func<BinaryReader, T> read)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsConnected) throw new MemoryConnectionLostException("The native worker connection ended.");
            using var deadline = new CancellationTokenSource(_options.OperationTimeoutMs);
            try
            {
                using var response = ExchangeAsync(operation, write, deadline.Token).GetAwaiter().GetResult();
                using var reader = new BinaryReader(response, WorkerProtocol.Utf8);
                CheckStatus(reader); var value = read(reader); WorkerProtocol.End(reader); return value;
            }
            catch (RemoteReadException ex) { throw new IOException(ex.Message); }
            catch (TargetProcessUnavailableException) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or DecoderFallbackException or OperationCanceledException or MemoryWorkerConfigurationException)
            {
                NotifyDisconnected();
                try { TerminateAndWait(); }
                catch (Exception cleanup) { throw new MemoryConnectionLostException("Native worker failed and its termination remains unconfirmed.", new AggregateException(ex, cleanup)); }
                if (ex is MemoryWorkerConfigurationException) throw;
                throw new MemoryConnectionLostException("Native worker timed out, exited, or returned an invalid response. The connection is retired.", ex);
            }
        }
    }
    private async Task<MemoryStream> ExchangeAsync(WorkerOperation operation, Action<BinaryWriter>? write, CancellationToken token)
    {
        var sequence = unchecked(++_sequence); // Match the server's Int32 rollover during long-running connections.
        using var request = WorkerProtocol.Message(sequence, operation, write);
        await WorkerProtocol.WriteAsync(_pipe, request, token).ConfigureAwait(false);
        var response = await WorkerProtocol.ReadAsync(_pipe, token).ConfigureAwait(false);
        try
        {
            using var reader = new BinaryReader(response, WorkerProtocol.Utf8, leaveOpen: true);
            if (reader.ReadInt32() != WorkerProtocol.Version || reader.ReadInt32() != sequence || reader.ReadByte() != (byte)operation)
                throw new InvalidDataException("Native worker protocol/sequence mismatch.");
            return response;
        }
        catch { response.Dispose(); throw; }
    }
    private sealed class RemoteReadException(string message) : IOException(message);
    private static void CheckStatus(BinaryReader reader)
    {
        var status = (WorkerStatus)reader.ReadByte();
        if (status == WorkerStatus.Success) return;
        var message = WorkerProtocol.ReadString(reader); WorkerProtocol.End(reader);
        throw status switch
        {
            WorkerStatus.ReadError => new RemoteReadException(message),
            WorkerStatus.ProcessExited => new TargetProcessUnavailableException(int.TryParse(message, out var pid) ? pid : 0),
            WorkerStatus.ConfigurationError => new MemoryWorkerConfigurationException(message),
            _ => new InvalidDataException("Unknown worker response status.")
        };
    }
    private void NotifyDisconnected()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0) return;
        foreach (var callback in Disconnected?.GetInvocationList() ?? [])
            try { ((Action)callback)(); }
            catch (Exception ex) { AppendDiagnostic("Lifecycle observer: " + ex.Message); }
    }
    private async Task DrainOutputAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try { int count; while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0) AppendDiagnostic(new string(buffer, 0, count)); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    private void AppendDiagnostic(string value)
    {
        lock (_diagnostic) { _diagnostic.Append(value); if (_diagnostic.Length > 4096) _diagnostic.Remove(0, _diagnostic.Length - 4096); }
    }
    private void TerminateAndWait()
    {
        if (!_process.HasExited)
        {
            try { if (_assigned) _job.Terminate(); else _process.Kill(entireProcessTree: true); }
            catch (Win32Exception) { _process.Kill(entireProcessTree: true); }
            if (!_process.WaitForExit(_options.ShutdownTimeoutMs))
                throw new IOException("Native worker termination is not confirmed; physical ownership remains in the worker.");
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _disposed = true;
            NotifyDisconnected();
            if (!_process.HasExited)
            {
                try
                {
                    using var deadline = new CancellationTokenSource(_options.ShutdownTimeoutMs);
                    using var response = ExchangeAsync(WorkerOperation.Close, null, deadline.Token).GetAwaiter().GetResult();
                    using var reader = new BinaryReader(response, WorkerProtocol.Utf8); CheckStatus(reader); WorkerProtocol.End(reader);
                    if (!_process.WaitForExit(_options.ShutdownTimeoutMs)) TerminateAndWait();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ArgumentException) { TerminateAndWait(); }
            }
            _pipe.Dispose(); _job.Dispose();
            Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromMilliseconds(_options.ShutdownTimeoutMs)).GetAwaiter().GetResult();
            _process.Dispose(); _closed = true;
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
}
