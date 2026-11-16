using System;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>一个已安装模组的条目（供界面管理列表使用）。</summary>
    public sealed class InstalledMod
    {
        /// <summary>mod id（读不到时为文件名，保证界面不出现空行）。</summary>
        public string ModId;
        /// <summary>jar 完整路径。</summary>
        public string FilePath;
        /// <summary>是否处于启用状态（未启用 = 已被改名为 .disabled）。</summary>
        public bool Enabled;
        /// <summary>版本号（读不到时为空）。</summary>
        public string Version;
        /// <summary>显示名（读不到时用文件名）。</summary>
        public string DisplayName;

        public long Size;
    }

    /// <summary>
    /// 模组管理：列出 / 禁用 / 启用 / 删除已安装模组。
    ///
    /// 设计约定（用户数据安全优先）：
    /// - **不真正删除**除非用户显式确认；禁用只是把扩展名改成 `.jar.disabled`，
    ///   Fabric 只扫描 `*.jar`，因此被禁用的模组不会被加载；
    /// - 所有操作都只作用于 mods 目录内的文件，拒绝越界路径；
    /// - 读不到 fabric.mod.json 的 jar 也照常列出（可能不是 Fabric 模组），
    ///   只是显示名退化为文件名。
    /// </summary>
    public static class ModManager
    {
        /// <summary>被禁用模组的后缀。Fabric/Forge 都只加载 .jar，因此加上它即可停用。</summary>
        public const string DisabledSuffix = ".disabled";

        /// <summary>列出 mods 目录下的模组（含已禁用的）。</summary>
        public static List<InstalledMod> List(string modsDirectory)
        {
            List<InstalledMod> result = new List<InstalledMod>();
            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                return result;
            }
            string[] files;
            try
            {
                // 同时列出 .jar 与 .jar.disabled，让用户能看到被禁用的条目并恢复。
                files = InstancePaths.ListModFiles(modsDirectory);
            }
            catch
            {
                return result;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                InstalledMod mod = new InstalledMod();
                mod.FilePath = files[i];
                string name = Path.GetFileName(files[i]);
                mod.Enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                // 读取元数据时用真实 jar 路径（禁用的文件已改名，ZipFile 仍可打开）
                string id = "";
                string version = "";
                try
                {
                    using (System.IO.Compression.ZipArchive archive =
                        System.IO.Compression.ZipFile.OpenRead(files[i]))
                    {
                        System.IO.Compression.ZipArchiveEntry entry =
                            archive.GetEntry("fabric.mod.json");
                        if (entry != null && entry.Length <= 256 * 1024)
                        {
                            string json;
                            using (Stream stream = entry.Open())
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                json = reader.ReadToEnd();
                            }
                            Dictionary<string, object> root = JsonUtil.ParseObject(json);
                            id = JsonUtil.GetString(root, "id") ?? "";
                            version = JsonUtil.GetString(root, "version") ?? "";
                            string displayName = JsonUtil.GetString(root, "name") ?? "";
                            if (displayName.Length > 0)
                            {
                                mod.DisplayName = displayName;
                            }
                        }
                    }
                }
                catch
                {
                }
                if (string.IsNullOrEmpty(mod.DisplayName))
                {
                    mod.DisplayName = name;
                }
                mod.ModId = string.IsNullOrEmpty(id) ? name : id;
                mod.Version = version;
                try
                {
                    mod.Size = new FileInfo(files[i]).Length;
                }
                catch
                {
                }
                result.Add(mod);
            }
            return result;
        }

        /// <summary>
        /// 禁用模组：把 `.jar` 改名为 `.jar.disabled`。
        /// 返回操作后的新路径；失败抛异常由调用方提示。
        /// </summary>
        public static string Disable(string jarPath)
        {
            if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath))
            {
                throw new FileNotFoundException("模组文件不存在。", jarPath);
            }
            if (jarPath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return jarPath;
            }
            string target = jarPath + DisabledSuffix;
            if (File.Exists(target))
            {
                throw new InvalidOperationException("目标文件已存在：" + Path.GetFileName(target));
            }
            File.Move(jarPath, target);
            Log.Info("已禁用模组：" + Path.GetFileName(jarPath) + " → " + Path.GetFileName(target));
            return target;
        }

        /// <summary>启用模组：把 `.jar.disabled` 改回 `.jar`。返回新路径。</summary>
        public static string Enable(string disabledPath)
        {
            if (string.IsNullOrEmpty(disabledPath) || !File.Exists(disabledPath))
            {
                throw new FileNotFoundException("模组文件不存在。", disabledPath);
            }
            if (!disabledPath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return disabledPath;
            }
            string target = disabledPath.Substring(0,
                disabledPath.Length - DisabledSuffix.Length);
            if (File.Exists(target))
            {
                throw new InvalidOperationException("目标文件已存在：" + Path.GetFileName(target));
            }
            File.Move(disabledPath, target);
            Log.Info("已启用模组：" + Path.GetFileName(disabledPath) + " → " + Path.GetFileName(target));
            return target;
        }

        /// <summary>
        /// 删除模组文件。为了安全，只删除 mods 目录内、扩展名为 .jar 或 .jar.disabled 的文件。
        /// 越界路径或其它扩展名一律拒绝。
        /// </summary>
        public static void Delete(string modsDirectory, string filePath)
        {
            if (string.IsNullOrEmpty(modsDirectory) || string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("参数为空。");
            }
            string fullMods = Path.GetFullPath(modsDirectory);
            string fullFile = Path.GetFullPath(filePath);
            if (!fullFile.StartsWith(fullMods + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝删除 mods 目录之外的文件：" + fullFile);
            }
            string name = Path.GetFileName(fullFile);
            bool allowed = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                           name.EndsWith(".jar" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (!allowed)
            {
                throw new InvalidOperationException("只能删除模组 jar 文件：" + name);
            }
            File.Delete(fullFile);
            Log.Info("已删除模组文件：" + name);
        }
    }
}
