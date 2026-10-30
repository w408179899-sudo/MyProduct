using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private RoundedCheckBox? scheduleOnlineCheckBox;
    private RoundedCheckBox? scheduleReloginCheckBox;
    private RoundedTextBox? scheduleStartTextBox;
    private RoundedTextBox? scheduleEndTextBox;
    private RoundedTextBox? scheduleReloginTimesTextBox;
    public event Action<AccountConfig>? AccountConfigurationSaved;

    private void BuildDailySchedule(Panel panel)
    {
        scheduleOnlineCheckBox = AddCheckBox(panel, "定时挂机", 12, 38, 140, false);
        scheduleOnlineCheckBox.Name = "scheduleOnlineCheckBox";
        AddLabel(panel, "每天上线", 170, 38, 80, 24);
        scheduleStartTextBox = AddTextBox(panel, "08:00", 252, 36, 100, 28);
        scheduleStartTextBox.Name = "scheduleStartTextBox";
        AddLabel(panel, "下线", 380, 38, 60, 24);
        scheduleEndTextBox = AddTextBox(panel, "22:00", 440, 36, 100, 28);
        scheduleEndTextBox.Name = "scheduleEndTextBox";
        AddLabel(panel, "其余时间下线，支持跨午夜", 564, 38, 250, 24);
        scheduleReloginCheckBox = AddCheckBox(panel, "定时重登", 12, 76, 140, false);
        scheduleReloginCheckBox.Name = "scheduleReloginCheckBox";
        AddLabel(panel, "每天时间", 170, 76, 80, 24);
        scheduleReloginTimesTextBox = AddTextBox(panel, "22:00", 252, 74, 288, 28);
        scheduleReloginTimesTextBox.Name = "scheduleReloginTimesTextBox";
        AddLabel(panel, "多个时间用逗号分隔，例如 10:00,18:00", 564, 76, 252, 24);
        AddLabel(panel, "北京时间（24小时制）；计划仅当前账号生效，主界面需保持运行。", 12, 112, 804, 24);
        AddLabel(panel, "手动停止后等待下次计划上线；两项都开启时，仅在挂机时段内重登。", 12, 140, 804, 24);
        scheduleOnlineCheckBox.Click += (_, _) => RefreshDailyScheduleControls();
        scheduleReloginCheckBox.Click += (_, _) => RefreshDailyScheduleControls();
        RefreshDailyScheduleControls();
    }

    private void RefreshDailyScheduleControls()
    {
        if (scheduleStartTextBox != null) scheduleStartTextBox.Enabled = scheduleOnlineCheckBox?.Checked == true;
        if (scheduleEndTextBox != null) scheduleEndTextBox.Enabled = scheduleOnlineCheckBox?.Checked == true;
        if (scheduleReloginTimesTextBox != null) scheduleReloginTimesTextBox.Enabled = scheduleReloginCheckBox?.Checked == true;
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
            account.DailySchedule = new DailyGameScheduleSettings
            {
                OnlineEnabled = scheduleOnlineCheckBox?.Checked == true,
                OnlineStart = scheduleStartTextBox?.Text ?? "08:00", OnlineEnd = scheduleEndTextBox?.Text ?? "22:00",
                ReloginEnabled = scheduleReloginCheckBox?.Checked == true,
                ReloginTimes = (scheduleReloginTimesTextBox?.Text ?? "").Split([',', '，', ';', '；', ' ', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            }.Normalize();
            if (account.DailySchedule.Enabled && (string.IsNullOrWhiteSpace(account.GameLoginAccount) || string.IsNullOrWhiteSpace(account.ProtectedGameLoginPassword)))
                throw new FormatException("请先在设备/角色中保存游戏登录账号和密码，再启用定时计划。");
            error = ""; return true;
        }
        catch (FormatException exception) { error = exception.Message; return false; }
    }
}
