using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class OpeningSkillListTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static Task CompatibilityAsync()
    {
        var legacy = JsonSerializer.Deserialize<OpeningSkillConfig>("""
            {"Enabled":true,"SkillId":101,"SkillName":"A","Key":"F1"}
            """)!;
        Check(legacy.GetEffectiveSkills().Single().SkillId == 101 && !legacy.ReleaseAll, "legacy becomes one entry in single mode");
        Check(legacy.Clone().GetEffectiveSkills().Single().Key == "F1", "legacy clone retains manual key");
        legacy.Skills = new()
        {
            new() { SkillId = 102, SkillName = "B", Key = "F2" },
            new() { SkillId = 103, SkillName = "C", Key = "F3" },
            new() { SkillId = 104, SkillName = "missing", Key = "F4" }
        };
        legacy.ReleaseAll = true;
        var cloned = legacy.Clone();
        cloned.Skills![0].Key = "F9";
        Check(legacy.Skills[0].Key == "F2", "clone owns its entries");
        var roundtrip = JsonSerializer.Deserialize<OpeningSkillConfig>(JsonSerializer.Serialize(legacy.Clone()))!;
        Check(roundtrip.ReleaseAll && roundtrip.GetEffectiveSkills().Select(s => s.SkillId).SequenceEqual(new uint[] { 102, 103, 104 }), "array order and mode survive clone and JSON");
        var settings = new SkillScriptSettings { OpeningSkill = roundtrip };
        var unbound = SemiAutoSkillPlan.FromSettings(settings);
        Check(unbound.OpeningSkills.Count == 3 && unbound.ReleaseAllOpeningSkills, "new list overrides legacy fields");
        Check(new uint[] { 102, 103, 104 }.All(unbound.SkillReadIds.Contains), "all openers are registered in plan read IDs");
        var snapshots = new uint[] { 102, 103 }.Select(id => new SkillSnapshot(id, "Skill " + id, 1, 1, "Skill " + id, 1, false, 0, 0)).ToArray();
        var bar = new QuickbarSnapshot(0, new QuickbarSlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, 102), new(SkillQuickbar.Alt, 1, 21, 103)
        });
        var missing = new List<string>();
        var bound = SemiAutoSkillPlan.FromSettings(settings, new SkillKeyBindings(bar, snapshots), missing.Add);
        Check(bound.OpeningSkills.Select(s => s.Key).SequenceEqual(new[] { "D1", "NumPad2" }), "every entry binds independently in list order");
        Check(missing.Count == 1 && bound.OpeningSkills.Count == 2, "unbound entry skips with a diagnostic");
        roundtrip.Skills = new();
        Check(!SemiAutoSkillPlan.FromSettings(settings).HasOpeningSkill && roundtrip.Clone().GetEffectiveSkills().Count == 0, "explicit empty list never falls back to stale legacy fields");
        settings.OpeningSkill = JsonSerializer.Deserialize<OpeningSkillConfig>("{\"Enabled\":true,\"Skills\":[null]}")!;
        Check(!SemiAutoSkillPlan.FromSettings(settings).HasOpeningSkill, "null array entries are ignored");
        settings.OpeningSkill = new() { Enabled = true, Skills = new() { new() { SkillName = "name only", Key = "F1" } } };
        Check(SemiAutoSkillPlan.FromSettings(settings).RequiresFullSkillRead, "legacy name-only entries retain name resolution");
        settings.OpeningSkill.Enabled = false;
        Check(!SemiAutoSkillPlan.FromSettings(settings).HasOpeningSkill, "disabled list cannot execute");
        return Task.CompletedTask;
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var source = new ScriptSettings();
                source.Skills.OpeningSkill = new() { Enabled = true, SkillId = 101, SkillName = "A", Key = "F1" };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "opening-list", ScriptSettings = source });
                var log = new InMemoryRoadhogLogger();
                using var form = new AccountSettingsForm("opening-list", new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(form, args);
                Control Find(string name) => form.Controls.Find(name, true).Single();
                var tabs = form.Controls.Find("openingSkillPanel", true).Single().Parent!;
                while (tabs is not TabPage) tabs = tabs.Parent!;
                ((TabControl)tabs.Parent!).SelectedTab = (TabPage)tabs;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new(-32000, -32000);
                form.Show(); Application.DoEvents();
                var rows = (FlowLayoutPanel)Find("openingSkillRows");
                var releaseAll = (RoundedCheckBox)Find("openingSkillReleaseAllCheckBox");
                Check(rows.Controls.Count == 1 && !releaseAll.Checked, "legacy UI loads one entry and defaults to single mode");
                ((Button)Find("openingSkillAddButton")).PerformClick();
                Check(rows.Controls.Count == 2, "add button creates a row");
                var second = rows.Controls[1];
                Call("PopulateOpeningSkillCombo", second.Controls["openingSkillCombo"], 102u, "B");
                ((Button)second.Controls["openingSkillUp"]!).PerformClick();
                Check(ReferenceEquals(rows.Controls[0], second), "up changes list order");
                ((Button)second.Controls["openingSkillDown"]!).PerformClick();
                Check(ReferenceEquals(rows.Controls[1], second), "down changes list order");
                ((Button)second.Controls["openingSkillUp"]!).PerformClick();
                releaseAll.Checked = true;
                var args = new object?[] { null };
                Check((bool)Call("SaveCurrentSettings", args)!, "actual settings save succeeds");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Skills.OpeningSkill;
                Check(saved.ReleaseAll && saved.Enabled && saved.Skills!.Select(s => s.SkillId).SequenceEqual(new uint[] { 102, 101 }), "save persists mode and reordered list");
                Check(saved.SkillId == 102, "legacy fields mirror first entry for older clients");
                Call("ApplyOpeningSkillSettings", JsonSerializer.Deserialize<OpeningSkillConfig>(JsonSerializer.Serialize(saved.Clone())));
                Check(rows.Controls.Count == 2 && releaseAll.Checked, "reloading retains rows and mode");
                var preview = new SkillKeyBindings(new QuickbarSnapshot(0, new QuickbarSlotSnapshot[]
                {
                    new(SkillQuickbar.Main, 0, 21, 101), new(SkillQuickbar.Alt, 1, 21, 102)
                }), Array.Empty<SkillSnapshot>());
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, preview);
                Call("RefreshAutomaticSkillDisplays");
                Check(rows.Controls[0].Controls["openingSkillKeyButton"]!.Text.Contains("Num2") &&
                    rows.Controls[1].Controls["openingSkillKeyButton"]!.Text.Contains("1"), "every row refreshes its own automatic key");
                foreach (Control row in rows.Controls)
                {
                    Check(row.Bottom <= rows.Height, "rows fit list");
                    foreach (Control child in row.Controls) Check(child.Right <= row.Width && child.Bottom <= row.Height, "row controls fit");
                    Check(!row.Controls["openingSkillKeyButton"]!.Enabled, "all rows use automatic key binding");
                }
                var previewPath = Environment.GetEnvironmentVariable("ROADHOG_OPENING_LIST_PREVIEW");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    var openingPanel = Find("openingSkillPanel");
                    using var bitmap = new System.Drawing.Bitmap(openingPanel.Width, openingPanel.Height);
                    openingPanel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                    bitmap.Save(previewPath);
                }
                form.Size = form.MinimumSize;
                Application.DoEvents();
                var panel = Find("openingSkillPanel");
                Check(panel.Bottom <= panel.Parent!.Height, "expanded list remains inside scrollable skill page");
                foreach (Control row in rows.Controls.Cast<Control>().ToArray())
                    ((Button)row.Controls["openingSkillRemove"]!).PerformClick();
                Check(rows.Controls.Count == 0, "all entries can be removed");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "empty list saves");
                saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Skills.OpeningSkill;
                Check(saved.Skills is { Count: 0 } && saved.SkillId == 0, "empty save clears legacy identity");
                Call("ApplyOpeningSkillSettings", saved);
                Check(rows.Controls.Count == 0, "empty list stays empty when reopened");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
