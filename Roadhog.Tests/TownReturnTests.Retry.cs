using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static partial class TownReturnTests
{
    private sealed class ReturnClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    public static async Task RetryMinuteBoundaryAsync()
    {
        var clock = new ReturnClock();
        using var h = new Harness(clock);
        await h.Tick();
        clock.Advance(TimeSpan.FromSeconds(30));
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "F8") == 1, "30 seconds must no longer retry");
        clock.Advance(TimeSpan.FromMilliseconds(29999));
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "F8") == 1, "59.999 seconds is still inside confirmation window");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "F8") == 2, "60 seconds sends the second attempt");
        for (var i = 0; i < 4; i++) await h.Tick();
        Check(h.Input.Keys.Count(k => k == "F8") == 2 && !h.Input.KeyDowns.Contains("W"),
            "repeated ticks cannot spam the key or move during confirmation");
        clock.Advance(TimeSpan.FromMinutes(1));
        h.Api.TransitionRead = () => Scene(0);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "F8") == 2 && h.State.StartupRecoveryActive &&
              h.State.StartupRecoveryPointIndex == 0 && !h.State.StartupTownReturnPending,
            "a late landing observed at the retry boundary wins over another key press and attaches the route");
    }

    public static async Task RetryTwentyAttemptLimitAsync()
    {
        foreach (var noKill in new[] { false, true })
        {
            var clock = new ReturnClock();
            using var h = new Harness(clock);
            await h.Tick();
            if (noKill)
            {
                var transition = h.State.ReturnTransition!;
                var points = h.State.StartupRecoveryPoints.ToArray();
                h.State.ClearStartupRecovery();
                h.State.NoKillRecovery.StartTownReturn(Scene(1000).Player!.Position!.Value,
                    h.Path.Name, points, clock.GetUtcNow());
                h.State.NoKillRecovery.ReturnTransition = transition;
            }
            for (var attempt = 2; attempt <= 20; attempt++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                await h.Tick();
                Check(h.Input.Keys.Count(k => k == "F8") == attempt, "exactly one attempt per minute: " + attempt);
            }
            clock.Advance(TimeSpan.FromSeconds(59));
            await h.Tick();
            Check(noKill ? h.State.NoKillRecovery.Active : h.State.StartupTownReturnPending,
                "the twentieth attempt gets its confirmation window");
            clock.Advance(TimeSpan.FromSeconds(1));
            await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 20 && !h.Input.KeyDowns.Contains("W"),
                "the initial press is included; there is no twenty-first press or blind walking");
            Check(!h.State.StartupTownReturnPending && !h.State.NoKillRecovery.Active && !h.State.StartupRecoveryActive,
                "exhaustion clears all recall ownership");
            var restartId = h.Context.GameSessionRequests.ScriptRestartRequestId;
            Check(restartId.HasValue && restartId != Guid.Empty, "exhaustion requests a script restart");
            Check(await h.Controller.TryTickTownReturnAsync(h.Context, h.Plan, h.SemiState, h.State) != null,
                "ordinary work remains blocked until the manager replaces this worker");
            await h.Tick();
            Check(h.Context.GameSessionRequests.ScriptRestartRequestId == restartId &&
                  h.Input.Keys.Count(k => k == "F8") == 20, "repeated ticks preserve one restart request without replay");
            Check(h.Logger.Entries.Count(e => e.EventName == "town_return.retry") == 19 &&
                  h.Logger.Entries.Count(e => e.EventName == "town_return.retry_exhausted") == 1,
                "each retry and the final release have diagnostics");
        }
    }

    public static async Task RetrySceneGuardsAsync()
    {
        foreach (var scenario in new[] { "loading", "wrong-map", "death" })
        {
            var clock = new ReturnClock();
            using var h = new Harness(clock);
            await h.Tick();
            clock.Advance(TimeSpan.FromMinutes(1));
            h.Api.TransitionRead = () => scenario == "loading" ? Loading() :
                scenario == "wrong-map" ? Scene(0, 9) : Scene(1000, hp: 0);
            await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 1, "never retry during " + scenario);
            if (scenario == "death")
            {
                Check(h.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery,
                    "death is handled before any retry");
                continue;
            }
            // Even if loading returns to the original scene, do not interrupt a known transition.
            h.Api.TransitionRead = () => Scene(1000);
            clock.Advance(TimeSpan.FromMinutes(1));
            await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 1, "observed departure cannot be undone to permit a blind retry");
            clock.Advance(TimeSpan.FromMinutes(18));
            await h.Tick();
            Check(!h.State.StartupTownReturnPending && h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue,
                "loading and destination mismatch also request a restart at the deadline");
        }
    }

    public static async Task RetryInputFailuresAsync()
    {
        foreach (var scenario in new[] { "failed", "throw", "missing" })
        {
            var clock = new ReturnClock();
            using var h = new Harness(clock);
            await h.Tick();
            if (scenario == "missing") h.Context.Config.ScriptSettings!.Paths.TownReturnKey = "";
            else h.Input.PressResult = _ => scenario == "throw"
                ? throw new InvalidOperationException("uncertain acknowledgement") : OperationResult.Fail("input unavailable");
            clock.Advance(TimeSpan.FromMinutes(1));
            await h.Tick();
            if (scenario == "missing")
            {
                Check(!h.State.StartupTownReturnPending && h.Input.Keys.Count(k => k == "F8") == 1,
                    "missing retry key abandons the branch");
                Check(!h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue,
                    "missing configuration alone does not trigger the exhaustion restart");
                continue;
            }
            Check(h.State.ReturnTransition!.AttemptCount == 2, "input failure still consumes an attempt");
            for (var i = 0; i < 3; i++) await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 2, "failed input is not retried on every tick");
            clock.Advance(TimeSpan.FromMinutes(1));
            await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 3, "input failure can retry after the next full minute");
        }
    }

    public static async Task RetryPendingReadAsync()
    {
        foreach (var scenario in new[] { "deadline", "stop", "yield" })
        {
            using var h = new Harness();
            await h.Tick();
            if (scenario == "deadline")
                h.State.ReturnTransition!.Start(Scene(1000), h.Path, DateTimeOffset.UtcNow.AddMinutes(-20).AddMilliseconds(150));
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api.TransitionReadAsync = async token =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { canceled.TrySetResult(); }
                return Loading();
            };
            using var budget = scenario == "yield" ? SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(50)) : null;
            var work = h.Tick();
            if (scenario == "stop") h.Stop.Cancel();
            try
            {
                await work.WaitAsync(TimeSpan.FromSeconds(2));
                Check(scenario == "deadline" && !h.State.StartupTownReturnPending &&
                      h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue,
                    "the operation deadline cancels the pending scene read and requests restart");
            }
            catch (SnapshotReadBudgetExceededException) when (scenario == "yield")
            {
                Check(h.State.StartupTownReturnPending, "a short scheduling yield retains recall ownership");
                h.Api.TransitionReadAsync = null;
                h.Api.TransitionRead = () => Scene(0);
                await h.Tick();
                Check(h.State.StartupRecoveryActive, "recall continues after read scheduling yield");
            }
            catch (OperationCanceledException) when (scenario == "stop" && h.Stop.IsCancellationRequested)
            {
                Check(!h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue, "user stop must not request a restart");
            }
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(h.Input.Keys.Count(k => k == "F8") == 1, "pending scene data never authorizes another press");
        }
    }

    public static async Task RetryExhaustionRestartsWorkerAsync()
    {
        var clock = new ReturnClock();
        using var h = new Harness(clock);
        EnableMissingSpiritmasterPet(h);
        h.Input.AfterPress = key =>
        {
            if (key == "F8") clock.Advance(TimeSpan.FromMinutes(1));
        };
        var work = new DefaultAccountWorkerLoop(h.Input, h.SemiAuto, h.Controller).RunAsync(h.Context);
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!h.Logger.Entries.Any(e => e.EventName == "account.restart.requested"))
            {
                if (work.IsCompleted) await work;
                await Task.Delay(10, wait.Token);
            }
            var reads = h.Api.PlayerReadCount;
            await Task.Delay(500, wait.Token);
            Check(h.Input.Keys.Count(k => k == "F8") == 20, "worker completes exactly twenty recall attempts");
            Check(h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue && !work.IsCompleted,
                "worker keeps the restart request visible until manager stop/start");
            Check(!h.Input.Keys.Contains("F7") && !h.Input.KeyDowns.Contains("W") && h.Api.PlayerReadCount == reads,
                "pending restart suppresses summon, movement and game-world reads");
            Check(h.Logger.Entries.Count(e => e.EventName == "account.restart.requested") == 1,
                "restart handoff is published once");
        }
        finally
        {
            h.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
        }
    }

    public static async Task ScriptRestartHostLifecycleAsync()
    {
        using var h = new Harness();
        var loop = new RestartHostLoop();
        var host = new AccountWorkerHost(h.Api, h.Logger, h.Context.RuntimeStates, loop, new());
        try
        {
            Check(host.Start(h.Context.Config).Success, "start host");
            var first = await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var id = first.GameSessionRequests.RequestScriptRestart();
            Check(first.GameSessionRequests.RequestScriptRestart() == id && host.ScriptRestartRequestId == id,
                "host publishes the same idempotent request used by controller and worker");
            Check((await host.StopAsync()).Success && host.ScriptRestartRequestId == null,
                "user stop hides the pending restart immediately");
            loop.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check(host.Start(h.Context.Config).Success, "start replacement session");
            var second = await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!ReferenceEquals(first.GameSessionRequests, second.GameSessionRequests) &&
                  second.GameSessionRequests.ScriptRestartRequestId == null && host.ScriptRestartRequestId == null,
                "fresh worker session cannot inherit the previous restart");
        }
        finally { await host.StopAsync(); }
    }

    private sealed class RestartHostLoop : IAccountWorkerLoop
    {
        public TaskCompletionSource<AccountWorkerContext> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(AccountWorkerContext context)
        {
            Entered.TrySetResult(context);
            await Task.Delay(Timeout.InfiniteTimeSpan, context.StopToken);
        }
    }
}
