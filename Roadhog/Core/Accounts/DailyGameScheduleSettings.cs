using System.Globalization;

namespace Roadhog.Core.Accounts;

/// <summary>Account-owned schedule; switching a combat profile must not change login times.</summary>
public sealed class DailyGameScheduleSettings
{
    public bool OnlineEnabled { get; set; }
    public string OnlineStart { get; set; } = "08:00";
    public string OnlineEnd { get; set; } = "22:00";
    public bool ReloginEnabled { get; set; }
    public List<string> ReloginTimes { get; set; } = ["22:00"];
    public bool Enabled => OnlineEnabled || ReloginEnabled;

    public DailyGameScheduleSettings Clone() => new()
    {
        OnlineEnabled = OnlineEnabled, OnlineStart = OnlineStart, OnlineEnd = OnlineEnd,
        ReloginEnabled = ReloginEnabled, ReloginTimes = ReloginTimes?.ToList() ?? []
    };

    public DailyGameScheduleSettings Normalize()
    {
        var copy = Clone();
        if (OnlineEnabled)
        {
            copy.OnlineStart = Format(Parse(OnlineStart)); copy.OnlineEnd = Format(Parse(OnlineEnd));
            if (copy.OnlineStart == copy.OnlineEnd) throw new FormatException("上线和下线时间不能相同；全天挂机请关闭定时挂机。");
        }
        if (ReloginEnabled)
        {
            copy.ReloginTimes = copy.ReloginTimes.Select(Parse).Distinct().Order().Select(Format).ToList();
            if (copy.ReloginTimes.Count is 0 or > 24) throw new FormatException("请填写 1 至 24 个每日重登时间。");
        }
        return copy;
    }

    public static TimeOnly Parse(string text) => TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var time) ? time : throw new FormatException("定时时间必须为 HH:mm（00:00 至 23:59）。");
    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
