using System.Text.Json;
using Roadhog.Application.Login;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;

internal static class AutoLoginTests
{
    private static readonly AutoLoginOptions Fast = new() { PollInterval=TimeSpan.FromMilliseconds(1),TransitionTimeout=TimeSpan.FromMilliseconds(80),Timeout=TimeSpan.FromSeconds(3),LauncherReadyDelay=TimeSpan.Zero };
    private static LoginSnapshot State(LoginStage stage,LoginAction action=LoginAction.None,string key="",string session="one",string character="")=>
        new(session,stage,action,key.Length==0?stage.ToString():key,stage.ToString(),character,stage==LoginStage.InWorld?123:0);
    private static LoginSnapshot World(string name="HiApple")=>State(LoginStage.InWorld,character:name);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static string Fixtures
    {
        get
        {
            var directory=new DirectoryInfo(AppContext.BaseDirectory);
            while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"Roadhog.Tests")))directory=directory.Parent;
            return Path.Combine(directory!.FullName,"Roadhog.Tests","Fixtures","Login");
        }
    }
    private static JsonElement Fixture(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures,name+".json"))).RootElement.Clone();
    private static JsonElement Window(int width=1024,int height=768)=>JsonSerializer.SerializeToElement(new{Hwnd="0x10001",ClientRect=new[]{0,0,width,height}});
    private sealed class Session(params LoginSnapshot[] snapshots):IAutoLoginSession
    {
        public List<LoginAction> Actions {get;}=[];
        public int Released;
        public Func<CancellationToken,Task>? OnAction;
        public Action<LoginSnapshot>? OnRead;
        public Func<LoginAction,bool>? AcceptAction;
        private int _index;
        public Task<LoginSnapshot> ReadAsync(CancellationToken token){token.ThrowIfCancellationRequested();var snapshot=snapshots[Math.Min(_index++,snapshots.Length-1)];OnRead?.Invoke(snapshot);return Task.FromResult(snapshot);}
        public async Task<bool> ExecuteAsync(LoginSnapshot snapshot,LoginAction action,CancellationToken token){Actions.Add(action);if(OnAction is not null)await OnAction(token);return AcceptAction?.Invoke(action)??true;}
        public Task ReleaseInputAsync(){Released++;return Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Messages : IProgress<string>
    {
        public List<string> Values { get; } = [];
        public void Report(string value) => Values.Add(value);
    }

    internal static async Task DisconnectWaitsForExitAndRestartsAsync()
    {
        var popup=State(LoginStage.Disconnected,LoginAction.ConfirmGameExit) with {GameProcessCount=1,GameProcessId=123};
        var messages=new Messages();
        await using var session=new Session(
            State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),
            State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),
            State(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement) with {GameProcessCount=1},
            popup,
            World() with {GameProcessCount=1}, // World fields can persist after disconnect.
            popup with {ActionIdentity="popup-moved"},
            State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate) with {GameProcessCount=1},
            State(LoginStage.Desktop,LoginAction.OpenAccelerator,session:"exited"),
            State(LoginStage.AcceleratorReady,LoginAction.LaunchGame,session:"exited"),
            State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials,session:"new-login"),
            World() with {Session="new-game",GameProcessCount=1,GameProcessId=456});
        var result=await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",messages,CancellationToken.None);
        Require(result.Success&&result.Value==456,"Only the new game process may complete relogin after disconnect confirmation.");
        Require(session.Actions.SequenceEqual(new[]{LoginAction.LaunchGame,LoginAction.SubmitCredentials,LoginAction.ConfirmEntryAgreement,
            LoginAction.ConfirmGameExit,LoginAction.OpenAccelerator,LoginAction.LaunchGame,LoginAction.SubmitCredentials}),
            "Confirm exit bypasses entry guards, waits without repeated clicks, and resets the previous login only after process absence.");
        Require(messages.Values.Any(s=>s.Contains("等待游戏进程退出"))&&session.Released==1,"Exit waiting is visible and releases input on completion.");
    }

    internal static async Task DisconnectWaitTimeoutAndCancellationAsync()
    {
        var popup=State(LoginStage.Disconnected,LoginAction.ConfirmGameExit) with {GameProcessCount=1,GameProcessId=123};
        var options=new AutoLoginOptions{PollInterval=Fast.PollInterval,ActionRetryInterval=TimeSpan.Zero,TransitionTimeout=TimeSpan.FromMilliseconds(30),Timeout=Fast.Timeout};
        await using(var session=new Session(popup,World() with {GameProcessCount=1}))
        {
            var result=await new AutoLoginRunner(options).RunAsync(session,"HiApple",null,CancellationToken.None);
            Require(!result.Success&&result.Error!.Contains("等待游戏进程退出"),"A process still alive cannot complete login from residual world data.");
            Require(session.Actions.SequenceEqual(new[]{LoginAction.ConfirmGameExit})&&session.Released==1,"No repeated confirmation or new login while exit is pending.");
        }
        using var cancel=new CancellationTokenSource();int reads=0;
        await using var pending=new Session(popup){OnRead=_=>{if(++reads==3)cancel.Cancel();}};
        bool canceled=false;
        var cancelOptions=new AutoLoginOptions{PollInterval=Fast.PollInterval,TransitionTimeout=TimeSpan.FromSeconds(1),Timeout=Fast.Timeout};
        try{await new AutoLoginRunner(cancelOptions).RunAsync(pending,"HiApple",null,cancel.Token);}catch(OperationCanceledException){canceled=true;}
        Require(canceled&&pending.Actions.SequenceEqual(new[]{LoginAction.ConfirmGameExit})&&pending.Released==1,"Stopping cancels process-exit wait and releases input.");
    }

    internal static async Task UnsentDisconnectConfirmationRetriesAsync()
    {
        var popup=State(LoginStage.Disconnected,LoginAction.ConfirmGameExit) with {GameProcessCount=1};int attempts=0;
        await using var session=new Session(popup,popup,State(LoginStage.Desktop),World() with{GameProcessCount=1})
        {AcceptAction=_=>++attempts>=2};
        Require((await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,
            "A rejected input attempt must retry against the current popup instead of entering exit wait.");
        Require(attempts==2&&session.Released==1,"Exactly one accepted confirmation precedes relogin.");
    }

    public static async Task LaunchWaitsForNextWindowAsync()
    {
        var messages = new Messages();
        await using var session = new Session(
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "initial-desktop"),
            State(LoginStage.AcceleratorReady, LoginAction.LaunchGame),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "uu-minimized"),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "foreground-changed", "changed"),
            State(LoginStage.AcceleratorReady, LoginAction.LaunchGame, "uu-restored", "changed"),
            State(LoginStage.AcceleratorHome, LoginAction.Accelerate, session: "changed"),
            State(LoginStage.Accelerating, session: "changed"),
            State(LoginStage.Loading, session: "launcher-created"),
            State(LoginStage.Launcher, LoginAction.StartLauncher, session: "launcher-created"),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "launcher-to-passport-gap", "changed-again"),
            State(LoginStage.PasswordLogin, LoginAction.SubmitCredentials, session: "passport-created"),
            World());
        var options = new AutoLoginOptions { PollInterval = Fast.PollInterval, TransitionTimeout = TimeSpan.FromSeconds(1), Timeout = Fast.Timeout, LauncherReadyDelay = TimeSpan.Zero };
        var result = await new AutoLoginRunner(options).RunAsync(session, "HiApple", messages, CancellationToken.None);
        Require(result.Success, "launcher and login resume after UU minimizes and process identities change");
        Require(session.Actions.SequenceEqual(new[] { LoginAction.OpenAccelerator, LoginAction.LaunchGame, LoginAction.StartLauncher, LoginAction.SubmitCredentials }),
            "after launch do not reopen UU, accelerate again or click launch twice");
        Require(messages.Values.Any(value => value.Contains("等待启动器或游戏窗口", StringComparison.Ordinal)), "show launch wait progress");
        Require(messages.Values.Count(value => value == LoginStage.Desktop.ToString()) == 1, "desktop fallback after launch must not advertise reopening UU");
        Require(session.Released == 1, "completion releases input");
    }

    public static async Task LaunchWaitTimeoutAndNewAttemptAsync()
    {
        var runner = new AutoLoginRunner(Fast);
        await using (var session = new Session(State(LoginStage.AcceleratorReady, LoginAction.LaunchGame),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "uu-minimized")))
        {
            var result = await runner.RunAsync(session, "HiApple", null, CancellationToken.None);
            Require(!result.Success && result.Error!.Contains("等待超时", StringComparison.Ordinal)
                && result.Error.Contains("等待启动器或游戏窗口", StringComparison.Ordinal), "missing launcher ends with the bounded launch-wait timeout");
            Require(session.Actions.SequenceEqual(new[] { LoginAction.LaunchGame }), "timeout sends no extra UU input");
            Require(session.Released == 1, "timeout releases input");
        }
        await using var retry = new Session(State(LoginStage.Desktop, LoginAction.OpenAccelerator),
            State(LoginStage.AcceleratorReady, LoginAction.LaunchGame), State(LoginStage.Launcher, LoginAction.StartLauncher), World());
        Require((await runner.RunAsync(retry, "HiApple", null, CancellationToken.None)).Success, "a user-requested new attempt can launch again");
        Require(retry.Actions.SequenceEqual(new[] { LoginAction.OpenAccelerator, LoginAction.LaunchGame, LoginAction.StartLauncher }), "launch wait is local to one attempt");
    }

    public static async Task LaunchWaitCancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        await using var session = new Session(State(LoginStage.AcceleratorReady, LoginAction.LaunchGame),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "uu-minimized"))
        { OnRead = snapshot => { if (snapshot.Stage == LoginStage.Desktop) cancellation.Cancel(); } };
        bool canceled = false;
        try { await new AutoLoginRunner(Fast).RunAsync(session, "HiApple", null, cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Require(canceled, "stop cancels the launch wait");
        Require(session.Actions.SequenceEqual(new[] { LoginAction.LaunchGame }) && session.Released == 1,
            "stop during minimized UU wait sends no further input and releases held input");
    }

    public static async Task UnsentLaunchDoesNotStartWaitAsync()
    {
        int launches = 0;
        await using var session = new Session(State(LoginStage.AcceleratorReady, LoginAction.LaunchGame),
            State(LoginStage.Desktop, LoginAction.OpenAccelerator, "page-changed-before-click"),
            State(LoginStage.AcceleratorReady, LoginAction.LaunchGame), State(LoginStage.Launcher, LoginAction.StartLauncher), World())
        { AcceptAction = action => action != LoginAction.LaunchGame || ++launches > 1 };
        Require((await new AutoLoginRunner(Fast).RunAsync(session, "HiApple", null, CancellationToken.None)).Success,
            "an invalidated unsent launch can resume normally");
        Require(session.Actions.SequenceEqual(new[] { LoginAction.LaunchGame, LoginAction.OpenAccelerator, LoginAction.LaunchGame, LoginAction.StartLauncher }),
            "only a successfully dispatched launch suppresses subsequent UU actions");
    }

    public static async Task FullAndResumeAsync()
    {
        var sequence=new[]{State(LoginStage.Desktop,LoginAction.OpenAccelerator),State(LoginStage.AcceleratorHome,LoginAction.Accelerate),
            State(LoginStage.Accelerating),State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),State(LoginStage.Launcher,LoginAction.StartLauncher),
            State(LoginStage.PasswordLogin,LoginAction.SelectPasswordTab,"tab"),State(LoginStage.PasswordLogin,LoginAction.AcceptLoginProtocol,"protocol"),
            State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials,"credentials"),State(LoginStage.Captcha,LoginAction.SolveCaptcha),
            State(LoginStage.Loading),State(LoginStage.Agreement,LoginAction.AcceptAgreement),State(LoginStage.ServerSelection,LoginAction.SelectServer),
            State(LoginStage.CharacterSelection,LoginAction.SelectCharacter,"character"),State(LoginStage.CharacterSelection,LoginAction.EnterCharacter,"enter"),
            State(LoginStage.EntryAgreement,LoginAction.AcceptEntryAgreement,"check"),State(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement,"confirm"),World(),World()};
        for(int start=0;start<sequence.Length-1;start++)
        {
            await using var session=new Session(sequence[start..]);
            var result=await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None);
            Require(result.Success&&result.Value==123,"resume reaches configured character at stage "+start);
            Require(session.Released==1,"input released after completion");
            Require(session.Actions.Count==sequence[start..].Count(s=>s.Action!=LoginAction.None),"only remaining actions dispatched");
        }
    }

    public static async Task UuReconnectResumesAsync()
    {
        foreach(bool submitted in new[]{false,true})
        {
            var steps=new List<LoginSnapshot>{State(LoginStage.AcceleratorReady,LoginAction.LaunchGame)};
            if(submitted)steps.Add(State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials));
            steps.AddRange([
                State(LoginStage.Desktop,LoginAction.OpenAccelerator,"minimized"),
                State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate,"error-40002"),
                State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate,"error-40002"),
                State(LoginStage.Accelerating),State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),
                State(LoginStage.Desktop,LoginAction.OpenAccelerator,"minimized-again"),
                State(LoginStage.Launcher,LoginAction.StartLauncher),World()]);
            await using var session=new Session(steps.ToArray());
            Require((await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"Reconnect resumes login after interrupted launch/authentication.");
            Require(session.Actions.Count(a=>a==LoginAction.LaunchGame)==2&&session.Actions.Count(a=>a==LoginAction.Reaccelerate)==1
                &&session.Actions.All(a=>a!=LoginAction.OpenAccelerator)&&session.Actions.Last()==LoginAction.StartLauncher,
                "The popup bypasses minimized launch wait; UU restores itself; confirmed absence permits a new launch.");
        }
        await using var existing=new Session(State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),
            State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate),State(LoginStage.Launcher,LoginAction.StartLauncher),World());
        Require((await new AutoLoginRunner(Fast).RunAsync(existing,"HiApple",null,CancellationToken.None)).Success
            &&existing.Actions.SequenceEqual(new[]{LoginAction.SubmitCredentials,LoginAction.Reaccelerate}),
            "Reconnect alone must not resubmit an existing launcher after authentication.");
        var confirm=State(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement);
        await using var entering=new Session(confirm,State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate),confirm,
            State(LoginStage.AcceleratorHome,LoginAction.Accelerate),State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),
            State(LoginStage.CharacterSelection,LoginAction.EnterCharacter),confirm,World());
        Require((await new AutoLoginRunner(Fast).RunAsync(entering,"HiApple",null,CancellationToken.None)).Success
            &&entering.Actions.Count(a=>a==LoginAction.ConfirmEntryAgreement)==2
            &&entering.Actions.Count(a=>a==LoginAction.Reaccelerate)==1,"Popup can interrupt entry; only confirmed client absence resets entry guards.");
    }

    public static async Task UuReconnectInputAndStopAsync()
    {
        await using(var rejected=new Session(State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),
            State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate),State(LoginStage.AcceleratorReady,LoginAction.LaunchGame),World())
            {AcceptAction=action=>action!=LoginAction.Reaccelerate})
        {
            Require((await new AutoLoginRunner(Fast).RunAsync(rejected,"HiApple",null,CancellationToken.None)).Success
                &&rejected.Actions.Count(a=>a==LoginAction.LaunchGame)==1,"A rejected/unsent reconnect cannot clear launch guards.");
        }
        int clicks=0;
        await using(var retry=new Session(State(LoginStage.AcceleratorReconnect,LoginAction.Reaccelerate)))
        {
            var options=new AutoLoginOptions{PollInterval=Fast.PollInterval,ActionRetryInterval=TimeSpan.FromMilliseconds(5),TransitionTimeout=Fast.TransitionTimeout,Timeout=Fast.Timeout};
            using var cancel=new CancellationTokenSource();
            retry.OnAction=_=>{if(++clicks==2)cancel.Cancel();return Task.CompletedTask;};
            try{await new AutoLoginRunner(options).RunAsync(retry,"HiApple",null,cancel.Token);throw new InvalidOperationException("Stop was ignored.");}
            catch(OperationCanceledException){}
            Require(clicks==2&&retry.Released==1,"A still-verified popup can retry; stop releases input and ends the reconnect loop.");
        }
    }
    public static async Task AlreadyOnlineAndMismatchAsync()
    {
        const ulong gameBase=0x10000000,dialog=0x20000000;
        var messageSlots=new byte[35*8];BitConverter.GetBytes(dialog).CopyTo(messageSlots,34*8);
        byte[] ReadMessage(ulong address,int size)=>address==gameBase+0xd63990+336*8?messageSlots:BitConverter.GetBytes(1u);
        Require(LoginNativeReader.HasVisibleGameMessage(ReadMessage,gameBase),"a visible exit message prevents accepting retained world data");
        Require(!LoginNativeReader.HasVisibleGameMessage((address,size)=>address==gameBase+0xd63990+336*8?messageSlots:BitConverter.GetBytes(0u),gameBase),"hidden message is not an active popup");
        bool rejected=false;
        try { LoginNativeReader.HasVisibleGameMessage((address,size)=>address==gameBase+0xd63990+336*8?messageSlots:[],gameBase); }
        catch(InvalidOperationException){rejected=true;}
        Require(rejected,"partial message visibility cannot mean no exit popup");
        foreach(var name in new[]{"HiApple","Other"})
        {
            await using var session=new Session(World(name));
            var result=await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None);
            Require(result.Success==(name=="HiApple"),"wrong character never starts");Require(session.Actions.Count==0,"online check sends no input");
        }
    }
    public static async Task StalePagesAndRetriesAsync()
    {
        await using(var session=new Session(State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),
            State(LoginStage.Launcher,LoginAction.StartLauncher,session:"replacement"),State(LoginStage.Loading,session:"replacement"),World()))
        {
            Require((await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"login process replacement waits for game");
            Require(session.Actions.SequenceEqual(new[]{LoginAction.SubmitCredentials}),"passport exit cannot cause duplicate launcher submission");
        }
        var enter=State(LoginStage.CharacterSelection,LoginAction.EnterCharacter,"enter");
        var confirm=State(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement,"confirm");
        await using(var session=new Session(enter,enter,confirm,enter,enter,World(),World()))
        {
            Require((await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"world transition completes");
            Require(session.Actions.SequenceEqual(new[]{LoginAction.EnterCharacter,LoginAction.ConfirmEntryAgreement}),"stale char page is not clicked after confirm");
        }
        await using(var session=new Session(State(LoginStage.Captcha,LoginAction.SolveCaptcha,"captcha1"),State(LoginStage.CaptchaError,LoginAction.DismissCaptchaError),
            State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),State(LoginStage.Captcha,LoginAction.SolveCaptcha,"captcha2"),World(),World()))
        {
            Require((await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"captcha retry completes");
            Require(session.Actions.Count==4,"retry uses password and new captcha");
        }
        foreach(var stage in new[]{LoginStage.CredentialsRejected,LoginStage.Blocked})
        {
            await using var session=new Session(State(stage));
            Require(!(await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"blocking message stops");
            Require(session.Actions.Count==0,"unknown popup is not dismissed");
        }
    }
    public static async Task CancellationAndTimeoutAsync()
    {
        using var cancel=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using(var session=new Session(State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials)))
        {
            session.OnAction=async token=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);};
            var task=new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,cancel.Token);
            await entered.Task;cancel.Cancel();
            try{await task;throw new InvalidOperationException("cancel did not propagate");}catch(OperationCanceledException){}
            Require(session.Released==1&&session.Actions.Count==1,"cancel releases input without continuing");
        }
        await using(var session=new Session(State(LoginStage.ServerSelection,LoginAction.SelectServer)))
        {
            Require(!(await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None)).Success,"no response times out");
            Require(session.Actions.Count==1,"slow server is never clicked repeatedly");
        }
    }
    public static Task ArchivedGamePagesAsync()
    {
        foreach(var test in new[]{("25-server-state",LoginAction.SelectServer),("30-server-transition",LoginAction.AcceptAgreement),
            ("32-character-list",LoginAction.SelectCharacter),("34-character-selected",LoginAction.EnterCharacter),
            ("36-entry-confirmation",LoginAction.AcceptEntryAgreement),("38-entry-checked",LoginAction.ConfirmEntryAgreement)})
        {
            var result=LoginNativeReader.ProjectGame(Fixture(test.Item1),Window(),"HiApple","test",1);
            Require(result.Snapshot.Action==test.Item2,"archived stage "+test.Item1+": "+result.Snapshot.Description);
            Require(result.Bounds.Contains(result.Point),"button inside client");
        }
        Require(LoginNativeReader.ProjectGame(Fixture("32-character-list"),Window(),"Missing","test",1).Snapshot.Stage==LoginStage.Blocked,"unknown character cannot select first slot");
        Require(LoginNativeReader.ProjectGame(Fixture("36-entry-confirmation"),Window(),"FONE","test",1).Snapshot.Stage==LoginStage.Blocked,"confirmation must belong to configured character");
        Require(LoginNativeReader.ProjectGame(Fixture("40-after-entry"),Window(),"HiApple","test",1).Snapshot.Stage==LoginStage.Loading,"wait state suppresses stale visible character window after restarting login");
        return Task.CompletedTask;
    }
    public static Task ArchivedUuAndPassportAsync()
    {
        foreach(var test in new[]{("05-uu-ui",LoginAction.Accelerate),("11-uu-ui-after-acc",LoginAction.LaunchGame)})
        {
            var manager=Fixture(test.Item1).EnumerateArray().Single(m=>m.GetProperty("Controls").GetArrayLength()>100);
            var result=LoginNativeReader.ProjectUu(manager,Window(1000,688),"test",1,2);
            Require(result.Snapshot.Action==test.Item2,"UU archived controls: "+result.Snapshot.Description);
        }
        var password=LoginNativeReader.ProjectSdo(Fixture("password"),"test",1,2);
        Require(password.Snapshot.Action==LoginAction.SubmitCredentials,"real password page selects credential action");
        return Task.CompletedTask;
    }
    public static Task PublicationFaultsAsync()
    {
        using var publisher=new LoginSnapshotPublisher(new GameApiReadContext("one",0,"","dma-one"));
        var first=new LoginObservation(State(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),1,2,"window",new(10,20),new(0,0,100,100));
        Require(publisher.Capture(l=>throw new Vmmsharp.VmmException("VMM INIT pending")) is null,"native cold initialization cannot terminate login before a session exists");
        Require(publisher.Capture(l=>{l("a");throw new Vmmsharp.VmmException("Failed to load PDB for module");}) is null,"native PDB cold start waits below the business boundary");
        Require(publisher.Capture(l=>{l("a");throw new IOException();}) is null,"cold failed reads publish nothing");
        Require(publisher.Capture(l=>{l("a");return first;})==first,"complete snapshot publishes");
        Require(publisher.Capture(l=>{l("a");throw new IOException();})==first,"read failure retains official scene");
        Require(publisher.Capture(l=>{l("a");throw new Vmmsharp.VmmException("Failed to load PDB for module");})==first,"native symbol errors retain the official same-session scene");
        Require(publisher.Capture(l=>{l("b");throw new Vmmsharp.VmmException("Failed to load PDB for module");}) is null,"new native session cannot reuse prior controls while symbols initialize");
        Require(publisher.Capture(l=>{l("b");throw new IOException();}) is null,"new process/module generation invalidates old controls before capture");
        var absent=first with{Snapshot=State(LoginStage.Desktop,LoginAction.OpenAccelerator,"desktop","b")};
        Require(publisher.Capture(l=>{l("b");return absent;})==absent,"complete process absence is a valid new state");
        Require(AionVmmSnapshotChannels.All.Single(c=>c.Name=="login_scene").ReadPolicy==DmaSnapshotReadPolicy.Stable,"login registered in sealed stable catalog");
        var properties=typeof(LoginSnapshot).GetProperties().Select(p=>p.Name).ToArray();
        Require(!properties.Any(p=>new[]{"Success","Error","Quality","Fresh","Age","ReadValid"}.Contains(p)),"business snapshot exposes facts only");
        var application=Path.Combine(Directory.GetParent(Directory.GetParent(Fixtures)!.FullName)!.FullName,"..","Roadhog","Application","Login");
        Require(Directory.GetFiles(application,"*.cs").All(p=>!File.ReadAllText(p).Contains("MemRead",StringComparison.Ordinal)),"application login cannot read raw DMA");
        return Task.CompletedTask;
    }
    public static Task CredentialsAndImageBindingAsync()
    {
        const string secret="UnitTest-only-42!";var config=new AccountConfig{GameLoginAccount="unit-test",ProtectedGameLoginPassword=LoginPasswordProtector.Protect(secret)};
        var json=JsonSerializer.Serialize(config.Clone());Require(!json.Contains(secret,StringComparison.Ordinal),"password is not serialized in clear text");
        Require(LoginPasswordProtector.Unprotect(JsonSerializer.Deserialize<AccountConfig>(json)!.ProtectedGameLoginPassword)==secret,"encrypted credentials roundtrip");
        bool rejected=false;try{LoginPasswordProtector.Unprotect("invalid");}catch(InvalidOperationException){rejected=true;}Require(rejected,"corrupt password fails clearly");
        var old=new CaptchaImage(10,[1],[1],"old");var changed=old with{Hash="new"};var unrelated=new CaptchaImage(20,[2],[2],"newer");
        var before=new Dictionary<ulong,CaptchaImage>{{10,old}};
        Require(CaptchaMemoryReader.Changed(before,[old,unrelated]) is null,"unrelated newly found old image cannot be submitted");
        Require(CaptchaMemoryReader.Changed(before,[changed])==changed,"positive change at known buffer is usable");
        before[20]=unrelated;
        Require(CaptchaMemoryReader.Changed(before,[changed,unrelated with{Hash="other"}]) is null,"ambiguous changed buffers cannot be submitted");
        return Task.CompletedTask;
    }
    public static Task ModelParityAsync()
    {
        using var model=new OnnxCaptchaRecognizer(Path.Combine(AppContext.BaseDirectory,"Source","Login"));
        var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures,"Ocr","predictions.json"))).RootElement.EnumerateArray().ToArray();
        Require(rows.Length==100,"100 independent captured images used for conversion parity");
        foreach(var row in rows)
        {
            string file=row.GetProperty("file").GetString()!;var data=File.ReadAllBytes(Path.Combine(Fixtures,"Ocr",file));
            var tensor=OnnxCaptchaRecognizer.Preprocess(data);var expected=File.ReadAllBytes(Path.Combine(Fixtures,"Ocr",Path.ChangeExtension(file,"f32")));
            float error=0;for(int i=0;i<tensor.Length;i++)error=Math.Max(error,Math.Abs(tensor[i]-BitConverter.ToSingle(expected,i*4)));
            Require(error<.000001f,"Pillow preprocessing differs at "+file+": "+error);
            string text=row.GetProperty("pytorch").GetString()!;if(text.Length!=6||!text.All(char.IsAsciiLetterOrDigit))text="";
            Require(model.Predict(data)==text,"C# model differs from trained PyTorch at "+file);
        }
        return Task.CompletedTask;
    }
}
