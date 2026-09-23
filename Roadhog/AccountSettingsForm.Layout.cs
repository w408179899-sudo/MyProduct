namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private Action? layoutSkillPage;

    // Expand static form columns without scaling fonts, row heights or button heights.
    // Dynamic flow layouts retain their own layout rules.
    private void ConfigureWideSettingsPage(Panel page, int designWidth, bool expandLists = true)
    {
        var resizeColumns = CaptureSettingsColumns(page, designWidth);
        var lists = page.Controls.Cast<Control>().Where(c => expandLists && (c is ListBox or ListView)).ToArray();
        var listHeights = lists.ToDictionary(c => c, c => c.Height);
        var names = bagCleanupTradeItemGrid?.Parent;
        var bagNames = names?.Parent == page ? names : null;
        var bagListHeights = bagNames?.Controls.Cast<Control>()
            .Where(c => c is DataGridView || ReferenceEquals(c, bagCleanupExcludedItemListBox))
            .ToDictionary(c => c, c => c.Height);
        var resizing = false;
        void ResizePage()
        {
            if (resizing || page.IsDisposed) return;
            resizing = true;
            page.SuspendLayout();
            try
            {
                resizeColumns(Math.Max(designWidth, page.ClientSize.Width));
                foreach (var list in lists)
                    list.Height = Math.Max(listHeights[list], page.ClientSize.Height - list.Top - 16);
                if (bagNames is not null && bagListHeights is not null)
                {
                    bagNames.Height = Math.Max(460, page.ClientSize.Height - bagNames.Top - 16);
                    foreach (var (list, height) in bagListHeights)
                        list.Height = Math.Max(height, bagNames.Height - list.Top);
                }
            }
            finally { resizing = false; page.ResumeLayout(true); }
        }
        page.ClientSizeChanged += (_, _) => ResizePage();
        if (bagNames is not null) bagNames.LocationChanged += (_, _) => ResizePage();
        ResizePage();
    }

    private static Action<int> CaptureSettingsColumns(Control parent, int designWidth)
    {
        var entries = parent.Controls.Cast<Control>()
            .Where(c => c.Dock == DockStyle.None && c is not FlowLayoutPanel && c is not TabControl)
            .Select(c => (Control: c, Bounds: c.Bounds,
                Children: c.GetType() == typeof(Panel) ? CaptureSettingsColumns(c, Math.Max(1, c.Width)) : null))
            .ToArray();
        foreach (var entry in entries) entry.Control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        return width =>
        {
            var ratio = (double)width / designWidth;
            foreach (var (control, bounds, children) in entries)
            {
                var left = (int)Math.Round(bounds.Left * ratio);
                var right = (int)Math.Round(bounds.Right * ratio);
                if (control is Button)
                {
                    left += (right - left - bounds.Width) / 2;
                    right = left + bounds.Width;
                }
                control.SetBounds(left, control.Top, right - left, control.Height);
                children?.Invoke(control.Width);
            }
        };
    }

    private void ConfigurePathPageLayout(Panel page, ListView list, int footerTop, int designWidth)
    {
        ConfigureWideSettingsPage(page, designWidth, expandLists: false);
        list.BackColor = Color.White;
        var listHeight = list.Height;
        var footer = page.Controls.Cast<Control>().Where(c => c.Top >= footerTop)
            .Select(c => (Control: c, Top: c.Top)).ToArray();
        var designBottom = page.Controls.Cast<Control>().Max(c => c.Bottom);
        var resizing = false;
        void ResizePath()
        {
            if (resizing) return;
            resizing = true;
            page.SuspendLayout();
            try
            {
                var extra = Math.Max(0, page.ClientSize.Height - designBottom - 16);
                list.Height = listHeight + extra;
                foreach (var entry in footer) entry.Control.Top = entry.Top + extra;
            }
            finally { resizing = false; page.ResumeLayout(true); }
        }
        page.ClientSizeChanged += (_, _) => ResizePath();
        ResizePath();
    }

    private void ConfigureSkillPageLayout(Panel page, Panel options, Panel auto, Panel manual, Panel system)
    {
        var optionsLayout = CaptureSettingsColumns(options, 828);
        var manualLayout = CaptureSettingsColumns(manual, 828);
        var systemLayout = CaptureSettingsColumns(system, 828);
        var resizing = false;
        void ResizeSkills()
        {
            if (resizing || openingSkillRows?.Parent is not Panel opening || availableSkillTree is null || selectedSkillTree is null) return;
            resizing = true;
            page.SuspendLayout();
            try
            {
                // Use the full viewport to avoid keeping scrollbars created by an intermediate resize.
                var viewportWidth = page.ClientSize.Width + (page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
                var viewportHeight = page.ClientSize.Height + (page.HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);
                var minimumHeight = 136 + 38 + 292 + 16 + opening.Height + 14 + 16;
                page.AutoScroll = viewportWidth < 852 || viewportHeight < minimumHeight;
                if (!page.AutoScroll) auto.Top = 136;
                var width = Math.Max(828, viewportWidth - 24);
                options.Width = auto.Width = manual.Width = system.Width = width;
                optionsLayout(width); manualLayout(width); systemLayout(width);
                var treeWidth = (width - 196) / 2;
                var rightX = treeWidth + 100;
                var height = Math.Max(292, viewportHeight - 136 - 38 - 16 - opening.Height - 14 - 16);
                availableSkillTree.SetBounds(0, 38, treeWidth, height);
                selectedSkillTree.SetBounds(rightX, 38, treeWidth, height);
                foreach (var label in auto.Controls.OfType<Label>())
                    if (label.Text == "技能执行顺序") label.Left = rightX;
                var buttons = auto.Controls.OfType<Button>().ToArray();
                foreach (var button in buttons)
                {
                    switch (button.Text)
                    {
                        case "刷新当前技能": button.Left = treeWidth - button.Width; break;
                        case "刷新当前已选技能": button.Left = rightX + treeWidth - button.Width; break;
                        case "添加 >": button.Location = new(treeWidth + 12, 38 + (height - button.Height) / 2); break;
                    }
                }
                var order = new[] { "置顶", "上移", "下移", "置底", "移除", "清空" };
                for (var i = 0; i < order.Length; i++)
                    if (buttons.SingleOrDefault(b => b.Text == order[i]) is { } button)
                        button.Location = new(width - button.Width, 38 + (height - 230) / 2 + i * 40);
                opening.SetBounds(0, 38 + height + 16, width, opening.Height);
                opening.Controls["openingSkillAddButton"]!.Left = width - 112;
                openingSkillRows.Width = width - 24;
                foreach (Control row in openingSkillRows.Controls)
                {
                    row.Width = openingSkillRows.Width;
                    var extra = row.Width - 804;
                    row.Controls["openingSkillCombo"]!.Width = 408 + extra;
                    row.Controls["openingSkillKeyButton"]!.Left = 450 + extra;
                    row.Controls["openingSkillUp"]!.Left = 596 + extra;
                    row.Controls["openingSkillDown"]!.Left = 664 + extra;
                    row.Controls["openingSkillRemove"]!.Left = 732 + extra;
                }
                auto.Height = opening.Bottom + 14;
            }
            finally { resizing = false; page.ResumeLayout(true); }
        }
        layoutSkillPage = ResizeSkills;
        page.ClientSizeChanged += (_, _) => ResizeSkills();
        ResizeSkills();
    }
}
