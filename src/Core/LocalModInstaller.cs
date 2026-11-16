using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 从本地 JAR 文件读到的 Mod 元数据。
    /// </summary>
    public sealed class LocalModInfo
    {
        /// <summary>fabric.mod.json 里的 id（模组标识）。读不到时为空串。</summary>
        public string ModId;
        /// <summary>模组版本号。</summary>
        public string Version;
        /// <summary>模组显示名。</summary>
        public string DisplayName;
        /// <summary>必需前置（fabric.mod.json 的 depends 字段）。</summary>
        public List<string> Depends = new List<string>();
        /// <summary>来源文件名。</summary>
        public string FileName;

        /// <summary>是否是可识别的 Fabric 模组（有 fabric.mod.json 且有 id）。</summary>
        public bool IsFabricMod
        {
            get { return !string.IsNullOrEmpty(ModId); }
        }

        /// <summary>给用户看的简短描述。</summary>
        public string Describe()
        {
            if (!IsFabricMod)
            {
                return FileName + "（未找到 fabric.mod.json，可能是非 Fabric 模组）";
            }
            string result = DisplayName;
            if (string.IsNullOrEmpty(result))
            {
                result = ModId;
            }
            if (!string.IsNullOrEmpty(Version))
            {
                result += " " + Version;
            }
            return result;
        }
    }

    /// <summary>
    /// 安装「本地 Mod JAR」——用户从别处拿到的模组文件目前无处安放。
    ///
    /// 为什么需要这个功能：
    /// 启动器原本只支持从 Modrinth 安装。用户从整合包、从朋友那里、从
    /// 手动下载拿到一个 .jar 之后，只能自己去 mods 目录里放，版本是否兼容、
    /// 前置是否齐全都无从检查。
    ///
    /// 安全边界（比「整合包导入」严格得多，因为只涉及单个 jar 文件）：
    /// - 只接受**单个**本地 .jar 文件，不解压、不执行、不递归目录；
    /// - 文件名必须是单段合法名（拒绝子目录、`..`、隐藏文件、非法字符）；
    /// - 目标路径用 `PathUtil.SafeCombine` 确认仍在 mods 目录内；
    /// - 只**读取** jar 内的 fabric.mod.json（不加载类、不执行代码）；
    /// - 已存在同名文件时**不覆盖**，交给调用方决定。
    ///
    /// 与整合包导入的差别：这里完全不涉及 overrides/ 目录、server.properties、
    /// ops.json 等配置文件，因此没有"整合包注入启动命令"这一类风险。
    /// </summary>
    public static class LocalModInstaller
    {
        /// <summary>单个 jar 内 fabric.mod.json 的读取上限，防止异常 jar 耗尽内存。</summary>
        private const int MaxMetadataBytes = 256 * 1024;

        /// <summary>
        /// 读取本地 jar 的 Mod 元数据。文件不是合法 ZIP 或没有 fabric.mod.json 时
        /// 仍返回对象（IsFabricMod 为 false），由调用方决定是否拒绝。
        /// </summary>
        public static LocalModInfo ReadInfo(string jarPath)
        {
            LocalModInfo info = new LocalModInfo();
            if (string.IsNullOrEmpty(jarPath))
            {
                return info;
            }
            info.FileName = Path.GetFileName(jarPath);
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(jarPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("fabric.mod.json");
                    if (entry == null || entry.Length > MaxMetadataBytes)
                    {
                        return info;
                    }
                    string json;
                    using (Stream stream = entry.Open())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                    Dictionary<string, object> root = JsonUtil.ParseObject(json);
                    info.ModId = JsonUtil.GetString(root, "id") ?? "";
                    info.Version = JsonUtil.GetString(root, "version") ?? "";
                    info.DisplayName = JsonUtil.GetString(root, "name") ?? "";
                    ReadDepends(root, info.Depends);
                }
            }
            catch
            {
                // 读不到元数据不是致命错误：调用方可以仍然允许安装，
                // 只是提示"未识别为 Fabric 模组"。
            }
            return info;
        }

        private static void ReadDepends(Dictionary<string, object> root, List<string> result)
        {
            System.Collections.IList depends = JsonUtil.GetArray(root, "depends");
            if (depends == null)
            {
                return;
            }
            for (int i = 0; i < depends.Count; i++)
            {
                object item = depends[i];
                if (item == null)
                {
                    continue;
                }
                string id = item as string;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                id = id.Trim();
                if (id.Length == 0 || id.Length > 128)
                {
                    continue;
                }
                result.Add(id);
            }
        }

        /// <summary>
        /// 校验一个本地 jar 路径是否可以作为「本地 Mod」安装。
        /// 返回空串表示通过；否则返回给用户看的原因。
        /// </summary>
        public static string ValidateJarPath(string jarPath)
        {
            if (string.IsNullOrEmpty(jarPath))
            {
                return "没有选择文件。";
            }
            if (!File.Exists(jarPath))
            {
                return "文件不存在：" + jarPath;
            }
            string name = Path.GetFileName(jarPath);
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return "只支持 .jar 文件（当前是 " + name + "）。";
            }
            if (!ModrinthService.IsSafeModFileName(name))
            {
                return "文件名不安全或过长：" + name;
            }
            try
            {
                FileInfo info = new FileInfo(jarPath);
                if (info.Length <= 0)
                {
                    return "文件是空的：" + name;
                }
                if (info.Length > 256L * 1024L * 1024L)
                {
                    return "文件过大（超过 256MB），不像正常的模组文件。";
                }
            }
            catch (Exception ex)
            {
                return "无法读取文件信息：" + ex.Message;
            }
            return "";
        }

        /// <summary>
        /// 把本地 jar 复制进 mods 目录。
        ///
        /// 目标文件名：沿用源文件名（用户多半已经按 Modrinth 的规范命名）。
        /// 若 mods 目录里已有同名文件，则在扩展名后加 "-2"、"-3"… 直到不冲突，
        /// **绝不覆盖**用户的已有文件。
        ///
        /// 返回最终写入的文件名。
        /// </summary>
        public static string Install(string modsDirectory, string jarPath)
        {
            string problem = ValidateJarPath(jarPath);
            if (problem.Length > 0)
            {
                throw new InvalidOperationException(problem);
            }
            if (string.IsNullOrEmpty(modsDirectory))
            {
                throw new ArgumentNullException("modsDirectory");
            }
            Directory.CreateDirectory(modsDirectory);

            string sourceName = Path.GetFileName(jarPath);
            string targetName = sourceName;
            int suffix = 2;
            while (File.Exists(Path.Combine(modsDirectory, targetName)))
            {
                // 用 GetFileNameWithoutExtension 去掉 .jar 再加序号，
                // 避免出现 "my-mod-1.0-2.jar" 这种把版本号也拆开的名字。
                targetName = Path.GetFileNameWithoutExtension(sourceName) +
                             "-" + suffix.ToString() + ".jar";
                suffix++;
                if (suffix > 999)
                {
                    throw new InvalidOperationException("mods 目录里同名文件过多，无法安装。");
                }
            }

            // SafeCombine 保证目标一定在 mods 目录内（文件名已做安全校验）。
            string targetPath = PathUtil.SafeCombine(modsDirectory, targetName);

            // 先复制到临时文件再原子改名，避免中途失败留下半个 jar。
            string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.Copy(jarPath, tempPath, true);
                PathUtil.ReplaceFile(tempPath, targetPath);
            }
            finally
            {
                PathUtil.TryDeleteFile(tempPath);
            }
            Log.Info("已安装本地 Mod：" + targetName + "（来源 " + sourceName + "）");
            return targetName;
        }

        /// <summary>
        /// 列出本地 jar 里缺少的必需前置（在已安装的模组中查找）。
        /// 纯函数，便于离线测试。
        /// </summary>
        public static List<string> FindMissingDependencies(
            LocalModInfo info, List<string> installedModIds)
        {
            List<string> missing = new List<string>();
            if (info == null || info.Depends == null)
            {
                return missing;
            }
            for (int i = 0; i < info.Depends.Count; i++)
            {
                string required = info.Depends[i];
                if (string.IsNullOrEmpty(required))
                {
                    continue;
                }
                bool found = false;
                if (installedModIds != null)
                {
                    for (int j = 0; j < installedModIds.Count; j++)
                    {
                        if (string.Equals(installedModIds[j], required,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    missing.Add(required);
                }
            }
            return missing;
        }
    }
}
