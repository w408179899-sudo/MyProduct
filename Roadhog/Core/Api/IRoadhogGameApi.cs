using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IRoadhogGameApi
{
    Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken cancellationToken = default);
}
