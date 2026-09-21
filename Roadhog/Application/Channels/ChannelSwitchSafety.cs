using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
using Roadhog.Core.Api;

namespace Roadhog.Application.Channels;

internal static class ChannelSwitchSafety
{
    internal static bool IsWorkingOnCombat(StationaryCombatState state) => TeamLeaderRuntimePolicy.HasActiveCombatTarget(state);

    internal static bool HasExclusiveWork(StationaryCombatState state) =>
        state.StartupTownReturnPending || state.NoKillRecovery.Step == StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle ||
        state.ReturnNavigationBlocked || state.TopLevelState != StationaryCombatTopLevelState.Normal || state.BagCleanup.Active ||
        state.Gather.Phase != StationaryGatherPhase.Idle || state.LootAfterKill.Active;

    internal static async Task<(uint Hp, bool Busy)> ReadAsync(IRoadhogSnapshotReader snapshots, StationaryCombatState state)
    {
        var player = (await snapshots.ReadPlayerAsync().ConfigureAwait(false)).Value;
        if (player.IsDead || IsWorkingOnCombat(state)) return (player.CurrentHp, true);
        var target = (await snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value;
        var petId = state.LocalCombatSidePetServerObjectId;
        if (player.IsSpiritmaster)
        {
            var pet = (await snapshots.ReadSummonedPetAsync().ConfigureAwait(false)).Value;
            petId = pet.IsSummoned ? pet.ServerObjectId : 0;
        }
        // A selected live object may be idle or already abandoned by combat.
        // Only an actual local-side threat should keep resetting the peace timer.
        if (target.HasTarget && target.IsAlive &&
            (target.IsTargetingLocalPlayer || target.TargetServerObjectIdMatchesLocal ||
             (petId != 0 && target.TargetServerObjectId == petId))) return (player.CurrentHp, true);
        var world = (await snapshots.ReadWorldObjectsAsync().ConfigureAwait(false)).Value;
        return (player.CurrentHp, world.Any(item => item.IsAlive &&
            (item.IsTargetingLocalPlayer || (petId != 0 && item.TargetServerObjectId == petId))));
    }
}
