using System.Text;
using System.Text.Json;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

internal static class SdoUiProbe
{
    public static object Run(VmmProcess p,bool verifyFake=false,string? expectedAccount=null,int? expectedPasswordLength=null)
    {
        var module=p.MapModuleFromName("duilib.dll");
        if(module.vaBase==0||module.cbImageSize!=1581056)throw new InvalidOperationException("Unexpected SDO DuiLib version geometry.");
        byte[] Read(ulong a,int n)
        {
            var x=p.MemRead(a,(uint)n,1);var y=p.MemRead(a,(uint)n,1);
            if(x is null||x.Length!=n||y is null||!x.AsSpan().SequenceEqual(y))throw new InvalidOperationException($"Incomplete/changing UI read 0x{a:X}");
            return x;
        }
        uint U32(ulong a)=>BitConverter.ToUInt32(Read(a,4));
        if(U32(module.vaBase+U32(module.vaBase+0x3c)+8)!=0x61613740)throw new InvalidOperationException("Unexpected DuiLib PE timestamp.");
        bool Ptr(uint a)=>a>=0x10000&&a<0x80000000;
        string ReadString(uint a,int max)
        {
            if(!Ptr(a))return "";
            var chars=new List<byte>();
            for(int n=0;n<max;n+=16)
            {
                var b=Read(a+(ulong)n*2,32);
                for(int j=0;j<32;j+=2){if(b[j]==0&&b[j+1]==0)return Encoding.Unicode.GetString(chars.ToArray());chars.Add(b[j]);chars.Add(b[j+1]);}
            }
            throw new InvalidOperationException("UI string exceeds bound.");
        }
        string Class(uint a)
        {
            uint vt=U32(a);if(vt<module.vaBase||vt>=module.vaBase+module.cbImageSize)return "unknown/custom";
            uint col=U32(vt-4);if(!Ptr(col))return "unknown";
            uint type=U32(col+12);if(!Ptr(type))return "unknown";
            var b=Read(type+8,96);int n=Array.IndexOf(b,(byte)0);return Encoding.ASCII.GetString(b,0,n<0?b.Length:n);
        }
        var windows=JsonSerializer.SerializeToElement(WindowInventory.Run(p),new JsonSerializerOptions{IncludeFields=true}).GetProperty("Windows");
        var results=new List<object>();
        foreach(var window in windows.EnumerateArray().Where(w=>w.GetProperty("Visible").GetBoolean()&&(Convert.ToUInt32(w.GetProperty("Style").GetString()![2..],16)&0x40000000)==0))
        {
            uint user=Convert.ToUInt32(window.GetProperty("UserData").GetString()![2..],16),hwnd=Convert.ToUInt32(window.GetProperty("Hwnd").GetString()![2..],16);
            if(!Ptr(user)){results.Add(new{Window=window,Error="No object pointer"});continue;}
            var managers=new List<object>();
            for(uint offset=0;offset<0x800;offset+=4)
            {
                uint manager=user+offset;uint root;
                try{if(U32(manager)!=hwnd)continue;root=U32(manager+0x4c);if(!Ptr(root)||U32(root+0x40)!=manager||U32(root+0x44)!=0)continue;}
                catch(InvalidOperationException){continue;}
                var controls=new List<object>();var visited=new HashSet<uint>();
                void Visit(uint address,uint expectedParent,bool parentVisible,bool parentEnabled,int depth)
                {
                    if(depth>24||visited.Count>=350||!visited.Add(address))throw new InvalidOperationException("UI traversal exceeded bound.");
                    if(U32(address+0x40)!=manager||U32(address+0x44)!=expectedParent)throw new InvalidOperationException("Control backlink changed.");
                    string name=ReadString(U32(address+0x48),96),cls=Class(address);
                    var flags=Read(address+0x110,3);bool visible=flags[0]!=0&&flags[1]!=0,enabled=flags[2]!=0;
                    var rectBytes=Read(address+0xd0,16);var rect=Enumerable.Range(0,4).Select(i=>BitConverter.ToInt32(rectBytes,i*4)).ToArray();
                    var record=new Dictionary<string,object?>{{"Address",$"0x{address:X}"},{"Name",name},{"Class",cls},{"Rect",rect},{"Visible",visible},{"Enabled",enabled},{"VisibleThroughRoot",visible&&parentVisible},{"EnabledThroughRoot",enabled&&parentEnabled}};
                    if(cls.Contains("COptionUI"))record["Selected"]=Read(address+0xab0,1)[0]!=0;
                    if(name is "HintText" or "MESSAGE_HINT_HOST" or "IdentifyingCodeHintLabel")record["Text"]=ReadString(U32(address+0x23c),512);
                    if(verifyFake&&visible&&parentVisible&&name is "AccountCombox" or "PasswordEdit")
                    {
                        uint edit=name=="AccountCombox"?address+0x798:address;
                        uint helper=U32(edit+0x854);
                        if(!Ptr(helper)||U32(helper)!=edit)throw new InvalidOperationException("Editor helper backlink failed.");
                        uint length=U32(helper+0x18),capacity=U32(helper+0x1c),mode=U32(helper+0xbc);
                        if(length>64||capacity<length||capacity>1024)throw new InvalidOperationException("Invalid editor string bounds.");
                        record["InputLength"]=length;record["EditorMode"]=mode;
                        if(name=="AccountCombox")
                        {
                            if(mode!=0)throw new InvalidOperationException("Unexpected account editor mode.");
                            uint data=capacity<8?helper+8:U32(helper+8);
                            string value=length==0?"":Encoding.Unicode.GetString(Read(data,checked((int)length*2)));
                            record["MatchesExpectedInput"]=value==(expectedAccount??"");
                            record["MatchesExpectedInputIgnoringCase"]=value.Equals(expectedAccount??"",StringComparison.OrdinalIgnoreCase);
                            record["AsciiLettersOnlyWithDigits"]=value.All(char.IsAsciiLetterOrDigit);
                        }
                        else
                        {
                            record["MatchesExpectedLength"]=length==(expectedPasswordLength??0);
                        }
                    }
                    controls.Add(record);
                    bool container=depth==0||cls.Contains("LayoutUI@",StringComparison.Ordinal)
                        ||cls is ".?AVCContainerUI@DuiLib@@" or ".?AVCListUI@DuiLib@@"
                            or ".?AVCListBodyUI@DuiLib@@" or ".?AVCListHeaderUI@DuiLib@@";
                    var children=LoginUiChildren.Read((at,length)=>p.MemRead(at,(uint)length,1),address,manager,0x760,0x40,container,128);
                    foreach(uint child in children)Visit(child,address,parentVisible&&visible,parentEnabled&&enabled,depth+1);
                    if(!LoginUiChildren.Read((at,length)=>p.MemRead(at,(uint)length,1),address,manager,0x760,0x40,container,128).SequenceEqual(children))
                        throw new InvalidOperationException("Passport UI children changed during traversal.");
                }
                Visit(root,0,true,true,0);managers.Add(new{Manager=$"0x{manager:X}",Root=$"0x{root:X}",Controls=controls});
            }
            results.Add(new{Window=window,Managers=managers});
        }
        return new{CapturedUtc=DateTimeOffset.UtcNow,Windows=results,ReadCredentialValues=verifyFake,CredentialValuesRetained=false};
    }
}
