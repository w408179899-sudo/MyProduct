using Roadhog.Application;
using Roadhog.Application.GameSessions;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static Task ScheduleInterruptsReviveTravelWorkerAsync() =>
        RunReviveScheduleScenariosAsync("relogin", "rejected_relogin", "skip", "manual_transfer");

    public static Task ScheduleInterruptsStalledRevivePursuitAsync() => RunReviveScheduleScenariosAsync("stuck_fight");
    public static Task ScheduleWaitsOutRealAttackersAsync() => RunReviveScheduleScenariosAsync("surrounded");
    public static Task ScheduleResumesInterruptedReturnAsync() => RunReviveScheduleScenariosAsync("return_interrupted");
    public static Task ScheduleResumesInterruptedNpcReturnAsync() => RunReviveScheduleScenariosAsync("npc_return_interrupted");
    public static Task ScheduleWaitsForRevivalAsync() => RunReviveScheduleScenariosAsync("dead_at_due");

    private static async Task RunReviveScheduleScenariosAsync(params string[] scenarios)
    {
        foreach (var scenario in scenarios)
        {
            var skipTrip = scenario == "skip";
            var manualTransfer = scenario == "manual_transfer";
            var npcReturn = scenario == "npc_return_interrupted";
            var interruptedReturn = npcReturn || scenario == "return_interrupted";
            var deadAtDue = scenario == "dead_at_due";
            using var trip = new Trip(expectedUnitPrice: manualTransfer ? 1UL : 500UL)
                { Now = At(11, 59), ExpectBagMouseReset = !manualTransfer };
            if (npcReturn) AddNpcStages(trip);
            trip.Stop.CancelAfter(TimeSpan.FromSeconds(40));
            var settings = trip.Config.ScriptSettings!;
            settings.MainMode = trip.Config.MainMode = AccountMainMode.CustomCombat;
            settings.CombatMode = trip.Config.CombatMode = AccountCombatMode.Stationary;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.Maintenance.BagCleanupEnabled = true;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "12:00" };
            settings.Paths.RevivePathName = "revive";
            settings.Paths.TownReturnKey = "OemPlus";
            if (manualTransfer)
            {
                settings.Paths.StallPathName = trip.Route.Name;
                settings.Paths.StallReturnItemName = settings.Paths.GroceryReturnItemName;
                settings.Maintenance.ItemTransferItemNameKeywords = ["goods"];
            }
            settings.Combat.PathFollowReachDistance = 1;
            settings.Combat.StationaryCombatRadius = 5;
            settings.Combat.ReturnHomeWhenNoTarget = false;
            trip.Api.Player = new(1, 100, "revive-schedule", 100, 100, 100, 100, 0,
                new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90);
            trip.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
            // The shop fixture models item clicks; revive navigation also holds RMB to turn.
            var itemDown = trip.Input.AfterMouseDown;
            var itemUp = trip.Input.AfterMouseUp;
            var cameraHeld = false;
            trip.Input.AfterMouseDown = button =>
            {
                if (button == RoadhogMouseButton.Right && !trip.BagOpen)
                { cameraHeld = true; return; }
                itemDown?.Invoke(button);
            };
            trip.Input.AfterMouseUp = button =>
            {
                if (button == RoadhogMouseButton.Right && cameraHeld)
                { cameraHeld = false; return; }
                itemUp?.Invoke(button);
            };
            if (skipTrip) settings.Maintenance.BagCleanupStallItems.Clear();
            var revive = new SharedPathDocument { Name = "revive", Points = new()
                { new() { X = 0 }, new() { X = 20 }, new() { X = 40 } } };
            var paths = new InMemorySharedPathStore(revive, trip.Route);
            var semi = new SemiAutoCombatController(trip.Input);
            var exitSession = new GameExitWorkerIntegrationTests.ExitSession();
            exitSession.OnAction = action =>
            {
                if (action == GameExitAction.OpenMenu)
                    exitSession.Scene = exitSession.Scene with { MenuOpen = true, CanQuit = true };
                if (action == GameExitAction.Quit)
                    exitSession.Scene = exitSession.Scene with { ProcessPresent = false };
            };
            trip.Input.AfterPress += key =>
            {
                if (key != "OemPlus") return;
                trip.Api.Channel = trip.Api.Channel with { MapId = 1 };
                trip.Api.Player = trip.Api.Player with { Position = new(0, 0, 0) };
            };
            var loop = new DefaultAccountWorkerLoop(trip.Input, semi,
                new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner(),
                gameExitSessionFactory: _ => exitSession);
            var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop,
                new() { TickInterval = TimeSpan.FromMilliseconds(5) });
            async Task Until(Func<bool> ready, string stage)
            {
                while (!ready())
                {
                    if (trip.Stop.IsCancellationRequested)
                        throw new Exception("Timed out waiting for " + stage + ": " + string.Join(" | ", trip.Logger.Entries.TakeLast(12)
                            .Select(e => e.EventName + " " + string.Join(",", e.Fields.Select(f => f.Key + "=" + f.Value)))));
                    Check(host.IsRunning, "worker remains active through revival and trading");
                    await Task.Delay(10);
                }
            }
            int Follows() => trip.Logger.Entries.Count(e => e.EventName == "stationary_combat.death_recovery.path_follow");
            try
            {
                Check(host.Start(trip.Config).Success, "start actual worker before scheduled time");
                await Until(() => trip.Success.Loads > 0, "initial schedule observation");
                trip.Api.Player = trip.Api.Player with { CurrentHp = 0 };
                await Until(() => trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.death_recovery.stop_input"), "death recovery entry");
                if (deadAtDue)
                {
                    trip.Now = At(12);
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.schedule.due"), "register schedule while dead");
                    Check(trip.ScrollClicks == 0 && !trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.revive_path.paused"),
                        "a dead character keeps the due slot without any recall input");
                }
                trip.Api.Player = trip.Api.Player with { CurrentHp = 100 };
                if (!deadAtDue) await Until(() => Follows() > 0, "revive route movement");
                if (manualTransfer)
                {
                    var startingGold = trip.Api.InventoryMoney;
                    Check(trip.Api.Player.IsAlive && trip.Api.Player.Position!.Value.X < revive.Points[^1].X,
                        "character is alive before the revive route endpoint");
                    Check(host.RequestCleanup(settings, itemTransfer: true).Success, "submit priority transfer while following revive route");
                    trip.Stop.CancelAfter(TimeSpan.FromSeconds(5));
                    await Until(() => host.StandaloneShopRestartRequestId.HasValue, "priority transfer sellout");
                    Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == 1 && trip.Api.InventoryMoney == startingGold + 3,
                        "confirmed recall, warehouse travel and fixed-price sale each run once");
                    Check(trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.revive_path.paused")
                          && !trip.Logger.Entries.Any(e => e.EventName is "stationary_combat.death_recovery.complete" or "grocery_shop.schedule.due")
                          && trip.Success.Saves == 0 && !trip.Input.Keys.Contains("OemPlus"),
                        "manual transfer preempts the live revive route without requiring its completion or a scheduled cleanup");
                    continue;
                }
                var scheduleLoads = trip.Success.Loads;
                if (!deadAtDue)
                {
                    trip.Api.InventoryCapacity = 2;
                    await Until(() => trip.Success.Loads >= scheduleLoads + 2, "pre-schedule checks during full-bag revive travel");
                    Check(trip.ScrollClicks == 0 && !trip.Logger.Entries.Any(e =>
                            e.EventName is "grocery_shop.schedule.due" or "cleanup_workflow.revive_path.paused"),
                        "a full backpack cannot interrupt revive travel before the scheduled time");
                    trip.Api.InventoryCapacity = 100;
                }
                var follows = Follows();
                const ushort attacker = 51;
                const uint attackerId = 501;
                void SetAttacker(bool attacking, float distance)
                {
                    trip.Api.TargetEntityId = attacker;
                    trip.Api.TargetOwnServerObjectId = attackerId;
                    trip.Api.TargetObjectType = LockedTargetSnapshot.MonsterObjectType;
                    trip.Api.TargetName = "first attacker";
                    trip.Api.TargetIsTargetingLocalPlayer = attacking;
                    trip.Api.WorldObjects = [new(attacker, attackerId, "first attacker", "monster", new(distance, 0, 0), distance,
                        CurrentHp: 100, MaxHp: 100, IsTargetingLocalPlayer: attacking)];
                }
                if (scenario is "stuck_fight" or "surrounded")
                {
                    SetAttacker(true, 5);
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "combat_flow.health.observed" &&
                        Equals(e.Fields["fighting"], true)), "lock a defense target during revive travel");
                }
                if (scenario == "stuck_fight") SetAttacker(false, 75);
                if (scenario == "surrounded")
                    trip.Api.WorldObjects = [.. trip.Api.WorldObjects, new(52, 502, "second attacker", "monster", new(6, 0, 0), 6,
                        CurrentHp: 100, MaxHp: 100, IsTargetingLocalPlayer: true)];
                if (interruptedReturn) trip.AfterScrollClick = () => trip.Scenario = "attack";
                trip.Now = At(12);
                if (scenario == "surrounded")
                {
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.schedule.due"), "register the slot during real combat");
                    SetAttacker(false, 5);
                    trip.Api.WorldObjects = [.. trip.Api.WorldObjects, new(52, 502, "second attacker", "monster", new(6, 0, 0), 6,
                        CurrentHp: 100, MaxHp: 100, IsTargetingLocalPlayer: true)];
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.recovery_defense.target_selected" &&
                        Equals(e.Fields["phase"], "scheduled_cleanup") && Equals(e.Fields["serverObjectId"], 502u)), "defend against the remaining attacker");
                    Check(trip.ScrollClicks == 0, "true attackers hold the recall even after the old lock is released");
                    trip.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
                    trip.Api.TargetEntityId = 0;
                    trip.Api.TargetIsTargetingLocalPlayer = false;
                }
                if (interruptedReturn)
                {
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.schedule.return_interrupted"), "retain a cast interrupted by combat");
                    Check(trip.ScrollClicks == 1 && trip.ShopStarts == 0 && trip.NpcBatches.Count == 0,
                        "no new recall or sale while under attack");
                    trip.Scenario = "success";
                    trip.Api.TargetEntityId = 0;
                    trip.Api.TargetIsTargetingLocalPlayer = false;
                    trip.Now = trip.Now.AddSeconds(11);
                }
                await Until(() => trip.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.revive_path.paused"), "scheduled revive route pause");
                Check(!trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.death_recovery.complete"),
                    "schedule does not require or falsely report completion of the revive route");
                if (skipTrip)
                {
                    await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.skipped") && Follows() > follows, "skipped trip resume");
                    Check(trip.ScrollClicks == 0 && !host.StandaloneShopRestartRequestId.HasValue,
                        "no-goods skip resumes recovery travel in the same worker without recall or restart");
                }
                else
                {
                    await Until(() => host.StandaloneShopRestartRequestId.HasValue, "scheduled sellout");
                    var expectedStalls = npcReturn ? 0 : 1;
                    Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.ShopStarts == expectedStalls && trip.Success.Saves == expectedStalls,
                        "scheduled recall and configured town workflow run exactly once before the revive route endpoint");
                    if (npcReturn)
                        Check(trip.NpcSubmittedItems.SelectMany(items => items).SequenceEqual(new ulong[] { 100, 101, 102 }),
                            "the interrupted initial recall reaches the NPC and submits each configured item once");
                    var dueSlots = trip.Logger.Entries.Where(e => e.EventName == "grocery_shop.schedule.due").ToArray();
                    Check(dueSlots.Length == 1, "expected one due for " + scenario + ", got " + string.Join(", ",
                        dueSlots.Select(e => "due=" + e.Fields["dueAt"] + " now=" + e.Fields["now"] + " start=" + e.Fields["sessionStartedAt"])));
                    if (scenario == "stuck_fight")
                        Check(trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.schedule.combat_released" &&
                            Equals(e.Fields["serverObjectId"], attackerId)), "preempt the live pursuit without waiting for the old lock to drop");
                    var followsAtSellout = Follows();
                    Check(trip.Api.Channel.MapId == trip.Route.MapId && !trip.Input.Keys.Contains("OemPlus"),
                        "sellout finishes on the stall map, away from the original revive route");
                    if (scenario == "relogin")
                    {
                        Check(host.RequestGameSession(GameSessionOperation.Relogin, allowScheduledGroceryHandoff: true).Success,
                            "the manager can request relogin after scheduled sellout");
                        await Until(() => host.CompletedGameSession is not null, "exit completion after scheduled sellout");
                        Check(exitSession.Actions.SequenceEqual(new[] { GameExitAction.OpenMenu, GameExitAction.Quit }) &&
                              !trip.Input.Keys.Contains("OemPlus"), "relogin reaches normal exit without recalling or following the old route");
                    }
                    else if (scenario == "rejected_relogin")
                    {
                        host.ResumeAfterRejectedGroceryRelogin(host.StandaloneShopRestartRequestId!.Value, "missing credentials");
                        await Until(() => trip.Logger.Entries.Any(e => e.EventName == "stationary_combat.startup_recovery.return.verify.ok"),
                            "fresh return after relogin is rejected");
                        Check(exitSession.Actions.Count == 0 && trip.Input.Keys.Count(key => key == "OemPlus") == 1 &&
                            trip.Api.Channel.MapId == 1, "a refused relogin recovers from the stall map without exiting");
                    }
                    Check(Follows() == followsAtSellout &&
                          trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.revive_path.discarded") == 1 &&
                          trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.schedule.due") == 1 && trip.ShopStarts == expectedStalls,
                        "the cancelled revive leg and the completed sale must not resume or replay");
                }
                Check((await host.StopAsync()).Success, "Stop still drains the scheduled worker");
            }
            finally { await host.StopAsync(); }
        }
    }
}
