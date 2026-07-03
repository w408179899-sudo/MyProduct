using Microsoft.Win32;
using System.Runtime.InteropServices;
using Roadhog.Core.Common;
using Roadhog.Core.Hardware;

namespace Roadhog.Infrastructure.Hardware;

public sealed class WindowsHardwareDeviceResolver : IHardwareDeviceResolver
{
    private const string UsbEnumRegistryPath = @"SYSTEM\CurrentControlSet\Enum\USB";
    private readonly WindowsHardwareDeviceResolverOptions _options;

    public WindowsHardwareDeviceResolver(WindowsHardwareDeviceResolverOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<HardwareDeviceFeature> ListDevices()
    {
        using var usbRoot = Registry.LocalMachine.OpenSubKey(UsbEnumRegistryPath);
        if (usbRoot is null)
        {
            return Array.Empty<HardwareDeviceFeature>();
        }

        var rootDevicePrefix = BuildRootDevicePrefix();
        var devices = usbRoot.GetSubKeyNames()
            .Where(key => key.StartsWith(rootDevicePrefix, StringComparison.OrdinalIgnoreCase)
                && !key.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            .SelectMany(deviceKey => ReadDeviceInstances(usbRoot, deviceKey))
            .OrderBy(device => device.BindingKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ApplyIndexedVmmDeviceNames(devices);
    }

    public OperationResult<HardwareBinding> BindByKey(string accountName, string hardwareKey)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return OperationResult<HardwareBinding>.Fail("Account name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(hardwareKey))
        {
            return TryAutoBind(accountName);
        }

        var devices = ListDevices();
        var device = devices.FirstOrDefault(item => MatchesHardwareKey(item, hardwareKey));
        if (device is null)
        {
            return OperationResult<HardwareBinding>.Fail("Hardware device not found: " + hardwareKey);
        }

        return OperationResult<HardwareBinding>.Ok(ToBinding(accountName, device));
    }

    public OperationResult<HardwareBinding> TryAutoBind(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return OperationResult<HardwareBinding>.Fail("Account name cannot be empty.");
        }

        var devices = ListDevices();
        if (devices.Count == 0)
        {
            return OperationResult<HardwareBinding>.Fail("Hardware device not found: " + BuildRootDevicePrefix());
        }

        return OperationResult<HardwareBinding>.Ok(ToBinding(accountName, devices[0]));
    }

    private IEnumerable<HardwareDeviceFeature> ReadDeviceInstances(RegistryKey usbRoot, string deviceKeyName)
    {
        using var deviceKey = usbRoot.OpenSubKey(deviceKeyName);
        if (deviceKey is null)
        {
            yield break;
        }

        foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
        {
            using var instanceKey = deviceKey.OpenSubKey(instanceKeyName);
            if (instanceKey is null)
            {
                continue;
            }

            var parentInstanceId = @"USB\" + deviceKeyName + @"\" + instanceKeyName;
            if (!IsPresentDevice(parentInstanceId))
            {
                continue;
            }

            var containerId = ReadString(instanceKey, "ContainerID");
            var rootHardwareId = ReadStringArray(instanceKey, "HardwareID").FirstOrDefault() ?? deviceKeyName;
            var locationKey = BuildLocationKey(ReadString(instanceKey, "LocationInformation"));
            var interfaceInfo = FindInterfaceDevice(usbRoot, containerId, parentInstanceId);
            if (interfaceInfo == InterfaceDeviceInfo.Empty)
            {
                continue;
            }

            var deviceInstanceId = FirstNotBlank(interfaceInfo.DeviceInstanceId, parentInstanceId);
            var hardwareId = FirstNotBlank(interfaceInfo.HardwareId, rootHardwareId);
            var displayName = FirstNotBlank(
                interfaceInfo.DisplayName,
                CleanDeviceText(ReadString(instanceKey, "FriendlyName")),
                CleanDeviceText(ReadString(instanceKey, "DeviceDesc")),
                hardwareId);
            var manufacturer = FirstNotBlank(interfaceInfo.Manufacturer, CleanDeviceText(ReadString(instanceKey, "Mfg")));
            var bindingIdentity = ResolveBindingIdentity(parentInstanceId, containerId, locationKey);
            var aliasKeys = BuildAliasKeys(bindingIdentity.BindingKey, parentInstanceId, deviceInstanceId, containerId, hardwareId, locationKey);
            var vmmDeviceName = ResolveVmmDeviceName(aliasKeys);

            yield return new HardwareDeviceFeature(
                bindingIdentity.BindingKey,
                bindingIdentity.BindingKind,
                bindingIdentity.Confidence,
                deviceInstanceId,
                parentInstanceId,
                containerId,
                hardwareId,
                locationKey,
                displayName,
                manufacturer,
                vmmDeviceName,
                aliasKeys);
        }
    }

    private HardwareBinding ToBinding(string accountName, HardwareDeviceFeature device)
    {
        return new HardwareBinding(
            accountName,
            device.BindingKey,
            device.BindingKind,
            device.BindingConfidence,
            device.DeviceInstanceId,
            device.ParentInstanceId,
            device.ContainerId,
            device.HardwareId,
            device.LocationKey,
            device.DisplayName,
            device.Manufacturer,
            device.VmmDeviceName,
            device.AliasKeys,
            DateTimeOffset.Now);
    }

    private bool MatchesHardwareKey(HardwareDeviceFeature device, string hardwareKey)
    {
        return device.AliasKeys.Any(alias => EqualsKey(alias, hardwareKey));
    }

    private string BuildRootDevicePrefix()
    {
        return "VID_" + NormalizeHex(_options.UsbVid) + "&PID_" + NormalizeHex(_options.UsbPid);
    }

    private InterfaceDeviceInfo FindInterfaceDevice(RegistryKey usbRoot, string containerId, string parentInstanceId)
    {
        var interfaceKeyName = BuildRootDevicePrefix() + "&" + _options.UsbInterfaceId;
        using var interfaceKey = usbRoot.OpenSubKey(interfaceKeyName);
        if (interfaceKey is null)
        {
            return InterfaceDeviceInfo.Empty;
        }

        foreach (var instanceKeyName in interfaceKey.GetSubKeyNames())
        {
            using var instanceKey = interfaceKey.OpenSubKey(instanceKeyName);
            if (instanceKey is null)
            {
                continue;
            }

            var childContainerId = ReadString(instanceKey, "ContainerID");
            if (!EqualsKey(childContainerId, containerId))
            {
                continue;
            }

            var deviceInstanceId = @"USB\" + interfaceKeyName + @"\" + instanceKeyName;
            if (!IsPresentDevice(deviceInstanceId))
            {
                continue;
            }

            return new InterfaceDeviceInfo(
                deviceInstanceId,
                ReadStringArray(instanceKey, "HardwareID").FirstOrDefault() ?? interfaceKeyName,
                FirstNotBlank(
                    CleanDeviceText(ReadString(instanceKey, "FriendlyName")),
                    CleanDeviceText(ReadString(instanceKey, "DeviceDesc"))),
                CleanDeviceText(ReadString(instanceKey, "Mfg")));
        }

        return InterfaceDeviceInfo.Empty;
    }

    private BindingIdentity ResolveBindingIdentity(string parentInstanceId, string containerId, string locationKey)
    {
        var serial = ExtractUsbInstanceSerial(parentInstanceId);
        if (!string.IsNullOrWhiteSpace(serial)
            && !_options.KnownDuplicateSerials.Contains(serial)
            && !IsGeneratedUsbInstanceSerial(serial))
        {
            return new BindingIdentity("usb:" + serial, "usb-serial", "high");
        }

        if (!string.IsNullOrWhiteSpace(locationKey))
        {
            return new BindingIdentity(locationKey, "usb-port", "medium");
        }

        return new BindingIdentity(FirstNotBlank(containerId, parentInstanceId), "usb-instance", "low");
    }

    private string ResolveVmmDeviceName(IReadOnlyList<string> aliasKeys)
    {
        foreach (var key in aliasKeys)
        {
            if (_options.VmmDeviceByHardwareKey.TryGetValue(key, out var mappedDevice)
                && !string.IsNullOrWhiteSpace(mappedDevice))
            {
                return mappedDevice.Trim();
            }
        }

        return _options.DefaultVmmDeviceName;
    }

    private IReadOnlyList<HardwareDeviceFeature> ApplyIndexedVmmDeviceNames(IReadOnlyList<HardwareDeviceFeature> devices)
    {
        if (devices.Count <= 1)
        {
            return devices;
        }

        return devices
            .Select((device, index) => HasExplicitVmmDeviceMapping(device.AliasKeys)
                ? device
                : device with { VmmDeviceName = BuildIndexedVmmDeviceName(device.VmmDeviceName, index) })
            .ToArray();
    }

    private bool HasExplicitVmmDeviceMapping(IReadOnlyList<string> aliasKeys)
    {
        return aliasKeys.Any(key =>
            _options.VmmDeviceByHardwareKey.TryGetValue(key, out var mappedDevice) &&
            !string.IsNullOrWhiteSpace(mappedDevice));
    }

    private static string BuildIndexedVmmDeviceName(string baseDeviceName, int index)
    {
        var value = string.IsNullOrWhiteSpace(baseDeviceName) ? "fpga" : baseDeviceName.Trim();
        if (value.Contains("devindex=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("deviceindex=", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var separator = value.Contains("://", StringComparison.Ordinal) ? "," : "://";
        return value + separator + "devindex=" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> BuildAliasKeys(
        string bindingKey,
        string parentInstanceId,
        string deviceInstanceId,
        string containerId,
        string hardwareId,
        string locationKey)
    {
        var aliases = new List<string>();
        AddAlias(aliases, bindingKey);
        AddAlias(aliases, parentInstanceId);
        AddAlias(aliases, deviceInstanceId);
        AddAlias(aliases, containerId);
        AddAlias(aliases, hardwareId);
        AddAlias(aliases, locationKey);

        var serial = ExtractUsbInstanceSerial(parentInstanceId);
        if (!string.IsNullOrWhiteSpace(serial))
        {
            AddAlias(aliases, "usb:" + serial);
        }

        return aliases;
    }

    private static void AddAlias(List<string> aliases, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !aliases.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            aliases.Add(value);
        }
    }

    private static string BuildLocationKey(string locationInformation)
    {
        return string.IsNullOrWhiteSpace(locationInformation)
            ? string.Empty
            : "port:" + locationInformation.Trim();
    }

    private static string ExtractUsbInstanceSerial(string parentInstanceId)
    {
        if (string.IsNullOrWhiteSpace(parentInstanceId))
        {
            return string.Empty;
        }

        var marker = @"\";
        var index = parentInstanceId.LastIndexOf(marker, StringComparison.Ordinal);
        return index >= 0 && index + 1 < parentInstanceId.Length
            ? parentInstanceId[(index + 1)..]
            : string.Empty;
    }

    private static bool IsGeneratedUsbInstanceSerial(string serial)
    {
        return serial.Contains('&', StringComparison.Ordinal);
    }

    private static bool IsPresentDevice(string deviceInstanceId)
    {
        return !string.IsNullOrWhiteSpace(deviceInstanceId)
            && NativeMethods.CM_Locate_DevNodeW(out _, deviceInstanceId, 0) == 0;
    }

    private static string NormalizeHex(string value)
    {
        return (value ?? string.Empty).Trim().TrimStart('0', 'x', 'X').PadLeft(4, '0').ToUpperInvariant();
    }

    private static bool EqualsKey(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(RegistryKey key, string name)
    {
        return key.GetValue(name) as string ?? string.Empty;
    }

    private static IReadOnlyList<string> ReadStringArray(RegistryKey key, string name)
    {
        return key.GetValue(name) as string[] ?? Array.Empty<string>();
    }

    private static string CleanDeviceText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var semicolon = value.LastIndexOf(';');
        return semicolon >= 0 && semicolon + 1 < value.Length
            ? value[(semicolon + 1)..].Trim()
            : value.Trim();
    }

    private static string FirstNotBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private sealed record InterfaceDeviceInfo(
        string DeviceInstanceId,
        string HardwareId,
        string DisplayName,
        string Manufacturer)
    {
        public static InterfaceDeviceInfo Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private sealed record BindingIdentity(string BindingKey, string BindingKind, string Confidence);

    private static class NativeMethods
    {
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
    }
}
