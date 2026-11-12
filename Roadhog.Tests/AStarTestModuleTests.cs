using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.NavigationTests;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.NavigationTests;
using Roadhog.Infrastructure.NavigationTests;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class AStarTestModuleTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static AStarTestMap Map() => new(new AStarTestMapDocument { MapId = 7, Nodes = new()
    {
        new(new(0,0,0), new[]{1}), new(new(0,2,0),new[]{0,2}),
        new(new(2,2,0),new[]{1,3}), new(new(2,0,0),new[]{2}),
        new(new(0,0,10),new[]{5}), new(new(2,0,10),new[]{4})
    }});

    public static Task PlannerAsync()
    {
        var map = Map(); var planner = new AStarTestPlanner();
        var route = planner.Plan(map, new(0,0,0), new(2,0,0));
        Check(route.Success && route.Length == 6 && route.Points.Length == 4, "route follows known edges around wall");
        Check(!planner.Plan(map,new(0,0,0),new(0,0,10)).Success,"overlapping XY floors remain disconnected");
        Check(!planner.Plan(map,new(0,0,5),new(2,0,0)).Success,"no snapping across floors");
        Check(!planner.Plan(map,new(30,30,0),new(2,0,0)).Success,"outside coverage rejected");
        Check(planner.Plan(map,new(0,0,0),new(0,0,0)).Points.Length==1,"same-node route");
        using var stop=new CancellationTokenSource();stop.Cancel();
        try {planner.Plan(map,new(),new(),stop.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
        foreach(var bad in new[]{new AStarTestNode(new(float.NaN,0,0),Array.Empty<int>()),new AStarTestNode(new(),new[]{8})})
        {
            try { _=new AStarTestMap(new(){MapId=7,Nodes=new(){bad}});throw new Exception("invalid graph accepted"); }
            catch(InvalidDataException){}
        }
        return Task.CompletedTask;
    }

    public static async Task SampleAsync()
    {
        var map=await AStarTestMapFile.LoadAsync(AStarTestMapFile.SamplePath,default);
        Check(map.MapId==300100000&&map.Count==18115,"bundled converted sample loads");
        var route=new AStarTestPlanner().Plan(map,new(557,558,1022.11f),new(576,563,1022.02f));
        Check(route.Success&&route.Points.Length==23&&Math.Abs(route.Length-22.8305)<.01,"C# A* reproduces offline probe route");
        for(int i=1;i<route.Points.Length;i++) Check(AStarTestMap.Distance(route.Points[i-1],route.Points[i])<1.7,"preserve individual graph edges");
    }

    public static async Task BinaryMapAsync()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".astar.bin.gz");
        byte[] Document(int count, Action<BinaryWriter>? payload=null)
        {
            using var bytes=new MemoryStream();
            using(var w=new BinaryWriter(bytes,Encoding.UTF8,true))
            {
                w.Write(Encoding.ASCII.GetBytes("RHASTAR1"));w.Write(1);w.Write((uint)220050000);
                w.Write(count);w.Write(Math.Max(0,count-1));w.Write("test");w.Write("full map format");
                if(payload!=null)payload(w);
                else if(count>0&&count<=300001)
                {
                    for(int i=0;i<count;i++){w.Write((float)i);w.Write(0f);w.Write(12f);}
                    for(int i=0;i<=count;i++)w.Write(Math.Min(i,count-1));
                    for(int i=1;i<count;i++)w.Write(i);
                }
            }
            return bytes.ToArray();
        }
        void Save(byte[] data)
        {
            using var file=File.Create(path);using var gzip=new GZipStream(file,CompressionLevel.Fastest);gzip.Write(data);
        }
        try
        {
            var valid=Document(300001);Save(valid);
            var map=await AStarTestMapFile.LoadAsync(path,default);
            Check(map.Count==300001&&map.MapId==220050000&&map.EdgeCount==300000,"compact graph exceeds old sample limit");
            Check(map.Minimum==new Vector3Snapshot(0,0,12)&&map.Maximum==new Vector3Snapshot(300000,0,12),"cached bounds include every node");
            var route=new AStarTestPlanner().Plan(map,new(299998,0,12),new(300000,0,12));
            Check(route.Success&&route.Points.Length==3&&route.Length==2,"planner follows compact graph edges");
            using var stop=new CancellationTokenSource();stop.Cancel();
            try{await AStarTestMapFile.LoadAsync(path,stop.Token);throw new Exception("binary load cancellation ignored");}catch(OperationCanceledException){}
            foreach(var bad in new[]{valid[..^3],valid.Concat(new byte[]{1}).ToArray(),Document(AStarTestMap.MaximumNodes+1),Document(2,w=>
            {
                w.Write(0f);w.Write(0f);w.Write(0f);w.Write(1f);w.Write(0f);w.Write(0f);
                w.Write(0);w.Write(2);w.Write(1);w.Write(1); // non-monotone offsets
            }),Document(2,w=>
            {
                w.Write(0f);w.Write(0f);w.Write(0f);w.Write(1f);w.Write(0f);w.Write(0f);
                w.Write(0);w.Write(1);w.Write(1);w.Write(9); // invalid edge target
            })})
            {
                Save(bad);
                try{await AStarTestMapFile.LoadAsync(path,default);throw new Exception("malformed binary graph accepted");}
                catch(InvalidDataException){}catch(EndOfStreamException){}
            }
        }
        finally{File.Delete(path);}
    }

    public static async Task WalkingAsync()
    {
        var request=new AStarWalkTestRequest(7,"test",new[]{new Vector3Snapshot(0,0,0),new(0,1,0),new(1,1,0)});
        var move=new Movement();
        var result=await new AStarWalkTest().RunAsync(request,move,null,default);
        Check(result.Success&&move.Targets.SequenceEqual(request.Points.Skip(1))&&move.Stops>=3,"ordered arrival and release");
        foreach(var bad in new[]{Scene(new(0,0,0),8),Scene(new(0,0,6)),Scene(new(0,0,0)) with {InWorld=false},Scene(new(0,0,0)) with {Player=Player(new()) with {CurrentHp=0}}})
        {
            move=new Movement{SceneOverride=()=>bad};
            result=await new AStarWalkTest().RunAsync(request,move,null,default);
            Check(!result.Success&&move.Targets.Count==0&&move.Stops>0,"scene/floor/life guard before input");
        }
        move=new Movement();move.AfterMove=()=>move.Position=new(10,10,0);
        result=await new AStarWalkTest().RunAsync(request,move,null,default);
        Check(!result.Success&&result.Error!.Contains("偏离")&&move.Stops>0,"off-route guard");
        var clock=new Clock();move=new Movement{NoMovement=true};move.AfterMove=()=>clock.Advance(11);
        result=await new AStarWalkTest(clock).RunAsync(request,move,null,default);
        Check(!result.Success&&result.Error!.Contains("10 秒"),"no-progress timeout");
        using var stop=new CancellationTokenSource();move=new Movement();move.AfterMove=stop.Cancel;
        result=await new AStarWalkTest().RunAsync(request,move,null,stop.Token);
        Check(!result.Success&&move.Stops>0,"cancellation releases movement");
        move=new Movement{AfterMove=()=>throw new InvalidOperationException("test failure")};
        try {await new AStarWalkTest().RunAsync(request,move,null,default);throw new Exception("missing failure");}
        catch(InvalidOperationException e) when(e.Message=="test failure"){}
        Check(move.Stops>0,"exception releases movement");
    }

    public static async Task RpcAsync()
    {
        var fake=DispatchProxy.Create<IRoadhogRuntime, TestRuntime>();var spy=(TestRuntime)(object)fake;
        var dispatch=new RuntimeRpcDispatcher(fake,"test","fake");
        var request=new AStarWalkTestRequest(7,"sample",new[]{new Vector3Snapshot(1,2,3)});
        try {await dispatch.InvokeAsync(nameof(IRoadhogRuntime.RunAStarWalkTestAsync),new[]{JsonSerializer.SerializeToElement("other"),JsonSerializer.SerializeToElement(request)},new Progress<string>(),default);throw new Exception("cross-account call accepted");}
        catch(InvalidOperationException e){Check(e.Message.Contains("another account"),e.Message);}
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var pipe="roadhog-astar-test-"+Guid.NewGuid().ToString("N");
        var serving=new WorkerRpcServer(pipe,"token",dispatch.InvokeAsync).RunAsync(lifetime.Token);
        try
        {
            using var stop=new CancellationTokenSource();
            var proxy=new RemoteRoadhogRuntime(new WorkerRpcClient(pipe,"token"),"test");
            var running=proxy.RunAStarWalkTestAsync("test",request,null,stop.Token);
            await spy.Started.Task.WaitAsync(lifetime.Token);
            Check(spy.Request!.MapId==7&&spy.Request.Points[0]==request.Points[0],"route DTO survives account RPC");
            stop.Cancel();
            try {await running;}catch(OperationCanceledException){}
            await spy.Ended.Task.WaitAsync(lifetime.Token);
            Check(!spy.Active,"RPC stop reaches worker");
        }
        finally {lifetime.Cancel();await serving;}
    }

    public static async Task MovementAdapterAsync()
    {
        var input=new RecordingKeyboardInput();var api=new FakeGameApi{Player=Player(new()) with { CameraYawDegrees=180,CameraPitchDegrees=10,ActorYawDegrees=180 },Channel=new(0,1,7,DateTimeOffset.Now)};
        var logger=new InMemoryRoadhogLogger();
        var config=new AccountConfig{AccountName="test"};
        var controller=new Roadhog.Application.StationaryCombat.StationaryCombatController(input,new Roadhog.Application.SemiAuto.SemiAutoCombatController(input));
        var runtime=new RoadhogRuntime(api,logger,new AccountRuntimeManager(logger),null!,new InMemoryAccountConfigStore(config),keyboardInput:input,stationaryCombatController:controller);
        var request=new AStarWalkTestRequest(7,"test",new[]{new Vector3Snapshot(),new(0,1,0)});
        using var stop=new CancellationTokenSource();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var down=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        input.AfterKeyDown=k=>{if(k=="W")down.TrySetResult();};
        var running=runtime.RunAStarWalkTestAsync("test",request,null,stop.Token);
        try
        {
            await down.Task.WaitAsync(deadline.Token);
            var concurrent=await runtime.RunAStarWalkTestAsync("test",request,null,deadline.Token);
            Check(!concurrent.Success&&concurrent.Error!.Contains("另一个"),"runtime rejects overlapping manual input");
            stop.Cancel();await running.WaitAsync(deadline.Token);
            Check(input.KeyUps.Contains("W")&&input.MouseCommands.Contains("up:Right"),"real controller adapter releases W and right mouse on cancellation");
            api.Player=api.Player with {Position=new(0,0,0)};input.AfterKeyDown=k=>{if(k=="W")api.Player=api.Player with {Position=new(0,1,0)};};
            var complete=await runtime.RunAStarWalkTestAsync("test",request,null,deadline.Token);
            Check(complete.Success,"gate released and real movement controller can complete next test");
            // Simulate publication latency: the next position is only visible after W is released.
            // Continuous hold used to overshoot dense nodes and could not finish this movement.
            api.Player=api.Player with {Position=new(0,0,0)};
            bool pressed=false;var pulseWatch=new System.Diagnostics.Stopwatch();var pulses=new List<double>();
            input.AfterKeyDown=k=>{if(k=="W"){pressed=true;pulseWatch.Restart();}};
            input.AfterKeyUp=k=>
            {
                if(k!="W"||!pressed)return;
                pressed=false;pulses.Add(pulseWatch.Elapsed.TotalMilliseconds);
                api.Player=api.Player with {Position=new(0,api.Player.Position!.Value.Y+.2f,0)};
            };
            using var pulseDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
            complete=await runtime.RunAStarWalkTestAsync("test",request,null,pulseDeadline.Token);
            Check(complete.Success&&pulses.Count>=3&&pulses.All(ms=>ms<1000),"dense-node movement releases W before awaiting another published position");
            Check(!pressed,"no forward input remains held after short-pulse arrival");
            input.AfterKeyUp=null;
        }
        finally {stop.Cancel();try{await running.WaitAsync(TimeSpan.FromSeconds(3));}catch(OperationCanceledException){}}
    }

    public static Task UiAsync()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                var api=new FakeGameApi();var logger=new InMemoryRoadhogLogger();
                var store=new InMemoryAccountConfigStore(new AccountConfig{AccountName="test"});
                var fake=DispatchProxy.Create<IRoadhogRuntime,TestRuntime>();var spy=(TestRuntime)(object)fake;
                spy.Inner=new RoadhogRuntime(api,logger,new AccountRuntimeManager(logger),null!,store);
                using var form=new AccountSettingsForm("test",fake,store,new InMemorySharedPathStore(),new InMemoryScriptProfileStore(),new RecordingFolderLauncher(),"test-paths");
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new(-20000,-20000);form.Show();
                var tabs=(TabControl)typeof(AccountSettingsForm).GetField("settingsTabs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
                Check(tabs.TabPages[^1].Text=="A*测试"&&tabs.TabPages[^2].Text=="其他","test tab follows other");
                tabs.SelectedIndex=tabs.TabCount-1;Application.DoEvents();
                void CaptureScreenshot(string path)
                {
                    // Drawing every hidden tab creates thousands of unrelated control handles.
                    var page=tabs.SelectedTab!;
                    using var bitmap=new System.Drawing.Bitmap(page.Width,page.Height);
                    page.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,page.Width,page.Height));bitmap.Save(path);
                }
                Task Invoke(string method,params object[] args)=>(Task)typeof(AccountSettingsForm).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,args)!;
                T Find<T>(string name) where T:Control=>(T)form.Controls.Find(name,true).Single();
                void Pump(Task task)
                {
                    var until=DateTime.UtcNow.AddSeconds(30);
                    while(!task.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}
                    Check(task.IsCompleted,"UI task timed out");task.GetAwaiter().GetResult();Application.DoEvents();
                }
                Pump(Invoke("LoadAStarMapAsync",AStarTestMapFile.SamplePath,true));
                Check(Find<Button>("aStarWalk").Enabled&&Find<DataGridView>("aStarRoute").Rows.Count==23,"sample loads and computes in real UI");
                // Optional integration fixture generated locally from the user's client, never a CI dependency.
                if(Environment.GetEnvironmentVariable("ROADHOG_ASTAR_FULL_MAP") is {Length:>0} fullMap)
                {
                    Pump(Invoke("LoadAStarMapAsync",fullMap,false));
                    Find<TextBox>("aStarStartX").Text="2048";Find<TextBox>("aStarStartY").Text="16";Find<TextBox>("aStarStartZ").Text="301.12";
                    Find<TextBox>("aStarGoalX").Text="218";Find<TextBox>("aStarGoalY").Text="2900";Find<TextBox>("aStarGoalZ").Text="277.37";
                    Pump(Invoke("PlanAStarAsync"));
                    Check(Find<Button>("aStarWalk").Enabled&&Find<DataGridView>("aStarRoute").Rows.Count==4903,"full DF2A map loads and plans in real UI");
                    if(Environment.GetEnvironmentVariable("ROADHOG_ASTAR_FULL_SCREENSHOT") is {Length:>0} fullScreenshot)
                    {
                        CaptureScreenshot(fullScreenshot);
                    }
                    Pump(Invoke("LoadAStarMapAsync",AStarTestMapFile.SamplePath,true));
                }
                if(Environment.GetEnvironmentVariable("ROADHOG_ASTAR_SCREENSHOT") is {Length:>0} path)
                {
                    CaptureScreenshot(path);
                }
                Find<TextBox>("aStarGoalX").Text="575";
                Check(Find<Button>("aStarWalk").Enabled&&Find<DataGridView>("aStarRoute").Rows.Count==0,"editing endpoint invalidates preview while start can replan");
                Pump(Invoke("PlanAStarAsync"));
                var destination=new Vector3Snapshot(576,563,1022.02f);
                var current=destination;
                spy.ReadScene=_=>Task.FromResult(Scene(current,300100000));
                Pump(Invoke("ReadAStarPositionAsync",false));
                Check(Find<TextBox>("aStarGoalX").Text=="576.000"&&Find<Button>("aStarWalk").Enabled,"record current position as destination without planning");
                current=new(557,558,1022.11f);
                Find<TextBox>("aStarStartX").Text="0"; // stale preview origin must not be used by Start.
                var walking=Invoke("WalkAStarAsync");Pump(spy.Started.Task);
                Check(AStarTestMap.Distance(spy.Request!.Points[0],current)<.01&&
                    AStarTestMap.Distance(spy.Request.Points[^1],destination)<.01,"start reads moved character position and keeps recorded destination");
                Check(Find<TextBox>("aStarGoalX").Text=="576.000"&&Find<TextBox>("aStarStartX").Text=="557.000","current start displayed and destination preserved");
                Check(!tabs.TabPages[0].Enabled&&Find<Button>("aStarStop").Enabled,"other pages locked while stop remains enabled");
                Find<Button>("aStarStop").PerformClick();Pump(walking);
                Check(!spy.Active&&tabs.TabPages[0].Enabled,"stop restores UI and cancels walk");
                var nextStart=spy.Request.Points[1];var calls=spy.WalkCalls;
                current=new(9999,9999,1022);
                Pump(Invoke("WalkAStarAsync"));
                Check(spy.WalkCalls==calls&&Find<TextBox>("aStarLog").Text.Contains("覆盖范围"),"unmapped random start sends no movement request");
                spy.ReadScene=_=>Task.FromResult(Scene(current,220050000));
                Pump(Invoke("WalkAStarAsync"));
                Check(spy.WalkCalls==calls&&Find<TextBox>("aStarLog").Text.Contains("不一致"),"wrong current map rejected before movement");
                var reading=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                spy.ReadScene=async ct=>{reading.SetResult();await Task.Delay(Timeout.Infinite,ct);return Scene(current,300100000);};
                var cancelledRead=Invoke("WalkAStarAsync");Pump(reading.Task);
                Find<Button>("aStarStop").PerformClick();Pump(cancelledRead);
                Check(spy.WalkCalls==calls&&tabs.TabPages[0].Enabled,"stop during current-position read prevents movement and restores UI");
                current=nextStart;spy.ReadScene=_=>Task.FromResult(Scene(current,300100000));
                spy.Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
                var again=Invoke("WalkAStarAsync");Pump(spy.Started.Task);
                Check(AStarTestMap.Distance(spy.Request!.Points[0],nextStart)<.01&&
                    AStarTestMap.Distance(spy.Request.Points[^1],destination)<.01,"repeat start replans from another position to same destination");
                form.Close();Pump(again);
                Check(!spy.Active,"closing form cancels active test");
                done.SetResult();
            }
            catch(Exception e){Console.Error.WriteLine(e);done.SetException(e);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }

    private static PlayerSnapshot Player(Vector3Snapshot position)=>new(1,0,"test",100,100,100,100,0,position,DateTimeOffset.Now,0,0,0);
    private static ChannelTransitionSnapshot Scene(Vector3Snapshot position,uint map=7)=>new(true,Player(position),new(0,2,map,DateTimeOffset.Now),DateTimeOffset.Now);
    private sealed class Movement:IAStarTestMovement
    {
        public Vector3Snapshot Position;public int Stops;public bool NoMovement;public Action? AfterMove;public Func<ChannelTransitionSnapshot>? SceneOverride;
        public List<Vector3Snapshot> Targets=new();
        public Task<ChannelTransitionSnapshot> ReadSceneAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(SceneOverride?.Invoke()??Scene(Position));}
        public Task MoveStepAsync(PlayerSnapshot p,Vector3Snapshot target,double reach,CancellationToken token){Targets.Add(target);if(!NoMovement)Position=target;AfterMove?.Invoke();return Task.CompletedTask;}
        public Task StopAsync(){Stops++;return Task.CompletedTask;}
    }
    private sealed class Clock:TimeProvider
    {
        private long _ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>_ticks;public void Advance(double seconds)=>_ticks+=(long)(seconds*TimeSpan.TicksPerSecond);
    }
    public class TestRuntime:DispatchProxy
    {
        public IRoadhogRuntime? Inner;public AStarWalkTestRequest? Request;public bool Active;
        public Func<CancellationToken,Task<ChannelTransitionSnapshot>>? ReadScene;public int WalkCalls;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Ended=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name==nameof(IRoadhogRuntime.RunAStarWalkTestAsync))
            {WalkCalls++;Request=(AStarWalkTestRequest)args![1]!;return Run((CancellationToken)args[^1]!);}
            if(method.Name==nameof(IRoadhogRuntime.ReadSceneForPathRecordingAsync)&&ReadScene!=null)
                return ReadScene((CancellationToken)args![^1]!);
            return method.Invoke(Inner,args);
        }
        private async Task<OperationResult> Run(CancellationToken token)
        {Active=true;Started.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);return OperationResult.Ok();}finally{Active=false;Ended.TrySetResult();}}
    }
}
