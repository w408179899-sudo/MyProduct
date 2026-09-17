using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Smart.Adapters.Dma;
using Smart.ProbeProtocol;
using Smart.Runtime;

namespace Smart.HardwareProbe;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"]) { Console.WriteLine(ProbeCommand.Usage); return 0; }
        try
        {
            var command = ProbeCommand.Parse(args);
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("The native hardware probe requires Windows x64.");
            if (!File.Exists(command.LibraryPath)) throw new FileNotFoundException("The explicitly selected native library is missing.", command.LibraryPath);
            await using var manifestStream = new FileStream(command.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (manifestStream.Length is <= 0 or > 65536) throw new ArgumentException("Fixture manifest must exist and be at most 64 KiB.");
            var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            var manifest = await JsonSerializer.DeserializeAsync<ProbeManifest>(manifestStream, json)
                ?? throw new ArgumentException("Fixture manifest cannot be null.");
            manifest.Validate();
            using var stop = new CancellationTokenSource();
            var cancellationGate = new object(); var listening = true; Task cancellation = Task.CompletedTask;
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                lock (cancellationGate)
                    if (listening && !stop.IsCancellationRequested) cancellation = stop.CancelAsync();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                await using var runner = new HardwareProbeRunner(() => new VmmTransport(command.LibraryPath, command.DeviceUri,
                    VmmTransport.CreateArguments(command.DeviceUri)), new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory), command.DeviceUri);
                var report = await runner.RunAsync(manifest, command.Run, stop.Token);
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                return report.Passed ? 0 : report.Outcome == "Cancelled" ? 130 : 2;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                Task pending;
                lock (cancellationGate) { listening = false; pending = cancellation; }
                await pending;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
    }
}
