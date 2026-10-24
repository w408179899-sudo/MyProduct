using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    // Login and gameplay share the same connection and retirement lock; never open a second DMA owner.
    internal T WithLoginConnection<T>(string device, Func<Vmmsharp.Vmm, T> capture)
    {
        var connection = GetOrCreateConnection(device);
        lock (connection.SyncRoot) return capture(connection.Vmm);
    }

    internal static PlayerSnapshot ReadLoginPlayer(Vmmsharp.VmmProcess process, ulong gameBase)
    {
        if (!TryReadLocalPlayer(process, gameBase, true, out var player, out var error) || player is null)
            throw new InvalidOperationException(error ?? "角色数据尚未完整。");
        return player;
    }
}
