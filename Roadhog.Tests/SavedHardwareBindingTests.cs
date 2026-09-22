using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class SavedHardwareBindingTests
{
    public static Task BootSessionStorageAsync()
    {
        var path = @"Software\RoadhogTests\HardwareVerification-" + Guid.NewGuid().ToString("N");
        try
        {
            var first = HardwareVerificationSession.ReadOrCreate(path);
            Require(Guid.TryParseExact(first, "N", out _) && HardwareVerificationSession.ReadOrCreate(path) == first, "separate session lookups share the same volatile record");
            var account = new AccountConfig { CharacterName = "role", HardwareVerificationSessionId = first };
            Require(HardwareVerificationSession.IsCurrent(account, first), "matching session allows a saved confirmation");
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(path); // unique empty fixture key, never the live app key
            var second = HardwareVerificationSession.ReadOrCreate(path);
            Require(second != first && !HardwareVerificationSession.IsCurrent(account, second), "loss of volatile boot record invalidates previous confirmations");
            Require(!HardwareVerificationSession.IsCurrent(account, ""), "unavailable session fails closed");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false); }
        return Task.CompletedTask;
    }

    public static async Task StartupRejectsChangedRoleAsync()
    {
        var account = new AccountConfig { CharacterName = "角色2", HardwareVerificationSessionId = HardwareVerificationSession.CurrentId, VmmDeviceName = "fpga://devindex=0" };
        var reads = 0; var starts = 0;
        Task<Roadhog.Core.Model.PlayerSnapshot> Read(string role, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); reads++;
            return Task.FromResult(new Roadhog.Core.Model.PlayerSnapshot(1, 0, role, 100, 100, 100, 100, 0, null, DateTimeOffset.UtcNow));
        }
        OperationResult Start() { starts++; return OperationResult.Ok(); }
        var mismatch = await AccountIdentityStartGuard.RunAsync(account, token => Read("角色5", token), Start, CancellationToken.None);
        Require(!mismatch.Success && starts == 0 && reads == 1, "changed devindex cannot start business or input for a different role");
        Require(mismatch.Error!.Contains("角色2") && mismatch.Error.Contains("角色5") && account.CharacterName == "角色2", "mismatch reports both identities without overwriting saved binding");
        account.VmmDeviceName = "fpga://devindex=4";
        Require((await AccountIdentityStartGuard.RunAsync(account, token => Read("角色2", token), Start, CancellationToken.None)).Success && starts == 1, "explicitly corrected mapping starts only after matching the saved role");
        account.CharacterName = "";
        Require(!(await AccountIdentityStartGuard.RunAsync(account, token => Read("角色2", token), Start, CancellationToken.None)).Success && starts == 1 && reads == 2, "legacy unconfirmed configuration requires read-confirm-save before any start");
    }

    public static async Task StartupCancellationNeverStartsAsync()
    {
        var account = new AccountConfig { CharacterName = "角色2", HardwareVerificationSessionId = HardwareVerificationSession.CurrentId };
        var starts = 0;
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = AccountIdentityStartGuard.RunAsync(account, async token =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        }, () => { starts++; return OperationResult.Ok(); }, cancellation.Token);
        await entered.Task; cancellation.Cancel();
        try { await work; throw new InvalidOperationException("expected cancellation"); }
        catch (OperationCanceledException) { }
        Require(starts == 0, "cancelled identity read cannot start input later");
    }

    public static Task WorkerOptionsPreserveMappingAsync()
    {
        var account = new AccountConfig { AccountName = "script2", HardwareKey = "port:hub7", VmmDeviceName = "fpga://devindex=0" };
        var options = RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec { Account = account });
        Require(options.HardwareResolver.VmmDeviceByHardwareKey[account.HardwareKey] == account.VmmDeviceName, "actual worker options carry the saved device association");
        foreach (var invalid in new[] { "", "fpga", "fpga://devindex=-1", "fpga://devindex=0&extra=1" })
        {
            var copy = account.Clone(); copy.VmmDeviceName = invalid;
            Require(RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec { Account = copy }).HardwareResolver.VmmDeviceByHardwareKey.Count == 0, "ambiguous VMM is never turned into an explicit mapping");
        }
        foreach (var key in new[] { "", "0", "auto" })
        {
            var copy = account.Clone(); copy.HardwareKey = key;
            Require(RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec { Account = copy }).HardwareResolver.VmmDeviceByHardwareKey.Count == 0, "placeholder device is never mapped to device zero");
        }
        return Task.CompletedTask;
    }

    public static Task PhysicalIdentityGuardsAsync()
    {
        var account = new AccountConfig { AccountName = "script2", HardwareKey = "port:hub7", HardwareDeviceInstanceId = "device-2", VmmDeviceName = "fpga://devindex=0" };
        var binding = new HardwareBinding(account.AccountName, account.HardwareKey, "port", "physical", "device-2", "parent", "container", "hardware", "location", "dma", "mock", account.VmmDeviceName, new[] { account.HardwareKey }, DateTimeOffset.UtcNow);
        Require(SavedHardwareBindingPolicy.Validate(account, new Resolver(binding)).Success, "online saved physical identity and VMM pair is valid");
        Require(!SavedHardwareBindingPolicy.Validate(account, new Resolver(binding with { DeviceInstanceId = "device-other" })).Success, "changed physical identity is rejected");
        Require(!SavedHardwareBindingPolicy.Validate(account, new Resolver(binding with { VmmDeviceName = "fpga://devindex=1" })).Success, "different driver mapping is rejected");
        Require(!SavedHardwareBindingPolicy.Validate(account, new Resolver(null)).Success, "offline device is rejected");
        return Task.CompletedTask;
    }

    // Read-only local audit: enumerates present USB identities, never opens VMM or KMBox.
    public static async Task AuditLocalAsync(string accountPath)
    {
        var loaded = await new Roadhog.Infrastructure.Config.JsonAccountConfigStore(accountPath).LoadAllAsync();
        Require(loaded.Success && loaded.Value is { Count: > 0 }, "account file loads");
        var defaults = new WindowsHardwareDeviceResolver(new()).ListDevices();
        foreach (var account in loaded.Value!)
        {
            var options = RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec { Account = account });
            var resolver = new WindowsHardwareDeviceResolver(options.HardwareResolver);
            var validation = SavedHardwareBindingPolicy.Validate(account, resolver);
            Require(validation.Success, account.AccountName + ": " + validation.Error);
            var display = defaults.Single(d => d.BindingKey.Equals(account.HardwareKey, StringComparison.OrdinalIgnoreCase) || d.AliasKeys.Contains(account.HardwareKey, StringComparer.OrdinalIgnoreCase));
            Console.WriteLine($"PASS {account.AccountName}: USB display default={display.VmmDeviceName}; saved worker mapping={account.VmmDeviceName}; physical identity matches");
        }
    }

    private sealed class Resolver(HardwareBinding? binding) : IHardwareDeviceResolver
    {
        public IReadOnlyList<HardwareDeviceFeature> ListDevices() => [];
        public OperationResult<HardwareBinding> BindByKey(string accountName, string hardwareKey) => binding is null ? OperationResult<HardwareBinding>.Fail("offline") : OperationResult<HardwareBinding>.Ok(binding);
        public OperationResult<HardwareBinding> TryAutoBind(string accountName) => throw new InvalidOperationException("auto binding forbidden");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
