using System;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.7.0（F-1704 / F-1703）：启动页最近运行状态展示与设置页「下载与更新」配置卡。
    /// 记录来自 GameProcessExited（退出码 / 运行秒数 / 退出时间），落盘随 config 保存。
    /// </summary>
    public sealed partial class MainForm
    {
        private Label _lastRunLabel;
        private NumericUpDown _dlConcurrencyBox;
        private NumericUpDown _dlHistoryBox;
        private NumericUpDown _dlBackupBox;
        private CheckBox _dlCacheCheck;
        private CheckBox _dlResumeCheck;

        private void UpdateLastRunLabel()
        {
            if (_lastRunLabel == null || _lastRunLabel.IsDisposed)
            {
                return;
            }
            if (_config.LastGameExitCode < 0)
            {
                _lastRunLabel.Text = "尚无运行记录。";
                return;
            }
            string duration = FormatRunDuration(_config.LastGameRunSeconds);
            string outcome = _config.LastGameExitCode == 0
                ? "正常退出"
                : "异常退出（退出码 " + _config.LastGameExitCode + "）";
            string when = "";
            if (_config.LastGameExitAtUnix > 0)
            {
                try
                {
                    DateTime local = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(_config.LastGameExitAtUnix).ToLocalTime();
                    when = " · " + local.ToString("yyyy-MM-dd HH:mm");
                }
                catch
                {
                }
            }
            _lastRunLabel.Text = "上次运行：" + outcome + " · 时长 " + duration + when;
        }

        private static string FormatRunDuration(long seconds)
        {
            if (seconds <= 0)
            {
                return "—";
            }
            if (seconds < 60)
            {
                return seconds + " 秒";
            }
            long minutes = seconds / 60;
            long rest = seconds % 60;
            if (minutes < 60)
            {
                return minutes + " 分 " + rest + " 秒";
            }
            return (minutes / 60) + " 小时 " + (minutes % 60) + " 分";
        }

        /// <summary>设置页「下载与更新」卡：把 1.6.0 配置项暴露到界面。</summary>
        private void BuildDownloadSettingsCard(TableLayoutPanel scrolledContent, int row)
        {
            CardPanel card = UiTheme.CreateCard();
            card.Dock = DockStyle.Top;
            card.Margin = new Padding(0, 10, 0, 0);
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = UiTheme.CardBackground;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            grid.Controls.Add(UiTheme.CreateSectionLabel("下载与更新"), 0, 0);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, 0), 2);

            grid.Controls.Add(UiTheme.CreateFieldLabel("并发下载数"), 0, 1);
            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.Dock = DockStyle.Fill;
            row1.AutoSize = true;
            row1.WrapContents = true;  // 1.7.5（B-18）
            row1.BackColor = UiTheme.CardBackground;
            _dlConcurrencyBox = UiFactory.CreateNumeric(1, 8, 1, UiTheme.Scaled(64));
            _dlConcurrencyBox.Value = Math.Max(1, Math.Min(8, _config.DownloadMaxConcurrency));
            _dlConcurrencyBox.Margin = new Padding(0, 4, 8, 3);
            row1.Controls.Add(_dlConcurrencyBox);
            Label hint1 = UiTheme.CreateCaptionLabel("同时进行的下载任务数（1–8）。");
            hint1.Margin = new Padding(0, 9, 0, 3);
            row1.Controls.Add(hint1);
            grid.Controls.Add(row1, 1, 1);

            grid.Controls.Add(UiTheme.CreateFieldLabel("下载历史上限"), 0, 2);
            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Fill;
            row2.AutoSize = true;
            row2.WrapContents = true;
            row2.BackColor = UiTheme.CardBackground;
            _dlHistoryBox = UiFactory.CreateNumeric(10, 200, 10, UiTheme.Scaled(72));
            _dlHistoryBox.Value = _config.DownloadHistoryLimit >= 10
                ? Math.Min(200, _config.DownloadHistoryLimit) : 100;
            _dlHistoryBox.Margin = new Padding(0, 4, 8, 3);
            row2.Controls.Add(_dlHistoryBox);
            Label hint2 = UiTheme.CreateCaptionLabel("下载任务页保留的历史任务条数。");
            hint2.Margin = new Padding(0, 9, 0, 3);
            row2.Controls.Add(hint2);
            grid.Controls.Add(row2, 1, 2);

            grid.Controls.Add(UiTheme.CreateFieldLabel("模组备份保留份数"), 0, 3);
            FlowLayoutPanel row3 = new FlowLayoutPanel();
            row3.Dock = DockStyle.Fill;
            row3.AutoSize = true;
            row3.WrapContents = true;
            row3.BackColor = UiTheme.CardBackground;
            _dlBackupBox = UiFactory.CreateNumeric(1, 9, 1, UiTheme.Scaled(64));
            _dlBackupBox.Value = Math.Max(1, Math.Min(9, _config.ModUpdateBackupCount));
            _dlBackupBox.Margin = new Padding(0, 4, 8, 3);
            row3.Controls.Add(_dlBackupBox);
            Label hint3 = UiTheme.CreateCaptionLabel("模组更新时保留的旧版备份数。");
            hint3.Margin = new Padding(0, 9, 0, 3);
            row3.Controls.Add(hint3);
            grid.Controls.Add(row3, 1, 3);

            grid.Controls.Add(UiTheme.CreateFieldLabel("选项"), 0, 4);
            FlowLayoutPanel row4 = new FlowLayoutPanel();
            row4.Dock = DockStyle.Fill;
            row4.AutoSize = true;
            row4.WrapContents = true;
            row4.BackColor = UiTheme.CardBackground;
            _dlCacheCheck = new CheckBox();
            _dlCacheCheck.Text = "启用模组索引缓存";
            _dlCacheCheck.Checked = _config.ModIndexCacheEnabled;
            _dlCacheCheck.Margin = new Padding(0, 5, 16, 3);
            _dlCacheCheck.ForeColor = UiTheme.TextPrimary;
            row4.Controls.Add(_dlCacheCheck);
            _dlResumeCheck = new CheckBox();
            _dlResumeCheck.Text = "启用断点续传";
            _dlResumeCheck.Checked = _config.EnableDownloadResume;
            _dlResumeCheck.Margin = new Padding(0, 5, 3, 3);
            _dlResumeCheck.ForeColor = UiTheme.TextPrimary;
            row4.Controls.Add(_dlResumeCheck);
            grid.Controls.Add(row4, 1, 4);

            // 1.7.5（B-18）：改为 UiFactory.CreateWrappedNote。
            // 此前是 CreateCaptionLabel + Dock=Fill 放进 AutoSize 行 —— 这是主卡已明确弃用的
            // 反模式（Dock=Fill 的标签不参与行高计算，多行文本只会显示一行）。
            // CreateWrappedNote 用 AutoSize + MaximumSize 限宽自动折行，高度由行正确撑开。
            Label note = UiFactory.CreateWrappedNote(
                "改动即时生效的项：索引缓存 / 续传开关 / 备份数在保存后生效，下载并发与历史上限保存后重启生效。",
                UiTheme.CardBackground, UiTheme.TextSecondary);
            note.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(note, 0, 5);
            grid.SetColumnSpan(note, 2);
            card.Controls.Add(grid);
            scrolledContent.Controls.Add(card, 0, row);
        }

        private void ApplyDownloadSettingsFromUi()
        {
            if (_dlConcurrencyBox != null)
            {
                _config.DownloadMaxConcurrency = (int)_dlConcurrencyBox.Value;
            }
            if (_dlHistoryBox != null)
            {
                _config.DownloadHistoryLimit = (int)_dlHistoryBox.Value;
            }
            if (_dlBackupBox != null)
            {
                _config.ModUpdateBackupCount = (int)_dlBackupBox.Value;
            }
            if (_dlCacheCheck != null)
            {
                _config.ModIndexCacheEnabled = _dlCacheCheck.Checked;
            }
            if (_dlResumeCheck != null)
            {
                _config.EnableDownloadResume = _dlResumeCheck.Checked;
            }
            _downloadManager.MaxConcurrency = Math.Max(1, Math.Min(8, _config.DownloadMaxConcurrency));
            _downloadManager.HistoryLimit = _config.DownloadHistoryLimit > 0
                ? _config.DownloadHistoryLimit
                : _downloadManager.HistoryLimit;
            ModIndexService.CacheEnabled = _config.ModIndexCacheEnabled;
        }

        /// <summary>
        /// 1.7.7（附带修复）：切换页面会销毁隐藏页 ListView 的句柄，
        /// 而 ListView.Dispose() 会访问 Handle 属性；若句柄已不存在，
        /// WinForms 会在释放过程中重新 CreateHandle 并抛
        /// 「执行 CreateHandle() 时无法调用值 Dispose()」。
        ///
        /// 在用户正常关闭窗口（窗体仍存活、句柄可创建）时提前补齐，
        /// 让随后的释放只做销毁。该问题在 1.7.6 基线同样偶发，
        /// 界面冒烟测试的快速构造/释放夹具另做同样的补齐。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EnsureListViewHandles(this);
            base.OnFormClosing(e);
        }

        private static void EnsureListViewHandles(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control child = root.Controls[i];
                if (child == null || child.IsDisposed)
                {
                    continue;
                }
                ListView list = child as ListView;
                if (list != null && !list.IsHandleCreated)
                {
                    try
                    {
                        IntPtr handle = list.Handle;
                    }
                    catch
                    {
                        // 单个控件句柄创建失败不应阻止窗体释放。
                    }
                }
                EnsureListViewHandles(child);
            }
        }
    }
}
