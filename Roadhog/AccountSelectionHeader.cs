namespace Roadhog;

internal sealed class AccountSelectionHeader : CheckBox
{
    private readonly DataGridView grid;
    private bool updating;

    public AccountSelectionHeader(DataGridView grid)
    {
        this.grid = grid;
        Name = "accountSelectAllCheckBox";
        AccessibleName = "全部勾选当前列表账号";
        AutoCheck = false;
        Size = new Size(18, 18);
        CheckAlign = ContentAlignment.MiddleCenter;
        BackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        grid.Controls.Add(this);
        Click += (_, _) => SetAll(CheckState != CheckState.Checked);
        grid.CellValueChanged += (_, e) => { if (e.ColumnIndex == 0) Synchronize(); };
        grid.RowsAdded += (_, _) => Synchronize();
        grid.RowsRemoved += (_, _) => Synchronize();
        grid.SizeChanged += (_, _) => Position();
        grid.ColumnWidthChanged += (_, _) => Position();
        grid.ColumnHeadersHeightChanged += (_, _) => Position();
        grid.Scroll += (_, _) => Position();
        Synchronize();
        Position();
    }

    private void SetAll(bool value)
    {
        updating = true;
        try
        {
            grid.EndEdit();
            foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = value;
        }
        finally { updating = false; Synchronize(); }
    }

    private void Synchronize()
    {
        if (updating) return;
        var count = grid.Rows.Cast<DataGridViewRow>().Count(row => row.Cells[0].Value is true);
        Enabled = grid.Rows.Count > 0;
        CheckState = count == 0 ? CheckState.Unchecked
            : count == grid.Rows.Count ? CheckState.Checked : CheckState.Indeterminate;
    }

    private void Position()
    {
        var bounds = grid.GetCellDisplayRectangle(0, -1, true);
        Visible = grid.ColumnHeadersVisible && bounds.Width >= Width && bounds.Height >= Height;
        Location = new Point(bounds.Left + (bounds.Width - Width) / 2, bounds.Top + (bounds.Height - Height) / 2);
        BringToFront();
    }
}
