using Microsoft.Win32;
namespace Smart.Hosting.Windows;

public sealed record UsbDeviceInfo(string InstancePath, string Name);
public static class DeviceDiscovery
{
    public static D3xxInventory ListDmaDevices(string vmmLibraryPath, string driverFileName = "FTD3XX.dll")
    {
        using var source = new D3xxDeviceInventory(vmmLibraryPath, driverFileName);
        var first = source.Read(); var second = source.Read();
        if (!first.Devices.Select(x => (x.Index, x.Identity)).SequenceEqual(second.Devices.Select(x => (x.Index, x.Identity))))
            throw new DmaBindingException("D3XX topology changed during discovery. Wait until devices are stable and enumerate again.");
        return second;
    }
    // Informational inventory only. USB enumeration order is not a VMM devindex mapping.
    public static IReadOnlyList<UsbDeviceInfo> ListUsbDevices()
    {
        var devices = new List<UsbDeviceInfo>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (root is null) return devices;
        foreach (var modelName in root.GetSubKeyNames())
        {
            using var model = root.OpenSubKey(modelName);
            if (model is null) continue;
            foreach (var instance in model.GetSubKeyNames())
            {
                using var device = model.OpenSubKey(instance);
                var name = device?.GetValue("FriendlyName") as string ?? device?.GetValue("DeviceDesc") as string;
                if (name is not null) devices.Add(new(modelName + "\\" + instance, name));
            }
        }
        return devices;
    }
}
