using System.Globalization;
using System.Drawing.Drawing2D;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;

namespace Roadhog
{
    public sealed class AccountSettingsForm : Form
    {
        private const string ManualSkillMappingRowDragFormat = "Roadhog.ManualSkillMappingRow";
        private const double CleanupNpcSearchRadiusMeters = 10.0D;

        private readonly string _account;
        private readonly RoadhogRuntime _runtime;
        private readonly IAccountConfigStore _configStore;
        private readonly ISharedPathStore _pathStore;
        private readonly IScriptProfileStore _profileStore;
        private readonly Dictionary<SharedPathKind, PathEditorControls> pathEditors = new();
        private readonly Dictionary<SharedPathKind, Label> pathOverviewLabels = new();
        private readonly System.Windows.Forms.Timer pathRecordTimer = new() { Interval = 250 };
        private IReadOnlyList<SharedPathSummary> currentPathSummaries = Array.Empty<SharedPathSummary>();
        private IReadOnlyList<ScriptProfileSummary> currentProfileSummaries = Array.Empty<ScriptProfileSummary>();
        private SharedPathKind? recordingPathKind;
        private bool loadingPathCombos;
        private bool loadingProfileCombo;
        private bool pathRecordReadInFlight;
        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Color _pageBackground = Color.FromArgb(247, 252, 249);
        private readonly Color _inputBackground = Color.FromArgb(229, 245, 235);
        private readonly Color _textGreen = Color.FromArgb(20, 83, 45);

        private TabControl settingsTabs = null!;
        private Form? spiritmasterSettingsDialog;
        private Label? currentProfileLabel;
        private Label? profileStatusLabel;
        private RoundedTextBox? profileNameTextBox;
        private RoundedComboBox? savedProfileCombo;
        private RoundedComboBox? mainModeCombo;
        private Label? combatModeLabel;
        private RoundedComboBox? combatModeCombo;
        private Label? stationaryCombatRadiusLabel;
        private RoundedTextBox? stationaryCombatRadiusTextBox;
        private Label? stationaryCombatRadiusUnitLabel;
        private Label? pathCombatRadiusLabel;
        private RoundedTextBox? pathCombatRadiusTextBox;
        private Label? pathCombatRadiusUnitLabel;
        private Label? pathFollowReachDistanceLabel;
        private RoundedTextBox? pathFollowReachDistanceTextBox;
        private Label? pathFollowReachDistanceUnitLabel;
        private RoundedTextBox? cameraYawPixelsPerDegreeTextBox;
        private RoundedTextBox? cameraPitchPixelsPerDegreeTextBox;
        private RoundedCheckBox? enableLootCheckBox;
        private RoundedCheckBox? contestMonsterCheckBox;
        private RoundedCheckBox? counterEnemyRaceCheckBox;
        private RoundedCheckBox? preferAggressiveMonsterCheckBox;
        private RoundedComboBox? activeMonsterFilterCombo;
        private ListBox? activeMonsterFilterListBox;
        private Label? activeMonsterFilterStatusLabel;
        private RoundedCheckBox? openingAttackKeyCheckBox;
        private RoundedCheckBox? spiritmasterAutoSkillCheckBox;
        private Button? spiritmasterSettingsButton;
        private RoundedCheckBox? openingSkillEnabledCheckBox;
        private RoundedComboBox? openingSkillCombo;
        private Button? openingSkillKeyButton;
        private RoundedTextBox? revivePathNameTextBox;
        private RoundedTextBox? combatPathNameTextBox;
        private RoundedTextBox? maintenancePathNameTextBox;
        private RoundedTextBox? deathReviveClickPointTextBox;
        private Button? deathReviveTestMoveButton;
        private RoundedCheckBox? loopPathCheckBox;
        private RoundedCheckBox? reverseAtEndCheckBox;
        private RoundedCheckBox? deathStopPathCheckBox;
        private RoundedCheckBox? sitMaintenanceCheckBox;
        private RoundedTextBox? sitMpBelowTextBox;
        private RoundedTextBox? sitMpRecoverToTextBox;
        private RoundedTextBox? sitHpBelowTextBox;
        private RoundedTextBox? sitHpRecoverToTextBox;
        private FlowLayoutPanel? hpMaintenanceRuleList;
        private FlowLayoutPanel? mpMaintenanceRuleList;
        private FlowLayoutPanel? statusMaintenanceRuleList;
        private Label? hpMaintenanceEmptyLabel;
        private Label? mpMaintenanceEmptyLabel;
        private Label? statusMaintenanceEmptyLabel;
        private RoundedTextBox? bagCleanupThresholdTextBox;
        private RadioButton? skillAutoModeRadio;
        private RadioButton? skillManualModeRadio;
        private RadioButton? skillSystemModeRadio;
        private Panel? autoSkillPanel;
        private Panel? manualSkillPanel;
        private Panel? systemSkillPanel;
        private TreeView? availableSkillTree;
        private TreeView? selectedSkillTree;
        private TreeView? systemSkillTree;
        private TreeView? systemSelectedSkillTree;
        private FlowLayoutPanel? manualSkillMappingList;
        private FlowLayoutPanel? spiritmasterDotRuleList;
        private FlowLayoutPanel? spiritmasterSummonRuleList;
        private Button? spiritmasterOpeningAttackKeyButton;
        private FlowLayoutPanel? spiritmasterPetHpRuleList;
        private FlowLayoutPanel? spiritmasterPetBuffRuleList;
        private Control? draggingManualSkillRow;
        private IReadOnlyList<SkillSnapshot> currentManualSkills = Array.Empty<SkillSnapshot>();
        private readonly List<FlowLayoutPanel> spiritmasterRuleLists = new();
        private SpiritmasterSkillSettings currentSpiritmasterSettings = new();
        private int manualSkillDropLineY = -1;

        public AccountSettingsForm(
            string account,
            RoadhogRuntime runtime,
            IAccountConfigStore configStore,
            ISharedPathStore pathStore,
            IScriptProfileStore profileStore)
        {
            _account = account;
            _runtime = runtime;
            _configStore = configStore;
            _pathStore = pathStore;
            _profileStore = profileStore;
            pathRecordTimer.Tick += PathRecordTimer_Tick;
            InitializeSettingsForm();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            pathRecordTimer.Stop();
            pathRecordTimer.Dispose();
            base.OnFormClosed(e);
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
            settingsTabs.TabPages.Add(CreateFilterTab());
            settingsTabs.TabPages.Add(CreateBagCleanupTab());

            Controls.Add(settingsTabs);
            AddButton(this, "保存配置", 500, 3, 150, 30, SaveSettingsButton_Click).BringToFront();
            LoadSavedSettings();
        }

        private void LoadSavedSettings()
        {
            var account = LoadAccountConfigOrDefault();
            RefreshProfileLibrary();
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

        private ScriptSettings BuildEffectiveScriptSettings(AccountConfig account)
        {
            var profileName = account.ScriptSettings?.ProfileName;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                profileName = account.ProfileName;
            }

            if (!string.IsNullOrWhiteSpace(profileName))
            {
                var profileResult = _profileStore.LoadAsync(profileName).GetAwaiter().GetResult();
                if (profileResult.Success && profileResult.Value is not null)
                {
                    return profileResult.Value.Settings.Clone();
                }
            }

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
            UpdateCurrentProfileDisplay(settings.ProfileName);
            SelectProfileComboItem(settings.ProfileName, loadProfile: false);
            SetComboText(mainModeCombo, FormatMainMode(settings.MainMode));
            SetComboText(combatModeCombo, FormatCombatMode(settings.CombatMode));
            SetStationaryCombatRadius(settings.Combat);
            SetPathCombatRadius(settings.Combat);
            SetPathFollowReachDistance(settings.Combat);
            SetCameraTurnScales(settings.Combat);
            RefreshCombatModeVisibility();

            SetChecked(enableLootCheckBox, settings.Combat.EnableLoot);
            SetChecked(contestMonsterCheckBox, settings.Combat.ContestMonster);
            SetChecked(counterEnemyRaceCheckBox, settings.Combat.CounterEnemyRace);
            SetChecked(preferAggressiveMonsterCheckBox, settings.Combat.PreferAggressiveMonsters);
            PopulateActiveMonsterFilterList(settings.Combat.ActiveMonsterNameFilters);

            var paths = settings.Paths ?? new PathScriptSettings();
            SetText(revivePathNameTextBox, paths.RevivePathName);
            SetText(combatPathNameTextBox, paths.CombatPathName);
            SetText(maintenancePathNameTextBox, paths.MaintenancePathName);
            SetText(deathReviveClickPointTextBox, FormatScreenPoint(paths.DeathReviveClickX, paths.DeathReviveClickY));
            SetChecked(loopPathCheckBox, paths.LoopPath);
            SetChecked(reverseAtEndCheckBox, paths.ReverseAtEnd);
            SetChecked(deathStopPathCheckBox, paths.DeathStopPath);
            RefreshPathLibrary();
            SelectConfiguredPath(SharedPathKind.Revive, paths.RevivePathName);
            SelectConfiguredPath(SharedPathKind.Combat, paths.CombatPathName);
            SelectConfiguredPath(SharedPathKind.Maintenance, paths.MaintenancePathName);

            SetChecked(sitMaintenanceCheckBox, settings.Maintenance.SitMaintenanceEnabled);
            SetText(sitMpBelowTextBox, settings.Maintenance.SitMpBelowPercent.ToString());
            SetText(sitMpRecoverToTextBox, settings.Maintenance.SitMpRecoverToPercent.ToString());
            SetText(sitHpBelowTextBox, settings.Maintenance.SitHpBelowPercent.ToString());
            SetText(sitHpRecoverToTextBox, settings.Maintenance.SitHpRecoverToPercent.ToString());
            PopulateMaintenanceKeyRules(hpMaintenanceRuleList, hpMaintenanceEmptyLabel, settings.Maintenance.HpMaintenanceRules);
            PopulateMaintenanceKeyRules(mpMaintenanceRuleList, mpMaintenanceEmptyLabel, settings.Maintenance.MpMaintenanceRules);
            PopulateStatusMaintenanceRules(statusMaintenanceRuleList, statusMaintenanceEmptyLabel, settings.Maintenance.StatusMaintenanceRules);
            SetText(bagCleanupThresholdTextBox, settings.Maintenance.BagCleanupThreshold.ToString());
            SetChecked(openingAttackKeyCheckBox, settings.SemiAuto.AttackKeyLoopEnabled);
            SetChecked(spiritmasterAutoSkillCheckBox, settings.Skills.SpiritmasterAutoSkillLogicEnabled);
            ApplyOpeningSkillSettings(settings.Skills.OpeningSkill);
            currentSpiritmasterSettings = (settings.Skills.Spiritmaster ?? new SpiritmasterSkillSettings()).Clone();
            PopulateSpiritmasterRuleLists(currentSpiritmasterSettings);

            var manualSkillMode = settings.Skills.Mode == SkillConfigurationMode.ManualMapping;
            var systemSkillMode = settings.Skills.Mode == SkillConfigurationMode.SystemClassification;
            if (skillAutoModeRadio is not null)
            {
                skillAutoModeRadio.Checked = !manualSkillMode && !systemSkillMode;
            }

            if (skillManualModeRadio is not null)
            {
                skillManualModeRadio.Checked = manualSkillMode;
            }

            if (skillSystemModeRadio is not null)
            {
                skillSystemModeRadio.Checked = systemSkillMode;
            }

            ShowSkillMode(settings.Skills.Mode);

            if (selectedSkillTree is not null)
            {
                PopulateSelectedSkillTreeFromConfig(selectedSkillTree, settings.Skills.ExecutionTree);
            }

            if (systemSelectedSkillTree is not null)
            {
                PopulateSelectedSkillTreeFromConfig(systemSelectedSkillTree, settings.Skills.SystemExecutionTree);
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
            capturedSettings.Maintenance.AutoEquip = previousSettings.Maintenance.AutoEquip;
            capturedSettings.Maintenance.AutoDecompose = previousSettings.Maintenance.AutoDecompose;
            capturedSettings.Maintenance.BagTotalSlots = previousSettings.Maintenance.BagTotalSlots;
            capturedSettings.SemiAuto = previousSettings.SemiAuto.Clone();
            capturedSettings.SemiAuto.AttackKeyLoopEnabled =
                openingAttackKeyCheckBox?.Checked ?? capturedSettings.SemiAuto.AttackKeyLoopEnabled;
            if (!SaveSelectedCleanupNpcBinding(out var cleanupBindingError))
            {
                error = cleanupBindingError;
                return false;
            }

            capturedSettings.Skills.KeyOrder = previousSettings.Skills.KeyOrder.Count == 0
                ? SkillScriptSettings.DefaultKeyOrder()
                : previousSettings.Skills.KeyOrder.ToList();
            capturedSettings.Skills.TriggerPrefixMode = string.IsNullOrWhiteSpace(previousSettings.Skills.TriggerPrefixMode)
                ? "TopContiguousTriggerSkills"
                : previousSettings.Skills.TriggerPrefixMode;

            var profileName = string.IsNullOrWhiteSpace(capturedSettings.ProfileName)
                ? "default_profile"
                : capturedSettings.ProfileName.Trim();
            capturedSettings.ProfileName = profileName;
            var profileResult = _profileStore.SaveAsync(new ScriptProfileDocument
            {
                Name = profileName,
                Settings = capturedSettings.Clone()
            }).GetAwaiter().GetResult();
            if (!profileResult.Success)
            {
                error = profileResult.Error ?? "保存方案失败。";
                return false;
            }

            account.AccountName = _account;
            account.ScriptSettings = capturedSettings;
            ApplyScriptSettingsToLegacyFields(account, account.ScriptSettings);

            var result = _configStore.UpsertAsync(account).GetAwaiter().GetResult();
            error = result.Error ?? "保存账号配置失败。";
            if (!result.Success)
            {
                return false;
            }

            RefreshProfileLibrary();
            SelectProfileComboItem(profileName, loadProfile: false);
            UpdateCurrentProfileDisplay(profileName);
            SetProfileStatus("已保存方案: " + profileName, false);
            return true;
        }

        private bool SaveSelectedCleanupNpcBinding(out string error)
        {
            error = string.Empty;
            if (!pathEditors.TryGetValue(SharedPathKind.Maintenance, out var editor))
            {
                return true;
            }

            var pathName = GetText(maintenancePathNameTextBox, string.Empty);
            if (string.IsNullOrWhiteSpace(pathName))
            {
                pathName = GetSelectedPathName(editor);
            }

            if (string.IsNullOrWhiteSpace(pathName))
            {
                return true;
            }

            var npcName = GetSelectedCleanupNpcName(editor);
            var load = _pathStore.LoadAsync(pathName).GetAwaiter().GetResult();
            if (!load.Success || load.Value is null)
            {
                if (string.IsNullOrWhiteSpace(npcName))
                {
                    return true;
                }

                error = "清包路径未保存，无法绑定NPC: " + pathName;
                return false;
            }

            var document = load.Value;
            document.CleanupNpcName = npcName;
            var save = _pathStore.SaveAsync(document).GetAwaiter().GetResult();
            if (save.Success)
            {
                return true;
            }

            error = save.Error ?? "保存清包路径NPC绑定失败。";
            return false;
        }

        private ScriptSettings CaptureScriptSettings()
        {
            var deathReviveClickPoint = ReadScreenPoint(
                deathReviveClickPointTextBox,
                PathScriptSettings.DefaultDeathReviveClickX,
                PathScriptSettings.DefaultDeathReviveClickY);

            var settings = new ScriptSettings
            {
                ProfileName = GetText(profileNameTextBox, "default_profile"),
                MainMode = ParseMainMode(mainModeCombo?.Text),
                CombatMode = ParseCombatMode(combatModeCombo?.Text),
                Combat = new CombatScriptSettings
                {
                    EnableLoot = enableLootCheckBox?.Checked ?? true,
                    ContestMonster = contestMonsterCheckBox?.Checked ?? false,
                    CounterEnemyRace = counterEnemyRaceCheckBox?.Checked ?? false,
                    PreferAggressiveMonsters = preferAggressiveMonsterCheckBox?.Checked ?? false,
                    ActiveMonsterNameFilters = CaptureActiveMonsterFilterList(),
                    HasStationaryCombatPosition = false,
                    StationaryCombatX = 0.0D,
                    StationaryCombatY = 0.0D,
                    StationaryCombatZ = 0.0D,
                    StationaryCombatRadius = ReadDouble(stationaryCombatRadiusTextBox, 30.0D, 1.0D, 500.0D),
                    PathCombatRadius = ReadDouble(pathCombatRadiusTextBox, 30.0D, 1.0D, 500.0D),
                    PathFollowReachDistance = ReadDouble(pathFollowReachDistanceTextBox, 5.0D, 0.5D, 50.0D),
                    CameraYawPixelsPerDegree = ReadDouble(cameraYawPixelsPerDegreeTextBox, 11.0D, 0.1D, 100.0D),
                    CameraPitchPixelsPerDegree = ReadDouble(cameraPitchPixelsPerDegreeTextBox, 13.0D, 0.1D, 100.0D)
                },
                Paths = new PathScriptSettings
                {
                    RevivePathName = GetText(revivePathNameTextBox, string.Empty),
                    CombatPathName = GetText(combatPathNameTextBox, string.Empty),
                    MaintenancePathName = GetText(maintenancePathNameTextBox, string.Empty),
                    DeathReviveClickX = deathReviveClickPoint.X,
                    DeathReviveClickY = deathReviveClickPoint.Y,
                    LoopPath = loopPathCheckBox?.Checked ?? true,
                    ReverseAtEnd = reverseAtEndCheckBox?.Checked ?? false,
                    DeathStopPath = deathStopPathCheckBox?.Checked ?? true
                },
                Maintenance = new MaintenanceScriptSettings
                {
                    SitMaintenanceEnabled = sitMaintenanceCheckBox?.Checked ?? true,
                    SitMpBelowPercent = ReadPercent(sitMpBelowTextBox, 10),
                    SitMpRecoverToPercent = ReadPercent(sitMpRecoverToTextBox, 90),
                    SitHpBelowPercent = ReadPercent(sitHpBelowTextBox, 25),
                    SitHpRecoverToPercent = ReadPercent(sitHpRecoverToTextBox, 75),
                    HpMaintenanceRules = CaptureMaintenanceKeyRules(hpMaintenanceRuleList),
                    MpMaintenanceRules = CaptureMaintenanceKeyRules(mpMaintenanceRuleList),
                    StatusMaintenanceRules = CaptureStatusMaintenanceRules(statusMaintenanceRuleList),
                    BagCleanupThreshold = ReadInt(bagCleanupThresholdTextBox, 85)
                },
                Skills = new SkillScriptSettings
                {
                    Mode = CaptureSkillConfigurationMode(),
                    OpeningSkill = CaptureOpeningSkill(),
                    SpiritmasterAutoSkillLogicEnabled = spiritmasterAutoSkillCheckBox?.Checked ?? false,
                    Spiritmaster = CaptureSpiritmasterSettings(),
                    TriggerPrefixMode = "TopContiguousTriggerSkills",
                    ExecutionTree = selectedSkillTree is null
                        ? new List<SkillConfigNode>()
                        : CaptureSkillTree(selectedSkillTree.Nodes),
                    ManualMappings = CaptureManualSkillMappings(),
                    SystemExecutionTree = systemSelectedSkillTree is null
                        ? new List<SkillConfigNode>()
                        : CaptureSkillTree(systemSelectedSkillTree.Nodes)
                }
            };

            return settings;
        }

        private SkillConfigurationMode CaptureSkillConfigurationMode()
        {
            if (skillSystemModeRadio?.Checked == true)
            {
                return SkillConfigurationMode.SystemClassification;
            }

            return skillManualModeRadio?.Checked == true
                ? SkillConfigurationMode.ManualMapping
                : SkillConfigurationMode.Auto;
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

        private static int ReadPercent(RoundedTextBox? textBox, int fallback)
        {
            return Math.Clamp(ReadInt(textBox, fallback), 0, 100);
        }

        private static double ReadDouble(RoundedTextBox? textBox, double fallback, double minimum, double maximum)
        {
            if (!double.TryParse(textBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                !double.TryParse(textBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                value = fallback;
            }

            return Math.Clamp(value, minimum, maximum);
        }

        private static string FormatScreenPoint(int x, int y)
        {
            return Math.Clamp(x, 0, 32767).ToString(CultureInfo.InvariantCulture) +
                   "," +
                   Math.Clamp(y, 0, 32767).ToString(CultureInfo.InvariantCulture);
        }

        private static (int X, int Y) ReadScreenPoint(RoundedTextBox? textBox, int fallbackX, int fallbackY)
        {
            var text = textBox?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return (fallbackX, fallbackY);
            }

            var parts = text.Split(
                new[] { ',', '，', ';', '；', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                !TryReadScreenPointPart(parts[0], out var x) ||
                !TryReadScreenPointPart(parts[1], out var y))
            {
                return (fallbackX, fallbackY);
            }

            return (Math.Clamp(x, 0, 32767), Math.Clamp(y, 0, 32767));
        }

        private static bool TryReadScreenPointPart(string text, out int value)
        {
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                   int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
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

        private async void TestDeathReviveMoveButton_Click(object? sender, EventArgs e)
        {
            var button = sender as Button ?? deathReviveTestMoveButton;
            var point = ReadScreenPoint(
                deathReviveClickPointTextBox,
                PathScriptSettings.DefaultDeathReviveClickX,
                PathScriptSettings.DefaultDeathReviveClickY);
            SetText(deathReviveClickPointTextBox, FormatScreenPoint(point.X, point.Y));

            if (button is not null)
            {
                button.Enabled = false;
                button.Text = "移动中";
            }

            try
            {
                var result = await _runtime
                    .TestMoveMouseToScreenPointAsync(point.X, point.Y)
                    .ConfigureAwait(true);
                if (!result.Success)
                {
                    MessageBox.Show(
                        this,
                        result.Error ?? "测试移动失败。",
                        "测试移动",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (button is not null)
                {
                    button.Text = "已移动";
                    await Task.Delay(700).ConfigureAwait(true);
                }
            }
            finally
            {
                if (button is not null)
                {
                    button.Text = "测试移动";
                    button.Enabled = true;
                }
            }
        }

        private TabPage CreateSummaryTab()
        {
            var tab = CreateBaseTab("总览");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "方案", 4, 8, 80, 22);
            currentProfileLabel = AddLabel(page, "当前方案: default_profile", 84, 8, 220, 22, _textGreen, FontStyle.Bold);
            AddLabel(page, "已保存方案", 306, 8, 100, 22);
            profileStatusLabel = AddLabel(page, string.Empty, 648, 36, 190, 22);
            profileNameTextBox = AddTextBox(page, "default_profile", 4, 32, 220, 26);
            savedProfileCombo = AddCombo(page, 306, 32, 254, 28);
            savedProfileCombo.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
            AddButton(page, "删除", 568, 31, 72, 30, (_, _) => DeleteSavedProfile());
            AddLabel(page, "方案名", 230, 36, 80, 22);
            AddLabel(page, "水平", 306, 76, 38, 22);
            cameraYawPixelsPerDegreeTextBox = AddTextBox(page, "11.0", 346, 72, 70, 28);
            AddLabel(page, "俯仰", 482, 76, 38, 22);
            cameraPitchPixelsPerDegreeTextBox = AddTextBox(page, "13.0", 522, 72, 70, 28);

            mainModeCombo = AddCombo(page, 4, 72, 220, 28, "自定义打怪", "采集", "制作", "半自动");
            mainModeCombo.SelectedIndexChanged += (_, _) => RefreshCombatModeVisibility();
            AddLabel(page, "主模式", 230, 76, 80, 22, Color.FromArgb(220, 38, 38), FontStyle.Bold);

            combatModeCombo = AddCombo(page, 4, 104, 220, 28, "原地打怪", "路径打怪");
            combatModeCombo.SelectedIndexChanged += (_, _) => RefreshCombatModeVisibility();
            combatModeLabel = AddLabel(page, "打怪模式", 230, 108, 80, 22);
            stationaryCombatRadiusLabel = AddLabel(page, "半径", 306, 108, 38, 22);
            stationaryCombatRadiusTextBox = AddTextBox(page, "30.0", 346, 104, 70, 28);
            stationaryCombatRadiusUnitLabel = AddLabel(page, "m", 422, 108, 20, 22);
            pathCombatRadiusLabel = AddLabel(page, "半径", 306, 108, 38, 22);
            pathCombatRadiusTextBox = AddTextBox(page, "30.0", 346, 104, 70, 28);
            pathCombatRadiusUnitLabel = AddLabel(page, "m", 422, 108, 20, 22);
            pathFollowReachDistanceLabel = AddLabel(page, "精度", 482, 108, 38, 22);
            pathFollowReachDistanceTextBox = AddTextBox(page, "5.0", 522, 104, 70, 28);
            pathFollowReachDistanceUnitLabel = AddLabel(page, "m", 598, 108, 20, 22);
            RefreshCombatModeVisibility();

            enableLootCheckBox = AddCheckBox(page, "启用拾取", 4, 142, 88, true);
            contestMonsterCheckBox = AddCheckBox(page, "抢怪", 96, 142, 64, false);
            counterEnemyRaceCheckBox = AddCheckBox(page, "反击敌对种族", 160, 142, 140, false);
            preferAggressiveMonsterCheckBox = AddCheckBox(page, "优先攻击主动怪", 302, 142, 142, false);

            return tab;
        }

        private void RefreshCombatModeVisibility()
        {
            var visible = ParseMainMode(mainModeCombo?.Text) == AccountMainMode.CustomCombat;
            var combatMode = ParseCombatMode(combatModeCombo?.Text);
            var stationaryVisible = visible && combatMode == AccountCombatMode.Stationary;
            var pathVisible = visible && combatMode == AccountCombatMode.Path;
            if (combatModeCombo is not null)
            {
                combatModeCombo.Visible = visible;
            }

            if (combatModeLabel is not null)
            {
                combatModeLabel.Visible = visible;
            }

            if (stationaryCombatRadiusLabel is not null)
            {
                stationaryCombatRadiusLabel.Visible = stationaryVisible;
            }

            if (stationaryCombatRadiusTextBox is not null)
            {
                stationaryCombatRadiusTextBox.Visible = stationaryVisible;
            }

            if (stationaryCombatRadiusUnitLabel is not null)
            {
                stationaryCombatRadiusUnitLabel.Visible = stationaryVisible;
            }

            if (pathCombatRadiusLabel is not null)
            {
                pathCombatRadiusLabel.Visible = pathVisible;
            }

            if (pathCombatRadiusTextBox is not null)
            {
                pathCombatRadiusTextBox.Visible = pathVisible;
            }

            if (pathCombatRadiusUnitLabel is not null)
            {
                pathCombatRadiusUnitLabel.Visible = pathVisible;
            }

            if (pathFollowReachDistanceLabel is not null)
            {
                pathFollowReachDistanceLabel.Visible = pathVisible;
            }

            if (pathFollowReachDistanceTextBox is not null)
            {
                pathFollowReachDistanceTextBox.Visible = pathVisible;
            }

            if (pathFollowReachDistanceUnitLabel is not null)
            {
                pathFollowReachDistanceUnitLabel.Visible = pathVisible;
            }
        }

        private void RefreshProfileLibrary()
        {
            var result = _profileStore.LoadSummariesAsync().GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                currentProfileSummaries = Array.Empty<ScriptProfileSummary>();
                SetProfileStatus(result.Error ?? "读取方案失败。", true);
                return;
            }

            currentProfileSummaries = result.Value;
            RefreshSavedProfileCombo();
        }

        private void RefreshSavedProfileCombo()
        {
            if (savedProfileCombo is null)
            {
                return;
            }

            var selectedName = GetSelectedProfileName();
            if (string.IsNullOrWhiteSpace(selectedName))
            {
                selectedName = profileNameTextBox?.Text;
            }

            loadingProfileCombo = true;
            try
            {
                savedProfileCombo.Items.Clear();
                foreach (var summary in currentProfileSummaries)
                {
                    savedProfileCombo.Items.Add(new ProfileComboItem(summary));
                }

                SelectProfileComboItem(selectedName, loadProfile: false);
            }
            finally
            {
                loadingProfileCombo = false;
            }
        }

        private bool SelectProfileComboItem(string? profileName, bool loadProfile)
        {
            if (savedProfileCombo is null || string.IsNullOrWhiteSpace(profileName))
            {
                return false;
            }

            for (var i = 0; i < savedProfileCombo.Items.Count; i++)
            {
                if (savedProfileCombo.Items[i] is ProfileComboItem item &&
                    string.Equals(item.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var wasLoading = loadingProfileCombo;
                    loadingProfileCombo = true;
                    try
                    {
                        savedProfileCombo.SelectedIndex = i;
                    }
                    finally
                    {
                        loadingProfileCombo = wasLoading;
                    }

                    if (loadProfile)
                    {
                        LoadProfileByName(item.Name);
                    }

                    return true;
                }
            }

            return false;
        }

        private void LoadSelectedProfile()
        {
            if (loadingProfileCombo)
            {
                return;
            }

            var name = GetSelectedProfileName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                LoadProfileByName(name);
            }
        }

        private void LoadProfileByName(string name)
        {
            var result = _profileStore.LoadAsync(name).GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                SetProfileStatus(result.Error ?? "读取方案失败。", true);
                return;
            }

            ApplyScriptSettings(result.Value.Settings);
            SetProfileStatus("已加载方案: " + result.Value.Name, false);
        }

        private async void DeleteSavedProfile()
        {
            var name = GetSelectedProfileName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = profileNameTextBox?.Text;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                SetProfileStatus("未选择方案。", true);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                "删除已保存方案: " + name + "?",
                "删除方案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var result = await _profileStore.DeleteAsync(name).ConfigureAwait(true);
            if (!result.Success)
            {
                SetProfileStatus(result.Error ?? "删除方案失败。", true);
                return;
            }

            RefreshProfileLibrary();
            SetProfileStatus("已删除方案: " + name, false);
        }

        private string GetSelectedProfileName()
        {
            if (savedProfileCombo is null)
            {
                return string.Empty;
            }

            var selectedIndex = savedProfileCombo.SelectedIndex;
            if (selectedIndex >= 0 &&
                selectedIndex < savedProfileCombo.Items.Count &&
                savedProfileCombo.Items[selectedIndex] is ProfileComboItem item)
            {
                return item.Name;
            }

            return savedProfileCombo.Text;
        }

        private void UpdateCurrentProfileDisplay(string? profileName)
        {
            var name = string.IsNullOrWhiteSpace(profileName) ? "default_profile" : profileName.Trim();
            if (currentProfileLabel is not null)
            {
                currentProfileLabel.Text = "当前方案: " + name;
            }
        }

        private void SetProfileStatus(string text, bool isError)
        {
            if (profileStatusLabel is null)
            {
                return;
            }

            profileStatusLabel.Text = text;
            profileStatusLabel.ForeColor = isError ? Color.FromArgb(166, 40, 40) : _textGreen;
        }

        private void SetStationaryCombatRadius(CombatScriptSettings combat)
        {
            var radius = combat.StationaryCombatRadius <= 0.0D
                ? 30.0D
                : Math.Min(combat.StationaryCombatRadius, 500.0D);
            SetText(stationaryCombatRadiusTextBox, radius.ToString("F1", CultureInfo.InvariantCulture));
        }

        private void SetPathCombatRadius(CombatScriptSettings combat)
        {
            var radius = combat.PathCombatRadius <= 0.0D
                ? 30.0D
                : Math.Min(combat.PathCombatRadius, 500.0D);
            SetText(pathCombatRadiusTextBox, radius.ToString("F1", CultureInfo.InvariantCulture));
        }

        private void SetPathFollowReachDistance(CombatScriptSettings combat)
        {
            var reachDistance = combat.PathFollowReachDistance <= 0.0D
                ? 5.0D
                : Math.Clamp(combat.PathFollowReachDistance, 0.5D, 50.0D);
            SetText(pathFollowReachDistanceTextBox, reachDistance.ToString("F1", CultureInfo.InvariantCulture));
        }

        private void SetCameraTurnScales(CombatScriptSettings combat)
        {
            var yaw = combat.CameraYawPixelsPerDegree <= 0.0D
                ? 11.0D
                : Math.Clamp(combat.CameraYawPixelsPerDegree, 0.1D, 100.0D);
            var pitch = combat.CameraPitchPixelsPerDegree <= 0.0D
                ? 13.0D
                : Math.Clamp(combat.CameraPitchPixelsPerDegree, 0.1D, 100.0D);
            SetText(cameraYawPixelsPerDegreeTextBox, yaw.ToString("0.###", CultureInfo.InvariantCulture));
            SetText(cameraPitchPixelsPerDegreeTextBox, pitch.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private TabPage CreatePathTab()
        {
            var tab = CreateBaseTab("路径");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "挂机路径选择:", 4, 8, 130, 22, _textGreen, FontStyle.Bold);
            pathOverviewLabels[SharedPathKind.Revive] = AddLabel(page, "复活路径:  未选（0点）", 24, 34, 320, 22);
            pathOverviewLabels[SharedPathKind.Combat] = AddLabel(page, "打怪路径:  未选（0点）", 24, 60, 260, 22);
            pathOverviewLabels[SharedPathKind.Maintenance] = AddLabel(page, "清包路径:  未选（0点）", 24, 86, 260, 22);
            AddLabel(page, "死亡复活坐标:", 310, 86, 104, 22, _textGreen, FontStyle.Bold);
            deathReviveClickPointTextBox = AddTextBox(
                page,
                FormatScreenPoint(PathScriptSettings.DefaultDeathReviveClickX, PathScriptSettings.DefaultDeathReviveClickY),
                420,
                82,
                150,
                28);
            deathReviveTestMoveButton = AddButton(page, "测试移动", 586, 82, 96, 28, TestDeathReviveMoveButton_Click);

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
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Revive, "复活路径", "死亡复活后返回主路径", true));
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Combat, "打怪路径", "打怪巡逻路径", false));
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Maintenance, "清包路径", "清包路径", false));
            page.Controls.Add(pathTabs);

            return tab;
        }

        private TabPage CreatePathEditorTab(SharedPathKind kind, string title, string caption, bool includeSamplePoint)
        {
            var tab = new TabPage
            {
                BackColor = _pageBackground,
                Padding = Padding.Empty,
                Text = title
            };

            var page = CreatePagePanel();
            tab.Controls.Add(page);

            var editor = new PathEditorControls(kind);
            pathEditors[kind] = editor;

            AddLabel(page, caption, 4, 8, 220, 22, _textGreen, FontStyle.Bold);
            var pathNameTextBox = AddTextBox(page, includeSamplePoint ? "穆尔海姆00133" : string.Empty, 4, 38, 242, 28);
            editor.PathNameTextBox = pathNameTextBox;
            if (kind == SharedPathKind.Revive)
            {
                revivePathNameTextBox = pathNameTextBox;
            }
            else if (kind == SharedPathKind.Combat)
            {
                combatPathNameTextBox = pathNameTextBox;
            }
            else if (kind == SharedPathKind.Maintenance)
            {
                maintenancePathNameTextBox = pathNameTextBox;
            }

            AddLabel(page, "路径名", 252, 42, 54, 22);
            editor.SavedPathCombo = AddCombo(page, 306, 38, 254, 28);
            editor.SavedPathCombo.SelectedIndexChanged += (_, _) => LoadSelectedPath(editor);
            AddLabel(page, "已保存路径", 566, 42, 120, 22);

            AddButton(page, "保存到列表", 6, 74, 100, 30, (_, _) => SavePath(editor));
            AddButton(page, "删除保存", 114, 74, 92, 30, (_, _) => DeleteSavedPath(editor));
            if (kind == SharedPathKind.Maintenance)
            {
                AddLabel(page, "清包NPC", 222, 78, 72, 24, _textGreen, FontStyle.Bold);
                editor.CleanupNpcRefreshButton = AddButton(page, "刷新附近NPC", 300, 74, 104, 30);
                editor.CleanupNpcRefreshButton.Click += async (_, _) =>
                    await RefreshCleanupNpcsAsync(editor).ConfigureAwait(true);
                editor.CleanupNpcCombo = AddCombo(page, 414, 74, 238, 28);
            }

            editor.SummaryLabel = AddLabel(page, "点数  0  |  总距  0.0  |  跳过  0", 6, 112, 300, 24, _textGreen, FontStyle.Bold);
            editor.StatusLabel = AddLabel(page, "等待读取坐标", 316, 112, 420, 24);

            editor.ManualButton = AddButton(page, "手动录点", 6, 144, 92, 30, (_, _) => AddManualPathPoint(editor));
            editor.StartButton = AddButton(page, "开始录制", 106, 144, 92, 30, (_, _) => StartPathRecording(editor));
            editor.StopButton = AddButton(page, "停止录制", 206, 144, 92, 30, (_, _) => StopPathRecording(editor));
            AddButton(page, "删除末点", 306, 144, 82, 30, (_, _) => RemoveLastPathPoint(editor));
            AddButton(page, "清空", 396, 144, 62, 30, (_, _) => ClearPathPoints(editor));
            AddButton(page, "复制路径", 466, 144, 88, 30, (_, _) => CopyPath(editor));
            editor.ExecutePathButton = AddButton(page, "执行路径", 564, 144, 92, 30);
            editor.ExecutePathButton.Click += async (_, _) => await ExecutePathAsync(editor).ConfigureAwait(true);

            var pointsBox = new RoundedTextBox
            {
                BackColor = _inputBackground,
                BorderColor = Color.FromArgb(134, 239, 172),
                CornerRadius = 9,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(6, 184),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(562, 106),
                Text = string.Empty
            };
            editor.PointsTextBox = pointsBox;
            page.Controls.Add(pointsBox);

            var pathAdvanced = CreateFoldout(page, "高级路径设置", 302, 850, true);
            pathAdvanced.Content.Height = 68;
            var loopCheckBox = AddCheckBox(pathAdvanced.Content, "循环路径", 6, 12, 92, true);
            var reverseCheckBox = AddCheckBox(pathAdvanced.Content, "到终点反向", 102, 12, 106, false);
            var deathStopCheckBox = AddCheckBox(pathAdvanced.Content, "死亡停止路径", 206, 12, 130, true);
            AddLabel(pathAdvanced.Content, "最短录制距离固定 5 米，自动录制每 250ms 读取一次", 346, 13, 360, 24);
            if (kind == SharedPathKind.Revive)
            {
                loopPathCheckBox = loopCheckBox;
                reverseAtEndCheckBox = reverseCheckBox;
                deathStopPathCheckBox = deathStopCheckBox;
            }

            RefreshPathEditor(editor);
            return tab;
        }

        private void RefreshPathLibrary()
        {
            var result = _pathStore.LoadSummariesAsync().GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                currentPathSummaries = Array.Empty<SharedPathSummary>();
                foreach (var editor in pathEditors.Values)
                {
                    SetPathStatus(editor, result.Error ?? "读取共享路径失败", true);
                }

                return;
            }

            currentPathSummaries = result.Value;
            RefreshSavedPathCombos();
            RefreshPathOverviews();
        }

        private void RefreshSavedPathCombos()
        {
            loadingPathCombos = true;
            try
            {
                foreach (var editor in pathEditors.Values)
                {
                    var selectedName = GetSelectedPathName(editor);
                    if (string.IsNullOrWhiteSpace(selectedName))
                    {
                        selectedName = editor.PathNameTextBox?.Text;
                    }

                    editor.SavedPathCombo?.Items.Clear();
                    foreach (var summary in currentPathSummaries)
                    {
                        editor.SavedPathCombo?.Items.Add(new PathComboItem(summary));
                    }

                    SelectPathComboItem(editor, selectedName, loadPath: false);
                }
            }
            finally
            {
                loadingPathCombos = false;
            }
        }

        private void SelectConfiguredPath(SharedPathKind kind, string? pathName)
        {
            if (!pathEditors.TryGetValue(kind, out var editor) || string.IsNullOrWhiteSpace(pathName))
            {
                RefreshPathOverviews();
                return;
            }

            if (!SelectPathComboItem(editor, pathName, loadPath: true))
            {
                SetPathStatus(editor, "路径未保存: " + pathName, true);
                RefreshPathOverviews();
            }
        }

        private bool SelectPathComboItem(PathEditorControls editor, string? pathName, bool loadPath)
        {
            if (editor.SavedPathCombo is null || string.IsNullOrWhiteSpace(pathName))
            {
                return false;
            }

            for (var i = 0; i < editor.SavedPathCombo.Items.Count; i++)
            {
                if (editor.SavedPathCombo.Items[i] is PathComboItem item &&
                    string.Equals(item.Name, pathName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var wasLoading = loadingPathCombos;
                    loadingPathCombos = true;
                    try
                    {
                        editor.SavedPathCombo.SelectedIndex = i;
                    }
                    finally
                    {
                        loadingPathCombos = wasLoading;
                    }

                    if (loadPath)
                    {
                        LoadPathByName(editor, item.Name);
                    }

                    return true;
                }
            }

            return false;
        }

        private void LoadSelectedPath(PathEditorControls editor)
        {
            if (loadingPathCombos)
            {
                return;
            }

            var name = GetSelectedPathName(editor);
            if (!string.IsNullOrWhiteSpace(name))
            {
                LoadPathByName(editor, name);
            }
        }

        private void LoadPathByName(PathEditorControls editor, string name)
        {
            var result = _pathStore.LoadAsync(name).GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                SetPathStatus(editor, result.Error ?? "加载路径失败", true);
                return;
            }

            editor.Buffer.Load(result.Value.Points);
            editor.SkippedCount = 0;
            SetText(editor.PathNameTextBox, result.Value.Name);
            SetCleanupNpcSelection(editor, result.Value.CleanupNpcName);
            RefreshPathEditor(editor);
            RefreshPathOverviews();
            SetPathStatus(editor, "已加载路径: " + result.Value.Name, false);
        }

        private async void SavePath(PathEditorControls editor)
        {
            var name = GetText(editor.PathNameTextBox, string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                SetPathStatus(editor, "路径名不能为空", true);
                return;
            }

            var document = editor.Buffer.ToDocument(name);
            if (editor.Kind == SharedPathKind.Maintenance)
            {
                document.CleanupNpcName = GetSelectedCleanupNpcName(editor);
            }

            var result = await _pathStore.SaveAsync(document).ConfigureAwait(true);
            if (!result.Success)
            {
                SetPathStatus(editor, result.Error ?? "保存路径失败", true);
                return;
            }

            RefreshPathLibrary();
            SelectPathComboItem(editor, name, loadPath: false);
            RefreshPathOverviews();
            SetPathStatus(editor, "已保存共享路径: " + name, false);
        }

        private async void DeleteSavedPath(PathEditorControls editor)
        {
            var name = GetSelectedPathName(editor);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetText(editor.PathNameTextBox, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                SetPathStatus(editor, "没有可删除的路径名", true);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                "删除共享路径: " + name + "?",
                "删除保存路径",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var result = await _pathStore.DeleteAsync(name).ConfigureAwait(true);
            if (!result.Success)
            {
                SetPathStatus(editor, result.Error ?? "删除路径失败", true);
                return;
            }

            RefreshPathLibrary();
            SetPathStatus(editor, "已删除共享路径: " + name, false);
        }

        private async void AddManualPathPoint(PathEditorControls editor)
        {
            await AddCurrentPlayerPointAsync(editor, "手动录点", showSkipped: true).ConfigureAwait(true);
        }

        private void StartPathRecording(PathEditorControls editor)
        {
            if (recordingPathKind.HasValue && recordingPathKind.Value != editor.Kind)
            {
                if (pathEditors.TryGetValue(recordingPathKind.Value, out var previous))
                {
                    SetPathStatus(previous, "自动录制已切换到其他路径", false);
                }
            }

            recordingPathKind = editor.Kind;
            pathRecordTimer.Interval = 250;
            pathRecordTimer.Start();
            SetPathStatus(editor, "自动录制中", false);
        }

        private void StopPathRecording(PathEditorControls editor)
        {
            if (recordingPathKind == editor.Kind)
            {
                pathRecordTimer.Stop();
                recordingPathKind = null;
                pathRecordReadInFlight = false;
            }

            SetPathStatus(editor, "自动录制已停止", false);
        }

        private async void PathRecordTimer_Tick(object? sender, EventArgs e)
        {
            if (!recordingPathKind.HasValue ||
                pathRecordReadInFlight ||
                !pathEditors.TryGetValue(recordingPathKind.Value, out var editor))
            {
                return;
            }

            pathRecordReadInFlight = true;
            try
            {
                await AddCurrentPlayerPointAsync(editor, "自动录点", showSkipped: false).ConfigureAwait(true);
            }
            finally
            {
                pathRecordReadInFlight = false;
            }
        }

        private async Task AddCurrentPlayerPointAsync(PathEditorControls editor, string reason, bool showSkipped)
        {
            var result = await _runtime.ReadPlayerAsync(_account).ConfigureAwait(true);
            if (!result.Success || result.Value is null)
            {
                SetPathStatus(editor, result.Error ?? "读取玩家坐标失败", true);
                return;
            }

            if (result.Value.Position is not { } position)
            {
                SetPathStatus(editor, "玩家坐标为空", true);
                return;
            }

            var addResult = editor.Buffer.TryAdd(position, result.Value.CapturedAt);
            if (!addResult.Success)
            {
                editor.SkippedCount++;
                RefreshPathEditor(editor);
                if (showSkipped)
                {
                    SetPathStatus(editor, addResult.Error ?? "距离不足 5 米，未录点", true);
                }

                return;
            }

            RefreshPathEditor(editor);
            RefreshPathOverviews();
            SetPathStatus(editor, reason + "成功: " + FormatVector(position), false);
        }

        private void RemoveLastPathPoint(PathEditorControls editor)
        {
            var result = editor.Buffer.RemoveLast();
            if (!result.Success)
            {
                SetPathStatus(editor, result.Error ?? "没有路径点", true);
                return;
            }

            RefreshPathEditor(editor);
            RefreshPathOverviews();
            SetPathStatus(editor, "已删除末点", false);
        }

        private void ClearPathPoints(PathEditorControls editor)
        {
            editor.Buffer.Clear();
            editor.SkippedCount = 0;
            RefreshPathEditor(editor);
            RefreshPathOverviews();
            SetPathStatus(editor, "路径点已清空", false);
        }

        private void CopyPath(PathEditorControls editor)
        {
            var text = editor.Buffer.ToCoordinateText();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetPathStatus(editor, "没有可复制的路径点", true);
                return;
            }

            Clipboard.SetText(text);
            SetPathStatus(editor, "路径文本已复制", false);
        }

        private async Task ExecutePathAsync(PathEditorControls editor)
        {
            if (editor.ExecutePathCancellation is { IsCancellationRequested: false } running)
            {
                running.Cancel();
                SetPathStatus(editor, "正在停止路径", false);
                if (editor.ExecutePathButton is { IsDisposed: false } runningButton)
                {
                    runningButton.Text = "停止中...";
                }

                return;
            }

            if (editor.Buffer.Count == 0)
            {
                SetPathStatus(editor, "路径为空，无法执行", true);
                return;
            }

            if (editor.ExecutePathButton is not { } button)
            {
                return;
            }

            using var executionCts = new CancellationTokenSource();
            editor.ExecutePathCancellation = executionCts;
            button.Text = "停止路径";
            SetPathStatus(editor, "正在执行路径", false);

            try
            {
                var result = await _runtime
                    .ExecutePathAsync(
                        _account,
                        GetText(editor.PathNameTextBox, "manual_path"),
                        editor.Buffer.Points.ToArray(),
                        CaptureScriptSettings(),
                        executionCts.Token)
                    .ConfigureAwait(true);
                SetPathStatus(
                    editor,
                    executionCts.IsCancellationRequested ? "路径执行已停止" :
                    result.Success ? "路径执行完成" : result.Error ?? "路径执行失败",
                    !result.Success && !executionCts.IsCancellationRequested);
            }
            finally
            {
                if (ReferenceEquals(editor.ExecutePathCancellation, executionCts))
                {
                    editor.ExecutePathCancellation = null;
                }

                if (!button.IsDisposed)
                {
                    button.Text = "执行路径";
                }
            }
        }

        private void RefreshPathEditor(PathEditorControls editor)
        {
            if (editor.PointsTextBox is not null)
            {
                editor.PointsTextBox.Text = editor.Buffer.ToCoordinateText();
            }

            if (editor.SummaryLabel is not null)
            {
                editor.SummaryLabel.Text =
                    "点数  " + editor.Buffer.Count.ToString(CultureInfo.InvariantCulture) +
                    "  |  总距  " + editor.Buffer.TotalDistance.ToString("F1", CultureInfo.InvariantCulture) +
                    "  |  跳过  " + editor.SkippedCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        private async Task RefreshCleanupNpcsAsync(PathEditorControls editor)
        {
            if (editor.CleanupNpcRefreshButton is not { } button)
            {
                return;
            }

            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "刷新中...";

            try
            {
                var result = await _runtime.RefreshWorldObjectsAsync(_account).ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    SetPathStatus(editor, result.Error ?? "读取附近NPC失败", true);
                    return;
                }

                var count = PopulateCleanupNpcCombo(editor, result.Value);
                SetPathStatus(
                    editor,
                    "已刷新 10m 内 NPC " + count.ToString(CultureInfo.InvariantCulture) + " 个",
                    count == 0);
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

        private int PopulateCleanupNpcCombo(PathEditorControls editor, IEnumerable<WorldObjectSnapshot> objects)
        {
            if (editor.CleanupNpcCombo is null)
            {
                return 0;
            }

            var previousName = GetSelectedCleanupNpcName(editor);
            var items = objects
                .Where(IsCleanupNpcCandidate)
                .GroupBy(target => target.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new CleanupNpcComboItem(
                    group.Key,
                    group.Min(target => target.DistanceToLocalPlayer ?? double.MaxValue)))
                .OrderBy(item => item.DistanceMeters)
                .ThenBy(item => item.Name, StringComparer.CurrentCulture)
                .ToArray();

            editor.CleanupNpcCombo.Items.Clear();
            foreach (var item in items)
            {
                editor.CleanupNpcCombo.Items.Add(item);
            }

            if (items.Length == 0)
            {
                editor.CleanupNpcCombo.SelectedIndex = -1;
                editor.CleanupNpcCombo.Text = string.Empty;
                return 0;
            }

            var selectedIndex = Array.FindIndex(
                items,
                item => string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase));
            editor.CleanupNpcCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            return items.Length;
        }

        private static bool IsCleanupNpcCandidate(WorldObjectSnapshot target)
        {
            return string.Equals(target.ObjectKind, "npc", StringComparison.OrdinalIgnoreCase) &&
                   target.IsAlive &&
                   !string.IsNullOrWhiteSpace(target.Name) &&
                   target.DistanceToLocalPlayer is <= CleanupNpcSearchRadiusMeters;
        }

        private static string GetSelectedCleanupNpcName(PathEditorControls editor)
        {
            if (editor.CleanupNpcCombo is null)
            {
                return string.Empty;
            }

            if (editor.CleanupNpcCombo.SelectedItem is CleanupNpcComboItem item)
            {
                return item.Name;
            }

            return editor.CleanupNpcCombo.Text.Trim();
        }

        private static void SetCleanupNpcSelection(PathEditorControls editor, string? npcName)
        {
            if (editor.CleanupNpcCombo is null)
            {
                return;
            }

            var trimmed = npcName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                editor.CleanupNpcCombo.SelectedIndex = -1;
                editor.CleanupNpcCombo.Text = string.Empty;
                return;
            }

            for (var i = 0; i < editor.CleanupNpcCombo.Items.Count; i++)
            {
                if (editor.CleanupNpcCombo.Items[i] is CleanupNpcComboItem item &&
                    string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    editor.CleanupNpcCombo.SelectedIndex = i;
                    return;
                }
            }

            editor.CleanupNpcCombo.Items.Add(new CleanupNpcComboItem(trimmed, null));
            editor.CleanupNpcCombo.SelectedIndex = editor.CleanupNpcCombo.Items.Count - 1;
        }

        private void RefreshPathOverviews()
        {
            SetPathOverview(SharedPathKind.Revive, "复活路径", revivePathNameTextBox?.Text);
            SetPathOverview(SharedPathKind.Combat, "打怪路径", combatPathNameTextBox?.Text);
            SetPathOverview(SharedPathKind.Maintenance, "清包路径", maintenancePathNameTextBox?.Text);
        }

        private void SetPathOverview(SharedPathKind kind, string label, string? pathName)
        {
            if (!pathOverviewLabels.TryGetValue(kind, out var overview))
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(pathName) ? "未选" : pathName.Trim();
            var summary = currentPathSummaries.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            var pointCount = summary?.PointCount ?? (pathEditors.TryGetValue(kind, out var editor) ? editor.Buffer.Count : 0);
            overview.Text = label + ":  " + name + "（" + pointCount.ToString(CultureInfo.InvariantCulture) + "点）";
        }

        private string GetSelectedPathName(PathEditorControls editor)
        {
            if (editor.SavedPathCombo is null)
            {
                return string.Empty;
            }

            var selectedIndex = editor.SavedPathCombo.SelectedIndex;
            if (selectedIndex >= 0 &&
                selectedIndex < editor.SavedPathCombo.Items.Count &&
                editor.SavedPathCombo.Items[selectedIndex] is PathComboItem item)
            {
                return item.Name;
            }

            return editor.SavedPathCombo.Text;
        }

        private void SetPathStatus(PathEditorControls editor, string text, bool isError)
        {
            if (editor.StatusLabel is null)
            {
                return;
            }

            editor.StatusLabel.Text = text;
            editor.StatusLabel.ForeColor = isError ? Color.FromArgb(166, 40, 40) : _textGreen;
        }

        private static string FormatVector(Vector3Snapshot position)
        {
            return "X=" + position.X.ToString("F2", CultureInfo.InvariantCulture) +
                   " Y=" + position.Y.ToString("F2", CultureInfo.InvariantCulture) +
                   " Z=" + position.Z.ToString("F2", CultureInfo.InvariantCulture);
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

            AddLabel(page, "血量低于", 4, 78, 66, 24);
            sitHpBelowTextBox = AddTextBox(page, "25", 68, 76, 70, 28);
            AddLabel(page, "%  坐地板，恢复到", 144, 78, 130, 24);
            sitHpRecoverToTextBox = AddTextBox(page, "75", 272, 76, 70, 28);
            AddLabel(page, "%  起来继续打怪", 348, 78, 160, 24);

            AddLabel(page, "血量维护", 4, 120, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增血量维护", 68, 116, 120, 30, (_, _) => AddMaintenanceKeyRuleRow(hpMaintenanceRuleList, hpMaintenanceEmptyLabel));
            var refreshMaintenanceSkillsButton = AddButton(page, "刷新技能", 196, 116, 90, 30);
            refreshMaintenanceSkillsButton.Click += async (_, _) =>
                await RefreshCurrentSkillsAsync(refreshMaintenanceSkillsButton, availableTree: null, systemTree: null).ConfigureAwait(true);
            hpMaintenanceRuleList = CreateMaintenanceRuleList(page, 4, 154, 830, 82);
            hpMaintenanceEmptyLabel = AddLabel(page, "暂无血量维护", 4, 154, 140, 24);
            hpMaintenanceEmptyLabel.BringToFront();

            AddLabel(page, "蓝量维护", 4, 246, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增蓝量维护", 68, 242, 120, 30, (_, _) => AddMaintenanceKeyRuleRow(mpMaintenanceRuleList, mpMaintenanceEmptyLabel));
            mpMaintenanceRuleList = CreateMaintenanceRuleList(page, 4, 280, 830, 82);
            mpMaintenanceEmptyLabel = AddLabel(page, "暂无蓝量维护", 4, 280, 140, 24);
            mpMaintenanceEmptyLabel.BringToFront();

            AddLabel(page, "状态维护", 4, 372, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增状态维护", 68, 368, 120, 30, (_, _) => AddStatusMaintenanceRuleRow(statusMaintenanceRuleList, statusMaintenanceEmptyLabel));
            statusMaintenanceRuleList = CreateMaintenanceRuleList(page, 4, 406, 830, 66);
            statusMaintenanceEmptyLabel = AddLabel(page, "暂无状态维护", 4, 406, 140, 24);
            statusMaintenanceEmptyLabel.BringToFront();

            return tab;
        }

        private TabPage CreateBagCleanupTab()
        {
            var tab = CreateBaseTab("清包");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddCheckBox(page, "自动清包", 4, 16, 100, false);
            AddLabel(page, "清包阈值", 100, 16, 70, 26, _textGreen, FontStyle.Bold);
            bagCleanupThresholdTextBox = AddTextBox(page, "85", 172, 14, 84, 28);

            void AddCleanupOption(string text, int x, int y, int checkWidth)
            {
                AddCheckBox(page, text, x, y, checkWidth, false);
                AddCombo(page, x + checkWidth + 4, y - 1, 68, 28, "出售", "丢弃");
            }

            AddLabel(page, "清理物品类型", 4, 54, 120, 24, _textGreen, FontStyle.Bold);

            AddLabel(page, "装备品质", 18, 88, 80, 24, _textGreen, FontStyle.Bold);
            AddCleanupOption("绿色武器", 24, 116, 90);
            AddCleanupOption("蓝色武器", 214, 116, 90);
            AddCleanupOption("黄金武器", 404, 116, 90);
            AddCleanupOption("橙色武器", 594, 116, 90);
            AddCleanupOption("绿色防具", 24, 148, 90);
            AddCleanupOption("蓝色防具", 214, 148, 90);
            AddCleanupOption("黄金防具", 404, 148, 90);
            AddCleanupOption("橙色防具", 594, 148, 90);

            AddLabel(page, "魔石", 18, 196, 80, 24, _textGreen, FontStyle.Bold);
            AddCleanupOption("白色魔石", 24, 224, 90);
            AddCleanupOption("绿色魔石", 214, 224, 90);

            AddLabel(page, "书卷", 18, 272, 80, 24, _textGreen, FontStyle.Bold);
            AddCleanupOption("烙印", 24, 300, 70);
            AddCleanupOption("制作图纸/卷轴", 214, 300, 130);
            AddCleanupOption("技能书", 444, 300, 78);
            AddCleanupOption("咒语书", 624, 300, 78);

            AddLabel(page, "提炼石", 18, 348, 80, 24, _textGreen, FontStyle.Bold);
            AddCleanupOption("白色提炼石", 24, 376, 106);
            AddCleanupOption("绿色提炼石", 214, 376, 106);
            AddCleanupOption("蓝色提炼石", 404, 376, 106);
            AddCleanupOption("金色提炼石", 594, 376, 106);

            AddLabel(page, "药品", 18, 424, 80, 24, _textGreen, FontStyle.Bold);
            AddCleanupOption("药水/仙药/灵药", 24, 452, 140);

            return tab;
        }

        private FlowLayoutPanel CreateMaintenanceRuleList(Control parent, int x, int y, int width, int height)
        {
            var list = new FlowLayoutPanel
            {
                AutoScroll = true,
                BackColor = _pageBackground,
                FlowDirection = FlowDirection.TopDown,
                Location = new Point(x, y),
                Size = new Size(width, height),
                WrapContents = false
            };

            parent.Controls.Add(list);
            return list;
        }

        private void PopulateMaintenanceKeyRules(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            IEnumerable<MaintenanceKeyRuleConfig>? rules)
        {
            if (list is null)
            {
                return;
            }

            list.Controls.Clear();
            foreach (var rule in rules ?? Array.Empty<MaintenanceKeyRuleConfig>())
            {
                AddMaintenanceKeyRuleRow(
                    list,
                    emptyLabel,
                    rule.BelowPercent,
                    rule.Key,
                    rule.SkillId,
                    rule.SkillName,
                    rule.RunTiming);
            }

            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private void PopulateStatusMaintenanceRules(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            IEnumerable<StatusMaintenanceRuleConfig>? rules)
        {
            if (list is null)
            {
                return;
            }

            list.Controls.Clear();
            foreach (var rule in rules ?? Array.Empty<StatusMaintenanceRuleConfig>())
            {
                AddStatusMaintenanceRuleRow(
                    list,
                    emptyLabel,
                    rule.Key,
                    rule.SkillId,
                    rule.SkillName,
                    rule.RunTiming,
                    rule.AbnormalStatusId);
            }

            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private void AddMaintenanceKeyRuleRow(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            int belowPercent = 50,
            string key = "",
            uint skillId = 0,
            string skillName = "",
            MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always)
        {
            if (list is null)
            {
                return;
            }

            var row = new Panel
            {
                BackColor = _pageBackground,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 0, 7),
                Size = new Size(630, 31)
            };

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(0, 3),
                Size = new Size(34, 24),
                Text = "低于",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var thresholdTextBox = AddTextBox(
                row,
                Math.Clamp(belowPercent, 0, 100).ToString(CultureInfo.InvariantCulture),
                36,
                1,
                54,
                28);
            thresholdTextBox.Name = "maintenanceRuleBelowTextBox";

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(94, 3),
                Size = new Size(42, 24),
                Text = "% 按",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var skillCombo = AddCombo(row, 138, 1, 210, 28);
            skillCombo.Name = "maintenanceRuleSkillCombo";
            PopulateMaintenanceSkillCombo(skillCombo, skillId, skillName);

            var timingCombo = AddCombo(row, 356, 1, 90, 28);
            timingCombo.Name = "maintenanceRuleTimingCombo";
            PopulateMaintenanceTimingCombo(timingCombo, runTiming);

            var keyButton = AddButton(row, "选择按键", 454, 0, 104, 30);
            keyButton.Name = "maintenanceRuleKeyButton";
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = key;
                keyButton.Text = FormatSkillKey(key);
            }

            var deleteButton = AddButton(row, "删除", 566, 0, 58, 30);
            deleteButton.Click += (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
                RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
            };

            keyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(keyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    keyButton.Tag = selectedKey;
                    keyButton.Text = FormatSkillKey(selectedKey);
                }
            };

            list.Controls.Add(row);
            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private void AddStatusMaintenanceRuleRow(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            string key = "",
            uint skillId = 0,
            string skillName = "",
            MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
            uint abnormalStatusId = 0)
        {
            if (list is null)
            {
                return;
            }

            var row = new Panel
            {
                BackColor = _pageBackground,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 0, 7),
                Size = new Size(630, 31),
                Tag = abnormalStatusId
            };

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(0, 3),
                Size = new Size(28, 24),
                Text = "技能",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var skillCombo = AddCombo(row, 36, 1, 260, 28);
            skillCombo.Name = "maintenanceRuleSkillCombo";
            PopulateMaintenanceSkillCombo(skillCombo, skillId, skillName);

            var timingCombo = AddCombo(row, 304, 1, 90, 28);
            timingCombo.Name = "maintenanceRuleTimingCombo";
            PopulateMaintenanceTimingCombo(timingCombo, runTiming);

            var keyButton = AddButton(row, "选择按键", 402, 0, 104, 30);
            keyButton.Name = "maintenanceRuleKeyButton";
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = key;
                keyButton.Text = FormatSkillKey(key);
            }

            var deleteButton = AddButton(row, "删除", 514, 0, 58, 30);
            deleteButton.Click += (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
                RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
            };

            keyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(keyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    keyButton.Tag = selectedKey;
                    keyButton.Text = FormatSkillKey(selectedKey);
                }
            };

            list.Controls.Add(row);
            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private static void RefreshMaintenanceRuleEmptyLabel(FlowLayoutPanel? list, Label? emptyLabel)
        {
            if (emptyLabel is not null)
            {
                emptyLabel.Visible = list?.Controls.OfType<Panel>().Any() != true;
            }
        }

        private void RefreshMaintenanceSkillCombos()
        {
            RefreshMaintenanceSkillCombos(hpMaintenanceRuleList);
            RefreshMaintenanceSkillCombos(mpMaintenanceRuleList);
            RefreshMaintenanceSkillCombos(statusMaintenanceRuleList);
        }

        private void RefreshMaintenanceSkillCombos(FlowLayoutPanel? list)
        {
            if (list is null)
            {
                return;
            }

            foreach (var row in list.Controls.OfType<Panel>())
            {
                var skillCombo = row.Controls
                    .OfType<RoundedComboBox>()
                    .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleSkillCombo", StringComparison.Ordinal));
                if (skillCombo is null)
                {
                    continue;
                }

                var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);
                PopulateMaintenanceSkillCombo(skillCombo, selectedSkill.SkillId, selectedSkill.SkillName);
            }
        }

        private void PopulateMaintenanceSkillCombo(RoundedComboBox combo, uint selectedSkillId, string? selectedSkillName)
        {
            var normalizedSelectedName = selectedSkillName?.Trim() ?? string.Empty;
            combo.Items.Clear();
            combo.Items.Add(MaintenanceSkillComboItem.Empty);

            var selectedIndex = 0;
            var index = 1;
            foreach (var skill in currentManualSkills
                         .GroupBy(skill => skill.SkillId)
                         .Select(group => group.First())
                         .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture))
            {
                var name = FormatManualSkillName(skill);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var item = new MaintenanceSkillComboItem(skill.SkillId, name);
                combo.Items.Add(item);
                if ((selectedSkillId != 0 && skill.SkillId == selectedSkillId) ||
                    (selectedSkillId == 0 && string.Equals(name, normalizedSelectedName, StringComparison.Ordinal)))
                {
                    selectedIndex = index;
                }

                index++;
            }

            if (selectedIndex == 0 && (!string.IsNullOrWhiteSpace(normalizedSelectedName) || selectedSkillId != 0))
            {
                var savedItem = new MaintenanceSkillComboItem(selectedSkillId, normalizedSelectedName);
                combo.Items.Add(savedItem);
                selectedIndex = combo.Items.Count - 1;
            }

            combo.SelectedIndex = selectedIndex;
        }

        private static MaintenanceSkillComboItem GetSelectedMaintenanceSkill(RoundedComboBox? combo)
        {
            if (combo is null ||
                combo.SelectedIndex < 0 ||
                combo.SelectedIndex >= combo.Items.Count)
            {
                return MaintenanceSkillComboItem.Empty;
            }

            if (combo.Items[combo.SelectedIndex] is MaintenanceSkillComboItem item)
            {
                return item.SkillId == 0 && string.IsNullOrWhiteSpace(item.SkillName)
                    ? MaintenanceSkillComboItem.Empty
                    : item;
            }

            var text = combo.Text.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? MaintenanceSkillComboItem.Empty
                : new MaintenanceSkillComboItem(0, text);
        }

        private static void PopulateMaintenanceTimingCombo(RoundedComboBox combo, MaintenanceRuleRunTiming selectedTiming)
        {
            combo.Items.Clear();
            var items = new[]
            {
                new MaintenanceTimingComboItem(MaintenanceRuleRunTiming.Always, "全时"),
                new MaintenanceTimingComboItem(MaintenanceRuleRunTiming.InCombat, "战斗中"),
                new MaintenanceTimingComboItem(MaintenanceRuleRunTiming.AfterCombat, "战斗后")
            };

            var selectedIndex = 0;
            for (var i = 0; i < items.Length; i++)
            {
                combo.Items.Add(items[i]);
                if (items[i].RunTiming == selectedTiming)
                {
                    selectedIndex = i;
                }
            }

            combo.SelectedIndex = selectedIndex;
        }

        private static MaintenanceRuleRunTiming GetSelectedMaintenanceRunTiming(RoundedComboBox? combo)
        {
            if (combo is null ||
                combo.SelectedIndex < 0 ||
                combo.SelectedIndex >= combo.Items.Count)
            {
                return MaintenanceRuleRunTiming.Always;
            }

            return combo.Items[combo.SelectedIndex] is MaintenanceTimingComboItem item
                ? item.RunTiming
                : MaintenanceRuleRunTiming.Always;
        }

        private void ApplyOpeningSkillSettings(OpeningSkillConfig? config)
        {
            var openingSkill = config ?? new OpeningSkillConfig();
            if (openingSkillEnabledCheckBox is not null)
            {
                openingSkillEnabledCheckBox.Checked = openingSkill.Enabled;
            }

            PopulateOpeningSkillCombo(openingSkillCombo, openingSkill.SkillId, openingSkill.SkillName);
            SetOpeningSkillKey(openingSkill.Key);
        }

        private OpeningSkillConfig CaptureOpeningSkill()
        {
            var selectedSkill = GetSelectedOpeningSkill(openingSkillCombo);
            return new OpeningSkillConfig
            {
                Enabled = openingSkillEnabledCheckBox?.Checked ?? false,
                SkillId = selectedSkill.SkillId,
                SkillName = selectedSkill.SkillName,
                Key = openingSkillKeyButton?.Tag as string ?? string.Empty
            };
        }

        private void SetOpeningSkillKey(string? key)
        {
            if (openingSkillKeyButton is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                openingSkillKeyButton.Tag = null;
                openingSkillKeyButton.Text = "选择按键";
                return;
            }

            openingSkillKeyButton.Tag = key;
            openingSkillKeyButton.Text = FormatSkillKey(key);
        }

        private void RefreshOpeningSkillCombo()
        {
            var selectedSkill = GetSelectedOpeningSkill(openingSkillCombo);
            PopulateOpeningSkillCombo(openingSkillCombo, selectedSkill.SkillId, selectedSkill.SkillName);
        }

        private void PopulateOpeningSkillCombo(RoundedComboBox? combo, uint selectedSkillId, string? selectedSkillName)
        {
            if (combo is null)
            {
                return;
            }

            var normalizedSelectedName = selectedSkillName?.Trim() ?? string.Empty;
            combo.Items.Clear();
            combo.Items.Add(OpeningSkillComboItem.Empty);

            var selectedIndex = 0;
            var index = 1;
            foreach (var skill in currentManualSkills
                         .Where(skill => !ShouldHideManualSkillCandidate(skill))
                         .Where(IsOpeningSkillCandidate)
                         .GroupBy(skill => skill.SkillId)
                         .Select(group => group.First())
                         .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture))
            {
                var name = FormatManualSkillName(skill);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var item = new OpeningSkillComboItem(skill.SkillId, name);
                combo.Items.Add(item);
                if ((selectedSkillId != 0 && skill.SkillId == selectedSkillId) ||
                    (selectedSkillId == 0 && string.Equals(name, normalizedSelectedName, StringComparison.Ordinal)))
                {
                    selectedIndex = index;
                }

                index++;
            }

            if (selectedIndex == 0 && (!string.IsNullOrWhiteSpace(normalizedSelectedName) || selectedSkillId != 0))
            {
                combo.Items.Add(new OpeningSkillComboItem(selectedSkillId, normalizedSelectedName));
                selectedIndex = combo.Items.Count - 1;
            }

            combo.SelectedIndex = selectedIndex;
        }

        private static OpeningSkillComboItem GetSelectedOpeningSkill(RoundedComboBox? combo)
        {
            if (combo is null ||
                combo.SelectedIndex < 0 ||
                combo.SelectedIndex >= combo.Items.Count)
            {
                return OpeningSkillComboItem.Empty;
            }

            if (combo.Items[combo.SelectedIndex] is OpeningSkillComboItem item)
            {
                return item.SkillId == 0 && string.IsNullOrWhiteSpace(item.SkillName)
                    ? OpeningSkillComboItem.Empty
                    : item;
            }

            var text = combo.Text.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? OpeningSkillComboItem.Empty
                : new OpeningSkillComboItem(0, text);
        }

        private static bool IsOpeningSkillCandidate(SkillSnapshot skill)
        {
            return MatchesManualSkillType(skill, "主动技能") ||
                   MatchesManualSkillType(skill, "状态技能");
        }

        private static List<MaintenanceKeyRuleConfig> CaptureMaintenanceKeyRules(FlowLayoutPanel? list)
        {
            if (list is null)
            {
                return new List<MaintenanceKeyRuleConfig>();
            }

            return list.Controls
                .OfType<Panel>()
                .Select(row =>
                {
                    var belowTextBox = row.Controls
                        .OfType<RoundedTextBox>()
                        .FirstOrDefault(textBox => string.Equals(textBox.Name, "maintenanceRuleBelowTextBox", StringComparison.Ordinal));
                    var keyButton = row.Controls
                        .OfType<Button>()
                        .FirstOrDefault(button => string.Equals(button.Name, "maintenanceRuleKeyButton", StringComparison.Ordinal));
                    var skillCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleSkillCombo", StringComparison.Ordinal));
                    var timingCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleTimingCombo", StringComparison.Ordinal));
                    var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);

                    return new MaintenanceKeyRuleConfig
                    {
                        BelowPercent = ReadPercent(belowTextBox, 50),
                        Key = keyButton?.Tag as string ?? string.Empty,
                        SkillId = selectedSkill.SkillId,
                        SkillName = selectedSkill.SkillName,
                        RunTiming = GetSelectedMaintenanceRunTiming(timingCombo)
                    };
                })
                .ToList();
        }

        private static List<StatusMaintenanceRuleConfig> CaptureStatusMaintenanceRules(FlowLayoutPanel? list)
        {
            if (list is null)
            {
                return new List<StatusMaintenanceRuleConfig>();
            }

            return list.Controls
                .OfType<Panel>()
                .Select(row =>
                {
                    var keyButton = row.Controls
                        .OfType<Button>()
                        .FirstOrDefault(button => string.Equals(button.Name, "maintenanceRuleKeyButton", StringComparison.Ordinal));
                    var skillCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleSkillCombo", StringComparison.Ordinal));
                    var timingCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleTimingCombo", StringComparison.Ordinal));
                    var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);

                    return new StatusMaintenanceRuleConfig
                    {
                        Key = keyButton?.Tag as string ?? string.Empty,
                        SkillId = selectedSkill.SkillId,
                        SkillName = selectedSkill.SkillName,
                        AbnormalStatusId = row.Tag is uint abnormalStatusId ? abnormalStatusId : 0,
                        RunTiming = GetSelectedMaintenanceRunTiming(timingCombo)
                    };
                })
                .ToList();
        }

        private TabPage CreateSkillTab()
        {
            var tab = CreateBaseTab("技能");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "技能配置", 4, 16, 90, 24, _textGreen, FontStyle.Bold);
            var autoMode = AddRadioButton(page, "自动技能", 92, 14, 90, true);
            skillAutoModeRadio = autoMode;
            var manualMode = AddRadioButton(page, "手动设置", 190, 14, 96, false);
            skillManualModeRadio = manualMode;
            var systemMode = AddRadioButton(page, "系统设置", 292, 14, 96, false);
            skillSystemModeRadio = systemMode;
            openingAttackKeyCheckBox = AddCheckBox(page, "开怪按C", 548, 14, 92, true);
            spiritmasterAutoSkillCheckBox = AddCheckBox(page, "精灵专用", 648, 14, 110, false);
            spiritmasterAutoSkillCheckBox.Click += (_, _) => RefreshSpiritmasterAutoSkillCheckBoxState();
            spiritmasterSettingsButton = AddButton(page, "精灵设置", 740, 10, 96, 30, (_, _) => ShowSpiritmasterSettingsDialog());
            spiritmasterSettingsButton.Visible = false;

            var autoPanel = CreateSkillModePanel(page, "autoSkillPanel", true);
            autoSkillPanel = autoPanel;
            var manualPanel = CreateSkillModePanel(page, "manualSkillPanel", false);
            manualSkillPanel = manualPanel;
            var systemPanel = CreateSkillModePanel(page, "systemSkillPanel", false);
            systemSkillPanel = systemPanel;

            AddLabel(autoPanel, "可用技能", 8, 6, 120, 24, _textGreen, FontStyle.Bold);
            AddLabel(autoPanel, "技能执行顺序", 378, 6, 140, 24, _textGreen, FontStyle.Bold);

            var availableTree = CreateSkillTree(autoPanel, "availableSkillTree", 8, 34, 260, 260);
            availableSkillTree = availableTree;
            var selectedTree = CreateSkillTree(autoPanel, "selectedSkillTree", 378, 34, 300, 260);
            selectedSkillTree = selectedTree;
            PopulateAvailableSkillTree(availableTree);
            PopulateSelectedSkillTree(selectedTree);

            var refreshSkillsButton = AddButton(page, "刷新当前技能", 390, 10, 150, 30);
            refreshSkillsButton.Click += async (_, _) =>
                await RefreshCurrentSkillsAsync(refreshSkillsButton, availableTree, systemSkillTree).ConfigureAwait(true);

            AddButton(autoPanel, "添加 >", 288, 102, 70, 30, (_, _) => AddSkillSelection(availableTree, selectedTree));
            AddButton(autoPanel, "< 移除", 288, 140, 70, 30, (_, _) => RemoveSelectedSkill(selectedTree));
            AddButton(autoPanel, "全部 >>", 288, 178, 70, 30, (_, _) => AddAllAvailableSkills(availableTree, selectedTree));
            AddButton(autoPanel, "清空", 288, 216, 70, 30, (_, _) => selectedTree.Nodes.Clear());

            var refreshSelectedSkillsButton = AddButton(autoPanel, "刷新当前已选技能", 696, 62, 132, 30);
            refreshSelectedSkillsButton.Click += async (_, _) =>
                await RefreshSelectedSkillTreeAsync(refreshSelectedSkillsButton, selectedTree).ConfigureAwait(true);

            AddButton(autoPanel, "置顶", 696, 102, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Top));
            AddButton(autoPanel, "上移", 696, 140, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Up));
            AddButton(autoPanel, "下移", 696, 178, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Down));
            AddButton(autoPanel, "置底", 696, 216, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Bottom));

            openingSkillEnabledCheckBox = AddCheckBox(autoPanel, "启用起手技能", 20, 468, 118, false);
            AddLabel(autoPanel, "起手技能", 148, 468, 70, 24, _textGreen, FontStyle.Bold);
            openingSkillCombo = AddCombo(autoPanel, 220, 466, 260, 28);
            openingSkillCombo.Name = "openingSkillCombo";
            PopulateOpeningSkillCombo(openingSkillCombo, 0, string.Empty);
            openingSkillKeyButton = AddButton(autoPanel, "选择按键", 492, 465, 104, 30);
            openingSkillKeyButton.Name = "openingSkillKeyButton";
            openingSkillKeyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(openingSkillKeyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    openingSkillKeyButton.Tag = selectedKey;
                    openingSkillKeyButton.Text = FormatSkillKey(selectedKey);
                }
            };

            AddLabel(manualPanel, "手动分类 / 手动Mapping", 8, 6, 160, 24, _textGreen, FontStyle.Bold);

            var mappingRows = CreateManualSkillMappingList(manualPanel);

            AddButton(manualPanel, "新增Mapping", 176, 0, 116, 30, (_, _) => AddManualSkillMapping(mappingRows));
            AddButton(manualPanel, "清空", 300, 0, 62, 30, (_, _) => mappingRows.Controls.Clear());

            AddLabel(systemPanel, "系统分类", 8, 6, 120, 24, _textGreen, FontStyle.Bold);
            AddLabel(systemPanel, "系统执行顺序", 378, 6, 140, 24, _textGreen, FontStyle.Bold);

            var systemTree = CreateSkillTree(systemPanel, "systemSkillTree", 8, 34, 260, 260);
            systemSkillTree = systemTree;
            PopulateSystemSkillTree(systemTree);
            var systemSelectedTree = CreateSkillTree(systemPanel, "systemSelectedSkillTree", 378, 34, 300, 260);
            systemSelectedSkillTree = systemSelectedTree;
            PopulateSelectedSkillTree(systemSelectedTree);

            AddButton(systemPanel, "添加 >", 288, 102, 70, 30, (_, _) => AddSystemSkillSelection(systemTree, systemSelectedTree));
            AddButton(systemPanel, "< 移除", 288, 140, 70, 30, (_, _) => RemoveSelectedSkill(systemSelectedTree));
            AddButton(systemPanel, "全部 >>", 288, 178, 70, 30, (_, _) => AddAllSystemSkills(systemTree, systemSelectedTree));
            AddButton(systemPanel, "清空", 288, 216, 70, 30, (_, _) => systemSelectedTree.Nodes.Clear());

            AddButton(systemPanel, "置顶", 696, 102, 70, 30, (_, _) => MoveSelectedSkill(systemSelectedTree, SkillMove.Top));
            AddButton(systemPanel, "上移", 696, 140, 70, 30, (_, _) => MoveSelectedSkill(systemSelectedTree, SkillMove.Up));
            AddButton(systemPanel, "下移", 696, 178, 70, 30, (_, _) => MoveSelectedSkill(systemSelectedTree, SkillMove.Down));
            AddButton(systemPanel, "置底", 696, 216, 70, 30, (_, _) => MoveSelectedSkill(systemSelectedTree, SkillMove.Bottom));

            autoMode.CheckedChanged += (_, _) =>
            {
                if (autoMode.Checked)
                {
                    ShowSkillMode(SkillConfigurationMode.Auto);
                    if (availableTree is not null && currentManualSkills.Count > 0)
                    {
                        PopulateAvailableSkillTreeFromSkills(availableTree, currentManualSkills);
                    }
                }
            };

            manualMode.CheckedChanged += (_, _) =>
            {
                if (manualMode.Checked)
                {
                    ShowSkillMode(SkillConfigurationMode.ManualMapping);
                }
            };

            systemMode.CheckedChanged += (_, _) =>
            {
                if (systemMode.Checked)
                {
                    ShowSkillMode(SkillConfigurationMode.SystemClassification);
                    if (systemTree is not null && currentManualSkills.Count > 0)
                    {
                        PopulateSystemSkillTreeFromSkills(systemTree, currentManualSkills);
                    }
                }
            };

            RefreshSpiritmasterAutoSkillCheckBoxState();
            return tab;
        }

        private Form CreateSpiritmasterSettingsDialog()
        {
            var dialog = new Form
            {
                AutoScaleDimensions = new SizeF(7F, 17F),
                AutoScaleMode = AutoScaleMode.Font,
                BackColor = Color.FromArgb(248, 253, 250),
                ClientSize = new Size(860, 620),
                Font = new Font("Microsoft YaHei UI", 9F),
                MinimumSize = new Size(720, 420),
                Name = "SpiritmasterSettingsForm",
                ShowIcon = false,
                StartPosition = FormStartPosition.CenterParent,
                Text = "精灵设置 - " + _account
            };

            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(0, 590);
            dialog.Controls.Add(page);
            spiritmasterRuleLists.Clear();
            spiritmasterDotRuleList = null;
            spiritmasterSummonRuleList = null;
            spiritmasterOpeningAttackKeyButton = null;
            spiritmasterPetHpRuleList = null;
            spiritmasterPetBuffRuleList = null;

            AddLabel(page, "精灵专用设置", 4, 16, 130, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "保存配置", 620, 10, 110, 30, SaveSettingsButton_Click);
            AddButton(page, "关闭", 740, 10, 96, 30, (_, _) => dialog.Close());

            spiritmasterDotRuleList = CreateSpiritmasterRuleSection(page, "DOT技能设置", "新增DOT", 54, 96, 100, 92, list => AddSpiritmasterDotRuleRow(list));

            spiritmasterSummonRuleList = CreateSpiritmasterFixedRuleSection(page, "召唤宝宝技能设置", 158, 135, 100);

            spiritmasterPetHpRuleList = CreateSpiritmasterRuleSection(page, "精灵宝宝血量维护技能设置", "新增宝宝维护", 292, 190, 196, 116, list => AddSpiritmasterPetHpRuleRow(list));

            spiritmasterPetBuffRuleList = CreateSpiritmasterRuleSection(page, "宝宝Buff技能设置", "新增宝宝Buff", 396, 135, 142, 116, list => AddSpiritmasterPetBuffRuleRow(list));
            PopulateSpiritmasterRuleLists(currentSpiritmasterSettings);

            return dialog;
        }

        private TabPage CreateFilterTab()
        {
            var tab = CreateBaseTab("过滤");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "怪物过滤", 4, 16, 90, 24, _textGreen, FontStyle.Bold);
            var refreshMonstersButton = AddButton(page, "刷新当前怪物", 96, 12, 132, 30);
            refreshMonstersButton.Click += async (_, _) =>
                await RefreshCurrentMonstersAsync(refreshMonstersButton).ConfigureAwait(true);

            AddLabel(page, "当前怪物", 4, 58, 100, 24, _textGreen, FontStyle.Bold);
            activeMonsterFilterCombo = AddCombo(page, 4, 84, 280, 30);
            activeMonsterFilterCombo.Name = "activeMonsterFilterCombo";
            AddButton(page, "添加 >", 302, 84, 80, 30, (_, _) => AddSelectedActiveMonsterFilter());

            activeMonsterFilterStatusLabel = AddLabel(page, "等待刷新", 4, 124, 360, 24);

            AddLabel(page, "已过滤怪物", 404, 58, 120, 24, _textGreen, FontStyle.Bold);
            activeMonsterFilterListBox = CreateFilterListBox(page, 404, 84, 260, 420);
            AddButton(page, "移除", 684, 84, 80, 30, (_, _) => RemoveSelectedActiveMonsterFilter());
            AddButton(page, "清空", 684, 122, 80, 30, (_, _) => ClearActiveMonsterFilterList());

            return tab;
        }

        private ListBox CreateFilterListBox(Control parent, int x, int y, int width, int height)
        {
            var listBox = new ListBox
            {
                BackColor = _inputBackground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                IntegralHeight = false,
                Location = new Point(x, y),
                SelectionMode = SelectionMode.MultiExtended,
                Size = new Size(width, height)
            };

            parent.Controls.Add(listBox);
            return listBox;
        }

        private async Task RefreshCurrentMonstersAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "刷新中...";

            try
            {
                var result = await _runtime.RefreshWorldObjectsAsync(_account).ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    SetActiveMonsterFilterStatus(result.Error ?? "读取当前怪物失败", true);
                    return;
                }

                var count = PopulateActiveMonsterFilterCombo(result.Value);
                SetActiveMonsterFilterStatus("已刷新 " + count.ToString(CultureInfo.InvariantCulture) + " 个怪物", false);
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

        private int PopulateActiveMonsterFilterCombo(IEnumerable<WorldObjectSnapshot> objects)
        {
            if (activeMonsterFilterCombo is null)
            {
                return 0;
            }

            var previousName = GetSelectedActiveMonsterFilterName();
            var items = objects
                .Where(IsMonsterFilterCandidate)
                .GroupBy(target => target.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new MonsterFilterComboItem(group.Key))
                .OrderBy(item => item.Name, StringComparer.CurrentCulture)
                .ToArray();

            activeMonsterFilterCombo.Items.Clear();
            foreach (var item in items)
            {
                activeMonsterFilterCombo.Items.Add(item);
            }

            if (items.Length == 0)
            {
                activeMonsterFilterCombo.Text = string.Empty;
                return 0;
            }

            var selectedIndex = Array.FindIndex(
                items,
                item => string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase));
            activeMonsterFilterCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            return items.Length;
        }

        private static bool IsMonsterFilterCandidate(WorldObjectSnapshot target)
        {
            return string.Equals(target.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase) &&
                   target.IsAlive &&
                   !string.IsNullOrWhiteSpace(target.Name);
        }

        private string GetSelectedActiveMonsterFilterName()
        {
            if (activeMonsterFilterCombo is null || activeMonsterFilterCombo.SelectedIndex < 0)
            {
                return string.Empty;
            }

            return activeMonsterFilterCombo.Items[activeMonsterFilterCombo.SelectedIndex] is MonsterFilterComboItem item
                ? item.Name
                : activeMonsterFilterCombo.Text.Trim();
        }

        private void AddSelectedActiveMonsterFilter()
        {
            AddActiveMonsterFilterName(GetSelectedActiveMonsterFilterName());
        }

        private void AddActiveMonsterFilterName(string? name)
        {
            if (activeMonsterFilterListBox is null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var trimmed = name.Trim();
            foreach (var existing in activeMonsterFilterListBox.Items.Cast<object>())
            {
                if (string.Equals(Convert.ToString(existing), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    activeMonsterFilterListBox.SelectedItem = existing;
                    return;
                }
            }

            var index = activeMonsterFilterListBox.Items.Add(trimmed);
            activeMonsterFilterListBox.SelectedIndex = index;
            SetActiveMonsterFilterStatus("已添加 " + trimmed, false);
        }

        private void RemoveSelectedActiveMonsterFilter()
        {
            if (activeMonsterFilterListBox is null || activeMonsterFilterListBox.SelectedItems.Count == 0)
            {
                return;
            }

            var selected = activeMonsterFilterListBox.SelectedItems.Cast<object>().ToArray();
            foreach (var item in selected)
            {
                activeMonsterFilterListBox.Items.Remove(item);
            }
        }

        private void ClearActiveMonsterFilterList()
        {
            activeMonsterFilterListBox?.Items.Clear();
        }

        private void PopulateActiveMonsterFilterList(IEnumerable<string>? filters)
        {
            if (activeMonsterFilterListBox is null)
            {
                return;
            }

            activeMonsterFilterListBox.Items.Clear();
            foreach (var filter in filters?
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
            {
                activeMonsterFilterListBox.Items.Add(filter);
            }
        }

        private List<string> CaptureActiveMonsterFilterList()
        {
            return activeMonsterFilterListBox is null
                ? new List<string>()
                : activeMonsterFilterListBox.Items
                    .Cast<object>()
                    .Select(item => Convert.ToString(item)?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private void SetActiveMonsterFilterStatus(string text, bool isError)
        {
            if (activeMonsterFilterStatusLabel is null)
            {
                return;
            }

            activeMonsterFilterStatusLabel.ForeColor = isError ? Color.FromArgb(166, 40, 40) : _textGreen;
            activeMonsterFilterStatusLabel.Text = text;
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

        private Label AddLabel(Control parent, string text, int x, int y, int width, int height, Color? foreColor = null, FontStyle style = FontStyle.Regular)
        {
            var label = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, style),
                ForeColor = foreColor ?? _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };

            parent.Controls.Add(label);
            return label;
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

        private FlowLayoutPanel CreateSpiritmasterRuleSection(
            Control parent,
            string title,
            string addButtonText,
            int y,
            int titleWidth,
            int addButtonX,
            int addButtonWidth,
            Action<FlowLayoutPanel> addRow)
        {
            AddLabel(parent, title, 4, y + 4, titleWidth, 24, _textGreen, FontStyle.Bold);
            var list = CreateMaintenanceRuleList(parent, 4, y + 38, 830, 66);
            spiritmasterRuleLists.Add(list);
            AddButton(parent, addButtonText, addButtonX, y, addButtonWidth, 30, (_, _) => addRow(list));
            return list;
        }

        private FlowLayoutPanel CreateSpiritmasterFixedRuleSection(
            Control parent,
            string title,
            int y,
            int titleWidth,
            int listHeight = 66)
        {
            AddLabel(parent, title, 4, y + 4, titleWidth, 24, _textGreen, FontStyle.Bold);
            var list = CreateMaintenanceRuleList(parent, 4, y + 38, 830, listHeight);
            spiritmasterRuleLists.Add(list);
            return list;
        }

        private void PopulateSpiritmasterRuleLists(SpiritmasterSkillSettings? settings)
        {
            var spiritmaster = settings ?? new SpiritmasterSkillSettings();

            if (spiritmasterDotRuleList is not null)
            {
                spiritmasterDotRuleList.Controls.Clear();
                foreach (var rule in spiritmaster.DotSkills ?? new List<SpiritmasterSkillRefConfig>())
                {
                    AddSpiritmasterDotRuleRow(spiritmasterDotRuleList, rule.SkillId, rule.SkillName);
                }

                if (!spiritmasterDotRuleList.Controls.OfType<Panel>().Any())
                {
                    AddSpiritmasterDotRuleRow(spiritmasterDotRuleList);
                }
            }

            if (spiritmasterSummonRuleList is not null)
            {
                spiritmasterSummonRuleList.Controls.Clear();
                var summonRules = (spiritmaster.SummonSkills ?? new List<SpiritmasterSkillKeyRuleConfig>())
                    .Take(2)
                    .ToArray();
                AddSpiritmasterSummonButtonRow(
                    spiritmasterSummonRuleList,
                    1,
                    summonRules.Length > 0 ? summonRules[0].Key : string.Empty);
                AddSpiritmasterSummonButtonRow(
                    spiritmasterSummonRuleList,
                    2,
                    summonRules.Length > 1 ? summonRules[1].Key : string.Empty);
                AddSpiritmasterOpeningAttackKeyRow(spiritmasterSummonRuleList, spiritmaster.OpeningAttackKey);
            }

            if (spiritmasterPetHpRuleList is not null)
            {
                spiritmasterPetHpRuleList.Controls.Clear();
                foreach (var rule in spiritmaster.PetHpMaintenanceRules ?? new List<SpiritmasterPetHpRuleConfig>())
                {
                    AddSpiritmasterPetHpRuleRow(spiritmasterPetHpRuleList, rule.BelowPercent, rule.SkillId, rule.SkillName, rule.Key, rule.CooldownMs);
                }

                if (!spiritmasterPetHpRuleList.Controls.OfType<Panel>().Any())
                {
                    AddSpiritmasterPetHpRuleRow(spiritmasterPetHpRuleList);
                }
            }

            if (spiritmasterPetBuffRuleList is not null)
            {
                spiritmasterPetBuffRuleList.Controls.Clear();
                foreach (var rule in spiritmaster.PetBuffRules ?? new List<SpiritmasterPetBuffRuleConfig>())
                {
                    AddSpiritmasterPetBuffRuleRow(spiritmasterPetBuffRuleList, rule.SkillId, rule.SkillName, rule.Key);
                }

                if (!spiritmasterPetBuffRuleList.Controls.OfType<Panel>().Any())
                {
                    AddSpiritmasterPetBuffRuleRow(spiritmasterPetBuffRuleList);
                }
            }
        }

        private SpiritmasterSkillSettings CaptureSpiritmasterSettings()
        {
            if (spiritmasterDotRuleList is null &&
                spiritmasterSummonRuleList is null &&
                spiritmasterPetHpRuleList is null &&
                spiritmasterPetBuffRuleList is null)
            {
                return currentSpiritmasterSettings.Clone();
            }

            currentSpiritmasterSettings = new SpiritmasterSkillSettings
            {
                DotSkills = CaptureSpiritmasterSkillRefs(spiritmasterDotRuleList),
                SummonSkills = CaptureSpiritmasterSkillKeyRules(spiritmasterSummonRuleList),
                SummonKeyIntervalMs = 2000,
                OpeningAttackKey = CaptureSpiritmasterKey(spiritmasterOpeningAttackKeyButton),
                PetHpMaintenanceRules = CaptureSpiritmasterPetHpRules(spiritmasterPetHpRuleList),
                PetBuffRules = CaptureSpiritmasterPetBuffRules(spiritmasterPetBuffRuleList)
            };

            return currentSpiritmasterSettings.Clone();
        }

        private static List<SpiritmasterSkillRefConfig> CaptureSpiritmasterSkillRefs(FlowLayoutPanel? list)
        {
            var rules = new List<SpiritmasterSkillRefConfig>();
            foreach (var row in EnumerateSpiritmasterRows(list))
            {
                var selectedSkill = GetSelectedMaintenanceSkill(FindSpiritmasterSkillCombo(row));
                if (!HasSpiritmasterSkillSelection(selectedSkill))
                {
                    continue;
                }

                rules.Add(new SpiritmasterSkillRefConfig
                {
                    SkillId = selectedSkill.SkillId,
                    SkillName = selectedSkill.SkillName
                });
            }

            return rules;
        }

        private static List<SpiritmasterSkillKeyRuleConfig> CaptureSpiritmasterSkillKeyRules(FlowLayoutPanel? list)
        {
            var rules = new List<SpiritmasterSkillKeyRuleConfig>();
            foreach (var row in EnumerateSpiritmasterRows(list))
            {
                var selectedSkill = GetSelectedMaintenanceSkill(FindSpiritmasterSkillCombo(row));
                var key = FindSpiritmasterKeyButton(row)?.Tag as string ?? string.Empty;
                if (!HasSpiritmasterSkillSelection(selectedSkill) && string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                rules.Add(new SpiritmasterSkillKeyRuleConfig
                {
                    SkillId = selectedSkill.SkillId,
                    SkillName = selectedSkill.SkillName,
                    Key = key
                });
            }

            return rules;
        }

        private static List<SpiritmasterPetHpRuleConfig> CaptureSpiritmasterPetHpRules(FlowLayoutPanel? list)
        {
            var rules = new List<SpiritmasterPetHpRuleConfig>();
            foreach (var row in EnumerateSpiritmasterRows(list))
            {
                var selectedSkill = GetSelectedMaintenanceSkill(FindSpiritmasterSkillCombo(row));
                var key = FindSpiritmasterKeyButton(row)?.Tag as string ?? string.Empty;
                if (!HasSpiritmasterSkillSelection(selectedSkill) && string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                rules.Add(new SpiritmasterPetHpRuleConfig
                {
                    BelowPercent = ReadRowPercent(row, "spiritmasterPetHpBelowTextBox", 68),
                    SkillId = selectedSkill.SkillId,
                    SkillName = selectedSkill.SkillName,
                    Key = key,
                    CooldownMs = Math.Clamp(
                        ReadRowInt(row, "spiritmasterPetHpCooldownTextBox", SpiritmasterPetHpRuleConfig.DefaultCooldownMs),
                        SpiritmasterPetHpRuleConfig.MinCooldownMs,
                        SpiritmasterPetHpRuleConfig.MaxCooldownMs)
                });
            }

            return rules;
        }

        private static List<SpiritmasterPetBuffRuleConfig> CaptureSpiritmasterPetBuffRules(FlowLayoutPanel? list)
        {
            var rules = new List<SpiritmasterPetBuffRuleConfig>();
            foreach (var row in EnumerateSpiritmasterRows(list))
            {
                var selectedSkill = GetSelectedMaintenanceSkill(FindSpiritmasterSkillCombo(row));
                var key = FindSpiritmasterKeyButton(row)?.Tag as string ?? string.Empty;
                if (!HasSpiritmasterSkillSelection(selectedSkill) &&
                    string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                rules.Add(new SpiritmasterPetBuffRuleConfig
                {
                    SkillId = selectedSkill.SkillId,
                    SkillName = selectedSkill.SkillName,
                    Key = key
                });
            }

            return rules;
        }

        private static IEnumerable<Panel> EnumerateSpiritmasterRows(FlowLayoutPanel? list)
        {
            return list?.Controls.OfType<Panel>() ?? Enumerable.Empty<Panel>();
        }

        private static RoundedComboBox? FindSpiritmasterSkillCombo(Panel row)
        {
            return row.Controls
                .OfType<RoundedComboBox>()
                .FirstOrDefault(combo => string.Equals(combo.Name, "spiritmasterRuleSkillCombo", StringComparison.Ordinal));
        }

        private static Button? FindSpiritmasterKeyButton(Panel row)
        {
            return row.Controls
                .OfType<Button>()
                .FirstOrDefault(button => string.Equals(button.Name, "spiritmasterRuleKeyButton", StringComparison.Ordinal));
        }

        private static bool HasSpiritmasterSkillSelection(MaintenanceSkillComboItem selectedSkill)
        {
            return selectedSkill.SkillId != 0 || !string.IsNullOrWhiteSpace(selectedSkill.SkillName);
        }

        private static int ReadRowPercent(Panel row, string textBoxName, int fallback)
        {
            return Math.Clamp(ReadRowInt(row, textBoxName, fallback), 0, 100);
        }

        private static uint ReadRowUInt(Panel row, string textBoxName, uint fallback)
        {
            var text = row.Controls
                .OfType<RoundedTextBox>()
                .FirstOrDefault(textBox => string.Equals(textBox.Name, textBoxName, StringComparison.Ordinal))
                ?.Text;
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static int ReadRowInt(Panel row, string textBoxName, int fallback)
        {
            var text = row.Controls
                .OfType<RoundedTextBox>()
                .FirstOrDefault(textBox => string.Equals(textBox.Name, textBoxName, StringComparison.Ordinal))
                ?.Text;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private Panel CreateSpiritmasterRuleRow(int width = 760)
        {
            return new Panel
            {
                BackColor = _pageBackground,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 0, 2),
                Size = new Size(width, 31)
            };
        }

        private RoundedComboBox AddSpiritmasterSkillCombo(
            Control parent,
            int x,
            int y,
            int width,
            uint skillId = 0,
            string skillName = "")
        {
            var combo = AddCombo(parent, x, y, width, 28);
            combo.Name = "spiritmasterRuleSkillCombo";
            PopulateMaintenanceSkillCombo(combo, skillId, skillName);
            return combo;
        }

        private Button AddSpiritmasterKeyButton(Control parent, int x, int y, string key = "")
        {
            var keyButton = AddButton(parent, "选择按键", x, y, 104, 30);
            keyButton.Name = "spiritmasterRuleKeyButton";
            SetSpiritmasterKeyButton(keyButton, key);

            keyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(keyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    SetSpiritmasterKeyButton(keyButton, selectedKey);
                }
            };

            return keyButton;
        }

        private static string CaptureSpiritmasterKey(Button? keyButton)
        {
            return keyButton?.Tag as string ?? string.Empty;
        }

        private static void SetSpiritmasterKeyButton(Button? keyButton, string? key)
        {
            if (keyButton is null)
            {
                return;
            }

            var normalizedKey = key?.Trim() ?? string.Empty;
            keyButton.Tag = normalizedKey;
            keyButton.Text = string.IsNullOrWhiteSpace(normalizedKey)
                ? "选择按键"
                : FormatSkillKey(normalizedKey);
        }

        private void AddSpiritmasterDeleteButton(FlowLayoutPanel list, Panel row, int x)
        {
            AddButton(row, "删除", x, 0, 58, 30, (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
            });
        }

        private void AddSpiritmasterDotRuleRow(FlowLayoutPanel list, uint skillId = 0, string skillName = "")
        {
            var row = CreateSpiritmasterRuleRow(650);
            AddLabel(row, "技能", 0, 3, 34, 24);
            var skillCombo = AddSpiritmasterSkillCombo(row, 38, 1, 240, skillId, skillName);
            var statusLabel = AddLabel(row, "状态: 自动识别", 288, 3, 120, 24);
            statusLabel.Name = "spiritmasterDotStatusLabel";
            var durationLabel = AddLabel(row, "持续: XML自动", 416, 3, 116, 24);
            durationLabel.Name = "spiritmasterDotDurationLabel";
            skillCombo.SelectedIndexChanged += (_, _) => UpdateSpiritmasterDotRuleInfo(skillCombo, statusLabel, durationLabel);
            UpdateSpiritmasterDotRuleInfo(skillCombo, statusLabel, durationLabel);
            AddSpiritmasterDeleteButton(list, row, 544);
            list.Controls.Add(row);
        }

        private void UpdateSpiritmasterDotRuleInfo(
            RoundedComboBox skillCombo,
            Label statusLabel,
            Label durationLabel)
        {
            statusLabel.Text = "状态: 自动识别";

            var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);
            var skill = FindCurrentSkill(selectedSkill.SkillId, selectedSkill.SkillName);
            durationLabel.Text = "持续: " + FormatSpiritmasterDotDuration(skill, selectedSkill);
        }

        private SkillSnapshot? FindCurrentSkill(uint skillId, string? skillName)
        {
            if (skillId != 0)
            {
                var byId = currentManualSkills.FirstOrDefault(skill => skill.SkillId == skillId);
                if (byId is not null)
                {
                    return byId;
                }
            }

            var normalizedName = skillName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            return currentManualSkills.FirstOrDefault(skill =>
                string.Equals(FormatManualSkillName(skill), normalizedName, StringComparison.Ordinal) ||
                string.Equals(skill.Name, normalizedName, StringComparison.Ordinal) ||
                string.Equals(skill.DisplayBaseName, normalizedName, StringComparison.Ordinal));
        }

        private static string FormatSpiritmasterDotDuration(
            SkillSnapshot? skill,
            MaintenanceSkillComboItem selectedSkill)
        {
            if (selectedSkill.SkillId == 0 && string.IsNullOrWhiteSpace(selectedSkill.SkillName))
            {
                return "XML自动";
            }

            if (skill?.XmlEffectRemainMs is int remainMs && remainMs > 0)
            {
                return "XML " + FormatMillisecondsAsSeconds(remainMs);
            }

            return "XML未知";
        }

        private static string FormatMillisecondsAsSeconds(int milliseconds)
        {
            return milliseconds % 1000 == 0
                ? (milliseconds / 1000).ToString(CultureInfo.InvariantCulture) + "秒"
                : (milliseconds / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "秒";
        }

        private void AddSpiritmasterSummonButtonRow(
            FlowLayoutPanel list,
            int index,
            string key = "")
        {
            var row = CreateSpiritmasterRuleRow(260);
            AddLabel(row, GetSpiritmasterSummonButtonLabel(index), 0, 3, 96, 24);
            AddSpiritmasterKeyButton(row, 106, 0, key);
            list.Controls.Add(row);
        }

        private void AddSpiritmasterOpeningAttackKeyRow(
            FlowLayoutPanel list,
            string key = "")
        {
            var row = CreateSpiritmasterRuleRow(260);
            AddLabel(row, "开怪按键", 0, 3, 96, 24);
            spiritmasterOpeningAttackKeyButton = AddSpiritmasterKeyButton(row, 106, 0, key);
            list.Controls.Add(row);
        }

        private static string GetSpiritmasterSummonButtonLabel(int index)
        {
            return index == 1 ? "提速按键" : "召唤按键";
        }

        private void AddSpiritmasterPetHpRuleRow(
            FlowLayoutPanel list,
            int belowPercent = 68,
            uint skillId = 0,
            string skillName = "",
            string key = "",
            int cooldownMs = SpiritmasterPetHpRuleConfig.DefaultCooldownMs)
        {
            var row = CreateSpiritmasterRuleRow(680);
            AddLabel(row, "低于", 0, 3, 34, 24);
            var thresholdTextBox = AddTextBox(
                row,
                Math.Clamp(belowPercent, 0, 100).ToString(CultureInfo.InvariantCulture),
                36,
                1,
                54,
                28);
            thresholdTextBox.Name = "spiritmasterPetHpBelowTextBox";
            AddLabel(row, "% 按", 94, 3, 42, 24);
            AddSpiritmasterSkillCombo(row, 138, 1, 210, skillId, skillName);
            AddSpiritmasterKeyButton(row, 356, 0, key);
            AddLabel(row, "CD", 468, 3, 22, 24);
            var cooldownTextBox = AddTextBox(
                row,
                Math.Clamp(
                    cooldownMs <= 0 ? SpiritmasterPetHpRuleConfig.DefaultCooldownMs : cooldownMs,
                    SpiritmasterPetHpRuleConfig.MinCooldownMs,
                    SpiritmasterPetHpRuleConfig.MaxCooldownMs).ToString(CultureInfo.InvariantCulture),
                494,
                1,
                70,
                28);
            cooldownTextBox.Name = "spiritmasterPetHpCooldownTextBox";
            AddLabel(row, "ms", 568, 3, 22, 24);
            AddSpiritmasterDeleteButton(list, row, 608);
            list.Controls.Add(row);
        }

        private void AddSpiritmasterPetBuffRuleRow(
            FlowLayoutPanel list,
            uint skillId = 0,
            string skillName = "",
            string key = "")
        {
            var row = CreateSpiritmasterRuleRow(650);
            AddLabel(row, "缺少Buff", 0, 3, 60, 24);
            AddLabel(row, "状态: 自动识别", 66, 3, 118, 24);
            AddLabel(row, "按", 190, 3, 24, 24);
            AddSpiritmasterSkillCombo(row, 214, 1, 210, skillId, skillName);
            AddSpiritmasterKeyButton(row, 432, 0, key);
            AddSpiritmasterDeleteButton(list, row, 544);
            list.Controls.Add(row);
        }

        private void ShowSkillMode(SkillConfigurationMode mode)
        {
            if (autoSkillPanel is not null)
            {
                autoSkillPanel.Visible = mode == SkillConfigurationMode.Auto;
            }

            if (manualSkillPanel is not null)
            {
                manualSkillPanel.Visible = mode == SkillConfigurationMode.ManualMapping;
            }

            if (systemSkillPanel is not null)
            {
                systemSkillPanel.Visible = mode == SkillConfigurationMode.SystemClassification;
            }

            RefreshSpiritmasterAutoSkillCheckBoxState();
        }

        private void ShowSpiritmasterSettingsDialog()
        {
            if (spiritmasterAutoSkillCheckBox?.Checked != true)
            {
                return;
            }

            if (spiritmasterSettingsDialog is { IsDisposed: false })
            {
                spiritmasterSettingsDialog.Activate();
                return;
            }

            var dialog = CreateSpiritmasterSettingsDialog();
            spiritmasterSettingsDialog = dialog;
            dialog.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(spiritmasterSettingsDialog, dialog))
                {
                    spiritmasterSettingsDialog = null;
                    spiritmasterRuleLists.Clear();
                    spiritmasterDotRuleList = null;
                    spiritmasterSummonRuleList = null;
                    spiritmasterOpeningAttackKeyButton = null;
                    spiritmasterPetHpRuleList = null;
                    spiritmasterPetBuffRuleList = null;
                }
            };

            dialog.Show(this);
        }

        private void CloseSpiritmasterSettingsDialog()
        {
            if (spiritmasterSettingsDialog is null ||
                spiritmasterSettingsDialog.IsDisposed)
            {
                return;
            }

            spiritmasterSettingsDialog.Close();
        }

        private void RefreshSpiritmasterAutoSkillCheckBoxState()
        {
            if (spiritmasterAutoSkillCheckBox is null)
            {
                return;
            }

            var enabled = skillAutoModeRadio?.Checked == true;
            spiritmasterAutoSkillCheckBox.Enabled = enabled;
            spiritmasterAutoSkillCheckBox.ForeColor = enabled ? _textGreen : Color.FromArgb(107, 114, 128);
            spiritmasterAutoSkillCheckBox.Cursor = enabled ? Cursors.Hand : Cursors.Default;

            var showSettingsButton = enabled && spiritmasterAutoSkillCheckBox.Checked;
            if (spiritmasterSettingsButton is not null)
            {
                spiritmasterSettingsButton.Visible = showSettingsButton;
            }

            if (!showSettingsButton)
            {
                CloseSpiritmasterSettingsDialog();
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

        private void PopulateSystemSkillTree(TreeView tree)
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
                            visibleSkills.Where(skill => MatchesManualSkillType(skill, category)));
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

        private void PopulateSystemSkillTreeFromSkills(TreeView tree, IReadOnlyList<SkillSnapshot> skills)
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                var visibleSkills = skills
                    .Where(skill => !ShouldHideManualSkillCandidate(skill))
                    .ToArray();

                AddSystemSkillDimension(tree, "施放类型", visibleSkills, skill => FormatSystemValue(skill.XmlActivation));
                AddSystemSkillDimension(tree, "XML分类", visibleSkills, skill => FormatSystemValue(skill.XmlSkillCategory));
                AddSystemSkillDimension(tree, "攻击属性", visibleSkills, skill => FormatSystemValue(skill.XmlSkillType));
                AddSystemSkillDimension(tree, "用途", visibleSkills, skill => FormatSystemValue(skill.XmlSubType));
                AddSystemSkillDimension(tree, "目标槽位", visibleSkills, skill => FormatSystemValue(skill.XmlTargetSlot));
                AddSystemSkillDimension(tree, "可驱散", visibleSkills, skill => FormatSystemValue(skill.XmlDispelCategory));
                AddSystemSkillDimension(tree, "首目标", visibleSkills, skill => FormatSystemValue(skill.XmlFirstTarget));
                AddSystemSkillDimension(tree, "目标关系", visibleSkills, skill => FormatSystemValue(skill.XmlTargetRelationRestriction));
                AddSystemSkillDimension(tree, "目标范围", visibleSkills, skill => FormatSystemValue(skill.XmlTargetRange));
                AddSystemSkillDimension(tree, "效果机制", visibleSkills, GetSystemEffectCategory);

                tree.ExpandAll();
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private static void AddSystemSkillDimension(
            TreeView tree,
            string dimensionName,
            IReadOnlyList<SkillSnapshot> skills,
            Func<SkillSnapshot, string> selector)
        {
            var dimensionNode = tree.Nodes.Add(dimensionName, dimensionName);
            foreach (var group in skills
                         .GroupBy(selector, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.CurrentCulture))
            {
                var groupNode = dimensionNode.Nodes.Add(group.Key, group.Key);
                AddSystemSkillLeaves(groupNode, group);
            }

            if (dimensionNode.Nodes.Count == 0)
            {
                tree.Nodes.Remove(dimensionNode);
            }
        }

        private static void AddSystemSkillLeaves(TreeNode parentNode, IEnumerable<SkillSnapshot> skills)
        {
            foreach (var skill in skills
                         .GroupBy(GetSkillKey, StringComparer.Ordinal)
                         .Select(group => group.First())
                         .OrderBy(FormatManualSkillName, StringComparer.CurrentCulture))
            {
                var name = FormatManualSkillName(skill);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var nodeText = name + " #" + skill.SkillId;
                var node = parentNode.Nodes.Add(nodeText, nodeText);
                node.Tag = CreateSystemSkillTreeNodeData(skill);
            }
        }

        private static SkillTreeNodeData CreateSystemSkillTreeNodeData(SkillSnapshot skill)
        {
            return new SkillTreeNodeData(
                skill.SkillId,
                FormatManualSkillName(skill),
                GetSkillBaseName(skill),
                GetSystemExecutionCategory(skill),
                ParseNullableInt(skill.XmlChainTime));
        }

        private static string FormatSystemValue(string? value)
        {
            return HasUsefulSkillValue(value) ? value!.Trim() : "未标记";
        }

        private static string GetSystemEffectCategory(SkillSnapshot skill)
        {
            if (IsSystemDamageOverTimeSkill(skill))
            {
                return "持续伤害";
            }

            if (HasSystemEffect(skill, "Heal") || HasSystemEffect(skill, "Heal_Instant"))
            {
                return "治疗";
            }

            if (HasSystemEffect(skill, "StatUp") || HasSystemEffect(skill, "Shield") || HasSystemEffect(skill, "Reflector"))
            {
                return "增益";
            }

            if (HasSystemEffect(skill, "StatDown") || HasSystemEffect(skill, "Slow") || HasSystemEffect(skill, "Snare") ||
                HasSystemEffect(skill, "Root") || HasSystemEffect(skill, "Stun") || HasSystemEffect(skill, "Sleep") ||
                HasSystemEffect(skill, "Silence") || HasSystemEffect(skill, "Fear") || HasSystemEffect(skill, "Blind") ||
                HasSystemEffect(skill, "Paralyze"))
            {
                return "控制/减益";
            }

            if (HasSystemEffect(skill, "SkillATK_Instant") || HasSystemEffect(skill, "SpellATK_Instant") ||
                HasSystemEffect(skill, "SkillATK") || HasSystemEffect(skill, "SpellATK"))
            {
                return "直接伤害";
            }

            if (HasSystemEffect(skill, "Summon") || HasSystemEffect(skill, "SummonTrap"))
            {
                return "召唤/陷阱";
            }

            return "其他";
        }

        private static string GetSystemExecutionCategory(SkillSnapshot skill)
        {
            if (IsNamedSkill(GetSkillBaseName(skill), DpSkillBaseNames) ||
                HasSkillTag(skill, "dp") ||
                HasUsefulSkillValue(skill.XmlCostDp))
            {
                return "DP技能";
            }

            if (IsNamedSkill(GetSkillBaseName(skill), TriggerSkillBaseNames) ||
                HasSkillTag(skill, "counter") ||
                HasUsefulSkillValue(skill.XmlCounterSkill))
            {
                return "触发技能";
            }

            if (IsSystemDamageOverTimeSkill(skill))
            {
                return "持续伤害";
            }

            if (HasSystemEffect(skill, "Heal") || HasSystemEffect(skill, "Heal_Instant"))
            {
                return "治疗技能";
            }

            if (string.Equals(skill.XmlTargetSlot, "buff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skill.XmlSubType, "Buff", StringComparison.OrdinalIgnoreCase))
            {
                return "增益技能";
            }

            if (string.Equals(skill.XmlTargetSlot, "Debuff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skill.XmlSubType, "Debuff", StringComparison.OrdinalIgnoreCase))
            {
                return "减益技能";
            }

            if (HasUsefulSkillValue(skill.XmlPrechainCategory) || HasUsefulSkillValue(skill.XmlChainTime))
            {
                return "连续技";
            }

            if (string.Equals(skill.XmlActivation, "Toggle", StringComparison.OrdinalIgnoreCase))
            {
                return "激活技能";
            }

            return "主动技能";
        }

        private static bool IsSystemDamageOverTimeSkill(SkillSnapshot skill)
        {
            return (string.Equals(skill.XmlTargetSlot, "Debuff", StringComparison.OrdinalIgnoreCase) ||
                    (skill.XmlDispelCategory?.StartsWith("Debuff", StringComparison.OrdinalIgnoreCase) ?? false)) &&
                   (skill.XmlEffectRemainMs.GetValueOrDefault() > 0 || skill.XmlEffectCheckTimeMs.GetValueOrDefault() > 0) &&
                   (HasSystemEffect(skill, "Poison") ||
                    HasSystemEffect(skill, "Bleed") ||
                    HasSystemEffect(skill, "SpellATK") ||
                    HasSystemEffect(skill, "SkillATK"));
        }

        private static bool HasSystemEffect(SkillSnapshot skill, string effect)
        {
            if (string.IsNullOrWhiteSpace(skill.XmlEffects))
            {
                return false;
            }

            return skill.XmlEffects
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(value, effect, StringComparison.OrdinalIgnoreCase));
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

        private sealed class MonsterFilterComboItem
        {
            public MonsterFilterComboItem(string name)
            {
                Name = name.Trim();
            }

            public string Name { get; }

            public override string ToString()
            {
                return Name;
            }
        }

        private sealed class MaintenanceSkillComboItem
        {
            public static readonly MaintenanceSkillComboItem Empty = new(0, string.Empty, "选择技能");

            public MaintenanceSkillComboItem(uint skillId, string skillName)
                : this(
                    skillId,
                    skillName,
                    string.IsNullOrWhiteSpace(skillName)
                        ? (skillId == 0 ? "选择技能" : "Skill " + skillId)
                        : skillName.Trim() + (skillId == 0 ? string.Empty : " #" + skillId.ToString(CultureInfo.InvariantCulture)))
            {
            }

            private MaintenanceSkillComboItem(uint skillId, string skillName, string displayText)
            {
                SkillId = skillId;
                SkillName = skillName?.Trim() ?? string.Empty;
                DisplayText = displayText;
            }

            public uint SkillId { get; }

            public string SkillName { get; }

            private string DisplayText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private sealed class MaintenanceTimingComboItem
        {
            public MaintenanceTimingComboItem(MaintenanceRuleRunTiming runTiming, string displayText)
            {
                RunTiming = runTiming;
                DisplayText = displayText;
            }

            public MaintenanceRuleRunTiming RunTiming { get; }

            private string DisplayText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private sealed class OpeningSkillComboItem
        {
            public static readonly OpeningSkillComboItem Empty = new(0, string.Empty, "选择技能");

            public OpeningSkillComboItem(uint skillId, string skillName)
                : this(
                    skillId,
                    skillName,
                    string.IsNullOrWhiteSpace(skillName)
                        ? (skillId == 0 ? "选择技能" : "Skill " + skillId)
                        : skillName.Trim() + (skillId == 0 ? string.Empty : " #" + skillId.ToString(CultureInfo.InvariantCulture)))
            {
            }

            private OpeningSkillComboItem(uint skillId, string skillName, string displayText)
            {
                SkillId = skillId;
                SkillName = skillName?.Trim() ?? string.Empty;
                DisplayText = displayText;
            }

            public uint SkillId { get; }

            public string SkillName { get; }

            private string DisplayText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

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

        private static void AddSystemSkillSelection(TreeView source, TreeView target)
        {
            if (source.SelectedNode is null)
            {
                return;
            }

            AddSystemSkillNode(target, source.SelectedNode);
        }

        private static void AddAllSystemSkills(TreeView source, TreeView target)
        {
            foreach (TreeNode node in source.Nodes)
            {
                AddSystemSkillNode(target, node);
            }
        }

        private static void AddSystemSkillNode(TreeView target, TreeNode sourceNode)
        {
            TreeNode? selectedNode = null;
            foreach (var leaf in EnumerateSkillLeafNodes(sourceNode))
            {
                selectedNode = AddSystemSkillLeafIfMissing(target.Nodes, leaf);
            }

            target.ExpandAll();
            if (selectedNode is not null)
            {
                target.SelectedNode = selectedNode;
            }
        }

        private static IEnumerable<TreeNode> EnumerateSkillLeafNodes(TreeNode node)
        {
            if (node.Tag is SkillTreeNodeData)
            {
                yield return node;
            }

            foreach (TreeNode child in node.Nodes)
            {
                foreach (var leaf in EnumerateSkillLeafNodes(child))
                {
                    yield return leaf;
                }
            }
        }

        private static TreeNode AddSystemSkillLeafIfMissing(TreeNodeCollection targetNodes, TreeNode sourceNode)
        {
            var sourceData = sourceNode.Tag as SkillTreeNodeData;
            var targetNode = FindDirectSystemSkillNode(targetNodes, sourceData, sourceNode.Text);
            if (targetNode is null)
            {
                var text = sourceData?.Name ?? sourceNode.Text;
                targetNode = targetNodes.Add(text, text);
                targetNode.Tag = sourceNode.Tag;
            }

            return targetNode;
        }

        private static TreeNode? FindDirectSystemSkillNode(
            TreeNodeCollection nodes,
            SkillTreeNodeData? sourceData,
            string fallbackText)
        {
            foreach (TreeNode node in nodes)
            {
                var data = node.Tag as SkillTreeNodeData;
                if (sourceData is not null &&
                    data is not null &&
                    sourceData.SkillId != 0 &&
                    data.SkillId == sourceData.SkillId)
                {
                    return node;
                }

                if (string.Equals(node.Text, sourceData?.Name ?? fallbackText, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
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
                Size = new Size(610, 31),
                Tag = skillName
            };

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = _textGreen,
                Location = new Point(0, 3),
                Size = new Size(34, 24),
                Text = "分类",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var typeCombo = AddCombo(row, 40, 1, 118, 28, ManualSkillCategories);
            typeCombo.Name = "manualSkillTypeCombo";
            if (!string.IsNullOrWhiteSpace(skillType) && typeCombo.Items.Contains(skillType))
            {
                typeCombo.Text = skillType;
            }

            var skillCombo = AddCombo(row, 166, 1, 220, 28);
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
                Location = new Point(394, 3),
                Size = new Size(34, 24),
                Text = "按键",
                TextAlign = ContentAlignment.MiddleCenter
            });

            var keyButton = AddButton(row, "选择按键", 434, 0, 104, 30);
            keyButton.Name = "manualSkillKeyButton";
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = key;
                keyButton.Text = FormatSkillKey(key);
            }

            AddButton(row, "删除", 546, 0, 58, 30);

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

        private async Task RefreshCurrentSkillsAsync(Button button, TreeView? availableTree, TreeView? systemTree)
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
                if (availableTree is not null && skillAutoModeRadio?.Checked == true)
                {
                    PopulateAvailableSkillTreeFromSkills(availableTree, currentManualSkills);
                }

                if (systemTree is not null && skillSystemModeRadio?.Checked == true)
                {
                    PopulateSystemSkillTreeFromSkills(systemTree, currentManualSkills);
                }

                RefreshManualSkillMappingCombos();
                RefreshMaintenanceSkillCombos();
                RefreshSpiritmasterSkillCombos();
                RefreshOpeningSkillCombo();
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

        private async Task RefreshSelectedSkillTreeAsync(Button button, TreeView selectedTree)
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
                        result.Error ?? "刷新当前已选技能失败。",
                        "刷新当前已选技能",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                currentManualSkills = result.Value;
                var refreshResult = RefreshSelectedSkillTreeToHighestCurrentSkills(selectedTree, currentManualSkills);
                RefreshManualSkillMappingCombos();
                RefreshMaintenanceSkillCombos();
                RefreshSpiritmasterSkillCombos();
                RefreshOpeningSkillCombo();

                button.Text = "已刷新 " + refreshResult.UpdatedCount + " 删除 " + refreshResult.DeletedCount;
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

        private static (int UpdatedCount, int DeletedCount) RefreshSelectedSkillTreeToHighestCurrentSkills(
            TreeView selectedTree,
            IReadOnlyList<SkillSnapshot> currentSkills)
        {
            var candidates = currentSkills
                .Where(skill => !ShouldHideManualSkillCandidate(skill))
                .GroupBy(skill => NormalizeSkillBaseName(GetSkillBaseName(skill)), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(GetSkillRank).First(),
                    StringComparer.Ordinal);

            var updatedCount = 0;
            var deletedCount = 0;
            selectedTree.BeginUpdate();
            try
            {
                for (var i = selectedTree.Nodes.Count - 1; i >= 0; i--)
                {
                    var nodeResult = RefreshSelectedSkillNodeToHighestCurrentSkill(selectedTree.Nodes[i], candidates);
                    updatedCount += nodeResult.UpdatedCount;
                    deletedCount += nodeResult.DeletedCount;
                }

                selectedTree.ExpandAll();
            }
            finally
            {
                selectedTree.EndUpdate();
            }

            return (updatedCount, deletedCount);
        }

        private static (int UpdatedCount, int DeletedCount) RefreshSelectedSkillNodeToHighestCurrentSkill(
            TreeNode node,
            IReadOnlyDictionary<string, SkillSnapshot> candidates)
        {
            var updatedCount = 0;
            var deletedCount = 0;
            var data = node.Tag as SkillTreeNodeData;
            var key = NormalizeSkillBaseName(
                !string.IsNullOrWhiteSpace(data?.BaseName)
                    ? data.BaseName
                    : data?.Name ?? node.Text);

            if (string.IsNullOrWhiteSpace(key) ||
                !candidates.TryGetValue(key, out var currentSkill))
            {
                var removedCount = CountSkillTreeNodes(node);
                node.Remove();
                return (0, removedCount);
            }

            var currentData = CreateSkillTreeNodeData(currentSkill);
            if (data is null ||
                data.SkillId != currentData.SkillId ||
                !string.Equals(data.Name, currentData.Name, StringComparison.Ordinal) ||
                !string.Equals(node.Text, currentData.Name, StringComparison.Ordinal))
            {
                node.Text = currentData.Name;
                node.Name = currentData.Name;
                node.Tag = currentData;
                updatedCount++;
            }

            for (var i = node.Nodes.Count - 1; i >= 0; i--)
            {
                var childResult = RefreshSelectedSkillNodeToHighestCurrentSkill(node.Nodes[i], candidates);
                updatedCount += childResult.UpdatedCount;
                deletedCount += childResult.DeletedCount;
            }

            return (updatedCount, deletedCount);
        }

        private static int CountSkillTreeNodes(TreeNode node)
        {
            var count = 1;
            foreach (TreeNode child in node.Nodes)
            {
                count += CountSkillTreeNodes(child);
            }

            return count;
        }

        private static (int DisplayTier, int ItemLevel, int HighestLevel, uint SkillId) GetSkillRank(SkillSnapshot skill)
        {
            return (
                skill.DisplayTier.GetValueOrDefault(),
                skill.ItemLevel,
                skill.HighestLevel,
                skill.SkillId);
        }

        private static string NormalizeSkillBaseName(string? value)
        {
            var text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var hashIndex = text.LastIndexOf('#');
            if (hashIndex >= 0)
            {
                text = text[..hashIndex].TrimEnd();
            }

            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && LooksLikeSkillTier(parts[^1]))
            {
                text = string.Join(' ', parts.Take(parts.Length - 1));
            }

            return text.Trim();
        }

        private static bool LooksLikeSkillTier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.All(ch => ch is 'I' or 'V' or 'X');
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

        private void RefreshSpiritmasterSkillCombos()
        {
            foreach (var list in spiritmasterRuleLists.Where(list => !list.IsDisposed))
            {
                foreach (var row in list.Controls.OfType<Panel>())
                {
                    foreach (var skillCombo in row.Controls
                                 .OfType<RoundedComboBox>()
                                 .Where(combo => string.Equals(combo.Name, "spiritmasterRuleSkillCombo", StringComparison.Ordinal)))
                    {
                        var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);
                        PopulateMaintenanceSkillCombo(skillCombo, selectedSkill.SkillId, selectedSkill.SkillName);
                        var dotStatusLabel = row.Controls
                            .OfType<Label>()
                            .FirstOrDefault(label => string.Equals(label.Name, "spiritmasterDotStatusLabel", StringComparison.Ordinal));
                        var dotDurationLabel = row.Controls
                            .OfType<Label>()
                            .FirstOrDefault(label => string.Equals(label.Name, "spiritmasterDotDurationLabel", StringComparison.Ordinal));
                        if (dotStatusLabel is not null && dotDurationLabel is not null)
                        {
                            UpdateSpiritmasterDotRuleInfo(skillCombo, dotStatusLabel, dotDurationLabel);
                        }
                    }
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
                "触发技能" => new[] { "盾牌反击", "惩戒一击", "盾牌猛击", "脚踝重击" },
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
            "盾牌猛击",
            "脚踝重击"
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
                    ("=", "OemPlus"),
                    (",", "OemComma"),
                    ("X", "X")
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
                    ("Num0", "NumPad0"),
                    ("Num+", "NumPadAdd"),
                    ("Num-", "NumPadSubtract")
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
                    "Num6" or "Num7" or "Num8" or "Num9" or "Num0" or
                    "Num-" or "Num+" => 54,
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
                "OemComma" => ",",
                "X" => "X",
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
                "NumPadSubtract" => "Num-",
                "NumPadAdd" => "Num+",
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

        private sealed class PathEditorControls
        {
            public PathEditorControls(SharedPathKind kind)
            {
                Kind = kind;
            }

            public SharedPathKind Kind { get; }

            public RoundedTextBox? PathNameTextBox { get; set; }

            public RoundedComboBox? SavedPathCombo { get; set; }

            public Label? SummaryLabel { get; set; }

            public Label? StatusLabel { get; set; }

            public RoundedTextBox? PointsTextBox { get; set; }

            public Button? ManualButton { get; set; }

            public Button? StartButton { get; set; }

            public Button? StopButton { get; set; }

            public Button? CleanupNpcRefreshButton { get; set; }

            public RoundedComboBox? CleanupNpcCombo { get; set; }

            public Button? ExecutePathButton { get; set; }

            public CancellationTokenSource? ExecutePathCancellation { get; set; }

            public PathRecordingBuffer Buffer { get; } = new();

            public int SkippedCount { get; set; }
        }

        private sealed class CleanupNpcComboItem
        {
            public CleanupNpcComboItem(string name, double? distanceMeters)
            {
                Name = name;
                DistanceMeters = distanceMeters;
            }

            public string Name { get; }

            public double? DistanceMeters { get; }

            public override string ToString()
            {
                return DistanceMeters.HasValue && !double.IsInfinity(DistanceMeters.Value)
                    ? Name + " (" + DistanceMeters.Value.ToString("F1", CultureInfo.InvariantCulture) + "m)"
                    : Name;
            }
        }

        private sealed class PathComboItem
        {
            private readonly SharedPathSummary _summary;

            public PathComboItem(SharedPathSummary summary)
            {
                _summary = summary;
            }

            public string Name => _summary.Name;

            public override string ToString()
            {
                return _summary.Name +
                       "（" +
                       _summary.PointCount.ToString(CultureInfo.InvariantCulture) +
                       "点 / " +
                       _summary.TotalDistance.ToString("F1", CultureInfo.InvariantCulture) +
                       "m）";
            }
        }

        private sealed class ProfileComboItem
        {
            private readonly ScriptProfileSummary _summary;

            public ProfileComboItem(ScriptProfileSummary summary)
            {
                _summary = summary;
            }

            public string Name => _summary.Name;

            public override string ToString()
            {
                return _summary.Name;
            }
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
