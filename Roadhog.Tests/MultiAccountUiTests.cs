using System.Diagnostics;
using System.Reflection;
using Roadhog;
using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Shell;
using Roadhog.Infrastructure.WorkerProcesses;
using System.Windows.Forms;

internal static class MultiAccountUiTests
{
    public static Task DeleteSelectedAccountsAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1), test.Account(2), test.Account(3) };
        test.Initialize(accounts);
        using var form = test.Console(accounts);
        var grid = Field<DataGridView>(form, "_grid");
        var prompts = 0;
        bool Confirm(string text) { prompts++; Require(text.Contains(accounts[0].AccountName) && text.Contains("授权文件将保留"), "confirmation names accounts and retained files"); return true; }
        void Delete(Func<string, bool> confirm) => Pump((Task)Invoke(form, "DeleteSelectedAsync", confirm)!);
        Delete(Confirm);
        Require(prompts == 0 && grid.Rows.Count == 3, "highlight alone cannot delete accounts");
        grid.Rows[0].Cells[0].Value = true;
        Delete(_ => false);
        Require(grid.Rows.Count == 3 && Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Count == 3, "cancel leaves disk and UI unchanged");
        Require(Pump(test.Workspace.Processes.StartAsync(accounts[0].InstanceId)).Success, "selected account starts");
        Delete(Confirm);
        Require(prompts == 0 && grid.Rows.Count == 3, "running account prevents deletion before confirmation");
        Require(Pump(test.Workspace.Processes.StopAsync(accounts[0].InstanceId)).Success, "selected account stops");
        // A start while the confirmation is open must still be rejected by the save guard.
        var rejected = false;
        try { Delete(_ => { Require(Pump(test.Workspace.Processes.StartAsync(accounts[0].InstanceId)).Success, "start during confirmation"); return true; }); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Count == 3, "late start cannot delete persisted account");
        Pump(test.Workspace.Processes.StopAsync(accounts[0].InstanceId));
        var retained = Path.Combine(test.Workspace.Processes.PathsFor(accounts[0]).LogDirectory, "retained.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(retained)!); File.WriteAllText(retained, "keep");
        var latest = accounts.Select(account => account.Clone()).ToArray();
        latest[1].CharacterName = "最新角色名";
        Pump(test.Workspace.Accounts.SaveAllAsync(latest));
        Delete(Confirm);
        var saved = Pump(test.Workspace.Accounts.LoadAllAsync()).Value!;
        Require(prompts == 1 && grid.Rows.Count == 2 && saved.Count == 2, "confirmed deletion updates disk manager and UI");
        Require(saved.Single(account => account.InstanceId == accounts[1].InstanceId).CharacterName == "最新角色名", "surviving latest settings are retained");
        Require(File.ReadAllText(retained) == "keep", "deletion preserves account files");
        foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = true;
        Delete(_ => true);
        Require(grid.Rows.Count == 0 && Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Count == 0, "deleting remaining accounts supports empty list");
    });

    public static Task ConsoleSelectAllHeaderAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1), test.Account(2), test.Account(3) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        var grid = Field<DataGridView>(form, "_grid");
        var header = (CheckBox)grid.Controls["accountSelectAllCheckBox"]!;
        void ClickHeader() => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(header, new object[] { EventArgs.Empty });
        Require(header.Enabled && header.CheckState == CheckState.Unchecked, "initial header is unchecked");
        Require(grid.GetCellDisplayRectangle(0, -1, true).Contains(header.Bounds), "checkbox is contained in first header cell");
        ClickHeader();
        Require(grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells[0].Value is true) && header.Checked, "header checks all listed accounts");
        Invoke(form, "RefreshRows");
        Require(header.CheckState == CheckState.Checked, "timer refresh preserves checked header");
        grid.Rows[1].Cells[0].Value = false;
        Require(header.CheckState == CheckState.Indeterminate, "individual uncheck shows partial state");
        ClickHeader();
        Require(header.CheckState == CheckState.Checked, "partial state click selects all");
        ClickHeader();
        Require(grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells[0].Value is false), "checked header click clears all");
        var search = Field<Control>(form, "_search");
        search.Text = accounts[1].AccountName;
        Require(grid.Rows.Count == 1, "search reduces visible account list");
        ClickHeader();
        search.Text = "";
        Require(grid.Rows.Cast<DataGridViewRow>().Count(row => row.Cells[0].Value is true) == 1 &&
            grid.Rows.Cast<DataGridViewRow>().Single(row => row.Cells[0].Value is true).Tag as string == accounts[1].InstanceId,
            "filtered select all does not select hidden accounts");
        search.Text = "no-such-account";
        Require(!header.Enabled && header.CheckState == CheckState.Unchecked, "empty list disables and clears header");
        search.Text = "";
        foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = true;
        Require(header.CheckState == CheckState.Checked, "checking each row updates header to all selected");
        form.Width += 150;
        Application.DoEvents();
        Require(grid.GetCellDisplayRectangle(0, -1, true).Contains(header.Bounds), "resized header remains centered in column");
        var filter = Field<RoundedComboBox>(form, "_filter");
        filter.SelectedIndex = 1;
        Require(grid.Rows.Count == 0 && !header.Enabled, "rounded running filter updates the list");
        filter.SelectedIndex = 2;
        Require(grid.Rows.Count == 3 && header.Enabled, "rounded stopped filter restores stopped accounts");
        filter.SelectedIndex = 0;
        search.Controls.OfType<TextBox>().Single().Text = accounts[0].AccountName;
        Require(grid.Rows.Count == 1 && (string)grid.Rows[0].Tag! == accounts[0].InstanceId, "typing in rounded search updates account list");
    });

    public static Task ConsolePlayerInfoAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("player-info");
        var accounts = new[] { test.Account(1), test.Account(2), test.Account(3) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        foreach (var account in accounts.Take(2))
            Require(Pump(test.Workspace.Processes.StartAsync(account.InstanceId)).Success, "fixture worker starts");
        Until(() => test.View(accounts[0]).Worker?.Snapshot?.CharacterLevel == 50 &&
            test.View(accounts[1]).Worker?.Snapshot?.CharacterLevel == 32, "independent worker metadata arrives over IPC");
        var grid = Field<DataGridView>(form, "_grid");
        grid.Rows[0].Cells[0].Value = true;
        Invoke(form, "RefreshRows");
        Require(grid.Rows[0].Cells[1].Value?.ToString() == "账号 1 / 守望者\n50级 · 精灵星", "first account shows localized level and class");
        Require(grid.Rows[1].Cells[1].Value?.ToString() == "账号 2 / 晨光\n32级 · 守护星", "second account owns its metadata");
        Require(grid.Rows[2].Cells[1].Value?.ToString() == "账号 3 / 青岚", "unread account invents no level or class");
        Require(grid.Rows[0].Cells[0].Value is true && grid.Columns.Count == 12 &&
            grid.Columns[1].DefaultCellStyle.WrapMode == DataGridViewTriState.True, "metadata refresh preserves selection and existing action layout");
        Require(grid.Columns[2].HeaderText == "进程 PID" &&
            grid.Rows[0].Cells[2].Value?.ToString() == test.View(accounts[0]).WorkerProcessId?.ToString() &&
            grid.Rows[1].Cells[2].Value?.ToString() == test.View(accounts[1]).WorkerProcessId?.ToString() &&
            grid.Rows[2].Cells[2].Value?.ToString() == "—",
            "PID column identifies each account worker and leaves stopped accounts blank");
        Require(Field<Label>(form, "_summary").Text.Contains("主界面 PID：" + Environment.ProcessId),
            "main page shows its own process ID");
        form.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
        var output = Path.Combine(RepositoryRoot(), ".tmp", "multi-account-tests", "console-player-info.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); bitmap.Save(output);
        Require(Pump(test.Workspace.Processes.StopAsync(accounts[0].InstanceId)).Success, "first account stops");
        Invoke(form, "RefreshRows");
        Require(!grid.Rows[0].Cells[1].Value!.ToString()!.Contains("50级") &&
            grid.Rows[1].Cells[1].Value!.ToString()!.Contains("32级 · 守护星"), "stop removes stale data without affecting sibling display");
        Require(grid.Rows[0].Cells[2].Value?.ToString() == "—" &&
            grid.Rows[1].Cells[2].Value?.ToString() == test.View(accounts[1]).WorkerProcessId?.ToString(),
            "stopping one worker clears only its PID");
    });

    public static Task ConsoleRowsAndSelectionAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1), test.Account(2), test.Account(3) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        var grid = Field<DataGridView>(form, "_grid");
        Require(grid.Rows.Count == 3 && grid.Columns.Count == 12, "console has account rows and five action columns");
        Require(new[] { "清包", "设置", "硬件", "启动", "停止" }.SequenceEqual(grid.Columns.Cast<DataGridViewColumn>().Skip(7).Select(c => c.Name)), "console actions are available per account");
        grid.Rows[0].Cells[0].Value = true;
        grid.ClearSelection(); grid.Rows[1].Selected = true;
        Pump((Task)Invoke(form, "BatchAsync", true)!);
        Until(() => test.View(accounts[0]).Worker?.IsRunning == true, "checked account starts");
        Require(test.View(accounts[1]).WorkerProcessId is null && test.View(accounts[2]).WorkerProcessId is null, "selected but unchecked account does not start");
        Invoke(form, "RefreshRows");
        Require(grid.Rows[0].Cells[4].Value?.ToString() == "运行中" && grid.Rows[1].Cells[4].Value?.ToString() == "已停止", "running and stopped rows display independently");
        Require(grid.Rows[0].Cells[0].Value is true && (string?)Invoke(form, "SelectedId") == accounts[1].InstanceId, "refresh preserves checks and current detail row separately");
        var search = Field<Control>(form, "_search");
        search.Text = accounts[2].CharacterName;
        Require(grid.Rows.Count == 1 && (string)grid.Rows[0].Tag! == accounts[2].InstanceId, "role-name search filters accounts");
        search.Text = string.Empty;
        // Screenshot contains real WinForms controls rendered from simulated account data.
        Invoke(form, "RefreshRows");
        form.CreateControl(); form.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
        var output = Path.Combine(RepositoryRoot(), ".tmp", "multi-account-tests", "console.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); bitmap.Save(output);
        foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = (string)row.Tag! == accounts[0].InstanceId;
        Pump((Task)Invoke(form, "BatchAsync", false)!);
        Require(test.View(accounts[0]) is { State: "stopped", WorkerProcessId: null }, "batch stop affects checked account");
        test.HideTray(form);
    });

    public static Task ConsoleStopDuringStartAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("slow-start");
        var account = test.Account(1); test.Initialize(new[] { account });
        using var form = test.Console(new[] { account });
        Invoke(form, "GridAction", null, new DataGridViewCellEventArgs(10, 0));
        var entered = Path.Combine(test.Workspace.Processes.PathsFor(account).LogDirectory, "start-entered");
        Until(() => File.Exists(entered), "slow account start entered backend");
        Invoke(form, "GridAction", null, new DataGridViewCellEventArgs(11, 0));
        Until(() => test.View(account) is { DesiredRunning: false, WorkerProcessId: null, State: "stopped" }, "same-row stop cancels pending start", 6000);
        Until(() => Field<HashSet<string>>(form, "_busy").Count == 0, "start handler completes after cancellation");
        System.Windows.Forms.Application.DoEvents();
        test.HideTray(form);
    });

    public static Task HardwareEditingReleasesIdleWorkerAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("idle-start");
        var accounts = new[] { test.Account(1), test.Account(2) }; test.Initialize(accounts);
        using var form = test.Console(accounts);
        Require(Pump(test.Workspace.Processes.StartAsync(accounts[1].InstanceId)).Success, "healthy sibling starts");
        var healthyPid = test.View(accounts[1]).WorkerProcessId;
        Require(Pump(test.Workspace.Processes.StartAsync(accounts[0].InstanceId)).Success, "idle recovery fixture starts");
        Require(test.View(accounts[0]) is { DesiredRunning: true, WorkerProcessId: not null, Worker.IsRunning: false }, "fixture holds idle worker and retry intent");
        Require(Pump((Task<bool>)Invoke(form, "PrepareHardwareEditingAsync", accounts[0].InstanceId)!), "device editor can prepare a failed or idle account without a blocking dialog");
        Require(test.View(accounts[0]) is { DesiredRunning: false, WorkerProcessId: null, State: "stopped" }, "editing clears intent and releases worker first");
        Require(test.View(accounts[1]).WorkerProcessId == healthyPid && test.View(accounts[1]).Worker?.IsRunning == true, "editing does not restart or stop another account");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 1, "only healthy sibling retains a lease");
        Require(Pump((Task<bool>)Invoke(form, "PrepareHardwareEditingAsync", accounts[0].InstanceId)!), "repeated preparation remains idempotent");
    });

    public static Task HardwareIndexSwapSaveAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var first = test.Account(1); var second = test.Account(2);
        test.Initialize(new[] { first, second });
        var changedFirst = first.Clone(); changedFirst.VmmDeviceName = second.VmmDeviceName;
        Pump(test.Workspace.SaveAccountsAsync(new[] { changedFirst, second }));
        var intermediate = Pump(test.Workspace.Accounts.LoadAllAsync()).Value!;
        Require(intermediate.Single(a => a.InstanceId == second.InstanceId).VmmDeviceName == second.VmmDeviceName,
            "saving the first corrected index never silently edits the other stopped account");
        var changedSecond = second.Clone(); changedSecond.VmmDeviceName = first.VmmDeviceName;
        Pump(test.Workspace.SaveAccountsAsync(new[] { changedFirst, changedSecond }));
        var swapped = Pump(test.Workspace.Accounts.LoadAllAsync()).Value!;
        Require(swapped.Single(a => a.InstanceId == first.InstanceId).VmmDeviceName == second.VmmDeviceName
            && swapped.Single(a => a.InstanceId == second.InstanceId).VmmDeviceName == first.VmmDeviceName,
            "two stopped accounts can save swapped driver indices sequentially after reboot");
        Require(Pump(test.Workspace.Processes.StartAsync(second.InstanceId)).Success, "sibling starts on its corrected index");
        var runningPid = test.View(second).WorkerProcessId;
        var conflict = changedFirst.Clone(); conflict.VmmDeviceName = changedSecond.VmmDeviceName;
        try { Pump(test.Workspace.SaveAccountsAsync(new[] { conflict, changedSecond })); throw new InvalidOperationException("expected active-index rejection"); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("读取编号正被", StringComparison.Ordinal)) { }
        Require(test.View(second).WorkerProcessId == runningPid && test.View(second).Worker?.IsRunning == true, "failed save leaves running account untouched");
        Require(Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Single(a => a.InstanceId == first.InstanceId).VmmDeviceName == changedFirst.VmmDeviceName,
            "rejected active-index collision leaves the previous saved file intact");
    });

    public static Task HardwareEditorMappingAndRefreshAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1); account.VmmDeviceName = "fpga://devindex=4";
        account.HardwareDeviceInstanceId = "mock-device-instance";
        var device = Device(account) with { VmmDeviceName = "fpga://devindex=0" };
        var devices = SavedHardwareBindingPolicy.ForEditor(new[] { device, Device(test.Account(2)) }, new[] { account });
        using var form = new AccountHardwareForm(account, devices, (draft, _) =>
            Task.FromResult(new HardwareVerification("新读取角色", draft.HardwareKey, draft.VmmDeviceName, true, HardwareVerificationSession.CurrentId)));
        var combo = Field<ComboBox>(form, "_device"); combo.SelectedIndex = 1; combo.SelectedIndex = 0;
        Require(Field<ComboBox>(form, "_vmm").Text == account.VmmDeviceName, "reselecting physical device retains saved driver mapping");
        Pump((Task)Invoke(form, "VerifyAsync")!); Field<CheckBox>(form, "_confirm").Checked = true;
        Invoke(form, "Save");
        Require(form.Config.CharacterName == "新读取角色", "a confirmed fresh read updates the role even when hardware is unchanged");
        using var changed = new AccountHardwareForm(account, new[] { device with { DeviceInstanceId = "replacement-device" } }, (_, _) => throw new InvalidOperationException());
        Require(!Field<Button>(changed, "_save").Enabled, "replacement physical identity requires fresh verification even on same port and VMM");
    });

    public static Task HardwareOccupiedChoicesAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = Enumerable.Range(0, 6).Select(index =>
        {
            var item = test.Account(1); item.AccountName = "账号 " + index;
            item.HardwareKey = "mock-dma-" + index; item.VmmDeviceName = "fpga://devindex=" + index;
            return item;
        }).ToArray();
        var devices = accounts.Select(a => Device(a) with { DeviceInstanceId = "instance-" + a.InstanceId }).ToArray();
        var account = accounts[0]; account.HardwareDeviceInstanceId = devices[0].DeviceInstanceId;
        AccountProcessView View(int index, bool desired, int? pid) => new(accounts[index], "stopped", null, null, desired, pid, "");
        IReadOnlyList<AccountProcessView> views = new[] { View(0, false, null), View(1, true, null), View(2, false, 202), View(4, false, null) };
        IReadOnlyList<DeviceLease> leases = new[] { new DeviceLease(303, DateTimeOffset.UtcNow, "external", accounts[3].HardwareKey, accounts[3].VmmDeviceName, DateTimeOffset.UtcNow) };
        HardwareSelectionAvailability Available() => new(account.InstanceId, views, leases);
        var reads = 0;
        using var form = new AccountHardwareForm(account, devices, (draft, _) =>
        {
            reads++;
            return Task.FromResult(new HardwareVerification("Tone", draft.HardwareKey, draft.VmmDeviceName, true, HardwareVerificationSession.CurrentId));
        }, refreshDevices: () => devices, availability: Available);
        var dma = Field<ComboBox>(form, "_device"); var vmm = Field<ComboBox>(form, "_vmm");
        Require(dma.Items.Count == 3 && vmm.Items.Count == 3, "starting, live idle worker and external lease are hidden in both lists");
        Require(vmm.Items.Cast<object>().Select(x => x.ToString()!).Any(s => s == "fpga://devindex=5"), "filtering preserves original high index instead of renumbering");
        Require(dma.Items.Cast<object>().Any(x => x.ToString()!.Contains("已绑定：" + accounts[4].AccountName)) &&
            vmm.Items.Cast<object>().Any(x => x.ToString()!.Contains("已绑定：" + accounts[4].AccountName)), "stopped account binding is labeled in both lists");
        vmm.Text = accounts[1].VmmDeviceName;
        Pump((Task)Invoke(form, "VerifyAsync")!);
        Require(reads == 0 && Field<Label>(form, "_result").Text.Contains("占用"), "typing a hidden occupied index cannot start hardware verification");
        vmm.SelectedIndex = 1;
        Pump((Task)Invoke(form, "VerifyAsync")!);
        Require(reads == 0 && Field<Label>(form, "_result").Text.Contains("请先调整"), "stopped binding gives actionable conflict before connecting");
        vmm.Text = accounts[0].VmmDeviceName;
        Pump((Task)Invoke(form, "VerifyAsync")!); Field<CheckBox>(form, "_confirm").Checked = true;
        Require(reads == 1, "own stopped binding remains available");
        leases = leases.Append(new DeviceLease(404, DateTimeOffset.UtcNow, "late", account.HardwareKey, account.VmmDeviceName, DateTimeOffset.UtcNow)).ToArray();
        Invoke(form, "Save");
        Require(form.DialogResult != DialogResult.OK && Field<Label>(form, "_result").Text.Contains("占用"), "new occupation after proof blocks save");
        Invoke(form, "RefreshDevices");
        Require(dma.SelectedIndex == -1 && vmm.Text == "" && !Field<CheckBox>(form, "_confirm").Checked, "refresh clears occupied selections and old proof");
        views = Array.Empty<AccountProcessView>(); leases = Array.Empty<DeviceLease>(); Invoke(form, "RefreshDevices");
        Require(dma.Items.Count == 6 && vmm.Items.Count == 6, "released devices and original indices return");
        leases = accounts.Select((a, i) => new DeviceLease(500 + i, DateTimeOffset.UtcNow, "all", a.HardwareKey, a.VmmDeviceName, DateTimeOffset.UtcNow)).ToArray();
        Invoke(form, "RefreshDevices");
        Require(dma.Items.Count == 0 && vmm.Items.Count == 0 && Field<Label>(form, "_result").Text.Contains("暂无空闲"), "all occupied shows an explicit empty state");
    });

    public static Task HardwareCustomerRebindingAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1); account.HardwareDeviceInstanceId = "old-instance";
        var old = Device(account) with { DeviceInstanceId = "old-instance" };
        var replacement = old with { DeviceInstanceId = "new-instance" };
        IReadOnlyList<HardwareDeviceFeature> online = new[] { replacement };
        AccountConfig? verified = null;
        using var form = new AccountHardwareForm(account, new[] { old }, (draft, _) =>
        {
            verified = draft.Clone();
            return Task.FromResult(new HardwareVerification("Tone", draft.HardwareKey, draft.VmmDeviceName, true, HardwareVerificationSession.CurrentId));
        }, refreshDevices: () => online);
        Invoke(form, "RefreshDevices");
        Require(!Field<Button>(form, "_save").Enabled, "refresh replacement requires verification");
        Field<ComboBox>(form, "_vmm").Text = "fpga://devindex=4";
        Pump((Task)Invoke(form, "VerifyAsync")!);
        Require(verified!.HardwareDeviceInstanceId == "new-instance" && verified.ProcessId == 0 && verified.VmmDeviceName == "fpga://devindex=4", "verification uses refreshed identity and customer selected index without old PID");
        Require(!Field<Button>(form, "_save").Enabled, "new role still requires customer confirmation");
        Field<CheckBox>(form, "_confirm").Checked = true;
        Invoke(form, "RefreshDevices");
        Require(!Field<CheckBox>(form, "_confirm").Checked && !Field<Button>(form, "_save").Enabled, "refresh invalidates even a completed proof");
        online = Array.Empty<HardwareDeviceFeature>(); Invoke(form, "RefreshDevices");
        Require(Field<ComboBox>(form, "_device").SelectedIndex == -1 && !Field<Button>(form, "_save").Enabled, "disconnected device cannot be saved");
        online = new[] { replacement }; Invoke(form, "RefreshDevices");
        Pump((Task)Invoke(form, "VerifyAsync")!); Field<CheckBox>(form, "_confirm").Checked = true; Invoke(form, "Save");
        Require(form.Config.HardwareDeviceInstanceId == "new-instance" && form.Config.CharacterName == "Tone" && form.Config.VmmDeviceName == "fpga://devindex=4", "confirmed save replaces stale binding with tested customer selection");
        Require(account.HardwareDeviceInstanceId == "old-instance", "editing leaves original untouched until accepted");
    });

    public static Task HardwareRebootRequiresReadConfirmSaveAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1); account.HardwareDeviceInstanceId = "mock-device-instance";
        account.HardwareVerificationSessionId = "previous-boot";
        using var form = new AccountHardwareForm(account, new[] { Device(account) }, (draft, _) =>
            Task.FromResult(new HardwareVerification(draft.CharacterName, draft.HardwareKey, draft.VmmDeviceName, true, HardwareVerificationSession.CurrentId)));
        Require(!Field<Button>(form, "_save").Enabled, "unchanged hardware and known role cannot skip reboot verification");
        Pump((Task)Invoke(form, "VerifyAsync")!);
        Require(!Field<Button>(form, "_save").Enabled && form.Config.HardwareVerificationSessionId == "previous-boot", "reading does not automatically confirm or save");
        Field<CheckBox>(form, "_confirm").Checked = true; Invoke(form, "Save");
        Require(HardwareVerificationSession.IsCurrent(form.Config) && account.HardwareVerificationSessionId == "previous-boot", "confirmed save stamps this boot only on the accepted draft");
        var copy = System.Text.Json.JsonSerializer.Deserialize<AccountConfig>(System.Text.Json.JsonSerializer.Serialize(form.Config.Clone()))!;
        Require(HardwareVerificationSession.IsCurrent(copy), "boot confirmation survives clone and JSON persistence");
    });

    public static Task HardwareVerificationAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1); account.CharacterName = string.Empty;
        using var form = new AccountHardwareForm(account, new[] { Device(account) }, (draft, _) =>
            Task.FromResult(new HardwareVerification("验证角色", draft.HardwareKey, draft.VmmDeviceName, true, HardwareVerificationSession.CurrentId)));
        var save = Field<Button>(form, "_save"); var confirm = Field<CheckBox>(form, "_confirm");
        Require(!save.Enabled && !confirm.Enabled, "new hardware cannot be saved without a verified role");
        Pump((Task)Invoke(form, "VerifyAsync")!);
        Require(confirm.Enabled && !save.Enabled, "verification requires explicit confirmation before save");
        confirm.Checked = true; Require(save.Enabled, "confirmation enables save");
        Field<TextBox>(form, "_ip").Text = "127.0.0.22";
        Require(!confirm.Enabled && !confirm.Checked && !save.Enabled, "changing an endpoint invalidates proof and confirmation");
        Pump((Task)Invoke(form, "VerifyAsync")!); confirm.Checked = true;
        Invoke(form, "Save");
        Require(form.Config.CharacterName == "验证角色" && form.Config.KmBox!.IpAddress == "127.0.0.22", "only newly verified configuration is saved");
        Require(account.KmBox!.IpAddress == "127.0.0.1" && account.CharacterName.Length == 0, "dialog does not mutate original account before acceptance");
    });

    public static Task HardwareVerificationCancellationAsync() => Sta(() =>
    {
        using var test = new UiEnvironment("stuck-init");
        var account = test.Account(1); test.Initialize(new[] { account });
        using var form = new AccountHardwareForm(account, new[] { Device(account) },
            (draft, cancellationToken) => test.Workspace.Processes.VerifyHardwareAsync(draft, cancellationToken));
        ShowHidden(form);
        var verifying = (Task)Invoke(form, "VerifyAsync")!;
        var entered = Path.Combine(test.Workspace.Processes.PathsFor(account).LogDirectory, "init-entered");
        Until(() => File.Exists(entered), "verification created an isolated mock worker");
        form.Close();
        Require(!form.IsDisposed, "dialog remains open while pending worker cancellation cleans up");
        Pump(verifying);
        Until(() => form.IsDisposed, "cancelled validation finishes closing the dialog");
        Require(test.View(account).WorkerProcessId is null && !test.View(account).DesiredRunning, "cancelled verification leaves no worker or restart intent");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 0, "cancelled verification releases its hardware lease");
        foreach (var outcome in new[] { "cancelled", "failed", "late-success" })
        {
            var delayed = new TaskCompletionSource<HardwareVerification>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken verificationToken = default;
            using var shutdownForm = new AccountHardwareForm(account, new[] { Device(account) }, (_, token) =>
            { verificationToken = token; return delayed.Task; });
            ShowHidden(shutdownForm);
            var shutdownVerification = (Task)Invoke(shutdownForm, "VerifyAsync")!;
            var closing = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
            typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shutdownForm, new object[] { closing });
            Require(!closing.Cancel && verificationToken.IsCancellationRequested,
                "busy hardware verification cancels its work without vetoing Windows shutdown");
            var status = Field<Label>(shutdownForm, "_result");
            shutdownForm.Dispose();
            var statusWhenClosed = status.Text;
            if (outcome == "cancelled") delayed.SetCanceled(verificationToken);
            else if (outcome == "failed") delayed.SetException(new InvalidOperationException("fixture late verification failure"));
            else delayed.SetResult(new HardwareVerification("late role", account.HardwareKey, account.VmmDeviceName, true, HardwareVerificationSession.CurrentId));
            Pump(shutdownVerification);
            Require(status.Text == statusWhenClosed && Field<object?>(shutdownForm, "_verification") is null,
                "late verification completion must neither access closed controls nor publish hardware proof");
            Require(!Field<bool>(shutdownForm, "_busy") && Field<CancellationTokenSource?>(shutdownForm, "_operation") is null,
                "verification cleanup completes after the system has disposed its window");
        }
    });

    public static Task SettingsSaveWithoutAuthorizationAsync() => Sta(() =>
    {
        VerifyLegacyMigrationIsolation();
        using var test = new UiEnvironment("unauthorized");
        var account = test.Account(1); account.ScriptSettings = new(); test.Initialize(new[] { account });
        using var form = new AccountSettingsForm(account.AccountName, test.Workspace.Processes.RuntimeFor(account.InstanceId),
            test.Workspace.Accounts, test.Workspace.Paths, test.Workspace.Profiles, new WindowsFolderLauncher(),
            test.Workspace.Options.PathLibraryDirectory, account.CharacterName, test.Workspace.NameLists, test.Workspace.RadarMaps);
        object?[] arguments = { null };
        var saved = (bool)Method(form, "SaveCurrentSettings").Invoke(form, arguments)!;
        Require(saved, "script configuration saves without authorization: " + arguments[0]);
        Require(test.View(account).WorkerProcessId is null, "saving settings does not start an authorization or hardware worker");
        var loaded = Pump(test.Workspace.Accounts.LoadAllAsync());
        Require(loaded.Success && loaded.Value!.Single().ScriptSettings is not null, "saved script configuration remains on disk");
    });

    private static void VerifyLegacyMigrationIsolation()
    {
        using var test = new UiEnvironment();
        var accounts = new[]
        {
            new AccountConfig { AccountName = "旧占位零", HardwareKey = "0", VmmDeviceName = "fpga" },
            new AccountConfig { AccountName = "旧实际账号", HardwareKey = "mock-legacy-physical", VmmDeviceName = "fpga://devindex=7" },
            new AccountConfig { AccountName = "旧自动占位", HardwareKey = "auto", VmmDeviceName = "fpga" },
            new AccountConfig { AccountName = "旧空占位甲", VmmDeviceName = "fpga" },
            new AccountConfig { AccountName = "旧空占位乙" }
        };
        Require(Pump(test.Workspace.Accounts.SaveAllAsync(accounts)).Success, "legacy accounts can be seeded without instance IDs");
        var kmBoxStore = new Roadhog.Infrastructure.Config.JsonKmBoxNetDeviceConfigStore(test.Workspace.Options.KmBoxNetConfigPath);
        Require(Pump(kmBoxStore.SaveAsync(new Roadhog.Infrastructure.Input.KmBoxNetDeviceConfig
        { IpAddress = "127.0.0.21", Port = 1000, Mac = "A1B2C3D4" })).Success, "legacy KMBox settings are seeded locally");
        var migrated = Pump(test.Workspace.InitializeAsync(CancellationToken.None));
        var primary = migrated.Single(a => a.AccountName == "旧实际账号");
        Require(primary.KmBox?.Mac == "A1B2C3D4" && primary.LicenseCredentialPath == test.Workspace.Options.LicenseCredentialPath,
            "only the primary legacy physical account inherits the existing KMBox and authorization credential");
        Require(primary.VmmDeviceName == "fpga://devindex=7", "explicit legacy VMM mapping is preserved");
        foreach (var placeholder in migrated.Where(a => a.AccountName != primary.AccountName))
        {
            Require(placeholder.KmBox is null && placeholder.LicenseCredentialPath.Length == 0,
                "unconfigured legacy rows do not copy another account's KMBox or credential");
            Require(placeholder.VmmDeviceName.Length == 0, "empty, auto, and zero hardware keys never guess device zero");
        }
        Require(migrated.Select(a => a.InstanceId).Distinct().Count() == accounts.Length && migrated.All(a => Guid.TryParse(a.InstanceId, out _)),
            "migration assigns stable distinct instance IDs to every saved row");
        Pump(test.Workspace.SaveAccountsAsync(migrated));
        Require(Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Count == accounts.Length, "multiple unconfigured legacy rows remain saveable");
        Require(test.Workspace.Processes.Snapshot().All(v => v.WorkerProcessId is null), "configuration migration never launches a hardware worker");
    }

    private static HardwareDeviceFeature Device(AccountConfig account) => new(account.HardwareKey, "port", "physical", "mock-device-instance", "mock-parent", "mock-container",
        "mock-hardware-id", "mock-location", "测试 DMA 设备", "Mock", account.VmmDeviceName, new[] { account.HardwareKey });

    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext()); action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private static void ShowHidden(Form form)
    {
        form.ShowInTaskbar = false; form.Opacity = 0; form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-30000, -30000); form.Show();
    }
    private static void Pump(Task task)
    {
        Until(() => task.IsCompleted, "asynchronous UI operation completes", 15000);
        task.GetAwaiter().GetResult();
    }
    private static T Pump<T>(Task<T> task) { Pump((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition, string reason, int timeoutMs = 6000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(reason);
            System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5);
        }
    }
    private static MethodInfo Method(object value, string name) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static object? Invoke(object value, string name, params object?[] args) => Method(value, name).Invoke(value, args);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void SetField(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Roadhog.Tests"))) directory = directory.Parent;
        return directory?.FullName ?? Environment.CurrentDirectory;
    }

    private sealed class UiEnvironment : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "RoadhogUiTests", Guid.NewGuid().ToString("N"));
        public MultiAccountWorkspace Workspace { get; }
        public string LeasePath => Path.Combine(_root, "leases.json");
        public UiEnvironment(string scenario = "normal")
        {
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            Workspace = new(new RoadhogServiceOptions
            {
                AccountConfigPath = Path.Combine(_root, "config", "accounts.json"),
                PathLibraryDirectory = Path.Combine(_root, "config", "paths"), ProfileLibraryDirectory = Path.Combine(_root, "config", "profiles"),
                RadarMapDirectory = Path.Combine(_root, "config", "radar-maps"), LogDirectory = Path.Combine(_root, "logs"),
                KmBoxNetConfigPath = Path.Combine(_root, "unused-kmbox.json"), LicenseCredentialPath = Path.Combine(_root, "unused-license.dat"),
                OwnerLicenseGrantPath = Path.Combine(_root, "unused-owner.json")
            }, new WorkerProcessLaunchOptions
            {
                ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"), PrefixArguments = new[] { "--worker-scenario=" + scenario },
                LeasePath = LeasePath, StartupTimeout = TimeSpan.FromSeconds(10), StopTimeout = TimeSpan.FromSeconds(2),
                PollInterval = TimeSpan.FromMilliseconds(80), RecoveryDelay = TimeSpan.FromMilliseconds(100)
            });
        }
        public AccountConfig Account(int number) => new()
        {
            InstanceId = Guid.NewGuid().ToString("D"), AccountName = "账号 " + number, CharacterName = new[] { "守望者", "晨光", "青岚" }[number - 1],
            HardwareVerificationSessionId = HardwareVerificationSession.CurrentId,
            HardwareKey = "mock-dma-" + number, VmmDeviceName = "fpga://devindex=" + number,
            KmBox = new() { IpAddress = "127.0.0." + number, Port = 12345, Mac = "mock-mac-" + number }
        };
        public void Initialize(IReadOnlyList<AccountConfig> accounts)
        {
            Pump(Workspace.SaveAccountsAsync(accounts)); Pump(Workspace.InitializeAsync(CancellationToken.None));
        }
        public MultiAccountForm Console(IReadOnlyList<AccountConfig> accounts, bool renderWindow = false)
        {
            var form = new MultiAccountForm(Workspace); HideTray(form);
            form.Disposed += (_, _) =>
            {
                Field<System.Windows.Forms.Timer>(form, "_timer").Dispose();
                Field<NotifyIcon>(form, "_tray").Dispose();
            };
            if (renderWindow)
            {
                ShowHidden(form);
                Until(() => Field<bool>(form, "_initialized"), "console initializes its account rows");
            }
            else
            {
                SetField(form, "_accounts", accounts.ToList()); SetField(form, "_initialized", true); form.CreateControl();
            }
            Invoke(form, "RefreshRows"); return form;
        }
        public void HideTray(MultiAccountForm form) => Field<NotifyIcon>(form, "_tray").Visible = false;
        public AccountProcessView View(AccountConfig account) => Workspace.Processes.Snapshot().Single(v => v.Config.InstanceId == account.InstanceId);
        public void Dispose()
        {
            try { Pump(Workspace.Processes.StopAllAsync()); }
            finally
            {
                Pump(Workspace.DisposeAsync().AsTask());
                try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
            }
        }
    }
}
