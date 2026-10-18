using System.Globalization;

namespace Roadhog.Core.Accounts;

public static class GroceryShopSchedule
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> times)
    {
        return times.Select(t => TimeOnly.ParseExact(t.Trim(), "HH:mm", CultureInfo.InvariantCulture))
            .Distinct().Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();
    }

    // Generate today's elapsed slots within the current worker session. Carry an earlier
    // slot only when this running session observed it; never recover a stopped session's backlog.
    public static DateTimeOffset? LatestDue(CleanupWorkflowSettings settings, DateTimeOffset now,
        DateTimeOffset? lastSoldOut, DateTimeOffset? pendingDue = null, DateTimeOffset? sessionStartedAt = null)
    {
        if (settings.Mode != CleanupMode.GroceryShop || !settings.GroceryScheduleEnabled) return null;
        var local = now.ToOffset(TimeSpan.FromHours(8));
        var times = Normalize(settings.GroceryScheduleTimes);
        DateTimeOffset? latest = pendingDue.HasValue && pendingDue <= local &&
            (!sessionStartedAt.HasValue || pendingDue >= sessionStartedAt) &&
            times.Contains(pendingDue.Value.ToOffset(local.Offset).ToString("HH:mm", CultureInfo.InvariantCulture))
            ? pendingDue : null;
        foreach (var text in times)
        {
            var time = TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
            var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, time.Hour, time.Minute, 0, local.Offset);
            if (candidate > local || (sessionStartedAt.HasValue && candidate < sessionStartedAt)) continue;
            if (latest == null || candidate > latest) latest = candidate;
        }
        return latest.HasValue && (!lastSoldOut.HasValue || latest > lastSoldOut) ? latest : null;
    }
}
