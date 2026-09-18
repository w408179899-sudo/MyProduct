using System.Text.Json;
using SampleProject.Desktop.ViewModels;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Xunit;
namespace SampleProject.Tests;

public sealed class DmaBindingTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static D3xxDevice Device(int index, string serial, uint location = 10) => new(index, 0, new(601, 0x0403601F, location, serial, "FT601"));
    private static D3xxInventory Inventory(params D3xxDevice[] devices) => new("FTD3XX.dll", Hash, devices);
    private static DmaSettings Settings(D3xxInventory inventory, int index = 0)
    {
        var uri = "fpga://ft601=1,devindex=" + index;
        return new(Path.Combine(Path.GetTempPath(), "vmm.dll"), uri, [], 42, "", "module") { Binding = DmaBindingPolicy.Capture(uri, inventory) };
    }
    private sealed class Source(params D3xxInventory[] inventories) : ID3xxInventorySource
    {
        public int Reads, Disposals;
        public D3xxInventory Read() => inventories[Math.Min(Reads++, inventories.Length - 1)];
        public void Dispose() => Disposals++;
    }
    [Fact] public void ActualDriverOrderIsCheckedTwiceAndLeaseIgnoresUriSpelling()
    {
        var inventory = Inventory(Device(0, "B", 11), Device(1, "A", 22));
        var settings = Settings(inventory, 1); var source = new Source(inventory);
        using var guard = new DmaBindingVerifier(_ => source).Acquire(settings)!;
        Assert.Equal(2, source.Reads); Assert.Equal("A", settings.Binding!.Identity.SerialNumber);
        var alias = settings with { DeviceUri = "FPGA://DEVINDEX=1,FT601=1" };
        using var aliasGuard = new DmaBindingVerifier(_ => new Source(inventory)).Acquire(alias)!;
        Assert.Equal(guard.LeaseKey, aliasGuard.LeaseKey);
        guard.Verify(); Assert.Equal(4, source.Reads);
    }
    [Fact] public void ReorderingMissingAndReplacedPhysicalDevicesFailClosed()
    {
        var inventory = Inventory(Device(0, "B", 11), Device(1, "A", 22)); var settings = Settings(inventory, 1);
        Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.Verify(settings, Inventory(Device(0, "A", 22), Device(1, "B", 11))));
        Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.Verify(settings, Inventory(Device(0, "A", 22))));
        Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.Verify(settings, Inventory(Device(0, "B", 11), Device(1, "C", 22))));
        Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.Verify(settings, inventory with { DriverSha256 = new('A', 64) }));
    }
    [Fact] public void IncompleteOtherProcessMetadataAndDuplicateUnknownLocationsCannotBeBound()
    {
        Assert.Throws<DmaBindingException>(() => Settings(Inventory(Device(0, "") with { Flags = 1 })));
        Assert.Throws<DmaBindingException>(() => Settings(Inventory(Device(0, "same", 0), Device(1, "same", 0))));
        Assert.Throws<DmaBindingException>(() => Settings(Inventory(Device(0, "same", 5), Device(1, "same", 5))));
        var inventory = Inventory(Device(0, "same", 5), Device(1, "same", 6));
        Assert.Throws<DmaBindingException>(() => Settings(inventory));
        Assert.Throws<DmaBindingException>(() => Settings(inventory, 1));
    }
    [Fact] public void UniqueSerialLeaseRemainsStableAfterAnExplicitRebindToAnotherPort()
    {
        var first = Settings(Inventory(Device(0, "unique", 10))).Binding!;
        var rebound = Settings(Inventory(Device(0, "unique", 25))).Binding!;
        Assert.Equal(DmaBindingPolicy.LeaseKey(first), DmaBindingPolicy.LeaseKey(rebound));
        Assert.NotEqual(first.TopologySha256, rebound.TopologySha256);
    }
    [Fact] public void TopologyChangingDuringVerificationDisposesTheLibraryGuard()
    {
        var first = Inventory(Device(0, "A")); var second = Inventory(Device(0, "B")); var source = new Source(first, second);
        Assert.Throws<DmaBindingException>(() => new DmaBindingVerifier(_ => source).Acquire(Settings(first)));
        Assert.Equal(2, source.Reads); Assert.Equal(1, source.Disposals);
    }
    [Fact] public void OpenFlagChangesDoNotForgeAReorderButIdentityRemainsRequired()
    {
        var inventory = Inventory(Device(0, "A")); var settings = Settings(inventory);
        DmaBindingPolicy.Verify(settings, Inventory(Device(0, "A") with { Flags = 1 }));
        Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.Verify(settings,
            Inventory(Device(0, "A") with { Identity = new(0, 0, 0, "", ""), Flags = 1 })));
    }
    [Theory]
    [InlineData("fpga://devindex=0")]
    [InlineData("fpga://ft601=1")]
    [InlineData("fpga://ft601=1,devindex=64")]
    [InlineData("fpga://ft601=1,devindex=08")]
    [InlineData("fpga://ft601=1,devindex=0,devindex=1")]
    [InlineData("fpga://ft601=1,devindex=0,driver=1")]
    [InlineData("fpga://ft601=1,devindex=0,ft2232h=0")]
    [InlineData("fpga://ft601=1,devindex=0,ip=127.0.0.1")]
    [InlineData("other://ft601=1,devindex=0")]
    public void UnsupportedOrAmbiguousIndexSpaceIsRejected(string uri) => Assert.Throws<DmaBindingException>(() => DmaBindingPolicy.ParseDeviceIndex(uri));
    [Fact] public void LegacyConfigurationDoesNotLoadADriverAndOldConstructorStillDeconstructs()
    {
        var settings = new DmaSettings("library", "device", [], 1, "name", "module");
        var (library, device, arguments, pid, name, module) = settings;
        Assert.Equal("library", library); Assert.Equal("device", device); Assert.Empty(arguments);
        Assert.Equal(1, pid); Assert.Equal("name", name); Assert.Equal("module", module);
        Assert.Null(new DmaBindingVerifier(_ => throw new InvalidOperationException("Must not enumerate.")).Acquire(settings));
        Assert.Contains("未绑定", ProfileEditor.From(new("old", RuntimeMode.Hardware, settings)).BindingStatus);
    }
    [Fact] public void ProtectedLocalBindingRejectsRemoteOverridesBeforeLoadingAnyDriver()
    {
        var settings = Settings(Inventory(Device(0, "A"))) with { Arguments = ["-remote", "rpc://host"] };
        Assert.Throws<DmaBindingException>(() => new DmaBindingVerifier(_ => throw new Exception("Must not load.")).Acquire(settings));
        var diagnostics = settings with { Arguments = ["-printf", "-v"] };
        using var guard = new DmaBindingVerifier(_ => new Source(Inventory(Device(0, "A")))).Acquire(diagnostics);
        Assert.NotNull(guard);
    }
    [Fact] public void InvalidInjectedDiscoveryNeverFallsBackToNativeEnumeration()
    {
        var settings = Settings(Inventory(Device(0, "A")));
        var error = Assert.Throws<InvalidOperationException>(() => new DmaBindingVerifier(_ => null!).Acquire(settings));
        Assert.Contains("inventory factory returned null", error.Message);
    }
    [Fact] public void BindingAndWorkerConfigurationSurviveJsonAndDesktopEditing()
    {
        var settings = Settings(Inventory(Device(0, "A"))) with { Worker = new() { StartupTimeoutMs = 45000 } };
        var original = new AccountProfile("bound", RuntimeMode.Hardware, settings, new("127.0.0.1", 12345, "00112233"));
        var restored = JsonSerializer.Deserialize<AccountProfile>(JsonSerializer.Serialize(original))!;
        Assert.Equal(settings.Binding, restored.Dma!.Binding);
        var editor = ProfileEditor.From(restored); editor.Id = "renamed"; var edited = editor.ToProfile();
        Assert.Equal(settings.Binding, edited.Dma!.Binding); Assert.Equal(45000, edited.Dma.Worker.StartupTimeoutMs);
        editor.ClearBinding(); Assert.Null(editor.ToProfile().Dma!.Binding);
        editor.SetBinding(settings.Binding!); Assert.Equal(settings.Binding, editor.ToProfile().Dma!.Binding);
    }
    [Fact] public void DisposedGuardRejectsFurtherNativeEnumeration()
    {
        var inventory = Inventory(Device(0, "A")); var source = new Source(inventory);
        var guard = new DmaBindingVerifier(_ => source).Acquire(Settings(inventory))!;
        guard.Dispose(); guard.Dispose(); Assert.Equal(1, source.Disposals);
        Assert.Throws<ObjectDisposedException>(guard.Verify); Assert.Equal(2, source.Reads);
    }
}
