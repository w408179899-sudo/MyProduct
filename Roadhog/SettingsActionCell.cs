using System.Drawing.Drawing2D;

namespace Roadhog;

// Keep native button-cell hit testing, keyboard actions and accessibility.
internal sealed class SettingsActionCell : DataGridViewButtonCell
{
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText,
            cellStyle, advancedBorderStyle, paintParts & ~(DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.ContentBackground | DataGridViewPaintParts.Focus));
        if (!paintParts.HasFlag(DataGridViewPaintParts.ContentForeground)) return;
        var bounds = Rectangle.Inflate(cellBounds, -4, -6);
        if (bounds.Width < 8 || bounds.Height < 8) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UiChrome.RoundedRect(bounds, 7);
            using var fill = new LinearGradientBrush(bounds, ControlPaint.Light(SettingsPalette.Primary, .12f),
                ControlPaint.Dark(SettingsPalette.Primary, .08f), LinearGradientMode.Vertical);
            using var border = new Pen(SettingsPalette.Border);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            using var font = new Font(cellStyle.Font ?? Control.DefaultFont, FontStyle.Bold);
            TextRenderer.DrawText(graphics, formattedValue?.ToString() ?? "", font, bounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (DataGridView?.Focused == true && DataGridView.CurrentCell == this)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -3, -3), Color.White, SettingsPalette.Primary);
        }
        finally { graphics.Restore(state); }
    }
}
