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
        var lookup = new DataGridViewComboBoxColumn
        {
            Name = "PriceLookupMethod", HeaderText = "查价方式", FillWeight = 36,
            DisplayIndex = 1, Visible = false, FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        lookup.Items.AddRange("手动单价", "弹窗最低价", "搜索后计算");
        grid.Columns.Add(lookup);
        lookup.DisplayIndex = 1;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "AuctionDiscount", HeaderText = "折扣", FillWeight = 27,
            ToolTipText = "单位：折。8.01～9.99，保留两位小数；留空不打折。",
            Visible = false, SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = "不打折", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell?.OwningColumn == lookup)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != lookup.Index || loadingBagCleanupNameListEditor || bagCleanupNameListMutationInFlight) return;
            if (grid.Rows[e.RowIndex].Tag is BagCleanupTradeItemConfig item)
                await SaveAuctionLookupMethodAsync(item, Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)).ConfigureAwait(true);
        };
        grid.CellBeginEdit += (_, e) =>
        {
            if (bagCleanupNameListMutationInFlight || loadingBagCleanupNameListEditor) e.Cancel = true;
        };
        grid.EditingControlShowing += (_, e) =>
        {
            if (grid.CurrentCell?.OwningColumn.Name is "UnitPrice" or "AuctionDiscount" && grid.CurrentCell.Value is null && e.Control is TextBox textBox)
                textBox.Text = string.Empty;
        };
        grid.CellValidating += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["AuctionDiscount"].Index &&
                !grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly && !loadingBagCleanupNameListEditor && grid.IsCurrentCellInEditMode)
            {
                e.Cancel = !BagCleanupTradeItemConfig.TryParseDiscount(Convert.ToString(e.FormattedValue), out var validatedDiscount);
                grid.Rows[e.RowIndex].ErrorText = e.Cancel ? "折扣为 8.01～9.99，最多两位小数；留空不打折。" : string.Empty;
                if (e.Cancel) SetBagCleanupInventoryStatus(grid.Rows[e.RowIndex].ErrorText, true);
                return;
            }
            if (e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Cells[1].ReadOnly || loadingBagCleanupNameListEditor || !grid.IsCurrentCellInEditMode) return;
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
            if (e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["AuctionDiscount"].Index && !loadingBagCleanupNameListEditor)
            {
                if (grid.Rows[e.RowIndex].Tag is BagCleanupTradeItemConfig discountItem)
                    await SaveAuctionDiscountAsync(discountItem, Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)).ConfigureAwait(true);
                return;
            }
            if (e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Cells[1].ReadOnly || loadingBagCleanupNameListEditor) return;
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

    private static string FormatAuctionLookupMethod(AuctionPriceLookupMethod method) => method switch
    {
        AuctionPriceLookupMethod.DialogMinimum => "弹窗最低价",
        AuctionPriceLookupMethod.SearchCalculation => "搜索后计算",
        _ => "手动单价"
    };

    private bool UsesManualTradePrice(BagCleanupTradeItemConfig item) =>
        !bagCleanupAuctionHouseItemNames.Contains(item) || item.PriceLookupMethod == AuctionPriceLookupMethod.Manual;

    private void RefreshTradePriceCell(DataGridViewRow row)
    {
        if (row.Tag is not BagCleanupTradeItemConfig item) return;
        var manual = UsesManualTradePrice(item);
        var cell = row.Cells[1];
        cell.ReadOnly = !manual;
        cell.Value = manual ? FormatBagCleanupUnitPrice(item.UnitPrice) : null;
        cell.Style.NullValue = manual ? "未设置" : "—";
        cell.Style.BackColor = manual ? Color.White : SystemColors.Control;
        cell.Style.ForeColor = manual ? _textGreen : SystemColors.GrayText;
        cell.Style.SelectionForeColor = manual ? _textGreen : SystemColors.GrayText;
        cell.ToolTipText = manual ? "手动设置每个物品的单价" : "自动查价，此处手动价格不参与处理";
        var discountCell = row.Cells["AuctionDiscount"];
        var editableDiscount = bagCleanupAuctionHouseItemNames.Contains(item) && item.PriceLookupMethod == AuctionPriceLookupMethod.DialogMinimum;
        discountCell.ReadOnly = !editableDiscount;
        discountCell.Value = editableDiscount ? item.AuctionDiscount?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : null;
        discountCell.Style.NullValue = editableDiscount ? "不打折" : "—";
        discountCell.Style.BackColor = editableDiscount ? Color.White : SystemColors.Control;
        discountCell.ToolTipText = "8.01～9.99 折，留空不打折。折扣价不低于系统单价 ÷ 20；切换查价方式保留折扣。";
        row.ErrorText = string.Empty;
    }

    private Task SaveAuctionLookupMethodAsync(BagCleanupTradeItemConfig item, string? text) => RunBagCleanupNameListMutationAsync(async () =>
    {
        if (!bagCleanupAuctionHouseItemNames.Contains(item)) return;
        var method = text switch { "弹窗最低价" => AuctionPriceLookupMethod.DialogMinimum, "搜索后计算" => AuctionPriceLookupMethod.SearchCalculation, _ => AuctionPriceLookupMethod.Manual };
        if (item.PriceLookupMethod == method) return;
        var before = CaptureBagCleanupNameLists();
        item.PriceLookupMethod = method;
        if (bagCleanupTradeItemGrid is not null)
            foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows) RefreshTradePriceCell(row);
        await SaveBagCleanupNameListsOrRollbackAsync(before, "已自动保存查价方式：" + item.Name).ConfigureAwait(true);
    });

    private Task SaveAuctionDiscountAsync(BagCleanupTradeItemConfig item, string? text)
    {
        if (!bagCleanupAuctionHouseItemNames.Contains(item) || item.PriceLookupMethod != AuctionPriceLookupMethod.DialogMinimum)
            return Task.CompletedTask;
        if (!BagCleanupTradeItemConfig.TryParseDiscount(text, out var discount))
        {
            SetBagCleanupInventoryStatus("折扣无效：请输入 8.01～9.99，最多两位小数，或留空不打折。", true);
            if (bagCleanupTradeItemGrid is not null)
                foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows) RefreshTradePriceCell(row);
            return Task.CompletedTask;
        }
        return RunBagCleanupNameListMutationAsync(async () =>
        {
            if (!bagCleanupAuctionHouseItemNames.Contains(item) || item.PriceLookupMethod != AuctionPriceLookupMethod.DialogMinimum) return;
            if (item.AuctionDiscount != discount)
            {
                var before = CaptureBagCleanupNameLists();
                item.AuctionDiscount = discount;
                await SaveBagCleanupNameListsOrRollbackAsync(before, "已自动保存拍卖折扣：" + item.Name).ConfigureAwait(true);
            }
            if (bagCleanupTradeItemGrid is not null)
                foreach (DataGridViewRow row in bagCleanupTradeItemGrid.Rows) RefreshTradePriceCell(row);
        });
    }

    private Task SaveBagCleanupTradePriceAsync(BagCleanupTradeItemConfig item, string? text)
    {
        if (!UsesManualTradePrice(item)) return Task.CompletedTask;
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
            if ((!bagCleanupStallItemNames.Contains(item) && !bagCleanupAuctionHouseItemNames.Contains(item)) || !UsesManualTradePrice(item)) return;
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
                    RefreshTradePriceCell(row);
                }
            }
        });
    }
}
