using Roadhog.Application.SemiAuto;
using Roadhog.Core.Model;

internal static class QuickbarAttackWeaveCooldownStateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public static Task AvailablePressRequiredAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        state.TrackMainPress(Skill(1), wasAvailable: false, clock, Timeout);
        state.TrackMainPress(null, wasAvailable: true, clock, Timeout);
        state.TrackMainPress(Skill(2) with { CooldownDuration = 0 }, wasAvailable: true, clock, Timeout);
        state.TrackOpeningPress(Skill(3) with { CooldownDuration = 0 }, clock, Timeout);
        Check(!state.HasPendingAttempts && !state.HasOpeningAttempt, "unavailable, missing and zero-duration skills do not register cooldown attempts");
        Equal(0, state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200), Skill(3, 300) }, clock, 800).Count,
            "unregistered cooldown changes cannot count");
        state.TrackMainPress(Skill(1), wasAvailable: true, clock, Timeout);
        state.TrackOpeningPress(Skill(3), clock, Timeout);
        Sequence(new uint[] { 1, 3 }, state.PendingSkillIds.OrderBy(id => id), "main and opening actions register their exact skill IDs");
        Check(state.HasOpeningAttempt, "opening ownership remains visible while its cooldown is pending");
        Sequence(new uint[] { 1, 3 }, state.ObserveCooldowns(new[] { Skill(1, 100), Skill(3, 300) }, clock, 800),
            "available main and opening actions use the same cooldown-only evidence");
        Equal(2, state.ConfirmedCount, "main and opening cooldown transitions complete one pair");
        return Task.CompletedTask;
    }

    public static Task ExactCooldownAndWrapAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        state.TrackMainPress(Skill(1, 1000), true, clock, Timeout);
        Equal(0, state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 800).Count, "a missing snapshot does not count or replace the captured baseline");
        Sequence(new uint[] { 1 }, state.PendingSkillIds, "missing evidence keeps the exact attempt pending");
        foreach (var snapshot in new[] { Skill(2, 2000), Skill(1, 1000), Skill(1, 500), Skill(1, 0), Skill(1, 1001) with { CooldownDuration = 0 } })
            Equal(0, state.ObserveCooldowns(new[] { snapshot }, clock, 800).Count,
                "another skill, unchanged end-time, backwards end-time, cleared cooldown and zero duration cannot confirm this attempt");
        Sequence(new uint[] { 1 }, state.ObserveCooldowns(new[] { Skill(1, 1001) }, clock, 800),
            "the exact skill's first genuine forward cooldown transition counts once");
        Equal(0, state.ObserveCooldowns(new[] { Skill(1, 1002) }, clock, 800).Count, "a consumed attempt cannot count another snapshot without a new successful available key");

        state.Reset();
        state.TrackMainPress(Skill(1, uint.MaxValue - 100), true, clock, Timeout);
        Sequence(new uint[] { 1 }, state.ObserveCooldowns(new[] { Skill(1, 50) }, clock, 800),
            "a valid uint32 clock wrap is a forward cooldown transition");
        state.Reset();
        state.TrackMainPress(Skill(1, 50), true, clock, Timeout);
        Equal(0, state.ObserveCooldowns(new[] { Skill(1, uint.MaxValue - 100) }, clock, 800).Count,
            "a backwards wrapped clock cannot be mistaken for forward cooldown progression");
        return Task.CompletedTask;
    }

    public static Task FirstBaselineAndIdleBoundaryAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        state.TrackMainPress(Skill(1, 100), true, clock, Timeout);
        clock.Advance(80);
        state.TrackMainPress(Skill(1, 200), true, clock, Timeout);
        Equal(1, state.PendingSkillIds.Count, "repeated successful keys reserve only one attempt for the exact skill");
        Sequence(new uint[] { 1 }, state.ObserveCooldowns(new[] { Skill(1, 200) }, clock, 800),
            "a retry preserves the first successful key baseline rather than rebasing to the newer end-time");
        Equal(0, state.ObserveCooldowns(new[] { Skill(1, 200) }, clock, 800).Count, "the same progression cannot count once per retry");

        state.Reset();
        state.TrackMainPress(Skill(1), true, clock, Timeout);
        clock.Advance(1500);
        Sequence(new uint[] { 1 }, state.ObserveCooldowns(new[] { Skill(1, 100) }, clock, 800),
            "cooldown evidence at exactly 1500 milliseconds still belongs to the successful key");
        state.Reset();
        state.TrackMainPress(Skill(1), true, clock, Timeout);
        clock.Advance(1501);
        Equal(0, state.ObserveCooldowns(new[] { Skill(1, 100) }, clock, 800).Count,
            "cooldown evidence after 1501 milliseconds cannot reuse an expired single attempt");
        Check(!state.HasPendingAttempts && state.ConfirmedCount == 0, "idle expiry clears the old pair and attempt set");
        return Task.CompletedTask;
    }

    public static Task SimultaneousCooldownsSurviveAttackAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        foreach (var id in new uint[] { 1, 2, 3, 4 }) state.TrackMainPress(Skill(id), true, clock, Timeout);
        Sequence(new uint[] { 1, 2 }, state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200), Skill(3, 300) }, clock, 2500),
            "three simultaneous cooldown changes consume only two actions in the current pair");
        Equal(2, state.ConfirmedCount, "a batch cannot overflow the two-action pair");
        Check(state.HasPendingAttempts, "third confirmed action and fourth unconfirmed attempt remain available for the next pair");
        clock.Advance(2500);
        Equal(0, state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 2500).Count,
            "a long configured C wait cannot consume or discard the queued third action");
        Check(state.ShouldPressAttack(clock), "the long configured wait keeps its original deadline");
        state.MarkAttackSucceeded(clock);
        Equal(0, state.ConfirmedCount, "successful C clears the completed pair");
        Check(state.HasPendingAttempts, "successful C preserves outstanding cooldown attempts and unconsumed evidence");
        clock.Advance(29);
        Equal(0, state.ObserveCooldowns(new[] { Skill(4, 400) }, clock, 2500).Count,
            "late fourth cooldown can be stored during the post-C pause without consuming the next pair early");
        clock.Advance(1);
        Sequence(new uint[] { 3, 4 }, state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 2500),
            "after 30 milliseconds the retained third and late fourth cooldowns enter the next pair exactly once");
        Equal(2, state.ConfirmedCount, "the next pair remains bounded when buffered evidence is consumed");
        return Task.CompletedTask;
    }

    public static Task BufferedSingleIdleResetAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        foreach (var id in new uint[] { 1, 2, 3 }) state.TrackMainPress(Skill(id), true, clock, Timeout);
        state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200), Skill(3, 300) }, clock, 0);
        state.MarkAttackSucceeded(clock);
        clock.Advance(30);
        Sequence(new uint[] { 3 }, state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 0),
            "the buffered third action starts its own fresh pair after C");
        Equal(1, state.ConfirmedCount, "buffered third action leaves one count without a new skill key");
        clock.Advance(1500);
        state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 0);
        Equal(1, state.ConfirmedCount, "a buffered single count remains valid at exactly 1500 milliseconds");
        clock.Advance(1);
        state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 0);
        Equal(0, state.ConfirmedCount, "a buffered single count expires after 1501 milliseconds even without a later key");
        Check(!state.HasPendingAttempts, "buffered idle expiry cannot preserve stale queued evidence");
        return Task.CompletedTask;
    }

    public static Task CarriedPendingIdleDeadlineAsync()
    {
        var clock = new Clock();
        var state = new QuickbarAttackWeaveState();
        foreach (var id in new uint[] { 1, 2, 3 }) state.TrackMainPress(Skill(id), true, clock, Timeout);
        state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200) }, clock, 0);
        state.MarkAttackSucceeded(clock);
        clock.Advance(30);
        Equal(0, state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 0).Count,
            "an unconfirmed third action remains pending at the end of the post-C pause");
        Sequence(new uint[] { 3 }, state.PendingSkillIds, "successful C preserves the third action's original cooldown baseline");
        clock.Advance(1500);
        state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 0);
        Sequence(new uint[] { 3 }, state.PendingSkillIds,
            "an unconfirmed carried action remains pending exactly 1500 milliseconds after the post-C pause");
        clock.Advance(1);
        Equal(0, state.ObserveCooldowns(new[] { Skill(3, 300) }, clock, 0).Count,
            "late cooldown cannot count after the carried action's post-C idle deadline even within its eight-second attempt timeout");
        Check(!state.HasPendingAttempts && state.ConfirmedCount == 0,
            "carried pending actions expire after 1501 idle milliseconds without a new key");

        clock = new Clock();
        state = new QuickbarAttackWeaveState();
        foreach (var id in new uint[] { 1, 2, 3, 4 }) state.TrackMainPress(Skill(id), true, clock, Timeout);
        state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200), Skill(3, 300) }, clock, 0);
        state.MarkAttackSucceeded(clock);
        clock.Advance(1531);
        Equal(0, state.ObserveCooldowns(new[] { Skill(4, 400) }, clock, 0).Count,
            "a worker first returning after the carried idle deadline cannot revive either queued evidence or a pending cooldown");
        Check(!state.HasPendingAttempts && state.PendingSkillIds.Count == 0 && state.ConfirmedCount == 0,
            "late first observation clears both queued third evidence and the unconfirmed fourth action");
        return Task.CompletedTask;
    }

    public static Task TimeoutAndResetQueueAsync()
    {
        foreach (var elapsed in new[] { 7999, 8000 })
        {
            var clock = new Clock();
            var state = new QuickbarAttackWeaveState();
            foreach (var id in new uint[] { 1, 2, 3 }) state.TrackMainPress(Skill(id), true, clock, Timeout);
            state.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200) }, clock, 10000);
            clock.Advance(elapsed);
            state.ObserveCooldowns(new[] { Skill(3, 300) }, clock, 10000);
            Equal(elapsed == 7999, state.HasPendingAttempts,
                "the third attempt accepts cooldown before its eight-second deadline and expires exactly at the deadline");
            state.MarkAttackSucceeded(clock);
            clock.Advance(30);
            var counted = state.ObserveCooldowns(Array.Empty<SkillSnapshot>(), clock, 10000);
            Equal(elapsed == 7999 ? 1 : 0, counted.Count,
                "only cooldown evidence captured within the finite attempt budget survives C into the next pair");
        }

        var resetClock = new Clock();
        var reset = new QuickbarAttackWeaveState();
        foreach (var id in new uint[] { 1, 2, 3, 4 }) reset.TrackMainPress(Skill(id), true, resetClock, Timeout);
        reset.ObserveCooldowns(new[] { Skill(1, 100), Skill(2, 200), Skill(3, 300) }, resetClock, 10000);
        Check(reset.HasPendingAttempts, "reset fixture contains both queued evidence and an unconfirmed attempt");
        reset.Reset();
        Check(!reset.IsWaiting && !reset.HasPendingAttempts && reset.PendingSkillIds.Count == 0,
            "reset clears the successful pair, queued evidence and unconfirmed attempts together");
        resetClock.Advance(10030);
        Equal(0, reset.ObserveCooldowns(new[] { Skill(3, 300), Skill(4, 400) }, resetClock, 10000).Count,
            "late snapshots after reset cannot revive any queued or pending action");
        reset.TrackMainPress(Skill(4, 400), true, resetClock, Timeout);
        Sequence(new uint[] { 4 }, reset.ObserveCooldowns(new[] { Skill(4, 500) }, resetClock, 10000),
            "a fresh successful key after reset starts a clean pair");
        Equal(1, reset.ConfirmedCount, "cleared queued evidence cannot contaminate the next pair");
        return Task.CompletedTask;
    }

    private static SkillSnapshot Skill(uint id, uint endTime = 0) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, endTime);
    private sealed class Clock : TimeProvider
    {
        private long stamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(stamp);
        public override long GetTimestamp() => stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(int milliseconds) => stamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Sequence(IEnumerable<uint> expected, IEnumerable<uint> actual, string message) =>
        Check(expected.SequenceEqual(actual), $"{message}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
}
