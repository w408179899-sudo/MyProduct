using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public interface ITeamTacticalTargetRangePolicy
{
    Task<TeamTacticalTargetRangeDecision> EvaluateNewTargetAsync(
        AccountWorkerContext context,
        StationaryCombatState combatState,
        LockedTargetSnapshot target);
}

public sealed record TeamTacticalTargetRangeDecision(
    bool Allowed,
    string Reason,
    double? DistanceFromHome,
    double? Radius,
    string? Error)
{
    public static TeamTacticalTargetRangeDecision NotApplicable()
    {
        return new TeamTacticalTargetRangeDecision(true, "not_stationary_combat", null, null, null);
    }

    public static TeamTacticalTargetRangeDecision Inside(double distanceFromHome, double radius)
    {
        return new TeamTacticalTargetRangeDecision(
            true,
            "inside_stationary_radius",
            distanceFromHome,
            radius,
            null);
    }

    public static TeamTacticalTargetRangeDecision Rejected(
        string reason,
        double? distanceFromHome,
        double? radius,
        string? error = null)
    {
        return new TeamTacticalTargetRangeDecision(false, reason, distanceFromHome, radius, error);
    }
}

public sealed class TacticalMarkRangeRetryState
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(500);

    public ushort TargetEntityId { get; private set; }

    public uint TargetServerObjectId { get; private set; }

    public DateTimeOffset RetryAfter { get; private set; } = DateTimeOffset.MinValue;

    public bool ShouldWait(DateTimeOffset now)
    {
        return (TargetEntityId != 0 || TargetServerObjectId != 0) &&
               now < RetryAfter;
    }

    public void RememberRejected(LockedTargetSnapshot target, DateTimeOffset now)
    {
        TargetEntityId = target.TargetEntityId;
        TargetServerObjectId = target.ServerObjectId;
        RetryAfter = now + RetryInterval;
    }

    public void Clear()
    {
        TargetEntityId = 0;
        TargetServerObjectId = 0;
        RetryAfter = DateTimeOffset.MinValue;
    }
}
