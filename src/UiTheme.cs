using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteLauncher
{
    /// <summary>
    /// 统一视觉样式：配色、字体与圆角控件。
    /// 配色支持浅色 / 深色两套，切换后由 MainForm 重建界面以应用新色值。
    /// </summary>
    internal static class UiTheme
    {
        private static bool _dark;

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static void SetDark(bool dark)
        {
            _dark = dark;
        }

        // ---------- light ----------
        private static readonly Color LightBackground = Color.FromArgb(242, 245, 249);
        private static readonly Color LightSidebar = Color.FromArgb(252, 253, 255);
        private static readonly Color LightCard = Color.White;
        private static readonly Color LightBorder = Color.FromArgb(226, 230, 236);
        private static readonly Color LightAccent = Color.FromArgb(47, 111, 235);
        private static readonly Color LightAccentDark = Color.FromArgb(35, 88, 200);
        private static readonly Color LightAccentSoft = Color.FromArgb(233, 241, 255);
        private static readonly Color LightTextPrimary = Color.FromArgb(28, 33, 43);
        private static readonly Color LightTextSecondary = Color.FromArgb(108, 117, 130);
        private static readonly Color LightTextMuted = Color.FromArgb(150, 158, 170);

        // ---------- dark ----------
        private static readonly Color DarkBackground = Color.FromArgb(22, 24, 30);
        private static readonly Color DarkSidebar = Color.FromArgb(30, 33, 41);
        private static readonly Color DarkCard = Color.FromArgb(38, 42, 52);
        private static readonly Color DarkBorder = Color.FromArgb(56, 62, 76);
        private static readonly Color DarkAccent = Color.FromArgb(96, 148, 255);
        private static readonly Color DarkAccentDark = Color.FromArgb(70, 118, 220);
        private static readonly Color DarkAccentSoft = Color.FromArgb(44, 56, 80);
        private static readonly Color DarkTextPrimary = Color.FromArgb(233, 237, 245);
        private static readonly Color DarkTextSecondary = Color.FromArgb(158, 168, 184);
        private static readonly Color DarkTextMuted = Color.FromArgb(120, 130, 146);

        public static Color Background { get { return _dark ? DarkBackground : LightBackground; } }
        public static Color SidebarBackground { get { return _dark ? DarkSidebar : LightSidebar; } }
        public static Color CardBackground { get { return _dark ? DarkCard : LightCard; } }
        public static Color Border { get { return _dark ? DarkBorder : LightBorder; } }
        public static Color Accent { get { return _accentOverride ?? (_dark ? DarkAccent : LightAccent); } }
        public static Color AccentDark { get { return _accentDarkOverride ?? (_dark ? DarkAccentDark : LightAccentDark); } }
        public static Color AccentSoft { get { return _accentSoftOverride ?? (_dark ? DarkAccentSoft : LightAccentSoft); } }
        public static Color TextPrimary { get { return _dark ? DarkTextPrimary : LightTextPrimary; } }
        public static Color TextSecondary { get { return _dark ? DarkTextSecondary : LightTextSecondary; } }
        public static Color TextMuted { get { return _dark ? DarkTextMuted : LightTextMuted; } }

        public static Color Success { get { return _dark ? Color.FromArgb(96, 208, 148) : Color.FromArgb(22, 140, 84); } }
        public static Color Warning { get { return _dark ? Color.FromArgb(240, 180, 90) : Color.FromArgb(176, 104, 10); } }
        public static Color Danger { get { return _dark ? Color.FromArgb(240, 122, 122) : Color.FromArgb(188, 42, 42); } }

        /// <summary>1.7.7：卡片投影色，浅色下极淡、深色下近乎不可见，避免"脏边"。</summary>
        public static Color CardShadow
        {
            get { return _dark ? Color.FromArgb(50, 0, 0, 0) : Color.FromArgb(22, 30, 45, 70); }
        }

        public const string FontFamily = "Microsoft YaHei UI";

        // ---- 1.5.0：强调色覆写与字体缩放（构建 UI 前由 MainForm 应用）----

        private static Color? _accentOverride;
        private static Color? _accentDarkOverride;
        private static Color? _accentSoftOverride;
        private static double _fontScale = 1.0;

        /// <summary>
        /// 应用强调色主题。key 为空或未知时回落到默认蓝。
        /// 仅覆写 Accent/AccentDark/AccentSoft 三色，深浅色模式的其余配色不受影响。
        /// </summary>
        public static void ApplyAccent(string key)
        {
            _accentOverride = null;
            _accentDarkOverride = null;
            _accentSoftOverride = null;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            // 每组：浅色模式(强调/深强调/浅底) + 深色模式(强调/深强调/浅底)。
            switch (key)
            {
                case "green":
                    _accentOverride = Color.FromArgb(22, 140, 84);
                    _accentDarkOverride = Color.FromArgb(74, 200, 138);
                    _accentSoftOverride = _dark ? Color.FromArgb(26, 52, 40) : Color.FromArgb(227, 245, 235);
                    break;
                case "purple":
                    _accentOverride = Color.FromArgb(124, 77, 255);
                    _accentDarkOverride = Color.FromArgb(155, 123, 255);
                    _accentSoftOverride = _dark ? Color.FromArgb(51, 41, 79) : Color.FromArgb(240, 234, 255);
                    break;
                case "orange":
                    _accentOverride = Color.FromArgb(217, 119, 6);
                    _accentDarkOverride = Color.FromArgb(245, 158, 60);
                    _accentSoftOverride = _dark ? Color.FromArgb(63, 47, 26) : Color.FromArgb(255, 241, 220);
                    break;
                case "rose":
                    _accentOverride = Color.FromArgb(214, 51, 108);
                    _accentDarkOverride = Color.FromArgb(235, 109, 155);
                    _accentSoftOverride = _dark ? Color.FromArgb(64, 32, 47) : Color.FromArgb(255, 232, 241);
                    break;
                default:
                    // blue 与未知键：使用内置默认。
                    break;
            }
        }

        /// <summary>设置界面字体缩放百分比（90–130）。在 BuildUi 前调用。</summary>
        public static void SetFontScale(int percent)
        {
            if (percent < 90)
            {
                percent = 90;
            }
            if (percent > 130)
            {
                percent = 130;
            }
            _fontScale = percent / 100.0;
        }

        /// <summary>当前字体缩放百分比（90–130）。固定宽度控件可按它放大，避免缩放下截断。</summary>
        public static int FontScalePercent
        {
            get { return (int)(_fontScale * 100.0); }
        }

        /// <summary>
        /// 1.7.5（B-18）：把设计态像素宽度按当前界面缩放换算成实际宽度。
        /// 此前项目里所有固定宽度（下拉框 116/84/170、自定义镜像框 260、数值框 80/64/72）
        /// 都是硬编码，字号涨 30% 而控件宽度不变，导致文字截断与整行溢出。
        /// 新增固定宽度控件必须用 Scaled(px)，不要直接写死像素。
        /// </summary>
        public static int Scaled(int pixels)
        {
            return (int)Math.Ceiling(pixels * _fontScale);
        }

        public static Font FontRegular(float size)
        {
            return new Font(FontFamily, (float)(size * _fontScale), FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font FontBold(float size)
        {
            return new Font(FontFamily, (float)(size * _fontScale), FontStyle.Bold, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int diameter = radius * 2;
            if (diameter > bounds.Width)
            {
                diameter = bounds.Width;
            }
            if (diameter > bounds.Height)
            {
                diameter = bounds.Height;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control == null)
            {
                return;
            }
            EventHandler apply = delegate
            {
                // 注意：不要把本委托挂到 HandleCreated —— 该事件发生在
                // CreateHandle() 调用栈内，此时给 Region 赋新值会释放旧值，
                // 正是「执行 CreateHandle() 时无法调用值 Dispose()」的来源。
                // 构造时设置一次 + 后续仅在尺寸变化时更新即可。
                if (control.IsDisposed || control.Disposing)
                {
                    return;
                }
                if (control.Width <= 0 || control.Height <= 0)
                {
                    return;
                }
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, control.Width, control.Height), radius))
                {
                    control.Region = new Region(path);
                }
            };
            control.SizeChanged += apply;
            apply(null, EventArgs.Empty);
        }

        public static Label CreateTitleLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = FontBold(15F);
            label.ForeColor = TextPrimary;
            label.BackColor = Color.Transparent;
            label.Margin = new Padding(0);
            return label;
        }

        public static Label CreateCaptionLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = FontRegular(9F);
            label.ForeColor = TextSecondary;
            label.BackColor = Color.Transparent;
            label.Margin = new Padding(0);
            return label;
        }

        public static Label CreateFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = FontRegular(9.5F);
            label.ForeColor = TextPrimary;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            // 表单标签必须单行：放进固定宽度的 TableLayoutPanel 列时，
            // AutoSize 标签会被压窄并自动换行（"Java（需要 17）"曾因此断成两行）。
            // AutoEllipsis + 单行高度让文本在极端情况下以省略号收尾而不是折行。
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            label.Margin = new Padding(0, 7, 10, 3);
            return label;
        }

        public static Label CreateSectionLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = FontBold(10.5F);
            label.ForeColor = TextPrimary;
            label.BackColor = Color.Transparent;
            label.Margin = new Padding(0, 0, 0, 8);
            return label;
        }

        public static CardPanel CreateCard()
        {
            CardPanel panel = new CardPanel();
            panel.Padding = new Padding(20);
            return panel;
        }

        public static void StyleTextBox(TextBox box)
        {
            if (box == null)
            {
                return;
            }
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = FontRegular(9.5F);
            box.ForeColor = TextPrimary;
            box.BackColor = CardBackground;
        }

        public static void StyleCombo(ComboBox combo)
        {
            if (combo == null)
            {
                return;
            }
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = FontRegular(9.5F);
            combo.ForeColor = TextPrimary;
            combo.BackColor = CardBackground;
        }

        public static Button CreatePrimaryButton(string text)
        {
            // 1.7.7：主按钮改为渐变自绘（GradientButton），悬停时反向渐变提亮。
            GradientButton button = new GradientButton();
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.Font = FontBold(9.5F);
            button.Height = 36;
            button.Width = 150;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 3, 8, 3);
            // 渐变按钮自绘圆角（OnPaint 里用路径填充 + 清父背景），
            // 不需要 Region，也就不会在句柄创建期触发 Region 值释放。
            return button;
        }

        /// <summary>1.7.7：带底色的圆角信息徽章，用于"当前管理目录"等状态提示。</summary>
        public static Label CreateBadgeLabel(string text)
        {
            BadgeLabel label = new BadgeLabel();
            label.Text = text;
            label.Font = FontRegular(9F);
            label.ForeColor = TextPrimary;
            label.AutoSize = false;
            return label;
        }

        public static Button CreateSecondaryButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = AccentSoft;
            button.FlatAppearance.MouseDownBackColor = AccentSoft;
            button.BackColor = CardBackground;
            button.ForeColor = TextPrimary;
            button.Font = FontRegular(9.5F);
            button.AutoSize = true;
            button.MinimumSize = new Size(78, 32);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 3, 8, 3);
            // 1.7.7：悬停时边框点亮为强调色，给出明确的交互反馈。
            button.MouseEnter += delegate { button.FlatAppearance.BorderColor = UiTheme.Accent; };
            button.MouseLeave += delegate { button.FlatAppearance.BorderColor = UiTheme.Border; };
            ApplyRoundedRegion(button, 7);
            return button;
        }

        /// <summary>
        /// 固定尺寸按钮。用于状态栏这类由 TableLayoutPanel 自动布局的位置：
        /// AutoSize 会让按钮按 PreferredSize 撑高所在行，导致按钮溢出栏外显示异常。
        /// </summary>
        public static Button CreateFixedButton(string text, int width, int height)
        {
            Button button = CreateSecondaryButton(text);
            button.AutoSize = false;
            button.MinimumSize = Size.Empty;
            button.Size = new Size(width, height);
            button.Margin = new Padding(0);
            ApplyRoundedRegion(button, 7);
            return button;
        }

        public static NoticeBar CreateNotice()
        {
            return new NoticeBar();
        }
    }

    /// <summary>
    /// 1.7.7：主操作按钮。渐变填充 + 悬停提亮 + 圆角，替代原先的纯色 Button。
    /// 完全自绘，因此强调色与深浅主题切换后无需额外刷新逻辑（界面重建时重新构造）。
    /// </summary>
    internal sealed class GradientButton : Button
    {
        private bool _hover;

        public GradientButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.Background);

            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            Color from;
            Color to;
            if (!Enabled)
            {
                from = UiTheme.Border;
                to = UiTheme.Border;
            }
            else if (_hover)
            {
                from = UiTheme.AccentDark;
                to = UiTheme.Accent;
            }
            else
            {
                from = UiTheme.Accent;
                to = UiTheme.AccentDark;
            }

            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 8))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds, from, to, LinearGradientMode.Vertical))
            {
                graphics.FillPath(brush, path);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                bounds,
                Enabled ? ForeColor : UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 1.7.7：圆角信息徽章。用于把「当前管理目录 / 版本隔离状态」从一堆
    /// 普通说明文字里区分出来，避免用户漏看。
    /// </summary>
    internal sealed class BadgeLabel : Label
    {
        public BadgeLabel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.Transparent;
            Padding = new Padding(10, 0, 10, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.Background);

            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 7))
            using (SolidBrush fill = new SolidBrush(UiTheme.AccentSoft))
            using (Pen border = new Pen(UiTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            Rectangle textBounds = new Rectangle(
                bounds.Left + Padding.Left,
                bounds.Top,
                Math.Max(1, bounds.Width - Padding.Horizontal),
                bounds.Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                ForeColor,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Left |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 圆角卡片容器（浅色为白底，深色为深灰底）。
    /// </summary>
    internal sealed class CardPanel : Panel
    {
        public int CornerRadius = 14;

        public CardPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = UiTheme.Background;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(UiTheme.Background);

            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            // 1.7.7：向右下偏移的柔和投影，卡片从背景里"浮"起来。
            Rectangle shadow = new Rectangle(
                bounds.Left + 2, bounds.Top + 3, bounds.Width, bounds.Height);
            using (GraphicsPath shadowPath = UiTheme.RoundedRect(shadow, CornerRadius))
            using (SolidBrush shadowBrush = new SolidBrush(UiTheme.CardShadow))
            {
                graphics.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath path = UiTheme.RoundedRect(bounds, CornerRadius))
            using (SolidBrush fill = new SolidBrush(UiTheme.CardBackground))
            using (Pen border = new Pen(UiTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);

                // 顶部 3px 主题色渐变：给每个分区一个轻量的视觉锚点，
                // 用裁剪保证渐变不会溢出圆角。
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.SetClip(path);
                    Rectangle strip = new Rectangle(
                        bounds.Left + 1, bounds.Top + 1, Math.Max(1, bounds.Width - 2), 3);
                    using (LinearGradientBrush stripBrush = new LinearGradientBrush(
                        strip, UiTheme.Accent, UiTheme.AccentSoft,
                        LinearGradientMode.Horizontal))
                    {
                        graphics.FillRectangle(stripBrush, strip);
                    }
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }
    }

    /// <summary>
    /// 左侧导航按钮：选中时用主题色填充，并在左侧显示强调条。
    /// </summary>
    internal sealed class NavButton : Button
    {
        private bool _selected;
        private bool _hover;

        /// <summary>导航图标，由 MainForm 按页面顺序指定。</summary>
        public NavGlyphs.Glyph Glyph = NavGlyphs.Glyph.Play;

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    Invalidate();
                }
            }
        }

        public NavButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Height = 44;
            Width = 160;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontRegular(10F);
            BackColor = UiTheme.SidebarBackground;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(UiTheme.SidebarBackground);

            Rectangle bounds = new Rectangle(4, 2, Width - 9, Height - 5);

            // 1.7.7：选中项用横向渐变（强调色 → 深强调色），比纯色更有层次；
            // 未选中仅悬停时用浅色底，避免"两个按钮都像被选中"。
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 9))
            {
                if (_selected)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        bounds, UiTheme.Accent, UiTheme.AccentDark,
                        LinearGradientMode.Horizontal))
                    {
                        graphics.FillPath(brush, path);
                    }
                }
                else
                {
                    Color fill = _hover ? UiTheme.AccentSoft : UiTheme.SidebarBackground;
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        graphics.FillPath(brush, path);
                    }
                }
            }

            // 选中：左侧白色强调条；悬停未选中：细主题色提示条。
            int markerWidth = _selected ? 4 : (_hover ? 3 : 0);
            if (markerWidth > 0)
            {
                Rectangle marker = new Rectangle(
                    bounds.Left + 3, bounds.Top + 10, markerWidth, bounds.Height - 20);
                using (SolidBrush markerBrush = new SolidBrush(
                    _selected ? Color.White : UiTheme.Accent))
                {
                    graphics.FillRectangle(markerBrush, marker);
                }
            }

            Color textColor = _selected ? Color.White : UiTheme.TextPrimary;
            Font font = _selected ? UiTheme.FontBold(10F) : UiTheme.FontRegular(10F);

            // 左侧矢量图标：随选中状态变色，与文字基线居中对齐。
            RectangleF glyphBounds = new RectangleF(
                bounds.Left + 14, bounds.Top + (bounds.Height - 18) / 2f, 18, 18);
            NavGlyphs.Draw(graphics, Glyph, glyphBounds, textColor);

            Rectangle textBounds = new Rectangle(
                bounds.Left + 42,
                bounds.Top,
                bounds.Width - 46,
                bounds.Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                font,
                textBounds,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// 自绘进度条：圆角轨道 + 主题色填充。系统 ProgressBar 在深色主题下几乎不可见。
    /// </summary>
    internal sealed class FlatProgressBar : Control
    {
        private int _value;
        private bool _marquee;
        private int _marqueeOffset;
        private readonly Timer _marqueeTimer;

        public int Minimum { get; set; }
        public int Maximum { get; set; }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = value;
                if (clamped < Minimum)
                {
                    clamped = Minimum;
                }
                if (clamped > Maximum)
                {
                    clamped = Maximum;
                }
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                }
            }
        }

        public bool Marquee
        {
            get { return _marquee; }
            set
            {
                if (_marquee == value)
                {
                    return;
                }
                _marquee = value;
                if (value)
                {
                    _marqueeTimer.Start();
                }
                else
                {
                    _marqueeTimer.Stop();
                }
                Invalidate();
            }
        }

        public FlatProgressBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Minimum = 0;
            Maximum = 100;
            Height = 8;
            BackColor = UiTheme.Background;
            _marqueeTimer = new Timer();
            _marqueeTimer.Interval = 40;
            _marqueeTimer.Tick += delegate
            {
                _marqueeOffset = (_marqueeOffset + 8) % 120;
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _marqueeTimer != null)
            {
                _marqueeTimer.Stop();
                _marqueeTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.Background);

            int height = Math.Max(6, Math.Min(10, Height));
            Rectangle track = new Rectangle(0, (Height - height) / 2, Width - 1, height);
            if (track.Width <= 2)
            {
                return;
            }

            using (GraphicsPath path = UiTheme.RoundedRect(track, height / 2))
            using (SolidBrush trackBrush = new SolidBrush(UiTheme.Border))
            {
                graphics.FillPath(trackBrush, path);
            }

            if (_marquee)
            {
                int blockWidth = Math.Max(40, track.Width / 4);
                int left = track.Left + (_marqueeOffset * Math.Max(1, track.Width) / 120);
                if (left + blockWidth > track.Right)
                {
                    left = track.Right - blockWidth;
                }
                Rectangle block = new Rectangle(left, track.Top, blockWidth, track.Height);
                using (GraphicsPath path = UiTheme.RoundedRect(block, height / 2))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    block, UiTheme.Accent, UiTheme.AccentDark,
                    LinearGradientMode.Horizontal))
                {
                    graphics.FillPath(brush, path);
                }
                return;
            }

            int range = Math.Max(1, Maximum - Minimum);
            int filled = (int)((long)track.Width * (Value - Minimum) / range);
            if (filled <= 0)
            {
                return;
            }
            Rectangle fill = new Rectangle(track.Left, track.Top, filled, track.Height);
            using (GraphicsPath path = UiTheme.RoundedRect(fill, height / 2))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                fill, UiTheme.Accent, UiTheme.AccentDark,
                LinearGradientMode.Horizontal))
            {
                graphics.FillPath(brush, path);
            }
        }
    }

    /// <summary>
    /// 页面内信息条：替代部分弹窗，错误可读、可关闭。
    /// </summary>
    internal sealed class NoticeBar : Panel
    {
        private NoticeLevel _level;
        private bool _hoverClose;
        private Rectangle _closeBounds;

        public enum NoticeLevel
        {
            Info,
            Warning,
            Error,
            Success
        }

        public NoticeBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Height = 38;
            Visible = false;
            Cursor = Cursors.Default;
        }

        public NoticeLevel Level
        {
            get { return _level; }
            set
            {
                if (_level != value)
                {
                    _level = value;
                    Invalidate();
                }
            }
        }

        public string Message = "";

        public void ShowNotice(NoticeLevel level, string message)
        {
            Level = level;
            Message = message ?? "";
            Visible = true;
            // 高度随文本行数自适应：旧实现固定 38px + EndEllipsis，
            // 稍长的提示（如离线账号的组网说明）会被截成"…"，用户看不到关键内容。
            AdjustHeight();
            Invalidate();
        }

        /// <summary>按当前宽度下文本所需高度调整控件高度，最少保持单行高度。</summary>
        private void AdjustHeight()
        {
            int width = Width > 0 ? Width - 48 : 400;
            if (width < 80)
            {
                width = 80;
            }
            int needed = TextRenderer.MeasureText(
                Message,
                UiTheme.FontRegular(9F),
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height;
            int height = needed + 14;
            if (height < 38)
            {
                height = 38;
            }
            if (Height != height)
            {
                Height = height;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 宽度变化会改变换行结果，需要重新计算高度。
            if (Visible && Message != null && Message.Length > 0)
            {
                AdjustHeight();
            }
        }

        public void HideNotice()
        {
            Visible = false;
        }

        private Color LevelColor
        {
            get
            {
                switch (_level)
                {
                    case NoticeLevel.Success:
                        return UiTheme.Success;
                    case NoticeLevel.Warning:
                        return UiTheme.Warning;
                    case NoticeLevel.Error:
                        return UiTheme.Danger;
                    default:
                        return UiTheme.Accent;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hover = _closeBounds.Contains(e.Location);
            if (hover != _hoverClose)
            {
                _hoverClose = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverClose = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_closeBounds.Contains(e.Location))
            {
                HideNotice();
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.Background);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 8))
            using (SolidBrush fill = new SolidBrush(UiTheme.CardBackground))
            using (Pen border = new Pen(LevelColor))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            Rectangle accent = new Rectangle(bounds.Left + 1, bounds.Top + 1, 4, bounds.Height - 2);
            using (SolidBrush brush = new SolidBrush(LevelColor))
            {
                graphics.FillRectangle(brush, accent);
            }

            Rectangle textBounds = new Rectangle(bounds.Left + 16, bounds.Top + 6,
                bounds.Width - 48, bounds.Height - 12);
            // 用 WordBreak 允许多行显示；高度已由 AdjustHeight 保证够用。
            TextRenderer.DrawText(
                graphics,
                Message,
                UiTheme.FontRegular(9F),
                textBounds,
                UiTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

            // 关闭按钮贴右上：多行提示时居中会显得悬在中间。
            _closeBounds = new Rectangle(bounds.Right - 28, bounds.Top + 6, 18, 18);
            TextRenderer.DrawText(
                graphics,
                "×",
                UiTheme.FontBold(11F),
                _closeBounds,
                _hoverClose ? LevelColor : UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// 空状态占位：在列表/日志等区域还没有内容时给出说明与下一步动作，
    /// 避免用户看到一片空白而不知道能做什么。
    /// </summary>
    internal sealed class EmptyState : Control
    {
        public string TitleText = "";
        public string DetailText = "";
        /// <summary>可选：显示一个居中的操作按钮。</summary>
        public string ActionText = "";
        public event EventHandler ActionClicked;
        private bool _hoverAction;
        private Rectangle _actionBounds;

        public EmptyState()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Visible = false;
            BackColor = UiTheme.CardBackground;
        }

        public void ShowState(string title, string detail, string actionText)
        {
            TitleText = title ?? "";
            DetailText = detail ?? "";
            ActionText = actionText ?? "";
            Visible = true;
            Invalidate();
        }

        public void HideState()
        {
            Visible = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hover = ActionText.Length > 0 && _actionBounds.Contains(e.Location);
            if (hover != _hoverAction)
            {
                _hoverAction = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverAction = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (ActionText.Length > 0 && _actionBounds.Contains(e.Location))
            {
                EventHandler handler = ActionClicked;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.CardBackground);

            int centerY = Height / 2;
            int titleHeight = 28;
            int detailHeight = DetailText.Length > 0 ? 22 : 0;
            int actionHeight = ActionText.Length > 0 ? 36 : 0;
            int blockHeight = titleHeight + detailHeight + actionHeight;
            int top = Math.Max(8, centerY - blockHeight / 2);

            Rectangle titleBounds = new Rectangle(0, top, Width, titleHeight);
            TextRenderer.DrawText(
                graphics, TitleText, UiTheme.FontBold(11F), titleBounds,
                UiTheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);

            if (detailHeight > 0)
            {
                Rectangle detailBounds = new Rectangle(0, top + titleHeight, Width, detailHeight);
                TextRenderer.DrawText(
                    graphics, DetailText, UiTheme.FontRegular(9F), detailBounds,
                    UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
            }

            if (actionHeight > 0)
            {
                int buttonWidth = 120;
                _actionBounds = new Rectangle(
                    (Width - buttonWidth) / 2,
                    top + titleHeight + detailHeight + 8,
                    buttonWidth,
                    actionHeight - 8);
                using (GraphicsPath path = UiTheme.RoundedRect(_actionBounds, 7))
                using (SolidBrush fill = new SolidBrush(
                    _hoverAction ? UiTheme.AccentSoft : UiTheme.CardBackground))
                using (Pen border = new Pen(_hoverAction ? UiTheme.Accent : UiTheme.Border))
                {
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(border, path);
                }
                TextRenderer.DrawText(
                    graphics, ActionText, UiTheme.FontRegular(9F), _actionBounds,
                    _hoverAction ? UiTheme.Accent : UiTheme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
