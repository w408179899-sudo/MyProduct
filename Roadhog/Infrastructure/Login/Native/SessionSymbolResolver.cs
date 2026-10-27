using System.Runtime.CompilerServices;
using System.Text;
using Roadhog.Infrastructure.Vmm;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

internal sealed record SessionCodeView(Guid Guid, uint Age, ulong Address, byte[] Bytes);
internal sealed record SessionSymbolResolution(SessionSymbolRvas Rvas, SessionCodeView? Evidence = null);

// Immutable symbol metadata belongs to this connection/session, not to a global
// address cache. Live foreground ownership and mouse data are never cached here.
internal sealed class SessionSymbolCache
{
    private string? _scope;
    private SessionSymbolResolution? _value;
    internal SessionSymbolRvas Resolve(string scope, Func<SessionSymbolResolution> capture, Func<ulong, int, byte[]?> read)
    {
        if (_scope != scope) { _scope = scope; _value = null; }
        if (_value?.Evidence is { } evidence)
        {
            var current = read(evidence.Address, evidence.Bytes.Length);
            if (current is not null && current.Length == evidence.Bytes.Length && !current.AsSpan().SequenceEqual(evidence.Bytes))
            {
                _value = null;
                throw new InvalidOperationException("Windows session module CodeView identity changed.");
            }
        }
        _value ??= capture();
        return _value.Rvas;
    }
}

internal static class SessionSymbolResolver
{
    private static readonly ConditionalWeakTable<Vmmsharp.Vmm, SessionSymbolCache> Caches = new();
    private static readonly byte[] PdbName = Encoding.ASCII.GetBytes("win32kbase.pdb\0");

    internal static (ulong Foreground, ulong Cursor) Resolve(Vmmsharp.Vmm vmm, uint sessionPid)
    {
        var process = SessionInputProbe.SessionProcess(vmm, sessionPid);
        var info = process.GetInfo(); var module = process.MapModuleFromName("win32kbase.sys");
        if (!info.fValid || info.dwState != 0 || module.vaBase == 0 || module.cbImageSize == 0 || module.cbImageSize > 16 * 1024 * 1024)
            throw new InvalidOperationException("Windows session module is not initialized.");
        string scope = $"{info.dwPID}:{info.vaEPROCESS:X}:{info.vaPEB:X}:{module.vaBase:X}:{module.cbImageSize}";
        byte[]? Read(ulong at, int length) => process.MemRead(at, checked((uint)length), 1);
        var cache = Caches.GetValue(vmm, _ => new());
        SessionSymbolRvas rvas;
        lock (cache) rvas = cache.Resolve(scope, () => Capture(process, module.vaBase, module.cbImageSize, Read), Read);
        var after = process.GetInfo(); var afterModule = process.MapModuleFromName("win32kbase.sys");
        if (!after.fValid || after.dwState != 0 || after.vaEPROCESS != info.vaEPROCESS || after.vaPEB != info.vaPEB
            || afterModule.vaBase != module.vaBase || afterModule.cbImageSize != module.cbImageSize)
            throw new InvalidOperationException("Windows session changed while resolving symbols.");
        return (module.vaBase + rvas.Foreground, module.vaBase + rvas.Cursor);
    }

    private static SessionSymbolResolution Capture(VmmProcess process, ulong module, uint size, Func<ulong, int, byte[]?> read)
    {
        try
        {
            var pdb = process.Pdb(module);
            if (pdb.SymbolAddress("gptiForeground", out ulong foreground) && pdb.SymbolAddress("gptCursorAsync", out ulong cursor)
                && foreground >= module && cursor >= module && foreground - module + 8 <= size && cursor - module + 8 <= size)
                return new(new(checked((uint)(foreground - module)), checked((uint)(cursor - module))));
        }
        catch (VmmException) { /* A nonresident PE header can prevent native PDB registration. */ }
        var evidence = FindCodeView(read, module, size);
        string identity = evidence.Guid.ToString("N").ToUpperInvariant() + evidence.Age.ToString("X");
        string path = Path.Combine(MemProcFsNativeRuntime.LoadedDirectory, "Symbols", "win32kbase.pdb", identity, "win32kbase.pdb");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 64 * 1024 * 1024)
            throw new InvalidOperationException($"Matching Windows session PDB cache is unavailable: {identity}.");
        SessionSymbolRvas symbols;
        try { symbols = CachedPdbSessionSymbols.Read(File.ReadAllBytes(path), evidence.Guid, evidence.Age, size); }
        catch (InvalidDataException error) { throw new InvalidOperationException("Windows session PDB validation failed: " + error.Message, error); }
        var verify = read(evidence.Address, evidence.Bytes.Length);
        if (verify is null || !verify.AsSpan().SequenceEqual(evidence.Bytes))
            throw new InvalidOperationException("Windows session CodeView changed while reading its PDB.");
        return new(symbols, evidence);
    }

    internal static SessionCodeView FindCodeView(Func<ulong, int, byte[]?> read, ulong module, uint size)
    {
        if (size == 0 || size > 16 * 1024 * 1024 || module > ulong.MaxValue - size)
            throw new InvalidOperationException("Invalid Windows session module bounds.");
        var found = new List<SessionCodeView>(); byte[] previous = [];
        for (uint offset = 0; offset < size; offset += 4096)
        {
            int length = (int)Math.Min(4096, size - offset); var page = read(module + offset, length);
            if (page is null || page.Length != length) { previous = []; continue; }
            byte[] scan = [..previous, ..page];
            for (int i = 0; i + 4 <= scan.Length; i++)
            {
                if (!scan.AsSpan(i, 4).SequenceEqual("RSDS"u8)) continue;
                ulong at = module + offset - (uint)previous.Length + (uint)i;
                int count = 24 + PdbName.Length;
                if (at + (uint)count > module + size) continue;
                var bytes = read(at, count);
                if (bytes is null || bytes.Length != count || !bytes.AsSpan(0, 4).SequenceEqual("RSDS"u8)
                    || !bytes.AsSpan(24).SequenceEqual(PdbName)) continue;
                var guid = new Guid(bytes.AsSpan(4, 16)); uint age = BitConverter.ToUInt32(bytes, 20);
                if (guid == Guid.Empty || age == 0) continue;
                found.Add(new(guid, age, at, bytes));
            }
            previous = page[^Math.Min(3, page.Length)..];
        }
        if (found.Count != 1) throw new InvalidOperationException($"Windows session CodeView identity is not unique/readable (matches={found.Count}).");
        var result = found[0]; var recheck = read(result.Address, result.Bytes.Length);
        if (recheck is null || !recheck.AsSpan().SequenceEqual(result.Bytes))
            throw new InvalidOperationException("Windows session CodeView identity changed during capture.");
        return result;
    }
}
