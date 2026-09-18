using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleProject.Desktop.Services;
using Smart.Adapters.Dma;
using Smart.Hosting;
using Smart.Hosting.Windows;

namespace SampleProject.Desktop.ViewModels;

public sealed record DeviceChoice(DmaDeviceBinding Binding)
{
    public string Label => $"设备 {Binding.DeviceIndex} · {Binding.Identity.SerialNumber} · {Binding.Identity.LocationId:X8}";
}
public sealed record ProcessChoice(int Pid, string Name) { public string Label => Pid == 0 ? $"{Name} · 按名称匹配" : $"{Name} · PID {Pid}"; }
public sealed record UiMessage(string Time, string Text);

public partial class ShellViewModel : ObservableObject
{
    private readonly AccountWorkspace _workspace;
    private readonly IHardwareDiagnostics _diagnostics;
    private readonly IDesktopDialogs _dialogs;
    private readonly IAccountProfileVerifier _verifier;
    private readonly string _configPath;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operationCancellation;
    private Task? _operation;
    private string? _editingId;
    private bool _loadingEditor, _closed, _shutdownComplete;
    private long _editorRevision;
    private long _verifiedRevision = -1;
    private string? _verifiedProfile;
    private VerifiedCharacter? _verifiedCharacter;
    private readonly List<IRelayCommand> _guardedCommands = [];
    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public ObservableCollection<DeviceChoice> Devices { get; } = [];
    public ObservableCollection<ProcessChoice> Processes { get; } = [];
    public ObservableCollection<UiMessage> Messages { get; } = [];
    public ObservableCollection<DesktopPage> Pages { get; } = [];
    public RuntimeMode[] Modes { get; } = Enum.GetValues<RuntimeMode>();
    [ObservableProperty] private ProfileEditor _editor = new();
    [ObservableProperty] private AccountRow? _selectedAccount;
    [ObservableProperty] private DeviceChoice? _selectedDevice;
    [ObservableProperty] private ProcessChoice? _selectedProcess;
    [ObservableProperty] private DesktopPage? _selectedPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _notice = "就绪 · 默认使用模拟模式";
    [ObservableProperty] private string _dmaResult = "尚未测试";
    [ObservableProperty] private string _inputResult = "尚未测试";
    [ObservableProperty] private string _overview = "0 个账号运行中";
    [ObservableProperty] private string _verificationResult = "尚未验证，不能保存";
    public bool CanEdit => !IsBusy && !_closed;
    public bool CanUseHardware => CanEdit && Editor.IsHardware;
    public bool CanSave => CanEdit && _verifiedRevision == _editorRevision && _verifiedProfile is not null;
    public string ConfigurationPath => _configPath;
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand VerifyCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IRelayCommand BrowseLibraryCommand { get; }
    public IRelayCommand ClearBindingCommand { get; }
    public IAsyncRelayCommand DiscoverCommand { get; }
    public IAsyncRelayCommand ListProcessesCommand { get; }
    public IAsyncRelayCommand ProbeCommand { get; }
    public IAsyncRelayCommand TestInputCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand StartAllCommand { get; }
    public IAsyncRelayCommand StopAllCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand ExportDiagnosticsCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public ShellViewModel(AccountWorkspace workspace, IHardwareDiagnostics diagnostics, IDesktopDialogs dialogs, string configPath,
        IEnumerable<DesktopPage>? projectPages = null, IAccountProfileVerifier? verifier = null)
    {
        _workspace = workspace; _diagnostics = diagnostics; _dialogs = dialogs; _configPath = configPath;
        _verifier = verifier ?? new AccountProfileVerifier();
        Pages.Add(new("home", "脚本主页", "◈", new HomePage(this)));
        Pages.Add(new("settings", "账号配置", "⚙", new SettingsPage(this)));
        Pages.Add(new("diagnostics", "运行诊断", "≡", new DiagnosticsPage(this)));
        foreach (var page in projectPages ?? [])
        {
            if (string.IsNullOrWhiteSpace(page.Id) || Pages.Any(x => x.Id == page.Id) || page.Content is null)
                throw new ArgumentException("Page IDs must be unique and content must be supplied.");
            Pages.Add(page);
        }
        SelectedPage = Pages[0];
        SaveCommand = Command(SaveAsync, allowed: () => CanSave);
        VerifyCommand = Command(VerifyAsync);
        DeleteCommand = Command(async token =>
        {
            if (_editingId is null || !_dialogs.Confirm($"删除账号 {_editingId} 的配置？")) return;
            await _workspace.DeleteAccountAsync(_editingId, token); SyncAccounts(); LoadEditor(null); Report("账号已删除");
        });
        NewCommand = new RelayCommand(() => { if (ConfirmDiscard()) LoadEditor(null); }, () => CanEdit);
        BrowseLibraryCommand = new RelayCommand(() => { var path = _dialogs.OpenFile("选择 VMM 库", "VMM 库|vmm.dll|动态库|*.dll"); if (path is not null) Editor.LibraryPath = path; }, () => CanEdit);
        ClearBindingCommand = new RelayCommand(() => Editor.ClearBinding(), () => CanEdit);
        DiscoverCommand = Command(async token =>
        {
            InvalidateVerification("设备枚举后请重新验证配置");
            RequireHardware(); var inventory = await _diagnostics.DiscoverAsync(Editor.LibraryPath, Editor.BindingDriverFileName, token);
            token.ThrowIfCancellationRequested();
            Devices.Clear(); SelectedDevice = null;
            foreach (var device in inventory.Devices)
            {
                try { Devices.Add(new(DmaBindingPolicy.Capture($"fpga://ft601=1,devindex={device.Index}", inventory))); }
                catch (DmaBindingException ex) { Report($"设备 {device.Index} 无法绑定：{ex.Message}"); }
            }
            Report($"发现 {inventory.Devices.Count} 个设备，{Devices.Count} 个可绑定；请选择后验证配置。");
        }, hardware: true);
        ListProcessesCommand = Command(async token =>
        {
            InvalidateVerification("进程查询后请重新验证配置");
            RequireDmaIdle(); var processes = await _diagnostics.ListProcessesAsync(Editor.ToDmaSettings(), token);
            token.ThrowIfCancellationRequested();
            Processes.Clear(); SelectedProcess = null;
            foreach (var p in processes.OrderBy(x => x.Name).ThenBy(x => x.ProcessId)) Processes.Add(new(p.ProcessId, p.Name));
            Report($"已查询 {Processes.Count} 个进程，请选择目标进程。");
        }, hardware: true);
        ProbeCommand = Command(async token =>
        {
            InvalidateVerification("单项读取测试不能代替角色配置验证");
            RequireDmaIdle(); DmaResult = "正在测试…";
            var result = await _diagnostics.ProbeAsync(Editor.ToDmaSettings(), token);
            token.ThrowIfCancellationRequested();
            DmaResult = $"通过 · PID {result.Process.ProcessId} · {result.Process.Name} · 模块 0x{result.Process.ModuleBase:X}";
            Report("DMA 只读测试通过，连接已释放。");
        }, hardware: true);
        TestInputCommand = Command(async token =>
        {
            InvalidateVerification("单项握手测试不能代替角色配置验证");
            RequireHardware(); InputResult = "正在握手…";
            await _diagnostics.TestInputAsync(Editor.ToInputSettings(), token);
            token.ThrowIfCancellationRequested();
            InputResult = "连接成功 · 未发送键鼠操作"; Report(InputResult);
        }, hardware: true);
        StartCommand = Command(_ => { if (IsDirty) throw new InvalidOperationException("请先保存当前修改，再启动账号。"); SelectedAccount?.Account.Start(); return Task.CompletedTask; });
        PauseCommand = new AsyncRelayCommand(() => StopSelectedAsync(pause: true));
        StopCommand = new AsyncRelayCommand(() => StopSelectedAsync(pause: false));
        StartAllCommand = Command(_ =>
        {
            if (IsDirty) throw new InvalidOperationException("请先保存当前修改。");
            foreach (var account in _workspace.Accounts) account.Start(); return Task.CompletedTask;
        });
        StopAllCommand = new AsyncRelayCommand(async () => { try { await _workspace.StopAllAsync(TimeSpan.FromSeconds(10)); Report("全部账号已停止"); } catch (Exception ex) { Report(ex.Message); } finally { Refresh(); } });
        ImportCommand = Command(async token =>
        {
            if (!ConfirmDiscard()) return;
            var path = _dialogs.OpenFile("导入账号配置", "账号配置|*.json"); if (path is null) return;
            var settings = await Store(path).LoadAsync(token);
            InvalidateVerification("导入配置需逐项验证");
            var verifiedProfiles = ImmutableArray.CreateBuilder<AccountProfile>();
            foreach (var profile in settings.Accounts)
            {
                var verified = await _verifier.VerifyAsync(profile, token);
                token.ThrowIfCancellationRequested(); verified.ValidateFor(profile);
                verifiedProfiles.Add(profile with { TestedCharacter = verified.Character });
            }
            if (verifiedProfiles.Any(x => x.Mode == RuntimeMode.Hardware) && !_dialogs.Confirm("配置均已验证，确认以下角色与设备对应正确后导入：\n" +
                string.Join("\n", verifiedProfiles.Where(x => x.Mode == RuntimeMode.Hardware).Select(x => $"{x.Id} → {x.TestedCharacter!.Name} · 设备 {x.Dma!.Binding!.Identity.SerialNumber}")))) return;
            await _workspace.SaveAsync(new(verifiedProfiles.ToImmutable()), token);
            SyncAccounts(); LoadEditor(Accounts.FirstOrDefault()); Report("配置已导入");
        });
        ExportCommand = Command(async token =>
        {
            var path = _dialogs.SaveFile("导出已保存的配置", "账号配置|*.json", "accounts.json");
            if (path is not null) { await Store(path).SaveAsync(new(_workspace.Accounts.Select(x => x.Profile).ToImmutableArray()), token); Report("已保存的配置已导出"); }
        });
        ExportDiagnosticsCommand = Command(async _ =>
        {
            var path = _dialogs.SaveFile("导出诊断", "诊断包|*.zip", "smart-diagnostics.zip"); if (path is null) return;
            var logs = Path.Combine(Path.GetDirectoryName(_configPath)!, "logs");
            await DiagnosticBundle.ExportAsync(path, Directory.Exists(logs) ? Directory.GetFiles(logs, "smart-*.jsonl") : [], _workspace.Accounts.Select(x => (x.Profile.Id, x.Status)).ToArray());
            Report("诊断包已导出");
        });
        CancelCommand = new RelayCommand(() => _operationCancellation?.Cancel());
        _guardedCommands.AddRange([NewCommand, BrowseLibraryCommand, ClearBindingCommand]);
        Editor.PropertyChanged += EditorChanged;
    }
    private static JsonConfigStore<HostSettings> Store(string path) => new(path, 1, x => x.Validate());
    private IAsyncRelayCommand Command(Func<CancellationToken, Task> action, bool hardware = false, Func<bool>? allowed = null)
    {
        var command = new AsyncRelayCommand(() => RunOperationAsync(action), () =>
            (hardware ? CanUseHardware : CanEdit) && (allowed?.Invoke() ?? true));
        _guardedCommands.Add(command); return command;
    }
    public async Task InitializeAsync()
    {
        if (File.Exists(_configPath)) await _workspace.LoadAsync(_lifetime.Token);
        else
        {
            var profile = new AccountProfile("local");
            var result = await _verifier.VerifyAsync(profile, _lifetime.Token);
            result.ValidateFor(profile);
            await _workspace.SaveAccountAsync(profile, token: _lifetime.Token);
        }
        SyncAccounts(); LoadEditor(Accounts.FirstOrDefault()); Refresh();
    }
    private Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _closed) return Task.CompletedTask;
        _operation = ExecuteAsync(); return _operation;
        async Task ExecuteAsync()
        {
            IsBusy = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _operationCancellation = cancellation;
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { Report("操作已取消"); if (DmaResult == "正在测试…") DmaResult = "已取消"; if (InputResult == "正在握手…") InputResult = "已取消"; }
            catch (Exception ex)
            {
                if (DmaResult == "正在测试…") DmaResult = "未通过 · " + ex.Message;
                if (InputResult == "正在握手…") InputResult = "未通过 · " + ex.Message;
                Report("操作未完成：" + ex.Message);
            }
            finally { _operationCancellation = null; IsBusy = false; Refresh(); }
        }
    }
    private async Task SaveAsync(CancellationToken token)
    {
        var profile = Editor.ToProfile();
        if (_verifiedRevision != _editorRevision || _verifiedProfile != JsonSerializer.Serialize(profile))
            throw new InvalidOperationException("请先验证当前配置；硬件配置必须读取到角色并完成 KMBox 握手后才能保存。");
        if (profile.Mode == RuntimeMode.Hardware && !_dialogs.Confirm(
            $"本次读到角色：{_verifiedCharacter!.Name}\n角色 ID：{_verifiedCharacter.Id}\n设备：{profile.Dma!.Binding!.Identity.SerialNumber}\n确认这是此配置对应的角色并保存？")) return;
        profile = profile with { TestedCharacter = _verifiedCharacter };
        await _workspace.SaveAccountAsync(profile, _editingId, token);
        SyncAccounts(); LoadEditor(Accounts.Single(x => x.Id == profile.Id)); Report("配置已保存 · " + profile.Id);
    }
    private async Task VerifyAsync(CancellationToken token)
    {
        InvalidateVerification("正在验证配置…");
        var revision = _editorRevision;
        try
        {
            var profile = Editor.ToProfile();
            if (profile.Mode == RuntimeMode.Hardware) RequireDmaIdle();
            var signature = JsonSerializer.Serialize(profile);
            var result = await _verifier.VerifyAsync(profile, token);
            token.ThrowIfCancellationRequested(); result.ValidateFor(profile);
            if (_closed || revision != _editorRevision || signature != JsonSerializer.Serialize(Editor.ToProfile()))
                throw new InvalidOperationException("验证期间配置已修改，请重新验证。");
            _verifiedProfile = signature; _verifiedRevision = revision; _verifiedCharacter = result.Character;
            VerificationResult = profile.Mode == RuntimeMode.Mock ? "模拟配置验证通过（未验证真实角色），可以保存" :
                $"验证通过 · 角色 {result.Character!.Name} · ID {result.Character.Id} · PID {result.Target!.ProcessId} · KMBox 已握手";
            Report(VerificationResult);
        }
        catch (Exception ex)
        {
            InvalidateVerification(ex is OperationCanceledException ? "验证已取消，不能保存" : "验证未通过，不能保存：" + ex.Message);
            throw;
        }
        finally { NotifyCommands(); }
    }
    private void InvalidateVerification(string message)
    {
        _verifiedRevision = -1; _verifiedProfile = null; _verifiedCharacter = null; VerificationResult = message;
        NotifyCommands();
    }
    private async Task StopSelectedAsync(bool pause)
    {
        var account = SelectedAccount?.Account; if (account is null) return;
        try { if (pause) await account.PauseAsync(TimeSpan.FromSeconds(10)); else await account.StopAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) { Report("停止尚未完成：" + ex.Message); }
        finally { Refresh(); }
    }
    private bool ConfirmDiscard() => !IsDirty || _dialogs.Confirm("当前修改尚未保存，放弃这些修改？");
    public bool ConfirmClose() => ConfirmDiscard();
    partial void OnSelectedAccountChanged(AccountRow? oldValue, AccountRow? newValue)
    {
        if (_loadingEditor) return;
        if (IsBusy || !ConfirmDiscard()) { _loadingEditor = true; SelectedAccount = oldValue; _loadingEditor = false; return; }
        LoadEditor(newValue);
    }
    private void LoadEditor(AccountRow? row)
    {
        _loadingEditor = true;
        try
        {
            SelectedAccount = row; _editingId = row?.Id; Editor.PropertyChanged -= EditorChanged;
            Editor = row is null ? new() { Id = NextId() } : ProfileEditor.From(row.Account.Profile);
            Editor.PropertyChanged += EditorChanged; Devices.Clear(); Processes.Clear(); SelectedDevice = null; SelectedProcess = null;
            if (Editor.Binding is { } binding) { Devices.Add(new(binding)); SelectedDevice = Devices[0]; }
            if (!string.IsNullOrWhiteSpace(Editor.ProcessName) || Editor.ProcessId != "0")
            { Processes.Add(new(int.Parse(Editor.ProcessId), Editor.ProcessName)); SelectedProcess = Processes[0]; }
            IsDirty = false; DmaResult = InputResult = "尚未测试";
            _editorRevision++; InvalidateVerification("尚未验证；新增或修改后须验证才能保存");
        }
        finally { _loadingEditor = false; NotifyCommands(); }
    }
    private string NextId() { var i = 1; while (Accounts.Any(x => x.Id == "account-" + i)) i++; return "account-" + i; }
    partial void OnSelectedDeviceChanged(DeviceChoice? value)
    { if (!_loadingEditor && value is not null) Editor.SetBinding(value.Binding); }
    partial void OnSelectedProcessChanged(ProcessChoice? value)
    { if (!_loadingEditor && value is not null) { Editor.ProcessId = value.Pid.ToString(); Editor.ProcessName = value.Name; } }
    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingEditor) return;
        IsDirty = true;
        _editorRevision++; InvalidateVerification("配置已修改，请重新验证后保存");
        if (e.PropertyName is nameof(ProfileEditor.LibraryPath) or nameof(ProfileEditor.BindingDriverFileName)) { Devices.Clear(); SelectedDevice = null; }
        if (e.PropertyName is nameof(ProfileEditor.LibraryPath) or nameof(ProfileEditor.DeviceUri) or nameof(ProfileEditor.ArgumentsJson)) { Processes.Clear(); SelectedProcess = null; }
        DmaResult = InputResult = "配置已修改，请重新测试"; NotifyCommands();
    }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanUseHardware));
        OnPropertyChanged(nameof(CanSave));
        foreach (var command in _guardedCommands) command.NotifyCanExecuteChanged();
    }
    private void RequireHardware() { if (!Editor.IsHardware) throw new InvalidOperationException("请切换到 Hardware 模式后测试硬件。"); }
    private void RequireDmaIdle()
    {
        RequireHardware();
        if (_workspace.Accounts.Any(x => x.Profile.Dma?.DeviceUri == Editor.DeviceUri && x.Status.State is not (SessionState.Stopped or SessionState.Paused or SessionState.Faulted)))
            throw new InvalidOperationException("该 DMA 正被账号使用，请先停止相关账号再做独立连接测试。");
    }
    private void SyncAccounts()
    {
        _loadingEditor = true;
        try { Accounts.Clear(); foreach (var account in _workspace.Accounts) Accounts.Add(new(account)); }
        finally { _loadingEditor = false; }
    }
    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in Accounts) row.Refresh(now);
        Overview = $"{Accounts.Count(x => x.Account.Status.State == SessionState.Running)} / {Accounts.Count} 个账号运行中";
    }
    private void Report(string message)
    {
        Notice = message; Messages.Insert(0, new(DateTimeOffset.Now.ToString("HH:mm:ss"), message));
        while (Messages.Count > 100) Messages.RemoveAt(Messages.Count - 1);
    }
    public async Task ShutdownAsync()
    {
        if (_shutdownComplete) return;
        _closed = true; NotifyCommands(); await _lifetime.CancelAsync();
        if (_operation is { } pending) await pending;
        await _workspace.DisposeAsync(); _lifetime.Dispose(); _shutdownComplete = true;
    }
}
