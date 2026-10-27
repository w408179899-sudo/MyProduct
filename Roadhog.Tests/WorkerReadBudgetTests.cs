using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class WorkerReadBudgetTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static async Task ProviderBudgetAndRecoveryAsync()
    {
        var api = new FakeGameApi();
        var reader = new RoadhogSnapshotReader(new() { AccountName = "budget" }, api, new InMemoryRoadhogLogger(), CancellationToken.None);
        api.PlayerReadFallback = OperationResult<PlayerSnapshot>.Fail("partial");
        api.QuickbarRead = () => throw new IOException("partial");
        api.SkillAvailabilityRead = () => throw new IOException("partial");
        api.WorldObjectReadFallback = OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail("partial");
        api.ReviveUiRead = () => throw new IOException("partial");
        Func<Task>[] reads = {
            async () => { await reader.ReadPlayerAsync(); },
            async () => { await reader.ReadQuickbarAsync(); },
            async () => { await reader.ReadSkillAvailabilityAsync(); },
            async () => { await reader.ReadWorldObjectsAsync(); },
            async () => { await reader.ReadReviveUiAsync(); },
            async () => { await reader.ReadInventoryCapacityAsync(); }
        };
        foreach (var read in reads)
        {
            using var budget = SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(60));
            try { await read().WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("missing scheduling cancellation"); }
            catch (SnapshotReadBudgetExceededException) { }
        }
        api.PlayerReadFallback = null;
        using (SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(60)))
        {
            var first = await reader.ReadPlayerAsync();
            await Task.Delay(100);
            var second = await reader.ReadPlayerAsync(first.Version);
            Check(second.Value.CurrentHp == 100 && second.Version > first.Version, "budget is per read; official data resumes without resetting the reader");
        }
        using var stop = new CancellationTokenSource(100);
        var outside = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), stop.Token);
        try { await outside.ReadQuickbarAsync(); throw new Exception("stop expected"); }
        catch (OperationCanceledException ex) { Check(ex is not SnapshotReadBudgetExceededException, "budget scope must not leak and explicit stop must retain its meaning"); }
    }

    public static async Task OpeningEntryUsesBudgetAsync()
    {
        var api = new FakeGameApi();
        api.SkillAvailabilityRead = () => throw new IOException("partial opening target");
        var logger = new InMemoryRoadhogLogger();
        var context = new AccountWorkerContext(new(), api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
        var method = typeof(SemiAutoCombatController).GetMethod("ReadQuickbarWeaveAvailabilityAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var state = new SemiAutoCombatState();
        var target = new LockedTargetSnapshot(50, 100, 1, LockedTargetSnapshot.MonsterObjectType, "target", 100, 100, null, 1, DateTimeOffset.Now);
        Task<SkillAvailabilitySnapshot> Read() => (Task<SkillAvailabilitySnapshot>)method.Invoke(null, new object[] { context, state, target })!;
        using var scope = SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(60));
        try { await Read(); throw new Exception("opening must not wait forever"); }
        catch (SnapshotReadBudgetExceededException) { }
        api.SkillAvailabilityRead = null;
        var value = await Read();
        Check(value == api.SkillAvailability, "independent opening entry resumes official data without restarting the worker");
    }

    public static async Task PendingCaptureDrainedAsync()
    {
        var api = new FakeGameApi();
        var active = 0;
        var cancelled = 0;
        api.TransitionReadAsync = async token =>
        {
            Interlocked.Increment(ref active);
            try { await Task.Delay(Timeout.Infinite, token); throw new Exception("unreachable"); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
            finally { Interlocked.Decrement(ref active); }
        };
        var reader = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), CancellationToken.None);
        using (SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(60)))
        {
            try { await reader.ReadChannelTransitionAsync(); throw new Exception("budget expected"); }
            catch (SnapshotReadBudgetExceededException) { }
            Check(active == 0 && cancelled == 1, "the raw capture must finish cancellation before the next tick starts");
            api.TransitionReadAsync = null;
            api.TransitionRead = () => new(false, null, null, DateTimeOffset.Now);
            for (var i = 0; i < 3; i++)
            {
                Check(!(await reader.ReadChannelTransitionAsync()).Value.IsReady, "valid loading remains normal data, never a fabricated ready scene");
                await Task.Delay(75);
            }
        }
    }

    public static async Task WorkerYieldChecksDeathBeforeBlockedWorkAsync() => await RunWorkerAsync(paused: false);
    public static async Task QueuedCleanupYieldDoesNotTerminateAsync() => await RunWorkerAsync(paused: false, queued: true);
    public static async Task PausedWorkerRevivesWithoutResumingTradeAsync() => await RunWorkerAsync(paused: true);

    private static async Task RunWorkerAsync(bool paused, bool queued = false)
    {
        var previous = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        Task? run = null;
        try
        {
            var api = new FakeGameApi { InventoryCapacity = 81 };
            api.Player = api.Player with { CurrentHp = 0 };
            if (!paused)
                for (var i = 0; i < 10000; i++) api.LockedTargetReadResults.Enqueue(OperationResult<LockedTargetSnapshot>.Fail("unavailable"));
            var settings = new ScriptSettings { MainMode = AccountMainMode.SemiAuto };
            settings.Maintenance.AutoFoodEnabled = true;
            var mailbox = new CleanupRequestMailbox();
            CleanupRequest? request = null;
            if (paused || queued)
            {
                mailbox.Request(settings, manual: true, standaloneShop: true);
                request = mailbox.Current!;
                if (paused) request.Failure = "uncertain sale";
            }
            var logger = new InMemoryRoadhogLogger();
            var context = new AccountWorkerContext(new() { AccountName = "yield", MainMode = settings.MainMode, ScriptSettings = settings },
                api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(10) }, stop.Token, mailbox);
            var keyboard = new RecordingKeyboardInput();
            keyboard.AfterMouseUp = button =>
            {
                if (button == RoadhogMouseButton.Left && logger.Entries.Any(e => e.EventName == "player_life.death.detected"))
                    api.Player = api.Player with { CurrentHp = 100 };
            };
            var semi = new SemiAutoCombatController(keyboard);
            var worker = new DefaultAccountWorkerLoop(keyboard, semi, new StationaryCombatController(keyboard, semi));
            run = worker.RunAsync(context);
            while (api.Player.IsDead && !run.IsCompleted) await Task.Delay(20, stop.Token);
            Check(api.Player.IsAlive, "worker must reach and click revival even when earlier work cannot publish");
            Check(logger.Entries.Any(e => e.EventName == "player_life.death.detected"), "worker life guard must run");
            if (paused)
            {
                await Task.Delay(200, stop.Token);
                Check(mailbox.Current == null && !run.IsCompleted, "failed transaction is dropped so life recovery and normal work remain active");
                Check(logger.Entries.Count(e => e.EventName == "cleanup_workflow.failed_continuing") == 1,
                    "the old paused request is abandoned exactly once without replay");
            }
            else Check(logger.Entries.Any(e => e.EventName == "worker.read_budget.yield"), "two-second blocked read must yield before death handling");
            if (queued) Check(ReferenceEquals(request, mailbox.Current) && request!.Failure == null &&
                !logger.Entries.Any(e => e.EventName == "cleanup_workflow.operation_timeout"),
                "a queued cleanup has submitted no actions and cannot be terminated by an unrelated combat read");
        }
        finally
        {
            stop.Cancel();
            if (run is not null) { try { await run.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
            Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previous);
        }
    }

    public static async Task TransactionCancellationIsNotConfirmationFailureAsync()
    {
        var api = new FakeGameApi();
        var reader = api.Create(new(), new InMemoryRoadhogLogger());
        var actions = new TradingActions(new RecordingKeyboardInput(), reader, CancellationToken.None);
        api.PersonalShopRead = () => throw new IOException("partial");
        using var scope = SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(50));
        try
        {
            await actions.Wait(async () => (await reader.ReadPersonalShopAsync()).Value, _ => false);
            throw new Exception("budget expected");
        }
        catch (SnapshotReadBudgetExceededException) { }
    }
}
