using static SharedSkillAuditFixture;

internal static class SpiritmasterElementalRankTests
{
    private static readonly uint[] Family = { 1676, 1677, 1678, 1705 };

    public static async Task EveryOfficialRankPreservesPlayerHpSafetyAsync()
    {
        foreach (var id in Family)
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddElementalRule(id);
            f.Api.Player = f.Api.Player with { CurrentHp = 64 };
            await f.PrepareAsync();
            await f.TickAsync();
            Check(f.Keyboard.Keys.IsEmpty, $"elemental rank ID {id} blocks HP-cost input below 65 percent");
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "blocked input never starts confirmation");
            Check(f.Logged("semi_auto.spiritmaster.elemental_replenishment.player_hp_blocked", "skillId", id),
                "safety diagnostics retain the actual configured rank identity");
        }
    }

    public static async Task EveryOfficialRankConfirmsPetHpAndKeepsLocalHoldAsync()
    {
        foreach (var id in Family)
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddElementalRule(id);
            f.Api.Player = f.Api.Player with { CurrentHp = 65 };
            await f.PrepareAsync();
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, $"elemental rank ID {id} releases at the existing 65 percent boundary");
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == id, "pending confirmation stores the same rank");
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count == 1, "pending window holds another elemental release");
            f.SetPet(11, 55);
            f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "newer trusted pet HP increase confirms every rank");
            Check(f.Logged("semi_auto.spiritmaster.elemental_replenishment.pet_hp_confirmed", "skillId", id),
                "pet HP confirmation reports the actual rank");
            Check(f.Keyboard.Keys.Count == 1, "real HP confirmation retains the existing local cooldown hold");
        }
    }

    public static async Task UnknownIdentityDoesNotBecomeElementalByNameAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.ClearAttackTree();
        f.AddElementalRule(900009);
        f.Api.Player = f.Api.Player with { CurrentHp = 64 };
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "unknown ID with the same display name retains the ordinary pet HP rule");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "display name never grants an elemental family identity");
    }

    public static async Task EveryOfficialRankRechecksPlayerHpAfterPetReadAsync()
    {
        foreach (var id in Family)
        foreach (var hpAfterRead in new uint[] { 64, 0 })
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddElementalRule(id);
            f.Api.Player = f.Api.Player with { CurrentHp = 65 };
            await f.PrepareAsync();
            f.Observer.BeforePetHealthRead = read =>
            {
                if (read == 1) f.Api.Player = f.Api.Player with { CurrentHp = hpAfterRead };
            };
            await f.TickAsync();
            Check(f.Api.Player.CurrentHp == hpAfterRead && f.Keyboard.Keys.IsEmpty &&
                f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null,
                $"elemental rank ID {id} rechecks HP/death after the official pet read, HP={hpAfterRead}");
        }
    }
}
