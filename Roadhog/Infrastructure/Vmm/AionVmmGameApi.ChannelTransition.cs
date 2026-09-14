using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IChannelTransitionGameApi
{
    public Task<OperationResult<ChannelTransitionSnapshot>> ReadChannelTransitionAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.ChannelTransition,
            () => ReadChannelTransitionCore(context)), cancellationToken);

    private OperationResult<ChannelTransitionSnapshot> ReadChannelTransitionCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            ChannelTransitionSnapshot value;
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    return OperationResult<ChannelTransitionSnapshot>.Fail(error);
                var gameBase = process.GetModuleBase(ResolveModuleName());
                if (gameBase == 0) return OperationResult<ChannelTransitionSnapshot>.Fail("Module not found: Game.dll");
                value = new ChannelTransitionDecoder((address, size) =>
                {
                    if (!TryReadBytes(process, address, size, out var bytes, bypassMemoryCache: true) || bytes.Length != size)
                        throw new IOException("Failed to read channel transition memory.");
                    return bytes;
                }, () =>
                {
                    if (!TryReadLocalPlayer(process, gameBase, true, out var player, out var playerError))
                        throw new InvalidDataException(playerError);
                    return player;
                }).Read(gameBase);
            }
            ClearPlayerReadFailure(context);
            return OperationResult<ChannelTransitionSnapshot>.Ok(value);
        }
        catch (IOException ex)
        {
            // Only real transport failures count; a valid loading scene never retires the connection.
            if (RecordPlayerReadFailure(context) >= PlayerReadFailuresBeforeReconnect)
            {
                ClearPlayerReadFailure(context);
                ResetConnection(context.VmmDeviceName, context.AccountName, "channel_transition_read_failed", ex.Message);
            }
            return OperationResult<ChannelTransitionSnapshot>.Fail(ex.Message);
        }
        catch (Exception ex) { return OperationResult<ChannelTransitionSnapshot>.Fail(ex.Message); }
    }
}
