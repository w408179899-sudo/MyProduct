using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed record TeamLeaderProtectionThreat(
    WorldObjectSnapshot Target,
    TeamMemberSnapshot ProtectedMember,
    uint ProtectedServerObjectId,
    bool ProtectedObjectIsPet,
    int Priority);

public static class TeamLeaderProtectionSelector
{
    public static TeamLeaderProtectionThreat? SelectThreat(
        TeamSnapshot snapshot,
        IEnumerable<WorldObjectSnapshot> worldObjects,
        Vector3Snapshot leaderPosition,
        double groupDistanceMeters)
    {
        var protectedObjects = BuildProtectedObjects(snapshot, groupDistanceMeters);
        if (protectedObjects.Count == 0)
        {
            return null;
        }

        return worldObjects
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => target.TargetServerObjectId != 0)
            .Select(target => protectedObjects.TryGetValue(target.TargetServerObjectId, out var protectedObject)
                ? new TeamLeaderProtectionThreat(
                    target,
                    protectedObject.Member,
                    protectedObject.ServerObjectId,
                    protectedObject.IsPet,
                    protectedObject.Priority)
                : null)
            .Where(threat => threat is not null)
            .OrderBy(threat => threat!.Priority)
            .ThenBy(threat => StationaryCombatTargetSelector.HorizontalDistance(threat!.Target.Position!.Value, leaderPosition))
            .ThenBy(threat => threat!.Target.CurrentHp == 0 ? uint.MaxValue : threat.Target.CurrentHp)
            .ThenBy(threat => threat!.Target.ServerObjectId)
            .ThenBy(threat => threat!.Target.EntityId)
            .FirstOrDefault();
    }

    public static IReadOnlySet<uint> CreateProtectedServerObjectIds(
        TeamSnapshot snapshot,
        double groupDistanceMeters)
    {
        return BuildProtectedObjects(snapshot, groupDistanceMeters)
            .Keys
            .ToHashSet();
    }

    private static Dictionary<uint, ProtectedObject> BuildProtectedObjects(
        TeamSnapshot snapshot,
        double groupDistanceMeters)
    {
        var protectedObjects = new Dictionary<uint, ProtectedObject>();
        var groupDistance = Math.Max(0.0D, groupDistanceMeters);
        foreach (var member in snapshot.OtherMembers)
        {
            if (!member.PartyMember.IsAlive ||
                member.PartyMember.DistanceToLocalPlayer is not { } distanceToLocal ||
                distanceToLocal > groupDistance)
            {
                continue;
            }

            var priority = ResolveProtectionPriority(member.PartyMember.Class);
            if (member.ServerObjectId != 0)
            {
                protectedObjects[member.ServerObjectId] = new ProtectedObject(
                    member,
                    member.ServerObjectId,
                    IsPet: false,
                    priority);
            }

            var pet = member.SummonedPet?.Pet;
            if (member.PartyMember.Class == AionClassId.Spiritmaster &&
                pet?.IsAlive == true &&
                pet.ServerObjectId != 0)
            {
                protectedObjects[pet.ServerObjectId] = new ProtectedObject(
                    member,
                    pet.ServerObjectId,
                    IsPet: true,
                    priority);
            }
        }

        return protectedObjects;
    }

    private static int ResolveProtectionPriority(AionClassId? classId)
    {
        return classId switch
        {
            AionClassId.Cleric => 0,
            AionClassId.Chanter => 1,
            AionClassId.Priest => 2,
            _ => 3
        };
    }

    private sealed record ProtectedObject(
        TeamMemberSnapshot Member,
        uint ServerObjectId,
        bool IsPet,
        int Priority);
}
