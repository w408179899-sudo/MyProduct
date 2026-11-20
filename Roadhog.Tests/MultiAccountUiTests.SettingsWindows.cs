using Roadhog;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using System.Windows.Forms;

internal static partial class MultiAccountUiTests
{
    public static Task SettingsWindowsAllowIndependentMaintenanceAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("settings-maintenance");
        var accounts = new[] { test.Account(1), test.Account(2) };
        foreach (var account in accounts)
        {
            account.ProfileName = account.AccountName;
            account.ScriptSettings = new() { ProfileName = account.ProfileName };
        }
        test.Initialize(accounts);
        using var console = test.Console(accounts, renderWindow: true);
        var grid = Field<DataGridView>(console, "_grid");
        var windows = Field<Dictionary<string, AccountSettingsForm>>(console, "_settingsWindows");
        void OpenSettings(int row) => Invoke(console, "GridAction", null, new DataGridViewCellEventArgs(grid.Columns["设置"].Index, row));
        string OperationFile(AccountConfig account) => Path.Combine(test.Workspace.Processes.PathsFor(account).LogDirectory, "settings-operation");

        OpenSettings(0);
        var first = windows[accounts[0].InstanceId];
        first.Opacity = 0;
        Require(!first.Modal && first.Owner is null && console.Enabled, "a settings window does not block or cover the console as an owned dialog");
        var exchange = (Task)Invoke(first, "StartManastoneExchangeAsync")!;
        Until(() => File.Exists(OperationFile(accounts[0])), "first worker enters the mocked manastone exchange");
        var firstPid = test.View(accounts[0]).WorkerProcessId;

        OpenSettings(1);
        var second = windows[accounts[1].InstanceId];
        second.Opacity = 0;
        Require(console.Enabled && first.Enabled && second.Enabled && !second.Modal && !exchange.IsCompleted,
            "a running exchange leaves another account settings window interactive");
        OpenSettings(0);
        Require(windows.Count == 2 && ReferenceEquals(windows[accounts[0].InstanceId], first),
            "clicking the same account again reuses its open window");

        grid.Rows[1].Cells[0].Value = true;
        var confirmationShown = false;
        Pump((Task)Invoke(console, "DeleteSelectedAsync", (Func<string, bool>)(_ => { confirmationShown = true; return false; }))!);
        Require(!confirmationShown && test.Workspace.Processes.Snapshot().Count == 2, "an open settings window prevents deleting its stopped account");
        var saveArguments = new object?[] { null };
        Field<RoundedTextBox>(second, "profileNameTextBox").Text = "other-account-profile";
        Require((bool)Method(second, "SaveCurrentSettings").Invoke(second, saveArguments)!, "another account can save: " + saveArguments[0]);
        Require(test.View(accounts[1]).Config.ProfileName == "other-account-profile" &&
                test.View(accounts[0]).Config.ProfileName == accounts[0].AccountName && !exchange.IsCompleted,
            "saving the second account leaves the first configuration and operation alone");

        var upgrade = (Task)Invoke(second, "StartEquipmentUpgradeAsync")!;
        Until(() => File.Exists(OperationFile(accounts[1])), "second worker enters the mocked equipment upgrade");
        Require(File.ReadAllText(OperationFile(accounts[0])) == nameof(IRoadhogRuntime.RunManastoneExchangeAsync) &&
                File.ReadAllText(OperationFile(accounts[1])) == nameof(IRoadhogRuntime.RunEquipmentUpgradeBatchAsync) &&
                firstPid.HasValue && test.View(accounts[1]).WorkerProcessId != firstPid && !upgrade.IsCompleted && !exchange.IsCompleted,
            "both operations are isolated in separate account workers");

        second.Close();
        Pump(upgrade);
        Until(() => test.View(accounts[1]).WorkerProcessId is null, "closing the second window releases only its idle worker");
        Require(second.IsDisposed && !windows.ContainsKey(accounts[1].InstanceId) && !first.IsDisposed &&
                test.View(accounts[0]).WorkerProcessId == firstPid && !exchange.IsCompleted,
            "closing the second window cancels its operation without stopping the first account");

        first.Close();
        Pump(exchange);
        Until(() => windows.Count == 0 && test.View(accounts[0]).WorkerProcessId is null, "first idle worker is released on close");
    });

    public static Task SettingsWindowsRespectExitCancellationAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1) };
        test.Initialize(accounts);
        using var console = test.Console(accounts, renderWindow: true);
        var grid = Field<DataGridView>(console, "_grid");
        Invoke(console, "GridAction", null, new DataGridViewCellEventArgs(grid.Columns["设置"].Index, 0));
        var settings = Field<Dictionary<string, AccountSettingsForm>>(console, "_settingsWindows").Values.Single();
        settings.Opacity = 0;
        FormClosingEventHandler cancelClose = (_, args) => args.Cancel = true;
        settings.FormClosing += cancelClose;
        Pump((Task)Invoke(console, "ExitAsync")!);
        Require(console.Enabled && !console.IsDisposed && !settings.IsDisposed && !Field<bool>(console, "_exiting"),
            "a cancelled settings close leaves the console available instead of forcing shutdown");
        settings.FormClosing -= cancelClose;
        Pump((Task)Invoke(console, "ExitAsync")!);
        Require(settings.IsDisposed && console.IsDisposed, "explicit exit closes independent settings windows before the console");
    });
}
