using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.6.0（F-1603）：模组更新预览对话框。
    /// 列出有更新候选的模组，勾选要更新的项（自动安全项默认勾选，
    /// 存在兼容性/依赖/冲突提示的项默认不勾选），确认后返回所选 ModId 列表。
    /// </summary>
    public sealed class UpdatePreviewDialog : Form
    {
        private readonly ListView _list;
        private readonly List<ModUpdateCandidate> _candidates;

        /// <summary>用户勾选确认更新的 ModId 列表（ShowDialog == OK 时有效）。</summary>
        public List<string> SelectedModIds
        {
            get
            {
                List<string> result = new List<string>();
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    if (_list.Items[i].Checked)
                    {
                        ModUpdateCandidate candidate =
                            _list.Items[i].Tag as ModUpdateCandidate;
                        if (candidate != null)
                        {
                            result.Add(candidate.Entry.ModId);
                        }
                    }
                }
                return result;
            }
        }

        public UpdatePreviewDialog(ModUpdatePlan plan)
        {
            _candidates = new List<ModUpdateCandidate>();
            if (plan != null)
            {
                for (int i = 0; i < plan.Candidates.Count; i++)
                {
                    if (plan.Candidates[i].HasUpdate && !plan.Candidates[i].Skipped)
                    {
                        _candidates.Add(plan.Candidates[i]);
                    }
                }
            }

            Text = "模组更新预览";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 460);
            MinimumSize = new Size(640, 360);
            Font = UiTheme.FontRegular(9F);
            BackColor = UiTheme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = UiTheme.Background;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label caption = UiTheme.CreateCaptionLabel(
                "共 " + _candidates.Count + " 个可更新模组。绿色提示=可安全自动更新；黄色提示=存在兼容性/依赖/冲突注意项，默认不勾选。");
            caption.Margin = new Padding(12, 10, 12, 6);
            root.Controls.Add(caption, 0, 0);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.CheckBoxes = true;
            _list.HideSelection = false;
            _list.GridLines = false;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = UiTheme.FontRegular(9F);
            _list.Columns.Add("模组", 170);
            _list.Columns.Add("当前版本", 90);
            _list.Columns.Add("目标版本", 90);
            _list.Columns.Add("提示", 320);
            for (int i = 0; i < _candidates.Count; i++)
            {
                ModUpdateCandidate candidate = _candidates[i];
                ListViewItem item = new ListViewItem(candidate.Entry.DisplayName);
                item.SubItems.Add(string.IsNullOrEmpty(candidate.Entry.Version)
                    ? "未知" : candidate.Entry.Version);
                item.SubItems.Add(candidate.Target == null
                    ? "—" : candidate.Target.VersionNumber);
                string hint = candidate.AutoSafe ? "可安全更新" :
                    string.Join("；", candidate.Blockers.ToArray());
                item.SubItems.Add(hint);
                item.Checked = candidate.AutoSafe;
                item.Tag = candidate;
                if (candidate.AutoSafe)
                {
                    item.ForeColor = UiTheme.TextSecondary;
                }
                else
                {
                    item.ForeColor = UiTheme.Warning;
                }
                _list.Items.Add(item);
            }
            _list.Margin = new Padding(12, 0, 12, 0);
            root.Controls.Add(_list, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.WrapContents = true;  // 1.7.5（B-18）
            footer.BackColor = UiTheme.Background;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Margin = new Padding(12, 10, 12, 12);

            Button cancelButton = UiTheme.CreateSecondaryButton("取消");
            cancelButton.Width = 90;
            cancelButton.Margin = new Padding(0, 3, 0, 3);
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(cancelButton);

            Button okButton = UiTheme.CreatePrimaryButton("更新所选");
            okButton.Width = 110;
            okButton.Margin = new Padding(0, 3, 8, 3);
            okButton.Click += delegate
            {
                if (SelectedModIds.Count == 0)
                {
                    MessageBox.Show(
                        "请至少勾选一个要更新的模组。",
                        "模组更新预览",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            footer.Controls.Add(okButton);

            Label summary = UiTheme.CreateCaptionLabel("");
            summary.Margin = new Padding(2, 9, 12, 3);
            summary.Text = "勾选 " + CountChecked() + " / " + _list.Items.Count + " 个";
            _list.ItemChecked += delegate
            {
                summary.Text = "勾选 " + CountChecked() + " / " + _list.Items.Count + " 个";
            };
            footer.Controls.Add(summary);

            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private int CountChecked()
        {
            int count = 0;
            for (int i = 0; i < _list.Items.Count; i++)
            {
                if (_list.Items[i].Checked)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
