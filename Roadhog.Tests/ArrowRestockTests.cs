using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class ArrowRestockTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static Task ThresholdsAsync()
    {
        using var f = new Fixture(0);
        foreach (var (count, combat, cleanup, batches) in new (uint, bool, bool, int)[]
        { (0, true, true, 5), (9999, true, true, 5), (10000, false, true, 4), (24000, false, true, 3), (49999, false, true, 1), (50000, false, false, 0), (70000, false, false, 0) })
        {
            f.Api.InventoryItems = new[] { Arrow(count) };
            Check(ArrowRestockPolicy.NeedsRestock(f.Api.Player, f.Api.InventoryItems, false) == combat &&
                ArrowRestockPolicy.NeedsRestock(f.Api.Player, f.Api.InventoryItems, true) == cleanup && ArrowRestockPolicy.Batches(count) == batches,
                "separate combat/cleanup thresholds and rounded purchase quantity: " + count);
        }
        var bag = new[] { Arrow(20000), Arrow(8084) with { InstanceId = 2, IsEquipped = true }, new InventoryItemSnapshot(140000073, 3, "沉默箭 V", 1, 2, true, 9) };
        Check(ArrowRestockPolicy.Count(bag) == 28084 && ArrowRestockPolicy.Batches(28084) == 3, "equipped ammunition counts; an arrow skillbook does not");
        Check(!ArrowRestockPolicy.NeedsRestock(f.Api.Player with { CharacterClassId = AionClassId.Cleric }, Array.Empty<InventoryItemSnapshot>(), false), "other classes never restock");
        var mailbox = new CleanupRequestMailbox();
        var settings = new ScriptSettings(); settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
        settings.Maintenance.CleanupWorkflow.Auction = settings.Maintenance.CleanupWorkflow.TransferGold = true;
        Check(mailbox.Request(settings, false, arrowRestock: true).Success, "queue supply trip even in grocery mode");
        var request = mailbox.Current!;
        Check(request.ArrowRestock && !request.GroceryShop && !request.AllowNpcSell && !request.ResetsCooldown &&
            request.Settings.Maintenance.CleanupWorkflow.NpcCleanup && !request.Settings.Maintenance.CleanupWorkflow.Auction &&
            !request.Settings.Maintenance.CleanupWorkflow.TransferGold && !request.RequestsRestart,
            "arrow trip reuses cleanup route without selling, auction, warehouse or script restart");
        return Task.CompletedTask;
    }

    public static async Task PurchaseAsync()
    {
        foreach (var count in new uint[] { 0, 24000, 28084, 49999, 50000 })
        {
            using var f = new Fixture(count);
            await f.Run();
            var batches = ArrowRestockPolicy.Batches(count);
            Check(f.AllClicks == batches && f.ConfirmClicks == batches && f.RightClicks == batches && f.BuyClicks == (batches == 0 ? 0 : 1),
                "repeat Shift-right, ALL, confirm per stack; purchase once: " + count);
            Check(ArrowRestockPolicy.Count(f.Api.InventoryItems) == count + (ulong)batches * 10000 &&
                f.Api.InventoryMoney == 1000000UL - (ulong)batches * 10000 && !f.Shift && !f.Ui.IsOpen && !f.Ui.DialogOpen,
                "inventory and gold confirm purchase, controls released and window closed");
            if (batches == 0) Check(f.Input.Keys.Count == 0, "at threshold no NPC or inventory inputs");
        }
        using var other = new Fixture(0);
        other.Api.Player = other.Api.Player with { CharacterClassId = AionClassId.Cleric };
        await other.Run(); Check(other.Input.Keys.Count == 0 && other.BuyClicks == 0, "non-ranger no-op");
    }

    public static async Task PurchaseGuardsAsync()
    {
        foreach (var scenario in new[] { "insufficient_gold", "wrong_maximum", "missing_all", "foreign_basket", "changed_target", "partial_purchase", "money_only", "cancel" })
        {
            using var f = new Fixture(24000) { Scenario = scenario };
            if (scenario == "insufficient_gold") f.Api.InventoryMoney = 1;
            var failed = false;
            try { await f.Run(); }
            catch (Exception ex) when (ex is InvalidOperationException or ArrowPurchaseUnconfirmedException or OperationCanceledException) { failed = true; }
            Check(failed && !f.Shift && f.Input.KeyUps.Contains("ShiftKey"), "failure releases modifier: " + scenario);
            Check(f.BuyClicks == (scenario is "partial_purchase" or "money_only" ? 1 : 0), "never submit an invalid basket or repeat uncertain purchase: " + scenario);
        }
    }

    private static InventoryItemSnapshot Arrow(uint count) => new(ArrowRestockPolicy.ArrowTemplateId, 11, "弓箭", count, 0, false, 5);

    private sealed class Fixture : IDisposable
    {
        public readonly FakeGameApi Api = new() { TargetEntityId = 0, TargetIsTargetingLocalPlayer = false, InventoryMoney = 1000000 };
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(45));
        public readonly AccountWorkerContext Context;
        public NpcTradeSnapshot Ui = new(false, new Dictionary<string, GameUiPoint> { ["购买道具"] = new(100, 100), ["出售物品"] = new(110, 100) }, false, -1, 42, false, Array.Empty<NpcTradeItem>(), null, false);
        public string Scenario = "success";
        public bool Shift;
        private bool rightClickAwaitingClientFrame;
        private long rightClickAt;
        private bool down;
        public int RightClicks, AllClicks, ConfirmClicks, BuyClicks;
        public bool BagOpen;
        public int JunkClicks, SaleClicks;
        private static readonly NpcShopItem Item = new(4, ArrowRestockPolicy.ArrowTemplateId, "弓箭", 5, 1, new(200, 200));
        public Fixture(uint count)
        {
            Api.Player = Api.Player with { CharacterClassId = AionClassId.Ranger };
            Api.InventoryItems = new[] { Arrow(count), new InventoryItemSnapshot(999, 12, "protected", 3, 1, false) };
            Context = new(new() { AccountName = "arrows", ScriptSettings = new() }, Api, Logger, new AccountRuntimeManager(Logger), new(), Stop.Token) { WorkflowOwnsCleanup = true };
            Api.NpcTradeRead = () => Ui;
            Api.AuctionRead = () => new(Ui.DialogOpen, null, false, 0, new Dictionary<string, GameUiPoint>(), "", "", Array.Empty<AuctionMarketRow>(), false, 0, null);
            Api.InventoryInteractionRead = () => new(BagOpen, false, false, Array.Empty<InventoryUiItem>(), 0, 0, null, null, false);
            Input.AfterPress = key =>
            {
                if (key == "F8") { Api.TargetEntityId = 42; Api.TargetOwnServerObjectId = 42; Api.TargetName = "merchant"; }
                if (key == "C") Ui = Ui with { DialogOpen = true };
                if (key == "I") { BagOpen = !BagOpen; Ui = Ui with { InventoryOpen = BagOpen }; }
                if (key == "Space") Ui = Ui with { IsOpen = false, DialogOpen = false, Purchase = null, Basket = Array.Empty<NpcTradeItem>() };
            };
            Input.AfterKeyDown = key => { if (key == "ShiftKey") Shift = true; };
            Input.AfterKeyUp = key =>
            {
                if (key != "ShiftKey") return;
                Check(!rightClickAwaitingClientFrame || Environment.TickCount64 - rightClickAt >= 90,
                    "keep Shift held until the client can consume the right-click");
                rightClickAwaitingClientFrame = false;
                Shift = false;
            };
            Input.AfterMove = (x, y) =>
            {
                Api.InventoryUiCursor = new(Api.InventoryUiCursor.X + x, Api.InventoryUiCursor.Y + y);
                if (Ui.Purchase is { } purchase)
                {
                    Ui = Ui with { Purchase = purchase with { HoveredItemId = Api.InventoryUiCursor == Item.Point ? Item.ItemId : null } };
                    if (Scenario == "changed_target") Api.TargetOwnServerObjectId = 43;
                    if (Shift && Scenario == "cancel") Stop.Cancel();
                }
            };
            Input.AfterMouseDown = _ => down = true;
            Input.AfterMouseUp = button =>
            {
                if (!down) return; down = false;
                var point = Api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    Check(Shift && point == Item.Point && Ui.Purchase!.QuantityDialog == null, "hold Shift while moving and clicking exact arrow");
                    RightClicks++;
                    rightClickAwaitingClientFrame = true;
                    rightClickAt = Environment.TickCount64;
                    Ui = Ui with { Purchase = Ui.Purchase! with { QuantityDialog = new(4, 100, Scenario == "wrong_maximum" ? 20000UL : 10000UL,
                        Scenario == "missing_all" ? null : new(300, 300), new(400, 300)), BuyButton = null } };
                }
                else if (point == new GameUiPoint(100, 100))
                    Ui = Ui with { IsOpen = true, DialogOpen = false, Mode = 0, Purchase = new(new[] { Item },
                        Scenario == "foreign_basket" ? new[] { new NpcTradeItem(5, 999, 1) } : Array.Empty<NpcTradeItem>(), null, null, new(500, 300)) };
                else if (point == new GameUiPoint(300, 300))
                {
                    Check(!Shift, "release Shift before ALL"); AllClicks++;
                    Ui = Ui with { Purchase = Ui.Purchase! with { QuantityDialog = Ui.Purchase!.QuantityDialog! with { Quantity = 10000 } } };
                }
                else if (point == new GameUiPoint(400, 300))
                {
                    ConfirmClicks++;
                    Ui = Ui with { Purchase = Ui.Purchase! with { QuantityDialog = null,
                        Basket = Ui.Purchase!.Basket.Append(new(4, ArrowRestockPolicy.ArrowTemplateId, 10000)).ToArray(), BuyButton = new(500, 300) } };
                }
                else if (point == new GameUiPoint(500, 300))
                {
                    BuyClicks++; Check(!Shift && BuyClicks == 1, "purchase submitted once with Shift released");
                    var quantity = Ui.Purchase!.Basket.Sum(i => (long)i.Quantity);
                    if (Scenario != "money_only") Api.InventoryItems = Api.InventoryItems.Select(i => i.InstanceId == 11
                        ? i with { Count = i.Count + (uint)(Scenario == "partial_purchase" ? 10000 : quantity) } : i).ToArray();
                    Api.InventoryMoney -= (ulong)quantity;
                    Ui = Ui with { Purchase = Ui.Purchase with { Basket = Array.Empty<NpcTradeItem>() } };
                }
                else if (point == new GameUiPoint(110, 100))
                    Ui = Ui with { IsOpen = true, DialogOpen = false, Mode = 1, Purchase = null,
                        SellButton = new(500, 400), RegisterAllJunkButton = new(600, 400) };
                else if (point == new GameUiPoint(600, 400))
                {
                    Check(Ui.IsSelling && BagOpen, "junk registration belongs to sale page"); JunkClicks++;
                    Ui = Ui with { Basket = new[] { new NpcTradeItem(12, 999, 3) } };
                }
                else if (point == new GameUiPoint(500, 400))
                {
                    Check(Ui.IsSelling && BagOpen && Ui.Basket.Count == 1, "sale occurs before reopening for arrows"); SaleClicks++;
                    if (Scenario == "sale_throw") throw new InvalidOperationException("sale failed");
                    if (Scenario == "sale_limit") { Ui = Ui with { Basket = Array.Empty<NpcTradeItem>() }; return; }
                    Api.InventoryItems = Api.InventoryItems.Where(i => i.InstanceId != 12).ToArray();
                    Api.InventoryMoney += 3; Ui = Ui with { Basket = Array.Empty<NpcTradeItem>() };
                }
                else throw new Exception("unknown arrow input point " + point);
            };
        }
        public Task Pause(int ms, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (rightClickAwaitingClientFrame && Shift && ms >= 100) rightClickAwaitingClientFrame = false;
            return Task.CompletedTask;
        }
        public Task Run() => new NpcArrowPurchaseSequence(Input, Pause, 100).RunAsync(Context, "merchant");
        public void Dispose() => Stop.Dispose();
    }
}
