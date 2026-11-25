using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog;

public sealed class AccountHardwareForm : Form
{
    private sealed record Choice(HardwareDeviceFeature Device, string Owners = "")
    {
        public override string ToString() => Device.DisplayName + " · " + Device.BindingKey + (Owners.Length == 0 ? "" : "（已绑定：" + Owners + "）");
    }
    private sealed record VmmChoice(string Value, string Owners)
    {
        public override string ToString() => Value + (Owners.Length == 0 ? "" : "（已绑定：" + Owners + "）");
    }
    private readonly AccountConfig _original;
    private readonly Func<AccountConfig, CancellationToken, Task<HardwareVerification>> _verify;
    private readonly Func<AccountConfig, IWin32Window, Task>? _authorize;
    private readonly Func<IReadOnlyList<HardwareDeviceFeature>>? _refreshDevices;
    private readonly Func<HardwareSelectionAvailability>? _availability;
    private readonly Func<AccountConfig, CancellationToken, Task>? _testDma;
    private readonly Func<AccountKmBoxSettings, CancellationToken, Task>? _testKmBox;
    private HardwareSelectionAvailability? _currentAvailability;
    private IReadOnlyList<HardwareDeviceFeature> _allDevices;
    private HardwareDeviceFeature? _draftDevice;
    private readonly TextBox _name = new();
    private readonly TextBox _loginAccount = new() { Name = "GameLoginAccount" };
    private readonly TextBox _loginPassword = new() { Name = "GameLoginPassword", UseSystemPasswordChar = true };
    private readonly ComboBox _device = new();
    private readonly ComboBox _vmm = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _binding = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ip = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 1000 };
    private readonly TextBox _mac = new();
    private readonly CheckBox _recover = new() { Text = "异常退出后自动恢复此账号", AutoSize = true };
    private readonly CheckBox _confirm = new() { Text = "确认上方角色和设备属于此账号", AutoSize = true };
    private readonly Label _result = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "保存配置", AutoSize = true };
    private readonly Button _testConnection = new() { Text = "联通测试", AutoSize = true };
    private readonly Label _dmaConnection = new() { Name = "DmaConnectionStatus", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _kmBoxConnection = new() { Name = "KmBoxConnectionStatus", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly ToolTip _connectionDetails = new();
    private CancellationTokenSource? _connectionOperation;
    private int _dmaRevision;
    private int _kmBoxRevision;
    private CancellationTokenSource? _operation;
    private HardwareVerification? _verification;
    private string? _verifiedFingerprint;
    private bool _busy;
    private bool _closeAfterCancel;
    private bool _saveAfterCancel;

    public AccountConfig Config { get; private set; }

    public AccountHardwareForm(AccountConfig config, IReadOnlyList<HardwareDeviceFeature> devices,
        Func<AccountConfig, CancellationToken, Task<HardwareVerification>> verify,
        Func<AccountConfig, IWin32Window, Task>? authorize = null,
        Func<IReadOnlyList<HardwareDeviceFeature>>? refreshDevices = null,
        Func<HardwareSelectionAvailability>? availability = null,
        Func<AccountConfig, CancellationToken, Task>? testDma = null,
        Func<AccountKmBoxSettings, CancellationToken, Task>? testKmBox = null)
    {
        Config = config.Clone(); _original = config.Clone(); _verify = verify; _authorize = authorize;
        _refreshDevices = refreshDevices;
        _availability = availability; _allDevices = devices;
        _testDma = testDma; _testKmBox = testKmBox;
        _currentAvailability = availability?.Invoke();
        Text = config.AccountName + " · 设备与角色"; Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(720, 696); MinimumSize = Size; MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 13 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 100)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        void Field(string label, Control input, int row)
        {
            root.RowStyles.Add(new(SizeType.Absolute, 38));
            root.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, row);
            input.Dock = DockStyle.Fill; root.Controls.Add(input, 1, row);
        }
        var deviceRow = ConnectionRow(_device, _dmaConnection);
        deviceRow.ColumnCount = 3;
        deviceRow.ColumnStyles.Insert(1, new(SizeType.Absolute, 95));
        deviceRow.SetColumn(_dmaConnection, 2);
        var refreshButton = new Button { Text = "刷新设备", Dock = DockStyle.Fill, Enabled = refreshDevices is not null };
        deviceRow.Controls.Add(refreshButton, 1, 0);
        refreshButton.Click += (_, _) => RefreshDevices();
        Field("账号备注", _name, 0); Field("DMA设备", deviceRow, 1); Field("读取编号", _vmm, 2);
        Field("KMBox IP", ConnectionRow(_ip, _kmBoxConnection), 3); Field("KMBox端口", _port, 4); Field("KMBox MAC", _mac, 5);
        Field("游戏账号", _loginAccount, 6); Field("游戏密码", _loginPassword, 7);
        _name.Text = config.AccountName;
        _loginAccount.Text = config.GameLoginAccount;
        string? passwordError = null;
        try { _loginPassword.Text = Infrastructure.Login.LoginPasswordProtector.Unprotect(config.ProtectedGameLoginPassword); }
        catch (InvalidOperationException exception) { passwordError = exception.Message; }
        _device.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var device in devices.Where(d => _currentAvailability?.DeviceBusy(d) != true))
            _device.Items.Add(new Choice(device, _currentAvailability?.DeviceOwners(device) ?? ""));
        var selected = _device.Items.Cast<Choice>().Select(c => c.Device).FirstOrDefault(d => d.BindingKey.Equals(config.HardwareKey, StringComparison.OrdinalIgnoreCase) || d.AliasKeys.Contains(config.HardwareKey, StringComparer.OrdinalIgnoreCase));
        _draftDevice = selected;
        if (selected is not null) _device.SelectedItem = _device.Items.Cast<Choice>().First(c => c.Device == selected);
        _vmm.Text = config.VmmDeviceName;
        PopulateVmmChoices(devices);
        _ip.Text = config.KmBox?.IpAddress ?? ""; _port.Value = Math.Clamp(config.KmBox?.Port ?? 1000, 1, 65535); _mac.Text = config.KmBox?.Mac ?? "";
        _recover.Checked = config.AutoRecover;
        root.RowStyles.Add(new(SizeType.Absolute, 30)); root.Controls.Add(_recover, 1, 8);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var verifyButton = new Button { Text = "测试读取角色 / 检查设备", AutoSize = true };
        var authorizeButton = new Button { Text = "账号授权", AutoSize = true, Enabled = authorize is not null };
        verifyButton.Click += async (_, _) => await VerifyAsync();
        _testConnection.Enabled = _testDma is not null && _testKmBox is not null;
        _testConnection.Click += async (_, _) => await TestConnectionAsync();
        authorizeButton.Click += async (_, _) =>
        {
            if (_busy || _authorize is null || IsDisposed || Disposing) return;
            try { _busy = true; UpdateSave(); await _authorize(ReadDraft(requireDevice: false), this); }
            catch (Exception ex) { if (!IsDisposed && !Disposing) _result.Text = ex.Message; }
            finally
            {
                _busy = false;
                FinishOperation();
            }
        };
        actions.Controls.AddRange([_testConnection, verifyButton, authorizeButton]); root.RowStyles.Add(new(SizeType.Absolute, 38)); root.Controls.Add(actions, 1, 9);
        _result.Text = !Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(config)
            ? "本次开机尚未确认角色。核对角色并确认后即可启动。"
            : "已保存角色：" + config.CharacterName + "。可重新测试读取角色；修改设备后需要重新验证。";
        if (passwordError is not null) _result.Text = passwordError;
        root.RowStyles.Add(new(SizeType.Absolute, 60)); root.Controls.Add(_result, 0, 10); root.SetColumnSpan(_result, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _confirm.Enabled = false; _save.Click += (_, _) => Save();
        footer.Controls.AddRange([_confirm, _save]); root.RowStyles.Add(new(SizeType.Absolute, 45)); root.Controls.Add(footer, 0, 11); root.SetColumnSpan(footer, 2);
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.Controls.Add(_binding, 0, 12); root.SetColumnSpan(_binding, 2);
        _vmm.TextChanged += (_, _) => DmaChanged();
        foreach (var field in new Control[] { _ip, _mac }) field.TextChanged += (_, _) => KmBoxChanged();
        _port.ValueChanged += (_, _) => KmBoxChanged();
        _device.SelectedIndexChanged += (_, _) =>
        {
            if (_device.SelectedItem is Choice choice)
            {
                _draftDevice = choice.Device;
                SelectVmm(ExplicitVmm(choice.Device.VmmDeviceName));
            }
            DmaChanged();
            ShowBinding();
        };
        _confirm.CheckedChanged += (_, _) => UpdateSave();
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            _closeAfterCancel = true;
            _operation?.Cancel();
            // Let Windows finish shutdown even if a native verification has not returned yet.
            e.Cancel = e.CloseReason != CloseReason.WindowsShutDown;
            if (e.Cancel) _result.Text = "正在取消验证并释放设备…";
        };
        FormClosed += (_, _) => _connectionOperation?.Cancel();
        Disposed += (_, _) => { _connectionOperation?.Cancel(); _connectionDetails.Dispose(); };
        SetConnectionStatus(_dmaConnection, "未测试", SystemColors.GrayText);
        SetConnectionStatus(_kmBoxConnection, "未测试", SystemColors.GrayText);
        if (_device.Items.Count == 0) _result.Text = "暂无空闲 DMA 设备，请先停止占用设备的账号，再刷新设备。";
        ShowBinding(); UpdateSave();
    }

    private static TableLayoutPanel ConnectionRow(Control input, Label status)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.Absolute, 82));
        input.Dock = DockStyle.Fill;
        row.Controls.Add(input, 0, 0);
        row.Controls.Add(status, 1, 0);
        return row;
    }

    private static string ExplicitVmm(string value) => value.Equals("fpga", StringComparison.OrdinalIgnoreCase) ? "fpga://devindex=0" : value;
    private string ReadVmm() => _vmm.SelectedItem is VmmChoice choice && _vmm.Text == choice.ToString() ? choice.Value : _vmm.Text.Trim();
    private void SelectVmm(string value)
    {
        _vmm.SelectedIndex = -1;
        var choice = _vmm.Items.Cast<VmmChoice>().FirstOrDefault(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase));
        if (choice is not null) _vmm.SelectedItem = choice;
        else _vmm.Text = value;
    }

    private void PopulateVmmChoices(IReadOnlyList<HardwareDeviceFeature> devices)
    {
        var current = ReadVmm();
        _vmm.Items.Clear();
        foreach (var value in Enumerable.Range(0, devices.Count).Select(i => "fpga://devindex=" + i)
            .Concat(devices.Select(d => ExplicitVmm(d.VmmDeviceName))).Append(current)
            .Where(v => !string.IsNullOrWhiteSpace(v) && _currentAvailability?.VmmBusy(v) != true).Distinct(StringComparer.OrdinalIgnoreCase))
            _vmm.Items.Add(new VmmChoice(value, _currentAvailability?.VmmOwners(value) ?? ""));
        SelectVmm(ExplicitVmm(current));
    }

    private void ShowBinding()
    {
        var selected = (_device.SelectedItem as Choice)?.Device;
        var changed = selected is not null && !string.Equals(selected.DeviceInstanceId, _original.HardwareDeviceInstanceId, StringComparison.OrdinalIgnoreCase);
        _binding.Text = $"原角色：{_original.CharacterName} · 原设备：{_original.HardwareKey} · 原编号：{_original.VmmDeviceName}\n"
            + (selected is null ? "配置的设备当前不可选，保存时保留绑定。" : changed ? "设备身份已变化，启动前请重新确认角色。" : "选择 DMA 和读取编号后可测试读取角色。")
            + "\n读取编号可手动输入；设备列表顺序不代表实际读取顺序。";
    }

    private void RefreshDevices()
    {
        if (_busy) return;
        try
        {
            var selected = _draftDevice;
            var vmm = ReadVmm();
            DmaChanged();
            _currentAvailability = _availability?.Invoke();
            _allDevices = _refreshDevices?.Invoke() ?? _allDevices;
            var devices = _allDevices.Where(d => _currentAvailability?.DeviceBusy(d) != true).ToArray();
            _device.Items.Clear();
            foreach (var device in devices) _device.Items.Add(new Choice(device, _currentAvailability?.DeviceOwners(device) ?? ""));
            var match = devices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId, selected?.DeviceInstanceId, StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault(d => string.Equals(d.BindingKey, selected?.BindingKey ?? _original.HardwareKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null) _device.SelectedItem = _device.Items.Cast<Choice>().First(c => c.Device == match);
            _vmm.SelectedIndex = -1; _vmm.Text = vmm; PopulateVmmChoices(_allDevices);
            _result.Text = devices.Length == 0 ? "暂无空闲 DMA 设备，请先停止占用设备的账号，再刷新设备。"
                : $"可选 DMA：{devices.Length} 个，已隐藏占用中的设备和读取编号。标注“已绑定”的设备可重新选择并保存；停止账号的旧绑定不占用设备。";
        }
        catch (Exception ex) { _result.Text = "刷新设备失败：" + ex.Message; }
        ShowBinding(); UpdateSave();
    }

    private AccountConfig ReadDraft(bool requireDevice = true)
    {
        var config = ReadDmaDraft(requireDevice); config.AccountName = _name.Text.Trim(); config.AutoRecover = _recover.Checked;
        config.GameLoginAccount = _loginAccount.Text.Trim();
        config.ProtectedGameLoginPassword = Infrastructure.Login.LoginPasswordProtector.Protect(_loginPassword.Text);
        if (string.IsNullOrWhiteSpace(config.AccountName)) throw new InvalidOperationException("请输入账号备注。");
        config.KmBox = ReadKmBox();
        if (requireDevice && !config.KmBox.Validate(out var error)) throw new InvalidOperationException(error);
        if (!SameHardware(config, _original)) config.ProcessId = 0;
        return config;
    }

    private AccountKmBoxSettings ReadKmBox() => new() { IpAddress = _ip.Text.Trim(), Port = (int)_port.Value, Mac = _mac.Text.Trim() };

    private AccountConfig ReadDmaDraft(bool requireDevice)
    {
        var config = _original.Clone();
        if (requireDevice && _device.SelectedItem is not Choice) throw new InvalidOperationException("请选择在线的 DMA 设备。");
        if (_draftDevice is { } device)
        {
            config.HardwareKey = device.BindingKey; config.HardwareBindingKind = device.BindingKind;
            config.HardwareBindingConfidence = device.BindingConfidence; config.HardwareDeviceInstanceId = device.DeviceInstanceId;
            config.HardwareLocationKey = device.LocationKey; config.HardwareDisplayName = device.DisplayName;
        }
        config.VmmDeviceName = ReadVmm();
        if (requireDevice && (string.IsNullOrWhiteSpace(config.VmmDeviceName) || config.VmmDeviceName.Equals("fpga", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("请选择明确的 VMM 设备，例如 fpga://devindex=0。");
        return config;
    }

    private static bool SameHardware(AccountConfig a, AccountConfig b) =>
        a.HardwareKey == b.HardwareKey && a.HardwareDeviceInstanceId == b.HardwareDeviceInstanceId && a.VmmDeviceName == b.VmmDeviceName &&
        a.KmBox?.IpAddress == b.KmBox?.IpAddress && a.KmBox?.Port == b.KmBox?.Port && a.KmBox?.Mac == b.KmBox?.Mac;
    private static string Fingerprint(AccountConfig config) => System.Text.Json.JsonSerializer.Serialize(new { config.HardwareKey, config.HardwareDeviceInstanceId, config.VmmDeviceName, config.KmBox });
    private void InvalidateVerification()
    {
        if (IsDisposed || Disposing) return;
        _verification = null; _verifiedFingerprint = null; _confirm.Checked = false; _confirm.Enabled = false; _operation?.Cancel(); UpdateSave();
    }
    private void UpdateSave()
    {
        if (IsDisposed || Disposing) return;
        _save.Enabled = true;
    }

    private void DmaChanged()
    {
        _dmaRevision++;
        SetConnectionStatus(_dmaConnection, "未测试", SystemColors.GrayText);
        InvalidateVerification();
    }

    private void KmBoxChanged()
    {
        _kmBoxRevision++;
        SetConnectionStatus(_kmBoxConnection, "未测试", SystemColors.GrayText);
        InvalidateVerification();
    }

    private void SetConnectionStatus(Label status, string text, Color color, string? reason = null)
    {
        status.Text = text;
        status.ForeColor = color;
        status.AccessibleDescription = reason ?? text;
        _connectionDetails.SetToolTip(status, reason);
    }

    private async Task TestConnectionAsync()
    {
        if (_testDma is null || _testKmBox is null || _connectionOperation is not null || _busy || IsDisposed || Disposing) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _connectionOperation = operation;
        _testConnection.Enabled = false;
        var dma = ReadDmaDraft(requireDevice: false);
        dma.KmBox = null; // Availability for this probe depends only on DMA.
        var kmBox = ReadKmBox();
        var dmaRevision = _dmaRevision;
        var kmBoxRevision = _kmBoxRevision;
        try
        {
            await Task.WhenAll(
                CheckConnectionAsync(_dmaConnection, () => dmaRevision == _dmaRevision,
                    () => _testDma(dma, operation.Token), operation.Token),
                CheckConnectionAsync(_kmBoxConnection, () => kmBoxRevision == _kmBoxRevision,
                    () => _testKmBox(kmBox, operation.Token), operation.Token));
        }
        finally
        {
            _connectionOperation = null;
            if (!IsDisposed && !Disposing) _testConnection.Enabled = true;
        }
    }

    private async Task CheckConnectionAsync(Label status, Func<bool> isCurrent, Func<Task> check, CancellationToken token)
    {
        SetConnectionStatus(status, "测试中…", SystemColors.GrayText);
        try
        {
            await check();
            if (!IsDisposed && !Disposing && isCurrent())
                SetConnectionStatus(status, "✓ 已联通", Color.DarkGreen);
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || !isCurrent()) return;
            var reason = ex is OperationCanceledException && token.IsCancellationRequested ? "联通测试已取消或超时。" : ex.Message;
            var result = reason.Contains("占用") || reason.Contains("正在被") ? "× 占用" :
                reason.Contains("不在线") ? "× 离线" : "× 失败";
            SetConnectionStatus(status, result, Color.DarkRed, reason);
        }
    }

    private void FinishOperation()
    {
        if (IsDisposed || Disposing) return;
        if (_closeAfterCancel) Close();
        else if (_saveAfterCancel)
        {
            _saveAfterCancel = false;
            Save();
        }
    }

    private async Task VerifyAsync()
    {
        if (_busy || IsDisposed || Disposing) return;
        if (_connectionOperation is not null)
        {
            _result.Text = "联通测试进行中，请完成后再读取角色。";
            return;
        }
        try
        {
            var draft = ReadDraft(); var fingerprint = Fingerprint(draft);
            InvalidateVerification();
            _availability?.Invoke().EnsureAvailable(draft);
            _busy = true; _operation = new CancellationTokenSource(TimeSpan.FromSeconds(45)); UpdateSave();
            _result.Text = "正在连接设备、读取角色并验证 KMBox…";
            var proof = await _verify(draft, _operation.Token);
            if (IsDisposed || Disposing) return;
            _operation.Token.ThrowIfCancellationRequested();
            if (fingerprint != Fingerprint(ReadDraft())) return;
            if (string.IsNullOrWhiteSpace(proof.CharacterName) || !proof.KmBoxConnected
                || !string.Equals(proof.HardwareKey, draft.HardwareKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(proof.VmmDeviceName, draft.VmmDeviceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("验证结果与所选设备不匹配，或角色、KMBox 验证未完成，请重新测试。");
            if (string.IsNullOrWhiteSpace(proof.SessionId) || proof.SessionId != Infrastructure.Hardware.HardwareVerificationSession.CurrentId)
                throw new InvalidOperationException("验证记录不属于本次开机，请重新测试读取角色。");
            _verification = proof; _verifiedFingerprint = fingerprint; _confirm.Enabled = true;
            _result.Text = $"读取角色：{proof.CharacterName}\nDMA：{proof.HardwareKey} · VMM：{proof.VmmDeviceName} · KMBox：已连接";
        }
        catch (OperationCanceledException) { if (!IsDisposed && !Disposing) _result.Text = "验证已取消或超时，设备已请求释放。"; }
        catch (Exception ex) { if (!IsDisposed && !Disposing) _result.Text = ex.Message; }
        finally
        {
            _busy = false; _operation?.Dispose(); _operation = null;
            FinishOperation();
        }
    }
    private void Save()
    {
        if (IsDisposed || Disposing) return;
        if (_busy)
        {
            _saveAfterCancel = true;
            _operation?.Cancel();
            _result.Text = "当前操作结束后会保存配置。";
            return;
        }
        try
        {
            var draft = ReadDraft(requireDevice: false);
            if (_verification is { } proof && _confirm.Checked && _verifiedFingerprint == Fingerprint(draft) &&
                !string.IsNullOrWhiteSpace(proof.SessionId) && proof.SessionId == HardwareVerificationSession.CurrentId)
            {
                draft.CharacterName = proof.CharacterName;
                draft.HardwareVerificationSessionId = proof.SessionId;
            }
            else if (!SameHardware(draft, _original))
                draft.HardwareVerificationSessionId = string.Empty;
            Config = draft; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _result.Text = ex.Message; }
    }
}
