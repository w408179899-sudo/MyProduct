using Roadhog.Application.StationaryCombat;
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
