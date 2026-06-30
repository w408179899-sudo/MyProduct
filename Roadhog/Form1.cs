namespace Roadhog
{
    public partial class Form1 : Form
    {
        private const int HeaderRowHeight = 30;
        private const int AccountRowHeight = 34;
        private static readonly TimeSpan PlayerNameRefreshInterval = TimeSpan.FromSeconds(15);

        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Infrastructure.Composition.RoadhogServices _services = Infrastructure.Composition.RoadhogServices.Create();
        private readonly List<AccountRowModel> _accounts = new();
        private readonly Dictionary<string, AccountRowControls> _rowControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _lastPlayerNameRefreshAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _playerNameRefreshInFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Windows.Forms.Timer _uiRefreshTimer = new() { Interval = 1000 };

        private int _accountRows;
        private int _nextAccountNumber = 1;

        public Form1()
        {
            InitializeComponent();
            ApplyApplicationIcon();
            RebuildAccountsFromDevices();
            BuildAccountTable();
            _uiRefreshTimer.Tick += UiRefreshTimer_Tick;
            _uiRefreshTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _uiRefreshTimer.Stop();
            _uiRefreshTimer.Dispose();
            _services.Dispose();
            base.OnFormClosed(e);
        }

        private void ApplyApplicationIcon()
        {
            var icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            if (icon is not null)
            {
                Icon = icon;
            }
        }

        private void RebuildAccountsFromDevices()
        {
            _accounts.Clear();
            var devices = _services.HardwareResolver.ListDevices();
            var index = 1;

            foreach (var device in devices)
            {
                _accounts.Add(new AccountRowModel(
                    $"account{index++}",
                    "",
                    device.BindingKey,
                    "idle",
                    "0.0",
                    "00:00:00"));
            }

            _nextAccountNumber = index;
        }

        private void BuildAccountTable()
        {
            accountTable.SuspendLayout();
            accountTable.Controls.Clear();
            accountTable.ColumnStyles.Clear();
            accountTable.RowStyles.Clear();
            _rowControls.Clear();
            accountTable.RowCount = 1;
            accountTable.ColumnCount = 11;
            _accountRows = 0;

            AddColumns();
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderRowHeight));
            AddHeader("账号", 0);
            AddHeader("角色", 1);
            AddHeader("硬件特征(空=自动)", 2);
            AddHeader("状态", 3);
            AddHeader("杀怪/h", 4);
            AddHeader("时长", 5);
            AddHeader("操作", 6, 5);

            foreach (var account in _accounts)
            {
                AddAccountRow(account);
            }

            accountTable.ResumeLayout();
        }

        private void AddColumns()
        {
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));

            for (var i = 0; i < 5; i++)
            {
                accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 5.6F));
            }
        }

        private void AddHeader(string text, int column, int columnSpan = 1)
        {
            var label = new Label
            {
                BackColor = _headerGreen,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Margin = Padding.Empty,
                Padding = new Padding(6, 0, 6, 0),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter
            };

            accountTable.Controls.Add(label, column, 0);

            if (columnSpan > 1)
            {
                accountTable.SetColumnSpan(label, columnSpan);
            }
        }

        private void AddAccountRow(AccountRowModel account)
        {
            var row = accountTable.RowCount;
            accountTable.RowCount = row + 1;
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, AccountRowHeight));

            var alt = row % 2 == 0;
            var accountLabel = AddCell(account.Account, row, 0, alt);
            var roleLabel = AddCell(account.Role, row, 1, alt);
            var hardwareInput = AddHardwareInput(account, row, alt);
            var statusLabel = AddCell(account.Status, row, 3, alt);
            var killsPerHourLabel = AddCell(account.KillsPerHour, row, 4, alt, ContentAlignment.MiddleCenter);
            var durationLabel = AddCell(account.Duration, row, 5, alt, ContentAlignment.MiddleCenter);

            AddActionButton("登录", row, 6, account.Account);
            AddActionButton("设置", row, 7, account.Account);
            AddActionButton("启动", row, 8, account.Account);
            AddActionButton("停止", row, 9, account.Account);
            AddActionButton("删除", row, 10, account.Account);

            _rowControls[account.Account] = new AccountRowControls(
                accountLabel,
                roleLabel,
                hardwareInput,
                statusLabel,
                killsPerHourLabel,
                durationLabel);

            _accountRows++;
            UpdateTableHeight();
        }

        private Label AddCell(string text, int row, int column, bool alt, ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            var label = new Label
            {
                BackColor = alt ? _softGreen : Color.White,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 83, 45),
                Margin = Padding.Empty,
                Padding = new Padding(6, 0, 6, 0),
                Text = text,
                TextAlign = alignment
            };

            accountTable.Controls.Add(label, column, row);
            return label;
        }

        private RoundedTextBox AddHardwareInput(AccountRowModel account, int row, bool alt)
        {
            var input = new RoundedTextBox
            {
                BackColor = alt ? _softGreen : Color.White,
                BorderColor = Color.FromArgb(134, 239, 172),
                CornerRadius = 7,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 83, 45),
                Margin = new Padding(3),
                Tag = account.Account,
                Text = account.HardwareKey
            };

            input.TextChanged += AccountHardwareInput_TextChanged;
            accountTable.Controls.Add(input, 2, row);
            return input;
        }

        private void AddActionButton(string text, int row, int column, string account)
        {
            var button = new RoundedButton
            {
                BackColor = _primaryGreen,
                BorderColor = _darkGreen,
                CornerRadius = 7,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                Margin = new Padding(3),
                ShadowDepth = 2,
                Tag = account,
                Text = text,
                UseVisualStyleBackColor = false
            };

            if (text == "设置")
            {
                button.Click += AccountSettingsButton_Click;
            }
            else if (text == "启动")
            {
                button.Click += StartAccountButton_Click;
            }
            else if (text == "停止")
            {
                button.Click += StopAccountButton_Click;
            }
            else if (text == "删除")
            {
                button.Click += DeleteAccountButton_Click;
            }

            accountTable.Controls.Add(button, column, row);
        }

        private void UpdateTableHeight()
        {
            accountTable.Height = HeaderRowHeight + (_accountRows * AccountRowHeight) + accountTable.RowCount + 1;
        }

        private void RefreshDevicesButton_Click(object? sender, EventArgs e)
        {
            RebuildAccountsFromDevices();
            BuildAccountTable();
        }

        private void AccountHardwareInput_TextChanged(object? sender, EventArgs e)
        {
            if (sender is not Control { Tag: string account } input)
            {
                return;
            }

            var index = _accounts.FindIndex(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _accounts[index] = _accounts[index] with { HardwareKey = input.Text.Trim() };
        }

        private void AccountSettingsButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            using var settingsForm = new AccountSettingsForm(account, _services.Runtime, _services.AccountConfigStore, _services.SharedPathStore);
            settingsForm.ShowDialog(this);
        }

        private async void StartAccountButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            var buildResult = await TryBuildStartConfigAsync(account).ConfigureAwait(true);
            if (!buildResult.Success || buildResult.Config is null)
            {
                MessageBox.Show(buildResult.Error, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = _services.AccountOrchestrator.Start(buildResult.Config);

            if (!result.Success)
            {
                MessageBox.Show(result.Error, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (result.Success)
            {
                UpdateAccountRuntimeDisplay(account, updateHardwareKey: true);
            }
        }

        private async void StopAccountButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            var result = await _services.AccountOrchestrator.StopAsync(account).ConfigureAwait(true);
            if (!result.Success)
            {
                MessageBox.Show(result.Error, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (result.Success)
            {
                UpdateAccountRuntimeDisplay(account, updateHardwareKey: false);
            }
        }

        private async void DeleteAccountButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"确定要删除账号 [{account}] 吗？",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var stopResult = await _services.AccountOrchestrator.StopAsync(account).ConfigureAwait(true);
            if (!stopResult.Success)
            {
                MessageBox.Show(stopResult.Error, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var index = _accounts.FindIndex(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _accounts.RemoveAt(index);
            _rowControls.Remove(account);
            _lastPlayerNameRefreshAt.Remove(account);
            _playerNameRefreshInFlight.Remove(account);
            BuildAccountTable();
        }

        private async Task<StartConfigBuildResult> TryBuildStartConfigAsync(string account)
        {
            var loadResult = await _services.AccountConfigStore.LoadAllAsync().ConfigureAwait(true);
            if (!loadResult.Success)
            {
                return StartConfigBuildResult.Fail(loadResult.Error ?? "读取账号配置失败。");
            }

            var config = loadResult.Value?
                .FirstOrDefault(item => string.Equals(item.AccountName, account, StringComparison.OrdinalIgnoreCase))
                ?.Clone() ?? new Core.Accounts.AccountConfig
                {
                    AccountName = account,
                    Enabled = true,
                    ProfileName = "default_profile"
                };

            config.AccountName = account;
            config.Enabled = true;
            if (config.ScriptSettings is not null)
            {
                config.ProfileName = config.ScriptSettings.ProfileName;
                config.MainMode = config.ScriptSettings.MainMode;
                config.CombatMode = config.ScriptSettings.CombatMode;
                config.RevivePathName = config.ScriptSettings.Paths.RevivePathName;
                config.CombatPathName = config.ScriptSettings.Paths.CombatPathName;
                config.MaintenancePathName = config.ScriptSettings.Paths.MaintenancePathName;
            }

            var row = _accounts.FirstOrDefault(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            var hardwareKey = row?.HardwareKey.Trim() ?? string.Empty;
            if (IsAutoHardwareKey(hardwareKey))
            {
                config.HardwareKey = string.Empty;
                return StartConfigBuildResult.Ok(config);
            }

            config.HardwareKey = hardwareKey;
            return StartConfigBuildResult.Ok(config);
        }

        private void UpdateAccountRuntimeDisplay(string account, bool updateHardwareKey)
        {
            var snapshot = _services.AccountOrchestrator.Snapshot()
                .FirstOrDefault(item => string.Equals(item.AccountName, account, StringComparison.OrdinalIgnoreCase));
            if (snapshot is null)
            {
                return;
            }

            var index = _accounts.FindIndex(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var accountRow = _accounts[index];
            _accounts[index] = accountRow with
            {
                HardwareKey = updateHardwareKey && !string.IsNullOrWhiteSpace(snapshot.HardwareKey)
                    ? snapshot.HardwareKey
                    : accountRow.HardwareKey,
                Role = string.IsNullOrWhiteSpace(snapshot.CharacterName) ? accountRow.Role : snapshot.CharacterName,
                Status = snapshot.Status,
                KillsPerHour = FormatKillsPerHour(snapshot),
                Duration = FormatRuntimeDuration(snapshot)
            };

            UpdateAccountRowText(_accounts[index], snapshot, updateHardwareKey);
        }

        private void UiRefreshTimer_Tick(object? sender, EventArgs e)
        {
            var snapshots = _services.AccountOrchestrator.Snapshot();
            foreach (var snapshot in snapshots)
            {
                var index = _accounts.FindIndex(item => string.Equals(item.Account, snapshot.AccountName, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    continue;
                }

                var row = _accounts[index];
                row = row with
                {
                    Role = string.IsNullOrWhiteSpace(snapshot.CharacterName) ? row.Role : snapshot.CharacterName,
                    Status = snapshot.Status,
                    KillsPerHour = FormatKillsPerHour(snapshot),
                    Duration = FormatRuntimeDuration(snapshot)
                };
                _accounts[index] = row;
                UpdateAccountRowText(row, snapshot, updateHardwareKey: false);

                if (ShouldRefreshPlayerName(row, snapshot))
                {
                    _ = RefreshPlayerNameAsync(row.Account);
                }
            }
        }

        private bool ShouldRefreshPlayerName(AccountRowModel row, Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            if (snapshot.ProcessId <= 0 || string.Equals(snapshot.Status, "idle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_playerNameRefreshInFlight.Contains(row.Account))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.Role))
            {
                return true;
            }

            return !_lastPlayerNameRefreshAt.TryGetValue(row.Account, out var refreshedAt) ||
                   DateTimeOffset.Now - refreshedAt >= PlayerNameRefreshInterval;
        }

        private async Task RefreshPlayerNameAsync(string account)
        {
            _playerNameRefreshInFlight.Add(account);
            try
            {
                _lastPlayerNameRefreshAt[account] = DateTimeOffset.Now;
                var result = await _services.Runtime.ReadPlayerAsync(account).ConfigureAwait(true);
                if (!result.Success || result.Value is null || string.IsNullOrWhiteSpace(result.Value.CharacterName))
                {
                    return;
                }

                var index = _accounts.FindIndex(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return;
                }

                _accounts[index] = _accounts[index] with { Role = result.Value.CharacterName };
                if (_rowControls.TryGetValue(account, out var controls))
                {
                    SetTextIfChanged(controls.RoleLabel, result.Value.CharacterName);
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("ui.player_name.refresh_failed", new Dictionary<string, object?>
                {
                    ["account"] = account,
                    ["error"] = ex.Message
                });
            }
            finally
            {
                _playerNameRefreshInFlight.Remove(account);
            }
        }

        private void UpdateAccountRowText(
            AccountRowModel row,
            Core.Accounts.AccountRuntimeSnapshot? snapshot,
            bool updateHardwareKey)
        {
            if (!_rowControls.TryGetValue(row.Account, out var controls))
            {
                return;
            }

            SetTextIfChanged(controls.RoleLabel, row.Role);
            SetTextIfChanged(controls.StatusLabel, row.Status);
            SetTextIfChanged(controls.KillsPerHourLabel, row.KillsPerHour);
            SetTextIfChanged(controls.DurationLabel, row.Duration);

            if (updateHardwareKey && snapshot is not null && !string.IsNullOrWhiteSpace(row.HardwareKey))
            {
                SetTextIfChanged(controls.HardwareInput, row.HardwareKey);
            }
        }

        private static void SetTextIfChanged(Control control, string value)
        {
            if (!string.Equals(control.Text, value, StringComparison.Ordinal))
            {
                control.Text = value;
            }
        }

        private static string FormatKillsPerHour(Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            if (snapshot.KillCount < 2 ||
                snapshot.FirstKillAt is not { } firstKillAt ||
                snapshot.LastKillAt is not { } lastKillAt)
            {
                return "0.0";
            }

            var elapsed = lastKillAt - firstKillAt;
            if (elapsed <= TimeSpan.Zero)
            {
                return "0.0";
            }

            return ((snapshot.KillCount - 1) / Math.Max(elapsed.TotalHours, 1.0D / 3600.0D)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatRuntimeDuration(Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            return FormatDuration(GetRuntimeElapsed(snapshot));
        }

        private static TimeSpan GetRuntimeElapsed(Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            if (snapshot.StartedAt is not { } startedAt)
            {
                return TimeSpan.Zero;
            }

            var end = string.Equals(snapshot.Status, "idle", StringComparison.OrdinalIgnoreCase) && snapshot.StoppedAt.HasValue
                ? snapshot.StoppedAt.Value
                : DateTimeOffset.Now;
            var elapsed = end - startedAt;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        private static string FormatDuration(TimeSpan value)
        {
            var totalHours = (int)value.TotalHours;
            return totalHours.ToString("00", System.Globalization.CultureInfo.InvariantCulture) +
                   ":" +
                   value.Minutes.ToString("00", System.Globalization.CultureInfo.InvariantCulture) +
                   ":" +
                   value.Seconds.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsAutoHardwareKey(string hardwareKey)
        {
            return string.IsNullOrWhiteSpace(hardwareKey)
                || string.Equals(hardwareKey.Trim(), "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hardwareKey.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hardwareKey.Trim(), "automatic", StringComparison.OrdinalIgnoreCase);
        }

        private sealed record AccountRowModel(
            string Account,
            string Role,
            string HardwareKey,
            string Status,
            string KillsPerHour,
            string Duration);

        private sealed record AccountRowControls(
            Label AccountLabel,
            Label RoleLabel,
            RoundedTextBox HardwareInput,
            Label StatusLabel,
            Label KillsPerHourLabel,
            Label DurationLabel);

        private sealed record StartConfigBuildResult(bool Success, Core.Accounts.AccountConfig? Config, string Error)
        {
            public static StartConfigBuildResult Ok(Core.Accounts.AccountConfig config)
            {
                return new StartConfigBuildResult(true, config, string.Empty);
            }

            public static StartConfigBuildResult Fail(string error)
            {
                return new StartConfigBuildResult(false, null, error);
            }
        }
    }
}
