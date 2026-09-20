using System.Text;
using Roadhog.Application.Input;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class ReviveConfirmationTests
{
    public static Task GeometryAsync()
    {
        var m = new Memory();
        Require(m.Read().ConfirmButton == new GameUiPoint(375, 302), "include root, client and button offsets");
        m.D(Memory.Root + 0x58, 450);
        Require(m.Read().ConfirmButton == new GameUiPoint(525, 302), "follow moved dialog");
        m.Q(Memory.Button + 0x28, 1);
        Require(m.Read().ConfirmButton == null, "disabled button");
        m.Q(Memory.Button + 0x28, 0);
        Require(m.Read().ConfirmButton == null, "hidden button");
        m.Q(Memory.Button + 0x28, 3); m.D(Memory.Button + 0x58, 2000);
        Require(m.Read().ConfirmButton == null, "outside viewport");
        m = new(); m.Q(Memory.Root + 0x28, 0);
        Require(m.Read() == ReviveUiSnapshot.Closed, "hidden dialog publishes closed");
        m = new(); m.Q(Memory.Base + 0xD63990 + 210 * 8, Memory.Root);
        Require(m.Read().ConfirmButton == null, "incoming revive blocks ordinary dialog");
        m = new(); m.Q(Memory.Base + 0xD63990 + 336 * 8, Memory.Root);
        Require(m.Read().ConfirmButton == null, "other modal blocks click through");
        m = new(); m.Name(Memory.Button, "cancel");
        Throws(() => m.Read(), "wrong button identity rejected");
        m = new(); m.Q(Memory.Node, Memory.Node);
        Throws(() => m.Read(), "cyclic UI list rejected");
        m = new(); var reads = 0;
        var fixture = m;
        var decoder = new InventoryInteractionDecoder((a, n) =>
        {
            if (a == Memory.Root + 0x58 && ++reads == 2) fixture.D(a, 400);
            return fixture.Bytes(a, n);
        });
        Throws(() => decoder.ReadRevive(Memory.Base), "geometry changed during capture rejected");
        Throws(() => new InventoryInteractionDecoder((a, n) => Array.Empty<byte>()).ReadRevive(Memory.Base), "short read rejected");
        return Task.CompletedTask;
    }

    public static async Task ActionsAsync()
    {
        foreach (var scenario in new[] { "success", "missing", "disabled", "moved", "closed", "identity", "alive", "cursor", "cancel" })
        {
            var api = new FakeGameApi { Player = new(1, 0, "test", 0, 100, 50, 100, 0, new(0,0,0), DateTimeOffset.Now) };
            var expected = api.Player;
            var ui = new ReviveUiSnapshot(true, 209, new(617,411));
            if (scenario == "missing") ui = ReviveUiSnapshot.Closed;
            if (scenario == "disabled") ui = ui with { ConfirmButton = null };
            api.ReviveUiRead = () => ui;
            var input = new RecordingKeyboardInput();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            input.AfterMove = (x, y) =>
            {
                api.InventoryUiCursor = new(api.InventoryUiCursor.X+x, api.InventoryUiCursor.Y+y);
                if (scenario == "moved") ui = ui with { ConfirmButton = new(700,400) };
                if (scenario == "closed") ui = ReviveUiSnapshot.Closed;
                if (scenario == "identity") api.Player = api.Player with { CharacterName = "other" };
                if (scenario == "alive") api.Player = api.Player with { CurrentHp = 50 };
            };
            if (scenario == "cancel") input.AfterMouseDown = _ => { stop.Cancel(); stop.Token.ThrowIfCancellationRequested(); };
            var reader = api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), stop.Token);
            var action = new ReviveConfirmation(input, reader, (ms, token) =>
            {
                if (scenario == "cursor" && ms == 200) api.InventoryUiCursor = new(1,1);
                token.ThrowIfCancellationRequested(); return Task.CompletedTask;
            });
            try
            {
                var point = await action.TryClickAsync(expected, 1, stop.Token);
                Require((point != null) == (scenario == "success"), scenario);
            }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            var down = input.MouseCommands.Count(c => c == "down:Left");
            Require(down == (scenario is "success" or "cancel" ? 1 : 0), "click count " + scenario);
            Require(input.MouseCommands.Count(c => c == "up:Left") == down, "release even on cancellation " + scenario);
            Require(!input.MouseCommands.Contains("move:-2000,-2000"), "never reset cursor to screen corner");
        }
    }

    public static async Task PublicationAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var context = new GameApiReadContext("test",1,"Aion.bin","fake");
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.ReviveUi;
        var failed = OperationResult<ReviveUiSnapshot>.Fail("short read");
        Require(!store.Resolve("a",channel,context,failed,now).Result.Success,"cold start cannot fabricate closed");
        var ui = new ReviveUiSnapshot(true,209,new(600,400));
        store.Resolve("a",channel,context,OperationResult<ReviveUiSnapshot>.Ok(ui),now);
        Require(ReferenceEquals(ui, store.Resolve("a",channel,context,failed,now.AddDays(1)).Result.Value),"retain unchanged snapshot without TTL");
        Require(!store.Resolve("b",channel,context,failed,now).Result.Success,"session isolation");
        store.Resolve("a",channel,context,OperationResult<ReviveUiSnapshot>.Ok(ReviveUiSnapshot.Closed),now);
        Require(store.Resolve("a",channel,context,failed,now).Result.Value == ReviveUiSnapshot.Closed,"valid closed replaces open");
        store.ClearSession("a");
        Require(!store.Resolve("a",channel,context,failed,now).Result.Success,"lifecycle reset invalidates publication");
        var api = new FakeGameApi();
        api.ReviveUiReadResults.Enqueue(failed); api.ReviveUiReadResults.Enqueue(failed);
        api.ReviveUiReadResults.Enqueue(OperationResult<ReviveUiSnapshot>.Ok(ui));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var snapshots = api.Create(new(),new InMemoryRoadhogLogger(),stop.Token);
        Require((await snapshots.ReadReviveUiAsync()).Value == ui,"retry below business boundary until first publication");
    }

    private static void Require(bool ok, string text) { if(!ok) throw new Exception(text); }
    private static void Throws(Action action,string text) { try { action(); } catch(InvalidDataException) { return; } throw new Exception(text); }
    private sealed class Memory
    {
        internal const ulong Base=0x180000000, Root=0x200000, Button=0x210000, Head=0x220000, Node=0x230000;
        private readonly Dictionary<ulong,byte> _data=new();
        private ulong _strings=0x300000;
        internal Memory()
        {
            D(Base+0xDACF00,1280);D(Base+0xDACF08,720);
            Q(Base+0xD63990+209*8,Root);Q(Base+0xD63990+210*8,0);
            for(var id=336;id<=365;id++)Q(Base+0xD63990+(ulong)id*8,0);
            Widget(Root,"resurrect_dialog",300,200,250,150);Widget(Button,"resurrect_ok",20,80,100,30);
            Q(Root+0x4E0,Button);D(Root+0x78,5);D(Root+0x80,7);
            Q(Root+0x238,Head);Q(Head,Node);Q(Node,Head);Q(Node+0x10,Button);
        }
        internal void Put(ulong a,byte[] value){for(int i=0;i<value.Length;i++)_data[a+(ulong)i]=value[i];}
        internal void Q(ulong a,ulong v)=>Put(a,BitConverter.GetBytes(v));
        internal void D(ulong a,double v)=>Put(a,BitConverter.GetBytes(v));
        internal void Name(ulong a,string name)
        {
            var bytes=Encoding.UTF8.GetBytes(name);Q(a+0x18,(ulong)bytes.Length);Q(a+0x20,bytes.Length<16?15UL:31UL);
            if(bytes.Length<16)Put(a+8,bytes);else {Q(a+8,_strings);Put(_strings,bytes);_strings+=256;}
        }
        private void Widget(ulong a,string name,double x,double y,double w,double h)
        {Put(a,new byte[0x280]);Name(a,name);Q(a+0x28,3);D(a+0x58,x);D(a+0x60,y);D(a+0x68,w);D(a+0x70,h);}
        internal byte[] Bytes(ulong a,int n)=>Enumerable.Range(0,n).Select(i=>_data[a+(ulong)i]).ToArray();
        internal ReviveUiSnapshot Read()=>new InventoryInteractionDecoder(Bytes).ReadRevive(Base);
    }
}
