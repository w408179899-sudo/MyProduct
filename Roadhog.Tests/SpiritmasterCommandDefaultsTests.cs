using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class SpiritmasterCommandDefaultsTests
{
    public static Task DefaultsAndOverridesAsync()
    {
        foreach (var settings in new[] { new SpiritmasterSkillSettings(), JsonSerializer.Deserialize<SpiritmasterSkillSettings>("{}")! })
        {
            Check(settings.SummonSkills.Count == 2, "defaults contain two summon slots and a separate attack command");
            Check(settings.SummonSkills[0].SkillId == 1778 && settings.SummonSkills[0].Key == "", "single-rank handprint uses skill identity without a fixed key");
            Check(settings.SummonSkills[1].SkillId == 0 && settings.SummonSkills[1].Key == "Alt+OemMinus", "pet summon defaults to manual Alt-minus");
            Check(settings.OpeningAttackSkillId == 0 && settings.OpeningAttackKey == "Alt+D2", "pet attack defaults to manual Alt-2");
            Check(JsonSerializer.Serialize(settings.Clone()) == JsonSerializer.Serialize(settings), "defaults survive clone and save");
        }
        var custom = JsonSerializer.Deserialize<SpiritmasterSkillSettings>("""{"SummonSkills":[{"Key":"F7"},{"Key":"F8"}],"OpeningAttackKey":"F9"}""")!;
        Sequence(new[] { "F7", "F8" }, custom.Clone().SummonSkills.Select(rule => rule.Key), "existing explicit summon keys survive defaults");
        Check(custom.Clone().OpeningAttackKey == "F9", "existing explicit attack command survives defaults");
        var disabled = JsonSerializer.Deserialize<SpiritmasterSkillSettings>("""{"SummonSkills":[],"OpeningAttackKey":""}""")!;
        Check(disabled.Clone().SummonSkills.Count == 0 && disabled.Clone().OpeningAttackKey == "", "explicit empty actions remain disabled");
        return Task.CompletedTask;
    }

    public static Task AutomaticBindingAsync()
    {
        foreach (var (bar, slot, expected) in new[] { (SkillQuickbar.Alt, 6, "Alt+D7"), (SkillQuickbar.Main, 2, "D3") })
        {
            var bindings = new SkillKeyBindings(new(0, new[] { new QuickbarSlotSnapshot(bar, slot, 21, 1778) }), new[] { Skill(1778, "服从的手印", 1) });
            var bound = bindings.ApplyTo(new ScriptSettings()).Skills.Spiritmaster;
            Sequence(new[] { expected, "Alt+OemMinus" }, bound.SummonSkills.Select(rule => rule.Key), "handprint follows actual bar position while manual summon stays fixed");
            Check(bound.OpeningAttackKey == "Alt+D2", "automatic binding preserves manual pet attack");
        }
        var missing = new SkillKeyBindings(new(0, Array.Empty<QuickbarSlotSnapshot>()), Array.Empty<SkillSnapshot>()).ApplyTo(new ScriptSettings()).Skills.Spiritmaster;
        Check(missing.SummonSkills[0].Key == "", "missing handprint never falls back to a stale fixed key");
        Check(missing.SummonSkills[1].Key == "Alt+OemMinus" && missing.OpeningAttackKey == "Alt+D2", "missing skill does not remove manual commands");
        return Task.CompletedTask;
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                using var form = new AccountSettingsForm("command-defaults", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    new InMemoryAccountConfigStore(new AccountConfig { AccountName = "command-defaults", ScriptSettings = new() }),
                    new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                var bindings = new SkillKeyBindings(new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Alt, 6, 21, 1778) }), new[] { Skill(1778, "服从的手印", 1) });
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, bindings);
                object? Call(string name) => typeof(AccountSettingsForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                using var dialog = (Form)Call("CreateSpiritmasterSettingsDialog")!;
                var list = (FlowLayoutPanel)typeof(AccountSettingsForm).GetField("spiritmasterSummonRuleList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Check(list.Controls.Count == 3, "editor shows exactly the three requested actions");
                var first = list.Controls[0].Controls.Find("spiritmasterRuleKeyButton", true).Single();
                Check(!first.Enabled && first.Text.Contains("A+7"), "handprint renders the automatic Alt-7 binding");
                Check(list.Controls[1].Controls.Find("spiritmasterRuleKeyButton", true).Single().Text == "A+-", "summon button renders manual Alt-minus");
                Check(list.Controls[2].Controls.Find("spiritmasterRuleKeyButton", true).Single().Text == "A+2", "attack button renders manual Alt-2");
                var captured = (SpiritmasterSkillSettings)Call("CaptureSpiritmasterSettings")!;
                Check(captured.SummonSkills[0].SkillId == 1778 && captured.SummonSkills[0].Key == "" &&
                    captured.SummonSkills[1].Key == "Alt+OemMinus" && captured.OpeningAttackKey == "Alt+D2", "UI capture preserves automatic/manual ownership");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
