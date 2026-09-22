namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private Form CreateSpiritmasterSettingsDialog()
    {
        var dialog = new Form
        {
            AutoScaleDimensions = new SizeF(7F, 17F), AutoScaleMode = AutoScaleMode.Font,
            BackColor = _pageBackground, ClientSize = new Size(880, 690), MinimumSize = new Size(740, 520),
            Font = new Font("Microsoft YaHei UI", 9F), Name = "SpiritmasterSettingsForm",
            ShowIcon = false, StartPosition = FormStartPosition.CenterParent, Text = "精灵设置 - " + _account
        };
        spiritmasterRuleLists.Clear();
        spiritmasterOpeningAttackKeyButton = null;
        spiritmasterOpeningSkillCombo = null;

        var header = new Panel { Dock = DockStyle.Top, Size = new Size(880, 66), BackColor = _pageBackground };
        AddLabel(header, "精灵专用设置", 20, 10, 170, 26, _textGreen, FontStyle.Bold);
        AddLabel(header, "技能自动匹配主栏 / Alt 栏，指令按键可手动设置", 20, 36, 540, 22, Color.FromArgb(92, 112, 101));
        var refresh = AddSpiritmasterSecondaryButton(header, "刷新技能", 742, 17, 116);
        refresh.Name = "spiritmasterRefreshSkillsButton";
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += async (_, _) => await RefreshCurrentSkillsAsync(refresh, availableSkillTree, systemSkillTree).ConfigureAwait(true);

        var footer = new Panel { Dock = DockStyle.Bottom, Size = new Size(880, 56), BackColor = Color.White };
        AddLabel(footer, "保存后，下次启动脚本生效", 20, 15, 430, 26, Color.FromArgb(92, 112, 101));
        var save = AddButton(footer, "保存配置", 620, 12, 126, 32, SaveSettingsButton_Click);
        save.Name = "spiritmasterSaveButton";
        save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var close = AddSpiritmasterSecondaryButton(footer, "关闭", 758, 12, 100);
        close.Name = "spiritmasterCloseButton";
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += (_, _) => dialog.Close();
        dialog.AcceptButton = save;
        dialog.CancelButton = close;
        dialog.Shown += (_, _) => refresh.Focus();

        var page = new FlowLayoutPanel
        {
            Name = "spiritmasterSections", Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = _pageBackground, Padding = new Padding(16, 4, 16, 8)
        };
        dialog.Controls.Add(page);
        dialog.Controls.Add(footer);
        dialog.Controls.Add(header);
        spiritmasterSummonRuleList = CreateSpiritmasterCard(page, "summon", "召唤与指令", "选择技能自动匹配；留空使用手动按键");
        spiritmasterDotRuleList = CreateSpiritmasterCard(page, "dot", "持续伤害 · DOT", "自动判断目标状态，避免重复补技能", "添加技能", list => AddSpiritmasterDotRuleRow(list));
        spiritmasterPetHpRuleList = CreateSpiritmasterCard(page, "hp", "宝宝血量维护", "血量低于设定值时使用技能", "添加规则", list => AddSpiritmasterPetHpRuleRow(list));
        spiritmasterPetBuffRuleList = CreateSpiritmasterCard(page, "buff", "宝宝增益 · Buff", "缺少增益时自动补充", "添加技能", list => AddSpiritmasterPetBuffRuleRow(list));
        PopulateSpiritmasterRuleLists(currentSpiritmasterSettings);

        void ResizeCards()
        {
            var width = Math.Max(640, page.ClientSize.Width - page.Padding.Horizontal - 2);
            foreach (Control card in page.Controls)
                if (card.Width != width) card.Width = width;
        }
        page.ClientSizeChanged += (_, _) => ResizeCards();
        page.Layout += (_, _) => ResizeCards();
        ResizeCards();
        return dialog;
    }

    private FlowLayoutPanel CreateSpiritmasterCard(FlowLayoutPanel page, string name, string title, string description,
        string? addText = null, Action<FlowLayoutPanel>? addRow = null)
    {
        var card = new RoundedPanel
        {
            Name = "spiritmasterCard_" + name, Size = new Size(828, 100),
            BackColor = Color.White, BorderColor = Color.FromArgb(208, 229, 216),
            CornerRadius = 10, ShadowDepth = 0, Margin = new Padding(0, 0, 0, 10)
        };
        page.Controls.Add(card);
        AddLabel(card, title, 16, 10, 150, 26, _textGreen, FontStyle.Bold);
        var hint = AddLabel(card, description, 174, 10, addRow is null ? 630 : 506, 26, Color.FromArgb(103, 120, 109));
        hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        hint.AutoEllipsis = true;
        var list = new FlowLayoutPanel
        {
            Name = "spiritmasterList_" + name, Location = new Point(16, 46), Size = new Size(796, 36),
            BackColor = Color.White, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty
        };
        card.Controls.Add(list);
        spiritmasterRuleLists.Add(list);
        var empty = AddLabel(card, "暂无规则，点击右上角添加", 18, 48, 460, 30, Color.FromArgb(125, 139, 130));
        empty.Name = "spiritmasterEmptyLabel";
        if (addRow is not null)
        {
            var add = AddSpiritmasterSecondaryButton(card, "+ " + addText, 704, 8, 108);
            add.Name = "spiritmasterAdd_" + name;
            add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            add.Click += (_, _) =>
            {
                addRow(list);
                var row = list.Controls.OfType<Panel>().Last();
                page.ScrollControlIntoView(row);
                row.Controls.OfType<RoundedComboBox>().FirstOrDefault()?.Focus();
            };
        }
        void ResizeRows()
        {
            foreach (Control row in list.Controls)
                if (row.Width != list.ClientSize.Width) row.Width = list.ClientSize.Width;
            var height = Math.Max(36, list.Controls.Cast<Control>().Sum(row => row.Height + row.Margin.Vertical));
            if (list.Height != height) list.Height = height;
            if (card.Height != list.Bottom + 12) card.Height = list.Bottom + 12;
            empty.Visible = list.Controls.Count == 0;
        }
        list.ControlAdded += (_, _) => ResizeRows();
        list.ControlRemoved += (_, _) => ResizeRows();
        list.ClientSizeChanged += (_, _) => ResizeRows();
        ResizeRows();
        return list;
    }

    private Button AddSpiritmasterSecondaryButton(Control parent, string text, int x, int y, int width, bool removal = false)
    {
        var button = new Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(width, 30), FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, ForeColor = removal ? Color.FromArgb(159, 69, 62) : _textGreen,
            Font = new Font("Microsoft YaHei UI", 9F), Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = removal ? Color.FromArgb(236, 218, 215) : Color.FromArgb(198, 221, 206);
        button.FlatAppearance.MouseOverBackColor = removal ? Color.FromArgb(254, 242, 240) : _softGreen;
        parent.Controls.Add(button);
        return button;
    }

    private void ShowSpiritmasterAutomaticKeyAsLabel(Button button)
    {
        var badge = AddLabel(button.Parent!, "", button.Left, button.Top, button.Width, button.Height);
        badge.Name = "spiritmasterAutomaticKeyLabel";
        badge.Anchor = button.Anchor;
        badge.TextAlign = ContentAlignment.MiddleCenter;
        badge.AutoEllipsis = true;
        void Refresh()
        {
            badge.Visible = !button.Enabled;
            button.Visible = button.Enabled;
            badge.Text = button.Text.Replace("自动: ", "自动 · ", StringComparison.Ordinal);
            var missing = button.Text.Contains("未放入", StringComparison.Ordinal);
            badge.BackColor = missing ? Color.FromArgb(255, 246, 224) : _inputBackground;
            badge.ForeColor = missing ? Color.FromArgb(153, 103, 25) : _textGreen;
        }
        button.EnabledChanged += (_, _) => Refresh();
        button.TextChanged += (_, _) => Refresh();
        Refresh();
    }
}
