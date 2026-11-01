using System.Text.Json;
using Roadhog.Core.Accounts;
using static Roadhog.Infrastructure.Login.Native.LoginNativeReader;

namespace Roadhog.Infrastructure.Login.Native;

internal static class UuDesktopReader
{
    internal static UuDesktopContext? Capture(Vmmsharp.Vmm vmm, AccountConfig account)
    {
        var session = SessionKey(vmm, account);
        var processes = Inventory(vmm);
        if (UuLaunchFallbackWindow.HasStartedClient(processes.Select(p => p.Info.sName))) return null;
        var sessionPid = processes.Single(p => p.Info.sName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase)).Info.dwPID;
        // No UU windows, managers, child arrays or control metadata are read here.
        var foreground = SessionInputProbe.Foreground(vmm, sessionPid);
        var context = Project(session, sessionPid, processes.Select(p => p.Info.sName), foreground);
        if (session != SessionKey(vmm, account))
            throw new InvalidOperationException("Login desktop session changed during capture.");
        return context;
    }

    internal static UuDesktopContext? Project(string session, uint sessionPid,
        IEnumerable<string> processNames, JsonElement foreground)
    {
        if (UuLaunchFallbackWindow.HasStartedClient(processNames)) return null;
        var name = Text(foreground, "Name");
        if (string.IsNullOrWhiteSpace(session) || sessionPid == 0 || name.Length == 0
            || foreground.GetProperty("Pid").GetUInt32() == 0 || foreground.GetProperty("Tid").GetUInt32() == 0)
            throw new InvalidOperationException("Login desktop foreground is incomplete.");
        return new(session, sessionPid, name);
    }
}
