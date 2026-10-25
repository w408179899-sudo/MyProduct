using System.Text.Json;
using Hardware.KmBox;
using Roadhog.Infrastructure.Vmm;
using Vmmsharp;

// Isolated live diagnostic: read-only target memory and explicit KMBox input.
namespace Roadhog.Infrastructure.Login.Native;

internal static class SessionInputProbe
{
    // VMM caches the session process module map on first use. Initializing it through
    // a user-only view can hide win32kbase from later foreground reads. Shared USER
    // tables and foreground symbols must consistently use the kernel-inclusive view.
    internal static Vmmsharp.VmmProcess SessionProcess(Vmmsharp.Vmm vmm,uint sessionPid) =>
        vmm.GetProcessByPID(sessionPid|0x80000000);

    public static JsonElement Foreground(Vmmsharp.Vmm vmm,uint sessionPid)
    {
        var kernel=SessionProcess(vmm,sessionPid);
        var module=kernel.MapModuleFromName("win32kbase.sys");
        if(module.vaBase==0||module.cbImageSize==0)
            throw new InvalidOperationException("Foreground session module is not initialized.");
        var pdb=kernel.Pdb(module.vaBase);
        if(!pdb.SymbolAddress("gptiForeground",out ulong symbol)||symbol<module.vaBase||symbol>=module.vaBase+module.cbImageSize)throw new InvalidOperationException("Foreground session symbol unavailable.");
        ulong ReadPointer(ulong address)
        {
            var a=kernel.MemRead(address,8,1);var b=kernel.MemRead(address,8,1);
            if(a is not{Length:8}||b is not{Length:8}||!a.AsSpan().SequenceEqual(b))throw new InvalidOperationException("Foreground pointer incomplete or changing.");
            return BitConverter.ToUInt64(a);
        }
        ulong pti=ReadPointer(symbol);
        if(pti<0xffff000000000000)throw new InvalidOperationException("Foreground thread unavailable.");
        ulong ethread=ReadPointer(pti);
        var matches=new List<object>();
        foreach(var process in vmm.Processes)
        {
            var info=process.GetInfo();if(!info.fValid||info.dwState!=0)continue;
            foreach(var thread in process.MapThread())
                if(thread.vaETHREAD==ethread&&thread.dwPID==info.dwPID&&thread.ftExitTime==0)
                    matches.Add(new{Pid=info.dwPID,Name=info.sName,Tid=thread.dwTID,Eprocess=$"0x{info.vaEPROCESS:X}",Ethread=$"0x{ethread:X}",Win32Thread=$"0x{pti:X}"});
        }
        if(matches.Count!=1||ReadPointer(symbol)!=pti||ReadPointer(pti)!=ethread)throw new InvalidOperationException("Foreground owner could not be uniquely revalidated.");
        return JsonSerializer.SerializeToElement(matches.Single());
    }

}
