using System;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>一条模组冲突或缺失依赖的检查结论。</summary>
    public sealed class ModCheckItem
    {
        /// <summary>冲突 / 缺少依赖 / 需要更新。</summary>
        public string Kind;
        public string Title;
        public string Detail;
    }

    /// <summary>
    /// 模组冲突与依赖检查：在安装前给出提示，不强制阻止安装。
    ///
    /// 检查两类问题：
    /// 1. 冲突：本地 mods 目录里已有同一 Modrinth 项目（同 slug / 同 project id）的文件。
    /// 2. 缺失依赖：目标模组声明的必需前置未出现在本地文件与本次待装清单中。
    ///
    /// 判定依据是"文件名包含项目 slug"这一宽松匹配（与界面「可更新」提示一致），
    /// 因此结论只作为提示，不作为删除或阻断的依据。
    /// </summary>
    public static class ModDependencyScanner
    {
        /// <summary>
        /// 扫描待安装项目的冲突与缺失依赖。
        /// </summary>
        /// <param name="modsDirectory">游戏目录下的 mods 文件夹（可为空或不存在）。</param>
        /// <param name="installedJarNames">本次已存在/将要安装的 jar 文件名（用于判断依赖是否已满足）。</param>
        /// <param name="projectSlug">待安装项目的 slug。</param>
        /// <param name="projectId">待安装项目的 project id（可为空）。</param>
        /// <param name="requiredProjects">该版本声明的必需前置（slug 或 project id）。</param>
        public static List<ModCheckItem> Scan(
            string modsDirectory,
            IList<string> installedJarNames,
            string projectSlug,
            string projectId,
            IList<string> requiredProjects)
        {
            List<ModCheckItem> result = new List<ModCheckItem>();
            List<string> jars = CollectJarNames(modsDirectory, installedJarNames);

            // 1. 冲突：本地已有同一项目的文件。待装版本通常文件名不同，
            //    因此"同 slug 但不同文件名"意味着会与旧版本共存。
            string existing = FindProjectFile(jars, projectSlug, projectId);
            if (existing != null)
            {
                ModCheckItem conflict = new ModCheckItem();
                conflict.Kind = "冲突";
                conflict.Title = projectSlug;
                conflict.Detail = "本地已存在同名项目的模组文件：" + existing +
                                  "。继续安装可能同时加载两个版本。";
                result.Add(conflict);
            }

            // 2. 缺失依赖：必需的 project 既不在本地文件里，也不在本次待装清单里。
            if (requiredProjects != null)
            {
                for (int i = 0; i < requiredProjects.Count; i++)
                {
                    string required = requiredProjects[i];
                    if (string.IsNullOrEmpty(required))
                    {
                        continue;
                    }
                    if (FindProjectFile(jars, required, required) != null)
                    {
                        continue;
                    }
                    ModCheckItem missing = new ModCheckItem();
                    missing.Kind = "缺少依赖";
                    missing.Title = required;
                    missing.Detail = "必需前置 " + required +
                                     " 未在本地 mods 目录中找到；安装时通常会自动下载，" +
                                     "若失败请手动安装该前置。";
                    result.Add(missing);
                }
            }
            return result;
        }

        /// <summary>
        /// 把检查结论渲染成给用户看的多行文本（用于确认对话框）。
        /// </summary>
        public static string Format(IList<ModCheckItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return "";
            }
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append("检测到以下情况：\r\n");
            for (int i = 0; i < items.Count; i++)
            {
                builder.Append("\r\n· [").Append(items[i].Kind).Append("] ")
                       .Append(items[i].Title).Append("\r\n    ")
                       .Append(items[i].Detail);
            }
            builder.Append("\r\n\r\n是否继续安装？");
            return builder.ToString();
        }

        private static List<string> CollectJarNames(string modsDirectory, IList<string> extra)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(modsDirectory) && Directory.Exists(modsDirectory))
            {
                try
                {
                    // 同时包含 .jar 与 .jar.disabled：被禁用的模组依然占位，
                    // 依赖检查必须把它算进去，否则会误报"缺少前置"。
                    string[] files = InstancePaths.ListModFiles(modsDirectory);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string name = Path.GetFileName(files[i]);
                        if (seen.Add(name))
                        {
                            names.Add(name);
                        }
                    }
                }
                catch
                {
                }
            }
            if (extra != null)
            {
                for (int i = 0; i < extra.Count; i++)
                {
                    string name = extra[i];
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }
            return names;
        }

        private static string FindProjectFile(List<string> jars, string slug, string projectId)
        {
            for (int i = 0; i < jars.Count; i++)
            {
                string name = jars[i];
                if (!string.IsNullOrEmpty(slug) &&
                    name.IndexOf(slug, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return name;
                }
                // project id 是 Modrinth 的稳定 ID，部分模组文件名用它而不是 slug。
                if (!string.IsNullOrEmpty(projectId) &&
                    name.IndexOf(projectId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return name;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断 mods 目录里是否装了指定 mod（按 fabric.mod.json 的 id 匹配）。
        ///
        /// 用途：离线联机的"无效会话"提示需要区分场景——
        /// 装了 mcwifipnp（更高级联机设置）时，提醒用户在「对局域网开放」界面
        /// 把「正版验证」设为「禁用」；没装则说明原版没有该开关。
        /// 只读 jar 内的小文件，不加载类，也不执行任何代码。
        /// </summary>
        public static bool IsModInstalled(string modsDirectory, string modId)
        {
            if (string.IsNullOrEmpty(modsDirectory) || string.IsNullOrEmpty(modId) ||
                !Directory.Exists(modsDirectory))
            {
                return false;
            }
            string[] jars;
            try
            {
                jars = InstancePaths.ListModFiles(modsDirectory);
            }
            catch
            {
                return false;
            }
            for (int i = 0; i < jars.Length; i++)
            {
                try
                {
                    using (System.IO.Compression.ZipArchive archive =
                        System.IO.Compression.ZipFile.OpenRead(jars[i]))
                    {
                        System.IO.Compression.ZipArchiveEntry entry =
                            archive.GetEntry("fabric.mod.json");
                        if (entry == null)
                        {
                            continue;
                        }
                        // 限制读取大小，防止异常 jar 耗尽内存。
                        if (entry.Length > 256 * 1024)
                        {
                            continue;
                        }
                        string json;
                        using (Stream stream = entry.Open())
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            json = reader.ReadToEnd();
                        }
                        string id = JsonUtil.GetString(JsonUtil.ParseObject(json), "id");
                        if (string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // 单个 jar 损坏不应影响判断，继续看下一个。
                }
            }
            return false;
        }
    }
}
