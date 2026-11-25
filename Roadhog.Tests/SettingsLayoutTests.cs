using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Core.Radar;

internal static class SettingsLayoutTests
{
    public static Task ResponsiveAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var settings = JsonSerializer.Deserialize<ScriptSettings>("""{"Maintenance":{}}""")!;
                settings.Paths = new PathScriptSettings
                {
                    RevivePathName = "人马西线",
                    MaintenancePathName = "村口清包",
                    AuctionPathName = "交易中介",
                    StallPathName = "仓库交接",
                    GroceryStallPathName = "杂货摊位"
                };
                settings.SemiAuto.AttackWeaveDelayMs = 530;
                settings.FixedChannelNumber = ScriptSettings.MaximumFixedChannelNumber;
                settings.Skills.OpeningSkill = new() { Enabled = true, SkillId = 101, SkillName = "起手技能", Key = "F1" };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "layout", ScriptSettings = settings });
                var profiles = new InMemoryScriptProfileStore();
                static SharedPathDocument Route(string name, int pointCount) => new()
                {
                    Name = name,
                    Points = Enumerable.Range(1, pointCount)
                        .Select(index => new SharedPathPoint { Index = index, X = index * 10 }).ToList()
                };
                var pathStore = new InMemorySharedPathStore(
                    Route("人马西线", 2), Route("村口清包", 3), Route("交易中介", 4), Route("仓库交接", 5),
                    Route("杂货摊位", 6), Route("备用交易中介", 7));
                AccountSettingsForm CreateForm() => new("layout", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    store, pathStore, profiles, new RecordingFolderLauncher(), "test-paths",
                    radarMapStore: new StaticRadarMapStore(new RadarMapDocument { MapId = 777 }));
                using var form = CreateForm();
                object Field(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
                void CheckSection(Panel section)
                {
                    var controls = section.Controls.Cast<Control>().Where(control => control.Visible).ToArray();
                    for (var i = 0; i < controls.Length; i++)
                    {
                        var control = controls[i];
                        var caption = string.IsNullOrEmpty(control.Name) ? control.Text : control.Name;
                        Check(section.ClientRectangle.Contains(control.Bounds), $"{caption} stays inside its section");
                        Check(controls.Skip(i + 1).All(other => !control.Bounds.IntersectsWith(other.Bounds)),
                            $"{caption} does not cover the next control");
                    }
                }
                Check(form.ClientSize == new Size(1000, 800), "default settings content is 1000 by 800");
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-32000, -32000);
                form.Show(); Application.DoEvents();
                var tabs = (TabControl)Field("settingsTabs");
                var pressCount = (NumericUpDown)Field("statusMaintenancePressCountNumeric");
                var pressInterval = (NumericUpDown)Field("statusMaintenancePressIntervalNumeric");
                Check(pressCount.Value == 1 && pressInterval.Value == 0, "legacy settings default to one press without an interval");
                var preview = Environment.GetEnvironmentVariable("ROADHOG_SETTINGS_LAYOUT_PREVIEW");
                void Capture(string name)
                {
                    if (string.IsNullOrWhiteSpace(preview)) return;
                    Directory.CreateDirectory(preview);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    foreach (var button in form.Controls.OfType<Button>())
                        button.DrawToBitmap(bitmap, new Rectangle(button.Location + (Size)(form.PointToScreen(Point.Empty) - (Size)form.Location), button.Size));
                    bitmap.Save(Path.Combine(preview, name + ".png"));
                }
                Check(tabs.TabPages.Cast<TabPage>().Select(tab => tab.Text).SequenceEqual(
                    new[] { "挂机", "路径", "技能", "维护", "清包", "组队", "更多" }), "primary navigation follows account setup");
                var save = (Button)form.Controls.Find("saveAccountSettings", true).Single();
                foreach (TabPage tab in tabs.TabPages)
                {
                    tabs.SelectedTab = tab; Application.DoEvents();
                    if (tab.Text == "挂机")
                    {
                        var hangup = (TabControl)tab.Controls.Find("hangupTabs", true).Single();
                        var summary = (Panel)tab.Controls.Find("hangupOverview", true).Single();
                        var loadProfile = (Button)tab.Controls.Find("loadAccountProfile", true).Single();
                        var modeSection = ((Control)Field("mainModeCombo")).Parent!;
                        var behavior = (Panel)((Control)Field("enableLootCheckBox")).Parent!;
                        var profileName = (Control)Field("profileNameTextBox");
                        var region = tab.Controls.Find("accountRegionCombo", true).Single();
                        var channel = (RoundedComboBox)tab.Controls.Find("fixedChannelCombo", true).Single();
                        var channelEditor = channel.Controls.OfType<ComboBox>().Single();
                        var channelTextWidth = channel.Items.Cast<string>()
                            .Max(item => TextRenderer.MeasureText(item, channelEditor.Font).Width);
                        var expectedRoutes = new (SharedPathKind Kind, string Tab, string Summary)[]
                        {
                            (SharedPathKind.Revive, "复活路径", "复活路径 · 人马西线（2 点）"),
                            (SharedPathKind.Combat, "打怪路径", "打怪路径 · 未选"),
                            (SharedPathKind.Maintenance, "清包路径", "清包路径 · 村口清包（3 点）"),
                            (SharedPathKind.Gather, "采集路径", "采集路径 · 未选"),
                            (SharedPathKind.Auction, "拍卖行路径", "拍卖行路径 · 交易中介（4 点）"),
                            (SharedPathKind.Stall, "转移到仓库号路径", "倒货路径 · 仓库交接（5 点）"),
                            (SharedPathKind.GroceryStall, "杂货摆摊路径", "杂货摆摊路径 · 杂货摊位（6 点）")
                        };
                        var routeLinks = expectedRoutes.Select(route =>
                            (Button)tab.Controls.Find("overviewPath" + route.Kind, true).Single()).ToArray();
                        Check(routeLinks.Select(button => button.Text).SequenceEqual(expectedRoutes.Select(route => route.Summary)),
                            "home shows the account's path names and point counts for all seven route types");
                        foreach (var size in new[] { new Size(1000, 800), new Size(1200, 900), new Size(704, 381), new Size(1000, 800) })
                        {
                            form.ClientSize = size; Application.DoEvents();
                            Capture(size.Width < 800 ? "挂机-小窗口" : "挂机-标准");
                            Check(!summary.HorizontalScroll.Visible, $"home uses vertical scrolling only at {size}; viewport {summary.ClientSize}, extent {summary.DisplayRectangle}");
                            Check(save.Bottom <= tabs.Top && form.ClientRectangle.Contains(save.Bounds), "save remains separate from tabs at each width");
                            Check(size.Width < 800 || !summary.VerticalScroll.Visible, "home fits normal window");
                            CheckSection((Panel)loadProfile.Parent!);
                            CheckSection((Panel)modeSection);
                            CheckSection(behavior);
                            var behaviorRows = behavior.Controls.OfType<RoundedCheckBox>()
                                .Where(checkBox => checkBox.Visible).GroupBy(checkBox => checkBox.Top)
                                .Select(row => row.Select(checkBox => checkBox.Left).Order().ToArray()).ToArray();
                            Check(behaviorRows.Length == 2 && behaviorRows[0].Length == 4 &&
                                behaviorRows[0].SequenceEqual(behaviorRows[1]),
                                "stationary combat shows two aligned rows of four options on opening");
                            var routeColumns = routeLinks.Count(button => button.Top == routeLinks[0].Top);
                            Check(routeColumns == (size.Width < 900 ? 2 : 3), "overview adapts route columns to the window");
                            Check(loadProfile.Visible && loadProfile.Parent!.Bottom <= modeSection.Top &&
                                summary.ClientRectangle.Contains(summary.RectangleToClient(loadProfile.RectangleToScreen(loadProfile.ClientRectangle))),
                                "profile controls are immediately accessible above the mode");
                            Check(region.Visible && region.Parent == profileName.Parent && region.Top == profileName.Top &&
                                region.Left > profileName.Right &&
                                summary.ClientRectangle.Contains(summary.RectangleToClient(region.RectangleToScreen(region.ClientRectangle))),
                                "region fits to the right of the profile name at each window width");
                            Check(channel.Visible && channel.Parent == region.Parent && channel.Top == region.Top &&
                                channel.Left > region.Right &&
                                summary.ClientRectangle.Contains(summary.RectangleToClient(channel.RectangleToScreen(channel.ClientRectangle))),
                                "fixed channel is visible to the right of the region at each window width");
                            Check(channelEditor.ClientSize.Width >= channelTextWidth + SystemInformation.VerticalScrollBarWidth,
                                $"channel editor at {size} has room for the longest value and arrow");
                            foreach (var routeLink in routeLinks)
                            {
                                Check(routeLink.Visible && routeLink.Parent!.ClientRectangle.Contains(routeLink.Bounds),
                                    $"{routeLink.Name} stays inside the route section at {size}");
                                Check(routeLinks.All(other => other == routeLink || !other.Bounds.IntersectsWith(routeLink.Bounds)),
                                    $"{routeLink.Name} does not overlap other paths at {size}");
                            }
                            Check(routeLinks[0].Parent!.Bottom <= ((Control)Field("scheduleOnlineCheckBox")).Parent!.Top,
                                "all routes fit above the schedule");
                            Check(!((Control)Field("scheduleStartTextBox")).Visible, "disabled schedules hide inactive time controls");
                        }
                        Check(!behavior.Controls.OfType<Button>().Any(), "combat options have no expand or collapse button");
                        var schedule = (RoundedCheckBox)Field("scheduleOnlineCheckBox");
                        var relogin = (RoundedCheckBox)Field("scheduleReloginCheckBox");
                        var clickToggle = typeof(RoundedCheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        clickToggle.Invoke(schedule, [EventArgs.Empty]);
                        clickToggle.Invoke(relogin, [EventArgs.Empty]);
                        var modeChooser = (RoundedComboBox)Field("mainModeCombo");
                        foreach (var size in new[] { new Size(1000, 800), new Size(704, 381) })
                        {
                            form.ClientSize = size; Application.DoEvents();
                            Check(!summary.HorizontalScroll.Visible, "overview with schedules enabled scrolls vertically in a narrow window");
                            foreach (var modeIndex in new[] { 0, 1, 2, 3 })
                            {
                                modeChooser.SelectedIndex = modeIndex;
                                CheckSection((Panel)modeSection);
                                CheckSection(behavior);
                                CheckSection((Panel)schedule.Parent!);
                            }
                            summary.ScrollControlIntoView(relogin);
                            Check(summary.ClientRectangle.Contains(summary.RectangleToClient(relogin.RectangleToScreen(relogin.ClientRectangle))),
                                "the schedule remains reachable with the combat options shown");
                        }
                        form.ClientSize = new Size(1000, 800);
                        modeChooser.SelectedIndex = 0;
                        Capture("挂机-定时开启");
                        clickToggle.Invoke(schedule, [EventArgs.Empty]);
                        clickToggle.Invoke(relogin, [EventArgs.Empty]);
                        summary.AutoScrollPosition = Point.Empty;
                        var routes = (TabControl)form.Controls.Find("pathTabs", true).Single();
                        foreach (var (link, route) in routeLinks.Zip(expectedRoutes))
                        {
                            link.PerformClick();
                            Check(tabs.SelectedTab?.Text == "路径" && routes.SelectedTab?.Text == route.Tab,
                                $"{link.Name} opens the matching editor");
                            tabs.SelectedTab = tab;
                        }
                        foreach (TabPage filter in hangup.TabPages.Cast<TabPage>().Skip(1))
                        {
                            hangup.SelectedTab = filter; Application.DoEvents(); Capture("挂机-" + filter.Text);
                        }
                        hangup.SelectedIndex = 0; Application.DoEvents();
                    }
                    if (tab.Text == "路径")
                    {
                        var pathTabs = tab.Controls.OfType<Panel>().Single().Controls.OfType<TabControl>().Single();
                        var shortcut = (Button)tab.Controls.Find("pathObstacleAvoidanceButton", true).Single();
                        var obstacleTab = pathTabs.TabPages[^1];
                        Check(obstacleTab.Text == "绕障", "obstacle settings are the last path tab");
                        Check(shortcut.Visible && shortcut.Bottom < pathTabs.Top, "the obstacle shortcut sits above the path tabs");
                        shortcut.PerformClick();
                        Check(pathTabs.SelectedTab == obstacleTab, "the shortcut opens the last path tab");
                        Check(form.Controls.Find("aStarWalk", true).Length == 0 && form.Controls.Find("aStarLoad", true).Length == 0,
                            "the removed standalone navigation feature has no commands in account settings");
                        form.ClientSize = new Size(704, 381); Application.DoEvents();
                        Check(Enumerable.Range(0, pathTabs.TabCount).All(index => pathTabs.ClientRectangle.Contains(pathTabs.GetTabRect(index))),
                            "the path tab strip wraps within narrow windows");
                        Check(pathTabs.Parent!.ClientRectangle.Contains(shortcut.Bounds), "the obstacle shortcut stays accessible in a narrow window");
                        Capture("路径-小窗口");
                        form.ClientSize = new Size(1000, 800); Application.DoEvents();
                        foreach (TabPage pathTab in pathTabs.TabPages)
                        {
                            pathTabs.SelectedTab = pathTab; Application.DoEvents();
                            if (pathTab.Text == "绕障")
                            {
                                Check(pathTab.Controls.Find("radarEditorButton", true).Single().Visible &&
                                    pathTab.Controls.Find("radarEnabledCheckBox", true).Single().Visible,
                                    "radar editor and on-off control are accessible together");
                                Capture("路径-" + pathTab.Text);
                                continue;
                            }
                            if (pathTab.Text == "工具与诊断")
                            {
                                Check(!form.Controls.Find("radarEditorButton", true).Single().Visible,
                                    "radar controls are confined to the obstacle page");
                                Capture("路径-" + pathTab.Text);
                                continue;
                            }
                            var coordinate = pathTab.Controls.Find("pathCoordinateX", true).Single();
                            var list = coordinate.Parent!.Controls.OfType<ListView>().Single();
                            var foldout = (Button)pathTab.Controls.Find("pathAdvancedOptions", true).Single();
                            Check(list.Bottom < coordinate.Top, "path list cannot overlap coordinate editor");
                            var expandedHeight = list.Height;
                            foldout.PerformClick(); Application.DoEvents();
                            Check(list.Height > expandedHeight && list.Bottom < coordinate.Top,
                                "collapsing path options gives their space back to the point list");
                            foldout.PerformClick(); Application.DoEvents();
                            Check(list.Height == expandedHeight, "reopening path options restores the layout");
                            if (pathTab == pathTabs.TabPages[0])
                            {
                                form.ClientSize = new Size(704, 381); Application.DoEvents();
                                var editorPage = (Panel)coordinate.Parent!;
                                var undo = editorPage.Controls.OfType<Button>().Single(button => button.Text == "撤销上一步");
                                editorPage.ScrollControlIntoView(undo);
                                Check(editorPage.ClientRectangle.Contains(editorPage.RectangleToClient(undo.RectangleToScreen(undo.ClientRectangle))),
                                    "scrolling a narrow route editor reaches the lower right action");
                                Capture("路径-小窗口底部");
                                editorPage.AutoScrollPosition = Point.Empty;
                            }
                            form.ClientSize = new(1200, 900); Application.DoEvents();
                            Check(list.Bottom < coordinate.Top, "expanded path list cannot overlap coordinate editor");
                            form.ClientSize = new(1000, 800); Application.DoEvents();
                            Capture("路径-" + pathTab.Text);
                        }
                        pathTabs.SelectedIndex = 0; Application.DoEvents();
                    }
                    if (tab.Text == "组队")
                    {
                        var role = (Control)Field("teamRoleCombo");
                        var selectedIndex = role.GetType().GetProperty("SelectedIndex")!;
                        for (var index = 0; index < 3; index++)
                        {
                            selectedIndex.SetValue(role, index); Application.DoEvents();
                            Capture("组队-" + role.Text);
                        }
                        var healRules = (FlowLayoutPanel)Field("teamHealSkillRuleList");
                        var cleanse = (Control)Field("teamMentalCleanseKeyButton");
                        var initialCleanseTop = cleanse.Top;
                        var addHeal = healRules.Parent!.Controls.OfType<Button>().Single(b => b.Text == "新增加血技能");
                        for (var i = 0; i < 6; i++) addHeal.PerformClick();
                        Application.DoEvents();
                        Check(cleanse.Top > initialCleanseTop && cleanse.Top > healRules.Bottom,
                            "healing rules grow without overlapping cleanse controls");
                        while (healRules.Controls.Count > 0)
                            healRules.Controls[0].Controls.OfType<Button>().Single(button => button.Text == "删除").PerformClick();
                        Application.DoEvents();
                        Check(cleanse.Top == initialCleanseTop, "empty healing list releases its reserved space");
                        selectedIndex.SetValue(role, 0); Application.DoEvents();
                    }
                    if (tab.Text == "维护")
                    {
                        var sections = (FlowLayoutPanel)tab.Controls.Find("maintenanceSections", true).Single();
                        foreach (var section in sections.Controls.OfType<Panel>())
                            section.Controls.OfType<Button>().Single().PerformClick();
                        Application.DoEvents();
                        var ordered = sections.Controls.Cast<Control>().ToArray();
                        Check(ordered.Zip(ordered.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top),
                            "expanded maintenance sections do not overlap");
                        Check(pressCount.Visible && pressInterval.Visible && pressCount.Parent == pressInterval.Parent,
                            "press timing is accessible inside the status maintenance foldout");
                        Capture("维护-展开");
                        foreach (var section in sections.Controls.OfType<Panel>())
                            section.Controls.OfType<Button>().Single().PerformClick();
                        Application.DoEvents();
                    }
                    if (tab.Text == "清包")
                    {
                        var warehouse = (Button)tab.Controls.Find("cleanupWorkflowOptionsButton", true).Single();
                        var cleanup = (Panel)warehouse.Parent!;
                        var inventory = tab.Controls.Find("bagCleanupInventoryCheckedListBox", true).Single();
                        var names = (Panel)inventory.Parent!;
                        var selectors = (TableLayoutPanel)names.Controls.Find("bagCleanupNameListTabs", true).Single();
                        void CheckCleanupLayout()
                        {
                            Check(!cleanup.HorizontalScroll.Visible, "cleanup lists fit at the normal window width");
                            CheckSection(names);
                        }
                        warehouse.PerformClick(); Application.DoEvents();
                        CheckCleanupLayout();
                        Capture("清包-仓库配置");
                        warehouse.PerformClick();
                        ((RadioButton)Field("groceryCleanupModeRadio")).Checked = true;
                        Application.DoEvents();
                        CheckCleanupLayout();
                        Capture("清包-杂货摆摊");
                        ((RadioButton)Field("normalCleanupModeRadio")).Checked = true;
                        foreach (var selector in selectors.Controls.OfType<RadioButton>())
                        {
                            selector.Checked = true;
                            Application.DoEvents();
                            CheckCleanupLayout();
                            Capture("清包-" + selector.Text);
                        }
                        selectors.Controls.OfType<RadioButton>().First().Checked = true;
                        Application.DoEvents();
                    }
                    if (tab.Text == "更多")
                    {
                        var otherTabs = (TabControl)tab.Controls.Find("otherTabs", true).Single();
                        foreach (TabPage otherTab in otherTabs.TabPages)
                        {
                            otherTabs.SelectedTab = otherTab; Application.DoEvents();
                            Capture("更多-" + otherTab.Text);
                        }
                        otherTabs.SelectedIndex = 0; Application.DoEvents();
                    }
                    Capture(tab.Text);
                }
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "技能");
                Application.DoEvents();
                var left = (TreeView)Field("quickbarAvailableSkillTree");
                var right = (TreeView)Field("quickbarSelectedSkillTree");
                var opening = form.Controls.Find("openingSkillPanel", true).Single();
                Check(left.Size == right.Size && left.Width > 316 && left.Height is >= 210 and < 292,
                    "both skill trees retain their width and use the compact half-height area");
                Check(opening.Top > right.Bottom && opening.Right == right.Parent!.ClientSize.Width, "opening section spans panel under both trees");
                var oldSize = left.Size;
                form.ClientSize = new(1200, 900); Application.DoEvents();
                Check(left.Width > oldSize.Width && left.Height > oldSize.Height, "trees grow with window");
                Check(left.Height == oldSize.Height + 50, "100px of added window height gives the skill trees half that height");
                form.ClientSize = new(1000, 800); Application.DoEvents();
                Check(left.Size == oldSize, $"resize roundtrip cannot accumulate geometry drift: {oldSize} -> {left.Size}");
                var skillPage = (Panel)right.Parent!.Parent!;
                Check(!skillPage.HorizontalScroll.Visible && !skillPage.VerticalScroll.Visible, "default size does not retain unnecessary scrollbars");
                void CheckTreeActions()
                {
                    var buttons = right.Parent!.Controls.OfType<Button>().Where(b => new[] { "置顶", "上移", "下移", "置底", "移除", "清空" }.Contains(b.Text)).OrderBy(b => b.Top).ToArray();
                    foreach (var button in buttons)
                        Check(button.Left > right.Right && button.Top >= right.Top && button.Bottom <= right.Bottom && button.Height == 30,
                            "right actions keep their control size and fit inside the reduced tree height");
                    Check(buttons.Zip(buttons.Skip(1)).All(pair => pair.First.Bottom < pair.Second.Top), "compact action buttons never overlap");
                }
                CheckTreeActions();
                var rows = (FlowLayoutPanel)Field("openingSkillRows");
                var add = (Button)form.Controls.Find("openingSkillAddButton", true).Single();
                for (var i = 0; i < 4; i++)
                {
                    add.PerformClick();
                    if (i == 0) { Application.DoEvents(); Capture("技能-两个起手技能"); }
                }
                Application.DoEvents();
                Check(rows.Controls.Count == 5 && opening.Top > right.Bottom, "adding openers keeps sections separated");
                CheckTreeActions();
                while (rows.Controls.Count > 1) ((Button)rows.Controls[^1].Controls["openingSkillRemove"]!).PerformClick();
                Application.DoEvents();
                Check(left.Size == oldSize, "removing added rows restores list space");
                form.Size = form.MinimumSize; Application.DoEvents();
                var page = (Panel)right.Parent.Parent!;
                Check(page.HorizontalScroll.Visible && page.VerticalScroll.Visible, "small windows retain scroll access");
                CheckTreeActions();
                form.ClientSize = new(1000, 800); Application.DoEvents();
                pressCount.Value = 3;
                pressInterval.Value = 80;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var refreshDraft = typeof(AccountSettingsForm).GetMethod("RefreshAccountDraftStatus", flags)!;
                var preserveDraft = typeof(AccountSettingsForm).GetMethod("ResolveAccountDraftChoice", flags)!;
                refreshDraft.Invoke(form, null);
                var status = (Label)Field("accountSaveStatus");
                Check(status.Text.Contains("有未保存修改"), "account header reflects unsaved edits on another page");
                Check(!(bool)preserveDraft.Invoke(form, [DialogResult.Cancel])!, "cancel keeps the current draft");
                save.PerformClick();
                Check(save.Text == "已保存", "settings remain saveable");
                Check(status.Text.StartsWith("已保存"), "save reflects the committed account draft");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SemiAuto.AttackWeaveDelayMs == 530 && saved.Skills.OpeningSkill.GetEffectiveSkills().Single().SkillId == 101, "layout changes preserve settings");
                Check(saved.Maintenance.StatusMaintenancePressCount == 3 && saved.Maintenance.StatusMaintenancePressIntervalMs == 80,
                    "maintenance saves status maintenance timing to the account");
                var profile = profiles.LoadAsync(saved.ProfileName).GetAwaiter().GetResult().Value!;
                var restored = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(profile.Settings))!;
                Check(restored.Maintenance.StatusMaintenancePressCount == 3 && restored.Maintenance.StatusMaintenancePressIntervalMs == 80,
                    "status maintenance timing survives profile cloning and JSON");
                var newProfile = new ScriptSettings { ProfileName = "备用路径采集", MainMode = AccountMainMode.Gather };
                newProfile.Paths.AuctionPathName = "备用交易中介";
                newProfile.Paths.StallPathName = "缺失的仓库路线";
                profiles.SaveAsync(new Roadhog.Core.Profiles.ScriptProfileDocument { Name = newProfile.ProfileName, Settings = newProfile })
                    .GetAwaiter().GetResult();
                typeof(AccountSettingsForm).GetMethod("RefreshProfileLibrary", flags)!.Invoke(form, null);
                tabs.SelectedIndex = 0;
                var homeTabs = (TabControl)form.Controls.Find("hangupTabs", true).Single();
                homeTabs.SelectedIndex = 0;
                var chooser = (RoundedComboBox)Field("savedProfileCombo");
                var load = (Button)form.Controls.Find("loadAccountProfile", true).Single();
                var range = (RoundedTextBox)form.Controls.Find("targetAcquireDistanceTextBox", true).Single();
                range.Text = "37";
                void SelectAlternate() => chooser.SelectedIndex = Array.FindIndex(chooser.Items.Cast<object>().ToArray(),
                    item => item.ToString()!.Contains(newProfile.ProfileName));
                SelectAlternate();
                form.AccountDraftChoiceForTesting = _ => DialogResult.Cancel;
                load.PerformClick(); Application.DoEvents();
                Check(range.Text == "37", "cancelling profile switch preserves the visible draft");
                SelectAlternate();
                form.AccountDraftChoiceForTesting = _ => DialogResult.No;
                load.PerformClick(); Application.DoEvents();
                Check(((RoundedComboBox)Field("mainModeCombo")).Text == "路径采集" &&
                    ((TabControl)form.Controls.Find("pathTabs", true).Single()).SelectedTab!.Text == "采集路径",
                    "profile switch loads the selected draft and suggests the matching path");
                Check(form.Controls.Find("overviewPathAuction", true).Single().Text == "拍卖行路径 · 备用交易中介（7 点）" &&
                    form.Controls.Find("overviewPathStall", true).Single().Text == "倒货路径 · 缺失的仓库路线（未保存或不可用）" &&
                    form.Controls.Find("overviewPathGroceryStall", true).Single().Text == "杂货摆摊路径 · 未选",
                    "switching profiles updates changed, unavailable, and cleared route summaries");
                Check(store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.MainMode == AccountMainMode.CustomCombat,
                    "loading a profile into the form does not save changes to the account");
                using var reopened = CreateForm();
                Check(((NumericUpDown)reopened.Controls.Find(pressCount.Name, true).Single()).Value == 3 &&
                    ((NumericUpDown)reopened.Controls.Find(pressInterval.Name, true).Single()).Value == 80,
                    "reopening the overview shows the saved timing");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
