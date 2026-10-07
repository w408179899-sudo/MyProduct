using Roadhog.Core.Common;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class SpiritmasterPetBuffMaintenanceTests
{
    public static async Task MissingStatusStartsBoundedBurstAsync()
    {
        foreach (var id in new uint[] { 1662, 1787 })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(id);
            await f.PrepareAsync();
            await f.TickAsync();
            var key = id == 1662 ? "NumPad1" : "NumPad2";
            Sequence(Enumerable.Repeat(key, 3), f.Keyboard.Keys,
                "missing pet status uses a finite three-press maintenance burst");
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count == 3, "confirmation blocks duplicate bursts and attack input");
        }
    }

    public static async Task CooldownAloneDoesNotReleaseStatusConfirmationAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        await f.PrepareAsync();
        await f.TickAsync();
        var before = f.Keyboard.Keys.Count;
        f.Confirm(1662);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == before,
            "cooldown evidence alone must not release the pet status confirmation or allow attack to interrupt it");
        Check(!f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662),
            "cooldown alone is not a confirmed buff status");
    }

    public static async Task MissingStatusRetryWaitsThreeSecondsAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.ClearAttackTree();
        f.AddPetBuff();
        await f.PrepareAsync();
        await f.TickAsync();
        var before = f.Keyboard.Keys.Count;
        f.Clock.Advance(2000);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == before, "unconfirmed status must not start another burst after only two seconds");
    }

    public static async Task ZeroCooldownStillProtectsTheCastAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == 1662
            ? skill with { CooldownDuration = 0 } : skill).ToArray();
        await f.PrepareAsync();
        await f.TickAsync();
        var before = f.Keyboard.Keys.Count;
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == before,
            "zero cooldown detail must not bypass cast protection and repeatedly press a missing pet buff");
    }

    public static async Task ExistingStatusesSkipAndUnrelatedStatusesRemainUntrustedAsync()
    {
        foreach (var id in new uint[] { 1662, 1787 })
        foreach (var configured in new[] { false, true })
        {
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddPetBuff(id);
            f.Settings.Skills.Spiritmaster.PetBuffRules[0].AbnormalStatusId = configured ? 9000u : 0;
            f.SetPet(11, 50, configured ? 9000u : id);
            await f.PrepareAsync();
            await f.TickAsync();
            Check(f.Keyboard.Keys.IsEmpty, "configured or exact skill status already on the same pet needs no maintenance");
        }
        using var unrelated = new SharedSkillAuditFixture();
        unrelated.AddPetBuff();
        unrelated.Settings.Skills.Spiritmaster.PetBuffRules[0].AbnormalStatusId = 0;
        await unrelated.PrepareAsync();
        unrelated.Keyboard.AfterPress = _ => unrelated.SetPet(11, 50, 1787, 8218, 16545);
        await unrelated.TickAsync();
        unrelated.Clock.Advance(1500);
        await unrelated.TickAsync();
        Check(unrelated.Logged("semi_auto.spiritmaster.pet_buff_unconfirmed", "skillId", 1662) &&
            !unrelated.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662),
            "another buff and unrelated newly appearing statuses never confirm this buff");
        Check(!unrelated.State.TryGetSpiritmasterPetBuffAbnormalId(1662, out _), "unrelated statuses are never remembered");
    }

    public static async Task BurstStopsOnMatchingStatusOrCoolingAsync()
    {
        foreach (var id in new uint[] { 1662, 1787 })
        foreach (var evidence in new[] { "status", "cooldown" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(id);
            f.Settings.Skills.Spiritmaster.PetBuffRules[0].AbnormalStatusId = 0;
            await f.PrepareAsync();
            f.Keyboard.AfterPress = _ =>
            {
                if (evidence == "status") f.SetPet(11, 50, id);
                else f.Confirm(id);
            };
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count == 1, "observed status or actual cooling stops remaining burst inputs");
            await f.TickAsync();
            if (evidence == "status")
            {
                Check(f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", id), "matching pet status confirms maintenance");
                Check(f.Keyboard.Keys.Last() == "D1", "attack resumes after matching status confirmation");
            }
            else
            {
                Check(f.Keyboard.Keys.Count == 1, "cooldown alone still protects the pending status cast");
                f.Clock.Advance(1500);
                await f.TickAsync();
                Check(f.Logged("semi_auto.spiritmaster.pet_buff_unconfirmed", "skillId", id), "timeout without status stays unconfirmed despite real cooldown");
                Check(!f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", id), "no fabricated buff confirmation from cooldown");
            }
        }
    }

    public static async Task BurstLifecycleAndDpGuardsAsync()
    {
        foreach (var id in new uint[] { 1662, 1787 })
        foreach (var change in new[] { "death", "pet_replaced", "pet_dead", "pet_gone", "dp" })
        {
            if (change == "dp" && id != 1787) continue;
            using var f = new SharedSkillAuditFixture();
            f.ClearAttackTree();
            f.AddPetBuff(id);
            await f.PrepareAsync();
            f.BeforePetBuffDelay = _ =>
            {
                if (change == "death") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
                else if (change == "pet_replaced") f.SetPet(12, 50);
                else if (change == "pet_dead") f.SetPet(11, 0);
                else if (change == "pet_gone") f.Api.SummonedPetRoster = f.Api.SummonedPetRoster with
                {
                    LocalPlayerPet = f.Api.SummonedPetRoster.LocalPlayerPet with
                    { Pet = f.Api.SummonedPetRoster.LocalPlayerPet.Pet with { IsSummoned = false } }
                };
                else f.Api.Player = f.Api.Player with { CurrentDp = 1999 };
            };
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count == 1, "life, pet identity and DP are rechecked before every burst input: " + change);
            if (change != "dp") Check(!f.State.HasSpiritmasterPetBuffConfirmation, "invalid life/pet clears the old reservation");
            Check(!f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", id), "a partial interrupted burst is never a confirmed status");
        }
    }

    public static async Task PartialFailureAndCancellationKeepInputsBoundedAsync()
    {
        using (var failed = new SharedSkillAuditFixture())
        {
            failed.AddPetBuff();
            await failed.PrepareAsync();
            var inputs = 0;
            failed.Keyboard.PressResult = _ => ++inputs == 2 ? OperationResult.Fail("input rejected") : OperationResult.Ok();
            await failed.TickAsync();
            Check(failed.Keyboard.Keys.Count == 2, "failed second press stops the burst, with no third input");
            await failed.TickAsync();
            Check(failed.Keyboard.Keys.Count == 2 && failed.State.HasSpiritmasterPetBuffConfirmation,
                "accepted first input retains cast protection after a later input fails");
            var pressed = failed.Logger.Entries.Single(entry => entry.EventName == "semi_auto.spiritmaster.pet_buff_key_pressed");
            Check(Convert.ToInt32(pressed.Fields["pressCount"]) == 1, "diagnostics count accepted input only");
        }
        using (var canceled = new SharedSkillAuditFixture())
        {
            canceled.AddPetBuff();
            await canceled.PrepareAsync();
            canceled.BeforePetBuffDelay = _ => canceled.Cancel();
            try { await canceled.TickAsync(); throw new InvalidOperationException("burst did not cancel"); }
            catch (OperationCanceledException) { }
            Check(canceled.Keyboard.Keys.Count == 1, "stop during the burst delay never sends another key");
        }
    }

    public static async Task LastInputAnchorsCastAndRetryWithUtcChangesAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.PetBuffDelays.SequenceEqual(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100) }),
            "three inputs are separated by the existing status maintenance 100ms interval");
        f.Clock.ShiftUtc(TimeSpan.FromDays(1));
        f.Clock.Advance(1499);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 3, "confirmation deadline is measured from the last input, not the first input or UTC");
        f.Clock.ShiftUtc(TimeSpan.FromDays(-2));
        f.Clock.Advance(1);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Last() == "D1", "the exact bounded confirmation deadline releases attack");
        Check(f.Logged("semi_auto.spiritmaster.pet_buff_unconfirmed", "skillId", 1662), "expired status wait records one unconfirmed result");
        f.ClearAttackTree();
        f.Clock.Advance(1499);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "NumPad1") == 3, "retry stays blocked at last-input plus 2999ms");
        f.Clock.Advance(1);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "NumPad1") == 6, "retry starts at exactly last-input plus 3000ms");
        Check(f.Logger.Entries.Count(entry => entry.EventName == "semi_auto.spiritmaster.pet_buff_unconfirmed") == 1,
            "an expired wait logs once rather than once per tick");
    }

    public static async Task MissingSkillDuringBurstAndConfirmationCannotAuthorizeInputsAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        await f.PrepareAsync();
        f.BeforePetBuffDelay = _ => f.Api.Skills = f.Api.Skills.Where(skill => skill.SkillId != 1662).ToArray();
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 1, "missing exact skill detail stops remaining burst inputs");
        f.Clock.Advance(1499);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 1, "missing skill detail cannot end status confirmation early");
        f.Clock.Advance(1);
        await f.TickAsync();
        Check(f.Logged("semi_auto.spiritmaster.pet_buff_unconfirmed", "skillId", 1662), "a missing skill still has a finite status deadline");
    }

    public static async Task LatePetReplacementCannotLearnTheOldBuffAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetBuff();
        await f.PrepareAsync();
        f.Observer.BeforePetRead = read =>
        {
            // Context + final guard + three burst guards precede the learning read.
            if (read == 6) f.SetPet(12, 50, 1662);
        };
        await f.TickAsync();
        Check(!f.State.TryGetSpiritmasterPetBuffAbnormalId(1662, out _) && !f.State.HasSpiritmasterPetBuffConfirmation,
            "a replacement appearing during the post-input status read cannot confirm or train the old pet attempt");
    }

    public static async Task AccountReservationsRemainIndependentAsync()
    {
        using var first = new SharedSkillAuditFixture();
        using var second = new SharedSkillAuditFixture();
        first.AddPetBuff(); second.AddPetBuff();
        await first.PrepareAsync(); await second.PrepareAsync();
        await first.TickAsync(); await second.TickAsync();
        first.SetPet(11, 50, 1662);
        await first.TickAsync(); await second.TickAsync();
        Check(first.Keyboard.Keys.Last() == "D1" && second.Keyboard.Keys.Count == 3,
            "one account's matching status releases only its own cast reservation");
        Check(first.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662) &&
            !second.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662), "account confirmations never leak");
    }

    public static async Task FailedDeliveryBatchBacksOffWithoutInventingCooldownAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.ClearAttackTree();
        f.AddPetBuff();
        await f.PrepareAsync();
        f.Keyboard.PressResult = _ => OperationResult.Fail("input rejected");
        await f.TickAsync();
        f.Clock.Advance(250); await f.TickAsync();
        f.Clock.Advance(250); await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 3 && !f.State.HasSpiritmasterPetBuffConfirmation,
            "three failed deliveries are finite and never reserve a cast that did not start");
        f.Clock.ShiftUtc(TimeSpan.FromDays(1));
        f.Clock.Advance(2999); await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 3, "failed delivery batch has a three-second monotonic backoff");
        f.Keyboard.PressResult = _ => OperationResult.Ok();
        f.Clock.Advance(1); await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 6 && f.State.HasSpiritmasterPetBuffConfirmation,
            "input delivery recovery starts one normal burst at the exact backoff boundary");
        Check(f.Api.Skills.Single(skill => skill.SkillId == 1662).CooldownEndTime == 0 &&
            !f.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662), "failure/backoff never fabricates cooldown or status");
    }

    public static async Task LongWindowAndDeathDuringSkillReadRemainSafeAsync()
    {
        using (var longWindow = new SharedSkillAuditFixture())
        {
            longWindow.ClearAttackTree();
            longWindow.AddPetBuff();
            longWindow.Settings.SemiAuto.ConfirmTimeoutMs = 6000;
            await longWindow.PrepareAsync();
            await longWindow.TickAsync();
            longWindow.Clock.Advance(5999); await longWindow.TickAsync();
            Check(longWindow.Keyboard.Keys.Count == 3, "three-second retry does not truncate a configured longer confirmation window");
            longWindow.SetPet(11, 50, 1662);
            longWindow.Clock.Advance(1); await longWindow.TickAsync();
            Check(longWindow.Logged("semi_auto.spiritmaster.pet_buff_confirmed", "skillId", 1662),
                "matching status at the deadline wins over timeout");
        }
        using (var dead = new SharedSkillAuditFixture())
        {
            dead.AddPetBuff();
            await dead.PrepareAsync();
            // Reads one and two are the context/final pre-burst guard. The third
            // occurs after the exact skill read and before the last player guard.
            dead.Observer.BeforePetRead = read => { if (read == 3) dead.Api.Player = dead.Api.Player with { CurrentHp = 0 }; };
            await dead.TickAsync();
            Check(dead.Keyboard.Keys.IsEmpty, "death in the burst's yielded reads cannot fall through into normal attack input");
        }
    }

    public static async Task TwoUnconfirmedBuffsYieldToManaAndAttackAsync()
    {
        foreach (var resource in new[] { "mp", "attack" })
        {
            using var f = new SharedSkillAuditFixture();
            f.AddPetBuff(); f.AddPetBuff(1787);
            await f.PrepareAsync();
            await f.TickAsync();
            if (resource == "mp")
            {
                f.ConfigureResource("mp");
                f.Keyboard.AfterPress = key => { if (key == "D3") f.Confirm(LowMpId); };
            }
            f.Clock.Advance(1500);
            await f.TickAsync();
            var expected = resource == "mp" ? "D3" : "D1";
            Check(f.Keyboard.Keys.Last() == expected,
                "an unconfirmed buff timeout yields one input opportunity before another pet buff can reclaim the tick: " + resource);
            Check(!f.Keyboard.Keys.Contains("NumPad2"), "the second missing buff cannot starve normal work at the first timeout");
            f.Api.Player = f.Api.Player with { CurrentMp = 100 };
            await f.TickAsync();
            Check(f.Keyboard.Keys.Count(key => key == "NumPad2") == 3, "the second buff still gets its own subsequent maintenance burst");
            f.Clock.Advance(1500);
            await f.TickAsync();
            Check(f.Keyboard.Keys.Last() == "D1", "the second timeout also yields rather than immediately retrying the first buff");
        }
    }
}
