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

        private const string FallbackVersion = "1.4.3";

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
                if (IsPortable)
                {
                    return Path.Combine(BaseDirectory, "data");
                }
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LauncherName);
            }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(DataDirectory, "config.json"); }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(DataDirectory, "logs"); }
        }

        /// <summary>
        /// 默认游戏目录为启动器所在目录下的 .minecraft，便于整个启动器随文件夹移动。
        /// 用户仍可在设置中改成其他目录。
        /// </summary>
        public static string DefaultGameDirectory
        {
            get { return Path.Combine(BaseDirectory, ".minecraft"); }
        }

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
        }
    }
}
