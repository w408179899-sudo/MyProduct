using System.Text;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

internal static class WindowInventory
{
    internal sealed record WindowThread(uint Id, ulong Teb);

    public static object Run(VmmProcess p)
    {
        var threads=p.MapThread().Where(t=>t.dwPID==p.PID&&t.ftExitTime==0)
            .Select(t=>new WindowThread(t.dwTID,t.vaTeb)).ToArray();
        return Capture((address,length)=>p.MemRead(address,(uint)length,1),
            p.GetProcAddress("user32.dll","gSharedInfo"),threads,
            p.MapModule(false).Any(m=>m.sText.Equals("ui.dll",StringComparison.OrdinalIgnoreCase)));
    }

    internal static object Capture(Func<ulong,int,byte[]?> read,ulong sharedAddress,
        IReadOnlyList<WindowThread> threadList,bool inspectDuiManager)
    {
        byte[] Read(ulong a,int n){var b=read(a,n);return b is not null&&b.Length==n?b:throw new InvalidOperationException($"Incomplete window inventory read {a:X}, length {n}");}
        if(threadList.Count==0)throw new InvalidOperationException("Window owner thread inventory is not initialized.");
        var shared=Read(sharedAddress,24);
        ulong server=BitConverter.ToUInt64(shared,0), table=BitConverter.ToUInt64(shared,8);
        int stride=BitConverter.ToInt32(shared,16),count=BitConverter.ToInt32(Read(server,16),8);
        if(stride!=32||count<=0||count>65536)throw new InvalidOperationException($"Invalid handle table geometry {stride}/{count}");
        var threads=threadList.ToDictionary(t=>(ulong)t.Id);
        // Desktop heap mappings are shared by threads of this process. An inactive thread's
        // TEB may be unreadable while its windows remain valid. Index independently validated
        // mappings by the desktop identity from the handle table, never by a guessed address.
        var desktopMappings=new Dictionary<ulong,HashSet<ulong>>();
        foreach(var thread in threadList)
        {
            if(thread.Teb<0x10000)continue;
            var ci=read(thread.Teb+0x800,0x50);
            if(ci is not{Length:0x50})continue;
            ulong desktop=BitConverter.ToUInt64(ci,0x20),mapping=BitConverter.ToUInt64(ci,0x28);
            if(desktop<0x10000||desktop>=0x800000000000||mapping<0x10000||mapping>=0x800000000000)continue;
            var identity=read(desktop,8);var verify=read(thread.Teb+0x800,0x50);
            if(identity is not{Length:8}||verify is not{Length:0x50}||!ci.AsSpan(0x20,16).SequenceEqual(verify.AsSpan(0x20,16)))continue;
            ulong key=BitConverter.ToUInt64(identity);
            if(!desktopMappings.TryGetValue(key,out var mappings))desktopMappings[key]=mappings=[];
            mappings.Add(mapping);
        }
        var windows=new List<object>();
        for(int first=0;first<count;first+=128)
        {
            int entries=Math.Min(128,count-first);var page=Read(table+(ulong)(first*stride),entries*stride);
            for(int index=0;index<entries;index++)
            {
                var e=page.AsSpan(index*stride,stride);ulong owner=BitConverter.ToUInt64(e[8..]);
                if(e[24]!=1||(e[25]&1)!=0||!threads.ContainsKey(owner))continue;
                uint hwnd=(uint)(first+index)|((uint)BitConverter.ToUInt16(e[26..])<<16);
                ulong desktopId=BitConverter.ToUInt64(e[16..]);
                if(!desktopMappings.TryGetValue(desktopId,out var mappings)||mappings.Count!=1)
                    throw new InvalidOperationException($"No unique verified desktop mapping for window {hwnd:X}.");
                ulong mapping=mappings.Single();
                ulong address=mapping+BitConverter.ToUInt64(e);
                var w=Read(address,0x130);if(BitConverter.ToUInt64(w)!=hwnd)continue;
                uint style=BitConverter.ToUInt32(w,0x1c);ulong user=BitConverter.ToUInt64(w,0xd8);
                ulong manager=0,root=0;
                if(inspectDuiManager&&user>=0x10000&&user<0x80000000)
                {
                    var obj=read(user,0x240);
                    if(obj is{Length:0x240})for(int offset=0;offset<0x180;offset+=4)
                    {
                        if(BitConverter.ToUInt32(obj,offset+0xc)!=hwnd)continue;
                        uint candidateRoot=BitConverter.ToUInt32(obj,offset+0x60);
                        if(candidateRoot<0x10000||candidateRoot>=0x80000000)continue;
                        var rootFields=read(candidateRoot+0x14c,8);
                        if(rootFields is{Length:8}&&BitConverter.ToUInt32(rootFields)==user+(ulong)offset&&BitConverter.ToUInt32(rootFields,4)==0){manager=user+(ulong)offset;root=candidateRoot;break;}
                    }
                }
                string? title=null;int titleLength=BitConverter.ToUInt16(w,0xb8);ulong titlePtr=BitConverter.ToUInt64(w,0xc0);
                if((style&0x40000000)==0&&titleLength>0&&titleLength<=240&&titlePtr>=0x10000){var t=read(mapping+titlePtr,titleLength);if(t?.Length==titleLength)title=Encoding.Unicode.GetString(t);}
                windows.Add(new{Hwnd=$"0x{hwnd:X}",OwnerThread=owner,Address=$"0x{address:X}",Style=$"0x{style:X}",Visible=(style&0x10000000)!=0,Disabled=(style&0x08000000)!=0,Rect=new[]{BitConverter.ToInt32(w,0x58),BitConverter.ToInt32(w,0x5c),BitConverter.ToInt32(w,0x60),BitConverter.ToInt32(w,0x64)},ClientRect=new[]{BitConverter.ToInt32(w,0x68),BitConverter.ToInt32(w,0x6c),BitConverter.ToInt32(w,0x70),BitConverter.ToInt32(w,0x74)},UserData=$"0x{user:X}",Manager=$"0x{manager:X}",Root=$"0x{root:X}",TitleCandidate=title});
            }
        }
        return new{Count=count,Windows=windows};
    }
}
