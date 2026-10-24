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
}
