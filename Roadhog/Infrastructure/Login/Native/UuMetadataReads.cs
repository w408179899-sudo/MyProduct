using System.Runtime.CompilerServices;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

// A missing read may try another complete proof. A contradictory identity must
// still abort the capture; it is never converted into a leaf or a guessed type.
internal sealed class UuMetadataReadException(string field, ulong address, int expected, int actual)
    : InvalidOperationException($"Incomplete UU {field}: address=0x{address:X}, expected={expected}, actual={actual}.");

internal sealed class UuModuleIdentityCache
{
    private string? _session;
    private readonly Dictionary<UuControlTypes.Module, bool> _values = [];
    internal void Bind(string session)
    {
        if (_session == session) return;
        _session = session; _values.Clear();
    }
    internal bool Resolve(UuControlTypes.Module module, Func<bool> verify)
    {
        try { return _values[module] = verify(); }
        catch (UuMetadataReadException) when (_values.ContainsKey(module)) { return _values[module]; }
        catch { _values.Remove(module); throw; }
    }
}

internal static class UuMetadataReads
{
    private static readonly ConditionalWeakTable<Vmmsharp.Vmm, UuModuleIdentityCache> Identities = new();
    internal static UuUiReader Reader(Vmmsharp.Vmm vmm, VmmProcess process, string session)
    {
        var identities = Identities.GetValue(vmm, _ => new());
        identities.Bind(session);
        return new((at, count) => process.MemRead(at, (uint)count, 1), UuControlTypes.For(process, identities), UuMethodTypes.For(process));
    }
}
