using System.Buffers.Binary;
using System.Text.Json;
using Smart.ProbeProtocol;
using Xunit;
namespace Smart.Probe.Tests;

public sealed class ProtocolTests
{
    private static byte[] Frame(Guid session, long counter = 0, long sequence = 2)
    {
        var bytes = new byte[ProbeMemoryProtocol.Size];
        ProbeMemoryProtocol.Encode(bytes, session, sequence, counter, DateTimeOffset.UnixEpoch);
        return bytes;
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(long.MaxValue)]
    public void WholeFramesIncludeRealZeroAndNeverTreatItAsAReadFailure(long counter)
    {
        var session = Guid.NewGuid();
        Assert.True(ProbeMemoryProtocol.TryDecode(Frame(session, counter), session, out var sample, out var error));
        Assert.Equal(ProbeReadError.None, error); Assert.NotNull(sample);
        Assert.Equal(counter, sample.Counter); Assert.Equal(counter % 8, sample.Value);
        Assert.Equal(2, sample.Sequence); Assert.Equal(session, sample.SessionId);
    }
    [Theory] [InlineData(0)] [InlineData(95)] [InlineData(97)]
    public void PartialOrOversizedBuffersCannotPublish(int length)
    {
        Assert.False(ProbeMemoryProtocol.TryDecode(new byte[length], Guid.NewGuid(), out var value, out var error));
        Assert.Null(value); Assert.Equal(ProbeReadError.WrongLength, error);
    }
    [Fact] public void UpdatingAndMismatchedSequenceWordsAreRejected()
    {
        var session = Guid.NewGuid(); var bytes = Frame(session);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(ProbeMemoryProtocol.BeginSequenceOffset), 3);
        Assert.False(ProbeMemoryProtocol.TryDecode(bytes, session, out _, out var updating));
        Assert.Equal(ProbeReadError.Updating, updating);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(ProbeMemoryProtocol.BeginSequenceOffset), 4);
        Assert.False(ProbeMemoryProtocol.TryDecode(bytes, session, out _, out var mismatched));
        Assert.Equal(ProbeReadError.SequenceMismatch, mismatched);
    }
    [Theory] [InlineData(24)] [InlineData(31)] [InlineData(39)] [InlineData(40)] [InlineData(48)] [InlineData(56)] [InlineData(64)] [InlineData(72)] [InlineData(80)]
    public void CorruptPayloadOrChecksumCannotPublish(int offset)
    {
        var session = Guid.NewGuid(); var bytes = Frame(session); bytes[offset] ^= 1;
        Assert.False(ProbeMemoryProtocol.TryDecode(bytes, session, out var sample, out var error));
        Assert.Null(sample); Assert.Equal(ProbeReadError.ChecksumMismatch, error);
    }
    [Fact] public void AStitchedCaptureCannotPassMerelyBecauseItsSequenceWordsMatch()
    {
        var session = Guid.NewGuid(); var old = Frame(session, 1); var newer = Frame(session, 2, 4);
        newer.AsSpan(40, 40).CopyTo(old.AsSpan(40));
        Assert.False(ProbeMemoryProtocol.TryDecode(old, session, out _, out var error));
        Assert.Equal(ProbeReadError.ChecksumMismatch, error);
    }
    [Fact] public void OldMatchingSequenceGuardsCannotAuthorizeANewPayloadWithItsValidChecksum()
    {
        var session = Guid.NewGuid(); var old = Frame(session, 1); var newer = Frame(session, 2, 4);
        newer.AsSpan(ProbeMemoryProtocol.PayloadOffset, ProbeMemoryProtocol.EndSequenceOffset - ProbeMemoryProtocol.PayloadOffset)
            .CopyTo(old.AsSpan(ProbeMemoryProtocol.PayloadOffset));
        Assert.False(ProbeMemoryProtocol.TryDecode(old, session, out var sample, out var error));
        Assert.Null(sample); Assert.Equal(ProbeReadError.ChecksumMismatch, error);
    }
    [Fact] public void CompleteValidNewProcessSessionCannotReuseAnOldManifest()
    {
        Assert.False(ProbeMemoryProtocol.TryDecode(Frame(Guid.NewGuid()), Guid.NewGuid(), out _, out var error));
        Assert.Equal(ProbeReadError.WrongSession, error);
    }
    [Fact] public void UnknownHeaderVersionCannotBeDecodedAsCurrentLayout()
    {
        var session = Guid.NewGuid(); var bytes = Frame(session);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 999);
        Assert.False(ProbeMemoryProtocol.TryDecode(bytes, session, out _, out var error));
        Assert.Equal(ProbeReadError.WrongHeader, error);
    }
    [Fact] public void ManifestRoundTripsAndRejectsForeignLayoutOrInvalidAddress()
    {
        var original = new ProbeManifest(1, Guid.NewGuid(), 42, "ProbeTarget", DateTimeOffset.UtcNow,
            "ProbeTarget.exe", "0x0000000000123000", 96, 8, "mixed", 100, 250);
        var decoded = JsonSerializer.Deserialize<ProbeManifest>(JsonSerializer.Serialize(original));
        Assert.Equal(original, decoded); decoded!.Validate(); Assert.Equal(0x123000UL, decoded.GetAddress());
        Assert.Throws<InvalidDataException>(() => (decoded with { Length = 95 }).Validate());
        Assert.Throws<InvalidDataException>(() => (decoded with { PointerSize = 4 }).Validate());
        Assert.Throws<InvalidDataException>(() => (decoded with { Address = "123000" }).Validate());
        Assert.Throws<InvalidDataException>(() => (decoded with { Address = "0xFFFFFFFFFFFFFFFF" }).Validate());
    }
}
