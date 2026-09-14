using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>Game.dll scene adapter; offsets and live evidence are in docs/CHANNEL_TRANSITION_DATA.md.</summary>
internal sealed class ChannelTransitionDecoder(Func<ulong, int, byte[]> read, Func<PlayerSnapshot> readPlayer)
{
    internal const ulong StateRva = 0xD6CA90, PhaseRva = 0xD65B10, LocalIdRva = 0xD6CB08;

    internal readonly record struct Header(uint State, uint Phase, ushort LocalId)
    {
        public bool InWorld => State == 15 && Phase == 2 && LocalId != 0;
        public bool IsLoading => State == 20 || (State == 15 && (Phase != 2 || LocalId == 0));
    }

    internal Header ReadHeader(ulong gameBase)
    {
        var result = new Header(U32(gameBase + StateRva), U32(gameBase + PhaseRva),
            BitConverter.ToUInt16(Bytes(gameBase + LocalIdRva, 2)));
        if (result.State > 24 || result.Phase > 2) throw new InvalidDataException("Invalid scene state or loading phase.");
        return result;
    }

    public ChannelTransitionSnapshot Read(ulong gameBase)
    {
        var before = ReadHeader(gameBase);
        PlayerSnapshot? player = null;
        ChannelSnapshot? channel = null;
        if (before.InWorld)
        {
            channel = ReadChannel(gameBase);
            player = readPlayer();
            if (player.EntityId != before.LocalId || player.Position is null || !channel.IsValid)
                throw new InvalidDataException("Incomplete scene/player capture.");
            var afterChannel = ReadChannel(gameBase);
            if (channel.Index != afterChannel.Index || channel.Count != afterChannel.Count || channel.MapId != afterChannel.MapId)
                throw new InvalidDataException("Channel changed during scene capture.");
        }
        if (before != ReadHeader(gameBase)) throw new InvalidDataException("Scene changed during capture.");
        return new(before.InWorld, player, channel, DateTimeOffset.UtcNow);
    }

    private ChannelSnapshot ReadChannel(ulong gameBase)
    {
        var pair = Bytes(gameBase + 0xD71CB0, 8);
        return new(unchecked((int)BitConverter.ToUInt32(pair, 0)), unchecked((int)BitConverter.ToUInt32(pair, 4)),
            U32(gameBase + 0xD6689C), DateTimeOffset.UtcNow);
    }

    private uint U32(ulong address) => BitConverter.ToUInt32(Bytes(address, 4));
    private byte[] Bytes(ulong address, int size)
    {
        var bytes = read(address, size);
        if (bytes.Length != size) throw new InvalidDataException("Incomplete scene memory read.");
        return bytes;
    }
}
