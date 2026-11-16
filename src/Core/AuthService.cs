using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 账号集合的统一入口：增删改查、离线账号兜底、第三方登录、
    /// 以及把已保存的认证服务器主机同步进 HttpUtil 白名单。
    /// 所有修改都应通过本服务进行，确保 ID 规则与配置持久化一致。
    /// </summary>
    public static class AuthService
    {
        /// <summary>
        /// 返回排序后的账号列表（最近使用优先）。
        /// </summary>
        public static List<Account> GetAccounts(LauncherConfig config)
        {
            List<Account> result = new List<Account>();
            if (config == null || config.Accounts == null)
            {
                return result;
            }
            for (int i = 0; i < config.Accounts.Count; i++)
            {
                Account account = config.Accounts[i];
                if (account != null && !string.IsNullOrEmpty(account.Id) &&
                    !string.IsNullOrEmpty(account.UserName))
                {
                    result.Add(account);
                }
            }
            return AccountIds.SortByLastUsed(result);
        }

        public static Account GetAccountById(LauncherConfig config, string id)
        {
            if (config == null || config.Accounts == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < config.Accounts.Count; i++)
            {
                if (config.Accounts[i] != null &&
                    string.Equals(config.Accounts[i].Id, id, StringComparison.Ordinal))
                {
                    return config.Accounts[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 取当前选中的账号。纯查询：不修改 config，也不会为了"有个默认账号"凭空写入一条记录
        /// （否则每次读取都会改变配置，账号列表也会多出意料之外的条目）。
        /// 配置里没有账号时返回一个未保存的临时离线账号；需要落盘请显式调用 EnsureDefaultAccount。
        /// </summary>
        public static Account GetSelectedAccount(LauncherConfig config)
        {
            if (config == null)
            {
                return CreateOffline("Player");
            }
            List<Account> accounts = GetAccounts(config);
            if (accounts.Count == 0)
            {
                return CreateOffline(
                    string.IsNullOrEmpty(config.LastUsername) ? "Player" : config.LastUsername);
            }
            if (!string.IsNullOrEmpty(config.SelectedAccountId))
            {
                Account selected = GetAccountById(config, config.SelectedAccountId);
                if (selected != null)
                {
                    return selected;
                }
            }
            // 选中项失效时退回第一个账号；只修正内存中的选中标记，由调用方负责保存。
            config.SelectedAccountId = accounts[0].Id;
            return accounts[0];
        }

        /// <summary>
        /// 确保配置中至少存在一个可用账号并设为选中项。
        /// 仅在明确需要"初始化账号"时调用（如首次启动写入默认离线账号），
        /// 与纯查询的 GetSelectedAccount 区分开，避免查询路径产生写入副作用。
        /// </summary>
        public static Account EnsureDefaultAccount(LauncherConfig config)
        {
            if (config == null)
            {
                return CreateOffline("Player");
            }
            List<Account> valid = GetAccounts(config);
            if (valid.Count > 0)
            {
                Account selected = GetSelectedAccount(config);
                config.SelectedAccountId = selected.Id;
                return selected;
            }
            Account fallback = CreateOffline(
                string.IsNullOrEmpty(config.LastUsername) ? "Player" : config.LastUsername);
            if (config.Accounts == null)
            {
                config.Accounts = new List<Account>();
            }
            config.Accounts.Add(fallback);
            config.SelectedAccountId = fallback.Id;
            return fallback;
        }

        public static Account CreateOffline(string username)
        {
            Account account = new Account();
            account.TypeCode = (int)AccountType.Offline;
            account.UserName = username;
            account.DisplayName = username;
            account.Uuid = "";
            account.AccessToken = "";
            account.ClientToken = "";
            account.AuthServerUrl = "";
            account.Id = AccountIds.Offline(username);
            return account;
        }

        /// <summary>
        /// 更新/新增账号。离线账号按用户名、认证账号按 ID 去重。
        /// </summary>
        public static void AddOrUpdateAccount(LauncherConfig config, Account account)
        {
            if (config == null || account == null || string.IsNullOrEmpty(account.Id))
            {
                return;
            }
            if (config.Accounts == null)
            {
                config.Accounts = new List<Account>();
            }
            for (int i = 0; i < config.Accounts.Count; i++)
            {
                if (config.Accounts[i] != null &&
                    string.Equals(config.Accounts[i].Id, account.Id, StringComparison.Ordinal))
                {
                    config.Accounts[i] = account;
                    return;
                }
            }
            config.Accounts.Add(account);
        }

        public static void DeleteAccount(LauncherConfig config, string id)
        {
            if (config == null || config.Accounts == null || string.IsNullOrEmpty(id))
            {
                return;
            }
            for (int i = config.Accounts.Count - 1; i >= 0; i--)
            {
                if (config.Accounts[i] != null &&
                    string.Equals(config.Accounts[i].Id, id, StringComparison.Ordinal))
                {
                    config.Accounts.RemoveAt(i);
                }
            }
            if (string.Equals(config.SelectedAccountId, id, StringComparison.Ordinal))
            {
                config.SelectedAccountId = "";
            }
        }

        /// <summary>
        /// 第三方登录：认证成功后返回带会话令牌的账号（密码不落盘）。
        /// 认证服务器主机同时被加入 HttpUtil 白名单。
        /// </summary>
        public static async Task<Account> LoginAuthAsync(
            LauncherConfig config,
            string serverUrl,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (!AuthValidation.IsValidAuthServerUrl(normalized))
            {
                throw new ArgumentException("认证服务器地址无效：必须是 https:// 开头的合法地址。");
            }

            // 关键：必须在发请求【之前】把主机加入运行时白名单。
            // 旧实现是登录成功后才加，形成死锁——请求本身要先通过白名单校验，
            // 于是首次登录必然报"不允许的请求地址"（实测 littleskin.cn 命中此问题）。
            // 用户主动填写的地址即视为信任；仍然只在内存中放行，重启后需重新登录或
            // 由保存的预设恢复，不写进配置文件的白名单。
            HttpUtil.AddAllowedHost(AuthValidation.GetHost(normalized));

            string clientToken = YggdrasilClient.GenerateClientToken();

            YggdrasilResult result = await YggdrasilClient.AuthenticateAsync(
                normalized, username, password, clientToken, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "登录失败。");
            }

            Account account = new Account();
            account.TypeCode = (int)AccountType.Authlib;
            account.UserName = username.Trim();
            account.DisplayName = result.Name;
            account.Uuid = result.Uuid;
            account.AccessToken = result.AccessToken;
            account.ClientToken = result.ClientToken;
            account.AuthServerUrl = normalized;
            account.LastUsedUnix = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            account.Id = AccountIds.Auth(normalized, result.Name);
            return account;
        }

        /// <summary>
        /// 把配置里所有已保存的认证服务器主机加入 HttpUtil 白名单。
        /// 在启动器启动（加载配置后）与账号保存后调用。
        /// </summary>
        public static void ApplyConfigToHttp(LauncherConfig config)
        {
            if (config == null)
            {
                return;
            }
            if (config.AuthServers != null)
            {
                for (int i = 0; i < config.AuthServers.Count; i++)
                {
                    AuthServerPreset preset = config.AuthServers[i];
                    if (preset != null)
                    {
                        HttpUtil.AddAllowedHost(AuthValidation.GetHost(preset.Url));
                    }
                }
            }
            List<Account> accounts = GetAccounts(config);
            for (int i = 0; i < accounts.Count; i++)
            {
                if (accounts[i].Type == AccountType.Authlib)
                {
                    HttpUtil.AddAllowedHost(AuthValidation.GetHost(accounts[i].AuthServerUrl));
                }
            }
            // 内置便捷预设（如 LittleSkin）不会写入配置，但界面上可直接选中登录，
        // 因此也要放行；否则"选了下拉里的 LittleSkin 却登录失败"。
            HttpUtil.AddAllowedHost(AuthValidation.GetHost(
                BuiltinAuthServerPresets.LittleSkinUrl));
        }
    }
}
