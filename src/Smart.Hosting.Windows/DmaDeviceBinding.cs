using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
namespace Smart.Hosting.Windows;

public sealed record D3xxDevice(int Index, uint Flags, DmaDeviceIdentity Identity);
public sealed record D3xxInventory(string DriverFileName, string DriverSha256, IReadOnlyList<D3xxDevice> Devices);
public interface ID3xxInventorySource : IDisposable
{
    D3xxInventory Read();
}
public interface IDmaBindingGuard : IDisposable
{
    string LeaseKey { get; }
    void Verify();
}
public interface IDmaBindingVerifier
{
    // Null is the explicitly unprotected legacy configuration; it does not enumerate or load DLLs.
    IDmaBindingGuard? Acquire(DmaSettings settings);
}
public sealed class DmaBindingException(string message) : InvalidOperationException(message);

public static class DmaBindingPolicy
{
    // A protected URI must select FT601 explicitly. Other transports do not use this D3XX index space.
    public static int ParseDeviceIndex(string uri)
    {
        if (uri is null || !uri.StartsWith("fpga://", StringComparison.OrdinalIgnoreCase))
            throw new DmaBindingException("Physical binding supports only explicit FT601 FPGA devices.");
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri[7..].Split(','))
        {
            var pair = part.Split('=');
            if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0 || !parameters.TryAdd(pair[0].ToLowerInvariant(), pair[1]))
                throw new DmaBindingException("Use unambiguous comma-separated FPGA URI parameters.");
        }
        if (!parameters.TryGetValue("ft601", out var ft601) || ft601 != "1" ||
            parameters.Keys.Any(key => key is not ("ft601" or "devindex")))
            throw new DmaBindingException("Protected binding requires exactly fpga://ft601=1,devindex=N; no alternate driver or transport options.");
        if (!parameters.TryGetValue("devindex", out var text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index is < 0 or > 63 || text != index.ToString(CultureInfo.InvariantCulture))
            throw new DmaBindingException("A protected binding requires a canonical decimal D3XX index from 0 through 63, without leading zeros.");
        return index;
    }
    public static DmaDeviceBinding Capture(string deviceUri, D3xxInventory inventory)
    {
        var index = ParseDeviceIndex(deviceUri);
        ValidateInventory(inventory);
        if (index >= inventory.Devices.Count) throw new DmaBindingException("The configured D3XX index is not present.");
        var selected = inventory.Devices[index];
        // Locations cannot distinguish identical-serial boards swapped between ports. Do not weaken this to a port-presence check.
        if (inventory.Devices.Count(x => x.Identity.SerialNumber == selected.Identity.SerialNumber) != 1)
            throw new DmaBindingException("The selected physical device is ambiguous: duplicate D3XX serial numbers cannot be protected, even at different locations.");
        var binding = new DmaDeviceBinding(index, selected.Identity, Fingerprint(inventory), inventory.DriverFileName, inventory.DriverSha256);
        binding.Validate(); return binding;
    }
    public static void Verify(DmaSettings settings, D3xxInventory inventory)
    {
        ValidateSettings(settings);
        var expected = settings.Binding ?? throw new DmaBindingException("No physical binding was configured.");
        expected.Validate();
        var actual = Capture(settings.DeviceUri, inventory);
        if (actual.DeviceIndex != expected.DeviceIndex || actual.Identity != expected.Identity ||
            !string.Equals(actual.DriverFileName, expected.DriverFileName, StringComparison.Ordinal) ||
            !string.Equals(actual.DriverSha256, expected.DriverSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actual.TopologySha256, expected.TopologySha256, StringComparison.OrdinalIgnoreCase))
            throw new DmaBindingException("DMA binding mismatch: index, physical identity, driver or ordered USB topology changed. Reconfirm the physical mapping before reconnecting.");
    }
    public static void ValidateSettings(DmaSettings settings)
    {
        if (settings.Binding is null) return;
        settings.Binding.Validate(); ParseDeviceIndex(settings.DeviceUri);
        // A remote/device override would make a local driver index meaningless. Keep the first protected mode explicit.
        if (settings.Arguments is null || settings.Arguments.Length > 32 || settings.Arguments.Any(argument =>
                argument is null || argument.ToLowerInvariant() is not ("-printf" or "-v" or "-vv" or "-vvv")))
            throw new DmaBindingException("Protected local binding allows only diagnostic VMM arguments (-printf/-v/-vv/-vvv); remote or backend overrides cannot be verified.");
    }
    public static string LeaseKey(DmaDeviceBinding binding) => "dma-physical:ft601:" + Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { binding.Identity.Type, binding.Identity.VendorProductId,
            binding.Identity.SerialNumber })));
    private static void ValidateInventory(D3xxInventory inventory)
    {
        if (inventory.Devices is null || inventory.Devices.Count is < 1 or > 64)
            throw new DmaBindingException("D3XX enumeration must return between 1 and 64 devices.");
        for (var index = 0; index < inventory.Devices.Count; index++)
        {
            var device = inventory.Devices[index];
            if (device is null || device.Index != index || device.Identity is null ||
                device.Identity.Type == 0 || device.Identity.VendorProductId == 0 ||
                string.IsNullOrWhiteSpace(device.Identity.SerialNumber) || string.IsNullOrWhiteSpace(device.Identity.Description))
                throw new DmaBindingException("D3XX returned incomplete identity data, often because another process owns a device. Binding cannot be verified.");
        }
    }
    private static string Fingerprint(D3xxInventory inventory) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(inventory.Devices.Select(x => new { x.Index, x.Identity }))));
}

public sealed class DmaBindingVerifier(Func<DmaSettings, ID3xxInventorySource>? open = null) : IDmaBindingVerifier
{
    public IDmaBindingGuard? Acquire(DmaSettings settings)
    {
        if (settings.Binding is null) return null;
        DmaBindingPolicy.ValidateSettings(settings);
        var source = open is null ? new D3xxDeviceInventory(settings.LibraryPath, settings.Binding.DriverFileName)
            : open(settings) ?? throw new InvalidOperationException("The configured D3XX inventory factory returned null.");
        try
        {
            var guard = new Guard(settings, source); guard.Verify(); return guard;
        }
        catch { source.Dispose(); throw; }
    }
    private sealed class Guard(DmaSettings settings, ID3xxInventorySource source) : IDmaBindingGuard
    {
        public string LeaseKey { get; } = DmaBindingPolicy.LeaseKey(settings.Binding!);
        private readonly object _sync = new();
        private bool _disposed;
        public void Verify()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Two complete enumerations reject topology changes during inventory creation.
                DmaBindingPolicy.Verify(settings, source.Read());
                DmaBindingPolicy.Verify(settings, source.Read());
            }
        }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; source.Dispose(); _disposed = true; }
        }
    }
}
