using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IQuickbarGameApi
{
    public Task<OperationResult<QuickbarSnapshot>> ReadQuickbarAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.Quickbar, () => ReadQuickbarCore(context)), cancellationToken);

    private OperationResult<QuickbarSnapshot> ReadQuickbarCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    return OperationResult<QuickbarSnapshot>.Fail(error);
                var module = process.GetModuleBase(ResolveModuleName());
                if (module == 0) return OperationResult<QuickbarSnapshot>.Fail("Game.dll not found.");
                var decoder = new QuickbarDecoder((address, length) =>
                {
                    if (!TryReadBytes(process, address, length, out var bytes, bypassMemoryCache: true) || bytes.Length != length)
                        throw new InvalidDataException("Incomplete quickbar memory read.");
                    return bytes;
                });
                return OperationResult<QuickbarSnapshot>.Ok(decoder.Read(module));
            }
        }
        catch (Exception ex) { return OperationResult<QuickbarSnapshot>.Fail(ex.Message); }
    }
}
