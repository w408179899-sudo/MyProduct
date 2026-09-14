using System.Reflection;
using Roadhog.Application.Channels;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class ChannelTransitionTests
{
    private static readonly PlayerSnapshot Player = new(7, 0, "test", 100, 100, 100, 100, 0,
        new Vector3Snapshot(1, 2, 3), DateTimeOffset.UtcNow);
    private static ChannelTransitionSnapshot Ready(int number = 3, uint map = 100) =>
        new(true, Player, new(number - 1, 3, map, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
    private static ChannelTransitionSnapshot Loading() => new(false, null, null, DateTimeOffset.UtcNow);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static async Task DelayedAndSlowLoadingAsync()
    {
        var clock = new ManualTimeProvider();
        var start = clock.GetUtcNow();
        var api = new FakeGameApi();
        var guards = 0;
        api.TransitionRead = () =>
        {
            var elapsed = (clock.GetUtcNow() - start).TotalSeconds;
            return elapsed < 4.8 ? Ready(1) : elapsed < 70 ? Loading() : Ready();
        };
        var logger = new InMemoryRoadhogLogger();
        var result = await new ChannelTransitionWaiter(logger, clock, Advance).WaitAsync(
            api.Create(new AccountConfig(), logger, CancellationToken.None), "test", 3, 100, CancellationToken.None,
            () => { guards++; Require(clock.GetUtcNow() - start < TimeSpan.FromSeconds(5), "no combat or world scan during loading"); return Task.CompletedTask; });
        Require(result.Success && clock.GetUtcNow() - start >= TimeSpan.FromSeconds(70), "must await delayed loading and recovered player beyond sixty seconds");
        Require(logger.Entries.Count(e => e.EventName == "channel_switch.loading_slow") == 1, "slow loading warning must appear once without resuming work");
        Require(guards > 0 && api.ChannelReadCount == 0 && api.PlayerReadCount == 0, "recovery must use cohesive scene snapshot, not old independent caches");
        Task Advance(TimeSpan duration, CancellationToken token) { token.ThrowIfCancellationRequested(); clock.Advance(duration); return Task.CompletedTask; }
    }

    public static async Task NoLoadingAndWrongDestinationAsync()
    {
        var clock = new ManualTimeProvider();
        var api = new FakeGameApi { TransitionRead = () => Ready() }; // An already changed channel alone is insufficient.
        var logger = new InMemoryRoadhogLogger();
        var waiter = new ChannelTransitionWaiter(logger, clock, (duration, token) => { token.ThrowIfCancellationRequested(); clock.Advance(duration); return Task.CompletedTask; });
        var reader = api.Create(new AccountConfig(), logger, CancellationToken.None);
        var start = clock.GetUtcNow();
        Require(!(await waiter.WaitAsync(reader, "test", 3, 100, CancellationToken.None)).Success, "channel alone must not bypass loading/player recovery");
        Require(clock.GetUtcNow() - start == TimeSpan.FromSeconds(5), "rejection must return after five second entry window");
        var calls = 0;
        api.TransitionRead = () => ++calls == 1 ? Loading() : Ready(2);
        Require(!(await waiter.WaitAsync(reader, "test", 3, 100, CancellationToken.None)).Success, "wrong recovered channel must remain retryable");
        calls = 0;
        api.TransitionRead = () => ++calls == 1 ? Loading() : Ready(3, 200);
        Require(!(await waiter.WaitAsync(reader, "test", 3, 100, CancellationToken.None)).Success, "different map cannot confirm requested switch");
    }

    public static async Task StopDuringLoadingAsync()
    {
        using var stop = new CancellationTokenSource();
        var api = new FakeGameApi { TransitionRead = Loading };
        var logger = new InMemoryRoadhogLogger();
        var clock = new ManualTimeProvider();
        var polls = 0;
        var task = new ChannelTransitionWaiter(logger, clock, (duration, token) =>
        {
            clock.Advance(duration);
            if (++polls == 350) stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }).WaitAsync(api.Create(new AccountConfig(), logger, stop.Token), "test", 3, 100, stop.Token,
            () => throw new InvalidOperationException("must not read combat while loading"));
        try { await task; throw new InvalidOperationException("stop must cancel loading wait"); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Require(logger.Entries.Any(e => e.EventName == "channel_switch.loading_slow"), "stop still works after slow-load warning");
    }

    public static async Task StopDuringPendingReadAsync()
    {
        using var stop = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeGameApi { TransitionReadAsync = token => pending.Task.WaitAsync(token) };
        var clock = new ManualTimeProvider();
        var start = clock.GetUtcNow();
        var logger = new InMemoryRoadhogLogger();
        var task = new ChannelTransitionWaiter(logger, clock, async (duration, token) =>
        {
            clock.Advance(duration);
            if (clock.GetUtcNow() - start >= TimeSpan.FromSeconds(61)) stop.Cancel();
            token.ThrowIfCancellationRequested();
            await Task.Yield();
        }).WaitAsync(api.Create(new AccountConfig(), logger, stop.Token), "test", 3, 100, stop.Token);
        try { await task; throw new Exception("cold/reconnecting read must be cancellable"); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Require(api.TransitionReadCount == 1, "must not create overlapping provider reads while one is pending");
        Require(logger.Entries.Count(e => e.EventName == "channel_switch.loading_slow") == 1,
            "pending provider read must warn once and keep waiting until user stops");
    }

    public static Task DecoderAndCachedPlayerAsync()
    {
        const ulong b = 0x10000000;
        var memory = new Dictionary<ulong, byte[]>
        {
            [b + ChannelTransitionDecoder.StateRva] = BitConverter.GetBytes(15U),
            [b + ChannelTransitionDecoder.PhaseRva] = BitConverter.GetBytes(2U),
            [b + ChannelTransitionDecoder.LocalIdRva] = BitConverter.GetBytes((ushort)7),
            [b + 0xD71CB0] = BitConverter.GetBytes(2U).Concat(BitConverter.GetBytes(3U)).ToArray(),
            [b + 0xD6689C] = BitConverter.GetBytes(100U)
        };
        var directPlayerCalls = 0;
        var failPlayer = false;
        PlayerSnapshot DirectPlayer()
        {
            directPlayerCalls++;
            if (failPlayer) throw new InvalidDataException("new character read failed");
            return Player;
        }
        var decoder = new ChannelTransitionDecoder((address, _) => memory[address], DirectPlayer);
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var context = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        var token = AionVmmSnapshotChannels.ChannelTransition;
        var now = DateTimeOffset.UtcNow;
        var ready = decoder.Read(b);
        Require(ready.IsReady && directPlayerCalls == 1, "ready publication requires a direct complete player capture");
        store.Resolve("session", AionVmmSnapshotChannels.Player, context, OperationResult<PlayerSnapshot>.Ok(Player), now);
        store.Resolve("session", token, context, OperationResult<ChannelTransitionSnapshot>.Ok(ready), now);
        foreach (var values in new[] { (20U, 2U, (ushort)0), (15U, 2U, (ushort)0), (15U, 1U, (ushort)0) })
        {
            memory[b + ChannelTransitionDecoder.StateRva] = BitConverter.GetBytes(values.Item1);
            memory[b + ChannelTransitionDecoder.PhaseRva] = BitConverter.GetBytes(values.Item2);
            memory[b + ChannelTransitionDecoder.LocalIdRva] = BitConverter.GetBytes(values.Item3);
            var loading = decoder.Read(b);
            Require(!loading.IsReady && loading.Player is null && directPlayerCalls == 1, "early channel/state restoration cannot imply player recovery");
            store.Resolve("session", token, context, OperationResult<ChannelTransitionSnapshot>.Ok(loading), now);
        }
        memory[b + ChannelTransitionDecoder.StateRva] = BitConverter.GetBytes(15U);
        memory[b + ChannelTransitionDecoder.PhaseRva] = BitConverter.GetBytes(2U);
        memory[b + ChannelTransitionDecoder.LocalIdRva] = BitConverter.GetBytes((ushort)7);
        failPlayer = true;
        OperationResult<ChannelTransitionSnapshot> failed;
        try { decoder.Read(b); throw new Exception("failed new player capture cannot use cached player"); }
        catch (InvalidDataException ex) { failed = OperationResult<ChannelTransitionSnapshot>.Fail(ex.Message); }
        Require(!store.Resolve("session", token, context, failed, now).Result.Value!.IsReady, "retain loading despite old successful player cache");
        Require(!store.Resolve("new-session", token, context, failed, now).Result.Success, "new session must not inherit scene or player");
        failPlayer = false;
        Require(store.Resolve("session", token, context, OperationResult<ChannelTransitionSnapshot>.Ok(decoder.Read(b)), now).Result.Value!.IsReady,
            "successful direct capture must immediately publish recovery");
        memory[b + ChannelTransitionDecoder.LocalIdRva] = new byte[1];
        MustReject(() => decoder.Read(b), "short local ID read");
        memory[b + ChannelTransitionDecoder.LocalIdRva] = BitConverter.GetBytes((ushort)7);
        var stateReads = 0;
        var changing = new ChannelTransitionDecoder((address, _) => address == b + ChannelTransitionDecoder.StateRva && ++stateReads == 2
            ? BitConverter.GetBytes(20U) : memory[address], DirectPlayer);
        MustReject(() => changing.Read(b), "scene changed during player capture");
        var mismatch = new ChannelTransitionDecoder((address, _) => memory[address], () => Player with { EntityId = 8 });
        MustReject(() => mismatch.Read(b), "different local player");
        return Task.CompletedTask;
    }

    public static Task ReconnectClassificationAsync()
    {
        var method = typeof(AionVmmGameApi).GetMethod("ShouldReconnectAfterPlayerReadFailure", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool ShouldReconnect(string error) => (bool)method.Invoke(null, new object[] { error })!;
        Require(!ShouldReconnect("local player is not present in scene"), "valid absent player during loading must not reconnect");
        Require(ShouldReconnect("failed to read local entity id at Game.dll+0xD6CB08"), "genuine transport read failure must retain reconnect path");
        return Task.CompletedTask;
    }

    private static void MustReject(Action read, string reason)
    {
        try { read(); throw new Exception("must reject " + reason); }
        catch (InvalidDataException) { }
    }
}
