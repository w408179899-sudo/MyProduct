using System.Collections.Immutable;
using System.Text.Json;
using SampleProject.Bootstrap;
using SampleProject.ConsoleHost;
using Smart.Adapters.Dma;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;

var options = ConsoleRunOptions.Parse(args);
if (options.ListDevices)
{
    Console.WriteLine(JsonSerializer.Serialize(DeviceDiscovery.ListUsbDevices(), new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var path = Path.GetFullPath(options.ConfigPath ?? Path.Combine(AppContext.BaseDirectory, "data", "accounts.json"));
var store = new JsonConfigStore<HostSettings>(path, 1, x => x.Validate());
if (!File.Exists(path)) await store.SaveAsync(new([new("local")]));
await using var logs = new JsonLineEventSink(Path.Combine(AppContext.BaseDirectory, "logs"));
var settings = await store.LoadAsync();
if (options.ProbeId is { } probeId)
{
    var profile = settings.Accounts.Single(x => x.Id == probeId);
    if (profile.Mode != RuntimeMode.Hardware) throw new ArgumentException("A read-only hardware probe requires a hardware profile.");
    await using var pool = new VmmConnectionPool(new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory));
    await using var connection = await pool.AcquireAsync(profile.Dma!, default);
    var transport = (IsolatedVmmTransport)connection.Transport;
    Console.WriteLine(JsonSerializer.Serialize(new { transport.UsesScatter, transport.WorkerProcessId, Processes = transport.ListProcesses(profile.Dma!.ModuleName) }));
    return;
}
await using var workspace = new AccountWorkspace(store, new ProjectHost(logs), logs);
await workspace.LoadAsync();
using var shutdown = new CancellationTokenSource();
Task cancellation = Task.CompletedTask;
var cancellationGate = new object(); var listening = true;
ConsoleCancelEventHandler cancel = (_, e) =>
{
    e.Cancel = true;
    lock (cancellationGate)
        if (listening && !shutdown.IsCancellationRequested) cancellation = shutdown.CancelAsync();
};
Console.CancelKeyPress += cancel;
try
{
    var results = await ConsoleAccountRunner.RunAsync(workspace.Accounts, options, shutdown.Token,
        failure => Console.Error.WriteLine(JsonSerializer.Serialize(failure)));
    foreach (var result in results) Console.WriteLine(JsonSerializer.Serialize(result));
    if (results.Any(x => x.Failure is not null || x.Status.State != SessionState.Stopped || x.Status.Generation == 0)) Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= cancel;
    Task pendingCancellation;
    lock (cancellationGate) { listening = false; pendingCancellation = cancellation; }
    await pendingCancellation;
}
