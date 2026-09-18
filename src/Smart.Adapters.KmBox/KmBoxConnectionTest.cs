using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Hardware.KmBox;

namespace Smart.Adapters.KmBox;

public static class KmBoxConnectionTest
{
    // A separate socket is intentional: the normal input device sends ReleaseAll on Dispose.
    public static async Task ConnectAsync(KmBoxOptions options, CancellationToken token = default)
    {
        var validated = options.CloneAndValidate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(validated.CommandTimeoutMs);
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Connect(IPAddress.Parse(validated.IpAddress), validated.Port);
        var random = new byte[4]; RandomNumberGenerator.Fill(random);
        var request = new KmBoxRequestHeader(KmBoxProtocol.MacToUInt32(validated.Mac), BinaryPrimitives.ReadUInt32LittleEndian(random), 0, KmBoxCommand.Connect);
        try
        {
            await socket.SendAsync(KmBoxProtocol.BuildPacket(request, []), deadline.Token).ConfigureAwait(false);
            for (var rejected = 0; rejected < 128; rejected++)
            {
                var response = await socket.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (response.Buffer.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(response.Buffer.AsSpan(8)) == request.Index &&
                    BinaryPrimitives.ReadUInt32LittleEndian(response.Buffer.AsSpan(12)) == (uint)KmBoxCommand.Connect) return;
            }
            throw new IOException("KMBox 返回过多无效握手响应。");
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("KMBox 连接握手超时，请检查 IP、端口和设备。", ex); }
    }
}
