using System.Drawing.Drawing2D;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog
{
    public sealed class AccountSettingsForm : Form
    {
        private const string ManualSkillMappingRowDragFormat = "Roadhog.ManualSkillMappingRow";

        private readonly string _account;
        private readonly RoadhogRuntime _runtime;
        private readonly IAccountConfigStore _configStore;
        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Color _pageBackground = Color.FromArgb(247, 252, 249);
        private readonly Color _inputBackground = Color.FromArgb(229, 245, 235);
        private readonly Color _textGreen = Color.FromArgb(20, 83, 45);

        private TabControl settingsTabs = null!;
        private RoundedTextBox? profileNameTextBox;
        private RoundedComboBox? mainModeCombo;
        private RoundedComboBox? combatModeCombo;
        private RoundedCheckBox? enableLootCheckBox;
        private RoundedCheckBox? contestMonsterCheckBox;
        private RoundedCheckBox? counterEnemyRaceCheckBox;
        private RoundedTextBox? revivePathNameTextBox;
        private RoundedTextBox? combatPathNameTextBox;
        private RoundedTextBox? maintenancePathNameTextBox;
        private RoundedCheckBox? loopPathCheckBox;
        private RoundedCheckBox? reverseAtEndCheckBox;
        private RoundedCheckBox? deathStopPathCheckBox;
        private RoundedCheckBox? sitMaintenanceCheckBox;
        private RoundedTextBox? sitMpBelowTextBox;
        private RoundedTextBox? sitMpRecoverToTextBox;
        private RoundedCheckBox? autoEquipCheckBox;
        private RoundedCheckBox? autoDecomposeCheckBox;
        private RoundedTextBox? bagCleanupThresholdTextBox;
        private RoundedTextBox? bagTotalSlotsTextBox;
        private RadioButton? skillAutoModeRadio;
        private RadioButton? skillManualModeRadio;
        private Panel? autoSkillPanel;
        private Panel? manualSkillPanel;
        private TreeView? availableSkillTree;
        private TreeView? selectedSkillTree;
        private FlowLayoutPanel? manualSkillMappingList;
        private Control? draggingManualSkillRow;
        private IReadOnlyList<SkillSnapshot> currentManualSkills = Array.Empty<SkillSnapshot>();
        private int manualSkillDropLineY = -1;

        public AccountSettingsForm(string account, RoadhogRuntime runtime, IAccountConfigStore configStore)
        {
            _account = account;
            _runtime = runtime;
            _configStore = configStore;
            InitializeSettingsForm();
        }

        private void InitializeSettingsForm()
        {
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(248, 253, 250);
            ClientSize = new Size(860, 620);
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(720, 420);
            Name = "AccountSettingsForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = $"账号设置 - {_account}";

            settingsTabs = new TabControl
            {
                Alignment = TabAlignment.Top,
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(74, 28),
                SizeMode = TabSizeMode.Fixed
            };

            settingsTabs.DrawItem += GreenTabs_DrawItem;
            settingsTabs.TabPages.Add(CreateSummaryTab());
            settingsTabs.TabPages.Add(CreatePathTab());
            settingsTabs.TabPages.Add(CreateMaintenanceTab());
            settingsTabs.TabPages.Add(CreateSkillTab());
            settingsTabs.TabPages.Add(CreateEmptyTab("测试"));

            Controls.Add(settingsTabs);
            AddButton(this, "保存配置", 418, 3, 150, 30, SaveSettingsButton_Click).BringToFront();
            LoadSavedSettings();
        }

        private void LoadSavedSettings()
        {
            var account = LoadAccountConfigOrDefault();
            ApplyScriptSettings(BuildEffectiveScriptSettings(account));
        }

        private async void SaveSettingsButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "保存中...";

            try
            {
                if (!SaveCurrentSettings(out var error))
                {
                    MessageBox.Show(this, error, "保存配置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                button.Text = "已保存";
                await Task.Delay(700).ConfigureAwait(true);
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Text = originalText;
                    button.Enabled = true;
                }
            }
        }

        private AccountConfig LoadAccountConfigOrDefault()
        {
            var result = _configStore.LoadAllAsync().GetAwaiter().GetResult();
            if (!result.Success)
            {
                MessageBox.Show(
                    this,
                    result.Error ?? "读取账号配置失败。",
                    "读取设置失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return new AccountConfig { AccountName = _account };
            }

            return result.Value?
                .FirstOrDefault(account => string.Equals(account.AccountName, _account, StringComparison.OrdinalIgnoreCase))
                ?.Clone() ?? new AccountConfig { AccountName = _account };
        }

        private static ScriptSettings BuildEffectiveScriptSettings(AccountConfig account)
        {
            if (account.ScriptSettings is not null)
            {
                return account.ScriptSettings.Clone();
            }

            return new ScriptSettings
            {
                ProfileName = string.IsNullOrWhiteSpace(account.ProfileName) ? "default_profile" : account.ProfileName,
                MainMode = account.MainMode,
                CombatMode = account.CombatMode,
                Paths = new PathScriptSettings
                {
                    RevivePathName = account.RevivePathName,
                    CombatPathName = account.CombatPathName,
                    MaintenancePathName = account.MaintenancePathName
                }
            };
        }

        private void ApplyScriptSettings(ScriptSettings settings)
        {
            SetText(profileNameTextBox, settings.ProfileName);
            SetComboText(mainModeCombo, FormatMainMode(settings.MainMode));
            SetComboText(combatModeCombo, FormatCombatMode(settings.CombatMode));

            SetChecked(enableLootCheckBox, settings.Combat.EnableLoot);
            SetChecked(contestMonsterCheckBox, settings.Combat.ContestMonster);
            SetChecked(counterEnemyRaceCheckBox, settings.Combat.CounterEnemyRace);

            SetText(revivePathNameTextBox, settings.Paths.RevivePathName);
            SetText(combatPathNameTextBox, settings.Paths.CombatPathName);
            SetText(maintenancePathNameTextBox, settings.Paths.MaintenancePathName);
            SetChecked(loopPathCheckBox, settings.Paths.LoopPath);
            SetChecked(reverseAtEndCheckBox, settings.Paths.ReverseAtEnd);
            SetChecked(deathStopPathCheckBox, settings.Paths.DeathStopPath);

            SetChecked(sitMaintenanceCheckBox, settings.Maintenance.SitMaintenanceEnabled);
            SetText(sitMpBelowTextBox, settings.Maintenance.SitMpBelowPercent.ToString());
            SetText(sitMpRecoverToTextBox, settings.Maintenance.SitMpRecoverToPercent.ToString());
            SetChecked(autoEquipCheckBox, settings.Maintenance.AutoEquip);
            SetChecked(autoDecomposeCheckBox, settings.Maintenance.AutoDecompose);
            SetText(bagCleanupThresholdTextBox, settings.Maintenance.BagCleanupThreshold.ToString());
            SetText(bagTotalSlotsTextBox, settings.Maintenance.BagTotalSlots.ToString());

            if (settings.Skills.Mode == SkillConfigurationMode.Auto)
            {
                if (skillAutoModeRadio is not null)
                {
                    skillAutoModeRadio.Checked = true;
                }

                ShowSkillMode(false);
            }
            else
            {
                if (skillManualModeRadio is not null)
                {
                    skillManualModeRadio.Checked = true;
                }

                ShowSkillMode(true);
            }

            if (selectedSkillTree is not null)
            {
                PopulateSelectedSkillTreeFromConfig(selectedSkillTree, settings.Skills.ExecutionTree);
            }

            if (manualSkillMappingList is not null)
            {
                manualSkillMappingList.Controls.Clear();
                foreach (var mapping in settings.Skills.ManualMappings)
                {
                    AddManualSkillMappingRow(manualSkillMappingList, mapping.SkillType, mapping.SkillName, mapping.Key);
                }
            }
        }

        private bool SaveCurrentSettings(out string error)
        {
            var account = LoadAccountConfigOrDefault();
            var previousSettings = BuildEffectiveScriptSettings(account);
            var capturedSettings = CaptureScriptSettings();
            capturedSettings.SemiAuto = previousSettings.SemiAuto.Clone();
            capturedSettings.Skills.KeyOrder = previousSettings.Skills.KeyOrder.Count == 0
                ? SkillScriptSettings.DefaultKeyOrder()
                : previousSettings.Skills.KeyOrder.ToList();
            capturedSettings.Skills.TriggerPrefixMode = string.IsNullOrWhiteSpace(previousSettings.Skills.TriggerPrefixMode)
                ? "TopContiguousTriggerSkills"
                : previousSettings.Skills.TriggerPrefixMode;

            account.AccountName = _account;
            account.ScriptSettings = capturedSettings;
            ApplyScriptSettingsToLegacyFields(account, account.ScriptSettings);

            var result = _configStore.UpsertAsync(account).GetAwaiter().GetResult();
            error = result.Error ?? "保存账号配置失败。";
            return result.Success;
        }

        private ScriptSettings CaptureScriptSettings()
        {
            var settings = new ScriptSettings
            {
                ProfileName = GetText(profileNameTextBox, "default_profile"),
                MainMode = ParseMainMode(mainModeCombo?.Text),
                CombatMode = ParseCombatMode(combatModeCombo?.Text),
                Combat = new CombatScriptSettings
                {
                    EnableLoot = enableLootCheckBox?.Checked ?? true,
                    ContestMonster = contestMonsterCheckBox?.Checked ?? false,
                    CounterEnemyRace = counterEnemyRaceCheckBox?.Checked ?? false
                },
                Paths = new PathScriptSettings
                {
                    RevivePathName = GetText(revivePathNameTextBox, string.Empty),
                    CombatPathName = GetText(combatPathNameTextBox, string.Empty),
                    MaintenancePathName = GetText(maintenancePathNameTextBox, string.Empty),
                    LoopPath = loopPathCheckBox?.Checked ?? true,
                    ReverseAtEnd = reverseAtEndCheckBox?.Checked ?? false,
                    DeathStopPath = deathStopPathCheckBox?.Checked ?? true
                },
                Maintenance = new MaintenanceScriptSettings
                {
                    SitMaintenanceEnabled = sitMaintenanceCheckBox?.Checked ?? true,
                    SitMpBelowPercent = ReadInt(sitMpBelowTextBox, 10),
                    SitMpRecoverToPercent = ReadInt(sitMpRecoverToTextBox, 90),
                    AutoEquip = autoEquipCheckBox?.Checked ?? true,
                    AutoDecompose = autoDecomposeCheckBox?.Checked ?? true,
                    BagCleanupThreshold = ReadInt(bagCleanupThresholdTextBox, 85),
                    BagTotalSlots = ReadInt(bagTotalSlotsTextBox, 100)
                },
                Skills = new SkillScriptSettings
                {
                    Mode = skillAutoModeRadio?.Checked == true
                        ? SkillConfigurationMode.Auto
                        : SkillConfigurationMode.ManualMapping,
                    TriggerPrefixMode = "TopContiguousTriggerSkills",
                    ExecutionTree = selectedSkillTree is null
                        ? new List<SkillConfigNode>()
                        : CaptureSkillTree(selectedSkillTree.Nodes),
                    ManualMappings = CaptureManualSkillMappings()
                }
            };

            return settings;
        }

        private static void ApplyScriptSettingsToLegacyFields(AccountConfig account, ScriptSettings settings)
        {
            account.ProfileName = settings.ProfileName;
            account.MainMode = settings.MainMode;
            account.CombatMode = settings.CombatMode;
            account.RevivePathName = settings.Paths.RevivePathName;
            account.CombatPathName = settings.Paths.CombatPathName;
            account.MaintenancePathName = settings.Paths.MaintenancePathName;
        }

        private static void SetText(RoundedTextBox? textBox, string? value)
        {
            if (textBox is not null)
            {
                textBox.Text = value ?? string.Empty;
            }
        }

        private static string GetText(RoundedTextBox? textBox, string fallback)
        {
            return string.IsNullOrWhiteSpace(textBox?.Text)
                ? fallback
                : textBox.Text.Trim();
        }

        private static void SetComboText(RoundedComboBox? comboBox, string value)
        {
            if (comboBox is null)
            {
                return;
            }

            comboBox.Text = value;
        }

        private static void SetChecked(RoundedCheckBox? checkBox, bool value)
        {
            if (checkBox is not null)
            {
                checkBox.Checked = value;
            }
        }

        private static int ReadInt(RoundedTextBox? textBox, int fallback)
        {
            return int.TryParse(textBox?.Text, out var value)
                ? value
                : fallback;
        }

        private static string FormatMainMode(AccountMainMode mode)
        {
            return mode switch
            {
                AccountMainMode.Gather => "采集",
                AccountMainMode.Craft => "制作",
                AccountMainMode.SemiAuto => "半自动",
                _ => "自定义打怪"
            };
        }

        private static AccountMainMode ParseMainMode(string? text)
        {
            return text switch
            {
                "采集" => AccountMainMode.Gather,
                "制作" => AccountMainMode.Craft,
                "半自动" => AccountMainMode.SemiAuto,
                _ => AccountMainMode.CustomCombat
            };
        }

        private static string FormatCombatMode(AccountCombatMode mode)
        {
            return mode == AccountCombatMode.Path ? "路径打怪" : "原地打怪";
        }

        private static AccountCombatMode ParseCombatMode(string? text)
        {
            return string.Equals(text, "路径打怪", StringComparison.Ordinal)
                ? AccountCombatMode.Path
                : AccountCombatMode.Stationary;
        }

        private TabPage CreateSummaryTab()
        {
            var tab = CreateBaseTab("总览");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "方案", 4, 8, 80, 22);
            profileNameTextBox = AddTextBox(page, "default_profile", 4, 32, 220, 26);
            AddLabel(page, "方案名", 230, 36, 80, 22);

            mainModeCombo = AddCombo(page, 4, 72, 220, 28, "自定义打怪", "采集", "制作", "半自动");
            AddLabel(page, "主模式", 230, 76, 80, 22, Color.FromArgb(220, 38, 38), FontStyle.Bold);

            combatModeCombo = AddCombo(page, 4, 104, 220, 28, "原地打怪", "路径打怪");
            AddLabel(page, "打怪模式", 230, 108, 80, 22);

            enableLootCheckBox = AddCheckBox(page, "启用拾取", 4, 142, 88, true);
            contestMonsterCheckBox = AddCheckBox(page, "抢怪", 96, 142, 64, false);
            counterEnemyRaceCheckBox = AddCheckBox(page, "反击敌对种族", 160, 142, 140, false);

            var combatAdvanced = CreateFoldout(page, "高级打怪设置", 176, 850, false);
            combatAdvanced.Content.Height = 58;

            return tab;
        }

        private TabPage CreatePathTab()
        {
            var tab = CreateBaseTab("路径");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "挂机路径选择:", 4, 8, 130, 22, _textGreen, FontStyle.Bold);
            AddLabel(page, "复活路径:  穆尔海姆00133（1点）", 24, 34, 320, 22);
            AddLabel(page, "打怪路径:  未选（0点）", 24, 60, 260, 22);
            AddLabel(page, "维护路径:  未选（0点）", 24, 86, 260, 22);

            var pathTabs = new TabControl
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(92, 28),
                Location = new Point(0, 114),
                Name = "pathTabs",
                SelectedIndex = 0,
                Size = new Size(850, 462),
                SizeMode = TabSizeMode.Fixed
            };

            pathTabs.DrawItem += GreenTabs_DrawItem;
            pathTabs.TabPages.Add(CreatePathEditorTab("复活路径", "死亡复活后返回主路径", true));
            pathTabs.TabPages.Add(CreatePathEditorTab("打怪路径", "打怪巡逻路径", false));
            pathTabs.TabPages.Add(CreatePathEditorTab("维护路径", "维护补给路径", false));
            page.Controls.Add(pathTabs);

            return tab;
        }

        private TabPage CreatePathEditorTab(string title, string caption, bool includeSamplePoint)
        {
            var tab = new TabPage
            {
                BackColor = _pageBackground,
                Padding = Padding.Empty,
                Text = title
            };

            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, caption, 4, 8, 220, 22, _textGreen, FontStyle.Bold);
            var pathNameTextBox = AddTextBox(page, includeSamplePoint ? "穆尔海姆00133" : string.Empty, 4, 38, 242, 28);
            if (string.Equals(title, "复活路径", StringComparison.Ordinal))
            {
                revivePathNameTextBox = pathNameTextBox;
            }
            else if (string.Equals(title, "打怪路径", StringComparison.Ordinal))
            {
                combatPathNameTextBox = pathNameTextBox;
            }
            else if (string.Equals(title, "维护路径", StringComparison.Ordinal))
            {
                maintenancePathNameTextBox = pathNameTextBox;
            }

            AddLabel(page, "路径名", 252, 42, 54, 22);
            AddCombo(page, "穆尔海姆00133（1点）", 306, 38, 254, 28);
            AddLabel(page, "已保存路径", 566, 42, 120, 22);

            AddButton(page, "保存到列表", 6, 74, 100, 30);
            AddButton(page, "删除保存", 114, 74, 92, 30);
            AddLabel(page, "点数  1  |  总距  0.0  |  无效  0", 6, 112, 300, 24, _textGreen, FontStyle.Bold);

            AddButton(page, "开始录制", 6, 144, 100, 30);
            AddButton(page, "停止录制", 116, 144, 100, 30);
            AddButton(page, "清空", 226, 144, 68, 30);
            AddButton(page, "复制路径", 304, 144, 88, 30);

            var pointsBox = new RoundedTextBox
            {
                BackColor = _inputBackground,
                BorderColor = Color.FromArgb(134, 239, 172),
                CornerRadius = 9,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(6, 184),
                Multiline = true,
                ReadOnly = false,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(562, 106),
                Text = includeSamplePoint ? "1307.758, 2844.230, 259.832" : string.Empty
            };
            page.Controls.Add(pointsBox);

            var pathAdvanced = CreateFoldout(page, "高级路径设置", 302, 850, true);
            pathAdvanced.Content.Height = 68;
            var loopCheckBox = AddCheckBox(pathAdvanced.Content, "循环路径", 6, 12, 92, true);
            var reverseCheckBox = AddCheckBox(pathAdvanced.Content, "到终点反向", 102, 12, 106, false);
            var deathStopCheckBox = AddCheckBox(pathAdvanced.Content, "死亡停止路径", 206, 12, 130, true);
            if (string.Equals(title, "复活路径", StringComparison.Ordinal))
            {
                loopPathCheckBox = loopCheckBox;
                reverseAtEndCheckBox = reverseCheckBox;
                deathStopPathCheckBox = deathStopCheckBox;
            }

            return tab;
        }

        private TabPage CreateMaintenanceTab()
        {
            var tab = CreateBaseTab("维护");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "坐地板维护", 4, 8, 82, 24, _textGreen, FontStyle.Bold);
            sitMaintenanceCheckBox = AddCheckBox(page, "启用", 84, 6, 70, true);

            AddLabel(page, "蓝量低于", 4, 44, 66, 24);
            sitMpBelowTextBox = AddTextBox(page, "10", 68, 42, 70, 28);
            AddLabel(page, "%  坐地板，恢复到", 144, 44, 130, 24);
            sitMpRecoverToTextBox = AddTextBox(page, "90", 272, 42, 70, 28);
            AddLabel(page, "%  起来继续打怪", 348, 44, 160, 24);

            AddLabel(page, "血量维护", 4, 82, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增血量维护", 68, 78, 120, 30);
            AddLabel(page, "暂无血量维护", 4, 116, 140, 24);

            AddLabel(page, "蓝量维护", 4, 154, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增蓝量维护", 68, 150, 120, 30);
            AddLabel(page, "暂无蓝量维护", 4, 188, 140, 24);

            var separator = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _softGreen,
                Location = new Point(0, 222),
                Size = new Size(850, 1)
            };
            page.Controls.Add(separator);

            autoEquipCheckBox = AddCheckBox(page, "自动穿装备", 4, 230, 106, true);
            autoDecomposeCheckBox = AddCheckBox(page, "自动分解装备", 112, 230, 126, true);

            var advanced = CreateFoldout(page, "高级设置", 266, 850, true);
            advanced.Content.Height = 88;
            bagCleanupThresholdTextBox = AddNumberSetting(advanced.Content, "85", "清包阈值", 6, 12);
            bagTotalSlotsTextBox = AddNumberSetting(advanced.Content, "100", "背包总格数", 6, 46);

            return tab;
        }

        private TabPage CreateSkillTab()
        {
            var tab = CreateBaseTab("技能");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "技能配置", 4, 16, 90, 24, _textGreen, FontStyle.Bold);
            var autoMode = AddRadioButton(page, "自动技能", 92, 14, 90, false);
            skillAutoModeRadio = autoMode;
            var manualMode = AddRadioButton(page, "手动技能Mapping", 184, 14, 142, true);
            skillManualModeRadio = manualMode;

            var autoPanel = CreateSkillModePanel(page, "autoSkillPanel", false);
            autoSkillPanel = autoPanel;
            var manualPanel = CreateSkillModePanel(page, "manualSkillPanel", true);
            manualSkillPanel = manualPanel;

            AddLabel(autoPanel, "可用技能", 8, 6, 120, 24, _textGreen, FontStyle.Bold);
            AddLabel(autoPanel, "技能执行顺序", 378, 6, 140, 24, _textGreen, FontStyle.Bold);

            var availableTree = CreateSkillTree(autoPanel, "availableSkillTree", 8, 34, 260, 410);
            availableSkillTree = availableTree;
            var selectedTree = CreateSkillTree(autoPanel, "selectedSkillTree", 378, 34, 300, 410);
            selectedSkillTree = selectedTree;
            PopulateAvailableSkillTree(availableTree);
            PopulateSelectedSkillTree(selectedTree);

            var refreshSkillsButton = AddButton(page, "刷新当前技能", 390, 10, 150, 30);
            refreshSkillsButton.Click += async (_, _) =>
                await RefreshCurrentSkillsAsync(refreshSkillsButton, autoMode.Checked, availableTree).ConfigureAwait(true);

            AddButton(autoPanel, "添加 >", 288, 102, 70, 30, (_, _) => AddSkillSelection(availableTree, selectedTree));
            AddButton(autoPanel, "< 移除", 288, 140, 70, 30, (_, _) => RemoveSelectedSkill(selectedTree));
            AddButton(autoPanel, "全部 >>", 288, 178, 70, 30, (_, _) => AddAllAvailableSkills(availableTree, selectedTree));
            AddButton(autoPanel, "清空", 288, 216, 70, 30, (_, _) => selectedTree.Nodes.Clear());

            AddButton(autoPanel, "置顶", 696, 102, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Top));
            AddButton(autoPanel, "上移", 696, 140, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Up));
            AddButton(autoPanel, "下移", 696, 178, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Down));
            AddButton(autoPanel, "置底", 696, 216, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Bottom));

            AddLabel(manualPanel, "手动技能Mapping", 8, 6, 130, 24, _textGreen, FontStyle.Bold);

            var mappingRows = CreateManualSkillMappingList(manualPanel);

            AddButton(manualPanel, "新增技能Mapping", 136, 0, 132, 30, (_, _) => AddManualSkillMapping(mappingRows));
            AddButton(manualPanel, "清空", 276, 0, 62, 30, (_, _) => mappingRows.Controls.Clear());

            autoMode.CheckedChanged += (_, _) =>
            {
                if (autoMode.Checked)
                {
                    ShowSkillMode(false);
                }
            };

            manualMode.CheckedChanged += (_, _) =>
            {
                if (manualMode.Checked)
                {
                    ShowSkillMode(true);
                }
            };

            return tab;
        }

        private TabPage CreateEmptyTab(string title)
        {
            var tab = CreateBaseTab(title);
            tab.Controls.Add(CreatePagePanel());
            return tab;
        }

        private TabPage CreateBaseTab(string title)
        {
            return new TabPage
            {
                BackColor = _pageBackground,
                Padding = Padding.Empty,
                Text = title
            };
        }

        private Panel CreatePagePanel()
        {
            return new Panel
            {
                BackColor = _pageBackground,
                Dock = DockStyle.Fill
            };
        }

        private void AddLabel(Control parent, string text, int x, int y, int width, int height, Color? foreColor = null, FontStyle style = FontStyle.Regular)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, style),
                ForeColor = foreColor ?? _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }

        private RoundedTextBox AddTextBox(Control parent, string text, int x, int y, int width, int height)
        {
            var textBox = new RoundedTextBox
            {
                BackColor = _inputBackground,
                BorderColor = Color.FromArgb(134, 239, 172),
                CornerRadius = 8,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text
            };

            parent.Controls.Add(textBox);
            return textBox;
        }

        private RoundedComboBox AddCombo(Control parent, string value, int x, int y, int width, int height)
        {
            return AddCombo(parent, x, y, width, height, value);
        }

        private RoundedComboBox AddCombo(Control parent, int x, int y, int width, int height, params string[] values)
        {
            var combo = new RoundedComboBox
            {
                BackColor = _inputBackground,
                BorderColor = Color.FromArgb(134, 239, 172),
                CornerRadius = 8,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height)
            };

            combo.Items.AddRange(values);
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            parent.Controls.Add(combo);
            return combo;
        }

        private Button AddButton(Control parent, string text, int x, int y, int width, int height, EventHandler? click = null)
        {
            var button = new RoundedButton
            {
                BackColor = _primaryGreen,
                BorderColor = _darkGreen,
                CornerRadius = 8,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                ShadowDepth = 2,
                Size = new Size(width, height),
                Text = text,
                UseVisualStyleBackColor = false
            };

            if (click is not null)
            {
                button.Click += click;
            }

            parent.Controls.Add(button);
            return button;
        }

        private RoundedTextBox AddNumberSetting(Control parent, string value, string label, int x, int y)
        {
            var textBox = AddTextBox(parent, value, x, y, 56, 28);
            AddSmallButton(parent, "-", x + 62, y, 24, 28);
            AddSmallButton(parent, "+", x + 90, y, 24, 28);
            AddLabel(parent, label, x + 122, y + 2, 120, 24);
            return textBox;
        }

        private void AddSmallButton(Control parent, string text, int x, int y, int width, int height)
        {
            var button = new RoundedButton
            {
                BackColor = _primaryGreen,
                BorderColor = _darkGreen,
                CornerRadius = 7,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                ShadowDepth = 2,
                Size = new Size(width, height),
                Text = text,
                UseVisualStyleBackColor = false
            };

            parent.Controls.Add(button);
        }

        private RoundedCheckBox AddCheckBox(Control parent, string text, int x, int y, int width, bool isChecked)
        {
            var checkBox = new RoundedCheckBox
            {
                BackColor = _pageBackground,
                Checked = isChecked,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Text = text
            };

            parent.Controls.Add(checkBox);
            return checkBox;
        }

        private RadioButton AddRadioButton(Control parent, string text, int x, int y, int width, bool isChecked)
        {
            var radioButton = new RadioButton
            {
                Appearance = Appearance.Normal,
                AutoSize = false,
                BackColor = _pageBackground,
                Checked = isChecked,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Text = text,
                UseVisualStyleBackColor = false
            };

            parent.Controls.Add(radioButton);
            return radioButton;
        }

        private Panel CreateSkillModePanel(Control parent, string name, bool visible)
        {
            var panel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _pageBackground,
                Location = new Point(0, 48),
                Name = name,
                Size = new Size(850, 528),
                Visible = visible
            };

            parent.Controls.Add(panel);
            return panel;
        }

        private void ShowSkillMode(bool manual)
        {
            if (autoSkillPanel is not null)
            {
                autoSkillPanel.Visible = !manual;
            }

            if (manualSkillPanel is not null)
            {
                manualSkillPanel.Visible = manual;
            }
        }

        private FoldoutSection CreateFoldout(Control parent, string title, int y, int width, bool expanded)
        {
            var header = new RoundedPanel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _softGreen,
                BorderColor = Color.FromArgb(187, 247, 208),
                CornerRadius = 8,
                Cursor = Cursors.Hand,
                Location = new Point(0, y),
                ShadowDepth = 1,
                Size = new Size(width, 28)
            };

            var label = new Label
            {
                AutoSize = false,
                Cursor = Cursors.Hand,
                Dock = DockStyle.Left,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                Padding = new Padding(10, 0, 0, 0),
                Text = $"{(expanded ? "▼" : "▶")}  {title}",
                TextAlign = ContentAlignment.MiddleLeft,
                Width = 220
            };

            var content = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _pageBackground,
                Location = new Point(0, y + 28),
                Size = new Size(width, 0),
                Visible = expanded
            };

            void ToggleFoldout()
            {
                expanded = !expanded;
                content.Visible = expanded;
                label.Text = $"{(expanded ? "▼" : "▶")}  {title}";
            }

            header.Click += (_, _) => ToggleFoldout();
            label.Click += (_, _) => ToggleFoldout();

            header.Controls.Add(label);
            parent.Controls.Add(header);
            parent.Controls.Add(content);

            return new FoldoutSection(content);
        }

        private sealed record FoldoutSection(Panel Content);

        private TreeView CreateSkillTree(Control parent, string name, int x, int y, int width, int height)
        {
            var tree = new TreeView
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                FullRowSelect = true,
                HideSelection = false,
                Location = new Point(x, y),
                Name = name,
                PathSeparator = " / ",
                ShowLines = true,
                Size = new Size(width, height)
            };

            parent.Controls.Add(tree);
            return tree;
        }

        private FlowLayoutPanel CreateManualSkillMappingList(Control parent)
        {
            var list = new FlowLayoutPanel
            {
                AllowDrop = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BackColor = _pageBackground,
                FlowDirection = FlowDirection.TopDown,
                Location = new Point(8, 38),
                Name = "manualSkillMappingList",
                Padding = new Padding(0),
                Size = new Size(810, 406),
                WrapContents = false
            };

            list.DragEnter += ManualSkillMappingList_DragEnter;
            list.DragOver += ManualSkillMappingList_DragOver;
            list.DragDrop += ManualSkillMappingList_DragDrop;
            list.DragLeave += ManualSkillMappingList_DragLeave;
            list.Paint += ManualSkillMappingList_Paint;

            parent.Controls.Add(list);
            manualSkillMappingList = list;
            return list;
        }

        private void PopulateAvailableSkillTree(TreeView tree)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            tree.EndUpdate();
        }

        private void PopulateAvailableSkillTreeFromSkills(TreeView tree, IReadOnlyList<SkillSnapshot> skills)
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                var visibleSkills = skills
                    .Where(skill => !ShouldHideManualSkillCandidate(skill))
                    .ToArray();
                var chainRootSkillKeys = GetChainRootSkillKeys(visibleSkills);

                foreach (var category in ManualSkillCategories)
                {
                    var categoryNode = tree.Nodes.Add(category, category);

                    if (string.Equals(category, "连续技", StringComparison.Ordinal))
                    {
                        PopulateChainSkillTree(categoryNode, visibleSkills);
                    }
                    else
                    {
                        AddSkillLeaves(
                            categoryNode,
                            visibleSkills
                                .Where(skill => !chainRootSkillKeys.Contains(GetSkillKey(skill)))
                                .Where(skill => MatchesManualSkillType(skill, category)));
                    }

                    if (categoryNode.Nodes.Count == 0)
                    {
                        tree.Nodes.Remove(categoryNode);
                    }
                }

                tree.ExpandAll();
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private void PopulateChainSkillTree(TreeNode categoryNode, IReadOnlyList<SkillSnapshot> visibleSkills)
        {
            var chainSkills = visibleSkills
                .Where(skill => MatchesManualSkillType(skill, "连续技"))
                .ToArray();
            var emittedSkillKeys = new HashSet<string>(StringComparer.Ordinal);
            var chainRoots = visibleSkills
                .Where(skill => !MatchesManualSkillType(skill, "连续技"))
                .Where(skill => HasUsefulSkillValue(skill.XmlChainCategory))
                .Where(skill => chainSkills.Any(child => SameSkillValue(child.XmlPrechainCategory, skill.XmlChainCategory)))
                .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture)
                .ToArray();

            foreach (var rootSkill in chainRoots)
            {
                var rootName = FormatManualSkillName(rootSkill);
                var rootNode = categoryNode.Nodes.Add(rootName, rootName);
                rootNode.Tag = CreateSkillTreeNodeData(rootSkill);
                AddChainChildren(
                    rootNode,
                    rootSkill,
                    chainSkills,
                    emittedSkillKeys,
                    new HashSet<string>(StringComparer.Ordinal));

                if (rootNode.Nodes.Count == 0)
                {
                    categoryNode.Nodes.Remove(rootNode);
                }
            }

            AddSkillLeaves(
                categoryNode,
                chainSkills
                    .Where(skill => !emittedSkillKeys.Contains(GetSkillKey(skill))));
        }

        private static HashSet<string> GetChainRootSkillKeys(IReadOnlyList<SkillSnapshot> visibleSkills)
        {
            var chainSkills = visibleSkills
                .Where(skill => MatchesManualSkillType(skill, "连续技"))
                .ToArray();

            return visibleSkills
                .Where(skill => !MatchesManualSkillType(skill, "连续技"))
                .Where(skill => HasUsefulSkillValue(skill.XmlChainCategory))
                .Where(skill => chainSkills.Any(child => SameSkillValue(child.XmlPrechainCategory, skill.XmlChainCategory)))
                .Select(GetSkillKey)
                .ToHashSet(StringComparer.Ordinal);
        }

        private void AddChainChildren(
            TreeNode parentNode,
            SkillSnapshot parentSkill,
            IReadOnlyList<SkillSnapshot> chainSkills,
            HashSet<string> emittedSkillKeys,
            HashSet<string> pathSkillKeys)
        {
            if (!HasUsefulSkillValue(parentSkill.XmlChainCategory))
            {
                return;
            }

            var children = chainSkills
                .Where(skill => SameSkillValue(skill.XmlPrechainCategory, parentSkill.XmlChainCategory))
                .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture)
                .ToArray();

            foreach (var childSkill in children)
            {
                var childKey = GetSkillKey(childSkill);
                if (!emittedSkillKeys.Add(childKey) || !pathSkillKeys.Add(childKey))
                {
                    continue;
                }

                var childName = FormatManualSkillName(childSkill);
                var childNode = parentNode.Nodes.Add(childName, childName);
                childNode.Tag = CreateSkillTreeNodeData(childSkill);
                AddChainChildren(childNode, childSkill, chainSkills, emittedSkillKeys, pathSkillKeys);
                pathSkillKeys.Remove(childKey);
            }
        }

        private static void AddSkillLeaves(TreeNode parentNode, IEnumerable<SkillSnapshot> skills)
        {
            var orderedSkills = skills
                .GroupBy(GetSkillKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture);

            foreach (var skill in orderedSkills)
            {
                var skillName = FormatManualSkillName(skill);
                if (string.IsNullOrWhiteSpace(skillName))
                {
                    continue;
                }

                var node = parentNode.Nodes.Add(skillName, skillName);
                node.Tag = CreateSkillTreeNodeData(skill);
            }
        }

        private static SkillTreeNodeData CreateSkillTreeNodeData(SkillSnapshot skill)
        {
            return new SkillTreeNodeData(
                skill.SkillId,
                FormatManualSkillName(skill),
                GetSkillBaseName(skill),
                GetManualSkillCategory(skill),
                ParseNullableInt(skill.XmlChainTime));
        }

        private static void AddSkillLeaves(TreeNode parentNode, IEnumerable<string> skillNames)
        {
            foreach (var skillName in skillNames
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(name => name, StringComparer.CurrentCulture))
            {
                parentNode.Nodes.Add(skillName, skillName);
            }
        }

        private static bool SameSkillValue(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right) ||
                !HasUsefulSkillValue(left) ||
                !HasUsefulSkillValue(right))
            {
                return false;
            }

            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSkillKey(SkillSnapshot skill)
        {
            return skill.SkillId + "|" + FormatManualSkillName(skill);
        }

        private static int? ParseNullableInt(string? value)
        {
            return int.TryParse(value, out var parsed)
                ? parsed
                : null;
        }

        private sealed record SkillTreeNodeData(
            uint SkillId,
            string Name,
            string BaseName,
            string Type,
            int? ChainTimeMs);

        private void PopulateSelectedSkillTree(TreeView tree)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            tree.EndUpdate();
        }

        private static void PopulateSelectedSkillTreeFromConfig(TreeView tree, IReadOnlyList<SkillConfigNode> nodes)
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                foreach (var node in nodes)
                {
                    AddConfiguredSkillNode(tree.Nodes, node);
                }

                tree.ExpandAll();
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private static TreeNode AddConfiguredSkillNode(TreeNodeCollection targetNodes, SkillConfigNode config)
        {
            var text = string.IsNullOrWhiteSpace(config.Name)
                ? "Skill " + config.SkillId
                : config.Name;
            var node = targetNodes.Add(text, text);
            node.Tag = new SkillTreeNodeData(
                config.SkillId,
                text,
                config.BaseName,
                config.Type,
                config.ChainTimeMs);

            foreach (var child in config.Children)
            {
                AddConfiguredSkillNode(node.Nodes, child);
            }

            return node;
        }

        private static List<SkillConfigNode> CaptureSkillTree(TreeNodeCollection nodes)
        {
            var results = new List<SkillConfigNode>();
            foreach (TreeNode node in nodes)
            {
                results.Add(CaptureSkillNode(node));
            }

            return results;
        }

        private static SkillConfigNode CaptureSkillNode(TreeNode node)
        {
            var data = node.Tag as SkillTreeNodeData;
            var config = new SkillConfigNode
            {
                SkillId = data?.SkillId ?? 0,
                Name = data?.Name ?? node.Text,
                BaseName = data?.BaseName ?? node.Text,
                Type = data?.Type ?? InferSkillNodeType(node),
                ChainTimeMs = data?.ChainTimeMs,
                Children = CaptureSkillTree(node.Nodes)
            };

            return config;
        }

        private static string InferSkillNodeType(TreeNode node)
        {
            return node.Parent is null ? string.Empty : "连续技";
        }

        private List<ManualSkillMappingConfig> CaptureManualSkillMappings()
        {
            if (manualSkillMappingList is null)
            {
                return new List<ManualSkillMappingConfig>();
            }

            return manualSkillMappingList.Controls
                .OfType<Panel>()
                .Select(row =>
                {
                    var typeCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "manualSkillTypeCombo", StringComparison.Ordinal));
                    var skillCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "manualSkillNameCombo", StringComparison.Ordinal));
                    var keyButton = row.Controls
                        .OfType<Button>()
                        .FirstOrDefault(button => string.Equals(button.Name, "manualSkillKeyButton", StringComparison.Ordinal));

                    return new ManualSkillMappingConfig
                    {
                        SkillType = typeCombo?.Text ?? string.Empty,
                        SkillName = skillCombo?.Text ?? string.Empty,
                        Key = keyButton?.Tag as string ?? string.Empty
                    };
                })
                .Where(mapping =>
                    !string.IsNullOrWhiteSpace(mapping.SkillType) ||
                    !string.IsNullOrWhiteSpace(mapping.SkillName) ||
                    !string.IsNullOrWhiteSpace(mapping.Key))
                .ToList();
        }

        private void AddSkillSelection(TreeView source, TreeView target)
        {
            if (source.SelectedNode is null)
            {
                return;
            }

            AddAvailableSkillNode(target, source.SelectedNode);
        }

        private void AddAllAvailableSkills(TreeView source, TreeView target)
        {
            foreach (TreeNode node in source.Nodes)
            {
                AddAvailableSkillNode(target, node);
            }
        }

        private static void AddAvailableSkillNode(TreeView target, TreeNode sourceNode)
        {
            TreeNode? selectedNode = null;

            if (IsAvailableSkillCategoryNode(sourceNode))
            {
                foreach (TreeNode child in sourceNode.Nodes)
                {
                    selectedNode = AddSkillSubtreeIfMissing(target.Nodes, child);
                }
            }
            else
            {
                selectedNode = AddSkillSubtreeIfMissing(target.Nodes, sourceNode);
            }

            target.ExpandAll();
            if (selectedNode is not null)
            {
                target.SelectedNode = selectedNode;
            }
        }

        private static TreeNode AddSkillSubtreeIfMissing(TreeNodeCollection targetNodes, TreeNode sourceNode)
        {
            var targetNode = FindDirectNodeByText(targetNodes, sourceNode.Text);
            if (targetNode is null)
            {
                targetNode = targetNodes.Add(sourceNode.Text, sourceNode.Text);
                targetNode.Tag = sourceNode.Tag;
            }

            foreach (TreeNode child in sourceNode.Nodes)
            {
                AddSkillSubtreeIfMissing(targetNode.Nodes, child);
            }

            targetNode.Expand();
            return targetNode;
        }

        private static TreeNode? FindDirectNodeByText(TreeNodeCollection nodes, string text)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(node.Text, text, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        private static bool IsAvailableSkillCategoryNode(TreeNode node)
        {
            return node.Parent is null &&
                   ManualSkillCategories.Any(category => string.Equals(category, node.Text, StringComparison.Ordinal));
        }

        private void AddManualSkillMapping(FlowLayoutPanel target)
        {
            AddManualSkillMappingRow(target);
        }

        private void AddManualSkillMappingRow(
            FlowLayoutPanel list,
            string skillType = "主动技能",
            string skillName = "",
            string key = "")
        {
            var row = new Panel
            {
                BackColor = _pageBackground,
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.SizeAll,
                Margin = new Padding(0, 0, 0, 7),
                Size = new Size(506, 31),
                Tag = skillName
            };

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = _textGreen,
                Location = new Point(0, 3),
                Size = new Size(32, 24),
                Text = "技能",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var typeCombo = AddCombo(row, 34, 1, 118, 28, ManualSkillCategories);
            typeCombo.Name = "manualSkillTypeCombo";
            if (!string.IsNullOrWhiteSpace(skillType) && typeCombo.Items.Contains(skillType))
            {
                typeCombo.Text = skillType;
            }

            var skillCombo = AddCombo(row, 158, 1, 132, 28);
            skillCombo.Name = "manualSkillNameCombo";
            PopulateManualSkillNameCombo(skillCombo, typeCombo.Text);
            if (!string.IsNullOrWhiteSpace(skillName))
            {
                skillCombo.Text = skillName;
            }

            typeCombo.SelectedIndexChanged += (_, _) => PopulateManualSkillNameCombo(skillCombo, typeCombo.Text);

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(296, 3),
                Size = new Size(24, 24),
                Text = "按",
                TextAlign = ContentAlignment.MiddleCenter
            });

            var keyButton = AddButton(row, "选择按键", 324, 0, 104, 30);
            keyButton.Name = "manualSkillKeyButton";
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = key;
                keyButton.Text = FormatSkillKey(key);
            }

            AddButton(row, "删除", 436, 0, 58, 30);

            keyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(keyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    keyButton.Tag = selectedKey;
                    keyButton.Text = FormatSkillKey(selectedKey);
                }
            };

            row.Controls.OfType<RoundedButton>().First(button => button.Text == "删除").Click += (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
            };

            EnableManualSkillMappingRowDrag(list, row);
            list.Controls.Add(row);
        }

        private async Task RefreshCurrentSkillsAsync(Button button, bool refreshAutoTree, TreeView availableTree)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "刷新中...";

            try
            {
                var result = await _runtime.RefreshSkillsAsync(_account).ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    MessageBox.Show(
                        this,
                        result.Error ?? "刷新当前技能失败。",
                        "刷新当前技能",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                currentManualSkills = result.Value;
                if (refreshAutoTree)
                {
                    PopulateAvailableSkillTreeFromSkills(availableTree, currentManualSkills);
                }
                else
                {
                    RefreshManualSkillMappingCombos();
                }

                button.Text = "已刷新 " + currentManualSkills.Count;
                await Task.Delay(700).ConfigureAwait(true);
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Text = originalText;
                    button.Enabled = true;
                }
            }
        }

        private void RefreshManualSkillMappingCombos()
        {
            if (manualSkillMappingList is null)
            {
                return;
            }

            foreach (var row in manualSkillMappingList.Controls.OfType<Panel>())
            {
                var typeCombo = row.Controls
                    .OfType<RoundedComboBox>()
                    .FirstOrDefault(combo => string.Equals(combo.Name, "manualSkillTypeCombo", StringComparison.Ordinal));
                var skillCombo = row.Controls
                    .OfType<RoundedComboBox>()
                    .FirstOrDefault(combo => string.Equals(combo.Name, "manualSkillNameCombo", StringComparison.Ordinal));

                if (typeCombo is not null && skillCombo is not null)
                {
                    PopulateManualSkillNameCombo(skillCombo, typeCombo.Text);
                }
            }
        }

        private void PopulateManualSkillNameCombo(RoundedComboBox combo, string skillType)
        {
            var previous = combo.Text;
            combo.Items.Clear();
            combo.Items.AddRange(GetManualSkillNames(skillType));

            if (!string.IsNullOrWhiteSpace(previous) && combo.Items.Contains(previous))
            {
                combo.Text = previous;
                return;
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private string[] GetManualSkillNames(string skillType)
        {
            if (currentManualSkills.Count > 0)
            {
                var names = currentManualSkills
                    .Where(skill => !ShouldHideManualSkillCandidate(skill))
                    .Where(skill => MatchesManualSkillType(skill, skillType))
                    .Select(FormatManualSkillName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.CurrentCulture)
                    .ToArray();

                if (names.Length > 0)
                {
                    return names;
                }
            }

            return GetDefaultManualSkillNames(skillType);
        }

        private static string[] GetDefaultManualSkillNames(string skillType)
        {
            return skillType switch
            {
                "状态技能" => new[] { "保护之盾", "主神之盔甲", "捕获" },
                "触发技能" => new[] { "盾牌反击", "惩戒一击", "盾牌猛击" },
                "连续技" => new[] { "会心一击", "气合", "必灭一击", "连续乱打" },
                "DP技能" => new[] { "暗黑之惩戒" },
                "激活技能" => new[] { "铜墙铁壁", "盾牌防御" },
                _ => new[] { "弱化之猛击", "挑衅", "猛烈一击", "突击", "盾牌重击", "闪光斩", "挑衅猛击" }
            };
        }

        private static bool MatchesManualSkillType(SkillSnapshot skill, string skillType)
        {
            return string.Equals(GetManualSkillCategory(skill), skillType, StringComparison.Ordinal);
        }

        private static bool HasSkillTag(SkillSnapshot skill, string tag)
        {
            if (string.IsNullOrWhiteSpace(skill.XmlTags))
            {
                return false;
            }

            return skill.XmlTags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetManualSkillCategory(SkillSnapshot skill)
        {
            var baseName = GetSkillBaseName(skill);

            if (IsNamedSkill(baseName, DpSkillBaseNames) ||
                HasSkillTag(skill, "dp") ||
                HasUsefulSkillValue(skill.XmlCostDp))
            {
                return "DP技能";
            }

            if (IsNamedSkill(baseName, ActivatedSkillBaseNames) ||
                HasSkillTag(skill, "toggle") ||
                string.Equals(skill.XmlActivation, "Toggle", StringComparison.OrdinalIgnoreCase))
            {
                return "激活技能";
            }

            if (IsNamedSkill(baseName, TriggerSkillBaseNames) ||
                HasSkillTag(skill, "counter") ||
                HasUsefulSkillValue(skill.XmlCounterSkill))
            {
                return "触发技能";
            }

            if (IsNamedSkill(baseName, ChainSkillBaseNames) ||
                HasUsefulSkillValue(skill.XmlPrechainCategory) ||
                HasUsefulSkillValue(skill.XmlChainTime))
            {
                return "连续技";
            }

            if (IsNamedSkill(baseName, ActiveSkillBaseNames))
            {
                return "主动技能";
            }

            if (IsNamedSkill(baseName, StatusSkillBaseNames) ||
                HasSkillTag(skill, "status") ||
                HasSkillTag(skill, "buff") ||
                HasSkillTag(skill, "debuff"))
            {
                return "状态技能";
            }

            return "主动技能";
        }

        private static string GetSkillBaseName(SkillSnapshot skill)
        {
            var name = string.IsNullOrWhiteSpace(skill.DisplayBaseName)
                ? skill.Name
                : skill.DisplayBaseName;

            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim();
        }

        private static bool IsNamedSkill(string baseName, string[] names)
        {
            return names.Any(name => string.Equals(baseName, name, StringComparison.Ordinal));
        }

        private static bool HasUsefulSkillValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var token = value.Trim();
            return !string.Equals(token, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(token, "n/a", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(token, "none", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(token, "null", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(token, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldHideManualSkillCandidate(SkillSnapshot skill)
        {
            var baseName = GetSkillBaseName(skill);
            if (IsExplicitManualMappingSkill(baseName))
            {
                return false;
            }

            if (IsNamedSkill(baseName, HiddenSkillBaseNames) ||
                ContainsAny(baseName, HiddenSkillNameParts) ||
                ContainsAny(skill.Name, HiddenSkillNameParts))
            {
                return true;
            }

            return HasSkillTag(skill, "passive") ||
                   string.Equals(skill.XmlActivation, "Passive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skill.XmlActivation, "Provoked", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitManualMappingSkill(string baseName)
        {
            return IsNamedSkill(baseName, ActiveSkillBaseNames) ||
                   IsNamedSkill(baseName, StatusSkillBaseNames) ||
                   IsNamedSkill(baseName, TriggerSkillBaseNames) ||
                   IsNamedSkill(baseName, ChainSkillBaseNames) ||
                   IsNamedSkill(baseName, DpSkillBaseNames) ||
                   IsNamedSkill(baseName, ActivatedSkillBaseNames);
        }

        private static bool ContainsAny(string? text, string[] values)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   values.Any(value => text.IndexOf(value, StringComparison.Ordinal) >= 0);
        }

        private static string FormatManualSkillName(SkillSnapshot skill)
        {
            var name = string.IsNullOrWhiteSpace(skill.Name)
                ? skill.DisplayBaseName
                : skill.Name;

            return string.IsNullOrWhiteSpace(name)
                ? "Skill " + skill.SkillId
                : name.Trim();
        }

        private static readonly string[] ManualSkillCategories =
        {
            "主动技能",
            "状态技能",
            "触发技能",
            "连续技",
            "DP技能",
            "激活技能"
        };

        private static readonly string[] ActiveSkillBaseNames =
        {
            "弱化之猛击",
            "挑衅",
            "猛烈一击",
            "突击",
            "盾牌重击",
            "闪光斩",
            "挑衅猛击"
        };

        private static readonly string[] StatusSkillBaseNames =
        {
            "保护之盾",
            "主神之盔甲",
            "捕获"
        };

        private static readonly string[] TriggerSkillBaseNames =
        {
            "盾牌反击",
            "惩戒一击",
            "盾牌猛击"
        };

        private static readonly string[] ChainSkillBaseNames =
        {
            "会心一击",
            "气合",
            "必灭一击",
            "连续乱打"
        };

        private static readonly string[] DpSkillBaseNames =
        {
            "暗黑之惩戒"
        };

        private static readonly string[] ActivatedSkillBaseNames =
        {
            "铜墙铁壁",
            "盾牌防御"
        };

        private static readonly string[] HiddenSkillBaseNames =
        {
            "回程",
            "绷带治疗",
            "药草治疗",
            "精神力恢复",
            "元素防御强化",
            "武器防御率强化",
            "物理攻击力强化",
            "生命力强化",
            "盾牌防御强化",
            "魔法防御强化",
            "物理防御强化",
            "魔法命中强化",
            "物理命中强化",
            "魔法抵抗强化",
            "回避强化",
            "武器精通",
            "盾牌精通"
        };

        private static readonly string[] HiddenSkillNameParts =
        {
            "强化",
            "精通",
            "修炼",
            "穿着",
            "防御力增加",
            "攻击力强化",
            "生命力强化",
            "防御率强化",
            "抵抗强化",
            "命中强化",
            "回避强化",
            "属性防御",
            "上升量增加"
        };

        private void EnableManualSkillMappingRowDrag(FlowLayoutPanel list, Panel row)
        {
            var hasDragStart = false;
            var dragStart = Point.Empty;

            void BeginDrag(object? sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                hasDragStart = true;
                dragStart = e.Location;
            }

            void MoveDrag(object? sender, MouseEventArgs e)
            {
                if (!hasDragStart || e.Button != MouseButtons.Left)
                {
                    return;
                }

                var dragSize = SystemInformation.DragSize;
                var dragBounds = new Rectangle(
                    dragStart.X - dragSize.Width / 2,
                    dragStart.Y - dragSize.Height / 2,
                    dragSize.Width,
                    dragSize.Height);

                if (dragBounds.Contains(e.Location))
                {
                    return;
                }

                hasDragStart = false;
                var data = new DataObject();
                data.SetData(ManualSkillMappingRowDragFormat, row);
                BeginManualSkillRowDragVisual(row);
                row.DoDragDrop(data, DragDropEffects.Move);
                EndManualSkillRowDragVisual(row);
            }

            row.MouseDown += BeginDrag;
            row.MouseMove += MoveDrag;

            foreach (var label in row.Controls.OfType<Label>())
            {
                label.Cursor = Cursors.SizeAll;
                label.MouseDown += BeginDrag;
                label.MouseMove += MoveDrag;
            }
        }

        private void ManualSkillMappingList_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(ManualSkillMappingRowDragFormat) == true
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }

        private void ManualSkillMappingList_DragOver(object? sender, DragEventArgs e)
        {
            if (sender is not FlowLayoutPanel list ||
                e.Data?.GetData(ManualSkillMappingRowDragFormat) is not Control draggedRow ||
                draggedRow.Parent != list)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            var point = list.PointToClient(new Point(e.X, e.Y));
            manualSkillDropLineY = GetManualSkillMappingDropLineY(list, draggedRow, point);
            list.Invalidate();
        }

        private void ManualSkillMappingList_DragDrop(object? sender, DragEventArgs e)
        {
            if (sender is FlowLayoutPanel list &&
                e.Data?.GetData(ManualSkillMappingRowDragFormat) is Control draggedRow &&
                draggedRow.Parent == list)
            {
                var point = list.PointToClient(new Point(e.X, e.Y));
                var currentIndex = list.Controls.GetChildIndex(draggedRow);
                var targetIndex = GetManualSkillMappingDropIndex(list, draggedRow, point, currentIndex);

                if (targetIndex != currentIndex)
                {
                    list.Controls.SetChildIndex(draggedRow, targetIndex);
                }
            }

            ClearManualSkillDropIndicator();
        }

        private void ManualSkillMappingList_DragLeave(object? sender, EventArgs e)
        {
            ClearManualSkillDropIndicator();
        }

        private void ManualSkillMappingList_Paint(object? sender, PaintEventArgs e)
        {
            if (manualSkillDropLineY < 0 || sender is not FlowLayoutPanel list)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var startX = 2;
            var endX = Math.Min(list.ClientSize.Width - 18, 524);
            var y = Math.Clamp(manualSkillDropLineY, 2, Math.Max(2, list.ClientSize.Height - 3));

            using var pen = new Pen(_primaryGreen, 3F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(pen, startX + 10, y, endX, y);

            using var brush = new SolidBrush(_primaryGreen);
            var points = new[]
            {
                new Point(startX + 2, y),
                new Point(startX + 10, y - 5),
                new Point(startX + 10, y + 5)
            };
            e.Graphics.FillPolygon(brush, points);
        }

        private static int GetManualSkillMappingDropIndex(FlowLayoutPanel list, Control draggedRow, Point point, int currentIndex)
        {
            var targetIndex = list.Controls.Count - 1;

            for (var i = 0; i < list.Controls.Count; i++)
            {
                var row = list.Controls[i];
                if (row == draggedRow)
                {
                    continue;
                }

                var rowMiddleY = row.Top + row.Height / 2;
                if (point.Y >= rowMiddleY)
                {
                    continue;
                }

                targetIndex = i;
                if (currentIndex < i)
                {
                    targetIndex--;
                }

                break;
            }

            return Math.Clamp(targetIndex, 0, list.Controls.Count - 1);
        }

        private static int GetManualSkillMappingDropLineY(FlowLayoutPanel list, Control draggedRow, Point point)
        {
            Control? lastRow = null;
            for (var i = 0; i < list.Controls.Count; i++)
            {
                var row = list.Controls[i];
                if (row == draggedRow)
                {
                    continue;
                }

                lastRow = row;
                var rowMiddleY = row.Top + row.Height / 2;
                if (point.Y < rowMiddleY)
                {
                    return row.Top - 4;
                }
            }

            return lastRow is null
                ? 2
                : lastRow.Bottom + 3;
        }

        private void BeginManualSkillRowDragVisual(Control row)
        {
            draggingManualSkillRow = row;
            row.BackColor = _softGreen;

            if (row is Panel panel)
            {
                panel.BorderStyle = BorderStyle.FixedSingle;
            }

            foreach (var label in row.Controls.OfType<Label>())
            {
                label.ForeColor = _darkGreen;
            }

            manualSkillMappingList?.Invalidate();
        }

        private void EndManualSkillRowDragVisual(Control row)
        {
            row.BackColor = _pageBackground;

            if (row is Panel panel)
            {
                panel.BorderStyle = BorderStyle.None;
            }

            foreach (var label in row.Controls.OfType<Label>())
            {
                label.ForeColor = _textGreen;
            }

            draggingManualSkillRow = null;
            ClearManualSkillDropIndicator();
        }

        private void ClearManualSkillDropIndicator()
        {
            manualSkillDropLineY = -1;
            manualSkillMappingList?.Invalidate();
        }

        private string? ShowKeyboardPicker(string? currentKey)
        {
            using var dialog = new Form
            {
                AutoScaleDimensions = new SizeF(7F, 17F),
                AutoScaleMode = AutoScaleMode.Font,
                BackColor = _pageBackground,
                ClientSize = new Size(760, 282),
                Font = new Font("Microsoft YaHei UI", 9F),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Name = "KeyboardPickerForm",
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                Text = "选择技能按键"
            };

            var selectedKey = currentKey;
            var title = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(14, 12),
                Size = new Size(220, 24),
                Text = "选择技能按键",
                TextAlign = ContentAlignment.MiddleLeft
            };
            dialog.Controls.Add(title);

            var current = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(238, 12),
                Size = new Size(220, 24),
                Text = $"当前: {(string.IsNullOrWhiteSpace(currentKey) ? "未选择" : FormatSkillKey(currentKey))}",
                TextAlign = ContentAlignment.MiddleLeft
            };
            dialog.Controls.Add(current);

            AddKeyboardRows(dialog, key =>
            {
                selectedKey = key;
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            });

            var cancel = new RoundedButton
            {
                BackColor = Color.FromArgb(107, 114, 128),
                BorderColor = Color.FromArgb(75, 85, 99),
                CornerRadius = 8,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(664, 242),
                ShadowDepth = 2,
                Size = new Size(78, 30),
                Text = "取消",
                UseVisualStyleBackColor = false
            };
            cancel.Click += (_, _) => dialog.Close();
            dialog.Controls.Add(cancel);

            return dialog.ShowDialog(this) == DialogResult.OK ? selectedKey : null;
        }

        private void AddKeyboardRows(Control parent, Action<string> selectKey)
        {
            var rows = new (string Text, string Value)[][]
            {
                new[]
                {
                    ("1", "D1"),
                    ("2", "D2"),
                    ("3", "D3"),
                    ("4", "D4"),
                    ("5", "D5"),
                    ("6", "D6"),
                    ("7", "D7"),
                    ("8", "D8"),
                    ("9", "D9"),
                    ("0", "D0"),
                    ("-", "OemMinus"),
                    ("=", "OemPlus")
                },
                new[]
                {
                    ("Num1", "NumPad1"),
                    ("Num2", "NumPad2"),
                    ("Num3", "NumPad3"),
                    ("Num4", "NumPad4"),
                    ("Num5", "NumPad5"),
                    ("Num6", "NumPad6"),
                    ("Num7", "NumPad7"),
                    ("Num8", "NumPad8"),
                    ("Num9", "NumPad9"),
                    ("Num0", "NumPad0")
                }
            };

            var y = 48;
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var x = 14;

                foreach (var key in rows[rowIndex])
                {
                    var width = GetKeyboardKeyWidth(key.Text);
                    var button = CreateKeyboardKeyButton(key.Text, x, y, width);
                    button.Click += (_, _) => selectKey(key.Value);
                    parent.Controls.Add(button);
                    x += width + 6;
                }

                y += 32;
            }
        }

        private RoundedButton CreateKeyboardKeyButton(string text, int x, int y, int width)
        {
            return new RoundedButton
            {
                BackColor = _primaryGreen,
                BorderColor = _darkGreen,
                CornerRadius = 7,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                ShadowDepth = 2,
                Size = new Size(width, 28),
                Text = text,
                UseVisualStyleBackColor = false
            };
        }

        private static int GetKeyboardKeyWidth(string key)
        {
            return key switch
            {
                "Backspace" => 78,
                "Tab" => 54,
                "Caps" => 62,
                "Enter" => 70,
                "Shift" => 78,
                "Space" => 176,
                "Ctrl" or "Alt" or "Win" or "Menu" => 54,
                "Num1" or "Num2" or "Num3" or "Num4" or "Num5" or
                    "Num6" or "Num7" or "Num8" or "Num9" or "Num0" => 54,
                _ => 42
            };
        }

        private static string FormatSkillKey(string? key)
        {
            return key switch
            {
                "D1" => "1",
                "D2" => "2",
                "D3" => "3",
                "D4" => "4",
                "D5" => "5",
                "D6" => "6",
                "D7" => "7",
                "D8" => "8",
                "D9" => "9",
                "D0" => "0",
                "OemMinus" => "-",
                "OemPlus" => "=",
                "NumPad1" => "Num1",
                "NumPad2" => "Num2",
                "NumPad3" => "Num3",
                "NumPad4" => "Num4",
                "NumPad5" => "Num5",
                "NumPad6" => "Num6",
                "NumPad7" => "Num7",
                "NumPad8" => "Num8",
                "NumPad9" => "Num9",
                "NumPad0" => "Num0",
                _ => key ?? string.Empty
            };
        }

        private static void RemoveSelectedSkill(TreeView tree)
        {
            tree.SelectedNode?.Remove();
        }

        private static void MoveSelectedSkill(TreeView tree, SkillMove move)
        {
            var node = tree.SelectedNode;
            if (node is null)
            {
                return;
            }

            var collection = node.Parent?.Nodes ?? tree.Nodes;
            var currentIndex = node.Index;
            var targetIndex = move switch
            {
                SkillMove.Top => 0,
                SkillMove.Up => Math.Max(0, currentIndex - 1),
                SkillMove.Down => Math.Min(collection.Count - 1, currentIndex + 1),
                SkillMove.Bottom => collection.Count - 1,
                _ => currentIndex
            };

            if (targetIndex == currentIndex)
            {
                return;
            }

            var moved = (TreeNode)node.Clone();
            moved.Tag = node.Tag;
            collection.RemoveAt(currentIndex);
            collection.Insert(targetIndex, moved);
            tree.SelectedNode = moved;
        }

        private enum SkillMove
        {
            Top,
            Up,
            Down,
            Bottom
        }

        private void GreenTabs_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabControl)
            {
                return;
            }

            var tabPage = tabControl.TabPages[e.Index];
            var selected = e.Index == tabControl.SelectedIndex;
            var bounds = e.Bounds;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var tabBounds = new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width - 5, bounds.Height - 5);
            var shadowBounds = new RectangleF(tabBounds.X + 1, tabBounds.Y + 2, tabBounds.Width, tabBounds.Height);
            var topColor = selected ? ControlPaint.Light(_primaryGreen, 0.16F) : Color.White;
            var bottomColor = selected ? ControlPaint.Dark(_primaryGreen, 0.05F) : _softGreen;

            using var shadowPath = UiChrome.RoundedRect(shadowBounds, 7);
            using var shadowBrush = new SolidBrush(Color.FromArgb(selected ? 58 : 28, 15, 23, 42));
            e.Graphics.FillPath(shadowBrush, shadowPath);

            using var tabPath = UiChrome.RoundedRect(tabBounds, 7);
            using var fill = new LinearGradientBrush(tabBounds, topColor, bottomColor, LinearGradientMode.Vertical);
            using var border = new Pen(selected ? _darkGreen : Color.FromArgb(134, 239, 172));
            using var font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);

            e.Graphics.FillPath(fill, tabPath);
            e.Graphics.DrawPath(border, tabPath);

            TextRenderer.DrawText(
                e.Graphics,
                tabPage.Text,
                font,
                Rectangle.Round(tabBounds),
                selected ? Color.White : _textGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
