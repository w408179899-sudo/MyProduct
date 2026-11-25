using System.Globalization;
using Roadhog.Core.Accounts;

namespace Roadhog;

internal sealed class GlobalSettingsForm : Form
{
    private readonly Func<RoadhogGlobalSettings, CancellationToken, Task> _save;
    private readonly CancellationToken _cancellationToken;
    private readonly TextBox _readCache = new() { Name = "dmaReadCacheInput" };
    private readonly TextBox _noKill = new()
    {
        Name = "noKillReturnInput", PlaceholderText = $"默认 {RoadhogGlobalSettings.DefaultNoKillReturnMinutes}"
    };
    private readonly TextBox _reviveDelay = new()
    {
        Name = "reviveDelayInput", PlaceholderText = $"默认 {RoadhogGlobalSettings.DefaultReviveDelaySeconds}"
    };
    private readonly TextBox _logHours = new() { Name = "logRetentionInput" };
    private readonly TextBox _logSize = new() { Name = "logDirectoryLimitInput" };
    private readonly Label _status = new() { Name = "globalSettingsStatus", AutoSize = true, MaximumSize = new Size(455, 0) };
    private readonly Button _saveButton = new() { Name = "saveGlobalSettings", Text = "保存", Width = 84, Height = 30 };
    private bool _saving;

    public GlobalSettingsForm(RoadhogGlobalSettings settings, Func<RoadhogGlobalSettings, CancellationToken, Task> save,
        CancellationToken cancellationToken, string? warning = null)
    {
        _save = save;
        _cancellationToken = cancellationToken;
        Text = "全局设置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 345);
        MinimumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        BackColor = SettingsPalette.Page;
        ForeColor = SettingsPalette.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 6), AutoScroll = true, ColumnCount = 2
        };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.ColumnStyles.Add(new(SizeType.Absolute, 112));

        AddField(content, "缓存（ms）", _readCache, settings.ReadCacheMs);
        AddWide(content, Heading("挂机恢复"));
        AddField(content, "无击杀回城（分钟）", _noKill, settings.NoKillReturnMinutes);
        AddField(content, "死亡后等待复活（秒，高级）", _reviveDelay, settings.ReviveDelaySeconds);
        AddWide(content, new Label { AutoSize = true, Text = "留空沿用原有默认值或环境配置。0 表示立即执行。" });
        AddWide(content, Heading("日志"));
        AddField(content, "保留时长（小时）", _logHours, settings.LogRetentionHours);
        AddField(content, "每个日志目录上限（MiB）", _logSize, settings.LogDirectoryMiB);
        _status.Text = warning ?? string.Empty;
        AddWide(content, _status);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 6, 12, 0)
        };
        var cancel = new Button { Text = "取消", Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_saveButton);
        Controls.Add(content);
        Controls.Add(buttons);
        AcceptButton = _saveButton;
        CancelButton = cancel;
        _saveButton.Click += async (_, _) => await SaveAsync();
        FormClosing += (_, args) =>
        {
            if (_saving && DialogResult != DialogResult.OK) args.Cancel = true;
        };
    }

    private Label Heading(string title) => new()
    {
        Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 5, 3, 2)
    };

    private static void AddWide(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static void AddField(TableLayoutPanel table, string caption, TextBox input, int? value)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        input.AccessibleName = caption;
        input.MaxLength = 10;
        input.Dock = DockStyle.Fill;
        input.BackColor = SettingsPalette.Input;
        input.Text = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        table.Controls.Add(input, 1, row);
    }

    private static int? ReadInteger(TextBox input, bool optional = false)
    {
        if (optional && string.IsNullOrWhiteSpace(input.Text)) return null;
        if (int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        throw new InvalidDataException($"请输入整数：{input.AccessibleName}。");
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        try
        {
            var settings = new RoadhogGlobalSettings
            {
                ReadCacheMs = ReadInteger(_readCache)!.Value,
                NoKillReturnMinutes = ReadInteger(_noKill, optional: true),
                ReviveDelaySeconds = ReadInteger(_reviveDelay, optional: true),
                LogRetentionHours = ReadInteger(_logHours)!.Value,
                LogDirectoryMiB = ReadInteger(_logSize)!.Value
            };
            if (settings.Validate() is { } error)
            {
                _status.Text = error;
                return;
            }
            _saving = true;
            _status.Text = string.Empty;
            _saveButton.Enabled = false;
            await _save(settings, _cancellationToken);
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _status.Text = "保存已取消。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _saving = false;
            _saveButton.Enabled = true;
        }
    }
}
