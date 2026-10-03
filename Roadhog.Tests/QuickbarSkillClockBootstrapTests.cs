using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSkillClockBootstrapTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T14:00:00+08:00");

    public static async Task DarkConditionalFirstAndMissingClockAsync()
    {
        foreach (var lastSkill in new uint[] { 0, 21 })
        {
            var fixture = new Fixture();
            fixture.Reader.Value = fixture.Reader.Value with { LastReleasedSkillId = lastSkill, LastReleasedSkillTime = 0 };
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D3" }),
                "startup skips the preceding dark counter and self-condition skills and tries the exact ordinary root despite a missing actor clock");
            Check(fixture.State.ClockBootstrap.AttemptedCandidateCount == 1 && !fixture.SharedClock.HasCooldownTickCalibration,
                "a bootstrap key is a bounded attempt rather than an invented ready result or completed clock calibration");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(79));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.Count == 1, "bootstrap obeys the existing 80ms retry cadence");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D3", "D3" }) && fixture.Keyboard.Holds.All(hold => hold <= TimeSpan.FromMilliseconds(30)),
                "still-unknown ordinary skill retries with a finite key and never presses the dark condition shortcuts");
        }
    }

    public static Task CandidateBudgetAndRotationAsync()
    {
        var fixture = new Fixture();
        var bootstrap = fixture.State.ClockBootstrap;
        var first = fixture.SelectCandidate()!;
        Check(first.SkillId == 11 && bootstrap.MarkAttemptStarted(first), "first actual attempt starts the finite budget");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1199));
        Check(fixture.SelectCandidate()?.SkillId == 11, "one candidate retains its opportunity until the fixed deadline");
        bootstrap.MarkAttemptStarted(first);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var second = fixture.SelectCandidate()!;
        Check(second.SkillId == 31 && bootstrap.MarkAttemptStarted(second), "retries do not extend the first candidate deadline; the next configured candidate follows");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1200));
        var third = fixture.SelectCandidate()!;
        Check(third.SkillId == 41 && bootstrap.MarkAttemptStarted(third), "third exact configured candidate is the last permitted bootstrap candidate");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1200));
        Check(fixture.SelectCandidate() is null && bootstrap.IsCompleted && bootstrap.AttemptedCandidateCount == 3,
            "three unsuccessful candidate windows finish bootstrap without looping back or trying a fourth candidate");
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Check(fixture.SelectCandidate() is null, "a finished budget stays finished while the target and bindings remain the same");
        return Task.CompletedTask;
    }

    public static Task MonotonicTotalBudgetAsync()
    {
        var fixture = new Fixture();
        var bootstrap = fixture.State.ClockBootstrap;
        fixture.SelectCandidate();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Check(!bootstrap.IsCompleted && fixture.SelectCandidate()?.SkillId == 11,
            "selection without an actual key does not consume the time or candidate budget");
        bootstrap.MarkAttemptStarted(fixture.SelectCandidate()!);
        fixture.Clock.JumpWallClock(TimeSpan.FromDays(4));
        Check(fixture.SelectCandidate()?.SkillId == 11, "a UTC jump cannot expire the monotonic candidate budget");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(3999));
        var second = fixture.SelectCandidate()!;
        Check(second.SkillId == 31 && bootstrap.MarkAttemptStarted(second), "a delayed next attempt still uses the original total budget");
        fixture.Clock.JumpWallClock(TimeSpan.FromDays(-8));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Check(fixture.SelectCandidate() is null && bootstrap.IsCompleted && bootstrap.AttemptedCandidateCount == 2,
            "the absolute four-second budget wins over a newly started candidate and backward UTC jump");
        return Task.CompletedTask;
    }

    public static Task ReadyAndCoolingPriorityAsync()
    {
        var fixture = new Fixture();
        fixture.Readiness = skill => skill.SkillId == 31 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        Check(fixture.SelectCandidate()?.SkillId == 31, "an actually ready ordinary skill is preferred over a preceding unknown-CD candidate");
        fixture.Readiness = skill => skill.SkillId is 11 or 31 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Unknown;
        Check(fixture.SelectCandidate()?.SkillId == 41, "known cooling candidates are skipped even while the clock is uncalibrated");
        fixture.Readiness = _ => SemiAutoSkillCooldownReadiness.CoolingDown;
        Check(fixture.SelectCandidate() is null && fixture.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "a bar containing only explicitly cooling ordinary attacks sends no speculative attempt");
        return Task.CompletedTask;
    }

    public static Task ReadinessCapabilityAndProposalHandoffAsync()
    {
        var fixture = new Fixture();
        Check(fixture.State.ClockBootstrap.SelectCandidate(fixture.Plan, fixture.Reader.Value, fixture.Skills.Values.ToArray(),
            _ => null, false) is null, "an absent readiness capability does not authorize even a finite startup key");
        var first = fixture.SelectCandidate()!;
        fixture.State.ClockBootstrap.MarkAttemptStarted(first);
        fixture.Readiness = skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        Check(fixture.SelectCandidate()?.SkillId == 11 && fixture.State.ClockBootstrap.MarkAttemptStarted(first),
            "a candidate proven ready after its first attempt may retain its existing attempt without opening a new budget");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1200));
        Check(fixture.State.ClockBootstrap.IsAttemptExpired(first), "new Ready evidence does not renew the candidate's finite retry window");

        fixture = new Fixture();
        first = fixture.SelectCandidate()!;
        fixture.State.ClockBootstrap.MarkAttemptStarted(first);
        fixture.Readiness = skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Unknown;
        var next = fixture.SelectCandidate()!;
        fixture.State.ClockBootstrap.FinishCandidate(first, "old_pending_closed");
        Check(next.SkillId == 31 && fixture.State.ClockBootstrap.MarkAttemptStarted(next),
            "closing an old pending action cannot erase a new candidate proposal before the actual key boundary");
        return Task.CompletedTask;
    }

    public static Task ExactBindingAndConfiguredScopeAsync()
    {
        var changes = new Func<SkillAvailabilitySnapshot, SkillAvailabilitySnapshot>[]
        {
            bar => bar with { Page = 1 },
            bar => bar with { UnsupportedSkillIds = Array.Empty<uint>() },
            bar => bar with { BindingSlots = null },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11 ? slot with { EffectiveSkillId = 111 } : slot).ToArray() },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11 ? slot with { BaseSkillId = 111 } : slot).ToArray() },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11 ? slot with { ContentType = 1 } : slot).ToArray() },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11 ? slot with { Slot = 8 } : slot).ToArray() },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11 ? slot with { Bar = SkillQuickbar.Alt } : slot).ToArray() }
        };
        foreach (var change in changes)
        {
            var fixture = new Fixture(21, 22, 11);
            fixture.Reader.Value = change(fixture.Reader.Value);
            Check(fixture.SelectCandidate() is null,
                "bootstrap requires the exact configured rank, page, main/Alt slot, skill content and unsupported-signal identity");
        }
        var missing = new Fixture(21, 22, 11);
        missing.Skills.Remove(11);
        missing.Skills[111] = Attack(111) with { Name = "skill11", DisplayBaseName = "skill11", HighestLevel = 5 };
        Check(missing.SelectCandidate() is null, "missing exact learned rank cannot be replaced with a same-name higher rank or unconfigured attack");
        return Task.CompletedTask;
    }

    public static Task HostileAttackEligibilityAsync()
    {
        var attack = Attack(11);
        Check(QuickbarSkillClockBootstrap.IsEligibleOrdinaryAttack(attack), "an active learned hostile attack with a real CD is eligible");
        Check(QuickbarSkillClockBootstrap.IsEligibleOrdinaryAttack(attack with { XmlSubType = "Debuff", XmlEffects = "SkillATK_Instant,Slow" }),
            "a damaging debuff root remains eligible, matching the actual resonance-chain starter XML");
        foreach (var unsuitable in new[]
        {
            attack with { HighestLevel = 0 }, attack with { CooldownDuration = 0 }, attack with { IsToggle = true },
            attack with { XmlActivation = "Passive" }, attack with { XmlActivation = null },
            attack with { XmlTargetRelationRestriction = "Friend" }, attack with { XmlTargetRelationRestriction = null },
            attack with { XmlCounterSkill = "Parry" }, attack with { XmlPrechainCategory = "first" },
            attack with { XmlTargetValidStatuses = "Stun" }, attack with { XmlSelfConditionStatuses = "Dodge" },
            attack with { XmlUltraTransfer = "1" }, attack with { XmlCostDp = "1000" },
            attack with { XmlCostDp = "unavailable" }, attack with { XmlTags = "Attack,dp" },
            attack with { XmlSkillType = null }, attack with { XmlSkillCategory = "Chant" },
            attack with { XmlSubType = "Heal", XmlEffects = "Heal_Instant" },
            attack with { XmlSubType = "Buff", XmlEffects = "StatUp" },
            attack with { XmlSubType = null, XmlEffects = null }
        })
            Check(!QuickbarSkillClockBootstrap.IsEligibleOrdinaryAttack(unsuitable),
                "passive, missing, zero-CD, conditional, resource-special, friendly and non-damaging skills cannot be guessed usable for clock bootstrap");
        return Task.CompletedTask;
    }

    public static async Task CalibrationRestoresChainPriorityAsync()
    {
        var fixture = new Fixture();
        await fixture.Tick();
        fixture.Skills[11] = fixture.Skills[11] with { CooldownEndTime = 1_020_000 };
        fixture.Reader.Value = fixture.Reader.Value with
        {
            LastReleasedSkillId = 11, LastReleasedSkillTime = 990_000,
            Slots = new SkillAvailabilitySlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 21, 21, true),
                new(SkillQuickbar.Main, 2, 21, 11, 12, true)
            },
            BindingSlots = fixture.Reader.Value.BindingSlots!.Select(slot => slot.BaseSkillId == 11
                ? slot with { EffectiveSkillId = 12 } : slot).ToArray()
        };
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
        await fixture.Tick();
        Check(fixture.SharedClock.HasCooldownTickCalibration && fixture.State.ClockBootstrap.IsCompleted,
            "the exact observed CD advancing after a key calibrates the existing clock and closes bootstrap");
        Check(fixture.State.ActiveChainSource?.SkillId == 11 && fixture.State.PendingAction?.Node.SkillId == 12,
            "after calibration a lit configured continuation immediately outranks the earlier lit counter root");
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D3", "D3" }), "child uses its inherited exact source slot");
    }

    public static async Task ReadyCandidateKeepsNormalConfirmationAsync()
    {
        var fixture = new Fixture();
        fixture.Readiness = skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        await fixture.Tick();
        Check(fixture.State.PendingAction is { IsClockBootstrap: false } && fixture.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "a known-ready ordinary root enters the normal release lifecycle without spending speculative bootstrap budget");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1200));
        await fixture.Tick();
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D3", "D3", "D3" }) &&
              fixture.State.PendingAction is { IsClockBootstrap: false, AttemptCount: 3 },
            "the unknown-CD candidate timeout cannot prematurely rotate a known-ready attack whose animation is still waiting for normal confirmation");
    }

    public static async Task ControllerExhaustionDoesNotLoopAsync()
    {
        var fixture = new Fixture();
        for (var poll = 0; poll <= 50; poll++)
        {
            if (poll > 0) fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
            await fixture.Tick();
        }
        Check(fixture.State.ClockBootstrap.IsCompleted && fixture.State.ClockBootstrap.AttemptedCandidateCount == 3,
            "real controller exhausts three finite bootstrap candidates instead of retaining the old eight-second pending timeout");
        Check(fixture.Keyboard.Keys.Count > 0 && fixture.Keyboard.Keys.Count <= 45 &&
              fixture.Keyboard.Keys.All(key => key is "D3" or "D4" or "D5"),
            "one finite key per 80ms poll stays bounded and never presses a fourth candidate or a dark conditional skill");
        var count = fixture.Keyboard.Keys.Count;
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == count, "finished bootstrap cannot restart indefinitely without a lifecycle reset");
        fixture.Readiness = skill => skill.SkillId == 11 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.Unknown;
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.Count == count + 1 && fixture.Keyboard.Keys[^1] == "D3" &&
              fixture.State.PendingAction is { IsClockBootstrap: false },
            "budget exhaustion stops speculative startup retries while a subsequently proven-ready root still uses ordinary combat normally");
    }

    public static async Task BoundaryRecheckAsync()
    {
        var empty = new Fixture(21, 22, 11);
        var root = empty.Skills[11];
        empty.Skills.Remove(11);
        await empty.Tick();
        Check(empty.Keyboard.Keys.Count == 0 && !empty.State.ClockBootstrap.IsCompleted,
            "an initially empty official exact-skill read neither sends a key nor spends the startup budget");
        empty.Skills[11] = root;
        empty.Clock.Advance(TimeSpan.FromMilliseconds(80));
        await empty.Tick();
        Check(empty.Keyboard.Keys.SequenceEqual(new[] { "D3" }), "a subsequent valid exact snapshot still starts the one finite bootstrap opportunity");

        var cooling = new Fixture(21, 22, 11);
        cooling.Reader.OnRead = count =>
        {
            if (count >= 2) cooling.Readiness = _ => SemiAutoSkillCooldownReadiness.CoolingDown;
        };
        await cooling.Tick();
        Check(cooling.Keyboard.Keys.Count == 0 && cooling.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "known cooling discovered at the actual press boundary cancels the speculative candidate without charging a key attempt");

        var binding = new Fixture(21, 22, 11);
        binding.Reader.OnRead = count =>
        {
            if (count == 2)
                binding.Reader.Value = binding.Reader.Value with { BindingSlots = binding.Reader.Value.BindingSlots!
                    .Select(slot => slot.BaseSkillId == 11 ? slot with { BaseSkillId = 111, EffectiveSkillId = 111 } : slot).ToArray() };
        };
        await binding.Tick();
        Check(binding.Keyboard.Keys.Count == 0 && binding.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "a changed exact slot between selection and press yields before any key or budget starts");
    }

    public static async Task MaintenancePreservesAbsoluteBudgetAsync()
    {
        var fixture = new Fixture();
        await fixture.Tick();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
        await fixture.Tick(allowCombat: _ => false);
        Check(fixture.State.PendingAction is null && fixture.State.ClockBootstrap.AttemptedCandidateCount == 1,
            "maintenance suspends attack retries while retaining the already-started bootstrap budget");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1100));
        await fixture.Tick();
        Check(fixture.Keyboard.Keys.SequenceEqual(new[] { "D3", "D4" }) && fixture.State.ClockBootstrap.AttemptedCandidateCount == 2,
            "maintenance time consumes the original candidate window; resuming cannot restart the failed first candidate");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(2800));
        await fixture.Tick();
        Check(fixture.State.ClockBootstrap.IsCompleted && fixture.Keyboard.Keys.Count == 2,
            "the original total budget expires across maintenance rather than receiving a new four seconds");
    }

    public static async Task ScopeAndCancellationResetAsync()
    {
        foreach (var change in new Func<SkillAvailabilitySnapshot, SkillAvailabilitySnapshot>[]
        {
            bar => bar with { CombatState = bar.CombatState! with { CurrentHp = 0 } },
            bar => bar with { CombatState = bar.CombatState! with { TargetServerObjectId = 999 } },
            bar => bar with { Page = 1 },
            bar => bar with { BindingSlots = bar.BindingSlots!.Select(slot => slot.BaseSkillId == 11
                ? slot with { BaseSkillId = 111, EffectiveSkillId = 111 } : slot).ToArray() }
        })
        {
            var fixture = new Fixture();
            await fixture.Tick();
            fixture.Reader.Value = change(fixture.Reader.Value);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(80));
            await fixture.Tick();
            Check(fixture.Keyboard.Keys.Count == 1 && fixture.State.PendingAction is null &&
                  fixture.State.ClockBootstrap.AttemptedCandidateCount == 0,
                "death, changed target, page and binding transitions clear the bootstrap lifecycle before another key");
        }
        var cancelled = new Fixture();
        await cancelled.Tick();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        try { await cancelled.Tick(cancellationToken: stop.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Check(cancelled.State.PendingAction is null && cancelled.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "cancellation resets bootstrap without leaving a pending attack");
        var stopped = new Fixture();
        await stopped.Tick();
        stopped.State.Reset();
        Check(stopped.State.ClockBootstrap.AttemptedCandidateCount == 0 && !stopped.State.ClockBootstrap.IsCompleted,
            "explicit stop clears all finite bootstrap budget state for a new session");
    }

    public static async Task OptionalCapabilityAndLegacyIsolationAsync()
    {
        var fixture = new Fixture();
        await fixture.Tick(enableBootstrap: false);
        Check(fixture.Keyboard.Keys.Count == 0 && fixture.State.ClockBootstrap.AttemptedCandidateCount == 0,
            "the existing narrow controller seam without the clock capability retains its old Unknown-CD semantics");

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var api = new FakeGameApi();
        var logger = new InMemoryRoadhogLogger();
        var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy };
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.QuickbarSkills.ExecutionTree.Add(Node(11));
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "clock-bootstrap-legacy", ScriptSettings = settings },
            api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var keyboard = new Keyboard();
        var state = new SemiAutoCombatState();
        await new SemiAutoCombatController(keyboard).TickAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), state);
        Check(api.SkillAvailabilityReadCount == 0 && keyboard.Keys.Count == 0 && state.QuickbarSkills.ClockBootstrap.AttemptedCandidateCount == 0,
            "legacy mode never reads quickbar opportunities or starts the new configured ordinary clock bootstrap");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
        { SkillId = id, Name = "skill" + id, BaseName = "skill" + id, Children = children.ToList() };

    private static SkillSnapshot Attack(uint id, string? chain = null, string? pre = null) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 500000,
            XmlActivation: "Active", XmlSkillType: "Physical", XmlSubType: "Attack", XmlFirstTarget: "Target",
            XmlTargetRelationRestriction: "Enemy", XmlEffects: "SkillATK_Instant", XmlChainCategory: chain, XmlPrechainCategory: pre);

    private static LockedTargetSnapshot Target() =>
        new(50, 100, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);

    private sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly QuickbarSkillCombatState State;
        public readonly SemiAutoCombatState SharedClock = new();
        public readonly Reader Reader = new();
        public readonly Keyboard Keyboard = new();
        public readonly Dictionary<uint, SkillSnapshot> Skills = new()
        {
            [21] = Attack(21) with { XmlCounterSkill = "Parry" },
            [22] = Attack(22) with { XmlSelfConditionStatuses = "Dodge" },
            [11] = Attack(11, chain: "a"), [12] = Attack(12, pre: "a") with { CooldownEndTime = 0 },
            [31] = Attack(31), [41] = Attack(41), [51] = Attack(51), [61] = Attack(61)
        };
        public readonly QuickbarSkillPlan Plan;
        public Func<SkillSnapshot, SemiAutoSkillCooldownReadiness>? Readiness;
        private readonly QuickbarSkillCombatController _controller;
        private const uint ForeignOsTick = 2_000_000_000;

        public Fixture(params uint[] roots)
        {
            State = new(Clock);
            var boundIds = new uint[] { 21, 22, 11, 31, 41, 51, 61 };
            var bindings = new QuickbarSnapshot(0, boundIds.Select((id, slot) => new QuickbarSlotSnapshot(SkillQuickbar.Main, slot, 21, id)).ToArray());
            Plan = QuickbarSkillPlan.FromSettings(new()
            {
                ExecutionTree = (roots.Length == 0 ? new uint[] { 21, 22, 11, 31, 41, 51 } : roots)
                    .Select(id => id == 11 ? Node(11, Node(12)) : Node(id)).ToList()
            }, new(bindings, Skills.Values.ToArray()));
            Reader.Value = new(0,
                new SkillAvailabilitySlotSnapshot[]
                {
                    new(SkillQuickbar.Main, 0, 21, 21, 21, false),
                    new(SkillQuickbar.Main, 1, 21, 22, 22, false)
                }, UnsupportedSkillIds: new uint[] { 11, 31, 41, 51, 61 },
                BindingSlots: bindings.Slots.Select(slot => new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, slot.ContentType, slot.SkillId, slot.SkillId)).ToArray(),
                CombatState: new(1, 10, 50, 100, 100, 100, 100, 100));
            _controller = new(Keyboard, Clock);
        }

        public QuickbarSkillNode? SelectCandidate() => State.ClockBootstrap.SelectCandidate(Plan, Reader.Value, Skills.Values.ToArray(),
            skill => Readiness?.Invoke(skill) ?? SemiAutoSkillCooldownReadiness.Unknown, SharedClock.HasCooldownTickCalibration);

        public Task<TimeSpan> Tick(bool enableBootstrap = true, Func<SkillAvailabilityCombatSnapshot, bool>? allowCombat = null,
            CancellationToken cancellationToken = default) => _controller.TickAsync(Plan, State, Target(), Reader, ReadSkills,
            new() { ConfirmTimeoutMs = 500, KeyHoldMs = 25 }, cancellationToken: cancellationToken,
            ordinaryReadiness: skills => skills.Select(skill => skill.SkillId).ToHashSet(),
            allowCombatSnapshot: allowCombat,
            availabilityCooldownReadiness: (skill, _) => Readiness?.Invoke(skill) ??
                (skill.CooldownEndTime == 0 || skill.CooldownDuration == 0 ? SemiAutoSkillCooldownReadiness.Ready :
                 SharedClock.HasCooldownTickCalibration
                    ? (unchecked((int)(skill.CooldownEndTime - SharedClock.EstimateGameTick(ForeignOsTick))) <= 0
                        ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.CoolingDown)
                    : SemiAutoSkillCooldownReadiness.Unknown),
            isCooldownClockCalibrated: enableBootstrap ? () => SharedClock.HasCooldownTickCalibration : null);

        private Task<IReadOnlyList<SkillSnapshot>> ReadSkills(IReadOnlyCollection<uint> ids)
        {
            IReadOnlyList<SkillSnapshot> skills = ids.Where(Skills.ContainsKey).Select(id => Skills[id]).ToArray();
            SharedClock.TryUpdateCooldownTickCalibration(skills, ForeignOsTick, Clock.Now, out _);
            return Task.FromResult(skills);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Start;
        private long _stamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => _stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { Now += duration; _stamp += duration.Ticks; }
        public void JumpWallClock(TimeSpan duration) => Now += duration;
    }

    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = null!;
        public int ReadCount;
        public Action<int>? OnRead;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ++ReadCount;
            OnRead?.Invoke(ReadCount);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(ReadCount, Value));
        }
    }

    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<string> Keys = new();
        public readonly List<TimeSpan> Holds = new();
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key); Holds.Add(holdDuration);
            return Task.FromResult(OperationResult.Ok());
        }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
