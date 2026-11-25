using System.Windows.Forms;
using Roadhog;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class MultiAccountUiTests
{
    public static Task ConsoleToolbarFitsFixedWidthAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        var root = Field<TableLayoutPanel>(form, "_layout");
        var toolbar = root.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Name == "accountToolbar");
        var initialHeight = form.Height;
        var fixedWidth = form.Width;
        Require(!form.MaximizeBox, "the console does not expose maximize");
        foreach (var width in new[] { fixedWidth - 200, fixedWidth + 200 })
        {
            form.Width = width;
            Require(form.Width == fixedWidth, "the console rejects horizontal resizing in both directions");
        }
        form.Height = initialHeight + 80;
        Require(form.Height == initialHeight + 80 && form.Width == fixedWidth, "the console can grow vertically");
        form.Height = initialHeight;
        Require(form.Height == initialHeight && form.Width == fixedWidth, "the console can shrink vertically");

        form.PerformLayout(); toolbar.PerformLayout(); Application.DoEvents();
        var buttons = toolbar.Controls.OfType<Button>().ToArray();
        Require(buttons.All(button => button.Text != "战斗版本") && buttons.Last().Text == "退出程序",
            "the retired version control is gone and exit is the final toolbar action");
        Require(buttons.Any(button => button.Text == "添加账号") && buttons.All(button => button.Text != "导入旧配置"),
            "accounts can be added manually without a legacy import action");
        Require(buttons.All(button => button.Left >= 0 && button.Right <= toolbar.ClientSize.Width &&
            button.Top >= 0 && button.Bottom <= toolbar.ClientSize.Height), "all toolbar actions fit at the fixed width");
        Require(root.GetRowHeights()[0] >= toolbar.Controls.Cast<Control>().Max(control => control.Bottom),
            "toolbar receives sufficient row height");
        var exit = buttons.Single(button => button.Text == "退出程序");
        var cleanup = buttons.Single(button => button.Text == "清包");
        var transfer = buttons.Single(button => button.Text == "倒货");
        Require(cleanup.Top == exit.Top && transfer.Top == exit.Top && transfer.Left > cleanup.Right && exit.Left > transfer.Right,
            "cleanup, transfer and exit fit on the same row in that order");

        var globalButton = (Button)form.Controls.Find("globalSettingsButton", true).Single();
        var filters = (TableLayoutPanel)globalButton.Parent!;
        Require(root.GetRow(filters) == 1 && filters.GetColumn(globalButton) == 1 &&
            globalButton.Right <= filters.ClientSize.Width && globalButton.Bottom <= filters.ClientSize.Height,
            "global settings fits at the right of the second row");
        Require(form.Controls.Find("dmaReadCacheInput", true).Length == 0, "the cache field moved into global settings");

        var workerOptions = RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec
        {
            Account = accounts.Single(),
            Paths = test.Workspace.Processes.PathsFor(accounts.Single())
        });
        var workerSettings = new JsonGlobalSettingsStore(workerOptions.AccountConfigPath);
        var logger = new InMemoryRoadhogLogger();
        workerSettings.Refresh(logger);
        Require(workerSettings.Current == new RoadhogGlobalSettings(), "a worker with no config starts with the defaults");

        var settingsPath = Path.Combine(Path.GetDirectoryName(workerOptions.AccountConfigPath)!, "dma-read-cache.json");
        File.WriteAllText(settingsPath, """{"readCacheMs":200}""");
        var store = test.Workspace.GlobalSettings;
        var legacy = new RoadhogGlobalSettings { ReadCacheMs = 200 };
        Require(store.Load() == legacy, "the legacy cache value is preserved and missing settings use the defaults");
        var saved = new RoadhogGlobalSettings
        {
            ReadCacheMs = 100, NoKillReturnMinutes = 35, ReviveDelaySeconds = 12, LogRetentionHours = 72, LogDirectoryMiB = 128
        };

        OpenGlobalSettings(globalButton, dialog =>
        {
            var cache = SettingControl<TextBox>(dialog, "dmaReadCacheInput");
            var noKill = SettingControl<TextBox>(dialog, "noKillReturnInput");
            var revive = SettingControl<TextBox>(dialog, "reviveDelayInput");
            var hours = SettingControl<TextBox>(dialog, "logRetentionInput");
            var size = SettingControl<TextBox>(dialog, "logDirectoryLimitInput");
            var save = SettingControl<Button>(dialog, "saveGlobalSettings");
            Require(cache.Text == "200" && noKill.Text == "" && revive.Text == "" && hours.Text == "24" && size.Text == "64",
                "the dialog displays the legacy cache and the default settings");
            cache.Text = "125";
            save.PerformClick();
            Require(dialog.Visible && SettingControl<Label>(dialog, "globalSettingsStatus").Text.Length > 0 && store.Load() == legacy,
                "invalid cache values are rejected without saving");
            cache.Text = "100";
            noKill.Text = "35";
            revive.Text = "12";
            hours.Text = "72";
            size.Text = "128";
            save.PerformClick();
            Until(() => dialog.DialogResult == DialogResult.OK || (save.Enabled &&
                SettingControl<Label>(dialog, "globalSettingsStatus").Text.Length > 0), "saving valid global settings completes");
            Require(dialog.DialogResult == DialogResult.OK, SettingControl<Label>(dialog, "globalSettingsStatus").Text);
        });
        Require(store.Current == saved && store.Load() == saved, "saving publishes a complete shared configuration");
        Until(() =>
        {
            workerSettings.Refresh(logger);
            return workerSettings.Current == saved;
        }, "a separate worker settings instance reloads all saved values");

        OpenGlobalSettings(globalButton, dialog =>
        {
            var cache = SettingControl<TextBox>(dialog, "dmaReadCacheInput");
            Require(cache.Text == "100" && SettingControl<TextBox>(dialog, "noKillReturnInput").Text == "35",
                "reopening the dialog displays the saved settings");
            cache.Text = "150";
            dialog.CancelButton!.PerformClick();
        });
        Require(store.Load() == saved, "cancel discards changes");

        File.WriteAllText(settingsPath, """{"readCacheMs":50,"logRetentionHours":0}""");
        Until(() =>
        {
            workerSettings.Refresh(logger);
            return logger.Entries.Any(entry => entry.EventName == "global_settings.load_failed");
        }, "the worker reports an invalid replacement");
        Require(workerSettings.Current == saved, "an invalid replacement leaves the last valid settings active");
    });

    private static T SettingControl<T>(Form dialog, string name) where T : Control =>
        (T)dialog.Controls.Find(name, true).Single();

    private static void OpenGlobalSettings(Button entry, Action<GlobalSettingsForm> verify)
    {
        var owner = entry.FindForm()!;
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dialog = owner.OwnedForms.OfType<GlobalSettingsForm>().SingleOrDefault();
            try { verify(dialog ?? throw new InvalidOperationException("global settings dialog did not open")); }
            catch (Exception ex) { failure = ex; }
            finally { if (dialog is { DialogResult: DialogResult.None }) dialog.Close(); }
        };
        timer.Start();
        entry.PerformClick();
        Require(!timer.Enabled, "the toolbar button opens the modal settings dialog");
        if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
    }
}
