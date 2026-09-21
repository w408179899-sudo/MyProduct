using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class CleanupWorkflowTests
{
    private static async Task ShiftClickTimingAsync()
    {
        foreach (var scenario in new[] { "normal", "cancel", "owner_changed" })
        {
            var api = new FakeGameApi(); var input = new RecordingKeyboardInput(); Cursor(api,input);
            using var stop = new CancellationTokenSource();
            var point = new GameUiPoint(500,300); bool valid=true, mouseDown=false, mouseUp=false;
            int modifierElapsed=0, afterMouseUp=0;
            Task Delay(int ms,CancellationToken token)
            {
                if(input.KeyDowns.Count>input.KeyUps.Count)
                {
                    if(mouseUp)afterMouseUp+=ms;else modifierElapsed+=ms;
                    if(scenario=="cancel")stop.Cancel();
                    if(scenario=="owner_changed")valid=false;
                }
                token.ThrowIfCancellationRequested();return Task.CompletedTask;
            }
            input.AfterMouseDown=button=>
            {
                Require(button==RoadhogMouseButton.Right&&modifierElapsed>=80,"device must observe shift before right button");
                mouseDown=true;
            };
            input.AfterMouseUp=_=>{if(mouseDown)mouseUp=true;};
            input.AfterKeyUp=key=>
            {
                if(key=="ShiftKey"&&scenario=="normal")Require(mouseUp&&afterMouseUp>=80,"shift must remain held while mouse release reaches game");
            };
            var actions=new TradingActions(input,api.Create(new AccountConfig(),new InMemoryRoadhogLogger(),stop.Token),stop.Token,Delay);
            try
            {
                await actions.Click(()=>Task.FromResult(valid),_=>point,s=>s,RoadhogMouseButton.Right,shift:true);
                Require(scenario=="normal","changed target or cancellation must stop click");
            }
            catch(OperationCanceledException)when(scenario=="cancel"){}
            catch(InvalidOperationException)when(scenario=="owner_changed"){}
            Require(input.KeyUps.Contains("ShiftKey"),"modifier released on every exit");
            Require(mouseDown==(scenario=="normal"),"no click after identity change or cancellation");
        }
    }
}
