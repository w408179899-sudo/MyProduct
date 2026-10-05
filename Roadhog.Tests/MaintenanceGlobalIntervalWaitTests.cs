using Roadhog.Application.SemiAuto;

internal static class MaintenanceGlobalIntervalWaitTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-05T12:00:00+08:00");
    private static readonly TimeSpan Interval = SemiAutoCombatController.MaintenanceGlobalKeyInterval;
    private static readonly TimeSpan KeyRetry = TimeSpan.FromSeconds(3);

    public static async Task RemainingIntervalAndKeyRetryAsync()
    {
        var state = new SemiAutoCombatState();
        Check(state.GetRemainingMaintenanceGlobalInterval(Start, Interval) == TimeSpan.Zero,
            "an unmarked state has no global throttle");
        state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
        Check(state.GetRemainingMaintenanceGlobalInterval(Start, TimeSpan.Zero) == TimeSpan.Zero &&
            state.GetRemainingMaintenanceGlobalInterval(Start, TimeSpan.FromMilliseconds(-1)) == TimeSpan.Zero,
            "a disabled global interval has no remaining wait");
        var beforeBoundary = Start + Interval - TimeSpan.FromTicks(1);
        Check(state.GetRemainingMaintenanceGlobalInterval(beforeBoundary, Interval) == TimeSpan.FromTicks(1) &&
            !CanPress(state, "NumPad3", beforeBoundary), "the exact final tick remains blocked");
        Check(state.GetRemainingMaintenanceGlobalInterval(Start + Interval, Interval) == TimeSpan.Zero &&
            CanPress(state, "NumPad3", Start + Interval) && !CanPress(state, "NumPadAdd", Start + Interval),
            "600ms opens a different key while preserving the three-second same-key retry");
        Check(!CanPress(state, "NumPadAdd", Start + KeyRetry - TimeSpan.FromTicks(1)) &&
            CanPress(state, "NumPadAdd", Start + KeyRetry), "the same-key retry retains its exact boundary");

        var now = Start.AddMilliseconds(1200);
        var waits = new List<TimeSpan>();
        var ready = await Wait(state, () => now, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            waits.Add(duration);
            now += duration;
            return Task.CompletedTask;
        });
        Check(ready && waits.SequenceEqual(new[] { Interval }) && now == Start.AddMilliseconds(1800),
            "the original full 600ms pause remains even when the global gate was already ready");
        Check(!CanPress(state, "NumPadAdd", now) && CanPress(state, "NumPad3", now),
            "waiting does not clear or overwrite maintenance retry history");
        foreach (var cleared in new[] { false, true })
        {
            state = new();
            if (cleared)
            {
                state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
                state.ClearMaintenanceKeyAttempt("NumPadAdd");
            }
            now = Start;
            waits.Clear();
            ready = await Wait(state, () => now, (duration, _) =>
            {
                waits.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });
            Check(ready && waits.SequenceEqual(new[] { Interval }),
                "an unmarked or previously cleared state still retains the original full pause");
        }
    }

    public static async Task EarlyWakeWaitsUntilReadyAsync()
    {
        foreach (var shortfall in new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromTicks(1) })
        {
            var state = new SemiAutoCombatState();
            state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
            var now = Start;
            var waits = new List<TimeSpan>();
            var timer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Wait(state, () => now, (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                waits.Add(duration);
                if (waits.Count == 1)
                {
                    now += duration - shortfall;
                    return Task.CompletedTask;
                }
                return timer.Task;
            });
            Check(waits.SequenceEqual(new[] { Interval, TimeSpan.FromMilliseconds(1) }) &&
                !pending.IsCompleted && !CanPress(state, "NumPad3", now),
                "an early timer wake must not finish the round's wait; the remaining fraction rounds up without a spin");
            now += waits[^1];
            timer.SetResult();
            Check(await pending && CanPress(state, "NumPad3", now) && !CanPress(state, "NumPadAdd", now),
                "the next key becomes eligible only after the original throttle permits it");
        }
    }

    public static async Task WallClockChangesPreserveGateAsync()
    {
        foreach (var jump in new[] { TimeSpan.FromMilliseconds(-40), TimeSpan.FromHours(-1), TimeSpan.FromHours(1) })
        {
            var state = new SemiAutoCombatState();
            state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
            var now = Start;
            var waits = new List<TimeSpan>();
            var ready = await Wait(state, () => now, (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                waits.Add(duration);
                now += duration;
                if (waits.Count == 1) now += jump;
                return Task.CompletedTask;
            });
            Check(waits[0] == Interval && waits.All(wait => wait > TimeSpan.Zero && wait <= Interval),
                "every wait preserves the original first interval and remains bounded");
            if (jump == TimeSpan.FromHours(-1))
                Check(!ready && waits.SequenceEqual(new[] { Interval }) && !CanPress(state, "NumPad3", now),
                    "a large backward adjustment yields after the original pause instead of blocking the worker for an hour");
            else
                Check(ready && state.GetRemainingMaintenanceGlobalInterval(now, Interval) == TimeSpan.Zero &&
                    waits.SequenceEqual(jump < TimeSpan.Zero ? new[] { Interval, TimeSpan.FromMilliseconds(40) } : new[] { Interval }),
                    "a small backward change waits its remaining interval; a forward change needs no extra pause");
        }

        var stalled = new SemiAutoCombatState();
        stalled.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
        var stalledWaits = new List<TimeSpan>();
        var stalledReady = await Wait(stalled, () => Start, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            stalledWaits.Add(duration);
            return Task.CompletedTask;
        });
        Check(!stalledReady && stalledWaits.SequenceEqual(new[] { Interval, Interval }) &&
            !CanPress(stalled, "NumPad3", Start),
            "a stalled clock spends at most one extra interval and yields without weakening the original throttle");
    }

    public static async Task CancellationStopsWaitAsync()
    {
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel();
            var calls = 0;
            await ExpectCancellation(Wait(new(), () => Start, (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }, stop.Token));
            Check(calls == 0, "pre-cancellation prevents even the first delay");
        }
        foreach (var duringRemainder in new[] { false, true })
        {
            using var stop = new CancellationTokenSource();
            var state = new SemiAutoCombatState();
            state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
            var now = Start;
            var calls = 0;
            var pending = Wait(state, () => now, (duration, token) =>
            {
                calls++;
                Check(token == stop.Token, "every timer receives the worker's exact stop token");
                if (duringRemainder && calls == 1)
                {
                    now += duration - TimeSpan.FromMilliseconds(1);
                    return Task.CompletedTask;
                }
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, stop.Token);
            Check(!pending.IsCompleted && calls == (duringRemainder ? 2 : 1),
                "the wait is suspended at the intended cancellable timer");
            stop.Cancel();
            await ExpectCancellation(pending);
            Check(calls == (duringRemainder ? 2 : 1) && !CanPress(state, "NumPad3", now),
                "cancellation neither starts another timer nor alters the throttle history");
        }
        using (var stop = new CancellationTokenSource())
        {
            var state = new SemiAutoCombatState();
            state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
            var now = Start;
            var calls = 0;
            await ExpectCancellation(Wait(state, () => now, (duration, token) =>
            {
                Check(token == stop.Token, "completion cancellation retains the exact stop token");
                calls++;
                now += duration;
                stop.Cancel();
                return Task.CompletedTask;
            }, stop.Token));
            Check(calls == 1 && CanPress(state, "NumPad3", now),
                "cancellation on timer completion wins even when the global gate is already open");
        }
    }

    public static async Task UpdatedAndClearedAnchorsAsync()
    {
        var state = new SemiAutoCombatState();
        state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
        var now = Start;
        var waits = new List<TimeSpan>();
        var ready = await Wait(state, () => now, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            waits.Add(duration);
            now += duration;
            if (waits.Count == 1) state.MarkMaintenanceKeyAttempted("NumPad6", now);
            return Task.CompletedTask;
        });
        Check(ready && waits.SequenceEqual(new[] { Interval, Interval }) && CanPress(state, "NumPad3", now) &&
            !CanPress(state, "NumPad6", now), "a newer key attempt extends the global gate without clearing its key retry");

        state = new();
        state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
        now = Start;
        waits.Clear();
        ready = await Wait(state, () => now, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            waits.Add(duration);
            now += duration;
            state.MarkMaintenanceKeyAttempted("NumPad6", now);
            return Task.CompletedTask;
        });
        Check(!ready && waits.SequenceEqual(new[] { Interval, Interval }) && !CanPress(state, "NumPad3", now),
            "repeated anchor updates exhaust the extra wait budget and yield without permitting another maintenance key");

        foreach (var clearAll in new[] { false, true })
        {
            state = new();
            state.MarkMaintenanceKeyAttempted("NumPadAdd", Start);
            if (!clearAll) state.MarkMaintenanceKeyAttempted("NumPad6", Start.AddMilliseconds(200));
            now = Start;
            waits.Clear();
            ready = await Wait(state, () => now, (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                waits.Add(duration);
                now += duration - (clearAll ? TimeSpan.FromMilliseconds(1) : TimeSpan.Zero);
                state.ClearMaintenanceKeyAttempt(clearAll ? "NumPadAdd" : "NumPad6");
                return Task.CompletedTask;
            });
            Check(ready && waits.SequenceEqual(new[] { Interval }) && CanPress(state, "NumPad3", now),
                "clearing the latest or sole anchor is observed after the original full pause");
            if (!clearAll)
                Check(!CanPress(state, "NumPadAdd", now), "clearing another key preserves the older key's three-second retry");
        }
    }

    private static Task<bool> Wait(SemiAutoCombatState state, Func<DateTimeOffset> now,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken = default) =>
        SemiAutoCombatController.WaitForMaintenanceGlobalIntervalAsync(state, Interval, now, delay, cancellationToken);

    private static bool CanPress(SemiAutoCombatState state, string key, DateTimeOffset now) =>
        state.ShouldPressMaintenanceKey(key, now, KeyRetry, Interval);

    private static async Task ExpectCancellation(Task pending)
    {
        try { await pending; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("waiting must propagate cancellation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
