using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class LowRankMaintenanceTests
{
    public static async Task HpMpAndDpUseExactRankThroughoutConfirmationAsync()
    {
        foreach (var resource in new[] { "hp", "mp", "dp" })
        {
            using var f = new SharedSkillAuditFixture();
            f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            if (resource == "dp") f.Settings.Maintenance.DpMaintenanceRules.Add(new()
                { SkillId = LowHpId, SkillName = "heal I", Key = "D2", RequiredDp = 2000 });
            else f.ConfigureResource(resource);
            await f.PrepareAsync();
            var id = resource == "mp" ? LowMpId : LowHpId;
            var key = resource == "mp" ? "D3" : "D2";
            f.Keyboard.AfterPress = pressed => { if (pressed == key) f.Confirm(id); };
            Check(await f.MaintainAsync(), resource + " maintenance executes the selected lower rank");
            Sequence(new[] { key }, f.Keyboard.Keys, resource + " action confirms once from its actual cooldown");
            Check(f.Logged(resource == "dp" ? "semi_auto.maintenance.dp_key_pressed" : "semi_auto.maintenance.key_pressed",
                "confirmedSkillId", id), resource + " confirmation retains the selected exact rank");
            Check(f.Observer.Requests.Count >= 3 && f.Observer.Requests.All(ids => ids is not null && ids.SequenceEqual(new[] { id })),
                resource + " selection, baseline and confirmation all use the same exact official skill channel");
            await f.MaintainAsync();
            Check(f.Keyboard.Keys.Count == 1, resource + " observed lower-rank cooldown prevents another input");
        }
    }

    public static async Task StatusUsesExactRankAndTrustedAbnormalConfirmationAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
        f.Settings.Maintenance.StatusMaintenanceRules.Add(new()
            { SkillId = LowHpId, SkillName = "heal I", Key = "D2", AbnormalStatusId = 7777 });
        await f.PrepareAsync();
        f.Keyboard.AfterPress = key =>
        {
            if (key == "D2") f.Api.PlayerAbnormalStatuses = new(1, DateTimeOffset.Now, 0, new[] { Buff(7777) });
        };
        Check(await f.MaintainAsync(), "lower-rank status maintenance executes");
        Sequence(new[] { "D2", "D2", "D2" }, f.Keyboard.Keys, "existing status burst count is unchanged");
        Check(f.Logged("semi_auto.maintenance.status_key_pressed", "skillId", LowHpId),
            "trusted abnormal status confirms the actual lower-rank skill");
        Check(f.Observer.Requests.All(ids => ids is not null && ids.SequenceEqual(new[] { LowHpId })),
            "status selection never switches to the highest editor rank");
        await f.MaintainAsync();
        Check(f.Keyboard.Keys.Count == 3, "existing trusted status prevents another maintenance burst");
    }

    public static async Task NameOnlyFullReadIncludesPlacedRankAndExplicitMissingNeverPromotesAsync()
    {
        using (var named = new SharedSkillAuditFixture())
        {
            named.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            await named.PrepareAsync();
            // A compatibility rule created after startup still has no identity.
            named.Settings.Maintenance.HpMaintenanceRules.Add(new() { SkillName = "heal I", Key = "D2", BelowPercent = 80 });
            named.Api.Player = named.Api.Player with { CurrentHp = 50 };
            named.Keyboard.AfterPress = key => { if (key == "D2") named.Confirm(LowHpId); };
            Check(await named.MaintainAsync(), "name-only full list is supplemented with actual bar ranks");
            Check(named.Observer.Requests.Any(ids => ids is null), "name-only compatibility retains the official full read");
            Check(named.Logged("semi_auto.maintenance.key_pressed", "confirmedSkillId", LowHpId),
                "name-only lower-rank baseline and confirmation resolve to the actual rank");
            Check(named.Observer.Requests.TakeLast(2).All(ids => ids is not null && ids.SequenceEqual(new[] { LowHpId })),
                "resolved name-only action polls its actual exact identity");
        }
        using (var missing = new SharedSkillAuditFixture())
        {
            missing.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            await missing.PrepareAsync();
            missing.Settings.Maintenance.HpMaintenanceRules.Add(new() { SkillId = 999999, SkillName = "heal", Key = "D2", BelowPercent = 80 });
            missing.Api.Player = missing.Api.Player with { CurrentHp = 50 };
            Check(!await missing.MaintainAsync(), "missing explicit ID cannot fall back to another learned rank");
            Check(missing.Keyboard.Keys.IsEmpty, "missing exact identity never sends a misleading key");
        }
    }
}
