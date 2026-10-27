using System.Buffers.Binary;
using System.Text;

namespace Roadhog.Infrastructure.Login.Native;

internal sealed record SessionSymbolRvas(uint Foreground, uint Cursor);

// Two public data symbols, parsed without sharing native DbgHelp state with VMM.
// All offsets below describe documented MSF/DBI/CodeView formats, not a Windows build.
internal static class CachedPdbSessionSymbols
{
    internal static SessionSymbolRvas Read(byte[] file, Guid guid, uint imageAge, uint imageSize)
    {
        var msf = new Msf(file);
        var info = msf.Stream(1, 4096); var dbi = msf.Stream(3, 16 * 1024 * 1024);
        if (info.Length < 28 || dbi.Length < 64 || U32(info, 0) != 20000404
            || new Guid(info.AsSpan(12, 16)) != guid || imageAge == 0 || U32(info, 8) < imageAge
            || U32(dbi, 0) != uint.MaxValue || U32(dbi, 4) != 19990903
            || U32(dbi, 8) != imageAge || U16(dbi, 58) != 0x8664)
            throw new InvalidDataException("Cached Windows PDB identity does not match the current module.");
        // Microsoft OpenValidate permits a newer stripped-PDB Info age, but its
        // DBI age must match the executable's CodeView age.
        long optionalAt = 64;
        foreach (int field in new[] { 24, 28, 32, 36, 40, 52 }) optionalAt += U32(dbi, field);
        uint optionalSize = U32(dbi, 48);
        if (optionalSize < 12 || optionalSize % 2 != 0 || optionalAt + optionalSize > dbi.Length)
            throw new InvalidDataException("Invalid Windows PDB debug stream table.");
        int optional = checked((int)optionalAt);
        if (U16(dbi, optional + 6) != ushort.MaxValue || U16(dbi, optional + 8) != ushort.MaxValue)
            throw new InvalidDataException("Windows PDB address remapping is not supported by the login symbol reader.");
        var sections = msf.Stream(U16(dbi, optional + 10), 65536);
        var records = msf.Stream(U16(dbi, 20), 16 * 1024 * 1024);
        if (sections.Length == 0 || sections.Length % 40 != 0) throw new InvalidDataException("Invalid Windows PDB section table.");
        var found = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int at = 0; at < records.Length;)
        {
            if (records.Length - at < 4) throw new InvalidDataException("Truncated Windows PDB symbol record.");
            int length = U16(records, at), end = checked(at + 2 + length);
            if (length < 2 || end > records.Length) throw new InvalidDataException("Invalid Windows PDB symbol record length.");
            if (U16(records, at + 2) == 0x110e) // S_PUB32
            {
                if (length < 13) throw new InvalidDataException("Truncated Windows PDB public symbol.");
                int terminator = Array.IndexOf(records, (byte)0, at + 14, end - at - 14);
                if (terminator < 0) throw new InvalidDataException("Unterminated Windows PDB public symbol.");
                string name = Encoding.ASCII.GetString(records, at + 14, terminator - at - 14);
                if (name is "gptiForeground" or "gptCursorAsync")
                {
                    uint offset = U32(records, at + 8); int segment = U16(records, at + 12);
                    if (segment == 0 || segment > sections.Length / 40 || (U32(records, at + 4) & 3) != 0)
                        throw new InvalidDataException("Windows session symbol is not a mapped data symbol.");
                    int section = (segment - 1) * 40;
                    uint extent = Math.Max(U32(sections, section + 8), U32(sections, section + 16));
                    ulong rva = (ulong)U32(sections, section + 12) + offset;
                    if ((ulong)offset + 8 > extent || rva == 0 || rva + 8 > imageSize || rva % 4 != 0
                        || !found.TryAdd(name, checked((uint)rva)))
                        throw new InvalidDataException("Windows session symbol is ambiguous or outside its module.");
                }
            }
            at = end;
        }
        if (!found.TryGetValue("gptiForeground", out uint foreground) || !found.TryGetValue("gptCursorAsync", out uint cursor))
            throw new InvalidDataException("Required Windows session symbols are absent from the matching PDB.");
        return new(foreground, cursor);
    }

    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
    private static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));

    private sealed class Msf
    {
        private readonly byte[] _file;
        private readonly int _blockSize;
        private readonly uint[] _sizes;
        private readonly uint[][] _blocks;
        internal Msf(byte[] file)
        {
            _file = file;
            if (file.Length < 56 || file.Length > 64 * 1024 * 1024
                || !file.AsSpan(0, 32).SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8))
                throw new InvalidDataException("Unsupported Windows PDB container.");
            _blockSize = checked((int)U32(file, 32));
            if (_blockSize is not (512 or 1024 or 2048 or 4096) || (ulong)U32(file, 40) * (uint)_blockSize > (ulong)file.Length)
                throw new InvalidDataException("Invalid Windows PDB block size.");
            uint directorySize = U32(file, 44);
            if (directorySize < 4 || directorySize > 4 * 1024 * 1024) throw new InvalidDataException("Invalid Windows PDB directory size.");
            int count = checked((int)((directorySize + (uint)_blockSize - 1) / (uint)_blockSize));
            long map = (long)U32(file, 52) * _blockSize;
            if (map < 0 || map + count * 4L > file.Length) throw new InvalidDataException("Invalid Windows PDB directory block map.");
            var directoryBlocks = Enumerable.Range(0, count).Select(i => U32(file, checked((int)map + i * 4))).ToArray();
            var directory = Assemble(directoryBlocks, directorySize);
            uint streamCount = U32(directory, 0);
            if (streamCount < 4 || streamCount > 65536 || 4L + streamCount * 4L > directory.Length)
                throw new InvalidDataException("Invalid Windows PDB stream count.");
            _sizes = new uint[streamCount]; _blocks = new uint[streamCount][];
            int at = checked(4 + (int)streamCount * 4);
            for (int i = 0; i < streamCount; i++)
            {
                uint size = _sizes[i] = U32(directory, 4 + i * 4);
                int pages = size == uint.MaxValue ? 0 : checked((int)(((ulong)size + (uint)_blockSize - 1) / (uint)_blockSize));
                if (at + pages * 4L > directory.Length) throw new InvalidDataException("Truncated Windows PDB stream block map.");
                _blocks[i] = new uint[pages];
                for (int j = 0; j < pages; j++) { _blocks[i][j] = U32(directory, at); at += 4; }
            }
        }
        internal byte[] Stream(int index, int maximum)
        {
            if (index < 0 || index >= _sizes.Length || _sizes[index] > maximum) throw new InvalidDataException("Missing or oversized Windows PDB stream.");
            return Assemble(_blocks[index], _sizes[index]);
        }
        private byte[] Assemble(uint[] blocks, uint size)
        {
            var result = new byte[checked((int)size)]; int written = 0;
            foreach (uint block in blocks)
            {
                long source = (long)block * _blockSize; int count = Math.Min(_blockSize, result.Length - written);
                if (source < 0 || source + count > _file.Length) throw new InvalidDataException("Windows PDB block is outside the file.");
                _file.AsSpan(checked((int)source), count).CopyTo(result.AsSpan(written)); written += count;
            }
            if (written != result.Length) throw new InvalidDataException("Incomplete Windows PDB stream.");
            return result;
        }
    }
}
