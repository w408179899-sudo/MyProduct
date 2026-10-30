using Roadhog;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Shell;
using System.Windows.Forms;

internal static partial class MultiAccountUiTests
{
    public static Task DailyScheduleOverviewSaveAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1); account.ScriptSettings = new();
        account.GameLoginAccount = "schedule-test";
        account.ProtectedGameLoginPassword = Roadhog.Infrastructure.Login.LoginPasswordProtector.Protect("schedule-test-password");
        test.Initialize([account]);
        using var form = new AccountSettingsForm(account.AccountName, test.Workspace.Processes.RuntimeFor(account.InstanceId),
            test.Workspace.Accounts, test.Workspace.Paths, test.Workspace.Profiles, new WindowsFolderLauncher(),
            test.Workspace.Options.PathLibraryDirectory, account.CharacterName, test.Workspace.NameLists, test.Workspace.RadarMaps);
        var tabs = Field<TabControl>(form, "settingsTabs");
        var online = Field<RoundedCheckBox>(form, "scheduleOnlineCheckBox");
        Control? ancestor = online;
        while (ancestor is not null && ancestor is not TabPage) ancestor = ancestor.Parent;
        Require(ReferenceEquals(ancestor, tabs.TabPages[0]) && tabs.TabPages[0].Text == "总览", "schedule lives on the Overview page");
        online.Checked = true;
        Field<RoundedTextBox>(form, "scheduleStartTextBox").Text = "22:00";
        Field<RoundedTextBox>(form, "scheduleEndTextBox").Text = "08:00";
        Field<RoundedCheckBox>(form, "scheduleReloginCheckBox").Checked = true;
        Field<RoundedTextBox>(form, "scheduleReloginTimesTextBox").Text = "23:00，01:00,23:00";
        var notifications = 0;
        form.AccountConfigurationSaved += saved => { notifications++; Require(saved.DailySchedule.OnlineEnabled, "save notification carries account schedule"); };
        object?[] args = [null];
        Require((bool)Method(form, "SaveCurrentSettings").Invoke(form, args)!, "overview schedule save: " + args[0]);
        var saved = Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Single();
        Require(saved.DailySchedule.OnlineStart == "22:00" && saved.DailySchedule.OnlineEnd == "08:00" &&
            saved.DailySchedule.ReloginTimes.SequenceEqual(new[] { "01:00", "23:00" }), "overnight schedule and fixed times persist per account");
        Require(notifications == 1, "successful save refreshes the manager once");
        Field<RoundedTextBox>(form, "scheduleEndTextBox").Text = "25:00";
        Require(!(bool)Method(form, "SaveCurrentSettings").Invoke(form, args)! && notifications == 1, "invalid time cannot save or notify");
        Require(Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Single().DailySchedule.OnlineEnd == "08:00", "invalid edit preserves saved schedule");
        Field<RoundedTextBox>(form, "scheduleEndTextBox").Text = "08:00";
        var previewDirectory = Path.Combine(RepositoryRoot(), ".tmp", "daily-schedule-preview"); Directory.CreateDirectory(previewDirectory);
        Invoke(form, "RefreshDailyScheduleControls");
        form.Height = 1030; ShowHidden(form); form.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
        bitmap.Save(Path.Combine(previewDirectory, "overview.png"));
    });
}
