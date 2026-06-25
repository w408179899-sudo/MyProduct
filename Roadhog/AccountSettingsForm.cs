namespace Roadhog
{
    public sealed class AccountSettingsForm : Form
    {
        private readonly string _account;
        private readonly Color _primaryGreen = Color.FromArgb(22, 163, 74);
        private readonly Color _darkGreen = Color.FromArgb(21, 128, 61);
        private readonly Color _headerGreen = Color.FromArgb(34, 139, 84);
        private readonly Color _softGreen = Color.FromArgb(240, 253, 244);
        private readonly Color _pageBackground = Color.FromArgb(247, 252, 249);
        private readonly Color _inputBackground = Color.FromArgb(229, 245, 235);
        private readonly Color _textGreen = Color.FromArgb(20, 83, 45);

        private TabControl settingsTabs = null!;

        public AccountSettingsForm(string account)
        {
            _account = account;
            InitializeSettingsForm();
        }

        private void InitializeSettingsForm()
        {
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(860, 620);
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(720, 420);
            Name = "AccountSettingsForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = $"账号设置 - {_account}";

            settingsTabs = new TabControl
            {
                Alignment = TabAlignment.Top,
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(74, 28),
                SizeMode = TabSizeMode.Fixed
            };

            settingsTabs.DrawItem += GreenTabs_DrawItem;
            settingsTabs.TabPages.Add(CreateSummaryTab());
            settingsTabs.TabPages.Add(CreatePathTab());
            settingsTabs.TabPages.Add(CreateMaintenanceTab());
            settingsTabs.TabPages.Add(CreateSkillTab());
            settingsTabs.TabPages.Add(CreateEmptyTab("测试"));

            Controls.Add(settingsTabs);
        }

        private TabPage CreateSummaryTab()
        {
            var tab = CreateBaseTab("总览");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "方案", 4, 8, 80, 22);
            AddTextBox(page, "default_profile", 4, 32, 220, 26);
            AddLabel(page, "方案名", 230, 36, 80, 22);

            AddCombo(page, 4, 72, 220, 28, "自定义打怪", "采集", "制作", "半自动");
            AddLabel(page, "主模式", 230, 76, 80, 22, Color.FromArgb(220, 38, 38), FontStyle.Bold);

            AddCombo(page, 4, 104, 220, 28, "原地打怪", "路径打怪");
            AddLabel(page, "打怪模式", 230, 108, 80, 22);

            AddCheckBox(page, "启用拾取", 4, 142, 88, true);
            AddCheckBox(page, "抢怪", 96, 142, 64, false);
            AddCheckBox(page, "反击敌对种族", 160, 142, 140, false);

            var combatAdvanced = CreateFoldout(page, "高级打怪设置", 176, 850, false);
            combatAdvanced.Content.Height = 58;

            return tab;
        }

        private TabPage CreatePathTab()
        {
            var tab = CreateBaseTab("路径");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "挂机路径选择:", 4, 8, 130, 22, _textGreen, FontStyle.Bold);
            AddLabel(page, "复活路径:  穆尔海姆00133（1点）", 24, 34, 320, 22);
            AddLabel(page, "打怪路径:  未选（0点）", 24, 60, 260, 22);

            var pathTabs = new TabControl
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(92, 28),
                Location = new Point(0, 88),
                Name = "pathTabs",
                SelectedIndex = 0,
                Size = new Size(850, 488),
                SizeMode = TabSizeMode.Fixed
            };

            pathTabs.DrawItem += GreenTabs_DrawItem;
            pathTabs.TabPages.Add(CreatePathEditorTab("复活路径", "死亡复活后返回主路径", true));
            pathTabs.TabPages.Add(CreatePathEditorTab("打怪路径", "打怪巡逻路径", false));
            pathTabs.TabPages.Add(CreatePathEditorTab("维护路径", "维护补给路径", false));
            page.Controls.Add(pathTabs);

            return tab;
        }

        private TabPage CreatePathEditorTab(string title, string caption, bool includeSamplePoint)
        {
            var tab = new TabPage
            {
                BackColor = _pageBackground,
                Padding = Padding.Empty,
                Text = title
            };

            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, caption, 4, 8, 220, 22, _textGreen, FontStyle.Bold);
            AddTextBox(page, "穆尔海姆00133", 4, 38, 242, 28);
            AddLabel(page, "路径名", 252, 42, 54, 22);
            AddCombo(page, "穆尔海姆00133（1点）", 306, 38, 254, 28);
            AddLabel(page, "已保存路径", 566, 42, 120, 22);

            AddButton(page, "保存到列表", 6, 74, 100, 30);
            AddButton(page, "删除保存", 114, 74, 92, 30);
            AddLabel(page, "点数  1  |  总距  0.0  |  无效  0", 6, 112, 300, 24, _textGreen, FontStyle.Bold);

            AddButton(page, "开始录制", 6, 144, 100, 30);
            AddButton(page, "停止录制", 116, 144, 100, 30);
            AddButton(page, "清空", 226, 144, 68, 30);
            AddButton(page, "复制路径", 304, 144, 88, 30);

            var pointsBox = new TextBox
            {
                BackColor = _inputBackground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(6, 184),
                Multiline = true,
                ReadOnly = false,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(562, 106),
                Text = includeSamplePoint ? "1307.758, 2844.230, 259.832" : string.Empty
            };
            page.Controls.Add(pointsBox);

            var pathAdvanced = CreateFoldout(page, "高级路径设置", 302, 850, true);
            pathAdvanced.Content.Height = 68;
            AddCheckBox(pathAdvanced.Content, "循环路径", 6, 12, 92, true);
            AddCheckBox(pathAdvanced.Content, "到终点反向", 102, 12, 106, false);
            AddCheckBox(pathAdvanced.Content, "死亡停止路径", 206, 12, 130, true);

            return tab;
        }

        private TabPage CreateMaintenanceTab()
        {
            var tab = CreateBaseTab("维护");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "坐地板维护", 4, 8, 82, 24, _textGreen, FontStyle.Bold);
            AddCheckBox(page, "启用", 84, 6, 70, true);

            AddLabel(page, "蓝量低于", 4, 44, 66, 24);
            AddTextBox(page, "10", 68, 42, 70, 28);
            AddLabel(page, "%  坐地板，恢复到", 144, 44, 130, 24);
            AddTextBox(page, "90", 272, 42, 70, 28);
            AddLabel(page, "%  起来继续打怪", 348, 44, 160, 24);

            AddLabel(page, "血量维护", 4, 82, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增血量维护", 68, 78, 120, 30);
            AddLabel(page, "暂无血量维护", 4, 116, 140, 24);

            AddLabel(page, "蓝量维护", 4, 154, 66, 24, _textGreen, FontStyle.Bold);
            AddButton(page, "新增蓝量维护", 68, 150, 120, 30);
            AddLabel(page, "暂无蓝量维护", 4, 188, 140, 24);

            var separator = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _softGreen,
                Location = new Point(0, 222),
                Size = new Size(850, 1)
            };
            page.Controls.Add(separator);

            AddCheckBox(page, "自动穿装备", 4, 230, 106, true);
            AddCheckBox(page, "自动分解装备", 112, 230, 126, true);

            var advanced = CreateFoldout(page, "高级设置", 266, 850, true);
            advanced.Content.Height = 88;
            AddNumberSetting(advanced.Content, "85", "清包阈值", 6, 12);
            AddNumberSetting(advanced.Content, "100", "背包总格数", 6, 46);

            return tab;
        }

        private TabPage CreateSkillTab()
        {
            var tab = CreateBaseTab("技能");
            var page = CreatePagePanel();
            tab.Controls.Add(page);

            AddLabel(page, "技能配置", 4, 16, 90, 24, _textGreen, FontStyle.Bold);
            AddLabel(page, "可用技能", 8, 54, 120, 24, _textGreen, FontStyle.Bold);
            AddLabel(page, "技能执行顺序", 378, 54, 140, 24, _textGreen, FontStyle.Bold);

            var availableTree = CreateSkillTree(page, "availableSkillTree", 8, 82, 260, 410);
            var selectedTree = CreateSkillTree(page, "selectedSkillTree", 378, 82, 300, 410);
            PopulateAvailableSkillTree(availableTree);
            PopulateSelectedSkillTree(selectedTree);

            AddButton(page, "刷新技能", 92, 12, 120, 30, (_, _) => PopulateAvailableSkillTree(availableTree));
            AddButton(page, "添加 >", 288, 150, 70, 30, (_, _) => AddSkillSelection(availableTree, selectedTree));
            AddButton(page, "< 移除", 288, 188, 70, 30, (_, _) => RemoveSelectedSkill(selectedTree));
            AddButton(page, "全部 >>", 288, 226, 70, 30, (_, _) => AddAllAvailableSkills(availableTree, selectedTree));
            AddButton(page, "清空", 288, 264, 70, 30, (_, _) => selectedTree.Nodes.Clear());

            AddButton(page, "置顶", 696, 150, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Top));
            AddButton(page, "上移", 696, 188, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Up));
            AddButton(page, "下移", 696, 226, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Down));
            AddButton(page, "置底", 696, 264, 70, 30, (_, _) => MoveSelectedSkill(selectedTree, SkillMove.Bottom));

            return tab;
        }

        private TabPage CreateEmptyTab(string title)
        {
            var tab = CreateBaseTab(title);
            tab.Controls.Add(CreatePagePanel());
            return tab;
        }

        private TabPage CreateBaseTab(string title)
        {
            return new TabPage
            {
                BackColor = _pageBackground,
                Padding = Padding.Empty,
                Text = title
            };
        }

        private Panel CreatePagePanel()
        {
            return new Panel
            {
                BackColor = _pageBackground,
                Dock = DockStyle.Fill
            };
        }

        private void AddLabel(Control parent, string text, int x, int y, int width, int height, Color? foreColor = null, FontStyle style = FontStyle.Regular)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, style),
                ForeColor = foreColor ?? _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }

        private void AddTextBox(Control parent, string text, int x, int y, int width, int height)
        {
            parent.Controls.Add(new TextBox
            {
                BackColor = _inputBackground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text
            });
        }

        private void AddCombo(Control parent, string value, int x, int y, int width, int height)
        {
            AddCombo(parent, x, y, width, height, value);
        }

        private void AddCombo(Control parent, int x, int y, int width, int height, params string[] values)
        {
            var combo = new ComboBox
            {
                BackColor = _inputBackground,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, height)
            };

            combo.Items.AddRange(values);
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            parent.Controls.Add(combo);
        }

        private Button AddButton(Control parent, string text, int x, int y, int width, int height, EventHandler? click = null)
        {
            var button = new Button
            {
                BackColor = _primaryGreen,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderColor = _darkGreen;
            if (click is not null)
            {
                button.Click += click;
            }

            parent.Controls.Add(button);
            return button;
        }

        private void AddNumberSetting(Control parent, string value, string label, int x, int y)
        {
            AddTextBox(parent, value, x, y, 56, 28);
            AddSmallButton(parent, "-", x + 62, y, 24, 28);
            AddSmallButton(parent, "+", x + 90, y, 24, 28);
            AddLabel(parent, label, x + 122, y + 2, 120, 24);
        }

        private void AddSmallButton(Control parent, string text, int x, int y, int width, int height)
        {
            var button = new Button
            {
                BackColor = _primaryGreen,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderColor = _darkGreen;
            parent.Controls.Add(button);
        }

        private void AddCheckBox(Control parent, string text, int x, int y, int width, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                AutoSize = false,
                BackColor = _pageBackground,
                Checked = isChecked,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Text = text,
                UseVisualStyleBackColor = false
            };

            checkBox.FlatAppearance.BorderColor = _darkGreen;
            checkBox.FlatAppearance.CheckedBackColor = _primaryGreen;
            parent.Controls.Add(checkBox);
        }

        private FoldoutSection CreateFoldout(Control parent, string title, int y, int width, bool expanded)
        {
            var header = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _softGreen,
                Cursor = Cursors.Hand,
                Location = new Point(0, y),
                Size = new Size(width, 28)
            };

            var label = new Label
            {
                AutoSize = false,
                Cursor = Cursors.Hand,
                Dock = DockStyle.Left,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = _textGreen,
                Padding = new Padding(10, 0, 0, 0),
                Text = $"{(expanded ? "▼" : "▶")}  {title}",
                TextAlign = ContentAlignment.MiddleLeft,
                Width = 220
            };

            var content = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = _pageBackground,
                Location = new Point(0, y + 28),
                Size = new Size(width, 0),
                Visible = expanded
            };

            void ToggleFoldout()
            {
                expanded = !expanded;
                content.Visible = expanded;
                label.Text = $"{(expanded ? "▼" : "▶")}  {title}";
            }

            header.Click += (_, _) => ToggleFoldout();
            label.Click += (_, _) => ToggleFoldout();

            header.Controls.Add(label);
            parent.Controls.Add(header);
            parent.Controls.Add(content);

            return new FoldoutSection(content);
        }

        private sealed record FoldoutSection(Panel Content);

        private TreeView CreateSkillTree(Control parent, string name, int x, int y, int width, int height)
        {
            var tree = new TreeView
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = _textGreen,
                FullRowSelect = true,
                HideSelection = false,
                Location = new Point(x, y),
                Name = name,
                PathSeparator = " / ",
                ShowLines = true,
                Size = new Size(width, height)
            };

            parent.Controls.Add(tree);
            return tree;
        }

        private void PopulateAvailableSkillTree(TreeView tree)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();

            var attack = tree.Nodes.Add("attack", "主动技能");
            attack.Nodes.Add("normal_attack", "普通攻击");
            attack.Nodes.Add("main_attack", "主输出技能");
            attack.Nodes.Add("aoe_attack", "范围技能");
            attack.Nodes.Add("finish_attack", "终结技能");

            var buff = tree.Nodes.Add("buff", "增益技能");
            buff.Nodes.Add("self_buff", "自身增益");
            buff.Nodes.Add("combat_buff", "战斗增益");
            buff.Nodes.Add("speed_buff", "移动增益");

            var recover = tree.Nodes.Add("recover", "恢复技能");
            recover.Nodes.Add("hp_recover", "生命恢复");
            recover.Nodes.Add("mp_recover", "魔法恢复");

            tree.ExpandAll();
            tree.EndUpdate();
        }

        private void PopulateSelectedSkillTree(TreeView tree)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            tree.Nodes.Add("普通攻击");
            tree.Nodes.Add("主输出技能");
            tree.Nodes.Add("自身增益");
            tree.EndUpdate();
        }

        private void AddSkillSelection(TreeView source, TreeView target)
        {
            if (source.SelectedNode is null)
            {
                return;
            }

            foreach (var text in GetLeafTexts(source.SelectedNode))
            {
                AddSkillIfMissing(target, text);
            }
        }

        private void AddAllAvailableSkills(TreeView source, TreeView target)
        {
            foreach (TreeNode node in source.Nodes)
            {
                foreach (var text in GetLeafTexts(node))
                {
                    AddSkillIfMissing(target, text);
                }
            }
        }

        private void AddSkillIfMissing(TreeView target, string text)
        {
            foreach (TreeNode node in target.Nodes)
            {
                if (node.Text == text)
                {
                    target.SelectedNode = node;
                    return;
                }
            }

            target.SelectedNode = target.Nodes.Add(text);
        }

        private static IEnumerable<string> GetLeafTexts(TreeNode node)
        {
            if (node.Nodes.Count == 0)
            {
                yield return node.Text;
                yield break;
            }

            foreach (TreeNode child in node.Nodes)
            {
                foreach (var text in GetLeafTexts(child))
                {
                    yield return text;
                }
            }
        }

        private static void RemoveSelectedSkill(TreeView tree)
        {
            tree.SelectedNode?.Remove();
        }

        private static void MoveSelectedSkill(TreeView tree, SkillMove move)
        {
            var node = tree.SelectedNode;
            if (node is null)
            {
                return;
            }

            var collection = node.Parent?.Nodes ?? tree.Nodes;
            var currentIndex = node.Index;
            var targetIndex = move switch
            {
                SkillMove.Top => 0,
                SkillMove.Up => Math.Max(0, currentIndex - 1),
                SkillMove.Down => Math.Min(collection.Count - 1, currentIndex + 1),
                SkillMove.Bottom => collection.Count - 1,
                _ => currentIndex
            };

            if (targetIndex == currentIndex)
            {
                return;
            }

            var moved = (TreeNode)node.Clone();
            collection.RemoveAt(currentIndex);
            collection.Insert(targetIndex, moved);
            tree.SelectedNode = moved;
        }

        private enum SkillMove
        {
            Top,
            Up,
            Down,
            Bottom
        }

        private void GreenTabs_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabControl)
            {
                return;
            }

            var tabPage = tabControl.TabPages[e.Index];
            var selected = e.Index == tabControl.SelectedIndex;
            var bounds = e.Bounds;

            using var background = new SolidBrush(selected ? _primaryGreen : _softGreen);
            using var foreground = new SolidBrush(selected ? Color.White : _textGreen);
            using var border = new Pen(_darkGreen);
            using var font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);

            e.Graphics.FillRectangle(background, bounds);
            e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

            TextRenderer.DrawText(
                e.Graphics,
                tabPage.Text,
                font,
                bounds,
                selected ? Color.White : _textGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
