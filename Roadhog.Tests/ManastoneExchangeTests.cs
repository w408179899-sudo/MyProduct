using Roadhog.Application.ManastoneExchange;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class ManastoneExchangeTests
{
    public static async Task LoopAsync()
    {
        var game = new Simulation(25, 35);
        var untouched = game.Api.InventoryItems.Where(i => i.TemplateId == 167000358 || i.QualityRank == 2).ToArray();
        var result = await game.Run();
        Require(result.Success && result.Value is { Bags: 2, Bundles: 3, MaterialsExhausted: true }, result.Error ?? "both tasks exhausted");
        Require(game.Confirmed.SequenceEqual(new uint[] { 4113, 4114, 4113, 4114, 4114 }), "continue the second task after the first becomes short");
        Require(game.Confirmed.Count == 5 && game.Accepted.Count == 5 && game.Submitted.Count == 5, "each completed round accepts, hands over, and confirms once");
        foreach (var quest in ManastoneExchangePlan.Quests)
            foreach (var id in quest.MaterialIds)
                Require(ManastoneExchangePlan.Count(game.Api.InventoryItems, id, true) == 5, "keep insufficient remainder: " + id);
        Require(untouched.All(item => game.Api.InventoryItems.Contains(item)), "other white stones and all green stones remain untouched");
        Require(game.Input.Releases == 1, "completed batch releases input");

        var empty = new Simulation(9, 9);
        var insufficient = empty.Api.InventoryItems.First(i => i.TemplateId == 167000354);
        empty.Api.InventoryItems = empty.Api.InventoryItems
            .Append(insufficient with { InstanceId = 9100, Slot = 31, QualityRank = 2, Count = 100 })
            .Append(insufficient with { InstanceId = 9101, Slot = 32, IsEquipped = true, Count = 100 })
            .Append(insufficient with { InstanceId = 9102, Slot = -1, Count = 100 }).ToArray();
        result = await empty.Run();
        Require(result.Success && result.Value is { Bags: 0, Bundles: 0, MaterialsExhausted: true }, "initial shortage is a normal completion");
        Require(empty.Input.Record.Keys.SequenceEqual(new[] { "F1", "F8" }) && empty.Input.Record.MouseCommands.Count == 0, "press F1 once before selecting the fixed NPC without accepting unfinishable quests");

        var split = new Simulation(10, 0);
        var item = split.Api.InventoryItems.First(i => i.TemplateId == 167000354);
        split.Api.InventoryItems = split.Api.InventoryItems.Where(i => i != item)
            .Append(item with { Count = 4 }).Append(item with { InstanceId = 9000, Slot = 30, Count = 6 }).ToArray();
        result = await split.Run();
        Require(result.Success && result.Value is { Bags: 1, Bundles: 0, MaterialsExhausted: true }, "sum matching white materials across bag stacks");
    }

    public static async Task RecycleAsync()
    {
        const uint bag = 188051284, bundle = 188051283;
        var game = new Simulation(9, 29) { LootDelayPolls = 3 };
        game.AddPackage(bag, 9401, 3, 22);
        game.AddPackage(bag, 9402, 1, 23);
        game.AddPackage(bundle, 9403, 2, 24);
        game.AddPackage(188051285, 9404, 5, 25);
        game.Loot = used => used.InstanceId == 9401
            ? [(167000354, 1, 1), (167000356, 1, 1), (167000361, 1, 1), (167000515, 27, 2)]
            : [(167000359, used.Count, 1), (167000515, 9 * used.Count, 2)];

        var result = await game.Run();

        Require(result.Success && result.Value is { Bags: 1, Bundles: 2, MaterialsExhausted: true }, result.Error ?? "recycling finishes after new materials are consumed");
        Require(game.Confirmed.SequenceEqual(new uint[] { 4114, 4114, 4113 }), "finish the available task before opening and then use delayed drops for the other task");
        Require(game.Actions.Take(2).SequenceEqual(new[] { "exchange:4114", "exchange:4114" }), "opening starts only after both recipes are short");
        Require(game.Actions.IndexOf("exchange:4113") == 5, "finish all three original stacks and their drops before returning to the NPC");
        Require(game.Opened.Select(i => (i.InstanceId, i.Count)).ToHashSet().SetEquals(
            new (ulong, uint)[] { (9401, 3), (9402, 1), (9403, 4), (bag, 1) }), "open split stacks, remaining singles, and old and newly earned stock together");
        Require(game.AllClicks == 2 && game.OpenConfirms == 2, "use ALL and confirm exactly once for each multi-item stack");
        Require(game.Api.InventoryItems.Any(item => item.InstanceId == 9404 && item.Count == 5), "other packages remain untouched");
        Require(game.BagOpen && game.Input.Record.Keys.Count(key => key == "I") == 3 && game.Input.Releases == 1,
            "close the bag before revisiting the NPC, then finish without another bag toggle");
    }

    public static async Task SequentialOpeningAsync()
    {
        var game = new Simulation(9, 9) { SequentialLoot = true, LootDelayPolls = 3 };
        game.AddPackage(188051284, 9401, 5, 22);

        var result = await game.Run();

        Require(result.Success && result.Value is { MaterialsExhausted: true }, result.Error ?? "sequential ALL waits for the final package and its drops");
        Require(game.PackageRemainders.SequenceEqual(new uint[] { 4, 3, 2, 1, 0 }) &&
            game.OpenRightClicks == 1 && game.AllClicks == 1 && game.OpenConfirms == 1,
            "ALL is submitted once and allowed to consume every unit sequentially");
        Require(ManastoneExchangePlan.Count(game.Api.InventoryItems, 167000359) == 5 &&
            ManastoneExchangePlan.Count(game.Api.InventoryItems, 167000515) == 45,
            "confirm all ten drops per package across both white and green qualities");
    }

    public static async Task OpenGuardsAsync()
    {
        foreach (var acknowledgement in new[] { "wrong-item", "wrong-maximum", "all-incomplete", "no-decrement", "no-loot" })
        {
            var game = new Simulation(9, 9) { OpenAcknowledgement = acknowledgement };
            game.AddPackage(188051284, 9401, 3, 22);

            var result = await game.Run();

            Require(!result.Success, acknowledgement + " cannot indicate materials exhausted");
            var expectedConfirmations = acknowledgement is "no-decrement" or "no-loot" ? 1 : 0;
            Require(game.OpenRightClicks == 1 && game.OpenConfirms == expectedConfirmations,
                acknowledgement + " neither retries the package nor confirms an invalid popup");
            Require(game.Confirmed.Count == 0 && game.Input.Releases == 1, acknowledgement + " leaves the exchange stopped and releases input");
        }
    }

    public static async Task OpenCancellationAndLimitAsync()
    {
        using var stop = new CancellationTokenSource();
        var cancelled = new Simulation(9, 9) { AfterAll = stop.Cancel };
        cancelled.AddPackage(188051284, 9401, 3, 22);
        var result = await cancelled.Run(stop.Token);
        Require(result.Success && result.Value is { MaterialsExhausted: false } && cancelled.AllClicks == 1 &&
            cancelled.OpenConfirms == 0 && cancelled.Input.Releases == 1, "stop after ALL before clicking confirm");
        Require(cancelled.Api.InventoryItems.Single(i => i.InstanceId == 9401).Count == 3, "cancellation preserves the unopened stack");

        var limited = new Simulation(20, 20);
        limited.AddPackage(188051284, 9401, 3, 22);
        result = await limited.Run(maximumCompletions: 1);
        Require(result.Success && result.Value is { Bags: 1, Bundles: 0, MaterialsExhausted: false } &&
            limited.OpenRightClicks == 0 && limited.Confirmed.Count == 1, "the live probe limit stops before starting the package phase");
    }

    public static async Task ResumeAsync()
    {
        foreach (var phase in new[] { 3, 4 })
        {
            var game = new Simulation(phase == 4 ? 0U : 10U, 0) { Phase = phase, ActiveQuest = 4113 };
            if (phase == 4) game.AddPackage(188051284, 9401, 2, 22);
            var result = await game.Run();
            Require(result.Success && result.Value is { Bags: 1, Bundles: 0, MaterialsExhausted: true }, "resume known task phase " + phase + ": " + result.Error);
            Require(game.Accepted.Count == 0 && game.Confirmed.Count == 1, "resume does not accept an already accepted task");
            Require(game.Submitted.Count == (phase == 3 ? 1 : 0), "resume rewards does not hand over twice");
            if (phase == 4)
                Require(game.Actions.First() == "exchange:4113" && game.Opened.Single().Count == 3,
                    "collect a pending reward before opening the existing stack");
        }
        var shortActive = new Simulation(9, 10) { Phase = 3, ActiveQuest = 4113 };
        var switched = await shortActive.Run();
        Require(switched.Success && switched.Value is { Bags: 0, Bundles: 1, MaterialsExhausted: true }, "close material-short active task and finish the other: " + switched.Error);
        Require(shortActive.Closed == 1 && shortActive.Confirmed.SequenceEqual(new uint[] { 4114 }), "leave short active task without submitting it");
    }

    public static async Task GuardsAsync()
    {
        foreach (var scenario in new[] { "wrong-target", "foreign-dialog", "foreign-quest", "wrong-reward", "changed-target", "modal" })
        {
            var game = new Simulation(10, 10);
            switch (scenario)
            {
                case "wrong-target": game.Api.TargetName = "其他NPC"; break;
                case "foreign-dialog": game.Phase = 1; game.DialogNpc = 800; break;
                case "foreign-quest": game.Phase = 2; game.ActiveQuest = 4072; break;
                case "wrong-reward": game.WrongReward = true; break;
                case "changed-target": game.OnMove = () => game.Api.TargetOwnServerObjectId = 800; break;
                case "modal": game.OtherModal = true; break;
            }
            var result = await game.Run();
            Require(!result.Success && game.Confirmed.Count == 0, scenario + " prevents reward confirmation: " + result.Error);
            Require(game.Input.Releases == 1, scenario + " releases all input");
        }
    }

    public static async Task ConfirmationAsync()
    {
        foreach (var acknowledgement in new[] { "none", "reward-only", "materials-only", "wrong-material" })
        {
            var game = new Simulation(20, 20) { Acknowledgement = acknowledgement };
            var result = await game.Run();
            Require(!result.Success, acknowledgement + " cannot count an unverified completion");
            Require(game.Submitted.SequenceEqual(new uint[] { 4113 }) && game.Confirmed.Count <= 1 && game.Confirmed.All(id => id == 4113),
                acknowledgement + " never repeats handover/confirm or advances to another task");
            Require(game.Logger.Entries.All(e => e.EventName != "manastone_exchange.completed"), acknowledgement + " never logs completed");
            Require(game.Input.Releases == 1, acknowledgement + " releases input after timeout");
        }
    }

    public static async Task CancellationAsync()
    {
        using var stop = new CancellationTokenSource();
        var game = new Simulation(20, 20);
        game.Input.Record.AfterMouseDown = _ => { game.MouseHeld = true; stop.Cancel(); };
        var result = await game.Run(stop.Token);
        Require(result.Success && result.Value is { MaterialsExhausted: false, Bags: 0, Bundles: 0 }, "manual cancellation is distinct from exhausted completion");
        Require(game.Input.Record.MouseCommands.Last() == "up:Left" && !game.MouseHeld && game.Input.Releases == 1,
            "cancellation during click releases mouse and all input");
        Require(game.Confirmed.Count == 0, "cancelled batch does not submit a reward");

        using var afterHandoverStop = new CancellationTokenSource();
        var pending = new Simulation(10, 0) { AfterHandover = afterHandoverStop.Cancel };
        result = await pending.Run(afterHandoverStop.Token);
        Require(result.Success && result.Value is { Bags: 0, MaterialsExhausted: false } && pending.Phase == 4,
            "stop after paid handover leaves reward pending");
        Require(ManastoneExchangePlan.Quests[0].Available(pending.Api.InventoryItems) == 0 && pending.Confirmed.Count == 0,
            "handover consumed materials, cancellation did not confirm");
        pending.AfterHandover = null;
        result = await pending.Run();
        Require(result.Success && result.Value is { Bags: 1, MaterialsExhausted: true } && pending.Submitted.Count == 1 && pending.Confirmed.Count == 1,
            "resume paid pending reward without handing over again: " + result.Error);
    }

    private sealed class Simulation
    {
        public readonly FakeGameApi Api = new();
        public readonly ResettableInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly List<uint> Accepted = new(), Submitted = new(), Confirmed = new();
        public readonly List<string> Actions = new();
        public readonly List<InventoryItemSnapshot> Opened = new();
        public readonly List<uint> PackageRemainders = new();
        public int Phase, Closed, OpenRightClicks, AllClicks, OpenConfirms, LootDelayPolls;
        public uint ActiveQuest, DialogNpc = 700;
        public bool WrongReward, OtherModal, MouseHeld, BagOpen, SequentialLoot;
        public string Acknowledgement = "all", OpenAcknowledgement = "all";
        public Action? OnMove, AfterHandover, AfterAll;
        public Func<InventoryItemSnapshot, IReadOnlyList<(uint TemplateId, uint Count, byte QualityRank)>>? Loot;
        private int _confirmationPolls, _openPolls;
        private InventoryItemSnapshot? _pendingUse, _pendingLoot;
        private uint _useQuantity, _settledPackages;
        private static readonly GameUiPoint First = new(100, 100), Second = new(100, 150), Action = new(200, 200), Confirm = new(300, 300), Close = new(400, 400);
        private static readonly GameUiPoint All = new(550, 220), OpenConfirm = new(650, 220);

        public Simulation(uint bagMaterials, uint bundleMaterials)
        {
            Api.TargetName = "比洛加";
            Api.TargetOwnServerObjectId = 700;
            Api.InventoryItems = ManastoneExchangePlan.Quests.SelectMany((q, index) => q.MaterialIds.Select((id, slot) =>
                new InventoryItemSnapshot(id, id, "白色魔石", index == 0 ? bagMaterials : bundleMaterials, index * 3 + slot, false, 24, 1)))
                .Append(new(167000358, 999, "攻击力+3", 77, 6, false, 24, 1))
                .Append(new(167000514, 998, "绿色生命魔石", 99, 7, false, 24, 2))
                .ToArray();
            Api.ManastoneExchangeRead = Read;
            Api.InventoryInteractionRead = ReadBag;
            Input.Record.AfterPress = key =>
            {
                if (key == "C")
                {
                    Require(!BagOpen, "close the inventory before resuming NPC exchange");
                    Phase = 1;
                    ActiveQuest = 0;
                }
                if (key == "I")
                {
                    Require(Phase == 0, "finish or close the NPC dialog before opening the bag");
                    BagOpen = !BagOpen;
                }
            };
            Input.Record.AfterMove = (x, y) => { Api.InventoryUiCursor = new(Api.InventoryUiCursor.X + x, Api.InventoryUiCursor.Y + y); OnMove?.Invoke(); };
            Input.Record.AfterMouseDown = _ => MouseHeld = true;
            Input.Record.AfterMouseUp = button =>
            {
                if (!MouseHeld) return;
                MouseHeld = false;
                var point = Api.InventoryUiCursor;
                if (BagOpen)
                {
                    ClickBag(button, point);
                    return;
                }
                Require(button == RoadhogMouseButton.Left, "exchange uses left clicks only");
                if (point == Close) { Phase = 0; Closed++; return; }
                switch (Phase)
                {
                    case 1:
                        Require(point == First || point == Second, "only batch quest entry may be clicked");
                        ActiveQuest = point == First ? 4113U : 4114U; Phase = 2; break;
                    case 2:
                        Require(point == Action, "accept exact selected task"); Accepted.Add(ActiveQuest); Phase = 3; break;
                    case 3:
                        Require(point == Action, "hand over exact selected task"); Submitted.Add(ActiveQuest); ConsumeMaterials(); Phase = 4; AfterHandover?.Invoke(); break;
                    case 4:
                        Require(point == Confirm, "confirm exact reward"); Confirmed.Add(ActiveQuest); Actions.Add($"exchange:{ActiveQuest}"); GrantReward(); Phase = 0; break;
                    default: throw new Exception("clicked while closed");
                }
            };
        }

        public void AddPackage(uint templateId, uint instanceId, uint count, int slot) =>
            Api.InventoryItems = Api.InventoryItems.Append(new InventoryItemSnapshot(templateId, instanceId, "魔石奖励", count, slot, false, 42, 1)).ToArray();

        private InventoryInteractionSnapshot ReadBag()
        {
            var items = BagOpen && _pendingUse is null
                ? Api.InventoryItems.Where(i => !i.IsEquipped && i.Slot >= 0 && i.Count > 0)
                    .Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, Point(i))).ToArray()
                : Array.Empty<InventoryUiItem>();
            var hover = items.SingleOrDefault(i => i.Point == Api.InventoryUiCursor)?.InstanceId ?? 0;
            return new(BagOpen, false, false, items, hover, 0, null, null, OtherModal || _pendingUse is not null)
            {
                ItemUseQuantity = !OtherModal && _pendingUse is { } item
                    ? new(OpenAcknowledgement == "wrong-item" ? 9000U : (uint)item.InstanceId,
                        _useQuantity, OpenAcknowledgement == "wrong-maximum" ? item.Count + 1 : item.Count, All, OpenConfirm)
                    : null
            };
        }

        private static GameUiPoint Point(InventoryItemSnapshot item) =>
            new(100 + item.Slot % 10 * 47, 500 + item.Slot / 10 * 38);

        private void ClickBag(RoadhogMouseButton button, GameUiPoint point)
        {
            if (button == RoadhogMouseButton.Right)
            {
                var item = Api.InventoryItems.SingleOrDefault(i => !i.IsEquipped && i.Slot >= 0 && Point(i) == point);
                Require(_pendingUse is null && item is not null && ManastoneExchangePlan.Quests.Any(q => q.RewardId == item.TemplateId),
                    "right-click the intended reward stack only");
                OpenRightClicks++;
                Require(OpenAcknowledgement == "all" || OpenRightClicks == 1, "an unconfirmed opening must never be repeated");
                if (item!.Count == 1)
                    UsePackage(item);
                else
                {
                    _pendingUse = item;
                    _useQuantity = 1;
                }
                return;
            }
            var pending = _pendingUse ?? throw new Exception("clicked without the expected use quantity dialog");
            if (point == All)
            {
                AllClicks++;
                _useQuantity = OpenAcknowledgement == "all-incomplete" ? pending.Count - 1 : pending.Count;
                AfterAll?.Invoke();
                return;
            }
            Require(point == OpenConfirm && _useQuantity == pending.Count, "confirm only after ALL sets the exact stack quantity");
            OpenConfirms++;
            _pendingUse = null;
            UsePackage(pending);
        }

        private void UsePackage(InventoryItemSnapshot item)
        {
            if (!SequentialLoot && OpenAcknowledgement != "no-decrement")
                ConsumePackages(item.InstanceId, item.Count);
            if (OpenAcknowledgement == "no-loot") return;
            Require(_pendingLoot is null, "observe current drops before opening another stack");
            _pendingLoot = item;
            _openPolls = 0;
            _settledPackages = 0;
            if (LootDelayPolls == 0) SettleLoot();
        }

        private void ConsumePackages(ulong instanceId, uint amount)
        {
            var remaining = Api.InventoryItems.Single(i => i.InstanceId == instanceId).Count - amount;
            Api.InventoryItems = Api.InventoryItems.Select(i => i.InstanceId == instanceId ? i with { Count = remaining } : i)
                .Where(i => i.Count > 0).ToArray();
            PackageRemainders.Add(remaining);
        }

        private void SettleLoot()
        {
            var used = _pendingLoot!;
            var amount = SequentialLoot ? 1U : used.Count;
            if (SequentialLoot) ConsumePackages(used.InstanceId, amount);
            var drops = Loot?.Invoke(used with { Count = amount }) ?? [(167000359, amount, 1), (167000515, 9 * amount, 2)];
            var items = Api.InventoryItems.ToList();
            foreach (var (templateId, count, quality) in drops)
            {
                var index = items.FindIndex(i => i.TemplateId == templateId && i.ItemType == 24 && i.QualityRank == quality);
                if (index >= 0) items[index] = items[index] with { Count = items[index].Count + count };
                else items.Add(new(templateId, templateId, "开包掉落魔石", count, items.Max(i => i.Slot) + 1, false, 24, quality));
            }
            Api.InventoryItems = items.ToArray();
            _settledPackages += amount;
            _openPolls = 0;
            if (_settledPackages < used.Count) return;
            Opened.Add(used);
            Actions.Add($"open:{used.TemplateId}");
            _pendingLoot = null;
        }

        private ManastoneExchangeSnapshot Read()
        {
            if (Phase == 0) return new(false, 0, 0, 0, 0, "", "", Array.Empty<ManastoneDialogEntry>(), null, null, OtherModal);
            if (Phase == 1) return new(true, DialogNpc, 0, 5, 10, "select_quest", "", new[]
            {
                new ManastoneDialogEntry("去除魔石", "NpcFunc37", new(100, 50)),
                new ManastoneDialogEntry("43级 新任务：[魔石批量交换]比洛加的魔石袋", "QuestSelect4113", First),
                new ManastoneDialogEntry("43级 新任务：[魔石批量交换]比洛加的魔石包袱", "QuestSelect4114", Second),
                new ManastoneDialogEntry("43级 新任务：[魔石交换]研究用魔石", "QuestSelect4072", new(100, 250))
            }, null, Close, OtherModal);
            if (Phase is 2 or 3) return new(true, DialogNpc, ActiveQuest, 1, Phase == 2 ? 1011 : 1003,
                Phase == 2 ? "select1" : "quest_accept_1", "", new[] { new ManastoneDialogEntry(Phase == 2 ? "接受。" : "交出魔石。", "select1", Action) }, null, Close, OtherModal);
            var reward = WrongReward ? 188000001 : ManastoneExchangePlan.Quests.Single(q => q.QuestId == ActiveQuest).RewardId;
            return new(true, DialogNpc, ActiveQuest, 4, 5, "select_quest_reward1", $"<item itemnameid=\"{reward}\" itemcount=\"1\">奖励</item>",
                Array.Empty<ManastoneDialogEntry>(), Confirm, Close, OtherModal);
        }

        private void ConsumeMaterials()
        {
            var quest = ManastoneExchangePlan.Quests.Single(q => q.QuestId == ActiveQuest);
            var items = Api.InventoryItems.ToList();
            if (Acknowledgement is "all" or "materials-only" or "wrong-material")
            {
                foreach (var id in quest.MaterialIds)
                {
                    var remaining = Acknowledgement == "wrong-material" && id == quest.MaterialIds[0] ? 9U : 10U;
                    for (var index = 0; index < items.Count && remaining > 0; index++)
                    {
                        if (items[index].TemplateId != id || items[index].QualityRank != 1) continue;
                        var take = Math.Min(remaining, items[index].Count);
                        items[index] = items[index] with { Count = items[index].Count - take };
                        remaining -= take;
                    }
                    Require(remaining == 0, "fixture submitted with insufficient materials");
                }
            }
            Api.InventoryItems = items.Where(i => i.Count > 0).ToArray();
        }

        private void GrantReward()
        {
            var quest = ManastoneExchangePlan.Quests.Single(q => q.QuestId == ActiveQuest);
            var items = Api.InventoryItems.ToList();
            if (Acknowledgement is "all" or "reward-only" or "wrong-material")
            {
                var index = items.FindIndex(i => i.TemplateId == quest.RewardId);
                if (index >= 0) items[index] = items[index] with { Count = items[index].Count + 1 };
                else items.Add(new(quest.RewardId, quest.RewardId, quest.Name, 1, 20 + (int)(quest.QuestId - 4113), false, 42, 1));
            }
            Api.InventoryItems = items.Where(i => i.Count > 0).ToArray();
        }

        public Task<OperationResult<ManastoneExchangeResult>> Run(CancellationToken token = default, int maximumCompletions = int.MaxValue) =>
            new ManastoneExchangeSequence(Input, Logger, (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (ms == 100 && Submitted.Count > 0 && Acknowledgement != "all" && ++_confirmationPolls == 4)
                    throw new TradingActions.ConfirmationTimeoutException();
                if (ms == 100 && OpenRightClicks > 0 && OpenAcknowledgement != "all" && ++_openPolls == 8)
                    throw new TradingActions.ConfirmationTimeoutException();
                if (ms == 100 && _pendingLoot is not null && ++_openPolls == LootDelayPolls)
                    SettleLoot();
                return Task.CompletedTask;
            }).RunAsync(Api.Create(new AccountConfig { AccountName = "exchange-test" }, Logger, token), "exchange-test", null, token, maximumCompletions);
    }

    private sealed class ResettableInput : IKeyboardInput, IInputStateReset
    {
        public RecordingKeyboardInput Record { get; } = new();
        public int Releases;
        public Task<OperationResult> ReleaseAllAsync(CancellationToken cancellationToken = default) { Releases++; return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan duration, CancellationToken cancellationToken = default) => Record.PressKeyAsync(key, duration, cancellationToken);
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => Record.KeyDownAsync(key, cancellationToken);
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => Record.KeyUpAsync(key, cancellationToken);
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => Record.MouseDownAsync(button, cancellationToken);
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => Record.MouseUpAsync(button, cancellationToken);
        public Task<OperationResult> MoveMouseRelativeAsync(int x, int y, CancellationToken cancellationToken = default) => Record.MoveMouseRelativeAsync(x, y, cancellationToken);
        public Task<OperationResult> ScrollMouseAsync(int delta, CancellationToken cancellationToken = default) => Record.ScrollMouseAsync(delta, cancellationToken);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
