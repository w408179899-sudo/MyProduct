using System.Diagnostics;
using System.Text.Json;
using Smart.Adapters.Dma;
using Xunit;

namespace Smart.Isolation.Tests;

public sealed class InventoryIsolationTests
{
    private static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "fake-worker", "Smart.Dma.Worker.Fake.exe");
    private static bool IsAlive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    [WindowsFact] public async Task SuccessfulInventoryWorkerExitsAfterItsResponse()
    {
        var ready = Path.GetTempFileName();
        try
        {
            var result = await NativeWorkerCommand.RunInventoryAsync(JsonSerializer.Serialize(new { Mode = "normal", ReadyFile = ready }), workerPath: WorkerPath);
            Assert.Contains("Devices", result); Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(ready))));
        }
        finally { File.Delete(ready); }
    }
    [WindowsFact] public async Task HungInventoryIsTerminatedOnTimeout()
    {
        var ready = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => NativeWorkerCommand.RunInventoryAsync(JsonSerializer.Serialize(new { Mode = "hang", ReadyFile = ready }), 1500, WorkerPath));
            Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(ready))));
        }
        finally { File.Delete(ready); }
    }
    [WindowsFact] public async Task CancellationWaitsForInventoryWorkerExit()
    {
        var ready = Path.GetTempFileName(); using var stop = new CancellationTokenSource();
        try
        {
            var run = NativeWorkerCommand.RunInventoryAsync(JsonSerializer.Serialize(new { Mode = "hang", ReadyFile = ready }), workerPath: WorkerPath, token: stop.Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (new FileInfo(ready).Length == 0) await Task.Delay(10, deadline.Token);
            await stop.CancelAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(ready))));
        }
        finally { File.Delete(ready); }
    }
    [WindowsFact] public async Task OversizedInventoryResponseIsRejected()
    {
        var ready = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => NativeWorkerCommand.RunInventoryAsync(JsonSerializer.Serialize(new { Mode = "overflow", ReadyFile = ready }), workerPath: WorkerPath));
            Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(ready))));
        }
        finally { File.Delete(ready); }
    }
}
