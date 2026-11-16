using System;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>一项磁盘占用统计。</summary>
    public sealed class SpaceUsageItem
    {
        /// <summary>稳定键名，用于清理时回查。</summary>
        public string Key;
        /// <summary>界面显示名。</summary>
        public string Title;
        /// <summary>占用字节数。</summary>
        public long Bytes;
        /// <summary>对应的文件/目录路径（可能为空）。</summary>
        public string Path;
        /// <summary>是否允许一键清理。</summary>
        public bool Cleanable;
        /// <summary>清理后果的说明（展示给用户）。</summary>
        public string Hint;
    }

    /// <summary>
    /// 1.5.0 轻量化：统计与清理启动器产生的各类磁盘占用。
    /// 只删除「可再生」内容（日志、崩溃报告、natives 解压缓存、@argfile 残留），
    /// 绝不触碰版本 JAR、资源索引、存档与模组。
    /// 所有删除都有 try/catch 兜底，失败只计跳过，不中断整体清理。
    /// </summary>
    public static class SpaceManager
    {
        /// <summary>@argfile 残留文件前缀（正常在启动后被界面层删除，异常退出会留下）。</summary>
        private const string ArgFilePrefix = "rll-args-";

        /// <summary>统计启动器全部占用。gameDir 为空时跳过游戏侧条目。</summary>
        public static List<SpaceUsageItem> ComputeUsage(string gameDir)
        {
            List<SpaceUsageItem> items = new List<SpaceUsageItem>();
            string dataDir = AppPaths.DataDirectory;
            string logDir = AppPaths.LogDirectory;

            items.Add(new SpaceUsageItem
            {
                Key = "launcher_logs",
                Title = "启动器日志",
                Bytes = DirectorySize(logDir),
                Path = logDir,
                Cleanable = true,
                Hint = "删除全部启动器/游戏日志（不影响游戏）。"
            });
            items.Add(new SpaceUsageItem
            {
                Key = "crash_reports",
                Title = "崩溃报告",
                Bytes = DirectorySize(Combine(gameDir, "crash-reports")),
                Path = Combine(gameDir, "crash-reports"),
                Cleanable = true,
                Hint = "删除 crash-reports 目录（下次崩溃会重新生成）。"
            });
            items.Add(new SpaceUsageItem
            {
                Key = "argfiles",
                Title = "@argfile 残留",
                Bytes = DirectoryFilesSize(dataDir, ArgFilePrefix + "*", SearchOption.TopDirectoryOnly),
                Path = dataDir,
                Cleanable = true,
                Hint = "删除启动参数缓存文件（异常退出留下的临时文件）。"
            });
            items.Add(new SpaceUsageItem
            {
                Key = "natives",
                Title = "本地库解压缓存",
                Bytes = DirectorySize(Combine(gameDir, "natives")),
                Path = Combine(gameDir, "natives"),
                Cleanable = true,
                Hint = "删除 natives 解压结果（下次启动自动重新解压）。"
            });
            items.Add(new SpaceUsageItem
            {
                Key = "easytier",
                Title = "联机核心（EasyTier）",
                Bytes = DirectorySize(Combine(dataDir, "easytier")),
                Path = Combine(dataDir, "easytier"),
                Cleanable = true,
                Hint = "删除 EasyTier 核心与 wintun（下次联机需重新下载约 6MB）。"
            });
            items.Add(new SpaceUsageItem
            {
                Key = "versions",
                Title = "版本与资源（不可清理）",
                Bytes = DirectorySize(Combine(gameDir, "versions"))
                      + DirectorySize(Combine(gameDir, "assets"))
                      + DirectorySize(Combine(gameDir, "libraries")),
                Path = gameDir,
                Cleanable = false,
                Hint = "游戏本体，清理会导致版本失效，请用「下载」页管理。"
            });
            return items;
        }

        /// <summary>按 Key 清理单项，返回释放的字节数（-1 = 不允许清理）。</summary>
        public static long Clean(string key, string gameDir)
        {
            string dataDir = AppPaths.DataDirectory;
            long freed = 0;
            switch (key)
            {
                case "launcher_logs":
                    freed = Log.PurgeAllLogs();
                    break;
                case "crash_reports":
                    freed = DeleteDirectoryContents(Combine(gameDir, "crash-reports"));
                    break;
                case "argfiles":
                    freed = DeleteMatchingFiles(dataDir, ArgFilePrefix + "*", SearchOption.TopDirectoryOnly);
                    break;
                case "natives":
                    freed = DeleteDirectoryContents(Combine(gameDir, "natives"));
                    break;
                case "easytier":
                    // 仅在核心未运行时清理，避免删掉正在使用的 exe。
                    if (EasyTierService.IsRunning())
                    {
                        return -1;
                    }
                    freed = DeleteDirectoryContents(Combine(dataDir, "easytier"));
                    break;
                default:
                    return -1;
            }
            return freed;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "未知";
            }
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            double value = bytes;
            string[] units = new string[] { "KB", "MB", "GB" };
            for (int i = 0; i < units.Length; i++)
            {
                value /= 1024.0;
                if (value < 1024 || i == units.Length - 1)
                {
                    return value.ToString("0.#") + " " + units[i];
                }
            }
            return bytes + " B";
        }

        /// <summary>目录总大小（不存在返回 0）。跳过无法访问的子项。</summary>
        public static long DirectorySize(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            long total = 0;
            try
            {
                DirectoryInfo info = new DirectoryInfo(directory);
                FileInfo[] files = info.GetFiles("*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    total += files[i].Length;
                }
            }
            catch
            {
            }
            return total;
        }

        private static long DirectoryFilesSize(string directory, string pattern, SearchOption option)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            long total = 0;
            try
            {
                FileInfo[] files = new DirectoryInfo(directory).GetFiles(pattern, option);
                for (int i = 0; i < files.Length; i++)
                {
                    total += files[i].Length;
                }
            }
            catch
            {
            }
            return total;
        }

        private static long DeleteMatchingFiles(string directory, string pattern, SearchOption option)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            long freed = 0;
            try
            {
                FileInfo[] files = new DirectoryInfo(directory).GetFiles(pattern, option);
                for (int i = 0; i < files.Length; i++)
                {
                    freed += TryDeleteFileCountBytes(files[i].FullName);
                }
            }
            catch
            {
            }
            return freed;
        }

        /// <summary>清空目录内容但保留目录本身，返回释放字节数。</summary>
        private static long DeleteDirectoryContents(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            long freed = 0;
            try
            {
                string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    freed += TryDeleteFileCountBytes(files[i]);
                }
                string[] dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories);
                // 先深后浅删除空目录。
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    try
                    {
                        Directory.Delete(dirs[i], true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return freed;
        }

        /// <summary>删除单个文件并返回其大小；失败返回 0（PathUtil.TryDeleteFile 静默吞异常，故这里自行统计）。</summary>
        private static long TryDeleteFileCountBytes(string path)
        {
            try
            {
                long size = new FileInfo(path).Length;
                PathUtil.TryDeleteFile(path);
                return File.Exists(path) ? 0 : size;
            }
            catch
            {
                return 0;
            }
        }

        private static string Combine(string basePath, string relative)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return "";
            }
            try
            {
                return Path.Combine(basePath, relative);
            }
            catch
            {
                return "";
            }
        }
    }
}
