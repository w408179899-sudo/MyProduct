using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.Hosting.Windows;
using Smart.Runtime;

namespace Smart.Dma.Worker;

internal static class WorkerServer
{
    // Factory injection is compiled into a separate test executable, never selected by production arguments.
    internal static async Task<int> RunAsync(string[] args, Func<WorkerStartup, IProcessMemoryTransport> connect)
    {
        if (args.Length == 0 || args is ["--help"])
        { Console.WriteLine("Smart native worker. Started by the DMA adapter; no device is opened without its private pipe handshake."); return 0; }
        if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--parent-pid" ||
            !int.TryParse(args[3], out var parentPid) || parentPid <= 0 || args[1].Length > 128) return 2;
        IProcessMemoryTransport? transport = null; IDisposable? lease = null;
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using (var handshake = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await pipe.ConnectAsync(handshake.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != (uint)parentPid)
                throw new InvalidDataException("Unexpected native worker owner.");
            var expectedSequence = 1;
            while (true)
            {
                using var frame = await WorkerProtocol.ReadAsync(pipe, default).ConfigureAwait(false);
                using var reader = new BinaryReader(frame, WorkerProtocol.Utf8);
                if (reader.ReadInt32() != WorkerProtocol.Version || reader.ReadInt32() != expectedSequence++)
                    throw new InvalidDataException("Invalid native worker protocol sequence.");
                var operation = (WorkerOperation)reader.ReadByte();
                var closing = false;
                MemoryStream response;
                try
                {
                    response = WorkerProtocol.Message(expectedSequence - 1, operation, writer =>
                    {
                        writer.Write((byte)WorkerStatus.Success);
                        switch (operation)
                        {
                            case WorkerOperation.Initialize:
                                if (transport is not null || expectedSequence != 2) throw new InvalidDataException("Duplicate native initialization.");
                                var startup = JsonSerializer.Deserialize<WorkerStartup>(WorkerProtocol.ReadString(reader, 262144)) ?? throw new InvalidDataException("Missing worker initialization.");
                                WorkerProtocol.End(reader);
                                if (!Path.IsPathFullyQualified(startup.LibraryPath) || !Path.IsPathFullyQualified(startup.LeaseDirectory))
                                    throw new ArgumentException("Native worker paths must be absolute.");
                                VmmTransport.ValidateWorkerArguments(startup.Arguments, startup.DeviceId.Trim());
                                lease = new InputLeaseRegistry(startup.LeaseDirectory).Acquire("dma:" + startup.DeviceId.Trim());
                                transport = connect(startup);
                                if (transport.DeviceId != startup.DeviceId) throw new InvalidDataException("Connected the wrong native device.");
                                WorkerProtocol.WriteString(writer, transport.ConnectionId);
                                writer.Write(transport is NativeConnection connection && connection.UsesScatter);
                                break;
                            case WorkerOperation.ListProcesses:
                                RequireTransport();
                                var module = reader.ReadBoolean() ? WorkerProtocol.ReadString(reader) : null;
                                WorkerProtocol.End(reader);
                                var processes = transport!.ListProcesses(module);
                                if (processes.Count > WorkerProtocol.MaximumProcesses) throw new InvalidDataException("Process list exceeds its count budget.");
                                writer.Write(processes.Count);
                                foreach (var process in processes) WorkerProtocol.WriteBinding(writer, process);
                                break;
                            case WorkerOperation.GetProcess:
                                RequireTransport();
                                var pid = reader.ReadInt32(); var requiredModule = WorkerProtocol.ReadString(reader); WorkerProtocol.End(reader);
                                WorkerProtocol.WriteBinding(writer, transport!.GetProcess(pid, requiredModule));
                                break;
                            case WorkerOperation.ReadBatch:
                                RequireTransport();
                                var processId = reader.ReadInt32(); var count = reader.ReadInt32();
                                if (count is < 1 or > 256) throw new InvalidDataException("Read batch exceeds its count budget.");
                                var reads = new MemoryReadRequest[count];
                                for (var i = 0; i < count; i++) reads[i] = new(reader.ReadUInt64(), reader.ReadInt32());
                                WorkerProtocol.End(reader); WorkerProtocol.ValidateReads(processId, reads);
                                var blocks = transport!.ReadBatch(processId, reads);
                                if (blocks.Length != count) throw new InvalidDataException("Native transport returned an invalid block count.");
                                writer.Write(count);
                                for (var i = 0; i < count; i++)
                                {
                                    var block = blocks[i];
                                    if (block.Address != reads[i].Address) throw new InvalidDataException("Native transport returned an invalid address.");
                                    var complete = block.Complete && block.Bytes.Length == reads[i].Length;
                                    writer.Write(block.Address); writer.Write(complete); writer.Write(complete ? block.Bytes.Length : 0);
                                    if (complete) writer.Write(block.Bytes.AsSpan());
                                }
                                break;
                            case WorkerOperation.Close:
                                WorkerProtocol.End(reader);
                                transport?.Dispose(); transport = null; lease?.Dispose(); lease = null; closing = true;
                                break;
                            default: throw new InvalidDataException("Unknown worker operation.");
                        }
                        void RequireTransport() { if (transport is null) throw new InvalidDataException("Worker is not initialized."); }
                    });
                }
                catch (Exception ex)
                {
                    var status = ex is TargetProcessUnavailableException ? WorkerStatus.ProcessExited :
                        ex is ArgumentException or DmaBindingException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                            ? WorkerStatus.ConfigurationError : WorkerStatus.ReadError;
                    var detail = ex is TargetProcessUnavailableException ? ExtractPid(ex.Message) : ex.GetType().Name + ": " + ex.Message;
                    if (detail.Length > 2048) detail = detail[..2048];
                    response = WorkerProtocol.Message(expectedSequence - 1, operation, writer =>
                    { writer.Write((byte)status); WorkerProtocol.WriteString(writer, detail); });
                }
                using (response) await WorkerProtocol.WriteAsync(pipe, response, default).ConfigureAwait(false);
                if (closing) return 0;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
        finally { transport?.Dispose(); lease?.Dispose(); }
    }
    private static string ExtractPid(string message) => message[(message.LastIndexOf(':') + 1)..].Trim();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
}
