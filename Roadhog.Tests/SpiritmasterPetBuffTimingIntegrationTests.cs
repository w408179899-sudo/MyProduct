using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using static SharedSkillAuditFixture;

internal static partial class SpiritmasterPetBuffTimingTests
{
    public static async Task AfterLootAsync()
    {
        var names = new[] { "ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "ROADHOG_LOOT_AFTER_PICK_WAIT_MS",
            "ROADHOG_LOOT_PRESS_COUNT", "ROADHOG_LOOT_PRESS_INTERVAL_MS" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, name.EndsWith("COUNT") ? "1" : "0");
            foreach (var loot in new[] { true, false })
            {
                using var f = new SharedSkillAuditFixture();
                f.AddPetBuff(runTiming: MaintenanceRuleRunTiming.AfterCombat);
                f.Settings.MainMode = AccountMainMode.CustomCombat;
                f.Settings.CombatMode = AccountCombatMode.Stationary;
                f.Settings.Combat = new() { EnableLoot = loot, HasStationaryCombatPosition = true, StationaryCombatRadius = 60 };
                f.Api.Player = f.Api.Player with { Position = new(0, 0, 0) };
                f.Api.TargetCurrentHp = 0;
                f.Api.TargetPosition = new(2.5f, 0, 0);
                var pressed = false;
                f.Keyboard.AfterPress = key => { if (key == "Alt+D1") pressed = true; };
                // Status appears during the confirmation wait, not during the burst.
                f.BeforePetBuffDelay = count => { if (pressed && count == 3) f.SetPet(11, 50, 1662); };
                await f.PrepareAsync();
                var controller = new StationaryCombatController(f.Keyboard, f.Controller);
                var state = new StationaryCombatState { Fighting = true, CurrentTargetEntityId = 100, CandidateEntityId = 100 };
                await controller.TickAsync(f.Context, f.Plan, f.State, state);
                Sequence((loot ? new[] { "NumPadDecimal" } : Array.Empty<string>()).Concat(Enumerable.Repeat("Alt+D1", 3)),
                    f.Keyboard.Keys, "after-combat pet buff follows loot and also runs when loot is disabled");
                Check(f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662) &&
                    !f.State.HasSpiritmasterPetBuffConfirmation && !state.LootAfterKill.Active,
                    "post-combat round waits for delayed pet status before finishing");
            }
        }
        finally { for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]); }
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var config = new AccountConfig { AccountName = "pet-timing-ui", ScriptSettings = new() };
                var configs = new InMemoryAccountConfigStore(config);
                var logger = new InMemoryRoadhogLogger();
                var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!, configs);
                using var form = new AccountSettingsForm(config.AccountName, runtime, configs, new InMemorySharedPathStore(),
                    new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "paths");
                object? Call(string name, params object?[] values) => typeof(AccountSettingsForm)
                    .GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(form, values);
                using var dialog = (Form)Call("CreateSpiritmasterSettingsDialog")!;
                var list = (FlowLayoutPanel)typeof(AccountSettingsForm)
                    .GetField("spiritmasterPetBuffRuleList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                list.Controls.Clear();
                Call("AddSpiritmasterPetBuffRuleRow", list, 1662u, "强化甲胄 I", "Alt+D1", Type.Missing, 9000u);
                var combo = list.Controls[0].Controls.Find("spiritmasterPetBuffTimingCombo", false).Single();
                var selected = combo.GetType().GetProperty("SelectedIndex")!;
                Check((int)selected.GetValue(combo)! == 0, "UI defaults to Always");
                for (var i = 0; i < 3; i++)
                {
                    selected.SetValue(combo, i);
                    var captured = (SpiritmasterSkillSettings)Call("CaptureSpiritmasterSettings")!;
                    Check(captured.PetBuffRules.Single().RunTiming == (MaintenanceRuleRunTiming)i &&
                        captured.PetBuffRules.Single().AbnormalStatusId == 9000,
                        "UI captures timing without losing the configured abnormal status");
                    var settings = new ScriptSettings();
                    settings.Skills.Spiritmaster = captured;
                    Call("ApplyScriptSettings", settings.Clone());
                    combo = list.Controls[0].Controls.Find("spiritmasterPetBuffTimingCombo", false).Single();
                    Check((int)selected.GetValue(combo)! == i, "UI reload restores selected timing");
                }
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
