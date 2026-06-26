using System.Drawing.Drawing2D;
using System.Diagnostics.CodeAnalysis;

namespace Roadhog
{
    internal static class UiChrome
    {
        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1F, radius * 2F);
            var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        public static Color Blend(Color from, Color to, int amount)
        {
            amount = Math.Clamp(amount, 0, 255);
            var inverse = 255 - amount;
            return Color.FromArgb(
                (from.R * inverse + to.R * amount) / 255,
                (from.G * inverse + to.G * amount) / 255,
                (from.B * inverse + to.B * amount) / 255);
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
        }

        public int CornerRadius { get; set; } = 8;

        public int ShadowDepth { get; set; } = 2;

        public Color BorderColor { get; set; } = Color.FromArgb(21, 128, 61);

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            if (Width < 8 || Height < 8)
            {
                return;
            }

            var shadowDepth = _pressed ? 1 : ShadowDepth;
            var offset = _pressed ? 1 : 0;
            var buttonBounds = new RectangleF(1, offset, Width - 3, Height - shadowDepth - offset - 1);
            var shadowBounds = new RectangleF(2, shadowDepth + 1, Width - 4, Height - shadowDepth - 1);
            var baseColor = Enabled ? BackColor : Color.FromArgb(156, 163, 175);
            var topColor = _pressed
                ? ControlPaint.Dark(baseColor, 0.10F)
                : _hovered
                    ? ControlPaint.Light(baseColor, 0.22F)
                    : ControlPaint.Light(baseColor, 0.12F);
            var bottomColor = _pressed
                ? ControlPaint.Dark(baseColor, 0.28F)
                : _hovered
                    ? ControlPaint.Light(baseColor, 0.04F)
                    : ControlPaint.Dark(baseColor, 0.08F);

            using (var shadowPath = UiChrome.RoundedRect(shadowBounds, CornerRadius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(_pressed ? 32 : 58, 15, 23, 42)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (var path = UiChrome.RoundedRect(buttonBounds, CornerRadius))
            using (var fill = new LinearGradientBrush(buttonBounds, topColor, bottomColor, LinearGradientMode.Vertical))
            using (var border = new Pen(_hovered ? ControlPaint.Light(BorderColor, 0.18F) : BorderColor))
            using (var highlight = new Pen(Color.FromArgb(90, Color.White)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);

                var highlightBounds = buttonBounds;
                highlightBounds.Inflate(-1, -1);
                using var highlightPath = UiChrome.RoundedRect(highlightBounds, Math.Max(2, CornerRadius - 1));
                e.Graphics.DrawPath(highlight, highlightPath);
            }

            var textBounds = Rectangle.Round(buttonBounds);
            if (_pressed)
            {
                textBounds.Offset(0, 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                Enabled ? ForeColor : Color.FromArgb(229, 231, 235),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public int CornerRadius { get; set; } = 12;

        public int ShadowDepth { get; set; } = 2;

        public Color BorderColor { get; set; } = Color.FromArgb(187, 247, 208);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            if (Width < 8 || Height < 8)
            {
                return;
            }

            var panelBounds = new RectangleF(1, 1, Width - 3, Height - ShadowDepth - 2);
            var shadowBounds = new RectangleF(2, ShadowDepth + 1, Width - 4, Height - ShadowDepth - 2);

            using (var shadowPath = UiChrome.RoundedRect(shadowBounds, CornerRadius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(38, 15, 23, 42)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (var path = UiChrome.RoundedRect(panelBounds, CornerRadius))
            using (var fill = new SolidBrush(BackColor))
            using (var border = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class RoundedTextBox : UserControl
    {
        private readonly TextBox _innerTextBox = new();

        public RoundedTextBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            BackColor = Color.FromArgb(229, 245, 235);
            BorderColor = Color.FromArgb(134, 239, 172);
            ForeColor = Color.FromArgb(20, 83, 45);
            _innerTextBox.BorderStyle = BorderStyle.None;
            _innerTextBox.BackColor = BackColor;
            _innerTextBox.ForeColor = ForeColor;
            _innerTextBox.Location = new Point(8, 6);
            _innerTextBox.TextChanged += (_, _) => base.Text = _innerTextBox.Text;
            Controls.Add(_innerTextBox);
        }

        public int CornerRadius { get; set; } = 8;

        public Color BorderColor { get; set; }

        [AllowNull]
        public override string Text
        {
            get => _innerTextBox.Text;
            set
            {
                _innerTextBox.Text = value ?? string.Empty;
                base.Text = value ?? string.Empty;
            }
        }

        public bool Multiline
        {
            get => _innerTextBox.Multiline;
            set
            {
                _innerTextBox.Multiline = value;
                UpdateInnerBounds();
            }
        }

        public bool ReadOnly
        {
            get => _innerTextBox.ReadOnly;
            set => _innerTextBox.ReadOnly = value;
        }

        public ScrollBars ScrollBars
        {
            get => _innerTextBox.ScrollBars;
            set => _innerTextBox.ScrollBars = value;
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            _innerTextBox.BackColor = BackColor;
            base.OnBackColorChanged(e);
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            _innerTextBox.ForeColor = ForeColor;
            base.OnForeColorChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _innerTextBox.Font = Font;
            UpdateInnerBounds();
            base.OnFontChanged(e);
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateInnerBounds();
            Invalidate();
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            var shadowBounds = new RectangleF(2, 3, Width - 4, Height - 4);
            var boxBounds = new RectangleF(1, 1, Width - 3, Height - 4);

            using (var shadowPath = UiChrome.RoundedRect(shadowBounds, CornerRadius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(24, 15, 23, 42)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using var path = UiChrome.RoundedRect(boxBounds, CornerRadius);
            using var fill = new LinearGradientBrush(boxBounds, Color.White, BackColor, LinearGradientMode.Vertical);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private void UpdateInnerBounds()
        {
            _innerTextBox.Bounds = Multiline
                ? new Rectangle(8, 7, Math.Max(1, Width - 16), Math.Max(1, Height - 14))
                : new Rectangle(8, Math.Max(4, (Height - _innerTextBox.PreferredHeight) / 2), Math.Max(1, Width - 16), _innerTextBox.PreferredHeight);
        }
    }

    internal sealed class RoundedComboBox : UserControl
    {
        private readonly ComboBox _comboBox = new();

        public RoundedComboBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            BackColor = Color.FromArgb(229, 245, 235);
            BorderColor = Color.FromArgb(134, 239, 172);
            ForeColor = Color.FromArgb(20, 83, 45);
            _comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _comboBox.FlatStyle = FlatStyle.Flat;
            _comboBox.BackColor = BackColor;
            _comboBox.ForeColor = ForeColor;
            _comboBox.SelectedIndexChanged += (_, _) =>
            {
                base.Text = _comboBox.Text;
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            };
            Controls.Add(_comboBox);
        }

        public int CornerRadius { get; set; } = 8;

        public Color BorderColor { get; set; }

        public event EventHandler? SelectedIndexChanged;

        public ComboBox.ObjectCollection Items => _comboBox.Items;

        public int SelectedIndex
        {
            get => _comboBox.SelectedIndex;
            set => _comboBox.SelectedIndex = value;
        }

        [AllowNull]
        public override string Text
        {
            get => _comboBox.Text;
            set
            {
                _comboBox.Text = value ?? string.Empty;
                base.Text = value ?? string.Empty;
            }
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            _comboBox.BackColor = BackColor;
            base.OnBackColorChanged(e);
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            _comboBox.ForeColor = ForeColor;
            base.OnForeColorChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _comboBox.Font = Font;
            UpdateInnerBounds();
            base.OnFontChanged(e);
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateInnerBounds();
            Invalidate();
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            var shadowBounds = new RectangleF(2, 3, Width - 4, Height - 4);
            var boxBounds = new RectangleF(1, 1, Width - 3, Height - 4);

            using (var shadowPath = UiChrome.RoundedRect(shadowBounds, CornerRadius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(24, 15, 23, 42)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using var path = UiChrome.RoundedRect(boxBounds, CornerRadius);
            using var fill = new LinearGradientBrush(boxBounds, Color.White, BackColor, LinearGradientMode.Vertical);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private void UpdateInnerBounds()
        {
            _comboBox.Bounds = new Rectangle(6, Math.Max(3, (Height - _comboBox.PreferredHeight) / 2), Math.Max(1, Width - 12), _comboBox.PreferredHeight);
        }
    }

    internal sealed class RoundedCheckBox : Control
    {
        private bool _checked;
        private bool _hovered;

        public RoundedCheckBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            Cursor = Cursors.Hand;
            ForeColor = Color.FromArgb(20, 83, 45);
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                _checked = value;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            var boxBounds = new RectangleF(1, Math.Max(1, (Height - 18) / 2), 18, 18);
            using (var shadowPath = UiChrome.RoundedRect(new RectangleF(2, boxBounds.Y + 2, 17, 17), 5))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(20, 15, 23, 42)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (var path = UiChrome.RoundedRect(boxBounds, 5))
            using (var fill = new LinearGradientBrush(boxBounds, Checked ? Color.FromArgb(74, 222, 128) : Color.White, Checked ? Color.FromArgb(22, 163, 74) : Color.FromArgb(229, 245, 235), LinearGradientMode.Vertical))
            using (var border = new Pen(_hovered ? Color.FromArgb(22, 163, 74) : Color.FromArgb(21, 128, 61)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            if (Checked)
            {
                using var checkPen = new Pen(Color.White, 2.2F)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawLines(checkPen, new[]
                {
                    new PointF(5, boxBounds.Y + 9),
                    new PointF(9, boxBounds.Y + 13),
                    new PointF(15, boxBounds.Y + 5)
                });
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(24, 0, Math.Max(1, Width - 24), Height),
                ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
