using Roadhog.Core.Common;
using static SharedSkillAuditFixture;

internal static class SpiritmasterPetBuffConfirmationTests
{
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
            f.Api.Player = f.Api.Player with { CurrentHp = 100, CurrentMp = 100 };
            await f.TickAsync();
            Sequence(new[] { "NumPad1", key, "D1" }, f.Keyboard.Keys,
                "unconfirmed pet buff yields to the player's maintenance and normal attack");
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
        await f.TickAsync();
        Sequence(new[] { "NumPad1", "NumPad2", "D1" }, f.Keyboard.Keys,
            "second buff never overwrites the first buff's confirmation reservation");
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
        f.Clock.Advance(3000);
        await f.TickAsync();
        f.Clock.Advance(3000);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 3, "three unconfirmed inputs back off instead of continuously reclaiming ticks");
        f.Clock.Advance(27000);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 4, "a bounded retry batch resumes using elapsed time even after UTC rollback");
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
            Sequence(new[] { "NumPad1" }, cooling.Keyboard.Keys, "real cooldown confirmation retains the existing cooldown hold");
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
            Sequence(new[] { "NumPad1", "NumPad1" }, status.Keyboard.Keys,
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
            Sequence(new[] { "NumPad1", "NumPad1" }, zero.Keyboard.Keys, "zero cooldown preserves the existing status-only rule");
        }
        using (var replacement = new SharedSkillAuditFixture())
        {
            replacement.ClearAttackTree();
            replacement.AddPetBuff();
            await replacement.PrepareAsync();
            await replacement.TickAsync();
            replacement.SetPet(12, 50);
            await replacement.TickAsync();
            Sequence(new[] { "NumPad1", "NumPad1" }, replacement.Keyboard.Keys,
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
}
