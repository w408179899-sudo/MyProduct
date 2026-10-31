using System.Text.Json;
using Roadhog.Application.Login;
using static Roadhog.Infrastructure.Login.Native.LoginNativeReader;

namespace Roadhog.Infrastructure.Login.Native;

internal static class UuLaunchFallbackWindow
{
    // Archived 100% layout: cycle-final-verify.json and 11-uu-ui-after-acc.json.
    // Client 1000x688; launch_game rect (40,328)-(150,364). Do not guess scaling.
    internal static readonly Size ClientSize = new(1000, 688);
    internal static readonly Point LaunchPoint = new(95, 346);

    internal static LoginObservation? Capture(Vmmsharp.Vmm vmm, string session)
    {
        var processes = Inventory(vmm);
        if (HasStartedClient(processes.Select(p => p.Info.sName))) return null;
        var sessionPid = processes.Single(p => p.Info.sName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase)).Info.dwPID;
        var foreground = SessionInputProbe.Foreground(vmm, sessionPid);
        var pid = foreground.GetProperty("Pid").GetUInt32();
        var candidates = processes.Where(p => p.Info.dwPID == pid && p.Info.sName.Equals("uu.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) return null;
        // No DuiLib manager, root, control type or child-tree dependency here.
        var windows = Json(WindowInventory.Run(candidates[0].Process,
            SessionInputProbe.SessionProcess(vmm, sessionPid), inspectDuiManager: false)).GetProperty("Windows");
        var after = SessionInputProbe.Foreground(vmm, sessionPid);
        if (foreground.GetRawText() != after.GetRawText()) return null;
        return Project(session, sessionPid, processes.Select(p => p.Info.sName), foreground, windows.EnumerateArray());
    }

    internal static bool HasStartedClient(IEnumerable<string> names) => names.Any(name =>
        name.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Launcher.dat", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("sdologin.exe", StringComparison.OrdinalIgnoreCase));

    internal static LoginObservation? Project(string session, uint sessionPid, IEnumerable<string> processNames,
        JsonElement foreground, IEnumerable<JsonElement> windows)
    {
        if (HasStartedClient(processNames) || !Text(foreground, "Name").Equals("uu.exe", StringComparison.OrdinalIgnoreCase)) return null;
        // Foreground is a verified process/thread. Require its sole visible top-level
        // window, rejecting popups even when they share the main window's thread.
        var visible = windows.Where(w => Flag(w, "Visible") && (Address(Text(w, "Style")) & 0x60000000) == 0).ToArray();
        if (visible.Length != 1) return null;
        var window = visible[0];
        if (Flag(window, "Disabled") || window.GetProperty("OwnerThread").GetUInt64() != foreground.GetProperty("Tid").GetUInt32()) return null;
        var bounds = Client(window);
        if (bounds.Size != ClientSize || bounds.Left < 0 || bounds.Top < 0 || bounds.Right > 4096 || bounds.Bottom > 2160) return null;
        var pid = foreground.GetProperty("Pid").GetUInt32();
        var hwnd = Text(window, "Hwnd");
        if (pid == 0 || Address(hwnd) == 0) return null;
        var point = new Point(bounds.Left + LaunchPoint.X, bounds.Top + LaunchPoint.Y);
        return new(new(session, LoginStage.AcceleratorReady, LoginAction.LaunchGame,
            UuLaunchFallback.Identity + hwnd, "UU 页面读取持续失败，尝试固定位置启动游戏"), pid, sessionPid, hwnd, point, bounds);
    }
}
