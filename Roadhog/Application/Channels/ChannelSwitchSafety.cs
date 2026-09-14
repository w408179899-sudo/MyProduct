using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Api;

namespace Roadhog.Application.Channels;

internal static class ChannelSwitchSafety
{
    internal static bool IsWorkingOnCombat(StationaryCombatState state) => TeamLeaderRuntimePolicy.HasActiveCombatTarget(state);

    internal static bool HasExclusiveWork(StationaryCombatState state) =>
        state.TopLevelState != StationaryCombatTopLevelState.Normal || state.BagCleanup.Active ||
        state.Gather.Phase != StationaryGatherPhase.Idle || state.LootAfterKill.Active;

    internal static async Task<(uint Hp, bool Busy)> ReadAsync(IRoadhogSnapshotReader snapshots, StationaryCombatState state)
    {
        var player = (await snapshots.ReadPlayerAsync().ConfigureAwait(false)).Value;
        if (player.IsDead || IsWorkingOnCombat(state)) return (player.CurrentHp, true);
        var target = (await snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;
        if (target.IsMonsterAlive || (target.HasTarget && target.IsAlive && target.IsTargetingLocalPlayer)) return (player.CurrentHp, true);
        var petId = state.LocalCombatSidePetServerObjectId;
        if (player.IsSpiritmaster)
        {
            var pet = (await snapshots.ReadSummonedPetAsync().ConfigureAwait(false)).Value;
            petId = pet.IsSummoned ? pet.ServerObjectId : 0;
        }
        var world = (await snapshots.ReadWorldObjectsAsync().ConfigureAwait(false)).Value;
        return (player.CurrentHp, world.Any(item => item.IsAlive &&
            (item.IsTargetingLocalPlayer || (petId != 0 && item.TargetServerObjectId == petId))));
    }
}
