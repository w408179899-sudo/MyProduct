namespace Hardware.KmBox;

internal enum KmBoxCommand : uint
{
    Connect = 0xaf3c2828,
    MouseMove = 0xaede7345,
    MouseButton = 0x9823ae8d,
    MouseWheel = 0xffeead38,
    MouseAutoMove = 0xaede7346,
    KeyboardAll = 0x123c2c2f
}

internal readonly struct KmBoxRequestHeader
{
    public KmBoxRequestHeader(uint mac, uint rand, uint index, KmBoxCommand command)
    {
        Mac = mac;
        Rand = rand;
        Index = index;
        Command = command;
    }

    public uint Mac { get; }

    public uint Rand { get; }

    public uint Index { get; }

    public KmBoxCommand Command { get; }

    public KmBoxRequestHeader WithRand(uint rand)
    {
        return new KmBoxRequestHeader(Mac, rand, Index, Command);
    }
}

internal readonly struct KmBoxResponseHeader
{
    public KmBoxResponseHeader(uint mac, uint rand, uint index, KmBoxCommand command)
    {
        Mac = mac;
        Rand = rand;
        Index = index;
        Command = command;
    }

    public uint Mac { get; }

    public uint Rand { get; }

    public uint Index { get; }

    public KmBoxCommand Command { get; }
}

internal static class KmBoxProtocol
{
    public const int MaxKeyboardButtons = 10;
    private const int HeaderSize = 16;
    private const int MousePayloadSize = 56;
    private const int KeyboardPayloadSize = 12;

    public static uint MacToUInt32(string mac)
    {
        if (mac.Length != 8)
        {
            throw new ArgumentException("KMBox MAC must be 8 hexadecimal characters.", nameof(mac));
        }

        uint value = 0;
        for (var i = 0; i < mac.Length; i += 2)
        {
            value <<= 8;
            value |= Convert.ToByte(mac.Substring(i, 2), 16);
        }

        return value;
    }

    public static byte[] BuildPacket(KmBoxRequestHeader header, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        var packet = new byte[HeaderSize + payload.Length];

        WriteUInt32(packet, 0, header.Mac);
        WriteUInt32(packet, 4, header.Rand);
        WriteUInt32(packet, 8, header.Index);
        WriteUInt32(packet, 12, (uint)header.Command);
        Buffer.BlockCopy(payload, 0, packet, HeaderSize, payload.Length);

        return packet;
    }

    public static byte[] BuildMousePayload(int buttons, int x, int y, int wheel)
    {
        var payload = new byte[MousePayloadSize];
        WriteInt32(payload, 0, buttons);
        WriteInt32(payload, 4, x);
        WriteInt32(payload, 8, y);
        WriteInt32(payload, 12, wheel);
        return payload;
    }

    public static byte[] BuildKeyboardPayload(byte modifiers, IReadOnlyList<byte> buttons)
    {
        var payload = new byte[KeyboardPayloadSize];
        payload[0] = modifiers;

        var count = Math.Min(buttons.Count, MaxKeyboardButtons);
        for (var i = 0; i < count; i++)
        {
            payload[2 + i] = buttons[i];
        }

        return payload;
    }

    public static KmBoxResponseHeader ParseResponseHeader(byte[] response)
    {
        if (response is null || response.Length < HeaderSize)
        {
            throw new KmBoxException("KMBox response was shorter than the command header.");
        }

        return new KmBoxResponseHeader(
            ReadUInt32(response, 0),
            ReadUInt32(response, 4),
            ReadUInt32(response, 8),
            (KmBoxCommand)ReadUInt32(response, 12));
    }

    public static bool IsMatchingResponse(KmBoxRequestHeader request, KmBoxResponseHeader response)
    {
        return request.Command == response.Command &&
               request.Index == response.Index;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        WriteUInt32(buffer, offset, unchecked((uint)value));
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xff);
        buffer[offset + 1] = (byte)((value >> 8) & 0xff);
        buffer[offset + 2] = (byte)((value >> 16) & 0xff);
        buffer[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return buffer[offset] |
               ((uint)buffer[offset + 1] << 8) |
               ((uint)buffer[offset + 2] << 16) |
               ((uint)buffer[offset + 3] << 24);
    }
}
