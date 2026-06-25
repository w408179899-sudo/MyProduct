namespace Roadhog.Infrastructure.Hardware;

public sealed class WindowsHardwareDeviceResolverOptions
{
    public string UsbVid { get; set; } = "0403";

    public string UsbPid { get; set; } = "601F";

    public string UsbInterfaceId { get; set; } = "MI_00";

    public string DefaultVmmDeviceName { get; set; } = "fpga";

    public Dictionary<string, string> VmmDeviceByHardwareKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> KnownDuplicateSerials { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "000000000001"
    };
}
