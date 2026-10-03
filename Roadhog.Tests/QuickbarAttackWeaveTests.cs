using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarAttackWeaveTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T10:00:00+08:00");

    public static async Task PairDelayAndMixedSkillsAsync()
    {
        var f = new Fixture();
        await MixedPair(f);
        Equal(2, f.State.AttackWeave.ConfirmedCount, "ordinary and conditional confirmed releases form one pair");
        Check(f.State.AttackWeave.IsWaiting, "second confirmed release starts the configured pause");
        Sequence(new[] { "NumPadAdd", "D2" }, f.Keyboard.Keys, "a confirmed pair has not yet sent C");
        f.Clock.ShiftWall(TimeSpan.FromHours(1));
        await f.Tick();
        Check(!f.Keyboard.Keys.Contains("C"), "a wall-clock jump cannot shorten the monotonic attack pause");
        var reads = f.Reader.ReadCount;
        f.Clock.Advance(529);
        await f.Tick();
        Equal(reads + 1, f.Reader.ReadCount, "waiting keeps reading the official combat guard");
        Check(!f.Keyboard.Keys.Contains("C"), "C cannot run before all 530 milliseconds elapse");
        f.Clock.Advance(1);
        await f.Tick();
        Sequence(new[] { "NumPadAdd", "D2", "C" }, f.Keyboard.Keys, "C runs once at the deadline");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "successful C clears only the completed pair");
        Check(!f.State.AttackWeave.IsWaiting, "successful C ends the pause");
    }

    public static async Task ChainStagesAndExpiryAsync()
    {
        var f = new Fixture();
        await ChainPair(f);
        Equal(2, f.State.AttackWeave.ConfirmedCount, "root and a confirmed continuation both count");
        Sequence(new[] { "D1", "D1" }, f.Keyboard.Keys, "third stage is held while C is pending");
        f.Clock.Advance(530);
        await f.Tick();
        Sequence(new[] { "D1", "D1", "C" }, f.Keyboard.Keys, "C owns its tick");
        await f.Tick();
        Sequence(new[] { "D1", "D1", "C", "D1" }, f.Keyboard.Keys, "still available continuation retains its inherited shortcut");
        Equal(13u, f.State.PendingAction!.Node.SkillId, "the third configured stage follows the pause");

        f = new Fixture(delayMs: 1600);
        await ChainPair(f);
        f.Reader.Value = Bar(counter: true, effective: 13, last: 12, time: 20);
        f.Clock.Advance(1600);
        await f.Tick();
        await f.Tick();
        Sequence(new[] { "D1", "D1", "C", "D2" }, f.Keyboard.Keys, "expired dark continuation cannot be made usable by C");
        Check(f.State.ChainTransition is null, "C does not extend the real chain handoff window");
    }

    public static async Task RetryConfirmationAsync()
    {
        var f = new Fixture();
        f.Reader.Value = Bar(ordinary: true);
        await f.Tick();
        f.Clock.Advance(80);
        await f.Tick();
        f.Clock.Advance(80);
        await f.Tick();
        Equal(0, f.State.AttackWeave.ConfirmedCount, "three input attempts without execution evidence count zero");
        f.Reader.Value = Bar(counter: true, last: 31, time: 10);
        f.Clock.Advance(80);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "late acceptance counts the retried ordinary action once");
        f.Clock.Advance(80);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "retrying the next conditional action cannot duplicate the first release");
        f.Reader.Value = Bar(last: 21, time: 20);
        f.Clock.Advance(80);
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "one actual second release completes the pair");
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "unchanged actor record cannot count again");
        f.Clock.Advance(530);
        await f.Tick();
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "retries result in exactly one C for two releases");
    }

    public static async Task AttackFailureAndZeroDelayAsync()
    {
        var f = new Fixture(delayMs: 0);
        f.Keyboard.FailAttack = true;
        await MixedPair(f);
        Sequence(new[] { "NumPadAdd", "D2", "C" }, f.Keyboard.Keys, "zero-delay C runs in the second-confirmation tick");
        Equal(2, f.State.AttackWeave.ConfirmedCount, "failed C retains the successful pair");
        f.Reader.Value = Bar(root: true, last: 21, time: 20);
        f.Clock.Advance(99);
        await f.Tick();
        Equal(3, f.Keyboard.Keys.Count, "failed C blocks attacks and retrying before 100 milliseconds");
        f.Clock.Advance(1);
        f.Keyboard.FailAttack = false;
        await f.Tick();
        Sequence(new[] { "NumPadAdd", "D2", "C", "C" }, f.Keyboard.Keys, "C retries at 100 milliseconds and owns that tick");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "successful retry clears the pair");
        await f.Tick();
        Equal("D1", f.Keyboard.Keys.Last(), "normal skill selection resumes after successful C");
    }

    public static async Task DisabledReadCadenceAsync()
    {
        var f = new Fixture(enabled: false, delayMs: 530);
        var delays = new List<TimeSpan>();
        f.Reader.Value = Bar(root: true);
        delays.Add(await f.Tick());
        f.Clock.Advance(80);
        delays.Add(await f.Tick());
        f.Reader.Value = Bar(root: true, effective: 12, last: 11, time: 10);
        f.Clock.Advance(80);
        delays.Add(await f.Tick());
        f.Reader.Value = Bar(root: true, effective: 13, last: 12, time: 20);
        f.Clock.Advance(80);
        delays.Add(await f.Tick());
        f.Reader.Value = Bar(counter: true, last: 13, time: 30);
        f.Clock.Advance(80);
        delays.Add(await f.Tick());
        Sequence(new[] { "D1", "D1", "D1", "D1", "D2" }, f.Keyboard.Keys, "disabled mode preserves immediate retries, chains and conditional handoff");
        Equal(10, f.Reader.ReadCount, "disabled mode retains two availability reads per skill input");
        Sequence(new[] { "11", "11", "11", "12", "12", "13", "13", "21" }, f.SkillReads,
            "disabled mode retains the original exact pending, child and selected-root skill queries");
        Check(delays.All(delay => delay == TimeSpan.FromMilliseconds(80)), "disabled mode retains the original 80 millisecond cadence");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "disabled confirmations do not leave card-blade state");
        Check(!f.State.AttackWeave.IsWaiting && f.State.PendingAction?.Node.SkillId == 21,
            "disabled card-blade logic leaves the original pending conditional action intact");

        f = new Fixture();
        await MixedPair(f);
        f.Settings.AttackWeaveEnabled = false;
        f.Reader.Value = Bar(root: true, last: 21, time: 20);
        var reads = f.Reader.ReadCount;
        await f.Tick();
        Equal("D1", f.Keyboard.Keys.Last(), "turning off the option immediately resumes the original skill path");
        Equal(reads + 2, f.Reader.ReadCount, "turning off does not add a C-specific guard read");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "turning off cancels the scheduled C");
    }

    public static async Task LifecycleResetAsync()
    {
        foreach (var transition in new[] { "target", "death", "page", "bindings", "stop", "maintenance" })
        {
            var f = new Fixture();
            await MixedPair(f);
            f.Reader.Value = Bar(last: 21, time: 20);
            switch (transition)
            {
                case "target":
                    f.Target = Target(server: 101);
                    f.Reader.Value = f.Reader.Value with { CombatState = Guard(targetServer: 101) };
                    break;
                case "death": f.Reader.Value = f.Reader.Value with { CombatState = Guard(hp: 0) }; break;
                case "page": f.Reader.Value = f.Reader.Value with { Page = 1 }; break;
                case "bindings": f.Reader.Value = f.Reader.Value with { BindingSignature = "changed-binding-layout" }; break;
                case "maintenance": f.State.SuspendInputAttempts(); break;
                case "stop":
                    using (var stop = new CancellationTokenSource())
                    {
                        stop.Cancel();
                        try { await f.Tick(stop.Token); throw new Exception("cancellation expected"); }
                        catch (OperationCanceledException) { }
                    }
                    break;
            }
            if (transition is not ("stop" or "maintenance")) await f.Tick();
            Check(!f.State.AttackWeave.IsWaiting && f.State.AttackWeave.ConfirmedCount == 0,
                transition + " clears the successful pair and scheduled C");
            f.Clock.Advance(1000);
            await f.Tick();
            Check(!f.Keyboard.Keys.Contains("C"), transition + " cannot send the cancelled C later");
        }
    }

    public static async Task GuardAndMaintenancePreemptionAsync()
    {
        var f = new Fixture();
        await MixedPair(f);
        var reads = f.Reader.ReadCount;
        await f.Tick(allowCombat: _ => false);
        Equal(reads + 1, f.Reader.ReadCount, "maintenance policy runs on the initial official wait guard");
        Check(f.State.YieldToWorker && !f.State.AttackWeave.IsWaiting, "maintenance takeover cancels C and returns control to the worker");
        f.Clock.Advance(530);
        await f.Tick();
        Check(!f.Keyboard.Keys.Contains("C"), "maintenance takeover cannot leave a delayed attack behind");

        foreach (var change in new[] { "death", "target", "maintenance", "binding" })
        {
            f = new Fixture();
            await MixedPair(f);
            f.Clock.Advance(530);
            var boundaryRead = f.Reader.ReadCount + 2;
            f.Reader.AfterRead = count =>
            {
                if (count != boundaryRead) return;
                if (change == "death") f.Reader.Value = f.Reader.Value with { CombatState = Guard(hp: 0) };
                if (change == "target") f.Reader.Value = f.Reader.Value with { CombatState = Guard(targetServer: 101) };
                if (change == "maintenance") f.Reader.Value = f.Reader.Value with { CombatState = Guard(hp: 40) };
                if (change == "binding") f.Reader.Value = f.Reader.Value with { BindingSignature = "new-layout-at-C-boundary" };
            };
            await f.Tick(allowCombat: combat => combat.HpPercent > 50);
            Equal(boundaryRead, f.Reader.ReadCount, change + " is rechecked immediately before C");
            Check(!f.Keyboard.Keys.Contains("C") && f.State.YieldToWorker,
                change + " arriving at the C boundary prevents the attack");
            Check(!f.State.AttackWeave.IsWaiting, change + " at the C boundary clears the pause");
        }

        f = new Fixture();
        await MixedPair(f);
        f.Clock.Advance(530);
        f.Reader.Value = f.Reader.Value with { CombatState = null };
        var targetReads = 0;
        await f.Tick(readTarget: () => { targetReads++; return Task.FromResult(Target(server: 101)); });
        Equal(1, targetReads, "older snapshot capability still obtains an official target guard before C");
        Check(!f.Keyboard.Keys.Contains("C") && !f.State.AttackWeave.IsWaiting,
            "older capability target change cancels C");

        f = new Fixture();
        await MixedPair(f);
        f.Clock.Advance(530);
        using var stop = new CancellationTokenSource();
        var cancellationRead = f.Reader.ReadCount + 2;
        f.Reader.AfterRead = count => { if (count == cancellationRead) stop.Cancel(); };
        try { await f.Tick(stop.Token); throw new Exception("cancellation expected"); }
        catch (OperationCanceledException) { }
        Check(!f.Keyboard.Keys.Contains("C") && !f.State.AttackWeave.IsWaiting,
            "cancellation during the action-boundary read clears the scheduled attack before input");
    }

    public static async Task AccountIsolationAndIdleResetAsync()
    {
        var a = new Fixture(delayMs: 0);
        var b = new Fixture();
        b.Reader.Value = Bar(ordinary: true);
        await b.Tick();
        b.Reader.Value = Bar(last: 31, time: 10);
        b.Clock.Advance(80);
        await b.Tick();
        await MixedPair(a);
        Equal(1, a.Keyboard.Keys.Count(key => key == "C"), "zero-delay account completes its own pair");
        Equal(1, b.State.AttackWeave.ConfirmedCount, "another account retains its independent first release");
        Check(!b.Keyboard.Keys.Contains("C"), "another account cannot inherit an attack deadline");
        b.Clock.Advance(1501);
        b.Reader.Value = Bar(counter: true, last: 31, time: 10);
        await b.Tick();
        b.Clock.Advance(80);
        b.Reader.Value = Bar(last: 21, time: 20);
        await b.Tick();
        Equal(1, b.State.AttackWeave.ConfirmedCount, "a 1500 millisecond idle gap starts a fresh successful pair");
        Check(!b.State.AttackWeave.IsWaiting, "a stale single release cannot form a pair with the next fight");

        var waiting = new Fixture(delayMs: 2500);
        await MixedPair(waiting);
        waiting.Clock.Advance(1501);
        await waiting.Tick();
        Equal(2, waiting.State.AttackWeave.ConfirmedCount, "scheduled C survives the single-release idle timeout");
        waiting.Clock.Advance(999);
        await waiting.Tick();
        Equal(1, waiting.Keyboard.Keys.Count(key => key == "C"), "long configured pause still ends at its own deadline");

        var slowRead = new Fixture();
        slowRead.Reader.Value = Bar(ordinary: true);
        await slowRead.Tick();
        slowRead.Clock.Advance(80);
        slowRead.Reader.Value = Bar(counter: true, last: 31, time: 10);
        await slowRead.Tick();
        slowRead.Reader.Value = Bar(last: 21, time: 20);
        var delayedRead = slowRead.Reader.ReadCount + 1;
        slowRead.Reader.AfterRead = count => { if (count == delayedRead) slowRead.Clock.Advance(1501); };
        await slowRead.Tick();
        Equal(0, slowRead.State.AttackWeave.ConfirmedCount,
            "a read that crosses the key-gap deadline cannot combine late evidence with a stale pair");
        Check(!slowRead.Keyboard.Keys.Contains("C"), "slow snapshot reading cannot create a stale delayed attack");
    }

    public static async Task ConfirmationEvidenceAsync()
    {
        var failed = new Fixture();
        failed.Keyboard.FailSkills = true;
        failed.Reader.Value = Bar(ordinary: true);
        await failed.Tick();
        failed.Reader.Value = Bar(last: 31, time: 10);
        await failed.Tick();
        Equal(0, failed.State.AttackWeave.ConfirmedCount, "failed transport does not create a confirmation candidate");

        var f = new Fixture();
        f.Reader.Value = Bar(ordinary: true);
        await f.Tick();
        f.SetCooldown(31, 30000);
        f.Reader.Value = Bar(last: 0, time: 10);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "exact-skill cooldown advancement with a new release clock can confirm a cleared actor id");
        f.Reader.Value = Bar(counter: true, last: 0, time: 10);
        await f.Tick();
        f.SetCooldown(21, 40000);
        f.Reader.Value = Bar(last: 0, time: 20);
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "two valid cooldown fallback confirmations form a pair");

        f = new Fixture();
        f.Reader.Value = Bar(root: true);
        await f.Tick();
        f.SetCooldown(11, 30000);
        f.Reader.Value = Bar(effective: 12);
        await f.Tick(cooldown: skill => skill.SkillId == 11
            ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready);
        Check(f.State.ChainTransition is { Confirmed: false }, "cooldown started can protect a chain before actual release is confirmed");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "cooldown-started handoff with an unchanged actor clock cannot count an unconfirmed release");

        f = new Fixture(delayMs: 0);
        IReadOnlySet<uint> Ordinary(IReadOnlyList<SkillSnapshot> skills) => skills.Select(skill => skill.SkillId).ToHashSet();
        f.Reader.Value = Bar(ordinary: true);
        await f.Tick(ordinary: Ordinary);
        f.Clock.Advance(80);
        f.Reader.Value = Bar(counter: true, last: 31, time: 10);
        await f.Tick(ordinary: Ordinary);
        Equal(1, f.State.AttackWeave.ConfirmedCount, "production-style ordinary readiness retains the first confirmed release");
        f.Clock.Advance(80);
        f.Reader.Value = Bar(root: true, counter: true, last: 0, time: 20);
        var fullRootsRead = false;
        f.BeforeSkillRead = ids =>
        {
            if (ids.Count <= 1 || fullRootsRead) return;
            fullRootsRead = true;
            f.Clock.Advance(200);
            f.SetCooldown(21, 40000);
        };
        var delay = await f.Tick(ordinary: Ordinary);
        Check(fullRootsRead, "production-style selection reads the ordinary roots after the early pending read");
        Sequence(new[] { "NumPadAdd", "D2", "C" }, f.Keyboard.Keys,
            "a confirmation first visible during slow ordinary-root selection gives zero-delay C the same tick");
        Equal(TimeSpan.Zero, delay, "a slow root snapshot read consumes the tick budget without sending an extra skill");
        Check(f.State.PendingAction is null && !f.State.AttackWeave.IsWaiting,
            "late selection confirmation completes the pair before a later ordinary root can own input");
    }

    private static async Task MixedPair(Fixture f)
    {
        f.Reader.Value = Bar(ordinary: true);
        await f.Tick();
        f.Clock.Advance(80);
        f.Reader.Value = Bar(counter: true, last: 31, time: 10);
        await f.Tick();
        f.Clock.Advance(80);
        f.Reader.Value = Bar(last: 21, time: 20);
        await f.Tick();
    }

    private static async Task ChainPair(Fixture f)
    {
        f.Reader.Value = Bar(root: true);
        await f.Tick();
        f.Clock.Advance(80);
        f.Reader.Value = Bar(root: true, effective: 12, last: 11, time: 10);
        await f.Tick();
        f.Clock.Advance(80);
        f.Reader.Value = Bar(root: true, effective: 13, last: 12, time: 20);
        await f.Tick();
    }

    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
        { SkillId = id, Name = "skill" + id, BaseName = "skill" + id, Children = children.ToList() };
    private static SkillSnapshot Skill(uint id, string? chain = null, string? pre = null) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 0,
            XmlChainCategory: chain, XmlPrechainCategory: pre);
    private static readonly SkillSnapshot[] Learned =
        { Skill(11, "a"), Skill(12, "b", "a"), Skill(13, pre: "b"), Skill(21) with { XmlCounterSkill = "Parry" }, Skill(31) };
    private static QuickbarSnapshot Bindings() => new(0, new QuickbarSlotSnapshot[]
        { new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 21), new(SkillQuickbar.Alt, 10, 21, 31) });
    private static LockedTargetSnapshot Target(uint server = 100) =>
        new(50, server, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);
    private static SkillAvailabilityCombatSnapshot Guard(uint targetServer = 100, uint hp = 100) =>
        new(1, 10, 50, targetServer, hp, 100, 100, 100);
    private static SkillAvailabilitySnapshot Bar(bool root = false, bool counter = false, bool ordinary = false,
        uint effective = 11, uint last = 0, uint time = 0) => new(0,
        new SkillAvailabilitySlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, 11, effective, root),
            new(SkillQuickbar.Main, 1, 21, 21, 21, counter),
            new(SkillQuickbar.Alt, 10, 21, 31, 31, ordinary)
        }, last, time, BindingSignature: "main:0:11;main:1:21;alt:10:31",
        BindingSlots: new SkillAvailabilityBindingSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, 11, effective),
            new(SkillQuickbar.Main, 1, 21, 21, 21),
            new(SkillQuickbar.Alt, 10, 21, 31, 31)
        }, CombatState: Guard());

    private sealed class Fixture
    {
        public QuickbarSkillPlan Plan { get; } = QuickbarSkillPlan.FromSettings(new()
            { ExecutionTree = new() { Node(21), Node(11, Node(12, Node(13))), Node(31) } }, new(Bindings(), Learned));
        public Clock Clock { get; } = new();
        public QuickbarSkillCombatState State { get; }
        public Reader Reader { get; } = new();
        public Keyboard Keyboard { get; } = new();
        public List<string> SkillReads { get; } = new();
        public Action<IReadOnlyCollection<uint>>? BeforeSkillRead;
        public IReadOnlyList<SkillSnapshot> Skills = Learned;
        public LockedTargetSnapshot Target = QuickbarAttackWeaveTests.Target();
        public SemiAutoScriptSettings Settings { get; }
        private readonly QuickbarSkillCombatController controller;

        public Fixture(bool enabled = true, int delayMs = 530)
        {
            State = new(Clock);
            Settings = new() { AttackWeaveEnabled = enabled, AttackWeaveDelayMs = delayMs, KeyHoldMs = 1, ConfirmTimeoutMs = 8000 };
            controller = new(Keyboard, Clock);
        }

        public Task<TimeSpan> Tick(CancellationToken cancellationToken = default,
            Func<SkillAvailabilityCombatSnapshot, bool>? allowCombat = null,
            Func<Task<LockedTargetSnapshot>>? readTarget = null,
            Func<SkillSnapshot, SemiAutoSkillCooldownReadiness>? cooldown = null,
            Func<IReadOnlyList<SkillSnapshot>, IReadOnlySet<uint>>? ordinary = null) =>
            controller.TickAsync(Plan, State, Target, Reader, ids =>
            {
                SkillReads.Add(string.Join(",", ids.OrderBy(id => id)));
                BeforeSkillRead?.Invoke(ids);
                return Task.FromResult<IReadOnlyList<SkillSnapshot>>(Skills.Where(skill => ids.Contains(skill.SkillId)).ToArray());
            }, Settings, cancellationToken: cancellationToken, allowCombatSnapshot: allowCombat,
                readTargetBeforePress: readTarget, cooldownReadiness: cooldown, ordinaryReadiness: ordinary);

        public void SetCooldown(uint id, uint endTime) =>
            Skills = Skills.Select(skill => skill.SkillId == id ? skill with { CooldownEndTime = endTime } : skill).ToArray();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = Start;
        private long stamp;
        public override DateTimeOffset GetUtcNow() => now;
        public override long GetTimestamp() => stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void ShiftWall(TimeSpan duration) => now += duration;
        public void Advance(int milliseconds)
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            now += duration;
            stamp += duration.Ticks;
        }
    }

    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Bar();
        public Action<int>? AfterRead;
        public int ReadCount { get; private set; }
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            AfterRead?.Invoke(ReadCount);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(ReadCount, Value));
        }
    }

    private sealed class Keyboard : IKeyboardInput
    {
        public List<string> Keys { get; } = new();
        public bool FailAttack;
        public bool FailSkills;
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return Task.FromResult((key == "C" ? FailAttack : FailSkills) ? OperationResult.Fail("mock transport") : OperationResult.Ok());
        }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) =>
        Check(expected.SequenceEqual(actual), $"{message}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
}
