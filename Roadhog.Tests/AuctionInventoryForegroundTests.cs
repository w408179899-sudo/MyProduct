using Roadhog.Application.Trading;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class CleanupWorkflowTests
{
    public static async Task AuctionInventoryForegroundAsync()
    {
        foreach (var scenario in new[] { "covered", "closed", "moved", "delayed", "cancel_close", "cancel_open", "page_changed", "modal" })
        {
            var api=new FakeGameApi(); var input=new RecordingKeyboardInput(); Cursor(api,input);
            using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var item=new InventoryItemSnapshot(20,11,"item",3,0,false);
            var point=new GameUiPoint(700,300);
            bool open=scenario is not ("closed" or "cancel_open"), foreground=false, pageValid=true, down=false;
            bool? pending=null; int pendingReads=0, toggles=0, right=0;
            api.InventoryInteractionRead=()=>
            {
                if(pending.HasValue && ++pendingReads==3)
                {
                    if(scenario.StartsWith("cancel_"))stop.Cancel();
                    else {open=pending.Value;foreground=open;pending=null;}
                }
                return new(open,false,false,open?new[]{new InventoryUiItem(11,20,3,point)}:Array.Empty<InventoryUiItem>(),
                    api.InventoryUiCursor==point?11u:0u,0,null,null,scenario=="modal");
            };
            input.AfterPress=key=>
            {
                Require(key=="I","foreground change only toggles inventory");toggles++;
                Require(!pending.HasValue,"must confirm close/open before another toggle");
                if(scenario is "delayed" or "cancel_close" or "cancel_open") {pending=!open;pendingReads=0;}
                else {open=!open;foreground=open;}
                if(scenario=="moved"&&open)point=new(800,400);
                if(scenario=="page_changed"&&!open)pageValid=false;
            };
            input.AfterMouseDown=button=>
            {
                Require(button==RoadhogMouseButton.Right&&open&&foreground&&pageValid&&!pending.HasValue,"right-click must land on the reopened foreground inventory");
                Require(api.InventoryUiCursor==point,"use coordinates captured after reopening");down=true;right++;
            };
            input.AfterMouseUp=_=>down=false;
            Exception? error=null;
            try
            {
                var actions=new TradingActions(input,api.Create(new(),new InMemoryRoadhogLogger(),stop.Token),stop.Token,Fast);
                await actions.BringBagToFront(()=>Task.FromResult(pageValid));
                await actions.RightClickBag(item,()=>Task.FromResult(pageValid));
            }
            catch(Exception ex){error=ex;}
            var succeeds=scenario is "covered" or "closed" or "moved" or "delayed";
            Require((error==null)==succeeds,"foreground result: "+scenario+" "+error?.Message);
            Require(right==(succeeds?1:0)&&!down,"no right click when reopening cannot be confirmed: "+scenario);
            Require(toggles==(scenario=="modal"?0:scenario is "closed" or "cancel_close" or "cancel_open" or "page_changed"?1:2),"exact open/close sequence: "+scenario);
        }
    }
}
