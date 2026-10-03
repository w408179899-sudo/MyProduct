using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class QuickbarSkillChainHandoffTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T10:00:00+08:00");

    public static async Task DelayedReleaseAndIconAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar();
        await fixture.TickAt(80);
        var deadline = fixture.State.ChainTransition?.Deadline;
        Check(deadline.HasValue && fixture.State.ChainTransition?.Source.SkillId == 11,
            "the exact preceding CD opens a bounded handoff before the actor record or continuation appears");

        for (var elapsed = 160; elapsed <= 560; elapsed += 80)
        {
            await fixture.TickAt(elapsed);
            Check(fixture.State.ChainTransition?.Deadline == deadline,
                "repeated official dark frames do not extend the handoff deadline");
        }
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(640);
        Check(fixture.State.ChainTransition?.Deadline == deadline,
            "a late actual-release record confirms the same attempt without restarting its handoff");
        await fixture.TickAt(720);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }),
            "an unrelated ready root never interrupts the delayed continuation");

        fixture.Reader.Value = Bar(effective: 12, canUse: true, last: 11, time: 1100);
        await fixture.TickAt(800);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12 }) &&
              fixture.State.PendingAction?.Node.SkillId == 12,
            "the first truly open continuation is pressed in that poll, after an800ms display delay");
        Check(fixture.Reader.ReadCount >= 11 && fixture.Keyboard.Holds.All(hold => hold <= TimeSpan.FromMilliseconds(30)),
            "the wait continues fresh80ms polling and sends only finite key taps");
    }

    public static async Task FourStagesAcrossFramesAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar();
        await fixture.TickAt(80);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(160);
        await fixture.TickAt(560);
        fixture.Reader.Value = Bar(effective: 12, canUse: true, last: 11, time: 1100);
        await fixture.TickAt(640);

        fixture.AdvanceCooldown(12);
        fixture.Reader.Value = Bar(effective: 12, last: 11, time: 1100);
        await fixture.TickAt(720);
        Check(fixture.State.ChainTransition?.Source.SkillId == 12,
            "the second accepted/CD-observed stage receives its own bounded transition");
        fixture.Reader.Value = Bar(effective: 12, last: 12, time: 1200);
        await fixture.TickAt(800);
        await fixture.TickAt(1200);
        fixture.Reader.Value = Bar(effective: 13, canUse: true, last: 12, time: 1200);
        await fixture.TickAt(1280);

        fixture.AdvanceCooldown(13);
        fixture.Reader.Value = Bar(effective: 13, last: 12, time: 1200);
        await fixture.TickAt(1360);
        fixture.Reader.Value = Bar(effective: 13, last: 13, time: 1300);
        await fixture.TickAt(1440);
        await fixture.TickAt(1920);
        fixture.Reader.Value = Bar(effective: 14, canUse: true, last: 13, time: 1300);
        await fixture.TickAt(2000);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 13, 14 }),
            "third and fourth configured stages open on later frames without unrelated roots or repeated predecessors");
        Check(fixture.State.PendingAction?.Node.SkillId == 14,
            "a fourth-stage key still requires real acceptance rather than an assumed chain result");
    }

    public static async Task ProbabilityDeadlineAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(80);
        var deadline = fixture.State.ChainTransition?.Deadline ?? throw new Exception("missing probability handoff window");
        Check(deadline - fixture.Clock.Now <= TimeSpan.FromMilliseconds(1500),
            "an unopened probability continuation has at most1500ms to become available");

        for (var elapsed = 160; Start.AddMilliseconds(elapsed) < deadline; elapsed += 80)
        {
            fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
            await fixture.TickAt(elapsed);
            Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }) &&
                  fixture.State.ChainTransition?.Deadline == deadline,
                "officially false CanUse never authorizes the probability stage or extends the wait");
        }
        await fixture.TickAt(deadline);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && fixture.State.ChainTransition is null,
            "the exact handoff deadline returns to the next ready root in the same poll");
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await fixture.TickAt(deadline.AddMilliseconds(80));
        Check(fixture.State.ChainTransition is null && !fixture.Keyboard.SkillIds.Contains(12),
            "unchanged old release evidence cannot reopen an expired probability wait");

        var thirdStage = new Fixture();
        await thirdStage.TickAt(0);
        thirdStage.AdvanceCooldown(11);
        thirdStage.Reader.Value = Bar(effective: 12, canUse: true, last: 11, time: 1100);
        await thirdStage.TickAt(80);
        thirdStage.AdvanceCooldown(12);
        thirdStage.Reader.Value = Bar(effective: 12, canUse: false, last: 12, time: 1200);
        await thirdStage.TickAt(160);
        var thirdDeadline = thirdStage.State.ChainTransition?.Deadline ?? throw new Exception("missing third-stage probability window");
        await thirdStage.TickAt(thirdDeadline.AddMilliseconds(-1));
        Check(thirdStage.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12 }),
            "a probability third stage is never inferred from its successful second stage");
        await thirdStage.TickAt(thirdDeadline);
        Check(thirdStage.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 31 }),
            "an untriggered third stage returns to roots when its own finite opportunity wait expires");
    }

    public static async Task CoolingChildReleasesRootsAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.AdvanceCooldown(12);
        fixture.Reader.Value = Bar(effective: 12, canUse: true);
        await fixture.TickAt(80);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && fixture.State.ChainTransition is null,
            "every direct configured continuation on official CD releases the wait immediately, even if its opportunity bit is true");
        Check(fixture.SkillReads.Any(ids => ids.Contains(12u)),
            "the decision to end the wait uses the exact child's current official cooldown");

        fixture = new();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(80);
        fixture.AdvanceCooldown(12);
        await fixture.TickAt(160);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && fixture.State.ChainTransition is null,
            "a child entering CD during an existing wait releases roots without waiting for the remaining deadline");
    }

    public static async Task OfficialEmptyFrameAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(80);
        var deadline = fixture.State.ChainTransition?.Deadline;

        fixture.Reader.Value = Bar(last: 0, time: 0) with { Slots = Array.Empty<SkillAvailabilitySlotSnapshot>() };
        fixture.ReturnEmptySkills = true;
        await fixture.TickAt(160);
        await fixture.TickAt(240);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }) && fixture.State.ChainTransition?.Deadline == deadline,
            "published empty skills/slots and zero actor fields do not reuse an earlier opportunity or prove every child cooling");

        fixture.ReturnEmptySkills = false;
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await fixture.TickAt(320);
        Check(!fixture.Keyboard.SkillIds.Contains(12), "a current official false remains false after an empty frame");
        fixture.Reader.Value = Bar(effective: 12, canUse: true, last: 11, time: 1100);
        await fixture.TickAt(400);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12 }),
            "a fresh published true can open the exact continuation after legitimate empty frames");

        var zeroActor = new Fixture();
        zeroActor.Reader.Value = Bar(last: 0, time: 0);
        await zeroActor.TickAt(0);
        zeroActor.AdvanceCooldown(11);
        await zeroActor.TickAt(80);
        Check(zeroActor.State.PendingAction?.Node.SkillId == 11 && zeroActor.State.ActiveChainSource?.SkillId != 11 &&
              zeroActor.State.ChainTransition?.Source.SkillId == 11,
            "a production-capable official snapshot with zero actor ID/time may protect the real CD handoff but cannot claim release confirmation");
        zeroActor.Reader.Value = Bar(effective: 12, canUse: true, last: 0, time: 0);
        await zeroActor.TickAt(160);
        Check(zeroActor.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12 }),
            "a genuinely open continuation can follow a CD-only predecessor even while official actor fields remain zero");
    }

    public static async Task FixedDeadlineAndRetryBaselineAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        var firstAttempt = fixture.State.PendingAction ?? throw new Exception("first attack not recorded");
        await fixture.TickAt(80);
        var retry = fixture.State.PendingAction ?? throw new Exception("retry not recorded");
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 11 }) &&
              retry.StartedAt == firstAttempt.StartedAt && retry.Deadline == firstAttempt.Deadline,
            "unaccepted retries preserve the original action baseline before any handoff starts");

        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar();
        await fixture.TickAt(160);
        var deadline = fixture.State.ChainTransition?.Deadline ?? throw new Exception("retry CD did not open handoff");
        await fixture.TickAt(240);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(800);
        Check(fixture.State.ChainTransition?.Deadline == deadline && fixture.Keyboard.SkillIds.Count == 2,
            "late confirmation of a retried action retains the first transition's absolute deadline and stops predecessor retries");
        await fixture.TickAt(deadline.AddMilliseconds(-1));
        Check(fixture.Keyboard.SkillIds.Count == 2 && fixture.State.ChainTransition?.Deadline == deadline,
            "a repeated actor record immediately before expiry cannot prolong the wait");
        await fixture.TickAt(deadline);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 11, 31 }) && fixture.State.ChainTransition is null,
            "the retry/confirmation history does not delay root recovery beyond the original transition deadline");
    }

    public static async Task ConfirmedOpportunityNotRepeatedAsync()
    {
        var fixture = new Fixture();
        fixture.Skills[14] = fixture.Skills[14] with { CooldownDuration = 0 };
        await fixture.TickAt(0);
        fixture.Reader.Value = Bar(effective: 14, canUse: true, last: 11, time: 1100);
        await fixture.TickAt(80);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 14 }),
            "a genuinely open configured later stage is usable even when intermediate releases were not observed");
        fixture.Reader.Value = Bar(effective: 14, canUse: true, last: 14, time: 1200);
        await fixture.TickAt(160);
        await fixture.TickAt(240);
        Check(fixture.Keyboard.SkillIds.Count(id => id == 14) == 1 && fixture.State.ChainTransition is null,
            "a confirmed zero-CD final stage is not repeated while the same opportunity remains lit");
        Check(fixture.Keyboard.SkillIds.Contains(31), "a completed chain returns to root priority");
        await fixture.TickAt(320, allowCombat: _ => false);
        await fixture.TickAt(400);
        Check(fixture.Keyboard.SkillIds.Count(id => id == 14) == 1,
            "maintenance yielding with the same living target preserves consumption of that still-lit successful opportunity");
    }

    public static async Task ScopeAndMaintenanceResetAsync()
    {
        foreach (var mutation in new[] { "death", "target", "target-server", "page", "binding" })
        {
            var fixture = await WaitingFixture();
            fixture.Reader.Value = mutation switch
            {
                "death" => fixture.Reader.Value with { CombatState = Guard(hp: 0) },
                "target" => fixture.Reader.Value with { CombatState = Guard(target: 51) },
                "target-server" => fixture.Reader.Value with { CombatState = Guard(server: 101) },
                "page" => fixture.Reader.Value with { Page = 1 },
                _ => fixture.Reader.Value with
                {
                    BindingSlots = new SkillAvailabilityBindingSnapshot[]
                    {
                        new(SkillQuickbar.Main, 0, 21, 41, 41),
                        new(SkillQuickbar.Main, 1, 21, 31, 31)
                    }
                }
            };
            await fixture.TickAt(160);
            Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }) && fixture.State.YieldToWorker &&
                  fixture.State.PendingAction is null && fixture.State.ChainTransition is null,
                mutation + " invalidates the handoff before any other attack key");
        }

        var maintenance = await WaitingFixture();
        Check(maintenance.State.ActiveChainSource?.SkillId == 11, "maintenance fixture has a truly confirmed predecessor");
        maintenance.State.SuspendInputAttempts();
        Check(maintenance.State.ChainTransition is null && maintenance.State.PendingAction is null &&
              maintenance.State.ActiveChainSource?.SkillId == 11,
            "existing serial maintenance takeover clears the handoff and pending key, retaining confirmed chain identity");
        await maintenance.TickAt(160);
        Check(maintenance.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && maintenance.State.ChainTransition is null,
            "the consumed old release cannot reopen its wait after maintenance returns");

        var dueGuard = await WaitingFixture();
        await dueGuard.TickAt(160, allowCombat: _ => false);
        Check(dueGuard.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }) && dueGuard.State.YieldToWorker &&
              dueGuard.State.ChainTransition is null,
            "a newly due maintenance guard interrupts the waiting poll before input");
    }

    public static async Task CancellationResetAsync()
    {
        var fixture = await WaitingFixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await ExpectCancellation(() => fixture.TickAt(160, cancellationToken: canceled.Token));
        Check(fixture.State.ChainTransition is null && fixture.State.PendingAction is null && fixture.Keyboard.SkillIds.Count == 1,
            "an already canceled worker clears the handoff and never presses another key");

        fixture = await WaitingFixture();
        using var duringRead = new CancellationTokenSource();
        fixture.Reader.OnRead = _ => duringRead.Cancel();
        await ExpectCancellation(() => fixture.TickAt(160, cancellationToken: duringRead.Token));
        Check(fixture.State.ChainTransition is null && fixture.State.PendingAction is null && fixture.Keyboard.SkillIds.Count == 1,
            "cancellation during a fresh official read clears the transition before action preparation");
    }

    public static async Task XmlChainWindowBoundAsync()
    {
        foreach (var sample in new[] { (Xml: "240", ExpectedMs: 240), (Xml: "3000", ExpectedMs: 1500) })
        {
            var fixture = new Fixture(sample.Xml, obsoleteConfigDelay: 1);
            await fixture.TickAt(0);
            fixture.AdvanceCooldown(11);
            fixture.Reader.Value = Bar(last: 11, time: 1100);
            await fixture.TickAt(80);
            var deadline = fixture.State.ChainTransition?.Deadline ?? throw new Exception("missing XML bounded handoff");
            Check(deadline - fixture.Clock.Now == TimeSpan.FromMilliseconds(sample.ExpectedMs),
                "handoff starts at actual release/CD evidence and uses exact-skill XML capped at1500ms, without the old per-stage delay");
            await fixture.TickAt(deadline.AddMilliseconds(-1));
            Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }), "the XML-bound wait remains active until its exact deadline");
            await fixture.TickAt(deadline);
            Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }), "the XML-bound deadline immediately restores root priority");
        }

        var lateAcceptance = new Fixture("240", obsoleteConfigDelay: 10000);
        for (var elapsed = 0; elapsed <= 3200; elapsed += 80) await lateAcceptance.TickAt(elapsed);
        Check(lateAcceptance.State.PendingAction?.StartedAt == Start && lateAcceptance.Keyboard.SkillIds.Count == 41,
            "an animation-blocked predecessor can remain unaccepted and retry for more than three seconds");
        lateAcceptance.AdvanceCooldown(11);
        lateAcceptance.Reader.Value = Bar();
        await lateAcceptance.TickAt(3280);
        var lateDeadline = lateAcceptance.State.ChainTransition?.Deadline;
        Check(lateDeadline == Start.AddMilliseconds(3520),
            "late real CD advancement still opens a full240ms XML window rather than treating the first attempted key as execution");
        lateAcceptance.Reader.Value = Bar(last: 11, time: 1100);
        await lateAcceptance.TickAt(3360);
        Check(lateAcceptance.State.ChainTransition?.Deadline == lateDeadline,
            "a later actor confirmation never restarts that accepted action's window");
        lateAcceptance.Reader.Value = Bar(effective: 12, canUse: true, last: 11, time: 1100);
        await lateAcceptance.TickAt(3440);
        Check(lateAcceptance.Keyboard.SkillIds.Last() == 12 && !lateAcceptance.Keyboard.SkillIds.Contains(31),
            "the continuation still wins after a predecessor's multi-second unaccepted retry history");

        var fork = new Fixture("240", siblingChainTime: "1000");
        await fork.TickAt(0);
        fork.AdvanceCooldown(11);
        fork.Reader.Value = Bar(last: 11, time: 1100);
        await fork.TickAt(80);
        Check(fork.State.ChainTransition?.Deadline == Start.AddMilliseconds(1080),
            "a short sibling's XML window cannot prematurely end another configured sibling's longer window");
        fork.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await fork.TickAt(400);
        Check(fork.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }), "the longer configured branch remains protected after the short branch window");
        fork.Reader.Value = Bar(effective: 15, canUse: true, last: 11, time: 1100);
        await fork.TickAt(800);
        Check(fork.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 15 }), "a later-opened configured long-window sibling is pressed directly");
    }

    public static async Task MonotonicDeadlineAsync()
    {
        var fixture = await WaitingFixture();
        var deadline = fixture.State.ChainTransition?.Deadline;
        fixture.Clock.JumpWallClock(TimeSpan.FromDays(3));
        await fixture.TickAt(fixture.Clock.Now.AddMilliseconds(80));
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }) && fixture.State.ChainTransition?.Deadline == deadline,
            "a forward wall-clock jump neither expires nor changes an80ms-old handoff");
        fixture.Clock.JumpWallClock(TimeSpan.FromDays(-6));
        await fixture.TickAt(fixture.Clock.Now.AddMilliseconds(1419));
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11 }),
            "a backward wall-clock jump cannot extend the handoff's monotonic1500ms limit");
        await fixture.TickAt(fixture.Clock.Now.AddMilliseconds(1));
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && fixture.State.ChainTransition is null,
            "root recovery follows elapsed monotonic time despite both wall-clock jumps");
    }

    public static async Task DifferentActualReleaseEndsProvisionalWaitAsync()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar();
        await fixture.TickAt(80);
        Check(fixture.State.ChainTransition is not null, "the provisional CD-only window must exist before another release is observed");
        fixture.Reader.Value = Bar(last: 21, time: 1200);
        await fixture.TickAt(160);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 31 }) && fixture.State.ChainTransition is null &&
              fixture.State.ActiveChainSource is null,
            "a different actual skill invalidates shared-CD provisional evidence without confirming the requested predecessor");
    }

    public static async Task LateParentActorDoesNotBreakChildHandoffAsync()
    {
        var fixture = await ChildPressedBeforeParentActor();
        fixture.AdvanceCooldown(12);
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await fixture.TickAt(240);
        var deadline = fixture.State.ChainTransition?.Deadline;
        Check(deadline.HasValue && fixture.State.ChainTransition?.Source.SkillId == 12 &&
              fixture.State.PendingAction?.Node.SkillId == 12 && fixture.State.ActiveChainSource?.SkillId != 12,
            "the late exact provisional parent record permits the child's own real-CD handoff without falsely confirming that child");
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 0, time: 1100);
        await fixture.TickAt(320);
        Check(fixture.State.PendingAction?.Node.SkillId == 12 && fixture.State.ActiveChainSource?.SkillId != 12 &&
              fixture.State.ChainTransition?.Deadline == deadline,
            "an official zero actor ID retaining only the late parent's release time does not falsely confirm the child's CD");
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 12, time: 1100);
        await fixture.TickAt(400);
        Check(fixture.State.PendingAction?.Node.SkillId == 12 && fixture.State.ActiveChainSource?.SkillId != 12 &&
              fixture.State.ChainTransition?.Deadline == deadline,
            "an early child ID retaining the known parent timestamp still does not confirm a new child release");
        fixture.Reader.Value = Bar(effective: 12, canUse: false, last: 12, time: 1200);
        await fixture.TickAt(480);
        Check(fixture.State.ActiveChainSource?.SkillId == 12 && fixture.State.ChainTransition?.Deadline == deadline,
            "the later actual child release confirms it without restarting its existing CD-observed window");
        fixture.Reader.Value = Bar(effective: 13, canUse: false, last: 12, time: 1200);
        await fixture.TickAt(560);
        fixture.Reader.Value = Bar(effective: 13, canUse: true, last: 12, time: 1200);
        await fixture.TickAt(640);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 13 }),
            "a delayed exact parent actor record never allows ordinary roots to interrupt the real next continuation");

        var wrongRank = await ChildPressedBeforeParentActor();
        wrongRank.AdvanceCooldown(12);
        wrongRank.Reader.Value = Bar(effective: 12, canUse: false, last: 111, time: 1100);
        await wrongRank.TickAt(240);
        Check(wrongRank.State.ChainTransition is null && wrongRank.State.ActiveChainSource?.SkillId != 12 &&
              wrongRank.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 31 }),
            "another rank or unrelated actual ID cannot use the exact provisional-parent exception to open a child window");

        var parentTwice = await ChildPressedBeforeParentActor();
        parentTwice.AdvanceCooldown(12);
        parentTwice.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await parentTwice.TickAt(240);
        Check(parentTwice.State.ChainTransition?.Source.SkillId == 12, "the first exact late-parent record is usable once");
        parentTwice.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1150);
        await parentTwice.TickAt(320);
        Check(parentTwice.State.ChainTransition is null && parentTwice.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 31 }),
            "a second new release time of the same parent is a different action, not a permanent exception");

        var parentAfterCd = await ChildPressedBeforeParentActor();
        parentAfterCd.AdvanceCooldown(12);
        parentAfterCd.Reader.Value = Bar(effective: 12, canUse: false);
        await parentAfterCd.TickAt(240);
        var afterCdDeadline = parentAfterCd.State.ChainTransition?.Deadline;
        Check(afterCdDeadline.HasValue, "the child's real CD can open its provisional handoff before either actor record changes");
        parentAfterCd.Reader.Value = Bar(effective: 12, canUse: false, last: 11, time: 1100);
        await parentAfterCd.TickAt(320);
        Check(parentAfterCd.State.ChainTransition?.Deadline == afterCdDeadline && parentAfterCd.State.PendingAction?.Node.SkillId == 12,
            "a parent record arriving after the child's CD preserves the existing child window without confirming it");
        parentAfterCd.Reader.Value = Bar(effective: 12, canUse: false, last: 12, time: 1200);
        await parentAfterCd.TickAt(400);
        parentAfterCd.Reader.Value = Bar(effective: 13, canUse: true, last: 12, time: 1200);
        await parentAfterCd.TickAt(640);
        Check(parentAfterCd.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12, 13 }),
            "both official CD/late-parent arrival orders preserve the genuine configured continuation");
    }

    private static async Task<Fixture> ChildPressedBeforeParentActor()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar();
        await fixture.TickAt(80);
        fixture.Reader.Value = Bar(effective: 12, canUse: true);
        await fixture.TickAt(160);
        Check(fixture.Keyboard.SkillIds.SequenceEqual(new uint[] { 11, 12 }),
            "an actual open child may take over while its CD-observed predecessor still has no actor confirmation");
        return fixture;
    }

    private static async Task<Fixture> WaitingFixture()
    {
        var fixture = new Fixture();
        await fixture.TickAt(0);
        fixture.AdvanceCooldown(11);
        fixture.Reader.Value = Bar(last: 11, time: 1100);
        await fixture.TickAt(80);
        Check(fixture.State.ChainTransition is not null, "lifecycle fixture must establish a real waiting chain");
        return fixture;
    }

    private static async Task ExpectCancellation(Func<Task<TimeSpan>> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("expected cancellation to propagate to the worker");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
        { SkillId = id, Name = "skill" + id, BaseName = "skill" + id, Children = children.ToList() };

    private static SkillSnapshot Skill(uint id, string? chain = null, string? pre = null) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 30000, 0,
            XmlChainCategory: chain, XmlPrechainCategory: pre);

    private static LockedTargetSnapshot Target() =>
        new(50, 100, 1, LockedTargetSnapshot.MonsterObjectType, "dummy", 100, 100, null, 1, Start);

    private static SkillAvailabilityCombatSnapshot Guard(ushort target = 50, uint server = 100, uint hp = 100) =>
        new(1, 10, target, server, hp, 100, 100, 100);

    private static SkillAvailabilitySnapshot Bar(uint effective = 11, bool canUse = false, uint last = 31, uint time = 1000) =>
        new(0,
            effective == 11 ? Array.Empty<SkillAvailabilitySlotSnapshot>() :
                new[] { new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 0, 21, 11, effective, canUse) },
            last, time,
            UnsupportedSkillIds: new uint[] { 11, 31 },
            BindingSlots: new SkillAvailabilityBindingSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11, effective),
                new(SkillQuickbar.Main, 1, 21, 31, 31)
            },
            CombatState: Guard());

    private sealed class Fixture
    {
        public readonly Dictionary<uint, SkillSnapshot> Skills = new()
        {
            [11] = Skill(11, "a"), [12] = Skill(12, "b", "a"),
            [13] = Skill(13, "c", "b"), [14] = Skill(14, pre: "c"), [31] = Skill(31)
        };
        public readonly QuickbarSkillPlan Plan;
        public readonly QuickbarSkillCombatState State = new();
        public readonly Reader Reader = new();
        public readonly Clock Clock = new();
        public readonly Keyboard Keyboard = new();
        public readonly List<uint[]> SkillReads = new();
        public bool ReturnEmptySkills;
        private readonly QuickbarSkillCombatController _controller;

        public Fixture(string? childChainTime = null, int? obsoleteConfigDelay = null, string? siblingChainTime = null)
        {
            if (childChainTime is not null) Skills[12] = Skills[12] with { XmlChainTime = childChainTime };
            if (siblingChainTime is not null) Skills[15] = Skill(15, pre: "a") with { XmlChainTime = siblingChainTime };
            var bindings = new QuickbarSnapshot(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 31)
            });
            var child = Node(12, Node(13, Node(14)));
            child.ChainTimeMs = obsoleteConfigDelay;
            var root = Node(11, child);
            if (siblingChainTime is not null) root.Children.Add(Node(15));
            Plan = QuickbarSkillPlan.FromSettings(new()
            {
                ExecutionTree = new() { root, Node(31) }
            }, new(bindings, Skills.Values.ToArray()));
            Keyboard.CurrentSkillId = () => State.PendingAction?.Node.SkillId ?? 0;
            _controller = new(Keyboard, Clock);
        }

        public void AdvanceCooldown(uint id) => Skills[id] = Skills[id] with { CooldownEndTime = 100000 + id };

        public Task<TimeSpan> TickAt(int elapsedMs, CancellationToken cancellationToken = default,
            Func<SkillAvailabilityCombatSnapshot, bool>? allowCombat = null) =>
            TickAt(Start.AddMilliseconds(elapsedMs), cancellationToken, allowCombat);

        public Task<TimeSpan> TickAt(DateTimeOffset at, CancellationToken cancellationToken = default,
            Func<SkillAvailabilityCombatSnapshot, bool>? allowCombat = null)
        {
            Clock.Set(at);
            return _controller.TickAsync(Plan, State, Target(), Reader, ReadSkills,
                new() { ConfirmTimeoutMs = 500, KeyHoldMs = 25 }, cancellationToken: cancellationToken,
                ordinaryReadiness: skills => skills.Select(skill => skill.SkillId).ToHashSet(),
                cooldownReadiness: skill => skill.CooldownEndTime == 0 ? SemiAutoSkillCooldownReadiness.Ready :
                    SemiAutoSkillCooldownReadiness.CoolingDown,
                allowCombatSnapshot: allowCombat);
        }

        private Task<IReadOnlyList<SkillSnapshot>> ReadSkills(IReadOnlyCollection<uint> ids)
        {
            SkillReads.Add(ids.ToArray());
            IReadOnlyList<SkillSnapshot> result = ReturnEmptySkills ? Array.Empty<SkillSnapshot>() :
                ids.Where(Skills.ContainsKey).Select(id => Skills[id]).ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Start;
        private long _stamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => _stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Set(DateTimeOffset value)
        {
            if (value < Now) throw new Exception("test clock cannot move backwards");
            _stamp += (value - Now).Ticks;
            Now = value;
        }
        public void JumpWallClock(TimeSpan delta) => Now += delta;
    }

    private sealed class Reader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Value = Bar();
        public int ReadCount;
        public Action<int>? OnRead;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
            long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ++ReadCount;
            OnRead?.Invoke(ReadCount);
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(ReadCount, Value));
        }
    }

    private sealed class Keyboard : IKeyboardInput
    {
        public readonly List<uint> SkillIds = new();
        public readonly List<string> Keys = new();
        public readonly List<TimeSpan> Holds = new();
        public Func<uint>? CurrentSkillId;
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            SkillIds.Add(CurrentSkillId?.Invoke() ?? 0);
            Holds.Add(holdDuration);
            return Task.FromResult(OperationResult.Ok());
        }
        public Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
