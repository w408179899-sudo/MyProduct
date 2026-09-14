using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    public Task<OperationResult<ChannelSwitchUiSnapshot>> ReadChannelSwitchUiAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.ChannelSwitchUi,
            () => ReadChannelSwitchUiCore(context)), cancellationToken);

    private OperationResult<ChannelSwitchUiSnapshot> ReadChannelSwitchUiCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    return OperationResult<ChannelSwitchUiSnapshot>.Fail(error);
                var gameBase = process.GetModuleBase(ResolveModuleName());
                if (gameBase == 0)
                    return OperationResult<ChannelSwitchUiSnapshot>.Fail("Module not found: Game.dll");
                // UI trees and cursor feedback change within a frame. Keep this policy inside the provider.
                var decoder = new ChannelSwitchUiDecoder((address, size) =>
                {
                    if (!TryReadBytes(process, address, size, out var bytes, bypassMemoryCache: true) || bytes.Length != size)
                        throw new InvalidDataException("Incomplete channel UI read.");
                    return bytes;
                });
                return OperationResult<ChannelSwitchUiSnapshot>.Ok(decoder.Read(gameBase));
            }
        }
        catch (Exception ex)
        {
            return OperationResult<ChannelSwitchUiSnapshot>.Fail(ex.Message);
        }
    }
}
