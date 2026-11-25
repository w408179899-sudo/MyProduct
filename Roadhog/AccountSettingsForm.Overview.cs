using System.Globalization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Paths;
using Roadhog.Core.Radar;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private static readonly (SharedPathKind Kind, string Label)[] OverviewPaths =
    [
        (SharedPathKind.Revive, "复活路径"),
        (SharedPathKind.Combat, "打怪路径"),
        (SharedPathKind.Maintenance, "清包路径"),
        (SharedPathKind.Gather, "采集路径"),
        (SharedPathKind.Auction, "拍卖行路径"),
        (SharedPathKind.Stall, "倒货路径"),
        (SharedPathKind.GroceryStall, "杂货摆摊路径")
    ];

    private TabControl? accountPathTabs;
    private TabPage? accountPathPage;
    private TabPage? obstacleAvoidanceTab;
    private RoundedCheckBox? radarEnabledCheckBox;
    private AccountCombatMode selectedCombatMode;
    private Control[] overviewCombatControls = [];
    private Action? layoutOverview;
    private Label? effectiveRadiusLabel;
    private readonly Dictionary<SharedPathKind, Button> overviewPathLinks = new();

    private TabPage CreateHangupTab()
    {
        var tab = CreateBaseTab("挂机");
        var hangupTabs = new TabControl
        {
            Name = "hangupTabs", Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(92, 28), SizeMode = TabSizeMode.Fixed
        };
        hangupTabs.DrawItem += GreenTabs_DrawItem;
        hangupTabs.TabPages.Add(CreateSummaryTab());
        hangupTabs.TabPages.Add(CreateMonsterFilterTab());
        hangupTabs.TabPages.Add(CreateGatherFilterPreviewTab());
        tab.Controls.Add(hangupTabs);
        return tab;
    }

    private TabPage CreateSummaryTab()
    {
        const int width = 672;
        const int headingHeight = 24;
        const int sectionGap = 6;
        const int rowTop = 28;
        var tab = CreateBaseTab("基本设置");
        var page = CreatePagePanel();
        page.Name = "hangupOverview";
        page.AutoScroll = true;
        tab.Controls.Add(page);

        Panel Section(string title, int height)
        {
            var section = new Panel { Left = 10, Size = new Size(width - 20, height) };
            page.Controls.Add(section);
            var heading = AddLabel(section, "  " + title, 0, 0, section.Width, headingHeight, _textGreen, FontStyle.Bold);
            heading.BackColor = _inputBackground;
            return section;
        }

        Button Secondary(Control parent, string text, int x, int y, int w, EventHandler click)
        {
            var button = (RoundedButton)AddButton(parent, text, x, y, w, 28, click);
            button.BackColor = _inputBackground;
            button.ForeColor = _textGreen;
            button.BorderColor = Color.FromArgb(159, 193, 172);
            button.ShadowDepth = 0;
            return button;
        }

        var mode = Section("挂机方式", 60);
        mainModeCombo = AddCombo(mode, 10, rowTop, 139, 28, "原地打怪", "路径打怪", "路径采集", "半自动");
        mainModeCombo.Name = "hangupMode";
        stationaryCombatRadiusLabel = AddLabel(mode, "打怪半径", 162, 30, 62, 24);
        stationaryCombatRadiusTextBox = AddTextBox(mode, "30.0", 225, rowTop, 65, 28);
        stationaryCombatRadiusUnitLabel = AddLabel(mode, "米", 296, 30, 20, 24);
        pathCombatRadiusLabel = AddLabel(mode, "打怪半径", 162, 30, 62, 24);
        pathCombatRadiusTextBox = AddTextBox(mode, "30.0", 225, rowTop, 65, 28);
        pathCombatRadiusUnitLabel = AddLabel(mode, "米", 296, 30, 20, 24);
        var lockLabel = AddLabel(mode, "锁怪距离", 326, 30, 62, 24);
        targetAcquireDistanceTextBox = AddTextBox(mode, "29", 389, rowTop, 58, 28);
        targetAcquireDistanceTextBox.Name = "targetAcquireDistanceTextBox";
        var lockUnit = AddLabel(mode, "米", 453, 30, 20, 24);
        overviewCombatControls = [lockLabel, targetAcquireDistanceTextBox, lockUnit];
        effectiveRadiusLabel = AddLabel(mode, "", 483, 30, 159, 24);
        effectiveRadiusLabel.AutoEllipsis = true;

        const int behaviorRowHeight = 32;
        const int behaviorColumnWidth = 132;
        const int behaviorCheckWidth = behaviorColumnWidth - 12;
        const int behaviorLeft = 10;
        const int behaviorSecondRowTop = rowTop + behaviorRowHeight;
        const int behaviorValuesTop = rowTop + 2 * behaviorRowHeight;
        const int returnDistanceLeft = behaviorLeft + 3 * behaviorColumnWidth + behaviorCheckWidth + 4;
        var behavior = Section("战斗行为", behaviorValuesTop + behaviorRowHeight);
        RoundedCheckBox BehaviorCheck(string text, int column, int row, bool isChecked = false) =>
            AddCheckBox(behavior, text, behaviorLeft + column * behaviorColumnWidth,
                rowTop + row * behaviorRowHeight + 1, behaviorCheckWidth, isChecked);

        enableLootCheckBox = BehaviorCheck("拾取物品", 0, 0, true);
        contestMonsterCheckBox = BehaviorCheck("抢怪", 1, 0);
        counterEnemyRaceCheckBox = BehaviorCheck("反击敌对种族", 2, 0);
        preferAggressiveMonsterCheckBox = BehaviorCheck("优先攻击主动怪", 3, 0);
        jumpAssistEnabledCheckBox = BehaviorCheck("打怪跳跃", 0, 1);
        returnHomeWhenNoTargetCheckBox = BehaviorCheck("没怪回中心", 1, 1, true);
        sitWhenNoTargetAtHomeCheckBox = BehaviorCheck("没怪坐地板", 2, 1);
        returnHomeAfterCombatCheckBox = BehaviorCheck("战斗结束回原点", 3, 1);

        returnHomeAfterCombatDistanceLabel = AddLabel(behavior, "超过", returnDistanceLeft, behaviorSecondRowTop + 2, 30, 24);
        returnHomeAfterCombatDistanceTextBox = AddTextBox(behavior, "5", returnDistanceLeft + 33, behaviorSecondRowTop, 55, 28);
        returnHomeAfterCombatDistanceUnitLabel = AddLabel(behavior, "米", returnDistanceLeft + 93, behaviorSecondRowTop + 2, 19, 24);
        stalledTargetExclusionSecondsLabel = AddLabel(behavior, "卡怪排除时长", behaviorLeft, behaviorValuesTop + 2, 108, 24);
        stalledTargetExclusionSecondsTextBox = AddTextBox(behavior, "60", 126, behaviorValuesTop, 75, 28);
        stalledTargetExclusionSecondsTextBox.Name = "stalledTargetExclusionSecondsTextBox";
        stalledTargetExclusionSecondsUnitLabel = AddLabel(behavior, "秒", 208, behaviorValuesTop + 2, 24, 24);
        pathFollowReachDistanceLabel = AddLabel(behavior, "到点判定距离", behaviorLeft, behaviorValuesTop + 2, 108, 24);
        pathFollowReachDistanceTextBox = AddTextBox(behavior, "5.0", 126, behaviorValuesTop, 75, 28);
        pathFollowReachDistanceUnitLabel = AddLabel(behavior, "米", 208, behaviorValuesTop + 2, 24, 24);

        const int pathRowHeight = 32;
        var paths = Section("当前路线", 0);
        var pathToolTip = new ToolTip();
        paths.Disposed += (_, _) => pathToolTip.Dispose();
        for (var index = 0; index < OverviewPaths.Length; index++)
        {
            var (kind, label) = OverviewPaths[index];
            var link = Secondary(paths, label, 10, rowTop, 310,
                (_, _) =>
                {
                    settingsTabs.SelectedTab = accountPathPage;
                    SelectPathEditor(kind);
                });
            link.Name = "overviewPath" + kind;
            link.TextChanged += (_, _) => pathToolTip.SetToolTip(link, link.Text);
            overviewPathLinks[kind] = link;
        }

        var plan = Section("定时计划 · 当前账号", 144);
        BuildDailySchedule(plan);
        var profile = Section("方案", 94);
        profile.Controls[0].Width = 64;
        currentProfileLabel = AddLabel(profile, "", 64, 0, 323, headingHeight);
        currentProfileLabel.BackColor = _inputBackground;
        currentProfileLabel.AutoEllipsis = true;
        AddLabel(profile, "已保存方案", 10, 30, 86, 24);
        savedProfileCombo = AddCombo(profile, 100, rowTop, 283, 28);
        AddButton(profile, "加载到本账号", 394, rowTop, 134, 28, (_, _) => LoadSelectedProfile()).Name = "loadAccountProfile";
        AddButton(profile, "删除方案", 540, rowTop, 102, 28, (_, _) => DeleteSavedProfile());
        AddLabel(profile, "保存为", 10, 62, 86, 24);
        profileNameTextBox = AddTextBox(profile, "default_profile", 100, 60, 241, 28);
        var regionLabel = AddLabel(profile, "所属区服", 352, 62, 58, 24);
        regionCombo = AddCombo(profile, 412, 60, 70, 28, "未分区", "一区", "二区", "三区", "四区", "五区", "六区", "七区", "八区", "九区", "十区");
        regionCombo.Name = "accountRegionCombo";
        regionCombo.AccessibleName = "所属区服";
        regionCombo.SelectedIndexChanged += (_, _) => ChangeRegion();
        var regionTip = new ToolTip();
        regionTip.SetToolTip(regionLabel, "仅当前账号，用于选择共享拍卖行名单。");
        regionLabel.Disposed += (_, _) => regionTip.Dispose();
        AddLabel(profile, "固定频道", 492, 62, 58, 24);
        fixedChannelCombo = AddCombo(profile, 552, 60, 90, 28, new[] { "不固定" }
            .Concat(Enumerable.Range(1, ScriptSettings.MaximumFixedChannelNumber).Select(number => number + "频道")).ToArray());
        fixedChannelCombo.Name = "fixedChannelCombo";
        fixedChannelCombo.AccessibleName = "固定频道";
        profileStatusLabel = AddLabel(profile, "", 387, 0, 265, headingHeight);
        profileStatusLabel.BackColor = _inputBackground;
        profileStatusLabel.AutoEllipsis = true;

        var sections = new[] { profile, mode, behavior, paths, plan };
        for (var i = 0; i < sections.Length; i++) sections[i].TabIndex = i;
        var resizeColumns = CaptureSettingsColumns(page, width);
        var arranging = false;
        layoutOverview = () =>
        {
            if (arranging) return;
            arranging = true;
            var previousScroll = page.AutoScrollPosition;
            page.SuspendLayout();
            try
            {
                // Clear WinForms' cached display rectangle after shrinking the window.
                page.AutoScroll = false;
                var hasCombatValues = ParseMainMode(mainModeCombo.Text) == AccountMainMode.CustomCombat;
                behavior.Height = behaviorValuesTop + (hasCombatValues ? behaviorRowHeight : 0);
                var columns = page.Width >= 900 ? 3 : 2;
                var rows = (OverviewPaths.Length + columns - 1) / columns;
                paths.Height = rowTop + rows * pathRowHeight;
                var top = sectionGap;
                foreach (var section in sections)
                {
                    section.Top = top;
                    top += section.Height + sectionGap;
                }
                var scrollbar = top > page.Height ? SystemInformation.VerticalScrollBarWidth : 0;
                resizeColumns(Math.Max(width, page.Width - scrollbar - 8));
                const int pathInset = 10;
                const int columnGap = 10;
                var columnSpace = paths.Width - 2 * pathInset - (columns - 1) * columnGap;
                for (var index = 0; index < OverviewPaths.Length; index++)
                {
                    var column = index % columns;
                    var left = columnSpace * column / columns;
                    var right = columnSpace * (column + 1) / columns;
                    overviewPathLinks[OverviewPaths[index].Kind].SetBounds(
                        pathInset + left + column * columnGap, rowTop + index / columns * pathRowHeight, right - left, 28);
                }
                page.AutoScrollMinSize = new Size(width - 10, top);
            }
            finally
            {
                page.AutoScroll = true;
                page.ResumeLayout(true);
                page.AutoScrollPosition = new Point(-previousScroll.X, -previousScroll.Y);
                arranging = false;
            }
        };
        page.SizeChanged += (_, _) => layoutOverview?.Invoke();
        layoutOverview();
        mainModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (ParseMainMode(mainModeCombo.Text) == AccountMainMode.CustomCombat)
                selectedCombatMode = ParseCombatMode(mainModeCombo.Text);
            RefreshCombatModeVisibility();
            SelectSuggestedPath();
            layoutOverview?.Invoke();
        };
        returnHomeAfterCombatCheckBox.Click += (_, _) => RefreshCombatModeVisibility();
        return tab;
    }

    private void SelectSuggestedPath()
    {
        var kind = ParseMainMode(mainModeCombo?.Text) switch
        {
            AccountMainMode.Gather => SharedPathKind.Gather,
            AccountMainMode.CustomCombat when selectedCombatMode == AccountCombatMode.Path => SharedPathKind.Combat,
            _ => SharedPathKind.Revive
        };
        SelectPathEditor(kind);
    }

    private void SelectPathEditor(SharedPathKind kind)
    {
        if (accountPathTabs is not null && pathEditors.TryGetValue(kind, out var editor) && editor.Page?.Parent is TabPage route)
            accountPathTabs.SelectedTab = route;
    }

    private void RefreshEffectiveRadius()
    {
        if (effectiveRadiusLabel is null) return;
        effectiveRadiusLabel.Text = "";
        foreach (var (kind, label) in new[] { (SharedPathKind.Revive, "复活路径"), (SharedPathKind.Combat, "打怪路径") })
        {
            if (!pathEditors.TryGetValue(kind, out var editor) || editor.LoadedDocument is not { } document ||
                !string.Equals(document.Name, editor.PathNameTextBox?.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !document.TryGetBoundStationaryCombatRadius(out var radius)) continue;
            effectiveRadiusLabel.Text = $"生效 {radius:0.##} 米 · {label}";
            break;
        }
    }

    private TabPage CreateAccountOptionsTab()
    {
        var tab = CreateBaseTab("账号选项");
        var page = CreatePagePanel();
        tab.Controls.Add(page);
        generateLogFilesCheckBox = AddCheckBox(page, "生成本账号日志文件", 16, 16, 220, true);
        generateLogFilesCheckBox.Name = "generateLogFilesCheckBox";
        AddLabel(page, "保存后更新日志开关", 256, 16, 260, 26);
        return tab;
    }

    private TabPage CreateObstacleAvoidanceTab()
    {
        var tab = CreateBaseTab("绕障");
        tab.Name = "obstacleAvoidanceTab";
        var page = CreatePagePanel();
        tab.Controls.Add(page);
        var panel = new Panel { Location = new Point(12, 8), Size = new Size(656, 68), BackColor = _inputBackground };
        page.Controls.Add(panel);
        radarEnabledCheckBox = AddCheckBox(panel, "启用绕障", 12, 12, 124, false);
        radarEnabledCheckBox.Name = "radarEnabledCheckBox";
        radarEnabledCheckBox.Click += (_, _) => RefreshRadarStatus();
        radarEditorButton = AddButton(panel, "绘制雷达", 155, 10, 124, 30, (_, _) => OpenRadarEditor());
        radarEditorButton.Name = "radarEditorButton";
        AddLabel(panel, "绕障精度", 304, 13, 78, 24);
        radarEndpointExtensionNumeric = new NumericUpDown
        {
            Name = "radarEndpointExtensionNumeric", Location = new Point(384, 13), Size = new Size(90, 28),
            DecimalPlaces = 2, Increment = 0.1M,
            Minimum = (decimal)RadarNavigationGraphBuilder.MinimumExtensionMeters,
            Maximum = (decimal)RadarNavigationGraphBuilder.MaximumExtensionMeters,
            Value = (decimal)RadarNavigationGraphBuilder.ExtensionMeters,
            BackColor = _inputBackground, ForeColor = _textGreen
        };
        panel.Controls.Add(radarEndpointExtensionNumeric);
        AddLabel(panel, "米", 482, 13, 28, 24);
        AddLabel(panel, "使用雷达上的障碍线绕行。保存账号设置后应用。", 12, 43, 598, 21);
        return tab;
    }

    private TabPage CreatePathDiagnosticsTab()
    {
        var tab = CreateBaseTab("工具与诊断");
        var page = CreatePagePanel();
        tab.Controls.Add(page);
        var panel = new Panel { Location = new Point(12, 8), Size = new Size(656, 76), BackColor = _inputBackground };
        page.Controls.Add(panel);
        AddLabel(panel, "镜头校准", 12, 6, 82, 24, _textGreen, FontStyle.Bold);
        AddLabel(panel, "水平", 100, 6, 40, 24);
        cameraYawPixelsPerDegreeTextBox = AddTextBox(panel, "11.0", 146, 4, 76, 28);
        AddLabel(panel, "俯仰", 234, 6, 40, 24);
        cameraPitchPixelsPerDegreeTextBox = AddTextBox(panel, "13.0", 278, 4, 76, 28);
        AddLabel(panel, "转向重置坐标", 12, 41, 115, 24);
        deathReviveClickPointTextBox = AddTextBox(panel,
            FormatScreenPoint(PathScriptSettings.DefaultDeathReviveClickX, PathScriptSettings.DefaultDeathReviveClickY),
            146, 39, 120, 28);
        deathReviveTestMoveButton = AddButton(panel, "测试移动", 278, 39, 112, 28, TestDeathReviveMoveButton_Click);
        return tab;
    }
}
