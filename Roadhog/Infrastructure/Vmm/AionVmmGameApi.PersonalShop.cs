using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IPersonalShopGameApi
{
    public Task<OperationResult<PersonalShopSnapshot>> ReadPersonalShopAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.PersonalShop,
            () => ReadPersonalShopCore(context, static (decoder, gameBase) => decoder.Read(gameBase))), cancellationToken);

    public Task<OperationResult<PersonalShopCursorSnapshot>> ReadPersonalShopCursorAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.PersonalShopCursor,
            () => ReadPersonalShopCore(context, static (decoder, gameBase) => decoder.ReadCursor(gameBase))), cancellationToken);

    private OperationResult<T> ReadPersonalShopCore<T>(GameApiReadContext context, Func<PersonalShopDecoder, ulong, T> capture)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error)) return OperationResult<T>.Fail(error);
                var gameBase = process.GetModuleBase(ResolveModuleName());
                if (gameBase == 0) return OperationResult<T>.Fail("Module not found: Game.dll");
                var decoder = new PersonalShopDecoder((address, size) =>
                {
                    if (!TryReadBytes(process, address, size, out var bytes, bypassMemoryCache: true) || bytes.Length != size)
                        throw new InvalidDataException("Incomplete personal shop UI read.");
                    return bytes;
                }, requests =>
                {
                    if (requests.Count == 0) return Array.Empty<byte[]>();
                    using var batch = process.Scatter_Initialize(1);
                    if (batch == null) throw new InvalidDataException("Could not initialize shop UI batch.");
                    foreach (var request in requests)
                        if (!batch.Prepare(request.Address, (uint)request.Size)) throw new InvalidDataException("Could not prepare shop UI batch.");
                    if (!batch.Execute()) throw new InvalidDataException("Could not execute shop UI batch.");
                    return requests.Select(request => batch.Read(request.Address, (uint)request.Size)).ToArray();
                });
                return OperationResult<T>.Ok(capture(decoder, gameBase));
            }
        }
        catch (Exception ex) { return OperationResult<T>.Fail(ex.Message); }
    }
}
