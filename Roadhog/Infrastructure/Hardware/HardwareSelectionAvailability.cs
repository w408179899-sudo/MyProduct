using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog.Infrastructure.Hardware;

/// <summary>A point-in-time view for the editor; actual acquisition still belongs to the worker.</summary>
public sealed class HardwareSelectionAvailability
{
    private sealed record Use(string Name, string Key, string Instance, string Vmm, bool Busy);
    private readonly IReadOnlyList<Use> _uses;

    public HardwareSelectionAvailability(string editingId, IReadOnlyList<AccountProcessView> accounts,
        IReadOnlyList<DeviceLease> leases)
    {
        var others = accounts.Where(a => a.Config.InstanceId != editingId).ToArray();
        _uses = others.Select(a => new Use(a.Config.AccountName, a.Config.HardwareKey,
            a.Config.HardwareDeviceInstanceId, a.Config.VmmDeviceName,
            a.DesiredRunning || a.WorkerProcessId.HasValue || a.Worker?.IsRunning == true))
            .Concat(leases.Select(l => new Use("其他客户端（进程 " + l.ProcessId + "）", l.HardwareKey, "", l.VmmDeviceName, true))).ToArray();
    }

    private static bool Equal(string? a, string? b) => !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Matches(HardwareDeviceFeature device, Use use) => Equal(device.BindingKey, use.Key)
        || device.AliasKeys.Any(k => Equal(k, use.Key)) || Equal(device.DeviceInstanceId, use.Instance);
    private static bool MatchesVmm(string vmm, Use use) => !string.IsNullOrWhiteSpace(vmm)
        && !string.IsNullOrWhiteSpace(use.Vmm) && Equal(DeviceLeaseStore.CanonicalVmmDeviceName(vmm), DeviceLeaseStore.CanonicalVmmDeviceName(use.Vmm));

    public bool DeviceBusy(HardwareDeviceFeature device) => _uses.Any(u => u.Busy && Matches(device, u));
    public bool VmmBusy(string vmm) => _uses.Any(u => u.Busy && MatchesVmm(vmm, u));
    public string DeviceOwners(HardwareDeviceFeature device) => string.Join("、", _uses.Where(u => !u.Busy && Matches(device, u)).Select(u => u.Name).Distinct());
    public string VmmOwners(string vmm) => string.Join("、", _uses.Where(u => !u.Busy && MatchesVmm(vmm, u)).Select(u => u.Name).Distinct());

    public void EnsureAvailable(AccountConfig draft)
    {
        var conflict = _uses.FirstOrDefault(u => Equal(draft.HardwareKey, u.Key)
            || Equal(draft.HardwareDeviceInstanceId, u.Instance) || MatchesVmm(draft.VmmDeviceName, u));
        if (conflict is not null)
            throw new InvalidOperationException(conflict.Busy
                ? $"所选设备或读取编号正被“{conflict.Name}”占用，请刷新设备后重新选择。"
                : $"所选设备或读取编号已绑定“{conflict.Name}”，请先调整该账号的绑定，再测试或保存。");
    }
}
