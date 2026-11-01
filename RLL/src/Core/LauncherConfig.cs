using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    public sealed class LauncherConfig
    {
        /// <summary>
        /// 1.3.0–1.3.3 内置过的 EasyTier 公共节点地址。实测已 NXDOMAIN 失效，
        /// 载入旧配置时清空，避免用户继续用坏地址。
        /// </summary>
        public const string LegacyDeadPublicNode = "tcp://public.easytier.top:11010";

        public string GameDir { get; set; }
        public string JavaPath { get; set; }
        public int MinMemoryMB { get; set; }
        public int MaxMemoryMB { get; set; }
        public string JvmArgs { get; set; }
        public int DownloadThreads { get; set; }
        public string LastVersion { get; set; }
        public string LastUsername { get; set; }
        public bool ShowSnapshots { get; set; }
        public bool CustomResolution { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        /// <summary>深色主题。默认 false，与旧版视觉保持一致。</summary>
        public bool DarkTheme { get; set; }
        /// <summary>已保存的账号列表（离线 + 第三方认证）。会话令牌可持久化，密码绝不持久化。</summary>
        public List<Account> Accounts { get; set; }
        /// <summary>当前选中的账号 ID（Accounts 中某项的 Id）。</summary>
        public string SelectedAccountId { get; set; }
        /// <summary>用户保存的认证服务器预设（名称 + API 根地址）。</summary>
        public List<AuthServerPreset> AuthServers { get; set; }
        /// <summary>authlib-injector JAR 路径；为空时使用数据目录默认路径。</summary>
        public string AuthlibInjectorPath { get; set; }

        /// <summary>启用异地联机（EasyTier）。启动游戏时自动加入网络。</summary>
        public bool EasyTierEnabled { get; set; }
        /// <summary>EasyTier 网络名（1-64 字符，仅字母数字与 -_）。种子网络靠它组成。</summary>
        public string EasyTierNetworkName { get; set; }
        /// <summary>EasyTier 网络密钥。明文存储：它是网络的准入凭证，需如实告知用户。</summary>
        public string EasyTierNetworkSecret { get; set; }
        /// <summary>公共节点地址；为空时使用内置默认节点。</summary>
        public string EasyTierPublicNode { get; set; }
        /// <summary>easytier-core.exe 路径；为空时使用数据目录下的默认路径。</summary>
        public string EasyTierBinaryPath { get; set; }

        /// <summary>下载源：official（默认）/ bmclapi / 自定义 URL。仅影响资源下载，不影响 Modrinth API。</summary>
        public string DownloadMirror { get; set; }

        /// <summary>版本隔离：每个版本使用独立存档/配置/模组目录。</summary>
        public bool IsolateVersions { get; set; }

        public LauncherConfig()
        {
            GameDir = AppPaths.DefaultGameDirectory;
            JavaPath = "";
            MinMemoryMB = 1024;
            MaxMemoryMB = 4096;
            JvmArgs = "";
            DownloadThreads = 8;
            LastVersion = "";
            LastUsername = "Player";
            ShowSnapshots = false;
            CustomResolution = false;
            WindowWidth = 854;
            WindowHeight = 480;
            DarkTheme = false;
            Accounts = new List<Account>();
            SelectedAccountId = "";
            AuthServers = new List<AuthServerPreset>();
            AuthlibInjectorPath = "";
            EasyTierEnabled = false;
            EasyTierNetworkName = "";
            EasyTierNetworkSecret = "";
            EasyTierPublicNode = "";
            EasyTierBinaryPath = "";
            DownloadMirror = "official";
            IsolateVersions = false;
        }

        public static LauncherConfig Load()
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                return LoadFrom(AppPaths.ConfigFile);
            }

            // 首次以 RLL 名称启动时，尽量保留旧版设置；旧版默认目录改为
            // 当前程序目录，用户自定义目录则保持不变。
            string legacyPath = AppPaths.LegacyConfigFile;
            if (File.Exists(legacyPath))
            {
                LauncherConfig migrated = LoadFrom(legacyPath);
                string legacyDefault = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".minecraft");
                if (string.Equals(
                    PathUtil.NormalizeDirectory(migrated.GameDir, ""),
                    PathUtil.NormalizeDirectory(legacyDefault, ""),
                    StringComparison.OrdinalIgnoreCase))
                {
                    migrated.GameDir = AppPaths.DefaultGameDirectory;
                }
                Log.Info("已迁移旧版 LiteLauncher 配置到 " + AppPaths.ConfigFile);
                // 立即持久化，使迁移严格"一次性"：否则在用户首次保存设置之前，
                // 每次启动都会重新迁移并重复记录日志。
                try
                {
                    migrated.Save();
                }
                catch (Exception ex)
                {
                    Log.Warn("迁移后的配置写入失败，将在下次启动重试：" + ex.Message);
                }
                return migrated;
            }

            LauncherConfig config = new LauncherConfig();
            config.Normalize();
            return config;
        }

        public static LauncherConfig LoadFrom(string path)
        {
            LauncherConfig config = new LauncherConfig();
            if (!File.Exists(path))
            {
                config.Normalize();
                return config;
            }
            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, object> root = JsonUtil.ParseObject(json);
                config.GameDir = JsonUtil.GetString(root, "GameDir", config.GameDir);
                config.JavaPath = JsonUtil.GetString(root, "JavaPath", config.JavaPath);
                config.MinMemoryMB = JsonUtil.GetInt(root, "MinMemoryMB", config.MinMemoryMB);
                config.MaxMemoryMB = JsonUtil.GetInt(root, "MaxMemoryMB", config.MaxMemoryMB);
                config.JvmArgs = JsonUtil.GetString(root, "JvmArgs", config.JvmArgs);
                config.DownloadThreads = JsonUtil.GetInt(root, "DownloadThreads", config.DownloadThreads);
                config.LastVersion = JsonUtil.GetString(root, "LastVersion", config.LastVersion);
                config.LastUsername = JsonUtil.GetString(root, "LastUsername", config.LastUsername);
                config.ShowSnapshots = JsonUtil.GetBool(root, "ShowSnapshots", config.ShowSnapshots);
                config.CustomResolution = JsonUtil.GetBool(root, "CustomResolution", config.CustomResolution);
                config.WindowWidth = JsonUtil.GetInt(root, "WindowWidth", config.WindowWidth);
                config.WindowHeight = JsonUtil.GetInt(root, "WindowHeight", config.WindowHeight);
                config.DarkTheme = JsonUtil.GetBool(root, "DarkTheme", config.DarkTheme);
                config.Accounts = ReadAccounts(root);
                config.SelectedAccountId = JsonUtil.GetString(root, "SelectedAccountId", "");
                config.AuthServers = ReadAuthServers(root);
                config.AuthlibInjectorPath = JsonUtil.GetString(root, "AuthlibInjectorPath", "");
                config.EasyTierEnabled = JsonUtil.GetBool(root, "EasyTierEnabled", false);
                config.EasyTierNetworkName = JsonUtil.GetString(root, "EasyTierNetworkName", "");
                config.EasyTierNetworkSecret = JsonUtil.GetString(root, "EasyTierNetworkSecret", "");
                config.EasyTierPublicNode = JsonUtil.GetString(root, "EasyTierPublicNode", "");
                config.EasyTierBinaryPath = JsonUtil.GetString(root, "EasyTierBinaryPath", "");
                config.DownloadMirror = JsonUtil.GetString(root, "DownloadMirror", "official");
                config.IsolateVersions = JsonUtil.GetBool(root, "IsolateVersions", false);
            }
            catch (Exception ex)
            {
                Log.Warn("读取配置失败，使用默认配置：" + ex.Message);
            }
            config.Normalize();
            return config;
        }

        private static List<Account> ReadAccounts(Dictionary<string, object> root)
        {
            List<Account> result = new List<Account>();
            IList list = JsonUtil.GetArray(root, "Accounts");
            if (list == null)
            {
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(list[i]);
                if (item == null)
                {
                    continue;
                }
                try
                {
                    Account account = new Account();
                    account.Id = JsonUtil.GetString(item, "Id");
                    account.TypeCode = (int)JsonUtil.GetLong(item, "TypeCode", 0);
                    account.UserName = JsonUtil.GetString(item, "UserName");
                    account.DisplayName = JsonUtil.GetString(item, "DisplayName");
                    account.Uuid = JsonUtil.GetString(item, "Uuid");
                    // 旧版本可能存下了无连字符的 UUID（部分认证服务器返回 32 位形式），
                    // 载入时统一规范化，避免老账号一直提示"UUID 无效"。
                    // 离线账号的 Uuid 为空，NormalizeUuid 会原样返回空串。
                    if (account.TypeCode == (int)AccountType.Authlib)
                    {
                        string normalizedUuid = AuthValidation.NormalizeUuid(account.Uuid);
                        if (normalizedUuid.Length > 0)
                        {
                            account.Uuid = normalizedUuid;
                        }
                    }
                    account.AccessToken = JsonUtil.GetString(item, "AccessToken");
                    account.ClientToken = JsonUtil.GetString(item, "ClientToken");
                    account.AuthServerUrl = JsonUtil.GetString(item, "AuthServerUrl");
                    account.LastUsedUnix = JsonUtil.GetLong(item, "LastUsedUnix", 0);
                    if (!string.IsNullOrEmpty(account.Id) && !string.IsNullOrEmpty(account.UserName))
                    {
                        result.Add(account);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("跳过无法解析的账号条目：" + ex.Message);
                }
            }
            return result;
        }

        private static List<AuthServerPreset> ReadAuthServers(Dictionary<string, object> root)
        {
            List<AuthServerPreset> result = new List<AuthServerPreset>();
            IList list = JsonUtil.GetArray(root, "AuthServers");
            if (list == null)
            {
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(list[i]);
                if (item == null)
                {
                    continue;
                }
                AuthServerPreset preset = new AuthServerPreset();
                preset.Name = JsonUtil.GetString(item, "Name");
                preset.Url = JsonUtil.GetString(item, "Url");
                if (!string.IsNullOrEmpty(preset.Name) && !string.IsNullOrEmpty(preset.Url))
                {
                    result.Add(preset);
                }
            }
            return result;
        }

        public void Save()
        {
            SaveTo(AppPaths.ConfigFile);
        }

        public void SaveTo(string path)
        {
            Normalize();
            string json = JsonUtil.Serialize(this);
            PathUtil.WriteAllTextAtomic(path, json);
            // 配置里包含第三方认证服务器的会话令牌，因此收紧文件权限：
            // 只允许当前用户与其所属身份访问，避免同机其他账户读取令牌。
            PathUtil.RestrictFileToCurrentUser(path);
        }

        public void Normalize()
        {
            GameDir = PathUtil.NormalizeDirectory(GameDir, AppPaths.DefaultGameDirectory);
            JavaPath = (JavaPath ?? "").Trim();
            if (JavaPath.Length > 0)
            {
                try
                {
                    JavaPath = Path.GetFullPath(JavaPath);
                }
                catch
                {
                    JavaPath = "";
                }
            }

            MinMemoryMB = Clamp(MinMemoryMB, 512, 65536);
            MaxMemoryMB = Clamp(MaxMemoryMB, 512, 65536);
            if (MaxMemoryMB < MinMemoryMB)
            {
                MaxMemoryMB = MinMemoryMB;
            }
            DownloadThreads = Clamp(DownloadThreads, 1, 16);
            JvmArgs = (JvmArgs ?? "").Trim();
            if (JvmArgs.Length > 8192)
            {
                JvmArgs = JvmArgs.Substring(0, 8192);
            }
            LastVersion = (LastVersion ?? "").Trim();
            if (LastVersion.Length > 0 && !PathUtil.IsValidVersionId(LastVersion))
            {
                LastVersion = "";
            }
            LastUsername = (LastUsername ?? "").Trim();
            if (!CommandLine.IsValidUsername(LastUsername))
            {
                LastUsername = "Player";
            }
            WindowWidth = Clamp(WindowWidth, 320, 7680);
            WindowHeight = Clamp(WindowHeight, 240, 4320);

            NormalizeAccounts();
            NormalizeAuthServers();
            NormalizeAuthlibInjectorPath();
            NormalizeEasyTier();
            NormalizeDownloadMirror();
        }

        /// <summary>
        /// 异地联机配置清理：网络名限制字符集与长度（它同时用于组网标识，
        /// 非法字符会让核心进程拒绝启动）；公共节点为空时回落到内置默认节点。
        /// 开关在缺少网络名时自动关闭，避免"勾选但无法加入"的半配置状态。
        /// </summary>
        private void NormalizeEasyTier()
        {
            EasyTierNetworkName = (EasyTierNetworkName ?? "").Trim();
            EasyTierNetworkSecret = (EasyTierNetworkSecret ?? "").Trim();
            EasyTierBinaryPath = (EasyTierBinaryPath ?? "").Trim();
            if (EasyTierBinaryPath.Length > 0)
            {
                try
                {
                    EasyTierBinaryPath = Path.GetFullPath(EasyTierBinaryPath);
                }
                catch
                {
                    EasyTierBinaryPath = "";
                }
            }

            if (EasyTierNetworkName.Length > 64)
            {
                EasyTierNetworkName = EasyTierNetworkName.Substring(0, 64);
            }
            for (int i = 0; i < EasyTierNetworkName.Length; i++)
            {
                char c = EasyTierNetworkName[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok)
                {
                    EasyTierNetworkName = "";
                    break;
                }
            }
            if (EasyTierNetworkSecret.Length > 128)
            {
                EasyTierNetworkSecret = EasyTierNetworkSecret.Substring(0, 128);
            }

            // 公共节点：空值时填实测可用的默认节点（见 EasyTierService.DefaultPublicNode）。
            // 旧的失效地址在下方被识别并替换。
            EasyTierPublicNode = (EasyTierPublicNode ?? "").Trim();
            if (EasyTierPublicNode.Length > 200)
            {
                EasyTierPublicNode = EasyTierPublicNode.Substring(0, 200);
            }
            // 兼容旧配置：保存的是已失效的内置地址，替换为当前可用节点。
            if (string.Equals(EasyTierPublicNode, LegacyDeadPublicNode,
                StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("配置中的 EasyTier 公共节点已失效，已替换为默认节点：" + EasyTierPublicNode);
                EasyTierPublicNode = EasyTierService.DefaultPublicNode;
            }
            if (EasyTierPublicNode.Length == 0)
            {
                EasyTierPublicNode = EasyTierService.DefaultPublicNode;
            }

            // 没有网络名就无法组成网络：自动关闭开关，而不是留下一半配置。
            if (EasyTierEnabled && EasyTierNetworkName.Length == 0)
            {
                EasyTierEnabled = false;
            }
        }

        /// <summary>
        /// 下载源清理：只接受 official / bmclapi / https 自定义地址。
        /// 自定义地址必须能通过 HTTPS 主机校验，否则回落到 official。
        /// </summary>
        private void NormalizeDownloadMirror()
        {
            DownloadMirror = (DownloadMirror ?? "").Trim();
            if (DownloadMirror.Length == 0 ||
                string.Equals(DownloadMirror, "official", StringComparison.OrdinalIgnoreCase))
            {
                DownloadMirror = "official";
                return;
            }
            if (string.Equals(DownloadMirror, "bmclapi", StringComparison.OrdinalIgnoreCase))
            {
                DownloadMirror = "bmclapi";
                return;
            }
            if (!DownloadMirrors.IsValidCustomMirror(DownloadMirror))
            {
                Log.Warn("下载源地址无效，已回落到官方源：" + DownloadMirror);
                DownloadMirror = "official";
            }
        }

        /// <summary>
        /// 清理账号列表：剔除字段不完整的条目、按 Id 去重；
        /// 选中项必须仍存在，否则清空待 UI 重新选择。
        /// </summary>
        private void NormalizeAccounts()
        {
            if (Accounts == null)
            {
                Accounts = new List<Account>();
                return;
            }
            List<Account> cleaned = new List<Account>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Accounts.Count; i++)
            {
                Account account = Accounts[i];
                if (account == null || string.IsNullOrEmpty(account.Id) ||
                    string.IsNullOrEmpty(account.UserName) ||
                    !seen.Add(account.Id))
                {
                    continue;
                }
                account.UserName = (account.UserName ?? "").Trim();
                account.DisplayName = string.IsNullOrEmpty(account.DisplayName)
                    ? account.UserName
                    : account.DisplayName;
                cleaned.Add(account);
            }
            Accounts = cleaned;
            if (!string.IsNullOrEmpty(SelectedAccountId) &&
                !AccountIdExists(SelectedAccountId))
            {
                SelectedAccountId = "";
            }
        }

        private bool AccountIdExists(string id)
        {
            for (int i = 0; i < Accounts.Count; i++)
            {
                if (string.Equals(Accounts[i].Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private void NormalizeAuthServers()
        {
            if (AuthServers == null)
            {
                AuthServers = new List<AuthServerPreset>();
                return;
            }
            List<AuthServerPreset> cleaned = new List<AuthServerPreset>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < AuthServers.Count; i++)
            {
                AuthServerPreset preset = AuthServers[i];
                if (preset == null || string.IsNullOrEmpty(preset.Name))
                {
                    continue;
                }
                preset.Url = AuthValidation.NormalizeAuthServerUrl(preset.Url);
                if (!AuthValidation.IsValidAuthServerUrl(preset.Url) || !seen.Add(preset.Url))
                {
                    continue;
                }
                cleaned.Add(preset);
            }
            AuthServers = cleaned;
        }

        private void NormalizeAuthlibInjectorPath()
        {
            AuthlibInjectorPath = (AuthlibInjectorPath ?? "").Trim();
            if (AuthlibInjectorPath.Length > 0)
            {
                try
                {
                    AuthlibInjectorPath = Path.GetFullPath(AuthlibInjectorPath);
                }
                catch
                {
                    AuthlibInjectorPath = "";
                }
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}
