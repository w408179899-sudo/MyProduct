using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRoadhogScopedGameApi : IRoadhogGameApi
{
    Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        IReadOnlyCollection<uint> skillIds,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}
