using System.Collections.Immutable;
using SampleProject.Bootstrap;
using Smart.Hosting;
using Smart.Hosting.Windows;
namespace SampleProject.Desktop;

public sealed class MainForm : Form
{
    private readonly JsonLineEventSink _logs;
    private readonly AccountWorkspace _workspace;
    private readonly string _configPath;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 45 };
    private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Top, Height = 80, AutoSize = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly bool _smoke;
    private bool _closing, _refreshing;
    private string? _editingId;
    public MainForm(bool smoke = false)
    {
        _smoke = smoke;
        Text = "Smart 公共工程模板"; Width = 1050; Height = 650;
        if (smoke) { Opacity = 0; ShowInTaskbar = false; }
        var data = Path.Combine(AppContext.BaseDirectory, smoke ? "smoke-" + Guid.NewGuid().ToString("N") : "data");
        _configPath = Path.Combine(data, "accounts.json");
        _logs = new(Path.Combine(data, "logs"));
        _workspace = new(new JsonConfigStore<HostSettings>(_configPath, 1, x => x.Validate()), new ProjectHost(_logs), _logs);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430, Width = 1000 };
        split.Panel1.Controls.Add(_list); split.Panel2.Controls.Add(_properties);
        Controls.Add(split); Controls.Add(_buttons); Controls.Add(_status);
        Button("新增", () => { _editingId = null; _properties.SelectedObject = new ProfileEditor { Id = "account-" + (_workspace.Accounts.Count + 1) }; return Task.CompletedTask; });
        Button("保存配置", SaveEditorAsync);
        Button("绑定 DMA", BindDmaAsync);
        Button("清除绑定", () =>
        {
            if (_properties.SelectedObject is ProfileEditor editor) { editor.ClearBinding(); _properties.Refresh(); }
            return Task.CompletedTask;
        });
        Button("删除", async () =>
        {
            if (Selected is not { } selected) return;
            await _workspace.SaveAsync(new(_workspace.Accounts.Where(x => x != selected).Select(x => x.Profile).ToImmutableArray()));
            _editingId = null; _properties.SelectedObject = null;
        });
        Button("导入", async () =>
        {
            using var dialog = new OpenFileDialog { Filter = "账号配置|*.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var settings = await new JsonConfigStore<HostSettings>(dialog.FileName, 1, x => x.Validate()).LoadAsync();
            await _workspace.SaveAsync(settings);
        });
        Button("导出", async () =>
        {
            using var dialog = new SaveFileDialog { Filter = "账号配置|*.json", FileName = "accounts.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await new JsonConfigStore<HostSettings>(dialog.FileName, 1, x => x.Validate()).SaveAsync(new(_workspace.Accounts.Select(x => x.Profile).ToImmutableArray()));
        });
        Button("启动选中", () => { Selected?.Start(); return Task.CompletedTask; });
        Button("暂停选中", async () => { if (Selected is { } a) await a.PauseAsync(TimeSpan.FromSeconds(10)); });
        Button("停止选中", async () => { if (Selected is { } a) await a.StopAsync(TimeSpan.FromSeconds(10)); });
        Button("启动全部", () => { foreach (var a in _workspace.Accounts) a.Start(); return Task.CompletedTask; });
        Button("停止全部", () => _workspace.StopAllAsync(TimeSpan.FromSeconds(10)));
        Button("导出诊断", async () =>
        {
            using var dialog = new SaveFileDialog { Filter = "诊断包|*.zip", FileName = "smart-diagnostics.zip" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var logs = Path.Combine(Path.GetDirectoryName(_configPath)!, "logs");
            await DiagnosticBundle.ExportAsync(dialog.FileName, Directory.Exists(logs) ? Directory.GetFiles(logs, "smart-*.jsonl") : [],
                _workspace.Accounts.Select(x => (x.Profile.Id, x.Status)).ToArray());
        });
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || Selected is not { } selected) return;
            _editingId = selected.Profile.Id; _properties.SelectedObject = ProfileEditor.From(selected.Profile);
        };
        _timer.Tick += (_, _) => RefreshStatus();
        Shown += OnShown;
        FormClosing += OnClosing;
    }
    private ManagedAccount? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _workspace.Accounts.Count ? _workspace.Accounts[_list.SelectedIndex] : null;
    private void Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) =>
        {
            _buttons.Enabled = false;
            try { await action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成"); }
            finally { _buttons.Enabled = true; RefreshStatus(); }
        };
        _buttons.Controls.Add(button);
    }
    private async Task SaveEditorAsync()
    {
        if (_properties.SelectedObject is not ProfileEditor editor) return;
        var profile = editor.ToProfile(); profile.Validate();
        var profiles = _workspace.Accounts.Where(x => x.Profile.Id != _editingId).Select(x => x.Profile).Append(profile).ToImmutableArray();
        await _workspace.SaveAsync(new(profiles)); _editingId = profile.Id;
    }
    private async Task BindDmaAsync()
    {
        if (_properties.SelectedObject is not ProfileEditor editor) return;
        if (editor.Mode != RuntimeMode.Hardware) throw new InvalidOperationException("先选择 Hardware 模式并填写 VMM 库路径。");
        var inventory = await Task.Run(() => DeviceDiscovery.ListDmaDevices(editor.LibraryPath, editor.BindingDriverFileName));
        using var dialog = new Form { Text = "选择实际 D3XX 设备（不连接 DMA）", Width = 760, Height = 360, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var device in inventory.Devices)
            list.Items.Add($"index={device.Index} | serial={device.Identity.SerialNumber} | location={device.Identity.LocationId:X8} | {device.Identity.Description}");
        list.SelectedIndex = -1;
        var confirm = new Button { Text = "保存此设备绑定到编辑项", Dock = DockStyle.Bottom, Height = 35, Enabled = false, DialogResult = DialogResult.OK };
        list.SelectedIndexChanged += (_, _) => confirm.Enabled = list.SelectedIndex >= 0;
        var hint = new Label { Text = "请根据驱动序列号/位置核对物理设备。重排或不明确时会拒绝连接；完成后仍需保存账号配置。", Dock = DockStyle.Top, Height = 42 };
        dialog.Controls.Add(list); dialog.Controls.Add(confirm); dialog.Controls.Add(hint);
        if (dialog.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var uri = "fpga://ft601=1,devindex=" + list.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        editor.SetBinding(DmaBindingPolicy.Capture(uri, inventory)); _properties.Refresh();
    }
    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            if (File.Exists(_configPath)) await _workspace.LoadAsync();
            else await _workspace.SaveAsync(new([new("local")]));
            RefreshStatus(); _timer.Start();
            if (!_smoke) return;
            var account = _workspace.Accounts.Single(); account.Start();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (account.Status.State != SessionState.Running) await Task.Delay(10, stop.Token);
            await account.PauseAsync(TimeSpan.FromSeconds(5));
            account.Start();
            while (account.Status.Generation < 2) await Task.Delay(10, stop.Token);
            await _workspace.StopAllAsync(TimeSpan.FromSeconds(5));
            if (account.Status.State != SessionState.Stopped) Environment.ExitCode = 1;
            Close();
        }
        catch (Exception ex)
        {
            if (_smoke) { Environment.ExitCode = 1; Close(); }
            else MessageBox.Show(this, ex.Message, "配置未加载");
        }
    }
    private void RefreshStatus()
    {
        var index = _list.SelectedIndex;
        _refreshing = true; _list.BeginUpdate(); _list.Items.Clear();
        foreach (var a in _workspace.Accounts)
            _list.Items.Add($"{a.Profile.Id} | {a.Profile.Mode} | {a.Status.State} | 会话 {a.Status.Generation} | {a.Status.Error}");
        if (index >= 0 && index < _list.Items.Count) _list.SelectedIndex = index;
        _list.EndUpdate(); _refreshing = false;
        _status.Text = $"配置：{_configPath}\n日志丢弃：{_logs.DroppedEvents}  {_logs.WriteError}";
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true; Enabled = false;
        try
        {
            await _workspace.DisposeAsync(); await _logs.DisposeAsync();
            _timer.Stop(); _timer.Dispose(); _closing = true; Close();
        }
        catch (Exception ex) { Enabled = true; if (_smoke) Environment.ExitCode = 1; else MessageBox.Show(this, ex.Message, "等待停止完成"); }
    }
}
