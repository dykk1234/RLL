using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        /// <summary>
        /// 1.7.5（B-03）：GameDir 是否由用户显式设置。
        /// false 表示当前值只是"系统默认"（启动器旁或回退到 %APPDATA%），
        /// 每次启动都应重新按当前 exe 位置解析，启动器搬家即可跟随；
        /// true 表示用户主动选过目录，必须永久尊重该值。
        /// 此前无此标记，new LauncherConfig() 把当时的默认值写进 config.json 后
        /// 被 LoadFrom 读回并永久固化，搬到可写目录也不会跟回来。
        /// </summary>
        public bool GameDirExplicit { get; set; }
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
        /// <summary>EasyTier 网络密钥（内存中的明文视图；落盘走 CredentialStore 保护）。</summary>
        public string EasyTierNetworkSecret { get; set; }
        /// <summary>1.6.0（F-1604）：EasyTier 密钥的保护值（落盘 JSON；解密失败时保留原文用于重存）。</summary>
        public ProtectedValue EasyTierNetworkSecretProtected { get; set; }
        /// <summary>1.6.0（F-1604）：凭证保护格式版本（1 = DPAPI / 非 Windows 降级 Base64）。</summary>
        public int CredentialProtectionVersion { get; set; }
        /// <summary>本次加载是否发生过明文→保护迁移（用于启动后立即落盘一次）。</summary>
        public bool CredentialsMigrated { get; set; }
        /// <summary>公共节点地址；为空时使用内置默认节点。</summary>
        public string EasyTierPublicNode { get; set; }
        /// <summary>easytier-core.exe 路径；为空时使用数据目录下的默认路径。</summary>
        public string EasyTierBinaryPath { get; set; }

        /// <summary>下载源：official（默认）/ bmclapi / 自定义 URL。仅影响资源下载，不影响 Modrinth API。</summary>
        public string DownloadMirror { get; set; }

        /// <summary>版本隔离：每个版本使用独立存档/配置/模组目录。</summary>
        public bool IsolateVersions { get; set; }

        // ---- 1.5.0 轻量化：存储与日志策略 ----

        /// <summary>启动器/游戏日志保留天数（默认 14）。</summary>
        public int LogRetentionDays { get; set; }
        /// <summary>单文件轮转份数（默认 2）。</summary>
        public int LogMaxRotatedFiles { get; set; }
        /// <summary>退出时清理 @argfile 等临时残留。</summary>
        public bool CleanTempOnExit { get; set; }

        // ---- 1.5.0 联机：独立页与稳定性 ----

        /// <summary>是否开启公共节点周期测速（默认开）。</summary>
        public bool EasyTierMonitorEnabled { get; set; }
        /// <summary>联机核心意外退出时是否自动重启（默认开）。</summary>
        public bool EasyTierAutoReconnect { get; set; }

        // ---- 1.5.0 界面：主题强调色与字体缩放 ----

        /// <summary>强调色键名（blue/green/purple/orange/rose，空 = 默认蓝）。</summary>
        public string AccentColor { get; set; }
        /// <summary>界面字体缩放百分比（90–130，默认 100）。</summary>
        public int UiFontScale { get; set; }

        // ---- 1.6.0 基础能力收敛：下载中心 / 索引 / 更新回滚 / 凭证 ----

        /// <summary>统一下载中心并发上限（1–8，默认 3）。</summary>
        public int DownloadMaxConcurrency { get; set; }
        /// <summary>下载历史保留条数（默认 100）。</summary>
        public int DownloadHistoryLimit { get; set; }
        /// <summary>允许断点续传（默认开；关闭后暂停功能禁用）。</summary>
        public bool EnableDownloadResume { get; set; }
        /// <summary>ModIndex 缓存开关（默认开）。</summary>
        public bool ModIndexCacheEnabled { get; set; }
        /// <summary>1.7.0（F-1702）：微软应用 client_id（Azure 注册，Public client/native）。</summary>
        public string MicrosoftClientId { get; set; }
        /// <summary>1.7.0（F-1704）：最近一次游戏运行记录（退出码 -1 表示尚无记录）。</summary>
        public int LastGameExitCode { get; set; }
        public long LastGameRunSeconds { get; set; }
        public long LastGameExitAtUnix { get; set; }
        /// <summary>模组更新备份保留份数（默认 3）。</summary>
        public int ModUpdateBackupCount { get; set; }

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
            LogRetentionDays = 14;
            LogMaxRotatedFiles = 2;
            CleanTempOnExit = true;
            EasyTierMonitorEnabled = true;
            EasyTierAutoReconnect = true;
            AccentColor = "";
            UiFontScale = 100;
            CredentialProtectionVersion = 1;
            DownloadMaxConcurrency = 3;
            DownloadHistoryLimit = 100;
            EnableDownloadResume = true;
            ModIndexCacheEnabled = true;
            ModUpdateBackupCount = 3;
            MicrosoftClientId = "";
            LastGameExitCode = -1;
        }

        public static LauncherConfig Load()
        {
            LauncherConfig config = LoadCore();
            if (config != null && config.CredentialsMigrated)
            {
                // F-1604：旧明文配置在内存中已完成保护转换，这里立即落盘一次，
                // 保证迁移严格一次性；失败不阻塞启动，下次保存设置时重写。
                try
                {
                    config.Save();
                }
                catch (Exception ex)
                {
                    Log.Warn("凭证迁移后写入失败，将在下次保存设置时重试：" + ex.Message);
                }
            }
            return config;
        }

        private static LauncherConfig LoadCore()
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
                    migrated.GameDirExplicit = false;
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
            bool migrated = false;
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
                // 1.7.5（B-03）：区分"用户显式设置"与"系统默认"。
                // 旧配置没有 GameDirExplicit 键：若存的目录与当时的默认目录相同，
                // 视为从未显式设置（每次启动重新解析）；不同则视为用户指定。
                if (root.ContainsKey("GameDirExplicit"))
                {
                    config.GameDirExplicit = JsonUtil.GetBool(root, "GameDirExplicit", false);
                }
                else
                {
                    config.GameDirExplicit = !PathsEqual(
                        config.GameDir, AppPaths.DefaultGameDirectory);
                }
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
                config.Accounts = ReadAccounts(root, ref migrated);
                config.SelectedAccountId = JsonUtil.GetString(root, "SelectedAccountId", "");
                config.AuthServers = ReadAuthServers(root);
                config.AuthlibInjectorPath = JsonUtil.GetString(root, "AuthlibInjectorPath", "");
                config.EasyTierEnabled = JsonUtil.GetBool(root, "EasyTierEnabled", false);
                config.EasyTierNetworkName = JsonUtil.GetString(root, "EasyTierNetworkName", "");
                // F-1604：EasyTier 密钥读取（兼容旧明文，就地迁移为保护值）。
                object secretValue;
                if (root.TryGetValue("EasyTierNetworkSecret", out secretValue))
                {
                    IDictionary<string, object> secretObj = secretValue as IDictionary<string, object>;
                    if (secretObj != null)
                    {
                        ProtectedValue protectedSecret = ProtectedValue.FromJson(secretObj);
                        if (protectedSecret != null)
                        {
                            config.EasyTierNetworkSecretProtected = protectedSecret;
                            try
                            {
                                config.EasyTierNetworkSecret =
                                    CredentialStore.Unprotect(protectedSecret, "easytier");
                            }
                            catch (Exception unprotectEx)
                            {
                                // 解密失败：不覆盖密文，要求用户重新输入密钥。
                                config.EasyTierNetworkSecret = "";
                                Log.Warn("EasyTier 密钥解密失败，请重新输入：" + unprotectEx.Message);
                            }
                        }
                    }
                    else if (secretValue is string && ((string)secretValue).Length > 0)
                    {
                        config.EasyTierNetworkSecret = (string)secretValue;
                        config.EasyTierNetworkSecretProtected =
                            CredentialStore.Protect(config.EasyTierNetworkSecret, "easytier");
                        migrated = true;
                    }
                }
                config.EasyTierPublicNode = JsonUtil.GetString(root, "EasyTierPublicNode", "");
                config.EasyTierBinaryPath = JsonUtil.GetString(root, "EasyTierBinaryPath", "");
                config.DownloadMirror = JsonUtil.GetString(root, "DownloadMirror", "official");
                config.IsolateVersions = JsonUtil.GetBool(root, "IsolateVersions", false);
                config.DownloadMaxConcurrency = JsonUtil.GetInt(root, "DownloadMaxConcurrency", 3);
                config.DownloadHistoryLimit = JsonUtil.GetInt(root, "DownloadHistoryLimit", 100);
                config.EnableDownloadResume = JsonUtil.GetBool(root, "EnableDownloadResume", true);
                config.ModIndexCacheEnabled = JsonUtil.GetBool(root, "ModIndexCacheEnabled", true);
                config.ModUpdateBackupCount = JsonUtil.GetInt(root, "ModUpdateBackupCount", 3);
                config.MicrosoftClientId = JsonUtil.GetString(root, "MicrosoftClientId");
                config.LastGameExitCode = (int)JsonUtil.GetLong(root, "LastGameExitCode", -1);
                config.LastGameRunSeconds = JsonUtil.GetLong(root, "LastGameRunSeconds", 0);
                config.LastGameExitAtUnix = JsonUtil.GetLong(root, "LastGameExitAtUnix", 0);
                // 1.7.5 修复（B-01/B-02）：此前 ToJsonObject() 写了 41 个键，
                // LoadFrom() 只读回 31 个，下面这 8 项"能存不能读"，重启后一律
                // 回落到构造函数默认值（表现为"改了主题色/缩放/日志策略，重启又还原"）。
                // 新增配置字段时必须同时在 ToJsonObject() 与这里各加一行，
                // TestConfigRoundtrip 会用反射比对两个键集合，漏加即测试失败。
                config.LogRetentionDays = JsonUtil.GetInt(
                    root, "LogRetentionDays", config.LogRetentionDays);
                config.LogMaxRotatedFiles = JsonUtil.GetInt(
                    root, "LogMaxRotatedFiles", config.LogMaxRotatedFiles);
                config.CleanTempOnExit = JsonUtil.GetBool(
                    root, "CleanTempOnExit", config.CleanTempOnExit);
                config.EasyTierMonitorEnabled = JsonUtil.GetBool(
                    root, "EasyTierMonitorEnabled", config.EasyTierMonitorEnabled);
                config.EasyTierAutoReconnect = JsonUtil.GetBool(
                    root, "EasyTierAutoReconnect", config.EasyTierAutoReconnect);
                config.AccentColor = JsonUtil.GetString(
                    root, "AccentColor", config.AccentColor);
                config.UiFontScale = JsonUtil.GetInt(
                    root, "UiFontScale", config.UiFontScale);
                config.CredentialProtectionVersion = JsonUtil.GetInt(
                    root, "CredentialProtectionVersion", config.CredentialProtectionVersion);
                // 1.7.5（B-03）：未显式设置过目录时，按当前 exe 位置重新解析，
                // 让"启动器搬家即可跟随"重新成立（此前会被旧默认值永久固化）。
                if (!config.GameDirExplicit)
                {
                    config.GameDir = AppPaths.DefaultGameDirectory;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取配置失败，使用默认配置：" + ex.Message);
            }
            config.Normalize();
            config.CredentialsMigrated = migrated;
            return config;
        }

        private static List<Account> ReadAccounts(
            Dictionary<string, object> root,
            ref bool migrated)
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
                    ProtectedValue protectedAccess = null;
                    account.AccessToken = ReadProtectedToken(item, "AccessToken", ref migrated, out protectedAccess);
                    account.AccessTokenProtected = protectedAccess;
                    ProtectedValue protectedClient = null;
                    account.ClientToken = ReadProtectedToken(item, "ClientToken", ref migrated, out protectedClient);
                    account.ClientTokenProtected = protectedClient;
                    ProtectedValue protectedRefresh = null;
                    account.MicrosoftRefreshToken = ReadProtectedToken(
                        item, "MicrosoftRefreshToken", ref migrated, out protectedRefresh,
                        "microsoft-refresh");
                    account.MicrosoftRefreshTokenProtected = protectedRefresh;
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

        /// <summary>
        /// 读凭证字段：兼容受保护 JSON 与旧明文，旧明文就地转为保护值并标记迁移。
        ///
        /// 1.7.5 修复（B-07）：新增 purpose 参数。此前无论读哪个字段都硬编码
        /// "account-token" 作为 DPAPI 熵，而写入端 MicrosoftRefreshToken 用的是
        /// "microsoft-refresh"（见 WriteAccounts）。CredentialStore 的熵包含 purpose
        /// （"rll-cred-" + purpose），加解密不匹配必然抛异常，被这里的 catch 吞成空串 ——
        /// 微软 refresh_token 落盘恒空，TryRefreshOAuthAsync 永远拿不到凭据。
        /// </summary>
        private static string ReadProtectedToken(
            IDictionary<string, object> item,
            string key,
            ref bool migrated,
            out ProtectedValue protectedValue,
            string purpose = "account-token")
        {
            protectedValue = null;
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
            {
                return "";
            }
            if (value is IDictionary)
            {
                ProtectedValue parsed = ProtectedValue.FromJson(JsonUtil.AsObject(value));
                if (parsed == null)
                {
                    return "";
                }
                protectedValue = parsed;
                try
                {
                    return CredentialStore.Unprotect(parsed, purpose);
                }
                catch
                {
                    // 解密失败：保留密文用于下次重存，明文返回空（UI 提示重新登录）。
                    return "";
                }
            }
            if (value is string)
            {
                string plaintext = (string)value;
                if (plaintext.Length == 0)
                {
                    return "";
                }
                protectedValue = CredentialStore.Protect(plaintext, purpose);
                migrated = true;
                return plaintext;
            }
            return "";
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

        /// <summary>
        /// 1.7.4：尝试保存并返回失败原因（成功返回 null）。
        ///
        /// 此前 Save() 失败时异常会一路抛到 UI 线程：有的调用点用 try/catch 吞掉只记日志
        /// （主题色/缩放就是这么"点了没反应、重启又还原"的），有的根本没包 try，
        /// 直接把"访问被拒绝"当崩溃报给用户。改为调用方拿到明确结果后自行提示。
        /// </summary>
        public string TrySave()
        {
            try
            {
                SaveTo(AppPaths.ConfigFile);
                return null;
            }
            catch (Exception ex)
            {
                Exception root = WriteErrorDiagnostics.GetRootCause(ex);
                string detail = root == null ? ex.Message : root.Message;
                return detail;
            }
        }

        /// <summary>
        /// 1.7.4：配置目录当前是否可写（用于启动时提前判断，避免事后才报错）。
        ///
        /// 1.7.5（B-05）：不再自己复制一份探针逻辑，改为委托 AppPaths.IsDirectoryWritable，
        /// 全项目只保留一处判定实现（此前两份实现的行为差异正是"同一次运行内结论不一致"的来源）。
        /// </summary>
        public static bool CanWriteConfigDirectory()
        {
            return AppPaths.IsDirectoryWritable(AppPaths.DataDirectory);
        }

        /// <summary>
        /// 1.6.0（F-1604）：把配置映射为 JSON 字典，凭证字段一律写保护值。
        /// 解密失败保留原密文，避免覆盖导致永久丢失。
        /// </summary>
        public Dictionary<string, object> ToJsonObject()
        {
            Normalize();
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["GameDir"] = GameDir;
            // 1.7.5（B-03）：只有用户显式设置过目录才落盘 GameDir 的语义标记。
            // GameDir 本身始终写出（LoadFrom 需要它）；标记决定重启时是否重新解析默认值。
            obj["GameDirExplicit"] = GameDirExplicit;
            obj["JavaPath"] = JavaPath;
            obj["MinMemoryMB"] = MinMemoryMB;
            obj["MaxMemoryMB"] = MaxMemoryMB;
            obj["JvmArgs"] = JvmArgs;
            obj["DownloadThreads"] = DownloadThreads;
            obj["LastVersion"] = LastVersion;
            obj["LastUsername"] = LastUsername;
            obj["ShowSnapshots"] = ShowSnapshots;
            obj["CustomResolution"] = CustomResolution;
            obj["WindowWidth"] = WindowWidth;
            obj["WindowHeight"] = WindowHeight;
            obj["DarkTheme"] = DarkTheme;
            obj["Accounts"] = WriteAccounts(Accounts);
            obj["SelectedAccountId"] = SelectedAccountId;
            obj["AuthServers"] = WriteAuthServers(AuthServers);
            obj["AuthlibInjectorPath"] = AuthlibInjectorPath;
            obj["EasyTierEnabled"] = EasyTierEnabled;
            obj["EasyTierNetworkName"] = EasyTierNetworkName;
            obj["EasyTierNetworkSecret"] = ToProtectedJson(
                EasyTierNetworkSecretProtected, EasyTierNetworkSecret, "easytier");
            obj["EasyTierPublicNode"] = EasyTierPublicNode;
            obj["EasyTierBinaryPath"] = EasyTierBinaryPath;
            obj["DownloadMirror"] = DownloadMirror;
            obj["IsolateVersions"] = IsolateVersions;
            obj["LogRetentionDays"] = LogRetentionDays;
            obj["LogMaxRotatedFiles"] = LogMaxRotatedFiles;
            obj["CleanTempOnExit"] = CleanTempOnExit;
            obj["EasyTierMonitorEnabled"] = EasyTierMonitorEnabled;
            obj["EasyTierAutoReconnect"] = EasyTierAutoReconnect;
            obj["AccentColor"] = AccentColor;
            obj["UiFontScale"] = UiFontScale;
            obj["CredentialProtectionVersion"] = CredentialProtectionVersion;
            obj["DownloadMaxConcurrency"] = DownloadMaxConcurrency;
            obj["DownloadHistoryLimit"] = DownloadHistoryLimit;
            obj["EnableDownloadResume"] = EnableDownloadResume;
            obj["ModIndexCacheEnabled"] = ModIndexCacheEnabled;
            obj["ModUpdateBackupCount"] = ModUpdateBackupCount;
            obj["MicrosoftClientId"] = MicrosoftClientId ?? "";
            obj["LastGameExitCode"] = LastGameExitCode;
            obj["LastGameRunSeconds"] = LastGameRunSeconds;
            obj["LastGameExitAtUnix"] = LastGameExitAtUnix;
            return obj;
        }

        private static object ToProtectedJson(ProtectedValue protectedValue, string plaintext, string purpose)
        {
            if (protectedValue != null)
            {
                return protectedValue.ToJson();
            }
            if (string.IsNullOrEmpty(plaintext))
            {
                return null;
            }
            ProtectedValue fresh = CredentialStore.Protect(plaintext, purpose);
            return fresh == null ? null : fresh.ToJson();
        }

        private static IList WriteAccounts(List<Account> accounts)
        {
            List<object> list = new List<object>();
            if (accounts == null)
            {
                return list;
            }
            for (int i = 0; i < accounts.Count; i++)
            {
                Account account = accounts[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["Id"] = account.Id;
                item["TypeCode"] = (int)account.TypeCode;
                item["UserName"] = account.UserName;
                item["DisplayName"] = account.DisplayName;
                item["Uuid"] = account.Uuid;
                item["AccessToken"] = ToProtectedJson(
                    account.AccessTokenProtected, account.AccessToken, "account-token");
                item["ClientToken"] = ToProtectedJson(
                    account.ClientTokenProtected, account.ClientToken, "account-token");
                item["MicrosoftRefreshToken"] = ToProtectedJson(
                    account.MicrosoftRefreshTokenProtected, account.MicrosoftRefreshToken,
                    "microsoft-refresh");
                item["AuthServerUrl"] = account.AuthServerUrl;
                item["LastUsedUnix"] = account.LastUsedUnix;
                list.Add(item);
            }
            return list;
        }

        private static IList WriteAuthServers(List<AuthServerPreset> presets)
        {
            List<object> list = new List<object>();
            if (presets == null)
            {
                return list;
            }
            for (int i = 0; i < presets.Count; i++)
            {
                AuthServerPreset preset = presets[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["Name"] = preset.Name;
                item["Url"] = preset.Url;
                list.Add(item);
            }
            return list;
        }

        public void SaveTo(string path)
        {
            Normalize();
            // F-1604：不直接序列化 this（会把明文凭证写盘），改用显式映射，
            // 会话令牌与 EasyTier 密钥一律经 CredentialStore 保护后落盘。
            string json = JsonUtil.Serialize(ToJsonObject());
            PathUtil.WriteAllTextAtomic(path, json);
            // 配置里包含第三方认证服务器的会话令牌，因此收紧文件权限：
            // 只允许当前用户与其所属身份访问，避免同机其他账户读取令牌。
            PathUtil.RestrictFileToCurrentUser(path);
        }

        public void Normalize()
        {
            // 1.7.5（B-03）：未显式设置过目录时，每次规范化都重新解析默认目录，
            // 保证"启动器搬到可写目录后自动跟回来"，而不是继续用被固化的旧默认值。
            GameDir = GameDirExplicit
                ? PathUtil.NormalizeDirectory(GameDir, AppPaths.DefaultGameDirectory)
                : AppPaths.DefaultGameDirectory;
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

            // 1.5.0 新增配置的钳制：日志策略、界面缩放、主题色键名。
            LogRetentionDays = Clamp(LogRetentionDays, 3, 90);
            LogMaxRotatedFiles = Clamp(LogMaxRotatedFiles, 1, 10);
            UiFontScale = Clamp(UiFontScale, 90, 130);
            AccentColor = NormalizeAccentColor(AccentColor);

            NormalizeAccounts();
            NormalizeAuthServers();
            NormalizeAuthlibInjectorPath();
            NormalizeEasyTier();
            NormalizeDownloadMirror();
        }

        /// <summary>
        /// 1.7.5 修复（B-01）：主题色键名白名单。
        /// 此前只做 Trim + 小写 + 长度截断，任意字符串（如 "#ff0000"）都会被落盘，
        /// 而 UiTheme.ApplyAccent 只认 5 个键名，非法值静默退回蓝色 —— 用户存了个
        /// 界面永远不生效的值。这里统一收敛：非白名单值一律归为 ""（默认蓝）。
        /// 新增主题色时必须同时改这里与 UiTheme.ApplyAccent 的 switch。
        /// </summary>
        public static string NormalizeAccentColor(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "":
                case "blue":
                    return "";
                case "green":
                case "purple":
                case "orange":
                case "rose":
                    return key;
                default:
                    Log.Warn("忽略无效的主题色键名：" + key);
                    return "";
            }
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

        /// <summary>
        /// 1.7.5（B-03）：比较两个目录路径是否指向同一位置（忽略大小写与末尾分隔符）。
        /// 用于判断旧配置里的 GameDir 是否只是"当时的默认值"。
        /// </summary>
        internal static bool PathsEqual(string left, string right)
        {
            string a = PathUtil.NormalizeDirectory(left, "");
            string b = PathUtil.NormalizeDirectory(right, "");
            if (a.Length == 0 || b.Length == 0)
            {
                return false;
            }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
