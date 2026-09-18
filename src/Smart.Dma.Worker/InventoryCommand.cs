using System.Text.Json;
using Smart.Hosting.Windows;

namespace Smart.Dma.Worker;

internal static class InventoryCommand
{
    internal sealed record Request(string LibraryPath, string DriverFileName);
    internal static async Task<int> RunAsync()
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false); Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var input = await Console.In.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (input is null || input.Length > 16384) throw new ArgumentException("Invalid inventory request.");
            var request = JsonSerializer.Deserialize<Request>(input) ?? throw new ArgumentException("Missing inventory request.");
            using var source = new D3xxDeviceInventory(request.LibraryPath, request.DriverFileName);
            var first = source.Read(); var second = source.Read();
            if (!first.Devices.SequenceEqual(second.Devices)) throw new IOException("设备列表发生变化，请等待设备稳定后刷新。");
            Console.Write(JsonSerializer.Serialize(second)); return 0;
        }
        catch (Exception ex) { Console.Error.Write(ex.Message.Length > 2048 ? ex.Message[..2048] : ex.Message); return 2; }
    }
}
