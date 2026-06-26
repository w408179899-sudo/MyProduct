namespace Roadhog
{
    public partial class Form1 : Form
    {
        private const int HeaderRowHeight = 30;
        private const int AccountRowHeight = 34;

        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Infrastructure.Composition.RoadhogServices _services = Infrastructure.Composition.RoadhogServices.Create();
        private readonly List<AccountRowModel> _accounts = new();

        private int _accountRows;
        private int _nextAccountNumber = 1;

        public Form1()
        {
            InitializeComponent();
            RebuildAccountsFromDevices();
            BuildAccountTable();
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
                    "0",
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
            accountTable.RowCount = 1;
            accountTable.ColumnCount = 12;
            _accountRows = 0;

            AddColumns();
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderRowHeight));
            AddHeader("账号", 0);
            AddHeader("角色", 1);
            AddHeader("硬件特征(空=自动)", 2);
            AddHeader("状态", 3);
            AddHeader("金币/h", 4);
            AddHeader("杀怪/h", 5);
            AddHeader("时长", 6);
            AddHeader("操作", 7, 5);

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
            AddCell(account.Account, row, 0, alt);
            AddCell(account.Role, row, 1, alt);
            AddHardwareInput(account, row, alt);
            AddCell(account.Status, row, 3, alt);
            AddCell(account.GoldPerHour, row, 4, alt, ContentAlignment.MiddleCenter);
            AddCell(account.KillsPerHour, row, 5, alt, ContentAlignment.MiddleCenter);
            AddCell(account.Duration, row, 6, alt, ContentAlignment.MiddleCenter);

            AddActionButton("登录", row, 7, account.Account);
            AddActionButton("设置", row, 8, account.Account);
            AddActionButton("启动", row, 9, account.Account);
            AddActionButton("停止", row, 10, account.Account);
            AddActionButton("删除", row, 11, account.Account);

            _accountRows++;
            UpdateTableHeight();
        }

        private void AddCell(string text, int row, int column, bool alt, ContentAlignment alignment = ContentAlignment.MiddleLeft)
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
        }

        private void AddHardwareInput(AccountRowModel account, int row, bool alt)
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

            using var settingsForm = new AccountSettingsForm(account, _services.Runtime);
            settingsForm.ShowDialog(this);
        }

        private void StartAccountButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            if (!TryBuildStartConfig(account, out var config, out var error))
            {
                MessageBox.Show(error, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = _services.AccountOrchestrator.Start(config);

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
            BuildAccountTable();
        }

        private bool TryBuildStartConfig(string account, out Core.Accounts.AccountConfig config, out string error)
        {
            config = new Core.Accounts.AccountConfig
            {
                AccountName = account,
                Enabled = true,
                ProfileName = "default_profile"
            };

            var row = _accounts.FirstOrDefault(item => string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
            var hardwareKey = row?.HardwareKey.Trim() ?? string.Empty;
            if (IsAutoHardwareKey(hardwareKey))
            {
                error = string.Empty;
                return true;
            }

            config.HardwareKey = hardwareKey;
            error = string.Empty;
            return true;
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
                Status = snapshot.Status
            };

            BuildAccountTable();
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
            string GoldPerHour,
            string KillsPerHour,
            string Duration);
    }
}
