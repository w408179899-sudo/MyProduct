using System.Buffers.Binary;
namespace Smart.ProbeProtocol;

public enum ProbeReadError { None, WrongLength, WrongHeader, Updating, SequenceMismatch, WrongSession, ChecksumMismatch, InvalidPayload }
public sealed record ProbeSample(Guid SessionId, long Sequence, long Counter, long Value, DateTimeOffset PublishedAt);

// This is a controlled validation protocol, not a game schema or a public business snapshot API.
public static class ProbeMemoryProtocol
{
    public const int Size = 96;
    public const int Version = 1;
    public const int BeginSequenceOffset = 16;
    public const int PayloadOffset = 24;
    public const int ChecksumOffset = 80;
    public const int EndSequenceOffset = 88;
    private const ulong Magic = 0x3142525054524D53; // "SMRTPRB1" in little-endian order.
    private const ulong PatternSalt = 0x9E3779B97F4A7C15;

    public static void Encode(Span<byte> destination, Guid session, long sequence, long counter, DateTimeOffset publishedAt)
    {
        if (destination.Length != Size) throw new ArgumentException("A probe frame must be exactly 96 bytes.", nameof(destination));
        if (session == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(session));
        if (sequence <= 0 || (sequence & 1) != 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        destination.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(destination, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], Version);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], Size);
        BinaryPrimitives.WriteInt64LittleEndian(destination[BeginSequenceOffset..], sequence);
        session.TryWriteBytes(destination[PayloadOffset..]);
        BinaryPrimitives.WriteInt64LittleEndian(destination[40..], counter);
        BinaryPrimitives.WriteInt64LittleEndian(destination[48..], counter % 8);
        BinaryPrimitives.WriteInt64LittleEndian(destination[56..], ~counter);
        BinaryPrimitives.WriteInt64LittleEndian(destination[64..], publishedAt.UtcTicks);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[72..], unchecked((ulong)counter * PatternSalt));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[ChecksumOffset..], Checksum(destination));
        BinaryPrimitives.WriteInt64LittleEndian(destination[EndSequenceOffset..], sequence);
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, Guid expectedSession, out ProbeSample? sample, out ProbeReadError error)
    {
        sample = null;
        if (bytes.Length != Size) { error = ProbeReadError.WrongLength; return false; }
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != Version ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != Size)
        { error = ProbeReadError.WrongHeader; return false; }
        var begin = BinaryPrimitives.ReadInt64LittleEndian(bytes[BeginSequenceOffset..]);
        var end = BinaryPrimitives.ReadInt64LittleEndian(bytes[EndSequenceOffset..]);
        if (begin <= 0 || end <= 0 || (begin & 1) != 0 || (end & 1) != 0)
        { error = ProbeReadError.Updating; return false; }
        if (begin != end) { error = ProbeReadError.SequenceMismatch; return false; }
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[ChecksumOffset..]) != Checksum(bytes))
        { error = ProbeReadError.ChecksumMismatch; return false; }
        var session = new Guid(bytes.Slice(PayloadOffset, 16));
        var counter = BinaryPrimitives.ReadInt64LittleEndian(bytes[40..]);
        var value = BinaryPrimitives.ReadInt64LittleEndian(bytes[48..]);
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes[64..]);
        if (session == Guid.Empty || counter < 0 || value != counter % 8 || BinaryPrimitives.ReadInt64LittleEndian(bytes[56..]) != ~counter ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[72..]) != unchecked((ulong)counter * PatternSalt) ||
            ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        { error = ProbeReadError.InvalidPayload; return false; }
        // Only a fully validated foreign frame proves a lifecycle change. Torn/corrupt GUID
        // bytes must remain an ordinary failed capture so the provider can retain its snapshot.
        if (session != expectedSession) { error = ProbeReadError.WrongSession; return false; }
        sample = new(session, begin, counter, value, new DateTimeOffset(ticks, TimeSpan.Zero));
        error = ProbeReadError.None;
        return true;
    }

    private static ulong Checksum(ReadOnlySpan<byte> bytes)
    {
        const ulong prime = 1099511628211;
        var hash = 14695981039346656037UL;
        // Bind the payload to its committed even sequence. Equal old sequence guards alone
        // cannot authorize a newer payload/checksum spliced from another external capture.
        foreach (var value in bytes[..ChecksumOffset]) hash = unchecked((hash ^ value) * prime);
        return hash;
    }
}
