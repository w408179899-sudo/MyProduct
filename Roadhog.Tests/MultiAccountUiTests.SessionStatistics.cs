using System.Windows.Forms;

internal static partial class MultiAccountUiTests
{
    public static Task SessionStatisticsAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("session-statistics");
        var accounts = new[] { test.Account(1), test.Account(2), test.Account(3) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        foreach (var account in accounts.Take(2))
            Require(Pump(test.Workspace.Processes.StartAsync(account.InstanceId)).Success, "fixture starts");
        Until(() => test.View(accounts[0]).Worker?.Snapshot is { DeathCount: 1, KillCount: 4, LootCount: 3 } &&
            test.View(accounts[1]).Worker?.Snapshot is { DeathCount: 2, KillCount: 9, LootCount: 7 },
            "per-account counts arrive over real worker IPC");
        var grid = Field<DataGridView>(form, "_grid");
        grid.Rows[0].Cells[0].Value = true;
        Invoke(form, "RefreshRows");
        Require(grid.Columns[5].Name == "DeathCount" && grid.Columns[6].Name == "KillCount" &&
            grid.Columns[6].HeaderText == "累计击杀" && grid.Columns[7].HeaderText == "杀怪/h",
            "death and cumulative kills precede kill efficiency");
        Require(grid.Rows[0].Cells["DeathCount"].Value?.ToString() == "1" &&
            grid.Rows[0].Cells["KillCount"].Value?.ToString() == "4" &&
            grid.Rows[1].Cells["DeathCount"].Value?.ToString() == "2" &&
            grid.Rows[1].Cells["KillCount"].Value?.ToString() == "9", "UI displays each account's own totals");
        Require(grid.Rows[2].Cells["DeathCount"].Value?.ToString() == "—" &&
            grid.Rows[2].Cells["KillCount"].Value?.ToString() == "—", "unstarted account has no fabricated totals");
        Require(grid.Columns["DeathCount"].ReadOnly && grid.Columns["KillCount"].ReadOnly &&
            grid.Rows[0].Cells[0].Value is true, "statistics are read-only and preserve selection");
        form.PerformLayout();
        Require(TextRenderer.MeasureText(accounts[0].VmmDeviceName, grid.Font, System.Drawing.Size.Empty,
                TextFormatFlags.NoPadding).Width + 8 <= grid.Columns[3].Width &&
            TextRenderer.MeasureText("00:00:00", grid.Font, System.Drawing.Size.Empty,
                TextFormatFlags.NoPadding).Width + 8 <= grid.Columns[8].Width,
            "default window retains readable device identity and elapsed time after adding counts");
        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
            var output = Path.Combine(RepositoryRoot(), ".tmp", "multi-account-tests", "console-session-statistics.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); bitmap.Save(output);
        }
        var sibling = test.View(accounts[1]).WorkerProcessId;
        File.WriteAllText(Path.Combine(test.View(accounts[0]).LogDirectory, "skip-session-statistics"), "restart with no events");
        Require(Pump(test.Workspace.Processes.StopAsync(accounts[0].InstanceId)).Success, "one account stops");
        Invoke(form, "RefreshRows");
        Require(grid.Rows[0].Cells["KillCount"].Value?.ToString() == "—" &&
            grid.Rows[1].Cells["KillCount"].Value?.ToString() == "9", "stop clears only that row's current-session display");
        Require(Pump(test.Workspace.Processes.StartAsync(accounts[0].InstanceId)).Success, "one account restarts");
        Until(() => test.View(accounts[0]).Worker?.Snapshot is { DeathCount: 0, KillCount: 0 }, "new run resets totals");
        Invoke(form, "RefreshRows");
        Require(grid.Rows[0].Cells["DeathCount"].Value?.ToString() == "0" &&
            grid.Rows[0].Cells["KillCount"].Value?.ToString() == "0" &&
            test.View(accounts[1]).WorkerProcessId == sibling &&
            grid.Rows[1].Cells["KillCount"].Value?.ToString() == "9", "restart resets one row without touching its sibling");
    });
}
