using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamLeaderTargetValidator
{
    public static bool IsLeaderAttackTarget(
        OperationResult<LockedTargetSnapshot> result,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        out string rejectReason)
    {
        if (!result.Success || result.Value is null)
        {
            rejectReason = "target_read_failed";
            return false;
        }

        var target = result.Value;
        if (leaderTargetServerObjectId == 0)
        {
            rejectReason = "leader_target_unknown";
            return false;
        }

        if (target.ServerObjectId == 0 || target.ServerObjectId != leaderTargetServerObjectId)
        {
            rejectReason = "target_mismatch";
            return false;
        }

        if (!target.IsMonsterAlive)
        {
            rejectReason = "not_alive_monster";
            return false;
        }

        if (!IsTargetingLeaderSide(target, leader))
        {
            rejectReason = "not_targeting_leader_side";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private static bool IsTargetingLeaderSide(
        LockedTargetSnapshot target,
        TeamMemberSnapshot leader)
    {
        if (target.TargetServerObjectId == 0)
        {
            return false;
        }

        if (target.TargetServerObjectId == leader.ServerObjectId)
        {
            return true;
        }

        if (leader.PartyMember.Class != AionClassId.Spiritmaster)
        {
            return false;
        }

        var pet = leader.SummonedPet?.Pet;
        return pet?.IsSummoned == true &&
               pet.ServerObjectId != 0 &&
               target.TargetServerObjectId == pet.ServerObjectId;
    }
}
