using System.Drawing;
using Roadhog.Application.Login;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class LoginOnlinePriorityTests
{
    private const ulong GameBase = 0x492f0000;
    private sealed class WorldMemory
    {
        internal readonly Dictionary<ulong, byte[]> Data = new()
        {
            [GameBase + 0xd6ca90] = BitConverter.GetBytes(15),
            [GameBase + 0xd647c0 + 0x20dc] = BitConverter.GetBytes(220050000u),
            [GameBase + 0xd6cb08] = BitConverter.GetBytes((ushort)65535),
            [GameBase + 0xd63990 + 336 * 8] = new byte[35 * 8]
        };
        internal PlayerSnapshot Player = new(65535, 0, "初次见面", 1, 1, 1, 1, 0, new(1, 2, 3), DateTimeOffset.UtcNow);
        internal int PlayerReads;
        internal LoginObservation? Capture() => LoginNativeReader.CaptureWorld(
            (address, size) => Data.TryGetValue(address, out var value) ? value : [],
            () => { PlayerReads++; return Player; }, GameBase, "script4-session", 588, 100);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Require(rejected, message); }
    private static LoginObservation Scene(LoginStage stage, LoginAction action = LoginAction.None) =>
        new(new("script4-session", stage, action, stage.ToString(), stage.ToString()), 3700, 100, "0x10001", new(1, 1), new(0, 0, 100, 100));

    private sealed class Session(Func<LoginObservation> capture) : IAutoLoginSession
    {
        private readonly LoginSnapshotPublisher _publisher = new(new("脚本4", 588, "Aion.bin", "fpga://devindex=3", true));
        internal int Inputs;
        public Task<LoginSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(
            _publisher.Capture(lifecycle => { lifecycle("script4-session"); return capture(); })!.Snapshot);
        public Task<bool> ExecuteAsync(LoginSnapshot expected, LoginAction action, CancellationToken token)
        { Inputs++; throw new InvalidOperationException("An online character must not receive login input."); }
        public Task ReleaseInputAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { _publisher.Dispose(); return ValueTask.CompletedTask; }
    }

    internal static async Task OnlineSkipsUnreadableUuAsync()
    {
        var memory = new WorldMemory();
        int uuReads = 0, loginReads = 0;
        await using var session = new Session(() => LoginNativeReader.CaptureInPriorityOrder(memory.Capture,
            () => { uuReads++; throw new InvalidOperationException("Incomplete window inventory read 0, length 24"); },
            () => { loginReads++; throw new InvalidOperationException("Game window table is not initialized."); }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await new AutoLoginRunner().RunAsync(session, "初次见面", null, timeout.Token);
        Require(result.Success && result.Value == 588, "The script4 cold start must accept the complete online scene.");
        Require(uuReads == 0 && loginReads == 0 && session.Inputs == 0,
            "Online startup cannot read unrelated UU/game window tables, reopen UU or submit login input.");
    }

    internal static Task ExitPopupAndReconnectPriorityAsync()
    {
        var memory = new WorldMemory();
        const ulong dialog = 0x500000;
        BitConverter.GetBytes(dialog).CopyTo(memory.Data[GameBase + 0xd63990 + 336 * 8], 34 * 8);
        memory.Data[dialog + 0x28] = BitConverter.GetBytes(1u);
        var reconnect = Scene(LoginStage.AcceleratorReconnect, LoginAction.Reaccelerate);
        var exit = Scene(LoginStage.Loading);
        int uuReads = 0, loginReads = 0;
        var scene = LoginNativeReader.CaptureInPriorityOrder(memory.Capture, () => { uuReads++; return reconnect; },
            () => { loginReads++; return exit; });
        Require(scene == reconnect && uuReads == 1 && loginReads == 0 && memory.PlayerReads == 0,
            "A visible exit message invalidates retained world data; reconnect still precedes login pages.");
        scene = LoginNativeReader.CaptureInPriorityOrder(memory.Capture, () => null, () => exit);
        Require(scene == exit, "Without a UU reconnect popup, the existing game message handling must run.");
        memory.Data[dialog + 0x28] = BitConverter.GetBytes(0u);
        Require(LoginNativeReader.CaptureInPriorityOrder(memory.Capture, () => reconnect, () => exit).Snapshot.Stage == LoginStage.InWorld,
            "A hidden old message cannot block an online character.");
        Require(LoginNativeReader.CaptureInPriorityOrder(() => null, () => reconnect, () => exit) == reconnect,
            "No game process or a loading game still permits UU reconnect.");
        Reject(() => LoginNativeReader.CaptureInPriorityOrder(() => null,
            () => throw new InvalidOperationException("partial UU windows"), () => exit),
            "When UU is needed, a failed read cannot be treated as an absent reconnect popup.");
        return Task.CompletedTask;
    }

    internal static Task IncompleteWorldCannotBecomeOnlineAsync()
    {
        var memory = new WorldMemory();
        foreach (ulong address in memory.Data.Keys.ToArray())
        {
            var saved = memory.Data[address]; memory.Data[address] = [];
            Reject(() => memory.Capture(), "A missing required world field must remain a provider failure.");
            memory.Data[address] = saved;
        }
        foreach (uint map in new[] { 0u, uint.MaxValue })
        {
            memory.Data[GameBase + 0xd647c0 + 0x20dc] = BitConverter.GetBytes(map);
            Require(memory.Capture() is null, "A loading map cannot prove online.");
        }
        memory = new WorldMemory(); memory.Data[GameBase + 0xd6ca90] = BitConverter.GetBytes(12);
        Require(memory.Capture() is null && memory.PlayerReads == 0, "Character selection must not reuse old player data.");
        memory = new WorldMemory(); memory.Data[GameBase + 0xd6cb08] = BitConverter.GetBytes((ushort)0);
        Require(memory.Capture() is null, "A missing local entity cannot prove online.");
        memory = new WorldMemory();
        foreach (var invalid in new[] { memory.Player with { EntityId = 0 }, memory.Player with { Position = null }, memory.Player with { CharacterName = "" } })
        {
            memory.Player = invalid;
            Require(memory.Capture() is null, "All player identity fields remain required.");
        }
        memory = new WorldMemory();
        using var publisher = new LoginSnapshotPublisher(new("脚本4", 588, "Aion.bin", "fpga://devindex=3", true));
        LoginObservation? Read() => publisher.Capture(lifecycle =>
        {
            lifecycle("script4-session");
            return LoginNativeReader.CaptureInPriorityOrder(memory.Capture,
                () => throw new InvalidOperationException("Unreadable UU"), () => Scene(LoginStage.Loading));
        });
        var state = memory.Data[GameBase + 0xd6ca90]; memory.Data[GameBase + 0xd6ca90] = [];
        Require(Read() is null, "A cold failed world read must not fabricate an online snapshot.");
        memory.Data[GameBase + 0xd6ca90] = state;
        var official = Read(); Require(official?.Snapshot.Stage == LoginStage.InWorld, "The first complete world read publishes immediately.");
        memory.Data[GameBase + 0xd6ca90] = [];
        Require(Read() == official, "A later failed world read retains only the official same-session scene.");
        return Task.CompletedTask;
    }
}
