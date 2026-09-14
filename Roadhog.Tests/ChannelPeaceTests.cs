using Roadhog.Application;
using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class ChannelPeaceTests
{
    public static async Task FinishCurrentThenReservePeaceAsync()
    {
        var h = new Harness();
        h.Api.TargetEntityId = 100;
        h.Api.TargetOwnServerObjectId = 100;
        await h.Tick();
        Require(h.Combat.Fighting && h.Combat.CurrentTargetEntityId == 100, "deadline must preserve current locked fight");
        Require(h.Channel.WaitingForPeace && h.Executor.Requests.Count == 0, "must wait for current fight before switching");
        Require(!h.Combat.NextTargetPreAim.IsWorkerRunning, "must not pre-aim the next monster while draining combat");
        h.Api.TargetCurrentHp = 0;
        await h.Tick(20);
        Require(!h.Combat.Fighting, "current kill must finish through ordinary combat cleanup");
        await h.Tick(1);
        await h.Tick(14);
        Require(h.Executor.Requests.Count == 0 && h.Combat.CandidateEntityId == 0, "nearby monsters must not be acquired during fifteen second wait");
        Require(!h.Input.Keys.Contains("Tab"), "peace hold must not tab to another monster");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1 && !h.Channel.WaitingForPeace && !h.Combat.ChannelSwitchPending,
            "failed switch must release hold and permit normal work immediately");
        Require(h.NormalWorkAllowed == 1, "normal work is blocked only while reserving peace");
        await h.Tick(59);
        Require(!h.Channel.WaitingForPeace && h.NormalWorkAllowed == 2, "normal hunting continues until retry deadline");
        h.Api.TargetEntityId = 200;
        h.Api.TargetOwnServerObjectId = 200;
        h.Api.TargetCurrentHp = 1000;
        h.Combat.Fighting = true;
        h.Combat.SetCurrentTarget(200, 200);
        h.Combat.MarkCandidate(200, 200, DateTimeOffset.Now);
        await h.Tick(1);
        Require(h.Channel.WaitingForPeace && h.Executor.Requests.Count == 1, "deadline starts a new quiet window");
        Require(h.Combat.Fighting && h.Combat.CurrentTargetEntityId == 200, "retry deadline must finish the newly active fight");
        h.Api.TargetCurrentHp = 0;
        await h.Tick(10);
        await h.Tick(1);
        await h.Tick(15);
        Require(h.Executor.Requests.Count == 2, "second attempt follows a full reserved peace window");
    }

    public static async Task IncomingDefenseRestartsPeaceAsync()
    {
        var h = new Harness();
        await h.Tick();
        await h.Tick(14);
        h.Api.WorldObjects = new[] { Monster(200, true) };
        h.Api.TargetEntityId = 200;
        h.Api.TargetOwnServerObjectId = 200;
        h.Api.TargetIsTargetingLocalPlayer = true;
        await h.Tick(1);
        Require(h.Combat.CandidateEntityId == 200 || h.Combat.CurrentTargetEntityId == 200,
            "incoming attacker must be selected during the reserved window");
        Require(h.Executor.Requests.Count == 0, "incoming attacker must prevent channel submission");
        h.Api.TargetCurrentHp = 0;
        h.Api.WorldObjects = new[] { Monster(300, false) };
        await h.Tick(1);
        await h.Tick(1);
        await h.Tick(14);
        Require(h.Executor.Requests.Count == 0, "peace must restart after the defense fight ends");
        await h.Tick(1);
        Require(h.Executor.Requests.Count == 1, "recovered peace must eventually permit channel submission");
        Require(h.Combat.CandidateEntityId != 300, "nearby aggressive but non-attacking monsters must not replace defense target");
    }

    public static async Task ReleaseAndExclusiveWorkAsync()
    {
        var h = new Harness();
        h.Combat.IsMovingForward = true;
        h.Combat.IsRightMouseDown = true;
        h.Combat.PathCombat.Start("route", new[] { new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(10, 0, 0) }, 1);
        h.Combat.MarkCandidate(200, 200, DateTimeOffset.Now);
        await h.Tick();
        Require(h.Combat.CandidateEntityId == 0 && !h.Combat.IsMovingForward && !h.Combat.IsRightMouseDown,
            "unengaged candidate must not trap peace; movement must stop");
        Require(h.Combat.PathCombat.PointIndex == 1, "hold must preserve path progress");
        h.Settings.FixedChannelNumber = 0;
        await h.Tick(1);
        Require(!h.Combat.ChannelSwitchPending && h.NormalWorkAllowed == 1, "disabling fixed channel releases hold");
        h.Settings.FixedChannelNumber = 3;
        h.Combat.BagCleanup.StartDiscard(1, 2, 1);
        await h.Tick(1);
        Require(h.Combat.BagCleanup.Active && h.NormalWorkAllowed == 2, "active cleanup must continue through existing guarded flow");
        h.Combat.EnterDeathRecovery(DateTimeOffset.Now);
        var result = await h.Controller.TryTickChannelSwitchWaitAsync(h.Context, h.Plan, h.Semi, h.Combat);
        Require(result is null, "death recovery must never be blocked by peace reservation");
    }

    public static async Task WorkerActuallyHoldsAsync()
    {
        var h = new Harness();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(h.Context.Config, h.Api, h.Logger, new AccountRuntimeManager(h.Logger),
            new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(10) }, stop.Token);
        var semi = new SemiAutoCombatController(h.Input);
        var stationary = new StationaryCombatController(h.Input, semi);
        var worker = new DefaultAccountWorkerLoop(h.Input, semi, stationary,
            fixedChannel: new FixedChannelController(h.Executor, h.Clock));
        var task = worker.RunAsync(context);
        while (!h.Logger.Entries.Any(e => e.EventName == "fixed_channel.wait.peace") && !task.IsCompleted)
            await Task.Delay(20, stop.Token);
        Require(h.Logger.Entries.Any(e => e.EventName == "fixed_channel.wait.peace"), "real worker must enter hold branch");
        Require(!h.Logger.Entries.Any(e => e.EventName == "stationary_combat.target.selected"), "worker must not pass hold to ordinary target acquisition");
        stop.Cancel();
        try { await task; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Require(!h.Input.Keys.Contains("Tab"), "stopping during hold must not produce target input");
    }

    public static async Task WorkerDeathStillWinsAsync()
    {
        var h = new Harness();
        h.Api.Player = h.Api.Player with { CurrentHp = 0 };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(h.Context.Config, h.Api, h.Logger, new AccountRuntimeManager(h.Logger),
            new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(10) }, stop.Token);
        var semi = new SemiAutoCombatController(h.Input);
        var stationary = new StationaryCombatController(h.Input, semi);
        var worker = new DefaultAccountWorkerLoop(h.Input, semi, stationary,
            fixedChannel: new FixedChannelController(h.Executor, h.Clock));
        var task = worker.RunAsync(context);
        while (!h.Logger.Entries.Any(e => e.EventName == "player_life.death.detected") && !task.IsCompleted)
            await Task.Delay(20, stop.Token);
        stop.Cancel();
        try { await task; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Require(h.Logger.Entries.Any(e => e.EventName == "player_life.death.detected"), "life guard must process death with channel retry pending");
        Require(h.Executor.Requests.Count == 0 && !h.Logger.Entries.Any(e => e.EventName == "fixed_channel.wait.peace"),
            "dead player must neither switch channel nor enter the ordinary peace hold");
    }

    private static WorldObjectSnapshot Monster(ushort id, bool attacking) => new(id, id, "monster", "monster",
        new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, IsTargetingLocalPlayer: attacking,
        AggressiveKnown: true, IsAggressiveToPlayer: true);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Harness
    {
        internal ScriptSettings Settings = new()
        {
            FixedChannelNumber = 3, MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new CombatScriptSettings { EnableLoot = false, SmartPreAimEnabled = true,
                HasStationaryCombatPosition = true, StationaryCombatRadius = 60 },
            Skills = new SkillScriptSettings { Mode = SkillConfigurationMode.ManualMapping }
        };
        internal FakeGameApi Api = new()
        {
            Player = new PlayerSnapshot(1, 0, "test", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0, TargetOwnServerObjectId = 0, TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false, TargetPosition = new Vector3Snapshot(8, 0, 0),
            Channel = new ChannelSnapshot(1, 3, 100, DateTimeOffset.Now),
            WorldObjects = new[] { Monster(300, false) }
        };
        internal InMemoryRoadhogLogger Logger = new();
        internal RecordingKeyboardInput Input = new();
        internal ManualTimeProvider Clock = new();
        internal FixedChannelState Channel = new();
        internal StationaryCombatState Combat = new();
        internal SemiAutoCombatState Semi = new();
        internal RecordingFixedChannelSwitchExecutor Executor = new() { Result = OperationResult.Fail("cooldown") };
        internal StationaryCombatController Controller;
        internal AccountWorkerContext Context;
        internal SemiAutoSkillPlan Plan;
        internal int NormalWorkAllowed;
        private readonly FixedChannelController _channels;
        internal Harness()
        {
            Controller = new(Input, new SemiAutoCombatController(Input));
            Context = new(new AccountConfig { AccountName = "test", ScriptSettings = Settings }, Api, Logger,
                new AccountRuntimeManager(Logger), new AccountWorkerOptions(), CancellationToken.None);
            Plan = SemiAutoSkillPlan.FromSettings(Settings.Skills);
            _channels = new(Executor, Clock);
        }
        internal async Task Tick(double seconds = 0)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            await _channels.TickAsync(Context, Settings, Channel, Combat, () => Controller.PrepareForChannelSwitchAttemptAsync(Context, Semi, Combat));
            await Controller.SetChannelSwitchPendingAsync(Context, Combat, Channel.WaitingForPeace);
            if (await Controller.TryTickChannelSwitchWaitAsync(Context, Plan, Semi, Combat) is null) NormalWorkAllowed++;
        }
    }
}
