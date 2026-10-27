using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class UuReconnectTests
{
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action action,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message);}
    private static JsonNode Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"uu-reconnect-40002.json")))!;
    private static JsonElement Json(JsonNode node)=>JsonSerializer.SerializeToElement(node);
    private static JsonNode Control(JsonNode fixture,string name)=>fixture["Ui"]!["Controls"]!.AsArray().Single(c=>c!["Name"]!.GetValue<string>()==name)!;
    private static LoginObservation? Project(JsonNode f)=>LoginNativeReader.ProjectUuReconnect(Json(f["Ui"]!),Json(f["Window"]!),"test",1,2);

    internal static Task ArchivedPopupAndGuardsAsync()
    {
        var fixture=Fixture();var result=Project(fixture)!;
        Require(result.Snapshot.Action==LoginAction.Reaccelerate&&result.Point==new Point(444,449),"The live 40002 popup targets reconnect, not feedback.");
        fixture["Window"]!["ClientRect"]=JsonSerializer.SerializeToNode(new[]{10,20,370,436});
        Require(Project(fixture)!.Point==new Point(122,313),"Coordinates follow the live window, not archived screen coordinates.");
        Control(fixture,"error_code")["Name"]="error_code_desc2";
        Control(fixture,"Ok")["Text"]="重新加速";Control(fixture,"Cancel")["Text"]="快速反馈";
        Require(Project(fixture)!.Snapshot.Action==LoginAction.Reaccelerate,"The second resource layout and localized labels are supported.");
        foreach(string missing in new[]{"Enabled","Visible","AncestorsVisible"})
        {
            var changed=Fixture();Control(changed,"Ok")[missing]=false;
            Require(Project(changed)!.Snapshot.Action==LoginAction.None,"An unavailable reconnect button cannot receive input: "+missing);
        }
        foreach(string wrong in new[]{"停止","确定",""})
        {
            var changed=Fixture();Control(changed,"Ok")["Text"]=wrong;
            Require(Project(changed)!.Snapshot.Action==LoginAction.None,"A generic Ok or stop button is not reconnect.");
        }
        var empty=Fixture();Control(empty,"error_code_desc")["Text"]="";
        Require(Project(empty)!.Snapshot.Action==LoginAction.None,"A partially populated popup waits.");
        var unrelated=Fixture();Control(unrelated,"error_code_desc")["Name"]="tip_desc";
        Require(Project(unrelated) is null,"The stop-confirmation family is not reconnect.");
        Control(unrelated,"Ok")["Name"]="btn_reaccel";
        Require(Project(unrelated) is null,"Steam binding reaccelerate is not the disconnect popup.");
        var invalid=Fixture();Control(invalid,"Ok")["Rect"]!["Right"]=52;
        Reject(()=>Project(invalid),"Unlaid-out target cannot publish coordinates.");
        invalid=Fixture();Control(invalid,"Ok")["Rect"]!["Left"]=-500;Control(invalid,"Ok")["Rect"]!["Right"]=-400;
        Reject(()=>Project(invalid),"Out-of-window target cannot publish coordinates.");
        return Task.CompletedTask;
    }

    internal static Task PopupWindowPriorityAsync()
    {
        var fixture=Fixture();var popup=Json(fixture["Window"]!);var main=fixture["Window"]!.DeepClone();
        main["Hwnd"]="0x123";main["Disabled"]=true;main["ClientRect"]=JsonSerializer.SerializeToNode(new[]{-32000,-32000,-31840,-31972});
        int reads=0;
        JsonElement Read(JsonElement w){reads++;Require(w.GetProperty("Hwnd").GetString()==popup.GetProperty("Hwnd").GetString(),"Do not traverse the disabled/minimized main tree.");return Json(fixture["Ui"]!);}
        var result=LoginNativeReader.ProjectUuReconnectWindows([Json(main),popup],Read,"test",1,2);
        Require(result!.Snapshot.Action==LoginAction.Reaccelerate&&reads==1,"An independent popup takes priority over restoring the main window.");
        Reject(()=>LoginNativeReader.ProjectUuReconnectWindows([popup,popup],Read,"test",1,2),"Multiple matching dialogs cannot choose an arbitrary target.");
        Reject(()=>LoginNativeReader.ProjectUuReconnectWindows([popup],_=>throw new InvalidOperationException("partial tree"),"test",1,2),"Read failure cannot become absence and fall through to desktop input.");
        fixture["Window"]!["Disabled"]=true;
        Require(LoginNativeReader.ProjectUuReconnectWindows([Json(fixture["Window"]!)],Read,"test",1,2) is null,"Disabled popup does not authorize input.");
        return Task.CompletedTask;
    }

    internal static Task VerifiedTypesWithoutRttiAsync()
    {
        var assembly=typeof(UuControlTypes).Assembly;
        foreach(string name in new[]{"ui","uu"})
        {
            using var stream=assembly.GetManifestResourceStream($"Roadhog.Infrastructure.Login.Native.UuTypes.{name}-type-catalog.json")!;
            var catalog=JsonSerializer.Deserialize<UuControlTypes.Catalog>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            const uint moduleBase=0x20000000;
            var data=new Dictionary<ulong,byte[]>();
            var dos=new byte[0x40];BitConverter.GetBytes((ushort)0x5a4d).CopyTo(dos,0);BitConverter.GetBytes(0x80).CopyTo(dos,0x3c);data[moduleBase]=dos;
            var pe=new byte[0x80];BitConverter.GetBytes(0x4550u).CopyTo(pe,0);BitConverter.GetBytes(catalog.Machine).CopyTo(pe,4);
            BitConverter.GetBytes(catalog.TimeDateStamp).CopyTo(pe,8);BitConverter.GetBytes(catalog.OptionalHeaderMagic).CopyTo(pe,0x18);
            BitConverter.GetBytes(catalog.SizeOfImage).CopyTo(pe,0x50);data[moduleBase+0x80]=pe;
            byte[]? Read(ulong at,int size)=>data.TryGetValue(at,out var value)&&value.Length==size?value.ToArray():null;
            UuControlTypes Types()=>new(Read,[new(catalog.ModuleName,moduleBase,catalog.SizeOfImage)]);
            foreach(var type in catalog.Types)
            {
                uint address=moduleBase+type.VtableRva;
                var slots=new byte[36];BitConverter.GetBytes(moduleBase+type.ColRva).CopyTo(slots,0);
                for(int i=0;i<8;i++)BitConverter.GetBytes(moduleBase+type.First8FunctionRvas[i]).CopyTo(slots,4+i*4);
                data[address-4]=slots;
                Require(Types().TryGet(address,out var actual)&&actual.DecoratedName==type.DecoratedName&&actual.IsContainer==type.IsContainer,
                    "Every archived type must authenticate without reading nonresident RTTI: "+type.DecoratedName);
                for(int slot=0;slot<9;slot++)
                {
                    slots[slot*4]^=1;Reject(()=>Types().TryGet(address,out _),"Every COL/function slot must be validated.");
                    bool alternateAttempted=false;
                    var methods=new UuMethodTypes((_,_)=>{alternateAttempted=true;return null;});
                    Reject(()=>new UuUiReader(Read,Types(),methods).GetTypeName(address),"A contradictory recorded identity must not switch to another proof.");
                    Require(!alternateAttempted,"Only missing metadata may continue to alternate type proof.");
                    slots[slot*4]^=1;
                }
                data.Remove(address-4);Reject(()=>Types().TryGet(address,out _),"Short or absent live identity is not a leaf type.");data[address-4]=slots;
            }
            Require(!Types().TryGet(moduleBase+0x1000,out _),"Unknown vtables retain native RTTI handling.");
            pe[8]^=1;
            Require(!Types().TryGet(moduleBase+catalog.Types[0].VtableRva,out _),"Different builds cannot reuse this type catalog.");
            data.Remove(moduleBase);Reject(()=>Types().TryGet(moduleBase+catalog.Types[0].VtableRva,out _),"Unreadable PE identity cannot authorize static types.");
        }
        return Task.CompletedTask;
    }

    internal static Task LiveMethodReplayAndFaultsAsync()
    {
        var samples=JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"uu-control-methods.json")))!.AsArray();
        var memory=new Dictionary<uint,byte[]>();
        static uint Address(string text)=>Convert.ToUInt32(text[2..],16);
        void String(uint at,string value)
        {var bytes=new byte[256];System.Text.Encoding.Unicode.GetBytes(value+"\0").CopyTo(bytes,0);memory[at]=bytes;}
        void Method(uint at,byte[] bytes)
        {
            memory[at]=bytes;
            if(bytes[3]==0x51&&bytes[32]==0xeb&&bytes[33]==2)String(BitConverter.ToUInt32(bytes,8),"Control");
        }
        foreach(var sample in samples)
        {
            uint vtable=Address(sample!["Vtable"]!.GetValue<string>()),getClass=Address(sample["GetClass"]!.GetValue<string>()),getInterface=Address(sample["GetInterface"]!.GetValue<string>());
            var classCode=Convert.FromHexString(sample["GetClassCode"]!.GetValue<string>());
            var interfaceCode=Convert.FromHexString(sample["GetInterfaceCode"]!.GetValue<string>());
            memory[vtable+20]=[..BitConverter.GetBytes(getClass),..BitConverter.GetBytes(getInterface)];memory[getClass]=classCode;Method(getInterface,interfaceCode);
            String(BitConverter.ToUInt32(classCode,8),sample["ClassName"]!.GetValue<string>());
            foreach(var evidence in sample["Evidence"]!.AsArray())
            {
                switch(evidence!["Kind"]!.GetValue<string>())
                {
                    case "literal":String(BitConverter.ToUInt32(interfaceCode,evidence["Offset"]!.GetValue<int>()),evidence["Value"]!.GetValue<string>());break;
                    case "parent":Method(Address(evidence["Address"]!.GetValue<string>()),Convert.FromHexString(evidence["Code"]!.GetValue<string>()));break;
                    case "compare":
                        uint compare=Address(evidence["Pointer"]!.GetValue<string>());
                        memory[BitConverter.ToUInt32(interfaceCode,interfaceCode[3]==0x83?20:18)]=BitConverter.GetBytes(compare);
                        memory[compare]=Convert.FromHexString(evidence["Code"]!.GetValue<string>());break;
                }
            }
        }
        uint? failed=null;
        byte[]? Read(ulong at,int size)
        {
            if(at==failed)return null;
            if(at<=uint.MaxValue&&memory.TryGetValue((uint)at,out var exact)&&exact.Length>=size)return exact.AsSpan(0,size).ToArray();
            foreach(var pair in memory)if(at>=pair.Key&&at+(uint)size<=(ulong)pair.Key+(uint)pair.Value.Length)
                return pair.Value.AsSpan((int)(at-pair.Key),size).ToArray();
            return null;
        }
        foreach(var sample in samples)
        {
            string expected=sample!["ClassName"]!.GetValue<string>();uint vtable=Address(sample["Vtable"]!.GetValue<string>());
            var methods=new UuMethodTypes(Read);
            Require(methods.TryGet(vtable,out string name,out bool container)&&name==expected
                &&container==(expected is "ContainerUI" or "VerticalLayoutUI" or "HorizontalLayoutUI"),"Actual old-build methods prove the full type chain: "+expected);
            var missingHeaderTypes=new UuControlTypes(Read,[new("ui.dll",0x10000,0x7ffe0000)]);
            Require(new UuUiReader(Read,missingHeaderTypes,new UuMethodTypes(Read)).GetTypeName(vtable)==expected,
                "Missing catalog header must allow the independent complete live-method proof: "+expected);
            failed=vtable+20;Reject(()=>new UuMethodTypes(Read).TryGet(vtable,out _,out _),"Short method slots cannot classify a control.");failed=null;
            uint getClass=Address(sample["GetClass"]!.GetValue<string>());var code=memory[getClass];uint namePointer=BitConverter.ToUInt32(code,8);
            failed=namePointer;Reject(()=>new UuMethodTypes(Read).TryGet(vtable,out _,out _),"Missing string is not a successful type proof.");failed=null;
            BitConverter.GetBytes(0x10fffu).CopyTo(code,8);Reject(()=>new UuMethodTypes(Read).TryGet(vtable,out _,out _),"Odd UTF16 pointer fails as retryable input, not an index error.");
            BitConverter.GetBytes(namePointer).CopyTo(code,8);
            code[0]^=1;Require(!new UuMethodTypes(Read).TryGet(vtable,out _,out _),"Unknown method bodies retain native RTTI handling.");code[0]^=1;
        }
        var first=samples[0]!;uint root=Address(first["Vtable"]!.GetValue<string>()),iface=Address(first["GetInterface"]!.GetValue<string>());
        var rootCode=memory[iface];var saved=rootCode.ToArray();
        BitConverter.GetBytes(-104).CopyTo(rootCode,100);Reject(()=>new UuMethodTypes(Read).TryGet(root,out _,out _),"Parent-chain cycles cannot classify containers.");memory[iface]=saved;
        uint iat=BitConverter.ToUInt32(saved,20),target=BitConverter.ToUInt32(memory[iat]);memory[target][0]^=1;
        Require(!new UuMethodTypes(Read).TryGet(root,out _,out _),"An unverified comparison routine cannot certify interface names.");
        return Task.CompletedTask;
    }

    internal static Task ExportNameOrdinalMappingAsync()
    {
        const uint module=0x10000000;var image=new byte[4096];
        void U32(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(image,at);
        void U16(int at,ushort value)=>BitConverter.GetBytes(value).CopyTo(image,at);
        U16(0,0x5a4d);U32(0x3c,0x80);U32(0x80,0x4550);U16(0x98,0x10b);U32(0xf8,0x200);U32(0xfc,0x100);
        U32(0x214,3);U32(0x218,3);U32(0x21c,0x300);U32(0x220,0x400);U32(0x224,0x500);
        U32(0x300,0x800);U32(0x304,0x900);U32(0x308,0xa00);
        U16(0x500,2);U16(0x502,0);U16(0x504,1);
        var names=new[]{"_wcsicmp","dummy","wcscmp"};
        for(int i=0;i<names.Length;i++){U32(0x400+4*i,(uint)(0x600+i*64));System.Text.Encoding.ASCII.GetBytes(names[i]+"\0").CopyTo(image,0x600+i*64);}
        ulong? failed=null;
        byte[]? Read(ulong at,int size)=>at==failed?null:image.AsSpan(checked((int)(at-module)),size).ToArray();
        uint Find(string name)=>LoginPeExports.Find(Read,module,4096,name);
        Require(Find("wcscmp")==module+0x900&&Find("_wcsicmp")==module+0xa00,"Name indices must map through the ordinal table, not directly into function addresses.");
        Require(Find("missing")==0,"Complete absence of an export is supported.");
        failed=module+0x504;Reject(()=>Find("wcscmp"),"An unreadable ordinal cannot authorize a comparison function.");failed=null;
        U16(0x504,9);Reject(()=>Find("wcscmp"),"Out-of-range export ordinals are rejected.");U16(0x504,1);
        U32(0x304,0x220);Require(Find("wcscmp")==0,"A forwarded export is not an executable comparison routine.");
        return Task.CompletedTask;
    }
}
