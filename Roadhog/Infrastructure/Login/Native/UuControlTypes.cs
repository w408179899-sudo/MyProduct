using System.Text.Json;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

// Immutable type metadata, not a UI snapshot. Some idle UU RTTI pages are paged out
// and unavailable to DMA. Use recorded types only after validating the actual PE
// identity and all nine resident vtable slots (COL plus eight function pointers).
// Unknown builds/types still use the native RTTI reader; no missing type means leaf.
internal sealed class UuControlTypes(Func<ulong,int,byte[]?> read, IReadOnlyList<UuControlTypes.Module> modules, UuModuleIdentityCache? sharedIdentity=null)
{
    internal sealed record Module(string Name, ulong Base, uint Size);
    internal sealed record ControlType(uint VtableRva, string DecoratedName, bool IsContainer, uint ColRva, uint[] First8FunctionRvas);
    internal sealed record Catalog(string ModuleName, ushort Machine, uint TimeDateStamp, uint SizeOfImage,
        ushort OptionalHeaderMagic, ControlType[] Types);
    private static readonly Catalog[] Catalogs = LoadCatalogs();
    private readonly Dictionary<ulong,bool> _identities = [];

    internal static UuControlTypes For(VmmProcess process, UuModuleIdentityCache? identity=null) => new((at,size)=>process.MemRead(at,(uint)size,1),
        new[]{"ui.dll","uu.exe"}.Select(name=>
        {
            var module=process.MapModuleFromName(name);
            return new Module(name,module.vaBase,module.cbImageSize);
        }).Where(m=>m.Base!=0).ToArray(), identity);

    internal bool TryGet(uint vtable, out ControlType type)
    {
        type=null!;
        var module=modules.SingleOrDefault(m=>vtable>=m.Base&&vtable<m.Base+m.Size);
        if(module is null)return false;
        var catalog=Catalogs.SingleOrDefault(c=>c.ModuleName.Equals(module.Name,StringComparison.OrdinalIgnoreCase));
        if(catalog is null)return false;
        if(!_identities.TryGetValue(module.Base,out bool matches))
        {
            bool Verify()
            {
                var dos=Exact(module.Base,0x40);
                int offset=BitConverter.ToInt32(dos,0x3c);
                if(BitConverter.ToUInt16(dos)!=0x5a4d||offset<0x40||offset>0x1000)
                    throw new InvalidOperationException("Invalid UU module header.");
                var pe=Exact(module.Base+(uint)offset,0x80);
                if(BitConverter.ToUInt32(pe)!=0x4550)throw new InvalidOperationException("Invalid UU PE signature.");
                return module.Size==catalog.SizeOfImage&&BitConverter.ToUInt16(pe,4)==catalog.Machine
                    &&BitConverter.ToUInt32(pe,8)==catalog.TimeDateStamp&&BitConverter.ToUInt16(pe,0x18)==catalog.OptionalHeaderMagic
                    &&BitConverter.ToUInt32(pe,0x50)==catalog.SizeOfImage;
            }
            matches=sharedIdentity is null?Verify():sharedIdentity.Resolve(module,Verify);
            _identities[module.Base]=matches;
        }
        if(!matches)return false;
        var known=catalog.Types.SingleOrDefault(t=>t.VtableRva==vtable-module.Base);
        if(known is null)return false;
        var slots=Exact(vtable-4,36);
        if(BitConverter.ToUInt32(slots)!=module.Base+known.ColRva||known.First8FunctionRvas.Length!=8
            ||known.First8FunctionRvas.Where((rva,index)=>BitConverter.ToUInt32(slots,4+index*4)!=module.Base+rva).Any())
            throw new InvalidOperationException("UU vtable does not match its recorded type identity.");
        type=known;
        return true;
    }

    private byte[] Exact(ulong at,int size)
    {
        var bytes=read(at,size);
        return bytes is not null&&bytes.Length==size?bytes:throw new UuMetadataReadException("type identity",at,size,bytes?.Length??0);
    }

    private static Catalog[] LoadCatalogs()
    {
        var assembly=typeof(UuControlTypes).Assembly;
        return new[]{"ui","uu"}.Select(name=>
        {
            using var stream=assembly.GetManifestResourceStream($"Roadhog.Infrastructure.Login.Native.UuTypes.{name}-type-catalog.json")
                ??throw new InvalidOperationException("Missing embedded UU type catalog.");
            return JsonSerializer.Deserialize<Catalog>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        }).ToArray();
    }
}
