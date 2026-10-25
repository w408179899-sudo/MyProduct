using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class LoginDisconnectTests
{
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static JsonNode Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures,"away-disconnect.json")))!;
    private static JsonElement Json(JsonNode value)=>JsonSerializer.SerializeToElement(value);
    private static JsonNode Modal(JsonNode value)=>value["Ui"]!["Windows"]!.AsArray().Single(w=>w!["Slot"]!.GetValue<int>()>=336)!;
    private static LoginObservation Project(JsonNode value)=>LoginNativeReader.ProjectGame(Json(value["Ui"]!),Json(value["Window"]!),"Tone","test",900);
    private static void Reject(Action action,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message);}

    internal static Task ArchivedPopupIdentityAndCoordinatesAsync()
    {
        var fixture=Fixture();var scene=Project(fixture);
        Require(scene.Snapshot.Stage==LoginStage.Disconnected&&scene.Snapshot.Action==LoginAction.ConfirmGameExit&&scene.Point==new Point(630,416),
            "The live away-disconnect popup authorizes its exact confirmation button.");
        fixture["Window"]!["ClientRect"]=JsonSerializer.SerializeToNode(new[]{100,50,1124,818});
        Require(Project(fixture).Point==new Point(730,466),"Confirmation coordinates follow the actual game window.");
        foreach(var field in new[]{"Owner","Style","ConfirmEvent","CancelEvent","Tag"})
        {
            var invalid=Fixture();var message=Modal(invalid)["Message"]!;message[field]=message[field]!.GetValue<int>()+1;
            Require(Project(invalid).Snapshot.Action!=LoginAction.ConfirmGameExit,"Unrelated modal identity cannot trigger exit: "+field);
        }
        foreach(string body in new[]{"是否退出游戏？","由于长时间在线，请重新登录。","由于您一直处于离开状态，为了安全，请注意。"})
        {
            var invalid=Fixture();Modal(invalid)["Message"]!["Body"]=body;
            Require(Project(invalid).Snapshot.Stage==LoginStage.Blocked,"Unverified messages must not inherit the away-exit action.");
        }
        var missing=Fixture();Modal(missing)["Message"]!["Body"]="";
        Require(Project(missing).Snapshot.Action==LoginAction.None,"Unpopulated message text cannot authorize a click.");
        var duplicate=Fixture();duplicate["Ui"]!["Windows"]!.AsArray().Add(Modal(duplicate).DeepClone());
        Require(Project(duplicate).Snapshot.Stage==LoginStage.Blocked,"Multiple visible modals cannot choose an arbitrary confirmation.");
        return Task.CompletedTask;
    }

    internal static Task ButtonAndProcessFactsAsync()
    {
        foreach(string fault in new[]{"Enabled","Address","Text","W"})
        {
            var invalid=Fixture();var ok=Modal(invalid)["Nodes"]!.AsArray().Single(n=>n!["Name"]!.GetValue<string>()=="ok")!;
            if(fault=="Enabled")ok[fault]=false;
            else if(fault=="W")ok[fault]=0;
            else ok[fault]="wrong";
            Reject(()=>Project(invalid),"Missing or invalid confirmation target cannot dispatch exit: "+fault);
        }
        var scene=Project(Fixture());
        foreach(var pids in new uint[][]{[],[7504],[7504,8000]})
        {
            var snapshot=LoginNativeReader.WithGameProcesses(scene,pids).Snapshot;
            Require(snapshot.GameProcessCount==pids.Length&&snapshot.GameProcessId==(pids.Length==1?(int)pids[0]:0),
                "Every scene carries the complete Aion process count, including UU scenes and multiple-process conditions.");
        }
        return Task.CompletedTask;
    }
}
