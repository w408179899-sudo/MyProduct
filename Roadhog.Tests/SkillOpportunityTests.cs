using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Application.SemiAuto;
using System.Reflection;
using System.Xml.Linq;

internal static class SkillOpportunityTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static Task DecoderScopeAsync()
    {
        var memory = new Memory();
        var read = memory.Capture();
        Check(read.Completeness == SkillOpportunityReadCompleteness.Complete, "rendered capture must be complete");
        var snapshot = SkillAvailabilityPublication.Merge(read, null)!.ToSnapshot();
        Check(snapshot.Slots.Count == 3, "only demonstrated special opportunities are exposed");
        Check(snapshot.UnsupportedSkillIds!.SequenceEqual(new uint[] { 985, 1260 }), "ordinary skills are explicit unsupported capabilities");
        Check(!snapshot.Slots.Any(s => s.EffectiveSkillId is 985 or 1260), "ordinary positive raw timers never become CanUse");
        Check(snapshot.Slots.Single(s => s.Slot == 1).CanUse, "first valid Parry glow is usable");
        Check(!snapshot.Slots.Single(s => s.Slot == 2).CanUse, "valid dark Parry slot publishes false");
        Check(snapshot.Slots.Single(s => s.Slot == 1).EffectiveSkillId == 1227, "visible rank I is never rewritten as learned rank III");
        Check(snapshot.LastReleasedSkillId == 1227 && snapshot.LastReleasedSkillTime == 900, "release pair preserves precise rank and game time");
        Check(snapshot.BindingSlots!.Count == 24 && snapshot.BindingSlots.Single(s => s.Bar == SkillQuickbar.Main && s.Slot == 0).EffectiveSkillId == 985,
            "ordinary actual bindings remain available separately from opportunity capability");
        Check(snapshot.BindingSlots.Single(s => s.Bar == SkillQuickbar.Main && s.Slot == 1).BaseSkillId == 1227,
            "complete bindings retain actual bound rank I");
        Check(new Memory { Fault = "frozen-render" }.Capture().Completeness == SkillOpportunityReadCompleteness.Complete,
            "valid first opportunity does not wait for repeated positive render confirmation");

        memory.SetDisplay(0, 1268);
        memory.SetTimer(0, 0.2f);
        var chained = SkillAvailabilityPublication.Merge(memory.Capture(), null)!.ToSnapshot();
        Check(chained.BindingSignature == snapshot.BindingSignature, "ordinary root to chain display is not a binding change");
        Check(chained.Slots.Single(s => s.Slot == 0).EffectiveSkillId == 1268, "dynamic chain icon exposes its real ID");
        Check(chained.BindingSlots!.Single(s => s.Bar == SkillQuickbar.Main && s.Slot == 0).EffectiveSkillId == 1268,
            "complete bindings follow the exact displayed chain ID");
        Check(!chained.UnsupportedSkillIds!.Contains(985u), "ordinary root disappears from unsupported IDs while a supported child is displayed");
        memory.SetSkill(0, 1260, 1260);
        Check(memory.Capture().BindingSignature != snapshot.BindingSignature, "actual base binding change changes signature");
        return Task.CompletedTask;
    }

    public static Task XmlSpecialClassificationAsync()
    {
        // Relevant fields copied from the supplied client_skills.xml. Exercise
        // the actual XML parser -> learned-skill projection -> icon capability
        // and ordinary selector, rather than assigning metadata in the test.
        var cases = new (string Xml, bool Special, string? Self, string? Transfer)[]
        {
            ("<skill><id>1937</id><name>Test_Evade</name><first_target>TargetorMe</first_target><effect1_type>Evade</effect1_type><effect1_reserved1>Effect_Type</effect1_reserved1><effect1_reserved2>Stun</effect1_reserved2><effect1_reserved3>Stagger</effect1_reserved3><effect1_reserved4>Stumble</effect1_reserved4><effect1_reserved5>Spin</effect1_reserved5><effect1_reserved6>OpenAerial</effect1_reserved6></skill>", true, "Stun,Stagger,Stumble,Spin,OpenAerial", null),
            ("<skill><id>1822</id><name>DP_Transfer</name><first_target>Target</first_target><ultra_transfer>1</ultra_transfer></skill>", true, null, "1"),
            ("<skill><id>1968</id><name>ALL_ShockReflect_G1</name><first_target>Me</first_target><target_valid_status1>Stun</target_valid_status1><target_valid_status2>Stagger</target_valid_status2><target_valid_status3>Stumble</target_valid_status3><target_valid_status4>Spin</target_valid_status4><target_valid_status5>OpenAerial</target_valid_status5><effect1_type>Evade</effect1_type><effect1_reserved1>Effect_Type</effect1_reserved1><effect1_reserved2>Stun</effect1_reserved2><effect1_reserved3>Stagger</effect1_reserved3><effect1_reserved4>Stumble</effect1_reserved4><effect1_reserved5>Spin</effect1_reserved5><effect1_reserved6>OpenAerial</effect1_reserved6></skill>", true, "Stun,Stagger,Stumble,Spin,OpenAerial", null),
            ("<skill><id>1335</id><name>CH_BrutalStrike_G1</name><first_target>Target</first_target><effect1_type>SkillATK_Instant</effect1_type><effect2_type>Stun</effect2_type><effect2_cond_preeffect>e1</effect2_cond_preeffect><effect2_cond_preeffect_prob2>50</effect2_cond_preeffect_prob2></skill>", false, null, null),
            ("<skill><id>2286</id><name>CH_SoaredRock_G1</name><first_target>Target</first_target><target_valid_status1>Stun</target_valid_status1><target_valid_status2>Stumble</target_valid_status2><effect1_type>SkillATK_Instant</effect1_type></skill>", true, null, null),
            ("<skill><id>1333</id><name>CH_SonicGenoside_G2</name><first_target>Target</first_target><target_valid_status1>Stumble</target_valid_status1><effect1_type>SkillATK_Instant</effect1_type></skill>", true, null, null),
            ("<skill><id>9001</id><first_target>Me</first_target><effect1_type>Evade</effect1_type><effect1_reserved1>Effect_Type</effect1_reserved1><effect1_reserved2>UnknownState</effect1_reserved2><effect1_reserved8>Stun</effect1_reserved8><ultra_transfer>0</ultra_transfer></skill>", false, null, null),
            ("<skill><id>9002</id><first_target>Me</first_target><effect1_type>Stun</effect1_type><effect1_reserved2>Stun</effect1_reserved2><ultra_transfer>2</ultra_transfer></skill>", false, null, null),
            ("<skill><id>9003</id><first_target>Me</first_target><self_flying_restriction>Ground</self_flying_restriction><ultra_transfer>unknown</ultra_transfer></skill>", false, null, null)
        };
        var api = typeof(AionVmmGameApi);
        var parse = api.GetMethod("TryReadSkillXmlStaticDetail", BindingFlags.Static | BindingFlags.NonPublic)!;
        var project = api.GetMethod("ToSkillSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
        var supports = api.GetMethod("SupportsSpecialOpportunity", BindingFlags.Static | BindingFlags.NonPublic)!;
        var learnedType = api.GetNestedType("LearnedSkillInfo", BindingFlags.NonPublic)!;
        foreach (var test in cases)
        {
            object?[] arguments = { XElement.Parse(test.Xml), null };
            Check((bool)parse.Invoke(null, arguments)!, "actual XML static parser accepts fixture");
            var detail = arguments[1]!;
            var id = (uint)detail.GetType().GetField("Id")!.GetValue(detail)!;
            var learned = Activator.CreateInstance(learnedType)!;
            learnedType.GetField("SkillId")!.SetValue(learned, id);
            learnedType.GetField("Name")!.SetValue(learned, "skill" + id);
            learnedType.GetField("HasXmlStaticDetail")!.SetValue(learned, true);
            learnedType.GetField("XmlStaticDetail")!.SetValue(learned, detail);
            var skill = (SkillSnapshot)project.Invoke(null, new[] { learned })!;
            var supported = (bool)supports.Invoke(null, new[] { detail })!;
            Check(skill.XmlSelfConditionStatuses == test.Self && skill.XmlUltraTransfer == test.Transfer,
                "official metadata preserves exact self/transfer classification: " + id);
            Check(supported == test.Special && QuickbarSkillCombatController.IsOrdinarySkill(skill) == !test.Special,
                "icon capability and ordinary fallback agree: " + id);

            var memory = new Memory();
            memory.SetSkill(0, id, id);
            memory.SetTimer(0, 0.25f);
            bool Supported(uint actualId) => actualId is 1227 or 1211 or 1268 || actualId == id && supported;
            var snapshot = SkillAvailabilityPublication.Merge(memory.Capture(Supported), null)!.ToSnapshot();
            var opportunity = snapshot.Slots.SingleOrDefault(s => s.Bar == SkillQuickbar.Main && s.Slot == 0);
            Check(test.Special ? opportunity is { CanUse: true } : opportunity is null && snapshot.UnsupportedSkillIds!.Contains(id),
                "special opportunities use the real timer; ordinary raw timers stay unsupported: " + id);
            if (test.Special)
            {
                memory.SetTimer(0, 0);
                var dark = SkillAvailabilityPublication.Merge(memory.Capture(Supported), null)!.ToSnapshot();
                Check(!dark.Slots.Single(s => s.Bar == SkillQuickbar.Main && s.Slot == 0).CanUse,
                    "self/transfer XML does not predict usability when the actual icon is dark: " + id);
            }
        }
        return Task.CompletedTask;
    }

    public static Task DecoderFaultsAsync()
    {
        foreach (var fault in new[] { "page-change", "display-change", "role-change", "short-binding", "invalid-pointer", "table-mismatch" })
        {
            var result = new Memory { Fault = fault }.Capture();
            Check(result.Completeness == SkillOpportunityReadCompleteness.Failed, "mixed structural capture rejected: " + fault);
        }
        foreach (var fault in new[] { "short-timer", "nan-timer", "negative-timer" })
        {
            var result = new Memory { Fault = fault }.Capture();
            Check(result.Completeness == SkillOpportunityReadCompleteness.Partial, "one bad field remains provider Partial: " + fault);
            Check(result.Slots.Single(s => s.Slot == 1 && s.Bar == SkillQuickbar.Main).CanUse is null,
                "invalid timer never produces false/true: " + fault);
            Check(SkillAvailabilityPublication.Merge(result, null) is null, "cold partial cannot invent an opportunity: " + fault);
        }
        var hidden = new Memory { Fault = "hidden-control" }.Capture();
        Check(hidden.PresentationInactive && hidden.Completeness == SkillOpportunityReadCompleteness.Failed,
            "explicitly hidden UI is a presentation lifecycle boundary");
        Check(new Memory { Fault = "page-change" }.Capture().ScopeChanged &&
            new Memory { Fault = "role-change" }.Capture().ScopeChanged,
            "known page or role transitions invalidate the old opportunity generation");
        Check(!new Memory { Fault = "display-change" }.Capture().ScopeChanged,
            "dynamic root-to-child change rejects a mixed capture without simulating a static rebind");
        var torn = new Memory { Fault = "torn-release" }.Capture();
        Check(torn.Completeness == SkillOpportunityReadCompleteness.Partial && torn.LastReleasedSkillId is null && torn.LastReleasedSkillTime is null,
            "torn release identity/time cannot confirm an input");
        return Task.CompletedTask;
    }

    public static Task BatchBudgetAndCombatMergeAsync()
    {
        var memory = new Memory();
        var captured = memory.CaptureBatched(includeCombatState: true);
        Check(captured.Completeness == SkillOpportunityReadCompleteness.Complete && memory.BatchCalls == 6 && memory.SingleCalls == 0,
            "all 24 slots and repeated guards use six dependent batches without individual reads");
        var prior = SkillAvailabilityPublication.Merge(captured, null)!;
        var combat = prior.ToSnapshot().CombatState!;
        Check(combat.PlayerEntityId == 7 && combat.PlayerServerObjectId == 77 && combat.TargetEntityId == 22 && combat.TargetServerObjectId == 100,
            "official combat extension carries exact local and target identities");
        Check(combat.IsAlive && !combat.IsDead && combat.CurrentHp == 80 && combat.MaxHp == 100 &&
            combat.CurrentMp == 30 && combat.MaxMp == 60 && combat.CurrentDp == 200 && combat.HpPercent == 80 && combat.MpPercent == 50,
            "official HP MP DP include lawful values and business percentages");
        for (var bar = 0; bar < 2; bar++)
            for (var slot = 0; slot < 12; slot++) memory.SetBarSkill(bar, slot, 1227, 1227);
        memory.ResetCounters();
        Check(memory.CaptureBatched().Completeness == SkillOpportunityReadCompleteness.Complete && memory.BatchCalls == 6 && memory.SingleCalls == 0,
            "24 special slots do not increase transport batch count or remove any slot guards");

        foreach (var fault in new[] { "short-timer", "nan-timer", "negative-timer" })
        {
            var partial = new Memory { Fault = fault }.CaptureBatched(includeCombatState: true);
            Check(partial.Completeness == SkillOpportunityReadCompleteness.Partial &&
                SkillAvailabilityPublication.Merge(partial, prior)!.ToSnapshot().Slots.Single(s => s.Slot == 1).CanUse,
                "bad individual scatter timer preserves the official field: " + fault);
        }
        memory = new Memory { Fault = "short-resources" };
        memory.SetResources(0, 100, 0, 60, 0);
        var resourcesPartial = memory.CaptureBatched(includeCombatState: true);
        var merged = SkillAvailabilityPublication.Merge(resourcesPartial, prior)!.ToSnapshot().CombatState!;
        Check(resourcesPartial.Completeness == SkillOpportunityReadCompleteness.Partial && merged.IsDead &&
            merged.CurrentMp == 30 && merged.CurrentDp == 200,
            "valid zero HP publishes immediately while unreadable MP DP retain official fields");
        Check(SkillAvailabilityPublication.Merge(resourcesPartial, null) is null, "cold partial resources never invent a combat extension");
        memory = new Memory(); memory.SetResources(80, 100, 0, 60, 0);
        var zeroResources = SkillAvailabilityPublication.Merge(memory.CaptureBatched(includeCombatState: true), prior)!.ToSnapshot().CombatState!;
        Check(zeroResources.CurrentMp == 0 && zeroResources.CurrentDp == 0, "valid zero MP DP replace nonzero prior fields");
        Check(new Memory { Fault = "target-change" }.CaptureBatched(includeCombatState: true).ScopeChanged,
            "target identity transition during a capture invalidates its opportunity generation");
        var failed = new Memory { Fault = "short-binding" }.CaptureBatched(includeCombatState: true);
        Check(failed.Completeness == SkillOpportunityReadCompleteness.Failed &&
            ReferenceEquals(SkillAvailabilityPublication.Merge(failed, prior), prior), "structural batch failure holds official object");
        return Task.CompletedTask;
    }

    public static Task BatchedActorIdentityAsync()
    {
        const ulong entity = 0x6000000, vtable = 0x7000000, function = 0x7100000, manager = 0x7200000;
        const ulong firstActor = 0x8000000, selectedActor = 0x8100000;
        var map = new Dictionary<ulong, byte[]>();
        map[entity] = BitConverter.GetBytes(vtable);
        map[vtable + 0xB8] = BitConverter.GetBytes(function);
        var code = new byte[16]; code[0] = 0x48; code[1] = 0x8B; code[2] = 0x81;
        BitConverter.GetBytes(0x120U).CopyTo(code, 3); map[function] = code;
        map[entity + 0x120] = BitConverter.GetBytes(manager);
        var pointers = new byte[0x400]; BitConverter.GetBytes(firstActor).CopyTo(pointers, 0);
        BitConverter.GetBytes(selectedActor).CopyTo(pointers, 8); map[manager] = pointers;
        foreach (var address in new[] { firstActor, selectedActor })
        {
            var identity = new byte[40]; BitConverter.GetBytes(entity).CopyTo(identity, 0);
            BitConverter.GetBytes(1U).CopyTo(identity, 24); BitConverter.GetBytes(77U).CopyTo(identity, 36);
            map[address + 8] = identity; map[address + 0x42] = new byte[128];
        }
        System.Text.Encoding.Unicode.GetBytes("actor").CopyTo(map[selectedActor + 0x42], 0);
        var singles = 0; var batches = 0;
        byte[] Read(ulong address, int size)
        {
            singles++;
            return map.TryGetValue(address, out var bytes) && bytes.Length == size ? bytes.ToArray() : Array.Empty<byte>();
        }
        IReadOnlyList<byte[]> Batch(IReadOnlyList<(ulong Address, int Size)> requests)
        {
            batches++;
            return requests.Select(request =>
            {
                if (map.TryGetValue(request.Address, out var bytes) && bytes.Length == request.Size) return bytes.ToArray();
                if (request.Size == 8 && request.Address == manager + 8) return pointers.AsSpan(8, 8).ToArray();
                foreach (var address in new[] { firstActor, selectedActor })
                    if (request.Address >= address + 8 && request.Address < address + 48)
                        return map[address + 8].Skip((int)(request.Address - address - 8)).Take(request.Size).ToArray();
                return Array.Empty<byte>();
            }).ToArray();
        }
        var capture = new SkillOpportunityActorResolver(Read, Batch).Read(entity)!;
        Check(capture.Actor == selectedActor && capture.ServerObjectId == 77 && singles == 5 && batches == 2,
            "one whole pointer-region read plus two batches preserves highest-scored candidate selection");
        Check(capture.Verify(Batch) == SkillOpportunityActorVerification.Complete && batches == 3,
            "one final batch verifies current locator path and actor identity");
        map[selectedActor + 8][8] = 1;
        Check(capture.Verify(Batch) == SkillOpportunityActorVerification.Complete,
            "uninterpreted mutable actor bytes are not lifecycle identity guards");
        var bad = map[selectedActor + 8]; map[selectedActor + 8] = bad[..^1];
        Check(capture.Verify(Batch) == SkillOpportunityActorVerification.Incomplete, "short identity verification is read failure, not lifecycle change");
        map[selectedActor + 8] = bad.ToArray(); BitConverter.GetBytes(88U).CopyTo(map[selectedActor + 8], 36);
        Check(capture.Verify(Batch) == SkillOpportunityActorVerification.Changed, "valid actor identity change retires the generation");
        return Task.CompletedTask;
    }

    public static Task PublicationMergeAsync()
    {
        var memory = new Memory();
        var initialRead = memory.Capture();
        var previous = SkillAvailabilityPublication.Merge(initialRead, null)!;
        memory.SetTimer(1, 0);
        var dark = SkillAvailabilityPublication.Merge(memory.Capture(), previous)!;
        Check(!dark.ToSnapshot().Slots.Single(s => s.Slot == 1).CanUse, "first valid false immediately replaces the published true");

        memory = new Memory { Fault = "short-timer" };
        memory.SetTimer(2, 0.7f);
        memory.SetRelease(1211, 950);
        var partial = memory.Capture();
        var merged = SkillAvailabilityPublication.Merge(partial, previous)!;
        Check(merged.ToSnapshot().Slots.Single(s => s.Slot == 1).CanUse, "invalid timer retains prior official field");
        Check(merged.ToSnapshot().Slots.Single(s => s.Slot == 2).CanUse, "independently valid opportunity publishes during Partial");
        Check(merged.LastReleasedSkillId == 1211 && merged.LastReleasedSkillTime == 950, "valid release pair publishes during Partial");

        memory.SetDisplay(1, 1211);
        var changedEffective = SkillAvailabilityPublication.Merge(memory.Capture(), previous)!;
        Check(changedEffective.ToSnapshot().Slots.Single(s => s.Slot == 1).EffectiveSkillId == 1227,
            "unknown new effective usability does not inherit the previous skill's true");
        Check(ReferenceEquals(SkillAvailabilityPublication.Merge(SkillOpportunityRead.Failed("DMA read failed"), previous), previous),
            "Failed preserves official canonical value exactly");
        Check(SkillAvailabilityPublication.Merge(partial with { ActorServerObjectId = 999 }, previous) is null,
            "new role cannot merge an old role opportunity");
        Check(SkillAvailabilityPublication.Merge(partial with { BindingSignature = "new-page-and-bindings" }, previous) is null,
            "new binding generation cannot merge old opportunities");
        return Task.CompletedTask;
    }

    public static Task ChannelLifetimeAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.SkillAvailability;
        var context = new GameApiReadContext("a", 1, "Aion.bin", "device");
        var now = DateTimeOffset.Now;
        var previous = SkillAvailabilityPublication.Merge(new Memory().Capture(), null)!;
        var failed = OperationResult<SkillAvailabilityPublication>.Fail("partial source");
        Check(!store.Resolve("connection\u001ea\u001eavailability:one", channel, context, failed, now).Result.Success,
            "cold failure has no default official snapshot");
        store.Resolve("connection\u001ea\u001eavailability:one", channel, context, OperationResult<SkillAvailabilityPublication>.Ok(previous), now);
        var held = store.Resolve("connection\u001ea\u001eavailability:one", channel, context, failed, now.AddYears(1)).Result.Value!;
        Check(ReferenceEquals(held, previous) && ReferenceEquals(held.ToSnapshot(), previous.ToSnapshot()),
            "failed read preserves the formal business value without expiry");
        Check(!store.Resolve("connection\u001ea\u001eavailability:two", channel, context, failed, now).Result.Success,
            "presentation generations cannot reuse a prior publication");
        Check(!store.Resolve("connection\u001eb\u001eavailability:one", channel, context, failed, now).Result.Success,
            "account sessions cannot share opportunities");
        Check(channel.ReadPolicy == DmaSnapshotReadPolicy.Stable && channel.MergePolicy == DmaSnapshotMergePolicy.FieldAware,
            "new registered channel keeps provider-owned stable field-aware policy");
        store.ClearConnection("connection");
        Check(!store.Resolve("connection\u001ea\u001eavailability:one", channel, context, failed, now).Result.Success,
            "connection retirement invalidates enriched presentation keys");
        return Task.CompletedTask;
    }

    public static async Task ReaderRetryAndLegacyIsolationAsync()
    {
        var api = new Api();
        var reader = new RoadhogSnapshotReader(new AccountConfig(), api, NoOpRoadhogLogger.Instance, CancellationToken.None);
        api.ReadAvailability = _ => throw new IOException("new mode cannot read yet");
        await reader.ReadQuickbarAsync();
        await reader.ReadPlayerAsync();
        Check(api.AvailabilityReads == 0, "old binding/player reads never initialize or request new availability");

        var publication = SkillAvailabilityPublication.Merge(new Memory().Capture(), null)!.ToSnapshot();
        api.ReadAvailability = _ => Task.FromResult(api.AvailabilityReads < 3
            ? OperationResult<SkillAvailabilitySnapshot>.Fail("cold partial")
            : OperationResult<SkillAvailabilitySnapshot>.Ok(publication));
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Check(ReferenceEquals((await reader.ReadSkillAvailabilityAsync(cancellationToken: bounded.Token)).Value, publication)
            && api.AvailabilityReads == 3, "cold retry remains below business and publishes the first official value");

        api.ReadAvailability = _ => Task.FromResult(OperationResult<SkillAvailabilitySnapshot>.Fail("still cold"));
        using var modeStop = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var cancelled = false;
        try { await reader.ReadSkillAvailabilityAsync(cancellationToken: modeStop.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "mode stop cancels its own cold-start wait");
        var readsAtSwitch = api.AvailabilityReads;
        await reader.ReadQuickbarAsync();
        await reader.ReadPlayerAsync();
        Check(api.AvailabilityReads == readsAtSwitch, "legacy reads continue after cancelling new mode");
    }

    private sealed class Memory
    {
        private const ulong Module = 0x180000000;
        private const ulong Actor = 0x5000000;
        private readonly Dictionary<ulong, byte[]> _memory = new();
        private readonly Dictionary<(ulong Address, int Size), int> _reads = new();
        public int BatchCalls { get; private set; }
        public int SingleCalls { get; private set; }
        private bool _insideBatch;
        public string? Fault { get; init; }
        private static ulong Control(int slot) => 0x11000000UL + (ulong)slot * 0x1000;

        public Memory()
        {
            _memory[Module + 0xD4AE0C] = BitConverter.GetBytes(0);
            _memory[Module + 0xD61260] = new byte[384];
            _memory[Module + 0x6E2180] = BitConverter.GetBytes(10U).Concat(BitConverter.GetBytes(11U)).ToArray();
            _memory[Actor + 0x2C] = BitConverter.GetBytes(77U);
            _memory[Module + 0xD6CB08] = BitConverter.GetBytes((uint)(7 | (22 << 16)));
            _memory[Actor + 0x358] = BitConverter.GetBytes(100U);
            SetResources(80, 100, 30, 60, 200);
            SetRelease(1227, 900);
            for (int bar = 0; bar < 2; bar++)
            {
                var panel = 0x10000000UL + (ulong)bar * 0x10000000;
                _memory[Module + 0xD63990 + (ulong)(10 + bar) * 8] = BitConverter.GetBytes(panel);
                _memory[panel + 1344] = BitConverter.GetBytes((uint)bar);
                _memory[panel + 0x28] = BitConverter.GetBytes(1U);
                var pointers = new byte[96];
                for (int slot = 0; slot < 12; slot++)
                {
                    var control = Control(slot) + (ulong)bar * 0x10000000;
                    BitConverter.GetBytes(control).CopyTo(pointers, slot * 8);
                    _memory[control + 952] = new byte[12];
                    _memory[control + 0x28] = BitConverter.GetBytes(1U);
                    _memory[control + 984] = BitConverter.GetBytes(0f);
                }
                _memory[panel + 1240] = pointers;
            }
            SetSkill(0, 985, 985); SetTimer(0, 10); // ordinary positive raw timer is deliberately ignored
            SetSkill(1, 1227, 1227); SetTimer(1, 0.5f);
            SetSkill(2, 1211, 1211);
            SetSkill(3, 985, 1268); SetTimer(3, 0.3f);
            SetSkill(4, 1260, 1260); SetTimer(4, 10);
        }

        public void SetSkill(int slot, uint baseId, uint effectiveId)
            => SetBarSkill(0, slot, baseId, effectiveId);
        public void SetBarSkill(int bar, int slot, uint baseId, uint effectiveId)
        {
            var binding = new byte[12];
            BitConverter.GetBytes(baseId).CopyTo(binding, 0);
            BitConverter.GetBytes(21U).CopyTo(binding, 4);
            BitConverter.GetBytes(effectiveId).CopyTo(binding, 8);
            _memory[Control(slot) + (ulong)bar * 0x10000000 + 952] = binding;
            var table = _memory[Module + 0xD61260];
            BitConverter.GetBytes(21U).CopyTo(table, (bar * 12 + slot) * 16);
            BitConverter.GetBytes(baseId).CopyTo(table, (bar * 12 + slot) * 16 + 4);
        }
        public void SetResources(uint hp, uint maxHp, uint mp, uint maxMp, ushort dp)
        {
            var bytes = new byte[20]; BitConverter.GetBytes(maxHp).CopyTo(bytes, 0); BitConverter.GetBytes(hp).CopyTo(bytes, 4);
            BitConverter.GetBytes(maxMp).CopyTo(bytes, 8); BitConverter.GetBytes(mp).CopyTo(bytes, 12); BitConverter.GetBytes(dp).CopyTo(bytes, 18);
            _memory[Module + 0xD71BB4] = bytes;
        }
        public void ResetCounters() { BatchCalls = 0; SingleCalls = 0; _reads.Clear(); }
        public SkillOpportunityRead CaptureBatched(bool includeCombatState = false) => new SkillOpportunityDecoder(Read,
            id => id is 1227 or 1211 or 1268, Batch).Read(Module, Actor, includeCombatState);
        private IReadOnlyList<byte[]> Batch(IReadOnlyList<(ulong Address, int Size)> requests)
        {
            BatchCalls++; _insideBatch = true;
            try { return requests.Select(request => Read(request.Address, request.Size)).ToArray(); }
            finally { _insideBatch = false; }
        }
        public void SetDisplay(int slot, uint id) => BitConverter.GetBytes(id).CopyTo(_memory[Control(slot) + 952], 8);
        public void SetTimer(int slot, float timer) => _memory[Control(slot) + 984] = BitConverter.GetBytes(timer);
        public void SetRelease(uint id, uint time) => _memory[Actor + 804] = BitConverter.GetBytes(id).Concat(BitConverter.GetBytes(time)).ToArray();
        public SkillOpportunityRead Capture(Func<uint, bool>? supports = null) =>
            new SkillOpportunityDecoder(Read, supports ?? (id => id is 1227 or 1211 or 1268)).Read(Module, Actor);

        private byte[] Read(ulong address, int size)
        {
            if (!_insideBatch) SingleCalls++;
            var key = (address, size);
            _reads.TryGetValue(key, out var count);
            _reads[key] = ++count;
            if (!_memory.TryGetValue(address, out var stored) || stored.Length != size) throw new IOException("missing test memory");
            var bytes = stored.ToArray();
            if (Fault == "short-resources" && address == Module + 0xD71BB4) return bytes[..8];
            if (Fault == "target-change" && address == Actor + 0x358 && count > 1) return BitConverter.GetBytes(101U);
            if (Fault == "page-change" && address == Module + 0xD4AE0C && count > 1) return BitConverter.GetBytes(1);
            if (Fault == "role-change" && address == Actor + 0x2C && count > 1) return BitConverter.GetBytes(88U);
            if (Fault == "invalid-pointer" && address == 0x10000000 + 1240) Array.Clear(bytes, 8, 8);
            if (Fault == "table-mismatch" && address == Module + 0xD61260) BitConverter.GetBytes(999U).CopyTo(bytes, 20);
            if (address == Control(1) + 952)
            {
                if (Fault == "short-binding") return bytes[..^1];
                if (Fault == "display-change" && count > 1) BitConverter.GetBytes(1268U).CopyTo(bytes, 8);
            }
            if (Fault == "hidden-control" && address == Control(1) + 0x28) return BitConverter.GetBytes(0U);
            if (Fault == "torn-release" && address == Actor + 804 && count > 1) BitConverter.GetBytes(901U).CopyTo(bytes, 4);
            if (address == Control(1) + 984)
            {
                if (Fault == "short-timer") return bytes[..^1];
                if (Fault == "nan-timer") return BitConverter.GetBytes(float.NaN);
                if (Fault == "negative-timer") return BitConverter.GetBytes(-1f);
            }
            if (size == 4 && _memory.ContainsKey(address - 32) && address >= 0x11000000 && address < 0x31000000)
            {
                var value = BitConverter.ToSingle(bytes);
                if (value > 0 && count > 1 && !(Fault == "frozen-render" && address == Control(1) + 984))
                    return BitConverter.GetBytes(value + count * 0.01f);
            }
            return bytes;
        }
    }

    private sealed class Api : IRoadhogGameApi, IQuickbarGameApi, ISkillAvailabilityGameApi
    {
        private readonly FakeGameApi _old = new();
        public int AvailabilityReads { get; private set; }
        public Func<CancellationToken, Task<OperationResult<SkillAvailabilitySnapshot>>>? ReadAvailability { get; set; }
        public Task<OperationResult<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(GameApiReadContext context, CancellationToken token = default)
        { AvailabilityReads++; return ReadAvailability!(token); }
        public Task<OperationResult<QuickbarSnapshot>> ReadQuickbarAsync(GameApiReadContext context, CancellationToken token = default) => _old.ReadQuickbarAsync(context, token);
        public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken token = default) => _old.ReadPlayerAsync(token);
        public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(CancellationToken token = default) => _old.ReadPlayerAbnormalStatusesAsync(token);
        public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken token = default) => _old.ReadSummonedPetAsync(token);
        public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(CancellationToken token = default) => _old.ReadSummonedPetRosterAsync(token);
        public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken token = default) => _old.ReadLockedTargetAsync(token);
        public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(CancellationToken token = default) => _old.ReadLockedTargetAbnormalStatusesAsync(token);
        public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken token = default) => _old.ReadSkillsAsync(token);
        public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken token = default) => _old.ReadInventoryAsync(token);
        public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken token = default) => _old.ReadWorldObjectsAsync(token);
        public Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(CancellationToken token = default) => _old.ReadGatherSnapshotAsync(token);
        public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken token = default) => _old.ReadLootCorpsesAsync(token);
    }
}
