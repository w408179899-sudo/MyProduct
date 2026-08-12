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

    public RadarRoutePlan? LastPlan { get; set; }

    public DateTimeOffset LastPlanAt { get; set; } = DateTimeOffset.MinValue;

    public void ClearRoute()
    {
        TargetServerObjectId = 0;
        PlannedGoal = default;
        Route = Array.Empty<RadarPoint>();
        WaypointIndex = 0;
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

        var goalMoved = state.PlannedGoal.DistanceTo(goal) > Math.Max(0.1D, settings.TargetReplanDistanceMeters);
        var hasCurrentWaypoint = state.WaypointIndex >= 0 && state.WaypointIndex < state.Route.Count;
        var routeMatches = state.Route.Count > 0 &&
                           hasCurrentWaypoint &&
                           state.Purpose == purpose &&
                           state.TargetServerObjectId == targetServerObjectId &&
                           !goalMoved &&
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

        while (state.WaypointIndex < state.Route.Count - 1 &&
               start.DistanceTo(state.Route[state.WaypointIndex]) <= Math.Max(0.25D, settings.WaypointReachMeters) &&
               IsRouteLegClear(index, start, state.Route[state.WaypointIndex + 1]))
        {
            state.WaypointIndex++;
        }

        var destination = state.Route[Math.Clamp(state.WaypointIndex, 0, state.Route.Count - 1)];
        var isFinal = state.WaypointIndex == state.Route.Count - 1;
        return new RadarNavigationDecision(
            RadarNavigationAction.MoveToWaypoint,
            destination,
            isFinal ? finalReachDistanceMeters : Math.Max(0.25D, settings.WaypointReachMeters),
            state.LastPlan,
            mapId,
            state.LastPlan?.EvaluatedObstacleCount ?? directObstacles.Count,
            isFinal ? "move_final" : "move_waypoint");
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
