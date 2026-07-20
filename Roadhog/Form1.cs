using Roadhog.Application.Licensing;

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
        private readonly Infrastructure.Composition.RoadhogServices _services =
            Infrastructure.Composition.RoadhogServices.Create(Infrastructure.Composition.RoadhogServiceOptions.FromEnvironment());
        private readonly List<AccountRowModel> _accounts = new();
        private readonly Dictionary<string, AccountRowControls> _rowControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _lastPlayerInfoRefreshAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _playerInfoRefreshInFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Windows.Forms.Timer _uiRefreshTimer = new() { Interval = 1000 };
        private readonly Infrastructure.Hardware.DeviceLeaseStore _deviceLeaseStore = new();
        private readonly DateTimeOffset _processStartedAtUtc = ResolveCurrentProcessStartTimeUtc();
        private readonly Label _licenseStatusLabel = new();

        private DateTimeOffset _topBarStatusMessageExpiresAt = DateTimeOffset.MinValue;
        private IReadOnlyList<Infrastructure.Hardware.DeviceLease> _otherDeviceLeases = Array.Empty<Infrastructure.Hardware.DeviceLease>();
        private Infrastructure.Hardware.DeviceLease? _deviceLease;
        private string _otherDeviceLeaseFingerprint = string.Empty;
        private string _lastDeviceLeaseError = string.Empty;
        private bool _suppressFpgaSelectionChanged;
        private bool _suppressHardwareInputChanged;
        private bool _licenseInitializationStarted;
        private int _accountRows;

        public Form1()
        {
            InitializeComponent();
            InitializeLicenseStatusLabel();
            _services.LicenseCoordinator.StateChanged += LicenseCoordinator_StateChanged;
            ApplyApplicationIcon();
            RebuildAccountsFromDevices();
            TryAcquireConfiguredDeviceLease();
            RefreshDeviceLeaseState(refreshListsWhenChanged: false);
            BuildAccountTable();
            RefreshFpgaDeviceCombo();
            RefreshVmmDeviceCombo();
            LoadKmBoxNetInputs();
            UpdateWindowTitle();
            UpdateTopBarProcessId();
            RefreshMissingPlayerInfoForRows();
            _uiRefreshTimer.Tick += UiRefreshTimer_Tick;
            _uiRefreshTimer.Start();
            ApplyLicenseState(_services.LicenseCoordinator.State);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_licenseInitializationStarted)
            {
                return;
            }

            _licenseInitializationStarted = true;
            await EnsureLicenseInteractiveAsync().ConfigureAwait(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _uiRefreshTimer.Stop();
            _uiRefreshTimer.Dispose();
            _services.LicenseCoordinator.StateChanged -= LicenseCoordinator_StateChanged;
            _deviceLeaseStore.Release(Environment.ProcessId, _processStartedAtUtc);
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
            var account = SelectClientAccount(LoadSavedAccountsForRows());
            if (account is null || IsAutoHardwareKey(account.HardwareKey))
            {
                return;
            }

            var devices = ListAvailableFpgaDevices();
            var device = FindFpgaDeviceByKey(account.HardwareKey, devices);
            if (device is null)
            {
                _services.Logger.Warn("ui.saved_fpga_device_not_online", new Dictionary<string, object?>
                {
                    ["account"] = account.AccountName,
                    ["hardwareKey"] = account.HardwareKey
                });
                return;
            }

            _accounts.Add(new AccountRowModel(
                account.AccountName,
                "",
                account.CharacterName ?? "",
                device.BindingKey,
                ResolveSavedOrDeviceVmmDeviceName(account.VmmDeviceName, device.VmmDeviceName),
                "idle",
                "0.0",
                "00:00:00"));
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

        private static Core.Accounts.AccountConfig? SelectClientAccount(
            IReadOnlyList<Core.Accounts.AccountConfig> savedAccounts)
        {
            return savedAccounts.FirstOrDefault(account => !IsAutoHardwareKey(account.HardwareKey))
                ?? savedAccounts.FirstOrDefault();
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
                ReadOnly = true,
                Tag = account.Account,
                Text = FormatHardwareDisplay(account.HardwareKey)
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
            TryAcquireConfiguredDeviceLease();
            RefreshDeviceLeaseState(refreshListsWhenChanged: false);
            BuildAccountTable();
            RefreshFpgaDeviceCombo();
            RefreshVmmDeviceCombo();
            UpdateWindowTitle();
            UpdateTopBarProcessId(force: true);
            RefreshMissingPlayerInfoForRows();
        }

        private void RefreshFpgaDeviceCombo(string? preferredHardwareKey = null)
        {
            var selectedHardwareKey = string.IsNullOrWhiteSpace(preferredHardwareKey)
                ? ResolvePreferredFpgaSelectionKey()
                : preferredHardwareKey.Trim();
            var devices = ListSelectableFpgaDevices();

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
                for (var i = 0; i < devices.Count; i++)
                {
                    if (DeviceMatchesHardwareKey(devices[i], selectedHardwareKey))
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
            // Selection is only a pending config choice. The account row is built from saved config
            // after restart, so unsaved FPGA changes do not make a new account row appear.
            UpdateWindowTitle();
        }

        private string ResolvePreferredFpgaSelectionKey()
        {
            var accountRowKey = _accounts
                .Select(account => account.HardwareKey)
                .FirstOrDefault(key => !IsAutoHardwareKey(key));
            if (!string.IsNullOrWhiteSpace(accountRowKey))
            {
                return accountRowKey;
            }

            var savedAccount = SelectClientAccount(LoadSavedAccountsForRows());
            return savedAccount is not null && !IsAutoHardwareKey(savedAccount.HardwareKey)
                ? savedAccount.HardwareKey
                : string.Empty;
        }

        private IReadOnlyList<Core.Hardware.HardwareDeviceFeature> ListAvailableFpgaDevices()
        {
            var devices = _services.HardwareResolver.ListDevices();
            var uniqueDevices = new List<Core.Hardware.HardwareDeviceFeature>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in devices)
            {
                var key = device.BindingKey.Trim();
                if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
                {
                    continue;
                }

                uniqueDevices.Add(device);
            }

            return uniqueDevices;
        }

        private IReadOnlyList<Core.Hardware.HardwareDeviceFeature> ListSelectableFpgaDevices()
        {
            var occupiedHardwareKeys = _otherDeviceLeases
                .Select(lease => lease.HardwareKey.Trim())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return ListAvailableFpgaDevices()
                .Where(device => !occupiedHardwareKeys.Contains(device.BindingKey.Trim()))
                .ToArray();
        }

        private static string FormatFpgaDeviceText(Core.Hardware.HardwareDeviceFeature device)
        {
            return FormatHardwareDisplay(device.BindingKey);
        }

        private void RefreshVmmDeviceCombo(string? preferredVmmDeviceName = null)
        {
            var selectedVmmDeviceName = string.IsNullOrWhiteSpace(preferredVmmDeviceName)
                ? ResolvePreferredVmmDeviceName()
                : preferredVmmDeviceName.Trim();
            var devices = ListAvailableFpgaDevices();
            var occupiedVmmDeviceNames = _otherDeviceLeases
                .Select(lease => lease.VmmDeviceName)
                .ToArray();
            var items = BuildVmmDeviceItems(devices, selectedVmmDeviceName, occupiedVmmDeviceNames);

            vmmDeviceComboBox.Items.Clear();
            foreach (var item in items)
            {
                vmmDeviceComboBox.Items.Add(item);
            }

            if (vmmDeviceComboBox.Items.Count == 0)
            {
                vmmDeviceComboBox.Items.Add(VmmDeviceComboItem.Empty);
            }

            var selectedIndex = 0;
            for (var i = 0; i < vmmDeviceComboBox.Items.Count; i++)
            {
                if (vmmDeviceComboBox.Items[i] is VmmDeviceComboItem item &&
                    string.Equals(item.Value, selectedVmmDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            vmmDeviceComboBox.SelectedIndex = selectedIndex;
        }

        private string ResolvePreferredVmmDeviceName()
        {
            var accountRowVmmDeviceName = _accounts
                .Select(account => account.VmmDeviceName)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(accountRowVmmDeviceName))
            {
                return accountRowVmmDeviceName.Trim();
            }

            var savedAccount = SelectClientAccount(LoadSavedAccountsForRows());
            if (!string.IsNullOrWhiteSpace(savedAccount?.VmmDeviceName))
            {
                return savedAccount.VmmDeviceName.Trim();
            }

            return "fpga";
        }

        private static IReadOnlyList<VmmDeviceComboItem> BuildVmmDeviceItems(
            IReadOnlyList<Core.Hardware.HardwareDeviceFeature> devices,
            string selectedVmmDeviceName,
            IReadOnlyList<string> occupiedVmmDeviceNames)
        {
            var items = new List<VmmDeviceComboItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddVmmDeviceItem(items, seen, selectedVmmDeviceName);
            AddVmmDeviceItem(items, seen, "fpga");
            foreach (var device in devices)
            {
                AddVmmDeviceItem(items, seen, device.VmmDeviceName);
            }

            var indexedCount = devices.Count;
            var selectedIndex = ExtractVmmDeviceIndex(selectedVmmDeviceName);
            if (selectedIndex >= 0)
            {
                indexedCount = Math.Max(indexedCount, selectedIndex + 1);
            }

            for (var i = 0; i < indexedCount; i++)
            {
                AddVmmDeviceItem(
                    items,
                    seen,
                    "fpga://devindex=" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var occupied = occupiedVmmDeviceNames
                .Select(Infrastructure.Hardware.DeviceLeaseStore.CanonicalVmmDeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return items
                .Where(item => !occupied.Contains(Infrastructure.Hardware.DeviceLeaseStore.CanonicalVmmDeviceName(item.Value)))
                .ToArray();
        }

        private static void AddVmmDeviceItem(
            List<VmmDeviceComboItem> items,
            HashSet<string> seen,
            string? vmmDeviceName)
        {
            var value = NormalizeVmmDeviceName(vmmDeviceName);
            if (!seen.Add(value))
            {
                return;
            }

            items.Add(new VmmDeviceComboItem(value, value));
        }

        private static int ExtractVmmDeviceIndex(string? vmmDeviceName)
        {
            if (string.IsNullOrWhiteSpace(vmmDeviceName))
            {
                return -1;
            }

            const string marker = "devindex=";
            var markerIndex = vmmDeviceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return -1;
            }

            var start = markerIndex + marker.Length;
            var end = start;
            while (end < vmmDeviceName.Length && char.IsDigit(vmmDeviceName[end]))
            {
                end++;
            }

            return end > start &&
                int.TryParse(vmmDeviceName[start..end], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                ? index
                : -1;
        }

        private string GetSelectedVmmDeviceName()
        {
            return vmmDeviceComboBox.SelectedItem is VmmDeviceComboItem item
                ? item.Value
                : NormalizeVmmDeviceName(vmmDeviceComboBox.Text);
        }

        private static string NormalizeVmmDeviceName(string? vmmDeviceName)
        {
            return string.IsNullOrWhiteSpace(vmmDeviceName)
                ? "fpga"
                : vmmDeviceName.Trim();
        }

        private void TryAcquireConfiguredDeviceLease()
        {
            var row = _accounts.FirstOrDefault();
            if (row is null || IsAutoHardwareKey(row.HardwareKey) || string.IsNullOrWhiteSpace(row.VmmDeviceName))
            {
                return;
            }

            if (!TryAcquireDeviceLease(row.HardwareKey, row.VmmDeviceName, out var error))
            {
                ReportDeviceLeaseError(error);
            }
        }

        private bool TryAcquireDeviceLease(string hardwareKey, string vmmDeviceName, out string error)
        {
            var previousLease = _deviceLease;
            var result = _deviceLeaseStore.TryAcquire(
                Environment.ProcessId,
                _processStartedAtUtc,
                ResolveLeaseClientRoot(),
                hardwareKey,
                vmmDeviceName);
            if (result.Success && result.Lease is not null)
            {
                _deviceLease = result.Lease;
                _lastDeviceLeaseError = string.Empty;
                error = string.Empty;
                return true;
            }

            _deviceLease = previousLease;
            if (result.Conflict is not null)
            {
                var owner = string.IsNullOrWhiteSpace(result.Conflict.ClientRoot)
                    ? string.Empty
                    : " (" + result.Conflict.ClientRoot + ")";
                error = "\u8bbe\u5907\u5df2\u88ab\u53e6\u4e00\u4e2a Roadhog.exe \u5360\u7528\uff0cPID " +
                    result.Conflict.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + owner + "\u3002";
                return false;
            }

            error = "\u8bfb\u5199\u8bbe\u5907\u5360\u7528\u8bb0\u5f55\u5931\u8d25\uff1a" + (result.Error ?? "unknown error");
            return false;
        }

        private void RestoreDeviceLease(Infrastructure.Hardware.DeviceLease? previousLease)
        {
            if (previousLease is null)
            {
                _deviceLeaseStore.Release(Environment.ProcessId, _processStartedAtUtc);
                _deviceLease = null;
                return;
            }

            var result = _deviceLeaseStore.TryAcquire(
                previousLease.ProcessId,
                previousLease.ProcessStartedAtUtc,
                previousLease.ClientRoot,
                previousLease.HardwareKey,
                previousLease.VmmDeviceName);
            _deviceLease = result.Success ? result.Lease : null;
        }

        private void RefreshDeviceLeaseState(bool refreshListsWhenChanged)
        {
            if (_deviceLease is not null)
            {
                if (!TryAcquireDeviceLease(_deviceLease.HardwareKey, _deviceLease.VmmDeviceName, out var renewError))
                {
                    ReportDeviceLeaseError(renewError);
                }
            }
            else
            {
                TryAcquireConfiguredDeviceLease();
            }

            var readResult = _deviceLeaseStore.ReadActive();
            if (!readResult.Success || readResult.Value is null)
            {
                ReportDeviceLeaseError("\u8bfb\u53d6\u8bbe\u5907\u5360\u7528\u8bb0\u5f55\u5931\u8d25\uff1a" + (readResult.Error ?? "unknown error"));
                return;
            }

            var otherLeases = readResult.Value
                .Where(lease => lease.ProcessId != Environment.ProcessId)
                .OrderBy(lease => lease.ProcessId)
                .ThenBy(lease => lease.HardwareKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var fingerprint = string.Join(
                "|",
                otherLeases.Select(lease =>
                    lease.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    lease.HardwareKey + ":" +
                    Infrastructure.Hardware.DeviceLeaseStore.CanonicalVmmDeviceName(lease.VmmDeviceName)));
            var changed = !string.Equals(fingerprint, _otherDeviceLeaseFingerprint, StringComparison.Ordinal);
            _otherDeviceLeases = otherLeases;
            _otherDeviceLeaseFingerprint = fingerprint;

            if (!changed || !refreshListsWhenChanged)
            {
                return;
            }

            var selectedHardwareKey = (fpgaDeviceComboBox.SelectedItem as FpgaDeviceComboItem)?.BindingKey;
            var selectedVmmDeviceName = (vmmDeviceComboBox.SelectedItem as VmmDeviceComboItem)?.Value;
            RefreshFpgaDeviceCombo(selectedHardwareKey);
            RefreshVmmDeviceCombo(selectedVmmDeviceName);
        }

        private void ReportDeviceLeaseError(string error)
        {
            if (string.IsNullOrWhiteSpace(error) || string.Equals(error, _lastDeviceLeaseError, StringComparison.Ordinal))
            {
                return;
            }

            _lastDeviceLeaseError = error;
            _services.Logger.Warn("ui.device_lease.failed", new Dictionary<string, object?>
            {
                ["error"] = error,
                ["pid"] = Environment.ProcessId,
                ["leaseFile"] = Infrastructure.Hardware.DeviceLeaseStore.DefaultPath
            });
        }

        private static DateTimeOffset ResolveCurrentProcessStartTimeUtc()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.StartTime.ToUniversalTime();
            }
            catch
            {
                return DateTimeOffset.UtcNow;
            }
        }

        private static string ResolveLeaseClientRoot()
        {
            var configuredRoot = Environment.GetEnvironmentVariable(
                Infrastructure.Composition.RoadhogServiceOptions.ClientRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                try
                {
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));
                }
                catch
                {
                }
            }

            return Path.GetFullPath(AppContext.BaseDirectory);
        }

        private static string ResolveSavedOrDeviceVmmDeviceName(string? savedVmmDeviceName, string? deviceVmmDeviceName)
        {
            return IsDefaultVmmDeviceName(savedVmmDeviceName)
                ? NormalizeVmmDeviceName(deviceVmmDeviceName)
                : savedVmmDeviceName!.Trim();
        }

        private void AccountHardwareInput_TextChanged(object? sender, EventArgs e)
        {
            if (_suppressHardwareInputChanged)
            {
                return;
            }

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
            var selectedItem = fpgaDeviceComboBox.SelectedItem as FpgaDeviceComboItem;
            var hardwareKey = selectedItem?.BindingKey.Trim() ?? string.Empty;
            var vmmDeviceName = GetSelectedVmmDeviceName();
            if (IsAutoHardwareKey(hardwareKey))
            {
                return Core.Common.OperationResult.Fail("\u8bf7\u5148\u9009\u62e9FPGA\u8bbe\u5907\u3002");
            }
            if (string.IsNullOrWhiteSpace(vmmDeviceName))
            {
                return Core.Common.OperationResult.Fail("\u6ca1\u6709\u53ef\u7528\u7684VMM\u8bbe\u5907\u3002");
            }

            var loadResult = await _services.AccountConfigStore.LoadAllAsync().ConfigureAwait(true);
            if (!loadResult.Success)
            {
                return Core.Common.OperationResult.Fail(loadResult.Error ?? "\u8bfb\u53d6\u8d26\u53f7\u914d\u7f6e\u5931\u8d25\u3002");
            }

            var savedAccounts = loadResult.Value ?? Array.Empty<Core.Accounts.AccountConfig>();
            var account = SelectClientAccount(savedAccounts)?.Clone() ?? new Core.Accounts.AccountConfig
            {
                AccountName = "account1",
                Enabled = true,
                ProfileName = "default_profile"
            };

            if (string.IsNullOrWhiteSpace(account.AccountName))
            {
                account.AccountName = "account1";
            }

            var device = FindFpgaDeviceByKey(hardwareKey);
            if (device is not null)
            {
                account.HardwareKey = device.BindingKey;
                account.HardwareBindingKind = device.BindingKind;
                account.HardwareBindingConfidence = device.BindingConfidence;
                account.HardwareDeviceInstanceId = device.DeviceInstanceId;
                account.HardwareLocationKey = device.LocationKey;
                account.HardwareDisplayName = device.DisplayName;
            }
            else
            {
                account.HardwareKey = hardwareKey;
            }

            account.VmmDeviceName = vmmDeviceName;

            var previousLease = _deviceLease;
            if (!TryAcquireDeviceLease(account.HardwareKey, account.VmmDeviceName, out var leaseError))
            {
                return Core.Common.OperationResult.Fail(leaseError);
            }

            var saveResult = await _services.AccountConfigStore.UpsertAsync(account).ConfigureAwait(true);
            if (!saveResult.Success)
            {
                RestoreDeviceLease(previousLease);
                return saveResult;
            }

            var index = _accounts.FindIndex(row =>
                string.Equals(row.Account, account.AccountName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var row = _accounts[index] with
                {
                    HardwareKey = account.HardwareKey,
                    VmmDeviceName = account.VmmDeviceName
                };
                _accounts[index] = row;
                UpdateAccountRowText(row, snapshot: null, updateHardwareKey: true);
            }

            UpdateWindowTitle();
            RefreshDeviceLeaseState(refreshListsWhenChanged: false);
            return Core.Common.OperationResult.Ok();
        }

        private async Task<Core.Common.OperationResult> SaveFirstVisibleAccountFpgaConfigAsync()
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
            var vmmDeviceName = GetSelectedVmmDeviceName();
            if (IsAutoHardwareKey(hardwareKey))
            {
                return Core.Common.OperationResult.Fail("请先选择FPGA设备。");
            }

            var loadResult = await _services.AccountConfigStore.LoadAllAsync().ConfigureAwait(true);
            if (!loadResult.Success)
            {
                return Core.Common.OperationResult.Fail(loadResult.Error ?? "读取账号配置失败。");
            }

            if (string.IsNullOrWhiteSpace(vmmDeviceName))
            {
                return Core.Common.OperationResult.Fail("\u6ca1\u6709\u53ef\u7528\u7684VMM\u8bbe\u5907\u3002");
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
                account.VmmDeviceName = vmmDeviceName;

                row = row with
                {
                    HardwareKey = device.BindingKey,
                    VmmDeviceName = vmmDeviceName
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

            var previousLease = _deviceLease;
            if (!TryAcquireDeviceLease(account.HardwareKey, account.VmmDeviceName, out var leaseError))
            {
                return Core.Common.OperationResult.Fail(leaseError);
            }

            var saveResult = await _services.AccountConfigStore.UpsertAsync(account).ConfigureAwait(true);
            if (!saveResult.Success)
            {
                RestoreDeviceLease(previousLease);
                return saveResult;
            }

            _accounts[0] = row;
            UpdateAccountRowText(row, snapshot: null, updateHardwareKey: true);
            UpdateWindowTitle();
            RefreshDeviceLeaseState(refreshListsWhenChanged: false);
            return Core.Common.OperationResult.Ok();
        }

        private async void TestVmmReadButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var vmmDeviceName = GetSelectedVmmDeviceName();
            button.Enabled = false;
            var oldText = button.Text;
            button.Text = "\u8bfb\u53d6\u4e2d...";
            ShowTopBarStatusMessage("VMM " + vmmDeviceName + " \u8bfb\u53d6\u4e2d", Color.FromArgb(22, 101, 52), TimeSpan.FromSeconds(30));
            try
            {
                var accountName = _accounts.FirstOrDefault()?.Account
                    ?? SelectClientAccount(LoadSavedAccountsForRows())?.AccountName
                    ?? "account1";
                var result = await ReadPlayerForVmmDeviceAsync(accountName, vmmDeviceName).ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    ShowTopBarStatusMessage("VMM\u8bfb\u53d6\u5931\u8d25: " + (result.Error ?? vmmDeviceName), Color.FromArgb(166, 40, 40), TimeSpan.FromSeconds(8));
                    return;
                }

                var player = result.Value;
                var levelClass = FormatLevelClass(player);
                var characterText = string.IsNullOrWhiteSpace(player.CharacterName)
                    ? "entity=" + player.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : player.CharacterName.Trim();
                ShowTopBarStatusMessage(string.IsNullOrWhiteSpace(levelClass)
                    ? "VMM OK " + characterText
                    : levelClass + " " + characterText, Color.FromArgb(22, 101, 52), TimeSpan.FromSeconds(6));
            }
            finally
            {
                button.Text = oldText;
                button.Enabled = true;
                UpdateTopBarProcessId();
            }
        }

        private Task<Core.Common.OperationResult<Core.Model.PlayerSnapshot>> ReadPlayerForVmmDeviceAsync(
            string accountName,
            string vmmDeviceName)
        {
            if (_services.GameApi is Core.Api.IRoadhogScopedGameApi scopedApi)
            {
                return scopedApi.ReadPlayerAsync(
                    new Core.Api.GameApiReadContext(
                        accountName,
                        0,
                        string.Empty,
                        NormalizeVmmDeviceName(vmmDeviceName)),
                    CancellationToken.None);
            }

            return _services.GameApi.ReadPlayerAsync(CancellationToken.None);
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

        private static Core.Hardware.HardwareDeviceFeature? FindFpgaDeviceByKey(
            string hardwareKey,
            IReadOnlyList<Core.Hardware.HardwareDeviceFeature> devices)
        {
            if (string.IsNullOrWhiteSpace(hardwareKey))
            {
                return null;
            }

            return devices.FirstOrDefault(device => DeviceMatchesHardwareKey(device, hardwareKey));
        }

        private static bool DeviceMatchesHardwareKey(Core.Hardware.HardwareDeviceFeature device, string hardwareKey)
        {
            if (string.IsNullOrWhiteSpace(hardwareKey))
            {
                return false;
            }

            var expected = hardwareKey.Trim();
            return string.Equals(device.BindingKey.Trim(), expected, StringComparison.OrdinalIgnoreCase) ||
                device.AliasKeys.Any(alias => string.Equals(alias.Trim(), expected, StringComparison.OrdinalIgnoreCase));
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

            using var settingsForm = new AccountSettingsForm(
                account,
                _services.Runtime,
                _services.AccountConfigStore,
                _services.SharedPathStore,
                _services.ScriptProfileStore);
            settingsForm.ShowDialog(this);
        }

        private async void StartAccountButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            if (!await EnsureLicenseInteractiveAsync().ConfigureAwait(true))
            {
                return;
            }

            var buildResult = await TryBuildStartConfigAsync(account).ConfigureAwait(true);
            if (!buildResult.Success || buildResult.Config is null)
            {
                MessageBox.Show(buildResult.Error, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!IsAutoHardwareKey(buildResult.Config.HardwareKey) &&
                !TryAcquireDeviceLease(
                    buildResult.Config.HardwareKey,
                    NormalizeVmmDeviceName(buildResult.Config.VmmDeviceName),
                    out var leaseError))
            {
                MessageBox.Show(leaseError, "\u542f\u52a8\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshDeviceLeaseState(refreshListsWhenChanged: true);
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
                UpdateTopBarProcessId(force: true);
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
                UpdateTopBarProcessId(force: true);
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
            await ApplySelectedProfileAsync(config).ConfigureAwait(true);
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
            if (row is not null && !string.IsNullOrWhiteSpace(row.VmmDeviceName))
            {
                config.VmmDeviceName = row.VmmDeviceName.Trim();
            }

            return StartConfigBuildResult.Ok(config);
        }

        private async Task ApplySelectedProfileAsync(Core.Accounts.AccountConfig config)
        {
            var profileName = config.ScriptSettings?.ProfileName;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                profileName = config.ProfileName;
            }

            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            var profileResult = await _services.ScriptProfileStore.LoadAsync(profileName).ConfigureAwait(true);
            if (!profileResult.Success || profileResult.Value is null)
            {
                _services.Logger.Warn("account.profile.load_failed", new Dictionary<string, object?>
                {
                    ["account"] = config.AccountName,
                    ["profileName"] = profileName,
                    ["error"] = profileResult.Error
                });
                return;
            }

            config.ScriptSettings = profileResult.Value.Settings.Clone();
            config.ProfileName = config.ScriptSettings.ProfileName;
            config.MainMode = config.ScriptSettings.MainMode;
            config.CombatMode = config.ScriptSettings.CombatMode;
            config.RevivePathName = config.ScriptSettings.Paths.RevivePathName;
            config.CombatPathName = config.ScriptSettings.Paths.CombatPathName;
            config.MaintenancePathName = config.ScriptSettings.Paths.MaintenancePathName;
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
                VmmDeviceName = updateHardwareKey && !string.IsNullOrWhiteSpace(snapshot.VmmDeviceName)
                    ? snapshot.VmmDeviceName
                    : accountRow.VmmDeviceName,
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
            UpdateTopBarProcessId();
            RefreshDeviceLeaseState(refreshListsWhenChanged: true);
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
                SetHardwareTextIfChanged(controls.HardwareInput, row.HardwareKey);
            }
        }

        private void SetHardwareTextIfChanged(Control control, string hardwareKey)
        {
            _suppressHardwareInputChanged = true;
            try
            {
                SetTextIfChanged(control, FormatHardwareDisplay(hardwareKey));
            }
            finally
            {
                _suppressHardwareInputChanged = false;
            }
        }

        private static void SetTextIfChanged(Control control, string value)
        {
            if (!string.Equals(control.Text, value, StringComparison.Ordinal))
            {
                control.Text = value;
            }
        }

        private void InitializeLicenseStatusLabel()
        {
            _licenseStatusLabel.AutoEllipsis = true;
            _licenseStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _licenseStatusLabel.BackColor = Color.Transparent;
            _licenseStatusLabel.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _licenseStatusLabel.ForeColor = Color.FromArgb(75, 85, 99);
            _licenseStatusLabel.Location = new Point(710, 50);
            _licenseStatusLabel.Name = "licenseStatusLabel";
            _licenseStatusLabel.Size = new Size(170, 26);
            _licenseStatusLabel.Text = "授权：未检查";
            _licenseStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            topBarPanel.Controls.Add(_licenseStatusLabel);
            _licenseStatusLabel.BringToFront();
        }

        private async Task<bool> EnsureLicenseInteractiveAsync()
        {
            var state = _services.LicenseCoordinator.State;
            if (state.IsAuthorized)
            {
                return true;
            }

            if (state.Kind != LicenseRuntimeStateKind.ActivationRequired)
            {
                try
                {
                    state = await _services.LicenseCoordinator.InitializeAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _services.Logger.Error("license.initialize.exception", ex);
                    MessageBox.Show(
                        "授权初始化失败。",
                        "授权失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (state.IsAuthorized)
            {
                return true;
            }

            if (state.Kind == LicenseRuntimeStateKind.ActivationRequired)
            {
                using var activationForm = new LicenseActivationForm(_services.LicenseCoordinator, state);
                return activationForm.ShowDialog(this) == DialogResult.OK
                    && _services.LicenseCoordinator.State.IsAuthorized;
            }

            MessageBox.Show(
                LicenseUiText.Describe(state),
                "授权失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private void LicenseCoordinator_StateChanged(object? sender, LicenseStateChangedEventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => ApplyLicenseState(e.State)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            ApplyLicenseState(e.State);
        }

        private void ApplyLicenseState(LicenseRuntimeState state)
        {
            SetTextIfChanged(_licenseStatusLabel, LicenseUiText.FormatStatus(state));
            _licenseStatusLabel.ForeColor = state.Kind switch
            {
                LicenseRuntimeStateKind.Authorized => Color.FromArgb(22, 101, 52),
                LicenseRuntimeStateKind.OfflineGrace => Color.FromArgb(180, 83, 9),
                LicenseRuntimeStateKind.Checking => Color.FromArgb(75, 85, 99),
                _ => Color.FromArgb(166, 40, 40)
            };

        }

        private void UpdateWindowTitle()
        {
            Text = RoadhogWindowTitleFormatter.Build(
                ResolveWindowTitleHardwareKey(),
                _services.KeyboardDeviceText,
                ResolveWindowTitleCharacterName());
        }

        private void UpdateTopBarProcessId(bool force = false)
        {
            if (!force && DateTimeOffset.Now < _topBarStatusMessageExpiresAt)
            {
                return;
            }

            _topBarStatusMessageExpiresAt = DateTimeOffset.MinValue;
            kmboxStatusLabel.ForeColor = Color.FromArgb(22, 101, 52);
            SetTextIfChanged(kmboxStatusLabel, "PID: " + ResolveTopBarProcessIdText());
        }

        private void ShowTopBarStatusMessage(string message, Color color, TimeSpan duration)
        {
            _topBarStatusMessageExpiresAt = DateTimeOffset.Now + duration;
            kmboxStatusLabel.ForeColor = color;
            SetTextIfChanged(kmboxStatusLabel, message);
        }

        private string ResolveTopBarProcessIdText()
        {
            return Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        private static string FormatHardwareDisplay(string hardwareKey)
        {
            return RoadhogWindowTitleFormatter.FormatHardware(hardwareKey);
        }

        private static bool IsAutoHardwareKey(string hardwareKey)
        {
            return string.IsNullOrWhiteSpace(hardwareKey)
                || string.Equals(hardwareKey.Trim(), "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hardwareKey.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hardwareKey.Trim(), "automatic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefaultVmmDeviceName(string? vmmDeviceName)
        {
            return string.IsNullOrWhiteSpace(vmmDeviceName)
                || string.Equals(vmmDeviceName.Trim(), "fpga", StringComparison.OrdinalIgnoreCase);
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

        private sealed record VmmDeviceComboItem(string Value, string Text)
        {
            public static VmmDeviceComboItem Empty { get; } = new(string.Empty, "\u65e0\u53ef\u7528VMM\u8bbe\u5907");

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
