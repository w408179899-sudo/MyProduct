using System.Text.Json;
using Roadhog.Core.Accounts;
using static SharedSkillAuditFixture;

internal static class SpiritmasterPetHpMaintenanceTests
{
    private static readonly uint[] Skills = { 1678, 1785, 900009 };

    public static Task SkillSpecificDefaultsAsync()
    {
        foreach (var id in new uint[] { 1676, 1677, 1678, 1705, 1785, 900009 })
        {
            var expected = id == 1785 ? (40, 0) : id == 900009 ? (68, 0) : (30, 80);
            var rule = new SpiritmasterPetHpRuleConfig { SkillId = id };
            Check((rule.BelowPercent, rule.MinPlayerHpPercent) == expected, "skill-specific defaults apply to new rules: " + id);
            var loaded = JsonSerializer.Deserialize<SpiritmasterPetHpRuleConfig>($"{{\"SkillId\":{id}}}")!;
            Check((loaded.BelowPercent, loaded.MinPlayerHpPercent) == expected, "missing JSON fields use the skill default: " + id);
            rule.BelowPercent = 17;
            rule.MinPlayerHpPercent = 0;
            var copied = JsonSerializer.Deserialize<SpiritmasterPetHpRuleConfig>(JsonSerializer.Serialize(rule.Clone()))!;
            Check(copied.BelowPercent == 17 && copied.MinPlayerHpPercent == 0, "explicit overrides including zero survive clone/save/load: " + id);
        }
        return Task.CompletedTask;
    }

    public static Task ConfigurationCompatibilityAsync()
    {
        var legacy = JsonSerializer.Deserialize<SpiritmasterPetHpRuleConfig>(
            """{"SkillId":1785,"SkillName":"精灵强化:恢复 I","Key":"D4","BelowPercent":22,"CooldownMs":10300}""")!;
        Check(legacy.BelowPercent == 22 && legacy.MinPlayerHpPercent == 0, "legacy pet threshold survives while absent player floor defaults to zero");
        var saved = JsonSerializer.Serialize(legacy);
        Check(!saved.Contains("CooldownMs", StringComparison.Ordinal), "obsolete interval is ignored on load and omitted on save");
        legacy.MinPlayerHpPercent = 65;
        var settings = new ScriptSettings();
        settings.Skills.Spiritmaster.PetHpMaintenanceRules.Add(legacy);
        var clone = settings.Clone();
        legacy.MinPlayerHpPercent = 40;
        var roundtrip = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(clone))!;
        var rule = roundtrip.Skills.Spiritmaster.PetHpMaintenanceRules.Single();
        Check(rule.BelowPercent == 22 && rule.MinPlayerHpPercent == 65 && rule.SkillId == 1785 && rule.Key == "D4",
            "full settings clone and JSON roundtrip independently preserve both HP limits and skill binding");
        Check(new SpiritmasterPetHpRuleConfig().MinPlayerHpPercent == 0, "new rules default to no player HP floor");
        return Task.CompletedTask;
    }

    public static async Task EverySkillUsesConfiguredPlayerFloorAsync()
    {
        foreach (var id in Skills)
        foreach (var (floor, hp, allowed) in new[] { (0, 1u, true), (40, 39u, false), (40, 40u, true), (65, 64u, false), (0, 0u, false) })
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddPetHpRule(id, floor);
            f.Api.Player = f.Api.Player with { CurrentHp = hp };
            await f.PrepareAsync();
            await f.TickAsync();
            Sequence(allowed ? new[] { "D4" } : Array.Empty<string>(), f.Keyboard.Keys, $"ID {id}, configured player floor {floor}, actual HP {hp}");
            Check((f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null) == allowed, "only accepted living-player input starts confirmation");
        }
    }

    public static async Task PetThresholdRemainsIndependentAsync()
    {
        foreach (var id in Skills)
        foreach (var (initialHp, freshHp, allowed) in new[] { (23u, 23u, false), (22u, 22u, true), (20u, 23u, false) })
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddPetHpRule(id);
            f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules.Single().BelowPercent = 22;
            f.Api.Player = f.Api.Player with { CurrentHp = 1 };
            f.SetPet(11, initialHp);
            await f.PrepareAsync();
            f.Observer.BeforePetHealthRead = _ => f.SetPet(11, freshHp);
            await f.TickAsync();
            Sequence(allowed ? new[] { "D4" } : Array.Empty<string>(), f.Keyboard.Keys,
                $"ID {id}: pet threshold remains active with player floor zero; initial {initialHp}, final {freshHp}");
        }
    }

    public static async Task EverySkillConfirmsRetriesAndUsesActualCooldownAsync()
    {
        foreach (var id in Skills)
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetHpRule(id);
            f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == id ? skill with { CooldownDuration = 500 } : skill).ToArray();
            await f.PrepareAsync();
            f.Confirm(id);
            await f.TickAsync();
            Sequence(new[] { "D1" }, f.Keyboard.Keys, "actual cooling prevents initial pet heal");
            f.Keyboard.Keys.Clear();
            await Task.Delay(550);
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "ready skill starts shared confirmation");
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == id, "every configured identity has confirmation state");

            f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
            f.Settings.Skills.Spiritmaster.OpeningAttackKey = "F8";
            f.Api.TargetOwnServerObjectId++;
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "pending generic heal reserves new-target opening and attack input");
            f.Settings.Skills.Spiritmaster.OpeningAttackKey = "";

            Due(f);
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "unchanged published snapshot does not prove success or permit retry");
            f.SetPet(11, 45);
            f.Confirm(id);
            Due(f);
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null && f.Keyboard.Keys.Count == 1,
                "real cooldown alone cannot prove pet HP recovery and blocks retry");
            await Task.Delay(550);
            Due(f);
            await f.TickAsync();
            Sequence(new[] { "D4", "D4" }, f.Keyboard.Keys, "fresh non-increased HP with ready CD retries the configured skill");
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.AttemptCount == 2, "retry has new HP baseline and attempt count");

            f.SetPet(11, 55);
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "actual pet HP increase confirms the generic skill");
            Check(f.Keyboard.Keys.Last() == "D1", "confirmation yields to normal attack in the same tick");
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count(key => key == "D4") == 3, "ready actual CD permits a new heal without the removed 10.3 second interval");
            f.SetPet(11, 60);
            f.Confirm(id);
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "second heal also confirms");
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count(key => key == "D4") == 3, "real CD still blocks another heal after confirmation");
        }
    }

    public static async Task GenericConfirmationReservesMaintenanceAsync()
    {
        foreach (var followup in new[] { "hp", "mp", "pet_buff" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetHpRule(1785);
            await f.PrepareAsync();
            await f.TickAsync();
            if (followup == "pet_buff") f.AddPetBuff();
            else f.ConfigureResource(followup);
            f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "unconfirmed generic heal reserves subsequent " + followup);
        }
    }

    public static async Task GenericRetryRechecksBothThresholdsAsync()
    {
        foreach (var change in new[] { "player_below", "player_equal", "disabled_floor", "pet_threshold", "rule_removed", "player_dead", "pet_replaced" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetHpRule(1785, 40);
            await f.PrepareAsync();
            await f.TickAsync();
            var rule = f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules.Single();
            Due(f);
            f.SetPet(11, 50);
            f.Observer.BeforePetHealthRead = _ =>
            {
                if (change == "player_below") f.Api.Player = f.Api.Player with { CurrentHp = 39 };
                if (change == "player_equal") f.Api.Player = f.Api.Player with { CurrentHp = 40 };
                if (change == "player_dead") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            };
            if (change == "disabled_floor") { rule.MinPlayerHpPercent = 0; f.Api.Player = f.Api.Player with { CurrentHp = 1 }; }
            if (change == "pet_threshold") rule.BelowPercent = 22;
            if (change == "rule_removed") f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules.Clear();
            if (change == "pet_replaced") f.SetPet(12, 50);
            await f.TickAsync();
            var retry = change is "player_equal" or "disabled_floor";
            Check(f.Keyboard.Keys.Count(key => key == "D4") == (retry ? 2 : 1), "retry respects updated pet/player constraints: " + change);
            Check((f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null) == retry, "obsolete/unsafe attempt is cancelled: " + change);
            if (change == "player_dead") Sequence(new[] { "D4" }, f.Keyboard.Keys, "player death never releases followup input");
        }
    }

    public static async Task ZeroFloorStillRechecksLifeAndStopAsync()
    {
        foreach (var boundary in new[] { "player_dead", "pet_dead", "stop" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetHpRule(1785);
            await f.PrepareAsync();
            f.Observer.BeforePetHealthRead = _ =>
            {
                if (boundary == "player_dead") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
                if (boundary == "pet_dead") f.SetPet(11, 0);
                if (boundary == "stop") f.Cancel();
            };
            try { await f.TickAsync(); Check(boundary != "stop", "manual stop must cancel the tick"); }
            catch (OperationCanceledException) when (boundary == "stop") { }
            Check(f.Keyboard.Keys.IsEmpty && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null,
                "zero player floor preserves final life/stop checks: " + boundary);
        }
    }

    public static async Task DuplicateSkillRulesKeepTheirOwnThresholdsAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetHpRule(1785, 65);
        var rules = f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules;
        rules.Single().BelowPercent = 22;
        var second = rules.Single().Clone();
        second.BelowPercent = 35;
        second.MinPlayerHpPercent = 0;
        rules.Add(second);
        f.Api.Player = f.Api.Player with { CurrentHp = 40 };
        f.SetPet(11, 20);
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "first rule is blocked by its player floor, second rule can heal");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.RuleIndex == 1, "confirmation retains the selected rule among duplicate skill IDs");
        f.SetPet(11, 19);
        Due(f);
        await f.TickAsync();
        Sequence(new[] { "D4", "D4" }, f.Keyboard.Keys, "retry uses the selected rule's zero floor instead of the earlier rule's 65 percent");
    }

    private static void Due(SharedSkillAuditFixture f) =>
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);
}
