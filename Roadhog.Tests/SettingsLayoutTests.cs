using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
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
                var settings = new ScriptSettings();
                settings.SemiAuto.AttackWeaveDelayMs = 530;
                settings.Skills.OpeningSkill = new() { Enabled = true, SkillId = 101, SkillName = "起手技能", Key = "F1" };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "layout", ScriptSettings = settings });
                using var form = new AccountSettingsForm("layout", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths",
                    radarMapStore: new StaticRadarMapStore(new RadarMapDocument { MapId = 777 }));
                object Field(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
                Check(form.ClientSize == new Size(1000, 800), "default settings content is 1000 by 800");
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-32000, -32000);
                form.Show(); Application.DoEvents();
                var tabs = (TabControl)Field("settingsTabs");
                var preview = Environment.GetEnvironmentVariable("ROADHOG_SETTINGS_LAYOUT_PREVIEW");
                void Capture(string name)
                {
                    if (string.IsNullOrWhiteSpace(preview)) return;
                    Directory.CreateDirectory(preview);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    foreach (var button in form.Controls.OfType<Button>())
                        button.DrawToBitmap(bitmap, new Rectangle(button.Location + (Size)(form.PointToScreen(Point.Empty) - (Size)form.Location), button.Size));
                    // DrawToBitmap paints overlapping WinForms children in the wrong order.
                    // Repaint summary headers back-to-front so their embedded editors remain visible.
                    if (tabs.SelectedTab?.Text == "总览")
                        foreach (var section in tabs.SelectedTab.Controls.OfType<Panel>().Single().Controls.OfType<Panel>())
                            foreach (var control in section.Controls.Cast<Control>().Reverse().Where(c => c.Visible && c.Top < 28))
                                control.DrawToBitmap(bitmap, new Rectangle(control.PointToScreen(Point.Empty) - (Size)form.Location, control.Size));
                    bitmap.Save(Path.Combine(preview, name + ".png"));
                }
                foreach (TabPage tab in tabs.TabPages)
                {
                    tabs.SelectedTab = tab; Application.DoEvents();
                    if (tab.Text == "总览")
                    {
                        foreach (var size in new[] { new Size(1000, 800), new Size(1200, 900), new Size(1000, 800) })
                        {
                            form.ClientSize = size; Application.DoEvents();
                            Check(!tab.Controls.OfType<Panel>().Single().HorizontalScroll.Visible,
                                "summary does not retain a horizontal scrollbar after resizing");
                            var summaryPage = tab.Controls.OfType<Panel>().Single();
                            Check(!summaryPage.VerticalScroll.Visible,
                                "compact summary fits the default window without vertical scrolling");
                            var logging = (Control)Field("generateLogFilesCheckBox");
                            var loggingBounds = summaryPage.RectangleToClient(logging.RectangleToScreen(logging.ClientRectangle));
                            Check(summaryPage.ClientRectangle.Contains(loggingBounds),
                                "logging option remains fully accessible without scrolling");
                            var channel = (Control)Field("fixedChannelCombo");
                            var radar = (Control)Field("radarEditorButton");
                            Check(radar.Visible && channel.Right < radar.Left && radar.Bottom <= radar.Parent!.ClientSize.Height,
                                "channel selector and radar action fit on the same row without overlap");
                            foreach (var column in new[]
                            {
                                new[] { "enableLootCheckBox", "returnHomeWhenNoTargetCheckBox" },
                                new[] { "contestMonsterCheckBox", "sitWhenNoTargetAtHomeCheckBox" },
                                new[] { "counterEnemyRaceCheckBox", "jumpAssistEnabledCheckBox" },
                                new[] { "combatModeCombo", "stalledTargetExclusionSecondsTextBox" }
                            })
                                Check(column.Select(name => ((Control)Field(name)).Left).Distinct().Count() == 1,
                                    "summary columns remain aligned after resizing: " + string.Join(", ", column));
                        }
                    }
                    if (tab.Text == "路径")
                    {
                        var pathTabs = tab.Controls.OfType<Panel>().Single().Controls.OfType<TabControl>().Single();
                        foreach (TabPage pathTab in pathTabs.TabPages)
                        {
                            pathTabs.SelectedTab = pathTab; Application.DoEvents();
                            var coordinate = pathTab.Controls.Find("pathCoordinateX", true).Single();
                            var list = coordinate.Parent!.Controls.OfType<ListView>().Single();
                            Check(list.Bottom < coordinate.Top, "path list cannot overlap coordinate editor");
                            form.ClientSize = new(1200, 900); Application.DoEvents();
                            Check(list.Bottom < coordinate.Top, "expanded path list cannot overlap coordinate editor");
                            form.ClientSize = new(1000, 800); Application.DoEvents();
                            Capture("路径-" + pathTab.Text);
                        }
                        pathTabs.SelectedIndex = 0; Application.DoEvents();
                    }
                    if (tab.Text == "过滤")
                    {
                        var filterTabs = (TabControl)tab.Controls.Find("filterTabs", true).Single();
                        foreach (TabPage filterTab in filterTabs.TabPages)
                        {
                            filterTabs.SelectedTab = filterTab; Application.DoEvents();
                            Capture("过滤-" + filterTab.Text);
                        }
                        filterTabs.SelectedIndex = 0; Application.DoEvents();
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
                        Capture("维护-展开");
                        foreach (var section in sections.Controls.OfType<Panel>())
                            section.Controls.OfType<Button>().Single().PerformClick();
                        Application.DoEvents();
                    }
                    if (tab.Text == "清包")
                    {
                        var warehouse = (Button)tab.Controls.Find("cleanupWorkflowOptionsButton", true).Single();
                        warehouse.PerformClick(); Application.DoEvents();
                        Capture("清包-仓库配置");
                        warehouse.PerformClick();
                        ((RadioButton)Field("groceryCleanupModeRadio")).Checked = true;
                        Application.DoEvents(); Capture("清包-杂货摆摊");
                        ((RadioButton)Field("normalCleanupModeRadio")).Checked = true;
                        Application.DoEvents();
                    }
                    if (tab.Text == "其他")
                    {
                        var otherTabs = (TabControl)tab.Controls.Find("otherTabs", true).Single();
                        foreach (TabPage otherTab in otherTabs.TabPages)
                        {
                            otherTabs.SelectedTab = otherTab; Application.DoEvents();
                            Capture("其他-" + otherTab.Text);
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
                Check(left.Size == right.Size && left.Width > 316 && left.Height > 292, "both skill trees use added width and height equally");
                Check(opening.Top > right.Bottom && opening.Right == right.Parent!.ClientSize.Width, "opening section spans panel under both trees");
                var oldSize = left.Size;
                form.ClientSize = new(1200, 900); Application.DoEvents();
                Check(left.Width > oldSize.Width && left.Height > oldSize.Height, "trees grow with window");
                form.ClientSize = new(1000, 800); Application.DoEvents();
                Check(left.Size == oldSize, $"resize roundtrip cannot accumulate geometry drift: {oldSize} -> {left.Size}");
                var skillPage = (Panel)right.Parent!.Parent!;
                Check(!skillPage.HorizontalScroll.Visible && !skillPage.VerticalScroll.Visible, "default size does not retain unnecessary scrollbars");
                foreach (var button in right.Parent!.Controls.OfType<Button>().Where(b => new[] { "置顶", "上移", "下移", "置底", "移除", "清空" }.Contains(b.Text)))
                    Check(button.Left > right.Right && button.Top >= right.Top && button.Bottom <= right.Bottom, "right actions stay within tree height");
                var rows = (FlowLayoutPanel)Field("openingSkillRows");
                var add = (Button)form.Controls.Find("openingSkillAddButton", true).Single();
                for (var i = 0; i < 4; i++) add.PerformClick();
                Application.DoEvents();
                Check(rows.Controls.Count == 5 && opening.Top > right.Bottom, "adding openers keeps sections separated");
                while (rows.Controls.Count > 1) ((Button)rows.Controls[^1].Controls["openingSkillRemove"]!).PerformClick();
                Application.DoEvents();
                Check(left.Size == oldSize, "removing added rows restores list space");
                form.Size = form.MinimumSize; Application.DoEvents();
                var page = (Panel)right.Parent.Parent!;
                Check(page.HorizontalScroll.Visible && page.VerticalScroll.Visible, "small windows retain scroll access");
                form.ClientSize = new(1000, 800); Application.DoEvents();
                var saveArgs = new object?[] { null };
                Check((bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, saveArgs)!, "settings remain saveable");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SemiAuto.AttackWeaveDelayMs == 530 && saved.Skills.OpeningSkill.GetEffectiveSkills().Single().SkillId == 101, "layout changes preserve settings");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
