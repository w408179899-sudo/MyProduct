using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSpecialPriorityTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-08T10:00:00+08:00");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static SkillSnapshot Skill(uint id) => new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 0,
        XmlActivation: "Active", XmlSkillType: "Physical", XmlSubType: "Attack", XmlTargetRelationRestriction: "Enemy");
    private static readonly SkillSnapshot[] Learned =
    {
        Skill(11) with { XmlChainCategory = "a" },
        Skill(12) with { XmlPrechainCategory = "a", XmlChainCategory = "b" },
        Skill(13) with { XmlPrechainCategory = "b" },
        Skill(21) with { XmlCounterSkill = "Parry" },
        Skill(31),
        Skill(41) with { XmlTargetValidStatuses = "Stun" },
        Skill(51) with { XmlSelfConditionStatuses = "Dodge" },
        Skill(61)
    };
    private static readonly QuickbarSnapshot Bindings = new(0, new QuickbarSlotSnapshot[]
    {
        new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 21),
        new(SkillQuickbar.Main, 2, 21, 31), new(SkillQuickbar.Main, 3, 21, 41),
        new(SkillQuickbar.Main, 4, 21, 51), new(SkillQuickbar.Alt, 0, 21, 61)
    });
    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) =>
        new() { SkillId = id, Name = "skill" + id, Children = children.ToList() };
    private static QuickbarSkillScriptSettings Settings(bool preempt = true) => new()
    {
        TriggerConditionSkillsPreemptChain = preempt,
        // Ordinary skills intentionally precede both opportunities in the saved tree.
        ExecutionTree = new() { Node(31), Node(11, Node(12, Node(13))), Node(41), Node(21), Node(51),
            new() { SkillId = 61, Name = "skill61", Type = "条件技能" } }
    };
    private static QuickbarSkillPlan Plan(bool preempt = true) => QuickbarSkillPlan.FromSettings(Settings(preempt), new(Bindings, Learned));
    private static LockedTargetSnapshot Target() =>
        new(50, 100, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);
    private static SkillAvailabilitySnapshot Bar(uint effective = 11, bool chain = false, bool trigger = false,
        bool condition = false, bool ordinary = false, uint last = 0, uint time = 0) => new(0,
        new SkillAvailabilitySlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, 11, effective, chain),
            new(SkillQuickbar.Main, 1, 21, 21, 21, trigger),
            new(SkillQuickbar.Main, 2, 21, 31, 31, ordinary),
            new(SkillQuickbar.Main, 3, 21, 41, 41, condition),
            new(SkillQuickbar.Main, 4, 21, 51, 51, false),
            new(SkillQuickbar.Alt, 0, 21, 61, 61, false)
        }, last, time);
    private static QuickbarSkillCombatState AcceptedRoot(QuickbarSkillPlan plan)
    {
        var state = new QuickbarSkillCombatState();
        state.BeginAction(plan.Roots[1], Skill(11), Bar(chain: true), Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Bar(last: 11, time: 1000), Learned, Start), "fixture confirms the exact chain predecessor");
        return state;
    }

    public static Task PlanAsync()
    {
        var settings = Settings();
        var plan = QuickbarSkillPlan.FromSettings(settings, new(Bindings, Learned));
        Check(new QuickbarSkillScriptSettings().TriggerConditionSkillsPreemptChain && plan.TriggerConditionSkillsPreemptChain,
            "new settings and immutable execution plans default to special skills first");
        Check(plan.Roots.Where(node => node.IsTriggerOrCondition).Select(node => node.SkillId)
            .SequenceEqual(new uint[] { 41, 21, 51, 61 }), "target, counter, self conditions and saved category identify eligible types");
        Check(!plan.Roots[1].IsTriggerOrCondition && !plan.Roots[1].Children[0].IsTriggerOrCondition,
            "chain relationship alone does not grant trigger or condition priority");
        settings.TriggerConditionSkillsPreemptChain = false;
        Check(plan.TriggerConditionSkillsPreemptChain && !QuickbarSkillPlan.FromSettings(settings, new(Bindings, Learned)).TriggerConditionSkillsPreemptChain,
            "a running plan owns its account switch and cannot be mutated by a draft setting");
        var lowerRank = Learned.Where(skill => skill.SkillId != 21).Append(Skill(21))
            .Append(Skill(22) with { XmlCounterSkill = "Parry" }).ToArray();
        Check(!QuickbarSkillPlan.FromSettings(Settings(), new(Bindings, lowerRank)).Roots[3].IsTriggerOrCondition,
            "priority metadata follows the exact bound rank rather than another learned rank");
        return Task.CompletedTask;
    }

    public static Task SelectionAsync()
    {
        var plan = Plan();
        var state = AcceptedRoot(plan);
        var all = Bar(12, chain: true, trigger: true, condition: true, ordinary: true, last: 11, time: 1000);
        QuickbarSkillReleaseDecision Select(SkillAvailabilitySnapshot value, IReadOnlySet<uint>? cooling = null,
            IReadOnlySet<uint>? rootSuppression = null, IReadOnlySet<uint>? suppression = null) =>
            QuickbarSkillReleasePriority.SelectNext(plan, state, value, Start, coolingSkillIds: cooling,
                suppressedRootSkillIds: rootSuppression, suppressedSkillIds: suppression);
        Check(Select(all).Node?.SkillId == 41, "available conditions preempt a lit continuation in configured special order");
        Check(Select(all with { Slots = all.Slots.Select(slot => slot.BaseSkillId == 41 ? slot with { CanUse = false } : slot).ToArray() })
            .Node?.SkillId == 21, "dark condition yields to an available trigger before the continuation");
        Check(Select(all, new HashSet<uint> { 41 }).Node?.SkillId == 21, "cooling condition cannot preempt");
        Check(Select(all, rootSuppression: new HashSet<uint> { 41, 21 }).Node?.SkillId == 12,
            "suppressed special roots cannot interrupt a continuation");
        Check(Select(all, suppression: new HashSet<uint> { 41, 21 }).Node?.SkillId == 12,
            "all-skill suppression also applies to special priority");
        var wrongBinding = all with { Slots = all.Slots.Select(slot => slot.BaseSkillId == 41 ? slot with { EffectiveSkillId = 999 } : slot).ToArray() };
        Check(Select(wrongBinding).Node?.SkillId == 21, "a substituted unrelated skill cannot inherit condition priority");
        Check(Select(all with { Page = 1 }).Kind == QuickbarSkillDecisionKind.None, "priority cannot bypass the configured quickbar page");
        Check(Select(Bar(12, chain: true, last: 11, time: 1000)).Node?.SkillId == 12,
            "no available special skill preserves chain priority");
        Check(Select(Bar(trigger: true, last: 11, time: 1000)).Node?.SkillId == 21,
            "an available trigger may also preempt the bounded wait for an unopened continuation");
        Check(Select(Bar(last: 11, time: 1000)).Kind == QuickbarSkillDecisionKind.WaitForChain,
            "dark special skills preserve the existing finite chain wait");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Bar(last: 11, time: 1000), Start,
            ordinaryReadyIds: new HashSet<uint> { 21, 41 }).Kind == QuickbarSkillDecisionKind.WaitForChain,
            "cooldown-ready proposals never make a dark trigger or condition usable");
        var off = Plan(false);
        Check(QuickbarSkillReleasePriority.SelectNext(off, AcceptedRoot(off), all, Start).Node?.SkillId == 12,
            "unchecked switch preserves continuation priority even with both special opportunities available");
        Check(QuickbarSkillReleasePriority.SelectNext(off, AcceptedRoot(off), Bar(trigger: true, last: 11, time: 1000), Start)
            .Kind == QuickbarSkillDecisionKind.WaitForChain, "unchecked switch preserves the finite handoff wait");
        Check(QuickbarSkillReleasePriority.SelectNext(off, new(), Bar(trigger: true, condition: true, ordinary: true), Start)
            .Node?.SkillId == 31, "unchecked switch preserves configured ordinary root order");
        var nested = QuickbarSkillPlan.FromSettings(new()
        {
            ExecutionTree = new() { Node(11, Node(12), Node(41)), Node(21) }
        }, new(Bindings, Learned));
        var nestedDecision = QuickbarSkillReleasePriority.SelectNext(nested, new(), all, Start,
            suppressedRootSkillIds: new HashSet<uint> { 41 });
        Check(nestedDecision is { Kind: QuickbarSkillDecisionKind.PressChain, Node.SkillId: 41 },
            "a configured conditional child gets special priority and keeps its independent binding and child decision");
        return Task.CompletedTask;
    }

    public static Task RetryAndFairnessAsync()
    {
        var plan = Plan();
        var state = AcceptedRoot(plan);
        var available = Bar(12, chain: true, trigger: true, last: 11, time: 1000);
        var trigger = plan.Roots[3];
        state.BeginAction(trigger, Skill(21), available, Start, TimeSpan.FromSeconds(8));
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, available with
            { Slots = available.Slots.Select(slot => slot.BaseSkillId == 41 ? slot with { CanUse = true } : slot).ToArray() }, Start)
            .Kind == QuickbarSkillDecisionKind.None, "a pending special owns its baseline while the80ms retry is not due");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, available, Start.AddMilliseconds(80)).Node?.SkillId == 21,
            "pending special retry stays ahead of the still-lit continuation");
        state.RejectAction(Start, TimeSpan.FromSeconds(1));
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, available, Start).Node?.SkillId == 12,
            "input failure suppresses the special temporarily and lets the open continuation proceed");
        state = AcceptedRoot(plan);
        state.BeginAction(trigger, Skill(21) with { CooldownDuration = 0 }, available, Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(available with { LastReleasedSkillId = 21, LastReleasedSkillTime = 1100 },
            new[] { Skill(21) with { CooldownDuration = 0 } }, Start), "zero-CD special is confirmed by exact release evidence");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, available, Start).Node?.SkillId == 12,
            "confirmed zero-CD special yields to a continuation instead of starving it");
        return Task.CompletedTask;
    }

    public static async Task ControllerAsync()
    {
        foreach (var id in new uint[] { 21, 41 })
        {
            var fixture = new Fixture();
            fixture.Reader.Value = Bar(12, chain: true, trigger: id == 21, condition: id == 41, last: 11, time: 1000);
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.SequenceEqual(new[] { id == 21 ? "D2" : "D4" }), "controller inserts the available special before the chain key");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(40));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.Count == 1, "special retry never borrows the continuation during its retry interval");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(40));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.Count == 2 && fixture.State.PendingAction?.AttemptCount == 2,
                "unconfirmed inserted skill retries at80ms using its original baseline");
            fixture.Reader.Value = Bar(12, chain: true, last: id, time: 1100);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.Last() == "D1" && fixture.State.PendingAction?.Node.SkillId == 12,
                "after exact special confirmation the original still-open continuation resumes");
            fixture.Reader.Value = Bar(13, chain: true, last: 12, time: 1200);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
            await fixture.Tick();
            Check(fixture.State.PendingAction?.Node.SkillId == 13, "the resumed chain can continue to its next configured stage");
            Check(fixture.Keyboard.Holds.All(hold => hold <= TimeSpan.FromMilliseconds(30)), "priority changes still send finite key taps");
        }
        var off = new Fixture(false);
        off.Reader.Value = Bar(12, chain: true, trigger: true, condition: true, last: 11, time: 1000);
        await off.Tick();
        Check(off.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && off.State.PendingAction?.Node.SkillId == 12,
            "unchecked controller releases the continuation first");
    }

    public static async Task GuardAndBootstrapAsync()
    {
        var fixture = new Fixture();
        fixture.Reader.Value = Bar(12, chain: true, trigger: true, last: 11, time: 1000);
        fixture.Reader.OnRead = count =>
        {
            if (count >= 2) fixture.Reader.Value = Bar(12, chain: true, last: 11, time: 1000);
        };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }), "a special that closes at the final guard yields to the still-open continuation");
        fixture = new Fixture();
        fixture.Reader.Value = Bar(12, chain: true, condition: true, last: 11, time: 1000);
        fixture.Reader.OnRead = count =>
        {
            if (count >= 2) fixture.Reader.Value = Bar(12, chain: true, condition: true, last: 11, time: 1000) with
                { CombatState = new(1, 2, 50, 100, 0, 100) };
        };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 0, "special priority never bypasses the final life and target guard");
        fixture = new Fixture();
        fixture.Reader.Value = Bar(12, chain: true, condition: true, last: 11, time: 1000) with
        {
            UnsupportedSkillIds = new uint[] { 31 },
            BindingSlots = new[] { new SkillAvailabilityBindingSnapshot(SkillQuickbar.Main, 2, 21, 31, 31) }
        };
        Check(new QuickbarSkillClockBootstrap(fixture.Clock).SelectCandidate(fixture.Plan, fixture.Reader.Value, Learned,
            _ => SemiAutoSkillCooldownReadiness.Unknown, false)?.SkillId == 31,
            "fixture exposes a genuinely eligible ordinary clock-bootstrap competitor");
        await fixture.Tick(calibrated: false);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D4" }), "ordinary cooldown-clock bootstrap cannot replace a selected special opportunity");
    }

    private sealed class Fixture
    {
        public readonly QuickbarSkillPlan Plan;
        public readonly QuickbarSkillCombatState State;
        public readonly Reader Reader = new();
        public readonly Keyboard Keyboard = new();
        public readonly Clock Clock = new();
        public Fixture(bool preempt = true) { Plan = QuickbarSpecialPriorityTests.Plan(preempt); State = AcceptedRoot(Plan); }
        public Task<TimeSpan> Tick(bool? calibrated = null) => new QuickbarSkillCombatController(Keyboard, Clock).TickAsync(
            Plan, State, Target(), Reader, _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned),
            new() { ConfirmTimeoutMs = 8000 }, ordinaryReadiness: _ => new HashSet<uint> { 11, 31 },
            cooldownReadiness: _ => calibrated == false ? SemiAutoSkillCooldownReadiness.Unknown : SemiAutoSkillCooldownReadiness.Ready,
            isCooldownClockCalibrated: calibrated.HasValue ? () => calibrated.Value : null);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = Start;
        private long stamp;
        public override DateTimeOffset GetUtcNow() => now;
        public override long GetTimestamp() => stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { now += duration; stamp += duration.Ticks; }
    }
    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Bar();
        public Action<int>? OnRead;
        private int reads;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnRead?.Invoke(++reads);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(reads, Value));
        }
    }
    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<string> Keys = new();
        public readonly List<TimeSpan> Holds = new();
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Keys.Add(key); Holds.Add(holdDuration); return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
