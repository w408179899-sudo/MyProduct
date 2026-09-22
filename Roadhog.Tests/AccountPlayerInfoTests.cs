using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class AccountPlayerInfoTests
{
    public static Task LifecycleAndIsolationAsync()
    {
        var states = new AccountRuntimeManager(NoOpRoadhogLogger.Instance);
        var first = Account("one", "角色一", 1);
        var second = Account("two", "角色二", 2);
        Require(states.CreatePlayerInfoObserver(first) is null, "unstarted accounts cannot publish display data");
        states.MarkStarting(first); states.MarkStarting(second);
        var observeFirst = states.CreatePlayerInfoObserver(first)!;
        var observeSecond = states.CreatePlayerInfoObserver(second)!;
        var now = DateTimeOffset.Now;
        observeFirst(Player(first, 50, "精灵星", now));
        observeSecond(Player(second, 32, "守护星", now));
        Require(Read(first).CharacterLevel == 50 && Read(second).CharacterClass == "守护星", "accounts publish independently");
        observeFirst(Player(first, 51, "精灵星", now.AddSeconds(1)));
        observeFirst(Player(first, 49, "剑星", now.AddSeconds(-1)));
        observeFirst(Player(second, 60, "杀星", now.AddSeconds(2)));
        Require(Read(first).CharacterLevel == 51 && Read(first).CharacterClass == "精灵星", "level-up survives late or foreign-character telemetry");
        var wrongDevice = first.Clone(); wrongDevice.VmmDeviceName = second.VmmDeviceName;
        Require(states.CreatePlayerInfoObserver(wrongDevice) is null, "another device does not observe this account");
        states.RequestStop(first.AccountName);
        observeFirst(Player(first, 60, "杀星", now.AddSeconds(3)));
        Require(Read(first).CharacterLevel == 51, "stopping blocks subsequent telemetry");
        states.MarkStopped(first.AccountName);
        Require(Read(first).CharacterLevel == 0 && Read(first).CharacterClass.Length == 0, "stop clears previous-session display");
        states.MarkStarting(first);
        observeFirst(Player(first, 60, "杀星", now.AddSeconds(4)));
        Require(Read(first).CharacterLevel == 0, "late old-reader result cannot cross a restart");
        states.CreatePlayerInfoObserver(first)!(Player(first, 52, "精灵星", now.AddSeconds(5)));
        states.MarkFailed(first.AccountName, "fixture crash");
        Require(Read(first).CharacterLevel == 0 && Read(second).CharacterLevel == 32, "crash clears only the affected account");
        return Task.CompletedTask;

        AccountRuntimeSnapshot Read(AccountConfig config) => states.Snapshot().Single(s => s.AccountName == config.AccountName);
    }

    public static async Task TrustedReaderAndWireAsync()
    {
        var config = Account("one", "角色一", 1);
        var states = new AccountRuntimeManager(NoOpRoadhogLogger.Instance);
        states.MarkStarting(config);
        var provider = new FakeGameApi { Player = Player(config, 50, "精灵星", DateTimeOffset.Now) };
        provider.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Fail("partial DMA read"));
        provider.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(provider.Player with { Position = null, Level = 99 }));
        var observed = 0;
        var factory = new RoadhogSnapshotReaderFactory(provider, c =>
        {
            var update = states.CreatePlayerInfoObserver(c)!;
            return player => { observed++; update(player); };
        });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reader = factory.Create(config, NoOpRoadhogLogger.Instance, stop.Token);
        var published = await reader.ReadPlayerAsync();
        Require(provider.PlayerReadCount == 3 && observed == 1 && published.Value.Level == 50,
            "only a trusted publication reaches telemetry, without an extra read");
        provider.Player = provider.Player with { Level = 51, CapturedAt = DateTimeOffset.Now.AddSeconds(1) };
        await reader.ReadPlayerAsync(published.Version);
        var wire = JsonSerializer.Deserialize<WorkerStatus>(JsonSerializer.Serialize(new WorkerStatus
        { Snapshot = states.Snapshot().Single() }))!;
        Require(wire.Snapshot is { CharacterLevel: 51, CharacterClass: "精灵星" }, "level-up and localized class survive worker JSON status");
        var reads = provider.PlayerReadCount;
        for (var i = 0; i < 20; i++) states.Snapshot();
        Require(provider.PlayerReadCount == reads, "status polling never reads the game");
        stop.Cancel();
        try { await reader.ReadPlayerAsync(); throw new InvalidOperationException("cancelled reader completed"); }
        catch (OperationCanceledException) { }
        Require(observed == 2, "cancelled read publishes no telemetry");
        var legacy = JsonSerializer.SerializeToNode(wire)!;
        legacy["Snapshot"]!.AsObject().Remove("CharacterLevel");
        legacy["Snapshot"]!.AsObject().Remove("CharacterClass");
        var oldStatus = legacy.Deserialize<WorkerStatus>()!;
        Require(oldStatus.Snapshot is { CharacterLevel: 0, CharacterClass: "" }, "older worker status remains compatible");
    }

    private static AccountConfig Account(string name, string character, int device) => new()
    { AccountName = name, CharacterName = character, ProcessId = 100 + device, VmmDeviceName = "fpga://devindex=" + device };

    private static PlayerSnapshot Player(AccountConfig config, ushort level, string characterClass, DateTimeOffset time) =>
        new(1, 0, config.CharacterName, 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), time,
            Level: level, CharacterClass: characterClass);

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
