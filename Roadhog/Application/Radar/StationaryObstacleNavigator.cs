using Roadhog.Core.Accounts;
using Roadhog.Core.Radar;

namespace Roadhog.Application.Radar;

public enum RadarNavigationPurpose
{
    ApproachTarget,
    ReturnHome
}

public enum RadarNavigationAction
{
    Direct,
    MoveToWaypoint,
    Ready,
    Unreachable
}

public sealed record RadarNavigationDecision(
    RadarNavigationAction Action,
    RadarPoint Destination,
    double ReachDistanceMeters,
    RadarRoutePlan? Plan,
    uint MapId,
    int RelevantObstacleCount,
    string Reason);

public sealed class StationaryObstacleNavigationState
{
    private uint _directCommittedTargetServerObjectId;

    public uint ObservedMapId { get; set; }

    public DateTimeOffset LastObservedMapReadAt { get; set; } = DateTimeOffset.MinValue;

    public uint CurrentMapId { get; set; }

    public RadarMapDocument? MapDocument { get; set; }

    public RadarObstacleSpatialIndex? SpatialIndex { get; set; }

    public long LoadedRevision { get; set; } = -1L;

    public RadarNavigationPurpose Purpose { get; set; }

    public uint TargetServerObjectId { get; set; }

    public RadarPoint PlannedGoal { get; set; }

    public IReadOnlyList<RadarPoint> Route { get; set; } = Array.Empty<RadarPoint>();

    public int WaypointIndex { get; set; }

    public bool HasWaypointMovementSample { get; set; }

    public RadarPoint LastWaypointMovementSample { get; set; }

    public int LastWaypointMovementSampleIndex { get; set; } = -1;

    public bool CurrentWaypointReached { get; set; }

    public RadarRoutePlan? LastPlan { get; set; }

    public DateTimeOffset LastPlanAt { get; set; } = DateTimeOffset.MinValue;

    public bool IsDirectApproachCommitted(uint targetServerObjectId) =>
        targetServerObjectId != 0 &&
        _directCommittedTargetServerObjectId == targetServerObjectId;

    public void CommitDirectApproach(uint targetServerObjectId)
    {
        if (targetServerObjectId == 0)
        {
            return;
        }

        ClearRouteCore();
        _directCommittedTargetServerObjectId = targetServerObjectId;
    }

    public void ClearDirectApproachCommit()
    {
        _directCommittedTargetServerObjectId = 0;
    }

    public void LatchCurrentWaypointArrival()
    {
        if (WaypointIndex >= 0 && WaypointIndex < Route.Count)
        {
            CurrentWaypointReached = true;
        }
    }

    public void ClearRoute()
    {
        ClearRouteCore();
        ClearDirectApproachCommit();
    }

    private void ClearRouteCore()
    {
        TargetServerObjectId = 0;
        PlannedGoal = default;
        Route = Array.Empty<RadarPoint>();
        WaypointIndex = 0;
        HasWaypointMovementSample = false;
        LastWaypointMovementSample = default;
        LastWaypointMovementSampleIndex = -1;
        CurrentWaypointReached = false;
        LastPlan = null;
        LastPlanAt = DateTimeOffset.MinValue;
    }

    public void ClearLoadedMap()
    {
        CurrentMapId = 0;
        MapDocument = null;
        SpatialIndex = null;
        LoadedRevision = -1L;
        ClearRoute();
    }

    public void Reset()
    {
        ObservedMapId = 0;
        LastObservedMapReadAt = DateTimeOffset.MinValue;
        ClearLoadedMap();
    }
}

public sealed class StationaryObstacleNavigator
{
    private const double MinimumWaypointReachMeters = 0.25D;
    private const double MaximumWaypointReachMeters = 1.5D;

    private readonly IRadarMapStore _mapStore;
    private readonly RadarRoutePlanner _planner;
    private readonly RadarMapRevisionRegistry _revisions;
    private readonly object _settingsSync = new();
    private readonly Dictionary<string, RadarObstacleScriptSettings> _settingsOverrides =
        new(StringComparer.OrdinalIgnoreCase);

    public StationaryObstacleNavigator(
        IRadarMapStore mapStore,
        RadarRoutePlanner planner,
        RadarMapRevisionRegistry revisions)
    {
        _mapStore = mapStore;
        _planner = planner;
        _revisions = revisions;
    }

    public RadarObstacleScriptSettings ResolveSettings(
        string account,
        RadarObstacleScriptSettings? configured)
    {
        lock (_settingsSync)
        {
            return _settingsOverrides.TryGetValue(account, out var value)
                ? value.Clone()
                : (configured ?? new RadarObstacleScriptSettings()).Clone();
        }
    }

    public void SetSettingsOverride(string account, RadarObstacleScriptSettings settings)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return;
        }

        lock (_settingsSync)
        {
            _settingsOverrides[account] = settings.Clone();
        }
    }

    public void NotifyMapSaved(uint mapId)
    {
        _revisions.Increment(mapId);
    }

    public async Task<RadarNavigationDecision> ResolveAsync(
        StationaryObstacleNavigationState state,
        uint mapId,
        RadarPoint start,
        RadarPoint goal,
        RadarNavigationPurpose purpose,
        uint targetServerObjectId,
        RadarObstacleScriptSettings settings,
        double finalReachDistanceMeters,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || mapId == 0)
        {
            state.ClearRoute();
            return Direct(goal, finalReachDistanceMeters, mapId, "disabled_or_map_unknown");
        }

        var revision = _revisions.Get(mapId);
        if (state.CurrentMapId != mapId ||
            state.MapDocument is null ||
            state.SpatialIndex is null ||
            state.LoadedRevision != revision)
        {
            var load = await _mapStore.LoadAsync(mapId, cancellationToken).ConfigureAwait(false);
            if (!load.Success || load.Value is null)
            {
                state.ClearLoadedMap();
                return Direct(goal, finalReachDistanceMeters, mapId, "map_load_failed");
            }

            state.CurrentMapId = mapId;
            state.MapDocument = load.Value.Document;
            state.SpatialIndex = new RadarObstacleSpatialIndex(load.Value.Document.Segments);
            state.LoadedRevision = revision;
            state.ClearRoute();
            if (!load.Value.Found || load.Value.Document.Segments.Count == 0)
            {
                return Direct(goal, finalReachDistanceMeters, mapId, "map_missing_or_empty");
            }
        }

        if (purpose == RadarNavigationPurpose.ApproachTarget &&
            state.IsDirectApproachCommitted(targetServerObjectId))
        {
            return Direct(goal, finalReachDistanceMeters, mapId, "direct_committed");
        }

        state.ClearDirectApproachCommit();

        var index = state.SpatialIndex;
        var directObstacles = index.QueryCorridor(start, goal, 1.0D);
        var directClear = RadarGeometry.IsPathClear(start, goal, directObstacles);
        var distanceToGoal = start.DistanceTo(goal);
        if (directClear && distanceToGoal <= Math.Max(0.1D, finalReachDistanceMeters))
        {
            state.ClearRoute();
            return new RadarNavigationDecision(
                RadarNavigationAction.Ready,
                goal,
                finalReachDistanceMeters,
                null,
                mapId,
                directObstacles.Count,
                "ready");
        }

        if (directClear)
        {
            state.ClearRoute();
            return Direct(goal, finalReachDistanceMeters, mapId, "direct", directObstacles.Count);
        }

        var waypointReachDistance = ResolveWaypointReachDistance(settings);
        var goalMoved = state.PlannedGoal.DistanceTo(goal) > Math.Max(0.1D, settings.TargetReplanDistanceMeters);
        var hasCurrentWaypoint = state.WaypointIndex >= 0 && state.WaypointIndex < state.Route.Count;
        var routeIdentityMatches = state.Route.Count > 0 &&
                                   hasCurrentWaypoint &&
                                   state.Purpose == purpose &&
                                   state.TargetServerObjectId == targetServerObjectId &&
                                   !goalMoved;
        if (routeIdentityMatches)
        {
            ObserveWaypointMovement(state, start, waypointReachDistance);
            AdvanceReachedWaypoints(state, index, start, waypointReachDistance);
            hasCurrentWaypoint = state.WaypointIndex >= 0 && state.WaypointIndex < state.Route.Count;
        }

        var routeMatches = routeIdentityMatches &&
                           hasCurrentWaypoint &&
                           IsRouteLegClear(index, start, state.Route[state.WaypointIndex]);
        if (state.LastPlan is { Success: false } failedPlan &&
            state.Purpose == purpose &&
            state.TargetServerObjectId == targetServerObjectId &&
            !goalMoved &&
            DateTimeOffset.Now - state.LastPlanAt < TimeSpan.FromSeconds(2))
        {
            return new RadarNavigationDecision(
                RadarNavigationAction.Unreachable,
                goal,
                finalReachDistanceMeters,
                failedPlan,
                mapId,
                failedPlan.EvaluatedObstacleCount,
                failedPlan.Reason);
        }

        if (!routeMatches)
        {
            var margin = Math.Max(
                10.0D,
                settings.MaximumDetourExtraMeters + 2.0D);
            var localObstacles = index.QueryCorridor(start, goal, margin);
            var request = new RadarRouteRequest(
                start,
                goal,
                localObstacles,
                settings.MaximumDetourExtraMeters);
            var plan = _planner.Plan(request);
            if (!plan.Success && localObstacles.Count < index.All.Count)
            {
                plan = _planner.Plan(request with { Obstacles = index.All });
            }

            state.Purpose = purpose;
            state.TargetServerObjectId = targetServerObjectId;
            state.PlannedGoal = goal;
            state.LastPlan = plan;
            state.LastPlanAt = DateTimeOffset.Now;
            state.Route = plan.Success ? plan.Points.Skip(1).ToArray() : Array.Empty<RadarPoint>();
            state.WaypointIndex = 0;
            ResetWaypointMovementObservation(state, start);
            if (!plan.Success || state.Route.Count == 0)
            {
                return new RadarNavigationDecision(
                    RadarNavigationAction.Unreachable,
                    goal,
                    finalReachDistanceMeters,
                    plan,
                    mapId,
                    plan.EvaluatedObstacleCount,
                    plan.Reason);
            }
        }

        ObserveWaypointMovement(state, start, waypointReachDistance);
        AdvanceReachedWaypoints(state, index, start, waypointReachDistance);

        var destination = state.Route[Math.Clamp(state.WaypointIndex, 0, state.Route.Count - 1)];
        var isFinal = state.WaypointIndex == state.Route.Count - 1;
        var nextLegBlockedInsideReach = !isFinal &&
                                        state.CurrentWaypointReached &&
                                        !IsRouteLegClear(index, start, state.Route[state.WaypointIndex + 1]);
        return new RadarNavigationDecision(
            RadarNavigationAction.MoveToWaypoint,
            destination,
            isFinal
                ? finalReachDistanceMeters
                : nextLegBlockedInsideReach
                    ? 0.0D
                    : waypointReachDistance,
            state.LastPlan,
            mapId,
            state.LastPlan?.EvaluatedObstacleCount ?? directObstacles.Count,
            isFinal
                ? "move_final"
                : nextLegBlockedInsideReach
                    ? "move_waypoint_precise"
                    : "move_waypoint");
    }

    private static double ResolveWaypointReachDistance(RadarObstacleScriptSettings settings)
    {
        return Math.Clamp(
            settings.WaypointReachMeters,
            MinimumWaypointReachMeters,
            MaximumWaypointReachMeters);
    }

    private static void ObserveWaypointMovement(
        StationaryObstacleNavigationState state,
        RadarPoint start,
        double waypointReachDistance)
    {
        if (state.WaypointIndex < 0 || state.WaypointIndex >= state.Route.Count)
        {
            ResetWaypointMovementObservation(state, start);
            return;
        }

        if (state.LastWaypointMovementSampleIndex != state.WaypointIndex)
        {
            ResetWaypointMovementObservation(state, start);
        }

        var waypoint = state.Route[state.WaypointIndex];
        var reachedByCurrentSample = start.DistanceTo(waypoint) <= waypointReachDistance;
        var reachedBetweenSamples = state.HasWaypointMovementSample &&
                                    RadarGeometry.PointToSegmentDistance(
                                        waypoint,
                                        state.LastWaypointMovementSample,
                                        start) <= waypointReachDistance;
        state.CurrentWaypointReached |= reachedByCurrentSample || reachedBetweenSamples;
        state.LastWaypointMovementSample = start;
        state.HasWaypointMovementSample = true;
        state.LastWaypointMovementSampleIndex = state.WaypointIndex;
    }

    private static void AdvanceReachedWaypoints(
        StationaryObstacleNavigationState state,
        RadarObstacleSpatialIndex index,
        RadarPoint start,
        double waypointReachDistance)
    {
        while (state.CurrentWaypointReached &&
               state.WaypointIndex >= 0 &&
               state.WaypointIndex < state.Route.Count - 1 &&
               IsRouteLegClear(index, start, state.Route[state.WaypointIndex + 1]))
        {
            state.WaypointIndex++;
            ResetWaypointMovementObservation(state, start);
            ObserveWaypointMovement(state, start, waypointReachDistance);
        }
    }

    private static void ResetWaypointMovementObservation(
        StationaryObstacleNavigationState state,
        RadarPoint start)
    {
        state.HasWaypointMovementSample = true;
        state.LastWaypointMovementSample = start;
        state.LastWaypointMovementSampleIndex = state.WaypointIndex;
        state.CurrentWaypointReached = false;
    }

    private static bool IsRouteLegClear(
        RadarObstacleSpatialIndex index,
        RadarPoint start,
        RadarPoint end)
    {
        var obstacles = index.QueryCorridor(start, end, 1.0D);
        return RadarGeometry.IsPathClear(start, end, obstacles);
    }

    private static RadarNavigationDecision Direct(
        RadarPoint goal,
        double reachDistance,
        uint mapId,
        string reason,
        int obstacleCount = 0)
    {
        return new RadarNavigationDecision(
            RadarNavigationAction.Direct,
            goal,
            reachDistance,
            null,
            mapId,
            obstacleCount,
            reason);
    }
}
