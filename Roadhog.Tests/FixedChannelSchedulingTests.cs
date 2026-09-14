using Roadhog.Application;
using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class FixedChannelSchedulingTests
{
    public static async Task CompletionMarkerAsync()
    {
        var h = new Harness();
        h.Settings.FixedChannelNumber = 1;
        await h.Tick();
        Require(h.State.Completed && h.Api.ChannelReadCount == 1, "startup matching target must complete immediately");
        h.Api.Channel = h.Api.Channel with { Index = 1 };
        await h.Tick(600);
        Require(h.Api.ChannelReadCount == 1 && h.Prepared == 0, "completed run must not poll even after a manual change");
        h.Settings.FixedChannelNumber = 3;
        await h.Tick();
        Require(!h.State.Completed && h.Api.ChannelReadCount == 2, "changing target must re-read and reset completion");
        h.Executor.Result = OperationResult.Ok(); // Executor now returns success only after verified recovery.
        await h.Tick(15);
        Require(h.State.Completed && h.Executor.Requests.Count == 1, "verified switch must complete this run");
        var reads = h.Api.ChannelReadCount;
        await h.Tick(600);
        Require(h.Api.ChannelReadCount == reads && h.Executor.Requests.Count == 1, "no channel polling after verified success");
        h.State = new FixedChannelState();
        await h.Tick();
        Require(h.Api.ChannelReadCount == reads + 1 && !h.State.Completed, "every new worker run must check actual channel again");
        h.Settings.FixedChannelNumber = 0;
        await h.Tick();
        Require(!h.State.Completed && h.Api.ChannelReadCount == reads + 1, "disable must reset without reading");
    }

    public static async Task StartupAndRetryAsync()
    {
        var h = new Harness();
        await h.Tick();
        Require(h.Api.ChannelReadCount == 1 && h.Prepared == 0, "startup must read channel and continue normal work");
        await h.Tick(14);
        Require(h.Executor.Requests.Count == 0, "must wait fifteen seconds of peace");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1 && h.Prepared == 1, "first attempt at fifteen seconds without returning to town");
        var request = h.Executor.Requests[0];
        Require(request.ClickPoints.Count == 0, "automatic attempt uses data-driven controls");
        await h.Tick(59);
        Require(h.Executor.Requests.Count == 1 && h.Prepared == 1, "waiting must not suspend normal work");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1 && h.State.WaitingForPeace, "retry deadline must reserve a new quiet window");
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 2, "retry every sixty seconds while actual channel mismatches");
        h.Api.Channel = h.Api.Channel with { Index = 2 };
        await h.Tick(60);
        Require(h.State.Completed, "actual channel must confirm success at next retry check");
        await h.Tick(120);
        Require(h.Executor.Requests.Count == 2, "successful target must stop retries");
    }

    public static async Task CombatResetsPeaceAsync()
    {
        var h = new Harness();
        await h.Tick();
        await h.Tick(14);
        h.Combat.Fighting = true;
        await h.Tick(.1);
        h.Combat.Fighting = false;
        await h.Tick(.9);
        await h.Tick(14);
        Require(h.Executor.Requests.Count == 0, "short fight between channel polls must reset peace");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1, "new fifteen second peace may attempt");
        h.Combat.Fighting = true;
        await h.Tick(60);
        Require(h.Executor.Requests.Count == 1, "retry deadline never overrides combat");
        h.Combat.Fighting = false;
        await h.Tick(1);
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 2, "overdue retry runs after peace");
    }

    public static async Task IncomingAttackAndDamageAsync()
    {
        var h = new Harness();
        await h.Tick();
        h.Api.WorldObjects = new[] { new WorldObjectSnapshot(9, 99, "attacker", "monster", null, 5, 100, 100, IsTargetingLocalPlayer: true) };
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 0, "incoming attack must block even without adopted fight target");
        h.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        await h.Tick(1);
        h.Api.Player = h.Api.Player with { CurrentHp = 90 };
        await h.Tick(14);
        await h.Tick(1);
        await h.Tick(14);
        Require(h.Executor.Requests.Count == 0, "damage must restart fifteen second peace");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1, "must recover after damage ceases");
    }

    public static async Task DeadSelfTargetAsync()
    {
        var h = new Harness();
        h.Api.WorldObjects = new[] { new WorldObjectSnapshot(65526, 2235040718, "corpse", "monster", null, 81,
            CurrentHp: 0, MaxHp: 13315, TargetServerObjectId: 2235040718) };
        await h.Tick();
        Require(h.Prepared == 0, "startup with a corpse must return to normal work while observing peace");
        await h.Tick(15);
        Require(h.Prepared == 1, "confirmed corpse must not indefinitely block channel scheduling");
        h.Api.WorldObjects = new[] { new WorldObjectSnapshot(10, 1234, "attacker", "monster", null, 2,
            CurrentHp: 100, MaxHp: 100, IsTargetingLocalPlayer: true) };
        Require(!await h.Executor.Requests[0].CanUseMouseAsync!(h.Context.Snapshots),
            "new living attacker must still interrupt channel input");
    }

    public static async Task DisabledUnavailableAndDeathAsync()
    {
        var h = new Harness();
        h.Settings.FixedChannelNumber = 0;
        await h.Tick();
        Require(h.Api.ChannelReadCount == 0 && h.Api.PlayerReadCount == 0, "disabled needs no reads");
        h.Settings.FixedChannelNumber = 1;
        await h.Tick();
        Require(h.Api.ChannelReadCount == 1 && h.Api.PlayerReadCount == 0, "matching channel needs no extra combat scans");
        h.Settings.FixedChannelNumber = 5;
        await h.Tick(1);
        await h.Tick(60);
        Require(h.Prepared == 0, "unavailable target must not interrupt work");
        h.Settings.FixedChannelNumber = 3;
        h.Api.Player = h.Api.Player with { CurrentHp = 0 };
        await h.Tick(1);
        await h.Tick(60);
        Require(h.Prepared == 0, "death must continue life guard without switching");
    }

    public static async Task MapAndFailureAsync()
    {
        var h = new Harness();
        h.Executor.ExceptionToThrow = new InvalidOperationException("input failure");
        await h.Tick();
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 1, "exception must return normal work");
        h.Api.Channel = h.Api.Channel with { MapId = 200 };
        await h.Tick(1);
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 1, "map transition must not bypass one-minute cadence");
        h.Executor.ExceptionToThrow = null;
        await h.Tick(44);
        Require(h.Executor.Requests.Count == 1, "new map must restart peace when observed");
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 2 && h.Executor.Requests[1].MapId == 200, "retry should use new map without five minute wait");
        h.Combat.Fighting = true;
        Require(!await h.Executor.Requests[1].CanUseMouseAsync!(h.Context.Snapshots), "input guard must abort if combat resumes mid attempt");
    }

    public static async Task PreservesWorkAsync()
    {
        var h = new Harness();
        var keyboard = new RecordingKeyboardInput();
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var points = new[] { new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(10, 0, 0), new Vector3Snapshot(20, 0, 0) };
        h.Combat.PathCombat.Start("hunting", points, 1);
        h.Combat.StartStartupRecovery("revive", points, 1);
        h.Combat.IsMovingForward = true;
        h.Combat.IsRightMouseDown = true;
        await controller.PrepareForChannelSwitchAttemptAsync(h.Context, new SemiAutoCombatState(), h.Combat);
        Require(h.Combat.PathCombat.PointIndex == 1 && h.Combat.PathCombat.PathName == "hunting", "mouse preparation must preserve combat route progress");
        Require(h.Combat.StartupRecoveryPointIndex == 1, "mouse preparation must preserve startup route progress");
        Require(!h.Combat.IsMovingForward && !h.Combat.IsRightMouseDown, "must release held movement before UI clicks");
        h.Combat.BagCleanup.StartDiscard(1, 2, 1);
        await h.Tick();
        await h.Tick(15);
        Require(h.Prepared == 0 && h.Combat.BagCleanup.Active, "active cleanup must continue without being reset for a channel attempt");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Harness
    {
        internal FakeGameApi Api = new()
        {
            TargetEntityId = 0,
            Channel = new ChannelSnapshot(0, 3, 100, DateTimeOffset.Now),
            Player = new PlayerSnapshot(1, 0, "test", 100, 100, 100, 100, 0, new Vector3Snapshot(900, 900, 0), DateTimeOffset.Now)
        };
        internal ScriptSettings Settings = new() { FixedChannelNumber = 3 };
        internal FixedChannelState State = new();
        internal StationaryCombatState Combat = new();
        internal ManualTimeProvider Clock = new();
        internal RecordingFixedChannelSwitchExecutor Executor = new() { Result = OperationResult.Fail("server cooldown") };
        internal AccountWorkerContext Context;
        internal int Prepared;
        private readonly FixedChannelController _controller;
        internal Harness()
        {
            var logger = new InMemoryRoadhogLogger();
            Context = new(new AccountConfig { AccountName = "test", ScriptSettings = Settings }, Api, logger,
                new AccountRuntimeManager(logger), new AccountWorkerOptions(), CancellationToken.None);
            _controller = new(Executor, Clock);
        }
        internal async Task Tick(double advance = 0)
        {
            Clock.Advance(TimeSpan.FromSeconds(advance));
            var result = await _controller.TickAsync(Context, Settings, State, Combat, () => { Prepared++; return Task.CompletedTask; });
            Require(result is null, "every scheduling outcome must permit normal worker tick");
        }
    }
}
