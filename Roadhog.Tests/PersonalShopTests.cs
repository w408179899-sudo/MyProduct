using Roadhog.Application.PersonalShop;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class PersonalShopTests
{
    public static async Task FullSequenceAsync()
    {
        var game = new Simulation(6);
        game.Api.InventoryItems = game.Api.InventoryItems.Select((i, n) => i with { Count = n == 1 ? 26U : 1U }).ToArray();
        var result = await game.Run();
        Require(result.Success && result.Value!.RegisteredCount == 6 && game.Selling && game.Starts == 1, result.Error ?? "complete flow failed");
        Require(game.Listings[1].Quantity == 26 && game.Listings.All(l => l.UnitPrice == 1), "whole stack at unit price 1");
        Require(game.Input.Keys.Contains("Y") && game.Input.Keys.Count(k => k == "I") == 2, "open shop, open bag, close bag before start");
        Require(game.Input.MouseCommands.Count(c => c == "down:Right") == 6 && game.Confirms == 6, "one registration per item");
        Require(game.Delays.Contains(70) && game.Delays.Contains(350) && game.Delays.Contains(250) && game.Delays.Contains(300), "validated timing profile retained");
    }

    public static async Task FilteringAndCapacityAsync()
    {
        var empty = new Simulation(1);
        empty.Settings.BagCleanupRules.Clear();
        var result = await empty.Run();
        Require(result.Success && result.Value!.RegisteredCount == 0 && empty.Input.MouseCommands.Count == 0 && !empty.Input.Keys.Any(), "no enabled sell rules must not touch game");
        var game = new Simulation(13);
        game.Settings.BagCleanupExcludedItemNames = new() { "item0" };
        game.Settings.BagCleanupDiscardItemNameKeywords = new() { "item1" };
        // Keyword item1 also protects item10, item11, item12.
        result = await game.Run();
        Require(result.Success && result.Value!.RegisteredCount == 8 && game.Listings.All(i => i.InstanceId is >= 102 and <= 109), "existing whitelist and blacklist semantics");
        var full = new Simulation(12);
        result = await full.Run();
        Require(result.Success && result.Value!.RegisteredCount == 10 && result.Value.RemainingCount == 2 && full.Starts == 1, "ten-slot capacity with explicit remainder");
    }

    public static async Task WrongHoverAndExistingShopAsync()
    {
        var wrong = new Simulation(1) { WrongHover = true };
        Require(!(await wrong.Run()).Success && !wrong.Input.MouseCommands.Contains("down:Right"), "wrong hover cannot right click");
        var selling = new Simulation(1) { Selling = true, ShopOpen = true };
        var result = await selling.Run();
        Require(result.Success && result.Value!.AlreadySelling && selling.Input.MouseCommands.Count == 0 && !selling.Input.Keys.Any(), "active shop must not toggle off");
        var existing = new Simulation(1);
        existing.Listings.Add(new(100, 567, 1, 1));
        Require(!(await existing.Run()).Success && existing.Input.MouseCommands.Count == 0, "existing plan cannot be replaced");
    }

    public static async Task BadEditorAndCancellationAsync()
    {
        var wrong = new Simulation(1) { WrongEditorQuantity = true };
        Require(!(await wrong.Run()).Success && wrong.Confirms == 0 && wrong.Starts == 0, "wrong quantity cannot confirm");
        using var cancel = new CancellationTokenSource();
        var game = new Simulation(1);
        game.Input.AfterMouseDown = _ => cancel.Cancel();
        try { await game.Run(cancel.Token); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { }
        Require(game.Input.MouseCommands.Last() == "up:Right" && game.Input.KeyUps.Contains("ControlKey") && game.Starts == 0, "cancellation releases input and never sells");
    }

    public static async Task RejectedStartAsync()
    {
        var game = new Simulation(1) { RejectStart = true };
        var result = await game.Run();
        Require(!result.Success && game.Starts == 1 && !game.Selling, "unconfirmed start must never click the toggle twice");
    }

    public static Task StablePublicationAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.PersonalShop;
        var context = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        var time = DateTimeOffset.Now;
        var failed = OperationResult<PersonalShopSnapshot>.Fail("short read");
        Require(!store.Resolve("s1", channel, context, failed, time).Result.Success, "cold start cannot fabricate empty shop");
        var first = new Simulation(1).Snapshot();
        store.Resolve("s1", channel, context, OperationResult<PersonalShopSnapshot>.Ok(first), time);
        Require(ReferenceEquals(first, store.Resolve("s1", channel, context, failed, time.AddHours(1)).Result.Value), "failed reads retain official shop without TTL");
        var changed = first with { IsSelling = true };
        Require(ReferenceEquals(changed, store.Resolve("s1", channel, context, OperationResult<PersonalShopSnapshot>.Ok(changed), time).Result.Value), "valid state publishes immediately");
        Require(!store.Resolve("s2", channel, context, failed, time).Result.Success, "session isolation");
        store.ClearSession("s1");
        Require(!store.Resolve("s1", channel, context, failed, time).Result.Success, "session invalidation");
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal sealed class Simulation
    {
        internal readonly FakeGameApi Api = new();
        internal readonly RecordingKeyboardInput Input = new();
        internal readonly List<PersonalShopListing> Listings = new();
        internal readonly List<int> Delays = new();
        internal MaintenanceScriptSettings Settings = new()
        {
            BagCleanupRules = new() { new() { Key = BagCleanupRuleCatalog.BlueEquipment, Enabled = true, Action = BagCleanupAction.Sell } }
        };
        internal bool Selling, ShopOpen, BagOpen, WrongHover, WrongEditorQuantity, RejectStart;
        internal int Starts, Confirms;
        private InventoryItemSnapshot? _editing;
        private ulong _price = 999;
        private GameUiPoint _cursor = new(500, 500);
        private static readonly GameUiPoint Price = new(400, 400), Confirm = new(460, 420), Start = new(270, 430);
        private static GameUiPoint ItemPoint(int index) => new(50 + index % 9 * 35, 60 + index / 9 * 35);
        private bool At(GameUiPoint point) => Math.Abs(point.X - _cursor.X) <= 1 && Math.Abs(point.Y - _cursor.Y) <= 1;

        internal Simulation(int count)
        {
            Api.InventoryItems = Enumerable.Range(0, count).Select(i => new InventoryItemSnapshot(567, (uint)(100 + i), "item" + i, 1, i, false, 7, 3)).ToArray();
            Api.PersonalShopRead = Snapshot;
            Api.UiCursorRead = () => new(1024, 768, _cursor);
            Input.AfterMove = (x, y) => _cursor = new(_cursor.X + (int)Math.Round(x * .8), _cursor.Y + (int)Math.Round(y * .8));
            Input.AfterPress = key =>
            {
                if (key == "Y") ShopOpen = !ShopOpen;
                if (key == "I") BagOpen = !BagOpen;
                if (key == "D1" && _editing != null && At(Price)) _price = 1;
            };
            var pressed = new HashSet<RoadhogMouseButton>();
            Input.AfterMouseDown = button => pressed.Add(button);
            Input.AfterMouseUp = button =>
            {
                if (!pressed.Remove(button)) return;
                if (button == RoadhogMouseButton.Right && ShopOpen && BagOpen && _editing == null)
                {
                    _editing = Api.InventoryItems.SingleOrDefault(i => At(ItemPoint(i.Slot)));
                    _price = 999;
                }
                if (button != RoadhogMouseButton.Left) return;
                if (_editing != null && At(Confirm))
                {
                    Confirms++;
                    Listings.Add(new((uint)_editing.InstanceId, _editing.TemplateId, _editing.Count, _price));
                    _editing = null;
                }
                else if (_editing == null && !BagOpen && At(Start)) { Starts++; Selling = !RejectStart; }
            };
        }
        internal PersonalShopSnapshot Snapshot() => new(ShopOpen, Selling, BagOpen,
            BagOpen ? Api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, ItemPoint(i.Slot))).ToArray() : Array.Empty<InventoryUiItem>(),
            WrongHover ? 999U : (uint)(Api.InventoryItems.SingleOrDefault(i => At(ItemPoint(i.Slot)))?.InstanceId ?? 0),
            Listings.ToArray(), _editing == null ? null : new((uint)_editing.InstanceId, true, _price, _price * _editing.Count,
                WrongEditorQuantity ? 0 : _editing.Count, Price, Confirm), ShopOpen && Listings.Count > 0 && !Selling ? Start : null);
        internal Task<OperationResult<PersonalShopTestResult>> Run(CancellationToken token = default)
        {
            var logger = new InMemoryRoadhogLogger();
            return new PersonalShopSequence(Input, logger, (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Delays.Add(ms);
                return Task.Delay(1, ct);
            }).RunAsync(Api.Create(new AccountConfig(), logger, token), "test", Settings, null, token);
        }
    }
}
