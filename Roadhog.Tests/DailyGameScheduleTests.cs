using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;

internal static class DailyGameScheduleTests
{
    internal sealed class Clock : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _now;
        public Clock(string time) { _now = At(time); }
        public override DateTimeOffset GetUtcNow() { lock (_sync) return _now.ToUniversalTime(); }
        public void Set(string time) { lock (_sync) _now = At(time); }
    }
    internal static DateTimeOffset At(string time) => DateTimeOffset.Parse(time + "+08:00");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    internal static Task WindowsAndMidnightAsync()
    {
        var settings = new DailyGameScheduleSettings { OnlineEnabled = true };
        foreach (var (time, online) in new[] { ("07:59:59", false), ("08:00:00", true), ("21:59:59", true), ("22:00:00", false) })
            Check(DailyGameSchedule.Window(settings, At("2026-10-30T" + time)).Online == online, "daytime boundary " + time);
        settings.OnlineStart = "22:00"; settings.OnlineEnd = "08:00";
        Check(DailyGameSchedule.Window(settings, At("2026-10-31T00:05:00")) == (true, At("2026-10-30T22:00:00")), "midnight retains previous evening's occurrence");
        Check(!DailyGameSchedule.Window(settings, At("2026-10-31T08:00:00")).Online, "overnight end is exclusive");
        Check(DailyGameSchedule.Window(settings, At("2026-10-30T23:00:00").ToUniversalTime()).Online, "same instant uses Beijing time regardless of caller offset");
        return Task.CompletedTask;
    }

    internal static Task FixedSlotsAndOfflinePriorityAsync()
    {
        var settings = new DailyGameScheduleSettings { OnlineEnabled = true, ReloginEnabled = true, ReloginTimes = ["10:00", "18:00", "22:00"] };
        var state = new DailyGameScheduleState { LastOnlineStart = At("2026-10-30T08:00:00") };
        var due = DailyGameSchedule.Decide(settings, state, At("2026-10-30T09:59:59"), At("2026-10-30T10:00:12"));
        Check(due == new DailyGameScheduleDecision(DailyGameScheduleAction.Relogin, At("2026-10-30T10:00:00")), "delayed poll observes fixed slot once");
        DailyGameSchedule.Record(state, due!);
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-30T09:59:59"), At("2026-10-30T10:00:30")) == null, "persisted completion rejects duplicate slot");
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-30T21:59:59"), At("2026-10-30T22:00:00"))?.Action == DailyGameScheduleAction.Exit, "offline beats simultaneous relogin");
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-30T12:00:00"), At("2026-10-30T12:00:01")) == null, "late opening does not replay morning slot");
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-30T12:00:00"), At("2026-10-30T11:59:00")) == null, "clock rollback cannot replay fixed login");
        return Task.CompletedTask;
    }

    internal static Task ManualStopAndNextDayAsync()
    {
        var settings = new DailyGameScheduleSettings { OnlineEnabled = true, ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var resume = DailyGameSchedule.NextResume(settings, At("2026-10-30T09:00:00"));
        Check(resume == At("2026-10-31T08:00:00"), "manual stop suppresses remaining daily window, including fixed relogins");
        var state = new DailyGameScheduleState { ManualResumeAt = resume };
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-30T09:59:59"), At("2026-10-30T10:00:01")) == null, "fixed relogin cannot undo manual stop");
        Check(DailyGameSchedule.Decide(settings, state, At("2026-10-31T07:59:59"), resume!.Value)?.Action == DailyGameScheduleAction.Start, "next daily window releases pause");
        settings.OnlineEnabled = false;
        Check(DailyGameSchedule.NextResume(settings, At("2026-10-30T09:00:00")) == At("2026-10-30T10:00:00"), "standalone fixed login is next planned online event");
        return Task.CompletedTask;
    }

    internal static Task ValidationAndCloneAsync()
    {
        var input = new DailyGameScheduleSettings { ReloginEnabled = true, ReloginTimes = ["18:00", "10:00", "10:00"] };
        Check(input.Normalize().ReloginTimes.SequenceEqual(new[] { "10:00", "18:00" }), "fixed times canonicalized and deduplicated");
        var account = new AccountConfig { DailySchedule = input }; var clone = account.Clone(); clone.DailySchedule.ReloginTimes.Clear();
        Check(account.DailySchedule.ReloginTimes.Count == 3, "schedule clones cannot mutate another account");
        foreach (var invalid in new[] { "24:00", "10:61", "bad", "8:00" })
        {
            try { new DailyGameScheduleSettings { OnlineEnabled = true, OnlineStart = invalid }.Normalize(); throw new InvalidOperationException("accepted " + invalid); }
            catch (FormatException) { }
        }
        try { new DailyGameScheduleSettings { OnlineEnabled = true, OnlineStart = "08:00", OnlineEnd = "08:00" }.Normalize(); throw new InvalidOperationException("ambiguous full day accepted"); }
        catch (FormatException) { }
        Check(DailyGameSchedule.Decide(new(), new(), At("2026-10-30T07:00:00"), At("2026-10-30T10:00:00")) == null, "legacy default has no schedule actions");
        return Task.CompletedTask;
    }
}
