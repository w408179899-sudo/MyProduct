using System.Text.Json;
using Roadhog.Application.Login;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Vmm;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

// Raw types stay in the adapter. The application receives only LoginSnapshot.
internal sealed record LoginObservation(LoginSnapshot Snapshot, uint Pid, uint SessionPid,
    string Window, Point Point, Rectangle Bounds, JsonElement Ui = default);

internal static class LoginNativeReader
{
    internal static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    internal static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    internal static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
    internal static ulong Address(string value) => Convert.ToUInt64(value[2..], 16);

    internal static (VmmProcess Process, VmmProcess.ProcessInfo Info)[] Inventory(Vmmsharp.Vmm vmm)
    {
        var all = vmm.Processes.Select(p => (Process: p, Info: p.GetInfo())).ToArray();
        if (all.Length < 5 || all.Any(p => !p.Info.fValid) || !all.Any(p => p.Info.dwPID == 4 && p.Info.dwState == 0))
            throw new InvalidOperationException("Process enumeration is incomplete.");
        return all.Where(p => p.Info.dwState == 0).ToArray();
    }

    internal static string SessionKey(Vmmsharp.Vmm vmm, AccountConfig account)
    {
        static bool Relevant(string name) => name.Equals("uu.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("sdologin.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Launcher.dat", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase);
        string Capture() => string.Join(";", Inventory(vmm).Where(p => Relevant(p.Info.sName)).OrderBy(p => p.Info.dwPID)
            .Select(p =>
            {
                var moduleName = p.Info.sName.Equals("Aion.bin",StringComparison.OrdinalIgnoreCase) ? "Game.dll"
                    : p.Info.sName.Equals("sdologin.exe",StringComparison.OrdinalIgnoreCase) ? "duilib.dll"
                    : p.Info.sName.Equals("uu.exe",StringComparison.OrdinalIgnoreCase) ? "ui.dll" : "";
                var module = moduleName.Length == 0 ? default : p.Process.MapModuleFromName(moduleName);
                return $"{p.Info.dwPID}:{p.Info.vaEPROCESS:X}:{p.Info.vaPEB:X}:{p.Info.fWow64}:{p.Info.sName}:{module.vaBase:X}:{module.cbImageSize}";
            }));
        var first = Capture();
        if (first != Capture()) throw new InvalidOperationException("Login process session changed during enumeration.");
        return account.InstanceId + ":" + account.VmmDeviceName + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vmm) + ":" + first;
    }

    internal static LoginObservation Capture(Vmmsharp.Vmm vmm, AccountConfig account, string session)
    {
        var processes = Inventory(vmm);
        var sessionPid = processes.Single(p => p.Info.sName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase)).Info.dwPID;
        var game = processes.Where(p => p.Info.sName.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (game.Length > 1) return State(LoginStage.Blocked, "目标电脑有多个 Aion 进程，无法唯一确定登录窗口。");
        if (game.Length == 1)
        {
            var process = game[0].Process;
            if (game[0].Info.fWow64) return State(LoginStage.Loading, "等待游戏启动器切换到 64 位游戏…");
            var module = process.MapModuleFromName("Game.dll");
            if (module.vaBase == 0) return State(LoginStage.Loading, "正在加载游戏模块…");
            var local = Exact(process, module.vaBase + 0xd6cb08, 2);
            int gameState = BitConverter.ToInt32(Exact(process,module.vaBase+0xd6ca90,4));
            uint mapId = BitConverter.ToUInt32(Exact(process,module.vaBase+0xd647c0+0x20dc,4));
            if (gameState == 15 && mapId is not 0 and not uint.MaxValue && BitConverter.ToUInt16(local) != 0
                && !HasVisibleGameMessage((address, size) => Exact(process, address, size), module.vaBase))
            {
                var player = AionVmmGameApi.ReadLoginPlayer(process, module.vaBase);
                if (player.EntityId != 0 && player.Position is not null && !string.IsNullOrWhiteSpace(player.CharacterName))
                    return new(new(session + ":" + module.vaBase, LoginStage.InWorld, LoginAction.None, "world", "角色已进入游戏：" + player.CharacterName,
                        player.CharacterName, checked((int)process.PID)), process.PID, sessionPid, "", Point.Empty, Rectangle.Empty);
            }
            var ui = Json(new GameLoginProbe(process).Capture());
            var windows = Windows(process);
            var viewport = ui.GetProperty("Viewport");
            var window = windows.Single(w => w.GetProperty("ClientRect")[2].GetInt32() - w.GetProperty("ClientRect")[0].GetInt32() == (int)viewport[0].GetDouble()
                && w.GetProperty("ClientRect")[3].GetInt32() - w.GetProperty("ClientRect")[1].GetInt32() == (int)viewport[1].GetDouble());
            return ProjectGame(ui, window, account.CharacterName, session + ":" + module.vaBase, sessionPid);
        }
        var sdo = processes.Where(p => p.Info.sName.Equals("sdologin.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sdo.Length > 1) return State(LoginStage.Blocked, "发现多个通行证登录进程，请先保留一个登录窗口。");
        if (sdo.Length == 1)
        {
            var ui = Json(SdoUiProbe.Run(sdo[0].Process));
            return ProjectSdo(ui, session, sdo[0].Info.dwPID, sessionPid);
        }
        var launchers = processes.Where(p => p.Info.sName.Equals("Launcher.dat", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (launchers.Length > 1) return State(LoginStage.Blocked, "发现多个游戏启动器，请先保留一个。");
        if (launchers.Length == 1)
        {
            var process = launchers[0].Process;
            var windows = Windows(process).Where(w => Text(w, "TitleCandidate") == "永恒之塔").ToArray();
            if (windows.Length == 0) return State(LoginStage.Loading, "等待游戏启动器页面…");
            var window = windows.Single(); var bounds = Client(window);
            if (bounds.Size != new Size(1280, 730)) return State(LoginStage.Blocked, "游戏启动器窗口尺寸与记录不符，请检查页面。");
            return Target(LoginStage.Launcher, LoginAction.StartLauncher, "点击开始游戏", session, process.PID, sessionPid,
                window, new(bounds.Left + 980, bounds.Top + 625), "launcher-start");
        }
        var uu = processes.Where(p => p.Info.sName.Equals("uu.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (uu.Length > 1) return State(LoginStage.Blocked, "发现多个 UU 主进程，请先保留一个。");
        if (uu.Length == 0) return OpenUu("打开 UU 加速器");
        if (SessionInputProbe.Foreground(vmm, sessionPid).GetProperty("Pid").GetUInt32() != uu[0].Info.dwPID)
            return OpenUu("将 UU 加速器置于前台");
        var uuWindows = Windows(uu[0].Process).Where(w => Text(w, "Manager") != "0x0" && Client(w).Width >= 800).ToArray();
        if (uuWindows.Length == 0) return OpenUu("打开 UU 主窗口");
        var uuWindow = uuWindows.Single();
        return ProjectUu(Json(new UuUiReader(uu[0].Process).Run(uuWindow)), uuWindow, session, uu[0].Info.dwPID, sessionPid);

        LoginObservation State(LoginStage stage, string description) => new(new(session, stage, LoginAction.None, stage.ToString(), description), 0, sessionPid, "", Point.Empty, Rectangle.Empty);
        LoginObservation OpenUu(string description) => new(new(session, LoginStage.Desktop, LoginAction.OpenAccelerator,
            "open-uu:" + SessionInputProbe.Foreground(vmm, sessionPid).GetRawText(), description), 0, sessionPid, "", new(35,35), Rectangle.Empty);
    }

    internal static JsonElement[] Windows(VmmProcess process) => Json(WindowInventory.Run(process)).GetProperty("Windows").EnumerateArray()
        .Where(w => Flag(w,"Visible") && !Flag(w,"Disabled") && (Address(Text(w,"Style")) & 0x40000000) == 0).ToArray();
    internal static Rectangle Client(JsonElement window)
    {
        var r = window.GetProperty("ClientRect");
        return Rectangle.FromLTRB(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32());
    }
    internal static byte[] Exact(VmmProcess process, ulong address, int count)
    {
        var a = process.MemRead(address, (uint)count, 1); var b = process.MemRead(address, (uint)count, 1);
        return a is not null && a.Length == count && b is not null && a.AsSpan().SequenceEqual(b)
            ? a : throw new InvalidOperationException("Incomplete or changing login memory field.");
    }

    // Exit/kick messages can coexist with the last world/player data. Resolve the message first.
    internal static bool HasVisibleGameMessage(Func<ulong, int, byte[]> read, ulong gameBase)
    {
        var slots = read(gameBase + 0xd63990 + 336 * 8, 35 * 8);
        if (slots.Length != 35 * 8) throw new InvalidOperationException("Incomplete game message table.");
        for (int i = 0; i < 35; i++)
        {
            ulong dialog = BitConverter.ToUInt64(slots, i * 8);
            if (dialog == 0) continue;
            if (dialog < 0x10000 || dialog >= 0x800000000000)
                throw new InvalidOperationException("Invalid game message pointer.");
            var flags = read(dialog + 0x28, 4);
            if (flags.Length != 4) throw new InvalidOperationException("Incomplete game message visibility.");
            if ((BitConverter.ToUInt32(flags) & 1) != 0) return true;
        }
        return false;
    }
    internal static LoginObservation Target(LoginStage stage, LoginAction action, string description, string session, uint pid,
        uint sessionPid, JsonElement window, Point point, string identity, JsonElement ui = default)
    {
        var bounds = Client(window);
        if (!bounds.Contains(point)) throw new InvalidOperationException("Login target is outside its client rectangle.");
        var hwnd = Text(window,"Hwnd");
        return new(new(session, stage, action, identity + ":" + hwnd + ":" + point, description), pid, sessionPid, hwnd, point, bounds, ui);
    }

    internal static LoginObservation ProjectGame(JsonElement ui, JsonElement window, string character, string session, uint sessionPid)
    {
        uint pid = ui.GetProperty("Pid").GetUInt32(); var windows = ui.GetProperty("Windows").EnumerateArray().ToArray();
        var chars = ui.GetProperty("Characters").EnumerateArray().Where(c => Text(c,"Name") == character).ToArray();
        int selected = ui.GetProperty("SelectedIndex").GetInt32();
        int gameState = ui.GetProperty("GameState").GetInt32();
        LoginObservation Block(string message) => new(new(session, LoginStage.Blocked, LoginAction.None, "blocked", message),pid,sessionPid,"",Point.Empty,Rectangle.Empty);
        LoginObservation Wait(string message) => new(new(session, LoginStage.Loading, LoginAction.None, "loading", message),pid,sessionPid,"",Point.Empty,Rectangle.Empty);
        LoginObservation Click(JsonElement w, JsonElement node, LoginStage stage, LoginAction action, string description)
        {
            if (!Flag(node,"Enabled")) throw new InvalidOperationException("Game login control is disabled.");
            var bounds = Client(window);
            var point = new Point(bounds.Left + (int)(node.GetProperty("X").GetDouble() + node.GetProperty("W").GetDouble()/2),
                bounds.Top + (int)(node.GetProperty("Y").GetDouble() + node.GetProperty("H").GetDouble()/2));
            return Target(stage,action,description,session,pid,sessionPid,window,point,
                action + ":" + Text(w,"Address") + ":" + Text(node,"Address") + ":" + selected,ui);
        }
        JsonElement Node(JsonElement w, string name) => w.GetProperty("Nodes").EnumerateArray().Single(n => Text(n,"Name") == name);
        var modals = windows.Where(w => w.GetProperty("Slot").GetInt32() >= 336).ToArray();
        if (modals.Length > 1) return Block("游戏有多个提示窗口，请先确认当前提示。");
        if (modals.Length == 1)
        {
            var w = modals[0]; var message = w.GetProperty("Message");
            int confirm = message.GetProperty("ConfirmEvent").GetInt32();
            if (message.GetProperty("Owner").GetInt32() == 4 && confirm == 2203)
            {
                if (chars.Length != 1 || selected != chars[0].GetProperty("Index").GetInt32()) return Block("进入游戏确认窗口的角色与配置不一致。");
                bool agreed = Flag(message,"Checked");
                var address = Text(message, agreed ? "Ok" : "Checkbox");
                var node = w.GetProperty("Nodes").EnumerateArray().Single(n => Text(n,"Address") == address);
                return Click(w,node,LoginStage.EntryAgreement,agreed ? LoginAction.ConfirmEntryAgreement : LoginAction.AcceptEntryAgreement,
                    agreed ? "确认进入游戏" : "勾选自由对战协议");
            }
            if (confirm == 2204 && message.GetProperty("Tag").GetInt32() == 24 && Text(message,"Body").Contains("限制创建",StringComparison.Ordinal))
                return Click(w,Node(w,"ok"),LoginStage.Agreement,LoginAction.AcceptAgreement,"确认种族创建限制提示");
            return Block("登录遇到未处理的游戏提示：" + Text(message,"Body"));
        }
        // Earlier root visibility bits persist. Later pages and modal identities take precedence.
        var characterWindow = windows.Where(w => w.GetProperty("Slot").GetInt32() == 4).ToArray();
        if (characterWindow.Length == 1 && gameState == 12)
        {
            if (ui.GetProperty("Characters").GetArrayLength() == 0) return Wait("等待角色列表…");
            if (chars.Length != 1) return Block("当前服务器未找到唯一的配置角色：" + character);
            int index = chars[0].GetProperty("Index").GetInt32(); var w = characterWindow[0];
            return selected == index
                ? Click(w,Node(w,"start_button"),LoginStage.CharacterSelection,LoginAction.EnterCharacter,"进入角色：" + character)
                : Click(w,Node(w,"char_sel_" + (index+1)),LoginStage.CharacterSelection,LoginAction.SelectCharacter,"选择角色：" + character);
        }
        var serverWindow = windows.Where(w => w.GetProperty("Slot").GetInt32() == 3).ToArray();
        if (serverWindow.Length == 1 && gameState == 10)
        {
            var servers = ui.GetProperty("Servers").EnumerateArray().ToArray();
            if (servers.Length == 0) return Wait("等待服务器列表…");
            if (servers.Length != 1 || !Flag(servers[0],"Online") || ui.GetProperty("SelectedServerIndex").GetInt32() != 0)
                return Block("无法唯一确认服务器，请先在游戏中选择要登录的服务器。");
            return Click(serverWindow[0],Node(serverWindow[0],"start_button"),LoginStage.ServerSelection,LoginAction.SelectServer,"进入服务器");
        }
        var agreements = windows.Where(w => w.GetProperty("Slot").GetInt32() == 1).ToArray();
        if (agreements.Length == 1 && gameState == 8) return Click(agreements[0],Node(agreements[0],"agreement_yes"),LoginStage.Agreement,LoginAction.AcceptAgreement,"同意游戏协议");
        return new(new(session,LoginStage.Loading,LoginAction.None,"loading","等待游戏加载…"),pid,sessionPid,"",Point.Empty,Rectangle.Empty);
    }

    internal static LoginObservation ProjectSdo(JsonElement ui, string session, uint pid, uint sessionPid)
    {
        var windows = ui.GetProperty("Windows").EnumerateArray().Where(w => !Flag(w.GetProperty("Window"),"Disabled")).ToArray();
        var choices = windows.Where(w => w.TryGetProperty("Managers",out var m) && m.GetArrayLength() == 1).ToArray();
        if (choices.Length != 1) throw new InvalidOperationException("Login dialog has not settled to one active manager.");
        var choice = choices[0]; var window = choice.GetProperty("Window");
        var controls = choice.GetProperty("Managers")[0].GetProperty("Controls").EnumerateArray().Where(c => Flag(c,"VisibleThroughRoot")).ToArray();
        JsonElement C(string name) => controls.Single(c => Text(c,"Name") == name);
        bool Has(string name) => controls.Any(c => Text(c,"Name") == name);
        LoginObservation Click(string name, LoginStage stage, LoginAction action, string description)
        {
            var c = C(name); if (!Flag(c,"EnabledThroughRoot")) throw new InvalidOperationException("Passport control is disabled.");
            var r = c.GetProperty("Rect"); var b = Client(window);
            return Target(stage,action,description,session,pid,sessionPid,window,
                new(b.Left+(r[0].GetInt32()+r[2].GetInt32())/2,b.Top+(r[1].GetInt32()+r[3].GetInt32())/2),action+":"+Text(c,"Address"),ui);
        }
        if (Has("HintText"))
        {
            string message = Text(C("HintText"),"Text");
            if (message.Contains("验证码错误",StringComparison.Ordinal)) return Click("OkBtn",LoginStage.CaptchaError,LoginAction.DismissCaptchaError,"验证码未通过，重新识别");
            var stage = message.Contains("帐号或者密码不正确",StringComparison.Ordinal) || message.Contains("账号或者密码不正确",StringComparison.Ordinal)
                ? LoginStage.CredentialsRejected : LoginStage.Blocked;
            return new(new(session,stage,LoginAction.None,"sdo-message",message),pid,sessionPid,Text(window,"Hwnd"),Point.Empty,Client(window),ui);
        }
        if (Has("IdentifyingCodeEdit") && Has("ChangeCodeBtn")) return Click("IdentifyingCodeEdit",LoginStage.Captcha,LoginAction.SolveCaptcha,"识别并提交验证码");
        if (!Flag(C("PwdLoginTab"),"Selected")) return Click("PwdLoginTab",LoginStage.PasswordLogin,LoginAction.SelectPasswordTab,"切换密码登录");
        if (!Flag(C("ProtocolCheckBox"),"Selected")) return Click("ProtocolCheckBox",LoginStage.PasswordLogin,LoginAction.AcceptLoginProtocol,"勾选通行证协议");
        _ = C("AccountCombox"); _ = C("PasswordEdit");
        return Click("LoginBtn",LoginStage.PasswordLogin,LoginAction.SubmitCredentials,"输入账号密码并登录");
    }

    internal static LoginObservation ProjectUu(JsonElement ui, JsonElement window, string session, uint pid, uint sessionPid)
    {
        var controls = ui.GetProperty("Controls").EnumerateArray().ToArray();
        bool Active(JsonElement c) => Flag(c,"Visible") && Flag(c,"AncestorsVisible") && Flag(c,"Enabled");
        JsonElement[] Named(string name) => controls.Where(c => Text(c,"Name") == name && Active(c)).ToArray();
        LoginObservation Click(JsonElement c, LoginStage stage, LoginAction action, string description)
        {
            var r = c.GetProperty("Rect"); var b = Client(window);
            return Target(stage,action,description,session,pid,sessionPid,window,
                new(b.Left+(r.GetProperty("Left").GetInt32()+r.GetProperty("Right").GetInt32())/2,
                    b.Top+(r.GetProperty("Top").GetInt32()+r.GetProperty("Bottom").GetInt32())/2),action+":"+Text(c,"Address"),ui);
        }
        var launch = Named("launch_game"); var stop = Named("stop_acc");
        if (launch.Length == 1 && stop.Length == 1 && Named("game_name").Any(c => Text(c,"Text") == "永恒之塔"))
            return Click(launch[0],LoginStage.AcceleratorReady,LoginAction.LaunchGame,"永恒之塔已加速，启动游戏");
        if (Named("accing_shade").Length != 0 || Named("acc_progress").Length != 0)
            return new(new(session,LoginStage.Accelerating,LoginAction.None,"accelerating","等待永恒之塔加速完成…"),pid,sessionPid,Text(window,"Hwnd"),Point.Empty,Client(window),ui);
        var card = Named("game_card_0");
        if (card.Length == 1)
        {
            bool Descendant(JsonElement c)
            {
                for(int i=0;i<40;i++)
                {
                    var parent=Text(c,"Parent"); if(parent==Text(card[0],"Address"))return true;
                    var found=controls.Where(n=>Text(n,"Address")==parent).ToArray(); if(found.Length!=1)return false;
                    c=found[0];
                }
                return false;
            }
            if (controls.Any(c => Descendant(c) && Text(c,"Text") == "永恒之塔"))
                return Click(card[0],LoginStage.AcceleratorHome,LoginAction.Accelerate,"开启永恒之塔加速");
        }
        return new(new(session,LoginStage.Blocked,LoginAction.None,"uu-unrecognized","UU 当前页面不是已记录的永恒之塔页面，请检查加速器。"),pid,sessionPid,Text(window,"Hwnd"),Point.Empty,Client(window),ui);
    }
}
