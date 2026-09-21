using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class NpcSaleTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Task Fast(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    public static Task SnapshotAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.NpcTrade;
        var context = new GameApiReadContext("npc", 1, "Aion.bin", "fake"); var now = DateTimeOffset.UtcNow;
        var failure = OperationResult<NpcTradeSnapshot>.Fail("short capture");
        var ready = new NpcTradeSnapshot(false, new Dictionary<string, GameUiPoint>(), true, 1, 42, false,
            new[] { new NpcTradeItem(11, 21, 3) }, new(250, 300), false);
        Require(!store.Resolve("a", channel, context, failure, now).Result.Success, "cold failure does not fabricate closed");
        store.Resolve("a", channel, context, OperationResult<NpcTradeSnapshot>.Ok(ready), now);
        Require(ReferenceEquals(ready, store.Resolve("a", channel, context, failure, now.AddDays(3)).Result.Value), "retain official UI after failure without TTL");
        Require(!store.Resolve("b", channel, context, failure, now).Result.Success, "session isolation");
        var closed = ready with { IsOpen = false, Basket = Array.Empty<NpcTradeItem>(), SellButton = null };
        store.Resolve("a", channel, context, OperationResult<NpcTradeSnapshot>.Ok(closed), now);
        Require(store.Resolve("a", channel, context, failure, now).Result.Value == closed, "valid empty replaces old basket");
        store.ClearSession("a");
        Require(!store.Resolve("a", channel, context, failure, now).Result.Success, "lifecycle invalidates");
        const ulong module = 0x180000000;
        byte[] Memory(ulong address, int size) => address == module + 0xDACF00 && size == 16
            ? BitConverter.GetBytes(1024d).Concat(BitConverter.GetBytes(768d)).ToArray() : new byte[size];
        Require(!new InventoryInteractionDecoder(Memory).ReadNpcTrade(module).IsOpen, "valid null roots publish closed");
        try { new InventoryInteractionDecoder((_, _) => Array.Empty<byte>()).ReadNpcTrade(module); throw new Exception("short accepted"); }
        catch (InvalidDataException) { }
        int reads = 0;
        try
        {
            new InventoryInteractionDecoder((a, n) => a == module + 0xD63990 + 134 * 8 && ++reads == 2 ? BitConverter.GetBytes(0x200000UL) : Memory(a, n)).ReadNpcTrade(module);
            throw new Exception("changed root accepted");
        }
        catch (InvalidDataException) { }
        return Task.CompletedTask;
    }

    public static async Task FlowAsync()
    {
        foreach (var scenario in new[] { "complete", "existing_basket", "wrong_npc", "foreign_item", "wrong_quantity", "hover_changed", "money_only", "stop" })
        {
            var api = new FakeGameApi { TargetName = "merchant", TargetOwnServerObjectId = 42, InventoryMoney = 100 };
            var input = new RecordingKeyboardInput();
            input.AfterMove = (x, y) => api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y);
            var items = Enumerable.Range(0, 4).Select(i => new InventoryItemSnapshot(21, (ulong)(11 + i), "equipment", 1, i, false, 7, 1)).ToArray();
            var reserved = new InventoryItemSnapshot(22, 30, "protected", 5, 4, false, 9, 2);
            api.InventoryItems = items.Append(reserved).ToArray();
            var settings = new ScriptSettings();
            settings.Maintenance.BagCleanupRules = new() { new() { Key = BagCleanupRuleCatalog.WhiteEquipment, Enabled = true, Action = BagCleanupAction.Sell } };
            var ui = new NpcTradeSnapshot(true, new Dictionary<string, GameUiPoint> { ["出售道具"] = new(169, 293) }, false, -1, 0, false, Array.Empty<NpcTradeItem>(), null, false);
            bool open = true, held = false;
            var submits = 0; var rights = 0;
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(scenario == "money_only" ? 1 : 3));
            InventoryUiItem[] Bag() => api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, new(700 + i.Slot * 32, 142))).ToArray();
            api.InventoryInteractionRead = () => new(open, false, false, Bag(), scenario == "hover_changed" ? 99u : Bag().FirstOrDefault(i => i.Point == api.InventoryUiCursor)?.InstanceId ?? 0, 0, null, new(512, 384), false);
            api.NpcTradeRead = () => ui with { InventoryOpen = open, SellButton = ui.IsSelling && !open ? new(280, 400) : null };
            input.AfterPress = key => { if (key == "I") open = !open; else throw new Exception("unexpected key " + key); };
            input.AfterMouseDown = _ => held = true;
            input.AfterMouseUp = button =>
            {
                if (!held) return; held = false;
                var cursor = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    rights++;
                    var item = Bag().Single(i => i.Point == cursor);
                    Require(item.InstanceId != reserved.InstanceId, "reserved item is never clicked");
                    ui = ui with { Basket = ui.Basket.Append(new NpcTradeItem(scenario == "foreign_item" ? 999u : item.InstanceId, item.TemplateId,
                        scenario == "wrong_quantity" ? 99UL : item.Quantity)).ToArray() };
                    if (scenario == "stop") stop.Cancel();
                }
                else if (cursor == new GameUiPoint(169, 293))
                    ui = ui with { DialogOpen = false, IsOpen = true, Mode = 1, NpcServerObjectId = scenario == "wrong_npc" ? 99u : 42u,
                        Basket = scenario == "existing_basket" ? new[] { new NpcTradeItem(999, 22, 1) } : Array.Empty<NpcTradeItem>() };
                else if (cursor == new GameUiPoint(280, 400))
                {
                    Require(!open && ui.Basket.Count is > 0 and <= 3, "closed bag and correct batch cap");
                    submits++; api.InventoryMoney += 50;
                    if (scenario != "money_only") api.InventoryItems = api.InventoryItems.Where(i => ui.Basket.All(b => b.InstanceId != i.InstanceId)).ToArray();
                    ui = ui with { Basket = Array.Empty<NpcTradeItem>() };
                }
                else throw new Exception("unexpected click " + cursor);
            };
            var logger = new InMemoryRoadhogLogger();
            var context = new AccountWorkerContext(new() { ScriptSettings = settings }, api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            var state = new BagCleanupState(); state.Start(0, 0);
            Exception? failure = null;
            try { await new NpcSaleSequence(input, Fast).RunAsync(context, "merchant", state); }
            catch (Exception ex) { failure = ex; }
            if (scenario == "complete")
                Require(failure == null && submits == 2 && rights == 4 && api.InventoryItems.Single() == reserved && state.TotalMoneyDelta == 100 && !open,
                    "two verified batches preserve other items and complete without moving bag");
            else
            {
                Require(failure != null && submits == (scenario == "money_only" ? 1 : 0), "unsafe or unconfirmed operation must not submit/re-submit: " + scenario);
                Require(state.SellBatchCount == 0, "money alone or changed basket is not a successful sale");
            }
            Require(!held, "release mouse on every exit");
        }
    }
}
