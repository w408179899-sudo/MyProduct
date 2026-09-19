using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Vmm;

internal static class AttackWeaveTests
{
    public static Task ConfirmationAccountingAsync()
    {
        var state = new AttackWeaveState();
        var now = DateTimeOffset.UtcNow;
        var a = Skill(1) with { CooldownEndTime = 100 };
        var b = Skill(2) with { CooldownDuration = 0 };
        state.TrackPress(a, now.AddSeconds(1));
        state.TrackPress(a, now.AddSeconds(2));
        state.TrackPress(b, now.AddSeconds(1));
        Check(!state.CanPress(3), "two unconfirmed attempts reserve the pair; no third skill");
        state.Observe(new[] { a with { CooldownEndTime = 0 }, b }, now, 600);
        state.Observe(new[] { a with { CooldownEndTime = 99 }, b }, now, 600);
        state.Observe(Array.Empty<SkillSnapshot>(), now, 600);
        Equal(0, state.ConfirmedCount, "missing, zero, backward and unchanged snapshots do not confirm");
        var advanced = a with { CooldownEndTime = 200 };
        state.Observe(new[] { advanced, b }, now, 600);
        state.Observe(new[] { advanced, b }, now, 600);
        Equal(1, state.ConfirmedCount, "retries and repeated confirmation snapshots count once");
        Check(!state.AttackDueAt.HasValue, "one confirmed cast does not schedule C");
        state.Observe(new[] { advanced, b with { CooldownEndTime = 300 } }, now, 600);
        Equal(2, state.ConfirmedCount, "zero-duration skills require actual cooldown-end advancement too");
        Check(!state.ShouldPressAttack(now.AddMilliseconds(599)), "C is not due before 600 ms");
        Check(state.ShouldPressAttack(now.AddMilliseconds(600)), "C is due at 600 ms");
        state.FinishPair(now.AddMilliseconds(600));
        state.Observe(new[] { advanced, b with { CooldownEndTime = 300 } }, now.AddMilliseconds(601), 600);
        Equal(0, state.ConfirmedCount, "a completed pair cannot recount old confirmations");
        state.TrackPress(advanced, now.AddMilliseconds(700));
        state.Observe(new[] { advanced with { CooldownEndTime = 400 } }, now.AddMilliseconds(701), 600);
        Equal(0, state.ConfirmedCount, "expired attempts do not accept a late cooldown change");
        Check(state.CanPress(3), "expired attempts release their reservation");
        return Task.CompletedTask;
    }

    public static async Task SixStageChainAsync()
    {
        using var f = new Fixture(Chain(1, 6));
        for (uint stage = 1; stage <= 6; stage++)
        {
            await f.Tick();
            Equal("Skill " + stage, f.LastSkill(), "chain must keep its exact next stage after C");
            f.Confirm(stage);
            if (stage % 2 == 0)
            {
                await f.Tick();
                Equal(2, f.State.AttackWeave.ConfirmedCount, "two confirmed chain stages schedule C");
                var beforeWait = f.Keyboard.Keys.Count;
                var oldDeadline = f.State.PendingChainExpiresAt;
                f.Clock.Advance(599);
                await f.Tick();
                Equal(beforeWait, f.Keyboard.Keys.Count, "no chain retry, next stage or C during wait");
                f.Clock.Advance(1);
                await f.Tick();
                Equal("C", f.Keyboard.Keys.Last(), "C after the configured delay");
                Equal(oldDeadline.AddMilliseconds(600), f.State.PendingChainExpiresAt,
                    "only the intentional pause is added to the existing chain deadline");
            }
        }

        Sequence(new[] { "D1", "D1", "C", "D1", "D1", "C", "D1", "D1", "C" }, f.Keyboard.Keys,
            "six chain stages weave every two despite sharing one key");
        Equal(6, f.Logger.Entries.Count(entry => entry.EventName == "semi_auto.attack_weave.skill_confirmed"),
            "all six actual releases counted exactly once");
    }

    public static async Task MixedRootsAndChainAsync()
    {
        using var f = new Fixture(Node(1), Chain(2, 3));
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        Equal("Skill 2", f.LastSkill(), "second root follows first success");
        f.Confirm(2);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "root and child share counter across first C");
        f.Confirm(3);
        await f.Tick();
        f.Confirm(4);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        Sequence(new[] { "D1", "D2", "C", "D2", "D2", "C" }, f.Keyboard.Keys, "mixed skills share the pair");
    }

    public static async Task IdleGapUnblocksChainRootAsync()
    {
        using var f = new Fixture(Node(1), Chain(2, 3));
        f.Settings.SemiAuto.ConfirmTimeoutMs = 500;
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        Equal("Skill 2", f.LastSkill(), "one success followed by an unconfirmed chain root reproduces the stall");
        var pressed = f.Keyboard.Keys.Count;
        f.Clock.Advance(1500);
        await f.Tick();
        Equal(pressed, f.Keyboard.Keys.Count, "exactly 1500 ms does not reset before the agreed boundary");
        f.Clock.Advance(1);
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "timeout releases the slot without requiring a next key or target change");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "timeout starts a fresh pair");
        Check(!f.Keyboard.Keys.Contains("C"), "timeout itself must not press C");
        Equal(1, f.Logger.Entries.Count(entry => entry.EventName == "semi_auto.attack_weave.idle_reset"),
            "stalled pair resets exactly once");
        f.Confirm(2);
        f.Confirm(3);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "late discarded root is not added to the new pair");
        Equal("Skill 4", f.LastSkill(), "existing chain order survives the counter reset");
        f.Confirm(4);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        Equal("C", f.Keyboard.Keys.Last(), "two fresh successful skills weave normally");
    }

    public static async Task IdleGapClearsBothUnconfirmedAttemptsAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        await f.Tick();
        await f.Tick();
        Check(!f.State.AttackWeave.CanPress(99), "two pending skills reserve both slots");
        f.Clock.Advance(1501);
        await f.Tick();
        Check(f.State.AttackWeave.CanPress(99), "both stale slots are cleared before tracking the new retry");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "unconfirmed input still does not count");
        f.Confirm(1);
        f.Confirm(2);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "only the retry in the new pair can confirm");
    }

    public static async Task IdleGapClearsCountWithoutPendingAttemptAsync()
    {
        using var f = new Fixture(Node(1), Node(2), Node(3));
        await f.Tick();
        f.Keyboard.PressResult = key => key == "D2" ? OperationResult.Fail("input failure") : OperationResult.Ok();
        f.Confirm(1);
        await f.Tick();
        Check(!f.State.AttackWeave.HasAttempts, "failed next key leaves only one confirmed success");
        f.Clock.Advance(1500);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "failed transport does not refresh the last skill key time");
        f.Clock.Advance(1);
        f.Keyboard.PressResult = null;
        await f.Tick();
        Equal(0, f.State.AttackWeave.ConfirmedCount, "one success without a pending slot also expires");
        f.Confirm(2);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "new success must not pair with the expired one");
        Check(!f.Keyboard.Keys.Contains("C"), "old success cannot cause an early C");
    }

    public static async Task IdleGapPreservesScheduledAttackAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        f.Settings.SemiAuto.AttackWeaveDelayMs = 2500;
        await f.PreparePair();
        var pressed = f.Keyboard.Keys.Count;
        f.Clock.Advance(1501);
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "scheduled C is exempt from the key-gap reset");
        Equal(pressed, f.Keyboard.Keys.Count, "long configured delay still blocks following skills");
        f.Clock.Advance(999);
        f.Keyboard.PressResult = key => key == "C" ? OperationResult.Fail("C failure") : OperationResult.Ok();
        await f.Tick();
        f.Clock.Advance(2000);
        f.Keyboard.PressResult = null;
        await f.Tick();
        Equal("C", f.Keyboard.Keys.Last(), "failed C keeps retrying even across a further 1.5 second gap");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.attack_weave.idle_reset"),
            "neither intentional C delay nor C retry resets the pair");
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "chain continues after the successful C");
    }

    public static async Task RetriesAndUnconfirmedAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        for (var i = 0; i < 4; i++)
        {
            await f.Tick();
        }

        Equal(1, f.State.AttackWeave.ConfirmedCount, "repeated second-stage keys are not extra successful skills");
        Check(!f.Keyboard.Keys.Contains("C"), "no C without second confirmed success");
        Equal("Skill 2", f.LastSkill(), "unconfirmed chain stays on the same stage");
        f.Confirm(2);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "one C for the confirmed pair");
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "third stage resumes after retried second stage");
    }

    public static async Task IdleGapIgnoresMaintenanceKeysAsync()
    {
        using var f = new Fixture(Node(1), Chain(2, 3));
        f.Settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
        {
            Key = "NumPad9", BelowPercent = 60, ActionType = MaintenanceRuleActionType.Potion,
            RunTiming = MaintenanceRuleRunTiming.Always
        });
        f.Api.InventoryItems = new[] { new InventoryItemSnapshot(1, 1, "精神之仙药", 1, 0, false, 17) };
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        f.Clock.Advance(1000);
        f.Api.Player = f.Api.Player with { CurrentMp = f.Api.Player.MaxMp / 2 };
        await f.Tick();
        Equal("NumPad9", f.Keyboard.Keys.Last(), "maintenance still runs while the combat pair is pending");
        f.Clock.Advance(501);
        f.Api.Player = f.Api.Player with { CurrentMp = f.Api.Player.MaxMp };
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "maintenance input cannot postpone the combat key-gap reset");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "maintenance does not count as a successful combat skill");
    }

    public static async Task IdleGapResetsOpeningLoopAsync()
    {
        using var f = new Fixture(Node(1));
        f.Settings.Skills.OpeningSkill = new OpeningSkillConfig
        {
            Enabled = true, SkillId = 4, SkillName = "Skill 4", Key = "NumPad0"
        };
        f.RebuildPlan();
        var target = (await f.Api.ReadLockedTargetAsync()).Value!;
        await f.Controller.TickOpeningAttackKeyLoopAsync(f.Context, f.Plan, f.State, target);
        f.Confirm(4);
        await f.Controller.TickOpeningAttackKeyLoopAsync(f.Context, f.Plan, f.State, target);
        Equal(1, f.State.AttackWeave.ConfirmedCount, "opening success starts the pair");
        f.Clock.Advance(1501);
        await f.Controller.TickOpeningAttackKeyLoopAsync(f.Context, f.Plan, f.State, target);
        Equal(0, f.State.AttackWeave.ConfirmedCount, "opening loop checks timeout without the normal combat tick");
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "combat handoff cannot reuse expired opening count");
    }

    public static async Task IdleGapTracksSpiritmasterRetryAsync()
    {
        foreach (var gapBeforeRetry in new[] { 1000, 1501 })
        {
            using var f = new Fixture(Node(1), Node(2), Node(3));
            f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
            f.RebuildPlan();
            await f.Tick();
            f.Confirm(1);
            await f.Tick();
            f.Clock.Advance(gapBeforeRetry);
            f.State.MarkPressedSkillCooldownRetried(DateTimeOffset.Now.AddSeconds(-1));
            await f.Tick();
            var reset = gapBeforeRetry > 1500;
            Equal(reset ? 0 : 1, f.State.AttackWeave.ConfirmedCount, "retry key preserves or restarts the correct pair");
            Check(f.State.AttackWeave.HasAttempts, "retry after a reset tracks a new confirmation attempt");
            f.Clock.Advance(600);
            f.Confirm(2);
            await f.Tick();
            Equal(reset ? 1 : 2, f.State.AttackWeave.ConfirmedCount,
                "retry refreshes the last-key time and its actual success is counted once");
            Equal(reset ? 1 : 0, f.Logger.Entries.Count(entry => entry.EventName == "semi_auto.attack_weave.idle_reset"),
                "idle timeout is measured from the most recent skill retry");
        }
    }

    public static async Task LateChainConfirmationAsync()
    {
        foreach (var spiritmaster in new[] { false, true })
        foreach (var stalePolls in new[] { false, true })
        {
            using var f = new Fixture(Chain(1, 6));
            f.Settings.SemiAuto.ConfirmTimeoutMs = 500;
            f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = spiritmaster;
            f.RebuildPlan();
            for (uint stage = 1; stage <= 6; stage++)
            {
                await f.Tick();
                Equal("Skill " + stage, f.LastSkill(), "late confirmation must preserve chain order");
                if (stage % 2 == 0)
                {
                    // Late relative to the ordinary timeout, within 1.5 seconds of the last key.
                    if (stalePolls)
                    {
                        for (var poll = 0; poll < 2; poll++)
                        {
                            f.Clock.Advance(1101);
                            f.State.MarkPressedSkillCooldownRetried(DateTimeOffset.Now.AddSeconds(-1));
                            await f.Tick();
                            Equal(1, f.State.AttackWeave.ConfirmedCount, "late retries alone do not count");
                            Equal("Skill " + stage, f.LastSkill(), "unconfirmed chain stays on the current stage");
                        }
                    }

                    f.Clock.Advance(1101);
                    f.Confirm(stage);
                    var beforeWait = f.Keyboard.Keys.Count;
                    await f.Tick();
                    Equal(2, f.State.AttackWeave.ConfirmedCount,
                        $"late stage {stage} must complete the pair (spiritmaster={spiritmaster}, stalePolls={stalePolls})");
                    Equal(beforeWait, f.Keyboard.Keys.Count, "late success must block the next stage before waiting");
                    f.Clock.Advance(599);
                    await f.Tick();
                    Equal(beforeWait, f.Keyboard.Keys.Count, "configured delay starts when late success is observed");
                    f.Clock.Advance(1);
                    await f.Tick();
                    Equal("C", f.Keyboard.Keys.Last(), "late confirmed pair presses C after 600 ms");
                }
                else
                {
                    f.Confirm(stage);
                }
            }

            Equal(3, f.Keyboard.Keys.Count(key => key == "C"), "six successful stages produce three C presses");
            Equal(6, f.Logger.Entries.Count(entry => entry.EventName == "semi_auto.attack_weave.skill_confirmed"),
                "late successes and repeated snapshots are counted exactly once");
        }
    }

    public static async Task LateChainRootConfirmationAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        f.Settings.SemiAuto.ConfirmTimeoutMs = 500;
        await f.Tick();
        await f.Tick();
        f.Clock.Advance(501);
        await f.Tick();
        Equal(0, f.State.AttackWeave.ConfirmedCount, "neither delayed stage is counted without evidence");
        f.Clock.Advance(501);
        f.Confirm(1);
        f.Confirm(2);
        var beforeWait = f.Keyboard.Keys.Count;
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "late root and second-stage confirmations both count");
        Equal(beforeWait, f.Keyboard.Keys.Count, "third stage is blocked for the late first pair");
        f.Clock.Advance(600);
        await f.Tick();
        Equal("C", f.Keyboard.Keys.Last(), "late first pair waits then presses C");
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "late root confirmation preserves the third stage after C");
    }

    public static async Task MissingChainSnapshotBeforeLateConfirmationAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        f.Settings.SemiAuto.ConfirmTimeoutMs = 500;
        await f.Tick();
        f.Confirm(1);
        await f.Tick();
        f.Clock.Advance(501);
        var skills = f.Api.Skills;
        f.Api.Skills = skills.Where(skill => skill.SkillId != 2).ToArray();
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "missing snapshot cannot confirm a chain release");
        f.Clock.Advance(501);
        f.Api.Skills = skills;
        f.Confirm(2);
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "original attempt survives missing snapshots and short timeout");
        Equal("Skill 2", f.LastSkill(), "late snapshot cannot allow third stage before C");
    }

    public static async Task EndedChainReleasesUnconfirmedAttemptsAsync()
    {
        foreach (var expired in new[] { false, true })
        {
            using var f = new Fixture(Chain(1, 3), Node(4));
            f.Settings.SemiAuto.ConfirmTimeoutMs = 500;
            await f.Tick();
            f.Confirm(1);
            await f.Tick();
            f.Clock.Advance(501);
            await f.Tick();
            Equal(1, f.State.AttackWeave.ConfirmedCount, "unsuccessful second stage is still uncounted");
            if (expired)
            {
                f.State.StartPendingChainWindow(DateTimeOffset.Now.AddSeconds(-1));
            }
            else
            {
                f.State.ClearChain();
            }

            await f.Tick();
            Check(!f.State.HasChainWork, "ended chain releases its confirmation scope");
            f.Confirm(2);
            await f.Tick();
            Equal(1, f.State.AttackWeave.ConfirmedCount, "late change after discarded attempt cannot create a phantom success");
            Check(!f.Keyboard.Keys.Contains("C"), "one success plus an abandoned attempt cannot press C");
            Equal("Skill 4", f.LastSkill(), "failed chain does not permanently block the following root");
            f.Confirm(4);
            await f.Tick();
            Equal(2, f.State.AttackWeave.ConfirmedCount, "the following actual success completes the pair");
            f.Clock.Advance(600);
            await f.Tick();
            Equal("C", f.Keyboard.Keys.Last(), "next successful pair can still weave after chain cleanup");
        }
    }

    public static async Task FailedInputAndAttackRetryAsync()
    {
        using (var failed = new Fixture(Node(1), Node(2)))
        {
            failed.Keyboard.PressResult = _ => OperationResult.Fail("test input failure");
            await failed.Tick();
            failed.Confirm(1);
            await failed.Tick();
            Equal(0, failed.State.AttackWeave.ConfirmedCount, "failed transport never creates a counted attempt");
        }

        using var f = new Fixture(Chain(1, 3));
        await f.PreparePair();
        f.Keyboard.PressResult = key => key == "C" ? OperationResult.Fail("test C failure") : OperationResult.Ok();
        f.Clock.Advance(600);
        await f.Tick();
        Equal(2, f.State.AttackWeave.ConfirmedCount, "failed C retains pair and blocks subsequent skills");
        var count = f.Keyboard.Keys.Count;
        await f.Tick();
        Equal(count, f.Keyboard.Keys.Count, "failed C retries are throttled");
        f.Keyboard.PressResult = null;
        f.Clock.Advance(100);
        await f.Tick();
        Equal(0, f.State.AttackWeave.ConfirmedCount, "successful C clears the pair");
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "C retry preserves the next chain stage");
    }

    public static async Task TargetAndDisableCancellationAsync()
    {
        foreach (var transition in new[] { "dead", "missing", "changed", "disabled" })
        {
            using var f = new Fixture(Chain(1, 3));
            await f.PreparePair();
            switch (transition)
            {
                case "dead": f.Api.TargetCurrentHp = 0; break;
                case "missing": f.Api.TargetEntityId = 0; break;
                case "changed": f.Api.TargetOwnServerObjectId = 98765; break;
                case "disabled": f.Settings.SemiAuto.AttackWeaveEnabled = false; break;
            }

            f.Clock.Advance(600);
            await f.Tick();
            Check(!f.Keyboard.Keys.Contains("C"), transition + " cancels scheduled C");
            Equal(0, f.State.AttackWeave.ConfirmedCount, transition + " resets old pair");
        }
    }

    public static async Task StopAndDeathAsync()
    {
        using (var f = new Fixture(Chain(1, 3)))
        {
            await f.PreparePair();
            f.Clock.Advance(600);
            f.Stop.Cancel();
            try
            {
                await f.Tick();
                throw new InvalidOperationException("cancelled tick must stop");
            }
            catch (OperationCanceledException) { }
            Check(!f.Keyboard.Keys.Contains("C"), "stop cannot emit a delayed C");
        }

        using var dead = new Fixture(Chain(1, 3));
        await dead.PreparePair();
        dead.Api.Player = dead.Api.Player with { CurrentHp = 0 };
        await new StationaryCombatController(dead.Keyboard, dead.Controller)
            .TickPlayerLifeGuardAsync(dead.Context, dead.Plan, dead.State, new StationaryCombatState(), false);
        Equal(0, dead.State.AttackWeave.ConfirmedCount, "player death clears delayed C before revive flow");
        Check(!dead.State.AttackWeave.AttackDueAt.HasValue, "no pending attack survives death");
    }

    public static async Task PrefixAndOpeningAsync()
    {
        using var f = new Fixture(Node(1), Node(2, "触发技能"), Node(3, "触发技能"));
        f.Settings.Skills.OpeningSkill = new OpeningSkillConfig
        {
            Enabled = true, SkillId = 4, SkillName = "Skill 4", Key = "NumPad0"
        };
        f.RebuildPlan();
        await f.Tick();
        f.Confirm(4);
        await f.Tick();
        Equal("Skill 2", f.LastSkill(), "first trigger after opening skill");
        f.Confirm(2);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        await f.Tick();
        Equal("Skill 3", f.LastSkill(), "prefix cursor survives C without replaying earlier triggers");
        f.Confirm(3);
        await f.Tick();
        Equal("Skill 1", f.LastSkill(), "main root runs after remaining prefix");
        f.Confirm(1);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        Sequence(new[] { "NumPad0", "D2", "C", "D3", "D1", "C" }, f.Keyboard.Keys,
            "opening, prefix and normal skills share confirmed count");
    }

    public static async Task DisabledCompatibilityAsync()
    {
        using var f = new Fixture(Node(1), Node(2, "触发技能"), Node(3, "触发技能"));
        f.Settings.SemiAuto.AttackWeaveEnabled = false;
        await f.Tick();
        Sequence(new[] { "D2", "D3", "D1" }, f.Keyboard.Keys, "disabled keeps existing prefix batching");
        f.Confirm(1);
        await f.Tick();
        Check(!f.Keyboard.Keys.Contains("C"), "disabled never inserts a weaving C");
        Check(!f.State.AttackWeave.HasAttempts, "disabled does not track attempts");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "disabled does not count successes");
    }

    public static async Task OpeningAttackSwitchIsIndependentAsync()
    {
        using var f = new Fixture(Chain(1, 3));
        f.Settings.SemiAuto.AttackKeyLoopEnabled = true;
        await f.Tick();
        Sequence(new[] { "C" }, f.Keyboard.Keys, "original opening C remains enabled");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "opening C is not a successful skill");
        await f.PreparePair();
        f.Clock.Advance(600);
        await f.Tick();
        Sequence(new[] { "C", "D1", "D1", "C" }, f.Keyboard.Keys, "opening C and pair C are independent");
    }

    public static async Task ConditionPriorityAsync()
    {
        foreach (var preempt in new[] { true, false })
        {
            using var f = new Fixture(Chain(1, 3), Node(4, "条件技能"));
            f.Settings.SemiAuto.ConditionSkillPreemptsChain = preempt;
            f.Api.LockedTargetAbnormalStatuses = LockedTargetAbnormalStatusSnapshot.Empty(DateTimeOffset.Now);
            f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == 4
                ? skill with { XmlTargetValidStatuses = "Stumble" } : skill).ToArray();
            await f.Tick();
            f.Confirm(1);
            var target = (await f.Api.ReadLockedTargetAsync()).Value!;
            f.Api.LockedTargetAbnormalStatuses = new LockedTargetAbnormalStatusSnapshot(target, 1,
                new[] { new AbnormalStatusEntrySnapshot(0, 8218, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory, 0, 1, 0) }, DateTimeOffset.Now);
            await f.Tick();
            Equal(preempt ? "Skill 4" : "Skill 2", f.LastSkill(), "condition preempt switch retains its priority");
            Equal(!preempt, f.State.HasChainWork, "only the existing condition-preempt rule clears the chain");
            f.Confirm(preempt ? 4u : 2u);
            await f.Tick();
            f.Clock.Advance(600);
            await f.Tick();
            Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "condition or chain success completes the same pair");
        }
    }

    public static async Task SpiritmasterRetryAccountingAsync()
    {
        using var f = new Fixture(Node(1), Node(2), Node(3));
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        f.RebuildPlan();
        await f.Tick();
        f.State.MarkPressedSkillCooldownRetried(DateTimeOffset.Now.AddSeconds(-1));
        await f.Tick();
        Equal(0, f.State.AttackWeave.ConfirmedCount, "spiritmaster cooldown retry does not count as a release");
        Equal(2, f.Keyboard.Keys.Count(key => key == "D1"), "existing retry-until-cooldown path still runs");
        f.Confirm(1);
        await f.Tick();
        Equal("Skill 2", f.LastSkill(), "spiritmaster continues after confirmed first skill");
        f.Confirm(2);
        await f.Tick();
        Check(!f.State.HasPressedSkillCooldownRetryKey(), "second success completes legacy cooldown confirmation before waiting");
        f.Clock.Advance(600);
        await f.Tick();
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "spiritmaster confirmed pair weaves once");
    }

    public static async Task OpeningLoopHandoffAsync()
    {
        using var f = new Fixture(Node(1), Node(2));
        f.Settings.Skills.OpeningSkill = new OpeningSkillConfig
        {
            Enabled = true, SkillId = 4, SkillName = "Skill 4", Key = "NumPad0"
        };
        f.RebuildPlan();
        var previousTarget = (await f.Api.ReadLockedTargetAsync()).Value!;
        f.State.ObserveTarget(previousTarget, out _, out _);
        f.Api.TargetOwnServerObjectId = 9988;
        var target = (await f.Api.ReadLockedTargetAsync()).Value!;
        await f.Controller.TickOpeningAttackKeyLoopAsync(f.Context, f.Plan, f.State, target);
        f.Confirm(4);
        await f.Controller.TickOpeningAttackKeyLoopAsync(f.Context, f.Plan, f.State, target);
        Equal(1, f.State.AttackWeave.ConfirmedCount, "opening path confirms first skill on new target");
        await f.Tick();
        Equal(1, f.State.AttackWeave.ConfirmedCount, "handoff to normal combat preserves new-target opening count");
        Equal("Skill 1", f.LastSkill(), "normal combat continues after opening path");
        f.Confirm(1);
        await f.Tick();
        f.Clock.Advance(600);
        await f.Tick();
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "opening path and normal combat form one pair");
    }

    public static async Task AccountIsolationAndDelayAsync()
    {
        using var a = new Fixture(Chain(1, 3));
        using var b = new Fixture(Chain(1, 3));
        a.Settings.SemiAuto.AttackWeaveDelayMs = 0;
        await a.PreparePair();
        Equal(1, a.Keyboard.Keys.Count(key => key == "C"), "zero delay is supported");
        Equal(0, b.State.AttackWeave.ConfirmedCount, "accounts do not share count or waiting state");
        b.Settings.SemiAuto.AttackWeaveDelayMs = 1200;
        await b.PreparePair();
        b.Clock.Advance(1199);
        await b.Tick();
        Check(!b.Keyboard.Keys.Contains("C"), "configured 1200 ms is respected");
        b.Clock.Advance(1);
        await b.Tick();
        Equal(1, b.Keyboard.Keys.Count(key => key == "C"), "one C at custom delay");
    }

    public static Task SettingsAndLayoutAsync()
    {
        var legacy = JsonSerializer.Deserialize<SemiAutoScriptSettings>("{}")!;
        Check(!legacy.AttackWeaveEnabled, "legacy configuration defaults off");
        Equal(600, legacy.AttackWeaveDelayMs, "default delay");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new ScriptSettings();
                settings.SemiAuto.AttackWeaveEnabled = true;
                settings.SemiAuto.AttackWeaveDelayMs = 725;
                settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
                settings.Skills.OpeningSkill = new OpeningSkillConfig
                {
                    Enabled = true, SkillId = 1671, SkillName = "命令:威胁的气势 I", Key = "NumPad1"
                };
                var configStore = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "account1", ScriptSettings = settings });
                var logger = new InMemoryRoadhogLogger();
                var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
                using var form = new AccountSettingsForm("account1", runtime, configStore,
                    new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                var check = Field<Control>(form, "attackWeaveCheckBox");
                var input = Field<Control>(form, "attackWeaveDelayTextBox");
                var condition = Field<Control>(form, "conditionSkillPreemptsChainCheckBox");
                var panel = Field<Control>(form, "autoSkillPanel");
                Check((bool)check.GetType().GetProperty("Checked")!.GetValue(check)!, "UI loads enabled setting");
                Equal("725", input.Text, "UI loads saved delay");
                Check(input.Enabled, "enabled switch enables delay field");
                Equal(condition.Top, check.Top, "timing switches share one row");
                var chainInput = Field<Control>(form, "chainWindowPerLinkTextBox");
                Equal(chainInput.Top, input.Top, "timing inputs align");
                Check(condition.Right < chainInput.Left && chainInput.Right < check.Left && check.Right < input.Left,
                    "timing controls do not overlap");
                Check(panel.Top > input.Parent!.Bottom, "skill lists sit below options");
                var available = Field<TreeView>(form, "availableSkillTree");
                var selected = Field<TreeView>(form, "selectedSkillTree");
                Equal(available.Size, selected.Size, "skill lists have equal sizes");
                Equal(available.Top, selected.Top, "skill lists align");
                var opener = Field<Control>(form, "openingSkillKeyButton").Parent!;
                Check(opener.Top > selected.Bottom && opener.Top - selected.Bottom <= 24,
                    "opening skill follows lists without a large empty gap");
                Check(opener.Bottom <= panel.Height, "opening skill stays inside skill panel");
                var spiritSwitch = Field<Control>(form, "spiritmasterAutoSkillCheckBox");
                var spiritButton = Field<Control>(form, "spiritmasterSettingsButton");
                Check(spiritSwitch.Right < spiritButton.Left, "spirit settings button does not overlap switch");

                var tabs = Find<TabControl>(form).First();
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(tab => tab.Text == "技能");
                form.CreateControl();
                form.PerformLayout();
                var preview = Environment.GetEnvironmentVariable("ROADHOG_ATTACK_WEAVE_PREVIEW");
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    // DrawToBitmap lets the docked tab cover sibling buttons; render the
                    // form's overlay buttons last to match their actual screen z-order.
                    foreach (var button in form.Controls.OfType<Button>())
                    {
                        var origin = button.PointToScreen(Point.Empty);
                        button.DrawToBitmap(bitmap, new Rectangle(
                            origin.X - form.Left, origin.Y - form.Top, button.Width, button.Height));
                    }
                    bitmap.Save(preview);
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    var scrollPage = (Panel)panel.Parent!;
                    Check(scrollPage.HorizontalScroll.Visible && scrollPage.VerticalScroll.Visible,
                        "small windows can scroll to all skill settings");
                    using var smallBitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(smallBitmap, new Rectangle(Point.Empty, form.Size));
                    smallBitmap.Save(Path.ChangeExtension(preview, ".small.png"));
                    form.Hide();
                }

                input.Text = "875";
                Save(form);
                var saved = configStore.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Equal(875, saved.SemiAuto.AttackWeaveDelayMs, "UI save persists delay");
                Check(saved.SemiAuto.AttackWeaveEnabled, "UI save persists checkbox");
                Equal(1671u, saved.Skills.OpeningSkill.SkillId, "rearranged opening skill retains selection");
                Equal("NumPad1", saved.Skills.OpeningSkill.Key, "rearranged opening skill retains key");
                Check(saved.Skills.OpeningSkill.Enabled && saved.Skills.SpiritmasterAutoSkillLogicEnabled,
                    "rearranged skill options retain enabled settings");
                var copy = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(saved.Clone()))!;
                Equal(875, copy.SemiAuto.AttackWeaveDelayMs, "clone and JSON retain delay");
                Check(copy.SemiAuto.AttackWeaveEnabled, "clone and JSON retain switch");

                check.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(check, new object[] { EventArgs.Empty });
                Check(!input.Enabled, "clicking off disables delay field");
                Save(form);
                Check(!configStore.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.SemiAuto.AttackWeaveEnabled,
                    "turning off survives save");
                foreach (var (value, expected) in new[] { ("-1", 0), ("10001", 10000), ("invalid", 10000) })
                {
                    input.Text = value;
                    Save(form);
                    Equal(expected, configStore.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.SemiAuto.AttackWeaveDelayMs,
                        "UI validates delay: " + value);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }

    private static void Save(AccountSettingsForm form)
    {
        var args = new object?[] { null };
        Check((bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, args)!, "UI save: " + args[0]);
    }

    private static T Field<T>(object obj, string name) =>
        (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;

    private static IEnumerable<T> Find<T>(Control parent) where T : Control =>
        parent.Controls.Cast<Control>().SelectMany(child => (child is T found ? new[] { found } : Array.Empty<T>()).Concat(Find<T>(child)));

    private sealed class Fixture : IDisposable
    {
        public ScriptSettings Settings { get; } = new();
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Keyboard { get; } = new();
        public InMemoryRoadhogLogger Logger { get; } = new();
        public SemiAutoCombatState State { get; } = new();
        public ManualClock Clock { get; } = new();
        public CancellationTokenSource Stop { get; } = new();
        public SemiAutoSkillPlan Plan { get; private set; } = null!;
        public AccountWorkerContext Context { get; }
        public SemiAutoCombatController Controller { get; }

        public Fixture(params SkillConfigNode[] roots)
        {
            Settings.SemiAuto.AttackWeaveEnabled = true;
            Settings.SemiAuto.AttackKeyLoopEnabled = false;
            Settings.SemiAuto.ConfirmTimeoutMs = 3000;
            Settings.Skills.ExecutionTree = roots.ToList();
            Settings.Maintenance.SitMaintenanceEnabled = false;
            Controller = new SemiAutoCombatController(Keyboard, timeProvider: Clock);
            var config = new AccountConfig { AccountName = "account1", ScriptSettings = Settings };
            Context = new AccountWorkerContext(config, new RoadhogSnapshotReaderFactory(Api), Logger,
                new AccountRuntimeManager(Logger), new AccountWorkerOptions(), Stop.Token);
            RebuildPlan();
        }

        public void RebuildPlan()
        {
            Plan = SemiAutoSkillPlan.FromSettings(Settings.Skills);
            Api.Skills = Plan.SkillReadIds.Concat(Plan.TriggerPrefixRoots.Select(node => node.SkillId)).Distinct().Select(Skill).ToArray();
        }

        public Task<TimeSpan> Tick() => Controller.TickAsync(Context, Plan, State);

        public void Confirm(uint id) => Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
            ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 5000u) } : skill).ToArray();

        public string LastSkill() => Logger.Entries.Last(entry => entry.EventName == "semi_auto.key.pressed").Fields["skill"]!.ToString()!;

        public async Task PreparePair()
        {
            await Tick();
            Confirm(1);
            await Tick();
            Confirm(2);
            await Tick();
        }

        public void Dispose() => Stop.Dispose();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int milliseconds) => now = now.AddMilliseconds(milliseconds);
    }

    private static SkillSnapshot Skill(uint id) => new(id, "Skill " + id, 1, 1, "Skill " + id, 1, false, 5000, 0);
    private static SkillConfigNode Node(uint id, string type = "主动技能") => new() { SkillId = id, Name = "Skill " + id, BaseName = "Skill " + id, Type = type };
    private static SkillConfigNode Chain(uint first, int count)
    {
        var root = Node(first);
        var current = root;
        for (var i = 1; i < count; i++)
        {
            var child = Node(first + (uint)i, "连续技");
            current.Children.Add(child);
            current = child;
        }
        return root;
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) =>
        Check(expected.SequenceEqual(actual), $"{message}: expected {string.Join(",", expected)}, got {string.Join(",", actual)}");
}
