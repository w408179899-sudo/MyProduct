using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static partial class SpiritmasterPetBuffTimingTests
{
    public static Task ConfigurationAsync()
    {
        Check(new SpiritmasterPetBuffRuleConfig().RunTiming == MaintenanceRuleRunTiming.Always,
            "new pet buffs default to Always");
        Check(JsonSerializer.Deserialize<SpiritmasterPetBuffRuleConfig>("{\"SkillId\":1662}")!.RunTiming == MaintenanceRuleRunTiming.Always,
            "legacy rules without a timing field default to Always");
        foreach (var timing in Enum.GetValues<MaintenanceRuleRunTiming>())
        {
            var settings = new ScriptSettings();
            settings.Skills.Spiritmaster.PetBuffRules.Add(new() { SkillId = 1662, RunTiming = timing });
            var copy = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(settings.Clone()))!;
            Check(copy.Skills.Spiritmaster.PetBuffRules.Single().RunTiming == timing, "clone and save preserve timing");
        }
        return Task.CompletedTask;
    }

    public static async Task CombatSelectionAsync()
    {
        foreach (var timing in Enum.GetValues<MaintenanceRuleRunTiming>())
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(runTiming: timing);
            await f.PrepareAsync();
            await f.TickAsync();
            Check(f.Keyboard.Keys.Contains("Alt+D1") == (timing != MaintenanceRuleRunTiming.AfterCombat),
                $"combat must select only Always/InCombat pet buffs: {timing}");
        }
    }

    public static async Task MaintenanceSelectionAsync()
    {
        foreach (var phase in new[] { MaintenanceRuleRunTiming.Always, MaintenanceRuleRunTiming.AfterCombat })
        foreach (var timing in Enum.GetValues<MaintenanceRuleRunTiming>())
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(runTiming: timing);
            f.Api.TargetEntityId = 0;
            f.Keyboard.AfterPress = key => { if (key == "Alt+D1") f.SetPet(11, 50, 1662); };
            await f.PrepareAsync();
            var handled = await f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player,
                allowSitMaintenance: false, plan: f.Plan, runTiming: phase);
            var expected = timing == MaintenanceRuleRunTiming.Always || timing == phase;
            Check(f.Keyboard.Keys.Contains("Alt+D1") == expected && handled == expected,
                $"pet-only maintenance runs without a monster: {phase}/{timing}");
            Check(!f.State.HasSpiritmasterPetBuffConfirmation, "standalone maintenance settles confirmation before returning");
        }
    }

    public static async Task IdleAndTimeoutAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff(runTiming: MaintenanceRuleRunTiming.Always);
        f.Api.TargetEntityId = 0;
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(Enumerable.Repeat("Alt+D1", 3), f.Keyboard.Keys, "idle Always buff gets one bounded burst");
        Check(f.Logged("semi_auto.spiritmaster.pet_buff_unconfirmed", "skillId", 1662), "idle timeout is observed");
        Check(!f.State.HasSpiritmasterPetBuffConfirmation, "idle timeout releases reservation");
        var count = f.Keyboard.Keys.Count;
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == count, "timeout does not immediately replay the buff");
    }

    public static async Task MixedRulesAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff(1662, MaintenanceRuleRunTiming.AfterCombat);
        f.AddPetBuff(1787, MaintenanceRuleRunTiming.InCombat);
        f.Keyboard.AfterPress = key =>
        {
            if (key == "Alt+D1") f.SetPet(11, 50, 1662, 1787);
            if (key == "Alt+D2") f.SetPet(11, 50, 1787);
        };
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "Alt+D2" }, f.Keyboard.Keys, "first after-combat rule cannot hide a later combat rule");
        await f.TickAsync();
        f.Keyboard.Keys.Clear();
        await f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player,
            allowSitMaintenance: false, plan: f.Plan, runTiming: MaintenanceRuleRunTiming.AfterCombat);
        Sequence(new[] { "Alt+D1" }, f.Keyboard.Keys, "after-combat uses only its missing rule");

        using var excluded = new SharedSkillAuditFixture();
        excluded.AddPetBuff(runTiming: MaintenanceRuleRunTiming.Always);
        await excluded.PrepareAsync();
        Check(!await excluded.Controller.TryHandleMaintenanceAsync(excluded.Context, excluded.State, excluded.Api.Player,
            allowSitMaintenance: false, plan: excluded.Plan, runTiming: MaintenanceRuleRunTiming.AfterCombat,
            includeAlwaysRules: false), "includeAlwaysRules=false excludes Always pet rules too");
        Check(excluded.Keyboard.Keys.IsEmpty, "excluded Always rules deliver no input");
    }

    public static async Task GuardsAsync()
    {
        foreach (var scenario in new[] { "missing", "dead", "player_dead", "disabled", "dp", "replacement", "cancel", "rest" })
        {
            using var f = new SharedSkillAuditFixture();
            var id = scenario == "dp" ? 1787u : 1662u;
            f.AddPetBuff(id, MaintenanceRuleRunTiming.AfterCombat);
            f.Settings.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = LowHpId, SkillName = "heal I", Key = "D2" });
            if (scenario == "missing") f.Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(10, DateTimeOffset.Now);
            if (scenario == "dead") f.SetPet(11, 0);
            if (scenario == "player_dead") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            if (scenario == "disabled") f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            if (scenario == "dp") f.Api.Player = f.Api.Player with { CurrentDp = 0 };
            if (scenario == "rest") f.Api.Player = f.Api.Player with { MotionMode = 1, StanceFlags = 5 };
            await f.PrepareAsync();
            var prepared = false;
            f.Keyboard.AfterPress = key =>
            {
                Check(prepared, "movement callback must precede standalone inputs");
                if (key != "Alt+D1") return;
                if (scenario == "replacement") f.SetPet(12, 50, 1662);
                if (scenario == "cancel") f.Cancel();
                if (scenario == "rest") f.SetPet(11, 50, 1662);
            };
            try
            {
                await f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player,
                    allowSitMaintenance: false, plan: f.Plan, runTiming: MaintenanceRuleRunTiming.AfterCombat,
                    beforeMaintenanceKeyPress: () => { prepared = true; return Task.CompletedTask; });
                Check(scenario != "cancel", "stop propagates cancellation");
            }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            if (scenario is "replacement" or "cancel")
                Sequence(new[] { "Alt+D1" }, f.Keyboard.Keys, "pet replacement/stop ends inputs");
            else if (scenario == "rest")
                Check(f.Keyboard.Keys.Count == 2 && f.Keyboard.Keys.Last() == "Alt+D1" &&
                    f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.rest_exit_before_key"),
                    "resting character stands before standalone pet buff");
            else Check(f.Keyboard.Keys.IsEmpty, $"{scenario} cannot cast or summon");
            if (scenario != "cancel") Check(!f.State.HasSpiritmasterPetBuffConfirmation, "no stale reservation");
        }
    }
}
