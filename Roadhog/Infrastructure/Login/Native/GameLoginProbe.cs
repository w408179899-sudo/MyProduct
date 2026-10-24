using System.Text;
using System.Text.Json;
using Vmmsharp;

// Diagnostic adapter for the layouts archived in Roadhog/docs/LOGIN_UI_DATA.md.
namespace Roadhog.Infrastructure.Login.Native;

internal sealed class GameLoginProbe(VmmProcess process)
{
    private readonly Dictionary<(ulong,int),byte[]> guards=new();
    private byte[] Read(ulong a,int n)
    {
        if(a<0x10000||a>=0x800000000000||n<0||n>65536)throw new InvalidOperationException("Invalid game diagnostic read.");
        var b=process.MemRead(a,(uint)n,1);
        return b is not null&&b.Length==n?b:throw new InvalidOperationException($"Incomplete game read {a:X}/{n}");
    }
    private byte[] Guard(ulong a,int n){if(!guards.TryGetValue((a,n),out var b)){b=Read(a,n);guards[(a,n)]=b;}return b;}
    private ulong U(ulong a)=>BitConverter.ToUInt64(Guard(a,8));
    private int I(ulong a)=>BitConverter.ToInt32(Guard(a,4));
    private string Name(ulong a)
    {
        var b=Guard(a+8,32);ulong n=BitConverter.ToUInt64(b,16),cap=BitConverter.ToUInt64(b,24);
        if(n>256||cap<n||cap>=4096)throw new InvalidOperationException("Invalid game widget name.");
        return n==0?"":Encoding.UTF8.GetString(cap<16?b.AsSpan(0,(int)n):Guard(BitConverter.ToUInt64(b),(int)n));
    }
    private string Wide(ulong a)
    {
        var b=Guard(a,32);ulong n=BitConverter.ToUInt64(b,16),cap=BitConverter.ToUInt64(b,24);
        if(n>8192||cap<n||cap>16384)throw new InvalidOperationException("Invalid game text.");
        return n==0?"":Encoding.Unicode.GetString(cap<8?b.AsSpan(0,(int)n*2):Guard(BitConverter.ToUInt64(b),(int)n*2));
    }
    private string Text(ulong a)
    {
        var code=Guard(U(U(a)+632),24);
        if(code[0]==0x33&&code[1]==0xc0&&code[2]==0xc3)return "";
        if(code[0]!=0x48||code[2]!=0x81)throw new InvalidOperationException("Unsupported text getter.");
        int off=BitConverter.ToInt32(code,3);
        if(code[1]==0x8b&&off==0x2f8){ulong begin=U(a+0x2f8),end=U(a+0x300);if(end<begin||end-begin>65536)throw new InvalidOperationException("Invalid label runs.");return begin==end?"":Wide(begin+40);}
        if(code[1]!=0x8d||off<0||off>8192)throw new InvalidOperationException("Unsupported text layout.");
        return Wide(a+(ulong)off);
    }
    private static string Hex(ulong a)=>$"0x{a:X}";
    private sealed record Node(string Address,string Name,double X,double Y,double W,double H,bool Enabled,string? Text,string? TextDiagnostic);
    private List<Node> Nodes(ulong root)
    {
        var list=new List<Node>();var seen=new HashSet<ulong>();
        void Walk(ulong a,double x,double y,int depth,bool parentEnabled)
        {
            if(depth>15||seen.Count>1200||!seen.Add(a))throw new InvalidOperationException("Invalid game widget tree.");
            uint flags=BitConverter.ToUInt32(Guard(a+0x28,4));if((flags&1)==0)return;
            var b=Guard(a+0x58,64);
            double D(int off){double value=BitConverter.ToDouble(b,off);if(!double.IsFinite(value)||Math.Abs(value)>100000)throw new InvalidOperationException("Invalid widget coordinate.");return value;}
            x+=D(0);y+=D(8);double w=D(16),h=D(24);if(w<0||h<0)throw new InvalidOperationException("Invalid widget dimensions.");
            string name=Name(a);bool enabled=parentEnabled&&(flags&2)!=0;string? text=null,error=null;
            if(!name.Contains("html",StringComparison.OrdinalIgnoreCase)){try{text=Text(a);}catch(InvalidOperationException ex){error=ex.Message;}}
            list.Add(new(Hex(a),name,x,y,w,h,enabled,text,error));
            ulong head=U(a+0x238);if(head==0)return;ulong n=U(head);
            for(int i=0;n!=head;i++){if(n==0||i>=256)throw new InvalidOperationException("Invalid children list.");Walk(U(n+0x10),x+D(32),y+D(40),depth+1,enabled);n=U(n);}
        }
        Walk(root,0,0,0,true);return list;
    }
    public object Capture()
    {
        var info=process.GetInfo();if(!info.fValid||info.dwState!=0||info.fWow64)throw new InvalidOperationException("Expected live 64-bit Aion.");
        var module=process.MapModuleFromName("Game.dll");ulong b=module.vaBase;
        if(b==0||module.cbImageSize<0xdb0000)throw new InvalidOperationException("Game.dll has not finished loading.");
        var mz=Guard(b,64);if(mz[0]!='M'||mz[1]!='Z')throw new InvalidOperationException("Game module identity invalid.");
        var viewport=Guard(b+0xdacf00,16);double width=BitConverter.ToDouble(viewport),height=BitConverter.ToDouble(viewport,8);
        if(width<320||height<200||width>16384||height>16384)throw new InvalidOperationException("Game viewport not ready.");
        var windows=new List<object>();
        foreach(int slot in new[]{1,2,3,4}.Concat(Enumerable.Range(336,35)))
        {
            ulong ptr=U(b+0xd63990+(ulong)slot*8);if(ptr==0)continue;
            if((I(ptr+0x28)&1)==0)continue;
            object? message=null;
            if(slot>=336)
            {
                ulong body=U(ptr+0x560),check=slot>=366?U(ptr+0x5a0):0;
                message=new{Owner=I(ptr+0x3c8),Style=I(ptr+0x4d8),ConfirmEvent=I(ptr+0x4dc),CancelEvent=I(ptr+0x4e0),Tag=I(ptr+0x4e4),Body=body==0?null:Text(body),Ok=Hex(U(ptr+0x578)),Cancel=Hex(U(ptr+0x580)),Checkbox=Hex(check),Checked=check!=0&&I(check+0x30)/4>0};
            }
            windows.Add(new{Slot=slot,Address=Hex(ptr),Name=Name(ptr),Message=message,Nodes=Nodes(ptr)});
        }
        var servers=new List<object>();ulong sb=U(b+0x951a28),se=U(b+0x951a30);
        if(se<sb||se-sb>8*512||(se-sb)%8!=0)throw new InvalidOperationException("Invalid server vector.");
        for(int i=0;i<(int)((se-sb)/8);i++)
        {
            ulong p=U(sb+(ulong)i*8);if(I(p)!=1)continue;
            var raw=Guard(p+20,48);int len=0;while(len<raw.Length&&BitConverter.ToUInt16(raw,len)!=0)len+=2;
            servers.Add(new{Index=servers.Count,Address=Hex(p),Id=I(p+4),PlayId=I(p+8),Name=Encoding.Unicode.GetString(raw,0,len),DisplayName=Wide(p+112),Online=Guard(p+19,1)[0]!=0,CharacterCount=I(p+344)});
        }
        ulong sw=U(b+0xd63990+3*8),sl=sw==0?0:U(sw+0x4d8);int selectedServer=sl==0?-1:I(sl+0x2ec);
        var chars=new List<object>();ulong begin=U(b+0xd66408),end=U(b+0xd66410);
        if(end<begin||end-begin>8*8||(end-begin)%8!=0)throw new InvalidOperationException("Invalid character vector.");
        for(int i=0;i<(int)((end-begin)/8);i++)
        {
            ulong p=U(begin+(ulong)i*8);var name=Guard(p+4,64);int len=0;while(len<name.Length&&BitConverter.ToUInt16(name,len)!=0)len+=2;
            chars.Add(new{Index=i,Address=Hex(p),Id=BitConverter.ToUInt32(Guard(p,4)),Name=Encoding.Unicode.GetString(name,0,len)});
        }
        int selected=I(b+0xd66888),gameState=I(b+0xd6ca90);uint map=BitConverter.ToUInt32(Guard(b+0xd647c0+0x20dc,4));ushort local=BitConverter.ToUInt16(Guard(b+0xd6cb08,2));
        foreach(var pair in guards)if(!Read(pair.Key.Item1,pair.Key.Item2).AsSpan().SequenceEqual(pair.Value))throw new InvalidOperationException($"Game snapshot changed at {pair.Key.Item1:X}; retry capture.");
        var after=process.GetInfo();if(!after.fValid||after.dwState!=0||after.vaEPROCESS!=info.vaEPROCESS||after.vaPEB!=info.vaPEB)throw new InvalidOperationException("Game process session changed.");
        return new{CapturedUtc=DateTimeOffset.UtcNow,Pid=process.PID,Eprocess=Hex(info.vaEPROCESS),GameBase=Hex(b),module.cbImageSize,Viewport=new[]{width,height},Windows=windows,Servers=servers,SelectedServerIndex=selectedServer,PreviousServerId=I(b+0x9519e4),Characters=chars,SelectedIndex=selected,GameState=gameState,MapId=map,LocalEntityId=local,CriticalFieldsRevalidated=true};
    }
}
