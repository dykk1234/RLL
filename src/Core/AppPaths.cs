using System;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 所有本地路径都集中在这里。程序不写注册表，也不读取任何远程配置来决定路径。
    /// </summary>
    public static class AppPaths
    {
        public const string LauncherName = "rll";

        /// <summary>
        /// 启动器版本。真源是 AssemblyInfo.cs 的 AssemblyFileVersion：
        /// 从程序集元数据读取，避免"版本号在三个地方各写一遍、发版时漏改"的问题。
        /// 读取失败时退回内置常量，保证测试宿主等非 exe 宿主也能正常显示。
        /// </summary>
        public static string LauncherVersion
        {
            get
            {
                try
                {
                    Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (version != null && version.Major > 0)
                    {
                        return version.ToString(3);
                    }
                }
                catch
                {
                }
                return FallbackVersion;
            }
        }

        private const string FallbackVersion = "1.7.6";

        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static bool IsPortable
        {
            get { return File.Exists(Path.Combine(BaseDirectory, "portable.txt")); }
        }

        public static string DataDirectory
        {
            get
            {
                // 1.7.5：测试宿主可把数据目录指向临时目录，避免测试写入用户真实数据目录
                // （此前 ModUpdateRollback 用例直接写 %APPDATA%\rll\modbk，跨运行残留，
                // 在受限环境里还会因无写权限而失败）。
                if (!string.IsNullOrEmpty(_dataDirectoryOverride))
                {
                    return _dataDirectoryOverride;
                }
                if (IsPortable)
                {
                    return Path.Combine(BaseDirectory, "data");
                }
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LauncherName);
            }
        }

        private static string _dataDirectoryOverride;

        /// <summary>1.7.5：仅测试宿主使用；设为 null 恢复正常解析。</summary>
        internal static void SetDataDirectoryOverride(string directory)
        {
            _dataDirectoryOverride = directory;
        }

        public static string ConfigFile
        {
            get { return Path.Combine(DataDirectory, "config.json"); }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(DataDirectory, "logs"); }
        }

        /// <summary>1.6.0（F-1601）：下载任务元数据持久化文件（不含任何凭证）。</summary>
        public static string DownloadTaskStateFile
        {
            get { return Path.Combine(DataDirectory, "download-tasks.json"); }
        }

        /// <summary>1.6.0（F-1601）：下载临时目录（更新/安装的暂存区，失败自动清理）。</summary>
        public static string DownloadTempDirectory
        {
            get { return Path.Combine(DataDirectory, "dl-tmp"); }
        }

        /// <summary>1.6.0（F-1603）：模组更新备份根目录（按实例/ModId 分层）。</summary>
        public static string ModUpdateBackupRoot
        {
            get { return Path.Combine(DataDirectory, "modbk"); }
        }

        /// <summary>某实例某模组的回滚备份目录。</summary>
        public static string ModUpdateBackupDirectory(string instanceId, string modId)
        {
            string safeInstance = PathUtil.SanitizeFileName(instanceId ?? "default");
            string safeMod = PathUtil.SanitizeFileName(modId ?? "unknown");
            return Path.Combine(ModUpdateBackupRoot, safeInstance, safeMod);
        }

        /// <summary>
        /// 默认游戏目录为启动器所在目录下的 .minecraft，便于整个启动器随文件夹移动。
        /// 用户仍可在设置中改成其他目录。
        ///
        /// 1.7.4：如果程序所在目录不可写（常见于装进 Program Files、或解压到系统盘受保护
        /// 目录），继续用它会让人人以为是"需要管理员权限才能下载"。此时自动回退到
        /// %APPDATA%\rll\.minecraft，普通权限即可写，无需提权。
        ///
        /// 1.7.5（B-03/B-05）：改为"一次启动只解析一次"并缓存结果。此前是属性里每次
        /// 访问都重新探测写盘，源码里有 4 处独立调用，探测结果偶发不同就会让同一次运行
        /// 内的默认目录前后不一致。回退原因通过 <see cref="DefaultGameDirectoryFallbackReason"/>
        /// 显式暴露，设置页据此提示用户，而不是只写一行日志。
        /// </summary>
        public static string DefaultGameDirectory
        {
            get { return ResolveDefaultGameDirectory(); }
        }

        /// <summary>
        /// 1.7.5：默认目录发生回退时的原因说明；未回退时返回空串。
        /// 设置页用它显示"程序目录不可写，当前使用 XXX"，让回退可见、可选。
        /// </summary>
        public static string DefaultGameDirectoryFallbackReason
        {
            get
            {
                ResolveDefaultGameDirectory();
                return _defaultGameDirFallbackReason;
            }
        }

        /// <summary>
        /// 1.7.5：解析默认游戏目录（惰性缓存，线程安全）。一次启动只探测一次，
        /// 其余模块消费同一结果，避免同一次运行内默认目录不一致。
        /// </summary>
        public static string ResolveDefaultGameDirectory()
        {
            lock (DefaultGameDirLock)
            {
                if (_defaultGameDirResolved)
                {
                    return _defaultGameDir;
                }
                _defaultGameDirResolved = true;
                _defaultGameDir = Path.Combine(BaseDirectory, ".minecraft");
                _defaultGameDirFallbackReason = "";
                try
                {
                    if (IsDirectoryWritable(BaseDirectory))
                    {
                        return _defaultGameDir;
                    }
                }
                catch
                {
                }

                if (IsPortable)
                {
                    // 便携模式：数据目录在 BaseDirectory\data，与程序目录同处只读位置。
                    // 回退到它只会让 EnsureDirectories() 抛异常、启动直接失败，
                    // 因此这里明确标记"便携模式要求程序目录可写"，由 Program 给出可照做的提示。
                    _defaultGameDirFallbackReason =
                        "便携模式要求程序目录可写：当前目录 " + BaseDirectory +
                        " 不可写，无法把游戏数据放在 data\\.minecraft。\r\n" +
                        "请把启动器移到可写目录，或删除 portable.txt 以改用 %APPDATA% 存储。";
                    return _defaultGameDir;
                }

                // 程序目录不可写：退到用户漫游目录，普通权限可写。
                _defaultGameDir = Path.Combine(DataDirectory, ".minecraft");
                _defaultGameDirFallbackReason =
                    "程序目录不可写（" + BaseDirectory + "），默认游戏目录已改为 " +
                    _defaultGameDir + "。" +
                    "把启动器移到可写目录后，点「恢复默认」即可改回程序目录。";
                return _defaultGameDir;
            }
        }

        /// <summary>1.7.5：清空默认目录缓存（测试用；正常流程一次启动只解析一次）。</summary>
        public static void ResetDefaultGameDirectoryCache()
        {
            lock (DefaultGameDirLock)
            {
                _defaultGameDirResolved = false;
                _defaultGameDir = null;
                _defaultGameDirFallbackReason = "";
            }
        }

        /// <summary>
        /// 1.7.5（B-05）：判断目录是否可写。用于在选定默认目录前提前发现"需要管理员"的情况，
        /// 而不是等用户开始下载才报错。目录不存在时看最近的已存在祖先目录。
        ///
        /// 判定标准改为"写入成功即视为可写"：此前 File.Delete(probe) 失败会抛异常并被
        /// catch 成"不可写"（杀软/受控文件夹访问拦截删除即触发），明明写入成功却判为不可写。
        /// 删除失败现在只做清理与日志，不改变判定；同时避免在目录里残留探针文件。
        /// </summary>
        public static bool IsDirectoryWritable(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }
            try
            {
                string probeDir = directory;
                if (!Directory.Exists(probeDir))
                {
                    // 目录还不存在：检查最近的已存在祖先，判断能否在此创建。
                    probeDir = Path.GetDirectoryName(
                        directory.TrimEnd(Path.DirectorySeparatorChar));
                    while (!string.IsNullOrEmpty(probeDir) && !Directory.Exists(probeDir))
                    {
                        probeDir = Path.GetDirectoryName(probeDir);
                    }
                    if (string.IsNullOrEmpty(probeDir))
                    {
                        return false;
                    }
                }
                string probe = Path.Combine(probeDir,
                    ".rll-write-probe-" + Guid.NewGuid().ToString("N"));
                bool wrote = false;
                try
                {
                    using (FileStream stream = new FileStream(
                        probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        16, FileOptions.DeleteOnClose))
                    {
                        stream.WriteByte((byte)'x');
                        stream.Flush();
                        wrote = true;
                    }
                }
                catch
                {
                    wrote = false;
                }
                if (!wrote)
                {
                    return false;
                }
                // DeleteOnClose 已删除文件；若因句柄延迟等原因残留，只清理、不改判定。
                try
                {
                    if (File.Exists(probe))
                    {
                        File.Delete(probe);
                    }
                }
                catch
                {
                    // 清理失败不影响"可写"判定，也不留噪声：下次探测用新文件名。
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly object DefaultGameDirLock = new object();
        private static bool _defaultGameDirResolved;
        private static string _defaultGameDir;
        private static string _defaultGameDirFallbackReason;

        /// <summary>
        /// 旧版 LiteLauncher 的配置位置，仅用于一次性迁移设置。
        /// </summary>
        public static string LegacyConfigFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LiteLauncher",
                    "config.json");
            }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(DownloadTempDirectory);
            Directory.CreateDirectory(ModUpdateBackupRoot);
        }
    }
}
