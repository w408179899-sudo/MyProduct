using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class LifeDrainMaintenanceTests
{
    public static async Task EveryLifeAbsorptionRankRejectsCorpseBeforeFirstInputAsync()
    {
        foreach (var id in new uint[] { 1755, 1756, 1757, 1758, 1779, 1790, 2229 })
        {
            using var f = await CreateAsync(id);
            // Simulate a kill after the outer combat check, while preparing maintenance.
            await f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player,
                allowSitMaintenance: false, plan: f.Plan,
                beforeMaintenanceKeyPress: () => { f.Api.TargetCurrentHp = 0; return Task.CompletedTask; },
                runTiming: MaintenanceRuleRunTiming.InCombat);
            Sequence(Array.Empty<string>(), f.Keyboard.Keys, $"rank {id} never presses against an already dead target");
            AssertBlocked(f, "target_dead", 0);
        }
    }

    public static async Task OtherDrainEffectsStopOnDeathAsync()
    {
        foreach (var effect in new[] { "SkillATKDrain_Instant", "SpellATKDrain_Instant", "SpellATKDrain" })
        {
            // An unlisted identity with a localized name proves classification uses effects.
            using var f = await CreateAsync(900123, effect);
            f.Keyboard.AfterPress = _ => f.Api.TargetCurrentHp = 0;
            await MaintainAsync(f);
            Sequence(new[] { "D2" }, f.Keyboard.Keys, effect + " stops before another input");
            AssertBlocked(f, "target_dead", 1);
        }
    }

    public static async Task LostChangedOrNonMonsterTargetStopsRetryAsync()
    {
        foreach (var change in new[] { "lost", "server", "entity", "non_monster" })
        {
            using var f = await CreateAsync(1790);
            f.Keyboard.AfterPress = _ =>
            {
                switch (change)
                {
                    case "lost": f.Api.TargetEntityId = 0; break;
                    case "server": f.Api.TargetOwnServerObjectId++; break;
                    case "entity": f.Api.TargetEntityId++; break;
                    case "non_monster": f.Api.TargetObjectType = LockedTargetSnapshot.PlayerObjectType; break;
                }
            };
            await MaintainAsync(f);
            Sequence(new[] { "D2" }, f.Keyboard.Keys, change + " stops the original drain attempt");
            AssertBlocked(f, change == "lost" ? "target_lost" : change == "non_monster" ? "target_not_monster" : "target_changed", 1);
            Check(f.State.ShouldPressMaintenanceKey("D2", DateTimeOffset.Now, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(600)),
                "an unconfirmed abandoned cast must not delay maintenance against the next target");
            Check(f.Api.LastLockedTargetContext?.BypassMemoryCache != true, "guard consumes the official target channel");
        }
    }

    public static async Task LivingTargetAndSuccessfulKillingCastStillConfirmAsync()
    {
        foreach (var killingCast in new[] { false, true })
        {
            using var f = await CreateAsync(1790);
            var presses = 0;
            f.Keyboard.AfterPress = _ =>
            {
                if (++presses == 2)
                {
                    if (killingCast) f.Api.TargetCurrentHp = 0;
                    f.Confirm(1790);
                }
            };
            await MaintainAsync(f);
            Sequence(new[] { "D2", "D2" }, f.Keyboard.Keys, "living target can retry and confirm");
            Check(f.Logged("semi_auto.maintenance.key_pressed", "confirmedSkillId", 1790),
                "observed cooldown confirms a successful cast even if that cast kills its target");
            Check(!f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.target_guard_blocked"),
                "successful cast is not misclassified as an abandoned cast");
        }
    }

    public static async Task AlwaysDrainWithoutTargetDoesNotReserveIdleTickAsync()
    {
        using var f = await CreateAsync(1790, timing: MaintenanceRuleRunTiming.Always);
        f.Api.TargetEntityId = 0;
        Check(!await f.MaintainAsync(), "idle Always drain yields so the caller can acquire a target");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "idle drain sends no input");
        AssertBlocked(f, "target_lost", 0);
    }

    public static async Task OrdinaryHealingAndManaMaintenanceKeepTheirBehaviorAsync()
    {
        foreach (var resource in new[] { "hp", "mp" })
        {
            using var f = new SharedSkillAuditFixture();
            f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            f.ConfigureResource(resource);
            f.Api.TargetEntityId = 0;
            await f.PrepareAsync();
            var id = resource == "hp" ? LowHpId : LowMpId;
            var key = resource == "hp" ? "D2" : "D3";
            f.Keyboard.AfterPress = _ => f.Confirm(id);
            Check(await f.MaintainAsync(), resource + " self maintenance still works without a monster");
            Sequence(new[] { key }, f.Keyboard.Keys, resource + " confirms normally");
            Check(f.Logged("semi_auto.maintenance.key_pressed", "confirmedSkillId", id), "ordinary maintenance still confirms");
        }
    }

    private static async Task<SharedSkillAuditFixture> CreateAsync(uint id, string? effect = null,
        MaintenanceRuleRunTiming timing = MaintenanceRuleRunTiming.InCombat)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
        f.Api.Player = f.Api.Player with { CurrentHp = 50 };
        f.Api.Skills = f.Api.Skills.Append(Skill(id, "drain", 1) with
        {
            XmlFirstTarget = effect is null ? null : "target",
            XmlTargetRelationRestriction = effect is null ? null : "enemy",
            XmlEffects = effect is null ? null : "OtherEffect, " + effect
        }).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with
        {
            Slots = f.Api.Quickbar.Slots.Select(slot => slot.SkillId == LowHpId ? slot with { SkillId = id } : slot).ToArray()
        };
        f.Settings.Maintenance.HpMaintenanceRules.Add(new()
            { SkillId = id, SkillName = "drain I", Key = "D2", BelowPercent = 80, RunTiming = timing });
        await f.PrepareAsync();
        return f;
    }

    private static Task<bool> MaintainAsync(SharedSkillAuditFixture f) =>
        f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player,
            allowSitMaintenance: false, plan: f.Plan, runTiming: MaintenanceRuleRunTiming.InCombat);

    private static void AssertBlocked(SharedSkillAuditFixture f, string reason, int attempts)
    {
        var entry = f.Logger.Entries.Single(e => e.EventName == "semi_auto.maintenance.target_guard_blocked");
        Check(Equals(entry.Fields["reason"], reason), "guard reports the observed reason");
        Check(Convert.ToInt32(entry.Fields["attempts"]) == attempts, "guard reports exact input count");
        Check(Convert.ToInt64(entry.Fields["confirmElapsedMs"]) < 3000, "target invalidation exits well before the six-second timeout");
        Check(!f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.unconfirmed"), "invalid target never waits for confirmation timeout");
    }
}
