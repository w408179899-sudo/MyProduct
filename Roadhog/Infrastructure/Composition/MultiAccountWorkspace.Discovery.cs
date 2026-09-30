using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog.Infrastructure.Composition;

public sealed partial class MultiAccountWorkspace
{
    internal IDeviceDiscoveryProbe DiscoveryProbe { get; set; }
    internal Func<IReadOnlyList<HardwareDeviceFeature>> DiscoveryDevices { get; set; }

    internal async Task<DeviceDiscoveryResult> DiscoverDevicesAsync(CancellationToken token)
    {
        await _saveDisposeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposing) != 0) throw new ObjectDisposedException(nameof(MultiAccountWorkspace));
            if (Options.UseMockGameApi || Options.UseToolTestBridge
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Options.AionVmm.VmmRemoteEnvironmentVariable)))
                throw new InvalidOperationException("自动识别设备仅支持本地 DMA 模式，请在设备/角色中验证当前连接");
            using var admission = await Processes.HoldStartAdmissionsAsync(token).ConfigureAwait(false);
            var views = Processes.Snapshot();
            var accounts = views.Select(v => v.Config.Clone()).ToArray();
            var active = views.Where(v => v.DesiredRunning || v.WorkerProcessId is not null)
                .Select(v => v.Config.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var leases = _deviceLeases.ReadActive();
            if (!leases.Success || leases.Value is null) throw new InvalidOperationException(leases.Error);
            var occupied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var lease in leases.Value)
            {
                var owner = views.FirstOrDefault(v => v.WorkerProcessId == lease.ProcessId
                    && HardwareVerificationSession.IsCurrent(v.Config)
                    && v.Worker?.ProcessId == lease.ProcessId
                    && v.Config.HardwareKey.Equals(lease.HardwareKey, StringComparison.OrdinalIgnoreCase)
                    && v.Config.VmmDeviceName.Equals(lease.VmmDeviceName, StringComparison.OrdinalIgnoreCase));
                // Only a live published worker snapshot can identify an occupied device. Never open it again.
                occupied[DeviceLeaseStore.CanonicalVmmDeviceName(lease.VmmDeviceName)] = owner?.Worker?.Snapshot?.CharacterName;
                foreach (var a in accounts.Where(a => a.HardwareKey.Equals(lease.HardwareKey, StringComparison.OrdinalIgnoreCase)))
                    active.Add(a.InstanceId);
            }
            var devices = DiscoveryDevices();
            var result = await new DeviceDiscoveryService(DiscoveryProbe).DiscoverAsync(accounts, devices, active, occupied, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var changed = result.Accounts.Where(a =>
            {
                var old = accounts.Single(o => o.InstanceId == a.InstanceId);
                return old.VmmDeviceName != a.VmmDeviceName || old.HardwareVerificationSessionId != a.HardwareVerificationSessionId;
            }).ToArray();
            if (changed.Length > 0)
            {
                var currentDevices = DiscoveryDevices();
                if (!devices.Select(d => (d.BindingKey, d.DeviceInstanceId)).OrderBy(x => x.BindingKey)
                    .SequenceEqual(currentDevices.Select(d => (d.BindingKey, d.DeviceInstanceId)).OrderBy(x => x.BindingKey)))
                    throw new InvalidOperationException("扫描期间设备发生变化，未保存，请重新识别");
                foreach (var a in changed) HardwareAvailability(a.InstanceId).EnsureAvailable(a);
                token.ThrowIfCancellationRequested();
                await SaveAccountsCoreAsync(result.Accounts, token).ConfigureAwait(false);
            }
            Logger.Info("manager.device_discovery.completed", new Dictionary<string, object?> {
                ["updatedAccounts"] = changed.Length, ["results"] = result.Messages });
            return result;
        }
        finally { _saveDisposeGate.Release(); }
    }
}
