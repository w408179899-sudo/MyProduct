using Roadhog.Core.Common;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class SpiritmasterSummonPostureTests
{
    public static async Task PostureAsync(bool combatTick, bool resting, bool recoveryLock = true)
    {
        using var f = CreateFixture(resting);
        if (!recoveryLock) f.State.ClearMaintenanceRest();
        await f.PrepareAsync();
        await SummonAsync(f, combatTick);

        var expected = resting
            ? new[] { "X", "NumPad6", "NumPad8" }
            : new[] { "NumPad6", "NumPad8" };
        Sequence(expected, f.Keyboard.Keys, "stand only when seated, before both summon keys");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "summon keys still require pet confirmation");
        Check(f.State.IsMaintenanceResting == (resting && recoveryLock), "temporary standing preserves the recovery lock");

        await SummonAsync(f, combatTick);
        Sequence(expected, f.Keyboard.Keys, "pending summon must not repeat posture or summon keys");
        f.SetPet(12, 100);
        Check(!await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State),
            "a living replacement pet releases summon blocking");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "pet appearance confirms the summon");

        if (resting && recoveryLock)
        {
            f.Settings.Maintenance.SitMaintenanceEnabled = true;
            f.Settings.Maintenance.SitHpRecoverToPercent = 80;
            f.Settings.Maintenance.SitMpRecoverToPercent = 80;
            f.Keyboard.Keys.Clear();
            Check(await f.Controller.TryHandleMaintenanceAsync(f.Context, f.State, f.Api.Player),
                "unfinished recovery resumes after the summon");
            Sequence(new[] { "OemComma" }, f.Keyboard.Keys, "sit again while HP and MP remain below recovery targets");
            Check(f.State.IsMaintenanceResting && f.State.MaintenanceRestingForHp && f.State.MaintenanceRestingForMp,
                "both original recovery goals remain active");
        }
    }

    public static async Task RestExitFailureAsync(bool combatTick)
    {
        using var f = CreateFixture(resting: true);
        f.Keyboard.PressResult = key => key == "X" ? OperationResult.Fail("rest exit failed") : OperationResult.Ok();
        await f.PrepareAsync();
        await SummonAsync(f, combatTick);
        Sequence(new[] { "X" }, f.Keyboard.Keys, "failed standing blocks summon and ordinary attack keys");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "failed standing must not start summon verification");
        Check(f.State.IsMaintenanceResting, "failed standing retains recovery state");

        f.Keyboard.PressResult = null;
        f.Keyboard.Keys.Clear();
        await SummonAsync(f, combatTick);
        Sequence(new[] { "X", "NumPad6", "NumPad8" }, f.Keyboard.Keys,
            "standing failure must not consume the summon retry interval");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "successful retry starts summon verification");
    }

    public static async Task UnneededSummonAsync()
    {
        using var f = CreateFixture(resting: true);
        f.SetPet(11, 50);
        await f.PrepareAsync();
        Check(!await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State),
            "a living pet requires no summon");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "do not stand merely because a living pet is checked");

        f.Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(10, DateTimeOffset.Now);
        f.Settings.Skills.Spiritmaster.SummonSkills.Clear();
        await f.PrepareAsync();
        Check(!await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State),
            "missing summon configuration must not change posture");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "missing summon keys must not stand");
        Check(f.State.IsMaintenanceResting, "skipped summon retains recovery state");
    }

    public static async Task WithoutSpeedKeyAsync()
    {
        using var f = CreateFixture(resting: true);
        f.Settings.Skills.Spiritmaster.SummonSkills[0].Key = string.Empty;
        await f.PrepareAsync();
        await SummonAsync(f, combatTick: false);
        Sequence(new[] { "X", "NumPad8" }, f.Keyboard.Keys, "stand before pet summon even without a speed key");
    }

    public static async Task RestExitCancellationAsync()
    {
        using var f = CreateFixture(resting: true);
        f.Keyboard.AfterPress = key => { if (key == "X") f.Cancel(); };
        await f.PrepareAsync();
        var canceled = false;
        try { await SummonAsync(f, combatTick: false); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "stop during the standing delay must cancel the summon flow");
        Sequence(new[] { "X" }, f.Keyboard.Keys, "stop must prevent both summon keys");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "canceled standing must not begin summon verification");
    }

    private static SharedSkillAuditFixture CreateFixture(bool resting)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.Spiritmaster.SummonSkills = new() { new() { Key = "NumPad6" }, new() { Key = "NumPad8" } };
        f.Api.Player = f.Api.Player with
        {
            CurrentHp = 50, MaxHp = 100, CurrentMp = 20, MaxMp = 100,
            StanceFlags = resting ? 5u : 1u, MotionMode = resting ? 1u : 0u
        };
        f.SetPet(11, 0);
        Check(!f.Api.SummonedPetRoster.LocalPlayerPet.Pet.IsAlive, "fixture starts with a dead pet");
        if (resting) f.State.StartMaintenanceRest(forHp: true, forMp: true);
        f.Keyboard.AfterPress = key =>
        {
            if (key == "X") f.Api.Player = f.Api.Player with { StanceFlags = 1, MotionMode = 0 };
        };
        return f;
    }

    private static async Task SummonAsync(SharedSkillAuditFixture f, bool combatTick)
    {
        if (combatTick) await f.TickAsync();
        else Check(await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State),
            "missing pet blocks normal work during summon preparation and verification");
    }
}
