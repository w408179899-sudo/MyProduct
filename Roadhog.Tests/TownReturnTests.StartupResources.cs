using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static partial class TownReturnTests
{
    private static ChannelTransitionSnapshot ResourceScene(uint hp, uint mp, uint maxHp = 100,
        uint maxMp = 100, float x = 10) => Scene(x) with
        { Player = Scene(x).Player! with { CurrentHp = hp, MaxHp = maxHp, CurrentMp = mp, MaxMp = maxMp } };

    private static Task<TimeSpan?> CheckStartupResources(Harness h, StationaryCombatState? state = null) =>
        h.Controller.TryPrepareStartupResourceReturnAsync(h.Context, h.SemiState, state ?? h.State);

    public static async Task StartupResourceThresholdAsync()
    {
        foreach (var (hp, mp, maxHp, maxMp, recall) in new[]
        {
            (25u, 25u, 100u, 100u, true),
            (29u, 29u, 100u, 100u, true),
            (30u, 29u, 100u, 100u, false),
            (29u, 30u, 100u, 100u, false),
            (30u, 30u, 100u, 100u, false),
            (20u, 90u, 100u, 100u, false),
            (90u, 20u, 100u, 100u, false),
            (299u, 299u, 1000u, 1000u, true),
            (0u, 25u, 100u, 100u, false),
            (25u, 0u, 100u, 100u, true),
            (25u, 0u, 100u, 0u, false),
            (0u, 25u, 0u, 100u, false),
            (1_000_000_000u, 1_000_000_000u, uint.MaxValue, uint.MaxValue, true)
        })
        {
            using var h = new Harness();
            h.Api.TransitionRead = () => ResourceScene(hp, mp, maxHp, maxMp);
            await CheckStartupResources(h);
            Check(h.Input.Keys.Count(k => k == "F8") == (recall ? 1 : 0),
                $"strict AND threshold for HP {hp}/{maxHp}, MP {mp}/{maxMp}");
            Check(h.State.StartupTownReturnPending == recall, "recall bypasses the nearby-route distance gate");
        }
    }

    public static async Task StartupResourceFirstSnapshotAndOnceAsync()
    {
        foreach (var lowAtLogin in new[] { true, false })
        {
            using var h = new Harness();
            h.Api.TransitionRead = () => ResourceScene(lowAtLogin ? 25u : 100u, lowAtLogin ? 25u : 100u);
            await h.Context.PrepareSkillBindingsAsync();
            h.Api.TransitionRead = () => ResourceScene(lowAtLogin ? 100u : 25u, lowAtLogin ? 100u : 25u);
            await CheckStartupResources(h);
            Check(h.Input.Keys.Count(k => k == "F8") == (lowAtLogin ? 1 : 0), "use the first ready login snapshot");
            h.State.ClearStartupRecovery();
            h.Api.TransitionRead = () => ResourceScene(10, 10);
            await CheckStartupResources(h);
            await CheckStartupResources(h, new StationaryCombatState());
            Check(h.Input.Keys.Count(k => k == "F8") == (lowAtLogin ? 1 : 0),
                "combat dips and loop recovery on the same context cannot repeat login recall");
            Check(!h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue, "normal startup does not request restart");
        }
        using var fresh = new Harness();
        fresh.Api.TransitionRead = () => ResourceScene(25, 25);
        await CheckStartupResources(fresh);
        Check(fresh.Input.Keys.Count(k => k == "F8") == 1, "a fresh account worker has its own login decision");
    }

    public static async Task StartupResourceWorkerLandingAsync()
    {
        using var h = new Harness();
        EnableMissingSpiritmasterPet(h);
        var recalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var summoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLanding = false;
        var pendingReads = 0;
        h.Input.AfterPress = key =>
        {
            if (key == "F8") recalled.TrySetResult();
            if (key == "F7") summoned.TrySetResult();
        };
        h.Api.TransitionRead = () =>
        {
            if (!recalled.Task.IsCompleted) return ResourceScene(25, 25, x: 60);
            if (Volatile.Read(ref allowLanding)) return Scene(0);
            if (++pendingReads >= 3) held.TrySetResult();
            return pendingReads == 1 ? Loading() : Scene(0, map: 9);
        };
        var work = new DefaultAccountWorkerLoop(h.Input, h.SemiAuto, h.Controller).RunAsync(h.Context);
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(h.Input.Keys.SequenceEqual(new[] { "F1", "F8" }), "low resources recall precedes pet summon and combat");
            Check(!h.Input.KeyDowns.Contains("W"), "no movement before confirmed landing");
            h.Api.Player = Scene(0).Player! with { CharacterClassId = AionClassId.Spiritmaster };
            Volatile.Write(ref allowLanding, true);
            await summoned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(h.Logger.Entries.Any(e => e.EventName == "worker.startup.low_resources.return"), "record startup HP and MP");
            Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.startup_recovery.return.verify.ok"),
                "confirmed landing hands off to normal startup recovery");
            Check(h.Input.Keys.SequenceEqual(new[] { "F1", "F8", "F7" }), "resume pet maintenance after landing exactly once");
        }
        finally
        {
            h.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
        }
        Check(h.Input.KeyUps.Contains("W"), "manual stop releases worker input");
    }

    public static async Task StartupResourceFailureContinuationAsync()
    {
        foreach (var failure in new[] { "key", "path", "press", "exception" })
        {
            using var h = new Harness();
            h.Api.TransitionRead = () => ResourceScene(25, 25);
            if (failure == "key") h.Context.Config.ScriptSettings!.Paths.TownReturnKey = "";
            if (failure == "path") h.Context.Config.ScriptSettings!.Paths.RevivePathName = "missing";
            h.Input.PressResult = _ => failure == "exception" ? throw new InvalidOperationException("test input failure")
                : OperationResult.Fail("test input failure");
            await CheckStartupResources(h);
            Check(await CheckStartupResources(h) is null, "failed branch returns control to normal work");
            Check(h.Input.Keys.Count(k => k == "F8") <= 1, "failed input cannot replay immediately");
            Check(!h.State.StartupTownReturnPending && !h.State.ReturnNavigationBlocked,
                "failure releases navigation ownership");
            Check(h.Logger.Entries.Any(e => e.EventName == "town_return.branch_abandoned"), "failure is diagnosed");
            Check(!h.Context.GameSessionRequests.ScriptRestartRequestId.HasValue, "failure cannot stop or restart the account");
        }
    }

    public static async Task StartupResourceReadYieldAndDeathAsync()
    {
        using var h = new Harness();
        h.Api.TransitionRead = Loading;
        Check(await CheckStartupResources(h) is not null && h.Input.Keys.Count == 0, "loading waits for first ready player");
        h.Api.TransitionRead = () => ResourceScene(25, 25);
        await h.Context.PrepareSkillBindingsAsync();
        h.Api.TransitionRead = () => throw new IOException("partial scene capture");
        using (SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(60)))
        {
            try { await CheckStartupResources(h); throw new Exception("expected read yield"); }
            catch (SnapshotReadBudgetExceededException) { }
        }
        Check(h.Input.Keys.Count == 0, "read yield cannot submit input");
        h.Api.TransitionRead = () => ResourceScene(100, 100);
        await CheckStartupResources(h);
        Check(h.Input.Keys.Count(k => k == "F8") == 1, "yield preserves the original low-resource decision");
        h.Api.TransitionRead = () => ResourceScene(0, 25);
        await CheckStartupResources(h);
        Check(h.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery && !h.State.StartupTownReturnPending,
            "death during recall hands off to the life guard");

        using var diedBeforePress = new Harness();
        diedBeforePress.Api.TransitionRead = () => ResourceScene(25, 25);
        await diedBeforePress.Context.PrepareSkillBindingsAsync();
        diedBeforePress.Api.TransitionRead = () => ResourceScene(0, 25);
        await CheckStartupResources(diedBeforePress);
        Check(diedBeforePress.Input.Keys.Count == 0, "death before submission must not recall");
    }

    public static async Task StartupResourceStopPendingReadAsync()
    {
        using var h = new Harness();
        h.Api.TransitionRead = () => ResourceScene(25, 25);
        await h.Context.PrepareSkillBindingsAsync();
        var pending = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.TransitionReadAsync = token => pending.Task.WaitAsync(token);
        var work = CheckStartupResources(h);
        h.Stop.Cancel();
        try { await work.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("expected manual stop"); }
        catch (OperationCanceledException) { }
        Check(h.Input.Keys.Count == 0 && !h.State.StartupTownReturnPending, "manual stop cancels pending preparation without input");
    }
}
