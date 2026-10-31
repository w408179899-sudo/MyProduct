using System.Diagnostics;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class LootTouchTests
{
    internal static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public readonly FakeGameApi Api = new()
        {
            TargetEntityId = 100, TargetOwnServerObjectId = 9001, TargetCurrentHp = 0,
            TargetMaxHp = 1000, TargetLootableRaw = 1, TargetPosition = new(2, 0, 0)
        };
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Log = new();
        public readonly StationaryCombatState State = new();
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(12));
        public readonly AccountWorkerContext Context;
        public readonly StationaryCombatController Controller;
        public readonly SemiAutoCombatState Combat = new();
        public readonly SemiAutoSkillPlan Plan;
        public LootInteractionSnapshot Reply = LootInteractionSnapshot.Closed;
        public int Presses => Input.Keys.Count(key => key == "NumPadDecimal");
        public bool Remembered => State.HasTouchedLootCorpse(100, 9001, DateTimeOffset.Now, TimeSpan.FromMinutes(5));
        public Fixture()
        {
            var settings = new ScriptSettings();
            settings.Combat.EnableLoot = true;
            settings.Combat.HasStationaryCombatPosition = true;
            settings.Combat.StationaryCombatRadius = 60;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            settings.Skills.OpeningSkill.Enabled = false;
            Api.Player = Api.Player with { Position = new(0, 0, 0) };
            Api.LootInteractionRead = () => Volatile.Read(ref Reply);
            Context = new(new AccountConfig { AccountName = "loot-test", ScriptSettings = settings }, Api, Log,
                new AccountRuntimeManager(Log), new(), Stop.Token);
            Controller = new(Input, new SemiAutoCombatController(Input));
            Plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
            Start();
        }
        public void Start() => State.StartLootAfterKill(new(100, 9001, 3, LockedTargetSnapshot.MonsterObjectType,
            "corpse", 0, 1000, new(2, 0, 0), 2, DateTimeOffset.Now, LootableRaw: 1), DateTimeOffset.Now);
        public Task<TimeSpan> Tick() => Controller.TickAsync(Context, Plan, Combat, State);
        public void Accept(bool window = false, bool confirmation = false) => Volatile.Write(ref Reply, new(9001, window, confirmation));
        public async Task Finish()
        {
            var timeout = Stopwatch.StartNew();
            while (State.LootAfterKill.Active && timeout.Elapsed < TimeSpan.FromSeconds(4))
            { await Tick(); await Task.Delay(20); }
            Check(!State.LootAfterKill.Active, "loot branch must release the combat loop: " + State.LootAfterKill.Step + "/" + State.LootAfterKill.Outcome + " " +
                string.Join(";", Log.Entries.TakeLast(8).Select(entry => entry.EventName + ":" + System.Text.Json.JsonSerializer.Serialize(entry.Fields))));
        }
        public void Dispose() { State.ClearLootAfterKill(); Stop.Cancel(); Stop.Dispose(); }
    }

    public static async Task ReadyAndTouchedAsync()
    {
        using var f = new Fixture();
        f.Input.AfterPress = key => { if (key == "NumPadDecimal") f.Accept(); };
        var elapsed = Stopwatch.StartNew();
        await f.Tick();
        Check(f.Presses == 1 && f.Remembered && !f.State.LootAfterKill.Active, "matching acknowledgement completes in the same tick");
        Check(elapsed.Elapsed < TimeSpan.FromSeconds(1), "ready corpse must not pay the old 1.2-second delay");
        Check(f.Api.TargetLootableRaw == 1, "success must not require an emptied corpse");
        Check(f.Api.LootReadCount == 0, "confirmation must not scan surrounding corpses");
        f.Start(); await f.Finish();
        Check(f.Presses == 1, "an already touched corpse must be ignored even while still lootable");
    }

    public static async Task ReadinessAsync()
    {
        using var f = new Fixture();
        f.Api.TargetLootableRaw = 0;
        await f.Tick();
        Check(f.Presses == 0 && f.State.LootAfterKill.Active, "wait for a delayed lootable flag");
        f.Api.TargetLootableRaw = 1;
        f.Input.AfterPress = key => { if (key == "NumPadDecimal") f.Accept(); };
        await f.Tick();
        Check(f.Remembered && f.Presses == 1, "publish readiness immediately without waiting out the old delay");
    }

    public static async Task KnockdownAndRecoveryAsync()
    {
        using var f = new Fixture();
        var blockedUntil = DateTimeOffset.Now.AddSeconds(2);
        f.Input.AfterPress = key => { if (key == "NumPadDecimal" && DateTimeOffset.Now >= blockedUntil) f.Accept(); };
        await f.Tick();
        Check(f.Presses == 1 && !f.Remembered && f.State.LootAfterKill.Active, "an accepted key is not an accepted loot action");
        await f.Finish();
        Check(f.Remembered && f.Presses >= 4 && f.Presses <= 7, "retry after two seconds of knockdown/animation without flooding input");
    }

    public static async Task TransientAndWrongCorpseAsync()
    {
        using var f = new Fixture();
        await f.Tick();
        var observer = f.State.LootAfterKill.Observation!;
        Volatile.Write(ref f.Reply, new(9002, true, false));
        await observer.CaptureAsync();
        Check(!observer.Touched && !f.Remembered, "another corpse's open window cannot confirm this corpse");
        f.Accept();
        await Until(() => observer.Touched);
        Volatile.Write(ref f.Reply, LootInteractionSnapshot.Closed);
        await Until(() => observer.Latest.CorpseServerObjectId == 0);
        Check(observer.Touched && observer.Latest.CorpseServerObjectId == 0, "latch the accepted interaction across automatic window closure");
        await f.Tick();
        Check(f.Remembered && f.Presses == 1, "a transient acknowledgement prevents unnecessary retries");
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    public static async Task ConfirmationAndLimitedItemsAsync()
    {
        foreach (var confirmation in new[] { false, true })
        {
            using var f = new Fixture();
            f.Input.AfterPress = key =>
            {
                if (key == "NumPadDecimal") f.Accept(window: true, confirmation);
                if (key == "Escape") Volatile.Write(ref f.Reply, LootInteractionSnapshot.Closed);
            };
            await f.Tick(); await f.Finish();
            Check(f.Remembered && f.Presses == 1 && f.Api.TargetLootableRaw == 1,
                "limited or confirmation items do not force repeat looting");
            Check(f.Input.Keys.Count(key => key == "Escape") == 1, "dismiss only the observed loot UI");
            f.Start(); await f.Finish(); Check(f.Presses == 1, "residual items must remain ignored");
        }
    }

    public static async Task AutoClosingWindowAsync()
    {
        using var f = new Fixture();
        f.Input.AfterPress = key => { if (key == "NumPadDecimal") f.Accept(window: true); };
        await f.Tick();
        Check(f.Remembered && !f.Input.Keys.Contains("Escape"), "transient auto-loot UI must not immediately receive Escape");
        await Task.Delay(50);
        Volatile.Write(ref f.Reply, LootInteractionSnapshot.Closed);
        await f.Finish();
        Check(f.Presses == 1 && !f.Input.Keys.Contains("Escape"), "automatically closed UI must not open the game menu");
    }

    public static async Task TimeoutAndChangedTargetAsync()
    {
        var previous = Environment.GetEnvironmentVariable("ROADHOG_LOOT_CONFIRM_TIMEOUT_MS");
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_CONFIRM_TIMEOUT_MS", "250");
        try
        {
            using var f = new Fixture(); await f.Tick(); await f.Finish();
            Check(!f.Remembered && f.State.IsLootCorpseDeferred(100, 9001, DateTimeOffset.Now), "timeout defers, never marks touched");
            Check(!f.State.IsLootCorpseDeferred(100, 9001, DateTimeOffset.Now.AddSeconds(6)), "failure backoff expires independently of success cache");
            var count = f.Presses; f.Start(); await f.Finish(); Check(f.Presses == count, "failure does not immediately replay");
        }
        finally { Environment.SetEnvironmentVariable("ROADHOG_LOOT_CONFIRM_TIMEOUT_MS", previous); }
        using var changed = new Fixture();
        await changed.Tick(); changed.Api.TargetOwnServerObjectId = 9002;
        await Task.Delay(620); await changed.Tick(); await changed.Finish();
        Check(changed.Presses == 1 && !changed.Remembered, "target changes must stop retries without sending loot to a different object");
    }

    public static async Task StopAndIsolationAsync()
    {
        using var a = new Fixture(); using var b = new Fixture();
        await a.Tick(); await b.Tick();
        a.Accept(); await a.State.LootAfterKill.Observation!.CaptureAsync();
        Check(a.State.LootAfterKill.Observation.Touched && !b.State.LootAfterKill.Observation!.Touched, "account observations remain isolated");
        var observer = b.State.LootAfterKill.Observation!;
        b.State.EnterDeathRecovery(DateTimeOffset.Now);
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        Check(!b.Remembered && b.State.LootAfterKill.Observation is null, "death cancels observation without recording a pickup");
        var active = a.State.LootAfterKill.Observation!;
        a.Stop.Cancel(); await active.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        Check(!a.Remembered, "worker stop never mutates the success cache from a background task");
    }

    public static async Task InputAndUiFailureAsync()
    {
        using (var f = new Fixture())
        {
            f.Input.PressResult = key => key == "NumPadDecimal" && f.Presses == 1 ? OperationResult.Fail("injected key failure") : OperationResult.Ok();
            f.Input.AfterPress = key => { if (key == "NumPadDecimal") f.Accept(); };
            await f.Tick(); Check(!f.Remembered, "failed keyboard input cannot complete pickup");
            await f.Finish(); Check(f.Remembered && f.Presses == 2, "input failure retries within the action deadline");
        }
        using var stuck = new Fixture();
        stuck.Input.AfterPress = key => { if (key == "NumPadDecimal") stuck.Accept(true, true); };
        await stuck.Tick(); await stuck.Finish();
        Check(stuck.Remembered && stuck.Presses == 1 && stuck.Input.Keys.Count(key => key == "Escape") <= 5,
            "stuck popup cleanup is bounded without losing the confirmed touch or replaying pickup");
    }

    public static Task DecoderAndPublicationAsync()
    {
        const ulong module = 0x10000000, window = 0x20000000;
        var bytes = new Dictionary<ulong, byte[]>
        {
            [module + LootInteractionDecoder.CorpseIdRva] = BitConverter.GetBytes(9001u),
            [module + LootInteractionDecoder.WindowRva] = BitConverter.GetBytes(window),
            [window + 0x28] = BitConverter.GetBytes(1UL),
            [window + 0x510] = BitConverter.GetBytes(-1)
        };
        var decoder = new LootInteractionDecoder((address, _) => bytes.GetValueOrDefault(address) ?? []);
        var complete = decoder.Capture(module).Merge(null)!;
        Check(complete == new LootInteractionSnapshot(9001, true, false), "decode the live-verified accepted corpse and UI fields");
        bytes[window + 0x510] = BitConverter.GetBytes(8);
        Check(decoder.Capture(module).Merge(complete)!.ConfirmationOpen, "pending item confirmation remains owned loot UI");
        bytes[window + 0x28] = [0, 0];
        bytes[module + LootInteractionDecoder.CorpseIdRva] = BitConverter.GetBytes(9002u);
        var partial = decoder.Capture(module);
        Check(partial.Merge(null) is null && partial.Merge(complete)!.CorpseServerObjectId == 9002 && partial.Merge(complete)!.WindowOpen,
            "partial reads publish valid acknowledgement fields and retain unreadable UI fields");
        bytes[module + LootInteractionDecoder.CorpseIdRva] = BitConverter.GetBytes(0xCDCDCDCDu);
        Check(decoder.Capture(module).CorpseId is null, "poison must never become an acknowledgement");
        bytes[module + LootInteractionDecoder.CorpseIdRva] = BitConverter.GetBytes(0u);
        bytes[module + LootInteractionDecoder.WindowRva] = BitConverter.GetBytes(0UL);
        Check(decoder.Capture(module).Merge(complete) == LootInteractionSnapshot.Closed, "valid zero and absent UI replace prior data");
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.LootInteraction;
        var context = new GameApiReadContext("a", 1, "Aion.bin", "test");
        var now = DateTimeOffset.Now;
        store.Resolve("session-a", channel, context, OperationResult<LootInteractionSnapshot>.Ok(complete), now);
        Check(store.Resolve("session-a", channel, context, OperationResult<LootInteractionSnapshot>.Fail("partial"), now.AddDays(1)).Result.Value == complete,
            "failed reads retain the official publication without TTL expiry");
        Check(!store.Resolve("session-b", channel, context, OperationResult<LootInteractionSnapshot>.Fail("cold"), now).Result.Success,
            "new sessions never inherit an old corpse acknowledgement");
        store.ClearSession("session-a");
        Check(!store.Resolve("session-a", channel, context, OperationResult<LootInteractionSnapshot>.Fail("reset"), now).Result.Success,
            "explicit reset invalidates the prior publication");
        return Task.CompletedTask;
    }

    public static async Task ColdStartAndCancellationAsync()
    {
        using var f = new Fixture();
        var calls = 0;
        f.Api.LootInteractionCapture = _ => Task.FromResult(Interlocked.Increment(ref calls) < 3
            ? OperationResult<LootInteractionSnapshot>.Fail("injected incomplete read")
            : OperationResult<LootInteractionSnapshot>.Ok(LootInteractionSnapshot.Closed));
        var published = await f.Context.Snapshots.ReadLootInteractionAsync(f.Stop.Token);
        Check(calls == 3 && published.Value == LootInteractionSnapshot.Closed, "cold-start failures retry below the business boundary");
        f.Api.LootInteractionCapture = async token => { await Task.Delay(Timeout.Infinite, token); return OperationResult<LootInteractionSnapshot>.Ok(new(9001, true, false)); };
        using var stop = new CancellationTokenSource(60);
        try { await f.Context.Snapshots.ReadLootInteractionAsync(stop.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Check(!f.Remembered, "cancelled read cannot invent successful loot");
    }
}
