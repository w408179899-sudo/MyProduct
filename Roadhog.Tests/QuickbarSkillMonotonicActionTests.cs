using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSkillMonotonicActionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-05T11:00:00+08:00");

    public static async Task RetryCadenceSurvivesWallClockJumpsAsync()
    {
        foreach (var jump in new[] { TimeSpan.FromHours(-1), TimeSpan.FromHours(1) })
        {
            var f = new Fixture();
            await f.Tick();
            var first = f.State.PendingAction!;
            f.Clock.JumpWallClock(jump);
            f.Clock.Advance(TimeSpan.FromMilliseconds(79));
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 1 && f.State.PendingAction?.AttemptCount == 1,
                "a wall-clock change neither stalls nor accelerates the real79ms retry interval");
            f.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await f.Tick();
            var retry = f.State.PendingAction!;
            Check(f.Keyboard.Keys.SequenceEqual(new[] { "D1", "D1" }) && retry.AttemptCount == 2 &&
                retry.AttemptId == first.AttemptId && retry.StartedAt == first.StartedAt && retry.Deadline == first.Deadline &&
                retry.PreviousCooldownEndTime == first.PreviousCooldownEndTime &&
                retry.PreviousReleasedSkillTime == first.PreviousReleasedSkillTime,
                "the actual80ms retry preserves the first action baseline and public diagnostic times");
        }
    }

    public static async Task DeadlineSurvivesWallClockJumpsAsync()
    {
        foreach (var jump in new[] { TimeSpan.FromDays(-3), TimeSpan.FromDays(3) })
        {
            var f = new Fixture();
            await f.Tick();
            var deadline = f.State.PendingAction!.Deadline;
            f.Clock.JumpWallClock(jump);
            f.Clock.Advance(TimeSpan.FromMilliseconds(7999));
            await f.Tick();
            Check(f.State.PendingAction is { AttemptCount: 2 } && f.State.PendingAction.Deadline == deadline,
                "UTC adjustment cannot reject the action before its real eight-second acceptance window");
            f.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await f.Tick();
            Check(f.State.PendingAction is null && f.Keyboard.Keys.Count == 2,
                "backward UTC adjustment cannot extend an expired eight-second action");
            f.Clock.Advance(TimeSpan.FromMilliseconds(499));
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 2, "unconfirmed-action suppression retains its500ms elapsed boundary");
            f.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 3 && f.State.PendingAction?.AttemptCount == 1,
                "a fresh action starts after the finite rejection hold instead of waiting for UTC to catch up");
        }
    }

    public static async Task FailedInputHoldSurvivesWallClockJumpsAsync()
    {
        foreach (var jump in new[] { TimeSpan.FromHours(-2), TimeSpan.FromHours(2) })
        {
            var f = new Fixture();
            f.Keyboard.Fail = true;
            await f.Tick();
            Check(f.State.PendingAction is null, "failed input is rejected immediately");
            f.Keyboard.Fail = false;
            f.Clock.JumpWallClock(jump);
            f.Clock.Advance(TimeSpan.FromMilliseconds(999));
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 1, "failed input retains its real one-second retry hold");
            f.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 2 && f.State.PendingAction?.AttemptCount == 1,
                "input retry resumes at one real second regardless of UTC adjustment");
        }
    }

    public static async Task MaximumAttemptsStayFiniteAsync()
    {
        var f = new Fixture();
        f.Settings.ConfirmTimeoutMs = 30000;
        for (var count = 0; count < QuickbarSkillCombatState.MaximumUnconfirmedAttempts; count++)
        {
            if (count > 0) f.Clock.Advance(QuickbarSkillCombatState.RetryInterval);
            f.Clock.JumpWallClock(TimeSpan.FromHours(count % 2 == 0 ? -1 : 1));
            await f.Tick();
        }
        Check(f.Keyboard.Keys.Count == 100 && f.State.PendingAction?.AttemptCount == 100,
            "the existing hundred-attempt budget remains exact despite repeated UTC adjustments");
        f.Clock.Advance(QuickbarSkillCombatState.RetryInterval);
        await f.Tick();
        Check(f.Keyboard.Keys.Count == 100 && f.State.PendingAction is null,
            "attempt exhaustion rejects the action even when its configured acceptance window is longer");
    }

    public static async Task PollCycleAnchorSurvivesMidReadClockJumpAsync()
    {
        var f = new Fixture();
        f.Reader.OnRead = count =>
        {
            if (count != 1) return;
            f.Clock.JumpWallClock(TimeSpan.FromHours(-1));
            f.Clock.Advance(TimeSpan.FromMilliseconds(20));
        };
        var pressedAt = new List<long>();
        f.Keyboard.AfterPress = hold => { pressedAt.Add(f.Clock.Stamp); f.Clock.Advance(hold); };
        var delay = await f.Tick();
        Check(delay == TimeSpan.FromMilliseconds(35), "read and finite key time still leave35ms of the80ms poll");
        f.Clock.Advance(delay);
        await f.Tick();
        Check(f.Keyboard.Keys.Count == 2 && pressedAt[1] - pressedAt[0] == TimeSpan.FromMilliseconds(60).Ticks,
            "monotonic retry scheduling retains the poll-cycle anchor across read latency and a UTC change");
    }

    public static Task PureApiCompatibilityAndLifecycleResetAsync()
    {
        var f = new Fixture();
        var node = f.Plan.Roots[0];
        var state = new QuickbarSkillCombatState();
        state.BeginAction(node, Skill(11), Bar(), Start, TimeSpan.FromSeconds(8));
        Check(state.IsRepeatBlocked(node, Start.AddMilliseconds(79)) && !state.IsRepeatBlocked(node, Start.AddMilliseconds(80)) &&
            !state.IsActionExpired(Start.AddMilliseconds(7999)) && state.IsActionExpired(Start.AddSeconds(8)),
            "pure callers without a TimeProvider retain the established explicit UTC contract");
        state.RejectAction(Start, TimeSpan.FromSeconds(1));
        Check(state.IsRepeatBlocked(node, Start.AddMilliseconds(999)) && !state.IsRepeatBlocked(node, Start.AddSeconds(1)),
            "pure rejection hold continues to use the caller-supplied time");

        foreach (var clear in new Action<QuickbarSkillCombatState>[]
        {
            value => value.Reset(),
            value => value.SuspendInputAttempts(),
            value => { value.ObserveScope(Target(), Bar()); value.ObserveScope(Target(server: 101), Bar()); }
        })
        {
            state.BeginAction(node, Skill(11), Bar(), Start, TimeSpan.FromSeconds(8), timeProvider: f.Clock);
            state.RejectAction(Start, TimeSpan.FromSeconds(1));
            clear(state);
            Check(!state.IsRepeatBlocked(node, Start), "lifecycle reset clears monotonic rejection state");
        }
        state.BeginAction(node, Skill(11), Bar(), Start, TimeSpan.FromSeconds(8), timeProvider: f.Clock);
        state.RejectAction(Start, TimeSpan.FromSeconds(1));
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        state.BeginAction(node, Skill(11), Bar(), Start, TimeSpan.FromSeconds(8));
        state.RejectAction(Start, TimeSpan.FromSeconds(1));
        Check(state.IsRepeatBlocked(node, Start.AddMilliseconds(999)) && !state.IsRepeatBlocked(node, Start.AddSeconds(1)),
            "a later pure action replaces the same node's old monotonic hold with its own UTC contract");
        return Task.CompletedTask;
    }

    public static async Task ChainBootstrapAndWeaveKeepMonotonicBoundariesAsync()
    {
        var chain = new Fixture();
        await chain.Tick();
        chain.Reader.Value = chain.Reader.Value with { LastReleasedSkillId = 11, LastReleasedSkillTime = 1100 };
        chain.Clock.JumpWallClock(TimeSpan.FromDays(2));
        chain.Clock.Advance(TimeSpan.FromMilliseconds(80));
        await chain.Tick();
        Check(chain.State.ChainTransition is not null && chain.Keyboard.Keys.Count == 1,
            "a real root release still starts its configured handoff under adjusted UTC");
        chain.Clock.JumpWallClock(TimeSpan.FromDays(-4));
        chain.Clock.Advance(TimeSpan.FromMilliseconds(1499));
        await chain.Tick();
        Check(chain.Keyboard.Keys.Count == 1, "the chain handoff retains its1499ms protected gap");
        chain.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await chain.Tick();
        Check(chain.Keyboard.Keys.Count == 2 && chain.State.ChainTransition is null,
            "the bounded1500ms chain handoff returns to normal root execution");

        var bootstrap = new Fixture();
        bootstrap.Reader.Value = Bar(ordinary: true);
        bootstrap.UnknownCooldown = true;
        await bootstrap.Tick(bootstrap: true);
        Check(bootstrap.State.PendingAction is { IsClockBootstrap: true }, "unknown ordinary cooldown starts one finite trial");
        bootstrap.Clock.JumpWallClock(TimeSpan.FromDays(-2));
        bootstrap.Clock.Advance(TimeSpan.FromMilliseconds(1199));
        await bootstrap.Tick(bootstrap: true);
        Check(bootstrap.State.PendingAction is { IsClockBootstrap: true }, "trial remains active before1200 real milliseconds");
        bootstrap.Clock.JumpWallClock(TimeSpan.FromDays(4));
        bootstrap.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await bootstrap.Tick(bootstrap: true);
        Check(bootstrap.State.PendingAction is null && bootstrap.State.ClockBootstrap.IsCompleted && bootstrap.Keyboard.Keys.Count == 2,
            "clock bootstrap still exhausts the single candidate at its monotonic1200ms boundary");

        var clock = new Clock();
        var weave = new QuickbarAttackWeaveState();
        weave.TrackMainPress(Skill(11), true, clock, TimeSpan.FromSeconds(8));
        weave.TrackMainPress(Skill(12), true, clock, TimeSpan.FromSeconds(8));
        weave.ObserveCooldowns(new[] { Skill(11) with { CooldownEndTime = 2000 }, Skill(12) with { CooldownEndTime = 2000 } }, clock, 600);
        clock.JumpWallClock(TimeSpan.FromDays(-3));
        clock.Advance(TimeSpan.FromMilliseconds(599));
        Check(weave.IsWaiting && !weave.ShouldPressAttack(clock), "weave retains its600ms elapsed wait");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Check(weave.ShouldPressAttack(clock), "weave C becomes due at the unchanged real boundary");
        weave.MarkAttackSucceeded(clock);
        clock.JumpWallClock(TimeSpan.FromDays(6));
        clock.Advance(TimeSpan.FromMilliseconds(29));
        Check(weave.IsWaiting, "post-C pause still protects29ms");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Check(!weave.IsWaiting, "post-C pause ends at30 real milliseconds");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) =>
        new() { SkillId = id, Name = "skill" + id, Children = children.ToList() };

    private static SkillSnapshot Skill(uint id) => new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 0,
        XmlActivation: "Active", XmlSkillType: "Physical", XmlTargetRelationRestriction: "Enemy", XmlSubType: "Attack");

    private static LockedTargetSnapshot Target(uint server = 100) =>
        new(50, server, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);

    private static SkillAvailabilitySnapshot Bar(bool ordinary = false) => new(0,
        ordinary ? Array.Empty<SkillAvailabilitySlotSnapshot>() : new[]
        {
            new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 0, 21, 11, 11, true),
            new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 1, 21, 12, 12, false)
        }, 31, 1000, UnsupportedSkillIds: ordinary ? new uint[] { 11 } : null,
        BindingSlots: new SkillAvailabilityBindingSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, 11, 11), new(SkillQuickbar.Main, 1, 21, 12, 12)
        }, CombatState: new(1, 10, 50, 100, 100, 100, 100, 100));

    private sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly QuickbarSkillPlan Plan;
        public readonly QuickbarSkillCombatState State;
        public readonly Reader Reader = new();
        public readonly Keyboard Keyboard = new();
        public readonly SemiAutoScriptSettings Settings = new() { KeyHoldMs = 25, PostPressSuppressMs = 500 };
        public bool UnknownCooldown;
        private readonly QuickbarSkillCombatController controller;

        public Fixture()
        {
            var bindings = new QuickbarSnapshot(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 12)
            });
            Plan = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(11, Node(12)) } },
                new(bindings, new[] { Skill(11), Skill(12) }));
            State = new(Clock);
            controller = new(Keyboard, Clock);
        }

        public Task<TimeSpan> Tick(bool bootstrap = false) => controller.TickAsync(Plan, State, Target(), Reader,
            ids => Task.FromResult<IReadOnlyList<SkillSnapshot>>(ids.Select(Skill).ToArray()), Settings,
            ordinaryReadiness: bootstrap ? skills => skills.Select(skill => skill.SkillId).ToHashSet() : null,
            cooldownReadiness: _ => UnknownCooldown ? SemiAutoSkillCooldownReadiness.Unknown : SemiAutoSkillCooldownReadiness.Ready,
            isCooldownClockCalibrated: bootstrap ? () => false : null);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Start;
        public long Stamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { Now += duration; Stamp += duration.Ticks; }
        public void JumpWallClock(TimeSpan duration) => Now += duration;
    }

    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Bar();
        public Action<int>? OnRead;
        private int reads;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads++;
            OnRead?.Invoke(reads);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(reads, Value));
        }
    }

    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<string> Keys = new();
        public Action<TimeSpan>? AfterPress;
        public bool Fail;
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Keys.Add(key); AfterPress?.Invoke(holdDuration); return Task.FromResult(Fail ? OperationResult.Fail("mock") : OperationResult.Ok()); }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
