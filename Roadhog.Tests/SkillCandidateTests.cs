using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class SkillCandidateTests
{
    public static Task HighestAndCompatibilityAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
                var settings = new ScriptSettings();
                settings.Skills.OpeningSkill = new() { Enabled = true, SkillId = 900, SkillName = "精气吸收 I", Key = "F1" };
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "candidates", ScriptSettings = settings });
                var log = new InMemoryRoadhogLogger();
                using var form = new AccountSettingsForm("candidates", new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm)
                    .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
                SkillSnapshot Skill(uint id, string name, int tier, string? baseName = null) =>
                    new(id, name, 1, 1, baseName, tier, false, 0, 0);
                var skills = new[]
                {
                    Skill(900, "精气吸收 I", 1), Skill(100, "精气吸收 II", 2),
                    Skill(100, "精气吸收 II", 2),
                    Skill(200, "召唤:风之精灵 I", 1), Skill(201, "召唤:风之精灵 II", 2),
                    Skill(202, "召唤:火之精灵 I", 1),
                    Skill(300, "技能别名 I", 1, "同类技能"), Skill(301, "技能别名 II", 2, "同类技能"),
                    Skill(400, "", 1), Skill(401, "", 1),
                    Skill(500, "未知技能", 1) with { DisplayTier = null, ItemLevel = 1 },
                    Skill(501, "未知技能", 1) with { DisplayTier = null, ItemLevel = 2 },
                };
                var field = typeof(AccountSettingsForm).GetField("currentManualSkills", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var opening = form.Controls.Find("openingSkillCombo", true).Single();
                using var maintenance = (Control)Activator.CreateInstance(opening.GetType())!;
                uint Id(object item) => (uint)item.GetType().GetProperty("SkillId")!.GetValue(item)!;
                object[] Items(Control combo) => ((System.Collections.IEnumerable)combo.GetType().GetProperty("Items")!.GetValue(combo)!).Cast<object>().ToArray();
                object Selected(Control combo) => combo.GetType().GetProperty("SelectedItem")!.GetValue(combo)!;
                foreach (var reverse in new[] { false, true })
                {
                    field.SetValue(form, reverse ? skills.Reverse().ToArray() : skills);
                    foreach (var (method, combo) in new[] { ("PopulateMaintenanceSkillCombo", maintenance), ("PopulateOpeningSkillCombo", opening) })
                    {
                        Call(method, combo, 0u, "");
                        var ids = Items(combo).Select(Id).Where(id => id != 0).Order().ToArray();
                        Check(ids.SequenceEqual(new uint[] { 100, 201, 202, 301, 400, 401, 501 }), method + " keeps highest ranks, distinct summons and unnamed IDs regardless of input order");
                        Call(method, combo, 900u, "精气吸收 I");
                        Check(Id(Selected(combo)) == 900 && Items(combo).Count(item => Id(item) == 900) == 1, "saved lower rank remains selected exactly once");
                        Call(method, combo, 999u, "未读取到的旧技能");
                        Check(Id(Selected(combo)) == 999, "missing saved ID remains available");
                        field.SetValue(form, Array.Empty<SkillSnapshot>());
                        Call(method, combo, 900u, "精气吸收 I");
                        Check(Id(Selected(combo)) == 900, "empty skill read preserves selection");
                        field.SetValue(form, reverse ? skills.Reverse().ToArray() : skills);
                    }
                }
                Call("PopulateOpeningSkillCombo", opening, 900u, "精气吸收 I");
                var saveArgs = new object?[] { null };
                Check((bool)Call("SaveCurrentSettings", saveArgs)!, "save succeeds with legacy lower rank selected");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Skills.OpeningSkill;
                Check(saved.GetEffectiveSkills().Single().SkillId == 900, "saving cannot silently upgrade the old selection");
                Check(((IReadOnlyList<SkillSnapshot>)field.GetValue(form)!).Count == skills.Length, "candidate filtering leaves original snapshots intact");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
