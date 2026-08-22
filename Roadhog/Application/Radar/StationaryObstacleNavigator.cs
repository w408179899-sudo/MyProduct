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

public sealed record RadarRouteDistanceRequest(string Key, RadarPoint Goal);

public sealed record RadarRouteDistanceScore(
    string Key,
    double DirectDistance,
    double EffectiveDistance,
    bool Reachable,
    bool UsesRouteDistance,
    uint MapId,
    int RelevantObstacleCount,
    string Reason);

public sealed record RadarRouteDistanceScoreBatch(
    uint MapId,
    bool UsesRouteScoring,
    string Reason,
    IReadOnlyDictionary<string, RadarRouteDistanceScore> Scores);

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

    public bool CurrentWaypointReachedPrecisely { get; set; }

    public int LooseAdvanceWaypointIndex { get; set; } = -1;

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
            CurrentWaypointReachedPrecisely = true;
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
        CurrentWaypointReachedPrecisely = false;
        LooseAdvanceWaypointIndex = -1;
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
    private const double MaximumWaypointReachMeters = 5.0D;
    private const double MaximumPrecisionCorrectionMeters = 1.5D;
    private const double LooseWaypointAdvanceMeters = 5.0D;

    private readonly IRadarMapStore _mapStore;
    private readonly RadarRoutePlanner _planner;
    private readonly RadarMapRevisionRegistry _revisions;
    private readonly object _settingsSync = new();
    private readonly object _scoreMapSync = new();
    private readonly Dictionary<string, RadarObstacleScriptSettings> _settingsOverrides =
        new(StringComparer.OrdinalIgnoreCase);
    private RadarScoringMap? _scoreMapCache;

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

    public async Task<RadarRouteDistanceScoreBatch> ScoreRouteDistancesAsync(
        uint mapId,
        RadarPoint start,
        IReadOnlyList<RadarRouteDistanceRequest> requests,
        RadarObstacleScriptSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return new RadarRouteDistanceScoreBatch(
                mapId,
                false,
                "no_targets",
                new Dictionary<string, RadarRouteDistanceScore>(StringComparer.Ordinal));
        }

        if (!settings.Enabled || mapId == 0)
        {
            return CreateDirectScoreBatch(mapId, start, requests, "disabled_or_map_unknown");
        }

        var loaded = await LoadScoringMapAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return CreateDirectScoreBatch(mapId, start, requests, "map_load_failed");
        }

        if (!loaded.Found || loaded.Document.Segments.Count == 0)
        {
            return CreateDirectScoreBatch(mapId, start, requests, "map_missing_or_empty");
        }

        var scores = new Dictionary<string, RadarRouteDistanceScore>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scores[request.Key] = ScoreRouteDistance(
                mapId,
                start,
                request,
                loaded.Index,
                settings);
        }

        return new RadarRouteDistanceScoreBatch(mapId, true, "route_scored", scores);
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
        CancellationToken cancellationToken = default,
        bool allowDirectCommitment = true)
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

        if (allowDirectCommitment &&
            purpose == RadarNavigationPurpose.ApproachTarget &&
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
        var waypointPrecisionDistance = ResolveWaypointPrecisionDistance(waypointReachDistance);
        var waypointAdvanceDistance = ResolveWaypointAdvanceDistance(waypointReachDistance);
        var goalMoved = state.PlannedGoal.DistanceTo(goal) > Math.Max(0.1D, settings.TargetReplanDistanceMeters);
        var hasCurrentWaypoint = state.WaypointIndex >= 0 && state.WaypointIndex < state.Route.Count;
        var routeIdentityMatches = state.Route.Count > 0 &&
                                   hasCurrentWaypoint &&
                                   state.Purpose == purpose &&
                                   state.TargetServerObjectId == targetServerObjectId &&
                                   !goalMoved;
        if (routeIdentityMatches)
        {
            ObserveWaypointMovement(state, start, waypointPrecisionDistance, waypointAdvanceDistance);
            AdvanceReachedWaypoints(state, index, start, waypointPrecisionDistance, waypointAdvanceDistance);
            hasCurrentWaypoint = state.WaypointIndex >= 0 && state.WaypointIndex < state.Route.Count;
        }

        var routeLegClear = hasCurrentWaypoint && IsRouteLegClear(index, start, state.Route[state.WaypointIndex]);
        if (routeLegClear && state.LooseAdvanceWaypointIndex == state.WaypointIndex)
        {
            state.LooseAdvanceWaypointIndex = -1;
        }

        var routeMatches = routeIdentityMatches &&
                           hasCurrentWaypoint &&
                           (routeLegClear || state.LooseAdvanceWaypointIndex == state.WaypointIndex);
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
            state.LooseAdvanceWaypointIndex = -1;
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

        ObserveWaypointMovement(state, start, waypointPrecisionDistance, waypointAdvanceDistance);
        AdvanceReachedWaypoints(state, index, start, waypointPrecisionDistance, waypointAdvanceDistance);

        var destination = state.Route[Math.Clamp(state.WaypointIndex, 0, state.Route.Count - 1)];
        var isFinal = state.WaypointIndex == state.Route.Count - 1;
        var nextLegBlockedInsideReach = !isFinal &&
                                        state.CurrentWaypointReachedPrecisely &&
                                        !IsRouteLegClear(index, start, state.Route[state.WaypointIndex + 1]);
        return new RadarNavigationDecision(
            RadarNavigationAction.MoveToWaypoint,
            destination,
            isFinal
                ? finalReachDistanceMeters
                : nextLegBlockedInsideReach
                    ? 0.0D
                    : waypointPrecisionDistance,
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

    private static double ResolveWaypointPrecisionDistance(double waypointReachDistance)
    {
        return Math.Min(waypointReachDistance, MaximumPrecisionCorrectionMeters);
    }

    private static double ResolveWaypointAdvanceDistance(double waypointReachDistance)
    {
        return Math.Max(waypointReachDistance, LooseWaypointAdvanceMeters);
    }

    private async Task<RadarScoringMap?> LoadScoringMapAsync(
        uint mapId,
        CancellationToken cancellationToken)
    {
        var revision = _revisions.Get(mapId);
        lock (_scoreMapSync)
        {
            if (_scoreMapCache is { } cached &&
                cached.MapId == mapId &&
                cached.Revision == revision)
            {
                return cached;
            }
        }

        var load = await _mapStore.LoadAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (!load.Success || load.Value is null)
        {
            return null;
        }

        var loaded = new RadarScoringMap(
            mapId,
            revision,
            load.Value.Found,
            load.Value.Document,
            new RadarObstacleSpatialIndex(load.Value.Document.Segments));
        lock (_scoreMapSync)
        {
            _scoreMapCache = loaded;
        }

        return loaded;
    }

    private RadarRouteDistanceScore ScoreRouteDistance(
        uint mapId,
        RadarPoint start,
        RadarRouteDistanceRequest request,
        RadarObstacleSpatialIndex index,
        RadarObstacleScriptSettings settings)
    {
        var directDistance = start.DistanceTo(request.Goal);
        var directObstacles = index.QueryCorridor(start, request.Goal, 1.0D);
        if (RadarGeometry.IsPathClear(start, request.Goal, directObstacles))
        {
            return new RadarRouteDistanceScore(
                request.Key,
                directDistance,
                directDistance,
                true,
                false,
                mapId,
                directObstacles.Count,
                "direct");
        }

        var margin = Math.Max(
            10.0D,
            settings.MaximumDetourExtraMeters + 2.0D);
        var localObstacles = index.QueryCorridor(start, request.Goal, margin);
        var plan = _planner.Plan(new RadarRouteRequest(
            start,
            request.Goal,
            localObstacles,
            settings.MaximumDetourExtraMeters));
        if (!plan.Success && localObstacles.Count < index.All.Count)
        {
            plan = _planner.Plan(new RadarRouteRequest(
                start,
                request.Goal,
                index.All,
                settings.MaximumDetourExtraMeters));
        }

        return plan.Success
            ? new RadarRouteDistanceScore(
                request.Key,
                directDistance,
                plan.RouteDistance,
                true,
                true,
                mapId,
                plan.EvaluatedObstacleCount,
                plan.Reason)
            : new RadarRouteDistanceScore(
                request.Key,
                directDistance,
                double.MaxValue,
                false,
                true,
                mapId,
                plan.EvaluatedObstacleCount,
                plan.Reason);
    }

    private static RadarRouteDistanceScoreBatch CreateDirectScoreBatch(
        uint mapId,
        RadarPoint start,
        IReadOnlyList<RadarRouteDistanceRequest> requests,
        string reason)
    {
        var scores = requests.ToDictionary(
            request => request.Key,
            request =>
            {
                var directDistance = start.DistanceTo(request.Goal);
                return new RadarRouteDistanceScore(
                    request.Key,
                    directDistance,
                    directDistance,
                    true,
                    false,
                    mapId,
                    0,
                    reason);
            },
            StringComparer.Ordinal);
        return new RadarRouteDistanceScoreBatch(mapId, false, reason, scores);
    }

    private static void ObserveWaypointMovement(
        StationaryObstacleNavigationState state,
        RadarPoint start,
        double waypointPrecisionDistance,
        double waypointAdvanceDistance)
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
        var distanceToWaypoint = start.DistanceTo(waypoint);
        var reachedPreciselyByCurrentSample = distanceToWaypoint <= waypointPrecisionDistance;
        var reachedLooselyAfterHardReach = state.HasWaypointMovementSample &&
                                           distanceToWaypoint > waypointPrecisionDistance &&
                                           distanceToWaypoint <= waypointAdvanceDistance &&
                                           MovedAcrossWaypointSide(
                                               state.LastWaypointMovementSample,
                                               start,
                                               waypoint,
                                               waypointAdvanceDistance);
        if (reachedPreciselyByCurrentSample)
        {
            state.CurrentWaypointReached = true;
            state.CurrentWaypointReachedPrecisely = true;
        }
        else if (reachedLooselyAfterHardReach)
        {
            state.CurrentWaypointReached = true;
        }

        state.LastWaypointMovementSample = start;
        state.HasWaypointMovementSample = true;
        state.LastWaypointMovementSampleIndex = state.WaypointIndex;
    }

    private static bool MovedAcrossWaypointSide(
        RadarPoint previous,
        RadarPoint current,
        RadarPoint waypoint,
        double waypointAdvanceDistance)
    {
        var previousX = previous.X - waypoint.X;
        var previousY = previous.Y - waypoint.Y;
        var currentX = current.X - waypoint.X;
        var currentY = current.Y - waypoint.Y;
        var dot = previousX * currentX + previousY * currentY;
        return dot <= 0.0D &&
               RadarGeometry.PointToSegmentDistance(waypoint, previous, current) <= waypointAdvanceDistance;
    }

    private static void AdvanceReachedWaypoints(
        StationaryObstacleNavigationState state,
        RadarObstacleSpatialIndex index,
        RadarPoint start,
        double waypointPrecisionDistance,
        double waypointAdvanceDistance)
    {
        while (state.CurrentWaypointReached &&
               state.WaypointIndex >= 0 &&
               state.WaypointIndex < state.Route.Count - 1)
        {
            var nextLegClear = IsRouteLegClear(index, start, state.Route[state.WaypointIndex + 1]);
            if (state.CurrentWaypointReachedPrecisely && !nextLegClear)
            {
                break;
            }

            state.WaypointIndex++;
            state.LooseAdvanceWaypointIndex = nextLegClear ? -1 : state.WaypointIndex;
            ResetWaypointMovementObservation(state, start);
            ObserveWaypointMovement(state, start, waypointPrecisionDistance, waypointAdvanceDistance);
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
        state.CurrentWaypointReachedPrecisely = false;
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

    private sealed record RadarScoringMap(
        uint MapId,
        long Revision,
        bool Found,
        RadarMapDocument Document,
        RadarObstacleSpatialIndex Index);
}
