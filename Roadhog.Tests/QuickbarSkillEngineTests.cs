using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSkillEngineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T10:00:00+08:00");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
        { SkillId = id, Name = "skill" + id, BaseName = "skill" + id, Children = children.ToList() };
    private static SkillSnapshot Skill(uint id, string? chain = null, string? pre = null, uint cd = 0, uint duration = 30000) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, duration, cd,
            XmlChainCategory: chain, XmlPrechainCategory: pre);
    private static readonly SkillSnapshot[] Learned = { Skill(11, "a"), Skill(12, "b", "a"), Skill(13, pre: "b"), Skill(21) with { XmlCounterSkill = "Parry" }, Skill(31), Skill(41) };
    private static QuickbarSnapshot Bindings(int page = 0) => new(page, new QuickbarSlotSnapshot[]
        { new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 21), new(SkillQuickbar.Alt, 10, 21, 31) });
    private static QuickbarSkillPlan Plan() => QuickbarSkillPlan.FromSettings(new()
        { ExecutionTree = new() { Node(21), Node(11, Node(12, Node(13))), Node(31) } }, new(Bindings(), Learned));
    private static LockedTargetSnapshot Target(uint server = 100, ushort entity = 50) =>
        new(entity, server, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);
    private static SkillAvailabilitySnapshot Available(bool root = false, bool counter = false, bool ordinary = false,
        uint effective = 11, uint last = 0, uint time = 0, int page = 0, uint baseId = 11) => new(page,
        new SkillAvailabilitySlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, baseId, effective, root),
            new(SkillQuickbar.Main, 1, 21, 21, 21, counter),
            new(SkillQuickbar.Alt, 10, 21, 31, 31, ordinary)
        }, last, time);

    private static SkillAvailabilitySnapshot HybridAvailable(bool counter = false, uint effective = 11, bool child = false, uint last = 0, uint time = 0) =>
        Available(counter: counter, effective: effective, last: last, time: time) with
        {
            Slots = new[] { new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 1, 21, 21, 21, counter) }
                .Concat(effective != 11 ? new[] { new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 0, 21, 11, effective, child) } : Array.Empty<SkillAvailabilitySlotSnapshot>()).ToArray(),
            UnsupportedSkillIds = new uint[] { 11, 31 },
            BindingSignature = "main:0:11;main:1:21;alt:10:31",
            BindingSlots = new SkillAvailabilityBindingSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11, effective),
                new(SkillQuickbar.Main, 1, 21, 21, 21),
                new(SkillQuickbar.Alt, 10, 21, 31, 31)
            }
        };

    public static Task PlanAsync()
    {
        var plan = Plan();
        Check(plan.Roots.Select(node => node.SkillId).SequenceEqual(new uint[] { 21, 11, 31 }), "configured order survives key position");
        Check(plan.Roots[2].Key == "NumPadAdd", "Alt Num+ slot binding preserved");
        Check(plan.Roots[1].Children[0].Key == "D1" && plan.Roots[1].Children[0].BaseSkillId == 11, "child keeps inherited source identity");
        Check(plan.SkillReadIds.Order().SequenceEqual(new uint[] { 11, 12, 13, 21, 31 }), "trigger and all child ids included for action confirmation");
        var missing = new List<string>();
        var tree = new QuickbarSkillScriptSettings { ExecutionTree = new() { Node(999), Node(11, Node(41), Node(12)) } };
        var filtered = QuickbarSkillPlan.FromSettings(tree, new(Bindings(2), Learned), missing.Add);
        Check(filtered.Page == 2 && filtered.Roots.Count == 1 && filtered.Roots[0].Children.Single().SkillId == 12, "unbound root and invalid inherited child omitted");
        Check(missing.Count == 2, "missing binding reports are precise");
        tree.ExecutionTree[1].Children.Clear();
        Check(filtered.Roots[0].Children.Count == 1, "plan independent of mutable settings");
        Check(!QuickbarSkillPlan.FromSettings(new(), new(Bindings(), Learned)).HasCombatActions, "empty new config does not run old skills");
        var ownBar = Bindings() with { Slots = Bindings().Slots.Append(new(SkillQuickbar.Main, 5, 21, 12)).ToArray() };
        var own = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(11, Node(12)) } }, new(ownBar, Learned));
        Check(own.Roots[0].Children[0].BaseSkillId == 12 && own.Roots[0].Children[0].Key == "D6", "own shortcut child keeps exact slot");
        return Task.CompletedTask;
    }

    public static Task SelectionAsync()
    {
        var plan = Plan();
        var state = new QuickbarSkillCombatState();
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(), Start).Kind == QuickbarSkillDecisionKind.None, "all dark means no press");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, counter: true, ordinary: true), Start).Node?.SkillId == 21, "unconditional client CanUse respects first configured trigger root");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, ordinary: true), Start).Node?.SkillId == 11, "dark preceding root skipped");
        var root = plan.Roots[1];
        state.BeginAction(root, Skill(11), Available(root: true), Start, TimeSpan.FromSeconds(8));
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, counter: true), Start).Kind == QuickbarSkillDecisionKind.None,
            "still open pending action retains first baseline while its80ms retry is not due");
        Check(state.TryConfirmAction(Available(last: 11, time: 10), Learned, Start), "exact actor release confirms root");
        var childLit = Available(root: true, counter: true, ordinary: true, effective: 12);
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, childLit, Start).Node?.SkillId == 12, "current chain outranks earlier trigger roots");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(counter: true, effective: 12), Start).Kind == QuickbarSkillDecisionKind.WaitForChain, "dark displayed continuation protects the finite handoff gap");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(counter: true), Start).Kind == QuickbarSkillDecisionKind.WaitForChain, "unopened probability branch waits only within the finite transition window");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(counter: true), Start.AddMilliseconds(1500)).Node?.SkillId == 21, "unopened probability branch returns to roots when the bounded window expires");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, effective: 999), Start).Kind == QuickbarSkillDecisionKind.WaitForChain, "unconfigured displayed child is never pressed while waiting");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, childLit with { Page = 1 }, Start).Kind == QuickbarSkillDecisionKind.None, "different page cannot use startup keys");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, effective: 12, baseId: 41), Start).Kind == QuickbarSkillDecisionKind.WaitForChain, "changed source binding cannot press its replacement");
        var child = root.Children[0];
        state.BeginAction(child, Skill(12), childLit, Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(effective: 13, last: 12, time: 20), Learned, Start), "middle stage accepted before final considered");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, Available(root: true, counter: true, effective: 13), Start).Node?.SkillId == 13, "final probability stage wins when opened");
        var final = child.Children[0];
        state.BeginAction(final, Skill(13), Available(effective: 13, last: 12, time: 20), Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(last: 13, time: 30), Learned, Start) && state.ActiveChainSource is null, "accepted final completes chain");

        var ownBar = Bindings() with { Slots = Bindings().Slots.Append(new(SkillQuickbar.Main, 5, 21, 12)).ToArray() };
        var ownPlan = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(11, Node(12)), Node(21) } }, new(ownBar, Learned));
        state = new();
        state.BeginAction(ownPlan.Roots[0], Skill(11), Available(root: true), Start, TimeSpan.FromSeconds(8));
        state.TryConfirmAction(Available(last: 11, time: 10), Learned, Start);
        var ownDark = Available(counter: true) with { Slots = Available(counter: true).Slots.Append(new(SkillQuickbar.Main, 5, 21, 12, 12, false)).ToArray() };
        Check(QuickbarSkillReleasePriority.SelectNext(ownPlan, state, ownDark, Start).Kind == QuickbarSkillDecisionKind.WaitForChain, "independent dark shortcut receives the same finite handoff protection");
        Check(QuickbarSkillReleasePriority.SelectNext(ownPlan, state, ownDark, Start.AddMilliseconds(1500)).Node?.SkillId == 21, "independent dark shortcut cannot hold unrelated roots indefinitely");
        return Task.CompletedTask;
    }

    public static Task ConfirmationAsync()
    {
        var plan = Plan(); var root = plan.Roots[1]; var state = new QuickbarSkillCombatState();
        state.BeginAction(root, Skill(11), Available(root: true, last: 11, time: 10), Start, TimeSpan.FromSeconds(8));
        Check(!state.TryConfirmAction(Available(root: true, effective: 12, last: 11, time: 10), Learned, Start), "displayed child and stale last release cannot alone confirm acceptance");
        Check(!state.TryConfirmAction(Available(last: 11, time: 10), new[] { Skill(11, cd: 50000) }, Start), "CD baseline race with unchanged available release clock does not falsely confirm");
        Check(!state.TryConfirmAction(Available(last: 21, time: 20), new[] { Skill(11, cd: 50000) }, Start), "another skill sharing cooldown cannot masquerade as executed node");
        Check(!state.TryConfirmAction(Available(last: 21, time: 10), new[] { Skill(11, cd: 50000) }, Start), "same-time shared cooldown does not confirm without advancing release clock");
        Check(state.ActiveChainSource is null && state.PendingAction is not null, "unaccepted key does not advance chain");
        Check(state.TryConfirmAction(Available(last: 11, time: 20), Learned, Start.AddSeconds(6)), "long action confirmed after six seconds");
        state.BeginAction(root, Skill(11, cd: 1000), Available(last: 11, time: 20), Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(last: 0, time: 30), new[] { Skill(11, cd: 31000) }, Start), "exact skill cooldown is compatible acceptance evidence when actor cleared chain");
        state.BeginAction(root, Skill(11, cd: uint.MaxValue - 20), Available(), Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(), new[] { Skill(11, cd: 50) }, Start), "wrapping game cooldown timestamp advances correctly");
        state.BeginAction(root, Skill(11), Available(), Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(), new[] { Skill(11, cd: 3000000000) }, Start), "first nonzero cooldown timestamp remains accepted after long game uptime");
        state = new();
        state.ObserveScope(Target(), Available(root: true));
        state.BeginAction(root, Skill(11, duration: 0), Available(root: true), Start, TimeSpan.FromSeconds(8));
        Check(!state.TryConfirmAction(Available(root: true), new[] { Skill(11, duration: 0) }, Start.AddSeconds(7)), "no-cooldown send without release evidence stays unconfirmed");
        Check(state.IsActionExpired(Start.AddSeconds(8)), "unaccepted action has finite confirmation deadline");
        state.RejectAction(Start.AddSeconds(8), TimeSpan.FromSeconds(1));
        Check(state.ActiveChainSource is null && state.IsRepeatBlocked(root, Start.AddMilliseconds(8500)), "unaccepted key has bounded local suppression");
        state.ObserveScope(Target(), Available());
        state.ObserveScope(Target(), Available(root: true));
        Check(!state.IsRepeatBlocked(root, Start.AddSeconds(10)), "finite suppression expires even if original opportunity stayed lit");
        return Task.CompletedTask;
    }

    public static Task ScopeAsync()
    {
        var plan = Plan(); var state = new QuickbarSkillCombatState();
        Check(!state.ObserveScope(Target(), Available()), "first scope accepted");
        state.BeginAction(plan.Roots[1], Skill(11), Available(root: true), Start, TimeSpan.FromSeconds(8));
        state.TryConfirmAction(Available(last: 11, time: 10), Learned, Start);
        Check(state.ObserveScope(Target(server: 101), Available(effective: 12)) && state.ActiveChainSource is null, "new server identity invalidates chain despite same entity slot");
        state.BeginAction(plan.Roots[1], Skill(11), Available(root: true), Start, TimeSpan.FromSeconds(8));
        Check(state.ObserveScope(Target(server: 101), Available(page: 1)) && state.PendingAction is null, "page change cancels pending old action");
        Check(state.ObserveScope(Target(server: 101), Available(page: 1, baseId: 41)), "base binding layout change invalidates state");
        state.BeginAction(plan.Roots[1], Skill(11), Available(root: true), Start, TimeSpan.FromSeconds(8));
        state.ObserveScope(LockedTargetSnapshot.Empty(Start), Available());
        Check(state.PendingAction is null && state.ActiveChainSource is null, "dead or missing target clears all combat work");

        state = new();
        var sparseRoot = Available(root: true) with { Slots = Array.Empty<SkillAvailabilitySlotSnapshot>(), BindingSignature = "page0-full-bindings" };
        state.ObserveScope(Target(), sparseRoot);
        state.BeginAction(plan.Roots[1], Skill(11), sparseRoot, Start, TimeSpan.FromSeconds(8));
        var sparseChild = Available(root: true, effective: 12, last: 11, time: 10) with
        { Slots = Available(root: true, effective: 12).Slots.Take(1).ToArray(), BindingSignature = "page0-full-bindings" };
        Check(!state.ObserveScope(Target(), sparseChild) && state.PendingAction is not null, "dynamic supported slot appearing does not cancel pending action when full binding signature is unchanged");
        Check(state.TryConfirmAction(sparseChild, Learned, Start) && state.ActiveChainSource is not null, "sparse root-to-child transition retains confirmed chain");
        Check(!state.ObserveScope(Target(), sparseRoot) && state.ActiveChainSource is not null, "supported slot disappearing leaves original layout scope intact");
        Check(state.ObserveScope(Target(), sparseRoot with { BindingSignature = "page0-replaced-binding" }) && state.ActiveChainSource is null, "full binding signature change still clears sparse state");
        return Task.CompletedTask;
    }

    public static async Task ControllerAsync()
    {
        var fixture = new Fixture();
        fixture.Reader.Value = Available(root: true);
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && fixture.State.PendingAction?.Node.SkillId == 11, "finite root press only after availability recheck");
        fixture.Reader.Value = Available(root: true, effective: 12, last: 11, time: 10);
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1", "D1" }) && fixture.State.PendingAction?.Node.SkillId == 12, "continuation uses inherited key ahead of counter");
        Check(fixture.State.ActiveChainSource?.SkillId == 11, "confirmation reselects the open continuation in the same tick");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 3 && fixture.State.PendingAction?.AttemptCount == 2, "still lit continuation retries after80ms without acceptance");
        fixture.Clock.Now += TimeSpan.FromSeconds(6);
        fixture.Reader.Value = Available(root: true, counter: true, effective: 13, last: 12, time: 20);
        await fixture.Tick();
        Check(fixture.State.ActiveChainSource?.SkillId == 12 && fixture.State.PendingAction?.Node.SkillId == 13 && fixture.Keyboard.Keys.Count == 4,
            "long middle release confirms and opened final executes in that tick");
        fixture.Reader.Value = Available(counter: true, last: 13, time: 30);
        await fixture.Tick();
        Check(fixture.State.ActiveChainSource is null && fixture.Keyboard.Keys.Last() == "D2", "confirmed final hands over to an available root immediately");

        fixture = new();
        fixture.Reader.Value = Available(root: true);
        fixture.Reader.AfterRead = count => { if (count == 2) fixture.Reader.Value = Available(); };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 0, "opportunity closing during baseline read prevents key");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        await fixture.Tick(readTarget: () => Task.FromResult(Target(server: 101)));
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.PendingAction is null, "target change at action boundary prevents key");

        fixture = new(); fixture.Reader.Value = Available(root: true); fixture.Keyboard.Fail = true;
        await fixture.Tick();
        Check(fixture.State.PendingAction is null && fixture.State.ActiveChainSource is null, "failed HID is not accepted game action");
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 1, "failed continuously lit key is suppressed");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        using var stop = new CancellationTokenSource();
        fixture.Reader.AfterRead = _ => stop.Cancel();
        try { await fixture.Tick(stop.Token); throw new Exception("cancellation expected"); }
        catch (OperationCanceledException) { }
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.PendingAction is null, "cancel during read prevents input and clears action state");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        await fixture.Tick();
        fixture.Reader.Value = Available();
        await fixture.Tick();
        fixture.Clock.Now += TimeSpan.FromSeconds(9);
        await fixture.Tick();
        Check(fixture.State.PendingAction is null && fixture.State.ActiveChainSource is null && fixture.Keyboard.Keys.Count == 1, "timeout does not falsely advance root or keep pressing");
    }

    public static async Task MixedReadinessAsync()
    {
        var plan = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(11, Node(12, Node(13))), Node(21), Node(31) } }, new(Bindings(), Learned));
        var state = new QuickbarSkillCombatState();
        var ready = new HashSet<uint> { 11, 31 };
        var hybrid = HybridAvailable(counter: true);
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid, Start, ready).Node?.SkillId == 11, "ordinary CD-ready root precedes later lit counter according to configured order");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid, Start, new HashSet<uint> { 31 }).Node?.SkillId == 21, "ordinary cooling root skipped before lit counter");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, HybridAvailable(), Start, new HashSet<uint>()).Kind == QuickbarSkillDecisionKind.None, "ordinary cooling roots and dark special roots send nothing");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid with { BindingSlots = null }, Start, ready).Node?.SkillId == 21, "unsupported ids alone do not prove ordinary shortcut binding");
        var replaced = hybrid with { BindingSlots = hybrid.BindingSlots!.Select(slot => slot.Slot == 0 && slot.Bar == SkillQuickbar.Main ? slot with { BaseSkillId = 41 } : slot).ToArray() };
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, replaced, Start, ready).Node?.SkillId == 21, "changed ordinary base anchor cannot press another skill");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, HybridAvailable(counter: true, effective: 12), Start, ready).Node?.SkillId == 21, "ordinary root key displaying a child cannot press root id");

        state.ObserveScope(Target(), hybrid);
        state.BeginAction(plan.Roots[0], Skill(11), hybrid, Start, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(HybridAvailable(last: 11, time: 10), Learned, Start), "ordinary root actual release confirms its chain source");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, HybridAvailable(counter: true, effective: 12, child: true), Start, ready).Node?.SkillId == 12, "opened special continuation precedes ordinary and counter roots");
        var unsupportedChild = HybridAvailable(counter: true, effective: 12) with
        { Slots = HybridAvailable(counter: true).Slots, UnsupportedSkillIds = new uint[] { 11, 12, 31 } };
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, unsupportedChild, Start, new HashSet<uint> { 11, 12, 31 }).Kind == QuickbarSkillDecisionKind.WaitForChain, "child never uses ordinary CD fallback even when ready set contains its id");
        state.EndChainTransition("test_elapsed");
        state.ObserveScope(Target(), hybrid);
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid, Start, ready).Node?.SkillId == 11,
            "confirmed ordinary root may be selected again when current calibrated CD says ready");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid, Start, new HashSet<uint> { 31 }).Node?.SkillId == 21,
            "ordinary root cooling now is skipped even though no supported slot exists for it");
        Check(QuickbarSkillReleasePriority.SelectNext(plan, state, hybrid, Start, ready).Node?.SkillId == 11,
            "ordinary CD ready-to-cooling-to-ready eligibility needs no synthetic icon gate");

        var keyboard = new Keyboard(); var reader = new Reader { Value = HybridAvailable(counter: true) }; var clock = new Clock();
        var controller = new QuickbarSkillCombatController(keyboard, clock); state = new();
        var reads = 0;
        await controller.TickAsync(plan, state, Target(), reader,
            _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(++reads == 1 ? Learned : Learned.Select(skill => skill.SkillId == 11 ? skill with { CooldownEndTime = 50000 } : skill).ToArray()),
            new(), ordinaryReadiness: skills => skills.Where(skill => skill.CooldownEndTime == 0).Select(skill => skill.SkillId).ToHashSet());
        Check(keyboard.Keys.SequenceEqual(new[] { "D2" }), "ordinary CD beginning during baseline read hands over to the lit counter in the same tick");

        keyboard = new(); reader = new() { Value = HybridAvailable() }; state = new(); controller = new(keyboard, clock);
        await controller.TickAsync(plan, state, Target(), reader, _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned), new(),
            ordinaryReadiness: _ => new HashSet<uint> { 11 });
        Check(keyboard.Keys.SequenceEqual(new[] { "D1" }), "mock without cooldown capability may explicitly authorize an ordinary root");
        await controller.TickAsync(plan, state, Target(), reader, _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned), new(),
            ordinaryReadiness: _ => new HashSet<uint> { 11 });
        Check(keyboard.Keys.Count == 1 && state.PendingAction is not null, "same unaccepted action is not retried before its80ms interval");

        keyboard = new(); reader = new() { Value = HybridAvailable() with { UnsupportedSkillIds = new uint[] { 21 }, Slots = Array.Empty<SkillAvailabilitySlotSnapshot>() } };
        state = new(); controller = new(keyboard, clock);
        await controller.TickAsync(plan, state, Target(), reader, _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned), new(),
            ordinaryReadiness: _ => new HashSet<uint> { 21 });
        Check(keyboard.Keys.Count == 0, "counter metadata prevents ordinary fallback despite caller mistakenly proposing its id");
        Check(!QuickbarSkillCombatController.IsOrdinarySkill(Learned.Single(skill => skill.SkillId == 12)), "prechain metadata excluded from ordinary readiness");
        Check(!QuickbarSkillCombatController.IsOrdinarySkill(Skill(41) with { XmlTargetValidStatuses = "Stun" }), "target-condition metadata excluded from ordinary readiness");
    }

    public static async Task ChainReadBudgetAsync()
    {
        Fixture Chaining(bool lit)
        {
            var fixture = new Fixture();
            fixture.State.ObserveScope(Target(), HybridAvailable());
            fixture.State.BeginAction(fixture.Plan.Roots[1], Skill(11), HybridAvailable(), Start, TimeSpan.FromSeconds(8));
            fixture.State.TryConfirmAction(HybridAvailable(last: 11, time: 10), Learned, Start);
            fixture.Reader.Value = HybridAvailable(counter: true, effective: 12, child: lit, last: 11, time: 10);
            return fixture;
        }

        var fixture = Chaining(true);
        var reads = new List<uint[]>(); var ordinaryCalculations = 0;
        var controller = new QuickbarSkillCombatController(fixture.Keyboard, fixture.Clock);
        Task<IReadOnlyList<SkillSnapshot>> Read(IReadOnlyCollection<uint> ids)
        {
            reads.Add(ids.ToArray());
            return Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned.Where(skill => ids.Contains(skill.SkillId)).ToArray());
        }
        IReadOnlySet<uint> Ordinary(IReadOnlyList<SkillSnapshot> skills)
        {
            ordinaryCalculations++;
            return new HashSet<uint> { 11, 31 };
        }
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Ordinary);
        Check(reads.Count == 1 && reads.Single().SequenceEqual(new uint[] { 12 }), "lit active continuation reads exactly its own baseline once, without full-plan reads");
        Check(ordinaryCalculations == 0 && fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }), "chain priority does not spend its opportunity calculating ordinary roots");
        reads.Clear(); fixture.Reader.Value = HybridAvailable(effective: 13, child: true, last: 12, time: 20);
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Ordinary);
        Check(reads.Count == 2 && reads[0].SequenceEqual(new uint[] { 12 }) && reads[1].SequenceEqual(new uint[] { 13 }) && ordinaryCalculations == 0,
            "pending special release confirms and reads only the next child baseline without any ordinary plan read");
        Check(fixture.State.ActiveChainSource?.SkillId == 12 && fixture.Keyboard.Keys.Count == 2, "fast confirmation advances accepted stage and presses the open next stage in the same tick");

        fixture = Chaining(false); reads.Clear(); ordinaryCalculations = 0;
        controller = new(fixture.Keyboard, fixture.Clock);
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Ordinary);
        Check(reads.Count == 1 && reads.Single().SequenceEqual(new uint[] { 12 }) && ordinaryCalculations == 0 && fixture.Keyboard.Keys.Count == 0,
            "dark continuation polls only its exact child while protecting the finite handoff gap");
        Check(fixture.State.ActiveChainSource?.SkillId == 11, "fast dark poll retains confirmed predecessor");

        fixture = Chaining(true); reads.Clear(); ordinaryCalculations = 0;
        controller = new(fixture.Keyboard, fixture.Clock);
        fixture.Reader.AfterRead = count => { if (count == 2) fixture.Reader.Value = HybridAvailable(counter: true); };
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Ordinary);
        Check(reads[0].SequenceEqual(new uint[] { 12 }) && fixture.Keyboard.Keys.Count == 0 && fixture.State.PendingAction is null,
            "fast path refuses a temporarily closed continuation and keeps its bounded handoff protection");

        fixture = new(); reads.Clear(); ordinaryCalculations = 0;
        fixture.State.ObserveScope(Target(), HybridAvailable());
        fixture.State.BeginAction(fixture.Plan.Roots[2], Skill(31), HybridAvailable(), Start, TimeSpan.FromSeconds(8));
        fixture.State.TryConfirmAction(HybridAvailable(last: 31, time: 5), Learned, Start);
        fixture.State.BeginAction(fixture.Plan.Roots[1], Skill(11), HybridAvailable(last: 31, time: 5), Start, TimeSpan.FromSeconds(8));
        fixture.Reader.Value = HybridAvailable(last: 11, time: 10);
        controller = new(fixture.Keyboard, fixture.Clock);
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader,
            ids => { reads.Add(ids.ToArray()); return Task.FromResult<IReadOnlyList<SkillSnapshot>>(new[] { Skill(11, cd: 31000) }); },
            new(), ordinaryReadiness: skills => { ordinaryCalculations++; return new HashSet<uint>(); });
        Check(reads.Count == 2 && reads[0].SequenceEqual(new uint[] { 11 }) && reads[1].SequenceEqual(new uint[] { 12 }) && ordinaryCalculations == 0,
            "pending ordinary confirmation reads its next exact child and protects the handoff without comparing roots");
        Check(fixture.State.PendingAction is null && fixture.Keyboard.Keys.Count == 0,
            "empty current ordinary readiness does not infer another root is usable from stale lifecycle state");
    }

    public static async Task EightyMillisecondLoopAsync()
    {
        var fixture = new Fixture();
        fixture.Reader.Value = Available(root: true, ordinary: true, time: 100);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        var first = fixture.State.PendingAction!;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(79));
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.Count == 1, "same action cannot retry at79ms");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        fixture.Skills = Learned.Select(skill => skill.SkillId == 11 ? skill with { CooldownEndTime = 77 } : skill).ToArray();
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        var retried = fixture.State.PendingAction!;
        Check(fixture.Keyboard.Keys.Count == 2 && retried.AttemptCount == 2, "lit ready action retries at80ms without waiting for a release");
        Check(retried.PreviousCooldownEndTime == first.PreviousCooldownEndTime && retried.PreviousReleasedSkillTime == first.PreviousReleasedSkillTime &&
            retried.StartedAt == first.StartedAt && retried.Deadline == first.Deadline, "retries preserve all first-press confirmation baselines");
        fixture.Reader.Value = Available(root: true, effective: 12, last: 11, time: 110);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.Count == 3 && fixture.State.PendingAction?.Node.SkillId == 12 && fixture.State.ActiveChainSource?.SkillId == 11,
            "actual acceptance immediately selects the currently bright configured next stage");

        fixture = new(); fixture.Reader.Value = Available(root: true, time: 100);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Reader.Value = Available(root: true, effective: 12, time: 100);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.State.ActiveChainSource is null && fixture.State.PendingAction?.Node.SkillId == 12 && fixture.Keyboard.Keys.Count == 2,
            "displayed bright configured child takes over without falsely confirming the previous root");
        fixture.Reader.Value = Available(root: true, effective: 999, time: 100);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.Count == 2 && fixture.State.PendingAction is null, "unconfigured displayed stage stops old retries and is never pressed");

        fixture = new(); fixture.Reader.Value = Available(root: true, ordinary: true, time: 100);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Skills = Learned.Select(skill => skill.SkillId == 11 ? skill with { CooldownEndTime = 50000 } : skill).ToArray();
        await fixture.Tick(cooldown: skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && fixture.State.ActiveChainSource is null && fixture.State.ChainTransition?.Reason == "cooldown_started",
            "true CD progression protects the handoff without pretending the actor release was confirmed");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        await fixture.Tick(cooldown: skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1", "NumPadAdd" }) && fixture.State.ActiveChainSource is null,
            "unconfirmed provisional handoff is finite and then allows the next ready root");

        fixture = new(); fixture.Reader.Value = Available(root: true, time: 100);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Reader.Value = Available(counter: true, effective: 12, last: 11, time: 110);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && fixture.State.ActiveChainSource?.SkillId == 11 && fixture.State.ChainTransition is not null,
            "dark next stage retains the confirmed predecessor and waits for its late opportunity");
        fixture.Reader.Value = Available(root: true, counter: true, effective: 12, last: 11, time: 110);
        await fixture.Tick(cooldown: skill => skill.SkillId == 12 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1", "D2" }) && fixture.State.ChainTransition is null, "all configured children in known CD end the wait and hand over without pressing a cooling stage");

        fixture = new(); fixture.Reader.Value = Available(root: true, counter: true, ordinary: true);
        fixture.Skills = Learned.Select(skill => skill with { CooldownDuration = 0 }).ToArray();
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Reader.Value = Available(root: true, counter: true, ordinary: true, last: 21, time: 1);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Reader.Value = Available(root: true, counter: true, ordinary: true, last: 11, time: 2);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        fixture.Reader.Value = Available(root: true, counter: true, ordinary: true, last: 31, time: 3);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D2", "D1", "NumPadAdd", "D2" }), "accepted zero-CD roots yield configured order before starting a new round");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        var originalAttempt = default(QuickbarSkillPendingAction);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
            originalAttempt ??= fixture.State.PendingAction;
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
        }
        Check(fixture.Keyboard.Keys.Count == 16 && fixture.State.PendingAction?.StartedAt == originalAttempt?.StartedAt,
            "normal animation longer than eight poll cycles keeps retrying the lit ready skill with its first baseline");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        for (var attempt = 0; attempt < QuickbarSkillCombatState.MaximumUnconfirmedAttempts; attempt++)
        {
            await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
        }
        fixture.Reader.Value = Available(root: true, ordinary: true);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        Check(fixture.Keyboard.Keys.Count == QuickbarSkillCombatState.MaximumUnconfirmedAttempts + 1 && fixture.Keyboard.Keys.Last() == "NumPadAdd",
            "bounded unaccepted retries locally suppress one root so another root cannot starve");

        fixture = new(); fixture.Reader.Value = HybridAvailable();
        await fixture.Tick(ordinary: _ => new HashSet<uint> { 11, 31 }, cooldown: _ => SemiAutoSkillCooldownReadiness.Unknown);
        Check(fixture.Keyboard.Keys.Count == 0, "ordinary Unknown is never silently treated as definitely ready");
        fixture.Reader.Value = HybridAvailable(counter: true);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Unknown);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D2" }), "validated special opportunity may use its own CanUse signal when CD domain is unknown");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        fixture.Reader.AfterRead = _ => fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        fixture.Keyboard.OnPress = hold => fixture.Clock.Advance(hold);
        var controller = new QuickbarSkillCombatController(fixture.Keyboard, fixture.Clock);
        var delay = await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader,
            _ => { fixture.Clock.Advance(TimeSpan.FromMilliseconds(15)); return Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned); },
            new() { KeyHoldMs = 2000 });
        Check(delay == TimeSpan.FromMilliseconds(15) && fixture.Keyboard.Holds.Single() == TimeSpan.FromMilliseconds(30),
            "80ms budget includes all reads and finite key hold capped at30ms for the new mode");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        fixture.Reader.AfterRead = _ => fixture.Clock.Advance(TimeSpan.FromMilliseconds(50));
        controller = new(fixture.Keyboard, fixture.Clock);
        delay = await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader,
            _ => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned), new());
        Check(delay == TimeSpan.Zero, "a tick already exceeding80ms does not request another sleep");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        fixture.Reader.AfterRead = _ => fixture.Clock.Now += TimeSpan.FromHours(1);
        delay = await fixture.Tick();
        Check(delay == TimeSpan.FromMilliseconds(80), "tick pacing is monotonic and does not follow wall clock jumps");
    }

    public static async Task RetryCycleReadVariationAsync()
    {
        var fixture = new Fixture();
        fixture.Reader.Value = Available(root: true, ordinary: true, time: 100);
        fixture.Reader.AfterRead = count => { if (count == 1) fixture.Clock.Advance(TimeSpan.FromMilliseconds(20)); };
        var pressedAt = new List<DateTimeOffset>();
        fixture.Keyboard.OnPress = hold => { pressedAt.Add(fixture.Clock.Now); fixture.Clock.Advance(hold); };
        var delay = await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        var first = fixture.State.PendingAction!;
        Check(delay == TimeSpan.FromMilliseconds(35) && first.StartedAt == Start.AddMilliseconds(20) && first.LastAttemptAt == Start,
            "first confirmation timestamp is the actual action boundary while retry schedule starts at the poll-cycle boundary");
        fixture.Clock.Advance(delay);
        await fixture.Tick(cooldown: _ => SemiAutoSkillCooldownReadiness.Ready);
        var second = fixture.State.PendingAction!;
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1", "D1" }) && second.AttemptCount == 2,
            "a faster next read retries at the next80ms poll instead of losing an entire second poll");
        Check(pressedAt[1] - pressedAt[0] == TimeSpan.FromMilliseconds(60),
            "variable read time may change press spacing without adding another80ms delay");
        Check(second.StartedAt == first.StartedAt && second.Deadline == first.Deadline &&
            second.PreviousCooldownEndTime == first.PreviousCooldownEndTime && second.PreviousReleasedSkillTime == first.PreviousReleasedSkillTime,
            "poll-cycle retries preserve original action lifetime and acceptance evidence");
    }

    public static async Task CombatGuardAsync()
    {
        SkillAvailabilityCombatSnapshot Guard(ushort targetEntity = 50, uint targetServer = 100, uint hp = 100) =>
            new(1, 10, targetEntity, targetServer, hp, 100, 100, 100);
        var fixture = new Fixture(); fixture.Reader.Value = Available(root: true) with { CombatState = Guard(targetServer: 101) };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.YieldToWorker && fixture.State.SupportsCombatState,
            "same entity with a different known target server identity yields before input");
        fixture = new(); fixture.Reader.Value = Available(root: true) with { CombatState = Guard(hp: 0) };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.YieldToWorker, "dead player guard yields before input");

        fixture = new(); fixture.Reader.Value = Available(root: true) with { CombatState = Guard() };
        var checks = 0;
        await fixture.Tick(readTarget: () => throw new Exception("combined guard must avoid old target scan"),
            allowCombat: _ => ++checks < 2);
        Check(checks == 2 && fixture.Keyboard.Keys.Count == 0 && fixture.State.YieldToWorker,
            "maintenance policy runs on both initial and action-boundary combat guards");
        fixture = new(); fixture.Reader.Value = Available(root: true) with { CombatState = Guard() };
        fixture.Reader.AfterRead = count => { if (count == 2) fixture.Reader.Value = fixture.Reader.Value with { CombatState = Guard(targetEntity: 51) }; };
        await fixture.Tick(readTarget: () => throw new Exception("no old target scan"));
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.PendingAction is null && fixture.State.YieldToWorker,
            "combined target change during action preparation clears old attempts");

        fixture = new(); fixture.Reader.Value = Available(root: true) with { CombatState = Guard(targetServer: 0) };
        await fixture.Tick(readTarget: () => throw new Exception("no old target scan"));
        Check(fixture.Keyboard.Keys.Count == 1 && fixture.State.SupportsCombatState && !fixture.State.YieldToWorker,
            "known selected target entity with unavailable server id uses entity identity");

        fixture.Reader.Value = Available(root: true, effective: 12, last: 11, time: 1) with { CombatState = Guard() };
        await fixture.Tick();
        Check(fixture.State.ActiveChainSource?.SkillId == 11 && fixture.State.PendingAction?.Node.SkillId == 12, "guarded real release creates chain and a new pending stage");
        fixture.State.SuspendInputAttempts();
        Check(fixture.State.PendingAction is null && fixture.State.ActiveChainSource?.SkillId == 11,
            "maintenance takeover clears old attempt baseline while retaining confirmed chain identity");

        fixture = new(); fixture.Reader.Value = Available(root: true);
        await fixture.Tick();
        Check(!fixture.State.SupportsCombatState, "old mock capability does not authorize a fast multi-tick burst");
        fixture.Reader.Value = fixture.Reader.Value with { Page = 1 };
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == 1 && fixture.State.YieldToWorker && fixture.State.PendingAction is null,
            "page change yields to worker and invalidates any pending key");
    }

    public static async Task AvailabilityClockBootstrapAsync()
    {
        SemiAutoSkillCooldownReadiness LowerBound(SkillSnapshot skill, SkillAvailabilitySnapshot availability) =>
            skill.CooldownDuration == 0 || skill.CooldownEndTime == 0 ||
            (availability.LastReleasedSkillTime != 0 && unchecked((int)(availability.LastReleasedSkillTime - skill.CooldownEndTime)) >= 0)
                ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        IReadOnlySet<uint> Propose(IReadOnlyList<SkillSnapshot> skills) => skills.Select(skill => skill.SkillId).ToHashSet();
        var fixture = new Fixture();
        fixture.Reader.Value = HybridAvailable(last: 31, time: 1_000_200);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = skill.SkillId == 31 ? 1_000_400u : 1_000_000u }).ToArray();
        var sharedClock = new SemiAutoCombatState();
        const uint unrelatedOsTick = 2_000_000_000;
        var controller = new QuickbarSkillCombatController(fixture.Keyboard, fixture.Clock);
        Task<IReadOnlyList<SkillSnapshot>> Read(IReadOnlyCollection<uint> ids)
        {
            IReadOnlyList<SkillSnapshot> skills = fixture.Skills.Where(skill => ids.Contains(skill.SkillId)).ToArray();
            sharedClock.TryUpdateCooldownTickCalibration(skills, unrelatedOsTick, fixture.Clock.Now, out _);
            return Task.FromResult(skills);
        }
        SemiAutoSkillCooldownReadiness Strict(SkillSnapshot skill, SkillAvailabilitySnapshot availability)
        {
            if (!sharedClock.HasCooldownTickCalibration) return LowerBound(skill, availability);
            var gameTick = sharedClock.EstimateGameTick(unrelatedOsTick);
            return skill.CooldownEndTime == 0 || unchecked((int)(skill.CooldownEndTime - gameTick)) <= 0
                ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.CoolingDown;
        }
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Propose,
            cooldownReadiness: _ => SemiAutoSkillCooldownReadiness.CoolingDown, availabilityCooldownReadiness: Strict);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && !sharedClock.HasCooldownTickCalibration,
            "same-domain actual release lower bound proves expired ordinary root ready without guessing a foreign OS clock");
        Check(fixture.State.PendingAction?.PreviousCooldownEndTime == 1_000_000,
            "safe bootstrap retains actual nonzero old-CD baseline for acceptance and calibration");

        fixture.Skills = fixture.Skills.Select(skill => skill.SkillId == 11 ? skill with { CooldownEndTime = 1_040_000 } : skill).ToArray();
        fixture.Reader.Value = HybridAvailable(effective: 12, child: true, last: 11, time: 1_010_000);
        await controller.TickAsync(fixture.Plan, fixture.State, Target(), fixture.Reader, Read, new(), ordinaryReadiness: Propose,
            availabilityCooldownReadiness: Strict);
        Check(sharedClock.HasCooldownTickCalibration && sharedClock.EstimateGameTick(unrelatedOsTick) == 1_010_000,
            "observed exact root CD advancing after safe first press calibrates the existing clock");
        Check(fixture.State.ActiveChainSource?.SkillId == 11 && fixture.State.PendingAction?.Node.SkillId == 12 && fixture.Keyboard.Keys.Count == 2,
            "actual bootstrap release immediately hands over to the currently open chain with calibrated cooldown readiness");

        fixture = new(); fixture.Reader.Value = HybridAvailable(last: 31, time: 1_000_200);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = 1_000_400 }).ToArray();
        await fixture.Tick(ordinary: Propose, availabilityClock: LowerBound);
        Check(fixture.Keyboard.Keys.Count == 0, "a future CD end relative to the actual-release lower bound remains Unknown and cannot bootstrap");
        fixture.Reader.Value = HybridAvailable();
        await fixture.Tick(ordinary: Propose, availabilityClock: LowerBound);
        Check(fixture.Keyboard.Keys.Count == 0, "absent release clock cannot authorize an arbitrary first press");

        fixture = new(); fixture.Reader.Value = HybridAvailable(time: 50);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = skill.SkillId == 11 ? uint.MaxValue - 20 : 100u }).ToArray();
        await fixture.Tick(ordinary: Propose, availabilityClock: LowerBound);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }), "same-domain lower bound handles uint32 clock rollover");

        fixture = new(); fixture.Reader.Value = HybridAvailable(time: 50);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = 1000, CooldownDuration = skill.SkillId == 11 ? 0u : 30000u }).ToArray();
        await fixture.Tick(ordinary: Propose, availabilityClock: LowerBound);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }), "official zero-duration skill needs no clock bootstrap even with a retained end tick");

        fixture = new(); fixture.Reader.Value = HybridAvailable(time: 1_000_200);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = 1_000_000 }).ToArray();
        var boundaryCalls = new List<uint>();
        fixture.Reader.AfterRead = count => { if (count == 2) fixture.Reader.Value = fixture.Reader.Value with { LastReleasedSkillTime = 1_000_300 }; };
        await fixture.Tick(ordinary: Propose, availabilityClock: (skill, availability) =>
        {
            boundaryCalls.Add(availability.LastReleasedSkillTime);
            return availability.LastReleasedSkillTime == 1_000_300 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready;
        });
        Check(boundaryCalls.Contains(1_000_300) && fixture.Keyboard.Keys.Count == 0,
            "action-boundary availability recomputes both ordinary eligibility and known CD before any key");
    }

    public static async Task ReleaseClockLowerBoundAsync()
    {
        var fixture = new Fixture();
        fixture.Reader.Value = HybridAvailable(last: 11, time: 1_000_000);
        fixture.Skills = Learned.Select(skill => skill with { CooldownEndTime = 1_001_000 }).ToArray();
        IReadOnlySet<uint> OnlyRoot(IReadOnlyList<SkillSnapshot> _) => new HashSet<uint> { 11 };
        SemiAutoSkillCooldownReadiness Readiness(SkillSnapshot skill, SkillAvailabilitySnapshot availability)
        {
            var lowerBound = fixture.State.ObserveReleaseClockLowerBound(availability.LastReleasedSkillTime, fixture.Clock);
            return lowerBound.HasValue && unchecked((int)(lowerBound.Value - skill.CooldownEndTime)) >= 0
                ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        }
        await fixture.Tick(ordinary: OnlyRoot, availabilityClock: Readiness);
        Check(fixture.Keyboard.Keys.Count == 0, "single ordinary skill still cooling at the first lower-bound observation is not guessed ready");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(999));
        fixture.Clock.Now += TimeSpan.FromDays(3);
        await fixture.Tick(ordinary: OnlyRoot, availabilityClock: Readiness);
        Check(fixture.Keyboard.Keys.Count == 0, "wall clock jumps cannot shorten a monotonic lower-bound cooldown wait");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await fixture.Tick(ordinary: OnlyRoot, availabilityClock: Readiness);
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D1" }) && fixture.Reader.Value.LastReleasedSkillTime == 1_000_000,
            "running-clock lower-bound estimate reaches CD end and bootstraps one skill without a new release event");

        var state = new QuickbarSkillCombatState(); var clock = new Clock();
        Check(state.ObserveReleaseClockLowerBound(0, clock) is null, "zero actual release time supplies no clock anchor");
        Check(state.ObserveReleaseClockLowerBound(1000, clock) == 1000, "first calibration retains the actual tick without extrapolating its unknown age");
        clock.Advance(TimeSpan.FromMilliseconds(2000));
        Check(state.ObserveReleaseClockLowerBound(1000, clock) == 3000, "unchanged release record advances only through elapsed monotonic time");
        Check(state.ObserveReleaseClockLowerBound(2000, clock) == 2000, "new actual release reanchors conservatively even if the old estimate was later");
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Check(state.ObserveReleaseClockLowerBound(2000, clock) == 2500, "new release anchor starts a new elapsed interval");
        Check(state.ObserveReleaseClockLowerBound(0, clock) is null, "loss of an actual release tick clears extrapolation instead of guessing from old history");

        state.Reset();
        Check(state.ObserveReleaseClockLowerBound(uint.MaxValue - 10, clock) == uint.MaxValue - 10, "rollover test starts from an actual tick");
        clock.Advance(TimeSpan.FromMilliseconds(20));
        Check(state.ObserveReleaseClockLowerBound(uint.MaxValue - 10, clock) == 9, "lower-bound game tick wraps uint32 just like cooldown ticks");
        state.Reset();
        Check(state.ObserveReleaseClockLowerBound(uint.MaxValue - 10, clock) == uint.MaxValue - 10, "stop resets all clock timing anchors");

        state.ObserveScope(Target(), HybridAvailable());
        state.ObserveReleaseClockLowerBound(1000, clock);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        state.ObserveScope(Target(server: 101), HybridAvailable());
        Check(state.ObserveReleaseClockLowerBound(1000, clock) == 1000, "target scope transition discards the previous calibration anchor");
        clock.Advance(TimeSpan.FromMilliseconds(300));
        state.SuspendInputAttempts();
        Check(state.ObserveReleaseClockLowerBound(1000, clock) == 1300, "maintenance input suspension preserves the session clock calibration");

        state.Reset(); state.ObserveReleaseClockLowerBound(1000, clock);
        clock.Advance(TimeSpan.FromMilliseconds((long)int.MaxValue + 1));
        Check(state.ObserveReleaseClockLowerBound(1000, clock) == 1000, "half-range elapsed interval reanchors instead of making ambiguous wrapped assumptions");
    }

    private sealed class Fixture
    {
        public readonly QuickbarSkillPlan Plan = QuickbarSkillEngineTests.Plan();
        public readonly QuickbarSkillCombatState State = new();
        public readonly Reader Reader = new();
        public readonly Keyboard Keyboard = new();
        public readonly Clock Clock = new();
        public IReadOnlyList<SkillSnapshot> Skills = Learned;
        public Task<TimeSpan> Tick(CancellationToken cancellationToken = default, Func<Task<LockedTargetSnapshot>>? readTarget = null,
            Func<IReadOnlyList<SkillSnapshot>, IReadOnlySet<uint>>? ordinary = null,
            Func<SkillSnapshot, SemiAutoSkillCooldownReadiness>? cooldown = null,
            Func<SkillAvailabilityCombatSnapshot, bool>? allowCombat = null,
            Func<SkillSnapshot, SkillAvailabilitySnapshot, SemiAutoSkillCooldownReadiness>? availabilityClock = null) =>
            new QuickbarSkillCombatController(Keyboard, Clock).TickAsync(Plan, State, Target(), Reader,
                _ => Task.FromResult(Skills), new() { ConfirmTimeoutMs = 500, ChainTickIntervalMs = 30 },
                cancellationToken: cancellationToken, readTargetBeforePress: readTarget, ordinaryReadiness: ordinary,
                cooldownReadiness: cooldown, allowCombatSnapshot: allowCombat, availabilityCooldownReadiness: availabilityClock);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Start;
        public long Stamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { Now += duration; Stamp += duration.Ticks; }
    }
    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Available();
        public Action<int>? AfterRead;
        private int _reads;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AfterRead?.Invoke(++_reads);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(_reads, Value));
        }
    }
    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<string> Keys = new();
        public readonly List<TimeSpan> Holds = new();
        public Action<TimeSpan>? OnPress;
        public bool Fail;
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Keys.Add(key); Holds.Add(holdDuration); OnPress?.Invoke(holdDuration); return Task.FromResult(Fail ? OperationResult.Fail("mock") : OperationResult.Ok()); }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
