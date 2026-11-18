using System.Reflection;
using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedPetSkillEligibilityTests
{
    private const uint Absorb = 1771, Summon = 1696;
    public static IEnumerable<(string, Func<Task>)> Cases => new (string, Func<Task>)[]
    {
        ("combat flow v2 pet prerequisites classify exact effects and pet target metadata", ClassificationAsync),
        ("combat flow v2 pet prerequisites block absorb without pet and use another MP rule", MissingAbsorbAsync),
        ("combat flow v2 pet prerequisites allow absorb with own living pet", LivingAbsorbAsync),
        ("combat flow v2 pet prerequisites stop MP retries when pet dies", RetryDeathAsync),
        ("combat flow v2 pet prerequisites stop MP retries when pet changes", RetryChangeAsync),
        ("combat flow v2 pet prerequisites recheck pet after maintenance preparation", PreparationAsync),
        ("combat flow v2 pet prerequisites resolve manual maintenance key without rewriting it", ManualAsync),
        ("combat flow v2 pet prerequisites leave legacy maintenance unchanged", LegacyAsync),
        ("combat flow v2 pet prerequisites respect disabled and other class scope", ScopeAsync),
        ("combat flow v2 pet prerequisites guard DP maintenance retries", DpAsync),
        ("combat flow v2 pet prerequisites guard status burst before every key", StatusAsync),
        ("combat flow v2 pet prerequisites skip absorb opening and continue ordinary attack", OpeningAsync),
        ("combat flow v2 pet prerequisites stop deferred opening retry", OpeningRetryAsync),
        ("combat flow v2 pet prerequisites block ordinary summon while pet lives", QuickbarSummonAsync),
        ("combat flow v2 pet prerequisites block ordinary absorb while pet is missing", QuickbarAbsorbAsync),
        ("combat flow v2 pet prerequisites stop ordinary absorb retries after pet loss", QuickbarRetryAsync),
        ("combat flow v2 pet prerequisites cancel ordinary pet skill at final input boundary", FinalBoundaryAsync),
        ("combat flow v2 pet prerequisites require exact ordinary summon cooldown expiry", QuickbarSummonCooldownAsync),
        ("combat flow v2 pet prerequisites allow ordinary summon when ready and pet absent", QuickbarSummonReadyAsync),
        ("combat flow v2 pet prerequisites recheck summon after optional speed key", SummonPetArrivalAsync),
        ("combat flow v2 pet prerequisites isolate pet state between accounts", IsolationAsync),
        ("combat flow v2 pet opening attack key skips pet lookup", PetAttackKeyAsync),
        ("combat flow v2 pet opening attack key keeps two presses ten milliseconds apart", PetAttackBurstAsync),
    };

    private static async Task<SharedSkillAuditFixture> CreateAsync(bool alive = false, bool manual = false)
    {
        var f = new SharedSkillAuditFixture();
        f.Context.Config.CombatFlowVersion = CombatFlowVersion.CoordinatedV2;
        f.SetPet(11, alive ? 80u : 0u);
        f.Api.Player = f.Api.Player with { CurrentMp = 50 };
        f.Api.Skills = f.Api.Skills.Append(Skill(Absorb, "精灵吸收", 1, 120000) with { XmlFirstTarget = "MyPet" })
            .Append(Skill(Summon, "召唤:火之精灵", 1, 5000) with { XmlEffects = "Summon", XmlSubType = "Summon" }).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots
            .Append(new(SkillQuickbar.Main, 3, 21, Absorb)).Append(new(SkillQuickbar.Main, 4, 21, Summon)).ToArray() };
        f.Settings.Maintenance.MpMaintenanceRules.Add(new() { SkillId = manual ? 0 : Absorb,
            SkillName = manual ? "" : "精灵吸收 I", Key = "D4", BelowPercent = 60 });
        await f.PrepareAsync();
        return f;
    }

    private static Task ClassificationAsync()
    {
        Check(SpiritmasterSkillEligibility.Classify(Skill(8000, "localized", 1) with { XmlFirstTarget = " mypet " }) == SpiritmasterPetRequirement.LivingPet,
            "target metadata covers pet skills independently of names and rank");
        foreach (var name in new[] { "命令:攻击 I", " 命令：攻击 II", "command:attack", "精灵吸收 II" })
            Check(SpiritmasterSkillEligibility.Classify(Skill(8000, name, 1)) == SpiritmasterPetRequirement.LivingPet, "known aliases require a living pet");
        Check(SpiritmasterSkillEligibility.Classify(null, Absorb) == SpiritmasterPetRequirement.LivingPet, "known absorb identity works without metadata");
        Check(SpiritmasterSkillEligibility.Classify(Skill(Summon, "any", 1) with { XmlEffects = "Buff, Summon" }) == SpiritmasterPetRequirement.MissingPet, "exact summon effect is recognized");
        foreach (var effect in new[] { "SummonHoming", "BoostSkillCastingTime", "SummonServant", "SpellATK_Instant" })
            Check(SpiritmasterSkillEligibility.Classify(Skill(8000, "any", 1) with { XmlEffects = effect }) == SpiritmasterPetRequirement.None,
                "energy attacks and casting buffs must not be mistaken for persistent pet summons");
        return Task.CompletedTask;
    }
    private static async Task MissingAbsorbAsync()
    {
        using var f = await CreateAsync();
        f.ConfigureResource("mp"); await f.PrepareAsync();
        f.Keyboard.AfterPress = _ => f.Confirm(LowMpId);
        await f.MaintainAsync();
        Sequence(new[] { "D3" }, f.Keyboard.Keys, "missing pet must not reserve the MP turn or starve a usable fallback");
    }
    private static async Task LivingAbsorbAsync()
    {
        using var f = await CreateAsync(true); f.Keyboard.AfterPress = _ => f.Confirm(Absorb);
        await f.MaintainAsync(); Sequence(new[] { "D4" }, f.Keyboard.Keys, "live pet permits normal absorb confirmation");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.key_pressed"), "the established cooldown evidence still confirms release");
    }
    private static async Task RetryDeathAsync() => await RetryAsync(false);
    private static async Task RetryChangeAsync() => await RetryAsync(true);
    private static async Task RetryAsync(bool changed)
    {
        using var f = await CreateAsync(true);
        f.Keyboard.AfterPress = _ => f.SetPet(changed ? 12u : 11u, changed ? 80u : 0u);
        await f.MaintainAsync(); Sequence(new[] { "D4" }, f.Keyboard.Keys, "pet invalidation ends the finite retry before another key");
        Check(!f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.unconfirmed"), "normal prerequisite loss is not a cast timeout");
    }
    private static async Task<bool> ExecuteAsync(SharedSkillAuditFixture f, string resource = "mp", Func<Task>? before = null)
    {
        var skill = f.Api.Skills.Single(s => s.SkillId == Absorb);
        var method = typeof(SemiAutoCombatController).GetMethod(resource == "dp" ? "ExecuteDpMaintenanceKeyRuleAsync" : "ExecuteMaintenanceKeyRuleAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] args = resource == "dp"
            ? new object?[] { f.Context, f.State, f.Settings.SemiAuto, new DpMaintenanceRuleConfig { Key = "D4", SkillId = Absorb, SkillName = skill.Name },
                resource, f.Api.Player, 4000, before, skill }
            : new object?[] { f.Context, f.State, f.Settings.SemiAuto, f.Settings.Maintenance.MpMaintenanceRules[0], resource,
                50u, 100u, 50.0, 60, f.Api.Player, before, skill, null };
        return await (Task<bool>)method.Invoke(f.Controller, args)!;
    }
    private static async Task PreparationAsync()
    {
        using var f = await CreateAsync(true);
        await ExecuteAsync(f, before: () => { f.SetPet(11, 0); return Task.CompletedTask; });
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "pet disappears between selection and actual input");
    }
    private static async Task ManualAsync()
    {
        using var f = await CreateAsync(manual: true); await f.MaintainAsync();
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "manual D4 resolves to the pet-dependent skill");
        Check(f.Settings.Maintenance.MpMaintenanceRules[0].SkillId == 0, "manual configuration stays manual");
    }
    private static async Task LegacyAsync()
    {
        using var f = await CreateAsync(); f.Context.Config.CombatFlowVersion = CombatFlowVersion.Legacy;
        f.Keyboard.AfterPress = _ => f.Confirm(Absorb); await f.MaintainAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "rollback retains original behavior");
    }
    private static async Task ScopeAsync()
    {
        foreach (var otherClass in new[] { false, true })
        {
            using var f = await CreateAsync();
            if (otherClass) f.Api.Player = f.Api.Player with { CharacterClassId = AionClassId.Sorcerer };
            else f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            f.Keyboard.AfterPress = _ => f.Confirm(Absorb); await f.MaintainAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "only enabled Spiritmaster V2 receives new prerequisites");
        }
    }
    private static async Task DpAsync()
    {
        using var f = await CreateAsync(true); f.Keyboard.AfterPress = _ => f.SetPet(11, 0);
        await ExecuteAsync(f, "dp"); Sequence(new[] { "D4" }, f.Keyboard.Keys, "DP configuration cannot bypass pet checks");
    }
    private static async Task StatusAsync()
    {
        using var f = await CreateAsync(true); f.Keyboard.AfterPress = _ => f.SetPet(11, 0);
        f.Settings.Maintenance.StatusMaintenancePressCount = 3;
        var method = typeof(SemiAutoCombatController).GetMethod("ExecuteStatusMaintenanceKeyRuleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task<bool>)method.Invoke(f.Controller, new object?[] { f.Context, f.State, f.Settings.SemiAuto,
            f.Settings.Maintenance,
            new StatusMaintenanceRuleConfig { SkillId = Absorb, SkillName = "精灵吸收", Key = "D4" }, "status", f.Api.Player,
            f.Api.PlayerAbnormalStatuses, null, f.Api.Skills.Single(s => s.SkillId == Absorb), false })!;
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "status burst stops immediately after pet loss");
    }
    private static async Task OpeningAsync()
    {
        using var f = await CreateAsync(); f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Settings.Skills.OpeningSkill.Enabled = true;
        f.Settings.Skills.OpeningSkill.Skills = new() { new() { SkillId = Absorb, SkillName = "精灵吸收", Key = "D4" } };
        await f.PrepareAsync(); await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D4") && f.Keyboard.Keys.Contains("D1"), "ineligible opener cannot delay normal attacks");
    }
    private static async Task OpeningRetryAsync()
    {
        using var f = await CreateAsync(); f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Settings.Skills.OpeningSkill.Enabled = true;
        f.Settings.Skills.OpeningSkill.Skills = new() { new() { SkillId = Absorb, SkillName = "精灵吸收", Key = "D4" } };
        await f.PrepareAsync();
        var target = await f.Context.Snapshots.ReadLockedTargetAsync();
        f.State.ObserveOpeningSkillTarget(target.Value); f.State.MarkOpeningSkillHandled(target.Value);
        f.State.OpeningSkillRetries.Defer(0, f.Plan.OpeningSkills[0], f.Api.Skills.Single(s => s.SkillId == Absorb), f.Clock);
        f.Clock.Advance(10000);
        var method = typeof(SemiAutoCombatController).GetMethod("TryRetryOpeningSkillAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task<bool>)method.Invoke(f.Controller, new object?[] { f.Context, f.State, f.Settings.SemiAuto, f.Plan, target.Value, f.Api.Skills })!;
        Check(f.State.OpeningSkillRetries.Current is null && !f.Keyboard.Keys.Contains("D4"), "pending opener yields when its pet prerequisite disappears");
    }
    private static async Task ConfigureQuickbarAsync(SharedSkillAuditFixture f, uint first)
    {
        f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Settings.QuickbarSkills.ExecutionTree = new() { new() { SkillId = first, Name = f.Api.Skills.Single(s => s.SkillId == first).Name },
            new() { SkillId = AttackId, Name = "attack" } };
        f.Api.SkillAvailability = f.Api.SkillAvailability with { UnsupportedSkillIds = new[] { first, AttackId },
            BindingSlots = f.Api.Quickbar.Slots.Select(s => new SkillAvailabilityBindingSnapshot(s.Bar, s.Slot, 21, s.SkillId, s.SkillId)).ToArray() };
        await f.PrepareAsync();
    }
    private static async Task QuickbarSummonAsync()
    {
        using var f = await CreateAsync(true); await ConfigureQuickbarAsync(f, Summon); await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D5") && f.Keyboard.Keys.Contains("D1"), "ordinary attack trees cannot summon over a living pet");
    }
    private static async Task QuickbarAbsorbAsync()
    {
        using var f = await CreateAsync(); await ConfigureQuickbarAsync(f, Absorb); await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D4") && f.Keyboard.Keys.Contains("D1"), "ordinary absorb is suppressed even when its own cooldown is ready");
    }
    private static async Task QuickbarRetryAsync()
    {
        using var f = await CreateAsync(true); await ConfigureQuickbarAsync(f, Absorb);
        f.Keyboard.AfterPress = key => { if (key == "D4") f.SetPet(11, 0); };
        await f.TickAsync(); f.Clock.Advance(500); await f.TickAsync();
        Check(f.Keyboard.Keys.Count(k => k == "D4") == 1, "missing pet stops an ordinary unconfirmed action from pressing again");
    }
    private static async Task FinalBoundaryAsync()
    {
        using var f = await CreateAsync(true); await ConfigureQuickbarAsync(f, Absorb);
        var reads = 0; f.Observer.BeforePetRead = _ => { if (++reads >= 4) f.SetPet(11, 0); };
        await f.TickAsync();
        Check(reads >= 4 && !f.Keyboard.Keys.Contains("D4"), "pet loss during candidate preparation is checked again before the key");
    }
    private static async Task QuickbarSummonCooldownAsync()
    {
        using var f = await CreateAsync(); await ConfigureQuickbarAsync(f, Summon);
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Summon ? s with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 5000) } : s).ToArray();
        await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D5") && f.Keyboard.Keys.Contains("D1"), "cooling summon yields to ordinary combat");
    }
    private static async Task QuickbarSummonReadyAsync()
    {
        using var f = await CreateAsync(); await ConfigureQuickbarAsync(f, Summon);
        await f.TickAsync(); Check(f.Keyboard.Keys.Contains("D5"), "ready summon is not suppressed without a pet");
    }
    private static async Task SummonPetArrivalAsync()
    {
        using var f = await CreateAsync(); f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Api.Skills = f.Api.Skills.Append(Skill(1778, "speed", 1)).ToArray();
        f.Settings.Skills.Spiritmaster.SummonSkills = new() { new() { SkillId = 1778, Key = "D6" }, new() { SkillId = Summon, Key = "D5" } };
        await f.PrepareAsync(); f.Keyboard.AfterPress = _ => f.SetPet(11, 80);
        await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State);
        Sequence(new[] { "D6" }, f.Keyboard.Keys, "pet arriving during preparation cancels the remaining summon key");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "cancelled summon does not reserve a cast window");
    }
    private static async Task IsolationAsync()
    {
        using var one = await CreateAsync(); using var two = await CreateAsync(true);
        two.Keyboard.AfterPress = _ => two.Confirm(Absorb);
        await one.MaintainAsync(); await two.MaintainAsync();
        Sequence(Array.Empty<string>(), one.Keyboard.Keys, "account without pet skips");
        Sequence(new[] { "D4" }, two.Keyboard.Keys, "other account remains eligible");
    }
    private static async Task PetAttackKeyAsync()
    {
        using var f = await CreateAsync(); f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Settings.Skills.Spiritmaster.OpeningAttackKey = "D7"; await f.PrepareAsync();
        var petReads = 0;
        f.Observer.BeforePetRead = _ => petReads++;
        await f.TickAsync();
        Sequence(new[] { "D7", "D7" }, f.Keyboard.Keys, "the opening command sends both presses with no living pet");
        Check(petReads == 0, "the opening command does not read the pet before either press");
    }
    private static async Task PetAttackBurstAsync()
    {
        using var f = await CreateAsync(true); f.Settings.Maintenance.MpMaintenanceRules.Clear();
        f.Settings.Skills.Spiritmaster.OpeningAttackKey = "D7"; await f.PrepareAsync();
        f.Keyboard.AfterPress = key => { if (key == "D7") f.SetPet(11, 0); };
        await f.TickAsync();
        Sequence(new[] { "D7", "D7" }, f.Keyboard.Keys, "pet state changes do not interrupt the opening command");
        var burst = f.Logger.Entries.Single(e => e.EventName == "semi_auto.spiritmaster.opening_attack_key.started");
        Check(Convert.ToInt64(burst.Fields["intervalMs"]) == 10, "the opening command waits ten milliseconds between presses");
    }
}
