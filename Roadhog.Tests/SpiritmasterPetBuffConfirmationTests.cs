using Roadhog.Core.Common;
using static SharedSkillAuditFixture;

internal static class SpiritmasterPetBuffConfirmationTests
{
    private static void BuffSequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) =>
        Sequence(expected.SelectMany(key => key is "NumPad1" or "NumPad2"
            ? Enumerable.Repeat(key, 3) : new[] { key }), actual, message);

    public static async Task PendingBuffYieldsToMaintenanceAndAttackAsync()
    {
        foreach (var resource in new[] { "hp", "mp" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff();
            await f.PrepareAsync();
            await f.TickAsync();
            f.ConfigureResource(resource);
            var id = resource == "hp" ? LowHpId : LowMpId;
            var key = resource == "hp" ? "D2" : "D3";
            f.Keyboard.AfterPress = pressed => { if (pressed == key) f.Confirm(id); };
            await f.TickAsync();
            if (resource == "mp")
            {
                BuffSequence(new[] { "NumPad1" }, f.Keyboard.Keys, "mana recovery waits for the pet cast window");
                f.Clock.Advance(1500);
                await f.TickAsync();
            }
            f.Api.Player = f.Api.Player with { CurrentHp = 100, CurrentMp = 100 };
            f.SetPet(11, 50, 1662);
            await f.TickAsync();
            BuffSequence(new[] { "NumPad1", key, "D1" }, f.Keyboard.Keys,
                "HP recovery may preempt; MP and attacks resume after confirmation or timeout");
            Check(f.Logged("semi_auto.maintenance.key_pressed", "confirmedSkillId", id),
                "yielded maintenance confirms the actual selected lower rank");
        }
    }

    public static async Task MultipleBuffsKeepIndependentPendingActionsAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        f.AddPetBuff(1787);
        await f.PrepareAsync();
        await f.TickAsync();
        await f.TickAsync();
        BuffSequence(new[] { "NumPad1" }, f.Keyboard.Keys, "second buff cannot interrupt the first cast");
        f.Clock.Advance(1500);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Last() == "D1", "an unconfirmed first buff yields normal attack before the second buff");
        await f.TickAsync();
        BuffSequence(new[] { "NumPad1", "D1", "NumPad2" }, f.Keyboard.Keys, "attack cannot interrupt the second cast");
        f.SetPet(11, 50, 1662, 1787);
        await f.TickAsync();
        Check(!f.State.HasSpiritmasterPetBuffConfirmation &&
            f.Keyboard.Keys.Count(key => key == "NumPad1") == 3 && f.Keyboard.Keys.Count(key => key == "NumPad2") == 3,
            "matching statuses end both independent confirmations without another pet burst");
    }

    public static async Task RetryBatchIsBoundedAndMonotonicAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.ClearAttackTree();
        f.AddPetBuff();
        await f.PrepareAsync();
        await f.TickAsync();
        f.Clock.ShiftUtc(TimeSpan.FromHours(-1));
        f.Clock.Advance(3000);
        await f.TickAsync();
        await f.TickAsync();
        f.Clock.Advance(3000);
        await f.TickAsync();
        await f.TickAsync();
        f.Clock.Advance(2999);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 9, "three unconfirmed bursts remain bounded by the same three-second guard");
        f.Clock.Advance(1);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 12, "retry resumes three seconds after the last input despite UTC rollback");
        Check(f.Api.Skills.Single(skill => skill.SkillId == 1662).CooldownEndTime == 0,
            "retry scheduling never invents game cooldown movement");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.cooldown.calibrated"),
            "unchanged cooldown never confirms a new clock calibration");
    }

    public static async Task RealCooldownAndTrustedBuffStatusConfirmAsync()
    {
        using (var cooling = new SharedSkillAuditFixture())
        {
            cooling.ClearAttackTree();
            cooling.AddPetBuff();
            await cooling.PrepareAsync();
            await cooling.TickAsync();
            cooling.Confirm(1662);
            await cooling.TickAsync();
            cooling.Api.Skills = cooling.Api.Skills.Select(skill => skill.SkillId == 1662
                ? skill with { CooldownEndTime = 0 } : skill).ToArray();
            cooling.Clock.Advance(3000);
            await cooling.TickAsync();
            BuffSequence(new[] { "NumPad1" }, cooling.Keyboard.Keys, "real cooldown confirmation retains the existing cooldown hold");
        }
        using (var status = new SharedSkillAuditFixture())
        {
            status.ClearAttackTree();
            status.AddPetBuff();
            await status.PrepareAsync();
            await status.TickAsync();
            status.SetPet(11, 50, 1662);
            await status.TickAsync();
            status.SetPet(11, 50);
            await status.TickAsync();
            BuffSequence(new[] { "NumPad1", "NumPad1" }, status.Keyboard.Keys,
                "trusted matching buff status confirms the action and a later missing status starts a new action");
        }
    }

    public static async Task ZeroCooldownAndPetLifetimeRemainIndependentAsync()
    {
        using (var zero = new SharedSkillAuditFixture())
        {
            zero.ClearAttackTree();
            zero.AddPetBuff();
            zero.Api.Skills = zero.Api.Skills.Select(skill => skill.SkillId == 1662
                ? skill with { CooldownDuration = 0 } : skill).ToArray();
            await zero.PrepareAsync();
            await zero.TickAsync();
            await zero.TickAsync();
            BuffSequence(new[] { "NumPad1" }, zero.Keyboard.Keys, "zero cooldown still protects the status-only cast");
            zero.Clock.Advance(3000);
            await zero.TickAsync();
            await zero.TickAsync();
            BuffSequence(new[] { "NumPad1", "NumPad1" }, zero.Keyboard.Keys, "missing zero-cooldown status retries after the maintenance guard");
        }
        using (var replacement = new SharedSkillAuditFixture())
        {
            replacement.ClearAttackTree();
            replacement.AddPetBuff();
            await replacement.PrepareAsync();
            await replacement.TickAsync();
            replacement.SetPet(12, 50);
            await replacement.TickAsync();
            BuffSequence(new[] { "NumPad1", "NumPad1" }, replacement.Keyboard.Keys,
                "replacement pet does not inherit the previous pet's input confirmation window");
        }
    }

    public static async Task FailedInputYieldsAndDeadOrStoppedCannotPressAsync()
    {
        using (var failed = new SharedSkillAuditFixture())
        {
            failed.AddPetBuff();
            failed.Keyboard.PressResult = key => key == "NumPad1" ? OperationResult.Fail("input rejected") : OperationResult.Ok();
            await failed.PrepareAsync();
            await failed.TickAsync();
            Sequence(new[] { "NumPad1", "D1" }, failed.Keyboard.Keys,
                "failed buff input is throttled and yields to the main attack in the same tick");
            await failed.TickAsync();
            Check(failed.Keyboard.Keys.Count(key => key == "NumPad1") == 1, "failed delivery also has bounded input retries");
        }
        using (var dead = new SharedSkillAuditFixture())
        {
            dead.ClearAttackTree();
            dead.AddPetBuff();
            await dead.PrepareAsync();
            dead.Api.Player = dead.Api.Player with { CurrentHp = 0 };
            await dead.TickAsync();
            Check(dead.Keyboard.Keys.IsEmpty, "dead player never presses a pet buff");
        }
        using (var stopped = new SharedSkillAuditFixture())
        {
            stopped.AddPetBuff();
            await stopped.PrepareAsync();
            stopped.Cancel();
            try { await stopped.TickAsync(); throw new InvalidOperationException("stopped tick did not cancel"); }
            catch (OperationCanceledException) { }
            Check(stopped.Keyboard.Keys.IsEmpty, "canceled worker never presses a pet buff");
        }
    }

    public static async Task DeathDuringFinalPetReadCannotPressAsync()
    {
        foreach (var change in new[] { "death", "dp" })
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddPetBuff();
            if (change == "dp") f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == 1662
                ? skill with { XmlCostDp = "1000" } : skill).ToArray();
            await f.PrepareAsync();
            f.Observer.BeforePetRead = read =>
            {
                if (read == 2) f.Api.Player = change == "death"
                    ? f.Api.Player with { CurrentHp = 0 } : f.Api.Player with { CurrentDp = 0 };
            };
            await f.TickAsync();
            Check(f.Keyboard.Keys.IsEmpty && (change == "death" ? f.Api.Player.IsDead : f.Api.Player.CurrentDp == 0),
                "life/DP changes while the final pet read yields are observed by the last official player guard: " + change);
        }
    }

    public static async Task CastWindowProtectsBothBuffsAndReleasesOnEvidenceAsync()
    {
        foreach (var id in new uint[] { 1662, 1787 })
        foreach (var evidence in new[] { "cooldown", "status", "timeout" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(id);
            await f.PrepareAsync();
            await f.TickAsync();
            var key = id == 1662 ? "NumPad1" : "NumPad2";
            f.Clock.Advance(648);
            await f.TickAsync();
            BuffSequence(new[] { key }, f.Keyboard.Keys, "reported 648ms attack must not interrupt a one-second pet cast");
            f.Clock.ShiftUtc(TimeSpan.FromHours(1));
            f.Clock.Advance(851);
            await f.TickAsync();
            BuffSequence(new[] { key }, f.Keyboard.Keys, "wall clock changes cannot end the 1500ms confirmation window");
            if (evidence == "cooldown")
            {
                f.Confirm(id);
                await f.TickAsync();
                BuffSequence(new[] { key }, f.Keyboard.Keys, "cooldown alone does not end status confirmation before its deadline");
                f.Clock.Advance(1);
            }
            else if (evidence == "status") f.SetPet(11, 50, id);
            else f.Clock.Advance(1);
            await f.TickAsync();
            BuffSequence(new[] { key, "D1" }, f.Keyboard.Keys, "trusted status or bounded timeout releases attack input: " + evidence);
        }
    }

    public static async Task PendingCastAllowsHpButBlocksOpeningAndHandlesDeathAsync()
    {
        using (var f = new SharedSkillAuditFixture())
        {
            f.AddPetBuff();
            await f.PrepareAsync();
            await f.TickAsync();
            f.Settings.Skills.Spiritmaster.OpeningAttackKey = "F8";
            f.Api.TargetOwnServerObjectId++;
            await f.TickAsync();
            BuffSequence(new[] { "NumPad1" }, f.Keyboard.Keys, "target changes and opening pet commands cannot interrupt the cast");
            f.ConfigureResource("hp");
            f.Keyboard.AfterPress = key => { if (key == "D2") f.Confirm(LowHpId); };
            await f.TickAsync();
            BuffSequence(new[] { "NumPad1", "D2" }, f.Keyboard.Keys, "urgent HP recovery still preempts during the cast");
        }
        using (var f = new SharedSkillAuditFixture())
        {
            f.AddPetBuff();
            await f.PrepareAsync();
            await f.TickAsync();
            f.Observer.BeforePetRead = _ => f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            await f.TickAsync();
            BuffSequence(new[] { "NumPad1" }, f.Keyboard.Keys, "death during confirmation read cannot issue another input");
            Check(!f.State.HasSpiritmasterPetBuffConfirmation, "death clears the cast reservation");
        }
    }
}
