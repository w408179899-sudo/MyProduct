using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class OpeningSkillRetryTests
{
    private const uint First = 901;
    private const uint Second = 902;

    private static async Task<SharedSkillAuditFixture> CreateAsync(bool single = false, bool weave = false)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
        f.Settings.SemiAuto.AttackWeaveEnabled = weave;
        f.Settings.Skills.OpeningSkill = new()
        {
            Enabled = true, ReleaseAll = !single,
            Skills = new()
            {
                new() { SkillId = First, SkillName = "first", Key = "D5" },
                new() { SkillId = Second, SkillName = "second", Key = "D6" }
            }
        };
        f.Api.Skills = f.Api.Skills.Concat(new[] { Skill(First, "first", 1), Skill(Second, "second", 1, 0) }).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots.Concat(new QuickbarSlotSnapshot[]
        {
            new(SkillQuickbar.Main, 4, 21, First), new(SkillQuickbar.Main, 5, 21, Second)
        }).ToArray() };
        await f.PrepareAsync();
        return f;
    }

    private static async Task TimeOutAsync(SharedSkillAuditFixture f, bool single = false)
    {
        await f.TickAsync();
        Sequence(new[] { "D5" }, f.Keyboard.Keys, "first opener starts");
        f.Clock.Advance(2300);
        await f.TickAsync();
        if (!single) await f.TickAsync();
        Sequence(single ? new[] { "D5", "D1" } : new[] { "D5", "D6", "D1" }, f.Keyboard.Keys,
            "timeout yields to later openers and the normal attack");
    }

    private static async Task StartRetryAsync(SharedSkillAuditFixture f)
    {
        f.Clock.Advance(1000);
        // The normal action is still pending: the retry must not replace it.
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, "pending normal action keeps input ownership");
        f.Confirm(AttackId);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, "normal executor first confirms and yields safely");
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 2, "failed opener receives one deferred retry");
    }

    public static async Task SuccessAsync()
    {
        using var f = await CreateAsync();
        await TimeOutAsync(f);
        await StartRetryAsync(f);
        f.Confirm(First);
        await f.TickAsync();
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == First ? s with { CooldownEndTime = 0 } : s).ToArray();
        f.Clock.Advance(16000);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 2, "confirmed retry is never replayed on the same target");
        Check(f.Keyboard.Keys.Count(key => key == "D6") == 1, "completed later opener is not replayed");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.opening_skill.retry_finished" &&
            Equals(e.Fields["reason"], "cooldown_advanced")), "retry confirmation is recorded once");
    }

    public static async Task FailureBoundedAsync()
    {
        using var f = await CreateAsync();
        await TimeOutAsync(f);
        await StartRetryAsync(f);
        var count = f.Keyboard.Keys.Count;
        f.Clock.Advance(2299);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == count, "retry reserves its confirmation window without spamming keys");
        f.Clock.Advance(1);
        await f.TickAsync();
        f.Clock.Advance(30000);
        for (var i = 0; i < 4; i++) await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 2, "failed retry cannot enqueue itself or block forever");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.opening_skill.retry_finished" &&
            Equals(e.Fields["reason"], "confirmation_timeout")), "retry has a bounded failure outcome");
    }

    public static async Task LateConfirmationAsync()
    {
        foreach (var atTimeout in new[] { true, false })
        {
            using var f = await CreateAsync();
            await f.TickAsync();
            f.Clock.Advance(2300);
            if (atTimeout) f.Confirm(First);
            await f.TickAsync();
            await f.TickAsync();
            if (!atTimeout) f.Confirm(First);
            f.Confirm(AttackId);
            f.Clock.Advance(1000);
            await f.TickAsync(); await f.TickAsync();
            Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, "late cooldown confirmation cancels extra input");
            if (atTimeout) Check(!f.Logger.Entries.Any(e => e.EventName == "semi_auto.opening_skill.retry_deferred"),
                "confirmation at the timeout boundary wins over expiry");
        }
    }

    public static async Task SingleModeAsync()
    {
        using var f = await CreateAsync(single: true);
        await TimeOutAsync(f, single: true);
        await StartRetryAsync(f);
        f.Confirm(First);
        await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D6"), "single mode retries only the selected opener, never the other entries");
    }

    public static async Task LifecycleAsync()
    {
        foreach (var boundary in new[] { "target", "death", "disabled", "stop" })
        {
            using var f = await CreateAsync();
            await TimeOutAsync(f);
            f.Confirm(First); f.Confirm(AttackId);
            if (boundary == "target") f.Api.TargetOwnServerObjectId = 2000;
            if (boundary == "death") f.Api.TargetCurrentHp = 0;
            if (boundary == "disabled") { f.Settings.Skills.OpeningSkill.Enabled = false; await f.PrepareAsync(); }
            if (boundary == "stop") f.Cancel();
            f.Clock.Advance(1000);
            try { await f.TickAsync(); Check(boundary != "stop", "stop must cancel the tick"); }
            catch (OperationCanceledException) { Check(boundary == "stop", "only explicit stop may cancel"); }
            Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, boundary + " prevents old-target retry input");
            if (boundary == "stop") continue;
            f.Api.TargetCurrentHp = 1000;
            // Do not start a new target's opener: the boundary tick already skipped its cooling skill.
            if (boundary != "death")
            {
                f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == First ? s with { CooldownEndTime = 0 } : s).ToArray();
                f.Clock.Advance(1000);
                await f.TickAsync();
                Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, boundary + " clears the deferred entry");
            }
        }
    }

    public static async Task ExpiryAndIsolationAsync()
    {
        using var a = await CreateAsync();
        using var b = await CreateAsync();
        await TimeOutAsync(a);
        await b.TickAsync(); b.Confirm(First); await b.TickAsync(); await b.TickAsync();
        a.Clock.Advance(15000); b.Clock.Advance(15000);
        a.Confirm(AttackId); b.Confirm(AttackId);
        await a.TickAsync(); await a.TickAsync(); await b.TickAsync(); await b.TickAsync();
        Check(a.Keyboard.Keys.Count(key => key == "D5") == 1, "expired retry is discarded instead of replayed much later");
        Check(b.Keyboard.Keys.Count(key => key == "D5") == 1, "one account's failure cannot queue another account's retry");
    }

    public static async Task TargetGuardAsync()
    {
        using var f = await CreateAsync();
        await TimeOutAsync(f);
        f.Clock.Advance(1000); f.Confirm(AttackId); await f.TickAsync();
        f.Api.SkillAvailability = f.Api.SkillAvailability with { Page = 1 };
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 1, "changed quickbar page blocks the retry key");
    }

    public static async Task OpeningOnlyAndWeaveAsync()
    {
        foreach (var weave in new[] { false, true })
        {
            using var f = await CreateAsync(weave: weave);
            f.ClearAttackTree(); await f.PrepareAsync();
            await f.TickAsync(); f.Clock.Advance(2300); await f.TickAsync(); await f.TickAsync();
            Sequence(new[] { "D5", "D6" }, f.Keyboard.Keys, "empty normal tree still completes the first opening pass");
            f.Clock.Advance(999); await f.TickAsync();
            Check(f.Keyboard.Keys.Count == 2, "retry waits for its delay");
            f.Clock.Advance(1); await f.TickAsync();
            Sequence(new[] { "D5", "D6", "D5" }, f.Keyboard.Keys, "empty normal tree allows the bounded retry");
            f.Confirm(First); await f.TickAsync();
            Check(!weave || f.State.QuickbarSkills.AttackWeave.ConfirmedCount == 1,
                "retry contributes exactly one real cooldown confirmation to attack weave");
            Check(!f.Keyboard.Keys.Contains("C"), "failed initial opener cannot contribute a false weave confirmation");
        }
    }

    public static async Task MultipleFailuresAsync()
    {
        using var f = await CreateAsync();
        f.ClearAttackTree();
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Second ? s with { CooldownDuration = 10000 } : s).ToArray();
        await f.PrepareAsync();
        await f.TickAsync(); f.Clock.Advance(2300); await f.TickAsync();
        f.Clock.Advance(2300); await f.TickAsync();
        Sequence(new[] { "D5", "D6", "D5" }, f.Keyboard.Keys, "second failed entry does not restart the whole list");
        f.Clock.Advance(2300); await f.TickAsync();
        Sequence(new[] { "D5", "D6", "D5", "D6" }, f.Keyboard.Keys, "each timed-out entry gets one extra attempt");
        f.Clock.Advance(2300); await f.TickAsync(); f.Clock.Advance(20000); await f.TickAsync();
        Check(f.Keyboard.Keys.Count == 4, "multiple failures exhaust their own finite retries");
    }

    public static async Task RetryInputFailureAsync()
    {
        using var f = await CreateAsync();
        await TimeOutAsync(f);
        f.Clock.Advance(1000); f.Confirm(AttackId); await f.TickAsync();
        f.Keyboard.PressResult = key => key == "D5"
            ? Roadhog.Core.Common.OperationResult.Fail("mock rejected retry") : Roadhog.Core.Common.OperationResult.Ok();
        await f.TickAsync(); f.Clock.Advance(3000); await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 2, "failed retry input is not resent indefinitely");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.opening_skill.retry_finished" &&
            Equals(e.Fields["reason"], "input_failed")), "input failure records a terminal retry result");
    }

    public static async Task PlayerDeathAsync()
    {
        using var f = await CreateAsync();
        await TimeOutAsync(f);
        f.ConfigureResource("hp");
        f.Api.Player = f.Api.Player with { CurrentHp = 0 };
        f.Clock.Advance(1000); await f.TickAsync();
        f.Api.Player = f.Api.Player with { CurrentHp = 100 };
        f.Confirm(AttackId);
        await f.TickAsync(); await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == "D5") == 1,
            "player death cancels deferred input even if the same monster stays selected after revival");
    }
}
