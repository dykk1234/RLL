using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace LiteLauncher.Core
{
    /// <summary>一个重复的模组 id 及其占用的 jar 文件。</summary>
    public sealed class DuplicateModGroup
    {
        public string ModId;
        public List<string> Files = new List<string>();
    }

    /// <summary>
    /// 重复模组检测。
    ///
    /// 背景：同一个 mod id 在 mods 目录里出现两份（例如用户手动装了一份、
    /// 启动器又下载了一份，或文件名带方括号前缀的副本），Fabric 加载时会因为
    /// 重复 mod id 直接失败，表现为"模组无法正常加载"，且日志里往往只有一句
    /// "Duplicate mod" 之类的信息，用户很难自己定位。
    ///
    /// 这里只读 jar 内的 fabric.mod.json 取 id（不加载、不执行代码）。
    /// </summary>
    public static class ModDuplicates
    {
        /// <summary>
        /// 扫描目录，返回存在重复的 mod id 分组（按 id 分组，每组至少两个文件）。
        /// 无法读取 id 的 jar（如非 Fabric 模组、损坏文件）被忽略。
        /// </summary>
        public static List<DuplicateModGroup> Find(string modsDirectory)
        {
            List<DuplicateModGroup> result = new List<DuplicateModGroup>();
            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                return result;
            }
            string[] jars;
            try
            {
                // 同时扫描 .jar 与 .jar.disabled：被禁用的模组同样占用一个 mod id，
                // 用户禁用旧版 sodium、保留新版 sodium 时，只扫 .jar 会漏掉这个冲突，
                // 游戏照样因为重复 mod id 加载失败。
                jars = InstancePaths.ListModFiles(modsDirectory);
            }
            catch
            {
                return result;
            }

            Dictionary<string, DuplicateModGroup> groups =
                new Dictionary<string, DuplicateModGroup>(StringComparer.OrdinalIgnoreCase);
            // 按文件名排序，保证结果稳定（同一个"保留哪份"的判断可重复）
            Array.Sort(jars, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < jars.Length; i++)
            {
                string id = ReadModId(jars[i]);
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                DuplicateModGroup group;
                if (!groups.TryGetValue(id, out group))
                {
                    group = new DuplicateModGroup();
                    group.ModId = id;
                    groups[id] = group;
                }
                group.Files.Add(jars[i]);
            }

            foreach (KeyValuePair<string, DuplicateModGroup> pair in groups)
            {
                if (pair.Value.Files.Count > 1)
                {
                    result.Add(pair.Value);
                }
            }
            // 结果排序，让界面提示稳定
            result.Sort(delegate(DuplicateModGroup a, DuplicateModGroup b)
            {
                return string.Compare(a.ModId, b.ModId, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>读取 jar 内 fabric.mod.json 的 id；不是 Fabric 模组或读不到时返回空串。</summary>
        public static string ReadModId(string jarPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(jarPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("fabric.mod.json");
                    if (entry == null || entry.Length > 256 * 1024)
                    {
                        return "";
                    }
                    string json;
                    using (Stream stream = entry.Open())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        json = reader.ReadToEnd();
                    }
                    return JsonUtil.GetString(JsonUtil.ParseObject(json), "id") ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 在同一 mod id 的多个文件里挑选"应当保留"的那一个。
        /// 规则：优先保留文件名不含方括号/中文前缀的（通常是官方原始文件名，
        /// 便于日后按滑块更新时匹配），其次按字节数更大的，最后按名称序。
        /// 返回要保留的文件路径；无法判断时返回列表第一个。
        /// </summary>
        public static string PickKeepFile(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return "";
            }
            string best = files[0];
            int bestScore = -1;
            long bestSize = -1;
            for (int i = 0; i < files.Count; i++)
            {
                string name = Path.GetFileName(files[i]);
                int score = 0;
                // 文件名里出现 [ 或中文，通常是重命名副本，优先级低
                if (name.IndexOf('[') < 0 && name.IndexOf('【') < 0)
                {
                    score += 2;
                }
                bool ascii = true;
                for (int c = 0; c < name.Length; c++)
                {
                    if (name[c] > 127)
                    {
                        ascii = false;
                        break;
                    }
                }
                if (ascii)
                {
                    score += 1;
                }
                long size = -1;
                try
                {
                    size = new FileInfo(files[i]).Length;
                }
                catch
                {
                }
                if (score > bestScore || (score == bestScore && size > bestSize))
                {
                    bestScore = score;
                    bestSize = size;
                    best = files[i];
                }
            }
            return best;
        }
    }
}
