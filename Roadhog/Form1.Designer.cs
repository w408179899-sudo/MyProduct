namespace Roadhog
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.shellPanel = new System.Windows.Forms.Panel();
            this.accountListPanel = new Roadhog.RoundedPanel();
            this.accountTable = new System.Windows.Forms.TableLayoutPanel();
            this.topBarPanel = new Roadhog.RoundedPanel();
            this.fpgaLabel = new System.Windows.Forms.Label();
            this.fpgaDeviceComboBox = new Roadhog.RoundedComboBox();
            this.refreshDevicesButton = new Roadhog.RoundedButton();
            this.kmboxTitleLabel = new System.Windows.Forms.Label();
            this.kmboxIpLabel = new System.Windows.Forms.Label();
            this.kmboxIpTextBox = new Roadhog.RoundedTextBox();
            this.kmboxPortLabel = new System.Windows.Forms.Label();
            this.kmboxPortTextBox = new Roadhog.RoundedTextBox();
            this.kmboxMacLabel = new System.Windows.Forms.Label();
            this.kmboxMacTextBox = new Roadhog.RoundedTextBox();
            this.kmboxSaveButton = new Roadhog.RoundedButton();
            this.kmboxStatusLabel = new System.Windows.Forms.Label();
            this.shellPanel.SuspendLayout();
            this.accountListPanel.SuspendLayout();
            this.topBarPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // shellPanel
            //
            this.shellPanel.BackColor = System.Drawing.Color.FromArgb(248, 253, 250);
            this.shellPanel.Controls.Add(this.accountListPanel);
            this.shellPanel.Controls.Add(this.topBarPanel);
            this.shellPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.shellPanel.Location = new System.Drawing.Point(0, 0);
            this.shellPanel.Name = "shellPanel";
            this.shellPanel.Padding = new System.Windows.Forms.Padding(14);
            this.shellPanel.Size = new System.Drawing.Size(940, 210);
            this.shellPanel.TabIndex = 0;
            //
            // accountListPanel
            //
            this.accountListPanel.BackColor = System.Drawing.Color.White;
            this.accountListPanel.BorderColor = System.Drawing.Color.FromArgb(187, 247, 208);
            this.accountListPanel.CornerRadius = 12;
            this.accountListPanel.Controls.Add(this.accountTable);
            this.accountListPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accountListPanel.Location = new System.Drawing.Point(14, 104);
            this.accountListPanel.Name = "accountListPanel";
            this.accountListPanel.Padding = new System.Windows.Forms.Padding(0);
            this.accountListPanel.ShadowDepth = 3;
            this.accountListPanel.Size = new System.Drawing.Size(912, 92);
            this.accountListPanel.TabIndex = 1;
            //
            // accountTable
            //
            this.accountTable.BackColor = System.Drawing.Color.White;
            this.accountTable.CellBorderStyle = System.Windows.Forms.TableLayoutPanelCellBorderStyle.Single;
            this.accountTable.ColumnCount = 10;
            this.accountTable.Dock = System.Windows.Forms.DockStyle.Top;
            this.accountTable.Location = new System.Drawing.Point(0, 0);
            this.accountTable.Name = "accountTable";
            this.accountTable.RowCount = 1;
            this.accountTable.Size = new System.Drawing.Size(890, 30);
            this.accountTable.TabIndex = 0;
            //
            // topBarPanel
            //
            this.topBarPanel.BackColor = System.Drawing.Color.FromArgb(240, 253, 244);
            this.topBarPanel.BorderColor = System.Drawing.Color.FromArgb(187, 247, 208);
            this.topBarPanel.CornerRadius = 12;
            this.topBarPanel.Controls.Add(this.kmboxStatusLabel);
            this.topBarPanel.Controls.Add(this.kmboxSaveButton);
            this.topBarPanel.Controls.Add(this.kmboxMacTextBox);
            this.topBarPanel.Controls.Add(this.kmboxMacLabel);
            this.topBarPanel.Controls.Add(this.kmboxPortTextBox);
            this.topBarPanel.Controls.Add(this.kmboxPortLabel);
            this.topBarPanel.Controls.Add(this.kmboxIpTextBox);
            this.topBarPanel.Controls.Add(this.kmboxIpLabel);
            this.topBarPanel.Controls.Add(this.kmboxTitleLabel);
            this.topBarPanel.Controls.Add(this.fpgaDeviceComboBox);
            this.topBarPanel.Controls.Add(this.fpgaLabel);
            this.topBarPanel.Controls.Add(this.refreshDevicesButton);
            this.topBarPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topBarPanel.Location = new System.Drawing.Point(14, 14);
            this.topBarPanel.Name = "topBarPanel";
            this.topBarPanel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.topBarPanel.ShadowDepth = 3;
            this.topBarPanel.Size = new System.Drawing.Size(912, 90);
            this.topBarPanel.TabIndex = 0;
            //
            // fpgaLabel
            //
            this.fpgaLabel.AutoSize = true;
            this.fpgaLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.fpgaLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.fpgaLabel.Location = new System.Drawing.Point(16, 17);
            this.fpgaLabel.Name = "fpgaLabel";
            this.fpgaLabel.Size = new System.Drawing.Size(40, 17);
            this.fpgaLabel.TabIndex = 11;
            this.fpgaLabel.Text = "FPGA";
            //
            // fpgaDeviceComboBox
            //
            this.fpgaDeviceComboBox.BackColor = System.Drawing.Color.FromArgb(229, 245, 235);
            this.fpgaDeviceComboBox.BorderColor = System.Drawing.Color.FromArgb(134, 239, 172);
            this.fpgaDeviceComboBox.CornerRadius = 8;
            this.fpgaDeviceComboBox.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Bold);
            this.fpgaDeviceComboBox.ForeColor = System.Drawing.Color.FromArgb(20, 83, 45);
            this.fpgaDeviceComboBox.Location = new System.Drawing.Point(106, 10);
            this.fpgaDeviceComboBox.Name = "fpgaDeviceComboBox";
            this.fpgaDeviceComboBox.Size = new System.Drawing.Size(646, 32);
            this.fpgaDeviceComboBox.TabIndex = 12;
            this.fpgaDeviceComboBox.SelectedIndexChanged += new System.EventHandler(this.FpgaDeviceComboBox_SelectedIndexChanged);
            //
            // refreshDevicesButton
            //
            this.refreshDevicesButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.refreshDevicesButton.BackColor = System.Drawing.Color.FromArgb(22, 163, 74);
            this.refreshDevicesButton.BorderColor = System.Drawing.Color.FromArgb(21, 128, 61);
            this.refreshDevicesButton.CornerRadius = 9;
            this.refreshDevicesButton.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.refreshDevicesButton.ForeColor = System.Drawing.Color.White;
            this.refreshDevicesButton.Location = new System.Drawing.Point(760, 10);
            this.refreshDevicesButton.Name = "refreshDevicesButton";
            this.refreshDevicesButton.ShadowDepth = 2;
            this.refreshDevicesButton.Size = new System.Drawing.Size(120, 32);
            this.refreshDevicesButton.TabIndex = 0;
            this.refreshDevicesButton.Text = "刷新设备";
            this.refreshDevicesButton.UseVisualStyleBackColor = false;
            this.refreshDevicesButton.Click += new System.EventHandler(this.RefreshDevicesButton_Click);
            //
            // kmboxTitleLabel
            //
            this.kmboxTitleLabel.AutoSize = true;
            this.kmboxTitleLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxTitleLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.kmboxTitleLabel.Location = new System.Drawing.Point(16, 57);
            this.kmboxTitleLabel.Name = "kmboxTitleLabel";
            this.kmboxTitleLabel.Size = new System.Drawing.Size(75, 17);
            this.kmboxTitleLabel.TabIndex = 2;
            this.kmboxTitleLabel.Text = "KMBox Net";
            //
            // kmboxIpLabel
            //
            this.kmboxIpLabel.AutoSize = true;
            this.kmboxIpLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxIpLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.kmboxIpLabel.Location = new System.Drawing.Point(106, 57);
            this.kmboxIpLabel.Name = "kmboxIpLabel";
            this.kmboxIpLabel.Size = new System.Drawing.Size(18, 17);
            this.kmboxIpLabel.TabIndex = 3;
            this.kmboxIpLabel.Text = "IP";
            //
            // kmboxIpTextBox
            //
            this.kmboxIpTextBox.BackColor = System.Drawing.Color.FromArgb(229, 245, 235);
            this.kmboxIpTextBox.BorderColor = System.Drawing.Color.FromArgb(134, 239, 172);
            this.kmboxIpTextBox.CornerRadius = 8;
            this.kmboxIpTextBox.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxIpTextBox.ForeColor = System.Drawing.Color.FromArgb(20, 83, 45);
            this.kmboxIpTextBox.Location = new System.Drawing.Point(132, 50);
            this.kmboxIpTextBox.Name = "kmboxIpTextBox";
            this.kmboxIpTextBox.Size = new System.Drawing.Size(150, 30);
            this.kmboxIpTextBox.TabIndex = 4;
            //
            // kmboxPortLabel
            //
            this.kmboxPortLabel.AutoSize = true;
            this.kmboxPortLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxPortLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.kmboxPortLabel.Location = new System.Drawing.Point(304, 57);
            this.kmboxPortLabel.Name = "kmboxPortLabel";
            this.kmboxPortLabel.Size = new System.Drawing.Size(35, 17);
            this.kmboxPortLabel.TabIndex = 5;
            this.kmboxPortLabel.Text = "Port";
            //
            // kmboxPortTextBox
            //
            this.kmboxPortTextBox.BackColor = System.Drawing.Color.FromArgb(229, 245, 235);
            this.kmboxPortTextBox.BorderColor = System.Drawing.Color.FromArgb(134, 239, 172);
            this.kmboxPortTextBox.CornerRadius = 8;
            this.kmboxPortTextBox.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxPortTextBox.ForeColor = System.Drawing.Color.FromArgb(20, 83, 45);
            this.kmboxPortTextBox.Location = new System.Drawing.Point(352, 50);
            this.kmboxPortTextBox.Name = "kmboxPortTextBox";
            this.kmboxPortTextBox.Size = new System.Drawing.Size(92, 30);
            this.kmboxPortTextBox.TabIndex = 6;
            //
            // kmboxMacLabel
            //
            this.kmboxMacLabel.AutoSize = true;
            this.kmboxMacLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxMacLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.kmboxMacLabel.Location = new System.Drawing.Point(482, 57);
            this.kmboxMacLabel.Name = "kmboxMacLabel";
            this.kmboxMacLabel.Size = new System.Drawing.Size(37, 17);
            this.kmboxMacLabel.TabIndex = 7;
            this.kmboxMacLabel.Text = "MAC";
            //
            // kmboxMacTextBox
            //
            this.kmboxMacTextBox.BackColor = System.Drawing.Color.FromArgb(229, 245, 235);
            this.kmboxMacTextBox.BorderColor = System.Drawing.Color.FromArgb(134, 239, 172);
            this.kmboxMacTextBox.CornerRadius = 8;
            this.kmboxMacTextBox.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxMacTextBox.ForeColor = System.Drawing.Color.FromArgb(20, 83, 45);
            this.kmboxMacTextBox.Location = new System.Drawing.Point(532, 50);
            this.kmboxMacTextBox.Name = "kmboxMacTextBox";
            this.kmboxMacTextBox.Size = new System.Drawing.Size(220, 30);
            this.kmboxMacTextBox.TabIndex = 8;
            //
            // kmboxSaveButton
            //
            this.kmboxSaveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.kmboxSaveButton.BackColor = System.Drawing.Color.FromArgb(22, 163, 74);
            this.kmboxSaveButton.BorderColor = System.Drawing.Color.FromArgb(21, 128, 61);
            this.kmboxSaveButton.CornerRadius = 9;
            this.kmboxSaveButton.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.kmboxSaveButton.ForeColor = System.Drawing.Color.White;
            this.kmboxSaveButton.Location = new System.Drawing.Point(760, 50);
            this.kmboxSaveButton.Name = "kmboxSaveButton";
            this.kmboxSaveButton.ShadowDepth = 2;
            this.kmboxSaveButton.Size = new System.Drawing.Size(120, 30);
            this.kmboxSaveButton.TabIndex = 9;
            this.kmboxSaveButton.Text = "保存硬件配置";
            this.kmboxSaveButton.UseVisualStyleBackColor = false;
            this.kmboxSaveButton.Click += new System.EventHandler(this.SaveKmBoxButton_Click);
            //
            // kmboxStatusLabel
            //
            this.kmboxStatusLabel.AutoSize = true;
            this.kmboxStatusLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F, System.Drawing.FontStyle.Bold);
            this.kmboxStatusLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.kmboxStatusLabel.Location = new System.Drawing.Point(554, 58);
            this.kmboxStatusLabel.Name = "kmboxStatusLabel";
            this.kmboxStatusLabel.Size = new System.Drawing.Size(0, 17);
            this.kmboxStatusLabel.TabIndex = 10;
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(248, 253, 250);
            this.ClientSize = new System.Drawing.Size(940, 210);
            this.Controls.Add(this.shellPanel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(760, 220);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "GreenPlayer";
            this.shellPanel.ResumeLayout(false);
            this.accountListPanel.ResumeLayout(false);
            this.topBarPanel.ResumeLayout(false);
            this.topBarPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel shellPanel;
        private Roadhog.RoundedPanel accountListPanel;
        private System.Windows.Forms.TableLayoutPanel accountTable;
        private Roadhog.RoundedPanel topBarPanel;
        private System.Windows.Forms.Label fpgaLabel;
        private Roadhog.RoundedComboBox fpgaDeviceComboBox;
        private Roadhog.RoundedButton refreshDevicesButton;
        private System.Windows.Forms.Label kmboxTitleLabel;
        private System.Windows.Forms.Label kmboxIpLabel;
        private Roadhog.RoundedTextBox kmboxIpTextBox;
        private System.Windows.Forms.Label kmboxPortLabel;
        private Roadhog.RoundedTextBox kmboxPortTextBox;
        private System.Windows.Forms.Label kmboxMacLabel;
        private Roadhog.RoundedTextBox kmboxMacTextBox;
        private Roadhog.RoundedButton kmboxSaveButton;
        private System.Windows.Forms.Label kmboxStatusLabel;
    }
}
