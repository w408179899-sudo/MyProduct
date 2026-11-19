using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class AccountSessionStatisticsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("session statistics lifecycle death deduplication and account isolation", LifecycleAsync),
        ("session statistics trusted observations and old worker wire compatibility", TrustedReaderAndWireAsync),
        ("session statistics legacy confirmed loot excludes retries and failed attempts", () => LootTouchTests.SessionStatisticsAsync(CombatFlowVersion.Legacy)),
        ("session statistics v2 confirmed loot excludes retries and failed attempts", () => LootTouchTests.SessionStatisticsAsync(CombatFlowVersion.CoordinatedV2)),
        ("session statistics UI counts arrive over IPC and reset on restart", RoadhogTestRunner.RunSerially(MultiAccountUiTests.SessionStatisticsAsync))
    ];

    public static Task LifecycleAsync()
    {
        var states = new AccountRuntimeManager(NoOpRoadhogLogger.Instance);
        var first = Account("one", 1);
        var second = Account("two", 2);
        states.MarkLoot(first.AccountName);
        Check(states.Snapshot().Count == 0, "loot cannot create statistics for an unstarted account");
        states.MarkStarting(first); states.MarkStarting(second);
        states.MarkRunning(first.AccountName, 1); states.MarkRunning(second.AccountName, 2);
        var observe = states.CreatePlayerInfoObserver(first)!;
        var now = DateTimeOffset.Now;
        observe(Player(first, 100, now));
        observe(Player(first, 0, now.AddSeconds(1)));
        observe(Player(first, 0, now.AddSeconds(2)));
        observe(Player(first, 0, now.AddSeconds(3)) with { MaxHp = 0 });
        observe(Player(first, 0, now.AddSeconds(4)));
        Check(Read(first).DeathCount == 1, "repeated death and unknown-health frames count once");
        observe(Player(first, 100, now));
        observe(Player(second, 100, now.AddSeconds(5)));
        observe(Player(first, 0, now.AddSeconds(6)));
        Check(Read(first).DeathCount == 1, "late alive and other-character frames cannot rearm a death");
        observe(Player(first, 100, now.AddSeconds(7)));
        observe(Player(first, 0, now.AddSeconds(8)));
        states.MarkLoot(first.AccountName); states.MarkLoot(first.AccountName);
        states.MarkLoot(second.AccountName);
        Check(Read(first) is { DeathCount: 2, LootCount: 2 } && Read(second) is { DeathCount: 0, LootCount: 1 },
            "revive rearms death and accounts own independent counts");
        states.RequestStop(first.AccountName);
        observe(Player(first, 100, now.AddSeconds(9)));
        states.MarkLoot(first.AccountName);
        states.MarkStopped(first.AccountName);
        Check(Read(first) is { DeathCount: 2, LootCount: 2 }, "stopping freezes the current run totals");
        states.MarkStarting(first);
        observe(Player(first, 0, now.AddSeconds(10)));
        Check(Read(first) is { DeathCount: 0, LootCount: 0 }, "restart clears both counts and rejects old reader observations");
        states.CreatePlayerInfoObserver(first)!(Player(first, 0, now.AddSeconds(11)));
        Check(Read(first).DeathCount == 1 && Read(second).LootCount == 1, "starting dead counts once without changing siblings");
        states.MarkFailed(first.AccountName, "fixture failure");
        states.MarkLoot(first.AccountName);
        Check(Read(first).LootCount == 0, "failed run accepts no new loot totals");
        return Task.CompletedTask;

        AccountRuntimeSnapshot Read(AccountConfig account) => states.Snapshot().Single(s => s.AccountName == account.AccountName);
    }

    public static async Task TrustedReaderAndWireAsync()
    {
        var account = Account("one", 1);
        var states = new AccountRuntimeManager(NoOpRoadhogLogger.Instance);
        states.MarkStarting(account);
        var provider = new FakeGameApi { Player = Player(account, 100, DateTimeOffset.Now) };
        provider.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Fail("failed player read"));
        provider.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(provider.Player with { CurrentHp = 0, Position = null }));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reader = new RoadhogSnapshotReaderFactory(provider, states.CreatePlayerInfoObserver)
            .Create(account, NoOpRoadhogLogger.Instance, stop.Token);
        await reader.ReadPlayerAsync();
        Check(states.Snapshot().Single().DeathCount == 0 && provider.PlayerReadCount == 3,
            "failed or unpublished reads do not count as death or add telemetry reads");
        provider.Player = provider.Player with { CurrentHp = 0, CapturedAt = DateTimeOffset.Now.AddSeconds(1) };
        await reader.ReadPlayerAsync(); await reader.ReadPlayerAsync();
        states.MarkLoot(account.AccountName);
        var status = new WorkerStatus { Snapshot = states.Snapshot().Single() };
        var wire = JsonSerializer.Deserialize<WorkerStatus>(JsonSerializer.Serialize(status))!;
        Check(wire.Snapshot is { DeathCount: 1, LootCount: 1 }, "both counts survive worker status JSON");
        var reads = provider.PlayerReadCount;
        for (var i = 0; i < 20; i++) states.Snapshot();
        Check(provider.PlayerReadCount == reads, "UI polling never causes extra client reads");
        var legacy = JsonSerializer.SerializeToNode(wire)!;
        legacy["Snapshot"]!.AsObject().Remove("DeathCount");
        legacy["Snapshot"]!.AsObject().Remove("LootCount");
        Check(legacy.Deserialize<WorkerStatus>()!.Snapshot is { DeathCount: null, LootCount: null },
            "an old worker cannot masquerade as zero deaths and pickups");
    }

    internal static void Populate(AccountRuntimeManager states, AccountConfig account, int deaths, int loots)
    {
        var observe = states.CreatePlayerInfoObserver(account)!;
        var now = DateTimeOffset.Now;
        for (var i = 0; i < deaths; i++)
        {
            observe(Player(account, 100, now.AddTicks(i * 2)));
            observe(Player(account, 0, now.AddTicks(i * 2 + 1)));
        }
        for (var i = 0; i < loots; i++) states.MarkLoot(account.AccountName);
    }

    private static AccountConfig Account(string name, int device) => new()
    { AccountName = name, CharacterName = name, ProcessId = device, VmmDeviceName = "fpga://devindex=" + device };

    private static PlayerSnapshot Player(AccountConfig account, uint hp, DateTimeOffset time) =>
        new(1, 0, account.CharacterName, hp, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), time);

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
