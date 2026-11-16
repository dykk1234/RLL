using System;
using System.Threading;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.7.0（F-1702）：微软正版登录（可选）的界面处理。
    /// 账号页「添加微软账号」+ 设置页 client_id 配置。流程与失败提示在
    /// MicrosoftLoginDialog 内完成；本文件负责把成功结果落成受保护凭证账号。
    /// </summary>
    public sealed partial class MainForm
    {
        private Button _microsoftLoginButton;
        private TextBox _msClientIdBox;
        private Label _msStatusLabel;
        private Button _msLoginButton;

        private void BuildMicrosoftCard(TableLayoutPanel scrolledContent, int row)
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
            for (int i = 0; i < 4; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            grid.Controls.Add(UiTheme.CreateSectionLabel(
                "微软正版登录（测试版）"), 0, 0);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, 0), 2);

            grid.Controls.Add(UiTheme.CreateFieldLabel("Azure 应用 Client ID"), 0, 1);
            _msClientIdBox = new TextBox();
            _msClientIdBox.Dock = DockStyle.Fill;
            _msClientIdBox.Margin = new Padding(0, 4, 8, 3);
            _msClientIdBox.Text = _config.MicrosoftClientId ?? "";
            _msClientIdBox.MaxLength = 256;
            UiTheme.StyleTextBox(_msClientIdBox);
            grid.Controls.Add(_msClientIdBox, 1, 1);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Fill;
            row2.AutoSize = true;
            row2.WrapContents = true;  // 1.7.5（B-18）
            row2.BackColor = UiTheme.CardBackground;
            row2.Margin = new Padding(0, 4, 0, 2);
            _msLoginButton = UiTheme.CreateSecondaryButton("开始微软登录");
            _msLoginButton.Margin = new Padding(0, 3, 8, 3);
            _msLoginButton.Click += MicrosoftLoginClick;
            row2.Controls.Add(_msLoginButton);
            _msStatusLabel = UiTheme.CreateFieldLabel("");
            _msStatusLabel.AutoEllipsis = true;
            _msStatusLabel.Margin = new Padding(0, 7, 0, 3);
            row2.Controls.Add(_msStatusLabel);
            grid.Controls.Add(row2, 0, 2);
            grid.SetColumnSpan(row2, 2);

            // 1.7.5（B-18）：说明文字改用自适应折行组件；此前 Dock=Fill 的多行 Label
            // 不参与 AutoSize 行高计算，长说明只显示一行。
            Label hint = UiFactory.CreateWrappedNote(
                "需要先在 Azure 门户注册一个应用（Public client/native，平台勾选 Mobile and desktop applications，" +
                "启用 OAuth 2.0 设备码，委托 XboxLive.signin 与 offline_access），把 Application (client) ID 填到这里。" +
                "未配置或流程未跑通时不会显示登录成功。登录得到的令牌与 refresh_token 会按 1.6.0 规则加密存储。",
                UiTheme.CardBackground, UiTheme.TextSecondary);
            hint.Margin = new Padding(0, 0, 0, 4);
            grid.Controls.Add(hint, 0, 3);
            grid.SetColumnSpan(hint, 2);
            card.Controls.Add(grid);
            scrolledContent.Controls.Add(card, 0, row);
        }

        private void AddMicrosoftAccountClick(object sender, EventArgs e)
        {
            string clientId = (_config.MicrosoftClientId ?? "").Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show(this,
                    "请先在「设置」页填写 Azure 应用 Client ID（并完成应用注册），再添加微软账号。",
                    "微软登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MicrosoftLoginDialog dialog = new MicrosoftLoginDialog(clientId);
            if (dialog.ShowDialog(this) == DialogResult.OK &&
                dialog.Profile != null)
            {
                SaveMicrosoftProfile(dialog.Profile);
                RefreshAccountList();
            }
        }

        private void MicrosoftLoginClick(object sender, EventArgs e)
        {
            string clientId = (_msClientIdBox.Text ?? "").Trim();
            _config.MicrosoftClientId = clientId;
            if (string.IsNullOrEmpty(clientId))
            {
                _msStatusLabel.Text = "请先填写 Azure 应用 Client ID（设置页说明见上）。";
                return;
            }
            _msStatusLabel.Text = "正在请求设备码…";
            MicrosoftLoginDialog dialog = new MicrosoftLoginDialog(clientId);
            if (dialog.ShowDialog(this) == DialogResult.OK &&
                dialog.Profile != null)
            {
                SaveMicrosoftProfile(dialog.Profile);
                _msStatusLabel.Text = "已添加微软账号：" + dialog.Profile.Name + "（可在账号页选择）。";
            }
        }

        private void SaveMicrosoftProfile(MicrosoftAuthService.MicrosoftProfile profile)
        {
            Account account = new Account();
            account.Id = "ms|" + profile.Name;
            account.TypeCode = (int)AccountType.Microsoft;
            account.UserName = profile.Name;
            account.DisplayName = profile.Name;
            account.Uuid = AuthValidation.NormalizeUuid(profile.Uuid);
            account.AccessToken = profile.AccessToken;
            account.ClientToken = "";
            account.MicrosoftRefreshToken = profile.RefreshToken ?? "";
            account.AccessTokenProtected = CredentialStore.Protect(
                profile.AccessToken, "account-token");
            if (!string.IsNullOrEmpty(account.MicrosoftRefreshToken))
            {
                account.MicrosoftRefreshTokenProtected = CredentialStore.Protect(
                    account.MicrosoftRefreshToken, "microsoft-refresh");
            }
            account.LastUsedUnix = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            AuthService.AddOrUpdateAccount(_config, account);
            _config.SelectedAccountId = account.Id;
        }

        /// <summary>
        /// 1.7.5（B-06/B-07）：启动游戏前用 refresh_token 刷新微软访问令牌。
        ///
        /// 为什么必须在启动前做：Minecraft 服务令牌有效期很短，1.7.0 的微软链路
        /// 既没有透传 refresh_token（B-07），也从未消费它 —— 结果是"登录成功"的账号
        /// 一旦令牌过期就再也进不了正版服，且没有任何提示。
        ///
        /// 返回 false 表示无法得到可用令牌，调用方必须中止启动（不要带着过期令牌启动）。
        /// </summary>
        private bool RefreshMicrosoftTokenBeforeLaunch(Account account)
        {
            string clientId = (_config.MicrosoftClientId ?? "").Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show(
                    "该微软账号需要 Azure 应用 Client ID 才能刷新令牌，但设置页未填写。\r\n" +
                    "请到「设置」页填写 Client ID，或改用离线账号启动。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(PageIndex.Settings);
                return false;
            }
            if (string.IsNullOrEmpty(account.MicrosoftRefreshToken))
            {
                MessageBox.Show(
                    "该微软账号没有可用的 refresh_token（1.7.4 及更早版本不会保存它）。\r\n" +
                    "请到账号页重新登录微软账号后再启动。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(PageIndex.Account);
                return false;
            }
            try
            {
                MicrosoftAuthService.OAuthRefreshResult refreshed =
                    new MicrosoftAuthService().TryRefreshOAuthAsync(
                        clientId, account.MicrosoftRefreshToken, CancellationToken.None)
                        .ConfigureAwait(true).GetAwaiter().GetResult();
                if (!refreshed.Success)
                {
                    MessageBox.Show(
                        "微软令牌刷新失败（refresh_token 可能已失效）。\r\n" +
                        "请到账号页重新登录微软账号后再启动。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ShowPage(PageIndex.Account);
                    return false;
                }
                MicrosoftAuthService.MicrosoftProfile profile =
                    new MicrosoftAuthService().CompleteExchangeAsync(
                        clientId, refreshed.AccessToken, refreshed.RefreshToken,
                        CancellationToken.None).ConfigureAwait(true).GetAwaiter().GetResult();
                if (profile == null || string.IsNullOrEmpty(profile.AccessToken))
                {
                    MessageBox.Show("微软令牌交换失败，请到账号页重新登录微软账号。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ShowPage(PageIndex.Account);
                    return false;
                }
                // 令牌刷新会旋转 refresh_token：必须把新的两个令牌都写回账号，
                // 否则下次启动又拿到已失效的旧值。
                account.AccessToken = profile.AccessToken;
                account.MicrosoftRefreshToken = profile.RefreshToken ?? account.MicrosoftRefreshToken;
                account.Uuid = AuthValidation.NormalizeUuid(profile.Uuid);
                account.AccessTokenProtected = CredentialStore.Protect(
                    account.AccessToken, "account-token");
                account.MicrosoftRefreshTokenProtected = CredentialStore.Protect(
                    account.MicrosoftRefreshToken, "microsoft-refresh");
                SaveWithFeedback("保存刷新后的微软令牌", CurrentPageIndex());
                Log.Info("微软令牌已刷新：" + account.GetDisplay());
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("启动前刷新微软令牌失败：" + ex.Message);
                MessageBox.Show(
                    "微软令牌刷新失败：" + ex.Message + "\r\n请到账号页重新登录微软账号后再启动。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(PageIndex.Account);
                return false;
            }
        }
    }
}
