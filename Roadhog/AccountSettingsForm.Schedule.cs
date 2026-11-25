using Roadhog.Core.Accounts;
using Roadhog.Application.GameSessions;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private RoundedCheckBox? scheduleOnlineCheckBox;
    private RoundedCheckBox? scheduleReloginCheckBox;
    private RoundedTextBox? scheduleStartTextBox;
    private RoundedTextBox? scheduleEndTextBox;
    private RoundedTextBox? scheduleReloginTimesTextBox;
    private Control[] onlineScheduleFields = [];
    private Control[] reloginScheduleFields = [];
    private Label? onlineScheduleDisabled;
    private Label? reloginScheduleDisabled;
    private Label? nextScheduleLabel;
    private Label? scheduleHelpLabel;
    public event Action<AccountConfig>? AccountConfigurationSaved;

    private void BuildDailySchedule(Panel panel)
    {
        scheduleOnlineCheckBox = AddCheckBox(panel, "定时挂机", 10, 29, 124, false);
        scheduleOnlineCheckBox.Name = "scheduleOnlineCheckBox";
        var start = AddLabel(panel, "每天上线", 142, 30, 72, 24);
        scheduleStartTextBox = AddTextBox(panel, "08:00", 216, 28, 92, 28);
        scheduleStartTextBox.Name = "scheduleStartTextBox";
        var end = AddLabel(panel, "下线", 320, 30, 50, 24);
        scheduleEndTextBox = AddTextBox(panel, "22:00", 374, 28, 92, 28);
        scheduleEndTextBox.Name = "scheduleEndTextBox";
        onlineScheduleFields = [start, scheduleStartTextBox, end, scheduleEndTextBox];
        onlineScheduleDisabled = AddLabel(panel, "未开启", 142, 30, 100, 24);
        scheduleReloginCheckBox = AddCheckBox(panel, "定时重登", 10, 61, 124, false);
        scheduleReloginCheckBox.Name = "scheduleReloginCheckBox";
        var relogin = AddLabel(panel, "每天时间", 142, 62, 72, 24);
        scheduleReloginTimesTextBox = AddTextBox(panel, "22:00", 216, 60, 250, 28);
        scheduleReloginTimesTextBox.Name = "scheduleReloginTimesTextBox";
        var separator = AddLabel(panel, "多个时间用逗号分隔", 476, 62, 164, 24);
        reloginScheduleFields = [relogin, scheduleReloginTimesTextBox, separator];
        reloginScheduleDisabled = AddLabel(panel, "未开启", 142, 62, 100, 24);
        nextScheduleLabel = AddLabel(panel, "", 10, 91, 626, 24);
        nextScheduleLabel.Name = "nextAccountSchedule";
        scheduleHelpLabel = AddLabel(panel, "北京时间（24 小时制） · 定时计划需要主界面保持运行", 10, 115, 626, 24);
        scheduleOnlineCheckBox.Click += (_, _) => RefreshDailyScheduleControls();
        scheduleReloginCheckBox.Click += (_, _) => RefreshDailyScheduleControls();
        foreach (var field in new[] { scheduleStartTextBox, scheduleEndTextBox, scheduleReloginTimesTextBox })
            field.TextChanged += (_, _) => RefreshDailyScheduleControls();
        RefreshDailyScheduleControls();
    }

    private void RefreshDailyScheduleControls()
    {
        var online = scheduleOnlineCheckBox?.Checked == true;
        var relogin = scheduleReloginCheckBox?.Checked == true;
        foreach (var control in onlineScheduleFields) control.Visible = online;
        foreach (var control in reloginScheduleFields) control.Visible = relogin;
        if (onlineScheduleDisabled is not null) onlineScheduleDisabled.Visible = !online;
        if (reloginScheduleDisabled is not null) reloginScheduleDisabled.Visible = !relogin;
        if (nextScheduleLabel is not null)
        {
            nextScheduleLabel.Text = DescribeNextSchedule(ReadDailyScheduleDraft(), DateTimeOffset.UtcNow);
            nextScheduleLabel.Visible = online || relogin;
            scheduleHelpLabel!.Visible = online || relogin;
            nextScheduleLabel.Parent!.Height = online || relogin ? 144 : 92;
            layoutOverview?.Invoke();
        }
    }

    private void LoadDailySchedule(DailyGameScheduleSettings settings)
    {
        SetChecked(scheduleOnlineCheckBox, settings.OnlineEnabled);
        SetChecked(scheduleReloginCheckBox, settings.ReloginEnabled);
        SetText(scheduleStartTextBox, settings.OnlineStart);
        SetText(scheduleEndTextBox, settings.OnlineEnd);
        SetText(scheduleReloginTimesTextBox, string.Join(",", settings.ReloginTimes ?? []));
        RefreshDailyScheduleControls();
    }

    private bool CaptureDailySchedule(AccountConfig account, out string error)
    {
        try
        {
            account.DailySchedule = ReadDailyScheduleDraft().Normalize();
            if (account.DailySchedule.Enabled && (string.IsNullOrWhiteSpace(account.GameLoginAccount) || string.IsNullOrWhiteSpace(account.ProtectedGameLoginPassword)))
                throw new FormatException("请先在设备/角色中保存游戏登录账号和密码，再启用定时计划。");
            error = ""; return true;
        }
        catch (FormatException exception) { error = exception.Message; return false; }
    }

    private DailyGameScheduleSettings ReadDailyScheduleDraft() => new()
    {
        OnlineEnabled = scheduleOnlineCheckBox?.Checked == true,
        OnlineStart = scheduleStartTextBox?.Text ?? "08:00",
        OnlineEnd = scheduleEndTextBox?.Text ?? "22:00",
        ReloginEnabled = scheduleReloginCheckBox?.Checked == true,
        ReloginTimes = (scheduleReloginTimesTextBox?.Text ?? "").Split([',', '，', ';', '；', ' ', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
    };

    internal static string DescribeNextSchedule(DailyGameScheduleSettings draft, DateTimeOffset now)
    {
        if (!draft.Enabled) return "";
        try
        {
            var settings = draft.Normalize();
            var local = DailyGameSchedule.Local(now);
            DateTimeOffset NextOccurrence(string text)
            {
                var date = new DateTimeOffset(local.Date + DailyGameScheduleSettings.Parse(text).ToTimeSpan(), local.Offset);
                return date > local ? date : date.AddDays(1);
            }

            var events = new List<(DateTimeOffset At, string Action)>();
            if (settings.OnlineEnabled)
            {
                events.Add((NextOccurrence(settings.OnlineStart), "上线"));
                events.Add((NextOccurrence(settings.OnlineEnd), "下线"));
            }
            if (settings.ReloginEnabled)
                foreach (var time in settings.ReloginTimes.Select(NextOccurrence))
                    if (!settings.OnlineEnabled || DailyGameSchedule.Window(settings, time).Online)
                        events.Add((time, "重登"));
            if (events.Count == 0) return "";
            var next = events.OrderBy(item => item.At).First();
            return $"下一计划时间  {next.At:MM-dd HH:mm} {next.Action}";
        }
        catch (FormatException)
        {
            return "请检查计划时间，格式为 HH:mm。";
        }
    }
}
