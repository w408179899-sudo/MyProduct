using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;

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
                settings.Combat.SmartPreAimEnabled = true;
                settings.SemiAuto.AttackWeaveDelayMs = 530;
                settings.Skills.OpeningSkill = new() { Enabled = true, SkillId = 101, SkillName = "起手技能", Key = "F1" };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "layout", ScriptSettings = settings });
                using var form = new AccountSettingsForm("layout", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
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
                            foreach (var column in new[]
                            {
                                new[] { "enableLootCheckBox", "smartPreAimEnabledCheckBox", "returnHomeWhenNoTargetCheckBox" },
                                new[] { "contestMonsterCheckBox", "smartPreAimUseFightTargetPositionCheckBox", "sitWhenNoTargetAtHomeCheckBox" },
                                new[] { "counterEnemyRaceCheckBox", "smartPreAimResponsiveSwitchingCheckBox", "jumpAssistEnabledCheckBox" },
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
                        selectedIndex.SetValue(role, 0); Application.DoEvents();
                    }
                    Capture(tab.Text);
                }
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "技能");
                Application.DoEvents();
                var left = (TreeView)Field("availableSkillTree");
                var right = (TreeView)Field("selectedSkillTree");
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
