using Roadhog.Application;
using Roadhog.Application.Radar;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Radar;

internal static class StationaryTabFacingTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task DetourInvalidatesPreAimAsync()
    {
        using var h = new Harness(radar: true);
        // Pre-aim had faced the candidate before navigation turned toward a waypoint.
        h.State.FacedCandidateEntityId = 100;
        var route = await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777,
            new(0, 0), new(20, 0), RadarNavigationPurpose.ApproachTarget, 9000,
            h.Settings.Combat.RadarObstacleAvoidance, 34);
        Check(route.Action == RadarNavigationAction.MoveToWaypoint, "fixture must require a detour");
        // The waypoint has been passed; the line is now clear but the camera still faces the waypoint.
        h.SetPlayer(new(12, 0, 0), 0);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        await h.Tick();
        Check(h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "must turn after the detour despite the old pre-aim marker");
        Check(!h.Input.Keys.Contains("Tab"), "unconfirmed camera correction must not send Tab");
        Check(h.State.FacedCandidateEntityId == 0, "failed recheck invalidates the old facing marker");
        Check(!h.State.Fighting, "must not enter combat without the intended lock");
    }

    public static async Task TurnsBeforeTabAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 0);
        h.State.FacedCandidateEntityId = 100;
        var moved = false;
        h.Input.AfterMove = (_, _) => { moved = true; h.SetPlayer(new(0, 0, 0), 90); };
        h.Input.AfterPress = key =>
        {
            if (key == "Tab") Check(moved && h.Api.Player.CameraYawDegrees == 90, "camera must align before Tab");
        };
        await h.Tick();
        Check(h.Input.Keys.Contains("Tab"), "confirmed correction should allow Tab in the same attempt");
        Check(h.State.FacedCandidateEntityId == 100, "confirmed facing refreshes the candidate marker");
    }

    public static async Task PendingRetryTracksCandidateAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 90);
        h.State.FacedCandidateEntityId = 100;
        h.LockWrongMonster();
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "fixture must first enter pending Tab verification: " +
            string.Join(" | ", h.Logger.Entries.Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
        Check(!h.State.Fighting, "wrong live monster must not be attacked");

        // The real candidate moves around the player; the wrong lock stays put. Follow only the candidate.
        h.Api.WorldObjects = new[] { h.Candidate with { Position = new(0, 20, 0) }, h.WrongMonster };
        h.State.LastTabAt = DateTimeOffset.MinValue;
        h.State.StartPendingTabVerification(h.Candidate, DateTimeOffset.Now.AddSeconds(-1), null);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "pending retry must not Tab before the moved candidate is faced");
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target" &&
            Equals(e.Fields["targetEntityId"], (ushort)100) && Convert.ToDouble(e.Fields["targetYaw"]) == 180),
            "retry must aim at the fresh candidate position, not the wrong locked monster: " +
            string.Join(" | ", h.Logger.Entries.Where(e => e.EventName.Contains("face")).Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
        Check(!h.State.Fighting, "wrong lock protection must survive reorientation");
        h.SetPlayer(new(0, 0, 0), 180);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 2, "retry resumes once the true candidate is aligned");
        Check(!h.State.Fighting, "an aligned retry still cannot attack a different live lock");
    }

    public static async Task AlignedDoesNotTurnAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 90);
        h.State.FacedCandidateEntityId = 100;
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "aligned acquisition should Tab immediately");
        Check(!h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "aligned acquisition must not move the camera");
    }

    public static async Task BlockedRadarDoesNotFaceOrTabAsync()
    {
        using var h = new Harness(radar: true);
        h.State.FacedCandidateEntityId = 100;
        h.SetPlayer(new(0, 0, 0), 0);
        // Simulate a stale direct commitment: the final strict radar check must take priority.
        await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777, new(0, 0), new(20, 0),
            RadarNavigationPurpose.ApproachTarget, 9000, h.Settings.Combat.RadarObstacleAvoidance, 34);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        await h.Tick();
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.tab.blocked_by_obstacle"), "strict radar guard must run");
        Check(!h.Input.Keys.Contains("Tab"), "blocked line must not Tab");
        Check(!h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target"), "do not turn toward the monster before the detour is resolved");
    }

    public static async Task MatchingLockSkipsTabFacingAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 0);
        h.Api.TargetEntityId = 100;
        h.Api.TargetOwnServerObjectId = 9000;
        h.Api.TargetCurrentHp = h.Api.TargetMaxHp = 100;
        h.Api.TargetPosition = h.Candidate.Position;
        await h.Tick();
        Check(h.State.Fighting, "matching lock should enter the existing combat flow");
        Check(!h.Input.Keys.Contains("Tab"), "matching lock needs no Tab");
        Check(!h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "the acquisition fix must not reorient an already locked fight");
    }

    private sealed class Harness : IDisposable
    {
        private readonly Dictionary<string, string?> _environment = new();
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        public readonly ScriptSettings Settings = new()
        {
            MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new() { HasStationaryCombatPosition = true, StationaryCombatRadius = 60, EnableLoot = false },
            Maintenance = new() { SitMaintenanceEnabled = false }
        };
        public readonly WorldObjectSnapshot Candidate = new(100, 9000, "candidate", "monster", new(20, 0, 0), 20, 100, 100);
        public readonly WorldObjectSnapshot WrongMonster = new(200, 8000, "wrong lock", "monster", new(10, 0, 0), 10, 100, 100);
        public readonly FakeGameApi Api;
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly StationaryCombatState State = new();
        public readonly SemiAutoCombatState SemiState = new();
        public readonly StationaryObstacleNavigator? Navigator;
        public readonly StationaryCombatController Controller;
        public readonly AccountWorkerContext Context;
        public Harness(bool radar = false)
        {
            SetEnvironment("AION_FACE_TARGET_BEARING_MODE", "y-x");
            foreach (var name in new[] { "SETTLE_MS", "MOUSE_DOWN_WARMUP_MS", "ADAPTIVE_READ_SETTLE_MS", "ADAPTIVE_READ_TIMEOUT_MS", "DRAG_STEP_DELAY_MS" })
                SetEnvironment("AION_FACE_TARGET_" + name, "0");
            SetEnvironment("AION_FACE_TARGET_TWO_PASS_MAX_PASSES", "1");
            Api = new()
            {
                Player = new(1, 0, "test", 100, 100, 100, 100, 0, new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
                TargetEntityId = 0, TargetCurrentHp = 0, TargetMaxHp = 0, TargetPosition = null,
                TargetServerObjectId = 0, TargetIsTargetingLocalPlayer = false,
                WorldObjects = new[] { Candidate }, Skills = Array.Empty<SkillSnapshot>(),
                Channel = new(0, 1, 777, DateTimeOffset.Now)
            };
            if (radar)
            {
                Settings.Combat.RadarObstacleAvoidance.Enabled = true;
                Navigator = new(new StaticRadarMapStore(new RadarMapDocument
                {
                    MapId = 777, Segments = new() { new() { Start = new(10, -8), End = new(10, 8) } }
                }), new RadarRoutePlanner(), new RadarMapRevisionRegistry());
            }
            Controller = new(Input, new SemiAutoCombatController(Input), obstacleNavigator: Navigator);
            Context = new(new() { AccountName = "tab-facing", ScriptSettings = Settings }, Api, Logger,
                new AccountRuntimeManager(Logger), new(), _stop.Token);
            State.MarkStartupRecoveryChecked();
            State.MarkCandidate(Candidate, DateTimeOffset.Now);
        }
        public void SetPlayer(Vector3Snapshot position, double yaw) => Api.Player = Api.Player with
        { Position = position, CameraYawDegrees = yaw, ActorYawDegrees = yaw, CapturedAt = DateTimeOffset.Now };
        public void LockWrongMonster()
        {
            Api.TargetEntityId = WrongMonster.EntityId;
            Api.TargetOwnServerObjectId = WrongMonster.ServerObjectId;
            Api.TargetCurrentHp = Api.TargetMaxHp = 100;
            Api.TargetPosition = WrongMonster.Position;
            Api.WorldObjects = new[] { Candidate, WrongMonster };
            State.SetCurrentTarget(Candidate);
        }
        public Task Tick() => Controller.TickAsync(Context, SemiAutoSkillPlan.FromSettings(Settings.Skills), SemiState, State);
        private void SetEnvironment(string name, string value)
        {
            _environment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose()
        {
            _stop.Cancel();
            Controller.StopWorkerBackgroundWork(Context, State);
            _stop.Dispose();
            foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
