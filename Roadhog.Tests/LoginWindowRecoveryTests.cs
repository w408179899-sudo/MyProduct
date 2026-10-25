using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class LoginWindowRecoveryTests
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static JsonArray Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"launcher-untitled.json")))!["Windows"]!.AsArray();
    private static LoginObservation Project(JsonArray windows)=>LoginNativeReader.ProjectLauncher(
        windows.Select(w=>JsonSerializer.SerializeToElement(w)).ToArray(),"live-launcher",8920,900);
    private static JsonObject MainWindow(JsonArray windows)=>windows.Select(w=>w!.AsObject()).Single(w=>w["Hwnd"]!.GetValue<string>()=="0x1B021A");
    private static JsonObject Host(JsonArray windows)=>windows.Select(w=>w!.AsObject()).Single(w=>w["Hwnd"]!.GetValue<string>()=="0x3000C4");

    internal static Task ArchivedUntitledLauncherAsync()
    {
        var windows=Fixture();var scene=Project(windows);
        Require(scene.Snapshot.Action==LoginAction.StartLauncher&&scene.Window=="0x1B021A","The live untitled dialog is selected instead of its browser host.");
        Require(scene.Point==new System.Drawing.Point(980,625)&&scene.Pid==8920,"Start input stays bound to the recorded launcher process and geometry.");
        foreach(var node in windows)
        {
            var rect=node!["ClientRect"]!.AsArray();
            rect[0]=rect[0]!.GetValue<int>()+100;rect[2]=rect[2]!.GetValue<int>()+100;
            rect[1]=rect[1]!.GetValue<int>()+50;rect[3]=rect[3]!.GetValue<int>()+50;
        }
        Require(Project(windows).Point==new System.Drawing.Point(1080,675),"The button follows the current window location.");
        windows=Fixture();MainWindow(windows)["TitleCandidate"]="永恒之塔";
        windows.Remove(Host(windows));
        Require(Project(windows).Snapshot.Action==LoginAction.StartLauncher,"The already supported titled launcher remains supported.");
        return Task.CompletedTask;
    }

    internal static Task UntitledLauncherGuardsAsync()
    {
        foreach(Action<JsonArray> change in new Action<JsonArray>[]
        {
            w=>MainWindow(w)["Visible"]=false,w=>MainWindow(w)["Disabled"]=true,
            w=>MainWindow(w)["Style"]="0xB40A004C",w=>MainWindow(w)["TitleCandidate"]="Other application",
            w=>MainWindow(w)["ClientRect"]![2]=1200,w=>MainWindow(w)["OwnerThread"]=0,
            w=>Host(w)["OwnerThread"]=999,w=>Host(w)["Visible"]=false,
            w=>Host(w)["ClientRect"]![0]=20,
            w=>{foreach(var n in w.Where(n=>n!["Style"]!.GetValue<string>()=="0x56010000").ToArray())w.Remove(n);}
        })
        {
            var windows=Fixture();change(windows);
            Require(Project(windows).Snapshot.Action==LoginAction.None,"Unrelated, ambiguous or incomplete launcher layouts must not authorize input.");
        }
        var duplicate=Fixture();var second=MainWindow(duplicate).DeepClone();second["Hwnd"]="0x123456";duplicate.Add(second);
        bool rejected=false;try{Project(duplicate);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Two matching dialog identities cannot choose an arbitrary window.");
        var titled=Fixture();MainWindow(titled)["TitleCandidate"]="永恒之塔";MainWindow(titled)["ClientRect"]![2]=900;
        Require(Project(titled).Snapshot.Action==LoginAction.None,"Titled but unsupported dimensions must still wait.");
        return Task.CompletedTask;
    }

    internal static async Task FocusRestoreAndCycleAsync()
    {
        var keys=new List<Keys[]>();uint foreground=8800;int validates=0;
        await LoginWindowFocus.EnsureAsync(7200,()=>foreground,()=>{validates++;return Task.CompletedTask;},
            (sent,_)=>{keys.Add(sent);foreground=7200;return Task.CompletedTask;},_=>Task.CompletedTask,null,CancellationToken.None);
        Require(validates==1&&keys.Count==1&&keys[0].SequenceEqual(new[]{Keys.Menu,Keys.Tab}),"A minimized UU is left by Alt+Tab before any credential input.");
        keys.Clear();foreground=1;validates=0;
        await LoginWindowFocus.EnsureAsync(3,()=>foreground,()=>{validates++;return Task.CompletedTask;},
            (sent,_)=>{keys.Add(sent);foreground=keys.Count==1?2u:3u;return Task.CompletedTask;},_=>Task.CompletedTask,null,CancellationToken.None);
        Require(keys.Count==2&&keys[1].SequenceEqual(new[]{Keys.Menu,Keys.Escape})&&validates==2,"Cycle beyond the last two windows and validate before each switch.");
        keys.Clear();
        await LoginWindowFocus.EnsureAsync(3,()=>3,()=>throw new InvalidOperationException("No switch needed"),
            (sent,_)=>{keys.Add(sent);return Task.CompletedTask;},_=>Task.CompletedTask,null,CancellationToken.None);
        Require(keys.Count==0,"An already foreground login window receives no switching input.");
    }

    internal static async Task FocusBoundsAndLifecycleAsync()
    {
        int sent=0;bool failed=false;
        try{await LoginWindowFocus.EnsureAsync(3,()=>1,()=>Task.CompletedTask,(_,_)=>{sent++;return Task.CompletedTask;},
            _=>Task.CompletedTask,null,CancellationToken.None);}catch(InvalidOperationException){failed=true;}
        Require(failed&&sent==20,"A permanently wrong foreground has a bounded failure instead of permitting input.");
        sent=0;failed=false;
        try{await LoginWindowFocus.EnsureAsync(3,()=>1,()=>throw new InvalidOperationException("Window replaced"),
            (_,_)=>{sent++;return Task.CompletedTask;},_=>Task.CompletedTask,null,CancellationToken.None);}catch(InvalidOperationException){failed=true;}
        Require(failed&&sent==0,"A changed target cannot receive switching input from its old observation.");
        using var stop=new CancellationTokenSource();sent=0;bool cancelled=false;
        try{await LoginWindowFocus.EnsureAsync(3,()=>1,()=>Task.CompletedTask,(_,_)=>{sent++;stop.Cancel();return Task.CompletedTask;},
            t=>{t.ThrowIfCancellationRequested();return Task.CompletedTask;},null,stop.Token);}catch(OperationCanceledException){cancelled=true;}
        Require(cancelled&&sent==1,"Stop prevents any further switching.");
        sent=0;
        await LoginWindowFocus.EnsureAsync(3,()=>sent==20?3u:1u,()=>Task.CompletedTask,(_,_)=>{sent++;return Task.CompletedTask;},
            _=>Task.CompletedTask,null,CancellationToken.None);
        Require(sent==20,"The final switch is confirmed before reporting failure.");
    }
}
