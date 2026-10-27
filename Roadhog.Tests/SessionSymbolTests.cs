using System.Text;
using Roadhog.Infrastructure.Login.Native;

internal static class SessionSymbolTests
{
    private static readonly Guid Id = Guid.Parse("6f63f616-4996-847e-b668-b0098d184f4b");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or ArgumentException or OverflowException) { rejected = true; }
        Require(rejected, message);
    }
    private static void U32(byte[] data, int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
    private static void U16(byte[] data, int at, ushort value) => BitConverter.GetBytes(value).CopyTo(data, at);

    internal static Task CachedPdbIdentityAndBoundsAsync()
    {
        var expected = new SessionSymbolRvas(0x1010, 0x1020);
        Require(CachedPdbSessionSymbols.Read(Pdb(), Id, 1, 0x4000) == expected, "Stripped public PDB Info age 3 and DBI age 1 match image age 1.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(), Guid.NewGuid(), 1, 0x4000), "A different PDB GUID cannot supply addresses.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(), Id, 2, 0x4000), "The image/DBI age must agree.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(pdbAge: 0), Id, 1, 0x4000), "An older PDB Info age cannot match.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(duplicate: true), Id, 1, 0x4000), "Duplicate symbols cannot authorize a target.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(code: true), Id, 1, 0x4000), "A function symbol cannot replace the required data symbol.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb(section: 0x4000), Id, 1, 0x4000), "Addresses must remain inside the current module.");
        Reject(() => CachedPdbSessionSymbols.Read(Pdb()[..^1], Id, 1, 0x4000), "A truncated container is rejected.");
        var corrupt = Pdb(); U32(corrupt, 2 * 512, uint.MaxValue);
        Reject(() => CachedPdbSessionSymbols.Read(corrupt, Id, 1, 0x4000), "Directory blocks are bounded by the file.");
        corrupt = Pdb(); U16(corrupt, 5 * 512 + 64 + 6, 0);
        Reject(() => CachedPdbSessionSymbols.Read(corrupt, Id, 1, 0x4000), "Unsupported OMAP cannot silently supply unadjusted RVAs.");
        corrupt = Pdb(); U16(corrupt, 7 * 512, ushort.MaxValue);
        Reject(() => CachedPdbSessionSymbols.Read(corrupt, Id, 1, 0x4000), "Symbol records cannot escape their stream.");
        return Task.CompletedTask;
    }

    internal static Task CodeViewFaultsAsync()
    {
        const ulong module = 0x100000; const int anchor = 0x1ffe;
        byte[] memory = new byte[0x4000]; var record = CodeView(); record.CopyTo(memory, anchor);
        bool missingRecord = false, changeOnRecheck = false; int recordReads = 0;
        byte[]? Read(ulong at, int count)
        {
            if (at < module + 4096 || at + (uint)count > module + (uint)memory.Length) return null;
            if (at == module + anchor && count == record.Length)
            {
                if (missingRecord) return record[..^1];
                if (changeOnRecheck && ++recordReads > 1) { var altered = record.ToArray(); altered[4] ^= 1; return altered; }
            }
            return memory.AsSpan((int)(at - module), count).ToArray();
        }
        SessionCodeView Capture() => SessionSymbolResolver.FindCodeView(Read, module, (uint)memory.Length);
        var found = Capture();
        Require(found.Guid == Id && found.Age == 1 && found.Address == module + anchor,
            "A missing PE header does not hide a complete CodeView record spanning readable pages.");
        missingRecord = true; Reject(() => Capture(), "Partial CodeView cannot become a valid identity."); missingRecord = false;
        changeOnRecheck = true; recordReads = 0; Reject(() => Capture(), "Changed identity cannot be published."); changeOnRecheck = false;
        record.CopyTo(memory, 0x2800); Reject(() => Capture(), "Multiple records must not select a version arbitrarily.");
        memory = new byte[0x4000]; Reject(() => Capture(), "No version means no guessed symbols.");
        return Task.CompletedTask;
    }

    internal static Task SymbolAndUuMetadataLifecycleAsync()
    {
        var cache = new SessionSymbolCache(); var record = CodeView(); byte[]? current = record;
        var evidence = new SessionCodeView(Id, 1, 0x1000, record); int captures = 0;
        SessionSymbolResolution Capture() { captures++; return new(new(0x10, 0x20), evidence); }
        byte[]? Read(ulong _, int count) => current;
        Require(cache.Resolve("device1/session1/module1", Capture, Read).Foreground == 0x10, "First valid symbols publish immediately.");
        current = null;
        cache.Resolve("device1/session1/module1", () => throw new InvalidOperationException("no recapture expected"), Read);
        Require(captures == 1, "Same-session immutable metadata survives a later missing page.");
        Reject(() => cache.Resolve("device2/session1/module1", () => throw new InvalidOperationException("cold device"), Read), "Other devices cannot reuse symbols.");
        current = record; cache.Resolve("device2/session1/module1", Capture, Read);
        current = record.ToArray(); current[4] ^= 1;
        Reject(() => cache.Resolve("device2/session1/module1", Capture, Read), "Observed identity changes invalidate cached symbols.");
        Reject(() => cache.Resolve("device2/session2/module2", () => throw new InvalidOperationException("cold module"), Read), "New session/module needs new evidence.");

        var types = new UuModuleIdentityCache(); var module = new UuControlTypes.Module("ui.dll", 0x100000, 4096);
        types.Bind("one"); Require(types.Resolve(module, () => true), "Verified UU module metadata is retained.");
        bool Missing() => throw new UuMetadataReadException("type identity", module.Base, 64, 0);
        Require(types.Resolve(module, Missing), "A later missing header preserves this module's validated identity.");
        Require(!types.Resolve(module, () => false) && !types.Resolve(module, Missing), "A successful version mismatch replaces old matching metadata.");
        types.Bind("two"); Reject(() => types.Resolve(module, Missing), "A replacement UU process cannot reuse the prior process metadata.");
        return Task.CompletedTask;
    }

    private static byte[] CodeView()
    {
        var name = Encoding.ASCII.GetBytes("win32kbase.pdb\0"); var bytes = new byte[24 + name.Length];
        "RSDS"u8.CopyTo(bytes); Id.ToByteArray().CopyTo(bytes, 4); U32(bytes, 20, 1); name.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Pdb(uint pdbAge = 3, bool duplicate = false, bool code = false, uint section = 0x1000)
    {
        var info = new byte[28]; U32(info, 0, 20000404); U32(info, 8, pdbAge); Id.ToByteArray().CopyTo(info, 12);
        var dbi = new byte[76]; U32(dbi, 0, uint.MaxValue); U32(dbi, 4, 19990903); U32(dbi, 8, 1); U16(dbi, 20, 5);
        U32(dbi, 48, 12); U16(dbi, 58, 0x8664);
        for (int i = 0; i < 6; i++) U16(dbi, 64 + i * 2, ushort.MaxValue);
        U16(dbi, 74, 4);
        var sections = new byte[40]; U32(sections, 8, 4096); U32(sections, 12, section); U32(sections, 16, 4096);
        byte[] Symbol(string name, uint offset)
        {
            byte[] text = Encoding.ASCII.GetBytes(name + "\0"), bytes = new byte[14 + text.Length];
            U16(bytes, 0, (ushort)(bytes.Length - 2)); U16(bytes, 2, 0x110e); U32(bytes, 4, code ? 1u : 0);
            U32(bytes, 8, offset); U16(bytes, 12, 1); text.CopyTo(bytes, 14); return bytes;
        }
        byte[][] streams = [[], info, [], dbi, sections, [..Symbol("gptiForeground", 0x10), ..Symbol(duplicate ? "gptiForeground" : "gptCursorAsync", 0x20)]];
        var file = new byte[8 * 512]; "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8.CopyTo(file);
        U32(file, 32, 512); U32(file, 40, 8); U32(file, 44, 44); U32(file, 52, 2); U32(file, 2 * 512, 3);
        int dir = 3 * 512; U32(file, dir, 6); int page = 4, blocks = dir + 28;
        for (int i = 0; i < streams.Length; i++)
        {
            U32(file, dir + 4 + i * 4, (uint)streams[i].Length);
            if (streams[i].Length == 0) continue;
            U32(file, blocks, (uint)page); blocks += 4; streams[i].CopyTo(file, page++ * 512);
        }
        return file;
    }
}
