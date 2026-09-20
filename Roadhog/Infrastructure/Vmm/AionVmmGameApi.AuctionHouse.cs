using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IAuctionHouseGameApi
{
    public Task<OperationResult<AuctionHouseSnapshot>> ReadAuctionHouseAsync(GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.AuctionHouse,
            () => ReadAuctionHouseCore(context)), cancellationToken);

    internal static bool IsAuctionBrokerIdentity(string name, string? titleKey) => name == "摩比隆" || titleKey == "STR_NPCTITLE_Middleman";

    private OperationResult<AuctionHouseSnapshot> ReadAuctionHouseCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error)) return OperationResult<AuctionHouseSnapshot>.Fail(error);
                var gameBase = process.GetModuleBase(ResolveModuleName());
                if (gameBase == 0 || !TryReadLockedTarget(process, gameBase, true, out var target, out error)) return OperationResult<AuctionHouseSnapshot>.Fail("Auction target capture failed.");
                var selected = ToLockedTargetSnapshot(target); string? title = null;
                if (selected.HasTarget && selected.Name != "摩比隆" && target.Actor is { ObjectType: LockedTargetSnapshot.MonsterObjectType } actor)
                {
                    if (!actor.NpcTemplateIdAvailable) return OperationResult<AuctionHouseSnapshot>.Fail("NPC template capture incomplete.");
                    var catalog = GetNpcXmlCatalog();
                    if (!string.IsNullOrEmpty(catalog.Error)) return OperationResult<AuctionHouseSnapshot>.Fail(catalog.Error);
                    if (catalog.Details.TryGetValue(actor.NpcTemplateId, out var detail)) title = detail.TitleKey;
                }
                var result = ReadInventoryInteractionCore(context, static (decoder, module) => decoder.ReadAuction(module));
                if (!result.Success || result.Value == null) return result;
                if (!TryReadLockedTarget(process, gameBase, true, out var after, out error) || ToLockedTargetSnapshot(after).ServerObjectId != selected.ServerObjectId)
                    return OperationResult<AuctionHouseSnapshot>.Fail("Selected NPC changed during auction capture.");
                return OperationResult<AuctionHouseSnapshot>.Ok(result.Value with
                {
                    BrokerTargetServerObjectId = selected.HasTarget && IsAuctionBrokerIdentity(selected.Name, title) ? selected.ServerObjectId : 0
                });
            }
        }
        catch (Exception ex) { return OperationResult<AuctionHouseSnapshot>.Fail(ex.Message); }
    }
}
