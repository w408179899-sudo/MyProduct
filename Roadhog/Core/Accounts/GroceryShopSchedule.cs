using System.Globalization;

namespace Roadhog.Core.Accounts;

public static class GroceryShopSchedule
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> times)
    {
        return times.Select(t => TimeOnly.ParseExact(t.Trim(), "HH:mm", CultureInfo.InvariantCulture))
            .Distinct().Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();
    }

    // Daily schedules use China time. A late sellout covers all earlier due slots,
    // including those that occurred while the account was still selling.
    public static DateTimeOffset? LatestDue(CleanupWorkflowSettings settings, DateTimeOffset now,
        DateTimeOffset? lastSoldOut)
    {
        if (settings.Mode != CleanupMode.GroceryShop || !settings.GroceryScheduleEnabled) return null;
        var local = now.ToOffset(TimeSpan.FromHours(8));
        DateTimeOffset? latest = null;
        foreach (var text in Normalize(settings.GroceryScheduleTimes))
        {
            var time = TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
            var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, time.Hour, time.Minute, 0, local.Offset);
            if (candidate > local) candidate = candidate.AddDays(-1);
            if (latest == null || candidate > latest) latest = candidate;
        }
        return latest.HasValue && (!lastSoldOut.HasValue || latest > lastSoldOut) ? latest : null;
    }
}
