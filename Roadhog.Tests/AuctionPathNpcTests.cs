using System.Collections;
using System.Reflection;
using System.Text.Json;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.AuctionHouse;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    public static Task AuctionNpcSettingsAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var config = new AccountConfig { AccountName = "npc-ui", ScriptSettings = new() };
                config.ScriptSettings.Paths.AuctionPathName = "shared";
                var paths = new InMemorySharedPathStore(new SharedPathDocument
                {
                    Name = "shared", CleanupNpcName = "merchant", AuctionNpcName = "broker-before",
                    BagCleanupSellItemClickX = 123, BoundStationaryCombatRadius = 35,
                    Points = new() { new() { X = 1 }, new() { X = 2 } }
                }, new SharedPathDocument { Name = "legacy" });
                var logger = new InMemoryRoadhogLogger();
                var configs = new InMemoryAccountConfigStore(config);
                var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!, configs);
                using var form = new AccountSettingsForm(config.AccountName, runtime, configs, paths, new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "paths");
                object Field(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(form, args);
                var editors = (IDictionary)Field("pathEditors");
                var auction = editors[SharedPathKind.Auction]!;
                var merchant = editors[SharedPathKind.Maintenance]!;
                var combat = editors[SharedPathKind.Combat]!;
                void Save(object editor)
                {
                    var task = (Task)Call("SavePathAsync", editor)!;
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                    Require(task.IsCompleted, "path save completed"); task.GetAwaiter().GetResult();
                }
                Call("LoadPathByName", merchant, "shared");
                Call("LoadPathByName", combat, "shared");
                Require((string)Call("GetSelectedCleanupNpcName", auction)! == "broker-before", "saved auction NPC loaded into selector");
                var nearby = new[]
                {
                    new WorldObjectSnapshot(1, 1, "broker-after", "npc", new(1, 0, 0), 3),
                    new WorldObjectSnapshot(2, 2, "far", "npc", new(20, 0, 0), 20),
                    new WorldObjectSnapshot(3, 3, "player", "player", new(1, 0, 0), 1)
                };
                Require((int)Call("PopulateCleanupNpcCombo", auction, nearby)! == 1, "same nearby NPC filtering as cleanup");
                Save(auction);
                var saved = paths.LoadAsync("shared").Result.Value!;
                Require(saved.AuctionNpcName == "broker-after" && saved.CleanupNpcName == "merchant" && saved.BagCleanupSellItemClickX == 123 && saved.BoundStationaryCombatRadius == 35, "auction save changes only auction NPC metadata");
                Save(merchant); Save(combat);
                Require(paths.LoadAsync("shared").Result.Value!.AuctionNpcName == "broker-after", "other editors preserve latest auction NPC");
                Call("LoadPathByName", auction, "legacy");
                Require((string)Call("GetSelectedCleanupNpcName", auction)! == "", "old paths leave selection blank");
                Call("LoadPathByName", auction, "shared");
                Require((string)Call("GetSelectedCleanupNpcName", auction)! == "broker-after", "path switching restores saved selection");
                Require(JsonSerializer.Deserialize<SharedPathDocument>(JsonSerializer.Serialize(saved.Clone()))!.AuctionNpcName == "broker-after", "clone and JSON preserve new field");
                Require(JsonSerializer.Deserialize<SharedPathDocument>("{}")!.AuctionNpcName == "", "legacy JSON is compatible");
                var tabs = (System.Windows.Forms.TabControl)Field("settingsTabs");
                tabs.SelectedTab = tabs.TabPages.Cast<System.Windows.Forms.TabPage>().Single(t => t.Text == "路径");
                var combo = (System.Windows.Forms.Control)auction.GetType().GetProperty("CleanupNpcCombo")!.GetValue(auction)!;
                var page = combo.Parent!.Parent!.Parent as System.Windows.Forms.TabPage;
                ((System.Windows.Forms.TabControl)page!.Parent!).SelectedTab = page;
                form.ShowInTaskbar = false; form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new(-32000, -32000); form.Show(); System.Windows.Forms.Application.DoEvents();
                using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new(System.Drawing.Point.Empty, form.Size));
                Directory.CreateDirectory(".tmp"); bitmap.Save(".tmp/auction-path-npc-ui.png");
                form.Close(); completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    public static async Task AuctionNpcSelectionAsync()
    {
        foreach (var scenario in new[] { "configured", "named_broker", "legacy_title", "legacy_name", "wrong_role", "wrong_name", "cancel" })
        {
            var api = new FakeGameApi { TargetName = "other", TargetOwnServerObjectId = 7 };
            var ui = Auction() with { IsOpen = false, BrokerTargetServerObjectId = 7 };
            api.AuctionRead = () => ui;
            var input = new RecordingKeyboardInput();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var configured = scenario.StartsWith("legacy") ? "" : "broker";
            if (scenario is "named_broker" or "legacy_name") { configured = scenario == "named_broker" ? "摩比隆" : ""; ui = ui with { BrokerTargetServerObjectId = 0 }; }
            if (scenario == "legacy_title") api.TargetName = "any broker";
            int attempts = 0;
            input.AfterPress = key =>
            {
                Require(key == "F8", "selection sends no interaction or mouse input"); attempts++;
                if (scenario == "cancel") { stop.Cancel(); return; }
                if (scenario == "wrong_role") { api.TargetName = "broker"; ui = ui with { BrokerTargetServerObjectId = 0 }; }
                else if (scenario == "wrong_name") api.TargetName = "other broker";
                else if (attempts >= 2) api.TargetName = scenario is "named_broker" or "legacy_name" ? "摩比隆" : "broker";
            };
            var selector = new AuctionBrokerSelector(input, api.Create(new(), new InMemoryRoadhogLogger(), stop.Token), configured, stop.Token, Fast);
            bool success = false;
            try { await selector.SelectAsync(); success = true; }
            catch (InvalidOperationException) when (scenario is "wrong_role" or "wrong_name") { }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            Require(success == (scenario is "configured" or "named_broker" or "legacy_title" or "legacy_name"), "name and broker identity both required: " + scenario);
            Require(attempts == (scenario == "legacy_title" ? 0 : scenario == "cancel" ? 1 : success ? 2 : 30), "bounded retry and legacy matching: " + scenario);
        }
    }

    public static async Task AuctionNpcFlowsAsync()
    {
        foreach (var diagnostic in new[] { false, true })
        {
            var api = new FakeGameApi { TargetName = "other broker", TargetOwnServerObjectId = 7 };
            var input = new RecordingKeyboardInput(); Cursor(api, input);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var logger = new InMemoryRoadhogLogger();
            var buttons = new Dictionary<string, GameUiPoint>(Auction().Buttons) { ["item_list_btn"] = new(350, 100) };
            var ui = Auction() with { IsOpen = false, ActiveTab = -1, BrokerTargetServerObjectId = 7, Buttons = buttons };
            api.AuctionRead = () => ui;
            api.InventoryInteractionRead = () => new(false, false, false, Array.Empty<InventoryUiItem>(), 0, 0, null, null, false);
            var selected = 0; var opened = 0; bool down = false;
            input.AfterPress = key =>
            {
                if (key == "F8") { selected++; api.TargetName = "path broker"; }
                else if (key == "C") { Require(api.TargetName == "path broker", "configured NPC selected before C"); ui = ui with { DialogOpen = true, TradeButton = new(800, 100) }; }
                else if (key == "Space") ui = ui with { IsOpen = false };
                else throw new Exception("unexpected key: " + key);
            };
            input.AfterMouseDown = _ => down = true;
            input.AfterMouseUp = button =>
            {
                if (!down) return; down = false;
                Require(button == RoadhogMouseButton.Left, "no registration or purchase");
                var point = api.InventoryUiCursor;
                if (point == new GameUiPoint(800, 100)) { opened++; ui = ui with { DialogOpen = false, IsOpen = true, ActiveTab = 0 }; }
                else if (point == new GameUiPoint(100, 100)) ui = ui with { ActiveTab = 2 };
                else if (point == new GameUiPoint(200, 100)) ui = ui with { ActiveTab = 1 };
                else if (point == new GameUiPoint(350, 100)) ui = ui with { ActiveTab = 0 };
                else throw new Exception("unexpected click or transaction: " + point);
            };
            if (diagnostic)
            {
                var result = await new AuctionHouseTestSequence(input, logger, Fast).RunAsync(api.Create(new(), logger, stop.Token), Array.Empty<BagCleanupTradeItemConfig>(), null, stop.Token, "path broker");
                Require(result.Success, "diagnostic respects configured NPC: " + result.Error);
            }
            else
            {
                var settings = new ScriptSettings(); settings.Paths.AuctionPathName = "auction";
                settings.Maintenance.CleanupWorkflow = new() { NpcCleanup = false, Auction = true };
                var config = new AccountConfig { AccountName = "npc-flow", ScriptSettings = settings };
                var paths = new InMemorySharedPathStore(new SharedPathDocument { Name = "auction", AuctionNpcName = "path broker", Points = new() { new() { X = 1 } } });
                var context = new AccountWorkerContext(config, api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
                await new CleanupWorkflowRunner(input, paths, (_, _, _) => Task.FromResult(OperationResult.Ok()), new Journal()).RunAsync(context, new(settings, true));
            }
            Require(selected == 1 && opened == 1 && !ui.IsOpen, "flow uses path NPC instead of accepting another broker, then closes");
        }
    }
}
