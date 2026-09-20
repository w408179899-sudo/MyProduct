using Roadhog.Application.BagCleanup;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class InventoryDiscardTests
{
    public static async Task AutomaticDelayedTwelfthDialogAsync()
    {
        var game = new Simulation(13);
        game.Api.TargetEntityId = 0;
        game.Api.TargetIsTargetingLocalPlayer = false;
        game.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        game.Api.InventoryCapacity = 100;
        game.Settings.BagCleanupEnabled = true;
        game.Settings.BagCleanupThreshold = 1;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var worker = new AccountWorkerContext(new AccountConfig { AccountName = "test", ScriptSettings = new() { Maintenance = game.Settings } },
            new RoadhogSnapshotReaderFactory(game.Api), logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var readContext = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        var faultCount = 0;
        var pendingOnly = false;
        var awaitingButton = false;
        game.Api.InventoryInteractionRead = () =>
        {
            var actual = game.Snapshot();
            OperationResult<InventoryInteractionSnapshot> raw;
            if (actual.PendingDiscardInstanceId == 111 && faultCount < 8)
            {
                faultCount++;
                raw = OperationResult<InventoryInteractionSnapshot>.Fail("Inventory UI changed during capture.");
            }
            else if (actual.PendingDiscardInstanceId == 111 && !pendingOnly)
            {
                pendingOnly = true;
                raw = OperationResult<InventoryInteractionSnapshot>.Ok(actual with { DiscardDialog = null });
            }
            else if (actual.PendingDiscardInstanceId == 111 && !awaitingButton)
            {
                awaitingButton = true;
                raw = OperationResult<InventoryInteractionSnapshot>.Ok(actual with { DiscardDialog = actual.DiscardDialog! with { ConfirmButton = null } });
            }
            else raw = OperationResult<InventoryInteractionSnapshot>.Ok(actual);
            var official = store.Resolve("session", AionVmmSnapshotChannels.InventoryInteraction, readContext, raw, DateTimeOffset.UtcNow).Result;
            Require(official.Success, "test must prime the provider before injecting a fault");
            // The old independent channel claims a foreign item is ready. It must never be read.
            game.Api.InventoryDiscardConfirm = new(true, 999, InventoryDiscardConfirmKind.Special, 356, 0, DateTimeOffset.Now);
            return official.Value!;
        };
        var seller = new BagCleanupSeller(game.Input);
        var controller = new BagCleanupController(game.Input, new InMemorySharedPathStore(),
            (_, _, _) => throw new Exception("must not start NPC cleanup"), seller: seller,
            discarder: new BagCleanupDiscarder(game.Input, seller, (_, ct) => Task.Delay(1, ct)));
        var state = new BagCleanupState(); state.StartDiscard(0, 1, 13);
        var waited = 0;
        while (state.Active)
        {
            stop.Token.ThrowIfCancellationRequested();
            var result = await controller.TickAfterLootAsync(worker, state);
            if (result.Reason == "waiting_for_discard_confirm")
            {
                waited++;
                Require(game.Confirms == 11, "delayed twelfth dialog must not be clicked early");
            }
            if (result.Status != BagCleanupTickStatus.Running)
                Require(result.Reason == "discard_completed_capacity_recovered", result.Reason);
        }
        Require(faultCount == 8 && waited >= 8 && pendingOnly && awaitingButton, "exercise retained publication and staged dialog readiness");
        Require(game.Removed.Count == 13 && game.Confirms == 13 && !game.Open, "clear item thirteen and close inventory");
        Require(game.Api.LastInventoryDiscardConfirmContext == null, "never consume the independent legacy confirmation channel");
        Require(!logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.failed"), "delayed dialog must not abort automatic cleanup");
    }

    public static async Task DialogReadinessAsync()
    {
        var game = new Simulation(1) { Open = true };
        var logger = new InMemoryRoadhogLogger();
        var snapshots = game.Api.Create(new AccountConfig(), logger, CancellationToken.None);
        var actions = new InventoryDiscardActions(game.Input, snapshots, logger, "test", (_, ct) => Task.Delay(1, ct));
        var item = game.Api.InventoryItems.Single();
        await actions.DragAsync(item, CancellationToken.None);
        var reads = 0;
        game.Api.InventoryInteractionRead = () =>
        {
            var ui = game.Snapshot();
            return ++reads <= 5 ? ui with { PendingDiscardInstanceId = 0, DiscardDialog = null } : ui;
        };
        await actions.ConfirmAsync(item, InventoryDiscardConfirmKind.Normal, CancellationToken.None);
        Require(reads > 5 && game.Confirms == 1 && game.Removed.Count == 1, "click only after delayed dialog appears");

        var empty = new Simulation(1) { Open = true };
        var missing = new InventoryDiscardActions(empty.Input, empty.Api.Create(new AccountConfig(), logger, CancellationToken.None),
            logger, "test", (_, ct) => Task.Delay(1, ct), TimeSpan.FromMilliseconds(40));
        try { await missing.ConfirmAsync(empty.Api.InventoryItems.Single(), InventoryDiscardConfirmKind.Normal, CancellationToken.None); throw new Exception("must time out"); }
        catch (TimeoutException) { }
        Require(!empty.Input.MouseCommands.Contains("down:Left"), "missing dialog times out without clicking");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await missing.WaitForDialogAsync(empty.Api.InventoryItems.Single(), cancel.Token); throw new Exception("must cancel"); }
        catch (OperationCanceledException) { }
    }

    public static async Task CancellationUsesUiAsync()
    {
        var game = new Simulation(1) { Open = true };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = new AccountWorkerContext(new AccountConfig(), new RoadhogSnapshotReaderFactory(game.Api), logger,
            new AccountRuntimeManager(logger), new(), stop.Token);
        await new InventoryDiscardActions(game.Input, context.Snapshots, logger, "test", (_, ct) => Task.Delay(1, ct))
            .DragAsync(game.Api.InventoryItems.Single(), stop.Token);
        // Legacy channel falsely says closed; cancellation must follow the shared UI channel.
        game.Api.InventoryDiscardConfirm = InventoryDiscardConfirmSnapshot.Closed(DateTimeOffset.Now);
        var discarded = await new BagCleanupDiscarder(game.Input, new BagCleanupSeller(game.Input)).CancelPendingDiscardAsync(context);
        Require(discarded.Success && game.Input.Keys.Contains("Escape"), "send Escape and verify pending state cleared");
        Require(game.Snapshot().PendingDiscardInstanceId == 0 && game.Removed.Count == 0, "cancel never deletes the item");
        Require(game.Api.LastInventoryDiscardConfirmContext == null, "cancellation must not use the legacy channel");
    }

    public static async Task LimitAndRulesAsync()
    {
        var game = new Simulation(6);
        game.Settings.BagCleanupExcludedItemNames = new() { "item0" };
        game.Api.InventoryItems = game.Api.InventoryItems.Select(i => i.InstanceId == 101 ? i with { Count = 26 } : i).ToArray();
        var result = await game.Run();
        Require(result.Success && result.Value is { DiscardedCount: 3, RemainingCount: 2 }, result.Error ?? "three-item limit");
        Require(game.Removed.SequenceEqual(new ulong[] { 101, 102, 103 }) && game.Api.InventoryItems.Any(i => i.InstanceId == 100), "whitelist and cap protect other items");
        Require(game.Drags == 3 && game.Confirms == 3 && game.Input.MouseCommands.All(c => !c.Contains("-2000")), "feedback drag without legacy corner reset");
        var empty = new Simulation(1); empty.Settings.BagCleanupRules.Clear();
        Require((await empty.Run()).Success && empty.Input.MouseCommands.Count == 0 && empty.Input.Keys.Count == 0, "no candidates means no input");
        var sell = new Simulation(1); sell.Settings.BagCleanupRules[0].Action = BagCleanupAction.Sell;
        Require((await sell.Run()).Value!.DiscardedCount == 0, "sell items are never discarded");
    }

    public static async Task HoverModalAndReleaseAsync()
    {
        foreach (var kind in new[] { "hover", "modal", "shop", "drop" })
        {
            var game = new Simulation(1) { WrongHover = kind == "hover", ForeignModal = kind == "modal", ShopOpen = kind == "shop", NoDropPoint = kind == "drop" };
            Require(!(await game.Run()).Success && !game.Input.MouseCommands.Contains("down:Left"), kind + " must stop before mouse-down");
        }
        var foreign = new Simulation(1) { ForeignDialog = true };
        Require(!(await foreign.Run()).Success && foreign.Confirms == 0 && foreign.Removed.Count == 0, "foreign dialog never confirmed");
        using var cancel = new CancellationTokenSource();
        var cancelled = new Simulation(1);
        var original = cancelled.Input.AfterMouseDown;
        cancelled.Input.AfterMouseDown = b => { original?.Invoke(b); cancel.Cancel(); };
        try { await cancelled.Run(cancel.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Require(cancelled.Input.MouseCommands.Contains("up:Left") && !cancelled.LeftHeld && cancelled.Removed.Count == 0, "cancel releases drag without confirming");
    }

    public static async Task ConfirmLayersAndRejectionAsync()
    {
        var layers = new Simulation(1) { TwoLayers = true };
        Require((await layers.Run()).Success && layers.Confirms == 2 && layers.Removed.Count == 1, "special then ordinary confirmation");
        var rejected = new Simulation(1) { RejectConfirm = true };
        Require(!(await rejected.Run()).Success && rejected.Confirms == 1 && rejected.Removed.Count == 0, "no blind confirmation retries");
    }

    public static Task OfficialLifecycleAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.InventoryInteraction;
        var context = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        var failed = OperationResult<InventoryInteractionSnapshot>.Fail("short read");
        var now = DateTimeOffset.Now;
        Require(!store.Resolve("s", channel, context, failed, now).Result.Success, "cold start does not fabricate absence");
        var snapshot = new Simulation(1).Snapshot();
        store.Resolve("s", channel, context, OperationResult<InventoryInteractionSnapshot>.Ok(snapshot), now);
        Require(ReferenceEquals(snapshot, store.Resolve("s", channel, context, failed, now.AddHours(2)).Result.Value), "failed read retains official snapshot");
        var updated = snapshot with { PendingDiscardInstanceId = 100 };
        Require(ReferenceEquals(updated, store.Resolve("s", channel, context, OperationResult<InventoryInteractionSnapshot>.Ok(updated), now).Result.Value), "valid update publishes immediately");
        store.ClearSession("s");
        Require(!store.Resolve("s", channel, context, failed, now).Result.Success, "session invalidation");
        return Task.CompletedTask;
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    internal sealed class Simulation
    {
        internal readonly FakeGameApi Api = new();
        internal readonly RecordingKeyboardInput Input = new();
        internal readonly List<ulong> Removed = new();
        internal readonly MaintenanceScriptSettings Settings = new()
        { BagCleanupRules = new() { new() { Key = BagCleanupRuleCatalog.WhiteEquipment, Enabled = true, Action = BagCleanupAction.Discard } } };
        internal bool Open, WrongHover, ForeignModal, ShopOpen, NoDropPoint, ForeignDialog, TwoLayers, RejectConfirm, LeftHeld;
        internal int Drags, Confirms;
        private uint _pending;
        private bool _normalLayer;
        private uint _heldItem;
        private GameUiPoint _cursor = new(500, 500);
        private static readonly GameUiPoint Drop = new(600, 300), Confirm = new(700, 400);
        // Slot locations shift after every deletion, proving each item is located again by identity.
        private GameUiPoint Point(InventoryItemSnapshot item) => new(80 + Array.IndexOf(Api.InventoryItems.ToArray(), item) * 40, 100);
        private bool At(GameUiPoint point) => Math.Abs(point.X - _cursor.X) <= 1 && Math.Abs(point.Y - _cursor.Y) <= 1;
        internal Simulation(int count)
        {
            Api.InventoryItems = Enumerable.Range(0, count).Select(i => new InventoryItemSnapshot(567, (uint)(100 + i), "item" + i, 1, i, false, 7, 1)).ToArray();
            Api.InventoryInteractionRead = Snapshot;
            Api.UiCursorRead = () => new(1024, 768, _cursor);
            Input.AfterPress = key =>
            {
                if (key == "I") Open = !Open;
                if (key == "Escape") { _pending = 0; _normalLayer = false; }
            };
            Input.AfterMove = (x, y) => _cursor = new(_cursor.X + (int)Math.Round(x * .8), _cursor.Y + (int)Math.Round(y * .8));
            Input.AfterMouseDown = button =>
            {
                if (button != RoadhogMouseButton.Left) return;
                LeftHeld = true;
                _heldItem = _pending == 0 ? (uint)(Api.InventoryItems.SingleOrDefault(i => At(Point(i)))?.InstanceId ?? 0) : 0;
            };
            Input.AfterMouseUp = button =>
            {
                if (button != RoadhogMouseButton.Left || !LeftHeld) return;
                LeftHeld = false;
                if (_heldItem != 0 && At(Drop)) { Drags++; _pending = _heldItem; _heldItem = 0; return; }
                if (_pending == 0 || !At(Confirm)) return;
                Confirms++;
                if (RejectConfirm) return;
                if (TwoLayers && !_normalLayer) { _normalLayer = true; return; }
                Removed.Add(_pending);
                Api.InventoryItems = Api.InventoryItems.Where(i => i.InstanceId != _pending).ToArray();
                _pending = 0; _normalLayer = false;
            };
        }
        internal InventoryInteractionSnapshot Snapshot() => new(Open, ShopOpen, false,
            Open && _pending == 0 ? Api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, Point(i))).ToArray() : Array.Empty<InventoryUiItem>(),
            WrongHover ? 999 : (uint)(Api.InventoryItems.SingleOrDefault(i => At(Point(i)))?.InstanceId ?? 0),
            _pending, _pending == 0 ? null : new(ForeignDialog ? 999U : _pending,
                TwoLayers && !_normalLayer ? InventoryDiscardConfirmKind.Special : InventoryDiscardConfirmKind.Normal,
                TwoLayers && !_normalLayer ? 356 : 336, Confirm, new(770, 400)), NoDropPoint ? null : Drop, ForeignModal);
        internal Task<OperationResult<InventoryDiscardTestResult>> Run(CancellationToken token = default)
        {
            var logger = new InMemoryRoadhogLogger();
            return new InventoryDiscardSequence(Input, logger, (ms, ct) => Task.Delay(1, ct))
                .RunAsync(Api.Create(new AccountConfig(), logger, token), "test", Settings, null, token);
        }
    }
}
