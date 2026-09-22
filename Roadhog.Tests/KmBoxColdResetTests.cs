using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Roadhog.Infrastructure.Input;

internal static class KmBoxColdResetTests
{
    private const uint Connect = 0xaf3c2828;
    private const uint MouseButton = 0x9823ae8d;
    private const uint KeyboardAll = 0x123c2c2f;

    public static async Task ColdStartClearsInheritedInputAsync()
    {
        await using var peer = new LoopbackPeer();
        using var input = CreateInput(peer);
        var result = await input.ReleaseAllAsync();
        Require(result.Success, "fresh input reset succeeds after a real loopback handshake");
        AssertReset(peer.Packets.ToArray());
        Require(!peer.MouseHeld && !peer.KeyboardHeld,
            "fresh worker clears simulated input held by a killed predecessor before any business action");

        Require((await input.ReleaseAllAsync()).Success, "repeated reset remains safe");
        var packets = peer.Packets.ToArray();
        Require(packets.Count(packet => Command(packet) == Connect) == 1, "connected reset does not re-handshake");
        Require(packets.Length == 5 && packets.Skip(3).All(IsNeutral), "repeated reset sends only neutral input");
    }

    public static async Task HandshakeFailureCanRetryAsync()
    {
        await using var peer = new LoopbackPeer { ShouldReply = command => command != Connect };
        using var input = CreateInput(peer);
        var result = await input.ReleaseAllAsync();
        Require(!result.Success && result.Error?.Contains("connect failed", StringComparison.OrdinalIgnoreCase) == true,
            "unanswered handshake fails reset rather than falsely reporting neutral input");
        Require(peer.Packets.Count == 1 && peer.MouseHeld && peer.KeyboardHeld,
            "failed handshake sends no keyboard or mouse packet");

        peer.ShouldReply = _ => true;
        Require((await input.ReleaseAllAsync()).Success, "failed connection can be retried");
        AssertReset(peer.Packets.Skip(1).ToArray());
        Require(!peer.MouseHeld && !peer.KeyboardHeld, "retry clears inherited state");
    }

    public static async Task CancellationDoesNotReportSuccessAsync()
    {
        await using var peer = new LoopbackPeer { ShouldReply = command => command != Connect };
        using var input = CreateInput(peer, receiveTimeoutMs: 5000);
        using var cancellation = new CancellationTokenSource();
        var resetting = input.ReleaseAllAsync(cancellation.Token);
        await peer.FirstPacket.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        try { await resetting; throw new InvalidOperationException("cancelled reset must not return success"); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require(peer.Packets.Count == 1 && peer.MouseHeld && peer.KeyboardHeld,
            "cancelled handshake does not send input or pretend it released inherited state");

        peer.ShouldReply = _ => true;
        Require((await input.ReleaseAllAsync()).Success, "cancelled connection releases its locks and can retry");
        AssertReset(peer.Packets.Skip(1).ToArray());
    }

    public static async Task ReleaseFailureReconnectsAsync()
    {
        await using var peer = new LoopbackPeer { ShouldReply = command => command != MouseButton };
        using var input = CreateInput(peer);
        var result = await input.ReleaseAllAsync();
        Require(!result.Success && result.Error?.Contains("release all failed", StringComparison.OrdinalIgnoreCase) == true,
            "missing neutral-input acknowledgement reports a release failure");
        var failed = peer.Packets.ToArray();
        Require(failed.Length == 2 && Command(failed[0]) == Connect && IsNeutral(failed[1]),
            "failed reset performs only handshake and neutral mouse input");
        Require(peer.KeyboardHeld, "keyboard release remains unconfirmed when mouse acknowledgement fails");

        peer.ShouldReply = _ => true;
        Require((await input.ReleaseAllAsync()).Success, "release failure reconnects before retrying neutral input");
        AssertReset(peer.Packets.Skip(2).ToArray());
        Require(!peer.MouseHeld && !peer.KeyboardHeld, "retry resets both device states");
    }

    private static KmBoxNetKeyboardInput CreateInput(LoopbackPeer peer, int receiveTimeoutMs = 1000) => new(new KmBoxNetKeyboardInputOptions
    {
        IpAddress = IPAddress.Loopback.ToString(), Port = peer.Port, Mac = "12345678",
        SendTimeoutMs = 1000, ReceiveTimeoutMs = receiveTimeoutMs, CommandTimeoutMs = 1000
    });

    private static void AssertReset(byte[][] packets)
    {
        Require(packets.Length == 3, "reset sends exactly handshake, neutral mouse and neutral keyboard");
        Require(Command(packets[0]) == Connect && packets[0].Length == 16, "first packet is handshake only");
        Require(Command(packets[1]) == MouseButton && packets[1].Length == 72 && IsNeutral(packets[1]),
            "mouse reset clears all buttons, movement and wheel fields");
        Require(Command(packets[2]) == KeyboardAll && packets[2].Length == 28 && IsNeutral(packets[2]),
            "keyboard reset clears modifiers and every held key");
    }

    private static uint Command(byte[] packet) => BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12, 4));
    private static bool IsNeutral(byte[] packet) => packet.Length > 16 &&
        Command(packet) is MouseButton or KeyboardAll && packet.Skip(16).All(value => value == 0);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // Real UDP transport, bound exclusively to loopback; the peer starts with predecessor input held.
    private sealed class LoopbackPeer : IAsyncDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _receiving;
        private Func<uint, bool> _shouldReply = _ => true;
        public ConcurrentQueue<byte[]> Packets { get; } = new();
        public TaskCompletionSource FirstPacket { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool MouseHeld { get; private set; } = true;
        public bool KeyboardHeld { get; private set; } = true;
        public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        public Func<uint, bool> ShouldReply { set => Volatile.Write(ref _shouldReply, value); }

        public LoopbackPeer() => _receiving = ReceiveAsync();

        private async Task ReceiveAsync()
        {
            try
            {
                while (true)
                {
                    var received = await _udp.ReceiveAsync(_lifetime.Token);
                    var packet = received.Buffer;
                    var command = Command(packet);
                    if (command == MouseButton) MouseHeld = !IsNeutral(packet);
                    if (command == KeyboardAll) KeyboardHeld = !IsNeutral(packet);
                    Packets.Enqueue(packet);
                    FirstPacket.TrySetResult();
                    if (Volatile.Read(ref _shouldReply)(command))
                        await _udp.SendAsync(packet.AsMemory(0, 16), received.RemoteEndPoint, _lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            try { await _receiving; }
            finally { _udp.Dispose(); _lifetime.Dispose(); }
        }
    }
}
