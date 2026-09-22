using System.Globalization;
using System.Drawing.Drawing2D;
using Roadhog.Application;
using Roadhog.Application.Shell;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Core.Radar;

namespace Roadhog
{
    public sealed partial class AccountSettingsForm : Form
    {
        private const string ManualSkillMappingRowDragFormat = "Roadhog.ManualSkillMappingRow";
        private const double CleanupNpcSearchRadiusMeters = 10.0D;
        private const int PathRecordTimerIntervalMs = 100;

        private readonly string _account;
        private readonly string _windowTitle;
        private readonly RoadhogRuntime _runtime;
        private readonly IAccountConfigStore _configStore;
        private readonly ISharedPathStore _pathStore;
        private readonly IScriptProfileStore _profileStore;
        private readonly IFolderLauncher _folderLauncher;
        private readonly string _pathLibraryDirectory;
        private readonly IBagCleanupNameListStore? _bagCleanupNameListStore;
        private readonly IRadarMapStore? _radarMapStore;
        private readonly Dictionary<SharedPathKind, PathEditorControls> pathEditors = new();
        private readonly Dictionary<SharedPathKind, Label> pathOverviewLabels = new();
        private readonly System.Windows.Forms.Timer pathRecordTimer = new() { Interval = PathRecordTimerIntervalMs };
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
        private RoundedComboBox? fixedChannelCombo;
        private FixedChannelMouseScriptSettings _legacyChannelMouse = new();
        private Label? stationaryCombatRadiusLabel;
        private RoundedTextBox? stationaryCombatRadiusTextBox;
        private Label? stationaryCombatRadiusUnitLabel;
        private Label? stalledTargetExclusionSecondsLabel;
        private RoundedTextBox? stalledTargetExclusionSecondsTextBox;
        private Label? stalledTargetExclusionSecondsUnitLabel;
        private Label? pathCombatRadiusLabel;
        private RoundedTextBox? pathCombatRadiusTextBox;
        private Label? pathCombatRadiusUnitLabel;
        private Label? pathFollowReachDistanceLabel;
        private RoundedTextBox? pathFollowReachDistanceTextBox;
        private Label? pathFollowReachDistanceUnitLabel;
        private RoundedTextBox? cameraYawPixelsPerDegreeTextBox;
        private RoundedTextBox? cameraPitchPixelsPerDegreeTextBox;
        private RoundedCheckBox? enableLootCheckBox;
        private RoundedCheckBox? jumpAssistEnabledCheckBox;
        private RoundedCheckBox? contestMonsterCheckBox;
        private RoundedCheckBox? counterEnemyRaceCheckBox;
        private RoundedCheckBox? preferAggressiveMonsterCheckBox;
        private RoundedCheckBox? smartPreAimEnabledCheckBox;
        private RoundedCheckBox? smartPreAimUseFightTargetPositionCheckBox;
        private RoundedCheckBox? smartPreAimResponsiveSwitchingCheckBox;
        private RoundedCheckBox? returnHomeWhenNoTargetCheckBox;
        private RoundedCheckBox? sitWhenNoTargetAtHomeCheckBox;
        private Button? radarEditorButton;
        private Label? radarStatusLabel;
        private RadarObstacleScriptSettings currentRadarSettings = new();
        private RoundedComboBox? activeMonsterFilterCombo;
        private ListBox? activeMonsterFilterListBox;
        private Label? activeMonsterFilterStatusLabel;
        private RoundedCheckBox? stationaryGatherEnabledCheckBox;
        private RoundedTextBox? gatherThreatRadiusTextBox;
        private RoundedComboBox? nearbyGatherFilterCombo;
        private ListView? gatherFilterListView;
        private Label? gatherFilterStatusLabel;
        private Button? readNearbyGatherFilterButton;
        private Button? addGatherFilterButton;
        private Button? gatherFilterKeyButton;
        private Button? removeGatherFilterButton;
        private Button? clearGatherFilterButton;
        private double legacyStationaryGatherSearchRadiusMeters = 10.0D;
        private double gatherOccupiedCheckRadiusMeters = 5.0D;
        private RoundedCheckBox? openingAttackKeyCheckBox;
        private RoundedCheckBox? conditionSkillPreemptsChainCheckBox;
        private RoundedCheckBox? attackWeaveCheckBox;
        private RoundedTextBox? attackWeaveDelayTextBox;
        private RoundedTextBox? chainWindowPerLinkTextBox;
        private RoundedCheckBox? spiritmasterAutoSkillCheckBox;
        private Button? spiritmasterSettingsButton;
        private RoundedCheckBox? openingSkillEnabledCheckBox;
        private RoundedComboBox? openingSkillCombo;
        private Button? openingSkillKeyButton;
        private RoundedTextBox? revivePathNameTextBox;
        private RoundedTextBox? combatPathNameTextBox;
        private RoundedTextBox? maintenancePathNameTextBox;
        private RoundedTextBox? gatherPathNameTextBox;
        private RoundedTextBox? auctionPathNameTextBox;
        private RoundedTextBox? stallPathNameTextBox;
        private Button? townReturnKeyButton;
        private Button? bagCleanupTownReturnKeyButton;
        private RoundedCheckBox? bagCleanupReturnByReversePathCheckBox;
        private RoundedTextBox? pathRecordingMinimumDistanceTextBox;
        private RoundedTextBox? deathReviveClickPointTextBox;
        private Button? deathReviveTestMoveButton;
        private RoundedCheckBox? loopPathCheckBox;
        private RoundedCheckBox? reverseAtEndCheckBox;
        private RoundedCheckBox? deathStopPathCheckBox;
        private RoundedTextBox? revivePathAggressiveClearRadiusTextBox;
        private RoundedCheckBox? sitMaintenanceCheckBox;
        private RoundedTextBox? sitMpBelowTextBox;
        private RoundedTextBox? sitMpRecoverToTextBox;
        private RoundedTextBox? sitHpBelowTextBox;
        private RoundedTextBox? sitHpRecoverToTextBox;
        private FlowLayoutPanel? hpMaintenanceRuleList;
        private FlowLayoutPanel? mpMaintenanceRuleList;
        private FlowLayoutPanel? statusMaintenanceRuleList;
        private FlowLayoutPanel? dpMaintenanceRuleList;
        private Label? hpMaintenanceEmptyLabel;
        private Label? mpMaintenanceEmptyLabel;
        private Label? statusMaintenanceEmptyLabel;
        private Label? dpMaintenanceEmptyLabel;
        private RoundedTextBox? bagCleanupThresholdTextBox;
        private RoundedCheckBox? bagCleanupEnabledCheckBox;
        private (int X, int Y) _bagCleanupDiscardConfirmPoint = (
            MaintenanceScriptSettings.DefaultBagCleanupDiscardConfirmClickX,
            MaintenanceScriptSettings.DefaultBagCleanupDiscardConfirmClickY);
        private (int X, int Y) _bagCleanupSellItemClickPoint;
        private (int X, int Y) _bagCleanupSellButtonClickPoint;
        private BagCleanupItemCoordinateMode _bagCleanupItemCoordinateMode;
        private RoundedTextBox? bagCleanupManualNameTextBox;
        private CheckedListBox? bagCleanupInventoryCheckedListBox;
        private ListBox? bagCleanupExcludedItemListBox;
        private DataGridView? bagCleanupTradeItemGrid;
        private Label? bagCleanupInventoryStatusLabel;
        private Label? bagCleanupNameListTitleLabel;
        private RadioButton? bagCleanupWhitelistRadio;
        private RadioButton? bagCleanupBlacklistRadio;
        private RadioButton? bagCleanupStallRadio;
        private RadioButton? bagCleanupAuctionHouseRadio;
        private Button? bagCleanupAddNameButton;
        private Button? bagCleanupRemoveNameButton;
        private Button? bagCleanupClearNamesButton;
        private readonly List<string> bagCleanupWhitelistItemNames = new();
        private readonly List<string> bagCleanupBlacklistItemNames = new();
        private readonly List<BagCleanupTradeItemConfig> bagCleanupStallItemNames = new();
        private readonly List<BagCleanupTradeItemConfig> bagCleanupAuctionHouseItemNames = new();
        private bool loadingBagCleanupNameListEditor;
        private bool bagCleanupNameListMutationInFlight;
        private readonly Dictionary<string, BagCleanupRuleControls> bagCleanupRuleControls = new(StringComparer.OrdinalIgnoreCase);
        private RoundedComboBox? teamRoleCombo;
        private Panel? teamLeaderPanel;
        private Panel? teamOutputPanel;
        private Panel? teamSupportPanel;
        private RoundedCheckBox? teamLeaderEnabledCheckBox;
        private RoundedCheckBox? teamLeaderDungeonModeCheckBox;
        private RoundedCheckBox? teamLeaderAllowSelfDefenseCheckBox;
        private RoundedCheckBox? teamLeaderStopAdvanceWhenMemberDisconnectedCheckBox;
        private RoundedCheckBox? teamLeaderTacticalMarkCheckBox;
        private Label? teamLeaderTacticalMarkKeyLabel;
        private Button? teamLeaderTacticalMarkKeyButton;
        private RoundedTextBox? teamGroupDistanceTextBox;
        private RoundedCheckBox? teamOutputEnabledCheckBox;
        private RoundedCheckBox? teamOutputDungeonModeCheckBox;
        private RoundedCheckBox? teamOutputAllowSelfDefenseCheckBox;
        private RoundedCheckBox? teamOutputFollowLeaderCheckBox;
        private RoundedCheckBox? teamOutputOnlyAttackLeaderMarkedTargetCheckBox;
        private RoundedCheckBox? teamOutputStopWhenLeaderHasNoTargetCheckBox;
        private RoundedCheckBox? teamOutputStopWhenLeaderDeadCheckBox;
        private RoundedTextBox? teamOutputLeaderDistanceTextBox;
        private Button? teamOutputAssistTargetKeyButton;
        private RoundedCheckBox? teamOutputTacticalMarkTargetingCheckBox;
        private Label? teamOutputSelectTacticalMarkTargetKeyLabel;
        private Button? teamOutputSelectTacticalMarkTargetKeyButton;
        private RoundedCheckBox? teamSupportEnabledCheckBox;
        private RoundedCheckBox? teamSupportDungeonModeCheckBox;
        private RoundedCheckBox? teamSupportJoinCombatCheckBox;
        private RoundedCheckBox? teamSupportMentalCleanseCheckBox;
        private RoundedCheckBox? teamSupportPhysicalCleanseCheckBox;
        private RoundedCheckBox? teamSupportAllowSelfDefenseCheckBox;
        private RoundedCheckBox? teamSupportStopWhenLeaderDeadCheckBox;
        private RoundedTextBox? teamSupportLeaderDistanceTextBox;
        private RoundedCheckBox? teamSupportTacticalMarkTargetingCheckBox;
        private Label? teamSupportSelectTacticalMarkTargetKeyLabel;
        private Button? teamSupportSelectTacticalMarkTargetKeyButton;
        private FlowLayoutPanel? teamHealSkillRuleList;
        private Label? teamHealSkillEmptyLabel;
        private Button? teamMentalCleanseKeyButton;
        private Button? teamPhysicalCleanseKeyButton;
        private Button? teamGroupCleanseKeyButton;
        private RadioButton? skillAutoModeRadio;
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
            IScriptProfileStore profileStore,
            IFolderLauncher folderLauncher,
            string pathLibraryDirectory,
            string accountDisplayText = "",
            IBagCleanupNameListStore? bagCleanupNameListStore = null,
            IRadarMapStore? radarMapStore = null)
        {
            _account = account;
            _windowTitle = BuildWindowTitle(account, accountDisplayText);
            _runtime = runtime;
            _configStore = configStore;
            _pathStore = pathStore;
            _profileStore = profileStore;
            _folderLauncher = folderLauncher;
            _pathLibraryDirectory = pathLibraryDirectory;
            _bagCleanupNameListStore = bagCleanupNameListStore;
            _radarMapStore = radarMapStore;
            pathRecordTimer.Tick += PathRecordTimer_Tick;
            InitializeSettingsForm();
        }

        private CancellationTokenSource? _personalShopTestCts;
        private CancellationTokenSource? _auctionTestCts;
        private CancellationTokenSource? _inventoryDiscardTestCts;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _personalShopTestCts?.Cancel();
            _auctionTestCts?.Cancel();
            _inventoryDiscardTestCts?.Cancel();
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
            Text = _windowTitle;

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
            settingsTabs.TabPages.Add(CreateTeamTab());

            Controls.Add(settingsTabs);
            var saveButton = AddButton(this, "保存配置", ClientSize.Width - 160, 3, 150, 30, SaveSettingsButton_Click);
            saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveButton.BringToFront();
            LoadSavedSettings();
        }

        private static string BuildWindowTitle(string account, string accountDisplayText)
        {
            var accountText = string.IsNullOrWhiteSpace(account)
                ? "账号"
                : account.Trim();
            var displayText = accountDisplayText?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(displayText) ||
                string.Equals(displayText, accountText, StringComparison.OrdinalIgnoreCase))
            {
                return "账号设置 - " + accountText;
            }

            return "账号设置 - " + displayText + " (" + accountText + ")";
        }

        private void LoadSavedSettings()
        {
            var account = LoadAccountConfigOrDefault();
            RefreshProfileLibrary();
            var settings = BuildEffectiveScriptSettings(account);
            var nameListLoadError = TryApplySharedBagCleanupNameLists(settings);
            ApplyScriptSettings(settings);
            if (!string.IsNullOrWhiteSpace(nameListLoadError))
            {
                SetBagCleanupInventoryStatus(
                    "黑白名单读取失败，背包清理启动时将被禁用: " + nameListLoadError,
                    true);
            }
        }

        private string? TryApplySharedBagCleanupNameLists(ScriptSettings settings)
        {
            if (_bagCleanupNameListStore is null)
            {
                return null;
            }

            var load = _bagCleanupNameListStore.LoadAsync().GetAwaiter().GetResult();
            if (!load.Success)
            {
                return load.Error ?? "未知读取错误";
            }

            if (load.Value is { Found: true, Document: { } document })
            {
                document.ApplyTo(settings.Maintenance);
            }

            return null;
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
            if (fixedChannelCombo is not null)
            {
                fixedChannelCombo.SelectedIndex = settings.FixedChannelNumber is >= ScriptSettings.MinimumFixedChannelNumber and <= ScriptSettings.MaximumFixedChannelNumber
                    ? settings.FixedChannelNumber
                    : 0;
            }
            _legacyChannelMouse = (settings.FixedChannelMouse ?? new FixedChannelMouseScriptSettings()).Clone();
            SetStationaryCombatRadius(settings.Combat);
            SetStalledTargetExclusionSeconds(settings.Combat);
            SetPathCombatRadius(settings.Combat);
            SetPathFollowReachDistance(settings.Combat);
            SetCameraTurnScales(settings.Combat);
            currentRadarSettings = (settings.Combat.RadarObstacleAvoidance ?? new RadarObstacleScriptSettings()).Clone();
            RefreshRadarStatus();
            RefreshCombatModeVisibility();

            SetChecked(enableLootCheckBox, settings.Combat.EnableLoot);
            SetChecked(jumpAssistEnabledCheckBox, settings.Combat.JumpAssistEnabled);
            SetChecked(contestMonsterCheckBox, settings.Combat.ContestMonster);
            SetChecked(counterEnemyRaceCheckBox, settings.Combat.CounterEnemyRace);
            SetChecked(preferAggressiveMonsterCheckBox, settings.Combat.PreferAggressiveMonsters);
            SetChecked(smartPreAimEnabledCheckBox, settings.Combat.SmartPreAimEnabled);
            SetChecked(smartPreAimUseFightTargetPositionCheckBox, settings.Combat.SmartPreAimUseFightTargetPosition);
            SetChecked(smartPreAimResponsiveSwitchingCheckBox, settings.Combat.SmartPreAimResponsiveSwitching);
            RefreshCombatModeVisibility();
            SetChecked(returnHomeWhenNoTargetCheckBox, settings.Combat.ReturnHomeWhenNoTarget);
            SetChecked(sitWhenNoTargetAtHomeCheckBox, settings.Combat.SitWhenNoTargetAtHome);
            PopulateActiveMonsterFilterList(settings.Combat.ActiveMonsterNameFilters);
            ApplyGatherSettings(settings.Gather ?? new GatherScriptSettings());

            var paths = settings.Paths ?? new PathScriptSettings();
            SetText(revivePathNameTextBox, paths.RevivePathName);
            SetText(combatPathNameTextBox, paths.CombatPathName);
            SetText(maintenancePathNameTextBox, paths.MaintenancePathName);
            SetText(gatherPathNameTextBox, paths.GatherPathName);
            SetText(auctionPathNameTextBox, paths.AuctionPathName);
            SetText(stallPathNameTextBox, paths.StallPathName);
            SetKeyButton(townReturnKeyButton, paths.TownReturnKey);
            SetKeyButton(
                bagCleanupTownReturnKeyButton,
                string.IsNullOrWhiteSpace(paths.BagCleanupTownReturnKey)
                    ? paths.TownReturnKey
                    : paths.BagCleanupTownReturnKey);
            SetChecked(bagCleanupReturnByReversePathCheckBox, paths.BagCleanupReturnByReversePath);
            SetText(
                pathRecordingMinimumDistanceTextBox,
                paths.RecordingMinimumDistance.ToString("0.###", CultureInfo.InvariantCulture));
            SetText(deathReviveClickPointTextBox, FormatScreenPoint(paths.DeathReviveClickX, paths.DeathReviveClickY));
            SetChecked(loopPathCheckBox, paths.LoopPath);
            SetChecked(reverseAtEndCheckBox, paths.ReverseAtEnd);
            SetChecked(deathStopPathCheckBox, paths.DeathStopPath);
            SetRevivePathAggressiveClearRadius(paths);
            RefreshPathLibrary();
            SelectConfiguredPath(SharedPathKind.Revive, paths.RevivePathName);
            SelectConfiguredPath(SharedPathKind.Combat, paths.CombatPathName);
            SelectConfiguredPath(SharedPathKind.Maintenance, paths.MaintenancePathName);
            SelectConfiguredPath(SharedPathKind.Gather, paths.GatherPathName);
            SelectConfiguredPath(SharedPathKind.Auction, paths.AuctionPathName);
            SelectConfiguredPath(SharedPathKind.Stall, paths.StallPathName);

            SetChecked(sitMaintenanceCheckBox, settings.Maintenance.SitMaintenanceEnabled);
            SetText(sitMpBelowTextBox, settings.Maintenance.SitMpBelowPercent.ToString());
            SetText(sitMpRecoverToTextBox, settings.Maintenance.SitMpRecoverToPercent.ToString());
            SetText(sitHpBelowTextBox, settings.Maintenance.SitHpBelowPercent.ToString());
            SetText(sitHpRecoverToTextBox, settings.Maintenance.SitHpRecoverToPercent.ToString());
            PopulateMaintenanceKeyRules(hpMaintenanceRuleList, hpMaintenanceEmptyLabel, settings.Maintenance.HpMaintenanceRules);
            PopulateMaintenanceKeyRules(mpMaintenanceRuleList, mpMaintenanceEmptyLabel, settings.Maintenance.MpMaintenanceRules);
            PopulateStatusMaintenanceRules(statusMaintenanceRuleList, statusMaintenanceEmptyLabel, settings.Maintenance.StatusMaintenanceRules);
            PopulateDpMaintenanceRules(dpMaintenanceRuleList, dpMaintenanceEmptyLabel, settings.Maintenance.DpMaintenanceRules);
            SetChecked(bagCleanupEnabledCheckBox, settings.Maintenance.BagCleanupEnabled);
            SetText(bagCleanupThresholdTextBox, settings.Maintenance.BagCleanupThreshold.ToString());
            LoadCleanupWorkflow(settings.Maintenance.CleanupWorkflow);
            _bagCleanupDiscardConfirmPoint = (
                settings.Maintenance.BagCleanupDiscardConfirmClickX,
                settings.Maintenance.BagCleanupDiscardConfirmClickY);
            SetBagCleanupClickPoints(
                settings.Maintenance.BagCleanupSellItemClickX,
                settings.Maintenance.BagCleanupSellItemClickY,
                settings.Maintenance.BagCleanupSellButtonClickX,
                settings.Maintenance.BagCleanupSellButtonClickY);
            _bagCleanupItemCoordinateMode = settings.Maintenance.BagCleanupItemCoordinateMode;
            ApplyBagCleanupRules(settings.Maintenance.BagCleanupRules);
            PopulateBagCleanupNameLists(BagCleanupNameListsDocument.FromSettings(settings.Maintenance));
            ApplyTeamSettings(settings.Team ?? new TeamScriptSettings());
            SetChecked(openingAttackKeyCheckBox, settings.SemiAuto.AttackKeyLoopEnabled);
            SetChecked(conditionSkillPreemptsChainCheckBox, settings.SemiAuto.ConditionSkillPreemptsChain);
            SetChecked(attackWeaveCheckBox, settings.SemiAuto.AttackWeaveEnabled);
            SetText(attackWeaveDelayTextBox, settings.SemiAuto.AttackWeaveDelayMs.ToString());
            if (attackWeaveDelayTextBox is not null)
            {
                attackWeaveDelayTextBox.Enabled = settings.SemiAuto.AttackWeaveEnabled;
            }
            SetText(chainWindowPerLinkTextBox, settings.SemiAuto.ChainWindowPerLinkMs.ToString());
            SetChecked(spiritmasterAutoSkillCheckBox, settings.Skills.SpiritmasterAutoSkillLogicEnabled);
            ApplyOpeningSkillSettings(settings.Skills.OpeningSkill);
            currentSpiritmasterSettings = (settings.Skills.Spiritmaster ?? new SpiritmasterSkillSettings()).Clone();
            PopulateSpiritmasterRuleLists(currentSpiritmasterSettings);

            if (skillAutoModeRadio is not null)
            {
                skillAutoModeRadio.Checked = true;
            }

            ShowSkillMode(SkillConfigurationMode.Auto);

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
            if (pathEditors.Values.Any(IsPathEditorBusy))
            {
                error = "路径正在操作中，请完成操作或停止路径后再保存配置。";
                return false;
            }
            if (bagCleanupTradeItemGrid?.IsCurrentCellInEditMode == true && bagCleanupTradeItemGrid.CurrentCell is { ColumnIndex: 1, ReadOnly: false } &&
                !BagCleanupTradeItemConfig.TryParseUnitPrice(bagCleanupTradeItemGrid.EditingControl?.Text, out _))
            {
                error = "请先修正物品单价：输入大于 0 的整数，或留空。";
                return false;
            }
            if (bagCleanupTradeItemGrid?.IsCurrentCellInEditMode == true && !bagCleanupTradeItemGrid.EndEdit())
            {
                error = "请先修正物品单价：输入大于 0 的整数，或留空。";
                return false;
            }
            if (bagCleanupTradeItemGrid?.CurrentCell is { ColumnIndex: 1 } priceCell)
            {
                bagCleanupTradeItemGrid.CurrentCell = bagCleanupTradeItemGrid.Rows[priceCell.RowIndex].Cells[0];
            }
            if (bagCleanupNameListMutationInFlight)
            {
                error = "物品名单正在保存，请稍后再保存配置。";
                return false;
            }
            var account = LoadAccountConfigOrDefault();
            var previousSettings = BuildEffectiveScriptSettings(account);
            var capturedSettings = CaptureScriptSettings();
            capturedSettings.Maintenance.AutoEquip = previousSettings.Maintenance.AutoEquip;
            capturedSettings.Maintenance.AutoDecompose = previousSettings.Maintenance.AutoDecompose;
            capturedSettings.SemiAuto = previousSettings.SemiAuto.Clone();
            capturedSettings.SemiAuto.AttackKeyLoopEnabled =
                openingAttackKeyCheckBox?.Checked ?? capturedSettings.SemiAuto.AttackKeyLoopEnabled;
            capturedSettings.SemiAuto.ConditionSkillPreemptsChain =
                conditionSkillPreemptsChainCheckBox?.Checked ?? capturedSettings.SemiAuto.ConditionSkillPreemptsChain;
            capturedSettings.SemiAuto.AttackWeaveEnabled =
                attackWeaveCheckBox?.Checked ?? capturedSettings.SemiAuto.AttackWeaveEnabled;
            capturedSettings.SemiAuto.AttackWeaveDelayMs = Math.Clamp(
                ReadInt(attackWeaveDelayTextBox, capturedSettings.SemiAuto.AttackWeaveDelayMs),
                0,
                SemiAutoScriptSettings.MaximumAttackWeaveDelayMs);
            capturedSettings.SemiAuto.ChainWindowPerLinkMs =
                Math.Clamp(
                    ReadInt(chainWindowPerLinkTextBox, capturedSettings.SemiAuto.ChainWindowPerLinkMs),
                    SemiAutoScriptSettings.MinimumChainWindowPerLinkMs,
                    SemiAutoScriptSettings.MaximumChainWindowPerLinkMs);
            if (!SaveSelectedPathRadiusBindings(out error))
            {
                return false;
            }
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
            _runtime.ApplyRadarObstacleSettings(_account, capturedSettings.Combat.RadarObstacleAvoidance);
            currentRadarSettings = capturedSettings.Combat.RadarObstacleAvoidance.Clone();
            RefreshRadarStatus();
            SetProfileStatus("已保存方案: " + profileName, false);
            return true;
        }

        private bool SaveSelectedPathRadiusBindings(out string error)
        {
            error = string.Empty;
            var changes = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (var editor in pathEditors.Values.Where(editor => editor.BindStationaryRadiusCheckBox is not null))
            {
                if (editor.SavingPath)
                {
                    error = "路径正在保存，请稍后再保存配置。";
                    return false;
                }
                if (!TryReadPathRadiusBinding(editor, out var radius, out error))
                {
                    SetPathStatus(editor, error, true);
                    return false;
                }

                var name = GetText(editor.PathNameTextBox, string.Empty);
                var loaded = editor.LoadedDocument;
                // Leave unchanged drafts alone so another tab/account's newer binding survives.
                if (radius == loaded?.BoundStationaryCombatRadius &&
                    (radius is null || string.Equals(name, loaded?.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    error = "绑定原地打半径前，请选择路径；新路径请先点击“保存修改”。";
                    return false;
                }
                if (changes.TryGetValue(name, out var otherRadius) && otherRadius != radius)
                {
                    error = "复活和打怪页对同一路径的半径设置不一致，请统一后保存: " + name;
                    return false;
                }
                changes[name] = radius;
            }

            // Validate and load all changes before writing. Only radius metadata is saved here;
            // route-point drafts still belong to the explicit save-to-list action.
            var documents = new List<SharedPathDocument>();
            foreach (var change in changes)
            {
                var load = _pathStore.LoadAsync(change.Key).GetAwaiter().GetResult();
                if (!load.Success || load.Value is null)
                {
                    error = "读取路径失败，无法保存原地打半径；新路径请先点击“保存修改”: " +
                        change.Key + "。" + load.Error;
                    return false;
                }
                var document = load.Value.Clone();
                document.BoundStationaryCombatRadius = change.Value;
                documents.Add(document);
            }

            foreach (var document in documents)
            {
                var save = _pathStore.SaveAsync(document).GetAwaiter().GetResult();
                if (!save.Success)
                {
                    error = "保存路径原地打半径失败: " + document.Name + "。" + save.Error;
                    return false;
                }
                foreach (var editor in pathEditors.Values.Where(editor =>
                    editor.BindStationaryRadiusCheckBox is not null &&
                    string.Equals(GetText(editor.PathNameTextBox, string.Empty), document.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    editor.LoadedDocument = document.Clone();
                    ApplyPathRadiusBindingToEditor(editor, document);
                    MarkPathMetadataSaved(editor, "RadiusEnabled", "Radius");
                    SetPathStatus(editor, "已保存路径半径设置: " + document.Name, false);
                }
            }
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
            CopyBagCleanupClickPointsToPath(document);
            var save = _pathStore.SaveAsync(document).GetAwaiter().GetResult();
            if (save.Success)
            {
                MarkPathMetadataSaved(editor, "Npc");
                return true;
            }

            error = save.Error ?? "保存清包路径NPC绑定失败。";
            return false;
        }

        private void CopyBagCleanupClickPointsToPath(SharedPathDocument document)
        {
            document.BagCleanupSellItemClickX = _bagCleanupSellItemClickPoint.X;
            document.BagCleanupSellItemClickY = _bagCleanupSellItemClickPoint.Y;
            document.BagCleanupSellButtonClickX = _bagCleanupSellButtonClickPoint.X;
            document.BagCleanupSellButtonClickY = _bagCleanupSellButtonClickPoint.Y;
        }

        private void ApplyBagCleanupPathClickPoints(SharedPathDocument document)
        {
            if (document.TryGetBagCleanupClickPoints(
                    out var sellItemClickX,
                    out var sellItemClickY,
                    out var sellButtonClickX,
                    out var sellButtonClickY))
            {
                SetBagCleanupClickPoints(sellItemClickX, sellItemClickY, sellButtonClickX, sellButtonClickY);
                return;
            }

            var settings = BuildEffectiveScriptSettings(LoadAccountConfigOrDefault());
            SetBagCleanupClickPoints(
                settings.Maintenance.BagCleanupSellItemClickX,
                settings.Maintenance.BagCleanupSellItemClickY,
                settings.Maintenance.BagCleanupSellButtonClickX,
                settings.Maintenance.BagCleanupSellButtonClickY);
        }

        private void SetBagCleanupClickPoints(
            int sellItemClickX,
            int sellItemClickY,
            int sellButtonClickX,
            int sellButtonClickY)
        {
            _bagCleanupSellItemClickPoint = (sellItemClickX, sellItemClickY);
            _bagCleanupSellButtonClickPoint = (sellButtonClickX, sellButtonClickY);
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
                FixedChannelNumber = fixedChannelCombo?.SelectedIndex is >= ScriptSettings.MinimumFixedChannelNumber and <= ScriptSettings.MaximumFixedChannelNumber
                    ? fixedChannelCombo.SelectedIndex
                    : 0,
                FixedChannelMouse = _legacyChannelMouse.Clone(),
                Combat = new CombatScriptSettings
                {
                    EnableLoot = enableLootCheckBox?.Checked ?? true,
                    JumpAssistEnabled = jumpAssistEnabledCheckBox?.Checked ?? false,
                    ContestMonster = contestMonsterCheckBox?.Checked ?? false,
                    CounterEnemyRace = counterEnemyRaceCheckBox?.Checked ?? false,
                    PreferAggressiveMonsters = preferAggressiveMonsterCheckBox?.Checked ?? false,
                    SmartPreAimEnabled = smartPreAimEnabledCheckBox?.Checked ?? false,
                    SmartPreAimUseFightTargetPosition = smartPreAimUseFightTargetPositionCheckBox?.Checked ?? false,
                    SmartPreAimResponsiveSwitching = smartPreAimResponsiveSwitchingCheckBox?.Checked ?? false,
                    ReturnHomeWhenNoTarget = returnHomeWhenNoTargetCheckBox?.Checked ?? true,
                    SitWhenNoTargetAtHome = sitWhenNoTargetAtHomeCheckBox?.Checked ?? false,
                    ActiveMonsterNameFilters = CaptureActiveMonsterFilterList(),
                    HasStationaryCombatPosition = false,
                    StationaryCombatX = 0.0D,
                    StationaryCombatY = 0.0D,
                    StationaryCombatZ = 0.0D,
                    StationaryCombatRadius = ReadDouble(stationaryCombatRadiusTextBox, 30.0D, 1.0D, 500.0D),
                    StalledTargetExclusionSeconds = Math.Clamp(
                        ReadInt(
                            stalledTargetExclusionSecondsTextBox,
                            CombatScriptSettings.DefaultStalledTargetExclusionSeconds),
                        CombatScriptSettings.MinimumStalledTargetExclusionSeconds,
                        CombatScriptSettings.MaximumStalledTargetExclusionSeconds),
                    PathCombatRadius = ReadDouble(pathCombatRadiusTextBox, 30.0D, 1.0D, 500.0D),
                    PathFollowReachDistance = ReadDouble(pathFollowReachDistanceTextBox, 5.0D, 0.5D, 50.0D),
                    CameraYawPixelsPerDegree = ReadDouble(cameraYawPixelsPerDegreeTextBox, 11.0D, 0.1D, 100.0D),
                    CameraPitchPixelsPerDegree = ReadDouble(cameraPitchPixelsPerDegreeTextBox, 13.0D, 0.1D, 100.0D),
                    RadarObstacleAvoidance = currentRadarSettings.Clone()
                },
                Gather = CaptureGatherSettings(),
                Paths = new PathScriptSettings
                {
                    RevivePathName = GetText(revivePathNameTextBox, string.Empty),
                    CombatPathName = GetText(combatPathNameTextBox, string.Empty),
                    MaintenancePathName = GetText(maintenancePathNameTextBox, string.Empty),
                    GatherPathName = GetText(gatherPathNameTextBox, string.Empty),
                    AuctionPathName = GetText(auctionPathNameTextBox, string.Empty),
                    StallPathName = GetText(stallPathNameTextBox, string.Empty),
                    TownReturnKey = townReturnKeyButton?.Tag as string ?? string.Empty,
                    BagCleanupTownReturnKey = bagCleanupTownReturnKeyButton?.Tag as string ?? string.Empty,
                    BagCleanupReturnByReversePath = bagCleanupReturnByReversePathCheckBox?.Checked ?? true,
                    RecordingMinimumDistance = ReadDouble(
                        pathRecordingMinimumDistanceTextBox,
                        PathScriptSettings.DefaultRecordingMinimumDistance,
                        PathRecordingBuffer.MinimumAllowedDistanceMeters,
                        PathRecordingBuffer.MaximumAllowedDistanceMeters),
                    DeathReviveClickX = deathReviveClickPoint.X,
                    DeathReviveClickY = deathReviveClickPoint.Y,
                    LoopPath = loopPathCheckBox?.Checked ?? true,
                    ReverseAtEnd = reverseAtEndCheckBox?.Checked ?? false,
                    DeathStopPath = deathStopPathCheckBox?.Checked ?? true,
                    RevivePathAggressiveClearRadius = ReadDouble(
                        revivePathAggressiveClearRadiusTextBox,
                        PathScriptSettings.DefaultRevivePathAggressiveClearRadius,
                        1.0D,
                        500.0D)
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
                    DpMaintenanceRules = CaptureDpMaintenanceRules(dpMaintenanceRuleList),
                    BagCleanupEnabled = bagCleanupEnabledCheckBox?.Checked ?? false,
                    BagCleanupThreshold = ReadInt(bagCleanupThresholdTextBox, 5),
                    CleanupWorkflow = CaptureCleanupWorkflow(),
                    BagCleanupSellItemClickX = _bagCleanupSellItemClickPoint.X,
                    BagCleanupSellItemClickY = _bagCleanupSellItemClickPoint.Y,
                    BagCleanupSellButtonClickX = _bagCleanupSellButtonClickPoint.X,
                    BagCleanupSellButtonClickY = _bagCleanupSellButtonClickPoint.Y,
                    BagCleanupDiscardConfirmClickX = _bagCleanupDiscardConfirmPoint.X,
                    BagCleanupDiscardConfirmClickY = _bagCleanupDiscardConfirmPoint.Y,
                    BagCleanupItemCoordinateMode = _bagCleanupItemCoordinateMode,
                    BagCleanupRules = CaptureBagCleanupRules(),
                    BagCleanupExcludedItemNames = CaptureBagCleanupExcludedItemList(),
                    BagCleanupDiscardItemNameKeywords = CaptureBagCleanupDiscardItemList(),
                    BagCleanupStallItems = BagCleanupTradeItemConfig.Normalize(bagCleanupStallItemNames),
                    BagCleanupAuctionHouseItems = BagCleanupTradeItemConfig.Normalize(bagCleanupAuctionHouseItemNames)
                },
                Team = CaptureTeamSettings(),
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

        private GatherScriptSettings CaptureGatherSettings()
        {
            var rules = gatherFilterListView?.Items
                .Cast<ListViewItem>()
                .Where(item => item.Tag is GatherFilterRuleDraft)
                .Select(item =>
                {
                    var draft = (GatherFilterRuleDraft)item.Tag!;
                    return new GatherFilterRuleSettings
                    {
                        Enabled = item.Checked,
                        GatherSourceId = draft.GatherSourceId,
                        Name = draft.DisplayName,
                        GatherKey = draft.GatherKey
                    };
                })
                .Where(rule => rule.GatherSourceId != 0)
                .ToList() ?? new List<GatherFilterRuleSettings>();

            return new GatherScriptSettings
            {
                StationaryPriorityEnabled = stationaryGatherEnabledCheckBox?.Checked ?? false,
                StationarySearchRadiusMeters = legacyStationaryGatherSearchRadiusMeters,
                ThreatClearRadiusMeters = ReadDouble(
                    gatherThreatRadiusTextBox,
                    7.0D,
                    0.5D,
                    50.0D),
                OccupiedCheckRadiusMeters = gatherOccupiedCheckRadiusMeters,
                Rules = rules
            };
        }

        private void ApplyGatherSettings(GatherScriptSettings settings)
        {
            SetChecked(stationaryGatherEnabledCheckBox, settings.StationaryPriorityEnabled);
            legacyStationaryGatherSearchRadiusMeters =
                Math.Clamp(settings.StationarySearchRadiusMeters, 1.0D, 100.0D);
            SetText(
                gatherThreatRadiusTextBox,
                Math.Clamp(settings.ThreatClearRadiusMeters, 0.5D, 50.0D)
                    .ToString("0.###", CultureInfo.InvariantCulture));
            gatherOccupiedCheckRadiusMeters =
                Math.Clamp(settings.OccupiedCheckRadiusMeters, 0.5D, 20.0D);

            if (gatherFilterListView is null)
            {
                return;
            }

            gatherFilterListView.Items.Clear();
            foreach (var rule in settings.Rules ?? new List<GatherFilterRuleSettings>())
            {
                if (rule.GatherSourceId == 0)
                {
                    continue;
                }

                var draft = new GatherFilterRuleDraft(rule);
                var item = CreateGatherFilterListItem(draft);
                item.Checked = rule.Enabled;
                gatherFilterListView.Items.Add(item);
            }

            UpdateGatherFilterKeyButton(null);
        }

        private SkillConfigurationMode CaptureSkillConfigurationMode()
        {
            return SkillConfigurationMode.Auto;
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
                AccountMainMode.Gather => "路径采集",
                AccountMainMode.SemiAuto => "半自动",
                _ => "自定义打怪"
            };
        }

        private static AccountMainMode ParseMainMode(string? text)
        {
            return text switch
            {
                "路径采集" => AccountMainMode.Gather,
                "采集" => AccountMainMode.Gather,
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

#if DEBUG
        private async Task RunApiProbeAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "探针中...";

            try
            {
                var result = await _runtime
                    .RunApiProbeAsync(_account)
                    .ConfigureAwait(true);
                var message = result.Success && result.Value is not null
                    ? result.Value.ToDisplayText()
                    : "API探针失败: " + (result.Error ?? "未知错误");
                var icon = result.Success && result.Value?.AllPassed == true
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning;

                ShowApiProbeResult(message, icon);
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

        private void ShowApiProbeResult(string message, MessageBoxIcon icon)
        {
            using var dialog = new Form
            {
                AutoScaleDimensions = new SizeF(7F, 17F),
                AutoScaleMode = AutoScaleMode.Font,
                BackColor = _pageBackground,
                ClientSize = new Size(920, 620),
                Font = new Font("Microsoft YaHei UI", 9F),
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false,
                MinimumSize = new Size(720, 460),
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                Text = icon == MessageBoxIcon.Information ? "API探针 - 全部通过" : "API探针 - 存在失败"
            };

            var resultTextBox = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                Location = new Point(12, 12),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Size = new Size(896, 548),
                Text = message,
                WordWrap = false
            };
            dialog.Controls.Add(resultTextBox);

            var copyButton = new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(726, 574),
                Size = new Size(84, 32),
                Text = "复制结果"
            };
            copyButton.Click += (_, _) => Clipboard.SetText(message);
            dialog.Controls.Add(copyButton);

            var closeButton = new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK,
                Location = new Point(824, 574),
                Size = new Size(84, 32),
                Text = "关闭"
            };
            dialog.AcceptButton = closeButton;
            dialog.CancelButton = closeButton;
            dialog.Controls.Add(closeButton);
            dialog.ShowDialog(this);
        }
#endif

        private TabPage CreateSummaryTab()
        {
            var tab = CreateBaseTab("总览");
            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(852, 538);
            tab.Controls.Add(page);

            Panel AddSection(string title, int top, int height)
            {
                var section = new Panel
                {
                    Location = new Point(12, top),
                    Size = new Size(828, height),
                    BackColor = _pageBackground
                };
                page.Controls.Add(section);
                var heading = AddLabel(section, "  " + title, 0, 0, 828, 28, _textGreen, FontStyle.Bold);
                heading.BackColor = _inputBackground;
                return section;
            }

            var profilePanel = AddSection("方案管理", 10, 80);
            var modePanel = AddSection("运行模式与范围", 102, 126);
            var behaviorPanel = AddSection("选怪与拾取", 240, 148);
            var environmentPanel = AddSection("镜头与频道", 400, 126);

            currentProfileLabel = AddLabel(profilePanel, "当前方案: default_profile", 124, 2, 304, 24, _textGreen, FontStyle.Bold);
            currentProfileLabel.BackColor = _inputBackground;
            AddLabel(profilePanel, "方案名", 12, 44, 64, 24);
            profileNameTextBox = AddTextBox(profilePanel, "default_profile", 80, 42, 236, 28);
            AddLabel(profilePanel, "已保存方案", 340, 44, 92, 24);
            profileStatusLabel = AddLabel(profilePanel, string.Empty, 436, 2, 380, 24);
            profileStatusLabel.BackColor = _inputBackground;
            savedProfileCombo = AddCombo(profilePanel, 436, 42, 272, 28);
            savedProfileCombo.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
            AddButton(profilePanel, "删除", 724, 41, 92, 30, (_, _) => DeleteSavedProfile());
            AddLabel(environmentPanel, "水平", 12, 44, 46, 24);
            cameraYawPixelsPerDegreeTextBox = AddTextBox(environmentPanel, "11.0", 64, 42, 80, 28);
            AddLabel(environmentPanel, "俯仰", 164, 44, 48, 24);
            cameraPitchPixelsPerDegreeTextBox = AddTextBox(environmentPanel, "13.0", 220, 42, 80, 28);

            mainModeCombo = AddCombo(modePanel, 88, 42, 236, 28, "自定义打怪", "路径采集", "半自动");
            mainModeCombo.SelectedIndexChanged += (_, _) => RefreshCombatModeVisibility();
            AddLabel(modePanel, "主模式", 12, 44, 72, 24, _textGreen, FontStyle.Bold);

            combatModeCombo = AddCombo(modePanel, 464, 42, 260, 28, "原地打怪", "路径打怪");
            combatModeCombo.SelectedIndexChanged += (_, _) => RefreshCombatModeVisibility();
            combatModeLabel = AddLabel(modePanel, "打怪模式", 380, 44, 80, 24);
            stationaryCombatRadiusLabel = AddLabel(modePanel, "半径", 12, 84, 72, 24);
            stationaryCombatRadiusTextBox = AddTextBox(modePanel, "30.0", 88, 82, 100, 28);
            stationaryCombatRadiusUnitLabel = AddLabel(modePanel, "m", 196, 84, 24, 24);
            stalledTargetExclusionSecondsLabel = AddLabel(modePanel, "卡怪排除", 380, 84, 80, 24);
            stalledTargetExclusionSecondsTextBox = AddTextBox(modePanel, "60", 464, 82, 100, 28);
            stalledTargetExclusionSecondsTextBox.Name = "stalledTargetExclusionSecondsTextBox";
            stalledTargetExclusionSecondsUnitLabel = AddLabel(modePanel, "秒", 576, 84, 24, 24);
            pathCombatRadiusLabel = AddLabel(modePanel, "半径", 12, 84, 72, 24);
            pathCombatRadiusTextBox = AddTextBox(modePanel, "30.0", 88, 82, 100, 28);
            pathCombatRadiusUnitLabel = AddLabel(modePanel, "m", 196, 84, 24, 24);
            pathFollowReachDistanceLabel = AddLabel(modePanel, "精度", 380, 84, 80, 24);
            pathFollowReachDistanceTextBox = AddTextBox(modePanel, "5.0", 464, 82, 100, 28);
            pathFollowReachDistanceUnitLabel = AddLabel(modePanel, "m", 576, 84, 24, 24);

            enableLootCheckBox = AddCheckBox(behaviorPanel, "启用拾取", 12, 42, 120, true);
            contestMonsterCheckBox = AddCheckBox(behaviorPanel, "抢怪", 164, 42, 100, false);
            counterEnemyRaceCheckBox = AddCheckBox(behaviorPanel, "反击敌对种族", 312, 42, 180, false);
            preferAggressiveMonsterCheckBox = AddCheckBox(behaviorPanel, "优先攻击主动怪", 520, 42, 190, false);

#if DEBUG
            var apiProbeButton = AddButton(page, "API探针", 702, 32, 134, 30);
            apiProbeButton.Visible = false;
            apiProbeButton.Click += async (_, _) =>
                await RunApiProbeAsync(apiProbeButton).ConfigureAwait(true);
#endif

            returnHomeWhenNoTargetCheckBox = AddCheckBox(behaviorPanel, "\u6ca1\u602a\u56de\u4e2d\u5fc3", 12, 114, 140, true);
            sitWhenNoTargetAtHomeCheckBox = AddCheckBox(behaviorPanel, "\u6ca1\u602a\u5750\u5730\u677f", 184, 114, 140, false);
            jumpAssistEnabledCheckBox = AddCheckBox(behaviorPanel, "\u6253\u602a\u8df3\u8dc3", 356, 114, 120, false);
            jumpAssistEnabledCheckBox.Name = "jumpAssistEnabledCheckBox";
            smartPreAimEnabledCheckBox = AddCheckBox(behaviorPanel, "\u667a\u80fd\u9009\u602a", 12, 78, 120, false);
            smartPreAimEnabledCheckBox.Name = "smartPreAimEnabledCheckBox";
            smartPreAimEnabledCheckBox.Click += (_, _) => RefreshCombatModeVisibility();
            smartPreAimUseFightTargetPositionCheckBox = AddCheckBox(
                behaviorPanel,
                "\u6309\u5f53\u524d\u602a\u4f4d\u7f6e\u9009\u602a",
                164,
                78,
                186,
                false);
            smartPreAimUseFightTargetPositionCheckBox.Name = "smartPreAimUseFightTargetPositionCheckBox";
            smartPreAimResponsiveSwitchingCheckBox = AddCheckBox(
                behaviorPanel,
                "\u7075\u654f\u5207\u6362",
                380,
                78,
                120,
                false);
            smartPreAimResponsiveSwitchingCheckBox.Name = "smartPreAimResponsiveSwitchingCheckBox";
            radarEditorButton = AddButton(
                environmentPanel,
                "\u7ed8\u5236\u96f7\u8fbe",
                12,
                82,
                156,
                34,
                (_, _) => OpenRadarEditor());
            radarEditorButton.Name = "radarEditorButton";
            radarStatusLabel = AddLabel(
                environmentPanel,
                string.Empty,
                184,
                84,
                620,
                30,
                _textGreen,
                FontStyle.Regular);
            radarStatusLabel.Name = "radarStatusLabel";
            AddLabel(environmentPanel, "\u56fa\u5b9a\u9891\u9053", 380, 44, 80, 24);
            fixedChannelCombo = AddCombo(
                environmentPanel,
                468,
                42,
                152,
                28,
                new[] { "\u4e0d\u56fa\u5b9a" }
                    .Concat(Enumerable.Range(1, ScriptSettings.MaximumFixedChannelNumber).Select(number => number + "\u9891\u9053"))
                    .ToArray());
            fixedChannelCombo.Name = "fixedChannelCombo";
            RefreshSmartPreAimOriginControlState();
            RefreshCombatModeVisibility();

            return tab;
        }

        private void RefreshCombatModeVisibility()
        {
            var visible = ParseMainMode(mainModeCombo?.Text) == AccountMainMode.CustomCombat;
            var combatMode = ParseCombatMode(combatModeCombo?.Text);
            var stationaryVisible = visible && combatMode == AccountCombatMode.Stationary;
            var pathVisible = visible && combatMode == AccountCombatMode.Path;
            var smartPreAimOptionsVisible = stationaryVisible && smartPreAimEnabledCheckBox?.Checked == true;
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

            if (stalledTargetExclusionSecondsLabel is not null)
            {
                stalledTargetExclusionSecondsLabel.Visible = stationaryVisible;
            }

            if (stalledTargetExclusionSecondsTextBox is not null)
            {
                stalledTargetExclusionSecondsTextBox.Visible = stationaryVisible;
            }

            if (stalledTargetExclusionSecondsUnitLabel is not null)
            {
                stalledTargetExclusionSecondsUnitLabel.Visible = stationaryVisible;
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

            if (returnHomeWhenNoTargetCheckBox is not null)
            {
                returnHomeWhenNoTargetCheckBox.Visible = stationaryVisible;
            }

            if (sitWhenNoTargetAtHomeCheckBox is not null)
            {
                sitWhenNoTargetAtHomeCheckBox.Visible = stationaryVisible;
            }

            if (smartPreAimEnabledCheckBox is not null)
            {
                smartPreAimEnabledCheckBox.Visible = stationaryVisible;
            }

            if (smartPreAimUseFightTargetPositionCheckBox is not null)
            {
                smartPreAimUseFightTargetPositionCheckBox.Visible = smartPreAimOptionsVisible;
            }

            if (smartPreAimResponsiveSwitchingCheckBox is not null)
            {
                smartPreAimResponsiveSwitchingCheckBox.Visible = smartPreAimOptionsVisible;
            }

            if (radarEditorButton is not null)
            {
                radarEditorButton.Visible = stationaryVisible && _radarMapStore is not null;
            }

            if (radarStatusLabel is not null)
            {
                radarStatusLabel.Visible = stationaryVisible && _radarMapStore is not null;
            }

            RefreshSmartPreAimOriginControlState();
        }

        private void OpenRadarEditor()
        {
            if (_radarMapStore is null)
            {
                MessageBox.Show(
                    this,
                    "\u96f7\u8fbe\u5730\u56fe\u5b58\u50a8\u672a\u521d\u59cb\u5316\u3002",
                    "\u7ed8\u5236\u96f7\u8fbe",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var editor = new RadarEditorForm(
                _account,
                _runtime,
                _radarMapStore,
                _folderLauncher,
                currentRadarSettings,
                ApplyRadarSettingsFromEditor);
            editor.ShowDialog(this);
            RefreshRadarStatus();
        }

        private Core.Common.OperationResult ApplyRadarSettingsFromEditor(RadarObstacleScriptSettings settings)
        {
            var previous = currentRadarSettings.Clone();
            currentRadarSettings = settings.Clone();
            if (!SaveCurrentSettings(out var error))
            {
                currentRadarSettings = previous;
                RefreshRadarStatus();
                return Core.Common.OperationResult.Fail(error);
            }

            return Core.Common.OperationResult.Ok();
        }

        private void RefreshRadarStatus()
        {
            if (radarStatusLabel is null)
            {
                return;
            }

            radarStatusLabel.Text = currentRadarSettings.Enabled
                ? "\u7ed5\u969c\uff1a\u5df2\u5f00\u542f"
                : "\u7ed5\u969c\uff1a\u672a\u5f00\u542f";
            radarStatusLabel.ForeColor = currentRadarSettings.Enabled
                ? _darkGreen
                : Color.FromArgb(107, 114, 128);
        }

        private void RefreshSmartPreAimOriginControlState()
        {
            if (smartPreAimUseFightTargetPositionCheckBox is not null)
            {
                smartPreAimUseFightTargetPositionCheckBox.Enabled = smartPreAimEnabledCheckBox?.Checked == true;
            }

            if (smartPreAimResponsiveSwitchingCheckBox is not null)
            {
                smartPreAimResponsiveSwitchingCheckBox.Enabled = smartPreAimEnabledCheckBox?.Checked == true;
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

        private async void LoadSelectedProfile()
        {
            if (loadingProfileCombo || resolvingProfilePaths)
            {
                return;
            }

            var name = GetSelectedProfileName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                resolvingProfilePaths = true;
                try
                {
                    var resolved = new Dictionary<PathEditorControls, string>();
                    foreach (var editor in pathEditors.Values)
                    {
                        if (!await ResolvePathDraftAsync(editor).ConfigureAwait(true))
                        {
                            SelectProfileComboItem(profileNameTextBox?.Text, loadProfile: false);
                            return;
                        }
                        resolved[editor] = PathDraftFingerprint(editor);
                    }
                    if (resolved.Any(entry => IsPathEditorBusy(entry.Key) || PathDraftFingerprint(entry.Key) != entry.Value))
                    {
                        SelectProfileComboItem(profileNameTextBox?.Text, loadProfile: false);
                        return;
                    }
                    LoadProfileByName(name);
                }
                finally { resolvingProfilePaths = false; }
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

            var settings = result.Value.Settings.Clone();
            var nameListLoadError = TryApplySharedBagCleanupNameLists(settings);
            ApplyScriptSettings(settings);
            if (!string.IsNullOrWhiteSpace(nameListLoadError))
            {
                SetBagCleanupInventoryStatus(
                    "黑白名单读取失败，背包清理启动时将被禁用: " + nameListLoadError,
                    true);
            }
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

        private void SetStalledTargetExclusionSeconds(CombatScriptSettings combat)
        {
            var seconds = combat.StalledTargetExclusionSeconds <= 0
                ? CombatScriptSettings.DefaultStalledTargetExclusionSeconds
                : Math.Clamp(
                    combat.StalledTargetExclusionSeconds,
                    CombatScriptSettings.MinimumStalledTargetExclusionSeconds,
                    CombatScriptSettings.MaximumStalledTargetExclusionSeconds);
            SetText(
                stalledTargetExclusionSecondsTextBox,
                seconds.ToString(CultureInfo.InvariantCulture));
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

        private void SetRevivePathAggressiveClearRadius(PathScriptSettings paths)
        {
            var radius = paths.RevivePathAggressiveClearRadius <= 0.0D
                ? PathScriptSettings.DefaultRevivePathAggressiveClearRadius
                : Math.Min(paths.RevivePathAggressiveClearRadius, 500.0D);
            SetText(
                revivePathAggressiveClearRadiusTextBox,
                radius.ToString("F1", CultureInfo.InvariantCulture));
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

            AddLabel(page, "当前使用路径", 12, 4, 130, 22, _textGreen, FontStyle.Bold);
            pathOverviewLabels[SharedPathKind.Revive] = AddLabel(page, "复活路径:  未选（0点）", 12, 28, 394, 22);
            pathOverviewLabels[SharedPathKind.Combat] = AddLabel(page, "打怪路径:  未选（0点）", 430, 28, 394, 22);
            pathOverviewLabels[SharedPathKind.Maintenance] = AddLabel(page, "清包路径:  未选（0点）", 12, 52, 394, 22);
            pathOverviewLabels[SharedPathKind.Gather] = AddLabel(page, "采集路径:  未选（0点）", 430, 52, 394, 22);
            AddLabel(page, "末尾加点间距", 12, 82, 104, 22, _textGreen, FontStyle.Bold);
            pathRecordingMinimumDistanceTextBox = AddTextBox(
                page,
                PathScriptSettings.DefaultRecordingMinimumDistance.ToString("0.###", CultureInfo.InvariantCulture),
                124,
                78,
                80,
                28);
            AddLabel(page, "米", 212, 82, 28, 22);
            AddLabel(page, "转向重置坐标", 430, 82, 104, 22, _textGreen, FontStyle.Bold);
            deathReviveClickPointTextBox = AddTextBox(
                page,
                FormatScreenPoint(PathScriptSettings.DefaultDeathReviveClickX, PathScriptSettings.DefaultDeathReviveClickY),
                540,
                78,
                150,
                28);
            deathReviveTestMoveButton = AddButton(page, "测试移动", 702, 78, 110, 28, TestDeathReviveMoveButton_Click);

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
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Gather, "采集路径", "采集路线点配置", false));
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Auction, "拍卖行路径", "从挂机点到交易中介，完成后原路返回", false));
            pathTabs.TabPages.Add(CreatePathEditorTab(SharedPathKind.Stall, "摆摊路径", "从挂机点到仓库 / 摆摊位置，完成后回城走复活路径", false));
            page.Controls.Add(pathTabs);
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(836, 520);
            page.SizeChanged += (_, _) => pathTabs.Size = new Size(Math.Max(836, page.ClientSize.Width), Math.Max(400, page.ClientSize.Height - pathTabs.Top));
            foreach (var label in pathOverviewLabels.Values) label.AutoEllipsis = true;

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

            page.AutoScroll = true;
            var contentOffset = kind == SharedPathKind.Maintenance ? 72 : kind == SharedPathKind.Auction ? 40 : 0;
            page.AutoScrollMinSize = new Size(824, kind == SharedPathKind.Maintenance ? 488 : 430 + contentOffset);
            var editor = new PathEditorControls(kind);
            pathEditors[kind] = editor;

            AddLabel(page, caption, 12, 6, 330, 22, _textGreen, FontStyle.Bold);
            var pathNameTextBox = AddTextBox(page, includeSamplePoint ? "穆尔海姆00133" : string.Empty, 80, 38, 270, 28);
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
            else if (kind == SharedPathKind.Auction) { auctionPathNameTextBox = pathNameTextBox; }
            else if (kind == SharedPathKind.Stall) { stallPathNameTextBox = pathNameTextBox; }
            else if (kind == SharedPathKind.Gather)
            {
                gatherPathNameTextBox = pathNameTextBox;
            }

            AddLabel(page, "路径名称", 12, 42, 64, 22);
            editor.SavedPathCombo = AddCombo(page, 452, 38, 360, 28);
            editor.SavedPathCombo.SelectedIndexChanged += (_, _) => LoadSelectedPath(editor);
            AddLabel(page, "已存路径", 376, 42, 72, 22);

            editor.SaveButton = AddButton(page, "保存修改", 12, 74, 100, 30, (_, _) => SavePath(editor));
            AddPathMoreMenu(page, editor);
            editor.DirtyLabel = AddLabel(page, "已保存", 350, 6, 96, 22);
            editor.DirtyLabel.AutoEllipsis = true;
            if (kind is SharedPathKind.Combat or SharedPathKind.Revive)
            {
                editor.BindStationaryRadiusCheckBox = AddCheckBox(page, "绑定原地打半径", 452, 76, 150, false);
                editor.BindStationaryRadiusCheckBox.Name = kind == SharedPathKind.Revive
                    ? "bindRevivePathStationaryRadiusCheckBox" : "bindPathStationaryRadiusCheckBox";
                editor.StationaryRadiusTextBox = AddTextBox(page, "30.0", 608, 74, 76, 28);
                editor.StationaryRadiusTextBox.Name = kind == SharedPathKind.Revive
                    ? "revivePathBoundStationaryRadiusTextBox" : "pathBoundStationaryRadiusTextBox";
                editor.StationaryRadiusTextBox.Enabled = false;
                editor.BindStationaryRadiusCheckBox.Click += (_, _) =>
                    editor.StationaryRadiusTextBox.Enabled = editor.BindStationaryRadiusCheckBox.Checked;
                AddLabel(page, "米", 692, 78, 24, 22);
                AddLabel(page, "半径同时绑定时，优先使用复活路径", 452, 6, 360, 22);
            }

            if (kind == SharedPathKind.Maintenance)
            {
                var maintenanceOptions = new Panel { Location = new Point(12, 110), Size = new Size(800, 68), BackColor = _softGreen };
                page.Controls.Add(maintenanceOptions);
                bagCleanupReturnByReversePathCheckBox = AddCheckBox(
                    maintenanceOptions,
                    "清完包原路返回复活点",
                    8,
                    2,
                    194,
                    true);
                AddLabel(maintenanceOptions, "清包返程按键", 230, 4, 96, 22, _textGreen, FontStyle.Bold);
                bagCleanupTownReturnKeyButton = AddButton(maintenanceOptions, "选择按键", 330, 2, 106, 28);
                bagCleanupTownReturnKeyButton.Click += (_, _) =>
                {
                    var selectedKey = ShowKeyboardPicker(bagCleanupTownReturnKeyButton.Tag as string);
                    if (!string.IsNullOrWhiteSpace(selectedKey))
                    {
                        SetKeyButton(bagCleanupTownReturnKeyButton, selectedKey);
                    }
                };

                AddLabel(maintenanceOptions, "回程按键", 490, 4, 70, 22, _textGreen, FontStyle.Bold);
                townReturnKeyButton = AddButton(maintenanceOptions, "选择按键", 566, 2, 104, 28);
                townReturnKeyButton.Click += (_, _) =>
                {
                    var selectedKey = ShowKeyboardPicker(townReturnKeyButton.Tag as string);
                    if (!string.IsNullOrWhiteSpace(selectedKey))
                    {
                        SetKeyButton(townReturnKeyButton, selectedKey);
                    }
                };

                AddPathNpcSelection(maintenanceOptions, editor, "清包NPC", 36);
            }
            else if (kind == SharedPathKind.Auction)
            {
                var auctionOptions = new Panel { Location = new Point(12, 110), Size = new Size(800, 36), BackColor = _softGreen };
                page.Controls.Add(auctionOptions);
                AddPathNpcSelection(auctionOptions, editor, "拍卖NPC", 4);
                AddLabel(auctionOptions, "随路径保存，执行前核对交易中介身份", 452, 6, 340, 24);
            }

            editor.SummaryLabel = AddLabel(page, "点数  0  |  总距  0.0  |  跳过  0", 12, 112 + contentOffset, 300, 24, _textGreen, FontStyle.Bold);
            editor.StatusLabel = AddLabel(page, "等待读取坐标", 350, 112 + contentOffset, 462, 24);

            editor.ManualButton = AddButton(page, "添加当前位置到末尾", 12, 144 + contentOffset, 170, 30, (_, _) => AddManualPathPoint(editor));
            var executePathX = 720;
            editor.ExecutePathButton = AddButton(
                page,
                kind == SharedPathKind.Gather ? "执行采集" : "执行路径",
                executePathX,
                144 + contentOffset,
                92,
                30);
            if (kind == SharedPathKind.Gather)
            {
                editor.ExecutePathButton.Enabled = false;
            }
            else
            {
                editor.ExecutePathButton.Click += async (_, _) => await ExecutePathAsync(editor).ConfigureAwait(true);
            }

            if (kind == SharedPathKind.Gather)
            {
                AddGatherPointEditorControls(page, editor);
            }
            else
            {
                AddPathPointList(page, editor, 184 + contentOffset);
            }

            var pointEditorTop = kind == SharedPathKind.Gather ? 350 : kind == SharedPathKind.Maintenance ? 284 + contentOffset : 326 + contentOffset;
            AddPathPointEditControls(page, editor, pointEditorTop);
            var pathAdvanced = CreateFoldout(
                page,
                "高级路径设置",
                pointEditorTop + 76,
                824,
                true);
            pathAdvanced.Content.Height = kind == SharedPathKind.Revive ? 68 : 40;
            editor.StatusLabel.AutoEllipsis = true;
            var loopCheckBox = AddCheckBox(pathAdvanced.Content, "循环路径", 6, 12, 92, true);
            var reverseCheckBox = AddCheckBox(pathAdvanced.Content, "到终点反向", 102, 12, 106, false);
            var deathStopCheckBox = AddCheckBox(pathAdvanced.Content, "死亡停止路径", 206, 12, 130, true);
            if (kind == SharedPathKind.Revive)
            {
                loopPathCheckBox = loopCheckBox;
                reverseAtEndCheckBox = reverseCheckBox;
                deathStopPathCheckBox = deathStopCheckBox;
                AddLabel(pathAdvanced.Content, "主动怪清除半径", 6, 42, 126, 22, _textGreen, FontStyle.Bold);
                revivePathAggressiveClearRadiusTextBox = AddTextBox(
                    pathAdvanced.Content,
                    PathScriptSettings.DefaultRevivePathAggressiveClearRadius.ToString("F1", CultureInfo.InvariantCulture),
                    136,
                    38,
                    70,
                    28);
                revivePathAggressiveClearRadiusTextBox.Name = "revivePathAggressiveClearRadiusTextBox";
                AddLabel(pathAdvanced.Content, "米", 212, 42, 28, 22);
            }

            InitializePathDraftTracking(editor);
            RefreshPathEditor(editor);
            return tab;
        }

        private void AddGatherPointEditorControls(Control page, PathEditorControls editor)
        {
            var pointsList = new ListView
            {
                BackColor = _inputBackground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = _textGreen,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                Location = new Point(6, 184),
                MultiSelect = false,
                Size = new Size(500, 158),
                UseCompatibleStateImageBehavior = false,
                View = View.Details
            };
            pointsList.Columns.Add("#", 42, HorizontalAlignment.Center);
            pointsList.Columns.Add("坐标", 214, HorizontalAlignment.Left);
            pointsList.Columns.Add("路径点动作", 226, HorizontalAlignment.Left);
            pointsList.SelectedIndexChanged += (_, _) => PopulatePathPointEditor(editor);
            editor.GatherPointsList = pointsList;
            page.Controls.Add(pointsList);

            editor.SelectedGatherPointLabel = AddLabel(
                page,
                "所选路径点: 未选择",
                518,
                184,
                118,
                26,
                _textGreen,
                FontStyle.Bold);
            editor.ReadNearestGatherButton = AddButton(page, "读取附近", 644, 184, 126, 26);
            editor.ReadNearestGatherButton.Enabled = false;
            editor.ManualGatherEntryButton = AddButton(page, "手动", 778, 184, 60, 26);
            editor.ManualGatherEntryButton.Click += (_, _) =>
                SetGatherManualEntryMode(editor, !editor.ManualGatherEntryEnabled);

            AddLabel(page, "附近采集物", 518, 216, 72, 28);
            editor.GatherCandidateCombo = AddCombo(page, 590, 216, 248, 28);
            editor.GatherCandidateCombo.DropDownWidth = 380;
            editor.GatherCandidateCombo.SelectedIndexChanged += (_, _) =>
                ApplyGatherCandidateSelection(editor);

            AddLabel(page, "SourceId", 518, 248, 58, 28);
            editor.GatherSourceIdTextBox = AddTextBox(page, string.Empty, 576, 248, 100, 28);
            editor.GatherSourceIdTextBox.ReadOnly = true;
            editor.GatherSourceIdTextBox.TabStop = false;
            editor.GatherSourceTypeLabel = AddLabel(
                page,
                "类别 —  |  采集类型 —",
                684,
                248,
                154,
                28);
            editor.GatherStaticInfoLabel = AddLabel(
                page,
                "熟练度 —  |  角色等级 —  |  理论次数 —  |  条件 —",
                518,
                280,
                320,
                26);

            AddLabel(page, "按键", 518, 312, 40, 30);
            editor.GatherKeyButton = AddButton(page, "选择按键", 558, 312, 82, 30);
            editor.GatherKeyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(editor.GatherKeyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    SetKeyButton(editor.GatherKeyButton, selectedKey);
                }
            };
            AddButton(page, "绑定到点", 648, 312, 90, 30, (_, _) => BindGatherAction(editor));
            AddButton(page, "取消绑定", 746, 312, 92, 30, (_, _) => ClearGatherAction(editor));
        }

        private void PopulateGatherPointEditor(PathEditorControls editor)
        {
            if (editor.RefreshingGatherPoints)
            {
                return;
            }

            var point = GetSelectedGatherPoint(editor);
            if (point is null)
            {
                if (editor.SelectedGatherPointLabel is not null)
                {
                    editor.SelectedGatherPointLabel.Text = "所选路径点: 未选择";
                }

                SetGatherManualEntryMode(editor, false);
                SetGatherCandidate(editor, null);
                SetText(editor.GatherSourceIdTextBox, string.Empty);
                SetKeyButton(editor.GatherKeyButton, null);
                ResetGatherStaticInfo(editor);
                return;
            }

            if (editor.SelectedGatherPointLabel is not null)
            {
                editor.SelectedGatherPointLabel.Text =
                    "所选路径点: #" + point.Index.ToString(CultureInfo.InvariantCulture);
            }

            var action = point.GatherActions?.FirstOrDefault();
            SetGatherManualEntryMode(editor, false);
            SetGatherCandidate(editor, action);
            SetText(
                editor.GatherSourceIdTextBox,
                action is null
                    ? string.Empty
                    : action.ExpectedGatherSourceId.ToString(CultureInfo.InvariantCulture));
            SetKeyButton(editor.GatherKeyButton, action?.GatherKey);
            ResetGatherStaticInfo(editor);
        }

        private static void SetGatherCandidate(PathEditorControls editor, GatherPointAction? action)
        {
            if (editor.GatherCandidateCombo is not { } combo)
            {
                return;
            }

            combo.SelectedIndex = -1;
            combo.Items.Clear();
            combo.Text = string.Empty;
            if (action is null)
            {
                return;
            }

            combo.Items.Add(
                new GatherCandidateComboItem(
                    action.GatherName,
                    action.ExpectedGatherSourceId,
                    distanceMeters: null));
            combo.SelectedIndex = 0;
        }

        private static void ApplyGatherCandidateSelection(PathEditorControls editor)
        {
            if (editor.GatherCandidateCombo?.SelectedItem is not GatherCandidateComboItem candidate)
            {
                return;
            }

            SetText(
                editor.GatherSourceIdTextBox,
                candidate.GatherSourceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            ResetGatherStaticInfo(editor);
        }

        private static void ResetGatherStaticInfo(PathEditorControls editor)
        {
            if (editor.GatherSourceTypeLabel is not null)
            {
                editor.GatherSourceTypeLabel.Text = "类别 —  |  采集类型 —";
            }

            if (editor.GatherStaticInfoLabel is not null)
            {
                editor.GatherStaticInfoLabel.Text =
                    "熟练度 —  |  角色等级 —  |  理论次数 —  |  条件 —";
            }
        }

        private static void SetGatherManualEntryMode(PathEditorControls editor, bool enabled)
        {
            var currentName = GetGatherName(editor, string.Empty);
            var currentSourceId = TryParseGatherSourceId(
                editor.GatherSourceIdTextBox?.Text,
                out var parsedSourceId)
                ? parsedSourceId
                : (uint?)null;

            editor.ManualGatherEntryEnabled = enabled;
            if (editor.GatherCandidateCombo is { } combo)
            {
                if (enabled)
                {
                    combo.DropDownStyle = ComboBoxStyle.DropDown;
                    combo.SelectedIndex = -1;
                    combo.Text = currentName;
                }
                else
                {
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    if (!string.IsNullOrWhiteSpace(currentName) &&
                        combo.SelectedItem is not GatherCandidateComboItem)
                    {
                        combo.Items.Clear();
                        combo.Items.Add(
                            new GatherCandidateComboItem(
                                currentName,
                                currentSourceId,
                                distanceMeters: null));
                        combo.SelectedIndex = 0;
                    }
                }
            }

            if (editor.GatherSourceIdTextBox is not null)
            {
                editor.GatherSourceIdTextBox.ReadOnly = !enabled;
                editor.GatherSourceIdTextBox.TabStop = enabled;
            }

            if (editor.ManualGatherEntryButton is not null)
            {
                editor.ManualGatherEntryButton.Text = enabled ? "锁定" : "手动";
            }
        }

        private static string GetGatherName(PathEditorControls editor, string fallback)
        {
            if (!editor.ManualGatherEntryEnabled &&
                editor.GatherCandidateCombo?.SelectedItem is GatherCandidateComboItem candidate)
            {
                return string.IsNullOrWhiteSpace(candidate.GatherName)
                    ? fallback
                    : candidate.GatherName;
            }

            return string.IsNullOrWhiteSpace(editor.GatherCandidateCombo?.Text)
                ? fallback
                : editor.GatherCandidateCombo.Text.Trim();
        }

        private void BindGatherAction(PathEditorControls editor)
        {
            if (IsPathEditorBusy(editor)) return;
            var point = GetSelectedGatherPoint(editor);
            if (point is null)
            {
                SetPathStatus(editor, "请先在左侧选择一个路径点", true);
                return;
            }

            if (!TryParseGatherSourceId(editor.GatherSourceIdTextBox?.Text, out var gatherSourceId))
            {
                SetPathStatus(editor, "GatherSourceId 必须是大于 0 的十进制或十六进制数", true);
                return;
            }

            var gatherKey = editor.GatherKeyButton?.Tag as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(gatherKey))
            {
                SetPathStatus(editor, "请为这个采集物类别选择按键", true);
                return;
            }

            var gatherName = GetGatherName(
                editor,
                "采集物 " + gatherSourceId.ToString(CultureInfo.InvariantCulture));

            editor.History.Remember(editor.Buffer.ToDocument(string.Empty), SelectedPathPointIndex(editor));
            foreach (var pathPoint in editor.Buffer.Points)
            {
                foreach (var sameTypeAction in (pathPoint.GatherActions ?? new List<GatherPointAction>())
                             .Where(action => action.ExpectedGatherSourceId == gatherSourceId))
                {
                    sameTypeAction.GatherName = gatherName;
                    sameTypeAction.GatherKey = gatherKey;
                }
            }

            var previousAction = point.GatherActions?.FirstOrDefault();
            point.GatherActions = new List<GatherPointAction>
            {
                new()
                {
                    ExpectedGatherSourceId = gatherSourceId,
                    GatherName = gatherName,
                    GatherKey = gatherKey,
                    SearchRadiusMeters = previousAction?.ExpectedGatherSourceId == gatherSourceId
                        ? previousAction.SearchRadiusMeters
                        : GatherPointAction.DefaultSearchRadiusMeters,
                    OccupiedCheckRadiusMeters = previousAction?.ExpectedGatherSourceId == gatherSourceId
                        ? previousAction.OccupiedCheckRadiusMeters
                        : GatherPointAction.DefaultOccupiedCheckRadiusMeters
                }
            };

            SetGatherManualEntryMode(editor, false);
            RefreshPathEditor(editor);
            SetPathStatus(
                editor,
                "已绑定 #" + point.Index.ToString(CultureInfo.InvariantCulture) +
                ": " + gatherName +
                " / " + FormatSkillKey(gatherKey) +
                "（同类按键已同步）",
                false);
        }

        private void ClearGatherAction(PathEditorControls editor)
        {
            if (IsPathEditorBusy(editor)) return;
            var point = GetSelectedGatherPoint(editor);
            if (point is null)
            {
                SetPathStatus(editor, "请先在左侧选择一个路径点", true);
                return;
            }

            editor.History.Remember(editor.Buffer.ToDocument(string.Empty), SelectedPathPointIndex(editor));
            point.GatherActions = new List<GatherPointAction>();
            SetGatherManualEntryMode(editor, false);
            RefreshPathEditor(editor);
            SetPathStatus(
                editor,
                "路径点 #" + point.Index.ToString(CultureInfo.InvariantCulture) + " 已设为普通移动点",
                false);
        }

        private static SharedPathPoint? GetSelectedGatherPoint(PathEditorControls editor)
        {
            return editor.GatherPointsList?.SelectedItems.Count > 0
                ? editor.GatherPointsList.SelectedItems[0].Tag as SharedPathPoint
                : null;
        }

        private static bool TryParseGatherSourceId(string? text, out uint gatherSourceId)
        {
            var value = text?.Trim() ?? string.Empty;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(
                           value[2..],
                           NumberStyles.AllowHexSpecifier,
                           CultureInfo.InvariantCulture,
                           out gatherSourceId) &&
                       gatherSourceId > 0;
            }

            return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out gatherSourceId) &&
                   gatherSourceId > 0;
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
            if (!pathEditors.TryGetValue(kind, out var editor))
            {
                return;
            }

            editor.LoadedDocument = null;
            editor.Buffer.Clear();
            editor.History.Clear();
            ApplyPathRadiusBindingToEditor(editor, null);
            if (editor.SavedPathCombo is not null)
            {
                var wasLoading = loadingPathCombos;
                loadingPathCombos = true;
                try
                {
                    editor.SavedPathCombo.SelectedIndex = -1;
                }
                finally
                {
                    loadingPathCombos = wasLoading;
                }
            }
            if (string.IsNullOrWhiteSpace(pathName))
            {
                MarkPathDraftSaved(editor);
                RefreshPathEditor(editor);
                RefreshPathOverviews();
                return;
            }

            if (!SelectPathComboItem(editor, pathName, loadPath: true))
            {
                MarkPathDraftSaved(editor);
                RefreshPathEditor(editor);
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

        private async void LoadSelectedPath(PathEditorControls editor)
        {
            if (loadingPathCombos)
            {
                return;
            }

            var name = GetSelectedPathName(editor);
            if (string.IsNullOrWhiteSpace(name)) return;
            var previousName = editor.LoadedDocument?.Name;
            if (await ResolvePathDraftAsync(editor).ConfigureAwait(true))
            {
                if (!LoadPathByName(editor, name)) RestorePathCombo(editor, editor.LoadedDocument?.Name);
            }
            else RestorePathCombo(editor, previousName);
        }

        private bool LoadPathByName(PathEditorControls editor, string name)
        {
            var result = _pathStore.LoadAsync(name).GetAwaiter().GetResult();
            if (!result.Success || result.Value is null)
            {
                SetPathStatus(editor, result.Error ?? "加载路径失败", true);
                return false;
            }

            editor.Buffer.Load(result.Value.Points, result.Value.MapId);
            editor.History.Clear();
            editor.PendingSelection = 0;
            editor.LoadedDocument = result.Value.Clone();
            ApplyPathRadiusBindingToEditor(editor, result.Value);
            editor.SkippedCount = 0;
            SetText(editor.PathNameTextBox, result.Value.Name);
            SetCleanupNpcSelection(editor, editor.Kind == SharedPathKind.Auction ? result.Value.AuctionNpcName : result.Value.CleanupNpcName);
            if (editor.Kind == SharedPathKind.Maintenance)
            {
                ApplyBagCleanupPathClickPoints(result.Value);
            }

            RefreshPathEditor(editor);
            RefreshPathOverviews();
            MarkPathDraftSaved(editor);
            SelectPathComboItem(editor, name, loadPath: false);
            SetPathStatus(editor, "已加载路径: " + result.Value.Name, false);
            return true;
        }

        private async void SavePath(PathEditorControls editor)
        {
            await SavePathAsync(editor).ConfigureAwait(true);
        }

        private void ApplyPathRadiusBindingToEditor(PathEditorControls editor, SharedPathDocument? document)
        {
            if (editor.BindStationaryRadiusCheckBox is null)
            {
                return;
            }

            var bound = document?.BoundStationaryCombatRadius;
            SetChecked(editor.BindStationaryRadiusCheckBox, bound.HasValue);
            if (editor.StationaryRadiusTextBox is not null)
            {
                editor.StationaryRadiusTextBox.Enabled = bound.HasValue;
            }
            SetText(editor.StationaryRadiusTextBox,
                (bound ?? ReadDouble(stationaryCombatRadiusTextBox, 30.0D, 1.0D, 500.0D))
                .ToString("G", CultureInfo.InvariantCulture));
        }

        private static bool TryReadPathRadiusBinding(PathEditorControls editor, out double? radius, out string error)
        {
            radius = null;
            error = string.Empty;
            if (editor.BindStationaryRadiusCheckBox?.Checked != true)
            {
                return true;
            }

            var text = GetText(editor.StationaryRadiusTextBox, string.Empty);
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                 !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) ||
                !double.IsFinite(value) || value is < 1.0D or > 500.0D)
            {
                error = "原地打半径请输入 1–500 米的有效数值";
                return false;
            }
            radius = value;
            return true;
        }

        private async Task SavePathAsync(PathEditorControls editor)
        {
            if (IsPathEditorBusy(editor))
            {
                return;
            }

            var name = GetText(editor.PathNameTextBox, string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                SetPathStatus(editor, "路径名不能为空", true);
                return;
            }

            var document = editor.Buffer.ToDocument(name);
            var bindingChecked = editor.BindStationaryRadiusCheckBox?.Checked == true;
            var bindingText = GetText(editor.StationaryRadiusTextBox, string.Empty);
            if (!TryReadPathRadiusBinding(editor, out var radius, out var bindingError))
            {
                SetPathStatus(editor, bindingError, true);
                return;
            }
            document.BoundStationaryCombatRadius = radius;

            if (editor.Kind == SharedPathKind.Maintenance)
            {
                document.CleanupNpcName = GetSelectedCleanupNpcName(editor);
                CopyBagCleanupClickPointsToPath(document);
            }
            else if (editor.Kind == SharedPathKind.Auction)
            {
                document.AuctionNpcName = GetSelectedCleanupNpcName(editor);
            }

            var loadedDocument = editor.LoadedDocument?.Clone();
            var savedFingerprint = PathDraftFingerprint(editor);
            editor.SavingPath = true;
            RefreshPathEditState(editor);
            try
            {
                // A shared file may also be edited from another path tab. Preserve its latest
                // metadata, and replace only the fields owned by this editor.
                var existing = await _pathStore.LoadAsync(name).ConfigureAwait(true);
                if (!existing.Success && currentPathSummaries.Any(path =>
                    string.Equals(path.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    SetPathStatus(editor, existing.Error ?? "读取原路径失败，未覆盖保存", true);
                    return;
                }

                var merged = (existing.Value ?? loadedDocument)?.Clone() ?? new SharedPathDocument();
                if (existing.Value is null)
                {
                    merged.CreatedAt = document.CreatedAt;
                }
                merged.Name = document.Name;
                merged.Points = document.Points;
                merged.MapId = document.MapId;
                // Both revive and combat editors can own this field. An unchanged draft must
                // not overwrite a newer binding saved through the other editor.
                if (editor.BindStationaryRadiusCheckBox is not null &&
                    (existing.Value is null || loadedDocument is null ||
                     !string.Equals(loadedDocument.Name, name, StringComparison.OrdinalIgnoreCase) ||
                     document.BoundStationaryCombatRadius != loadedDocument.BoundStationaryCombatRadius))
                {
                    merged.BoundStationaryCombatRadius = document.BoundStationaryCombatRadius;
                }
                if (editor.Kind == SharedPathKind.Maintenance)
                {
                    merged.CleanupNpcName = document.CleanupNpcName;
                    merged.BagCleanupSellItemClickX = document.BagCleanupSellItemClickX;
                    merged.BagCleanupSellItemClickY = document.BagCleanupSellItemClickY;
                    merged.BagCleanupSellButtonClickX = document.BagCleanupSellButtonClickX;
                    merged.BagCleanupSellButtonClickY = document.BagCleanupSellButtonClickY;
                }
                else if (editor.Kind == SharedPathKind.Auction)
                {
                    merged.AuctionNpcName = document.AuctionNpcName;
                }

                var result = await _pathStore.SaveAsync(merged).ConfigureAwait(true);
                if (!result.Success)
                {
                    SetPathStatus(editor, result.Error ?? "保存路径失败", true);
                    return;
                }

                RefreshPathLibrary();
                if (string.Equals(GetText(editor.PathNameTextBox, string.Empty), name, StringComparison.Ordinal))
                {
                    editor.LoadedDocument = merged.Clone();
                    var draftStillCurrent = PathDraftFingerprint(editor) == savedFingerprint;
                    if ((editor.BindStationaryRadiusCheckBox?.Checked == true) == bindingChecked &&
                        GetText(editor.StationaryRadiusTextBox, string.Empty) == bindingText)
                    {
                        ApplyPathRadiusBindingToEditor(editor, merged);
                    }
                    editor.SavedFingerprint = draftStillCurrent ? PathDraftFingerprint(editor) : savedFingerprint;
                    SelectPathComboItem(editor, name, loadPath: false);
                    SetPathStatus(editor, "已保存共享路径: " + name, false);
                }
                RefreshPathOverviews();
            }
            finally
            {
                editor.SavingPath = false;
                RefreshPathEditState(editor);
            }
        }

        private void OpenPathLibraryFolder(PathEditorControls editor)
        {
            var result = _folderLauncher.Open(_pathLibraryDirectory);
            SetPathStatus(
                editor,
                result.Success
                    ? "已打开路径文件夹"
                    : result.Error ?? "打开路径文件夹失败",
                !result.Success);
        }

        private async void DeleteSavedPath(PathEditorControls editor)
        {
            if (IsPathEditorBusy(editor)) return;
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

            editor.SavingPath = true;
            RefreshPathEditState(editor);
            try
            {
                var result = await _pathStore.DeleteAsync(name).ConfigureAwait(true);
                if (!result.Success)
                {
                    SetPathStatus(editor, result.Error ?? "删除路径失败", true);
                    return;
                }
                // Retain the current points as an explicitly unsaved draft after deleting its file.
                editor.LoadedDocument = null;
                editor.SavedFingerprint = string.Empty;
                RefreshPathLibrary();
                RestorePathCombo(editor, null);
                SetPathStatus(editor, "已删除共享路径，当前坐标保留为未保存草稿: " + name, false);
            }
            finally { editor.SavingPath = false; RefreshPathEditState(editor); }
        }

        private async void AddManualPathPoint(PathEditorControls editor)
        {
            if (IsPathEditorBusy(editor)) return;
            editor.ReadingPosition = true;
            RefreshPathEditState(editor);
            try
            {
                await AddCurrentPlayerPointAsync(editor, "手动录点", showSkipped: true, dense: false).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { SetPathStatus(editor, "读取坐标超时，请稍后重试", true); }
            catch (Exception ex) { SetPathStatus(editor, "读取坐标失败: " + ex.Message, true); }
            finally { editor.ReadingPosition = false; RefreshPathEditState(editor); }
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
            pathRecordTimer.Interval = PathRecordTimerIntervalMs;
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
                await AddCurrentPlayerPointAsync(editor, "自动录点", showSkipped: false, dense: true).ConfigureAwait(true);
            }
            finally
            {
                pathRecordReadInFlight = false;
            }
        }

        private async Task AddCurrentPlayerPointAsync(PathEditorControls editor, string reason, bool showSkipped, bool dense)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var scene = await _runtime.ReadSceneForPathRecordingAsync(_account, timeout.Token).ConfigureAwait(true);
            if (!scene.IsReady)
            {
                SetPathStatus(editor, "地图加载中，等待角色恢复后继续录点", false);
                return;
            }
            var before = editor.Buffer.ToDocument(string.Empty);
            var selection = SelectedPathPointIndex(editor);
            if (!editor.Buffer.AcceptRecordingMap(scene.Channel!.MapId))
            {
                StopPathRecording(editor);
                SetPathStatus(editor, "当前地图与路线不一致，已停止录点", true);
                return;
            }
            var player = scene.Player!;
            var position = player.Position!.Value;

            var minimumDistanceMeters = ReadDouble(
                pathRecordingMinimumDistanceTextBox,
                PathScriptSettings.DefaultRecordingMinimumDistance,
                PathRecordingBuffer.MinimumAllowedDistanceMeters,
                PathRecordingBuffer.MaximumAllowedDistanceMeters);
            var addResult = dense
                ? editor.Buffer.TryAddDense(position, player.CapturedAt, minimumDistanceMeters)
                : editor.Buffer.TryAdd(position, player.CapturedAt, minimumDistanceMeters);
            if (!addResult.Success)
            {
                editor.SkippedCount++;
                RefreshPathEditor(editor);
                if (showSkipped)
                {
                    SetPathStatus(editor, addResult.Error ?? "距离不足当前最小录制距离，未录点", true);
                }

                return;
            }

            editor.History.Remember(before, selection);
            editor.PendingSelection = editor.Buffer.Count - 1;
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
            if (IsPathEditorBusy(editor) || editor.Buffer.Count == 0) return;
            if (MessageBox.Show(this, "清空当前路径的全部坐标？可以撤销恢复。", "清空路径",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            editor.History.Remember(editor.Buffer.ToDocument(string.Empty), SelectedPathPointIndex(editor));
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

            if (IsPathEditorBusy(editor)) return;
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
            RefreshPathEditState(editor);
            button.Text = "停止路径";
            SetPathStatus(editor, "正在执行路径", false);

            try
            {
                var result = await _runtime
                    .ExecutePathAsync(
                        _account,
                        GetText(editor.PathNameTextBox, "manual_path"),
                        editor.Buffer.Points.Select(point => point.Clone()).ToArray(),
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
                RefreshPathEditState(editor);
            }
        }

        private void RefreshPathEditor(PathEditorControls editor)
        {
            if (editor.PointsTextBox is not null)
            {
                editor.PointsTextBox.Text = editor.Buffer.ToCoordinateText();
            }

            if (editor.GatherPointsList is not null)
            {
                RefreshGatherPointList(editor);
            }

            RefreshPathEditState(editor);
            if (editor.SummaryLabel is not null)
            {
                editor.SummaryLabel.Text =
                    "点数  " + editor.Buffer.Count.ToString(CultureInfo.InvariantCulture) +
                    "  |  总距  " + editor.Buffer.TotalDistance.ToString("F1", CultureInfo.InvariantCulture) +
                    "  |  跳过  " + editor.SkippedCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RefreshGatherPointList(PathEditorControls editor)
        {
            if (editor.GatherPointsList is not { } pointsList)
            {
                return;
            }

            var selectedIndex = editor.PendingSelection ?? SelectedPathPointIndex(editor);
            editor.PendingSelection = null;
            if (selectedIndex < 0 && editor.Buffer.Count > 0)
            {
                selectedIndex = editor.Buffer.Count - 1;
            }
            else if (selectedIndex >= editor.Buffer.Count)
            {
                selectedIndex = editor.Buffer.Count - 1;
            }

            editor.RefreshingGatherPoints = true;
            pointsList.BeginUpdate();
            try
            {
                pointsList.Items.Clear();
                foreach (var point in editor.Buffer.Points)
                {
                    var action = point.GatherActions?.FirstOrDefault();
                    var actionText = action is null
                        ? "普通移动点"
                        : (string.IsNullOrWhiteSpace(action.GatherName)
                            ? "采集物"
                            : action.GatherName) +
                          " / " +
                          action.ExpectedGatherSourceId.ToString(CultureInfo.InvariantCulture) +
                          " / " +
                          FormatSkillKey(action.GatherKey);
                    var item = new ListViewItem(point.Index.ToString(CultureInfo.InvariantCulture))
                    {
                        Tag = point
                    };
                    item.SubItems.Add(
                        point.X.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                        point.Y.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                        point.Z.ToString("F2", CultureInfo.InvariantCulture));
                    item.SubItems.Add(editor.Kind == SharedPathKind.Gather
                        ? actionText : point.Index == 1 ? "—" : point.SegmentDistance.ToString("F2", CultureInfo.InvariantCulture) + " 米");
                    pointsList.Items.Add(item);
                }

                if (selectedIndex >= 0 && selectedIndex < pointsList.Items.Count)
                {
                    pointsList.Items[selectedIndex].Selected = true;
                    pointsList.Items[selectedIndex].Focused = true;
                    pointsList.EnsureVisible(selectedIndex);
                }
            }
            finally
            {
                pointsList.EndUpdate();
                editor.RefreshingGatherPoints = false;
            }

            PopulatePathPointEditor(editor);
        }

        private void AddPathNpcSelection(Panel panel, PathEditorControls editor, string label, int top)
        {
            AddLabel(panel, label, 8, top + 2, 72, 24, _textGreen, FontStyle.Bold);
            editor.CleanupNpcRefreshButton = AddButton(panel, "刷新附近NPC", 330, top, 106, 28);
            editor.CleanupNpcRefreshButton.Click += async (_, _) =>
                await RefreshCleanupNpcsAsync(editor).ConfigureAwait(true);
            editor.CleanupNpcCombo = AddCombo(panel, 82, top, 238, 28);
            if (editor.Kind == SharedPathKind.Auction)
            {
                editor.CleanupNpcCombo.Name = "auctionPathNpcCombo";
                editor.CleanupNpcRefreshButton.Name = "auctionPathNpcRefreshButton";
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
                var objects = await _runtime.RefreshWorldObjectsAsync(_account).ConfigureAwait(true);
                var count = PopulateCleanupNpcCombo(editor, objects);
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
            SetPathOverview(SharedPathKind.Gather, "采集路径", gatherPathNameTextBox?.Text);
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
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(748, 0);
            tab.Controls.Add(page);

            AddLabel(page, "坐地板维护", 4, 8, 82, 24, _textGreen, FontStyle.Bold);
            sitMaintenanceCheckBox = AddCheckBox(page, "启用", 84, 6, 70, true);

            AddLabel(page, "血量低于", 4, 44, 66, 24);
            sitHpBelowTextBox = AddTextBox(page, "25", 68, 42, 70, 28);
            AddLabel(page, "%  坐地板，恢复到", 144, 44, 130, 24);
            sitHpRecoverToTextBox = AddTextBox(page, "75", 272, 42, 70, 28);
            AddLabel(page, "%  起来继续打怪", 348, 44, 160, 24);

            AddLabel(page, "蓝量低于", 4, 78, 66, 24);
            sitMpBelowTextBox = AddTextBox(page, "10", 68, 76, 70, 28);
            AddLabel(page, "%  坐地板，恢复到", 144, 78, 130, 24);
            sitMpRecoverToTextBox = AddTextBox(page, "90", 272, 76, 70, 28);
            AddLabel(page, "%  起来继续打怪", 348, 78, 160, 24);

            var refreshMaintenanceSkillsButton = AddButton(page, "刷新技能", 720, 6, 120, 30);
            refreshMaintenanceSkillsButton.Click += async (_, _) =>
                await RefreshCurrentSkillsAsync(refreshMaintenanceSkillsButton, availableTree: null, systemTree: null).ConfigureAwait(true);

            var sections = new FlowLayoutPanel
            {
                Name = "maintenanceSections",
                Location = new Point(12, 128),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(828, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };
            page.Controls.Add(sections);
            var hpSection = CreateMaintenanceRuleSection(sections, "血量维护", "hpMaintenance");
            hpMaintenanceRuleList = hpSection.List;
            hpMaintenanceEmptyLabel = hpSection.EmptyLabel;
            AddButton(hpSection.Content, "新增血量维护", 12, 4, 132, 30,
                (_, _) => AddMaintenanceKeyRuleRow(hpMaintenanceRuleList, hpMaintenanceEmptyLabel));

            var mpSection = CreateMaintenanceRuleSection(sections, "蓝量维护", "mpMaintenance");
            mpMaintenanceRuleList = mpSection.List;
            mpMaintenanceEmptyLabel = mpSection.EmptyLabel;
            AddButton(mpSection.Content, "新增蓝量维护", 12, 4, 132, 30,
                (_, _) => AddMaintenanceKeyRuleRow(mpMaintenanceRuleList, mpMaintenanceEmptyLabel));

            var statusSection = CreateMaintenanceRuleSection(sections, "状态维护", "statusMaintenance");
            statusMaintenanceRuleList = statusSection.List;
            statusMaintenanceEmptyLabel = statusSection.EmptyLabel;
            AddButton(statusSection.Content, "新增状态维护", 12, 4, 132, 30,
                (_, _) => AddStatusMaintenanceRuleRow(statusMaintenanceRuleList, statusMaintenanceEmptyLabel));

            var dpSection = CreateMaintenanceRuleSection(sections, "DP维护", "dpMaintenance");
            dpMaintenanceRuleList = dpSection.List;
            dpMaintenanceEmptyLabel = dpSection.EmptyLabel;
            AddButton(dpSection.Content, "新增DP维护", 12, 4, 132, 30,
                (_, _) => AddDpMaintenanceRuleRow(dpMaintenanceRuleList, dpMaintenanceEmptyLabel));

            void ResizeSections()
            {
                var width = Math.Max(724, page.ClientSize.Width - 24);
                refreshMaintenanceSkillsButton.Left = width - refreshMaintenanceSkillsButton.Width + 12;
                sections.MaximumSize = new Size(width, 0);
                sections.MinimumSize = new Size(width, 0);
                foreach (Control section in sections.Controls)
                {
                    section.Width = width;
                }
            }
            page.ClientSizeChanged += (_, _) => ResizeSections();
            ResizeSections();

            return tab;
        }

        private MaintenanceRuleSection CreateMaintenanceRuleSection(FlowLayoutPanel parent, string title, string name)
        {
            var section = new Panel
            {
                Name = name + "Section",
                Size = new Size(828, 36),
                Margin = new Padding(0, 0, 0, 12)
            };
            parent.Controls.Add(section);
            var header = new Button
            {
                Name = name + "FoldoutButton",
                Size = new Size(828, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _inputBackground,
                ForeColor = _textGreen,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            header.FlatAppearance.BorderSize = 0;
            section.Controls.Add(header);
            var content = new Panel
            {
                Location = new Point(0, 44),
                Width = 828,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false
            };
            section.Controls.Add(content);
            var list = CreateMaintenanceRuleList(content, 12, 42, 804, 28);
            list.AutoScroll = false;
            list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var emptyLabel = AddLabel(content, "暂无" + title, 12, 42, 300, 24);
            emptyLabel.BringToFront();
            var expanded = false;

            void RefreshSection()
            {
                var rows = list.Controls.OfType<Panel>().ToArray();
                list.Height = Math.Max(28, rows.Sum(row => row.Height + row.Margin.Vertical));
                content.Height = list.Bottom + 8;
                section.Height = expanded ? content.Bottom : header.Height;
                header.Text = $"{(expanded ? "▼" : "▶")}  {title}（{rows.Length} 项）";
                RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
            }

            header.Click += (_, _) =>
            {
                expanded = !expanded;
                content.Visible = expanded;
                RefreshSection();
            };
            list.ControlAdded += (_, _) => RefreshSection();
            list.ControlRemoved += (_, _) => RefreshSection();
            RefreshSection();
            return new MaintenanceRuleSection(content, list, emptyLabel);
        }

        private sealed record MaintenanceRuleSection(Panel Content, FlowLayoutPanel List, Label EmptyLabel);

        private TabPage CreateBagCleanupTab()
        {
            var tab = CreateBaseTab("清包");
            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(852, 556);
            tab.Controls.Add(page);
            bagCleanupRuleControls.Clear();

            var optionsPanel = new Panel
            {
                BackColor = _inputBackground,
                Location = new Point(12, 10),
                Size = new Size(828, 64)
            };
            page.Controls.Add(optionsPanel);
            var rulesPanel = new Panel { Location = new Point(12, 90), Size = new Size(404, 460) };
            var namesPanel = new Panel { Location = new Point(432, 90), Size = new Size(408, 460) };
            page.Controls.Add(rulesPanel);
            page.Controls.Add(namesPanel);

            bagCleanupEnabledCheckBox = AddCheckBox(optionsPanel, "自动清包", 12, 20, 112, false);
            bagCleanupEnabledCheckBox.BackColor = optionsPanel.BackColor;
            AddLabel(optionsPanel, "剩余格低于", 148, 20, 92, 26, _textGreen, FontStyle.Bold);
            bagCleanupThresholdTextBox = AddTextBox(optionsPanel, "5", 244, 18, 72, 28);
            BuildCleanupWorkflowOptions(page, optionsPanel, rulesPanel, namesPanel);

            const int leftOptionX = 8;
            const int leftComboX = 130;
            const int rightOptionX = 210;
            const int rightComboX = 334;
            const int cleanupOptionWidth = 118;

            void AddCleanupOption(BagCleanupRuleConfig rule, int optionX, int comboX, int y)
            {
                var checkBox = AddCheckBox(rulesPanel, rule.DisplayName, optionX, y, cleanupOptionWidth, false);
                checkBox.Font = new Font("Microsoft YaHei UI", 8.25F);
                checkBox.Size = new Size(cleanupOptionWidth, 22);

                var combo = AddCombo(rulesPanel, comboX, y - 1, 62, 24, "出售", "丢弃");
                combo.Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Bold);
                SetComboText(combo, FormatBagCleanupAction(rule.Action));
                bagCleanupRuleControls[rule.Key] = new BagCleanupRuleControls(checkBox, combo);
            }

            AddLabel(rulesPanel, "清理物品类型", 0, 0, 96, 26, _textGreen, FontStyle.Bold);
            var testAuctionButton = AddButton(rulesPanel, "测试拍卖行", 96, 0, 104, 28);
            testAuctionButton.Name = "testAuctionHouseButton";
            testAuctionButton.Click += async (_, _) => await TestAuctionHouseAsync(testAuctionButton).ConfigureAwait(true);
            var testPersonalShopButton = AddButton(rulesPanel, "测试摆摊", 204, 0, 96, 28);
            testPersonalShopButton.Name = "testPersonalShopButton";
            testPersonalShopButton.Click += async (_, _) => await TestPersonalShopAsync(testPersonalShopButton).ConfigureAwait(true);
            var testDiscardButton = AddButton(rulesPanel, "测试丢弃", 308, 0, 96, 28);
            testDiscardButton.Name = "testInventoryDiscardButton";
            testDiscardButton.Click += async (_, _) => await TestInventoryDiscardAsync(testDiscardButton).ConfigureAwait(true);

            void AddCategory(string title, int top)
            {
                var heading = AddLabel(rulesPanel, "  " + title, 0, top, 404, 24, _textGreen, FontStyle.Bold);
                heading.BackColor = _inputBackground;
            }

            AddCategory("装备品质", 36);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.GreenEquipment), leftOptionX, leftComboX, 68);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.BlueEquipment), rightOptionX, rightComboX, 68);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.WhiteEquipment), leftOptionX, leftComboX, 98);

            AddCategory("魔石", 134);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.WhiteManastone), leftOptionX, leftComboX, 166);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.GreenManastone), rightOptionX, rightComboX, 166);

            AddCategory("书卷", 202);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.Stigma), leftOptionX, leftComboX, 234);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.RecipeScroll), rightOptionX, rightComboX, 234);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.SkillBook), leftOptionX, leftComboX, 264);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.SpellBook), rightOptionX, rightComboX, 264);

            AddCategory("提炼石", 300);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.WhiteExtractionStone), leftOptionX, leftComboX, 332);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.GreenExtractionStone), rightOptionX, rightComboX, 332);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.BlueExtractionStone), leftOptionX, leftComboX, 362);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.GoldExtractionStone), rightOptionX, rightComboX, 362);

            AddCategory("药品", 398);
            AddCleanupOption(GetDefaultBagCleanupRule(BagCleanupRuleCatalog.Medicine), leftOptionX, leftComboX, 430);

            bagCleanupWhitelistRadio = AddRadioButton(namesPanel, "白名单（不丢弃）", 0, 0, 144, true);
            bagCleanupWhitelistRadio.Name = "bagCleanupWhitelistRadio";
            bagCleanupBlacklistRadio = AddRadioButton(namesPanel, "黑名单（丢弃）", 144, 0, 132, false);
            bagCleanupBlacklistRadio.Name = "bagCleanupBlacklistRadio";
            bagCleanupStallRadio = AddRadioButton(namesPanel, "摆摊", 276, 0, 60, false);
            bagCleanupStallRadio.Name = "bagCleanupStallRadio";
            bagCleanupAuctionHouseRadio = AddRadioButton(namesPanel, "拍卖行", 336, 0, 72, false);
            bagCleanupAuctionHouseRadio.Name = "bagCleanupAuctionHouseRadio";
            bagCleanupWhitelistRadio.CheckedChanged += (_, _) => RefreshBagCleanupNameListEditor();
            bagCleanupBlacklistRadio.CheckedChanged += (_, _) => RefreshBagCleanupNameListEditor();
            bagCleanupStallRadio.CheckedChanged += (_, _) => RefreshBagCleanupNameListEditor();
            bagCleanupAuctionHouseRadio.CheckedChanged += (_, _) => RefreshBagCleanupNameListEditor();

            var refreshInventoryButton = AddButton(namesPanel, "刷新背包", 300, 36, 108, 30);
            refreshInventoryButton.Click += async (_, _) =>
                await RefreshBagCleanupInventoryAsync(refreshInventoryButton).ConfigureAwait(true);

            AddLabel(namesPanel, "背包物品 / 关键字", 0, 40, 200, 24, _textGreen, FontStyle.Bold);
            bagCleanupManualNameTextBox = AddTextBox(namesPanel, string.Empty, 0, 76, 280, 28);
            bagCleanupManualNameTextBox.Name = "bagCleanupManualNameTextBox";
            bagCleanupAddNameButton = AddButton(namesPanel, "加入不丢弃", 292, 75, 116, 30);
            bagCleanupAddNameButton.Name = "bagCleanupAddNameButton";
            bagCleanupAddNameButton.Click += async (_, _) =>
                await AddSelectedBagCleanupNameAsync().ConfigureAwait(true);

            bagCleanupInventoryCheckedListBox = CreateBagCleanupInventoryCheckedListBox(namesPanel, 0, 114, 408, 126);
            bagCleanupInventoryCheckedListBox.BackColor = Color.White;
            bagCleanupInventoryCheckedListBox.Name = "bagCleanupInventoryCheckedListBox";
            bagCleanupInventoryStatusLabel = AddLabel(namesPanel, "等待刷新背包", 0, 244, 408, 24);

            bagCleanupNameListTitleLabel = AddLabel(namesPanel, "白名单：以下物品不丢弃", 0, 280, 236, 24, _textGreen, FontStyle.Bold);
            bagCleanupExcludedItemListBox = CreateFilterListBox(namesPanel, 0, 316, 408, 138);
            bagCleanupExcludedItemListBox.BackColor = Color.White;
            bagCleanupTradeItemGrid = CreateBagCleanupTradeItemGrid(namesPanel);
            bagCleanupRemoveNameButton = AddButton(namesPanel, "移除", 248, 276, 72, 30);
            bagCleanupRemoveNameButton.Click += async (_, _) =>
                await RemoveSelectedBagCleanupNameAsync().ConfigureAwait(true);
            bagCleanupClearNamesButton = AddButton(namesPanel, "清空", 336, 276, 72, 30);
            bagCleanupClearNamesButton.Click += async (_, _) =>
                await ClearSelectedBagCleanupNameListAsync().ConfigureAwait(true);

            return tab;
        }

        private TabPage CreateTeamTab()
        {
            var tab = CreateBaseTab("组队");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "组队模式", 4, 16, 90, 24, _textGreen, FontStyle.Bold);
            teamRoleCombo = AddCombo(page, 24, 52, 190, 28, "队长", "输出", "治疗");
            teamRoleCombo.Name = "teamRoleCombo";
            AddLabel(page, "抱团距离", 240, 52, 90, 24, _textGreen, FontStyle.Bold);
            teamGroupDistanceTextBox = AddTextBox(page, "20.0", 330, 50, 72, 28);
            teamGroupDistanceTextBox.Name = "teamGroupDistanceTextBox";
            AddLabel(page, "m", 408, 52, 24, 24, _textGreen, FontStyle.Bold);

            var leaderPanel = CreateTeamRolePanel(page);
            teamLeaderPanel = leaderPanel;
            AddLabel(leaderPanel, "队长开关", 4, 0, 90, 24, _textGreen, FontStyle.Bold);
            teamLeaderEnabledCheckBox = AddCheckBox(leaderPanel, "启用组队", 24, 30, 92, false);
            teamLeaderDungeonModeCheckBox = AddCheckBox(leaderPanel, "刷本模式", 132, 30, 92, false);
            teamLeaderAllowSelfDefenseCheckBox = AddCheckBox(leaderPanel, "允许自卫", 240, 30, 92, true);
            teamLeaderStopAdvanceWhenMemberDisconnectedCheckBox = AddCheckBox(leaderPanel, "队员掉线停止推进", 24, 62, 170, false);
            teamLeaderTacticalMarkCheckBox = AddCheckBox(leaderPanel, "攻击目标标记", 24, 94, 130, false);
            teamLeaderTacticalMarkKeyLabel = AddLabel(leaderPanel, "标记键", 170, 97, 62, 24, _textGreen, FontStyle.Bold);
            teamLeaderTacticalMarkKeyButton = AddTeamKeyButton(
                leaderPanel,
                236,
                92,
                TeamLeaderScriptSettings.DefaultTacticalMarkKey);

            var dpsPanel = CreateTeamRolePanel(page);
            teamOutputPanel = dpsPanel;
            AddLabel(dpsPanel, "输出队员开关", 4, 0, 110, 24, _textGreen, FontStyle.Bold);
            teamOutputEnabledCheckBox = AddCheckBox(dpsPanel, "启用组队", 24, 30, 92, false);
            teamOutputDungeonModeCheckBox = AddCheckBox(dpsPanel, "刷本模式", 132, 30, 92, false);
            teamOutputAllowSelfDefenseCheckBox = AddCheckBox(dpsPanel, "允许自卫", 240, 30, 92, true);
            teamOutputFollowLeaderCheckBox = AddCheckBox(dpsPanel, "跟随队长", 24, 62, 92, true);
            teamOutputOnlyAttackLeaderMarkedTargetCheckBox = AddCheckBox(dpsPanel, "只打队长标记", 132, 62, 130, true);
            teamOutputStopWhenLeaderHasNoTargetCheckBox = AddCheckBox(dpsPanel, "队长无目标停手", 278, 62, 150, true);
            teamOutputStopWhenLeaderDeadCheckBox = AddCheckBox(dpsPanel, "队长死亡停手", 444, 62, 130, true);
            AddLabel(dpsPanel, "和队长距离", 24, 94, 90, 24, _textGreen, FontStyle.Bold);
            teamOutputLeaderDistanceTextBox = AddTextBox(dpsPanel, "12.0", 116, 92, 72, 28);
            AddLabel(dpsPanel, "m", 194, 94, 24, 24, _textGreen, FontStyle.Bold);
            AddLabel(dpsPanel, "切目标键", 240, 94, 72, 24, _textGreen, FontStyle.Bold);
            teamOutputAssistTargetKeyButton = AddTeamKeyButton(
                dpsPanel,
                316,
                92,
                TeamOutputScriptSettings.DefaultAssistTargetKey);
            teamOutputTacticalMarkTargetingCheckBox = AddCheckBox(dpsPanel, "战术标记选怪", 24, 126, 130, false);
            teamOutputSelectTacticalMarkTargetKeyLabel = AddLabel(dpsPanel, "选标记键", 170, 129, 74, 24, _textGreen, FontStyle.Bold);
            teamOutputSelectTacticalMarkTargetKeyButton = AddTeamKeyButton(
                dpsPanel,
                248,
                124,
                TeamOutputScriptSettings.DefaultSelectTacticalMarkTargetKey);

            var supportPanel = CreateTeamRolePanel(page);
            teamSupportPanel = supportPanel;
            AddLabel(supportPanel, "治疗队员开关", 4, 0, 110, 24, _textGreen, FontStyle.Bold);
            teamSupportEnabledCheckBox = AddCheckBox(supportPanel, "启用组队", 24, 30, 92, false);
            teamSupportDungeonModeCheckBox = AddCheckBox(supportPanel, "刷本模式", 132, 30, 92, false);
            teamSupportJoinCombatCheckBox = AddCheckBox(supportPanel, "加入打怪", 240, 30, 92, false);
            teamSupportMentalCleanseCheckBox = AddCheckBox(supportPanel, "精神解除", 24, 62, 92, true);
            teamSupportPhysicalCleanseCheckBox = AddCheckBox(supportPanel, "肉体解除", 132, 62, 92, true);
            teamSupportAllowSelfDefenseCheckBox = AddCheckBox(supportPanel, "允许自卫", 240, 62, 92, false);
            teamSupportStopWhenLeaderDeadCheckBox = AddCheckBox(supportPanel, "队长死亡停手", 386, 62, 130, true);
            AddLabel(supportPanel, "和队长距离", 24, 94, 90, 24, _textGreen, FontStyle.Bold);
            teamSupportLeaderDistanceTextBox = AddTextBox(supportPanel, "12.0", 116, 92, 72, 28);
            AddLabel(supportPanel, "m", 194, 94, 24, 24, _textGreen, FontStyle.Bold);
            teamSupportTacticalMarkTargetingCheckBox = AddCheckBox(
                supportPanel,
                "\u6218\u672f\u6807\u8bb0\u9009\u602a",
                240,
                94,
                130,
                false);
            teamSupportSelectTacticalMarkTargetKeyLabel = AddLabel(
                supportPanel,
                "\u9009\u6807\u8bb0\u952e",
                386,
                97,
                74,
                24,
                _textGreen,
                FontStyle.Bold);
            teamSupportSelectTacticalMarkTargetKeyButton = AddTeamKeyButton(
                supportPanel,
                464,
                92,
                TeamSupportScriptSettings.DefaultSelectTacticalMarkTargetKey);
            AddLabel(supportPanel, "加血技能", 24, 132, 70, 24, _textGreen, FontStyle.Bold);
            teamHealSkillRuleList = CreateMaintenanceRuleList(supportPanel, 24, 166, 790, 82);
            teamHealSkillEmptyLabel = AddLabel(supportPanel, "暂无加血技能", 24, 166, 140, 24);
            teamHealSkillEmptyLabel.BringToFront();
            AddButton(
                supportPanel,
                "新增加血技能",
                94,
                128,
                120,
                30,
                (_, _) => AddTeamHealSkillRuleRow(teamHealSkillRuleList, teamHealSkillEmptyLabel));
            var refreshTeamHealSkillsButton = AddButton(supportPanel, "刷新技能", 222, 128, 90, 30);
            refreshTeamHealSkillsButton.Click += async (_, _) =>
            {
                await RefreshCurrentSkillsAsync(refreshTeamHealSkillsButton, availableTree: null, systemTree: null).ConfigureAwait(true);
                RefreshMaintenanceSkillCombos(teamHealSkillRuleList);
            };

            AddLabel(supportPanel, "解状态按键", 24, 266, 90, 24, _textGreen, FontStyle.Bold);
            AddLabel(supportPanel, "精神解除", 24, 300, 70, 24, _textGreen, FontStyle.Bold);
            teamMentalCleanseKeyButton = AddTeamKeyButton(supportPanel, 94, 297, "NumPad8");
            AddLabel(supportPanel, "肉体解除", 220, 300, 70, 24, _textGreen, FontStyle.Bold);
            teamPhysicalCleanseKeyButton = AddTeamKeyButton(supportPanel, 290, 297, "NumPad7");
            AddLabel(supportPanel, "群体解除", 416, 300, 70, 24, _textGreen, FontStyle.Bold);
            teamGroupCleanseKeyButton = AddTeamKeyButton(supportPanel, 486, 297, string.Empty);

            teamMentalCleanseSkillCombo = AddCombo(supportPanel, 24, 330, 184, 28);
            PopulateMaintenanceSkillCombo(teamMentalCleanseSkillCombo, 0, string.Empty);
            BindAutomaticSkillButton(teamMentalCleanseKeyButton, teamMentalCleanseSkillCombo,
                () => !HasSpiritmasterSkillSelection(GetSelectedMaintenanceSkill(teamMentalCleanseSkillCombo)));
            teamPhysicalCleanseSkillCombo = AddCombo(supportPanel, 220, 330, 184, 28);
            PopulateMaintenanceSkillCombo(teamPhysicalCleanseSkillCombo, 0, string.Empty);
            BindAutomaticSkillButton(teamPhysicalCleanseKeyButton, teamPhysicalCleanseSkillCombo,
                () => !HasSpiritmasterSkillSelection(GetSelectedMaintenanceSkill(teamPhysicalCleanseSkillCombo)));
            teamGroupCleanseSkillCombo = AddCombo(supportPanel, 416, 330, 184, 28);
            PopulateMaintenanceSkillCombo(teamGroupCleanseSkillCombo, 0, string.Empty);
            BindAutomaticSkillButton(teamGroupCleanseKeyButton, teamGroupCleanseSkillCombo,
                () => !HasSpiritmasterSkillSelection(GetSelectedMaintenanceSkill(teamGroupCleanseSkillCombo)));
            AddLabel(supportPanel, "选技能自动匹配\n留空时手动设置", 616, 297, 194, 61);

            teamRoleCombo.SelectedIndexChanged += (_, _) => RefreshTeamRolePanelVisibility();
            teamLeaderTacticalMarkCheckBox.Click += (_, _) => RefreshTeamTacticalMarkKeyVisibility();
            teamOutputTacticalMarkTargetingCheckBox.Click += (_, _) => RefreshTeamTacticalMarkKeyVisibility();
            teamSupportTacticalMarkTargetingCheckBox.Click += (_, _) => RefreshTeamTacticalMarkKeyVisibility();
            RefreshTeamRolePanelVisibility();
            RefreshTeamTacticalMarkKeyVisibility();

            return tab;
        }

        private Panel CreateTeamRolePanel(Control parent)
        {
            var panel = new Panel
            {
                BackColor = _pageBackground,
                Location = new Point(0, 96),
                Size = new Size(830, 360)
            };

            parent.Controls.Add(panel);
            return panel;
        }

        private void AddTeamHealSkillRuleRow(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            int belowPercent = 80,
            string key = "NumPad1",
            uint skillId = 0,
            string skillName = "",
            MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
            TeamHealSkillTargetType targetType = TeamHealSkillTargetType.Single)
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
                Size = new Size(764, 31)
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

            var belowTextBox = AddTextBox(
                row,
                Math.Clamp(belowPercent, 0, 100).ToString(CultureInfo.InvariantCulture),
                36,
                1,
                54,
                28);
            belowTextBox.Name = "teamHealRuleBelowTextBox";

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(94, 3),
                Size = new Size(18, 24),
                Text = "%",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var timingCombo = AddCombo(row, 116, 1, 90, 28);
            timingCombo.Name = "teamHealRuleTimingCombo";
            PopulateMaintenanceTimingCombo(timingCombo, runTiming);

            var targetTypeCombo = AddCombo(row, 212, 1, 76, 28, "单体", "群体");
            targetTypeCombo.Name = "teamHealRuleTargetTypeCombo";
            if (targetType == TeamHealSkillTargetType.Group)
            {
                targetTypeCombo.SelectedIndex = 1;
            }

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(296, 3),
                Size = new Size(34, 24),
                Text = "技能",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var skillCombo = AddCombo(row, 332, 1, 220, 28);
            skillCombo.Name = "maintenanceRuleSkillCombo";
            PopulateMaintenanceSkillCombo(skillCombo, skillId, skillName);

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(560, 3),
                Size = new Size(34, 24),
                Text = "按键",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var keyButton = AddTeamKeyButton(row, 594, 0, key);
            keyButton.Name = "teamHealRuleKeyButton";
            var deleteButton = AddButton(row, "删除", 706, 0, 58, 30);
            deleteButton.Click += (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
                RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
            };

            BindAutomaticSkillButton(keyButton, skillCombo);
            list.Controls.Add(row);
            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private Button AddTeamKeyButton(Control parent, int x, int y, string key)
        {
            var keyButton = AddButton(parent, "选择按键", x, y, 104, 30);
            SetKeyButton(keyButton, key);
            keyButton.Click += (_, _) =>
            {
                var selectedKey = ShowKeyboardPicker(keyButton.Tag as string);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                {
                    SetKeyButton(keyButton, selectedKey);
                }
            };

            return keyButton;
        }

        private void RefreshTeamRolePanelVisibility()
        {
            if (teamRoleCombo is null ||
                teamLeaderPanel is null ||
                teamOutputPanel is null ||
                teamSupportPanel is null)
            {
                return;
            }

            UpdateTeamRolePanelVisibility(teamRoleCombo, teamLeaderPanel, teamOutputPanel, teamSupportPanel);
        }

        private void RefreshTeamTacticalMarkKeyVisibility()
        {
            UpdateOptionalSettingVisibility(
                teamLeaderTacticalMarkCheckBox?.Checked == true,
                teamLeaderTacticalMarkKeyLabel,
                teamLeaderTacticalMarkKeyButton);
            UpdateOptionalSettingVisibility(
                teamOutputTacticalMarkTargetingCheckBox?.Checked == true,
                teamOutputSelectTacticalMarkTargetKeyLabel,
                teamOutputSelectTacticalMarkTargetKeyButton);
            UpdateOptionalSettingVisibility(
                teamSupportTacticalMarkTargetingCheckBox?.Checked == true,
                teamSupportSelectTacticalMarkTargetKeyLabel,
                teamSupportSelectTacticalMarkTargetKeyButton);
        }

        private static void UpdateOptionalSettingVisibility(bool visible, params Control?[] controls)
        {
            foreach (var control in controls)
            {
                if (control is not null)
                {
                    control.Visible = visible;
                }
            }
        }

        private void ApplyTeamSettings(TeamScriptSettings? settings)
        {
            var team = (settings ?? new TeamScriptSettings()).Clone();
            SetComboText(teamRoleCombo, FormatTeamRole(team.Role));
            SetText(
                teamGroupDistanceTextBox,
                team.GroupDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture));

            var leader = team.Leader ?? new TeamLeaderScriptSettings();
            SetChecked(teamLeaderEnabledCheckBox, leader.Enabled);
            SetChecked(teamLeaderDungeonModeCheckBox, leader.DungeonMode);
            SetChecked(teamLeaderAllowSelfDefenseCheckBox, leader.AllowSelfDefense);
            SetChecked(
                teamLeaderStopAdvanceWhenMemberDisconnectedCheckBox,
                leader.StopAdvanceWhenMemberDisconnected);
            SetChecked(teamLeaderTacticalMarkCheckBox, leader.TacticalMarkEnabled);
            SetKeyButton(
                teamLeaderTacticalMarkKeyButton,
                string.IsNullOrWhiteSpace(leader.TacticalMarkKey)
                    ? TeamLeaderScriptSettings.DefaultTacticalMarkKey
                    : leader.TacticalMarkKey);
            var output = team.Output ?? new TeamOutputScriptSettings();
            SetChecked(teamOutputEnabledCheckBox, output.Enabled);
            SetChecked(teamOutputDungeonModeCheckBox, output.DungeonMode);
            SetChecked(teamOutputAllowSelfDefenseCheckBox, output.AllowSelfDefense);
            SetChecked(teamOutputFollowLeaderCheckBox, output.FollowLeader);
            SetChecked(teamOutputOnlyAttackLeaderMarkedTargetCheckBox, output.OnlyAttackLeaderMarkedTarget);
            SetChecked(teamOutputTacticalMarkTargetingCheckBox, output.TacticalMarkTargetingEnabled);
            SetChecked(teamOutputStopWhenLeaderHasNoTargetCheckBox, output.StopWhenLeaderHasNoTarget);
            SetChecked(teamOutputStopWhenLeaderDeadCheckBox, output.StopWhenLeaderDead);
            SetText(
                teamOutputLeaderDistanceTextBox,
                output.LeaderDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture));
            SetKeyButton(
                teamOutputAssistTargetKeyButton,
                string.IsNullOrWhiteSpace(output.AssistTargetKey)
                    ? TeamOutputScriptSettings.DefaultAssistTargetKey
                    : output.AssistTargetKey);
            SetKeyButton(
                teamOutputSelectTacticalMarkTargetKeyButton,
                string.IsNullOrWhiteSpace(output.SelectTacticalMarkTargetKey)
                    ? TeamOutputScriptSettings.DefaultSelectTacticalMarkTargetKey
                    : output.SelectTacticalMarkTargetKey);

            var support = team.Support ?? new TeamSupportScriptSettings();
            SetChecked(teamSupportEnabledCheckBox, support.Enabled);
            SetChecked(teamSupportDungeonModeCheckBox, support.DungeonMode);
            SetChecked(teamSupportJoinCombatCheckBox, support.JoinCombat);
            SetChecked(teamSupportMentalCleanseCheckBox, support.MentalCleanseEnabled);
            SetChecked(teamSupportPhysicalCleanseCheckBox, support.PhysicalCleanseEnabled);
            SetChecked(teamSupportAllowSelfDefenseCheckBox, support.AllowSelfDefense);
            SetChecked(teamSupportStopWhenLeaderDeadCheckBox, support.StopWhenLeaderDead);
            SetChecked(teamSupportTacticalMarkTargetingCheckBox, support.TacticalMarkTargetingEnabled);
            SetText(
                teamSupportLeaderDistanceTextBox,
                support.LeaderDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture));
            SetKeyButton(
                teamSupportSelectTacticalMarkTargetKeyButton,
                string.IsNullOrWhiteSpace(support.SelectTacticalMarkTargetKey)
                    ? TeamSupportScriptSettings.DefaultSelectTacticalMarkTargetKey
                    : support.SelectTacticalMarkTargetKey);
            PopulateTeamHealSkillRules(teamHealSkillRuleList, teamHealSkillEmptyLabel, support.HealSkillRules);
            SetKeyButton(teamMentalCleanseKeyButton, support.MentalCleanseKey);
            if (teamMentalCleanseSkillCombo is not null) PopulateMaintenanceSkillCombo(teamMentalCleanseSkillCombo, support.MentalCleanseSkillId, support.MentalCleanseSkillName);
            SetKeyButton(teamPhysicalCleanseKeyButton, support.PhysicalCleanseKey);
            if (teamPhysicalCleanseSkillCombo is not null) PopulateMaintenanceSkillCombo(teamPhysicalCleanseSkillCombo, support.PhysicalCleanseSkillId, support.PhysicalCleanseSkillName);
            SetKeyButton(teamGroupCleanseKeyButton, support.GroupCleanseKey);
            if (teamGroupCleanseSkillCombo is not null) PopulateMaintenanceSkillCombo(teamGroupCleanseSkillCombo, support.GroupCleanseSkillId, support.GroupCleanseSkillName);

            RefreshTeamRolePanelVisibility();
            RefreshTeamTacticalMarkKeyVisibility();
        }

        private TeamScriptSettings CaptureTeamSettings()
        {
            return new TeamScriptSettings
            {
                Role = ParseTeamRole(teamRoleCombo?.Text),
                GroupDistanceMeters = ReadDouble(teamGroupDistanceTextBox, 20.0D, 0.0D, 100.0D),
                Leader = new TeamLeaderScriptSettings
                {
                    Enabled = teamLeaderEnabledCheckBox?.Checked ?? false,
                    DungeonMode = teamLeaderDungeonModeCheckBox?.Checked ?? false,
                    AllowSelfDefense = teamLeaderAllowSelfDefenseCheckBox?.Checked ?? true,
                    StopAdvanceWhenMemberDisconnected =
                        teamLeaderStopAdvanceWhenMemberDisconnectedCheckBox?.Checked ?? false,
                    TacticalMarkEnabled = teamLeaderTacticalMarkCheckBox?.Checked ?? false,
                    TacticalMarkKey =
                        teamLeaderTacticalMarkKeyButton?.Tag as string ??
                        TeamLeaderScriptSettings.DefaultTacticalMarkKey
                },
                Output = new TeamOutputScriptSettings
                {
                    Enabled = teamOutputEnabledCheckBox?.Checked ?? false,
                    DungeonMode = teamOutputDungeonModeCheckBox?.Checked ?? false,
                    AllowSelfDefense = teamOutputAllowSelfDefenseCheckBox?.Checked ?? true,
                    FollowLeader = teamOutputFollowLeaderCheckBox?.Checked ?? true,
                    OnlyAttackLeaderMarkedTarget =
                        teamOutputOnlyAttackLeaderMarkedTargetCheckBox?.Checked ?? true,
                    StopWhenLeaderHasNoTarget =
                        teamOutputStopWhenLeaderHasNoTargetCheckBox?.Checked ?? true,
                    StopWhenLeaderDead = teamOutputStopWhenLeaderDeadCheckBox?.Checked ?? true,
                    LeaderDistanceMeters = ReadDouble(teamOutputLeaderDistanceTextBox, 12.0D, 0.0D, 100.0D),
                    AssistTargetKey =
                        teamOutputAssistTargetKeyButton?.Tag as string ??
                        TeamOutputScriptSettings.DefaultAssistTargetKey,
                    TacticalMarkTargetingEnabled =
                        teamOutputTacticalMarkTargetingCheckBox?.Checked ?? false,
                    SelectTacticalMarkTargetKey =
                        teamOutputSelectTacticalMarkTargetKeyButton?.Tag as string ??
                        TeamOutputScriptSettings.DefaultSelectTacticalMarkTargetKey
                },
                Support = new TeamSupportScriptSettings
                {
                    Enabled = teamSupportEnabledCheckBox?.Checked ?? false,
                    DungeonMode = teamSupportDungeonModeCheckBox?.Checked ?? false,
                    JoinCombat = teamSupportJoinCombatCheckBox?.Checked ?? false,
                    MentalCleanseEnabled = teamSupportMentalCleanseCheckBox?.Checked ?? true,
                    PhysicalCleanseEnabled = teamSupportPhysicalCleanseCheckBox?.Checked ?? true,
                    AllowSelfDefense = teamSupportAllowSelfDefenseCheckBox?.Checked ?? false,
                    StopWhenLeaderDead = teamSupportStopWhenLeaderDeadCheckBox?.Checked ?? true,
                    LeaderDistanceMeters = ReadDouble(teamSupportLeaderDistanceTextBox, 12.0D, 0.0D, 100.0D),
                    TacticalMarkTargetingEnabled =
                        teamSupportTacticalMarkTargetingCheckBox?.Checked ?? false,
                    SelectTacticalMarkTargetKey =
                        teamSupportSelectTacticalMarkTargetKeyButton?.Tag as string ??
                        TeamSupportScriptSettings.DefaultSelectTacticalMarkTargetKey,
                    HealSkillRules = CaptureTeamHealSkillRules(teamHealSkillRuleList),
                    MentalCleanseSkillId = GetSelectedMaintenanceSkill(teamMentalCleanseSkillCombo).SkillId,
                    MentalCleanseSkillName = GetSelectedMaintenanceSkill(teamMentalCleanseSkillCombo).SkillName,
                    MentalCleanseKey = teamMentalCleanseKeyButton?.Tag as string ?? string.Empty,
                    PhysicalCleanseSkillId = GetSelectedMaintenanceSkill(teamPhysicalCleanseSkillCombo).SkillId,
                    PhysicalCleanseSkillName = GetSelectedMaintenanceSkill(teamPhysicalCleanseSkillCombo).SkillName,
                    PhysicalCleanseKey = teamPhysicalCleanseKeyButton?.Tag as string ?? string.Empty,
                    GroupCleanseSkillId = GetSelectedMaintenanceSkill(teamGroupCleanseSkillCombo).SkillId,
                    GroupCleanseSkillName = GetSelectedMaintenanceSkill(teamGroupCleanseSkillCombo).SkillName,
                    GroupCleanseKey = teamGroupCleanseKeyButton?.Tag as string ?? string.Empty
                }
            };
        }

        private void PopulateTeamHealSkillRules(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            IEnumerable<TeamHealSkillRuleConfig>? rules)
        {
            if (list is null)
            {
                return;
            }

            list.Controls.Clear();
            foreach (var rule in rules ?? Array.Empty<TeamHealSkillRuleConfig>())
            {
                AddTeamHealSkillRuleRow(
                    list,
                    emptyLabel,
                    rule.BelowPercent,
                    rule.Key,
                    rule.SkillId,
                    rule.SkillName,
                    rule.RunTiming,
                    rule.TargetType);
            }

            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private static List<TeamHealSkillRuleConfig> CaptureTeamHealSkillRules(FlowLayoutPanel? list)
        {
            if (list is null)
            {
                return new List<TeamHealSkillRuleConfig>();
            }

            return list.Controls
                .OfType<Panel>()
                .Select(row =>
                {
                    var belowTextBox = row.Controls
                        .OfType<RoundedTextBox>()
                        .FirstOrDefault(textBox => string.Equals(textBox.Name, "teamHealRuleBelowTextBox", StringComparison.Ordinal));
                    var timingCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "teamHealRuleTimingCombo", StringComparison.Ordinal));
                    var targetTypeCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "teamHealRuleTargetTypeCombo", StringComparison.Ordinal));
                    var skillCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleSkillCombo", StringComparison.Ordinal));
                    var keyButton = row.Controls
                        .OfType<Button>()
                        .FirstOrDefault(button => string.Equals(button.Name, "teamHealRuleKeyButton", StringComparison.Ordinal));
                    var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);

                    return new TeamHealSkillRuleConfig
                    {
                        BelowPercent = ReadPercent(belowTextBox, 80),
                        RunTiming = GetSelectedMaintenanceRunTiming(timingCombo),
                        TargetType = ParseTeamHealSkillTargetType(targetTypeCombo?.Text),
                        SkillId = selectedSkill.SkillId,
                        SkillName = selectedSkill.SkillName,
                        Key = keyButton?.Tag as string ?? string.Empty
                    };
                })
                .ToList();
        }

        private static string FormatTeamRole(TeamRole role)
        {
            return role switch
            {
                TeamRole.Output => "输出",
                TeamRole.Support => "治疗",
                _ => "队长"
            };
        }

        private static TeamRole ParseTeamRole(string? value)
        {
            return value?.Trim() switch
            {
                "输出" => TeamRole.Output,
                "治疗" => TeamRole.Support,
                _ => TeamRole.Leader
            };
        }

        private static TeamHealSkillTargetType ParseTeamHealSkillTargetType(string? value)
        {
            return string.Equals(value?.Trim(), "群体", StringComparison.Ordinal)
                ? TeamHealSkillTargetType.Group
                : TeamHealSkillTargetType.Single;
        }

        private static void UpdateTeamRolePanelVisibility(
            Control teamRoleCombo,
            Control leaderPanel,
            Control dpsPanel,
            Control supportPanel)
        {
            var role = teamRoleCombo.Text.Trim();
            leaderPanel.Visible = string.Equals(role, "队长", StringComparison.Ordinal);
            dpsPanel.Visible = string.Equals(role, "输出", StringComparison.Ordinal);
            supportPanel.Visible = string.Equals(role, "治疗", StringComparison.Ordinal);
        }

        private static BagCleanupRuleConfig GetDefaultBagCleanupRule(string key)
        {
            return BagCleanupRuleCatalog.CreateDefaultRules()
                .First(rule => string.Equals(rule.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyBagCleanupRules(IEnumerable<BagCleanupRuleConfig>? rules)
        {
            foreach (var rule in BagCleanupRuleCatalog.MergeWithDefaults(rules))
            {
                if (!bagCleanupRuleControls.TryGetValue(rule.Key, out var controls))
                {
                    continue;
                }

                SetChecked(controls.CheckBox, rule.Enabled);
                SetComboText(controls.ActionCombo, FormatBagCleanupAction(rule.Action));
            }
        }

        private List<BagCleanupRuleConfig> CaptureBagCleanupRules()
        {
            var rules = BagCleanupRuleCatalog.CreateDefaultRules();
            foreach (var rule in rules)
            {
                if (!bagCleanupRuleControls.TryGetValue(rule.Key, out var controls))
                {
                    continue;
                }

                rule.Enabled = controls.CheckBox.Checked;
                rule.Action = ParseBagCleanupAction(controls.ActionCombo.Text);
            }

            return rules;
        }

        private static string FormatBagCleanupAction(BagCleanupAction action)
        {
            return action == BagCleanupAction.Discard ? "丢弃" : "出售";
        }

        private static BagCleanupAction ParseBagCleanupAction(string? text)
        {
            var trimmed = text?.Trim();
            return string.Equals(trimmed, "丢弃", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, nameof(BagCleanupAction.Discard), StringComparison.OrdinalIgnoreCase)
                ? BagCleanupAction.Discard
                : BagCleanupAction.Sell;
        }

        private async Task RefreshBagCleanupInventoryAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "刷新中...";

            try
            {
                var inventory = await _runtime.RefreshInventoryAsync(_account).ConfigureAwait(true);
                var count = PopulateBagCleanupInventoryCandidates(inventory);
                SetBagCleanupInventoryStatus(
                    count == 0
                        ? "背包为空或当前接口未返回物品"
                        : "已刷新 " + count.ToString(CultureInfo.InvariantCulture) + " 种物品",
                    false);
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

        private async Task TestPersonalShopAsync(Button button)
        {
            if (_personalShopTestCts != null || _inventoryDiscardTestCts != null || _auctionTestCts != null) return;
            using var cancellation = new CancellationTokenSource();
            _personalShopTestCts = cancellation;
            button.Enabled = false;
            button.Text = "摆摊中...";
            SetBagCleanupInventoryStatus("正在测试摆摊：按当前出售规则，整叠登记，每件 1 金币。", false);
            try
            {
                var settings = new MaintenanceScriptSettings
                {
                    BagCleanupRules = CaptureBagCleanupRules(),
                    BagCleanupExcludedItemNames = CaptureBagCleanupExcludedItemList(),
                    BagCleanupDiscardItemNameKeywords = CaptureBagCleanupDiscardItemList()
                };
                var progress = new Progress<string>(text =>
                {
                    if (!IsDisposed && _personalShopTestCts == cancellation && !cancellation.IsCancellationRequested) SetBagCleanupInventoryStatus(text, false);
                });
                var result = await _runtime.TestPersonalShopAsync(_account, settings, progress, cancellation.Token).ConfigureAwait(true);
                if (IsDisposed) return;
                if (!result.Success || result.Value == null) SetBagCleanupInventoryStatus("测试摆摊失败：" + result.Error, true);
                else if (result.Value.AlreadySelling) SetBagCleanupInventoryStatus("角色已在摆摊，保持当前出售状态。", false);
                else if (result.Value.RegisteredCount == 0) SetBagCleanupInventoryStatus("没有匹配当前启用出售规则的背包物品。", false);
                else SetBagCleanupInventoryStatus($"已开始摆摊：{result.Value.RegisteredCount} 项，单价 1 金币。" +
                    (result.Value.RemainingCount > 0 ? $"摊位已满，剩余 {result.Value.RemainingCount} 项未登记。" : ""), false);
            }
            catch (Exception ex) { if (!IsDisposed) SetBagCleanupInventoryStatus("测试摆摊失败：" + ex.Message, true); }
            finally
            {
                _personalShopTestCts = null;
                if (!button.IsDisposed) { button.Text = "测试摆摊"; button.Enabled = true; }
            }
        }

        private async Task TestInventoryDiscardAsync(Button button)
        {
            if (_inventoryDiscardTestCts != null || _personalShopTestCts != null || _auctionTestCts != null) return;
            using var cancellation = new CancellationTokenSource();
            _inventoryDiscardTestCts = cancellation;
            button.Enabled = false;
            button.Text = "丢弃中...";
            SetBagCleanupInventoryStatus("正在测试丢弃：按当前规则，最多 3 项，整叠处理。", false);
            try
            {
                var settings = new MaintenanceScriptSettings
                {
                    BagCleanupRules = CaptureBagCleanupRules(),
                    BagCleanupExcludedItemNames = CaptureBagCleanupExcludedItemList(),
                    BagCleanupDiscardItemNameKeywords = CaptureBagCleanupDiscardItemList()
                };
                var progress = new Progress<string>(text =>
                {
                    if (!IsDisposed && _inventoryDiscardTestCts == cancellation && !cancellation.IsCancellationRequested)
                        SetBagCleanupInventoryStatus(text, false);
                });
                var result = await _runtime.TestInventoryDiscardAsync(_account, settings, progress, cancellation.Token).ConfigureAwait(true);
                if (IsDisposed) return;
                if (!result.Success || result.Value == null) SetBagCleanupInventoryStatus("测试丢弃失败：" + result.Error, true);
                else if (result.Value.DiscardedCount == 0) SetBagCleanupInventoryStatus("没有匹配当前丢弃规则的背包物品。", false);
                else SetBagCleanupInventoryStatus($"已丢弃 {result.Value.DiscardedCount} 项，测试结束。" +
                    (result.Value.RemainingCount > 0 ? $"剩余 {result.Value.RemainingCount} 项未处理。" : ""), false);
            }
            catch (Exception ex) { if (!IsDisposed) SetBagCleanupInventoryStatus("测试丢弃失败：" + ex.Message, true); }
            finally
            {
                _inventoryDiscardTestCts = null;
                if (!button.IsDisposed) { button.Text = "测试丢弃"; button.Enabled = true; }
            }
        }

        private async Task TestBagCleanupInventoryWindowNormalizeAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "测试中...";
            SetBagCleanupInventoryStatus("正在测试背包拖到左上角...", false);

            try
            {
                var result = await _runtime
                    .NormalizeInventoryWindowToTopLeftAsync(_account)
                    .ConfigureAwait(true);
                SetBagCleanupInventoryStatus(
                    result.Success
                        ? "背包已拖到左上角"
                        : "测试背包拖拽失败: " + (result.Error ?? "未知错误"),
                    !result.Success);
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

        private async Task TestBagCleanupSellRegisterAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "登记中...";
            SetBagCleanupInventoryStatus("正在按当前配置登记出售物品...", false);

            try
            {
                var settings = new MaintenanceScriptSettings
                {
                    BagCleanupItemCoordinateMode = _bagCleanupItemCoordinateMode,
                    BagCleanupRules = CaptureBagCleanupRules(),
                    BagCleanupExcludedItemNames = CaptureBagCleanupExcludedItemList(),
                    BagCleanupDiscardItemNameKeywords = CaptureBagCleanupDiscardItemList()
                };
                var result = await _runtime
                    .TestRegisterBagCleanupSellItemsAsync(_account, settings)
                    .ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    SetBagCleanupInventoryStatus(
                        "登记出售测试失败: " + (result.Error ?? "未知错误"),
                        true);
                    return;
                }

                if (result.Value.RegisteredCount == 0)
                {
                    SetBagCleanupInventoryStatus("没有匹配当前出售配置的背包物品", false);
                    return;
                }

                var names = string.Join(
                    ", ",
                    result.Value.Items
                        .Select(item => item.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3));
                var suffix = result.Value.Items.Count > 3 ? "..." : string.Empty;
                SetBagCleanupInventoryStatus(
                    "已登记出售 " + result.Value.RegisteredCount.ToString(CultureInfo.InvariantCulture) + " 件: " + names + suffix,
                    false);
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

        private async Task TestBagCleanupFromNpcAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "清包中...";
            SetBagCleanupInventoryStatus("正在测试清包：F8 查找清包 NPC...", false);

            try
            {
                var settings = CaptureScriptSettings();
                var pathName = settings.Paths.MaintenancePathName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pathName))
                {
                    SetBagCleanupInventoryStatus("测试清包失败：未配置清包路径", true);
                    return;
                }

                var path = await _pathStore.LoadAsync(pathName).ConfigureAwait(true);
                if (!path.Success || path.Value is null)
                {
                    SetBagCleanupInventoryStatus(
                        "测试清包失败：读取清包路径失败: " + (path.Error ?? pathName),
                        true);
                    return;
                }

                var npcName = path.Value.CleanupNpcName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(npcName))
                {
                    SetBagCleanupInventoryStatus("测试清包失败：清包路径未绑定 NPC", true);
                    return;
                }

                var result = await _runtime
                    .TestBagCleanupFromNpcAsync(_account, npcName, settings.Maintenance)
                    .ConfigureAwait(true);
                if (!result.Success || result.Value is null)
                {
                    SetBagCleanupInventoryStatus(
                        "测试清包失败: " + (result.Error ?? "未知错误"),
                        true);
                    return;
                }

                if (result.Value.RegisteredCount == 0)
                {
                    SetBagCleanupInventoryStatus("测试清包完成：没有匹配出售配置的背包物品", false);
                    return;
                }

                SetBagCleanupInventoryStatus(
                    "测试清包完成：登记 " +
                    result.Value.RegisteredCount.ToString(CultureInfo.InvariantCulture) +
                    " 件，金币 +" +
                    (result.Value.MoneyDelta ?? 0UL).ToString("N0", CultureInfo.InvariantCulture),
                    false);
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

        private async Task TestScreenPointMoveAsync(
            RoundedTextBox? textBox,
            Button button,
            int fallbackX,
            int fallbackY,
            string title)
        {
            var point = ReadScreenPoint(textBox, fallbackX, fallbackY);
            SetText(textBox, FormatScreenPoint(point.X, point.Y));

            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "移动中...";

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
                        title + "坐标",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                button.Text = "已移动";
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

        private int PopulateBagCleanupInventoryCandidates(IEnumerable<InventoryItemSnapshot> items)
        {
            if (bagCleanupInventoryCheckedListBox is null)
            {
                return 0;
            }

            var previousCheckedNames = GetCheckedBagCleanupInventoryItemNames()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var comboItems = items
                .Where(IsBagCleanupInventoryCandidate)
                .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new BagCleanupInventoryComboItem(
                    group.Key,
                    group.Aggregate(0UL, (total, item) => total + item.Count),
                    group.Min(item => item.Slot)))
                .OrderBy(item => item.Name, StringComparer.CurrentCulture)
                .ToArray();

            bagCleanupInventoryCheckedListBox.Items.Clear();
            foreach (var item in comboItems)
            {
                var index = bagCleanupInventoryCheckedListBox.Items.Add(item);
                if (previousCheckedNames.Contains(item.Name))
                {
                    bagCleanupInventoryCheckedListBox.SetItemChecked(index, true);
                }
            }

            if (comboItems.Length == 0)
            {
                return 0;
            }

            return comboItems.Length;
        }

        private static bool IsBagCleanupInventoryCandidate(InventoryItemSnapshot item)
        {
            return !item.IsEquipped &&
                   item.Slot >= 0 &&
                   !string.IsNullOrWhiteSpace(item.Name);
        }

        private List<string> GetSelectedBagCleanupInventoryItemNames()
        {
            var names = new List<string>();
            var manualName = bagCleanupManualNameTextBox?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(manualName))
            {
                names.Add(manualName);
            }

            names.AddRange(GetCheckedBagCleanupInventoryItemNames());
            return BagCleanupNameListsDocument.NormalizeKeywords(names);
        }

        private IEnumerable<string> GetCheckedBagCleanupInventoryItemNames()
        {
            if (bagCleanupInventoryCheckedListBox is null)
            {
                yield break;
            }

            foreach (var checkedItem in bagCleanupInventoryCheckedListBox.CheckedItems.Cast<object>())
            {
                var name = checkedItem is BagCleanupInventoryComboItem item
                    ? item.Name
                    : Convert.ToString(checkedItem)?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }
        }

        private Task AddSelectedBagCleanupNameAsync()
        {
            return RunBagCleanupNameListMutationAsync(AddSelectedBagCleanupNameCoreAsync);
        }

        private async Task AddSelectedBagCleanupNameCoreAsync()
        {
            var selectedNames = GetSelectedBagCleanupInventoryItemNames();
            if (selectedNames.Count == 0)
            {
                SetBagCleanupInventoryStatus("请选择物品或输入关键字", true);
                return;
            }

            var target = GetActiveBagCleanupNameList();
            var listsBefore = CaptureBagCleanupNameLists();
            var addedNames = new List<string>();
            var existingNames = new List<string>();
            foreach (var name in selectedNames)
            {
                var existingIndex = target.FindIndex(value =>
                    string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    existingNames.Add(target[existingIndex]);
                    continue;
                }

                target.Add(name);
                GetActiveBagCleanupTradeItems()?.Add(new BagCleanupTradeItemConfig { Name = name });
                addedNames.Add(name);
            }

            if (addedNames.Count == 0)
            {
                if (existingNames.Count > 0)
                {
                    SelectBagCleanupNameInActiveList(existingNames[0]);
                }

                SetBagCleanupInventoryStatus(
                    existingNames.Count <= 1
                        ? "该关键字已在当前名单中: " + (existingNames.FirstOrDefault() ?? selectedNames[0])
                        : "这些关键字已在当前名单中，未新增: " + FormatBagCleanupNameSummary(existingNames),
                    false);
                return;
            }

            NormalizeBagCleanupNameLists();
            RefreshBagCleanupNameListEditor(addedNames[0]);
            var actionText = ActiveBagCleanupNameList.SavedText;
            if (await SaveBagCleanupNameListsOrRollbackAsync(
                        listsBefore,
                        FormatBagCleanupNameListAddSuccess(actionText, addedNames, existingNames.Count))
                    .ConfigureAwait(true))
            {
                ClearBagCleanupInventoryInputSelection();
            }
        }

        private void ClearBagCleanupInventoryInputSelection()
        {
            if (bagCleanupManualNameTextBox is not null)
            {
                bagCleanupManualNameTextBox.Text = string.Empty;
            }

            if (bagCleanupInventoryCheckedListBox is null)
            {
                return;
            }

            for (var i = 0; i < bagCleanupInventoryCheckedListBox.Items.Count; i++)
            {
                bagCleanupInventoryCheckedListBox.SetItemChecked(i, false);
            }
        }

        private void SelectBagCleanupNameInActiveList(string name)
        {
            if (bagCleanupExcludedItemListBox is null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var values = GetActiveBagCleanupNameList();
            var existingIndex = values.FindIndex(value =>
                string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                bagCleanupExcludedItemListBox.SelectedIndex = existingIndex;
                SelectBagCleanupTradeItem(name);
            }
        }

        private static string FormatBagCleanupNameListAddSuccess(
            string actionText,
            IReadOnlyList<string> addedNames,
            int skippedExistingCount)
        {
            var text = addedNames.Count == 1
                ? actionText + ": " + addedNames[0]
                : actionText + "，新增 " + addedNames.Count.ToString(CultureInfo.InvariantCulture) +
                  " 个: " + FormatBagCleanupNameSummary(addedNames);
            if (skippedExistingCount > 0)
            {
                text += "，跳过已存在 " + skippedExistingCount.ToString(CultureInfo.InvariantCulture) + " 个";
            }

            return text;
        }

        private static string FormatBagCleanupNameSummary(IReadOnlyList<string> names)
        {
            const int maxNames = 3;
            var shown = names
                .Take(maxNames)
                .ToArray();
            var text = string.Join(", ", shown);
            if (names.Count > shown.Length)
            {
                text += " 等";
            }

            return text;
        }

        private Task RemoveSelectedBagCleanupNameAsync()
        {
            return RunBagCleanupNameListMutationAsync(RemoveSelectedBagCleanupNameCoreAsync);
        }

        private async Task RemoveSelectedBagCleanupNameCoreAsync()
        {
            var tradeItems = GetActiveBagCleanupTradeItems();
            var selected = tradeItems is not null && bagCleanupTradeItemGrid is not null
                ? bagCleanupTradeItemGrid.SelectedRows.Cast<DataGridViewRow>()
                    .Select(row => (string)row.Cells[0].Value).ToArray()
                : bagCleanupExcludedItemListBox?.SelectedItems.Cast<object>()
                    .Select(item => Convert.ToString(item)?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>();
            if (selected.Length == 0)
            {
                return;
            }

            var listsBefore = CaptureBagCleanupNameLists();
            var target = GetActiveBagCleanupNameList();
            target.RemoveAll(value => selected.Any(selectedValue =>
                string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase)));
            tradeItems?.RemoveAll(item => selected.Contains(item.Name, StringComparer.OrdinalIgnoreCase));
            RefreshBagCleanupNameListEditor();
            await SaveBagCleanupNameListsOrRollbackAsync(
                    listsBefore,
                    "已自动保存，移除 " + selected.Length + " 个关键字")
                .ConfigureAwait(true);
        }

        private Task ClearSelectedBagCleanupNameListAsync()
        {
            return RunBagCleanupNameListMutationAsync(ClearSelectedBagCleanupNameListCoreAsync);
        }

        private async Task ClearSelectedBagCleanupNameListCoreAsync()
        {
            var target = GetActiveBagCleanupNameList();
            if (target.Count == 0)
            {
                return;
            }

            var listsBefore = CaptureBagCleanupNameLists();
            target.Clear();
            GetActiveBagCleanupTradeItems()?.Clear();
            RefreshBagCleanupNameListEditor();
            await SaveBagCleanupNameListsOrRollbackAsync(
                    listsBefore,
                    "已自动保存：" + ActiveBagCleanupNameList.Name + "已清空")
                .ConfigureAwait(true);
        }

        private async Task RunBagCleanupNameListMutationAsync(Func<Task> mutation)
        {
            if (bagCleanupNameListMutationInFlight)
            {
                return;
            }

            bagCleanupNameListMutationInFlight = true;
            SetBagCleanupNameListMutationControlsEnabled(false);
            try
            {
                await mutation().ConfigureAwait(true);
            }
            finally
            {
                bagCleanupNameListMutationInFlight = false;
                SetBagCleanupNameListMutationControlsEnabled(true);
            }
        }

        private void SetBagCleanupNameListMutationControlsEnabled(bool enabled)
        {
            if (bagCleanupTradeItemGrid is not null)
            {
                bagCleanupTradeItemGrid.Enabled = enabled;
            }
            if (bagCleanupAddNameButton is not null)
            {
                bagCleanupAddNameButton.Enabled = enabled;
            }

            if (bagCleanupRemoveNameButton is not null)
            {
                bagCleanupRemoveNameButton.Enabled = enabled;
            }

            if (bagCleanupClearNamesButton is not null)
            {
                bagCleanupClearNamesButton.Enabled = enabled;
            }

            if (bagCleanupWhitelistRadio is not null)
            {
                bagCleanupWhitelistRadio.Enabled = enabled;
            }

            if (bagCleanupBlacklistRadio is not null)
            {
                bagCleanupBlacklistRadio.Enabled = enabled;
            }

            if (bagCleanupStallRadio is not null)
            {
                bagCleanupStallRadio.Enabled = enabled;
            }

            if (bagCleanupAuctionHouseRadio is not null)
            {
                bagCleanupAuctionHouseRadio.Enabled = enabled;
            }

            if (bagCleanupManualNameTextBox is not null)
            {
                bagCleanupManualNameTextBox.Enabled = enabled;
            }

            if (bagCleanupInventoryCheckedListBox is not null)
            {
                bagCleanupInventoryCheckedListBox.Enabled = enabled;
            }
        }

        private async Task<bool> SaveBagCleanupNameListsOrRollbackAsync(
            BagCleanupNameListsDocument listsBefore,
            string successText)
        {
            if (_bagCleanupNameListStore is null)
            {
                SetBagCleanupInventoryStatus(successText, false);
                return true;
            }

            var document = CaptureBagCleanupNameLists();
            var save = await _bagCleanupNameListStore.SaveAsync(document).ConfigureAwait(true);
            if (save.Success)
            {
                SetBagCleanupInventoryStatus(successText, false);
                return true;
            }

            PopulateBagCleanupNameLists(listsBefore);
            SetBagCleanupInventoryStatus(
                "物品名单保存失败，界面已恢复: " + (save.Error ?? "未知保存错误"),
                true);
            return false;
        }

        private void PopulateBagCleanupNameLists(BagCleanupNameListsDocument document)
        {
            loadingBagCleanupNameListEditor = true;
            try
            {
                bagCleanupWhitelistItemNames.Clear();
                bagCleanupWhitelistItemNames.AddRange(BagCleanupNameListsDocument.NormalizeKeywords(document.Whitelist));
                bagCleanupBlacklistItemNames.Clear();
                bagCleanupBlacklistItemNames.AddRange(BagCleanupNameListsDocument.NormalizeKeywords(document.Blacklist));
                bagCleanupStallItemNames.Clear();
                bagCleanupStallItemNames.AddRange(BagCleanupTradeItemConfig.Normalize(document.Stall));
                bagCleanupAuctionHouseItemNames.Clear();
                bagCleanupAuctionHouseItemNames.AddRange(BagCleanupTradeItemConfig.Normalize(document.AuctionHouse));
                if (bagCleanupWhitelistRadio?.Checked != true && bagCleanupBlacklistRadio?.Checked != true &&
                    bagCleanupStallRadio?.Checked != true && bagCleanupAuctionHouseRadio?.Checked != true &&
                    bagCleanupWhitelistRadio is not null)
                {
                    bagCleanupWhitelistRadio.Checked = true;
                }
            }
            finally
            {
                loadingBagCleanupNameListEditor = false;
            }

            RefreshBagCleanupNameListEditor();
        }

        private void NormalizeBagCleanupNameLists()
        {
            PopulateBagCleanupNameLists(CaptureBagCleanupNameLists());
        }

        private void RefreshBagCleanupNameListEditor(string? selectedName = null)
        {
            if (loadingBagCleanupNameListEditor || bagCleanupExcludedItemListBox is null)
            {
                return;
            }

            var activeList = ActiveBagCleanupNameList;
            var values = GetActiveBagCleanupNameList();
            var tradeItems = GetActiveBagCleanupTradeItems();
            bagCleanupExcludedItemListBox.Visible = tradeItems is null;
            if (bagCleanupTradeItemGrid is not null)
            {
                loadingBagCleanupNameListEditor = true;
                try
                {
                    bagCleanupTradeItemGrid.Visible = tradeItems is not null;
                    bagCleanupTradeItemGrid.Rows.Clear();
                    bagCleanupTradeItemGrid.Columns["PriceLookupMethod"].Visible = bagCleanupAuctionHouseRadio?.Checked == true;
                    if (tradeItems is not null)
                    {
                        foreach (var item in tradeItems)
                        {
                            var index = bagCleanupTradeItemGrid.Rows.Add(item.Name, FormatBagCleanupUnitPrice(item.UnitPrice), FormatAuctionLookupMethod(item.PriceLookupMethod));
                            bagCleanupTradeItemGrid.Rows[index].Tag = item;
                            RefreshTradePriceCell(bagCleanupTradeItemGrid.Rows[index]);
                        }
                    }
                    bagCleanupTradeItemGrid.ClearSelection();
                    SelectBagCleanupTradeItem(selectedName);
                }
                finally
                {
                    loadingBagCleanupNameListEditor = false;
                }
            }
            bagCleanupExcludedItemListBox.Items.Clear();
            foreach (var value in values)
            {
                bagCleanupExcludedItemListBox.Items.Add(value);
            }

            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                var selectedIndex = values.FindIndex(value =>
                    string.Equals(value, selectedName, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex >= 0)
                {
                    bagCleanupExcludedItemListBox.SelectedIndex = selectedIndex;
                }
            }

            if (bagCleanupAddNameButton is not null)
            {
                bagCleanupAddNameButton.Text = activeList.AddText;
            }

            if (bagCleanupNameListTitleLabel is not null)
            {
                bagCleanupNameListTitleLabel.Text = activeList.Title;
            }
        }

        private (string Name, string AddText, string Title, string SavedText) ActiveBagCleanupNameList
        {
            get
            {
                if (bagCleanupBlacklistRadio?.Checked == true)
                {
                    return ("黑名单", "加入处理（丢弃）",
                        "黑名单：以下物品强制丢弃", "已自动保存黑名单，将走丢弃逻辑");
                }

                if (bagCleanupStallRadio?.Checked == true)
                {
                    return ("摆摊名单", "加入摆摊",
                        "摆摊：单价留空则跳过", "已自动保存摆摊名单，执行逻辑待接入");
                }

                if (bagCleanupAuctionHouseRadio?.Checked == true)
                {
                    return ("拍卖行名单", "加入拍卖行",
                        "拍卖行：选择查价方式；测试不提交出售", "已自动保存拍卖行名单");
                }

                return ("白名单", "加入不丢弃",
                    "白名单：以下物品不丢弃", "已自动保存白名单，不会丢弃");
            }
        }

        private List<string> GetActiveBagCleanupNameList()
        {
            return GetActiveBagCleanupTradeItems()?.Select(item => item.Name).ToList()
                ?? (bagCleanupBlacklistRadio?.Checked == true ? bagCleanupBlacklistItemNames : bagCleanupWhitelistItemNames);
        }

        private List<BagCleanupTradeItemConfig>? GetActiveBagCleanupTradeItems()
        {
            if (bagCleanupStallRadio?.Checked == true) return bagCleanupStallItemNames;
            if (bagCleanupAuctionHouseRadio?.Checked == true) return bagCleanupAuctionHouseItemNames;
            return null;
        }

        private BagCleanupNameListsDocument CaptureBagCleanupNameLists()
        {
            return new BagCleanupNameListsDocument
            {
                Whitelist = CaptureBagCleanupExcludedItemList(),
                Blacklist = CaptureBagCleanupDiscardItemList(),
                Stall = BagCleanupTradeItemConfig.Normalize(bagCleanupStallItemNames),
                AuctionHouse = BagCleanupTradeItemConfig.Normalize(bagCleanupAuctionHouseItemNames)
            };
        }

        private List<string> CaptureBagCleanupExcludedItemList()
        {
            return BagCleanupNameListsDocument.NormalizeKeywords(bagCleanupWhitelistItemNames);
        }

        private List<string> CaptureBagCleanupDiscardItemList()
        {
            return BagCleanupNameListsDocument.NormalizeKeywords(bagCleanupBlacklistItemNames);
        }

        private void SetBagCleanupInventoryStatus(string text, bool isError)
        {
            if (bagCleanupInventoryStatusLabel is null)
            {
                return;
            }

            bagCleanupInventoryStatusLabel.ForeColor = isError ? Color.FromArgb(166, 40, 40) : _textGreen;
            bagCleanupInventoryStatusLabel.Text = text;
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
                    rule.RunTiming,
                    rule.ActionType);
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

        private void PopulateDpMaintenanceRules(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            IEnumerable<DpMaintenanceRuleConfig>? rules)
        {
            if (list is null)
            {
                return;
            }

            list.Controls.Clear();
            foreach (var rule in rules ?? Array.Empty<DpMaintenanceRuleConfig>())
            {
                AddDpMaintenanceRuleRow(
                    list,
                    emptyLabel,
                    rule.RequiredDp,
                    rule.Key,
                    rule.SkillId,
                    rule.SkillName,
                    rule.RunTiming);
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
            MaintenanceRuleRunTiming runTiming = MaintenanceRuleRunTiming.Always,
            MaintenanceRuleActionType actionType = MaintenanceRuleActionType.Skill)
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
                Size = new Size(ReferenceEquals(list, mpMaintenanceRuleList) ? 690 : 630, 31)
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

            var allowPotion = ReferenceEquals(list, mpMaintenanceRuleList);
            RoundedComboBox? actionCombo = null;
            if (allowPotion)
            {
                actionCombo = AddCombo(row, 138, 1, 74, 28);
                actionCombo.Name = "maintenanceRuleActionCombo";
                PopulateMaintenanceActionCombo(actionCombo, actionType, allowPotion: true);
            }

            var skillCombo = AddCombo(row, allowPotion ? 220 : 138, 1, allowPotion ? 190 : 210, 28);
            skillCombo.Name = "maintenanceRuleSkillCombo";
            PopulateMaintenanceSkillCombo(skillCombo, skillId, skillName);

            void RefreshActionState()
            {
                skillCombo.Enabled = GetSelectedMaintenanceActionType(actionCombo) == MaintenanceRuleActionType.Skill;
            }

            if (actionCombo is not null)
            {
                actionCombo.SelectedIndexChanged += (_, _) => RefreshActionState();
                RefreshActionState();
            }

            var timingCombo = AddCombo(row, allowPotion ? 418 : 356, 1, 90, 28);
            timingCombo.Name = "maintenanceRuleTimingCombo";
            PopulateMaintenanceTimingCombo(timingCombo, runTiming);

            var keyButton = AddButton(row, "选择按键", allowPotion ? 516 : 454, 0, 104, 30);
            keyButton.Name = "maintenanceRuleKeyButton";
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = key;
                keyButton.Text = FormatSkillKey(key);
            }

            var deleteButton = AddButton(row, "删除", allowPotion ? 628 : 566, 0, 58, 30);
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

            BindAutomaticSkillButton(keyButton, skillCombo,
                () => GetSelectedMaintenanceActionType(actionCombo) == MaintenanceRuleActionType.Potion);
            if (actionCombo is not null) actionCombo.SelectedIndexChanged += (_, _) => RefreshAutomaticSkillDisplays();
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

            BindAutomaticSkillButton(keyButton, skillCombo);
            list.Controls.Add(row);
            RefreshMaintenanceRuleEmptyLabel(list, emptyLabel);
        }

        private void AddDpMaintenanceRuleRow(
            FlowLayoutPanel? list,
            Label? emptyLabel,
            int requiredDp = 2000,
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
                Size = new Size(42, 24),
                Text = "DP≥",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var requiredDpTextBox = AddTextBox(
                row,
                Math.Clamp(requiredDp, 1, 4000).ToString(CultureInfo.InvariantCulture),
                44,
                1,
                64,
                28);
            requiredDpTextBox.Name = "maintenanceRuleRequiredDpTextBox";

            row.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(112, 3),
                Size = new Size(24, 24),
                Text = "按",
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

            BindAutomaticSkillButton(keyButton, skillCombo);
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
            RefreshMaintenanceSkillCombos(dpMaintenanceRuleList);
            RefreshMaintenanceSkillCombos(teamHealSkillRuleList);
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

        private static void PopulateMaintenanceActionCombo(
            RoundedComboBox combo,
            MaintenanceRuleActionType selectedActionType,
            bool allowPotion)
        {
            combo.Items.Clear();
            combo.Items.Add(new MaintenanceActionComboItem(MaintenanceRuleActionType.Skill, "技能"));
            if (allowPotion)
            {
                combo.Items.Add(new MaintenanceActionComboItem(MaintenanceRuleActionType.Potion, "药水"));
            }

            combo.SelectedIndex = allowPotion && selectedActionType == MaintenanceRuleActionType.Potion ? 1 : 0;
        }

        private static MaintenanceRuleActionType GetSelectedMaintenanceActionType(RoundedComboBox? combo)
        {
            if (combo is null ||
                combo.SelectedIndex < 0 ||
                combo.SelectedIndex >= combo.Items.Count)
            {
                return MaintenanceRuleActionType.Skill;
            }

            return combo.Items[combo.SelectedIndex] is MaintenanceActionComboItem item
                ? item.ActionType
                : MaintenanceRuleActionType.Skill;
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

            SetKeyButton(openingSkillKeyButton, key);
        }

        private static void SetKeyButton(Button? keyButton, string? key)
        {
            if (keyButton is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                keyButton.Tag = null;
                keyButton.Text = "选择按键";
                return;
            }

            keyButton.Tag = key.Trim();
            keyButton.Text = FormatSkillKey(key);
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
                    var actionCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleActionCombo", StringComparison.Ordinal));
                    var timingCombo = row.Controls
                        .OfType<RoundedComboBox>()
                        .FirstOrDefault(combo => string.Equals(combo.Name, "maintenanceRuleTimingCombo", StringComparison.Ordinal));
                    var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);

                    return new MaintenanceKeyRuleConfig
                    {
                        BelowPercent = ReadPercent(belowTextBox, 50),
                        ActionType = GetSelectedMaintenanceActionType(actionCombo),
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

        private static List<DpMaintenanceRuleConfig> CaptureDpMaintenanceRules(FlowLayoutPanel? list)
        {
            if (list is null)
            {
                return new List<DpMaintenanceRuleConfig>();
            }

            return list.Controls
                .OfType<Panel>()
                .Select(row =>
                {
                    var requiredDpTextBox = row.Controls
                        .OfType<RoundedTextBox>()
                        .FirstOrDefault(textBox => string.Equals(textBox.Name, "maintenanceRuleRequiredDpTextBox", StringComparison.Ordinal));
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

                    return new DpMaintenanceRuleConfig
                    {
                        RequiredDp = Math.Clamp(ReadInt(requiredDpTextBox, 2000), 1, 4000),
                        Key = keyButton?.Tag as string ?? string.Empty,
                        SkillId = selectedSkill.SkillId,
                        SkillName = selectedSkill.SkillName,
                        RunTiming = GetSelectedMaintenanceRunTiming(timingCombo)
                    };
                })
                .ToList();
        }

        private TabPage CreateSkillTab()
        {
            var tab = CreateBaseTab("技能");
            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(852, 556);
            tab.Controls.Add(page);

            var optionsPanel = new Panel
            {
                Name = "skillOptionsPanel",
                BackColor = _inputBackground,
                Location = new Point(12, 10),
                Size = new Size(828, 112)
            };
            page.Controls.Add(optionsPanel);
            AddLabel(optionsPanel, "技能配置", 12, 4, 90, 24, _textGreen, FontStyle.Bold);
            AddLabel(optionsPanel, "按键自动匹配主栏 / Alt栏；移动技能后重启脚本", 168, 4, 630, 24);
            var autoMode = AddRadioButton(optionsPanel, "自动技能", 12, 32, 120, true);
            autoMode.BackColor = optionsPanel.BackColor;
            skillAutoModeRadio = autoMode;
            openingAttackKeyCheckBox = AddCheckBox(optionsPanel, "开怪按C", 168, 32, 120, true);
            spiritmasterAutoSkillCheckBox = AddCheckBox(optionsPanel, "精灵专用", 324, 32, 120, false);
            spiritmasterAutoSkillCheckBox.Click += (_, _) => RefreshSpiritmasterAutoSkillCheckBoxState();
            spiritmasterSettingsButton = AddButton(optionsPanel, "精灵设置", 456, 29, 112, 30, (_, _) => ShowSpiritmasterSettingsDialog());
            spiritmasterSettingsButton.Visible = false;
            conditionSkillPreemptsChainCheckBox = AddCheckBox(optionsPanel, "条件抢连招", 12, 74, 126, true);
            AddLabel(optionsPanel, "连招段", 148, 74, 60, 24);
            chainWindowPerLinkTextBox = AddTextBox(
                optionsPanel,
                SemiAutoScriptSettings.DefaultChainWindowPerLinkMs.ToString(),
                212,
                72,
                64,
                28);
            AddLabel(optionsPanel, "ms", 284, 74, 32, 24);

            attackWeaveCheckBox = AddCheckBox(optionsPanel, "卡刀（每2技能）", 416, 74, 148, false);
            attackWeaveCheckBox.Name = "attackWeaveCheckBox";
            AddLabel(optionsPanel, "等待", 576, 74, 40, 24);
            attackWeaveDelayTextBox = AddTextBox(
                optionsPanel, SemiAutoScriptSettings.DefaultAttackWeaveDelayMs.ToString(), 620, 72, 64, 28);
            attackWeaveDelayTextBox.Name = "attackWeaveDelayTextBox";
            attackWeaveDelayTextBox.Enabled = false;
            attackWeaveCheckBox.Click += (_, _) =>
                attackWeaveDelayTextBox.Enabled = attackWeaveCheckBox.Checked;
            AddLabel(optionsPanel, "ms", 692, 74, 32, 24);
            foreach (var option in optionsPanel.Controls.OfType<RoundedCheckBox>())
            {
                option.BackColor = optionsPanel.BackColor;
            }

            var autoPanel = CreateSkillModePanel(page, "autoSkillPanel", true);
            autoSkillPanel = autoPanel;
            var manualPanel = CreateSkillModePanel(page, "manualSkillPanel", false);
            manualSkillPanel = manualPanel;
            var systemPanel = CreateSkillModePanel(page, "systemSkillPanel", false);
            systemSkillPanel = systemPanel;
            foreach (var panel in new[] { autoPanel, manualPanel, systemPanel })
            {
                panel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                panel.Location = new Point(12, 136);
                panel.Size = new Size(828, 420);
            }

            AddLabel(autoPanel, "可用技能", 0, 2, 120, 24, _textGreen, FontStyle.Bold);
            AddLabel(autoPanel, "技能执行顺序", 416, 2, 160, 24, _textGreen, FontStyle.Bold);

            var availableTree = CreateSkillTree(autoPanel, "availableSkillTree", 0, 38, 316, 292);
            availableSkillTree = availableTree;
            var selectedTree = CreateSkillTree(autoPanel, "selectedSkillTree", 416, 38, 316, 292);
            selectedSkillTree = selectedTree;
            AddSkillTreeBindingDisplay(selectedTree);
            PopulateAvailableSkillTree(availableTree);
            PopulateSelectedSkillTree(selectedTree);

            var refreshSkillsButton = AddButton(autoPanel, "刷新当前技能", 182, 0, 134, 30);
            refreshSkillsButton.Click += async (_, _) =>
                await RefreshCurrentSkillsAsync(refreshSkillsButton, availableTree, systemSkillTree).ConfigureAwait(true);

            AddButton(autoPanel, "添加 >", 328, 110, 76, 30, (_, _) => AddSkillSelection(availableTree, selectedTree));
            AddButton(autoPanel, "< 移除", 328, 150, 76, 30, (_, _) => RemoveSelectedSkill(selectedTree));
            AddButton(autoPanel, "全部 >>", 328, 190, 76, 30, (_, _) => AddAllAvailableSkills(availableTree, selectedTree));
            AddButton(autoPanel, "清空", 328, 230, 76, 30, (_, _) => selectedTree.Nodes.Clear());

            var refreshSelectedSkillsButton = AddButton(autoPanel, "刷新当前已选技能", 588, 0, 144, 30);
            refreshSelectedSkillsButton.Click += async (_, _) =>
                await RefreshSelectedSkillTreeAsync(refreshSelectedSkillsButton, selectedTree).ConfigureAwait(true);

            AddButton(autoPanel, "置顶", 744, 110, 84, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Top));
            AddButton(autoPanel, "上移", 744, 150, 84, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Up));
            AddButton(autoPanel, "下移", 744, 190, 84, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Down));
            AddButton(autoPanel, "置底", 744, 230, 84, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Bottom));

            var openingPanel = new Panel
            {
                Name = "openingSkillPanel",
                BackColor = _inputBackground,
                Location = new Point(0, 346),
                Size = new Size(828, 60)
            };
            autoPanel.Controls.Add(openingPanel);
            openingSkillEnabledCheckBox = AddCheckBox(openingPanel, "启用起手技能", 12, 18, 128, false);
            openingSkillEnabledCheckBox.BackColor = openingPanel.BackColor;
            AddLabel(openingPanel, "技能", 148, 18, 40, 24, _textGreen, FontStyle.Bold);
            openingSkillCombo = AddCombo(openingPanel, 192, 16, 430, 28);
            openingSkillCombo.Name = "openingSkillCombo";
            PopulateOpeningSkillCombo(openingSkillCombo, 0, string.Empty);
            AddLabel(openingPanel, "按键", 634, 18, 40, 24, _textGreen, FontStyle.Bold);
            openingSkillKeyButton = AddButton(openingPanel, "选择按键", 676, 15, 136, 30);
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

            BindAutomaticSkillButton(openingSkillKeyButton, openingSkillCombo);
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
            AddSkillTreeBindingDisplay(systemSelectedTree);
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

            RefreshSpiritmasterAutoSkillCheckBoxState();
            return tab;
        }

        private TabPage CreateFilterTab()
        {
            var tab = CreateBaseTab("过滤");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            var filterTabs = new TabControl
            {
                Alignment = TabAlignment.Top,
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(104, 28),
                Name = "filterTabs",
                SizeMode = TabSizeMode.Fixed
            };
            filterTabs.DrawItem += GreenTabs_DrawItem;
            filterTabs.TabPages.Add(CreateMonsterFilterTab());
            filterTabs.TabPages.Add(CreateGatherFilterPreviewTab());
            page.Controls.Add(filterTabs);

            return tab;
        }

        private TabPage CreateMonsterFilterTab()
        {
            var tab = CreateBaseTab("怪物过滤");
            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(836, 514);
            tab.Controls.Add(page);

            var selectionPanel = CreateFilterSelectionPanel(page, 12);
            AddLabel(selectionPanel, "当前怪物", 12, 10, 120, 24, _textGreen, FontStyle.Bold);
            var refreshMonstersButton = AddButton(selectionPanel, "刷新当前怪物", 656, 8, 144, 30);
            refreshMonstersButton.Click += async (_, _) =>
                await RefreshCurrentMonstersAsync(refreshMonstersButton).ConfigureAwait(true);

            activeMonsterFilterCombo = AddCombo(selectionPanel, 12, 46, 632, 30);
            activeMonsterFilterCombo.Name = "activeMonsterFilterCombo";
            AddButton(selectionPanel, "添加到过滤", 656, 46, 144, 30, (_, _) => AddSelectedActiveMonsterFilter());

            activeMonsterFilterStatusLabel = AddLabel(selectionPanel, "等待刷新", 12, 82, 788, 24);

            AddLabel(page, "已过滤怪物", 12, 142, 200, 24, _textGreen, FontStyle.Bold);
            activeMonsterFilterListBox = CreateFilterListBox(page, 12, 180, 812, 318);
            activeMonsterFilterListBox.BackColor = Color.White;
            AddButton(page, "移除", 632, 138, 88, 30, (_, _) => RemoveSelectedActiveMonsterFilter());
            AddButton(page, "清空", 736, 138, 88, 30, (_, _) => ClearActiveMonsterFilterList());

            return tab;
        }

        private Panel CreateFilterSelectionPanel(Control page, int top)
        {
            var panel = new Panel
            {
                BackColor = _inputBackground,
                Location = new Point(12, top),
                Size = new Size(812, 114)
            };
            page.Controls.Add(panel);
            return panel;
        }

        private TabPage CreateGatherFilterPreviewTab()
        {
            var tab = CreateBaseTab("采集物过滤");
            var page = CreatePagePanel();
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(836, 514);
            tab.Controls.Add(page);

            stationaryGatherEnabledCheckBox = AddCheckBox(page, "先采集后打怪", 24, 18, 176, false);
            stationaryGatherEnabledCheckBox.Name = "stationaryGatherEnabledCheckBox";

            AddLabel(page, "安全清怪", 228, 18, 80, 24, _textGreen, FontStyle.Bold);
            gatherThreatRadiusTextBox = AddTextBox(page, "7", 312, 16, 72, 28);
            gatherThreatRadiusTextBox.Name = "gatherThreatRadiusTextBox";
            AddLabel(page, "米", 396, 18, 28, 24);

            var selectionPanel = CreateFilterSelectionPanel(page, 60);
            gatherFilterStatusLabel = AddLabel(
                selectionPanel,
                "等待读取附近采集物",
                12,
                82,
                788,
                24,
                Color.FromArgb(166, 80, 24),
                FontStyle.Bold);
            gatherFilterStatusLabel.Name = "gatherFilterStatusLabel";

            AddLabel(selectionPanel, "附近采集物", 12, 10, 160, 24, _textGreen, FontStyle.Bold);
            readNearbyGatherFilterButton = AddButton(selectionPanel, "读取附近", 656, 8, 144, 30);
            readNearbyGatherFilterButton.Name = "readNearbyGatherFilterButton";
            readNearbyGatherFilterButton.Click += async (_, _) =>
                await RefreshNearbyGatherFiltersAsync(readNearbyGatherFilterButton).ConfigureAwait(true);

            nearbyGatherFilterCombo = AddCombo(selectionPanel, 12, 46, 632, 30);
            nearbyGatherFilterCombo.Name = "nearbyGatherFilterCombo";
            nearbyGatherFilterCombo.DropDownWidth = 632;
            nearbyGatherFilterCombo.SelectedIndexChanged += (_, _) =>
            {
                if (gatherFilterListView is not null)
                {
                    gatherFilterListView.SelectedItems.Clear();
                }
            };
            addGatherFilterButton = AddButton(selectionPanel, "加入采集", 656, 46, 144, 30, (_, _) => AddSelectedGatherFilter());
            addGatherFilterButton.Name = "addGatherFilterButton";

            AddLabel(page, "要采集的采集物", 12, 190, 200, 24, _textGreen, FontStyle.Bold);
            gatherFilterListView = new ListView
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CheckBoxes = true,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = _textGreen,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                Location = new Point(12, 228),
                MultiSelect = true,
                Name = "gatherFilterListView",
                Size = new Size(812, 270),
                UseCompatibleStateImageBehavior = false,
                View = View.Details
            };
            gatherFilterListView.Columns.Add("启用", 64, HorizontalAlignment.Center);
            gatherFilterListView.Columns.Add("名称", 572, HorizontalAlignment.Left);
            gatherFilterListView.Columns.Add("按键", 150, HorizontalAlignment.Center);
            gatherFilterListView.ItemSelectionChanged += (_, _) =>
            {
                var rule = GetSelectedGatherFilterRule();
                UpdateGatherFilterKeyButton(rule);
            };
            page.Controls.Add(gatherFilterListView);

            gatherFilterKeyButton = AddButton(page, "设置按键", 504, 186, 112, 30, (_, _) => SetSelectedGatherFilterKey());
            gatherFilterKeyButton.Name = "gatherFilterKeyButton";
            gatherFilterKeyButton.Enabled = false;
            removeGatherFilterButton = AddButton(page, "移除", 632, 186, 88, 30, (_, _) => RemoveSelectedGatherFilters());
            removeGatherFilterButton.Name = "removeGatherFilterButton";
            clearGatherFilterButton = AddButton(page, "清空", 736, 186, 88, 30, (_, _) => ClearGatherFilters());
            clearGatherFilterButton.Name = "clearGatherFilterButton";

            return tab;
        }

        private async Task RefreshNearbyGatherFiltersAsync(Button button)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "读取中...";
            SetGatherFilterStatus("正在读取附近采集物", false);

            try
            {
                var snapshot = await _runtime
                    .RefreshGatherSnapshotAsync(_account)
                    .ConfigureAwait(true);
                var sourceCount = PopulateNearbyGatherFilterCombo(snapshot.Objects);
                SetGatherFilterStatus(
                    "已读取 " +
                    snapshot.Objects.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个 / " +
                    sourceCount.ToString(CultureInfo.InvariantCulture) +
                    " 类",
                    false);
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

        private int PopulateNearbyGatherFilterCombo(IEnumerable<GatherObjectSnapshot> objects)
        {
            if (nearbyGatherFilterCombo is null)
            {
                return 0;
            }

            var previousSourceId = GetSelectedNearbyGatherFilter()?.Snapshot.GatherSourceId ?? 0;
            var items = objects
                .Where(item => item.GatherSourceId != 0)
                .GroupBy(item => item.GatherSourceId)
                .Select(group => group
                    .OrderBy(item => item.DistanceToLocalPlayer ?? double.MaxValue)
                    .ThenBy(item => item.ServerObjectId)
                    .First())
                .OrderBy(item => item.DistanceToLocalPlayer ?? double.MaxValue)
                .ThenBy(item => item.GatherSourceId)
                .Select(item => new GatherFilterComboItem(item))
                .ToArray();

            nearbyGatherFilterCombo.Items.Clear();
            nearbyGatherFilterCombo.Items.AddRange(items);
            if (items.Length == 0)
            {
                nearbyGatherFilterCombo.Text = string.Empty;
                return 0;
            }

            var selectedIndex = Array.FindIndex(
                items,
                item => item.Snapshot.GatherSourceId == previousSourceId);
            nearbyGatherFilterCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            return items.Length;
        }

        private GatherFilterComboItem? GetSelectedNearbyGatherFilter()
        {
            return nearbyGatherFilterCombo?.SelectedItem as GatherFilterComboItem;
        }

        private void AddSelectedGatherFilter()
        {
            if (gatherFilterListView is null || GetSelectedNearbyGatherFilter() is not { } selected)
            {
                SetGatherFilterStatus("请先选择附近采集物", true);
                return;
            }

            var sourceId = selected.Snapshot.GatherSourceId;
            var existing = gatherFilterListView.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item =>
                    item.Tag is GatherFilterRuleDraft rule &&
                    rule.GatherSourceId == sourceId);
            if (existing is not null)
            {
                existing.Selected = true;
                existing.Focused = true;
                existing.EnsureVisible();
                SetGatherFilterStatus("该采集物已经在列表中", false);
                return;
            }

            var draft = new GatherFilterRuleDraft(selected.Snapshot);
            var item = CreateGatherFilterListItem(draft);
            gatherFilterListView.Items.Add(item);
            item.Checked = true;
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            SetGatherFilterStatus("已加入，选中该行设置按键", false);
        }

        private static ListViewItem CreateGatherFilterListItem(GatherFilterRuleDraft draft)
        {
            var item = new ListViewItem(string.Empty)
            {
                Tag = draft
            };
            item.SubItems.Add(draft.DisplayName);
            item.SubItems.Add(string.IsNullOrWhiteSpace(draft.GatherKey) ? "未设置" : FormatSkillKey(draft.GatherKey));
            return item;
        }

        private GatherFilterRuleDraft? GetSelectedGatherFilterRule()
        {
            return gatherFilterListView?.SelectedItems.Count > 0
                ? gatherFilterListView.SelectedItems[0].Tag as GatherFilterRuleDraft
                : null;
        }

        private void SetSelectedGatherFilterKey()
        {
            if (gatherFilterListView?.SelectedItems.Count is not > 0 ||
                GetSelectedGatherFilterRule() is not { } rule)
            {
                SetGatherFilterStatus("请先选择右侧采集物", true);
                return;
            }

            var selectedKey = ShowKeyboardPicker(rule.GatherKey, "选择采集按键");
            if (string.IsNullOrWhiteSpace(selectedKey))
            {
                return;
            }

            rule.GatherKey = selectedKey.Trim();
            gatherFilterListView.SelectedItems[0].SubItems[2].Text = FormatSkillKey(rule.GatherKey);
            UpdateGatherFilterKeyButton(rule);
            SetGatherFilterStatus("已设置按键 " + FormatSkillKey(rule.GatherKey), false);
        }

        private void RemoveSelectedGatherFilters()
        {
            if (gatherFilterListView is null || gatherFilterListView.SelectedItems.Count == 0)
            {
                SetGatherFilterStatus("请先选择右侧采集物", true);
                return;
            }

            foreach (var item in gatherFilterListView.SelectedItems.Cast<ListViewItem>().ToArray())
            {
                gatherFilterListView.Items.Remove(item);
            }

            UpdateGatherFilterKeyButton(null);
            SetGatherFilterStatus("已移除", false);
        }

        private void ClearGatherFilters()
        {
            gatherFilterListView?.Items.Clear();
            UpdateGatherFilterKeyButton(null);
            SetGatherFilterStatus("已清空", false);
        }

        private void UpdateGatherFilterKeyButton(GatherFilterRuleDraft? rule)
        {
            if (gatherFilterKeyButton is null)
            {
                return;
            }

            gatherFilterKeyButton.Enabled = rule is not null;
            gatherFilterKeyButton.Tag = rule?.GatherKey;
            gatherFilterKeyButton.Text = rule is null || string.IsNullOrWhiteSpace(rule.GatherKey)
                ? "设置按键"
                : FormatSkillKey(rule.GatherKey);
        }

        private void SetGatherFilterStatus(string text, bool isError)
        {
            if (gatherFilterStatusLabel is null)
            {
                return;
            }

            gatherFilterStatusLabel.ForeColor = isError ? Color.FromArgb(166, 40, 40) : _textGreen;
            gatherFilterStatusLabel.Text = text;
        }

        private static string ResolveGatherFilterDisplayName(GatherObjectSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Name))
            {
                return snapshot.Name.Trim();
            }

            return !string.IsNullOrWhiteSpace(snapshot.Source?.InternalName)
                ? snapshot.Source.InternalName
                : "采集物";
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

        private CheckedListBox CreateBagCleanupInventoryCheckedListBox(Control parent, int x, int y, int width, int height)
        {
            var listBox = new CheckedListBox
            {
                BackColor = _inputBackground,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                IntegralHeight = false,
                Location = new Point(x, y),
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
                var objects = await _runtime.RefreshWorldObjectsAsync(_account).ConfigureAwait(true);
                var count = PopulateActiveMonsterFilterCombo(objects);
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
                Location = new Point(0, 110),
                Name = name,
                Size = new Size(850, 466),
                Visible = visible
            };

            parent.Controls.Add(panel);
            return panel;
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
                    summonRules.Length > 0 ? summonRules[0].Key : string.Empty,
                    summonRules.Length > 0 ? summonRules[0].SkillId : 0,
                    summonRules.Length > 0 ? summonRules[0].SkillName : string.Empty);
                AddSpiritmasterSummonButtonRow(
                    spiritmasterSummonRuleList,
                    2,
                    summonRules.Length > 1 ? summonRules[1].Key : string.Empty,
                    summonRules.Length > 1 ? summonRules[1].SkillId : 0,
                    summonRules.Length > 1 ? summonRules[1].SkillName : string.Empty);
                AddSpiritmasterOpeningAttackKeyRow(spiritmasterSummonRuleList, spiritmaster.OpeningAttackKey, spiritmaster.OpeningAttackSkillId, spiritmaster.OpeningAttackSkillName);
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
                OpeningAttackSkillId = GetSelectedMaintenanceSkill(spiritmasterOpeningSkillCombo).SkillId,
                OpeningAttackSkillName = GetSelectedMaintenanceSkill(spiritmasterOpeningSkillCombo).SkillName,
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
            foreach (var row in EnumerateSpiritmasterRows(list).Where(row => row.Name == "spiritmasterSummonRow"))
            {
                var selectedSkill = GetSelectedMaintenanceSkill(FindSpiritmasterSkillCombo(row));
                var key = FindSpiritmasterKeyButton(row)?.Tag as string ?? string.Empty;

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

        private Panel CreateSpiritmasterRuleRow(int width = 780)
        {
            return new Panel
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 0, 2),
                Size = new Size(width, 34)
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
            combo.DropDownWidth = Math.Max(340, width);
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
            var remove = AddSpiritmasterSecondaryButton(row, "移除", x, 0, 58, removal: true);
            remove.Name = "spiritmasterRemoveRuleButton";
            remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            remove.Click += (_, _) =>
            {
                list.Controls.Remove(row);
                row.Dispose();
            };
        }

        private void AddSpiritmasterDotRuleRow(FlowLayoutPanel list, uint skillId = 0, string skillName = "")
        {
            var row = CreateSpiritmasterRuleRow();
            AddLabel(row, "技能", 0, 3, 34, 24);
            var skillCombo = AddSpiritmasterSkillCombo(row, 44, 1, 408, skillId, skillName);
            skillCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var statusLabel = AddLabel(row, "状态自动识别", 464, 3, 114, 24);
            statusLabel.Name = "spiritmasterDotStatusLabel";
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var durationLabel = AddLabel(row, "持续时间自动识别", 582, 3, 132, 24);
            durationLabel.Name = "spiritmasterDotDurationLabel";
            durationLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            skillCombo.SelectedIndexChanged += (_, _) => UpdateSpiritmasterDotRuleInfo(skillCombo, statusLabel, durationLabel);
            UpdateSpiritmasterDotRuleInfo(skillCombo, statusLabel, durationLabel);
            AddSpiritmasterDeleteButton(list, row, 722);
            list.Controls.Add(row);
        }

        private void UpdateSpiritmasterDotRuleInfo(
            RoundedComboBox skillCombo,
            Label statusLabel,
            Label durationLabel)
        {
            statusLabel.Text = "状态自动识别";

            var selectedSkill = GetSelectedMaintenanceSkill(skillCombo);
            var skill = FindCurrentSkill(selectedSkill.SkillId, selectedSkill.SkillName);
            durationLabel.Text = FormatSpiritmasterDotDuration(skill, selectedSkill);
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
                return "持续时间自动识别";
            }

            if (skill?.XmlEffectRemainMs is int remainMs && remainMs > 0)
            {
                return "持续 " + FormatMillisecondsAsSeconds(remainMs);
            }

            return "持续时间待识别";
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
            string key = "", uint skillId = 0, string skillName = "")
        {
            var row = CreateSpiritmasterRuleRow();
            row.Name = "spiritmasterSummonRow";
            AddLabel(row, GetSpiritmasterSummonButtonLabel(index), 0, 3, 96, 24, _textGreen, FontStyle.Bold);
            var combo = AddSpiritmasterSkillCombo(row, 106, 1, 452, skillId, skillName);
            combo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var button = AddSpiritmasterKeyButton(row, 570, 0, key);
            button.Width = 120;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var mode = AddLabel(row, "手动按键", 702, 3, 78, 24, Color.FromArgb(112, 127, 116));
            mode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.EnabledChanged += (_, _) => mode.Text = button.Enabled ? "手动按键" : "自动匹配";
            BindAutomaticSkillButton(button, combo, () => !HasSpiritmasterSkillSelection(GetSelectedMaintenanceSkill(combo)));
            ShowSpiritmasterAutomaticKeyAsLabel(button);
            list.Controls.Add(row);
        }

        private void AddSpiritmasterOpeningAttackKeyRow(
            FlowLayoutPanel list,
            string key = "", uint skillId = 0, string skillName = "")
        {
            var row = CreateSpiritmasterRuleRow();
            row.Name = "spiritmasterOpeningRow";
            AddLabel(row, "宝宝攻击", 0, 3, 96, 24, _textGreen, FontStyle.Bold);
            spiritmasterOpeningSkillCombo = AddSpiritmasterSkillCombo(row, 106, 1, 452, skillId, skillName);
            spiritmasterOpeningSkillCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            spiritmasterOpeningAttackKeyButton = AddSpiritmasterKeyButton(row, 570, 0, key);
            spiritmasterOpeningAttackKeyButton.Width = 120;
            spiritmasterOpeningAttackKeyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var mode = AddLabel(row, "手动指令", 702, 3, 78, 24, Color.FromArgb(112, 127, 116));
            mode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var button = spiritmasterOpeningAttackKeyButton;
            button.EnabledChanged += (_, _) => mode.Text = button.Enabled ? "手动指令" : "自动匹配";
            BindAutomaticSkillButton(spiritmasterOpeningAttackKeyButton, spiritmasterOpeningSkillCombo,
                () => !HasSpiritmasterSkillSelection(GetSelectedMaintenanceSkill(spiritmasterOpeningSkillCombo)));
            ShowSpiritmasterAutomaticKeyAsLabel(button);
            list.Controls.Add(row);
        }

        private static string GetSpiritmasterSummonButtonLabel(int index)
        {
            return index == 1 ? "服从手印" : "精灵召唤";
        }

        private void AddSpiritmasterPetHpRuleRow(
            FlowLayoutPanel list,
            int belowPercent = 68,
            uint skillId = 0,
            string skillName = "",
            string key = "",
            int cooldownMs = SpiritmasterPetHpRuleConfig.DefaultCooldownMs)
        {
            var row = CreateSpiritmasterRuleRow();
            row.Height = 68;
            AddLabel(row, "技能", 0, 3, 34, 24);
            var skillCombo = AddSpiritmasterSkillCombo(row, 44, 1, 514, skillId, skillName);
            skillCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var keyButton = AddSpiritmasterKeyButton(row, 570, 0, key);
            keyButton.Width = 120;
            keyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            BindAutomaticSkillButton(keyButton, skillCombo);
            ShowSpiritmasterAutomaticKeyAsLabel(keyButton);
            AddSpiritmasterDeleteButton(list, row, 722);

            AddLabel(row, "血量低于", 0, 38, 64, 24);
            var thresholdTextBox = AddTextBox(row, Math.Clamp(belowPercent, 0, 100).ToString(CultureInfo.InvariantCulture), 68, 36, 54, 28);
            thresholdTextBox.Name = "spiritmasterPetHpBelowTextBox";
            AddLabel(row, "% 时使用", 130, 38, 70, 24);
            AddLabel(row, "最短间隔", 224, 38, 64, 24);
            var cooldownTextBox = AddTextBox(row,
                Math.Clamp(cooldownMs <= 0 ? SpiritmasterPetHpRuleConfig.DefaultCooldownMs : cooldownMs,
                    SpiritmasterPetHpRuleConfig.MinCooldownMs, SpiritmasterPetHpRuleConfig.MaxCooldownMs).ToString(CultureInfo.InvariantCulture),
                292, 36, 82, 28);
            cooldownTextBox.Name = "spiritmasterPetHpCooldownTextBox";
            AddLabel(row, "毫秒", 382, 38, 44, 24);
            list.Controls.Add(row);
        }

        private void AddSpiritmasterPetBuffRuleRow(
            FlowLayoutPanel list,
            uint skillId = 0,
            string skillName = "",
            string key = "")
        {
            var row = CreateSpiritmasterRuleRow();
            AddLabel(row, "技能", 0, 3, 34, 24);
            var skillCombo = AddSpiritmasterSkillCombo(row, 44, 1, 514, skillId, skillName);
            skillCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var keyButton = AddSpiritmasterKeyButton(row, 570, 0, key);
            keyButton.Width = 120;
            keyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            BindAutomaticSkillButton(keyButton, skillCombo);
            ShowSpiritmasterAutomaticKeyAsLabel(keyButton);
            AddSpiritmasterDeleteButton(list, row, 722);
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
            if (IsNamedSkill(GetSkillBaseName(skill), ConditionSkillBaseNames) ||
                HasSkillTag(skill, "condition") ||
                HasUsefulSkillValue(skill.XmlTargetValidStatuses))
            {
                return "条件技能";
            }

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

            if (IsNamedSkill(GetSkillBaseName(skill), ConditionSkillBaseNames))
            {
                return "条件技能";
            }

            if (IsNamedSkill(GetSkillBaseName(skill), TriggerSkillBaseNames) ||
                HasSkillTag(skill, "counter") ||
                HasUsefulSkillValue(skill.XmlCounterSkill))
            {
                return "触发技能";
            }

            if (HasSkillTag(skill, "condition") ||
                HasUsefulSkillValue(skill.XmlTargetValidStatuses))
            {
                return "条件技能";
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

        private sealed class MaintenanceActionComboItem
        {
            public MaintenanceActionComboItem(MaintenanceRuleActionType actionType, string displayText)
            {
                ActionType = actionType;
                DisplayText = displayText;
            }

            public MaintenanceRuleActionType ActionType { get; }

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
            var type = IsNamedSkill(config.BaseName, ConditionSkillBaseNames) ||
                       IsNamedSkill(config.Name, ConditionSkillBaseNames)
                ? "条件技能"
                : config.Type;
            var node = targetNodes.Add(text, text);
            node.Tag = new SkillTreeNodeData(
                config.SkillId,
                text,
                config.BaseName,
                type,
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
            BindAutomaticSkillButton(keyButton, skillCombo);
            list.Controls.Add(row);
        }

        private async Task RefreshCurrentSkillsAsync(Button button, TreeView? availableTree, TreeView? systemTree)
        {
            var originalText = button.Text;
            button.Enabled = false;
            button.Text = "刷新中...";

            try
            {
                currentManualSkills = await _runtime.RefreshSkillsAsync(_account).ConfigureAwait(true);
                await RefreshSkillBindingsPreviewAsync().ConfigureAwait(true);
                if (availableTree is not null && skillAutoModeRadio?.Checked == true)
                {
                    PopulateAvailableSkillTreeFromSkills(availableTree, currentManualSkills);
                }

                if (systemTree is not null)
                {
                    PopulateSystemSkillTreeFromSkills(systemTree, currentManualSkills);
                }

                RefreshManualSkillMappingCombos();
                RefreshMaintenanceSkillCombos();
                RefreshSpiritmasterSkillCombos();
                RefreshOpeningSkillCombo();
                foreach (var combo in new[] { teamMentalCleanseSkillCombo, teamPhysicalCleanseSkillCombo, teamGroupCleanseSkillCombo })
                    if (combo is not null) { var skill = GetSelectedMaintenanceSkill(combo); PopulateMaintenanceSkillCombo(combo, skill.SkillId, skill.SkillName); }
                RefreshAutomaticSkillDisplays();
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
                currentManualSkills = await _runtime.RefreshSkillsAsync(_account).ConfigureAwait(true);
                await RefreshSkillBindingsPreviewAsync().ConfigureAwait(true);
                var refreshResult = RefreshSelectedSkillTreeToHighestCurrentSkills(selectedTree, currentManualSkills);
                RefreshManualSkillMappingCombos();
                RefreshMaintenanceSkillCombos();
                RefreshSpiritmasterSkillCombos();
                RefreshOpeningSkillCombo();
                foreach (var combo in new[] { teamMentalCleanseSkillCombo, teamPhysicalCleanseSkillCombo, teamGroupCleanseSkillCombo })
                    if (combo is not null) { var skill = GetSelectedMaintenanceSkill(combo); PopulateMaintenanceSkillCombo(combo, skill.SkillId, skill.SkillName); }
                RefreshAutomaticSkillDisplays();

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
                "触发技能" => new[] { "盾牌反击", "惩戒一击", "盾牌猛击" },
                "条件技能" => new[] { "共鸣烟雾", "脚踝重击" },
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

            if (IsNamedSkill(baseName, ConditionSkillBaseNames))
            {
                return "条件技能";
            }

            if (IsNamedSkill(baseName, TriggerSkillBaseNames) ||
                HasSkillTag(skill, "counter") ||
                HasUsefulSkillValue(skill.XmlCounterSkill))
            {
                return "触发技能";
            }

            if (HasSkillTag(skill, "condition") ||
                HasUsefulSkillValue(skill.XmlTargetValidStatuses))
            {
                return "条件技能";
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
            "条件技能",
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

        private static readonly string[] ConditionSkillBaseNames =
        {
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

        private string? ShowKeyboardPicker(string? currentKey, string titleText = "选择技能按键")
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
                Text = titleText
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
                Text = titleText,
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
                },
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
                    ("`", "`"),
                    ("A", "A"),
                    ("B", "B"),
                    ("C", "C"),
                    ("D", "D"),
                    ("E", "E"),
                    ("F", "F"),
                    ("G", "G"),
                    ("H", "H"),
                    ("I", "I"),
                    ("J", "J"),
                    ("K", "K"),
                    ("L", "L"),
                    ("M", "M")
                },
                new[]
                {
                    ("N", "N"),
                    ("O", "O"),
                    ("P", "P"),
                    ("Q", "Q"),
                    ("R", "R"),
                    ("S", "S"),
                    ("T", "T"),
                    ("U", "U"),
                    ("V", "V"),
                    ("W", "W"),
                    ("X", "X"),
                    ("Y", "Y"),
                    ("Z", "Z")
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
                "1" or "2" or "3" or "4" or "5" or
                    "6" or "7" or "8" or "9" or "0" or
                    "-" or "=" or
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
                "Oem3" or "Backquote" => "`",
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

        private sealed class BagCleanupRuleControls
        {
            public BagCleanupRuleControls(RoundedCheckBox checkBox, RoundedComboBox actionCombo)
            {
                CheckBox = checkBox;
                ActionCombo = actionCombo;
            }

            public RoundedCheckBox CheckBox { get; }

            public RoundedComboBox ActionCombo { get; }
        }

        private sealed partial class PathEditorControls
        {
            public PathEditorControls(SharedPathKind kind)
            {
                Kind = kind;
            }

            public SharedPathKind Kind { get; }

            public RoundedTextBox? PathNameTextBox { get; set; }

            public RoundedComboBox? SavedPathCombo { get; set; }

            public RoundedCheckBox? BindStationaryRadiusCheckBox { get; set; }

            public RoundedTextBox? StationaryRadiusTextBox { get; set; }

            public SharedPathDocument? LoadedDocument { get; set; }

            public bool SavingPath { get; set; }

            public Label? SummaryLabel { get; set; }

            public Label? StatusLabel { get; set; }

            public RoundedTextBox? PointsTextBox { get; set; }

            public ListView? GatherPointsList { get; set; }

            public Label? SelectedGatherPointLabel { get; set; }

            public RoundedComboBox? GatherCandidateCombo { get; set; }

            public RoundedTextBox? GatherSourceIdTextBox { get; set; }

            public Label? GatherSourceTypeLabel { get; set; }

            public Label? GatherStaticInfoLabel { get; set; }

            public Button? GatherKeyButton { get; set; }

            public Button? ReadNearestGatherButton { get; set; }

            public Button? ManualGatherEntryButton { get; set; }

            public Button? ManualButton { get; set; }

            public Button? StartButton { get; set; }

            public Button? StopButton { get; set; }

            public Button? CleanupNpcRefreshButton { get; set; }

            public RoundedComboBox? CleanupNpcCombo { get; set; }

            public Button? ExecutePathButton { get; set; }

            public CancellationTokenSource? ExecutePathCancellation { get; set; }

            public PathRecordingBuffer Buffer { get; } = new();

            public int SkippedCount { get; set; }

            public bool RefreshingGatherPoints { get; set; }

            public bool ManualGatherEntryEnabled { get; set; }
        }

        private sealed class GatherCandidateComboItem
        {
            public GatherCandidateComboItem(string? gatherName, uint? gatherSourceId, double? distanceMeters)
            {
                GatherName = string.IsNullOrWhiteSpace(gatherName)
                    ? "未命名采集物"
                    : gatherName.Trim();
                GatherSourceId = gatherSourceId;
                DistanceMeters = distanceMeters;
            }

            public string GatherName { get; }

            public uint? GatherSourceId { get; }

            public double? DistanceMeters { get; }

            public override string ToString()
            {
                var text = GatherName;
                if (GatherSourceId.HasValue)
                {
                    text += " | SourceId " +
                            GatherSourceId.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (DistanceMeters.HasValue && !double.IsInfinity(DistanceMeters.Value))
                {
                    text += " | " +
                            DistanceMeters.Value.ToString("F1", CultureInfo.InvariantCulture) +
                            "m";
                }

                return text;
            }
        }

        private sealed class GatherFilterComboItem
        {
            public GatherFilterComboItem(GatherObjectSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public GatherObjectSnapshot Snapshot { get; }

            public override string ToString()
            {
                var distance = Snapshot.DistanceToLocalPlayer is { } value
                    ? value.ToString("0.0", CultureInfo.InvariantCulture) + " 米"
                    : "距离未知";
                return ResolveGatherFilterDisplayName(Snapshot) + "（" + distance + "）";
            }
        }

        private sealed class GatherFilterRuleDraft
        {
            public GatherFilterRuleDraft(GatherObjectSnapshot snapshot)
            {
                Snapshot = snapshot;
                GatherSourceId = snapshot.GatherSourceId;
                DisplayName = ResolveGatherFilterDisplayName(snapshot);
            }

            public GatherFilterRuleDraft(GatherFilterRuleSettings settings)
            {
                GatherSourceId = settings.GatherSourceId;
                DisplayName = string.IsNullOrWhiteSpace(settings.Name)
                    ? "采集物 " + settings.GatherSourceId.ToString(CultureInfo.InvariantCulture)
                    : settings.Name.Trim();
                GatherKey = settings.GatherKey?.Trim() ?? string.Empty;
            }

            public GatherObjectSnapshot? Snapshot { get; }

            public uint GatherSourceId { get; }

            public string GatherKey { get; set; } = string.Empty;

            public string DisplayName { get; }
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

        private sealed class BagCleanupInventoryComboItem
        {
            public BagCleanupInventoryComboItem(string name, ulong count, int firstSlot)
            {
                Name = name;
                Count = count;
                FirstSlot = firstSlot;
            }

            public string Name { get; }

            public ulong Count { get; }

            public int FirstSlot { get; }

            public override string ToString()
            {
                var text = Name;
                if (Count > 1)
                {
                    text += " x" + Count.ToString(CultureInfo.InvariantCulture);
                }

                if (FirstSlot >= 0)
                {
                    text += " [" + (FirstSlot + 1).ToString(CultureInfo.InvariantCulture) + "]";
                }

                return text;
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
