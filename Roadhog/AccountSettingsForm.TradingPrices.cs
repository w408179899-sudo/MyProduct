using System.Globalization;
using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private static string? FormatBagCleanupUnitPrice(long? price) => price?.ToString("N0", CultureInfo.InvariantCulture);

    private DataGridView CreateBagCleanupTradeItemGrid(Control parent)
    {
        var grid = new DataGridView
        {
            Name = "bagCleanupTradeItemGrid",
            Location = new Point(0, 316),
            Size = new Size(408, 138),
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            Visible = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = _pageBackground;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = _textGreen;
        grid.DefaultCellStyle.ForeColor = _textGreen;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 241, 224);
        grid.DefaultCellStyle.SelectionForeColor = _textGreen;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName", HeaderText = "物品名称 / 关键字", ReadOnly = true,
            FillWeight = 64, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "UnitPrice", HeaderText = "单价（每个）", FillWeight = 36,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                NullValue = "未设置", Alignment = DataGridViewContentAlignment.MiddleRight
            }
        });
        grid.CellBeginEdit += (_, e) =>
        {
            if (bagCleanupNameListMutationInFlight || loadingBagCleanupNameListEditor) e.Cancel = true;
        };
        grid.EditingControlShowing += (_, e) =>
        {
            if (grid.CurrentCell?.ColumnIndex == 1 && grid.CurrentCell.Value is null && e.Control is TextBox textBox)
                textBox.Text = string.Empty;
        };
        grid.CellValidating += (_, e) =>
        {
            if (e.ColumnIndex != 1 || loadingBagCleanupNameListEditor || !grid.IsCurrentCellInEditMode) return;
            if (!BagCleanupTradeItemConfig.TryParseUnitPrice(Convert.ToString(e.FormattedValue), out var validatedPrice))
            {
                e.Cancel = true;
                grid.Rows[e.RowIndex].ErrorText = "单价必须是大于 0 的整数，留空表示未设置。";
                SetBagCleanupInventoryStatus("单价无效：请输入大于 0 的整数，或留空", true);
            }
            else grid.Rows[e.RowIndex].ErrorText = string.Empty;
        };
        grid.CellEndEdit += async (_, e) =>
        {
            if (e.ColumnIndex != 1 || loadingBagCleanupNameListEditor) return;
            var row = grid.Rows[e.RowIndex];
            if (row.Tag is not BagCleanupTradeItemConfig item) return;
            await SaveBagCleanupTradePriceAsync(item, Convert.ToString(row.Cells[1].Value)).ConfigureAwait(true);
        };
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            SetBagCleanupInventoryStatus("单价无效：请输入大于 0 的整数，或留空", true);
        };
        parent.Controls.Add(grid);
        return grid;
    }

    private void SelectBagCleanupTradeItem(string? name)
    {
        if (bagCleanupTradeItemGrid is null || string.IsNullOrWhiteSpace(name)) return;
        foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows)
        {
            if (!string.Equals(Convert.ToString(row.Cells[0].Value), name, StringComparison.OrdinalIgnoreCase)) continue;
            bagCleanupTradeItemGrid.ClearSelection();
            // Select the name cell so adding items does not immediately enter price editing.
            bagCleanupTradeItemGrid.CurrentCell = row.Cells[0];
            row.Selected = true;
            break;
        }
    }

    private Task SaveBagCleanupTradePriceAsync(BagCleanupTradeItemConfig item, string? text)
    {
        if (!BagCleanupTradeItemConfig.TryParseUnitPrice(text, out var price))
        {
            if (bagCleanupTradeItemGrid is not null)
            {
                foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows)
                    if (ReferenceEquals(row.Tag, item)) row.Cells[1].Value = FormatBagCleanupUnitPrice(item.UnitPrice);
            }
            SetBagCleanupInventoryStatus("单价无效：请输入大于 0 的整数，或留空", true);
            return Task.CompletedTask;
        }

        return RunBagCleanupNameListMutationAsync(async () =>
        {
            // The row owns the entry; selection changes must not redirect a pending edit.
            if (!bagCleanupStallItemNames.Contains(item) && !bagCleanupAuctionHouseItemNames.Contains(item)) return;
            if (item.UnitPrice != price)
            {
                var before = CaptureBagCleanupNameLists();
                item.UnitPrice = price;
                await SaveBagCleanupNameListsOrRollbackAsync(before,
                    price.HasValue ? "已自动保存单价：" + item.Name : "已清除单价：" + item.Name).ConfigureAwait(true);
            }

            if (bagCleanupTradeItemGrid is not null)
            {
                foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows)
                {
                    if (row.Tag is BagCleanupTradeItemConfig current)
                        row.Cells[1].Value = FormatBagCleanupUnitPrice(current.UnitPrice);
                }
            }
        });
    }
}
