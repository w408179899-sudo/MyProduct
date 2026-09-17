using Smart.Adapters.Dma;
using Xunit;
namespace Smart.Data.Tests;

public sealed class NativeReadQualityTests
{
    [Fact] public void SuccessfulLaterPageDoesNotTurnUnreadLeadingPageIntoValidBytes()
    {
        // MemReadEx reports a total byte count; failed pages remain zeroed in place.
        var nativeBuffer = new byte[8192];
        nativeBuffer.AsSpan(4096).Fill(0xA5);
        var block = MemoryBlock.FromNativeOwnedBuffer(0x1000, nativeBuffer, success: true, bytesRead: 4096);
        Assert.False(block.Complete);
        Assert.Empty(block.Bytes);
    }

    [Theory] [InlineData(true, 4)] [InlineData(false, 8)] [InlineData(true, 9)] [InlineData(false, 0)]
    public void IncompleteOrFailedNativeReadExposesNoUnvalidatedByteRange(bool success, uint count)
    {
        var block = MemoryBlock.FromNativeOwnedBuffer(0x2000, [1, 2, 3, 4, 0, 0, 0, 0], success, count);
        Assert.False(block.Complete);
        Assert.Empty(block.Bytes);
    }

    [Fact] public void CompleteNativeReadPreservesAddressAndAllBytesIncludingValidZeros()
    {
        byte[] value = [0, 1, 0, 2, 3, 0];
        var block = MemoryBlock.FromNativeOwnedBuffer(0x3456, value, success: true, bytesRead: 6);
        Assert.True(block.Complete);
        Assert.Equal(0x3456UL, block.Address);
        Assert.Equal(value, block.Bytes);
    }

    [Fact] public void FailedModuleLookupCannotProduceAZeroModuleIdentity()
    {
        Assert.Throws<IOException>(() => VmmTransport.ValidateModuleBase(0, requiredBinding: true));
        Assert.Equal(0x140000000UL, VmmTransport.ValidateModuleBase(0x140000000, requiredBinding: true));
    }

    [Fact] public void DiagnosticProcessEnumerationMayRepresentAnUnboundModule()
    {
        Assert.Equal(0UL, VmmTransport.ValidateModuleBase(0, requiredBinding: false));
        Assert.Equal(0x140000000UL, VmmTransport.ValidateModuleBase(0x140000000, requiredBinding: false));
    }

    [Theory] [InlineData("-norefresh")] [InlineData("-NoRefresh")]
    public void DisablingLifecycleRefreshIsRejectedBeforeAnyNativeLibraryLoads(string option)
    {
        var missingLibrary = Path.Combine(Path.GetTempPath(), "smart-nonexistent-" + Guid.NewGuid().ToString("N") + ".dll");
        var failure = Assert.Throws<ArgumentException>(() => new VmmTransport(missingLibrary, "device", ["", option]));
        Assert.Contains("-norefresh", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
