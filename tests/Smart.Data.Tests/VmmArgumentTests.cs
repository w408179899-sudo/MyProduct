using Smart.Adapters.Dma;
using Xunit;
namespace Smart.Data.Tests;

public sealed class VmmArgumentTests
{
    private static string MissingLibrary => Path.Combine(Path.GetTempPath(), "smart-missing-" + Guid.NewGuid().ToString("N") + ".dll");

    [Theory] [InlineData("smart")] [InlineData("Smart.HardwareProbe")]
    public void ProgramNameIsRejectedBeforeNativeLoading(string programName)
    {
        var error = Assert.Throws<ArgumentException>(() => new VmmTransport(MissingLibrary, "device",
            [programName, "-device", "fpga://devindex=0"]));
        Assert.Contains("first", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void BuilderUsesBlankFirstArgumentAndPreservesAnOwnedCopyOfExtraOptions()
    {
        string[] extra = ["-printf", "-memmap", "auto"];
        var arguments = VmmTransport.CreateArguments("fpga://devindex=0", extra);
        extra[0] = "-norefresh";
        Assert.Equal(["", "-device", "fpga://devindex=0", "-printf", "-memmap", "auto"], arguments);
        Assert.Equal(["", "-device", "fpga://devindex=0"], VmmTransport.CreateArguments("fpga://devindex=0"));
    }

    [Theory] [InlineData("-device")] [InlineData("-DEVICE")] [InlineData("-f")] [InlineData("-z")]
    [InlineData("-norefresh")] [InlineData("-NoRefresh")]
    public void ExtraOptionsCannotOverrideTheSelectedDeviceOrDisableLifecycleRefresh(string option)
    {
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga://devindex=0", [option, "other"]));
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("-device")] [InlineData("fpga://devindex=0\0other")]
    public void BuilderRejectsInvalidDeviceValues(string? deviceUri)
    {
        Assert.ThrowsAny<ArgumentException>(() => VmmTransport.CreateArguments(deviceUri!));
    }

    [Fact] public void ArgumentAndNativeDeviceByteBudgetsAreEnforced()
    {
        Assert.Equal(35, VmmTransport.CreateArguments("fpga", Enumerable.Repeat("-printf", 32).ToArray()).Length);
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga", Enumerable.Repeat("-printf", 33).ToArray()));
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga", [new string('a', 4097)]));
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga", [null!]));
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga", [""]));
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments("fpga", ["-printf\0-norefresh"]));
        Assert.Equal(3, VmmTransport.CreateArguments(new string('a', 259)).Length);
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments(new string('a', 260)));
        Assert.Throws<ArgumentException>(() => VmmTransport.CreateArguments(new string('\u4E2D', 87)));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void BlankPrefixAndDirectOptionsBothReachNativeLoading(bool blankPrefix)
    {
        string[] arguments = blankPrefix ? ["", "-device", "fpga://devindex=0"] : ["-device", "fpga://devindex=0"];
        Assert.Throws<DllNotFoundException>(() => new VmmTransport(MissingLibrary, "device", arguments));
    }

    [Fact] public void MalformedNativeArgumentListsFailBeforeNativeLoading()
    {
        Assert.Throws<ArgumentNullException>(() => new VmmTransport(MissingLibrary, "device", null!));
        string[][] invalid = [[], [null!], ["", "-device"], ["", "-device", "fpga", "-z", "other"],
            ["", "-device", "fpga\0other"], Enumerable.Repeat("-printf", 36).ToArray()];
        foreach (var arguments in invalid)
            Assert.Throws<ArgumentException>(() => new VmmTransport(MissingLibrary, "device", arguments));
    }
}
