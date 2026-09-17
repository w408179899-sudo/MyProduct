using System.Buffers.Binary;
namespace Smart.NativeSmoke;

public sealed record PeHeaderInfo(int PeOffset, ushort Machine, ushort Sections, ushort OptionalHeaderSize, ushort OptionalMagic)
{
    public int RequiredPeBytes => 24 + OptionalHeaderSize;
}
public static class PeHeaderValidation
{
    public const int DosBytes = 64;
    public const int PePrefixBytes = 26;
    public const int FixedHeaderBytes = 4096;
    public const int MaximumPeOffset = 65536;
    public static int ReadDosOffset(ReadOnlySpan<byte> dos)
    {
        if (dos.Length != DosBytes || BinaryPrimitives.ReadUInt16LittleEndian(dos) != 0x5A4D)
            throw new InvalidDataException("A complete 64-byte MZ header is required.");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(dos[60..]);
        if (offset < DosBytes || offset > MaximumPeOffset) throw new InvalidDataException("PE header offset exceeds the bounded smoke-read range.");
        return offset;
    }
    public static PeHeaderInfo ReadPePrefix(ReadOnlySpan<byte> bytes, int peOffset)
    {
        if (peOffset < DosBytes || peOffset > MaximumPeOffset || bytes.Length < PePrefixBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x00004550)
            throw new InvalidDataException("A complete PE signature and COFF/optional prefix are required.");
        var machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]);
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..]);
        var minimum = magic switch { 0x10B => 96, 0x20B => 112, _ => throw new InvalidDataException("Unsupported PE optional-header magic.") };
        if (machine == 0 || sections is 0 or > 96 || optionalSize < minimum || optionalSize > FixedHeaderBytes)
            throw new InvalidDataException("PE machine/section count/optional-header size exceeds the validation bounds.");
        return new(peOffset, machine, sections, optionalSize, magic);
    }
    public static void ValidateCompletePe(ReadOnlySpan<byte> bytes, PeHeaderInfo expected)
    {
        if (bytes.Length != expected.RequiredPeBytes || ReadPePrefix(bytes, expected.PeOffset) != expected)
            throw new InvalidDataException("PE header changed or was not read completely.");
    }
}
