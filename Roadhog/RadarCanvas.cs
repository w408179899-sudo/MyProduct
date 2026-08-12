using System.Drawing.Drawing2D;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;

namespace Roadhog;

internal enum RadarCanvasMode
{
    Select,
    DrawObstacle
}

internal sealed class RadarSegmentCreatedEventArgs : EventArgs
{
    public RadarSegmentCreatedEventArgs(RadarPoint start, RadarPoint end)
    {
        Start = start;
        End = end;
    }

    public RadarPoint Start { get; }

    public RadarPoint End { get; }
}

internal sealed class RadarCanvas : Control
{
    private const string TopCompassLabel = "N  (\u5317 / -X)";
    private const string LeftCompassLabel = "W  (-Y)";
    private const string RightCompassLabel = "E  (\u4e1c / +Y)";
    private const string BottomCompassLabel = "S  (+X)";
    private const int PlayerMarkerArgb = unchecked((int)0xFF3753D6);
    private const int AggressiveMonsterMarkerArgb = unchecked((int)0xFFDC2626);
    private const int PassiveMonsterMarkerArgb = unchecked((int)0xFF16A34A);
    private const int UnknownMonsterMarkerArgb = unchecked((int)0xFF64748B);

    private RadarMapDocument _document = new();
    private RadarLiveSnapshot? _snapshot;
    private RadarRoutePlan? _routePlan;
    private RadarPoint _fixedCenter;
    private bool _hasFixedCenter;
    private bool _panning;
    private Point _lastMouse;
    private RadarPoint? _drawStart;
    private RadarPoint? _drawCurrent;
    private double _displayRangeMeters = RadarObstacleScriptSettings.DefaultDisplayRangeMeters;

    public RadarCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(248, 250, 252);
        Cursor = Cursors.Cross;
        TabStop = true;
    }

    public event EventHandler<RadarSegmentCreatedEventArgs>? SegmentCreated;

    public event EventHandler? SelectionChanged;

    public RadarCanvasMode Mode { get; set; } = RadarCanvasMode.DrawObstacle;

    public bool FollowPlayer { get; set; } = true;

    public bool ShowClearance { get; set; } = true;

    public bool ShowPlannedRoute { get; set; } = true;

    public double ClearanceMeters { get; set; } = RadarObstacleScriptSettings.DefaultClearanceMeters;

    public double DisplayRangeMeters
    {
        get => _displayRangeMeters;
        set
        {
            _displayRangeMeters = Math.Clamp(value, 10.0D, 1000.0D);
            Invalidate();
        }
    }

    public int SelectedSegmentIndex { get; private set; } = -1;

    public WorldObjectSnapshot? SelectedMonster { get; private set; }

    public RadarMapDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? new RadarMapDocument();
            SelectedSegmentIndex = -1;
            Invalidate();
        }
    }

    public RadarLiveSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            if (!_hasFixedCenter && value?.Player?.Position is { } position)
            {
                _fixedCenter = new RadarPoint(position.X, position.Y);
                _hasFixedCenter = true;
            }

            if (SelectedMonster is not null)
            {
                SelectedMonster = value?.WorldObjects.FirstOrDefault(item =>
                    (item.ServerObjectId != 0 && item.ServerObjectId == SelectedMonster.ServerObjectId) ||
                    (item.ServerObjectId == 0 && item.EntityId == SelectedMonster.EntityId));
            }

            Invalidate();
        }
    }

    public RadarRoutePlan? RoutePlan
    {
        get => _routePlan;
        set
        {
            _routePlan = value;
            Invalidate();
        }
    }

    public void SetMode(RadarCanvasMode mode)
    {
        Mode = mode;
        Cursor = mode == RadarCanvasMode.DrawObstacle ? Cursors.Cross : Cursors.Default;
        _drawStart = null;
        _drawCurrent = null;
        Invalidate();
    }

    public void ClearSelection()
    {
        SelectedSegmentIndex = -1;
        SelectedMonster = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public bool CancelPendingSegment()
    {
        var hadPendingSegment = _drawStart is not null;
        _drawStart = null;
        _drawCurrent = null;
        Capture = false;
        Invalidate();
        return hadPendingSegment;
    }

    public void CenterOnPlayer()
    {
        FollowPlayer = true;
        if (_snapshot?.Player?.Position is { } position)
        {
            _fixedCenter = new RadarPoint(position.X, position.Y);
            _hasFixedCenter = true;
        }

        Invalidate();
    }

    public void CenterOn(RadarPoint point)
    {
        FollowPlayer = false;
        _fixedCenter = point;
        _hasFixedCenter = true;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var factor = e.Delta > 0 ? 0.85D : 1.18D;
        DisplayRangeMeters *= factor;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button is MouseButtons.Middle or MouseButtons.Right)
        {
            _panning = true;
            _lastMouse = e.Location;
            Cursor = Cursors.Hand;
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (Mode == RadarCanvasMode.DrawObstacle)
        {
            var segment = RegisterDrawClick(ScreenToWorld(e.Location));
            if (segment is not null)
            {
                SegmentCreated?.Invoke(this, segment);
            }

            Invalidate();
            return;
        }

        SelectAt(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            var scale = PixelsPerMeter();
            if (scale > 0.0D)
            {
                var center = GetViewCenter();
                center = new RadarPoint(
                    center.X - (e.Y - _lastMouse.Y) / scale,
                    center.Y - (e.X - _lastMouse.X) / scale);
                _fixedCenter = center;
                _hasFixedCenter = true;
                FollowPlayer = false;
            }

            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (_drawStart is not null)
        {
            _drawCurrent = ScreenToWorld(e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && e.Button is MouseButtons.Middle or MouseButtons.Right)
        {
            _panning = false;
            Cursor = Mode == RadarCanvasMode.DrawObstacle ? Cursors.Cross : Cursors.Default;
            return;
        }

    }

    private RadarSegmentCreatedEventArgs? RegisterDrawClick(RadarPoint point)
    {
        if (_drawStart is not { } start)
        {
            _drawStart = point;
            _drawCurrent = point;
            return null;
        }

        _drawCurrent = point;
        if (start.DistanceTo(point) < 0.10D)
        {
            return null;
        }

        _drawStart = null;
        _drawCurrent = null;
        return new RadarSegmentCreatedEventArgs(start, point);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawGrid(e.Graphics);
        DrawClearance(e.Graphics);
        DrawObstacles(e.Graphics);
        DrawRoute(e.Graphics);
        DrawWorldObjects(e.Graphics);
        DrawPlayer(e.Graphics);
        DrawDraft(e.Graphics);
        DrawCompassAndInfo(e.Graphics);
    }

    private void DrawGrid(Graphics graphics)
    {
        var center = GetViewCenter();
        var scale = PixelsPerMeter();
        var gridMeters = SelectGridStep();
        var halfHorizontalMeters = Width / Math.Max(1.0D, scale) / 2.0D;
        var halfVerticalMeters = Height / Math.Max(1.0D, scale) / 2.0D;
        var firstX = Math.Floor((center.X - halfVerticalMeters) / gridMeters) * gridMeters;
        var lastX = center.X + halfVerticalMeters;
        var firstY = Math.Floor((center.Y - halfHorizontalMeters) / gridMeters) * gridMeters;
        var lastY = center.Y + halfHorizontalMeters;
        using var gridPen = new Pen(Color.FromArgb(226, 232, 240), 1.0F);
        using var axisPen = new Pen(Color.FromArgb(148, 163, 184), 1.25F);
        for (var x = firstX; x <= lastX; x += gridMeters)
        {
            var left = WorldToScreen(new RadarPoint(x, center.Y - halfHorizontalMeters));
            var right = WorldToScreen(new RadarPoint(x, center.Y + halfHorizontalMeters));
            graphics.DrawLine(Math.Abs(x) < 0.001D ? axisPen : gridPen, left, right);
        }

        for (var y = firstY; y <= lastY; y += gridMeters)
        {
            var top = WorldToScreen(new RadarPoint(center.X - halfVerticalMeters, y));
            var bottom = WorldToScreen(new RadarPoint(center.X + halfVerticalMeters, y));
            graphics.DrawLine(Math.Abs(y) < 0.001D ? axisPen : gridPen, top, bottom);
        }
    }

    private void DrawClearance(Graphics graphics)
    {
        if (!ShowClearance || ClearanceMeters <= 0.0D)
        {
            return;
        }

        var width = Math.Max(2.0F, (float)(ClearanceMeters * PixelsPerMeter() * 2.0D));
        using var clearancePen = new Pen(Color.FromArgb(42, 239, 68, 68), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        foreach (var segment in _document.Segments)
        {
            graphics.DrawLine(clearancePen, WorldToScreen(segment.Start), WorldToScreen(segment.End));
        }
    }

    private void DrawObstacles(Graphics graphics)
    {
        for (var index = 0; index < _document.Segments.Count; index++)
        {
            var segment = _document.Segments[index];
            using var pen = new Pen(
                index == SelectedSegmentIndex ? Color.FromArgb(220, 38, 38) : Color.FromArgb(15, 23, 42),
                index == SelectedSegmentIndex ? 4.0F : 3.0F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pen, WorldToScreen(segment.Start), WorldToScreen(segment.End));
        }
    }

    private void DrawRoute(Graphics graphics)
    {
        if (!ShowPlannedRoute || _routePlan?.Points is not { Count: > 1 } points)
        {
            return;
        }

        using var routePen = new Pen(Color.FromArgb(245, 158, 11), 3.0F)
        {
            DashStyle = DashStyle.Dash,
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };
        for (var index = 1; index < points.Count; index++)
        {
            graphics.DrawLine(routePen, WorldToScreen(points[index - 1]), WorldToScreen(points[index]));
        }

        using var waypointBrush = new SolidBrush(Color.FromArgb(245, 158, 11));
        foreach (var point in points.Skip(1).Take(Math.Max(0, points.Count - 2)))
        {
            var screen = WorldToScreen(point);
            graphics.FillEllipse(waypointBrush, screen.X - 4, screen.Y - 4, 8, 8);
        }
    }

    private void DrawWorldObjects(Graphics graphics)
    {
        if (_snapshot is null)
        {
            return;
        }

        using var aggressiveBrush = new SolidBrush(Color.FromArgb(AggressiveMonsterMarkerArgb));
        using var passiveBrush = new SolidBrush(Color.FromArgb(PassiveMonsterMarkerArgb));
        using var unknownBrush = new SolidBrush(Color.FromArgb(UnknownMonsterMarkerArgb));
        using var selectedPen = new Pen(Color.FromArgb(245, 158, 11), 2.0F);
        foreach (var monster in _snapshot.WorldObjects.Where(StationaryCombatTargetSelector.IsSelectableMonster))
        {
            var position = monster.Position!.Value;
            var screen = WorldToScreen(new RadarPoint(position.X, position.Y));
            var markerArgb = GetMonsterMarkerArgb(monster);
            var monsterBrush = markerArgb == AggressiveMonsterMarkerArgb
                ? aggressiveBrush
                : markerArgb == PassiveMonsterMarkerArgb
                    ? passiveBrush
                    : unknownBrush;
            graphics.FillEllipse(monsterBrush, screen.X - 4, screen.Y - 4, 8, 8);
            if (IsSameMonster(monster, SelectedMonster))
            {
                graphics.DrawEllipse(selectedPen, screen.X - 8, screen.Y - 8, 16, 16);
            }
        }
    }

    private void DrawPlayer(Graphics graphics)
    {
        if (_snapshot?.Player?.Position is not { } position)
        {
            return;
        }

        var screen = WorldToScreen(new RadarPoint(position.X, position.Y));
        using var playerBrush = new SolidBrush(Color.FromArgb(PlayerMarkerArgb));
        using var outline = new Pen(Color.White, 2.0F);
        graphics.FillEllipse(playerBrush, screen.X - 7, screen.Y - 7, 14, 14);
        graphics.DrawEllipse(outline, screen.X - 7, screen.Y - 7, 14, 14);
    }

    private void DrawDraft(Graphics graphics)
    {
        if (_drawStart is not { } start || _drawCurrent is not { } current)
        {
            return;
        }

        using var draftPen = new Pen(Color.FromArgb(22, 163, 74), 3.0F)
        {
            DashStyle = DashStyle.Dash
        };
        graphics.DrawLine(draftPen, WorldToScreen(start), WorldToScreen(current));
    }

    private void DrawCompassAndInfo(Graphics graphics)
    {
        using var font = new Font(Font.FontFamily, 9.0F, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(30, 41, 59));
        using var mutedBrush = new SolidBrush(Color.FromArgb(71, 85, 105));
        var topSize = graphics.MeasureString(TopCompassLabel, font);
        var leftSize = graphics.MeasureString(LeftCompassLabel, font);
        var rightSize = graphics.MeasureString(RightCompassLabel, font);
        var bottomSize = graphics.MeasureString(BottomCompassLabel, font);
        graphics.DrawString(TopCompassLabel, font, brush, (Width - topSize.Width) / 2.0F, 8.0F);
        graphics.DrawString(LeftCompassLabel, font, brush, 8.0F, (Height - leftSize.Height) / 2.0F);
        graphics.DrawString(RightCompassLabel, font, brush, Width - rightSize.Width - 8.0F, (Height - rightSize.Height) / 2.0F);
        graphics.DrawString(BottomCompassLabel, font, brush, (Width - bottomSize.Width) / 2.0F, Height - bottomSize.Height - 8.0F);
        var center = GetViewCenter();
        graphics.DrawString(
            $"MapId: {_document.MapId}   \u4e2d\u5fc3: {center.X:F1}, {center.Y:F1}   \u969c\u788d: {_document.Segments.Count}",
            Font,
            mutedBrush,
            10.0F,
            10.0F);
    }

    private void SelectAt(Point screenPoint)
    {
        var scale = Math.Max(0.001D, PixelsPerMeter());
        var world = ScreenToWorld(screenPoint);
        var segmentIndex = -1;
        var bestSegmentDistance = 8.0D / scale;
        for (var index = 0; index < _document.Segments.Count; index++)
        {
            var segment = _document.Segments[index];
            var distance = RadarGeometry.PointToSegmentDistance(world, segment.Start, segment.End);
            if (distance <= bestSegmentDistance)
            {
                segmentIndex = index;
                bestSegmentDistance = distance;
            }
        }

        WorldObjectSnapshot? selectedMonster = null;
        var bestMonsterDistance = 12.0D / scale;
        if (_snapshot is not null)
        {
            foreach (var monster in _snapshot.WorldObjects.Where(StationaryCombatTargetSelector.IsSelectableMonster))
            {
                var position = monster.Position!.Value;
                var distance = world.DistanceTo(new RadarPoint(position.X, position.Y));
                if (distance <= bestMonsterDistance)
                {
                    selectedMonster = monster;
                    bestMonsterDistance = distance;
                }
            }
        }

        SelectedMonster = selectedMonster;
        SelectedSegmentIndex = selectedMonster is null ? segmentIndex : -1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private RadarPoint GetViewCenter()
    {
        if (FollowPlayer && _snapshot?.Player?.Position is { } position)
        {
            return new RadarPoint(position.X, position.Y);
        }

        return _hasFixedCenter ? _fixedCenter : default;
    }

    private PointF WorldToScreen(RadarPoint point)
    {
        var center = GetViewCenter();
        var scale = PixelsPerMeter();
        return new PointF(
            (float)(Width / 2.0D + (point.Y - center.Y) * scale),
            (float)(Height / 2.0D + (point.X - center.X) * scale));
    }

    private RadarPoint ScreenToWorld(Point point)
    {
        var center = GetViewCenter();
        var scale = Math.Max(0.001D, PixelsPerMeter());
        return new RadarPoint(
            center.X + (point.Y - Height / 2.0D) / scale,
            center.Y + (point.X - Width / 2.0D) / scale);
    }

    private double PixelsPerMeter()
    {
        return Math.Max(1.0D, Math.Min(Width, Height)) / (DisplayRangeMeters * 2.0D);
    }

    private double SelectGridStep()
    {
        var desired = DisplayRangeMeters / 8.0D;
        var power = Math.Pow(10.0D, Math.Floor(Math.Log10(Math.Max(0.1D, desired))));
        var normalized = desired / power;
        var step = normalized <= 1.0D ? 1.0D : normalized <= 2.0D ? 2.0D : normalized <= 5.0D ? 5.0D : 10.0D;
        return step * power;
    }

    private static bool IsSameMonster(WorldObjectSnapshot left, WorldObjectSnapshot? right)
    {
        return right is not null &&
               (left.ServerObjectId != 0 && left.ServerObjectId == right.ServerObjectId ||
                left.ServerObjectId == 0 && left.EntityId == right.EntityId);
    }

    private static int GetMonsterMarkerArgb(WorldObjectSnapshot monster)
    {
        if (monster.IsAggressiveToPlayer)
        {
            return AggressiveMonsterMarkerArgb;
        }

        return monster.IsPassiveToPlayer
            ? PassiveMonsterMarkerArgb
            : UnknownMonsterMarkerArgb;
    }
}
