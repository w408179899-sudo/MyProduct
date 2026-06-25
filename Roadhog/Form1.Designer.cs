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
            this.pageTitleLabel = new System.Windows.Forms.Label();
            this.refreshDevicesButton = new Roadhog.RoundedButton();
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
            this.shellPanel.Size = new System.Drawing.Size(920, 360);
            this.shellPanel.TabIndex = 0;
            // 
            // accountListPanel
            // 
            this.accountListPanel.BackColor = System.Drawing.Color.White;
            this.accountListPanel.BorderColor = System.Drawing.Color.FromArgb(187, 247, 208);
            this.accountListPanel.CornerRadius = 12;
            this.accountListPanel.Controls.Add(this.accountTable);
            this.accountListPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accountListPanel.Location = new System.Drawing.Point(14, 66);
            this.accountListPanel.Name = "accountListPanel";
            this.accountListPanel.Padding = new System.Windows.Forms.Padding(0);
            this.accountListPanel.ShadowDepth = 3;
            this.accountListPanel.Size = new System.Drawing.Size(892, 280);
            this.accountListPanel.TabIndex = 1;
            // 
            // accountTable
            // 
            this.accountTable.BackColor = System.Drawing.Color.White;
            this.accountTable.CellBorderStyle = System.Windows.Forms.TableLayoutPanelCellBorderStyle.Single;
            this.accountTable.ColumnCount = 12;
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
            this.topBarPanel.Controls.Add(this.pageTitleLabel);
            this.topBarPanel.Controls.Add(this.refreshDevicesButton);
            this.topBarPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topBarPanel.Location = new System.Drawing.Point(14, 14);
            this.topBarPanel.Name = "topBarPanel";
            this.topBarPanel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.topBarPanel.ShadowDepth = 3;
            this.topBarPanel.Size = new System.Drawing.Size(892, 52);
            this.topBarPanel.TabIndex = 0;
            // 
            // pageTitleLabel
            // 
            this.pageTitleLabel.AutoSize = true;
            this.pageTitleLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.pageTitleLabel.ForeColor = System.Drawing.Color.FromArgb(22, 101, 52);
            this.pageTitleLabel.Location = new System.Drawing.Point(12, 15);
            this.pageTitleLabel.Name = "pageTitleLabel";
            this.pageTitleLabel.Size = new System.Drawing.Size(129, 19);
            this.pageTitleLabel.TabIndex = 1;
            this.pageTitleLabel.Text = "Roadhog 脚本UI";
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
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(248, 253, 250);
            this.ClientSize = new System.Drawing.Size(920, 360);
            this.Controls.Add(this.shellPanel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(760, 240);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Roadhog";
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
        private System.Windows.Forms.Label pageTitleLabel;
        private Roadhog.RoundedButton refreshDevicesButton;
    }
}
