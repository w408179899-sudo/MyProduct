using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static partial class TownReturnTests
{
    public static async Task GlobalRecoverySettingsApplyToActiveRecoveryAsync()
    {
        var settings = new RoadhogGlobalSettings { NoKillReturnMinutes = 30, ReviveDelaySeconds = 45 };
        using var walking = new Harness(globalSettings: () => settings);
        EnterDeathRevivePath(walking, DateTimeOffset.Now.AddMinutes(-21));
        await TickReviveLifeGuardAsync(walking);
        Check(walking.State.IsMovingForward && !walking.Input.Keys.Contains("F8"),
            "the globally configured 30 minute threshold keeps the current route running at 21 minutes");

        settings = settings with { NoKillReturnMinutes = 20 };
        await TickReviveLifeGuardAsync(walking);
        Check(walking.Input.Keys.Count(key => key == "F8") == 1 &&
            walking.State.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle,
            "the active route uses the new threshold without resetting the elapsed time");

        using var dead = new Harness(globalSettings: () => settings);
        var deathTime = DateTimeOffset.Now.AddSeconds(-15);
        dead.Api.Player = Scene(50, hp: 0).Player!;
        dead.Api.TransitionRead = () => Scene(50, hp: 0);
        dead.State.EnterDeathRecovery(deathTime);
        dead.State.DeathRecovery.Advance(deathTime);
        await TickReviveLifeGuardAsync(dead);
        Check(dead.State.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick &&
            dead.Logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.wait_before_click" &&
                Equals(entry.Fields["waitMs"], 45000L)), "an account uses the full configured wait before clicking revive");

        settings = settings with { ReviveDelaySeconds = 10 };
        await TickReviveLifeGuardAsync(dead);
        Check(dead.State.DeathRecovery.Step is StationaryCombatDeathRecoveryStep.ClickRevive or StationaryCombatDeathRecoveryStep.WaitAlive,
            "the existing death-recovery state proceeds once the updated wait has elapsed");
    }

    public static async Task NoKillDuringDeathRevivePathAsync()
    {
        foreach (var maintenanceBlocked in new[] { false, true })
        {
            using var h = new Harness();
            var now = DateTimeOffset.Now;
            EnterDeathRevivePath(h, now.AddMinutes(-19));
            if (maintenanceBlocked)
            {
                var settings = h.Context.Config.ScriptSettings!;
                settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
                settings.Skills.Spiritmaster.PetHpMaintenanceRules.Add(new() { Key = "F7", SkillId = 1785 });
                settings.Maintenance.MpMaintenanceRules.Add(new() { Key = "F6", SkillId = 1370, BelowPercent = 90 });
                h.Api.Player = h.Api.Player with { CurrentMp = 40 };
                h.Api.Skills = [new(1370, "精气吸收 I", 1, 1, "精气吸收", 1, false, 1000, 0)];
                TestCooldownClock.Calibrate(h.SemiState);
                h.SemiState.BeginSpiritmasterPetHpIncreaseConfirmation(1785, "F7", 100, 25,
                    now.AddMinutes(-19), now.AddMinutes(-19), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 0);
            }

            await TickReviveLifeGuardAsync(h);
            Check(h.State.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.Inactive &&
                h.State.IsMovingForward != maintenanceBlocked && !h.Input.Keys.Contains("F8"),
                "before the no-kill deadline the revive route retains its existing movement or maintenance behavior");

            h.State.NoKillRecovery.ResetWatch(now.AddMinutes(-21));
            await TickReviveLifeGuardAsync(h);
            Check(h.State.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle &&
                h.State.TopLevelState == StationaryCombatTopLevelState.Normal &&
                h.State.DeathRecovery.RevivePathPoints.Count == 0 && !h.State.IsMovingForward &&
                h.SemiState.PendingSpiritmasterPetHpIncreaseConfirmation is null && h.Input.Keys.Count(k => k == "F8") == 1,
                "no-kill recall takes ownership from the old revive route and its maintenance after one input");
            var forwardPresses = h.Input.KeyDowns.Count(k => k == "W");
            h.Api.TransitionRead = Loading;
            await h.Tick();
            h.Api.TransitionRead = () => Scene(0, 9);
            await h.Tick();
            Check(!h.State.StartupRecoveryActive && h.Input.KeyDowns.Count(k => k == "W") == forwardPresses &&
                h.Input.Keys.Count(k => k == "F8") == 1, "loading and wrong-map arrival retain one recall without walking");

            h.Api.Player = Scene(0).Player!;
            h.Api.TransitionRead = () => Scene(0);
            await h.Tick();
            Check(h.State.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.FollowRevivePath &&
                h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0 &&
                h.Input.KeyDowns.Count(k => k == "W") == forwardPresses, "verified landing attaches at the first point");
            await h.Tick();
            Check(h.State.StartupRecoveryPointIndex == 1, "the restarted route walks from the first point");

            h.Api.Player = Scene(100).Player!;
            await h.Tick();
            await h.Tick();
            Check(!h.State.NoKillRecovery.Active && !h.State.StartupRecoveryActive && h.Input.Keys.Count(k => k == "F8") == 1,
                "finishing the route resets the no-kill timer and resumes normal work");
        }
    }

    public static async Task NoKillDuringDeathRevivePathGuardsAsync()
    {
        using (var recentKill = new Harness())
        {
            EnterDeathRevivePath(recentKill, DateTimeOffset.Now.AddMinutes(-21));
            recentKill.Context.RuntimeStates.MarkKill(recentKill.Context.Config.AccountName, 123, 456, DateTimeOffset.Now);
            await TickReviveLifeGuardAsync(recentKill);
            Check(!recentKill.Input.Keys.Contains("F8") && recentKill.State.IsMovingForward,
                "a recent kill counts during revive travel and the current route continues");
        }

        using (var dead = new Harness())
        {
            EnterDeathRevivePath(dead, DateTimeOffset.Now.AddMinutes(-21));
            dead.Api.TransitionRead = () => Scene(50, hp: 0);
            await TickReviveLifeGuardAsync(dead);
            Check(!dead.Input.Keys.Contains("F8") && !dead.Input.KeyDowns.Contains("W") &&
                dead.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery,
                "death in the departure snapshot prevents both recall and movement");
        }

        using (var rejected = new Harness())
        {
            EnterDeathRevivePath(rejected, DateTimeOffset.Now.AddMinutes(-21));
            rejected.Input.PressResult = key => key == "F8" ? OperationResult.Fail("test input rejected") : OperationResult.Ok();
            await TickReviveLifeGuardAsync(rejected);
            Check(rejected.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
                rejected.State.DeathRecovery.RevivePathPointIndex == 1 && !rejected.State.NoKillRecovery.Active &&
                rejected.State.NoKillRecovery.RetryNotBefore > DateTimeOffset.Now, "a rejected recall keeps the old route and backs off");
            await TickReviveLifeGuardAsync(rejected);
            Check(rejected.State.IsMovingForward && rejected.Input.Keys.Count(k => k == "F8") == 1,
                "the existing revive route resumes during retry backoff without another recall");
        }
    }

    public static async Task DeathResetsPetHealBeforeRevivePathAsync()
    {
        using var h = new Harness();
        var settings = h.Context.Config.ScriptSettings!;
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.Skills.Spiritmaster.PetHpMaintenanceRules.Add(new() { Key = "F7", SkillId = 1785 });
        settings.Maintenance.SitMpRecoverToPercent = 30;
        settings.Maintenance.MpMaintenanceRules.Add(new() { Key = "F6", SkillId = 1370, BelowPercent = 90 });
        var mpSkill = new SkillSnapshot(1370, "MP recovery", 1, 1, "MP recovery", 1, false, 1000, 0);
        TestCooldownClock.Calibrate(h.SemiState);
        Check(h.Context.CleanupRequests.Request(settings, manual: false, groceryTrigger: GroceryShopTrigger.Scheduled).Success,
            "scheduled cleanup should be queued before death");
        var scheduled = h.Context.CleanupRequests.Current;

        h.Input.AfterPress = key =>
        {
            if (key != "F6") return;
            h.Api.Skills = [mpSkill with { CooldownEndTime = TestCooldownClock.CurrentTick + mpSkill.CooldownDuration }];
            h.Api.Player = h.Api.Player with { CurrentMp = 100 };
        };

        var petRoster = h.Api.SummonedPetRoster;
        for (uint life = 1; life <= 2; life++)
        {
            var now = DateTimeOffset.Now;
            h.SemiState.BeginSpiritmasterPetHpIncreaseConfirmation(1785, "F7", life, 25, now, now,
                TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 0);
            h.SemiState.BeginSpiritmasterSummonVerification(now, TimeSpan.FromMinutes(1));
            h.Api.Player = Scene(30, hp: 0).Player! with { CharacterClassId = AionClassId.Spiritmaster };
            h.Api.TransitionRead = () => Scene(30, hp: 0);
            var forwardPresses = h.Input.KeyDowns.Count(key => key == "W");
            // A second death while following the route first returns to the stop-input node.
            await TickReviveLifeGuardAsync(h);
            await TickReviveLifeGuardAsync(h);
            Check(!h.State.IsMovingForward && h.Input.KeyDowns.Count(key => key == "W") == forwardPresses &&
                h.Input.Keys.Count(key => key == "F6") == life - 1, "death stops travel and maintenance inputs");
            Check(h.SemiState.PendingSpiritmasterPetHpIncreaseConfirmation is null &&
                !h.SemiState.HasPendingSpiritmasterSummonVerification, "death clears pet confirmation before the player revives");

            var newPetId = life + 1;
            h.Api.SummonedPetRoster = petRoster with
            {
                LocalServerObjectId = 77,
                LocalLinkedPetServerObjectId = newPetId,
                LocalPlayerPet = petRoster.LocalPlayerPet with
                {
                    OwnerServerObjectId = 77,
                    Pet = petRoster.LocalPlayerPet.Pet with
                    {
                        IsSummoned = true, CurrentHp = 100, MaxHp = 100, HpPercent = 100, EntityId = 8,
                        ServerObjectId = newPetId, LocalServerObjectId = 77, LocalLinkedPetServerObjectId = newPetId,
                        OwnerConfirmed = true, HealthFields = new(true, true, true)
                    }
                }
            };
            h.Api.Player = Scene(0).Player! with { CharacterClassId = AionClassId.Spiritmaster, CurrentMp = 40 };
            h.Api.TransitionRead = () => Scene(0);
            h.Api.Skills = [mpSkill];
            await TickReviveLifeGuardAsync(h);
            Check(h.Input.Keys.Count(key => key == "F6") == life && !h.Input.Keys.Contains("F7"),
                "the previous pet heal must not block the restored player's eligible MP key");
            await TickReviveLifeGuardAsync(h);
            Check(h.State.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.FollowRevivePath &&
                h.State.DeathRecovery.RevivePathName == h.Path.Name && h.Input.KeyDowns.Count(key => key == "W") > forwardPresses,
                "death recovery selects the configured route and resumes movement after MP recovery");
            Check(ReferenceEquals(h.Context.CleanupRequests.Current, scheduled), "the same scheduled cleanup remains queued");
        }
    }

    public static async Task FastRevivalClearsPreviousCombatAsync()
    {
        using var h = new Harness();
        await h.Tick();
        Check(h.State.StartupTownReturnPending, "startup recall is waiting for arrival");

        var now = DateTimeOffset.Now;
        var target = new LockedTargetSnapshot(17, 43, 0, LockedTargetSnapshot.MonsterObjectType, "previous target", 100, 100,
            new(1000, 0, 0), 3, now);
        var first = new SemiAutoSkillNode(100, "first", "first", "", 1000, "F3");
        var follow = new SemiAutoSkillNode(101, "follow", "follow", "", 1000, "F4", first);
        var cooling = new SkillSnapshot(100, "first", 1, 1, "first", 1, false, 30_000, TestCooldownClock.CurrentTick + 30_000);
        var attempted = new SkillSnapshot(101, "follow", 1, 1, "follow", 1, false, 1000, 0);
        TestCooldownClock.Calibrate(h.SemiState);
        var offsetBeforeDeath = h.SemiState.CooldownTickOffsetMs;
        h.SemiState.TryUpdateCooldownTickCalibration([cooling], unchecked((uint)Environment.TickCount64), now, out _);
        h.SemiState.ObserveTarget(target, out _, out _);
        h.SemiState.MarkOpeningAttackKeyAttempted(target);
        h.SemiState.MarkOpeningSkillPressed(attempted);
        h.SemiState.MarkSkillPressed(attempted, now.AddMinutes(1), retryKey: "F4");
        h.SemiState.StartPendingChainAdvance(first, follow, now.AddMinutes(1), cooling.CooldownEndTime);
        h.SemiState.MarkSpiritmasterPetBuffAttempt(attempted, 52, TimeProvider.System,
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), inputAccepted: true);
        h.SemiState.StartMaintenanceRest(forHp: false, forMp: true);

        h.Api.TransitionRead = () => Scene(1000, hp: 0);
        await h.Tick();
        Check(h.State.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.StopInput,
            "death during recall queues the shared stop-input step");
        h.Api.Player = Scene(0).Player!;
        await h.Controller.TickPlayerLifeGuardAsync(h.Context, h.Plan, h.SemiState, h.State, followRevivePath: false, reviveOnly: true);

        Check(h.State.TopLevelState == StationaryCombatTopLevelState.Normal && !h.SemiState.HasChainWork &&
            !h.SemiState.OpeningSkillWasPressed && !h.SemiState.HasPressedSkillCooldownRetryKey() &&
            !h.SemiState.HasSpiritmasterPetBuffConfirmation && !h.SemiState.IsMaintenanceResting,
            "fast revival still clears unfinished actions from the previous life");
        Check(h.SemiState.ShouldPressOpeningAttackKey(target) &&
            !h.SemiState.ObserveTarget(target with { CurrentHp = 0 }, out _, out _),
            "a previously engaged target may be opened on again and its old death gives no kill credit");
        Check(h.SemiState.CooldownTickOffsetMs == offsetBeforeDeath &&
            h.SemiState.GetEffectiveCooldownEndTime(cooling with { CooldownEndTime = 0 }, TestCooldownClock.CurrentTick, 0) == cooling.CooldownEndTime,
            "cooldown calibration and the observed game cooldown survive death");
    }

    private static void EnterDeathRevivePath(Harness h, DateTimeOffset startedAt)
    {
        h.Api.Player = Scene(50).Player!;
        h.Api.TransitionRead = () => Scene(50);
        h.State.EnterDeathRecovery(startedAt);
        while (h.State.DeathRecovery.Step != StationaryCombatDeathRecoveryStep.FollowRevivePath)
            h.State.DeathRecovery.Advance(startedAt);
        h.State.DeathRecovery.RevivePathName = h.Path.Name;
        h.State.DeathRecovery.RevivePathPoints = h.Path.Points.Select(point => point.ToVector3()).ToArray();
        h.State.DeathRecovery.RevivePathPointIndex = 1;
    }

    private static Task TickReviveLifeGuardAsync(Harness h) =>
        h.Controller.TickPlayerLifeGuardAsync(h.Context, h.Plan, h.SemiState, h.State, followRevivePath: true);
}
