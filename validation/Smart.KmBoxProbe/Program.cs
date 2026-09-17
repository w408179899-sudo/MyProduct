using System.Globalization;
using System.Text.Json;
using Smart.Runtime;

namespace Smart.KmBoxProbe;

public static class Program
{
    public const string Usage = "Smart.KmBoxProbe --config C:\\absolute\\kmbox-net.json [--timeout-ms 1000]\nSends one Connect handshake only; no key, mouse, release-all, or reboot commands. Firmware-internal Connect effects cannot be guaranteed by this client code.";
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"]) { Console.WriteLine(Usage); return 0; }
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
                if (i + 1 >= args.Length || args[i] is not ("--config" or "--timeout-ms") || !values.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Unknown, repeated, or incomplete argument. " + Usage);
            if (!values.TryGetValue("--config", out var config)) throw new ArgumentException("Explicit --config is required.");
            var milliseconds = 1000;
            if (values.TryGetValue("--timeout-ms", out var duration) &&
                !int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds))
                throw new ArgumentException("Timeout must be integer milliseconds.");
            var options = await KmBoxProbeOptions.LoadAsync(config, TimeSpan.FromMilliseconds(milliseconds));
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
                var report = await ConnectProbe.RunAsync(options, new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory), stop.Token);
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                return report.Connected ? 0 : report.Outcome == "Cancelled" ? 130 : 2;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                Task pending;
                lock (cancellationGate) { listening = false; pending = cancellation; }
                await pending;
            }
        }
        catch (JsonException) { Console.Error.WriteLine("KMBox configuration contains invalid JSON or field types."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
    }
}
