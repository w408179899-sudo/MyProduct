using System.Text.Json;
using Roadhog.Application.EquipmentUpgrade;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog;
using Roadhog.Application;
using Roadhog.Infrastructure.WorkerProcesses;
using System.Reflection;
using System.Windows.Forms;

internal static class EquipmentUpgradeTests
{
    public static Task UiAndRpcAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Simulation(EquipmentUpgradeKind.Manastone);
                game.Items.Add(new(14,166000060,"level 60",5,3,false,0,0,Array.Empty<ushort>(),false,false,60));
                game.Items.Add(new(15,166000070,"level 70",5,4,false,0,0,Array.Empty<ushort>(),false,false,70));
                var settings = new ScriptSettings { EquipmentUpgrade = new() { ManastoneTargets = new() { new(12,113100650,"same name") { ManastoneId = 167000359 } },
                    EnchantTargets = new() { new(11,113100650,"same name") { EnchantStoneLevels=new(){60} }, new(12,113100650,"same name") { EnchantStoneLevels=new(){70} } } } };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName="test",ScriptSettings=settings });
                var logger = new InMemoryRoadhogLogger();
                var runtime = new RoadhogRuntime(game.Api, logger, new AccountRuntimeManager(logger), null!, store);
                using var form = new AccountSettingsForm("test",runtime,store,new InMemorySharedPathStore(),
                    new InMemoryScriptProfileStore(),new RecordingFolderLauncher(),"test-paths");
                object? Call(string method) => typeof(AccountSettingsForm).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null);
                form.Show();
                var tabs=(TabControl)typeof(AccountSettingsForm).GetField("settingsTabs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
                tabs.SelectedIndex=tabs.TabCount-1;Application.DoEvents();
                Require(tabs.SelectedTab!.Text=="其他","other tab follows team");
                var starts=(List<Button>)typeof(AccountSettingsForm).GetField("_equipmentUpgradeStarts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
                Require(starts.Count==1&&starts[0].Text=="一键执行","one start button submits both configured groups");
                var task=(Task)Call("RefreshEquipmentUpgradeListsAsync")!;
                var until=DateTime.UtcNow.AddSeconds(10);
                while(!task.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}
                task.GetAwaiter().GetResult();Application.DoEvents();
                var editors=(System.Collections.IDictionary)typeof(AccountSettingsForm).GetField("_equipmentUpgradeEditors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
                var editor=editors[EquipmentUpgradeKind.Enchant]!;
                var equipment=(CheckedListBox)editor.GetType().GetProperty("Equipment")!.GetValue(editor)!;
                var levels=(CheckedListBox)editor.GetType().GetProperty("Levels")!.GetValue(editor)!;
                Require(levels.GetItemChecked(0)&&!levels.GetItemChecked(1),"first equipment restores level 60 only");
                levels.SetItemChecked(1,true); equipment.SelectedIndex=1;
                Require(!levels.GetItemChecked(0)&&levels.GetItemChecked(1),"second equipment keeps independent level 70");
                equipment.SelectedIndex=0;
                var captured=(ScriptSettings)Call("CaptureScriptSettings")!;
                Require(captured.EquipmentUpgrade.EnchantTargets[0].EnchantStoneLevels.SequenceEqual(new[]{60,70})&&
                    captured.EquipmentUpgrade.EnchantTargets[1].EnchantStoneLevels.SequenceEqual(new[]{70}),"switching equipment saves each level selection separately");
                Require(captured.EquipmentUpgrade.ManastoneTargets.Single().InstanceId==12,"refresh and save retain the selected same-name instance");
                Require(captured.EquipmentUpgrade.ManastoneTargets.Single().ManastoneId==167000359,"refresh retains allowed material");
                if(Environment.GetEnvironmentVariable("ROADHOG_EQUIPMENT_SCREENSHOT") is {Length:>0} path)
                {
                    using var bitmap=new System.Drawing.Bitmap(form.Width,form.Height);
                    form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(path);
                }
                var dispatcher=new RuntimeRpcDispatcher(runtime,"test","fake");
                foreach(var method in new[]{"RefreshEquipmentUpgradeAsync","RunEquipmentUpgradeAsync","RunEquipmentUpgradeBatchAsync"})
                {
                    var args=method.StartsWith("Refresh")?new object[]{"other"}:method.EndsWith("BatchAsync")?new object[]{"other",settings.EquipmentUpgrade}:new object[]{"other",settings.EquipmentUpgrade,EquipmentUpgradeKind.Manastone};
                    try {dispatcher.InvokeAsync(method,args.Select(a=>JsonSerializer.SerializeToElement(a)).ToArray(),new Progress<string>(),CancellationToken.None).GetAwaiter().GetResult();throw new Exception("cross-account call admitted");}
                    catch(InvalidOperationException ex){Require(ex.Message.Contains("another account"),ex.Message);}
                }
                form.Close();done.SetResult();
            }
            catch(Exception ex){done.SetException(ex);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }
    public static async Task LoopsAsync()
    {
        var game = new Simulation(EquipmentUpgradeKind.Manastone) { FailFirst = true };
        var result = await game.Run();
        Require(result.Success && result.Value!.Attempts == 4 && result.Value.Completed == 2, result.Error ?? "four attempts including failure");
        Require(game.Items.Where(i => i.CanSocket).All(i => i.UsedSockets == 2), "both same-name instances filled after failure clears the first one");
        Require(game.Waits == 4 && game.Confirmed == 8 && game.Items.Single(i => i.IsManastone).Count == 6, "two confirms and six seconds per attempt");
        var enchant = new Simulation(EquipmentUpgradeKind.Enchant);
        result = await enchant.Run();
        Require(result.Success && result.Value!.Attempts == 2 && result.Value.Completed == 2, "each selected +9 reaches +10 exactly once");
        Require(enchant.Items.Where(i => i.CanEnchant).All(i => i.EnchantLevel == 10), "never exceed ten");
        var exhausted = new Simulation(EquipmentUpgradeKind.Manastone);
        exhausted.Items[2] = exhausted.Items[2] with { Count = 1 };
        result = await exhausted.Run();
        Require(result.Success && result.Value!.Attempts == 1 && result.Value.Completed == 1, "material exhausted stops without using other types");
    }

    public static async Task PerEquipmentMaterialsAsync()
    {
        foreach (var kind in Enum.GetValues<EquipmentUpgradeKind>())
        {
            var game = new Simulation(kind);
            game.Items.Add(game.Items[2] with { InstanceId=14,TemplateId=kind==EquipmentUpgradeKind.Enchant?166000070u:167000360u,Slot=3,EnchantStoneLevel=70 });
            var targets=kind==EquipmentUpgradeKind.Enchant?game.Settings.EnchantTargets:game.Settings.ManastoneTargets;
            targets[0].EnchantStoneLevels=new(){70}; targets[0].ManastoneId=167000360;
            var result=await game.Run();
            Require(result.Success && game.Used.SequenceEqual(new[]{(11u,14u),(12u,13u)}), "each equipment uses only its own selected material");
            game=new Simulation(kind);
            targets=kind==EquipmentUpgradeKind.Enchant?game.Settings.EnchantTargets:game.Settings.ManastoneTargets;
            targets[0].EnchantStoneLevels=new(){70};targets[0].ManastoneId=167000360;
            result=await game.Run();
            Require(result.Success && result.Value!.Completed==1 && game.Used.SequenceEqual(new[]{(12u,13u)}), "missing first material skips to next equipment without substitution");
            game=new Simulation(kind);
            targets=kind==EquipmentUpgradeKind.Enchant?game.Settings.EnchantTargets:game.Settings.ManastoneTargets;
            targets[1].EnchantStoneLevels.Clear();targets[1].ManastoneId=0;
            Require(!(await game.Run()).Success && game.Input.MouseCommands.Count==0, "validate every selected equipment before input");
        }
    }

    public static async Task BatchAsync()
    {
        var game=new Simulation(EquipmentUpgradeKind.Manastone);
        game.Items.Add(new(14,166000060,"enchant stone",5,3,false,0,0,Array.Empty<ushort>(),false,false,60));
        using var stop=new CancellationTokenSource();
        var original=game.Input.AfterPress;
        game.Input.AfterPress=key=>{original?.Invoke(key);if(game.Input.Keys.Count(k=>k=="Space")==3)stop.Cancel();};
        var intervals=0;
        var logger=new InMemoryRoadhogLogger();
        var result=await new EquipmentUpgradeBatch(game.Input,logger,(ms,ct)=>{
            ct.ThrowIfCancellationRequested();
            if(ms==1500) {
                intervals++;
                Require(game.Used.SequenceEqual(new[]{(11u,14u),(12u,14u),(11u,13u),(12u,13u)}),"all enchant then all socket actions precede first jump");
            }
            return Task.CompletedTask;
        }).RunAsync(game.Api.Create(new AccountConfig(),logger,stop.Token),"test",game.Settings,null,stop.Token);
        Require(result.Success&&result.Value!.Attempts==4&&intervals==3&&game.Input.Keys.Count(k=>k=="Space")==3,"batch loops at 1500ms until cancellation");
        Require(game.Input.KeyUps.Last()=="Space","stop releases space");
        foreach(var mode in new[]{"empty","invalid","shortage","cancel","foreign"}) {
            game=new Simulation(EquipmentUpgradeKind.Manastone);
            using var cancel=new CancellationTokenSource();
            if(mode=="empty") {game.Settings.EnchantTargets.Clear();game.Settings.ManastoneTargets.Clear();}
            else if(mode=="invalid")game.Settings.ManastoneTargets[0].ManastoneId=0;
            else {game.Settings.EnchantTargets=new();if(mode=="shortage")game.Items[2]=game.Items[2] with {Count=1};}
            if(mode=="foreign")game.WrongDialog=true;
            result=await new EquipmentUpgradeBatch(game.Input,logger,(ms,ct)=>{
                if(mode=="cancel"&&ms==6000)cancel.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;
            }).RunAsync(game.Api.Create(new AccountConfig(),logger,cancel.Token),"test",game.Settings,null,cancel.Token);
            Require(!game.Input.Keys.Contains("Space"),mode+" cannot enter completion space loop");
            if(mode is "empty" or "invalid")Require(game.Input.MouseCommands.Count==0,"validate whole batch before inputs");
        }
    }

    public static async Task BatchRpcStopAsync()
    {
        var game=new Simulation(EquipmentUpgradeKind.Manastone);
        game.Settings.EnchantTargets=new();
        for(var i=0;i<2;i++)game.Items[i]=game.Items[i] with {Manastones=new ushort[]{104,104}};
        var logger=new InMemoryRoadhogLogger();
        var store=new InMemoryAccountConfigStore(new AccountConfig {AccountName="test"});
        var runtime=new RoadhogRuntime(game.Api,logger,new AccountRuntimeManager(logger),null!,store,keyboardInput:game.Input);
        var dispatcher=new RuntimeRpcDispatcher(runtime,"test","fake");
        var pipe="equipment-batch-"+Guid.NewGuid().ToString("N");
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=new WorkerRpcServer(pipe,"token",async(method,args,progress,token)=>{
            try{return await dispatcher.InvokeAsync(method,args,progress,token);}finally{ended.TrySetResult();}
        });
        var serving=server.RunAsync(lifetime.Token);
        try {
            var remote=new RemoteRoadhogRuntime(new WorkerRpcClient(pipe,"token"),"test");
            var ticks=new System.Collections.Concurrent.ConcurrentQueue<long>();
            var two=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original=game.Input.AfterPress;
            game.Input.AfterPress=key=>{original?.Invoke(key);if(key=="Space"){ticks.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());if(ticks.Count==2)two.TrySetResult();}};
            using var stop=new CancellationTokenSource();
            var running=remote.RunEquipmentUpgradeBatchAsync("test",game.Settings,null,stop.Token);
            await two.Task.WaitAsync(lifetime.Token);
            Require(!running.IsCompleted,"RPC remains active after equipment finishes");
            var times=ticks.ToArray();var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(times[0],times[1]);
            Require(elapsed.TotalMilliseconds>=1300&&elapsed.TotalMilliseconds<3500,"real timer schedules one space every 1.5 seconds");
            stop.Cancel();
            try {await running;}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
            await ended.Task.WaitAsync(lifetime.Token);
            Require(game.Input.KeyUps.Last()=="Space"&&ticks.Count==2,"UI cancellation propagates across RPC and ends input");
            var empty=await runtime.RunEquipmentUpgradeBatchAsync("test",new(),null,lifetime.Token);
            Require(!empty.Success&&empty.Error!.Contains("勾选"),"manual input gate released after stop: "+empty.Error);
        }
        finally {lifetime.Cancel();await serving;}
    }

    public static async Task PriorityAsync()
    {
        foreach(var scenario in new[]{"base","rebalance","failure","mixed","unknown"})
        {
            var game=new Simulation(EquipmentUpgradeKind.Enchant);
            game.Settings.ManastoneTargets=new();
            uint[] expected;
            switch(scenario)
            {
                case "base":
                    game.Items[0]=game.Items[0] with {EquipmentLevel=40,EnchantLevel=8};
                    expected=new uint[]{12,11,11}; break;
                case "rebalance":
                    for(var i=0;i<2;i++)game.Items[i]=game.Items[i] with {EnchantLevel=8};
                    // Reverse saved configuration too: neither checkbox order nor bag traversal may override priority.
                    game.Settings.EnchantTargets.Reverse();
                    expected=new uint[]{11,12,11,12};break;
                case "failure":
                    game.FailFirst=true;game.Items[1]=game.Items[1] with {EnchantLevel=8};
                    expected=new uint[]{12,12,12,11,12};break;
                case "mixed":
                    game.Items[0]=game.Items[0] with {EquipmentLevel=40};
                    game.Items[1]=game.Items[1] with {EquipmentLevel=30,EnchantLevel=10};
                    game.Settings.ManastoneTargets.Add(game.Settings.EnchantTargets[1].Copy());
                    game.Items.Add(new(14,167000359,"mana",10,3,false,0,0,Array.Empty<ushort>(),false,false));
                    expected=new uint[]{12,11};break;
                default:
                    game.Items[0]=game.Items[0] with {EquipmentLevel=0};expected=Array.Empty<uint>();break;
            }
            using var stop=new CancellationTokenSource();var logger=new InMemoryRoadhogLogger();
            var result=await new EquipmentUpgradeBatch(game.Input,logger,(ms,ct)=>{
                if(ms==1500)stop.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;
            }).RunAsync(game.Api.Create(new AccountConfig(),logger,stop.Token),"test",game.Settings,null,stop.Token);
            Require(game.Used.Select(x=>x.Target).SequenceEqual(expected),scenario+" respects live base-level/enchant-level priority: "+string.Join(",",game.Used.Select(x=>x.Target)));
            Require(result.Success==(scenario!="unknown"),scenario+" completion result");
            if(scenario=="unknown")Require(game.Input.MouseCommands.Count==0,"missing level never sorts as lowest or starts input");
        }
    }

    public static async Task GuardsAsync()
    {
        foreach (var mode in new[] { "foreign", "hover", "equipped", "missing" })
        {
            var game = new Simulation(EquipmentUpgradeKind.Manastone) { WrongDialog = mode == "foreign", WrongHover = mode == "hover" };
            if (mode == "equipped") game.Items[0] = game.Items[0] with { IsEquipped = true };
            if (mode == "missing") game.Items.RemoveAt(0);
            Require(!(await game.Run()).Success && game.Confirmed == 0, mode + " prevents confirmation");
        }
        var cancel = new Simulation(EquipmentUpgradeKind.Manastone);
        using var stop = new CancellationTokenSource();
        cancel.OnCast = stop.Cancel;
        Require(!(await cancel.Run(stop.Token)).Success && cancel.Input.MouseCommands.Last() == "up:Right", "cancel releases input");
        var settings = new ScriptSettings { EquipmentUpgrade = new() { ManastoneTargets = new() { new(12, 113100650, "same name") { ManastoneId = 167000359, EnchantStoneLevels = new() { 60, 70 } } } } };
        var copy = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(settings))!.Clone();
        settings.EquipmentUpgrade.ManastoneTargets[0].EnchantStoneLevels.Clear();
        Require(copy.EquipmentUpgrade.ManastoneTargets.Single().ManastoneId == 167000359 && copy.EquipmentUpgrade.ManastoneTargets.Single().EnchantStoneLevels.Count == 2 && copy.EquipmentUpgrade.ManastoneTargets.Single().InstanceId == 12, "JSON and clone retain identity independently");
    }

    private sealed class Simulation
    {
        public readonly FakeGameApi Api = new();
        public readonly RecordingKeyboardInput Input = new();
        public readonly List<EquipmentUpgradeItem> Items;
        public bool FailFirst, WrongDialog, WrongHover;
        public int Confirmed, Waits;
        public Action? OnCast;
        private bool _open, _selected, _down;
        private EquipmentUpgradeDialog? _dialog;
        private uint _target, _material;
        public readonly List<(uint Target, uint Material)> Used = new();
        private readonly EquipmentUpgradeKind _kind;
        private EquipmentUpgradeKind _activeKind;
        public readonly EquipmentUpgradeSettings Settings;
        public Simulation(EquipmentUpgradeKind kind)
        {
            _kind = kind;
            Items = new() {
                new(11,113100650,"same name",1,0,false,9,2,new ushort[]{104,0},true,true,EquipmentLevel:30),
                new(12,113100650,"same name",1,1,false,9,2,new ushort[]{104,0},true,true,EquipmentLevel:30),
                new(13,kind==EquipmentUpgradeKind.Manastone?167000359u:166000001u,"stone",10,2,false,0,0,Array.Empty<ushort>(),false,false,60) };
            var targets = Items.Take(2).Select(i => new EquipmentUpgradeTarget(i.InstanceId,i.TemplateId,i.Name) { ManastoneId=167000359, EnchantStoneLevels=new(){60} }).ToList();
            Settings = new() { EnchantTargets=targets,ManastoneTargets=targets };
            Api.EquipmentInventoryRead = () => new(Items.ToArray());
            Api.EquipmentUiRead = () => {
                var visible = Items.Select(i => new InventoryUiItem(i.InstanceId,i.TemplateId,i.Count,new(100+i.Slot*50,100))).ToArray();
                return new(_open,visible,WrongHover?999:visible.FirstOrDefault(i=>i.Point==Api.InventoryUiCursor)?.InstanceId??0,_dialog,false,false);
            };
            Input.AfterPress = _ => _open=true;
            Input.AfterMove = (x,y) => Api.InventoryUiCursor=new(Api.InventoryUiCursor.X+x,Api.InventoryUiCursor.Y+y);
            Input.AfterMouseDown = _ => _down=true;
            Input.AfterMouseUp = button => {
                if (!_down) return; _down=false;
                if (button==RoadhogMouseButton.Right) { _selected=true; _material=Items.Single(i=>new GameUiPoint(100+i.Slot*50,100)==Api.InventoryUiCursor).InstanceId; _activeKind=Items.Single(i=>i.InstanceId==_material).IsEnchantStone?EquipmentUpgradeKind.Enchant:EquipmentUpgradeKind.Manastone; return; }
                if (_dialog!=null) {
                    Confirmed++;
                    if (!_dialog.FinalConfirmation) _dialog=_dialog with { DialogId=337,FinalConfirmation=true,ConfirmButton=new(320,300) };
                    else {
                        _dialog=null; Used.Add((_target,_material));
                        var index=Items.FindIndex(i=>i.InstanceId==_target);var item=Items[index];
                        if (_activeKind==EquipmentUpgradeKind.Enchant) Items[index]=item with { EnchantLevel=(byte)(FailFirst&&Confirmed==2?item.EnchantLevel-1:item.EnchantLevel+1) };
                        else {
                            var stones=item.Manastones.ToArray();
                            if(FailFirst&&Confirmed==2) Array.Clear(stones);else stones[Array.FindIndex(stones,s=>s==0)]=104;
                            Items[index]=item with {Manastones=stones};
                        }
                        var material=Items.FindIndex(i=>i.InstanceId==_material);
                        if(Items[material].Count==1)Items.RemoveAt(material);else Items[material]=Items[material] with {Count=Items[material].Count-1};
                    }
                } else if (_selected) {
                    _selected=false;_target=Items.Single(i=>new GameUiPoint(100+i.Slot*50,100)==Api.InventoryUiCursor).InstanceId;
                    _dialog=new(250,WrongDialog?999:_target,_material,_activeKind,new(300,300),null,false);
                }
            };
        }
        public Task<Roadhog.Core.Common.OperationResult<EquipmentUpgradeResult>> Run(CancellationToken token=default) =>
            new EquipmentUpgradeSequence(Input,new InMemoryRoadhogLogger(),(ms,ct)=> {
                if(ms==6000){Waits++;OnCast?.Invoke();}ct.ThrowIfCancellationRequested();return Task.CompletedTask;
            }).RunAsync(Api.Create(new AccountConfig(),new InMemoryRoadhogLogger(),token),"test",Settings,_kind,null,token);
    }
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
}
