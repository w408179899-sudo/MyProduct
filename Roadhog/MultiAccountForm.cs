using Roadhog.Application.Licensing;
using Roadhog.Application.Shell;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Shell;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog;

public sealed class MultiAccountForm : Form
{
    private readonly MultiAccountWorkspace _workspace;
    private readonly MainWindowOperations _operations = new();
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private Task? _initializationTask;
    private Task? _exitTask;
    private Task? _workspaceDisposeTask;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Label _detail = new();
    private readonly Label _message = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly NotifyIcon _tray = new();
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private List<AccountConfig> _accounts = [];
    private bool _exiting;
    private bool _allowClose;
    private bool _initialized;

    public MultiAccountForm(MultiAccountWorkspace? workspace = null)
    {
        _workspace = workspace ?? new();
        Text = "Roadhog · 多账号管理";
        Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(1120, 590); MinimumSize = new Size(920, 450);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 248, 245);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        BuildUi();
        _timer.Tick += (_, _) => RefreshRows();
        Shown += (_, _) => _initializationTask ??= RunUiActionAsync(InitializeAsync, requireInitialized: false);
        FormClosing += OnClosing;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Absolute, 42));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 64)); root.RowStyles.Add(new(SizeType.Absolute, 30));
        Controls.Add(root);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        tools.Controls.Add(new Label { Text = "账号列表", AutoSize = true, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Margin = new Padding(0, 5, 18, 0) });
        tools.Controls.Add(Button("添加账号", () => RunConfigurationActionAsync(() => EditHardwareAsync(null))));
        tools.Controls.Add(Button("导入旧配置", () => RunConfigurationActionAsync(ImportAsync)));
        tools.Controls.Add(Button("启动所选", () => BatchAsync(true)));
        tools.Controls.Add(Button("停止所选", () => BatchAsync(false)));
        tools.Controls.Add(Button("全选", () => { foreach (DataGridViewRow row in _grid.Rows) row.Cells[0].Value = true; return Task.CompletedTask; }));
        tools.Controls.Add(Button("退出程序", () => ExitAsync(), exitAction: true));
        root.Controls.Add(tools, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _filter.DropDownStyle = ComboBoxStyle.DropDownList; _filter.Width = 95;
        _filter.Items.AddRange(["全部", "运行中", "已停止", "需关注"]); _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => RefreshRows();
        _search.PlaceholderText = "搜索账号 / 角色"; _search.Width = 200; _search.TextChanged += (_, _) => RefreshRows();
        _summary.AutoSize = true; _summary.Margin = new Padding(16, 5, 0, 0);
        filters.Controls.AddRange([_filter, _search, _summary]); root.Controls.Add(filters, 0, 1);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = false; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false; _grid.RowHeadersVisible = false; _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 42; _grid.ColumnHeadersHeight = 34;
        _grid.EnableHeadersVisualStyles = false; _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(228, 241, 232);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 251, 247);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        AddTextColumn("账号 / 角色", 24); AddTextColumn("设备", 15); AddTextColumn("状态", 20); AddTextColumn("杀怪/h", 8); AddTextColumn("时长", 9);
        _grid.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (var action in new[] { "清包", "设置", "硬件", "启动", "停止" })
            _grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Name = action, Text = action == "硬件" ? "设备/角色" : action,
                UseColumnTextForButtonValue = true, Width = action == "硬件" ? 85 : 55, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellContentClick += GridAction;
        _grid.SelectionChanged += (_, _) => ShowDetail();
        root.Controls.Add(_grid, 0, 2);
        var detailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 0) };
        detailPanel.ColumnStyles.Add(new(SizeType.Percent, 100)); detailPanel.ColumnStyles.Add(new(SizeType.Absolute, 105)); detailPanel.ColumnStyles.Add(new(SizeType.Absolute, 105));
        _detail.Dock = DockStyle.Fill; _detail.AutoEllipsis = true;
        detailPanel.Controls.Add(_detail, 0, 0);
        detailPanel.Controls.Add(Button("账号日志", OpenLogsAsync), 1, 0);
        detailPanel.Controls.Add(Button("账号授权", () => RunConfigurationActionAsync(AuthorizeSelectedAsync)), 2, 0);
        root.Controls.Add(detailPanel, 0, 3);
        _message.Dock = DockStyle.Fill; _message.ForeColor = Color.FromArgb(74, 105, 84); _message.AutoEllipsis = true;
        _message.Text = "“设备/角色”可编辑 DMA、KMBox 并测试读取角色；关闭窗口收进托盘。";
        root.Controls.Add(_message, 0, 4);
        _tray.Icon = Icon; _tray.Text = "Roadhog 多账号管理"; _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        var menu = new ContextMenuStrip(); menu.Items.Add("显示主界面", null, (_, _) => RestoreWindow());
        menu.Items.Add("退出程序", null, async (_, _) => await ExitAsync()); _tray.ContextMenuStrip = menu;
    }

    private Button Button(string text, Func<Task> action, bool exitAction = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, Padding = new Padding(6, 0, 6, 0) };
        button.FlatAppearance.BorderColor = Color.FromArgb(192, 215, 199);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { if (exitAction) await action(); else await RunUiActionAsync(action); }
            finally { if (!button.IsDisposed) button.Enabled = true; }
        };
        return button;
    }

    private void AddTextColumn(string title, float weight) => _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, ReadOnly = true, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

    private async Task InitializeAsync()
    {
        var accounts = await _workspace.InitializeAsync(_operations.Token);
        if (_operations.IsClosing || IsDisposed) return;
        _accounts = accounts.ToList(); _initialized = true; RefreshRows(); _timer.Start();
    }

    private async Task RunUiActionAsync(Func<Task> action, bool requireInitialized = true)
    {
        if (_operations.IsClosing || IsDisposed) return;
        if (requireInitialized && !_initialized) { Report("账号配置尚未加载完成，请稍后再操作。"); return; }
        try { await _operations.RunAsync(action); }
        catch (OperationCanceledException) when (_operations.IsClosing || IsDisposed) { }
        catch (Exception ex)
        {
            _workspace.Logger.Error("manager.ui.operation_failed", ex);
            Report(ex.Message);
        }
    }

    private async Task RunConfigurationActionAsync(Func<Task> action)
    {
        if (!await _configurationGate.WaitAsync(0, _operations.Token))
            throw new InvalidOperationException("正在修改账号配置，请完成当前操作后再试。");
        try { await action(); }
        finally { _configurationGate.Release(); }
    }

    private void RefreshRows()
    {
        if (!_initialized || _exiting || IsDisposed) return;
        var checkedIds = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => (string)r.Tag!).ToHashSet();
        var selected = SelectedId();
        var views = _workspace.Processes.Snapshot();
        _summary.Text = $"共 {views.Count} 个账号 · {views.Count(v => v.Worker?.IsRunning == true)} 个运行中";
        var query = _search.Text.Trim();
        var shown = views.Where(v => (query.Length == 0 || (v.Config.AccountName + v.Config.CharacterName).Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (_filter.SelectedIndex == 0 || _filter.SelectedIndex == 1 && v.Worker?.IsRunning == true || _filter.SelectedIndex == 2 && v.State is "stopped" or "idle" or "verification_required" || _filter.SelectedIndex == 3 && v.State is "failed" or "recovering" or "verification_required")).ToArray();
        // Update existing rows in place so a timer never moves a button under the pointer.
        var ids = shown.Select(v => v.Config.InstanceId).ToArray();
        if (!_grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!).SequenceEqual(ids))
        {
            _grid.Rows.Clear();
            foreach (var view in shown) { var index = _grid.Rows.Add(); _grid.Rows[index].Tag = view.Config.InstanceId; }
        }
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var view = shown.First(v => v.Config.InstanceId == (string)row.Tag!);
            var snapshot = view.Worker?.Snapshot;
            row.Cells[0].Value = checkedIds.Contains(view.Config.InstanceId);
            var character = string.IsNullOrWhiteSpace(snapshot?.CharacterName) ? view.Config.CharacterName : snapshot.CharacterName;
            row.Cells[1].Value = view.Config.AccountName + (string.IsNullOrWhiteSpace(character) ? "" : " / " + character);
            if (snapshot is { CharacterLevel: > 0 } || !string.IsNullOrWhiteSpace(snapshot?.CharacterClass))
            {
                var level = snapshot!.CharacterLevel > 0 ? snapshot.CharacterLevel + "级" : "等级未知";
                var characterClass = string.IsNullOrWhiteSpace(snapshot.CharacterClass) ? "职业未知" : snapshot.CharacterClass.Trim();
                row.Cells[1].Value += "\n" + level + " · " + characterClass;
            }
            row.Cells[1].ToolTipText = row.Cells[1].Value?.ToString();
            row.Cells[2].Value = string.IsNullOrWhiteSpace(view.Config.VmmDeviceName) ? "待配置" : view.Config.VmmDeviceName;
            row.Cells[3].Value = StateText(view);
            var elapsed = snapshot?.StartedAt is { } started ? (snapshot.StoppedAt ?? DateTimeOffset.Now) - started : TimeSpan.Zero;
            row.Cells[4].Value = elapsed.TotalHours > 0 ? (snapshot!.KillCount / elapsed.TotalHours).ToString("0") : "—";
            row.Cells[5].Value = elapsed > TimeSpan.Zero ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : "—";
            row.DefaultCellStyle.ForeColor = view.State is "failed" or "recovering" or "verification_required" ? Color.DarkOrange : Color.FromArgb(30, 65, 43);
            if (view.Config.InstanceId == selected) row.Selected = true;
        }
        ShowDetail();
    }

    private static string StateText(AccountProcessView view) => string.IsNullOrWhiteSpace(view.Config.VmmDeviceName) || view.Config.KmBox is null ? "待配置"
        : !Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(view.Config) ? "待验证" : view.State switch
    {
        "starting" => "启动中", "stopping" => "停止中", "recovering" => "恢复中", "failed" => "需处理",
        "idle" => "已停止 · 设置连接中", "running" => view.Worker?.Snapshot?.CleanupProgress is { Length: > 0 } progress
            ? progress.StartsWith("等待仓库号", StringComparison.Ordinal) ? "等仓库" : progress.StartsWith("等待摆摊售罄", StringComparison.Ordinal) ? "摆摊中" : "清包中"
            : "运行中", _ => "已停止"
    };
    private string? SelectedId() => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as string : null;
    private void ShowDetail()
    {
        var id = SelectedId(); var view = _workspace.Processes.Snapshot().FirstOrDefault(v => v.Config.InstanceId == id);
        _detail.Text = view is null ? "选择账号查看状态；勾选后可批量操作。" :
            $"{view.Config.AccountName} · {StateText(view)} · 自动恢复：{(view.Config.AutoRecover ? "开启" : "关闭")}\n" +
            (view.Error ?? view.Worker?.Snapshot?.LastWarning ?? view.Worker?.Snapshot?.CleanupProgress ?? "");
    }

    private async void GridAction(object? sender, DataGridViewCellEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
        if (e.RowIndex < 0 || e.ColumnIndex < 6) return;
        var id = (string)_grid.Rows[e.RowIndex].Tag!;
        var action = _grid.Columns[e.ColumnIndex].Name;
        if (action == "停止")
        {
            try { Check(await _workspace.Processes.StopAsync(id)); }
            catch (Exception ex) { Report(ex.Message); }
            finally { RefreshRows(); }
            return;
        }
        if (!_busy.Add(id)) return;
        try
        {
            if (action == "启动") await RunAsync(id, false);
            else if (action == "停止") Check(await _workspace.Processes.StopAsync(id));
            else if (action == "清包")
            {
                var account = _accounts.Single(a => a.InstanceId == id);
                var description = account.ScriptSettings?.Maintenance.CleanupWorkflow.Describe();
                if (string.IsNullOrWhiteSpace(description)) throw new InvalidOperationException("请先在清包页选择执行项目。");
                if (MessageBox.Show(this, $"账号：{account.AccountName}\n{description}\n\n完成后继续挂机，停止按钮可取消流程。", "确认清包", MessageBoxButtons.OKCancel) == DialogResult.OK) await RunAsync(id, true);
            }
            else if (action == "硬件") await RunConfigurationActionAsync(() => EditHardwareAsync(id));
            else if (action == "设置") await RunConfigurationActionAsync(() => OpenSettingsAsync(id));
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            if (action == "硬件" && !IsDisposed && !_operations.IsClosing)
                MessageBox.Show(this, ex.Message, "设备与角色", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _busy.Remove(id); RefreshRows(); }
        });
    }

    private async Task RunAsync(string id, bool cleanup)
    {
        Check(await _workspace.Processes.StartAsync(id, cleanup, _operations.Token));
    }
    private static void Check(Core.Common.OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
    private async Task BatchAsync(bool start)
    {
        var ids = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => (string)r.Tag!).ToArray();
        if (ids.Length == 0) { Report("请先勾选账号。"); return; }
        var errors = await Task.WhenAll(ids.Select(async id =>
        {
            try { Check(start ? await _workspace.Processes.StartAsync(id, cancellationToken: _operations.Token) : await _workspace.Processes.StopAsync(id)); return null; }
            catch (Exception ex) { return _accounts.Single(a => a.InstanceId == id).AccountName + "：" + ex.Message; }
        }));
        Report(errors.Any(e => e is not null) ? string.Join("；", errors.Where(e => e is not null)) : $"已{(start ? "启动" : "停止")}所选账号。"); RefreshRows();
    }

    private async Task OpenSettingsAsync(string id)
    {
        var account = _accounts.Single(a => a.InstanceId == id);
        using var form = CreateAccountSettingsForm(account);
        form.ShowDialog(this);
        await ReloadAccountsAsync();
        if (!_workspace.Processes.Snapshot().Single(v => v.Config.InstanceId == id).DesiredRunning)
            await _workspace.Processes.StopAsync(id);
    }

    private AccountSettingsForm CreateAccountSettingsForm(AccountConfig account) =>
        new(account.AccountName, _workspace.Processes.RuntimeFor(account.InstanceId), _workspace.Accounts,
            _workspace.Paths, _workspace.Profiles, new WindowsFolderLauncher(), _workspace.Options.PathLibraryDirectory,
            account.CharacterName, _workspace.NameListsFor(account), _workspace.RadarMapsFor(account));

    private async Task ReloadAccountsAsync()
    {
        var loaded = await _workspace.Accounts.LoadAllAsync(_operations.Token);
        if (!loaded.Success || loaded.Value is null) throw new InvalidOperationException(loaded.Error);
        _accounts = loaded.Value.ToList();
        await _workspace.MigrateSharedConfigurationAsync(_accounts, _operations.Token);
        _workspace.Processes.UpdateAccounts(_accounts); RefreshRows();
    }

    private async Task EditHardwareAsync(string? id)
    {
        if (!_initialized) return;
        var existing = _accounts.FirstOrDefault(a => a.InstanceId == id);
        if (id is not null && !await PrepareHardwareEditingAsync(id)) return;
        var draft = existing?.Clone() ?? new AccountConfig { InstanceId = Guid.NewGuid().ToString("D"), AccountName = "账号" + (_accounts.Count + 1), ScriptSettings = new() };
        var devices = Infrastructure.Hardware.SavedHardwareBindingPolicy.ForEditor(_workspace.Hardware.ListDevices(), _accounts);
        using var form = new AccountHardwareForm(draft, devices, async (config, cancellationToken) =>
        {
            _workspace.Processes.UpdateAccounts(_accounts.Where(a => a.InstanceId != config.InstanceId).Append(config).ToArray());
            return await _workspace.Processes.VerifyHardwareAsync(config, cancellationToken);
        }, async (config, owner) =>
        {
            if (!_accounts.Any(a => a.InstanceId == config.InstanceId))
            {
                // Activation creates durable credentials. Keep the account identity even if setup is cancelled.
                var pending = new AccountConfig { InstanceId = config.InstanceId, AccountName = config.AccountName, ScriptSettings = new() };
                var withPending = _accounts.Append(pending).ToList();
                await _workspace.SaveAccountsAsync(withPending, _operations.Token);
                _accounts = withPending;
                Report("已建立待配置账号；取消硬件设置也会保留其授权。");
            }
            await using var coordinator = _workspace.CreateLicenseCoordinator(config);
            var state = await coordinator.InitializeAsync(_operations.Token);
            if (state.Kind == LicenseRuntimeStateKind.ActivationRequired)
            { using var activation = new LicenseActivationForm(coordinator, state); activation.ShowDialog(owner); }
            if (!coordinator.IsAuthorized) throw new InvalidOperationException(LicenseUiText.Describe(coordinator.State));
        });
        try
        {
            if (form.ShowDialog(this) != DialogResult.OK) return;
            var next = _accounts.Where(a => a.InstanceId != draft.InstanceId).Append(form.Config).ToList();
            await _workspace.SaveAccountsAsync(next, _operations.Token); _accounts = next; Report("账号配置已保存。");
        }
        finally { _workspace.Processes.UpdateAccounts(_accounts); RefreshRows(); }
    }

    private async Task<bool> PrepareHardwareEditingAsync(string id)
    {
        var view = _workspace.Processes.Snapshot().Single(v => v.Config.InstanceId == id);
        if (!view.DesiredRunning && !view.WorkerProcessId.HasValue) return true;
        if (view.Worker?.IsRunning == true && MessageBox.Show(this,
                $"需要先停止“{view.Config.AccountName}”，才能编辑设备或测试读取角色。\n\n现在停止此账号并打开设备设置？",
                "设备与角色", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return false;
        Report($"正在停止 {view.Config.AccountName} 并释放设备，随后打开角色测试…");
        Check(await _workspace.Processes.StopAsync(id));
        _operations.Token.ThrowIfCancellationRequested();
        RefreshRows();
        return true;
    }

    private async Task AuthorizeSelectedAsync()
    {
        var id = SelectedId(); if (id is null) return;
        var view = _workspace.Processes.Snapshot().Single(v => v.Config.InstanceId == id);
        if (view.DesiredRunning || view.WorkerProcessId.HasValue) throw new InvalidOperationException("请先停止此账号，再修改授权。");
        await using var coordinator = _workspace.CreateLicenseCoordinator(view.Config);
        var state = await coordinator.InitializeAsync(_operations.Token);
        if (state.Kind == LicenseRuntimeStateKind.ActivationRequired)
        { using var form = new LicenseActivationForm(coordinator, state); form.ShowDialog(this); }
        Report(LicenseUiText.Describe(coordinator.State));
    }

    private Task OpenLogsAsync()
    {
        var view = _workspace.Processes.Snapshot().FirstOrDefault(v => v.Config.InstanceId == SelectedId());
        if (view is not null) { Directory.CreateDirectory(view.LogDirectory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(view.LogDirectory) { UseShellExecute = true }); }
        return Task.CompletedTask;
    }

    private async Task ImportAsync()
    {
        using var picker = new OpenFileDialog { Title = "选择旧客户端的 config/accounts.json", Filter = "账号配置 (accounts.json)|accounts.json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var importer = new Infrastructure.Config.LegacyAccountImporter(_workspace.Options);
        var imported = await importer.ImportPreservingConflictsAsync(picker.FileName, _accounts, _operations.Token);
        var current = await _workspace.Accounts.LoadAllAsync(_operations.Token);
        if (!current.Success || current.Value is null) throw new InvalidOperationException(current.Error);
        var merged = current.Value.Concat(imported).ToList();
        await _workspace.SaveAccountsAsync(merged, _operations.Token); _accounts = merged; RefreshRows(); Report("旧账号已导入；同名冲突资料分别保留。请核对设置并验证设备。");
    }

    private void Report(string text) { if (!IsDisposed) _message.Text = text; }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            CloseForSystemShutdown();
            _allowClose = true;
            e.Cancel = false;
            return;
        }
        e.Cancel = true;
        if (e.CloseReason == CloseReason.UserClosing) Hide();
        else _ = ExitAsync();
    }
    private Task ExitAsync() => _exitTask ??= ExitCoreAsync();

    private async Task ExitCoreAsync()
    {
        // Assign _exitTask before a synchronous failure can reset it for a later retry.
        await Task.Yield();
        _exiting = true; _timer.Stop(); Enabled = false;
        var shutdownPending = false;
        try
        {
            if (_workspaceDisposeTask is null)
            {
                _workspace.Processes.BeginShutdown();
                _operations.BeginShutdown();
                CloseOwnedForms(this);
                try { await _operations.DrainAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException)
                {
                    _workspace.Logger.Warn("manager.ui.drain_timeout", new Dictionary<string, object?>());
                    shutdownPending = true;
                    await WaitForShutdownAsync(_workspace.Processes.ForceStopAllAsync(), TimeSpan.FromSeconds(5));
                    throw new TimeoutException("界面操作取消后尚未结束，后台已请求停止；请稍后再次退出。");
                }
                var stopping = _workspace.Processes.StopAllAsync();
                try { await WaitForShutdownAsync(stopping, TimeSpan.FromSeconds(15)); }
                catch (TimeoutException)
                {
                    shutdownPending = true;
                    await WaitForShutdownAsync(_workspace.Processes.ForceStopAllAsync(), TimeSpan.FromSeconds(5));
                    throw new TimeoutException("后台停止或停止状态保存尚未完成，请稍后再次退出。");
                }
                var results = await stopping;
                var errors = results.Where(p => !p.Value.Success).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join("；", errors.Select(p => p.Key + ": " + p.Value.Error)));
                _workspaceDisposeTask = _workspace.DisposeAsync().AsTask();
            }
            await WaitForShutdownAsync(_workspaceDisposeTask, TimeSpan.FromSeconds(5));
            _allowClose = true; _tray.Visible = false; Close();
        }
        catch (Exception ex)
        {
            _workspace.Logger.Error("manager.ui.exit_failed", ex);
            if (shutdownPending || _workspaceDisposeTask is not null || !_operations.DrainAsync().IsCompleted)
            {
                Report("退出尚未完成，已暂停新操作；可稍后再次点击退出：" + ex.Message);
                Enabled = true;
                _exitTask = null;
                return;
            }
            _workspace.Processes.CancelShutdown(); _operations.Resume();
            Report("退出未完成：" + ex.Message); _exiting = false; Enabled = true;
            if (_initialized) _timer.Start();
            _exitTask = null;
        }
    }

    private static void CloseOwnedForms(Form owner)
    {
        foreach (var child in owner.OwnedForms)
        {
            // Closing an inner modal dialog lets its parent's pending authorization/verification finish.
            CloseOwnedForms(child);
            if (!child.IsDisposed) child.Close();
        }
    }

    private async Task WaitForShutdownAsync(Task work, TimeSpan timeout)
    {
        try { await work.WaitAsync(timeout); }
        catch (TimeoutException)
        {
            // A native call or file writer may finish after the UI deadline; retain its failure observation.
            _ = work.ContinueWith(task => _workspace.Logger.Error("manager.ui.late_shutdown_failure", task.Exception!),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private void CloseForSystemShutdown()
    {
        _exiting = true; _timer.Stop(); _tray.Visible = false;
        // Windows requires a bounded answer to its shutdown message. Do not cancel system shutdown or await UI continuations.
        try
        {
            _workspace.Processes.BeginShutdown();
            _operations.BeginShutdown();
            var stop = Task.Run(async () =>
            {
                try { await WaitForShutdownAsync(_workspace.Processes.StopAllAsync(), TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (Exception exception) { _workspace.Logger.Error("manager.system_shutdown.stop_failed", exception); }
                await WaitForShutdownAsync(_workspace.Processes.ForceStopAllAsync(), TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            });
            stop.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception exception) { _workspace.Logger.Error("manager.system_shutdown.deadline", exception); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _tray.Dispose(); _operations.Dispose(); }
        base.Dispose(disposing);
    }
}
