using System.Runtime.InteropServices;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.NativeSmoke;
using Smart.Runtime;
namespace Smart.SnapshotSmoke;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"]) { Console.WriteLine(SnapshotSmokeCommand.Usage); return 0; }
        try
        {
            var command = SnapshotSmokeCommand.Parse(args);
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("The snapshot smoke tool requires Windows x64.");
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
                Func<IProcessMemoryTransport> connect = () => command.Isolated
                    ? new IsolatedVmmTransport(command.Library, command.Device, command.VmmArguments(), cancellationToken: shutdown.Token)
                    : new VmmTransport(command.Library, command.Device, command.VmmArguments());
                // The isolated worker holds the physical lease until native cleanup has completed.
                var leases = command.Isolated ? new InputLeaseRegistry() : new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory);
                return await ExecuteAsync(command, connect, leases, Console.Out, shutdown.Token).ConfigureAwait(false);
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
            Console.Error.WriteLine(JsonSerializer.Serialize(new { Passed = false, Outcome = "Failed", Error = error.GetType().Name + ": " + error.Message }));
            return 2;
        }
    }
    // The same executable path can be verified using a fake transport without loading any native library.
    public static async Task<int> ExecuteAsync(NativeSmokeCommand command, Func<IProcessMemoryTransport> connect,
        InputLeaseRegistry leases, TextWriter output, CancellationToken token = default)
    {
        SnapshotSmokeReport report;
        await using (var runner = new SnapshotSmokeRunner(connect, leases, command.Device))
            report = await runner.RunAsync(command, token).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(report)).ConfigureAwait(false);
        return report.Passed ? 0 : 2;
    }
}
