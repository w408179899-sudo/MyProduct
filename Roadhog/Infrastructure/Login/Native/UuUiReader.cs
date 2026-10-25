using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Vmmsharp;
namespace Roadhog.Infrastructure.Login.Native;
internal sealed class UuUiReader(Func<ulong,int,byte[]?> readMemory, UuControlTypes? verifiedTypes=null, UuMethodTypes? methodTypes=null)
{
    internal UuUiReader(VmmProcess process):this((address,length)=>process.MemRead(address,(uint)length,1),UuControlTypes.For(process),
        UuMethodTypes.For(process)) { }
    private readonly Dictionary<uint,string> typeCache=new();
    private readonly Dictionary<uint,bool> containerCache=new();
    private sealed record UiRect(int Left,int Top,int Right,int Bottom);
    private sealed record Node(string Address,string Parent,string Manager,string Class,string Name,UiRect Rect,bool Visible,bool AncestorsVisible,bool Enabled,string? Text,int Depth);
    public object Run(JsonElement window)
    {
        uint manager=Convert.ToUInt32(window.GetProperty("Manager").GetString()![2..],16);
        uint root=Convert.ToUInt32(window.GetProperty("Root").GetString()![2..],16);
        if(!Plausible(manager)||!Plausible(root))throw new InvalidOperationException("UU window manager is not ready.");
        typeCache.Clear();containerCache.Clear();
        var nodes=new List<Node>();
        Visit(root,manager,true,true,0,new(),nodes);
        return new{Controls=nodes};
    }
    private void Visit(uint address,uint manager,bool ancestorsVisible,bool ancestorsEnabled,int depth,HashSet<uint> visited,List<Node> nodes)
    {
        if(depth>40||visited.Count>=20000||!Plausible(address)||!visited.Add(address))
            throw new InvalidOperationException("UU traversal is incomplete or cyclic.");
        // An inactive control's later pages can be nonresident. Read its identity and
        // visibility independently before requiring the payload of an actionable node.
        var owner=Read(address+0x14c,8);
        var state=Read(address+0x2b0,3);
        if(owner is null||state is null||U32(owner,0)!=manager)throw new InvalidOperationException("UU control manager changed.");
        bool visible=state[0]!=0&&state[1]!=0, enabled=state[2]!=0&&ancestorsEnabled;
        // Visibility is an independently complete fact. A hidden subtree cannot contain
        // actionable controls, and its dormant/paged-out children are not scene inputs.
        if(!visible||!ancestorsVisible)return;
        var data=Read(address,0x370);
        if(data is null||!data.AsSpan(0x14c,8).SequenceEqual(owner)||!data.AsSpan(0x2b0,3).SequenceEqual(state))
            throw new InvalidOperationException("UU control identity or visibility changed during capture.");
        uint vtable=U32(data,0),parent=U32(data,0x150);
        string type=GetTypeName(vtable);
        string name=ReadString(U32(data,0x1d8),128)??"";
        var rect=new UiRect(I32(data,0x260),I32(data,0x264),I32(data,0x268),I32(data,0x26c));
        string? text=null;
        if(new[]{"launch_game","stop_acc","start_acc","game_name","acced_time","acced_game_name","acc_status","acc_state","acc_tip","net_delay","accing_text","accing_tips","accing_progress","acc_progress","Ok","Cancel","error_code_desc","error_code_desc2","error_code"}.Contains(name))text=ReadString(U32(data,0x2e4),512);
        nodes.Add(new(Hex(address),Hex(parent),Hex(manager),type,name,rect,visible,ancestorsVisible,enabled,text,depth));
        if(type.Length>0&&!IsContainer(vtable))return;
        var children=LoginUiChildren.Read(readMemory,address,manager,0x76c,0x14c,depth==0||type.Length>0,10000);
        foreach(uint child in children)
            Visit(child,manager,ancestorsVisible&&visible,enabled,depth+1,visited,nodes);
        if(!LoginUiChildren.Read(readMemory,address,manager,0x76c,0x14c,depth==0||type.Length>0,10000).SequenceEqual(children))
            throw new InvalidOperationException("UU children changed during traversal.");
    }
    private string GetTypeName(uint vtable)
    {
        if(typeCache.TryGetValue(vtable,out var name))return name;
        if(verifiedTypes is not null&&verifiedTypes.TryGet(vtable,out var known))
        {
            containerCache[vtable]=known.IsContainer;
            return typeCache[vtable]=known.DecoratedName;
        }
        if(methodTypes is not null&&methodTypes.TryGet(vtable,out var className,out bool container))
        {
            containerCache[vtable]=container;
            return typeCache[vtable]=className;
        }
        var slot=Read(vtable-4,4);
        var locator=slot is null?null:Read(U32(slot,0),20);
        var descriptor=locator is null?null:Read(U32(locator,12)+8,160);
        name=descriptor is null?"":Encoding.ASCII.GetString(descriptor).Split('\0')[0];
        if(!name.StartsWith(".?AV")&&!name.StartsWith(".?AU"))name="";
        typeCache[vtable]=name;
        return name;
    }
    private bool IsContainer(uint vtable)
    {
        if(containerCache.TryGetValue(vtable,out bool known))return known;
        bool found=false;
        var slot=Read(vtable-4,4);
        var locator=slot is null?null:Read(U32(slot,0),20);
        var hierarchy=locator is null?null:Read(U32(locator,16),16);
        if(hierarchy is null)throw new InvalidOperationException("UU control inheritance is incomplete.");
        if(hierarchy is not null)
        {
            int count=I32(hierarchy,8);
            if(count<=0||count>128)throw new InvalidOperationException("Invalid UU control inheritance bounds.");
            if(count>0&&count<=128)
            {
                var bases=Read(U32(hierarchy,12),count*4);
                if(bases is null)throw new InvalidOperationException("Incomplete UU control inheritance.");
                if(bases is not null)for(int i=0;i<count;i++)
                {
                    var b=Read(U32(bases,i*4),4);
                    var name=b is null?null:Read(U32(b,0)+8,100);
                    if(name is null)throw new InvalidOperationException("Incomplete UU base class.");
                    if(name is not null&&Encoding.ASCII.GetString(name).Split('\0')[0]==".?AVCContainerUI@DuiLib@@")found=true;
                }
            }
        }
        containerCache[vtable]=found;return found;
    }
    private string? ReadString(uint pointer,int maxChars)
    {
        if(pointer==0)return null;
        if(pointer<0x10000||pointer>=0x80000000||pointer%2!=0)throw new InvalidOperationException("Invalid UU string pointer.");
        var text=new List<byte>();
        for(int offset=0;offset<maxChars*2;)
        {
            uint at=pointer+(uint)offset;
            int length=Math.Min(maxChars*2-offset,4096-(int)(at%4096));
            var bytes=Read(at,length)??throw new InvalidOperationException("Incomplete UU control string.");
            for(int i=0;i<bytes.Length;i+=2)
            {
                if(bytes[i]==0&&bytes[i+1]==0)return Encoding.Unicode.GetString(text.ToArray());
                text.Add(bytes[i]);text.Add(bytes[i+1]);
            }
            offset+=length;
        }
        throw new InvalidOperationException("UU control string exceeds bound.");
    }
    private byte[]? Read(ulong address,int length)
    {
        if(length==0)return [];
        if(address<0x10000||address+((ulong)length)>0x80000000||length<0||length>128000)return null;
        var data=readMemory(address,length);
        if(data is null||data.Length!=length)throw new InvalidOperationException("Incomplete UU UI field.");
        return data;
    }
    private static bool Plausible(uint p)=>p>=0x10000&&p<0x80000000&&p%4==0;
    private static uint U32(byte[] bytes,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset,4));
    private static int I32(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static string Hex(uint value)=>$"0x{value:X}";
}
