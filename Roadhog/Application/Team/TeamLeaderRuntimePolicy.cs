using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamLeaderRuntimePolicy
{
    public static bool IsLeaderInGroupRange(
        TeamMemberSnapshot? leader,
        double configuredDistanceMeters)
    {
        if (leader is null ||
            leader.IsSelf ||
            leader.PartyMember.IsDead ||
            leader.PartyMember.DistanceToLocalPlayer is not { } distanceToLocal)
        {
            return false;
        }

        return distanceToLocal <= Math.Max(0.0D, configuredDistanceMeters);
    }

    public static bool HasActiveCombatTarget(StationaryCombatState? combatState)
    {
        return combatState is not null &&
               (combatState.Fighting ||
                combatState.CurrentTargetEntityId != 0 ||
                combatState.CurrentTargetServerObjectId != 0 ||
                combatState.CandidateEntityId != 0 ||
                combatState.CandidateServerObjectId != 0);
    }
}
