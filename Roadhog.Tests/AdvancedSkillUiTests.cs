using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class AdvancedSkillUiTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static Task EditorAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var source = new ScriptSettings();
                source.QuickbarSkills.ExecutionTree.Add(new() { SkillId = 101, Name = "ordinary" });
                source.Skills.OpeningSkill = new() { Enabled = true, SkillId = 201, SkillName = "opening" };
                source.SemiAuto.AttackWeaveDelayMs = 730;
                source.AdvancedSkills.Elite.Skills = new() { new() { SkillId = 301, Name = "精灵弱化 III",
                    Children = new() { new() { SkillId = 302, Name = "连续技" } } }, new() { SkillId = 303, Name = "侵蚀 V" } };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "advanced-ui", ScriptSettings = source });
                var profiles = new InMemoryScriptProfileStore();
                var logger = new InMemoryRoadhogLogger();
                var api = new FakeGameApi
                {
                    WorldObjects = new WorldObjectSnapshot[]
                    {
                        new(1, 1, "遍历怪物", "monster", null, 1, 100, 100),
                        new(2, 2, "遍历怪物", "monster", null, 1, 100, 100),
                        new(3, 3, "玩家", "player", null, 1), new(4, 4, "尸体", "monster", null, 1, 0, 100)
                    }
                };
                using var form = new AccountSettingsForm("advanced-ui", new RoadhogRuntime(api, logger, new AccountRuntimeManager(logger), null!),
                    store, new InMemorySharedPathStore(), profiles, new RecordingFolderLauncher(), "test-paths");
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(form, args);
                T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
                void Show(Form window) { window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new(-20000, -20000); window.Show(); Application.DoEvents(); }
                void Capture(Form window, string name)
                {
                    var path = Environment.GetEnvironmentVariable("ROADHOG_ADVANCED_UI_PREVIEW");
                    if (string.IsNullOrEmpty(path)) return;
                    Directory.CreateDirectory(path); using var bitmap = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(path, name + ".png"));
                }
                Show(form);
                var entry = Find<Button>(form, "advancedSkillSettingsButton");
                var tab = entry.Parent!; while (tab is not TabPage) tab = tab.Parent!;
                ((TabControl)tab.Parent!).SelectedTab = (TabPage)tab; Application.DoEvents();
                Check(entry.Visible && entry.Parent!.ClientRectangle.Contains(entry.Bounds), "actual settings page has an accessible advanced entry");
                Capture(form, "01-高级设置入口");

                var draft = new AdvancedSkillSettings();
                using (var monsters = (Form)Call("CreateAdvancedMonsterDialog", draft, AdvancedMonsterCategory.Elite)!)
                {
                    Show(monsters);
                    var input = Find<RoundedTextBox>(monsters, "advancedManualMonsterName");
                    input.Text = "  手动精英  "; Find<Button>(monsters, "advancedAddManualMonster").PerformClick();
                    Check(draft.Elite.MonsterNames.SequenceEqual(new[] { "手动精英" }) && input.Text.Length == 0, "manual button adds and trims name");
                    input.Text = "回车怪物";
                    typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(input.Controls.OfType<TextBox>().Single(), new object[] { new KeyEventArgs(Keys.Enter) });
                    Check(draft.Elite.MonsterNames.Contains("回车怪物"), "Enter adds the manually typed name");
                    var refresh = Find<Button>(monsters, "advancedScanMonsters"); refresh.PerformClick();
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!refresh.Enabled && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
                    var available = Find<ListBox>(monsters, "advancedAvailableMonsters");
                    Check(refresh.Enabled && available.Items.Count == 1, "scan deduplicates same names and ignores players/dead monsters");
                    available.SelectedIndex = 0; Find<Button>(monsters, "advancedAddSelectedMonster").PerformClick();
                    Check(draft.Elite.MonsterNames.Contains("遍历怪物"), "scan selection shares the add path");
                    Capture(monsters, "03-遍历与手动添加怪物");
                }
                using (var duplicate = (Form)Call("CreateAdvancedMonsterDialog", draft, AdvancedMonsterCategory.Ranged)!)
                {
                    Show(duplicate); Find<RoundedTextBox>(duplicate, "advancedManualMonsterName").Text = "手动精英";
                    Find<Button>(duplicate, "advancedAddManualMonster").PerformClick();
                    Check(draft.Ranged.MonsterNames.Count == 0 && Find<Label>(duplicate, "advancedMonsterStatus").Text.Contains("精英怪物"),
                        "manual cross-category duplicate is blocked with the existing owner shown");
                }

                using (var dialog = (Form)Call("CreateAdvancedSkillSettingsDialog")!)
                {
                    Show(dialog);
                    Check(!Find<RoundedCheckBox>(dialog, "advancedSkillsEnabled").Checked, "new module defaults off");
                    var cards = Find<TableLayoutPanel>(dialog, "advancedSkillCards");
                    foreach (var size in new[] { new Size(1000, 700), new Size(1200, 900), new Size(940, 650), new Size(1000, 700) })
                    {
                        dialog.ClientSize = size; Application.DoEvents();
                        foreach (Control card in cards.Controls)
                        {
                            Check(cards.ClientRectangle.Contains(card.Bounds), "all four cards remain in the viewport");
                            foreach (Control child in card.Controls) Check(card.ClientRectangle.Contains(child.Bounds), "card control stays inside " + child.Name);
                        }
                        foreach (var name in new[] { "advancedHeader", "advancedFooter" })
                        {
                            var panel = Find<Panel>(dialog, name);
                            foreach (Control child in panel.Controls)
                                Check(panel.ClientRectangle.Contains(child.Bounds), "header/footer action remains accessible: " + child.Name);
                        }
                    }
                    var elite = Find<Control>(dialog, "advancedCard_Elite");
                    var rows = Find<FlowLayoutPanel>(elite, "advancedSkillRows");
                    Check(rows.Controls.Count == 2, "saved advanced list loads");
                    var first = rows.Controls[0];
                    Find<Button>(first, "advancedSkillDown").PerformClick();
                    Check(ReferenceEquals(rows.Controls[1], first), "move down preserves row data");
                    Find<Button>(first, "advancedSkillUp").PerformClick();
                    Find<Button>(elite, "advancedAddSkill").PerformClick();
                    var added = rows.Controls[2]; Find<Button>(added, "advancedSkillRemove").PerformClick();
                    Check(rows.Controls.Count == 2, "remove only removes the added row");
                    Capture(dialog, "02-高级技能设置");
                    Find<RoundedCheckBox>(dialog, "advancedSkillsEnabled").Checked = true;
                    Find<Button>(dialog, "advancedSkillSave").PerformClick();
                    Check(dialog.DialogResult == DialogResult.OK, "actual save handler succeeds");
                }
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single();
                Check(saved.ScriptSettings!.AdvancedSkills.Enabled && saved.ScriptSettings.AdvancedSkills.Elite.Skills.Count == 2 &&
                    saved.ScriptSettings.AdvancedSkills.Elite.Skills[0].Children.Single().SkillId == 302, "save preserves advanced order and complete chain");
                Check(saved.ScriptSettings.Skills.OpeningSkill.GetEffectiveSkills().Single().SkillId == 201 &&
                    saved.ScriptSettings.QuickbarSkills.ExecutionTree.Single().SkillId == 101 && saved.ScriptSettings.SemiAuto.AttackWeaveDelayMs == 730,
                    "advanced UI does not replace original opening, ordinary skills or weave settings");
                Check(profiles.LoadAsync(saved.ProfileName).GetAwaiter().GetResult().Value!.Settings.AdvancedSkills.Enabled,
                    "profile save includes advanced settings");
                using (var reopened = (Form)Call("CreateAdvancedSkillSettingsDialog")!)
                {
                    Show(reopened); Check(Find<RoundedCheckBox>(reopened, "advancedSkillsEnabled").Checked, "reopen retains saved state");
                    Find<RoundedCheckBox>(reopened, "advancedSkillsEnabled").Checked = false;
                    reopened.Close();
                }
                var captured = (ScriptSettings)Call("CaptureScriptSettings")!;
                Check(captured.AdvancedSkills.Enabled, "closing without Save discards the dialog draft");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
