using System.Runtime.InteropServices;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.Runtime;
namespace Smart.NativeSmoke;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"]) { Console.WriteLine(NativeSmokeCommand.Usage); return 0; }
        try
        {
            var command = NativeSmokeCommand.Parse(args);
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("The native smoke tool requires Windows x64.");
            if (!File.Exists(command.Library)) throw new FileNotFoundException("The selected native library does not exist.", command.Library);
            using var shutdown = new CancellationTokenSource();
            var gate = new object(); var listening = true; Task cancellation = Task.CompletedTask;
            ConsoleCancelEventHandler cancel = (_, e) =>
            {
                e.Cancel = true;
                lock (gate) if (listening && !shutdown.IsCancellationRequested) cancellation = shutdown.CancelAsync();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                NativeSmokeReport report;
                Func<IProcessMemoryTransport> connect = () => command.Isolated
                    ? new IsolatedVmmTransport(command.Library, command.Device, command.VmmArguments(), cancellationToken: shutdown.Token)
                    : new VmmTransport(command.Library, command.Device, command.VmmArguments());
                // In isolated mode the worker owns the physical file lease; this scope only tracks local ownership.
                var leases = command.Isolated ? new InputLeaseRegistry() : new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory);
                await using (var runner = new NativeSmokeRunner(connect, leases, command.Device))
                    report = await runner.RunAsync(command, shutdown.Token).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report));
                return report.Passed ? 0 : 2;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
                Task pending;
                lock (gate) { listening = false; pending = cancellation; }
                await pending.ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            // A handled exit permits native printf streams to flush and avoids a managed abort.
            Console.Error.WriteLine(JsonSerializer.Serialize(new { Passed = false, Outcome = "Failed", Error = error.GetType().Name + ": " + error.Message }));
            return 2;
        }
    }
}
