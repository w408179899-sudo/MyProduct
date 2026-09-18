using System.Net;
using System.Text.Json;
using Hardware.KmBox;
using Smart.Adapters.Dma;
using Smart.Adapters.KmBox;
using Smart.Runtime;

namespace Smart.Hosting.Windows;

public sealed record DmaProbeResult(ProcessBinding Process, bool ModuleHeaderVerified);
public interface IHardwareDiagnostics
{
    Task<D3xxInventory> DiscoverAsync(string libraryPath, string driverFileName, CancellationToken token);
    Task<IReadOnlyList<ProcessBinding>> ListProcessesAsync(DmaSettings settings, CancellationToken token);
    Task<DmaProbeResult> ProbeAsync(DmaSettings settings, CancellationToken token);
    Task TestInputAsync(InputSettings settings, CancellationToken token);
}

// Diagnostic results describe connection tests only; business data continues through typed snapshots.
public sealed class HardwareDiagnostics : IHardwareDiagnostics
{
    public async Task<D3xxInventory> DiscoverAsync(string libraryPath, string driverFileName, CancellationToken token)
    {
        var json = await NativeWorkerCommand.RunInventoryAsync(JsonSerializer.Serialize(new { LibraryPath = libraryPath, DriverFileName = driverFileName }), token: token).ConfigureAwait(false);
        var inventory = JsonSerializer.Deserialize<D3xxInventory>(json) ?? throw new InvalidDataException("Empty device inventory.");
        if (inventory.Devices.Count is < 1 or > 64) throw new InvalidDataException("Invalid device inventory size.");
        return inventory;
    }
    public Task<IReadOnlyList<ProcessBinding>> ListProcessesAsync(DmaSettings settings, CancellationToken token) =>
        WithConnectionAsync(settings, transport => transport.ListProcesses(), token);
    public Task<DmaProbeResult> ProbeAsync(DmaSettings settings, CancellationToken token) => WithConnectionAsync(settings, transport =>
    {
        var selected = HardwareSessionFactory.Select(transport.ListProcesses(), settings, requireModule: false);
        var process = transport.GetProcess(selected.ProcessId, settings.ModuleName);
        if (process.ModuleBase == 0) throw new IOException("目标模块尚未加载。");
        var block = transport.ReadBatch(process.ProcessId, [new(process.ModuleBase, 2)]).Single();
        if (!block.Complete || block.Bytes.Length != 2 || block.Bytes[0] != 'M' || block.Bytes[1] != 'Z')
            throw new IOException("目标模块头读取未通过，请检查进程和模块配置。");
        return new DmaProbeResult(process, true);
    }, token);
    private static async Task<T> WithConnectionAsync<T>(DmaSettings settings, Func<IProcessMemoryTransport, T> inspect, CancellationToken token)
    {
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory));
        await using var connection = await pool.AcquireAsync(settings, token).ConfigureAwait(false);
        var result = await Task.Run(() => inspect(connection.Transport), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return result;
    }
    public async Task TestInputAsync(InputSettings settings, CancellationToken token)
    {
        var options = new KmBoxOptions { IpAddress = settings.Address, Port = settings.Port, Mac = settings.Mac };
        options = options.CloneAndValidate();
        options.IpAddress = IPAddress.Parse(options.IpAddress).ToString();
        using var lease = new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory).Acquire($"kmbox:{options.IpAddress}:{options.Port}");
        await KmBoxConnectionTest.ConnectAsync(options, token).ConfigureAwait(false);
    }
}
