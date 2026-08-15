using Roadhog.Application.Radar;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Radar;

internal static class RadarTests
{
    public static Task CanvasProjectionIsNorthUpAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvasType = typeof(Roadhog.AccountSettingsForm).Assembly.GetType("Roadhog.RadarCanvas");
                Require(canvasType is not null, "radar canvas type should exist");
                Require(
                    ReadCompassLabel(canvasType!, "TopCompassLabel") == "N  (\u5317 / -X)",
                    "radar top should represent north and negative X");
                Require(
                    ReadCompassLabel(canvasType!, "LeftCompassLabel") == "W  (-Y)",
                    "radar left should represent west and negative Y");
                Require(
                    ReadCompassLabel(canvasType!, "RightCompassLabel") == "E  (\u4e1c / +Y)",
                    "radar right should represent east and positive Y");
                Require(
                    ReadCompassLabel(canvasType!, "BottomCompassLabel") == "S  (+X)",
                    "radar bottom should represent south and positive X");

                using var canvas = (System.Windows.Forms.Control)Activator.CreateInstance(canvasType!, nonPublic: true)!;
                canvas.Size = new System.Drawing.Size(400, 300);
                canvasType!.GetProperty("DisplayRangeMeters")!.SetValue(canvas, 100.0D);
                canvasType.GetMethod("CenterOn")!.Invoke(canvas, new object[] { new RadarPoint(100.0D, 200.0D) });
                var worldToScreen = canvasType.GetMethod(
                    "WorldToScreen",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var screenToWorld = canvasType.GetMethod(
                    "ScreenToWorld",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Require(worldToScreen is not null && screenToWorld is not null, "radar projection methods should exist");

                var north = (System.Drawing.PointF)worldToScreen!.Invoke(
                    canvas,
                    new object[] { new RadarPoint(90.0D, 200.0D) })!;
                var east = (System.Drawing.PointF)worldToScreen.Invoke(
                    canvas,
                    new object[] { new RadarPoint(100.0D, 210.0D) })!;
                RequireNear(200.0D, north.X, "north should keep the horizontal center");
                RequireNear(135.0D, north.Y, "negative X should project upward");
                RequireNear(215.0D, east.X, "positive Y should project rightward");
                RequireNear(150.0D, east.Y, "east should keep the vertical center");

                var restoredNorth = (RadarPoint)screenToWorld!.Invoke(
                    canvas,
                    new object[] { System.Drawing.Point.Round(north) })!;
                RequireNear(90.0D, restoredNorth.X, "north-up screen conversion should restore X");
                RequireNear(200.0D, restoredNorth.Y, "north-up screen conversion should restore Y");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }

        return Task.CompletedTask;
    }

    public static Task SettingsDefaultDisabledAndCloneAsync()
    {
        var settings = new RadarObstacleScriptSettings();
        Require(!settings.Enabled, "radar obstacle avoidance must default to disabled");
        settings.Enabled = true;
        settings.WaypointReachMeters = 4.25D;
        settings.DisplayRangeMeters = 180.0D;

        var clone = settings.Clone();
        Require(clone.Enabled, "clone should preserve enabled state");
        RequireNear(4.25D, clone.WaypointReachMeters, "clone should preserve waypoint reach distance");
        RequireNear(180.0D, clone.DisplayRangeMeters, "clone should preserve display range");
        clone.WaypointReachMeters = 1.0D;
        RequireNear(4.25D, settings.WaypointReachMeters, "clone should be independent");
        return Task.CompletedTask;
    }

    public static Task CanvasMarkerColorsMatchDispositionAsync()
    {
        var canvasType = typeof(Roadhog.AccountSettingsForm).Assembly.GetType("Roadhog.RadarCanvas");
        Require(canvasType is not null, "radar canvas type should exist");
        Require(
            ReadConstantInt(canvasType!, "PlayerMarkerArgb") == System.Drawing.Color.FromArgb(55, 83, 214).ToArgb(),
            "player marker should be blue");

        var aggressive = new Roadhog.Core.Model.WorldObjectSnapshot(
            1,
            1,
            "aggressive",
            "monster",
            null,
            null,
            AggressiveKnown: true,
            IsAggressiveToPlayer: true);
        var passive = new Roadhog.Core.Model.WorldObjectSnapshot(
            2,
            2,
            "passive",
            "monster",
            null,
            null,
            AggressiveKnown: true,
            IsAggressiveToPlayer: false);
        var unknown = new Roadhog.Core.Model.WorldObjectSnapshot(
            3,
            3,
            "unknown",
            "monster",
            null,
            null);
        Require(
            ReadMonsterMarkerArgb(canvasType!, aggressive) == System.Drawing.Color.FromArgb(220, 38, 38).ToArgb(),
            "aggressive monster marker should be red");
        Require(
            ReadMonsterMarkerArgb(canvasType!, passive) == System.Drawing.Color.FromArgb(22, 163, 74).ToArgb(),
            "passive monster marker should be green");
        Require(
            ReadMonsterMarkerArgb(canvasType!, unknown) == System.Drawing.Color.FromArgb(100, 116, 139).ToArgb(),
            "unknown monster marker should be neutral gray");
        return Task.CompletedTask;
    }

    public static Task CanvasDrawsContinuousObstaclesUntilCancelledAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvasType = typeof(Roadhog.AccountSettingsForm).Assembly.GetType("Roadhog.RadarCanvas");
                Require(canvasType is not null, "radar canvas type should exist");
                using var canvas = (System.Windows.Forms.Control)Activator.CreateInstance(canvasType!, nonPublic: true)!;
                var registerClick = canvasType!.GetMethod(
                    "RegisterDrawClick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var drawStart = canvasType.GetField(
                    "_drawStart",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var mouseDown = canvasType.GetMethod(
                    "OnMouseDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var cancelPending = canvasType.GetMethod("CancelPendingSegment");
                var panning = canvasType.GetField(
                    "_panning",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Require(
                    registerClick is not null && drawStart is not null && mouseDown is not null &&
                    cancelPending is not null && panning is not null,
                    "radar continuous drawing members should exist");

                var first = registerClick!.Invoke(canvas, new object[] { new RadarPoint(10.0D, 20.0D) });
                Require(first is null, "first click should only set the obstacle start point");
                Require(drawStart!.GetValue(canvas) is RadarPoint, "first click should enter continuous drawing state");

                var tooClose = registerClick.Invoke(canvas, new object[] { new RadarPoint(10.05D, 20.0D) });
                Require(tooClose is null, "a near-identical second click should not create a tiny obstacle");
                Require(drawStart.GetValue(canvas) is RadarPoint, "rejected tiny obstacle should keep the start point pending");

                var second = registerClick.Invoke(canvas, new object[] { new RadarPoint(15.0D, 25.0D) });
                Require(second is not null, "second valid click should create the obstacle segment");
                Require(drawStart.GetValue(canvas) is RadarPoint, "completed obstacle should retain the endpoint as the next start");
                var secondAnchor = (RadarPoint)drawStart.GetValue(canvas)!;
                RequireNear(15.0D, secondAnchor.X, "second endpoint should become the next start X");
                RequireNear(25.0D, secondAnchor.Y, "second endpoint should become the next start Y");
                var start = (RadarPoint)second!.GetType().GetProperty("Start")!.GetValue(second)!;
                var end = (RadarPoint)second.GetType().GetProperty("End")!.GetValue(second)!;
                RequireNear(10.0D, start.X, "first continuous obstacle should preserve start X");
                RequireNear(20.0D, start.Y, "first continuous obstacle should preserve start Y");
                RequireNear(15.0D, end.X, "first continuous obstacle should preserve end X");
                RequireNear(25.0D, end.Y, "first continuous obstacle should preserve end Y");

                var third = registerClick.Invoke(canvas, new object[] { new RadarPoint(30.0D, 40.0D) });
                Require(third is not null, "third click should create a connected obstacle segment");
                var thirdStart = (RadarPoint)third!.GetType().GetProperty("Start")!.GetValue(third)!;
                var thirdEnd = (RadarPoint)third.GetType().GetProperty("End")!.GetValue(third)!;
                RequireNear(15.0D, thirdStart.X, "connected obstacle should start at the previous endpoint X");
                RequireNear(25.0D, thirdStart.Y, "connected obstacle should start at the previous endpoint Y");
                RequireNear(30.0D, thirdEnd.X, "connected obstacle should preserve its endpoint X");
                RequireNear(40.0D, thirdEnd.Y, "connected obstacle should preserve its endpoint Y");

                mouseDown!.Invoke(
                    canvas,
                    new object[]
                    {
                        new System.Windows.Forms.MouseEventArgs(
                            System.Windows.Forms.MouseButtons.Right,
                            1,
                            100,
                            100,
                            0)
                    });
                Require(drawStart.GetValue(canvas) is null, "right click should end the current continuous drawing state");
                Require(!(bool)panning!.GetValue(canvas)!, "right click must not pan while drawing obstacles");

                var restarted = registerClick.Invoke(canvas, new object[] { new RadarPoint(50.0D, 60.0D) });
                Require(restarted is null, "the first click after cancellation should start a new independent chain");
                Require((bool)cancelPending!.Invoke(canvas, null)!, "explicit cancellation should report the restarted chain");
                Require(drawStart.GetValue(canvas) is null, "explicit cancellation should clear the restarted chain");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }

        return Task.CompletedTask;
    }

    public static Task GeometryDetectsSegmentIntersectionAsync()
    {
        var wall = Segment(10.0D, -5.0D, 10.0D, 5.0D);
        Require(
            !RadarGeometry.IsPathClear(
                new RadarPoint(0.0D, 0.0D),
                new RadarPoint(20.0D, 0.0D),
                new[] { wall }),
            "crossing wall must block direct path");
        Require(
            !RadarGeometry.IsPathClear(
                new RadarPoint(0.0D, 5.0D),
                new RadarPoint(20.0D, 5.0D),
                new[] { wall }),
            "path touching a wall endpoint must be blocked");
        Require(
            RadarGeometry.IsPathClear(
                new RadarPoint(0.0D, 5.01D),
                new RadarPoint(20.0D, 5.01D),
                new[] { wall }),
            "nearby path that does not intersect the wall should be clear");
        return Task.CompletedTask;
    }

    public static Task PlannerKeepsClearDirectRouteAsync()
    {
        var planner = new RadarRoutePlanner();
        var plan = planner.Plan(new RadarRouteRequest(
            new RadarPoint(0.0D, 0.0D),
            new RadarPoint(40.0D, 0.0D),
            new[] { Segment(100.0D, -5.0D, 100.0D, 5.0D) }));

        Require(plan.Success, "clear direct route should succeed");
        Require(plan.Direct, "clear route should remain direct");
        Require(plan.Points.Count == 2, "direct route should contain start and goal only");
        RequireNear(40.0D, plan.RouteDistance, "direct route length should match direct distance");
        return Task.CompletedTask;
    }

    public static Task PlannerRoutesAroundWallAsync()
    {
        var obstacles = new[] { Segment(20.0D, -8.0D, 20.0D, 8.0D) };
        var planner = new RadarRoutePlanner();
        var plan = planner.Plan(new RadarRouteRequest(
            new RadarPoint(0.0D, 0.0D),
            new RadarPoint(40.0D, 0.0D),
            obstacles,
            30.0D));

        Require(plan.Success, "wall detour should be routable");
        Require(!plan.Direct, "wall detour must not be marked direct");
        Require(plan.Points.Count >= 3, "wall detour should generate automatic waypoint(s)");
        Require(plan.RouteDistance > plan.DirectDistance, "detour should be longer than direct distance");
        foreach (var waypoint in plan.Points.Skip(1).SkipLast(1))
        {
            var nearestEndpointDistance = Math.Min(
                waypoint.DistanceTo(obstacles[0].Start),
                waypoint.DistanceTo(obstacles[0].End));
            RequireNear(3.0D, nearestEndpointDistance, "wall waypoint should use the three meter endpoint radius");
        }

        AssertEveryLegClear(plan, obstacles);
        return Task.CompletedTask;
    }

    public static Task PlannerRoutesAroundRightAngleAsync()
    {
        var obstacles = new[]
        {
            Segment(16.0D, -10.0D, 26.0D, 0.0D),
            Segment(26.0D, 0.0D, 16.0D, 10.0D)
        };
        var planner = new RadarRoutePlanner();
        var plan = planner.Plan(new RadarRouteRequest(
            new RadarPoint(0.0D, 0.0D),
            new RadarPoint(40.0D, 0.0D),
            obstacles,
            40.0D));

        Require(plan.Success, "right-angle obstacle should be routable");
        Require(plan.Points.Count >= 3, "right-angle obstacle should produce waypoint(s)");
        AssertEveryLegClear(plan, obstacles);
        return Task.CompletedTask;
    }

    public static Task SpatialIndexFiltersLargeMapToLocalCorridorAsync()
    {
        var segments = new List<RadarObstacleSegment>();
        for (var index = 0; index < 10; index++)
        {
            segments.Add(Segment(5.0D + index * 4.0D, -2.0D, 5.0D + index * 4.0D, 2.0D));
        }

        for (var index = 0; index < 190; index++)
        {
            segments.Add(Segment(1000.0D + index * 3.0D, 1000.0D, 1001.0D + index * 3.0D, 1001.0D));
        }

        segments.Add(Segment(-1000.0D, 20.0D, 1000.0D, 20.0D));
        var indexer = new RadarObstacleSpatialIndex(segments);
        var local = indexer.QueryCorridor(
            new RadarPoint(0.0D, 0.0D),
            new RadarPoint(45.0D, 0.0D),
            5.0D);

        Require(local.Count == 10, "local corridor should evaluate only the ten nearby obstacles");
        Require(indexer.All.Count == 201, "index should retain the complete static map");
        var longWall = indexer.QueryCorridor(
            new RadarPoint(-5.0D, 18.0D),
            new RadarPoint(5.0D, 22.0D),
            1.0D);
        Require(longWall.Any(segment => segment.Start.X == -1000.0D), "long walls must be indexed in every crossed cell");
        return Task.CompletedTask;
    }

    public static async Task JsonStoreRoundTripsAndAtomicallyReplacesAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonRadarMapStore(directory);
            var missing = await store.LoadAsync(210010000).ConfigureAwait(false);
            Require(missing.Success && missing.Value is { Found: false }, "missing map should load as an empty document");

            var document = new RadarMapDocument
            {
                MapId = 210010000,
                Segments = new List<RadarObstacleSegment>
                {
                    Segment(1.0D, 2.0D, 3.0D, 4.0D),
                    Segment(1.0D, 2.0D, 3.0D, 4.0D)
                }
            };
            var firstSave = await store.SaveAsync(document).ConfigureAwait(false);
            Require(firstSave.Success, "first map save should succeed");

            document.Segments = new List<RadarObstacleSegment> { Segment(10.0D, 20.0D, 30.0D, 40.0D) };
            var secondSave = await store.SaveAsync(document).ConfigureAwait(false);
            Require(secondSave.Success, "replacement map save should succeed");

            var loaded = await store.LoadAsync(document.MapId).ConfigureAwait(false);
            Require(loaded.Success && loaded.Value is { Found: true }, "saved map should load");
            Require(loaded.Value!.Document.Segments.Count == 1, "replacement should not append stale segments");
            RequireNear(10.0D, loaded.Value.Document.Segments[0].Start.X, "replacement should contain the latest segment");
            Require(
                Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly).Length == 0,
                "successful atomic replacement should leave no temporary file");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    public static async Task NavigatorHonorsDisabledSwitchAndPlansWhenEnabledAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonRadarMapStore(directory);
            var revisions = new RadarMapRevisionRegistry();
            var navigator = new StationaryObstacleNavigator(store, new RadarRoutePlanner(), revisions);
            var state = new StationaryObstacleNavigationState();
            var disabled = await navigator.ResolveAsync(
                    state,
                    123,
                    new RadarPoint(0.0D, 0.0D),
                    new RadarPoint(40.0D, 0.0D),
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    new RadarObstacleScriptSettings { Enabled = false },
                    25.0D)
                .ConfigureAwait(false);
            Require(disabled.Action == RadarNavigationAction.Direct, "disabled switch must preserve direct legacy behavior");
            Require(!Directory.Exists(directory), "disabled navigation should not touch map storage");

            Directory.CreateDirectory(directory);
            var save = await store.SaveAsync(new RadarMapDocument
            {
                MapId = 123,
                Segments = new List<RadarObstacleSegment> { Segment(20.0D, -8.0D, 20.0D, 8.0D) }
            }).ConfigureAwait(false);
            Require(save.Success, "enabled navigator test map should save");

            var enabled = await navigator.ResolveAsync(
                    state,
                    123,
                    new RadarPoint(0.0D, 0.0D),
                    new RadarPoint(40.0D, 0.0D),
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    new RadarObstacleScriptSettings
                    {
                        Enabled = true,
                        MaximumDetourExtraMeters = 30.0D
                    },
                    25.0D)
                .ConfigureAwait(false);
            Require(enabled.Action == RadarNavigationAction.MoveToWaypoint, "enabled switch should produce a detour waypoint");
            Require(enabled.Plan is { Success: true, Direct: false }, "enabled navigator should retain successful detour plan");
            Require(enabled.RelevantObstacleCount == 1, "enabled navigator should evaluate the local wall");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    public static async Task NavigatorAllowsScript4NearWallRouteAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonRadarMapStore(directory);
            var save = await store.SaveAsync(new RadarMapDocument
            {
                MapId = 47,
                Segments = new List<RadarObstacleSegment>
                {
                    Segment(2363.938D, 1498.532D, 2382.528D, 1488.109D),
                    Segment(2382.528D, 1488.109D, 2398.439D, 1502.21D)
                }
            }).ConfigureAwait(false);
            Require(save.Success, "script 4 radar map should save");

            var navigator = new StationaryObstacleNavigator(
                store,
                new RadarRoutePlanner(),
                new RadarMapRevisionRegistry());
            var decision = await navigator.ResolveAsync(
                    new StationaryObstacleNavigationState(),
                    47,
                    new RadarPoint(2386.553D, 1488.57D),
                    new RadarPoint(2427.03D, 1475.54D),
                    RadarNavigationPurpose.ApproachTarget,
                    123,
                    new RadarObstacleScriptSettings { Enabled = true },
                    25.0D)
                .ConfigureAwait(false);

            Require(
                decision.Action == RadarNavigationAction.Direct,
                "script 4 near-wall route should remain direct when it does not intersect a drawn line");
            Require(decision.Reason == "direct", "script 4 near-wall route should resolve as direct");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    public static async Task NavigatorDoesNotSkipWaypointAcrossWallAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var wall = Segment(10.0D, -5.0D, 10.0D, 5.0D);
            var store = new JsonRadarMapStore(directory);
            var save = await store.SaveAsync(new RadarMapDocument
            {
                MapId = 124,
                Segments = new List<RadarObstacleSegment> { wall }
            }).ConfigureAwait(false);
            Require(save.Success, "waypoint safety map should save");

            var navigator = new StationaryObstacleNavigator(
                store,
                new RadarRoutePlanner(),
                new RadarMapRevisionRegistry());
            var state = new StationaryObstacleNavigationState();
            var settings = new RadarObstacleScriptSettings
            {
                Enabled = true,
                WaypointReachMeters = 3.0D,
                MaximumDetourExtraMeters = 30.0D
            };
            var start = new RadarPoint(0.0D, 0.0D);
            var goal = new RadarPoint(20.0D, 0.0D);
            var first = await navigator.ResolveAsync(
                    state,
                    124,
                    start,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);
            Require(first.Action == RadarNavigationAction.MoveToWaypoint, "wall should produce a waypoint");
            Require(first.Plan is { Success: true, Direct: false }, "wall route should be a successful detour");
            RequireNear(1.5D, first.ReachDistanceMeters, "intermediate waypoint reach must be capped at 1.5 meters");

            var waypoint = new RadarPoint(10.0D, 5.5D);
            var nearWaypoint = new RadarPoint(9.0D, 4.75D);
            Require(
                !RadarGeometry.IsPathClear(nearWaypoint, goal, new[] { wall }),
                "the early next leg must still cross the wall");
            Require(
                RadarGeometry.IsPathClear(nearWaypoint, waypoint, new[] { wall }),
                "the current waypoint must remain directly reachable");

            state.Purpose = RadarNavigationPurpose.ApproachTarget;
            state.TargetServerObjectId = 99;
            state.PlannedGoal = goal;
            state.Route = new[] { waypoint, goal };
            state.WaypointIndex = 0;

            var second = await navigator.ResolveAsync(
                    state,
                    124,
                    nearWaypoint,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);

            Require(second.Action == RadarNavigationAction.MoveToWaypoint, "navigator should keep moving to the safe waypoint");
            RequireNear(waypoint.X, second.Destination.X, "unsafe early advance must keep waypoint X");
            RequireNear(waypoint.Y, second.Destination.Y, "unsafe early advance must keep waypoint Y");
            RequireNear(0.0D, second.ReachDistanceMeters, "blocked next leg must keep moving instead of reporting arrival");
            Require(second.Reason == "move_waypoint_precise", "blocked next leg should use precise waypoint movement");
            Require(
                RadarGeometry.IsPathClear(nearWaypoint, second.Destination, new[] { wall }),
                "retained waypoint leg must not intersect the wall");

            var guardedGoal = new RadarPoint(20.0D, -10.0D);
            var crossedFrom = new RadarPoint(12.0D, 3.5D);
            var crossedTo = new RadarPoint(8.0D, 7.5D);
            Require(
                RadarGeometry.PointToSegmentDistance(waypoint, crossedFrom, crossedTo) <= 0.0001D,
                "safety scenario movement segment should cross the current waypoint");
            Require(
                !RadarGeometry.IsPathClear(crossedTo, guardedGoal, new[] { wall }),
                "safety scenario next leg should remain blocked by the wall");

            state.ClearRoute();
            state.Purpose = RadarNavigationPurpose.ApproachTarget;
            state.TargetServerObjectId = 99;
            state.PlannedGoal = guardedGoal;
            state.Route = new[] { waypoint, guardedGoal };
            state.WaypointIndex = 0;
            state.HasWaypointMovementSample = true;
            state.LastWaypointMovementSample = crossedFrom;
            state.LastWaypointMovementSampleIndex = 0;

            var crossedButBlocked = await navigator.ResolveAsync(
                    state,
                    124,
                    crossedTo,
                    guardedGoal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);

            Require(state.WaypointIndex == 0, "crossed waypoint must remain pending while the next leg crosses the wall");
            RequireNear(waypoint.X, crossedButBlocked.Destination.X, "blocked crossed waypoint X");
            RequireNear(waypoint.Y, crossedButBlocked.Destination.Y, "blocked crossed waypoint Y");
            RequireNear(0.0D, crossedButBlocked.ReachDistanceMeters, "blocked crossed waypoint must retain precise movement");
            Require(crossedButBlocked.Reason == "move_waypoint_precise", "blocked crossed waypoint should retain precise reason");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    public static async Task NavigatorAdvancesWaypointCrossedBetweenSamplesAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var firstWall = Segment(10.0D, -5.0D, 10.0D, 5.0D);
            var secondWall = Segment(20.0D, -5.0D, 20.0D, 5.0D);
            var store = new JsonRadarMapStore(directory);
            var save = await store.SaveAsync(new RadarMapDocument
            {
                MapId = 125,
                Segments = new List<RadarObstacleSegment> { firstWall, secondWall }
            }).ConfigureAwait(false);
            Require(save.Success, "waypoint overshoot map should save");

            var navigator = new StationaryObstacleNavigator(
                store,
                new RadarRoutePlanner(),
                new RadarMapRevisionRegistry());
            var state = new StationaryObstacleNavigationState();
            var settings = new RadarObstacleScriptSettings
            {
                Enabled = true,
                WaypointReachMeters = 1.5D,
                MaximumDetourExtraMeters = 30.0D
            };
            var goal = new RadarPoint(30.0D, 0.0D);

            await navigator.ResolveAsync(
                    state,
                    125,
                    new RadarPoint(0.0D, 0.0D),
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);

            var firstWaypoint = new RadarPoint(10.0D, 5.5D);
            var secondWaypoint = new RadarPoint(20.0D, 5.5D);
            state.ClearRoute();
            state.Purpose = RadarNavigationPurpose.ApproachTarget;
            state.TargetServerObjectId = 99;
            state.PlannedGoal = goal;
            state.Route = new[] { firstWaypoint, secondWaypoint, goal };
            state.WaypointIndex = 0;

            var beforePosition = new RadarPoint(5.0D, 10.5D);
            var afterPosition = new RadarPoint(11.11D, 4.39D);
            RequireNear(
                1.57D,
                Math.Round(afterPosition.DistanceTo(firstWaypoint), 2),
                "post-overshoot distance should mirror the live 1.57 meter sample");

            var beforeCrossing = await navigator.ResolveAsync(
                    state,
                    125,
                    beforePosition,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);
            RequireNear(firstWaypoint.X, beforeCrossing.Destination.X, "pre-crossing waypoint X");
            RequireNear(firstWaypoint.Y, beforeCrossing.Destination.Y, "pre-crossing waypoint Y");

            var afterCrossing = await navigator.ResolveAsync(
                    state,
                    125,
                    afterPosition,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);

            Require(state.WaypointIndex == 1, "crossed waypoint should be committed exactly once");
            RequireNear(secondWaypoint.X, afterCrossing.Destination.X, "overshoot must advance to next waypoint X");
            RequireNear(secondWaypoint.Y, afterCrossing.Destination.Y, "overshoot must advance to next waypoint Y");
            Require(
                RadarGeometry.IsPathClear(
                    afterPosition,
                    afterCrossing.Destination,
                    new[] { firstWall, secondWall }),
                "overshoot advancement must keep the next leg clear");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    public static async Task NavigatorDirectCommitmentStaysOnSameTargetAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonRadarMapStore(directory);
            var save = await store.SaveAsync(new RadarMapDocument
            {
                MapId = 126,
                Segments = new List<RadarObstacleSegment>
                {
                    Segment(10.0D, -5.0D, 10.0D, 5.0D)
                }
            }).ConfigureAwait(false);
            Require(save.Success, "direct commitment map should save");

            var navigator = new StationaryObstacleNavigator(
                store,
                new RadarRoutePlanner(),
                new RadarMapRevisionRegistry());
            var state = new StationaryObstacleNavigationState();
            var settings = new RadarObstacleScriptSettings
            {
                Enabled = true,
                WaypointReachMeters = 1.5D,
                MaximumDetourExtraMeters = 30.0D
            };
            var start = new RadarPoint(0.0D, 0.0D);
            var goal = new RadarPoint(20.0D, 0.0D);

            var planned = await navigator.ResolveAsync(
                    state,
                    126,
                    start,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);
            Require(planned.Action == RadarNavigationAction.MoveToWaypoint, "wall should first produce a detour");

            state.CommitDirectApproach(99);
            var committed = await navigator.ResolveAsync(
                    state,
                    126,
                    start,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    99,
                    settings,
                    1.0D)
                .ConfigureAwait(false);

            Require(committed.Action == RadarNavigationAction.Direct, "same target should stay in direct approach");
            Require(committed.Reason == "direct_committed", "same target should expose direct commitment reason");
            Require(state.Route.Count == 0, "direct commitment should discard the old waypoint route");

            var replacement = await navigator.ResolveAsync(
                    state,
                    126,
                    start,
                    goal,
                    RadarNavigationPurpose.ApproachTarget,
                    100,
                    settings,
                    1.0D)
                .ConfigureAwait(false);
            Require(replacement.Action == RadarNavigationAction.MoveToWaypoint, "target change should restore obstacle planning");
            Require(!state.IsDirectApproachCommitted(99), "target change should clear the old direct commitment");

            state.CommitDirectApproach(100);
            state.ClearRoute();
            Require(!state.IsDirectApproachCommitted(100), "route reset should clear direct commitment");
        }
        finally
        {
            DeleteVerifiedTemporaryDirectory(directory);
        }
    }

    private static void AssertEveryLegClear(
        RadarRoutePlan plan,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        for (var index = 1; index < plan.Points.Count; index++)
        {
            Require(
                RadarGeometry.IsPathClear(plan.Points[index - 1], plan.Points[index], obstacles),
                "every planned route leg must avoid crossing an obstacle segment");
        }
    }

    private static RadarObstacleSegment Segment(double startX, double startY, double endX, double endY)
    {
        return new RadarObstacleSegment
        {
            Start = new RadarPoint(startX, startY),
            End = new RadarPoint(endX, endY)
        };
    }

    private static string? ReadCompassLabel(Type canvasType, string fieldName)
    {
        return canvasType
            .GetField(
                fieldName,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?
            .GetRawConstantValue() as string;
    }

    private static int ReadConstantInt(Type canvasType, string fieldName)
    {
        return (int)canvasType
            .GetField(
                fieldName,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetRawConstantValue()!;
    }

    private static int ReadMonsterMarkerArgb(Type canvasType, Roadhog.Core.Model.WorldObjectSnapshot monster)
    {
        return (int)canvasType
            .GetMethod(
                "GetMonsterMarkerArgb",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object[] { monster })!;
    }

    private static string CreateTemporaryDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "roadhog-radar-tests-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteVerifiedTemporaryDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        Require(
            fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(fullPath).StartsWith("roadhog-radar-tests-", StringComparison.Ordinal),
            "temporary test cleanup path must stay inside the verified radar test root");
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static void RequireNear(double expected, double actual, string message)
    {
        Require(Math.Abs(expected - actual) <= 0.0001D, message + $" (expected {expected}, actual {actual})");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed class StaticRadarMapStore : IRadarMapStore
{
    private readonly RadarMapDocument _document;

    public StaticRadarMapStore(RadarMapDocument document)
    {
        _document = document.Clone();
    }

    public string DirectoryPath => Path.GetTempPath();

    public Task<OperationResult<RadarMapLoadResult>> LoadAsync(
        uint mapId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(mapId == _document.MapId
            ? OperationResult<RadarMapLoadResult>.Ok(new RadarMapLoadResult(true, _document.Clone()))
            : OperationResult<RadarMapLoadResult>.Ok(new RadarMapLoadResult(false, new RadarMapDocument { MapId = mapId })));
    }

    public Task<OperationResult> SaveAsync(
        RadarMapDocument document,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Ok());
    }
}
