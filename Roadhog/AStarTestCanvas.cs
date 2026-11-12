using System.Drawing.Drawing2D;
using Roadhog.Core.Model;
using Roadhog.Core.NavigationTests;

namespace Roadhog;

public sealed class AStarTestCanvas : Control
{
    public AStarTestMap? Map { get; set; }
    public Vector3Snapshot[] Route { get; set; } = Array.Empty<Vector3Snapshot>();
    public float FloorHeight { get; set; }
    public AStarTestCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(241, 248, 244);
        Dock = DockStyle.Fill;
        MinimumSize = new Size(180, 120);
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Map is not { } map)
        {
            TextRenderer.DrawText(e.Graphics, "加载导航地图后显示可通行节点与路线", Font, ClientRectangle, SettingsPalette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var minX = map.Minimum.X; var minY = map.Minimum.Y;
        var maxX = map.Maximum.X; var maxY = map.Maximum.Y;
        var scale = Math.Max(0.01f, Math.Min((Width - 48) / Math.Max(1, maxX - minX), (Height - 56) / Math.Max(1, maxY - minY)));
        var ox = (Width - (maxX - minX) * scale) / 2;
        var oy = (Height - 24 - (maxY - minY) * scale) / 2;
        PointF Project(Vector3Snapshot p) => new(ox + (p.X - minX) * scale, Height - 24 - oy - (p.Y - minY) * scale);
        using var nodeBrush = new SolidBrush(Color.FromArgb(102, 164, 147));
        var size = Math.Clamp(scale * 0.65f, 1, 7);
        var stride = Math.Max(1, map.Count / 60_000);
        for (var i = 0; i < map.Count; i += stride)
        {
            var n = map.Position(i);
            if (Math.Abs(n.Z - FloorHeight) > 2) continue;
            var p = Project(n);
            e.Graphics.FillRectangle(nodeBrush, p.X - size / 2, p.Y - size / 2, size, size);
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(225, 136, 25), 2.5f);
        if (Route.Length > 1) e.Graphics.DrawLines(pen, Route.Select(Project).ToArray());
        if (Route.Length > 0)
        {
            DrawMarker(Route[0], Color.ForestGreen, "起点");
            DrawMarker(Route[^1], Color.Crimson, "终点");
        }
        TextRenderer.DrawText(e.Graphics, $"俯视图 · 节点显示高度 {FloorHeight:F1} ± 2 米 · 上方为 +Y", Font,
            new Rectangle(8, Height - 24, Width - 16, 22), SettingsPalette.Text);
        void DrawMarker(Vector3Snapshot position, Color color, string text)
        {
            var p = Project(position);
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, p.X - 5, p.Y - 5, 10, 10);
            TextRenderer.DrawText(e.Graphics, text, Font, new Point((int)p.X + 7, (int)p.Y - 17), color);
        }
    }
}
