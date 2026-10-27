using System.Text;

namespace Roadhog.Infrastructure.Login.Native;

// Read-only recognition of complete DuiLib GetClass/GetInterface method bodies.
// This proves the container parent chain when RTTI is paged out; it never executes
// target code and never treats an unreadable/unknown method as a leaf control.
internal sealed class UuMethodTypes(Func<ulong,int,byte[]?> read, Func<uint,bool>? certifiedComparison=null)
{
    internal static UuMethodTypes For(Vmmsharp.VmmProcess process)
    {
        byte[]? Read(ulong at,int size)=>process.MemRead(at,(uint)size,1);
        HashSet<uint>? comparisons=null;
        return new(Read,address=>
        {
            if(comparisons is null)
            {
                var module=process.MapModuleFromName("ucrtbase.dll");
                if(module.vaBase==0)return false;
                comparisons=new[]{"wcscmp","_wcsicmp"}.Select(name=>LoginPeExports.Find(Read,checked((uint)module.vaBase),module.cbImageSize,name)).Where(a=>a!=0).ToHashSet();
            }
            return comparisons.Contains(address);
        });
    }
    private static readonly byte[] Class = Convert.FromHexString("558BEC51894DFCB8000000008BE55DC3");
    private static readonly byte[] Control = Convert.FromHexString("558BEC51894DFC68000000008B450850FF150000000083C40885C075058B45FCEB0233C08BE55DC20400");
    private static readonly byte[] Parent = Convert.FromHexString("558BEC51894DFC68000000008B450850FF150000000083C40885C075058B45FCEB0C8B4D08518B4DFCE8000000008BE55DC20400");
    private static readonly byte[] Container = Convert.FromHexString("558BEC83EC08894DFC68000000008B450850FF150000000083C40885C07522837DFC00740E8B4DFC81C168070000894DF8EB07C745F8000000008B45F8EB29EB1B68000000008B550852FF150000000083C40885C075058B45FCEB0C8B4508508B4DFCE8000000008BE55DC20400");
    // The complete, relocation-free UTF-16 comparison routine captured on script 2.
    // Its only return-zero path is equality through the terminating UTF-16 word.
    private static readonly byte[] WideCompare = Convert.FromHexString("8BFF558BEC568B750C578B7D080FB70E0FB7172BD175198BC12BFE6685C0741083C6020FB70E0FB714378BC12BD174EB8BC2C1EA1FF7D8C1E81F5F2BC25E5DC3");
    private readonly Dictionary<uint,bool?> _interfaces=[];
    private readonly HashSet<uint> _comparisons=[];

    internal bool TryGet(uint vtable,out string name,out bool container)
    {
        name="";container=false;
        if(!Pointer(vtable))return false;
        var slots=Exact(vtable+20,8);
        uint getClass=Resolve(BitConverter.ToUInt32(slots));
        var method=Exact(getClass,Class.Length);
        if(!Matches(method,Class,8))return false;
        name=Text(BitConverter.ToUInt32(method,8));
        if(name.Length==0)return false;
        var result=Interface(BitConverter.ToUInt32(slots,4),new HashSet<uint>());
        if(result is null)return false;
        container=result.Value;return true;
    }

    private bool? Interface(uint address,HashSet<uint> seen)
    {
        address=Resolve(address);
        if(seen.Count>=16||!seen.Add(address))throw new InvalidOperationException("Cyclic UU interface parent chain.");
        if(_interfaces.TryGetValue(address,out var known))return known;
        var prefix=Exact(address,Control.Length);
        bool? result;
        if(Matches(prefix,Control,8,18))
        {
            if(Text(BitConverter.ToUInt32(prefix,8))!="Control"||!Compare(BitConverter.ToUInt32(prefix,18)))return null;
            result=false;
        }
        else if(prefix.AsSpan(0,8).SequenceEqual(Container.AsSpan(0,8)))
        {
            var code=Exact(address,Container.Length);
            if(!Matches(code,Container,10,20,66,76,100)||Text(BitConverter.ToUInt32(code,10))!="IContainer"
                ||Text(BitConverter.ToUInt32(code,66))!="Container"||!Compare(BitConverter.ToUInt32(code,20))
                ||!Compare(BitConverter.ToUInt32(code,76))||Interface(Relative(address,code,100),seen)!=false)return null;
            result=true;
        }
        else
        {
            var code=Exact(address,Parent.Length);
            if(!Matches(code,Parent,8,18,42)||!Compare(BitConverter.ToUInt32(code,18)))return null;
            var label=Text(BitConverter.ToUInt32(code,8));
            if(label.Length==0||label is "Container" or "IContainer" or "Control")return null;
            result=Interface(Relative(address,code,42),seen);
        }
        _interfaces[address]=result;return result;
    }

    private bool Compare(uint iat)
    {
        uint pointer=BitConverter.ToUInt32(Exact(iat,4));
        if(certifiedComparison?.Invoke(pointer)==true)return true;
        uint target=Resolve(pointer);
        if(_comparisons.Contains(target))return true;
        if(!Exact(target,WideCompare.Length).AsSpan().SequenceEqual(WideCompare))return false;
        _comparisons.Add(target);return true;
    }
    private uint Resolve(uint function)
    {
        var seen=new HashSet<uint>();
        for(int i=0;i<4;i++)
        {
            if(!seen.Add(function))throw new InvalidOperationException("Cyclic UU method thunk.");
            var code=Exact(function,6);
            if(code[0]==0xff&&code[1]==0x25)function=BitConverter.ToUInt32(Exact(BitConverter.ToUInt32(code,2),4));
            else if(code[0]==0xe9)function=Relative(function,code,1);
            else return function;
        }
        throw new InvalidOperationException("UU method thunk chain exceeds bound.");
    }
    private static uint Relative(uint address,byte[] code,int field)=>checked((uint)((long)address+field+4+BitConverter.ToInt32(code,field)));
    private static bool Matches(byte[] code,byte[] pattern,params int[] fields)=>code.Length==pattern.Length
        &&Enumerable.Range(0,code.Length).All(i=>fields.Any(start=>i>=start&&i<start+4)||code[i]==pattern[i]);
    private string Text(uint address)
    {
        if(!Pointer(address)||address%2!=0)throw new InvalidOperationException("Invalid UU interface string pointer.");
        var bytes=new List<byte>();
        for(int offset=0;offset<256;)
        {
            uint at=checked(address+(uint)offset);int length=Math.Min(256-offset,4096-(int)(at%4096));
            var part=Exact(at,length);
            for(int i=0;i<part.Length;i+=2)
            {if(part[i]==0&&part[i+1]==0)return Encoding.Unicode.GetString(bytes.ToArray());bytes.Add(part[i]);bytes.Add(part[i+1]);}
            offset+=length;
        }
        throw new InvalidOperationException("UU interface name is not terminated.");
    }
    private static bool Pointer(uint value)=>value>=0x10000&&value<0x80000000;
    private byte[] Exact(uint address,int length)
    {
        if(!Pointer(address)||(ulong)address+(uint)length>=0x80000000)throw new InvalidOperationException("Invalid UU method pointer.");
        var bytes=read(address,length);
        return bytes is not null&&bytes.Length==length?bytes:throw new UuMetadataReadException("method identity",address,length,bytes?.Length??0);
    }
}
