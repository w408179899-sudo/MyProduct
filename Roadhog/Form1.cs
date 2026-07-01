namespace Roadhog
{
    public partial class Form1 : Form
    {
        private const int HeaderRowHeight = 30;
        private const int AccountRowHeight = 34;
        private const int WindowCornerRadius = 12;
        private static readonly TimeSpan PlayerInfoRefreshInterval = TimeSpan.FromSeconds(15);

        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Infrastructure.Composition.RoadhogServices _services = Infrastructure.Composition.RoadhogServices.Create();
        private readonly List<AccountRowModel> _accounts = new();
        private readonly Dictionary<string, AccountRowControls> _rowControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _lastPlayerInfoRefreshAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _playerInfoRefreshInFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Windows.Forms.Timer _uiRefreshTimer = new() { Interval = 1000 };

        private bool _suppressFpgaSelectionChanged;
        private int _accountRows;
        private int _nextAccountNumber = 1;

        public Form1()
        {
            InitializeComponent();
            ApplyApplicationIcon();
            RebuildAccountsFromDevices();
            BuildAccountTable();
            RefreshFpgaDeviceCombo();
            LoadKmBoxNetInputs();
            UpdateWindowTitle();
            RefreshMissingPlayerInfoForRows();
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedWindowRegion();
        }

        private void ApplyRoundedWindowRegion()
        {
            if (Width < 8 || Height < 8)
            {
                Region = null;
                return;
            }

            using var path = UiChrome.RoundedRect(new RectangleF(0, 0, Width, Height), WindowCornerRadius);
            Region = new Region(path);
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
            var savedAccounts = LoadSavedAccountsForRows();
            var index = 1;

            foreach (var device in devices)
            {
                var fallbackAccountName = $"account{index++}";
                var savedAccount = FindSavedAccountForDevice(savedAccounts, device.BindingKey, fallbackAccountName);
                _accounts.Add(new AccountRowModel(
                    savedAccount?.AccountName ?? fallbackAccountName,
                    "",
                    savedAccount?.CharacterName ?? "",
                    device.BindingKey,
                    device.VmmDeviceName,
                    "idle",
                    "0.0",
                    "00:00:00"));
            }

            _nextAccountNumber = index;
        }

        private IReadOnlyList<Core.Accounts.AccountConfig> LoadSavedAccountsForRows()
        {
            var result = _services.AccountConfigStore.LoadAllAsync().GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                _services.Logger.Warn("ui.account_config.load_for_rows_failed", new Dictionary<string, object?>
                {
                    ["error"] = result.Error
                });
                return Array.Empty<Core.Accounts.AccountConfig>();
            }

            return result.Value;
        }

        private static Core.Accounts.AccountConfig? FindSavedAccountForDevice(
            IReadOnlyList<Core.Accounts.AccountConfig> savedAccounts,
            string hardwareKey,
            string fallbackAccountName)
        {
            return savedAccounts.FirstOrDefault(account =>
                    !IsAutoHardwareKey(account.HardwareKey) &&
                    string.Equals(account.HardwareKey.Trim(), hardwareKey.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? savedAccounts.FirstOrDefault(account =>
                    string.Equals(account.AccountName, fallbackAccountName, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildAccountTable()
        {
            accountTable.SuspendLayout();
            accountTable.Controls.Clear();
            accountTable.ColumnStyles.Clear();
            accountTable.RowStyles.Clear();
            _rowControls.Clear();
            accountTable.RowCount = 1;
            accountTable.ColumnCount = 10;
            _accountRows = 0;

            AddColumns();
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderRowHeight));
            AddHeader("账号", 0);
            AddHeader("角色", 1);
            AddHeader("硬件特征(空=自动)", 2);
            AddHeader("状态", 3);
            AddHeader("杀怪/h", 4);
            AddHeader("时长", 5);
            AddHeader("操作", 6, 4);

            foreach (var account in _accounts)
            {
                AddAccountRow(account);
            }

            accountTable.ResumeLayout();
        }

        private void AddColumns()
        {
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));

            for (var i = 0; i < 4; i++)
            {
                accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            }
        }

        private void AddHeader(string text, int column, int columnSpan = 1)
        {
            text = NormalizeAccountHeaderText(text, column);
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

        private static string NormalizeAccountHeaderText(string text, int column)
        {
            return column switch
            {
                0 => "等级/职业",
                1 => "角色",
                _ => text
            };
        }

        private void AddAccountRow(AccountRowModel account)
        {
            var row = accountTable.RowCount;
            accountTable.RowCount = row + 1;
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, AccountRowHeight));

            var alt = row % 2 == 0;
            var levelClassLabel = AddCell(account.LevelClass, row, 0, alt, ContentAlignment.MiddleCenter);
            var roleLabel = AddCell(account.Role, row, 1, alt);
            var hardwareInput = AddHardwareInput(account, row, alt);
            var statusLabel = AddCell(account.Status, row, 3, alt);
            var killsPerHourLabel = AddCell(account.KillsPerHour, row, 4, alt, ContentAlignment.MiddleCenter);
            var durationLabel = AddCell(account.Duration, row, 5, alt, ContentAlignment.MiddleCenter);

            AddActionButton("登录", row, 6, account.Account);
            AddActionButton("设置", row, 7, account.Account);
            AddActionButton("启动", row, 8, account.Account);
            AddActionButton("停止", row, 9, account.Account);

            _rowControls[account.Account] = new AccountRowControls(
                levelClassLabel,
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
            RefreshFpgaDeviceCombo();
            UpdateWindowTitle();
            RefreshMissingPlayerInfoForRows();
        }

        private void RefreshFpgaDeviceCombo()
        {
            var selectedHardwareKey = _accounts.FirstOrDefault()?.HardwareKey ?? string.Empty;
            var devices = _services.HardwareResolver.ListDevices();

            _suppressFpgaSelectionChanged = true;
            try
            {
                fpgaDeviceComboBox.Items.Clear();
                foreach (var device in devices)
                {
                    fpgaDeviceComboBox.Items.Add(new FpgaDeviceComboItem(
                        device.BindingKey,
                        device.VmmDeviceName,
                        FormatFpgaDeviceText(device)));
                }

                if (fpgaDeviceComboBox.Items.Count == 0)
                {
                    fpgaDeviceComboBox.Items.Add(FpgaDeviceComboItem.Empty);
                    fpgaDeviceComboBox.SelectedIndex = 0;
                    return;
                }

                var selectedIndex = 0;
                for (var i = 0; i < fpgaDeviceComboBox.Items.Count; i++)
                {
                    if (fpgaDeviceComboBox.Items[i] is FpgaDeviceComboItem item &&
                        string.Equals(item.BindingKey, selectedHardwareKey, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                fpgaDeviceComboBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                _suppressFpgaSelectionChanged = false;
            }
        }

        private void FpgaDeviceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressFpgaSelectionChanged ||
                fpgaDeviceComboBox.SelectedItem is not FpgaDeviceComboItem item ||
                string.IsNullOrWhiteSpace(item.BindingKey))
            {
                return;
            }

            ApplySelectedFpgaDevice(item);
        }

        private void ApplySelectedFpgaDevice(FpgaDeviceComboItem item)
        {
            if (_accounts.Count == 0)
            {
                return;
            }

            var current = _accounts[0];
            _accounts[0] = current with
            {
                LevelClass = string.Empty,
                Role = string.Empty,
                HardwareKey = item.BindingKey,
                VmmDeviceName = item.VmmDeviceName
            };

            UpdateAccountRowText(_accounts[0], snapshot: null, updateHardwareKey: true);
            UpdateWindowTitle();
            _ = RefreshPlayerInfoAsync(_accounts[0].Account);
        }

        private static string FormatFpgaDeviceText(Core.Hardware.HardwareDeviceFeature device)
        {
            var binding = RoadhogWindowTitleFormatter.FormatHardware(device.BindingKey);
            var display = string.IsNullOrWhiteSpace(device.DisplayName)
                ? string.Empty
                : " " + device.DisplayName.Trim();
            return binding + " | " + device.VmmDeviceName + display;
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
            UpdateWindowTitle();
        }

        private void LoadKmBoxNetInputs()
        {
            var config = _services.KmBoxNetConfig;
            kmboxIpTextBox.Text = config.IpAddress.Trim();
            kmboxPortTextBox.Text = config.Port > 0
                ? config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            kmboxMacTextBox.Text = config.Mac.Trim();
        }

        private async void SaveKmBoxButton_Click(object? sender, EventArgs e)
        {
            if (!TryReadKmBoxNetInput(out var config, out var error))
            {
                MessageBox.Show(this, error, "保存硬件配置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            kmboxSaveButton.Enabled = false;
            var oldText = kmboxSaveButton.Text;
            kmboxSaveButton.Text = "保存中...";
            var closeAfterSave = false;
            try
            {
                var store = new Infrastructure.Config.JsonKmBoxNetDeviceConfigStore(_services.KmBoxNetConfigPath);
                var result = await store.SaveAsync(config).ConfigureAwait(true);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Error ?? "保存KMBox配置失败。", "保存硬件配置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var fpgaResult = await SaveSelectedFpgaConfigAsync().ConfigureAwait(true);
                if (!fpgaResult.Success)
                {
                    MessageBox.Show(this, fpgaResult.Error ?? "保存FPGA配置失败。", "保存硬件配置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                kmboxStatusLabel.Text = "已保存，重启后生效";
                closeAfterSave = MessageBox.Show(this, "硬件配置已保存，重启程序后生效。", "保存硬件配置", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    == DialogResult.OK;
            }
            finally
            {
                kmboxSaveButton.Text = oldText;
                kmboxSaveButton.Enabled = true;
            }

            if (closeAfterSave && !IsDisposed)
            {
                Close();
            }
        }

        private async Task<Core.Common.OperationResult> SaveSelectedFpgaConfigAsync()
        {
            if (_accounts.Count == 0)
            {
                return Core.Common.OperationResult.Fail("没有可保存的账号行。");
            }

            var row = _accounts[0];
            var selectedItem = fpgaDeviceComboBox.SelectedItem as FpgaDeviceComboItem;
            var hardwareKey = selectedItem is not null && !string.IsNullOrWhiteSpace(selectedItem.BindingKey)
                ? selectedItem.BindingKey.Trim()
                : row.HardwareKey.Trim();
            var vmmDeviceName = selectedItem is not null && !string.IsNullOrWhiteSpace(selectedItem.VmmDeviceName)
                ? selectedItem.VmmDeviceName.Trim()
                : row.VmmDeviceName.Trim();
            if (IsAutoHardwareKey(hardwareKey))
            {
                return Core.Common.OperationResult.Fail("请先选择FPGA设备。");
            }

            var loadResult = await _services.AccountConfigStore.LoadAllAsync().ConfigureAwait(true);
            if (!loadResult.Success)
            {
                return Core.Common.OperationResult.Fail(loadResult.Error ?? "读取账号配置失败。");
            }

            var account = loadResult.Value?
                .FirstOrDefault(item => string.Equals(item.AccountName, row.Account, StringComparison.OrdinalIgnoreCase))
                ?.Clone() ?? new Core.Accounts.AccountConfig
                {
                    AccountName = row.Account,
                    Enabled = true,
                    ProfileName = "default_profile"
                };

            account.AccountName = row.Account;
            account.HardwareKey = hardwareKey;
            account.VmmDeviceName = vmmDeviceName;

            var device = FindFpgaDeviceByKey(hardwareKey);
            if (device is not null)
            {
                account.HardwareKey = device.BindingKey;
                account.HardwareBindingKind = device.BindingKind;
                account.HardwareBindingConfidence = device.BindingConfidence;
                account.HardwareDeviceInstanceId = device.DeviceInstanceId;
                account.HardwareLocationKey = device.LocationKey;
                account.HardwareDisplayName = device.DisplayName;
                account.VmmDeviceName = device.VmmDeviceName;

                row = row with
                {
                    HardwareKey = device.BindingKey,
                    VmmDeviceName = device.VmmDeviceName
                };
            }
            else
            {
                row = row with
                {
                    HardwareKey = hardwareKey,
                    VmmDeviceName = vmmDeviceName
                };
            }

            var saveResult = await _services.AccountConfigStore.UpsertAsync(account).ConfigureAwait(true);
            if (!saveResult.Success)
            {
                return saveResult;
            }

            _accounts[0] = row;
            UpdateAccountRowText(row, snapshot: null, updateHardwareKey: true);
            UpdateWindowTitle();
            return Core.Common.OperationResult.Ok();
        }

        private Core.Hardware.HardwareDeviceFeature? FindFpgaDeviceByKey(string hardwareKey)
        {
            if (string.IsNullOrWhiteSpace(hardwareKey))
            {
                return null;
            }

            var devices = _services.HardwareResolver.ListDevices();
            return devices.FirstOrDefault(device =>
                string.Equals(device.BindingKey.Trim(), hardwareKey.Trim(), StringComparison.OrdinalIgnoreCase) ||
                device.AliasKeys.Any(alias => string.Equals(alias.Trim(), hardwareKey.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        private bool TryReadKmBoxNetInput(out Infrastructure.Input.KmBoxNetDeviceConfig config, out string error)
        {
            config = new Infrastructure.Input.KmBoxNetDeviceConfig
            {
                IpAddress = kmboxIpTextBox.Text.Trim(),
                Mac = kmboxMacTextBox.Text.Trim().ToUpperInvariant()
            };

            if (!int.TryParse(
                    kmboxPortTextBox.Text.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var port))
            {
                error = "KMBox Net端口必须是数字。";
                return false;
            }

            config.Port = port;
            return config.Validate(out error);
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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
            }
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
            UpdateWindowTitle();
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

                if (ShouldRefreshPlayerInfo(row, snapshot))
                {
                    _ = RefreshPlayerInfoAsync(row.Account);
                }
            }

            UpdateWindowTitle();
        }

        private bool ShouldRefreshPlayerInfo(AccountRowModel row, Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            if (snapshot.ProcessId <= 0 || string.Equals(snapshot.Status, "idle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_playerInfoRefreshInFlight.Contains(row.Account))
            {
                return false;
            }

            if ((string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.LevelClass)) &&
                !_lastPlayerInfoRefreshAt.ContainsKey(row.Account))
            {
                return true;
            }

            return !_lastPlayerInfoRefreshAt.TryGetValue(row.Account, out var refreshedAt) ||
                   DateTimeOffset.Now - refreshedAt >= PlayerInfoRefreshInterval;
        }

        private void RefreshMissingPlayerInfoForRows()
        {
            foreach (var row in _accounts.ToArray())
            {
                if (string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.LevelClass))
                {
                    _ = RefreshPlayerInfoAsync(row.Account);
                }
            }
        }

        private async Task RefreshPlayerInfoAsync(string account)
        {
            if (_playerInfoRefreshInFlight.Contains(account))
            {
                return;
            }

            _playerInfoRefreshInFlight.Add(account);
            try
            {
                _lastPlayerInfoRefreshAt[account] = DateTimeOffset.Now;
                var result = await ReadPlayerForRowAsync(account).ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    return;
                }

                var index = _accounts.FindIndex(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return;
                }

                var player = result.Value;
                var row = _accounts[index];
                var levelClass = FormatLevelClass(player);
                _accounts[index] = row with
                {
                    LevelClass = string.IsNullOrWhiteSpace(levelClass) ? row.LevelClass : levelClass,
                    Role = string.IsNullOrWhiteSpace(player.CharacterName) ? row.Role : player.CharacterName
                };

                if (_rowControls.TryGetValue(account, out var controls))
                {
                    SetTextIfChanged(controls.LevelClassLabel, _accounts[index].LevelClass);
                    SetTextIfChanged(controls.RoleLabel, _accounts[index].Role);
                }

                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("ui.player_info.refresh_failed", new Dictionary<string, object?>
                {
                    ["account"] = account,
                    ["error"] = ex.Message
                });
            }
            finally
            {
                _playerInfoRefreshInFlight.Remove(account);
            }
        }

        private Task<Core.Common.OperationResult<Core.Model.PlayerSnapshot>> ReadPlayerForRowAsync(string account)
        {
            var snapshot = _services.AccountOrchestrator.Snapshot()
                .FirstOrDefault(item => string.Equals(item.AccountName, account, StringComparison.OrdinalIgnoreCase));
            if (snapshot is not null && snapshot.ProcessId > 0)
            {
                return _services.Runtime.ReadPlayerAsync(account);
            }

            var row = _accounts.FirstOrDefault(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            if (row is not null && _services.GameApi is Core.Api.IRoadhogScopedGameApi scopedApi)
            {
                return scopedApi.ReadPlayerAsync(
                    new Core.Api.GameApiReadContext(
                        row.Account,
                        0,
                        string.Empty,
                        row.VmmDeviceName),
                    CancellationToken.None);
            }

            return _services.Runtime.ReadPlayerAsync(account);
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

            SetTextIfChanged(controls.LevelClassLabel, row.LevelClass);
            SetTextIfChanged(controls.RoleLabel, row.Role);
            SetTextIfChanged(controls.StatusLabel, row.Status);
            SetTextIfChanged(controls.KillsPerHourLabel, row.KillsPerHour);
            SetTextIfChanged(controls.DurationLabel, row.Duration);

            if (updateHardwareKey && !string.IsNullOrWhiteSpace(row.HardwareKey))
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

        private void UpdateWindowTitle()
        {
            Text = RoadhogWindowTitleFormatter.Build(
                ResolveWindowTitleHardwareKey(),
                _services.KeyboardDeviceText,
                ResolveWindowTitleCharacterName());
        }

        private string ResolveWindowTitleHardwareKey()
        {
            var activeSnapshot = _services.AccountOrchestrator.Snapshot()
                .FirstOrDefault(snapshot =>
                    !string.Equals(snapshot.Status, "idle", StringComparison.OrdinalIgnoreCase)
                    && !IsAutoHardwareKey(snapshot.HardwareKey));
            if (activeSnapshot is not null)
            {
                return activeSnapshot.HardwareKey;
            }

            return _accounts
                .Select(account => account.HardwareKey)
                .FirstOrDefault(key => !IsAutoHardwareKey(key)) ?? string.Empty;
        }

        private string ResolveWindowTitleCharacterName()
        {
            var activeSnapshot = _services.AccountOrchestrator.Snapshot()
                .FirstOrDefault(snapshot =>
                    !string.Equals(snapshot.Status, "idle", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(snapshot.CharacterName));
            if (activeSnapshot is not null)
            {
                return activeSnapshot.CharacterName;
            }

            return _accounts
                .Select(account => account.Role)
                .FirstOrDefault(role => !string.IsNullOrWhiteSpace(role)) ?? string.Empty;
        }

        private static string FormatKillsPerHour(Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            if (snapshot.KillCount <= 0)
            {
                return "0.0";
            }

            var elapsed = GetRuntimeElapsed(snapshot);
            if (elapsed <= TimeSpan.Zero)
            {
                return "0.0";
            }

            return (snapshot.KillCount / Math.Max(elapsed.TotalHours, 1.0D / 3600.0D)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatRuntimeDuration(Core.Accounts.AccountRuntimeSnapshot snapshot)
        {
            return FormatDuration(GetRuntimeElapsed(snapshot));
        }

        private static string FormatLevelClass(Core.Model.PlayerSnapshot player)
        {
            var hasLevel = player.Level > 0;
            var characterClass = player.CharacterClass?.Trim() ?? string.Empty;
            if (!hasLevel && string.IsNullOrWhiteSpace(characterClass))
            {
                return string.Empty;
            }

            var levelText = hasLevel
                ? player.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "-";
            var classText = string.IsNullOrWhiteSpace(characterClass) ? "-" : characterClass;
            return levelText + "/" + classText;
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
            string LevelClass,
            string Role,
            string HardwareKey,
            string VmmDeviceName,
            string Status,
            string KillsPerHour,
            string Duration);

        private sealed record AccountRowControls(
            Label LevelClassLabel,
            Label RoleLabel,
            RoundedTextBox HardwareInput,
            Label StatusLabel,
            Label KillsPerHourLabel,
            Label DurationLabel);

        private sealed record FpgaDeviceComboItem(string BindingKey, string VmmDeviceName, string Text)
        {
            public static FpgaDeviceComboItem Empty { get; } = new(string.Empty, string.Empty, "未检测到FPGA设备");

            public override string ToString()
            {
                return Text;
            }
        }

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
