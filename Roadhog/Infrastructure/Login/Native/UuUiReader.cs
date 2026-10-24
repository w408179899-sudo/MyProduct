using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Vmmsharp;
namespace Roadhog.Infrastructure.Login.Native;
internal sealed class UuUiReader(VmmProcess process)
{
    private const uint NoCache=1;
    private readonly Dictionary<uint,string> typeCache=new();
    private readonly Dictionary<uint,bool> containerCache=new();
    private int incompleteReads;
    private sealed record UiRect(int Left,int Top,int Right,int Bottom);
    private sealed record Node(string Address,string Parent,string Manager,string Class,string Name,UiRect Rect,bool Visible,bool AncestorsVisible,bool Enabled,string? Text,int Depth);
    public object Run(JsonElement window)
    {
        uint manager=Convert.ToUInt32(window.GetProperty("Manager").GetString()![2..],16);
        uint root=Convert.ToUInt32(window.GetProperty("Root").GetString()![2..],16);
        if(!Plausible(manager)||!Plausible(root))throw new InvalidOperationException("UU window manager is not ready.");
        var nodes=new List<Node>();
        Visit(root,manager,true,0,new(),nodes);
        return new{Controls=nodes};
    }
    private void Visit(uint address,uint manager,bool ancestorsVisible,int depth,HashSet<uint> visited,List<Node> nodes)
    {
        if(depth>40||visited.Count>=20000||!Plausible(address)||!visited.Add(address))return;
        var data=Read(address,0x370);
        if(data is null||U32(data,0x14c)!=manager)return;
        uint vtable=U32(data,0),parent=U32(data,0x150);
        string type=GetTypeName(vtable);
        string name=ReadString(U32(data,0x1d8),128)??"";
        var rect=new UiRect(I32(data,0x260),I32(data,0x264),I32(data,0x268),I32(data,0x26c));
        bool visible=data[0x2b0]!=0&&data[0x2b1]!=0, enabled=data[0x2b2]!=0;
        string? text=null;
        if(new[]{"launch_game","stop_acc","start_acc","game_name","acced_time","acced_game_name","acc_status","acc_state","acc_tip","net_delay","accing_text","accing_tips","accing_progress","acc_progress"}.Contains(name))text=ReadString(U32(data,0x2e4),128);
        nodes.Add(new(Hex(address),Hex(parent),Hex(manager),type,name,rect,visible,ancestorsVisible,enabled,text,depth));
        if(type.Length>0&&!IsContainer(vtable))return;
        var array=Read(address+0x76c,12);
        if(array is null)return;
        uint children=U32(array,0);int count=I32(array,4),capacity=I32(array,8);
        if(count<0||count>10000||capacity<count||capacity>20000)return;
        if(count==0)return;
        var childData=Read(children,count*4);
        if(childData is null)return;
        for(int i=0;i<count;i++)
        {
            uint child=U32(childData,i*4);
            var relationship=Read(child+0x14c,8);
            if(relationship is null||U32(relationship,0)!=manager||U32(relationship,4)!=address)continue;
            Visit(child,manager,ancestorsVisible&&visible,depth+1,visited,nodes);
        }
        var after=Read(address+0x76c,12);
        if(after is null||!after.AsSpan().SequenceEqual(array))throw new InvalidOperationException("UI children changed during traversal.");
    }
    private string GetTypeName(uint vtable)
    {
        if(typeCache.TryGetValue(vtable,out var name))return name;
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
        if(hierarchy is not null)
        {
            int count=I32(hierarchy,8);
            if(count>0&&count<=128)
            {
                var bases=Read(U32(hierarchy,12),count*4);
                if(bases is not null)for(int i=0;i<count;i++)
                {
                    var b=Read(U32(bases,i*4),4);
                    var name=b is null?null:Read(U32(b,0)+8,100);
                    if(name is not null&&Encoding.ASCII.GetString(name).Split('\0')[0]==".?AVCContainerUI@DuiLib@@")found=true;
                }
            }
        }
        containerCache[vtable]=found;return found;
    }
    private string? ReadString(uint pointer,int maxChars)
    {
        if(!Plausible(pointer))return null;
        int length=Math.Min(maxChars*2,4096-(int)(pointer%4096));
        var bytes=Read(pointer,length);
        if(bytes is null)return null;
        int end=0;while(end+1<bytes.Length && (bytes[end]!=0||bytes[end+1]!=0))end+=2;
        if(end>=bytes.Length-1)return null;
        return Encoding.Unicode.GetString(bytes,0,end);
    }
    private byte[]? Read(ulong address,int length)
    {
        if(length==0)return [];
        if(address<0x10000||address+((ulong)length)>0x80000000||length<0||length>128000){incompleteReads++;return null;}
        var data=process.MemRead(address,(uint)length,NoCache);
        if(data is null||data.Length!=length){incompleteReads++;return null;}
        return data;
    }
    private static bool Plausible(uint p)=>p>=0x10000&&p<0x80000000&&p%4==0;
    private static uint U32(byte[] bytes,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset,4));
    private static int I32(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static string Hex(uint value)=>$"0x{value:X}";
}
