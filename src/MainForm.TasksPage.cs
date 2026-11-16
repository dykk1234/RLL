using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.6.0（F-1605 MainForm 拆分 · M1 起步）：下载任务页。
    /// 页面构建与操作处理从 MainForm 主体拆出，与其余页面共用同一 Form 实例；
    /// 后续版本将按 M2/M3/M4 继续拆分事件处理与应用服务层。
    /// </summary>
    public sealed partial class MainForm
    {
        private void BuildDownloadTasksPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "下载任务",
                "所有下载统一在这里排队（并发上限由设置控制）。带哈希校验的任务支持断点续传：暂停后分片保留，重试自动续传；恢复的任务会重新走安全校验。");

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = UiTheme.Background;
            body.ColumnCount = 1;
            body.RowCount = 2;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = true;  // 1.7.5（B-18）
            toolbar.BackColor = UiTheme.Background;
            toolbar.Margin = new Padding(0, 0, 0, 8);
            _downloadPauseButton = UiTheme.CreateSecondaryButton("暂停");
            _downloadPauseButton.Width = 76;
            _downloadPauseButton.Margin = new Padding(0, 3, 8, 3);
            _downloadPauseButton.Click += DownloadPauseClick;
            toolbar.Controls.Add(_downloadPauseButton);
            _downloadResumeButton = UiTheme.CreateSecondaryButton("重试");
            _downloadResumeButton.Width = 76;
            _downloadResumeButton.Margin = new Padding(0, 3, 8, 3);
            _downloadResumeButton.Click += DownloadRetryClick;
            toolbar.Controls.Add(_downloadResumeButton);
            _downloadCancelButton = UiTheme.CreateSecondaryButton("取消");
            _downloadCancelButton.Width = 76;
            _downloadCancelButton.Margin = new Padding(0, 3, 8, 3);
            _downloadCancelButton.Click += DownloadCancelClick;
            toolbar.Controls.Add(_downloadCancelButton);
            _downloadClearButton = UiTheme.CreateSecondaryButton("清空历史");
            _downloadClearButton.Width = 86;
            _downloadClearButton.Margin = new Padding(0, 3, 8, 3);
            _downloadClearButton.Click += DownloadClearClick;
            toolbar.Controls.Add(_downloadClearButton);
            _downloadSummaryLabel = UiTheme.CreateCaptionLabel("");
            _downloadSummaryLabel.Margin = new Padding(2, 9, 3, 3);
            toolbar.Controls.Add(_downloadSummaryLabel);
            body.Controls.Add(toolbar, 0, 0);

            _downloadTaskList = new ListView();
            _downloadTaskList.Dock = DockStyle.Fill;
            _downloadTaskList.View = View.Details;
            _downloadTaskList.FullRowSelect = true;
            _downloadTaskList.MultiSelect = true;
            _downloadTaskList.HideSelection = false;
            _downloadTaskList.GridLines = false;
            _downloadTaskList.BorderStyle = BorderStyle.None;
            _downloadTaskList.Font = UiTheme.FontRegular(9F);
            _downloadTaskList.Columns.Add("任务", 210);
            _downloadTaskList.Columns.Add("状态", 90);
            // 1.7.3 UI 优化：进度列加宽到 160，并在列文本里带上 F1 百分比，
            // 同时用 OwnerDraw 画一条细进度条（见下方 DrawDownloadTaskColumn），
            // 让"下载到底走到哪了"一眼可见，而不是只有一串数字。
            _downloadTaskList.Columns.Add("进度", 160);
            _downloadTaskList.Columns.Add("大小", 80);
            _downloadTaskList.Columns.Add("速度", 80);
            _downloadTaskList.Columns.Add("剩余", 72);
            _downloadTaskList.Columns.Add("来源", 90);
            _downloadTaskList.Columns.Add("错误", 200);
            // 1.7.3：自绘进度列 + 状态列着色，提升可读性。
            _downloadTaskList.OwnerDraw = true;
            _downloadTaskList.DrawColumnHeader += DrawDownloadTaskHeader;
            _downloadTaskList.DrawSubItem += DrawDownloadTaskColumn;
            body.Controls.Add(_downloadTaskList, 0, 1);

            page.Controls.Add(body, 0, 2);
            _pages.Add(page);
            // 1.7.2 修复：此前漏了这一步，页面从未加入宿主，导航点「下载任务」切不到任何控件。
            host.Controls.Add(page);
        }
        /// <summary>
        /// 1.7.3：轮询任务快照。此前只在"第一次进入下载任务页"时启动，
        /// 于是用户在下载页点了安装、但没切到任务页时，界面完全不刷新，
        /// 看上去就像"下载没有进度"。现在改为随程序启动常驻（间隔 400ms），
        /// 且始终同步到隐藏页的状态标签，切页时立刻看到最新值。
        /// </summary>
        private void StartDownloadTasksTimer()
        {
            if (_downloadTasksTimer != null)
            {
                return;
            }
            _downloadTasksTimer = new System.Windows.Forms.Timer();
            _downloadTasksTimer.Interval = 400;
            _downloadTasksTimer.Tick += delegate
            {
                RefreshDownloadTasks();
                RefreshDownloadProgressLine();
            };
            _downloadTasksTimer.Start();
        }

        /// <summary>
        /// 1.7.3：全局下载进度行（状态栏右侧），让用户在任意页面都能看到
        /// "正在下载什么、百分比、速度、剩余时间"，不必特意切到任务页。
        /// </summary>
        private void RefreshDownloadProgressLine()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            List<DownloadTask> tasks = _downloadManager.GetTasks();
            DownloadTask active = null;
            int activeCount = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsTerminal)
                {
                    activeCount++;
                    if (active == null)
                    {
                        active = tasks[i];
                    }
                }
            }
            if (active == null)
            {
                if (!_busy)
                {
                    _downloadProgressLabel.Visible = false;
                }
                return;
            }
            _downloadProgressLabel.Visible = true;
            // 1.7.6（B-20）：汇总任务没有字节数，百分比来自 Percent；
            // 否则游戏安装期间状态栏会一直显示"名字"而没有进度。
            int percent = active.IsJob
                ? active.Percent
                : (active.TotalBytes > 0
                    ? (int)(active.BytesReceived * 100L / active.TotalBytes)
                    : -1);
            string text = active.TaskName;
            if (percent >= 0)
            {
                text += "  " + percent + "%";
            }
            if (active.SpeedBytesPerSec > 0)
            {
                text += "  " + PathUtil.FormatBytes((long)active.SpeedBytesPerSec) + "/s";
            }
            if (active.EtaSeconds > 0)
            {
                text += "  剩余 " + PathUtil.FormatDuration(active.EtaSeconds);
            }
            if (activeCount > 1)
            {
                text += "  (+" + (activeCount - 1) + ")";
            }
            _downloadProgressLabel.Text = text;
        }

        /// <summary>
        /// 1.7.3：自绘列表头（保持主题配色，关闭系统默认的灰白样式）。
        /// </summary>
        private void DrawDownloadTaskHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(UiTheme.CardBackground), e.Bounds);
            Rectangle textRect = new Rectangle(
                e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics, e.Header.Text, UiTheme.FontBold(9F), textRect,
                UiTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(UiTheme.Border))
            {
                e.Graphics.DrawLine(
                    pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        /// <summary>
        /// 1.7.3：自绘单元格。进度列画一条细进度条（百分比数字叠加在右侧），
        /// 其余列按状态着色，让"进行中 / 已完成 / 失败"在列表里一眼可辨。
        /// </summary>
        private void DrawDownloadTaskColumn(object sender, DrawListViewSubItemEventArgs e)
        {
            DownloadTask task = null;
            if (e.Item != null && e.Item.Tag is string)
            {
                task = _downloadManager.GetTask((string)e.Item.Tag);
            }

            // 整行底色：选中时用强调色浅底，未选中跟随卡片背景。
            Color backColor = e.Item.Selected ? UiTheme.AccentSoft : UiTheme.CardBackground;
            e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);

            int colIndex = e.ColumnIndex;
            string text = e.SubItem == null ? "" : e.SubItem.Text;
            Color foreColor = UiTheme.TextPrimary;
            if (task != null)
            {
                if (task.State == DownloadTaskState.Failed)
                {
                    foreColor = UiTheme.Danger;
                }
                else if (task.State == DownloadTaskState.Completed)
                {
                    foreColor = UiTheme.TextMuted;
                }
                else if (task.State == DownloadTaskState.Canceled)
                {
                    foreColor = UiTheme.Warning;
                }
            }

            // 进度列（第 2 列）：画进度条 + 文字。
            // 1.7.6（B-20）：汇总任务的进度来自 Percent 而不是字节数，
            // 因此判断条件要把 IsJob 一起放进来，否则游戏/模组安装这一行永远不画进度条。
            bool hasProgress = task != null &&
                (task.TotalBytes > 0 || (task.IsJob && !task.IsTerminal));
            if (colIndex == 2 && hasProgress)
            {
                int percent = task.IsJob
                    ? task.Percent
                    : (int)(task.BytesReceived * 100L / task.TotalBytes);
                if (percent < 0) { percent = 0; }
                if (percent > 100) { percent = 100; }

                int barHeight = 10;
                int barTop = e.Bounds.Y + (e.Bounds.Height - barHeight) / 2;
                // 右侧留出百分比文字的宽度。
                Rectangle track = new Rectangle(
                    e.Bounds.X + 6, barTop,
                    Math.Max(10, e.Bounds.Width - 56), barHeight);
                using (GraphicsPath trackPath = UiTheme.RoundedRect(track, barHeight / 2))
                using (SolidBrush trackBrush = new SolidBrush(UiTheme.Border))
                {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }
                int filled = (int)((long)track.Width * percent / 100L);
                if (filled > 0)
                {
                    Rectangle fill = new Rectangle(track.X, track.Y, filled, track.Height);
                    using (GraphicsPath fillPath = UiTheme.RoundedRect(fill, barHeight / 2))
                    using (SolidBrush fillBrush = new SolidBrush(
                        task.State == DownloadTaskState.Failed ? UiTheme.Danger : UiTheme.Accent))
                    {
                        e.Graphics.FillPath(fillBrush, fillPath);
                    }
                }
                Rectangle percentRect = new Rectangle(
                    track.Right + 6, e.Bounds.Y, 46, e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics, percent + "%", UiTheme.FontRegular(8.5F), percentRect,
                    task.State == DownloadTaskState.Failed ? UiTheme.Danger : UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            Rectangle textBounds = new Rectangle(
                e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics, text, e.Item.ListView.Font, textBounds, foreColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void RefreshDownloadTasks()
        {
            if (_downloadTaskList == null || _downloadTaskList.IsDisposed)
            {
                return;
            }
            List<DownloadTask> tasks = _downloadManager.GetTasks();
            _downloadTaskList.BeginUpdate();
            try
            {
                _downloadTaskList.Items.Clear();
                int active = 0;
                for (int i = 0; i < tasks.Count; i++)
                {
                    DownloadTask task = tasks[i];
                    if (!task.IsTerminal)
                    {
                        active++;
                    }
                    ListViewItem item = new ListViewItem(task.TaskName);
                    item.SubItems.Add(DownloadManager.DescribeState(task.State));
                    item.SubItems.Add(FormatTaskProgress(task));
                    // 1.7.6（B-20）：汇总任务没有单文件大小/速度，显示"—"而不是 0 B。
                    item.SubItems.Add(task.IsJob ? "—" : PathUtil.FormatBytes(task.TotalBytes));
                    item.SubItems.Add(task.IsJob
                        ? "—"
                        : (task.SpeedBytesPerSec > 0
                            ? PathUtil.FormatBytes((long)task.SpeedBytesPerSec) + "/s"
                            : "—"));
                    // 1.7.3：新增「剩余」列，下载大文件时能直接看出还要等多久。
                    item.SubItems.Add(FormatTaskEta(task));
                    item.SubItems.Add(string.IsNullOrEmpty(task.Source) ? "—" : task.Source);
                    item.SubItems.Add(FormatTaskError(task));
                    item.Tag = task.TaskId;
                    // 1.7.3：颜色交由 DrawDownloadTaskColumn 按状态绘制
                    // （OwnerDraw 下 item.ForeColor 不生效，保留会让后续维护者误判）。
                    _downloadTaskList.Items.Add(item);
                }
                _downloadSummaryLabel.Text = "活动任务 " + active + " 个，共 " + tasks.Count + " 条记录（历史上限 "
                    + _downloadManager.HistoryLimit + "）。";
                UpdateDownloadButtons();
            }
            finally
            {
                _downloadTaskList.EndUpdate();
            }
        }
        private static string FormatTaskProgress(DownloadTask task)
        {
            // 1.7.6（B-20）：汇总任务（游戏版本安装 / 模组安装）没有逐文件字节数，
            // 进度来自业务模块回填的百分比，直接显示"N%"。
            if (task.IsJob)
            {
                return task.IsTerminal
                    ? (task.State == DownloadTaskState.Completed ? "100%" : "—")
                    : task.Percent + "%";
            }
            // 1.7.3：格式化逻辑下沉到 PathUtil，便于离线回归测试。
            return PathUtil.FormatProgress(task.BytesReceived, task.TotalBytes);
        }
        /// <summary>1.7.3：把预计剩余时间格式化成「N 秒 / N 分 N 秒 / N 小时 N 分」。</summary>
        private static string FormatTaskEta(DownloadTask task)
        {
            if (task.IsJob || task.IsTerminal || task.EtaSeconds <= 0)
            {
                return "—";
            }
            return PathUtil.FormatDuration(task.EtaSeconds);
        }
        private static string FormatTaskError(DownloadTask task)
        {
            if (task.State != DownloadTaskState.Failed &&
                task.State != DownloadTaskState.Canceled)
            {
                return "";
            }
            if (task.State == DownloadTaskState.Canceled)
            {
                // 1.7.6（B-20）：汇总任务无法续传，提示不要承诺"重试续传"。
                return task.IsJob ? "已取消" : "已取消（可重试续传）";
            }
            return task.ErrorCode + "：" + task.ErrorMessage;
        }
        private List<string> SelectedDownloadTaskIds()
        {
            List<string> result = new List<string>();
            if (_downloadTaskList == null)
            {
                return result;
            }
            for (int i = 0; i < _downloadTaskList.SelectedItems.Count; i++)
            {
                string id = _downloadTaskList.SelectedItems[i].Tag as string;
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
            }
            return result;
        }
        private void UpdateDownloadButtons()
        {
            if (_downloadPauseButton == null)
            {
                return;
            }
            List<string> selected = SelectedDownloadTaskIds();
            bool anyActive = false;
            bool anyPausable = false;
            for (int i = 0; i < selected.Count; i++)
            {
                DownloadTask task = _downloadManager.GetTask(selected[i]);
                if (task == null)
                {
                    continue;
                }
                if (!task.IsTerminal)
                {
                    anyActive = true;
                }
                if (!task.IsTerminal && task.AllowResume && _config.EnableDownloadResume)
                {
                    anyPausable = true;
                }
            }
            _downloadPauseButton.Enabled = anyPausable;
            _downloadResumeButton.Enabled = anyActive || selected.Count > 0;
            _downloadCancelButton.Enabled = selected.Count > 0;
            _downloadClearButton.Enabled = true;
        }
        private void DownloadPauseClick(object sender, EventArgs e)
        {
            List<string> selected = SelectedDownloadTaskIds();
            int paused = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                if (_downloadManager.Pause(selected[i]))
                {
                    paused++;
                }
            }
            if (paused == 0)
            {
                SetStatus("没有可暂停的任务（仅支持续传的任务可暂停）");
            }
            else
            {
                SetStatus("已暂停 " + paused + " 个任务，分片已保留");
            }
            RefreshDownloadTasks();
        }
        private void DownloadRetryClick(object sender, EventArgs e)
        {
            List<string> selected = SelectedDownloadTaskIds();
            if (selected.Count == 0)
            {
                SetStatus("请先选择任务");
                return;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                _downloadManager.Retry(selected[i]);
            }
            SetStatus("已重新排队 " + selected.Count + " 个任务");
            RefreshDownloadTasks();
        }
        private void DownloadCancelClick(object sender, EventArgs e)
        {
            List<string> selected = SelectedDownloadTaskIds();
            if (selected.Count == 0)
            {
                SetStatus("请先选择任务");
                return;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                _downloadManager.Cancel(selected[i]);
            }
            SetStatus("已取消 " + selected.Count + " 个任务");
            RefreshDownloadTasks();
        }
        private void DownloadClearClick(object sender, EventArgs e)
        {
            _downloadManager.ClearHistory();
            RefreshDownloadTasks();
            SetStatus("历史任务已清空");
        }
    }
}
