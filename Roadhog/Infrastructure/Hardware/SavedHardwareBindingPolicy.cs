using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Hardware;

namespace Roadhog.Infrastructure.Hardware;

/// <summary>Windows USB display order is not the saved VMM device association.</summary>
internal static class SavedHardwareBindingPolicy
{
    public static void ConfigureResolver(WindowsHardwareDeviceResolverOptions options, AccountConfig account)
    {
        if (HasPhysicalKey(account.HardwareKey) && IsExplicitVmm(account.VmmDeviceName))
            options.VmmDeviceByHardwareKey[account.HardwareKey.Trim()] = account.VmmDeviceName.Trim();
    }

    public static IReadOnlyList<HardwareDeviceFeature> ForEditor(IReadOnlyList<HardwareDeviceFeature> devices,
        IReadOnlyList<AccountConfig> accounts) => devices.Select(device =>
    {
        var saved = accounts.Where(account => HasPhysicalKey(account.HardwareKey) && IsExplicitVmm(account.VmmDeviceName)
            && (string.Equals(device.BindingKey, account.HardwareKey.Trim(), StringComparison.OrdinalIgnoreCase)
                || device.AliasKeys.Contains(account.HardwareKey.Trim(), StringComparer.OrdinalIgnoreCase))).ToArray();
        return saved.Length == 1 ? device with { VmmDeviceName = saved[0].VmmDeviceName.Trim() } : device;
    }).ToArray();

    public static OperationResult Validate(AccountConfig account, IHardwareDeviceResolver resolver)
    {
        if (!HasPhysicalKey(account.HardwareKey) || !IsExplicitVmm(account.VmmDeviceName))
            return OperationResult.Fail("请在“设备/角色”中选择明确的 DMA 和读取编号，再测试读取角色。");
        var binding = resolver.BindByKey(account.AccountName, account.HardwareKey);
        if (!binding.Success || binding.Value is null)
            return OperationResult.Fail(binding.Error ?? "DMA 设备未连接。");
        if (!string.IsNullOrWhiteSpace(account.HardwareDeviceInstanceId)
            && !string.Equals(account.HardwareDeviceInstanceId, binding.Value.DeviceInstanceId, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("所选 DMA 的物理设备身份已变化，请在“设备/角色”中重新选择并验证角色。");
        if (!string.Equals(DeviceLeaseStore.CanonicalVmmDeviceName(binding.Value.VmmDeviceName),
                DeviceLeaseStore.CanonicalVmmDeviceName(account.VmmDeviceName), StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("所选物理 DMA 与读取编号不一致，请在“设备/角色”中重新选择并验证角色。");
        return OperationResult.Ok();
    }

    private static bool HasPhysicalKey(string? key) => !string.IsNullOrWhiteSpace(key)
        && key.Trim() != "0" && !key.Trim().StartsWith("auto", StringComparison.OrdinalIgnoreCase);
    private static bool IsExplicitVmm(string? value)
    {
        const string prefix = "fpga://devindex=";
        var name = value?.Trim() ?? string.Empty;
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0;
    }
}
