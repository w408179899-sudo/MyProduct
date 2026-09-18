using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Smart.Adapters.Dma;
using Smart.Dma.Worker;

namespace Smart.Dma.Worker.Fake;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        if (args is ["--inventory"])
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false); Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var request = System.Text.Json.JsonSerializer.Deserialize<InventoryRequest>(Console.ReadLine()!)!;
            File.WriteAllText(request.ReadyFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (request.Mode == "hang") Thread.Sleep(Timeout.Infinite);
            if (request.Mode == "overflow") Console.Write(new string('X', 140000));
            else Console.Write("{\"Devices\":[]}");
            return Task.FromResult(0);
        }
        if (args is ["--parent-death-host", var leaseDirectory, var readyFile, var device])
        {
            // No native factory is reachable: this client launches the same fake executable below.
            var executable = Environment.ProcessPath!;
            using var transport = new IsolatedVmmTransport(executable, device,
                VmmTransport.CreateArguments(device, ["-fake-mode", "normal"]), new()
                { WorkerPath = executable, LeaseDirectory = leaseDirectory, StartupTimeoutMs = 5000, OperationTimeoutMs = 1000, ShutdownTimeoutMs = 1000 });
            File.WriteAllText(readyFile, transport.WorkerProcessId.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(Timeout.Infinite);
            return Task.FromResult(0);
        }
        return WorkerServer.RunAsync(args, startup =>
        {
            var index = Array.IndexOf(startup.Arguments, "-fake-mode");
            var mode = index >= 0 && index + 1 < startup.Arguments.Length ? startup.Arguments[index + 1] : "normal";
            File.WriteAllText(Path.Combine(startup.LeaseDirectory, $"fake-worker-{Environment.ProcessId}.started"), mode);
            if (mode == "hang-init") Thread.Sleep(Timeout.Infinite);
            if (mode == "crash-init") Environment.Exit(76);
            if (mode == "dll-missing") throw new DllNotFoundException("Controlled missing native library.");
            if (mode == "bad-image") throw new BadImageFormatException("Controlled native architecture mismatch.");
            if (mode == "entry-missing") throw new EntryPointNotFoundException("Controlled missing native export.");
            return new FakeTransport(startup.DeviceId, startup.LeaseDirectory, mode);
        });
    }
    private sealed record InventoryRequest(string Mode, string ReadyFile);
    private sealed class FakeTransport(string device, string leaseDirectory, string mode) : IProcessMemoryTransport
    {
        private int _reads;
        public string DeviceId => device;
        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null) => [GetProcess(42, requiredModule ?? "fixture.exe")];
        public ProcessBinding GetProcess(int pid, string module)
        {
            if (pid == 404) throw new TargetProcessUnavailableException(pid);
            return new(pid, "isolated-fixture", "start-" + Environment.ProcessId, 0x100000, new string('A', 64));
        }
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
        {
            if (mode != "performance") File.WriteAllText(Path.Combine(leaseDirectory, $"fake-worker-{Environment.ProcessId}.read"), mode);
            if (mode == "hang-read") Thread.Sleep(Timeout.Infinite);
            if (mode == "crash-read") Environment.Exit(77);
            if (mode == "read-error-once" && Interlocked.Increment(ref _reads) == 1) throw new IOException("Controlled ordinary read error.");
            return requests.Select(request => request.Address switch
            {
                0 => new MemoryBlock(request.Address, [0xDE, 0xAD], false),
                0xBAD => new MemoryBlock(request.Address, new byte[request.Length - 1].ToImmutableArray(), true),
                _ => new MemoryBlock(request.Address, Enumerable.Range(0, request.Length).Select(offset => (byte)((request.Address + (ulong)offset) & 0xFF)).ToImmutableArray(), true)
            }).ToImmutableArray();
        }
        public void Dispose()
        {
            File.WriteAllText(Path.Combine(leaseDirectory, $"fake-worker-{Environment.ProcessId}.close"), mode);
            if (mode == "hang-close") Thread.Sleep(Timeout.Infinite);
        }
    }
}
