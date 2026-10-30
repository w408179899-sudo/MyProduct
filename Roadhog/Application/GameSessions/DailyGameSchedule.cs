using Roadhog.Core.Accounts;

namespace Roadhog.Application.GameSessions;

public enum DailyGameScheduleAction { Start, Exit, Relogin }
public sealed record DailyGameScheduleDecision(DailyGameScheduleAction Action, DateTimeOffset Occurrence);

public sealed class DailyGameScheduleState
{
    public DateTimeOffset? LastOnlineStart { get; set; }
    public DateTimeOffset? LastOfflineStart { get; set; }
    public DateTimeOffset? LastRelogin { get; set; }
    public DateTimeOffset? ManualResumeAt { get; set; }
}

/// <summary>Pure daily time decisions, independent of polling, processes and game input.</summary>
public static class DailyGameSchedule
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);
    public static DateTimeOffset Local(DateTimeOffset now) => now.ToOffset(Offset);
    private static DateTimeOffset At(DateTimeOffset day, TimeOnly time) =>
        new(day.Year, day.Month, day.Day, time.Hour, time.Minute, 0, Offset);

    public static (bool Online, DateTimeOffset Boundary) Window(DailyGameScheduleSettings settings, DateTimeOffset now)
    {
        now = Local(now);
        var start = At(now, DailyGameScheduleSettings.Parse(settings.OnlineStart));
        var end = At(now, DailyGameScheduleSettings.Parse(settings.OnlineEnd));
        if (end <= start) end = end.AddDays(1);
        if (now < start) { start = start.AddDays(-1); end = end.AddDays(-1); }
        return now < end ? (true, start) : (false, end);
    }

    public static DateTimeOffset? NextResume(DailyGameScheduleSettings settings, DateTimeOffset now)
    {
        now = Local(now);
        if (settings.OnlineEnabled)
        {
            var next = At(now, DailyGameScheduleSettings.Parse(settings.OnlineStart));
            return next > now ? next : next.AddDays(1);
        }
        if (!settings.ReloginEnabled) return null;
        return settings.ReloginTimes.Select(DailyGameScheduleSettings.Parse)
            .Select(t => At(now, t) is var at && at > now ? at : At(now, t).AddDays(1)).Min();
    }

    public static DailyGameScheduleDecision? Decide(DailyGameScheduleSettings settings, DailyGameScheduleState state,
        DateTimeOffset observedAt, DateTimeOffset now)
    {
        if (!settings.Enabled || state.ManualResumeAt > now) return null;
        if (settings.OnlineEnabled)
        {
            var window = Window(settings, now);
            if (!window.Online)
                return state.LastOfflineStart == window.Boundary ? null : new(DailyGameScheduleAction.Exit, window.Boundary);
        }
        if (settings.ReloginEnabled && now >= observedAt)
        {
            // Late application startup does not replay yesterday's or this morning's fixed-time login.
            var local = Local(now);
            var due = settings.ReloginTimes.Select(DailyGameScheduleSettings.Parse)
                .SelectMany(t => new[] { At(local, t), At(local.AddDays(-1), t) })
                .Where(t => t > observedAt && t <= now && (!state.LastRelogin.HasValue || t > state.LastRelogin.Value))
                .Where(t => !settings.OnlineEnabled || Window(settings, t).Online)
                .OrderDescending().FirstOrDefault();
            if (due != default) return new(DailyGameScheduleAction.Relogin, due);
        }
        if (settings.OnlineEnabled && Window(settings, now) is var online && online.Online && state.LastOnlineStart != online.Boundary)
            return new(DailyGameScheduleAction.Start, online.Boundary);
        return null;
    }

    public static void Record(DailyGameScheduleState state, DailyGameScheduleDecision decision)
    {
        switch (decision.Action)
        {
            case DailyGameScheduleAction.Start: state.LastOnlineStart = decision.Occurrence; break;
            case DailyGameScheduleAction.Exit: state.LastOfflineStart = decision.Occurrence; break;
            case DailyGameScheduleAction.Relogin: state.LastRelogin = decision.Occurrence; break;
        }
    }
}
