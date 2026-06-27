using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Mock;

public sealed class MockRoadhogGameApi : IRoadhogGameApi
{
    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new PlayerSnapshot(
            1,
            0,
            "Mock Character",
            100,
            100,
            80,
            100,
            0,
            new Vector3Snapshot(1303.07F, 2835.08F, 258.39F),
            DateTimeOffset.Now);

        return Task.FromResult(OperationResult<PlayerSnapshot>.Ok(snapshot));
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new LockedTargetSnapshot(
            2,
            10002,
            3,
            LockedTargetSnapshot.MonsterObjectType,
            "Mock Monster",
            100,
            100,
            new Vector3Snapshot(1307.5F, 2838.2F, 258.9F),
            5.6D,
            DateTimeOffset.Now);

        return Task.FromResult(OperationResult<LockedTargetSnapshot>.Ok(snapshot));
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SkillSnapshot> skills = new[]
        {
            new SkillSnapshot(1001, "普通攻击", 1, 1, "普通攻击", 1, false, 0, 0),
            new SkillSnapshot(1002, "主输出技能", 3, 3, "主输出技能", 3, false, 1200, 0),
            new SkillSnapshot(1003, "自身增益", 2, 2, "自身增益", 2, true, 30000, 0)
        };

        return Task.FromResult(OperationResult<IReadOnlyList<SkillSnapshot>>.Ok(skills));
    }

    public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Ok(Array.Empty<InventoryItemSnapshot>()));
    }

    public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Ok(Array.Empty<WorldObjectSnapshot>()));
    }

}
