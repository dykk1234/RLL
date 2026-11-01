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
        /// </summary>
        public static FlowLayoutPanel CreateToolbar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.WrapContents = false;
            bar.BackColor = UiTheme.Background;
            bar.Margin = new Padding(0, 0, 0, 8);
            return bar;
        }

        /// <summary>
        /// 卡片内部的横向工具条（背景跟随卡片）。
        /// </summary>
        public static FlowLayoutPanel CreateCardToolbar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.WrapContents = false;
            bar.BackColor = UiTheme.CardBackground;
            bar.Margin = new Padding(0, 2, 0, 6);
            return bar;
        }
    }
}
