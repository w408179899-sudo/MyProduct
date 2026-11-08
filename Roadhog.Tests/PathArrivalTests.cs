using System.Collections.Concurrent;
using System.Reflection;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static class PathArrivalTests
{
    public static async Task StopsWithoutForegroundTickAsync()
    {
        foreach (var death in new[] { false, true })
        using (var h = new Harness(death))
        {
            await h.Tick();
            Check(h.State.IsMovingForward && h.Input.Downs == 1, "route starts moving");
            h.Move(18.9f);
            await h.Polls(3);
            Check(h.Input.Ups == 0 && h.State.IsMovingForward, "outside configured one-meter precision must keep W");
            h.Move(19, noYaw: true);
            await Until(() => !h.State.IsMovingForward);
            Check(h.Input.Ups == 1 && h.PointIndex == 1, "coordinates alone stop W at the boundary without a foreground tick");
            Check(h.Input.OtherActions == 1, "arrival releases only W, retaining the existing right mouse hold");
            await h.Polls(3);
            Check(h.Input.Ups == 1 && h.Input.Downs == 1, "arrival is idempotent and cannot re-press W");
            h.Move(19);
            await h.Tick();
            Check(h.PointIndex == 2 && h.State.IsMovingForward && h.Input.Downs == 2,
                "foreground advances the route and starts the next point normally");
            Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.input.w_up" &&
                Equals(e.Fields["source"], "path_arrival")), "timely release is diagnosable");
        }
    }

    public static async Task ArrivalDuringKeyDownAsync()
    {
        using var h = new Harness();
        var entered = Signal();
        var finishDown = Signal();
        h.Input.DownWork = async () => { entered.TrySetResult(); await finishDown.Task; };
        var tick = h.Tick();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            h.Move(19);
            await Until(() => Property<bool>(h.State.PathFollowPoller!, "ArrivalObserved"));
            Check(h.Input.Ups == 0, "W-up waits for the in-flight W-down to finish");
        }
        finally { finishDown.TrySetResult(); }
        await tick;
        await Until(() => !h.State.IsMovingForward);
        Check(h.Input.Events.SequenceEqual(new[] { "down", "up" }), "arrival wins after the pending press, with no late W-down");
    }

    public static async Task RetargetRejectsOldSignalsAsync()
    {
        using var h = new Harness();
        await h.Tick();
        var oldPoller = h.State.PathFollowPoller!;
        var oldVersion = Property<long>(oldPoller, "TargetVersion");
        h.Move(19);
        await Until(() => !h.State.IsMovingForward);
        await h.Tick();
        Check(h.State.IsMovingForward && h.PointIndex == 2, "second segment is moving");
        var ups = h.Input.Ups;
        await Invoke(h, "StopArrivedPathForwardAsync", h.Context, h.State, oldPoller, oldVersion);
        await Invoke(h, "EnsureMoveForwardAsync", h.Context, h.State, oldPoller, (long?)oldVersion);
        Check(h.Input.Ups == ups && h.State.IsMovingForward, "old target version cannot release the next segment");

        // Simulate a stop/defense handoff, then returning to the route with a new poller.
        await h.Controller.PrepareForChannelSwitchAttemptAsync(h.Context, h.SemiState, h.State, "test_handoff");
        Check(!h.State.IsMovingForward && h.State.PathFollowPoller is null, "handoff invalidates the old route");
        await h.Tick();
        Check(h.State.IsMovingForward && !ReferenceEquals(oldPoller, h.State.PathFollowPoller), "resumed route owns a new poller");
        ups = h.Input.Ups;
        try { await Invoke(h, "StopArrivedPathForwardAsync", h.Context, h.State, oldPoller, oldVersion); }
        catch (OperationCanceledException) { }
        Check(h.Input.Ups == ups && h.State.IsMovingForward, "abandoned poller cannot release resumed movement");
    }

    public static async Task ArrivalDuringCameraAdjustmentAsync()
    {
        using var h = new Harness();
        await h.Tick();
        var turning = Signal();
        var finishTurn = Signal();
        h.Input.MoveWork = async () => { turning.TrySetResult(); await finishTurn.Task; };
        h.Api.Player = h.Api.Player with { CameraYawDegrees = 80, ActorYawDegrees = 80 };
        var tick = h.Tick();
        try
        {
            await turning.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(h.State.IsMovingForward, "small camera correction keeps forward held before arrival");
            h.Move(19);
            await Until(() => !h.State.IsMovingForward);
            Check(!tick.IsCompleted, "arrival releases W even while the foreground camera action is blocked");
        }
        finally { finishTurn.TrySetResult(); }
        await tick;
        Check(h.Input.Downs == 1 && h.Input.Ups == 1 && !h.State.IsMovingForward,
            "finishing an old camera correction cannot press W again after arrival");
    }

    public static async Task ReleaseFailureRetriesAsync()
    {
        foreach (var throws in new[] { false, true })
        using (var h = new Harness())
        {
            var attempts = 0;
            h.Input.UpWork = () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    if (throws) throw new InvalidOperationException("simulated input failure");
                    return Task.FromResult(OperationResult.Fail("simulated input failure"));
                }
                return Task.FromResult(OperationResult.Ok());
            };
            await h.Tick();
            h.Move(19);
            await Until(() => !h.State.IsMovingForward);
            Check(attempts == 2 && h.Input.Downs == 1 && h.Input.Ups == 1,
                "failed release keeps arrival latched and retries instead of claiming the key is up");
        }
    }

    public static async Task CancellationAndIsolationAsync()
    {
        using var first = new Harness();
        using var second = new Harness();
        await first.Tick(); await second.Tick();
        first.Move(19);
        await Until(() => !first.State.IsMovingForward);
        Check(second.State.IsMovingForward && second.Input.Ups == 0, "arrival is isolated to its worker");
        var poller = second.State.PathFollowPoller!;
        second.Controller.StopWorkerBackgroundWork(second.Context, second.State);
        second.Stop.Cancel();
        second.Move(19);
        await Property<Task>(poller, "Task").WaitAsync(TimeSpan.FromSeconds(3));
        Check(second.Input.Ups == 0, "abandoned poller submits no late input; worker cleanup owns final release");
        var worker = new DefaultAccountWorkerLoop(second.Input, second.SemiAuto, second.Controller);
        var release = typeof(DefaultAccountWorkerLoop).GetMethod("ReleaseActiveInputAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)release.Invoke(worker, new object[] { second.Context, second.State })!;
        Check(second.Input.Ups == 1 && !second.State.IsMovingForward, "worker cancellation still releases held W");
    }

    public static async Task PollingExceptionRetriesWithoutForegroundTickAsync()
    {
        using var h = new Harness(injectReadFaults: true);
        await h.Tick();
        var poller = h.State.PathFollowPoller!;
        Volatile.Write(ref h.ReadFaults!.Remaining, 3);
        await Until(() => h.Logger.Entries.Count(e => e.EventName == "stationary_combat.path_poll.retry") >= 3);
        Check(!Property<Task>(poller, "Task").IsCompleted && h.State.IsMovingForward,
            "repeated observation exceptions keep the arrival worker alive without inventing an arrival");
        h.Move(19);
        await Until(() => !h.State.IsMovingForward);
        Check(h.Input.Downs == 1 && h.Input.Ups == 1 && h.PointIndex == 1,
            "recovered poller releases W at the original threshold without needing another foreground tick");
        h.Controller.StopWorkerBackgroundWork(h.Context, h.State);
        await Property<Task>(poller, "Task").WaitAsync(TimeSpan.FromSeconds(3));
        Check(Property<Task>(poller, "Task").IsCompletedSuccessfully, "cancellation ends recovered polling without an unobserved exception");
    }

    public class FaultingSnapshots : DispatchProxy
    {
        public IRoadhogSnapshotReader Inner = null!;
        public int Remaining;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IRoadhogSnapshotReader.ReadPlayerAsync) &&
                Interlocked.Decrement(ref Remaining) >= 0)
                return Task.FromException<PublishedGameSnapshot<PlayerSnapshot>>(new IOException("simulated polling failure"));
            try { return method.Invoke(Inner, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
    }

    private sealed class FaultingFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        public FaultingSnapshots Reader = null!;
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            var proxy = DispatchProxy.Create<IRoadhogSnapshotReader, FaultingSnapshots>();
            Reader = (FaultingSnapshots)proxy;
            Reader.Inner = ((IRoadhogSnapshotReaderFactory)api).Create(config, logger, cancellationToken);
            return proxy;
        }
    }

    private static async Task Invoke(Harness h, string method, params object?[] args) =>
        await (Task)typeof(StationaryCombatController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Controller, args)!;
    private static T Property<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Harness : IDisposable
    {
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(20));
        public readonly ArrivalInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly FakeGameApi Api = new()
        {
            Player = new(1, 100, "arrival", 100, 100, 100, 100, 0, new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0, WorldObjects = Array.Empty<WorldObjectSnapshot>(), Skills = Array.Empty<SkillSnapshot>()
        };
        public readonly StationaryCombatState State = new();
        public readonly SemiAutoCombatState SemiState = new();
        public readonly SemiAutoCombatController SemiAuto;
        public readonly StationaryCombatController Controller;
        public readonly AccountWorkerContext Context;
        public readonly FaultingSnapshots? ReadFaults;
        private readonly SemiAutoSkillPlan _plan;
        private readonly bool _death;
        private readonly string? _bearing = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
        public int PointIndex => _death ? State.DeathRecovery.RevivePathPointIndex : State.StartupRecoveryPointIndex;
        public Harness(bool death = false, bool injectReadFaults = false)
        {
            _death = death;
            Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
            var points = new[] { new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(20, 0, 0), new Vector3Snapshot(40, 0, 0) };
            var settings = new ScriptSettings
            {
                MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
                Paths = new() { RevivePathName = "arrival" },
                Combat = new() { PathFollowReachDistance = 1, StationaryCombatRadius = 5, ReturnHomeWhenNoTarget = false },
                Maintenance = new() { SitMaintenanceEnabled = false }
            };
            var path = new SharedPathDocument { Name = "arrival", Points = points.Select(p => new SharedPathPoint { X = p.X, Y = p.Y, Z = p.Z }).ToList() };
            SemiAuto = new(Input);
            Controller = new(Input, SemiAuto, new InMemorySharedPathStore(path));
            var faulting = injectReadFaults ? new FaultingFactory(Api) : null;
            Context = new(new AccountConfig { AccountName = "arrival", ScriptSettings = settings }, (IRoadhogSnapshotReaderFactory?)faulting ?? Api, Logger,
                new AccountRuntimeManager(Logger), new(), Stop.Token) { WorkflowOwnsCleanup = true };
            ReadFaults = faulting?.Reader;
            _plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
            if (death)
            {
                State.EnterDeathRecovery(DateTimeOffset.Now);
                while (State.DeathRecovery.Step != StationaryCombatDeathRecoveryStep.FollowRevivePath)
                    State.DeathRecovery.Advance(DateTimeOffset.Now);
                State.DeathRecovery.RevivePathName = "arrival";
                State.DeathRecovery.RevivePathPoints = points;
                State.DeathRecovery.RevivePathPointIndex = 1;
            }
            else State.StartStartupRecovery("arrival", points, 1);
        }
        public Task Tick() => Controller.TickAsync(Context, _plan, SemiState, State);
        public void Move(float x, bool noYaw = false) => Api.Player = Api.Player with
        { Position = new(x, 0, 0), CameraYawDegrees = noYaw ? null : 90, ActorYawDegrees = noYaw ? null : 90, CapturedAt = DateTimeOffset.Now };
        public Task Polls(int count)
        {
            var reads = Api.PlayerReadCount;
            return Until(() => Api.PlayerReadCount >= reads + count);
        }
        public void Dispose()
        {
            Controller.StopWorkerBackgroundWork(Context, State);
            Stop.Cancel(); Stop.Dispose();
            Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", _bearing);
        }
    }

    private sealed class ArrivalInput : IKeyboardInput
    {
        public readonly ConcurrentQueue<string> Events = new();
        public Func<Task>? DownWork;
        public Func<Task>? MoveWork;
        public Func<Task<OperationResult>>? UpWork;
        public int Downs => Events.Count(e => e == "down");
        public int Ups => Events.Count(e => e == "up");
        public int OtherActions;
        public async Task<OperationResult> KeyDownAsync(string key, CancellationToken cancellationToken = default)
        {
            Check(key == "W", "only forward key expected");
            if (DownWork is not null) await DownWork();
            Events.Enqueue("down");
            return OperationResult.Ok();
        }
        public async Task<OperationResult> KeyUpAsync(string key, CancellationToken cancellationToken = default)
        {
            Check(key == "W", "only forward key expected");
            var result = UpWork is null ? OperationResult.Ok() : await UpWork();
            if (result.Success) Events.Enqueue("up");
            return result;
        }
        private Task<OperationResult> Other() { Interlocked.Increment(ref OtherActions); return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> PressKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken = default) => Other();
        public Task<OperationResult> MouseDownAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => Other();
        public Task<OperationResult> MouseUpAsync(RoadhogMouseButton button, CancellationToken cancellationToken = default) => Other();
        public async Task<OperationResult> MoveMouseRelativeAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default)
        {
            if (MoveWork is not null) await MoveWork();
            return await Other();
        }
        public Task<OperationResult> ScrollMouseAsync(int wheelDelta, CancellationToken cancellationToken = default) => Other();
    }
}
