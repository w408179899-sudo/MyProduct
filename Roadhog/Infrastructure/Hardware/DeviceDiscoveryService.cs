using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;

namespace Roadhog.Infrastructure.Hardware;

internal interface IDeviceDiscoveryProbe
{
    Task<string> ReadRoleAsync(string vmmDeviceName, string processName, CancellationToken token);
    Task VerifyKmBoxAsync(AccountKmBoxSettings settings, CancellationToken token);
}

internal sealed record DeviceDiscoveryResult(IReadOnlyList<AccountConfig> Accounts,
    IReadOnlyDictionary<string, string> Messages);

/// <summary>One scan shared by all accounts. USB enumeration supplies a count, never an index association.</summary>
internal sealed class DeviceDiscoveryService(IDeviceDiscoveryProbe probe)
{
    public async Task<DeviceDiscoveryResult> DiscoverAsync(IReadOnlyList<AccountConfig> accounts,
        IReadOnlyList<HardwareDeviceFeature> devices, IReadOnlySet<string> activeAccounts,
        IReadOnlyDictionary<string, string?> occupiedIndices, CancellationToken token)
    {
        var updated = accounts.Select(a => a.Clone()).ToArray();
        var messages = new Dictionary<string, string>();
        var eligible = new List<AccountConfig>();
        foreach (var account in updated)
        {
            if (activeAccounts.Contains(account.InstanceId)) { messages[account.InstanceId] = "账号后台正在使用设备，已跳过"; continue; }
            if (string.IsNullOrWhiteSpace(account.CharacterName) || string.IsNullOrWhiteSpace(account.HardwareVerificationSessionId))
            { messages[account.InstanceId] = "尚无已验证角色，请先在设备/角色中确认一次"; continue; }
            if (accounts.Count(a => a.CharacterName == account.CharacterName) != 1)
            { messages[account.InstanceId] = "已保存角色名重复，请手动验证设备"; continue; }
            if (!PhysicalIdentityPresent(account, devices))
            { messages[account.InstanceId] = "原物理设备身份已变化或离线，请手动验证设备"; continue; }
            if (account.KmBox is null || !account.KmBox.Validate(out _))
            { messages[account.InstanceId] = "KMBox 配置不完整"; continue; }
            eligible.Add(account);
        }
        if (eligible.Count == 0) return new(updated, messages);
        var processNames = accounts.Select(a => string.IsNullOrWhiteSpace(a.TargetProcessName) ? "Aion.bin" : a.TargetProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (processNames.Length != 1)
        {
            foreach (var a in eligible) messages[a.InstanceId] = "目标进程配置不同，请手动验证设备";
            return new(updated, messages);
        }
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var complete = true;
        var failures = new List<string>();
        for (var index = 0; index < devices.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var vmm = $"fpga://devindex={index}";
            if (occupiedIndices.TryGetValue(vmm, out var knownRole))
            {
                if (string.IsNullOrWhiteSpace(knownRole)) { complete = false; failures.Add($"编号 {index} 被占用"); }
                else mapping[vmm] = knownRole;
                continue;
            }
            try
            {
                var role = await probe.ReadRoleAsync(vmm, processNames[0], token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(role)) throw new InvalidOperationException("未读到角色");
                mapping[vmm] = role;
            }
            catch (DeviceDiscoveryCleanupException) { throw; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { complete = false; failures.Add($"编号 {index}：{ex.Message}"); }
        }
        foreach (var account in eligible)
        {
            token.ThrowIfCancellationRequested();
            var matches = mapping.Where(p => p.Value == account.CharacterName).Select(p => p.Key).ToArray();
            if (matches.Length > 1) { messages[account.InstanceId] = "多个设备读到同名角色，请手动验证设备"; continue; }
            if (!complete) { messages[account.InstanceId] = "扫描未完整，未修改绑定；" + string.Join("；", failures); continue; }
            if (matches.Length == 0) { messages[account.InstanceId] = "未找到原角色，请进入游戏后重新识别"; continue; }
            if (occupiedIndices.ContainsKey(matches[0])) { messages[account.InstanceId] = "角色所在设备正在使用，已跳过"; continue; }
            try { await probe.VerifyKmBoxAsync(account.KmBox!, token).ConfigureAwait(false); }
            catch (DeviceDiscoveryCleanupException) { throw; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { messages[account.InstanceId] = "KMBox 验证失败：" + ex.Message; continue; }
            token.ThrowIfCancellationRequested();
            account.VmmDeviceName = matches[0];
            account.HardwareVerificationSessionId = HardwareVerificationSession.CurrentId;
            messages[account.InstanceId] = $"已识别 {account.CharacterName} → {matches[0]}，等待启动";
        }
        token.ThrowIfCancellationRequested();
        return new(updated, messages);
    }

    internal static bool PhysicalIdentityPresent(AccountConfig account, IReadOnlyList<HardwareDeviceFeature> devices) =>
        !string.IsNullOrWhiteSpace(account.HardwareDeviceInstanceId) &&
        devices.Count(d => (d.BindingKey.Equals(account.HardwareKey, StringComparison.OrdinalIgnoreCase)
            || d.AliasKeys.Contains(account.HardwareKey, StringComparer.OrdinalIgnoreCase))
            && d.DeviceInstanceId.Equals(account.HardwareDeviceInstanceId, StringComparison.OrdinalIgnoreCase)) == 1;
}

internal sealed class DeviceDiscoveryCleanupException(string message, Exception inner) : Exception(message, inner);
