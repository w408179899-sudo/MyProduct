using SkillScheduling;

var cases = new (string Name, Func<Task> Run)[]
{
    ("priority, eligibility and final guard", PriorityAsync),
    ("continuation wins an equal priority", EqualPriorityAsync),
    ("paused attack resumes after a higher-priority effect", ResumeAfterPreemptionAsync),
    ("late effect completes the suspended attempt", LateConfirmationAsync),
    ("nested priority and lifecycle reset", NestedAttemptsAsync),
    ("monotonic retry and confirmation deadlines", TimingAsync),
    ("cancellation prevents further candidates", CancellationAsync)
};

var failures = 0;
foreach (var test in cases)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures++; Console.WriteLine($"FAIL {test.Name}\n{exception}"); }
}
Console.WriteLine($"SkillScheduling tests: {cases.Length - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static async Task PriorityAsync()
{
    var inputs = new List<int>();
    var selected = await SkillScheduler.RunAsync<int>(
    [
        Rule(4, priority: 40, eligible: true),
        Rule(1, priority: 10, eligible: false),
        Rule(2, priority: 20, eligible: true, ownsTurn: false),
        Rule(3, priority: 30, eligible: true)
    ]);
    Require(selected?.Id == 3 && inputs.SequenceEqual([2, 3]), "an ineligible or declined rule must yield");
    Require(await SkillScheduler.RunAsync<int>([Rule(1, 10, false)]) is null, "no eligible action means wait");
    Require(inputs.SequenceEqual([2, 3]), "the wait must send no input");

    SkillCandidate<int> Rule(int id, int priority, bool eligible, bool ownsTurn = true) =>
        new(id, priority, eligible, _ => { inputs.Add(id); return Task.FromResult(ownsTurn); });
}

static async Task EqualPriorityAsync()
{
    var inputs = new List<string>();
    var chosen = await SkillScheduler.RunAsync<string>(
    [
        new("new", 10, true, _ => Record("new")),
        new("pending", 10, true, _ => Record("pending"), ContinuesAttempt: true),
        new("later", 10, true, _ => Record("later"), ContinuesAttempt: true)
    ]);
    Require(chosen?.Id == "pending" && inputs.SequenceEqual(["pending"]), "continue A before a fresh equal candidate");
    var guarded = await SkillScheduler.RunAsync<string>(
    [
        new("pending", 10, false, _ => Record("pending"), ContinuesAttempt: true),
        new("new", 10, true, _ => Record("new"))
    ]);
    Require(guarded?.Id == "new", "a continuation still requires eligibility");

    Task<bool> Record(string id) { inputs.Add(id); return Task.FromResult(true); }
}

static async Task ResumeAfterPreemptionAsync()
{
    var clock = new TestClock();
    var attempts = new SkillAttempts<Release>();
    var keys = new List<string>();
    var needsHeal = false;
    var shieldVisible = false;

    await TickAsync();
    var first = attempts.Active!;
    Require(first.Evidence.Id == "attack", "the attack starts first");
    needsHeal = true;
    clock.Advance(100);
    await TickAsync();
    Require(attempts.Active?.Evidence.Id == "heal" && attempts.HasSuspended, "the higher priority heal owns input");
    Require(attempts.Active!.Confirmation.Evaluate(shieldVisible, clock.GetUtcNow()) == SkillConfirmation.Pending,
        "the key alone does not confirm the effect");
    shieldVisible = true;
    Require(attempts.Active.Confirmation.Evaluate(shieldVisible, clock.GetUtcNow()) == SkillConfirmation.Confirmed,
        "this game's effect confirms the heal");
    attempts.ClearActive();
    Require(attempts.TryResume(), "A resumes after B");
    needsHeal = false;
    clock.Advance(100);
    await TickAsync();
    Require(keys.SequenceEqual(["attack", "heal", "attack"]) && ReferenceEquals(first, attempts.Active),
        "the original attempt and baseline survive A, B, A");

    Task<SkillCandidate<string>?> TickAsync() => SkillScheduler.RunAsync<string>(
    [
        new("attack", 20, true, _ => Send("attack"), attempts.Active?.Evidence.Id == "attack"),
        new("heal", 10, needsHeal, _ => Send("heal"))
    ]);

    Task<bool> Send(string id)
    {
        keys.Add(id);
        if (attempts.Active?.Evidence.Id == id)
            attempts.Active.RecordInput(clock.GetUtcNow(), clock, clock.GetTimestamp());
        else
            attempts.Start(NewAttempt(id, keys.Count, clock), suspendActive: attempts.Active is not null);
        return Task.FromResult(true);
    }
}

static Task LateConfirmationAsync()
{
    var attempts = new SkillAttempts<Release>();
    var clock = new TestClock();
    var attack = NewAttempt("charged_action", 3, clock);
    attempts.Start(attack, suspendActive: false);
    attempts.Start(NewAttempt("dodge", 5, clock), suspendActive: true);

    // This adapter confirms from a consumed charge instead of a status effect.
    var charges = 3;
    Release? ObserveChargeUse() => attempts.FinishSuspended(evidence =>
        evidence.Id == "charged_action" && charges < evidence.Baseline);
    Require(ObserveChargeUse() is null, "unchanged charge cannot confirm");
    charges--;
    Require(ObserveChargeUse() == attack.Evidence,
        "late evidence completes only the matching suspended action");
    Require(attempts.Active?.Evidence.Id == "dodge", "late confirmation does not take input away from B");
    attempts.ClearActive();
    Require(!attempts.TryResume(), "the confirmed action does not replay");
    return Task.CompletedTask;
}

static Task NestedAttemptsAsync()
{
    var attempts = new SkillAttempts<Release>();
    var clock = new TestClock();
    attempts.Start(NewAttempt("A", 1, clock), suspendActive: false);
    attempts.Start(NewAttempt("B", 2, clock), suspendActive: true);
    attempts.Start(NewAttempt("C", 3, clock), suspendActive: true);
    Require(!attempts.TryResume(), "the active owner retains input");
    Require(attempts.FinishSuspended(evidence => evidence.Id == "A")?.Id == "A", "older suspended work can confirm first");
    attempts.ClearActive();
    Require(attempts.TryResume() && attempts.Active?.Evidence.Id == "B", "the remaining attempt can resume");
    attempts.Start(NewAttempt("D", 4, clock), suspendActive: true);
    attempts.Clear();
    Require(attempts.Active is null && !attempts.HasSuspended && !attempts.TryResume(), "session reset discards all input");
    return Task.CompletedTask;
}

static Task TimingAsync()
{
    var clock = new TestClock();
    var attempt = NewAttempt("A", 0, clock);
    var window = attempt.Confirmation;
    var cadence = TimeSpan.FromMilliseconds(80);
    clock.ShiftUtc(TimeSpan.FromHours(2));
    clock.Advance(79);
    Require(!attempt.IsRetryDue(clock.GetUtcNow(), cadence), "wall clock change cannot make a retry early");
    clock.Advance(1);
    Require(attempt.IsRetryDue(clock.GetUtcNow(), cadence), "retry opens on the exact monotonic boundary");
    attempt.RecordInput(clock.GetUtcNow(), clock, clock.GetTimestamp());
    clock.Advance(920);
    Require(window.Evaluate(false, clock.GetUtcNow()) == SkillConfirmation.TimedOut, "retry does not extend the original window");
    Require(window.Evaluate(true, clock.GetUtcNow()) == SkillConfirmation.Confirmed, "observed success wins at the deadline");

    var fallback = new SkillConfirmationWindow(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1));
    Require(fallback.Evaluate(false, fallback.Deadline.AddTicks(-1)) == SkillConfirmation.Pending &&
        fallback.Evaluate(false, fallback.Deadline) == SkillConfirmation.TimedOut, "explicit timestamps work without a clock");
    return Task.CompletedTask;
}

static async Task CancellationAsync()
{
    using var cancellation = new CancellationTokenSource();
    var laterRan = false;
    try
    {
        await SkillScheduler.RunAsync<int>(
        [
            new(1, 0, true, token =>
            {
                Require(token == cancellation.Token, "handlers receive the session token");
                cancellation.Cancel();
                return Task.FromResult(false);
            }),
            new(2, 1, true, _ => { laterRan = true; return Task.FromResult(true); })
        ], cancellation.Token);
        throw new InvalidOperationException("expected cancellation");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    Require(!laterRan, "a cancelled tick cannot run another candidate");
}

static SkillAttempt<Release> NewAttempt(string id, int baseline, TestClock clock)
{
    var now = clock.GetUtcNow();
    var timestamp = clock.GetTimestamp();
    return new(new(id, baseline), new(now, TimeSpan.FromSeconds(1), clock, timestamp), now, timestamp);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record Release(string Id, int Baseline);

sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private long _timestamp;
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _timestamp;
    public override long TimestampFrequency => 1000;
    public void Advance(int milliseconds)
    {
        _timestamp += milliseconds;
        _now = _now.AddMilliseconds(milliseconds);
    }
    public void ShiftUtc(TimeSpan delta) => _now += delta;
}
