using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

public interface IRoadhogSnapshotReader
{
    Task<PublishedGameSnapshot<PlayerSnapshot>> ReadPlayerAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<SummonedPetSnapshot>> ReadSummonedPetAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<PartySnapshot>> ReadPartyAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<TacticsSignSnapshot>> ReadTacticsSignsAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<ChannelSnapshot>> ReadChannelAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<LockedTargetSnapshot>> ReadLockedTargetAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        IReadOnlyCollection<uint>? skillIds = null,
        long afterVersion = 0);

    Task<PublishedGameSnapshot<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<ulong>> ReadInventoryMoneyAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<int>> ReadInventoryCapacityAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<GatherSnapshot>> ReadGatherSnapshotAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(long afterVersion = 0);

    Task<PublishedGameSnapshot<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        InventoryWindowRectSource rectSource = InventoryWindowRectSource.LegacyDialogRect,
        long afterVersion = 0);

    Task<PublishedGameSnapshot<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(long afterVersion = 0);
}
