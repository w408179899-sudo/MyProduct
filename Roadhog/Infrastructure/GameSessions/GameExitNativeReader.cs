using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;
using Vmmsharp;

namespace Roadhog.Infrastructure.GameSessions;

internal static class GameExitNativeReader
{
    internal static GameExitObservation Capture(Vmmsharp.Vmm vmm, AccountConfig account, Action<string> lifecycle)
    {
        // Complete process enumeration is independent of the old player/UI reads. Teardown
        // must remain observable after Game.dll or the local player has disappeared.
        var inventory = LoginNativeReader.Inventory(vmm);
        var games = inventory.Where(p => p.Info.sName.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase)).ToArray();
        static string Identity(IEnumerable<(VmmProcess Process, VmmProcess.ProcessInfo Info)> values) =>
            string.Join(";", values.Where(p => p.Info.sName.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Info.dwPID).Select(p => $"{p.Info.dwPID}:{p.Info.vaEPROCESS:X}:{p.Info.vaPEB:X}:{p.Info.fWow64}"));
        var identity = Identity(inventory);
        var session = account.InstanceId + ":" + account.VmmDeviceName + ":"
            + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vmm) + ":" + identity;
        if (games.Length == 0)
        {
            lifecycle(session);
            if (Identity(LoginNativeReader.Inventory(vmm)) != identity)
                throw new InvalidOperationException("Game process changed during exit confirmation.");
            return new(new(session, 0, false, false, false, false, false, 0, false),
                0, "", Rectangle.Empty, new(0, 0, false, false, false, 0, 0, null, null));
        }
        if (games.Length != 1 || games[0].Info.fWow64)
        {
            lifecycle(session);
            throw new InvalidOperationException("退出目标不是唯一的 64 位 Aion 游戏进程。");
        }
        var game = games[0];
        var module = game.Process.MapModuleFromName("Game.dll");
        session += $":{module.vaBase:X}:{module.cbImageSize}";
        lifecycle(session);
        if (module.vaBase == 0) throw new InvalidOperationException("等待游戏模块完成退出。");
        var ui = new InventoryInteractionDecoder((address, count) => LoginNativeReader.Exact(game.Process, address, count))
            .ReadGameExit(module.vaBase);
        var player = AionVmmGameApi.ReadLoginPlayer(game.Process, module.vaBase);
        if (string.IsNullOrWhiteSpace(account.CharacterName) || player.EntityId == 0
            || !string.Equals(player.CharacterName, account.CharacterName, StringComparison.Ordinal)
            || !player.HasReliableHealth)
            throw new InvalidOperationException("退出前尚未确认配置角色与完整生命值。");
        var sessionPid = inventory.Single(p => p.Info.sName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase)).Info.dwPID;
        var windows = LoginNativeReader.Windows(game.Process, SessionInputProbe.SessionProcess(vmm, sessionPid));
        var window = windows.Single(w => LoginNativeReader.Client(w).Size == new Size(ui.Width, ui.Height));
        var bounds = LoginNativeReader.Client(window);
        var verifyModule = game.Process.MapModuleFromName("Game.dll");
        if (Identity(LoginNativeReader.Inventory(vmm)) != identity || verifyModule.vaBase != module.vaBase
            || verifyModule.cbImageSize != module.cbImageSize)
            throw new InvalidOperationException("Game process changed during exit capture.");
        return new(new(session, checked((int)game.Info.dwPID), true, ui.MenuOpen, ui.CountdownOpen,
            ui.Quit != null, ui.Cancel != null, player.CurrentHp, player.IsDead), sessionPid,
            LoginNativeReader.Text(window, "Hwnd"), bounds, ui);
    }
}
