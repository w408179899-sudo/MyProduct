using System.Text.Json;
using Roadhog.Application.AuctionHouse;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class AuctionHouseTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static AuctionHouseSnapshot State() => new(false, null, true, 0, new Dictionary<string, GameUiPoint>
    {
        ["item_list_btn"] = new(100,100), ["register_item_btn"] = new(200,100), ["account_btn"] = new(300,100),
        ["search_btn"] = new(400,100), ["search_cancel_btn"] = new(500,100)
    }, "", "请输入搜索条件", Array.Empty<AuctionMarketRow>(), false, 0, null);

    public static Task ConfigAndPublicationAsync()
    {
        Require(AionVmmGameApi.IsAuctionBrokerIdentity("摩比隆",null), "known NPC name is sufficient");
        Require(AionVmmGameApi.IsAuctionBrokerIdentity("another broker","STR_NPCTITLE_Middleman"), "broker title accepts another name");
        Require(!AionVmmGameApi.IsAuctionBrokerIdentity("merchant","STR_NPCTITLE_Merchant"), "ordinary shop target is not a broker");
        foreach (var json in new[] { "\"legacy\"", "{\"name\":\"legacy\",\"unitPrice\":9000}" })
            Require(JsonSerializer.Deserialize<BagCleanupTradeItemConfig>(json)!.PriceLookupMethod == AuctionPriceLookupMethod.Manual, "legacy price stays manual");
        foreach (var method in Enum.GetValues<AuctionPriceLookupMethod>())
        {
            var source = new BagCleanupTradeItemConfig { Name = "same", UnitPrice = null, PriceLookupMethod = method };
            Require(JsonSerializer.Deserialize<BagCleanupTradeItemConfig>(JsonSerializer.Serialize(source))!.PriceLookupMethod == method, "method JSON round trip");
            Require(BagCleanupTradeItemConfig.Normalize(new[] { source, new() { Name="same" } })[0].PriceLookupMethod == method, "duplicate keeps original method");
        }
        try { JsonSerializer.Deserialize<BagCleanupTradeItemConfig>("{\"name\":\"x\",\"priceLookupMethod\":\"unknown\"}"); throw new Exception("invalid method accepted"); } catch (JsonException) { }
        foreach(var method in new[]{AuctionPriceLookupMethod.DialogMinimum,AuctionPriceLookupMethod.SearchCalculation})
        {
            var inactive=new BagCleanupTradeItemConfig{Name="ignored",UnitPrice=-1,PriceLookupMethod=method};
            Require(inactive.EffectiveUnitPrice==null,"automatic mode has no effective manual price");
            Require(BagCleanupTradeItemConfig.Normalize(new[]{inactive})[0].EffectiveUnitPrice==null,"automatic normalization ignores dormant price");
            Require(JsonSerializer.Deserialize<BagCleanupTradeItemConfig>(JsonSerializer.Serialize(inactive))!.EffectiveUnitPrice==null,"automatic serialization skips dormant price validation");
            foreach(var json in new[]{"{\"name\":\"x\",\"unitPrice\":\"unused\",\"priceLookupMethod\":\""+method+"\"}",
                "{\"name\":\"x\",\"priceLookupMethod\":\""+method+"\",\"unitPrice\":0}"})
                Require(JsonSerializer.Deserialize<BagCleanupTradeItemConfig>(json)!.EffectiveUnitPrice==null,"inactive price ignored regardless of JSON property order");
        }
        try { JsonSerializer.Deserialize<BagCleanupTradeItemConfig>("{\"name\":\"x\",\"unitPrice\":0}");throw new Exception("invalid manual price accepted"); }catch(JsonException){}
        var now = DateTimeOffset.UtcNow; var context = new GameApiReadContext("test",1,"Aion.bin","fake");
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry); var channel = AionVmmSnapshotChannels.AuctionHouse;
        var failed = OperationResult<AuctionHouseSnapshot>.Fail("short read"); var state = State();
        Require(!store.Resolve("a",channel,context,failed,now).Result.Success, "cold failure cannot fabricate closed");
        store.Resolve("a",channel,context,OperationResult<AuctionHouseSnapshot>.Ok(state),now);
        Require(ReferenceEquals(state,store.Resolve("a",channel,context,failed,now.AddDays(5)).Result.Value), "retain official snapshot without TTL");
        Require(!store.Resolve("b",channel,context,failed,now).Result.Success, "account session isolation");
        var closed = state with { IsOpen=false, MarketRows=Array.Empty<AuctionMarketRow>() };
        store.Resolve("a",channel,context,OperationResult<AuctionHouseSnapshot>.Ok(closed),now);
        Require(store.Resolve("a",channel,context,failed,now).Result.Value==closed,"closed and empty replace previous state");
        store.ClearSession("a"); Require(!store.Resolve("a",channel,context,failed,now).Result.Success,"lifecycle invalidates");
        Require(InventoryInteractionDecoder.AuctionMoney("1,000 \uE000 ")==1000,"market money parsing");
        Require(InventoryInteractionDecoder.AuctionMoney("-")==null,"unavailable quote is not zero");
        const ulong module=0x180000000;
        byte[] ClosedMemory(ulong address,int size)
        {
            if(address==module+0xDACF00&&size==16)return BitConverter.GetBytes(1024d).Concat(BitConverter.GetBytes(768d)).ToArray();
            if(address>=module+0xD63990&&address<=module+0xD63990+365*8&&size==8)return new byte[8];
            throw new InvalidDataException("Unexpected read");
        }
        Require(!new InventoryInteractionDecoder(ClosedMemory).ReadAuction(module).IsOpen,"complete absent roots publish closed");
        try { new InventoryInteractionDecoder((_,_)=>Array.Empty<byte>()).ReadAuction(module);throw new Exception("short read accepted"); }catch(InvalidDataException){}
        int captures=0;
        try
        {
            new InventoryInteractionDecoder((a,n)=>a==module+0xD63990+152*8&&++captures==2?BitConverter.GetBytes(0x200000UL):ClosedMemory(a,n)).ReadAuction(module);
            throw new Exception("root changed during capture accepted");
        }
        catch(InvalidDataException){}
        return Task.CompletedTask;
    }

    public static async Task ActionsAsync()
    {
        foreach (var scenario in new[] { "success", "search", "identity", "cancel", "wrong_editor" })
        {
            var state=State();var api=new FakeGameApi();var input=new RecordingKeyboardInput();var down=false;int right=0;
            var item=new InventoryItemSnapshot(152010313,11,"坚固的生皮子",367,0,false);
            api.InventoryItems=new[]{item with{InstanceId=22,IsEquipped=true},item};api.AuctionRead=()=>state;
            var bagItem=new InventoryUiItem(11,item.TemplateId,item.Count,new(700,300));
            api.InventoryInteractionRead=()=>new(true,false,false,new[]{bagItem},api.InventoryUiCursor==bagItem.Point?11u:0u,0,null,null,false);
            using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            input.AfterMove=(x,y)=> { api.InventoryUiCursor=new(api.InventoryUiCursor.X+x,api.InventoryUiCursor.Y+y); if(scenario=="identity")api.Player=api.Player with{CharacterName="changed"}; };
            input.AfterPress=key=> { if(key=="Space")state=state with{IsOpen=false}; else throw new Exception("unexpected key "+key); };
            input.AfterMouseDown=button=> { down=true; if(scenario=="cancel"){stop.Cancel();stop.Token.ThrowIfCancellationRequested();} };
            input.AfterMouseUp=button=>
            {
                if(!down)return;down=false;
                if(button==RoadhogMouseButton.Right)
                {
                    right++;
                    state=state.ActiveTab==0?state with{SearchName=item.Name}:state with{Editor=new(scenario=="wrong_editor"?999u:item.TemplateId,367,20,1000,new(600,100))};
                    return;
                }
                var p=api.InventoryUiCursor;
                if(p==new GameUiPoint(300,100))state=state with{ActiveTab=2,SettlementLoaded=true,SettlementMoney=2645512};
                else if(p==new GameUiPoint(100,100))state=state with{ActiveTab=0};
                else if(p==new GameUiPoint(200,100))state=state with{ActiveTab=1};
                else if(p==new GameUiPoint(500,100))state=state with{SearchName="",MarketRows=Array.Empty<AuctionMarketRow>()};
                else if(p==new GameUiPoint(400,100))state=state with{MarketRows=new[]{new AuctionMarketRow(item.TemplateId,1,item.Name,1000,1000)}};
                else if(p==new GameUiPoint(600,100))state=state with{Editor=null};
                else throw new Exception("unexpected click/commit "+p);
            };
            var snapshots=api.Create(new AccountConfig(),new InMemoryRoadhogLogger(),stop.Token);
            try
            {
                var result=await new AuctionHouseTestSequence(input,new InMemoryRoadhogLogger(),(_,t)=>{t.ThrowIfCancellationRequested();return Task.CompletedTask;})
                    .RunAsync(snapshots,new[]{new BagCleanupTradeItemConfig{Name=item.Name,PriceLookupMethod=scenario=="search"?AuctionPriceLookupMethod.SearchCalculation:AuctionPriceLookupMethod.DialogMinimum}},null,stop.Token);
                Require(result.Success==(scenario is "success" or "search"),scenario+": "+result.Error);
                if(result.Success){Require(!state.IsOpen&&state.Editor==null&&right==(scenario=="search"?2:1),"lookup method controls search; cancel and close verified");Require(result.Value!.Contains(scenario=="search"?"算法待定义":"1,000"),"reports selected method");}
            }
            catch(OperationCanceledException)when(scenario=="cancel"){}
            Require(!down,"input released "+scenario);
            if(scenario=="identity")Require(!input.MouseCommands.Contains("down:Left"),"identity change blocks click");
        }
    }
}
