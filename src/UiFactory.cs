using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteLauncher
{
    /// <summary>
    /// 页面骨架与通用小部件的构建工具。
    /// 从 MainForm 抽出，避免主窗口持续膨胀；这里只做无状态的布局组装，
    /// 不持有任何业务状态，因此可安全复用。
    /// </summary>
    internal static class UiFactory
    {
        /// <summary>
        /// 创建一页：页头（标题 + 说明）、页内提示条、内容区、页脚。
        /// 返回的页面有 4 行，行号含义：
        /// 0 = 页头，1 = 提示条，2 = 内容（填充剩余高度），3 = 页脚。
        /// </summary>
        public static TableLayoutPanel CreatePage(string title, string caption, out NoticeBar notice)
        {
            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.BackColor = UiTheme.Background;
            page.ColumnCount = 1;
            page.RowCount = 4;
            page.Padding = new Padding(0);
            // 1.7.7：唯一一列必须显式声明为 100% 父宽。未声明 ColumnStyle 时
            // TableLayoutPanel 默认按内容 AutoSize，最宽的子控件（模组页底部
            // 按钮行、搜索输入行）会把整页撑到窗口之外；FlowLayoutPanel 的
            // WrapContents 也因此按"无限宽"计算而不换行，表现为按钮溢出屏幕。
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(CreatePageHeader(title, caption), 0, 0);

            notice = UiTheme.CreateNotice();
            notice.Dock = DockStyle.Top;
            notice.Margin = new Padding(0, 0, 0, 10);
            page.Controls.Add(notice, 0, 1);
            return page;
        }

        public static TableLayoutPanel CreatePageHeader(string title, string caption)
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.AutoSize = true;
            header.ColumnCount = 1;
            header.RowCount = 2;
            header.BackColor = UiTheme.Background;
            header.Margin = new Padding(0, 0, 0, 10);

            Label titleLabel = UiTheme.CreateTitleLabel(title);
            header.Controls.Add(titleLabel, 0, 0);

            Label captionLabel = UiTheme.CreateCaptionLabel(caption);
            captionLabel.Margin = new Padding(0, 4, 0, 0);
            header.Controls.Add(captionLabel, 0, 1);
            return header;
        }

        /// <summary>
        /// 带标题的列表卡片：标题在顶部，列表填满剩余空间。
        /// </summary>
        public static CardPanel CreateListCard(string title, ListView list)
        {
            CardPanel card = UiTheme.CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.BackColor = UiTheme.CardBackground;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label section = UiTheme.CreateSectionLabel(title);
            layout.Controls.Add(section, 0, 0);
            layout.Controls.Add(list, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>
        /// 统一风格的明细列表：无边框、整行选中、单行选择。
        /// </summary>
        public static ListView CreateDetailList(params string[] columns)
        {
            ListView list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.GridLines = false;
            list.BorderStyle = BorderStyle.None;
            list.Font = UiTheme.FontRegular(9F);
            list.BackColor = UiTheme.CardBackground;
            list.ForeColor = UiTheme.TextPrimary;
            if (columns != null)
            {
                for (int i = 0; i < columns.Length; i++)
                {
                    // 列宽由调用方随后设置；此处只负责建立列。
                    list.Columns.Add(columns[i], 120);
                }
            }
            return list;
        }

        public static NumericUpDown CreateNumeric(int min, int max, int increment, int width)
        {
            NumericUpDown numeric = new NumericUpDown();
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.Increment = increment;
            numeric.Width = width;
            numeric.Font = UiTheme.FontRegular(9.5F);
            numeric.ForeColor = UiTheme.TextPrimary;
            numeric.BackColor = UiTheme.CardBackground;
            numeric.Margin = new Padding(0, 5, 0, 3);
            return numeric;
        }

        public static CheckBox CreateCheck(string text)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.AutoSize = true;
            check.ForeColor = UiTheme.TextPrimary;
            check.BackColor = Color.Transparent;
            check.Margin = new Padding(0, 8, 18, 8);
            return check;
        }

        /// <summary>
        /// 内容区常用的二列表单网格：左列固定宽度标签，右列填充。
        /// </summary>
        public static TableLayoutPanel CreateFormGrid(int labelWidth, int rows)
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.BackColor = UiTheme.CardBackground;
            grid.ColumnCount = 2;
            grid.RowCount = rows;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return grid;
        }

        /// <summary>
        /// 自动高度的横向工具条。
        ///
        /// 1.7.5（B-18）：默认改为允许换行。此前 WrapContents=false，一行塞满控件后
        /// 一旦总宽超过容器宽度，右侧控件直接被裁掉（表现为"缩放下拉框不见了"
        /// "保存设置按钮看不见"），既不换行也没有可靠滚动条。
        /// </summary>
        public static FlowLayoutPanel CreateToolbar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.WrapContents = true;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.BackColor = UiTheme.Background;
            bar.Margin = new Padding(0, 0, 0, 8);
            return bar;
        }

        /// <summary>
        /// 卡片内部的横向工具条（背景跟随卡片）。
        /// 1.7.5（B-18）：与 CreateToolbar 一致，默认允许换行。
        /// </summary>
        public static FlowLayoutPanel CreateCardToolbar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.WrapContents = true;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.BackColor = UiTheme.CardBackground;
            bar.Margin = new Padding(0, 2, 0, 6);
            return bar;
        }

        /// <summary>
        /// 1.7.5（B-18）：自动折行的多行说明组件。
        ///
        /// 用于替代两类已弃用写法：
        /// 1) Dock=Fill 的多行 Label —— 它不参与 TableLayoutPanel 的行高计算，
        ///    多行文本会被所在 AutoSize 行裁掉（1.7.2 的"安全提示被截断"就是它）；
        /// 2) 按构造时行数写死高度的只读 TextBox —— 排版宽度一变（缩放/DPI/换行）
        ///    行数与高度就不匹配，再次裁字。
        ///
        /// 这里用 AutoSize=true 的 Label + MaximumSize 限宽：宽度受限后自动折行，
        /// 高度由 AutoSize 正确撑开，不需要调用方算高度。空文本时不占空间。
        /// </summary>
        public static Label CreateWrappedNote(string text, Color background, Color foreground)
        {
            Label note = new Label();
            note.Text = text ?? "";
            note.AutoSize = true;
            note.BackColor = background;
            note.ForeColor = foreground;
            note.Font = UiTheme.FontRegular(8.5F);
            note.Margin = new Padding(0, 4, 3, 3);
            note.Visible = note.Text.Length > 0;
            // AutoSize 的 Label 放进 AutoSize 的 TableLayoutPanel 时不会折行 ——
            // 表格会按文本首选宽度整体变宽。要折行必须给出宽度上限，
            // 而该上限只能在父容器宽度定型后计算（见 BindNoteWidth）。
            note.ParentChanged += delegate { BindNoteWidth(note, 0); };
            return note;
        }

        /// <summary>
        /// 1.7.5（B-18）：给折行说明控件设置宽度上限，并在容器布局时刷新。
        ///
        /// MaximumSize.Width 是"折行阈值"：设为可用宽度后，超宽文本自动换行，
        /// 高度再由 AutoSize 正确撑开，调用方不需要手工算高度。
        ///
        /// 为什么必须在 Layout 时刷新而不能只算一次：
        /// 说明控件通常在构建期就加入表格，此时父容器宽度尚未定型（实测只有 ~200px），
        /// 一次性计算会把文字压成很窄的竖排。父容器宽度收敛后不会再变
        /// （说明所在的卡片是 Dock=Top，宽度由滚动宿主决定，不由内容决定），
        /// 因此每次布局重设不会产生收缩反馈。
        /// </summary>
        internal static void BindNoteWidth(Label note, int rightInset)
        {
            if (note == null)
            {
                return;
            }
            EventHandler apply = delegate { ApplyNoteWidth(note, rightInset); };
            // 只在"宽度会变化且宽度不由本控件内容决定"的父容器尺寸变化时重算。
            // 不挂 Layout：Layout 在 Dispose/句柄创建期间也会触发，此时改动
            // MaximumSize 会引发 "CreateHandle() 时无法调用 Dispose()" 的异常。
            note.ParentChanged += delegate
            {
                if (note.Parent != null && !note.Parent.IsDisposed)
                {
                    note.Parent.SizeChanged += new EventHandler(apply);
                }
                apply(note, EventArgs.Empty);
            };
            note.HandleCreated += delegate { apply(note, EventArgs.Empty); };
        }

        /// <summary>
        /// 1.7.5（B-18）：按父容器可用宽度设置折行说明控件的宽度上限。
        /// MaximumSize.Width 是"折行阈值"：设为可用宽度后超宽文本自动换行，
        /// 高度由 AutoSize 正确撑开，调用方无需手工算高度。
        /// </summary>
        private static void ApplyNoteWidth(Label note, int rightInset)
        {
            if (note == null || note.IsDisposed || !note.IsHandleCreated)
            {
                return;
            }
            Control parent = note.Parent;
            if (parent == null || parent.IsDisposed || parent.Disposing)
            {
                return;
            }
            // 1.7.6：父容器句柄尚未创建时不要改 MaximumSize —— WinForms 在
            // CreateHandle 期间对子控件做任何几何改动都可能抛
            // "执行 CreateHandle() 时无法调用值 Dispose()"（切换页面时偶发）。
            if (!parent.IsHandleCreated)
            {
                return;
            }
            int available = parent.ClientSize.Width - note.Left
                - note.Margin.Right - Math.Max(0, rightInset);
            if (available < 200)
            {
                // 容器尚未定型：保持当前限宽，等下一次尺寸变化。
                return;
            }
            if (note.MaximumSize.Width == available)
            {
                return;
            }
            note.MaximumSize = new Size(available, 0);
        }

        public static Label CreateWrappedNote(string text, Color background)
        {
            return CreateWrappedNote(text, background, UiTheme.TextSecondary);
        }

        /// <summary>
        /// 1.7.5（B-18）：把内容层正确放进 AutoScroll 容器的滚动宿主。
        ///
        /// 修掉的反模式：内容层同时设 AutoSize=true 与 Anchor=Top|Left|Right
        /// （两者互斥），且只用 Location(0,0) 定位。垂直滚动条出现时会占掉约 17px 宽度，
        /// 内容层不会可靠地变窄，右侧控件就被滚动条盖住。
        ///
        /// 现在由宿主 Resize 显式把内容层宽度设为
        /// （ClientSize.Width - 滚动条宽度），并在滚动条出现/消失后 PerformLayout()
        /// 刷新 DisplayRectangle；AutoScroll 仍保留作为兜底。
        /// </summary>
        public static Panel CreateScrollHost(Control content)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.BackColor = UiTheme.Background;
            host.Margin = new Padding(0);
            if (content != null)
            {
                host.Controls.Add(content);
            }
            EventHandler apply = delegate
            {
                if (content == null || content.IsDisposed)
                {
                    return;
                }
                int scrollBar = host.VerticalScroll.Visible
                    ? SystemInformation.VerticalScrollBarWidth : 0;
                int width = host.ClientSize.Width - scrollBar;
                if (width > 0 && content.Width != width)
                {
                    content.Width = width;
                }
                host.PerformLayout();
            };
            host.SizeChanged += new EventHandler(apply);
            host.Layout += delegate { apply(host, EventArgs.Empty); };
            return host;
        }
    }
}
