using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using Smart.Runtime;

namespace Smart.KmBoxProbe;

public sealed record ConnectProbeReport(bool Connected, string Outcome, string Endpoint, int SentPackets,
    int RejectedResponses, int ResponseBytes, double ElapsedMilliseconds, bool SocketClosed, bool LeaseReleased, string? Error);

public static class ConnectProbe
{
    // Same 16-byte little-endian Connect header as the vendored KMBox Net protocol; no input payload or opcode exists here.
    private const uint ConnectCommand = 0xaf3c2828;
    public static async Task<ConnectProbeReport> RunAsync(KmBoxProbeOptions options, InputLeaseRegistry leases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(leases);
        var watch = Stopwatch.StartNew();
        IDisposable? lease = null; UdpClient? socket = null;
        var connected = false; var outcome = "Failed"; string? error = null;
        var sent = 0; var rejected = 0; var responseBytes = 0;
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            lease = leases.Acquire(options.DeviceId);
            socket = new UdpClient(AddressFamily.InterNetwork);
            // A connected UDP socket only accepts datagrams from the configured endpoint.
            socket.Connect(options.Address, options.Port);
            var packet = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, options.DeviceCode);
            RandomNumberGenerator.Fill(packet.AsSpan(4, 4));
            const uint index = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), index);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), ConnectCommand);
            await socket.SendAsync(packet, lifetime.Token).ConfigureAwait(false);
            sent = 1;
            while (true)
            {
                var response = await socket.ReceiveAsync(lifetime.Token).ConfigureAwait(false);
                if (response.RemoteEndPoint.Address.Equals(options.Address) && response.RemoteEndPoint.Port == options.Port &&
                    response.Buffer.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(response.Buffer.AsSpan(8)) == index &&
                    BinaryPrimitives.ReadUInt32LittleEndian(response.Buffer.AsSpan(12)) == ConnectCommand)
                {
                    responseBytes = response.Buffer.Length; connected = true; outcome = "Connected"; break;
                }
                rejected++;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            outcome = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimedOut";
            error = "No matching Connect response was accepted before cancellation or the deadline.";
        }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        finally
        {
            // Do not use KmBoxNetDevice.Dispose/Disconnect: those methods send ReleaseAll input reports.
            try { socket?.Dispose(); }
            finally { lease?.Dispose(); }
        }
        watch.Stop();
        return new(connected, outcome, options.Address + ":" + options.Port, sent, rejected, responseBytes,
            watch.Elapsed.TotalMilliseconds, true, true, error);
    }
}
