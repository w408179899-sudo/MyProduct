using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSkillFinalGuardTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-05T10:00:00+08:00");

    public static async Task AvailabilityWaitYieldsAndResumesAsync()
    {
        foreach (var blockedRead in new[] { 1, 2, 3 })
        {
            var f = new Fixture();
            f.Reader.BlockAt = blockedRead;
            var started = System.Diagnostics.Stopwatch.StartNew();
            await f.Tick().WaitAsync(TimeSpan.FromSeconds(8));
            Check(started.Elapsed >= TimeSpan.FromSeconds(1.8) && started.Elapsed < TimeSpan.FromSeconds(7),
                "availability wait expires after two seconds");
            Check(f.State.YieldToWorker && !f.State.TickInFlight && f.Reader.ActiveReads == 0 &&
                f.Reader.CancelledReads == 1 && f.Keyboard.Keys.Count == 0,
                "initial/final guards cancel and drain the read, yield to life guard, and send no input");
            f.Reader.BlockAt = 0;
            f.Reader.Value = f.Reader.Value with { CombatState = Guard(hp: 0) };
            await f.Tick();
            Check(f.State.YieldToWorker && f.Keyboard.Keys.Count == 0,
                "official death after timeout keeps combat stopped");
            f.Reader.Value = Bar(true);
            f.Reader.OnRead = null;
            await f.Tick();
            Check(f.Keyboard.Keys.Count == 1 && !f.State.TickInFlight,
                "valid alive data resumes normal skill execution after timeout/death");
        }
    }

    public static async Task AvailabilityWaitPreservesStopAsync()
    {
        var f = new Fixture();
        f.Reader.BlockAt = 1;
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await f.Tick(token: stop.Token);
            throw new Exception("worker stop must propagate");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Check(f.Reader.ActiveReads == 0 && f.Reader.CancelledReads == 1 && !f.State.TickInFlight &&
            f.Keyboard.Keys.Count == 0, "explicit stop drains the reader and still propagates cancellation");
    }

    public static async Task PostGuardChildReadStopsTargetChangeAsync()
    {
        foreach (var sameEntity in new[] { false, true })
        {
            var f = new Fixture();
            f.AfterTransitionRead = () => f.Reader.Value = f.Reader.Value with
            {
                CombatState = sameEntity ? Guard(server: 101) : Guard(entity: 51)
            };
            await f.Tick();
            Check(f.SkillReadCount == 3 && f.Reader.ReadCount == 3 && f.Keyboard.Keys.Count == 0 &&
                f.State.YieldToWorker && f.State.PendingAction is null,
                "a target changed during the newly confirmed parent's child read yields before pressing that same child");
        }
    }

    public static async Task PostGuardChildReadStopsDeathAsync()
    {
        var f = new Fixture();
        f.AfterTransitionRead = () => f.Reader.Value = f.Reader.Value with { CombatState = Guard(hp: 0) };
        await f.Tick();
        Check(f.Keyboard.Keys.Count == 0 && f.State.YieldToWorker && f.State.PendingAction is null,
            "player death during the post-guard child read clears the attempt and sends no key");
    }

    public static async Task PostGuardChildReadYieldsToMaintenanceAsync()
    {
        var f = new Fixture();
        f.AfterTransitionRead = () => f.MaintenanceDue = true;
        await f.Tick();
        Check(f.Keyboard.Keys.Count == 0 && f.State.YieldToWorker && f.State.PendingAction is null &&
            f.State.ActiveChainSource?.SkillId == 11,
            "new maintenance after child preparation owns input while retaining the confirmed predecessor");
    }

    public static async Task FallbackPostGuardChildReadStopsTargetChangeAsync()
    {
        foreach (var sameEntity in new[] { false, true })
        {
            var f = new Fixture(combined: false);
            f.AfterTransitionRead = () => f.CurrentTarget = sameEntity ? Target(server: 101) : Target(entity: 51);
            await f.Tick();
            Check(f.SkillReadCount == 3 && f.TargetReadCount == 2 && f.Reader.ReadCount == 2 &&
                f.Keyboard.Keys.Count == 0 && f.State.YieldToWorker && f.State.PendingAction is null,
                "fallback official target is reread after a child read and stops entity or server identity changes");
        }
    }

    public static async Task PostGuardChildReadStillReleasesSameChildAsync()
    {
        foreach (var combined in new[] { false, true })
            foreach (var petGuard in new[] { false, true })
            {
                var f = new Fixture(combined);
                await f.Tick(petGuard: petGuard);
                Check(f.Keyboard.Keys.SequenceEqual(new[] { "D2" }) && f.State.PendingAction?.Node.SkillId == 12 &&
                    f.SkillReadCount == 3 && f.Reader.ReadCount == 3 && f.TargetReadCount == (combined ? 0 : 2) &&
                    f.SuppressionReadCount == (petGuard ? 3 : 0),
                    "an unchanged continuation still releases once; the existing pet guard shares the final availability read");
            }
    }

    public static async Task FinalCandidateChangeGetsFreshGuardAsync()
    {
        var f = new Fixture();
        f.Reader.OnRead = count =>
        {
            if (count == 2) f.Reader.Value = f.Reader.Value with { LastReleasedSkillId = 11, LastReleasedSkillTime = 1100 };
            if (count == 3) f.Reader.Value = f.Reader.Value with
            {
                Slots = Slots(childLit: false), LastReleasedSkillId = 31, LastReleasedSkillTime = 1200
            };
            if (count == 4) f.Reader.Value = f.Reader.Value with { CombatState = Guard(server: 101) };
        };
        await f.Tick();
        Check(f.SkillReadCount == 3 && f.Reader.ReadCount == 4 && f.Keyboard.Keys.Count == 0 && f.State.YieldToWorker,
            "a different candidate selected by the final guard enters the bounded loop and receives its own fresh guard");
    }

    public static async Task NoPostGuardReadKeepsReadBudgetAsync()
    {
        foreach (var childLit in new[] { false, true })
            foreach (var combined in new[] { false, true })
            {
                var f = new Fixture(combined, seedPending: false);
                f.Reader.Value = Bar(combined, childLit);
                f.Reader.OnRead = null;
                await f.Tick();
                Check(f.Keyboard.Keys.SequenceEqual(new[] { childLit ? "D2" : "D1" }) &&
                    f.SkillReadCount == 1 && f.Reader.ReadCount == 2 && f.TargetReadCount == (combined ? 0 : 1),
                    "root and already-lit child paths without an extra post-guard read retain their existing read budget" +
                    $" (childLit={childLit}, combined={combined}, keys={string.Join(',', f.Keyboard.Keys)}," +
                    $" skillReads={f.SkillReadCount}, availabilityReads={f.Reader.ReadCount}, targetReads={f.TargetReadCount})");
            }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) =>
        new() { SkillId = id, Name = "skill" + id, Children = children.ToList() };

    private static SkillSnapshot Skill(uint id) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 0);

    private static LockedTargetSnapshot Target(ushort entity = 50, uint server = 100) =>
        new(entity, server, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);

    private static SkillAvailabilityCombatSnapshot Guard(ushort entity = 50, uint server = 100, uint hp = 100) =>
        new(1, 10, entity, server, hp, 100, 100, 100);

    private static SkillAvailabilitySlotSnapshot[] Slots(bool childLit) => new[]
    {
        new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 0, 21, 11, 11, true),
        new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 1, 21, 12, 12, childLit)
    };

    private static SkillAvailabilitySnapshot Bar(bool combined, bool childLit = true) =>
        new(0, Slots(childLit), 31, 1000,
            BindingSlots: new SkillAvailabilityBindingSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11, 11),
                new(SkillQuickbar.Main, 1, 21, 12, 12)
            }, CombatState: combined ? Guard() : null);

    private sealed class Fixture
    {
        public readonly QuickbarSkillPlan Plan;
        public readonly QuickbarSkillCombatState State = new();
        public readonly Reader Reader = new();
        public readonly Keyboard Keyboard = new();
        public LockedTargetSnapshot CurrentTarget = Target();
        public Action? AfterTransitionRead;
        public bool MaintenanceDue;
        public int SkillReadCount;
        public int TargetReadCount;
        public int SuppressionReadCount;
        private readonly Clock clock = new();
        private readonly QuickbarSkillCombatController controller;

        public Fixture(bool combined = true, bool seedPending = true)
        {
            var skills = new[] { Skill(11), Skill(12) };
            var bindings = new QuickbarSnapshot(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 12)
            });
            Plan = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(11, Node(12)) } },
                new(bindings, skills));
            Reader.Value = Bar(combined);
            State.ObserveScope(Target(), Reader.Value);
            if (seedPending)
                State.BeginAction(Plan.Roots[0], skills[0], Reader.Value, Start.AddMilliseconds(-80),
                    TimeSpan.FromSeconds(8), timeProvider: clock);
            Reader.OnRead = count =>
            {
                if (count == 2) Reader.Value = Reader.Value with { LastReleasedSkillId = 11, LastReleasedSkillTime = 1100 };
            };
            controller = new(Keyboard, clock);
        }

        public Task<TimeSpan> Tick(bool petGuard = false, CancellationToken token = default) => controller.TickAsync(Plan, State, Target(), Reader,
            ReadSkills, new() { KeyHoldMs = 25 }, cancellationToken: token,
            readTargetBeforePress: () => { TargetReadCount++; return Task.FromResult(CurrentTarget); },
            cooldownReadiness: _ => SemiAutoSkillCooldownReadiness.Ready,
            allowCombatSnapshot: _ => !MaintenanceDue,
            readSuppressedSkillIds: petGuard ? ReadSuppressed : null);

        private async Task<IReadOnlyList<SkillSnapshot>> ReadSkills(IReadOnlyCollection<uint> ids)
        {
            SkillReadCount++;
            if (SkillReadCount == 3) AfterTransitionRead?.Invoke();
            await Task.Yield();
            return ids.Select(Skill).ToArray();
        }

        private Task<IReadOnlySet<uint>> ReadSuppressed()
        {
            SuppressionReadCount++;
            return Task.FromResult<IReadOnlySet<uint>>(new HashSet<uint>());
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Start;
        public override long GetTimestamp() => 0;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Bar(true);
        public int ReadCount;
        public Action<int>? OnRead;
        public int BlockAt, ActiveReads, CancelledReads;
        public async Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (ReadCount == BlockAt)
            {
                ActiveReads++;
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { CancelledReads++; throw; }
                finally { ActiveReads--; }
            }
            OnRead?.Invoke(ReadCount);
            return new PublishedGameSnapshot<SkillAvailabilitySnapshot>(ReadCount, Value);
        }
    }

    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<string> Keys = new();
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Keys.Add(key); return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
