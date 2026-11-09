using Roadhog.Core.Common;
using static SharedSkillAuditFixture;

internal static class SpiritmasterElementalConfirmationTests
{
    private const uint ElementalId = 1678;

    public static async Task WaitingReservesAttackAndMaintenanceAsync()
    {
        foreach (var followup in new[] { "attack", "hp", "mp", "pet_buff" })
        {
            using var f = Create();
            if (followup == "hp")
            {
                f.ConfigureResource("hp");
                f.Api.Player = f.Api.Player with { CurrentHp = 70 };
            }
            if (followup == "mp") f.ConfigureResource("mp");
            if (followup == "pet_buff") f.AddPetBuff();
            f.Keyboard.AfterPress = key =>
            {
                if (key == "D2") f.Confirm(LowHpId);
                if (key == "D3") f.Confirm(LowMpId);
            };
            await StartAsync(f);
            f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys,
                "pending elemental confirmation reserves the tick before retry is due: " + followup);
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null, "waiting retains the same confirmation");
        }
    }

    public static async Task StaleSnapshotAndCooldownKeepReservationAsync()
    {
        foreach (var reason in new[] { "snapshot_unchanged", "skill_cooling" })
        {
            using var f = Create();
            await StartAsync(f);
            MakeCheckDue(f);
            if (reason == "skill_cooling")
            {
                f.SetPet(11, 50);
                f.Confirm(ElementalId);
            }
            await f.TickAsync();
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "waiting cannot release normal attack: " + reason);
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null,
                "neither an unchanged snapshot nor cooldown is HP confirmation");
        }
    }

    public static async Task TargetChangeCannotRunOpeningDuringConfirmationAsync()
    {
        using var f = Create();
        await StartAsync(f);
        f.Settings.Skills.Spiritmaster.OpeningAttackKey = "F8";
        f.Settings.Skills.Spiritmaster.OpeningAttackDelayMs = 0;
        f.Api.TargetOwnServerObjectId++;
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
        await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "a new monster cannot send opening input through the reservation");
    }

    public static async Task LowPlayerHpReleasesBeforeRetryAndWithStalePetAsync()
    {
        foreach (var checkDue in new[] { false, true })
        {
            using var f = Create();
            f.ConfigureResource("hp");
            f.Api.Player = f.Api.Player with { CurrentHp = 100 };
            f.Keyboard.AfterPress = key => { if (key == "D2") f.Confirm(LowHpId); };
            await StartAsync(f);
            if (checkDue) MakeCheckDue(f);
            else f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
            f.Api.Player = f.Api.Player with { CurrentHp = 64 };
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null,
                "player HP below 65 cancels even before retry or while pet snapshot is unchanged");
            Sequence(new[] { "D4", "D2" }, f.Keyboard.Keys, "HP recovery resumes without another elemental press");
        }
    }

    public static async Task RetryRechecksHpAfterPetReadAndAllowsExactFloorAsync()
    {
        foreach (var hp in new uint[] { 64, 65 })
        {
            using var f = Create();
            await StartAsync(f);
            MakeCheckDue(f);
            f.Observer.BeforePetHealthRead = _ =>
            {
                f.SetPet(11, 50);
                f.Api.Player = f.Api.Player with { CurrentHp = hp };
            };
            await f.TickAsync();
            if (hp == 64)
            {
                Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "late HP drop ends the reservation");
                Check(f.Keyboard.Keys.Count(key => key == "D4") == 1, "late HP drop prevents retry");
                Check(f.Keyboard.Keys.Last() == "D1", "normal work resumes after HP safety cancels");
            }
            else
            {
                Sequence(new[] { "D4", "D4" }, f.Keyboard.Keys, "exactly 65 percent permits retry without attack input");
                Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.AttemptCount == 2, "retry remains tracked");
            }
        }
    }

    public static async Task HpIncreaseReleasesAttackWithRealCooldownAsync()
    {
        using var f = Create();
        await StartAsync(f);
        MakeCheckDue(f);
        f.SetPet(11, 55);
        f.Confirm(ElementalId);
        await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "newer HP increase ends confirmation");
        Check(f.Logged("semi_auto.spiritmaster.pet_hp.pet_hp_confirmed", "skillId", ElementalId),
            "release retains real HP evidence");
        Sequence(new[] { "D4", "D1" }, f.Keyboard.Keys, "attack resumes while pet is still below the configured threshold");
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D4") == 1, "real cooldown prevents another heal after confirmation");
    }

    public static async Task ExpiryYieldsToNormalWorkAsync()
    {
        using var f = Create();
        await StartAsync(f);
        ExpireConfirmation(f);
        await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "expired attempt cannot restart in the same tick");
        Sequence(new[] { "D4", "D1" }, f.Keyboard.Keys, "expiry yields an actual opportunity to resume normal work");
        Check(f.Logger.Entries.Count(entry => entry.EventName ==
            "semi_auto.spiritmaster.pet_hp.confirmation_expired") == 1, "expiry is logged once");
    }

    public static async Task LifecycleChangesReleaseOrStopConfirmationAsync()
    {
        foreach (var change in new[] { "pet_replaced", "pet_gone", "pet_dead", "player_dead", "player_dead_at_expiry", "skill_missing", "target_dead" })
        {
            using var f = Create();
            await StartAsync(f);
            MakeCheckDue(f);
            if (change == "pet_replaced") f.SetPet(12, 50);
            if (change == "pet_dead") f.SetPet(11, 0);
            if (change == "pet_gone")
            {
                f.Api.SummonedPet = f.Api.SummonedPet with { IsSummoned = false };
                f.Api.SummonedPetRoster = f.Api.SummonedPetRoster with
                { LocalPlayerPet = f.Api.SummonedPetRoster.LocalPlayerPet with { Pet = f.Api.SummonedPet } };
            }
            if (change is "player_dead" or "player_dead_at_expiry") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            if (change == "player_dead_at_expiry") ExpireConfirmation(f);
            if (change == "skill_missing") f.Api.Skills = f.Api.Skills.Where(skill => skill.SkillId != ElementalId).ToArray();
            if (change == "target_dead") f.Api.TargetCurrentHp = 0;
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "lifecycle clears the old reservation: " + change);
            Check(f.Keyboard.Keys.Count(key => key == "D4") == 1, "lifecycle never retries the old elemental attempt: " + change);
            if (change is "player_dead" or "player_dead_at_expiry" or "target_dead")
                Sequence(new[] { "D4" }, f.Keyboard.Keys, "death cannot fall through to attack input");
            else
                Check(f.Keyboard.Keys.Last() == "D1", "normal work is released after cancellation: " + change);
        }
    }

    public static async Task StopAndFailedRetryDoNotReleaseInputsAsync()
    {
        using (var f = Create())
        {
            await StartAsync(f);
            MakeCheckDue(f);
            f.SetPet(11, 50);
            f.Keyboard.PressResult = _ => OperationResult.Fail("input rejected");
            await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null, "failed retry keeps finite confirmation");
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.AttemptCount == 1, "failed delivery is not a successful retry");
            f.Keyboard.PressResult = null;
            f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
            await f.TickAsync();
            Sequence(new[] { "D4", "D4" }, f.Keyboard.Keys, "failed retry and wait do not release attack");
        }
        foreach (var cancelDuringRead in new[] { false, true })
        {
            using var f = Create();
            await StartAsync(f);
            if (cancelDuringRead) f.Observer.BeforePetHealthRead = _ => f.Cancel();
            else f.Cancel();
            try { await f.TickAsync(); throw new InvalidOperationException("pending confirmation ignored stop"); }
            catch (OperationCanceledException) { }
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "stop before or during confirmation never sends further input");
        }
    }

    public static async Task AccountReservationsRemainIndependentAsync()
    {
        using var first = Create();
        using var second = Create();
        await StartAsync(first);
        await StartAsync(second);
        first.SetPet(11, 55);
        MakeCheckDue(first);
        second.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
        await first.TickAsync();
        await second.TickAsync();
        Sequence(new[] { "D4", "D1" }, first.Keyboard.Keys, "confirmed account resumes attack");
        Sequence(new[] { "D4" }, second.Keyboard.Keys, "neighboring account retains its own reservation");
    }

    private static SharedSkillAuditFixture Create()
    {
        var f = new SharedSkillAuditFixture();
        f.AddElementalRule(ElementalId);
        return f;
    }

    private static async Task StartAsync(SharedSkillAuditFixture f)
    {
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "public combat tick starts elemental confirmation before normal work");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null, "accepted initial input creates confirmation");
    }

    private static void MakeCheckDue(SharedSkillAuditFixture f) =>
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);

    private static void ExpireConfirmation(SharedSkillAuditFixture f)
    {
        var pending = f.State.PendingSpiritmasterPetHpIncreaseConfirmation!;
        var pressedAt = DateTimeOffset.Now.AddMinutes(-1);
        f.State.BeginSpiritmasterPetHpIncreaseConfirmation(pending.SkillId, pending.Key, pending.PetServerObjectId,
            pending.BaselineCurrentHp, pending.BaselineCapturedAt, pressedAt,
            TimeSpan.Zero, TimeSpan.FromSeconds(30), pending.RuleIndex);
    }
}
