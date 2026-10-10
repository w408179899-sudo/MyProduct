using System.Reflection;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    public static async Task PathsAndPreflightAsync()
    {
        var api = new FakeGameApi {InventoryMoney=0,TargetName="warehouse",TargetOwnServerObjectId=7,TargetIsTargetingLocalPlayer=false};
        api.Player = api.Player with { Position = new(500, 0, 0) };
        var input = new RecordingKeyboardInput(); Cursor(api,input);
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var auction = Auction();var shop = Shop();
        var bagOpen = false;
        var scrollClicks = 0;
        InventoryUiItem[] BagItems() => api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId,
            i.TemplateId, i.Count, new(500 + i.Slot * 30, 300))).ToArray();
        api.InventoryInteractionRead = () => new(bagOpen, false, false, BagItems(),
            BagItems().FirstOrDefault(i => i.Point == api.InventoryUiCursor)?.InstanceId ?? 0, 0, null, new(500, 380), false);
        api.AuctionRead=()=>auction;api.PersonalShopRead=()=>shop;
        input.AfterPress=key=>
        {
            if(key=="Space"){auction=auction with{IsOpen=false};shop=shop with{Purchase=ShopPurchaseSnapshot.Closed};}
            else if(key=="C")shop=shop with{Purchase=new(true,7,new[]{new ShopPurchaseItem(8,20,1,30,new(500,300))},Array.Empty<ShopPurchaseItem>(),0,null,new(700,300))};
            else if(key=="I")bagOpen=!bagOpen;
            else if(key=="F5"){api.Player=api.Player with{Position=new(0,0,0)};api.Channel=api.Channel with{MapId=1};}
            else if(key=="F6"){api.Player=api.Player with{Position=new(1000,0,0)};api.Channel=api.Channel with{MapId=1};}
            else throw new Exception("unexpected route key "+key);
        };
        bool down=false;input.AfterMouseDown=_=>down=true;
        input.AfterMouseUp=button=>
        {
            if(!down)return;down=false;
            if (button == RoadhogMouseButton.Right)
            {
                var item = BagItems().Single(i => i.Point == api.InventoryUiCursor);
                Require(item.InstanceId == 12 && bagOpen, "only the configured warehouse scroll is used");
                scrollClicks++;
                api.InventoryItems = api.InventoryItems.Select(i => i.InstanceId == 12 ? i with { Count = i.Count - 1 } : i).Where(i => i.Count > 0).ToArray();
                api.Channel = api.Channel with { MapId = 2 }; api.Player = api.Player with { Position = new(100, 0, 0) };
                return;
            }
            if(api.InventoryUiCursor==new GameUiPoint(100,100))auction=auction with{ActiveTab=2};
            else if(api.InventoryUiCursor==new GameUiPoint(200,100))auction=auction with{ActiveTab=1};
            else throw new Exception("unexpected route click");
        };
        SharedPathDocument Path(string name,double start,double end)=>new(){Name=name,MapId=1,Points=new(){new(){X=start},new(){X=end}}};
        var warehousePath = Path("stall",100,170); warehousePath.MapId = 2;
        var paths=new InMemorySharedPathStore(Path("auction",0,30),warehousePath,Path("revive",1000,0));
        var config=new AccountConfig{AccountName="flow",ScriptSettings=new()};var settings=config.ScriptSettings;
        settings.Paths.AuctionPathName="auction";settings.Paths.StallPathName="stall";settings.Paths.RevivePathName="revive";settings.Paths.TownReturnKey="F6";settings.Paths.BagCleanupTownReturnKey="F5";
        settings.Paths.StallReturnItemName = "warehouse-scroll";
        settings.Maintenance.CleanupWorkflow=new(){NpcCleanup=false,Auction=true,TransferGold=true,PersonalShop=true,WarehouseName="warehouse",WarehouseSelectionKey="F7"};
        var reserved = new InventoryItemSnapshot(1, 11, "auction goods", 3, 0, false);
        var scroll = new InventoryItemSnapshot(2, 12, "warehouse-scroll", 2, 1, false, 18, UseGroup: 36);
        api.InventoryItems = new[] { reserved, scroll };
        settings.Maintenance.BagCleanupAuctionHouseItems.Add(new() { Name = "auction", UnitPrice = null });
        settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "goods", UnitPrice = 20 });
        settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "warehouse-scroll", UnitPrice = 20 });
        var context=new AccountWorkerContext(config,api,logger,new AccountRuntimeManager(logger),new(),stop.Token);
        var travel=new List<string>();
        Task<OperationResult> Follow(AccountWorkerContext c,string name,IReadOnlyList<Vector3Snapshot> points)
        {
            if (name == "stall") Require(scrollClicks > 0 && !bagOpen && api.Channel.MapId == 2 && api.Player.Position == new Vector3Snapshot(100,0,0),
                "warehouse route starts only after observed scroll landing and bag closure");
            travel.Add(name+":"+points[0].X+">"+points[^1].X);api.Player=api.Player with{Position=points[^1]};return Task.FromResult(OperationResult.Ok());
        }
        var runner=new CleanupWorkflowRunner(input,paths,Follow,new Journal(), groceryDelay: Fast);
        await runner.RunAsync(context,new(settings,true));
        Require(travel.SequenceEqual(new[]{"auction:0>30","auction:30>0","stall:100>170","revive:1000>0"}),"auction precedes scroll travel to the independent warehouse route");
        Require(input.Keys.Count(k=>k=="F5")==1&&input.Keys.Count(k=>k=="F6")==1&&api.InventoryMoney==0,"manual recall before auction and final recall after stall both verified");
        Require(api.InventoryItems.Single(i => i.InstanceId == 11) == reserved && api.InventoryItems.Single(i => i.InstanceId == 12).Count == 1 && !input.Keys.Contains("Y"),
            "full cleanup retains skipped auction items and unused scrolls without opening the configured stall");

        settings.Maintenance.CleanupWorkflow.Auction = false;
        settings.Paths.BagCleanupTownReturnKey = string.Empty;
        api.InventoryItems = new[] { reserved, scroll };
        api.Channel = api.Channel with { MapId = 1 }; api.Player = api.Player with { Position = new(500,0,0) };
        travel.Clear();
        await runner.RunAsync(context,new(settings,true));
        Require(travel.SequenceEqual(new[]{"stall:100>170","revive:1000>0"}) && scrollClicks == 2 && input.Keys.Count(k => k == "F5") == 1,
            "warehouse-only departure uses scroll directly without an initial cleanup return key");

        var keysBeforeInvalid = input.Keys.Count; var clicksBeforeInvalid = input.MouseCommands.Count;
        foreach (var invalid in new[] { "empty_selection", "missing_scroll", "missing_map" })
        {
            var badSettings = settings.Clone();
            if (invalid == "empty_selection") badSettings.Paths.StallReturnItemName = " ";
            if (invalid == "missing_scroll") badSettings.Paths.StallReturnItemName = "absent-scroll";
            var badPath = warehousePath.Clone(); if (invalid == "missing_map") badPath.MapId = null;
            var invalidRunner = new CleanupWorkflowRunner(input, new InMemorySharedPathStore(badPath,Path("revive",1000,0)), Follow, new Journal(), groceryDelay: Fast);
            try { await invalidRunner.RunAsync(context, new(badSettings,true)); throw new Exception("invalid warehouse departure accepted: " + invalid); }
            catch (InvalidOperationException) { }
            Require(input.Keys.Count == keysBeforeInvalid && input.MouseCommands.Count == clicksBeforeInvalid,
                "invalid warehouse scroll/map configuration executes no input: " + invalid);
        }
        settings.Paths.StallPathName="missing";var before=input.MouseCommands.Count;var keys=input.Keys.Count;
        try{await runner.RunAsync(context,new(settings,true));throw new Exception("missing later route accepted");}
        catch(InvalidOperationException){}
        Require(input.MouseCommands.Count==before&&input.Keys.Count==keys,"all later-stage paths validated before any inputs");
    }

    private sealed class CaptureLoop : IAccountWorkerLoop
    {
        public TaskCompletionSource<AccountWorkerContext> Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(AccountWorkerContext context)
        {
            Entered.TrySetResult(context);await Task.Delay(Timeout.Infinite,context.StopToken);
        }
    }
    public static async Task WorkerSessionAsync()
    {
        var api=new FakeGameApi();var logger=new InMemoryRoadhogLogger();var loop=new CaptureLoop();
        var host=new AccountWorkerHost(api,logger,new AccountRuntimeManager(logger),loop,new(){StopTimeout=TimeSpan.FromSeconds(2)});
        var config=new AccountConfig{AccountName="worker",ScriptSettings=new()};
        Require(host.Start(config,cleanupFirst:true).Success,"idle account starts cleanup before normal work");
        var first=await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(first.CleanupRequests.Current is{Manual:true}&&!host.RequestCleanup(config.ScriptSettings).Success,"pending startup request prevents duplicates");
        Require((await host.StopAsync()).Success&&first.StopToken.IsCancellationRequested,"Stop cancels the exact flow session");
        loop.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        Require(host.Start(config).Success,"worker restarts normally");var second=await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(second.CleanupRequests.Current==null,"canceled cleanup cannot revive on restart");
        Require(host.RequestCleanup(config.ScriptSettings).Success,"running worker accepts insertion without restart");
        Require(ReferenceEquals(second,await loop.Entered.Task)&&!second.StopToken.IsCancellationRequested,"request does not cancel current combat worker");
        await host.StopAsync();
    }

    public static Task SettingsUiAsync()
    {
        Exception? failure=null;var thread=new Thread(()=>
        {
            try
            {
                var config=new AccountConfig{AccountName="flow",ScriptSettings=new()};
                config.ScriptSettings.Paths.AuctionPathName="auction";config.ScriptSettings.Paths.StallPathName="stall";
                config.ScriptSettings.Maintenance.CleanupWorkflow=new(){Auction=true,TransferGold=true,PersonalShop=true,WarehouseName="仓库",WarehouseSelectionKey="F7",OldListingAction=AuctionOldListingAction.Reprice};
                var logger=new InMemoryRoadhogLogger();var store=new InMemoryAccountConfigStore(config);var api=new FakeGameApi();
                var runtime=new RoadhogRuntime(api,logger,new AccountRuntimeManager(logger),null!,store,keyboardInput:new RecordingKeyboardInput());
                using var form=new AccountSettingsForm("flow",runtime,store,new InMemorySharedPathStore(),new InMemoryScriptProfileStore(),new RecordingFolderLauncher(),"paths");
                object Field(string name)=>typeof(AccountSettingsForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
                var tabs=(System.Windows.Forms.TabControl)Field("settingsTabs");
                tabs.SelectedTab=tabs.TabPages.Cast<System.Windows.Forms.TabPage>().Single(t=>t.Text=="清包");
                form.ShowInTaskbar=false;form.StartPosition=System.Windows.Forms.FormStartPosition.Manual;form.Location=new(-32000,-32000);form.Show();System.Windows.Forms.Application.DoEvents();
                var settings=(ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null)!;
                Require(settings.Maintenance.CleanupWorkflow is{Auction:true,TransferGold:true,PersonalShop:true,WarehouseName:"仓库",WarehouseSelectionKey:"F7",OldListingAction:AuctionOldListingAction.Reprice},"UI round-trips all workflow controls");
                IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent) => parent.Controls.Cast<System.Windows.Forms.Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
                var controls = Descendants(form).ToArray();
                Require(controls.Any(c => c.Text == "拍卖行：全部撤单 → 按配置登录物品 → 计算领取金币"), "UI explains the fixed auction order");
                Require(!controls.OfType<System.Windows.Forms.ComboBox>().Any(c => c.Items.Cast<object>().Any(i => i.ToString() == "保持原挂售")), "obsolete old-listing choices no longer appear");
                Require(settings.Paths.AuctionPathName=="auction"&&settings.Paths.StallPathName=="stall","both path editors retain independent selection");
                var directory=System.IO.Path.Combine(Environment.CurrentDirectory,".tmp");Directory.CreateDirectory(directory);
                using var bitmap=new System.Drawing.Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new(System.Drawing.Point.Empty,form.Size));
                bitmap.Save(System.IO.Path.Combine(directory,"cleanup-workflow-ui.png"));form.Close();
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure!=null)throw failure;return Task.CompletedTask;
    }

    public static async Task ActualWorkerAndCombatAsync()
    {
        var api=new FakeGameApi{TargetEntityId=100,TargetOwnServerObjectId=100,TargetPosition=new(1,0,0)};
        var input=new RecordingKeyboardInput();var logger=new InMemoryRoadhogLogger();var runtime=new AccountRuntimeManager(logger);
        var config=new AccountConfig{AccountName="actual-worker",ScriptSettings=new()};
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var context=new AccountWorkerContext(config,api,logger,runtime,new(){TickInterval=TimeSpan.FromMilliseconds(1)},stop.Token);
        var semi=new SemiAutoCombatController(input);var combat=new StationaryCombatController(input,semi);
        var plan=SemiAutoSkillPlan.FromSettings(config.ScriptSettings.Skills);var semiState=new SemiAutoCombatState();
        var state=new StationaryCombatState{Fighting=true,CurrentTargetEntityId=100,CurrentTargetServerObjectId=100};
        Require(!await combat.PrepareCleanupTickAsync(context,plan,semiState,state),"existing living fight must finish before cleanup");
        Require(!input.Keys.Contains("Tab")&&!input.Keys.Contains("F8"),"no unrelated acquisition during graceful drain");
        api.TargetCurrentHp=0;api.TargetLootableRaw=0;
        for(var i=0;i<5&&!await combat.PrepareCleanupTickAsync(context,plan,semiState,state);i++){}
        Require(!state.Fighting,"dead fight releases ownership");
        api.Player=api.Player with{StanceFlags=5,MotionMode=1};
        input.AfterPress=key=>{if(key=="X")api.Player=api.Player with{StanceFlags=0,MotionMode=0};};
        Require(!await combat.PrepareCleanupTickAsync(context,plan,semiState,state),"wait for standing confirmation before cleanup");
        Require(await combat.PrepareCleanupTickAsync(context,plan,semiState,state)&&input.Keys.Count(k=>k=="X")==1,"standing observed without repeated toggle");
        config.ScriptSettings.Paths.RevivePathName="hub";
        var returnController=new StationaryCombatController(input,semi,new InMemorySharedPathStore(new SharedPathDocument{Name="hub",Points=new(){new(){X=0,Y=0,Z=0}}}));
        await returnController.ReturnAfterCleanupAsync(context,plan,semiState,state);
        Require(!state.CleanupReturnToCombatActive,"existing revive-return controller completes at the grinding hub");
        api.TargetEntityId=0;config.MainMode=config.ScriptSettings.MainMode=AccountMainMode.SemiAuto;
        var paths=ManualCleanupPaths(config.ScriptSettings,api,input);
        combat=new StationaryCombatController(input,semi,paths);
        var runner=new CleanupWorkflowRunner(input,paths,(_,_,_)=>Task.FromResult(OperationResult.Ok()),new Journal());
        Require(context.CleanupRequests.Request(config.ScriptSettings,true).Success,"manual bypasses disabled automatic switch");
        var loop=new DefaultAccountWorkerLoop(input,semi,combat,cleanupWorkflow:runner);
        var task=loop.RunAsync(context);
        async Task WaitCompleted(int count)
        {
            while(logger.Entries.Count(e=>e.EventName=="cleanup_workflow.complete")<count || context.CleanupRequests.Current!=null)
            {if(task.IsCompleted)await task;await Task.Delay(5,stop.Token);}
        }
        await WaitCompleted(1);
        Require(!task.IsCompleted&&!context.StopToken.IsCancellationRequested,"successful manual cleanup continues same worker");
        api.Player=api.Player with{Position=new(0,0,0)};
        Require(context.CleanupRequests.Request(config.ScriptSettings,true).Success,"resumed worker accepts later cleanup");
        await WaitCompleted(2);stop.Cancel();
        try{await task;}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        Require(logger.Entries.Count(e=>e.EventName=="cleanup_workflow.complete")==2,"stop never restarts a completed or canceled flow");
    }
}
