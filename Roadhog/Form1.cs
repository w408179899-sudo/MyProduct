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

        private int _accountRows;

        public Form1()
        {
            InitializeComponent();
            BuildAccountTable();
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
            AddHeader("PID", 2);
            AddHeader("状态", 3);
            AddHeader("金币/h", 4);
            AddHeader("杀怪/h", 5);
            AddHeader("时长", 6);
            AddHeader("操作", 7, 5);

            AddAccountRow("chahohyur", "", "0", "idle", "0", "0.0", "00:00:00");
            AddAccountRow("nnv2@nave", "", "0", "stopping", "0", "0.0", "00:00:00");
            accountTable.ResumeLayout();
        }

        private void AddColumns()
        {
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10F));

            for (var i = 0; i < 5; i++)
            {
                accountTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 5.8F));
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

        private void AddAccountRow(
            string account,
            string role,
            string pid,
            string status,
            string goldPerHour,
            string killsPerHour,
            string duration)
        {
            var row = accountTable.RowCount;
            accountTable.RowCount = row + 1;
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, AccountRowHeight));

            var alt = row % 2 == 0;
            AddCell(account, row, 0, alt);
            AddCell(role, row, 1, alt);
            AddCell(pid, row, 2, alt, ContentAlignment.MiddleCenter);
            AddCell(status, row, 3, alt);
            AddCell(goldPerHour, row, 4, alt, ContentAlignment.MiddleCenter);
            AddCell(killsPerHour, row, 5, alt, ContentAlignment.MiddleCenter);
            AddCell(duration, row, 6, alt, ContentAlignment.MiddleCenter);

            AddActionButton("登录", row, 7, account);
            AddActionButton("设置", row, 8, account);
            AddActionButton("启动", row, 9, account);
            AddActionButton("停止", row, 10, account);
            AddActionButton("删除", row, 11, account);

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

        private void AddActionButton(string text, int row, int column, string account)
        {
            var button = new Button
            {
                BackColor = _primaryGreen,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                Margin = new Padding(3),
                Tag = account,
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderColor = _darkGreen;

            if (text == "设置")
            {
                button.Click += AccountSettingsButton_Click;
            }

            accountTable.Controls.Add(button, column, row);
        }

        private void UpdateTableHeight()
        {
            accountTable.Height = HeaderRowHeight + (_accountRows * AccountRowHeight) + accountTable.RowCount + 1;
        }

        private void AddAccountButton_Click(object? sender, EventArgs e)
        {
            AddAccountRow($"account{_accountRows + 1}", "", "0", "idle", "0", "0.0", "00:00:00");
        }

        private void AccountSettingsButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string account })
            {
                return;
            }

            using var settingsForm = new AccountSettingsForm(account);
            settingsForm.ShowDialog(this);
        }
    }
}
