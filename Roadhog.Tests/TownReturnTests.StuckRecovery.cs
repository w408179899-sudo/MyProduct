using System.Reflection;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Api;
using Roadhog.Core.Common;

internal static partial class TownReturnTests
{
    private static Task InvokeController(Harness h, string method, params object[] args) =>
        (Task)typeof(StationaryCombatController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Controller, args)!;

    private static Task RecoverStuck(Harness h) => InvokeController(h, "RecoverBlockedReturnRouteAsync", h.Context, h.State);

    public static async Task StuckMovementTriggersRecallAsync()
    {
        foreach (var route in new[] { false, true })
        {
            using var h = new Harness();
            h.Api.Player = Scene(50).Player!;
            h.Api.TransitionRead = () => Scene(50);
            h.State.MarkStartupRecoveryChecked();
            if (route) h.State.StartStartupRecovery("return", h.Path.Points.Select(p => p.ToVector3()).ToArray(), 1);
            h.State.IsMovingForward = true;
            h.State.MarkReturnHomeProgress(h.Api.Player.Position!.Value, DateTimeOffset.Now.AddSeconds(-16));
            h.State.MarkStartupRecoveryProgress(1, h.Api.Player.Position.Value, DateTimeOffset.Now.AddSeconds(-16));
            await h.Tick();
            Check(h.State.StuckTownReturnPending && h.Input.Keys.Count(k => k == "F8") == 1,
                "sustained terrain obstruction reaches recall through the real " + (route ? "route" : "return-home") + " tick");
            h.Api.TransitionRead = Loading;
            await h.Tick();
            Check(h.State.StuckTownReturnPending, "loading retains recall ownership");
            h.Api.TransitionRead = () => Scene(0, 9);
            await h.Tick();
            Check(h.State.StuckTownReturnPending, "wrong destination does not start walking");
            h.Api.Player = Scene(1).Player!;
            h.Api.TransitionRead = () => Scene(1);
            await h.Tick();
            Check(!h.State.StuckTownReturnPending && h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0,
                "confirmed terrain recall resumes at route entry, not the nearest old point");
            await h.Tick();
            h.Api.Player = Scene(100).Player!;
            await h.Tick();
            Check(!h.State.StartupRecoveryActive && !h.State.StartupTownReturnPending && h.Input.Keys.Count(k => k == "F8") == 1,
                "route completes and normal work resumes without another recall");
        }
    }

    public static async Task StuckPauseAndProgressAsync()
    {
        foreach (var route in new[] { false, true })
        foreach (var mode in new[] { "channel_wait", "combat_stop", "new_move", "progress" })
        {
            using var h = new Harness();
            h.Api.Player = Scene(50).Player!;
            h.Api.TransitionRead = () => Scene(50);
            h.State.MarkStartupRecoveryChecked();
            if (route) h.State.StartStartupRecovery("return", h.Path.Points.Select(p => p.ToVector3()).ToArray(), 1);
            h.State.IsMovingForward = mode != "new_move";
            var previous = Scene(mode == "progress" ? 48 : 50).Player!.Position!.Value;
            h.State.MarkReturnHomeProgress(previous, DateTimeOffset.Now.AddSeconds(-40));
            h.State.MarkStartupRecoveryProgress(1, previous, DateTimeOffset.Now.AddSeconds(-40));
            if (mode == "channel_wait")
            {
                await h.Controller.SetChannelSwitchPendingAsync(h.Context, h.State, true);
                await h.Controller.TryTickChannelSwitchWaitAsync(h.Context, h.Plan, h.SemiState, h.State);
                Check(h.State.StartupRecoveryLastProgressAt == DateTimeOffset.MinValue &&
                    h.State.ReturnHomeLastProgressAt == DateTimeOffset.MinValue, "channel wait clears movement timers");
                await h.Controller.SetChannelSwitchPendingAsync(h.Context, h.State, false);
            }
            else if (mode == "combat_stop")
                await InvokeController(h, "StopMovementAsync", h.Context, h.State, false);
            await h.Tick();
            Check(!h.State.StuckTownReturnPending && !h.Input.Keys.Contains("F8"),
                "paused/restarted movement or real progress must not trigger recall: " + mode + "/" + route);
        }
    }

    public static async Task StuckRecallFailuresAsync()
    {
        foreach (var failure in new[] { "missing_key", "missing_path", "press_failed", "press_exception", "timeout", "wrong_map" })
        {
            using var h = new Harness();
            h.Api.Player = Scene(50).Player!;
            h.Api.TransitionRead = () => Scene(50);
            h.State.IsMovingForward = true;
            h.State.StartStartupRecovery("return", h.Path.Points.Select(p => p.ToVector3()).ToArray(), 1);
            if (failure == "missing_key") h.Context.Config.ScriptSettings!.Paths.TownReturnKey = "";
            if (failure == "missing_path") h.Context.Config.ScriptSettings!.Paths.RevivePathName = "missing";
            if (failure == "press_failed") h.Input.PressResult = _ => OperationResult.Fail("test input rejected");
            if (failure == "press_exception") h.Input.AfterPress = _ => throw new IOException("test input exception");
            await RecoverStuck(h);
            if (failure is "timeout" or "wrong_map")
            {
                if (failure == "wrong_map")
                {
                    h.Api.TransitionRead = () => Scene(0, 9);
                    await h.Tick();
                    Check(h.State.StuckTownReturnPending, "wrong-map arrival must remain pending until deadline");
                }
                h.State.ReturnTransition!.Start(Scene(50), h.Path, DateTimeOffset.UtcNow.AddSeconds(-91));
                await h.Tick();
            }
            Check(!h.State.StartupTownReturnPending && !h.State.StuckTownReturnPending && !h.State.StartupRecoveryActive &&
                !h.State.ReturningHome && h.State.ReturnNavigationRetryAfter > DateTimeOffset.UtcNow,
                "failure releases recovery ownership with bounded retry backoff: " + failure);
            var presses = h.Input.Keys.Count;
            var movement = h.Input.KeyDowns.Count(k => k == "W");
            await RecoverStuck(h);
            await h.Tick();
            Check(h.Input.Keys.Count == presses && h.Input.KeyDowns.Count(k => k == "W") == movement,
                "failed return must not instantly re-enter recall or direct walking: " + failure);
            Check(await h.Controller.TryTickTownReturnAsync(h.Context, h.Plan, h.SemiState, h.State) == null,
                "main loop is no longer owned by the failed recall");
        }
    }

    public static async Task StuckRecallReadDeadlineAndStopAsync()
    {
        foreach (var stop in new[] { false, true })
        {
            using var h = new Harness();
            h.Api.TransitionRead = () => Scene(50);
            await RecoverStuck(h);
            h.State.ReturnTransition!.Start(Scene(50), h.Path, DateTimeOffset.UtcNow.AddMilliseconds(stop ? 0 : -89800));
            var pending = new TaskCompletionSource<Roadhog.Core.Model.ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api.TransitionReadAsync = token => pending.Task.WaitAsync(token);
            var work = h.Tick();
            try
            {
                if (stop) h.Stop.Cancel();
                await work.WaitAsync(TimeSpan.FromSeconds(3));
                Check(!stop && !h.State.StuckTownReturnPending, "pending scene read cannot outlive terrain recall deadline");
            }
            catch (OperationCanceledException) when (stop && h.Stop.IsCancellationRequested) { }
            finally { pending.TrySetResult(Loading()); }
            Check(h.Input.Keys.Count(k => k == "F8") == 1 && !h.Input.KeyDowns.Contains("W"),
                "deadline/stop does not repeat recall or start movement");
        }

        using var yielded = new Harness();
        yielded.Api.TransitionRead = () => Scene(50);
        await RecoverStuck(yielded);
        yielded.Api.TransitionReadAsync = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Loading();
        };
        using (SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(50)))
        {
            try { await yielded.Tick(); throw new Exception("expected foreground read yield"); }
            catch (SnapshotReadBudgetExceededException) { }
        }
        Check(yielded.State.StuckTownReturnPending, "an individual read yield preserves submitted recall");
        yielded.Api.TransitionReadAsync = null;
        yielded.Api.TransitionRead = () => Scene(0);
        await yielded.Tick();
        Check(yielded.State.StartupRecoveryActive && !yielded.State.StuckTownReturnPending, "recall continues after read yield");
    }

    public static async Task StuckRecallResetsNoKillWatchAsync()
    {
        using var h = new Harness();
        h.State.NoKillRecovery.ResetWatch(DateTimeOffset.Now.AddMinutes(-21));
        h.Api.TransitionRead = () => Scene(50);
        await RecoverStuck(h);
        h.Api.TransitionRead = () => Scene(0);
        await h.Tick();
        Check(h.State.StartupRecoveryActive && !h.State.NoKillRecovery.IsDue(DateTimeOffset.Now.AddSeconds(31), TimeSpan.FromMinutes(20)),
            "successful terrain recall must not inherit an overdue no-kill watch and immediately recall again");
    }
}
