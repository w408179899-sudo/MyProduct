using System.Text.Json;
using Roadhog.Infrastructure.Login.Native;

internal static class LoginWindowInventoryTests
{
    private sealed class Memory
    {
        internal readonly Dictionary<ulong,byte[]> Data=[];
        internal readonly WindowInventory.WindowThread[] Threads=[new(10,0x300000),new(20,0x301000)];
        internal Memory()
        {
            var shared=new byte[24];U64(shared,0,0x110000);U64(shared,8,0x200000);U32(shared,16,32);Data[0x100000]=shared;
            var server=new byte[16];U32(server,8,2);Data[0x110000]=server;
            var ci=new byte[0x50];U64(ci,0x20,0x310000);U64(ci,0x28,0x400000);Data[0x300800]=ci;
            Data[0x310000]=BitConverter.GetBytes(0x123456789ABCUL);
            // The second window's owning TEB is deliberately unreadable.
            var entries=new byte[64];
            for(int i=0;i<2;i++)
            {
                int offset=i*32;U64(entries,offset,(ulong)(0x1000+i*0x1000));U64(entries,offset+8,(ulong)(10+i*10));
                U64(entries,offset+16,0x123456789ABC);entries[offset+24]=1;entries[offset+26]=1;
                var window=new byte[0x130];U64(window,0,(ulong)(0x10000+i));U32(window,0x1c,0x10000000);
                U32(window,0x68,100);U32(window,0x6c,200);U32(window,0x70,1100);U32(window,0x74,888);
                Data[(ulong)(0x401000+i*0x1000)]=window;
            }
            Data[0x200000]=entries;
        }
        internal byte[]? Read(ulong address,int length)=>Data.TryGetValue(address,out var data)&&data.Length==length?data.ToArray():null;
        internal JsonElement Capture()=>JsonSerializer.SerializeToElement(WindowInventory.Capture(Read,0x100000,Threads,false));
    }
    private static void U64(byte[] bytes,int offset,ulong value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
    private static void U32(byte[] bytes,int offset,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action action,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message);}

    internal static Task SharedDesktopMappingAsync()
    {
        var memory=new Memory();var windows=memory.Capture().GetProperty("Windows");
        Require(windows.GetArrayLength()==2,"An unreadable owning TEB cannot hide a window when its exact desktop mapping is validated elsewhere.");
        Require(windows[1].GetProperty("Hwnd").GetString()=="0x10001"&&windows[1].GetProperty("OwnerThread").GetUInt32()==20,"Window identity remains bound to its owning handle.");
        Require(windows[1].GetProperty("ClientRect")[2].GetInt32()==1100,"Coordinates still come from the actual window.");
        return Task.CompletedTask;
    }
    internal static Task MissingAndForeignMappingAsync()
    {
        var memory=new Memory();memory.Data.Remove(0x300800);
        Reject(()=>memory.Capture(),"No mapping must stay a provider failure, not an empty window list.");
        memory=new Memory();U64(memory.Data[0x200000],32+16,0x999999);
        Reject(()=>memory.Capture(),"Mappings cannot be borrowed from another desktop identity.");
        memory=new Memory();memory.Data.Remove(0x402000);
        Reject(()=>memory.Capture(),"Unreadable window fields cannot be silently skipped.");
        memory=new Memory();U64(memory.Data[0x402000],0,0x99999);
        Reject(()=>memory.Capture(),"A replaced window cannot be silently removed from the scene.");
        memory=new Memory();U64(memory.Data[0x401000],0xd8,0x500000);
        Reject(()=>WindowInventory.Capture(memory.Read,0x100000,memory.Threads,true),"A missing visible UU manager must not be published as a minimized window.");
        return Task.CompletedTask;
    }
    internal static Task ColdThreadsAndAmbiguityAsync()
    {
        var memory=new Memory();
        Reject(()=>WindowInventory.Capture(memory.Read,0x100000,[],false),"Cold native thread initialization must not publish absent windows.");
        var second=memory.Data[0x300800].ToArray();U64(second,0x28,0x500000);memory.Data[0x301800]=second;
        Reject(()=>memory.Capture(),"Conflicting mappings for one desktop cannot produce clickable coordinates.");
        memory=new Memory();memory.Data[0x200000]=new byte[64];
        Require(memory.Capture().GetProperty("Windows").GetArrayLength()==0,"A complete handle table without owned windows is a valid empty inventory.");
        return Task.CompletedTask;
    }

    internal static Task MissingSharedInfoAddressAsync()
    {
        var memory=new Memory();int reads=0;
        foreach(ulong address in new ulong[]{0,0xffff,0x800000000000})
            Reject(()=>WindowInventory.Capture((at,length)=>{reads++;return memory.Read(at,length);},address,memory.Threads,false),
                "An unavailable export cannot be read as an address or published as an empty window inventory.");
        Require(reads==0,"Missing/invalid exports must be rejected before any pointer dereference.");
        Require(memory.Capture().GetProperty("Windows").GetArrayLength()==2,"The next resolved address permits normal capture.");
        return Task.CompletedTask;
    }

    internal static Task SessionTableReadIsolationAsync()
    {
        var memory=new Memory();var tableAddresses=new HashSet<ulong>{0x100000,0x110000,0x200000};
        int sourceReads=0,targetReads=0;
        byte[]? Source(ulong at,int size)
        {
            Require(tableAddresses.Contains(at),"Window, TEB and desktop memory must not be read through winlogon.");
            sourceReads++;return memory.Read(at,size);
        }
        byte[]? Target(ulong at,int size)
        {
            Require(!tableAddresses.Contains(at),"The shared owner virtual addresses cannot be dereferenced through UU.");
            targetReads++;return memory.Read(at,size);
        }
        var windows=JsonSerializer.SerializeToElement(WindowInventory.Capture(Target,0x100000,memory.Threads,false,Source)).GetProperty("Windows");
        Require(windows.GetArrayLength()==2&&sourceReads>0&&targetReads>0,"A complete same-session table permits target-owned window capture.");
        Require(windows[1].GetProperty("Hwnd").GetString()=="0x10001"&&windows[1].GetProperty("ClientRect")[2].GetInt32()==1100,
            "Handle identity and coordinates must come from the same target window, including a target with one unreadable TEB.");
        return Task.CompletedTask;
    }

    internal static Task SharedOwnerSessionGuardsAsync()
    {
        var target=new Vmmsharp.VmmProcess.ProcessInfo{fValid=true,dwPID=3700,sName="uu.exe",fWow64=true,dwSessionId=1,vaEPROCESS=0xffff00001000,vaPEB=0x100000};
        var owner=new Vmmsharp.VmmProcess.ProcessInfo{fValid=true,dwPID=900,sName="winlogon.exe",dwSessionId=1,vaEPROCESS=0xffff00002000,vaPEB=0x200000};
        WindowInventory.ValidateSharedOwner(target,owner);
        foreach(var invalid in new[]{owner with{dwSessionId=2},owner with{dwState=1},owner with{fValid=false},owner with{fWow64=true},
            owner with{sName="explorer.exe"},owner with{vaEPROCESS=0},owner with{vaPEB=0},owner with{dwPID=0}})
            Reject(()=>WindowInventory.ValidateSharedOwner(target,invalid),"An unrelated or invalid source cannot lend its USER table.");
        Reject(()=>WindowInventory.ValidateSharedOwner(target with{fValid=false},owner),"A valid owner cannot authenticate an invalid target.");
        Reject(()=>WindowInventory.ValidateSharedOwner(target with{dwSessionId=2},owner),"A target in another Windows session cannot reuse the table.");
        return Task.CompletedTask;
    }

    internal static Task SharedTableMutationAndFaultsAsync()
    {
        var memory=new Memory();
        foreach(ulong missing in new ulong[]{0x100000,0x110000,0x200000})
            Reject(()=>WindowInventory.Capture(memory.Read,0x100000,memory.Threads,false,(at,size)=>at==missing?null:memory.Read(at,size)),
                "A partial source table cannot become an empty target window inventory.");
        U64(memory.Data[0x110000],8,0x100000002);
        Reject(()=>memory.Capture(),"The upper count bits are validated, not truncated to a plausible low count.");
        U64(memory.Data[0x110000],8,2);
        int headers=0;
        Reject(()=>WindowInventory.Capture(memory.Read,0x100000,memory.Threads,false,(at,size)=>
        {
            var bytes=memory.Read(at,size);
            if(at==0x100000&&++headers==2)bytes![0]^=1;
            return bytes;
        }),"A source shared-info replacement must invalidate capture before publication.");
        int servers=0;
        Reject(()=>WindowInventory.Capture(memory.Read,0x100000,memory.Threads,false,(at,size)=>
        {
            var bytes=memory.Read(at,size);
            if(at==0x110000&&++servers==2)bytes![8]^=1;
            return bytes;
        }),"A table size change must invalidate capture before publication.");
        return Task.CompletedTask;
    }

    internal static Task UuManagerCandidateScanAsync()
    {
        var memory=new Memory();
        U64(memory.Data[0x401000],0xd8,0x500000);
        var instance=new byte[0x240];memory.Data[0x500000]=instance;
        U32(instance,0xc,0x10000);U32(instance,0x60,0x10000); // HWND-shaped false root.
        U32(instance,0x4c+0xc,0x10000);U32(instance,0x4c+0x60,0x600000);
        memory.Data[0x60014c]=[..BitConverter.GetBytes(0x50004cu),..BitConverter.GetBytes(0u)];
        var windows=JsonSerializer.SerializeToElement(WindowInventory.Capture(memory.Read,0x100000,memory.Threads,true)).GetProperty("Windows");
        Require(windows[0].GetProperty("Manager").GetString()=="0x50004C","An earlier unreadable false root must not hide the later verified manager.");
        memory.Data.Remove(0x60014c);
        U32(memory.Data[0x401000],0x70,460); // A reconnect popup is narrower than the main window.
        Reject(()=>WindowInventory.Capture(memory.Read,0x100000,memory.Threads,true),"A small visible popup with no complete manager cannot be skipped as absent.");
        foreach(uint style in new uint[]{0x18000000,0x30000000})
        {
            U32(memory.Data[0x401000],0x1c,style);memory.Data.Remove(0x500000);
            Require(JsonSerializer.SerializeToElement(WindowInventory.Capture(memory.Read,0x100000,memory.Threads,true)).GetProperty("Windows").GetArrayLength()==2,
                "A confirmed disabled/minimized main window does not require a readable control tree and cannot block a separate popup.");
        }
        return Task.CompletedTask;
    }
}
