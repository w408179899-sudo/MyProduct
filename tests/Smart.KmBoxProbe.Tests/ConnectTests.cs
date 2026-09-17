using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Smart.KmBoxProbe;
using Smart.Runtime;
using Xunit;

namespace Smart.KmBoxProbe.Tests;

public sealed class ConnectTests
{
    private const uint ConnectCommand = 0xaf3c2828;
    private static UdpClient Server() => new(new IPEndPoint(IPAddress.Loopback, 0));
    private static KmBoxProbeOptions Options(UdpClient server, int milliseconds = 500) =>
        new("127.0.0.1", ((IPEndPoint)server.Client.LocalEndPoint!).Port, "AABBCCDD", TimeSpan.FromMilliseconds(milliseconds));
    private static async Task<UdpReceiveResult> ReceiveConnectAsync(UdpClient server)
    {
        var request = await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(16, request.Buffer.Length);
        Assert.Equal(0xaabbccddU, BinaryPrimitives.ReadUInt32LittleEndian(request.Buffer));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(request.Buffer.AsSpan(8)));
        Assert.Equal(ConnectCommand, BinaryPrimitives.ReadUInt32LittleEndian(request.Buffer.AsSpan(12)));
        return request;
    }

    [Fact]
    public async Task ExactlyOneConnectPacketAcceptsMatchingResponseAndReleasesSocketAndLease()
    {
        using var server = Server(); var options = Options(server); var leases = new InputLeaseRegistry();
        var run = ConnectProbe.RunAsync(options, leases);
        var request = await ReceiveConnectAsync(server);
        await server.SendAsync(request.Buffer, request.RemoteEndPoint);
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(report.Connected, report.Error); Assert.Equal("Connected", report.Outcome);
        Assert.Equal(1, report.SentPackets); Assert.Equal(16, report.ResponseBytes);
        Assert.True(report.SocketClosed); Assert.True(report.LeaseReleased);
        Assert.Equal(0, server.Available); // No automatic input release or disconnect packet.
        using var reacquired = leases.Acquire(options.DeviceId);
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("AABBCCDD", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2864434397", json);
    }

    [Theory]
    [InlineData("command")]
    [InlineData("index")]
    [InlineData("length")]
    public async Task IncorrectResponseIsRejectedBeforeMatchingResponse(string mismatch)
    {
        using var server = Server(); var options = Options(server); var leases = new InputLeaseRegistry();
        var run = ConnectProbe.RunAsync(options, leases);
        var request = await ReceiveConnectAsync(server);
        var wrong = (byte[])request.Buffer.Clone();
        if (mismatch == "command") BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(12), 0x123c2c2f);
        else if (mismatch == "index") BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(8), 123);
        else wrong = [0, 1];
        await server.SendAsync(wrong, request.RemoteEndPoint);
        await server.SendAsync(request.Buffer, request.RemoteEndPoint);
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(report.Connected, report.Error); Assert.Equal(1, report.RejectedResponses); Assert.Equal(1, report.SentPackets);
        using var reacquired = leases.Acquire(options.DeviceId);
    }

    [Fact]
    public async Task MatchingPacketFromDifferentSenderCannotSucceed()
    {
        using var server = Server(); using var other = Server(); var options = Options(server, 100); var leases = new InputLeaseRegistry();
        var run = ConnectProbe.RunAsync(options, leases);
        var request = await ReceiveConnectAsync(server);
        await other.SendAsync(request.Buffer, request.RemoteEndPoint);
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(report.Connected); Assert.Equal("TimedOut", report.Outcome); Assert.Equal(1, report.SentPackets);
        Assert.True(report.SocketClosed); Assert.True(report.LeaseReleased);
        using var reacquired = leases.Acquire(options.DeviceId);
    }

    [Fact]
    public async Task TimeoutClosesSocketAndReleasesLeaseWithoutSendingAdditionalPackets()
    {
        using var server = Server(); var options = Options(server, 100); var leases = new InputLeaseRegistry();
        var run = ConnectProbe.RunAsync(options, leases);
        await ReceiveConnectAsync(server);
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(report.Connected); Assert.Equal("TimedOut", report.Outcome); Assert.Equal(1, report.SentPackets);
        Assert.Equal(0, server.Available); Assert.True(report.SocketClosed); Assert.True(report.LeaseReleased);
        using var reacquired = leases.Acquire(options.DeviceId);
    }

    [Fact]
    public async Task CancellationEndsReceiveAndReleasesLease()
    {
        using var server = Server(); var options = Options(server); var leases = new InputLeaseRegistry();
        using var stop = new CancellationTokenSource();
        var run = ConnectProbe.RunAsync(options, leases, stop.Token);
        await ReceiveConnectAsync(server); await stop.CancelAsync();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(report.Connected); Assert.Equal("Cancelled", report.Outcome); Assert.Equal(1, report.SentPackets);
        using var reacquired = leases.Acquire(options.DeviceId);
    }

    [Fact]
    public async Task ExistingInputOwnerPreventsNetworkTraffic()
    {
        using var server = Server(); var options = Options(server); var leases = new InputLeaseRegistry();
        using var existing = leases.Acquire(options.DeviceId);
        var report = await ConnectProbe.RunAsync(options, leases);
        Assert.False(report.Connected); Assert.Equal(0, report.SentPackets); Assert.Equal(0, server.Available);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(options.DeviceId));
    }

    [Fact]
    public async Task ConfigLoaderReadsOnlyConnectionFieldsAndDoesNotSerializeDeviceCode()
    {
        var path = Path.Combine(Path.GetTempPath(), "smart-kmbox-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"IpAddress":"127.0.0.1","Port":12345,"Mac":"AA-BB-CC-DD","IsConfigured":true,"Unrelated":"ignored"}""");
            var options = await KmBoxProbeOptions.LoadAsync(path, TimeSpan.FromMilliseconds(500));
            Assert.Equal("kmbox:127.0.0.1:12345", options.DeviceId);
            Assert.DoesNotContain("AABBCCDD", options.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptyArgumentsAndHelpDoNotReadConfigurationOrStartNetworking()
    {
        Assert.Equal(0, await Program.Main([]));
        Assert.Equal(0, await Program.Main(["--help"]));
        Assert.Equal(2, await Program.Main(["--timeout-ms", "100"]));
    }
}
