using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.7.0（F-1702）：微软设备码登录对话框。
    /// 流程：请求设备码 → 展示 user_code 并打开授权页 → 轮询令牌 →
    /// Xbox/XSTS/Minecraft 全链路。任一环节失败都在对话框内显示原因
    /// （绝不显示「登录成功」），用户可取消或重试。
    /// 成功时通过 DialogResult + <see cref="Profile"/> 返回结果。
    /// </summary>
    public sealed class MicrosoftLoginDialog : Form
    {
        private readonly MicrosoftAuthService _service = new MicrosoftAuthService();
        private readonly string _clientId;
        private readonly Label _codeLabel;
        private readonly Label _statusLabel;
        private readonly Button _openButton;
        private readonly Button _copyButton;
        private readonly Button _cancelButton;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private MicrosoftAuthService.DeviceCodeResult _device;
        private MicrosoftAuthService.MicrosoftProfile _profile;

        public MicrosoftAuthService.MicrosoftProfile Profile
        {
            get { return _profile; }
        }

        public MicrosoftLoginDialog(string clientId)
        {
            _clientId = clientId;
            Text = "微软登录（测试版）";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 230);
            BackColor = UiTheme.CardBackground;
            Font = UiTheme.FontRegular(9F);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = UiTheme.CardBackground;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.Padding = new Padding(16, 12, 16, 12);
            for (int i = 0; i < 5; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            root.Controls.Add(UiTheme.CreateSectionLabel(
                "在浏览器里完成微软授权"), 0, 0);

            _codeLabel = new Label();
            _codeLabel.Text = "…";
            _codeLabel.AutoSize = false;
            _codeLabel.TextAlign = ContentAlignment.MiddleCenter;
            _codeLabel.Dock = DockStyle.Fill;
            _codeLabel.Font = UiTheme.FontRegular(20F);
            _codeLabel.ForeColor = UiTheme.Accent;
            _codeLabel.Height = 44;
            root.Controls.Add(_codeLabel, 0, 1);

            _statusLabel = UiTheme.CreateFieldLabel(
                "正在请求设备码…\r\n请稍候（需要先配置 Azure 应用 client_id）。");
            _statusLabel.AutoEllipsis = false;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Height = 46;
            root.Controls.Add(_statusLabel, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.WrapContents = true;  // 1.7.5（B-18）
            buttons.BackColor = UiTheme.CardBackground;
            buttons.Margin = new Padding(0, 6, 0, 0);
            _openButton = UiTheme.CreatePrimaryButton("打开授权页");
            _openButton.Enabled = false;
            _openButton.Margin = new Padding(0, 3, 8, 3);
            _openButton.Click += OpenPageClick;
            buttons.Controls.Add(_openButton);
            _copyButton = UiTheme.CreateSecondaryButton("复制代码");
            _copyButton.Enabled = false;
            _copyButton.Margin = new Padding(0, 3, 8, 3);
            _copyButton.Click += CopyCodeClick;
            buttons.Controls.Add(_copyButton);
            _cancelButton = UiTheme.CreateSecondaryButton("取消");
            _cancelButton.Margin = new Padding(0, 3, 3, 3);
            _cancelButton.Click += delegate
            {
                _cts.Cancel();
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(_cancelButton);
            root.Controls.Add(buttons, 0, 3);

            Controls.Add(root);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke(new Action(StartFlow));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _cts.Cancel();
            base.OnFormClosed(e);
        }

        private async void StartFlow()
        {
            try
            {
                _device = await _service.RequestDeviceCodeAsync(_clientId, _cts.Token);
                _codeLabel.Text = _device.UserCode;
                _statusLabel.Text = "请用浏览器打开授权页（或手动访问 " +
                    _device.VerificationUri + "），输入代码 " + _device.UserCode +
                    " 完成授权。";
                _openButton.Enabled = true;
                _copyButton.Enabled = true;
                OpenAuthPage();
                MicrosoftAuthService.TokenPollResult oauthToken = await _service.PollTokenAsync(
                    _clientId, _device.DeviceCode, _device.IntervalSeconds,
                    _device.ExpiresInSeconds, _cts.Token);
                _statusLabel.Text = "微软授权成功，正在换取 Minecraft 令牌…";
                // 1.7.5 修复（B-07）：轮询结果里的 refresh_token 必须透传给交换步骤，
                // 否则它永远落盘为空、访问令牌到期后无法刷新（此前这里传的是 null）。
                _profile = await _service.CompleteExchangeAsync(
                    _clientId, oauthToken.AccessToken, oauthToken.RefreshToken, _cts.Token);
                _statusLabel.Text = "登录成功：" + _profile.Name + "（请点击“完成”）。";
                _cancelButton.Text = "完成";
                _cancelButton.Click -= delegate { };
                _cancelButton.Click += delegate
                {
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "已取消。";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "登录失败：" + ex.Message;
                _openButton.Enabled = false;
                _copyButton.Enabled = false;
            }
        }

        private void OpenAuthPage()
        {
            try
            {
                if (_device != null && !string.IsNullOrEmpty(_device.VerificationUri))
                {
                    System.Diagnostics.Process.Start(_device.VerificationUri);
                }
            }
            catch
            {
                _statusLabel.Text = "未能自动打开浏览器，请手动访问：\r\n" +
                    (_device == null ? "" : _device.VerificationUri);
            }
        }

        private void OpenPageClick(object sender, EventArgs e)
        {
            OpenAuthPage();
        }

        private void CopyCodeClick(object sender, EventArgs e)
        {
            if (_device == null)
            {
                return;
            }
            try
            {
                Clipboard.SetText(_device.UserCode ?? "");
            }
            catch
            {
            }
        }
    }
}
