namespace Roadhog;

public sealed partial class MultiAccountForm
{
    private void FitToolbarRows(FlowLayoutPanel toolbar)
    {
        if (toolbar.ClientSize.Width <= 0 || toolbar.Controls.Count == 0) return;
        var requiredHeight = Math.Max(48, toolbar.Controls.Cast<Control>()
            .Max(control => control.Bottom + control.Margin.Bottom) + toolbar.Padding.Bottom + toolbar.Margin.Vertical);
        if ((int)_layout.RowStyles[0].Height == requiredHeight) return;
        _layout.RowStyles[0].Height = requiredHeight;
        _fittedRowCount = -1;
        FitHeightToRows();
    }
}
