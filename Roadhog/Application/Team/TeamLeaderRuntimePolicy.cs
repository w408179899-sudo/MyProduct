using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamLeaderRuntimePolicy
{
    public const double LeaderGroupExitDistanceMeters = 50.0D;
    public const int ConsecutiveLeaderUnavailableTicksBeforeNormalWork = 5;

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

    public static bool UpdateLeaderGroupState(
        TeamMemberSnapshot? leader,
        double configuredEnterDistanceMeters,
        bool wasInGroup)
    {
        if (leader is null ||
            leader.IsSelf ||
            leader.PartyMember.IsDead ||
            leader.PartyMember.DistanceToLocalPlayer is not { } distanceToLocal)
        {
            return false;
        }

        var enterDistanceMeters = Math.Max(0.0D, configuredEnterDistanceMeters);
        var exitDistanceMeters = ResolveLeaderGroupExitDistanceMeters(enterDistanceMeters);
        return wasInGroup
            ? distanceToLocal <= exitDistanceMeters
            : distanceToLocal <= enterDistanceMeters;
    }

    public static double ResolveLeaderGroupExitDistanceMeters(double configuredEnterDistanceMeters)
    {
        return Math.Max(Math.Max(0.0D, configuredEnterDistanceMeters), LeaderGroupExitDistanceMeters);
    }

    public static bool HasActiveCombatTarget(StationaryCombatState? combatState)
    {
        return combatState is not null &&
               (combatState.LootAfterKill.Active ||
                combatState.Fighting ||
                combatState.CurrentTargetEntityId != 0 ||
                combatState.CurrentTargetServerObjectId != 0 ||
                combatState.CandidateEntityId != 0 ||
                combatState.CandidateServerObjectId != 0);
    }
}
