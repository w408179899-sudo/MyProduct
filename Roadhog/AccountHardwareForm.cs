using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog;

public sealed class AccountHardwareForm : Form
{
    private sealed record Choice(HardwareDeviceFeature Device)
    {
        public override string ToString() => Device.BindingKey + " · " + Device.VmmDeviceName;
    }
    private readonly AccountConfig _original;
    private readonly Func<AccountConfig, CancellationToken, Task<HardwareVerification>> _verify;
    private readonly Func<AccountConfig, IWin32Window, Task>? _authorize;
    private readonly TextBox _name = new();
    private readonly ComboBox _device = new();
    private readonly TextBox _vmm = new();
    private readonly TextBox _ip = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 1000 };
    private readonly TextBox _mac = new();
    private readonly CheckBox _recover = new() { Text = "异常退出后自动恢复此账号", AutoSize = true };
    private readonly CheckBox _confirm = new() { Text = "确认上方角色和设备属于此账号", AutoSize = true };
    private readonly Label _result = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "保存硬件配置", AutoSize = true };
    private CancellationTokenSource? _operation;
    private HardwareVerification? _verification;
    private string? _verifiedFingerprint;
    private bool _busy;
    private bool _closeAfterCancel;

    public AccountConfig Config { get; private set; }

    public AccountHardwareForm(AccountConfig config, IReadOnlyList<HardwareDeviceFeature> devices,
        Func<AccountConfig, CancellationToken, Task<HardwareVerification>> verify,
        Func<AccountConfig, IWin32Window, Task>? authorize = null)
    {
        Config = config.Clone(); _original = config.Clone(); _verify = verify; _authorize = authorize;
        Text = config.AccountName + " · 设备与角色"; Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(670, 520); MinimumSize = Size; MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 10 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 100)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        void Field(string label, Control input, int row)
        {
            root.RowStyles.Add(new(SizeType.Absolute, 38));
            root.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, row);
            input.Dock = DockStyle.Fill; root.Controls.Add(input, 1, row);
        }
        Field("账号备注", _name, 0); Field("DMA设备", _device, 1); Field("读取VMM", _vmm, 2);
        Field("KMBox IP", _ip, 3); Field("KMBox端口", _port, 4); Field("KMBox MAC", _mac, 5);
        _name.Text = config.AccountName;
        _device.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var device in devices) _device.Items.Add(new Choice(device));
        var selected = devices.FirstOrDefault(d => d.BindingKey.Equals(config.HardwareKey, StringComparison.OrdinalIgnoreCase) || d.AliasKeys.Contains(config.HardwareKey, StringComparer.OrdinalIgnoreCase));
        if (selected is not null) _device.SelectedItem = _device.Items.Cast<Choice>().First(c => c.Device == selected);
        _vmm.Text = config.VmmDeviceName;
        _ip.Text = config.KmBox?.IpAddress ?? ""; _port.Value = Math.Clamp(config.KmBox?.Port ?? 1000, 1, 65535); _mac.Text = config.KmBox?.Mac ?? "";
        _recover.Checked = config.AutoRecover;
        root.RowStyles.Add(new(SizeType.Absolute, 30)); root.Controls.Add(_recover, 1, 6);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var verifyButton = new Button { Text = "测试读取角色 / 检查设备", AutoSize = true };
        var authorizeButton = new Button { Text = "账号授权", AutoSize = true, Enabled = authorize is not null };
        verifyButton.Click += async (_, _) => await VerifyAsync();
        authorizeButton.Click += async (_, _) =>
        {
            if (_busy || _authorize is null || IsDisposed || Disposing) return;
            try { _busy = true; UpdateSave(); await _authorize(ReadDraft(requireDevice: false), this); }
            catch (Exception ex) { if (!IsDisposed && !Disposing) _result.Text = ex.Message; }
            finally
            {
                _busy = false;
                if (!IsDisposed && !Disposing) { UpdateSave(); if (_closeAfterCancel) Close(); }
            }
        };
        actions.Controls.AddRange([verifyButton, authorizeButton]); root.RowStyles.Add(new(SizeType.Absolute, 38)); root.Controls.Add(actions, 1, 7);
        _result.Text = !Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(config)
            ? "本次开机需要重新验证。上方可编辑 DMA、读取编号和 KMBox；测试读取角色，核对后勾选确认并保存硬件配置。"
            : "已保存角色：" + config.CharacterName + "。可重新测试读取角色；修改设备后需要重新验证。";
        root.RowStyles.Add(new(SizeType.Absolute, 60)); root.Controls.Add(_result, 0, 8); root.SetColumnSpan(_result, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _confirm.Enabled = false; _save.Click += (_, _) => Save();
        footer.Controls.AddRange([_confirm, _save]); root.RowStyles.Add(new(SizeType.Absolute, 45)); root.Controls.Add(footer, 0, 9); root.SetColumnSpan(footer, 2);
        foreach (var field in new[] { _name, _vmm, _ip, _mac }) field.TextChanged += (_, _) => InvalidateVerification();
        _port.ValueChanged += (_, _) => InvalidateVerification();
        _device.SelectedIndexChanged += (_, _) => { if (_device.SelectedItem is Choice choice) _vmm.Text = choice.Device.VmmDeviceName; InvalidateVerification(); };
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
        UpdateSave();
    }

    private AccountConfig ReadDraft(bool requireDevice = true)
    {
        var config = _original.Clone(); config.AccountName = _name.Text.Trim(); config.AutoRecover = _recover.Checked;
        if (string.IsNullOrWhiteSpace(config.AccountName)) throw new InvalidOperationException("请输入账号备注。");
        if (_device.SelectedItem is Choice choice)
        {
            var device = choice.Device; config.HardwareKey = device.BindingKey; config.HardwareBindingKind = device.BindingKind;
            config.HardwareBindingConfidence = device.BindingConfidence; config.HardwareDeviceInstanceId = device.DeviceInstanceId;
            config.HardwareLocationKey = device.LocationKey; config.HardwareDisplayName = device.DisplayName;
        }
        else if (requireDevice) throw new InvalidOperationException("请选择在线的 DMA 设备。");
        config.VmmDeviceName = _vmm.Text.Trim();
        config.KmBox = new AccountKmBoxSettings { IpAddress = _ip.Text.Trim(), Port = (int)_port.Value, Mac = _mac.Text.Trim() };
        if (requireDevice && !config.KmBox.Validate(out var error)) throw new InvalidOperationException(error);
        if (requireDevice && (string.IsNullOrWhiteSpace(config.VmmDeviceName) || config.VmmDeviceName.Equals("fpga", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("请选择明确的 VMM 设备，例如 fpga://devindex=0。");
        // The target game PID must be resolved again after a hardware change.
        if (!SameHardware(config, _original)) config.ProcessId = 0;
        return config;
    }

    private static bool SameHardware(AccountConfig a, AccountConfig b) =>
        a.AccountName == b.AccountName && a.HardwareKey == b.HardwareKey && a.HardwareDeviceInstanceId == b.HardwareDeviceInstanceId && a.VmmDeviceName == b.VmmDeviceName &&
        a.KmBox?.IpAddress == b.KmBox?.IpAddress && a.KmBox?.Port == b.KmBox?.Port && a.KmBox?.Mac == b.KmBox?.Mac;
    private static string Fingerprint(AccountConfig config) => System.Text.Json.JsonSerializer.Serialize(new { config.AccountName, config.HardwareKey, config.HardwareDeviceInstanceId, config.VmmDeviceName, config.KmBox });
    private void InvalidateVerification()
    {
        if (IsDisposed || Disposing) return;
        _verification = null; _verifiedFingerprint = null; _confirm.Checked = false; _confirm.Enabled = false; _operation?.Cancel(); UpdateSave();
    }
    private void UpdateSave()
    {
        if (IsDisposed || Disposing) return;
        try { _save.Enabled = !_busy && (_verification is not null ? _confirm.Checked : SameHardware(ReadDraft(), _original) && Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(_original)); }
        catch { _save.Enabled = false; }
    }
    private async Task VerifyAsync()
    {
        if (_busy || IsDisposed || Disposing) return;
        try
        {
            var draft = ReadDraft(); var fingerprint = Fingerprint(draft);
            InvalidateVerification();
            _busy = true; _operation = new CancellationTokenSource(TimeSpan.FromSeconds(45)); UpdateSave();
            _result.Text = "正在连接设备、读取角色并验证 KMBox…";
            var proof = await _verify(draft, _operation.Token);
            if (IsDisposed || Disposing) return;
            _operation.Token.ThrowIfCancellationRequested();
            if (fingerprint != Fingerprint(ReadDraft())) return;
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
            if (!IsDisposed && !Disposing) { UpdateSave(); if (_closeAfterCancel) Close(); }
        }
    }
    private void Save()
    {
        if (_busy || IsDisposed || Disposing) return;
        try
        {
            var draft = ReadDraft();
            if (_verification is not null || !SameHardware(draft, _original) || !Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(_original))
            {
                if (_verification is null || !_confirm.Checked || _verifiedFingerprint != Fingerprint(draft)) throw new InvalidOperationException("请先验证并确认该账号角色。");
                draft.CharacterName = _verification.CharacterName;
                if (string.IsNullOrWhiteSpace(_verification.SessionId) || _verification.SessionId != Infrastructure.Hardware.HardwareVerificationSession.CurrentId)
                    throw new InvalidOperationException("请重新测试本次开机的硬件连接。");
                draft.HardwareVerificationSessionId = _verification.SessionId;
            }
            Config = draft; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _result.Text = ex.Message; }
    }
}
