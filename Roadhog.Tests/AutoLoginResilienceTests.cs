using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Application.Login;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class AutoLoginResilienceTests
{
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void Reject(Action action,string message)
    { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message); }
    private static LoginSnapshot Scene(LoginStage stage,LoginAction action=LoginAction.None,string key="")=>
        new("session",stage,action,key.Length==0?stage+":"+action:key,stage.ToString(),stage==LoginStage.InWorld?"HiApple":"",stage==LoginStage.InWorld?123:0);
    private static readonly AutoLoginOptions Fast=new(){PollInterval=TimeSpan.FromMilliseconds(1),TransitionTimeout=TimeSpan.FromMilliseconds(100),Timeout=TimeSpan.FromSeconds(3)};
    private sealed class Session(Func<LoginSnapshot> read,Func<LoginSnapshot,CancellationToken,Task<bool>> execute):IAutoLoginSession
    {
        internal int Releases;
        public Task<LoginSnapshot> ReadAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(read());}
        public Task<bool> ExecuteAsync(LoginSnapshot snapshot,LoginAction action,CancellationToken token)=>execute(snapshot,token);
        public Task ReleaseInputAsync(){Releases++;return Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }

    internal static async Task RecoverAtEveryActionAsync()
    {
        var steps=new[]
        {
            Scene(LoginStage.Desktop,LoginAction.OpenAccelerator),Scene(LoginStage.AcceleratorHome,LoginAction.Accelerate),
            Scene(LoginStage.AcceleratorReady,LoginAction.LaunchGame),Scene(LoginStage.Launcher,LoginAction.StartLauncher),
            Scene(LoginStage.PasswordLogin,LoginAction.SelectPasswordTab,"tab"),Scene(LoginStage.PasswordLogin,LoginAction.AcceptLoginProtocol,"protocol"),
            Scene(LoginStage.PasswordLogin,LoginAction.SubmitCredentials,"credentials"),Scene(LoginStage.Captcha,LoginAction.SolveCaptcha,"captcha1"),
            Scene(LoginStage.CaptchaError,LoginAction.DismissCaptchaError),Scene(LoginStage.PasswordLogin,LoginAction.SubmitCredentials,"credentials"),
            Scene(LoginStage.Captcha,LoginAction.SolveCaptcha,"captcha2"),Scene(LoginStage.Agreement,LoginAction.AcceptAgreement),
            Scene(LoginStage.ServerSelection,LoginAction.SelectServer),Scene(LoginStage.CharacterSelection,LoginAction.SelectCharacter,"select"),
            Scene(LoginStage.CharacterSelection,LoginAction.EnterCharacter,"enter"),Scene(LoginStage.EntryAgreement,LoginAction.AcceptEntryAgreement,"check"),
            Scene(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement,"confirm"),Scene(LoginStage.InWorld)
        };
        int index=0,attempt=0,releases=0;var recovery=new LoginInputRecovery("test");
        await using var session=new Session(()=>steps[index],(_,token)=>recovery.ExecuteAsync(()=>
        {
            if(++attempt%2==1)throw new InvalidOperationException("Page/focus changed during the action.");
            index++;return Task.FromResult(true);
        },_=>{releases++;return Task.FromResult(OperationResult.Ok());},token));
        var result=await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None);
        Require(result.Success&&result.Value==123&&index==steps.Length-1,"All login stages must resume after an interrupted action.");
        Require(releases==steps.Length-1&&attempt==releases*2&&session.Releases==1,"Each interrupted attempt releases input; successful stages are not duplicated.");
    }

    internal static async Task InputReleaseAndCancellationAsync()
    {
        var recovery=new LoginInputRecovery("test");int actions=0,releases=0;
        bool sent=await recovery.ExecuteAsync(()=>{actions++;throw new IOException("Device interrupted");},_=>
        { releases++;return Task.FromResult(releases==1?OperationResult.Fail("device offline"):OperationResult.Ok()); },CancellationToken.None);
        Require(!sent&&actions==1&&releases==2,"Retry release until neutral; never repeat the input while release failed.");
        using var cancel=new CancellationTokenSource();
        try
        {
            await recovery.ExecuteAsync(()=>throw new InvalidOperationException("Focus changed"),_=>
            { cancel.Cancel();return Task.FromResult(OperationResult.Fail("offline")); },cancel.Token);
            throw new InvalidOperationException("Cancellation was swallowed.");
        }
        catch(OperationCanceledException) { }
        bool escaped=false;
        try { await recovery.ExecuteAsync(()=>throw new ObjectDisposedException("session"),_=>Task.FromResult(OperationResult.Ok()),CancellationToken.None); }
        catch(ObjectDisposedException) { escaped=true; }
        Require(escaped,"Disposed sessions and programming faults are not retryable read errors.");
    }

    internal static async Task FinalPressWaitsForOutcomeAsync()
    {
        int calls=0,releases=0;bool pressed=false;var recovery=new LoginInputRecovery("test");
        bool awaiting=await recovery.ExecuteAsync(()=>
        { calls++;pressed=true;throw new IOException("Mouse-up acknowledgement failed after successful final mouse-down."); },_=>
        { releases++;return Task.FromResult(OperationResult.Ok()); },CancellationToken.None,()=>pressed);
        Require(awaiting&&calls==1&&releases==1,"A final launch/login/entry press waits for its outcome after successful release.");
        foreach(var pair in new[]{(LoginStage.AcceleratorReady,LoginAction.LaunchGame),(LoginStage.Launcher,LoginAction.StartLauncher),
            (LoginStage.PasswordLogin,LoginAction.SubmitCredentials),(LoginStage.ServerSelection,LoginAction.SelectServer),
            (LoginStage.CharacterSelection,LoginAction.EnterCharacter),(LoginStage.EntryAgreement,LoginAction.ConfirmEntryAgreement),
            (LoginStage.Disconnected,LoginAction.ConfirmGameExit)})
        {
            int clicks=0;await using var session=new Session(()=>Scene(pair.Item1,pair.Item2) with {GameProcessCount=pair.Item2==LoginAction.ConfirmGameExit?1:0},(_,_)=>{clicks++;return Task.FromResult(true);});
            var options=new AutoLoginOptions{PollInterval=TimeSpan.FromMilliseconds(1),ActionRetryInterval=TimeSpan.FromMilliseconds(5),TransitionTimeout=TimeSpan.FromMilliseconds(100),Timeout=TimeSpan.FromSeconds(3)};
            Require(!(await new AutoLoginRunner(options).RunAsync(session,"HiApple",null,CancellationToken.None)).Success&&clicks==1,"No duplicate final input while waiting: "+pair.Item2);
        }
    }

    internal static Task PublicationCannotBindHeldTargetsAsync()
    {
        using var publisher=new LoginSnapshotPublisher(new GameApiReadContext("test",0,"","dma"));
        var scene=new LoginObservation(Scene(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),1,2,"window",new(10,20),new(0,0,100,100));
        int bound=0;
        Require(publisher.Capture(l=>{l("first");return scene;},_=>bound++)==scene&&bound==1,"A complete scene can bind an input target.");
        foreach(var error in new Exception[]{new IOException(),new KeyNotFoundException(),new JsonException(),new FormatException(),new OverflowException(),new Vmmsharp.VmmException("native")})
        {
            Require(publisher.Capture(l=>{l("first");throw error;},_=>bound++)==scene,"Transient failures preserve the official scene.");
            Require(bound==1,"Held geometry cannot authorize physical input.");
        }
        Require(publisher.Capture(l=>{l("second");throw new IOException();},_=>bound++) is null&&bound==1,"Process replacement invalidates input and official old controls.");
        var next=scene with{Pid=3,Snapshot=scene.Snapshot with{Session="second"}};
        Require(publisher.Capture(l=>{l("second");return next;},_=>bound++)==next&&bound==2,"Recovery binds the new process only.");
        return Task.CompletedTask;
    }

    internal static Task TargetIdentityAsync()
    {
        var expected=new LoginObservation(Scene(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),1,2,"window",new(10,20),new(0,0,100,100));
        LoginInputTarget.Validate(expected,expected,true);
        foreach(var next in new[]
        {
            expected with{Pid=3},expected with{SessionPid=4},expected with{Window="other"},expected with{Bounds=new(2,2,100,100)},
            expected with{Snapshot=expected.Snapshot with{Session="replacement"}},expected with{Snapshot=expected.Snapshot with{Stage=LoginStage.Captcha}},
            expected with{Snapshot=expected.Snapshot with{ActionIdentity="moved-control"}},expected with{Snapshot=expected.Snapshot with{Action=LoginAction.SelectPasswordTab}}
        })Reject(()=>LoginInputTarget.Validate(expected,next,true),"Process, window, geometry, page and action changes must abandon input.");
        return Task.CompletedTask;
    }

    internal static async Task RejectedActionsTimeoutAsync()
    {
        int attempts=0;
        await using var session=new Session(()=>Scene(LoginStage.PasswordLogin,LoginAction.SubmitCredentials),(_,_)=>{attempts++;return Task.FromResult(false);});
        var result=await new AutoLoginRunner(Fast).RunAsync(session,"HiApple",null,CancellationToken.None);
        Require(!result.Success&&result.Error?.Contains("步骤等待超时",StringComparison.Ordinal)==true&&attempts>1,"Repeated rejected input obeys the step deadline, not only the ten-minute deadline.");
        Require(session.Releases==1,"Timeout releases input.");
    }

    internal static async Task LostControlClickRecoversAsync()
    {
        foreach(var action in new[]{LoginAction.SelectPasswordTab,LoginAction.AcceptLoginProtocol,LoginAction.AcceptAgreement,LoginAction.SelectCharacter,LoginAction.AcceptEntryAgreement})
        {
            int clicks=0;
            var stage=action==LoginAction.AcceptAgreement?LoginStage.Agreement:action==LoginAction.SelectCharacter?LoginStage.CharacterSelection:
                action==LoginAction.AcceptEntryAgreement?LoginStage.EntryAgreement:LoginStage.PasswordLogin;
            await using var session=new Session(()=>clicks<2?Scene(stage,action):Scene(LoginStage.InWorld),(_,_)=>{clicks++;return Task.FromResult(true);});
            var options=new AutoLoginOptions{PollInterval=TimeSpan.FromMilliseconds(1),ActionRetryInterval=TimeSpan.FromMilliseconds(5),TransitionTimeout=TimeSpan.FromMilliseconds(100),Timeout=TimeSpan.FromSeconds(3)};
            Require((await new AutoLoginRunner(options).RunAsync(session,"HiApple",null,CancellationToken.None)).Success&&clicks==2,"A control still requiring the action is retried after lost input: "+action);
        }
    }

    internal static Task UiChildrenFaultsAsync()
    {
        foreach(uint offset in new uint[]{0x760,0x76c})
        {
            var memory=new Dictionary<ulong,byte[]>();uint parent=0x20000,manager=0x10000,child=0x30000,data=0x40000;
            memory[parent+offset]=[ ..BitConverter.GetBytes(data),..BitConverter.GetBytes(1),..BitConverter.GetBytes(1) ];
            memory[data]=BitConverter.GetBytes(child);memory[child+0x40]=[ ..BitConverter.GetBytes(manager),..BitConverter.GetBytes(parent) ];
            byte[]? Read(ulong at,int size)=>memory.TryGetValue(at,out var bytes)&&bytes.Length==size?bytes.ToArray():null;
            uint[] Capture()=>LoginUiChildren.Read(Read,parent,manager,offset,0x40,true,128);
            Require(Capture().SequenceEqual(new[]{child}),"Complete DuiLib children are preserved.");
            var relationship=memory[child+0x40];memory.Remove(child+0x40);
            Reject(()=>Capture(),"Unreadable backlink must not turn into an empty child list.");
            Reject(()=>LoginUiChildren.Read(Read,parent,manager,offset,0x40,false,128),"Identified custom arrays cannot swallow partial entry reads.");
            memory[child+0x40]=relationship;memory[data]=new byte[2];
            Reject(()=>Capture(),"Short child array must fail.");
            memory[data]=BitConverter.GetBytes(child);int arrayReads=0;
            Reject(()=>LoginUiChildren.Read((at,size)=>at==data&&++arrayReads==2?BitConverter.GetBytes(child+4):Read(at,size),parent,manager,offset,0x40,true,128),"Replacing children without changing count must fail.");
            memory[parent+offset]=new byte[12];Require(Capture().Length==0,"A complete zero count is a valid empty list.");
            memory.Remove(parent+offset);Reject(()=>Capture(),"Known container header missing is not a leaf.");
            Require(LoginUiChildren.Read(Read,parent,manager,offset,0x40,false,128).Length==0,"Leaf/custom objects without a container header remain supported.");
        }
        return Task.CompletedTask;
    }

    // Reconstruct archived UU trees as raw memory, including custom controls without RTTI.
    // This exercises traversal rather than merely feeding already-clean JSON to the projector.
    private sealed class UuMemory
    {
        private readonly Dictionary<ulong,byte[]> _pages=[];
        internal readonly JsonElement Window;
        internal ulong? FailedAddress;
        internal uint Root;
        internal void DisableRoot()=>_pages[Root][0x2b2]=0;
        internal void HideRoot()=>_pages[Root][0x2b0]=0;
        internal UuMemory(string fixture)
        {
            var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,fixture+".json"))).RootElement;
            var nodes=json.EnumerateArray().Single(m=>m.GetProperty("Controls").GetArrayLength()>100).GetProperty("Controls").EnumerateArray().ToArray();
            var addresses=nodes.Select((n,i)=>(Name:n.GetProperty("Address").GetString()!,Address:(uint)(0x100000+i*0x4000))).ToDictionary(x=>x.Name,x=>x.Address);
            foreach(var node in nodes)
            {
                uint address=addresses[node.GetProperty("Address").GetString()!];var bytes=new byte[0x4000];_pages[address]=bytes;
                string parent=node.GetProperty("Parent").GetString()!;if(!addresses.ContainsKey(parent))Root=address;
                Put(bytes,0x14c,0x80000);Put(bytes,0x150,addresses.GetValueOrDefault(parent));
                Put(bytes,0x1d8,address+0x800);Encoding.Unicode.GetBytes(node.GetProperty("Name").GetString()!+"\0").CopyTo(bytes,0x800);
                var rect=node.GetProperty("Rect");int pos=0x260;
                foreach(string part in new[]{"Left","Top","Right","Bottom"}){Put(bytes,pos,unchecked((uint)rect.GetProperty(part).GetInt32()));pos+=4;}
                bytes[0x2b0]=bytes[0x2b1]=node.GetProperty("Visible").GetBoolean()?(byte)1:(byte)0;bytes[0x2b2]=node.GetProperty("Enabled").GetBoolean()?(byte)1:(byte)0;
                Put(bytes,0x2e4,address+0xa00);Encoding.Unicode.GetBytes((node.GetProperty("Text").GetString()??"")+"\0").CopyTo(bytes,0xa00);
                var children=nodes.Where(n=>n.GetProperty("Parent").GetString()==node.GetProperty("Address").GetString()).Select(n=>addresses[n.GetProperty("Address").GetString()!]).ToArray();
                Put(bytes,0x76c,address+0xc00);Put(bytes,0x770,(uint)children.Length);Put(bytes,0x774,(uint)children.Length);
                for(int i=0;i<children.Length;i++)Put(bytes,0xc00+i*4,children[i]);
                // Exercise real RTTI container/leaf decisions as well as the archived custom classes.
                string cls=node.GetProperty("Class").GetString()!;
                if(cls.Length>0)
                {
                    Put(bytes,0,address+0x1004);Put(bytes,0x1000,address+0x1100);
                    Put(bytes,0x110c,address+0x1200);Put(bytes,0x1110,address+0x1300);
                    Encoding.ASCII.GetBytes(cls+"\0").CopyTo(bytes,0x1208);
                    Put(bytes,0x1308,1);Put(bytes,0x130c,address+0x1400);Put(bytes,0x1400,address+0x1500);
                    Put(bytes,0x1500,address+0x1600);
                    Encoding.ASCII.GetBytes((children.Length>0?".?AVCContainerUI@DuiLib@@":".?AVCControlUI@DuiLib@@")+"\0").CopyTo(bytes,0x1608);
                }
            }
            Window=JsonSerializer.SerializeToElement(new{Hwnd="0x10001",ClientRect=new[]{0,0,1000,688},Manager="0x80000",Root=$"0x{Root:X}"});
        }
        private static void Put(byte[] bytes,int at,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
        internal byte[]? Read(ulong at,int length)
        {
            if(at==FailedAddress)return null;
            ulong page=at&~0x3fffUL;int offset=(int)(at-page);
            return _pages.TryGetValue(page,out var bytes)&&offset+length<=bytes.Length?bytes.AsSpan(offset,length).ToArray():null;
        }
        internal LoginObservation Capture()=>LoginNativeReader.ProjectUu(JsonSerializer.SerializeToElement(new UuUiReader(Read).Run(Window)),Window,"one",1,2);
    }

    internal static Task UuRawTreeReplayAsync()
    {
        foreach(var sample in new[]{("05-uu-ui",LoginAction.Accelerate),("11-uu-ui-after-acc",LoginAction.LaunchGame)})
        {
            var memory=new UuMemory(sample.Item1);
            Require(memory.Capture().Snapshot.Action==sample.Item2,"Raw archived UU tree retains supported action and unknown custom classes.");
            memory.FailedAddress=memory.Root+0xc00;
            Reject(()=>memory.Capture(),"Incomplete traversal cannot publish a partial ready/loading scene.");
            memory.FailedAddress=null;Require(memory.Capture().Snapshot.Action==sample.Item2,"Native traversal resumes after a missing page recovers.");
            memory.DisableRoot();Require(memory.Capture().Snapshot.Action==LoginAction.None,"A disabled parent disables all child targets.");
            memory.HideRoot();memory.FailedAddress=memory.Root+0xc00;
            Require(memory.Capture().Snapshot.Action==LoginAction.None,"A completely read hidden ancestor excludes its dormant child subtree from the actionable scene.");
        }
        return Task.CompletedTask;
    }

    internal static Task HiddenControlNonresidentPayloadAsync()
    {
        var memory=new UuMemory("11-uu-ui-after-acc");
        memory.FailedAddress=memory.Root;
        Reject(()=>memory.Capture(),"A visible control still requires its complete payload.");
        memory.HideRoot();
        Require(memory.Capture().Snapshot.Action==LoginAction.None,
            "Confirmed hidden controls do not require their nonresident payload pages.");
        foreach(uint offset in new uint[]{0x14c,0x2b0})
        {
            memory.FailedAddress=memory.Root+offset;
            Reject(()=>memory.Capture(),"Missing identity or visibility cannot become a hidden control.");
        }
        memory=new UuMemory("11-uu-ui-after-acc");
        byte[]? Changed(ulong at,int length)
        {
            var bytes=memory.Read(at,length);
            if(at==memory.Root&&bytes is{Length:>0x2b0})bytes[0x2b0]=0;
            return bytes;
        }
        Reject(()=>new UuUiReader(Changed).Run(memory.Window),
            "A control changing visibility between metadata and payload cannot publish a mixed target.");
        return Task.CompletedTask;
    }

    internal static Task TransitionalPagesAsync()
    {
        var window=JsonSerializer.SerializeToElement(new{Hwnd="0x10001",ClientRect=new[]{0,0,1000,768}});
        var uu=LoginNativeReader.ProjectUu(JsonSerializer.SerializeToElement(new{Controls=Array.Empty<object>()}),window,"one",1,2);
        Require(uu.Snapshot.Stage==LoginStage.Loading&&uu.Snapshot.Action==LoginAction.None,"UU changing/loading pages wait instead of terminating login.");
        var unlaidOut=JsonSerializer.SerializeToElement(new{Controls=new[]{"launch_game","stop_acc","game_name"}.Select(name=>new{
            Name=name,Visible=true,AncestorsVisible=true,Enabled=true,Text="永恒之塔",Rect=new{Left=0,Top=0,Right=0,Bottom=0}})});
        Reject(()=>LoginNativeReader.ProjectUu(unlaidOut,window,"one",1,2),"Visible buttons without a laid-out rectangle cannot be clicked at a default corner.");
        var passport=JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"password.json")))!;
        var controls=passport["Windows"]!.AsArray().Single(w=>w!["Managers"]?.AsArray().Count==1)!["Managers"]![0]!["Controls"]!.AsArray();
        controls.Add(JsonSerializer.SerializeToNode(new{Name="HintText",Text="",VisibleThroughRoot=true,EnabledThroughRoot=true}));
        Reject(()=>LoginNativeReader.ProjectSdo(JsonSerializer.SerializeToElement(passport),"one",1,2),"An unpopulated popup must not be classified as a terminal error.");
        var server=JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"25-server-state.json")))!;
        server["Servers"]![0]!["Online"]=false;
        Require(LoginNativeReader.ProjectGame(JsonSerializer.SerializeToElement(server),window,"HiApple","one",2).Snapshot.Stage==LoginStage.Loading,"A temporarily offline server waits.");
        return Task.CompletedTask;
    }
}
