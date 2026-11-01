using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteLauncher
{
    /// <summary>
    /// 新手引导面板：用编号步骤 + 当前进度 + 一个明确的主操作按钮，
    /// 解决"打开启动器不知道下一步做什么"的问题。
    ///
    /// 设计要点：
    /// - 步骤用序号与状态色区分（已完成 / 当前 / 未开始），用户一眼看出卡在哪一步；
    /// - 只给一个主操作按钮，避免多选困难；
    /// - 面板可整体隐藏，不占用日常使用的空间。
    /// </summary>
    internal sealed class GuidePanel : Panel
    {
        public sealed class Step
        {
            public string Title;
            public string Detail;
            /// <summary>0 = 未完成；1 = 当前待完成；2 = 已完成。</summary>
            public int State;

            public Step(string title, string detail)
            {
                Title = title;
                Detail = detail;
                State = 0;
            }
        }

        private readonly List<Step> _steps = new List<Step>();
        private string _headline = "";
        private string _actionText = "";
        private bool _hoverAction;
        private Rectangle _actionBounds;
        private Rectangle _closeBounds;
        private bool _hoverClose;

        public event EventHandler ActionClicked;

        public GuidePanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = UiTheme.CardBackground;
            Visible = false;
            Height = 150;
        }

        public void SetContent(string headline, IList<Step> steps, string actionText)
        {
            _headline = headline ?? "";
            _steps.Clear();
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i] != null)
                    {
                        _steps.Add(steps[i]);
                    }
                }
            }
            _actionText = actionText ?? "";
            Visible = true;
            Invalidate();
        }

        public void HideGuide()
        {
            Visible = false;
        }

        /// <summary>按步骤状态计算所需高度（供外层布局使用）。</summary>
        public int PreferredHeight
        {
            get
            {
                int height = 16 + 26;                       // 顶部内边距 + 标题
                height += _steps.Count * 38;                // 每个步骤一行
                if (_actionText.Length > 0)
                {
                    height += 46;                           // 按钮行
                }
                return height + 12;                         // 底部内边距
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool overAction = _actionText.Length > 0 && _actionBounds.Contains(e.Location);
            bool overClose = _closeBounds.Contains(e.Location);
            if (overAction != _hoverAction || overClose != _hoverClose)
            {
                _hoverAction = overAction;
                _hoverClose = overClose;
                Cursor = (overAction || overClose) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverAction = false;
            _hoverClose = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_actionText.Length > 0 && _actionBounds.Contains(e.Location))
            {
                EventHandler handler = ActionClicked;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
                return;
            }
            if (_closeBounds.Contains(e.Location))
            {
                HideGuide();
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.Background);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 4 || bounds.Height <= 4)
            {
                return;
            }

            // 卡片背景 + 左侧强调条
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 10))
            using (SolidBrush fill = new SolidBrush(UiTheme.CardBackground))
            using (Pen border = new Pen(UiTheme.Accent))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            Color accent = UiTheme.Accent;
            using (SolidBrush b = new SolidBrush(accent))
            {
                g.FillRectangle(b, bounds.Left + 1, bounds.Top + 10, 4, bounds.Height - 20);
            }

            int left = bounds.Left + 18;
            int top = bounds.Top + 14;
            int contentWidth = bounds.Width - 60;

            // 标题 + 关闭按钮
            TextRenderer.DrawText(
                g, _headline, UiTheme.FontBold(11F),
                new Rectangle(left, top, contentWidth, 22),
                UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            _closeBounds = new Rectangle(bounds.Right - 30, bounds.Top + 12, 20, 20);
            TextRenderer.DrawText(
                g, "×", UiTheme.FontBold(11F), _closeBounds,
                _hoverClose ? UiTheme.Danger : UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            top += 26;

            // 步骤列表：序号圆点 + 标题 + 说明
            for (int i = 0; i < _steps.Count; i++)
            {
                Step step = _steps[i];
                Color dotColor;
                Color titleColor;
                if (step.State == 2)
                {
                    dotColor = UiTheme.Success;
                    titleColor = UiTheme.TextSecondary;
                }
                else if (step.State == 1)
                {
                    dotColor = UiTheme.Accent;
                    titleColor = UiTheme.TextPrimary;
                }
                else
                {
                    dotColor = UiTheme.Border;
                    titleColor = UiTheme.TextMuted;
                }

                // 序号圆点（已完成显示对勾）
                Rectangle dot = new Rectangle(left, top + 4, 18, 18);
                using (SolidBrush b = new SolidBrush(dotColor))
                {
                    g.FillEllipse(b, dot);
                }
                string mark = step.State == 2 ? "✓" : (i + 1).ToString();
                TextRenderer.DrawText(
                    g, mark, UiTheme.FontBold(8F), dot, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // 标题与说明放在同一行右侧，说明用次要色
                Rectangle titleRect = new Rectangle(left + 26, top, contentWidth - 26, 20);
                TextRenderer.DrawText(
                    g, step.Title, UiTheme.FontBold(9.5F), titleRect, titleColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);

                Rectangle detailRect = new Rectangle(left + 26, top + 19, contentWidth - 26, 17);
                TextRenderer.DrawText(
                    g, step.Detail, UiTheme.FontRegular(8.5F), detailRect,
                    step.State == 2 ? UiTheme.TextMuted : UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);

                top += 38;
            }

            // 主操作按钮
            if (_actionText.Length > 0)
            {
                int buttonWidth = 150;
                _actionBounds = new Rectangle(left + 26, top + 4, buttonWidth, 32);
                using (GraphicsPath path = UiTheme.RoundedRect(_actionBounds, 8))
                using (SolidBrush fill = new SolidBrush(
                    _hoverAction ? UiTheme.AccentDark : UiTheme.Accent))
                {
                    g.FillPath(fill, path);
                }
                TextRenderer.DrawText(
                    g, _actionText, UiTheme.FontBold(9.5F), _actionBounds, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
