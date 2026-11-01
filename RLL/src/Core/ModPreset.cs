using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>预设里的一个模组条目。</summary>
    public sealed class ModPresetEntry
    {
        /// <summary>Modrinth 项目 id 或 slug（导入时用它去查版本）。</summary>
        public string Slug;
        /// <summary>记录时的版本号，仅作展示与提示，不强制锁定。</summary>
        public string Version;
        /// <summary>显示名。</summary>
        public string Title;

        public ModPresetEntry()
        {
        }

        public ModPresetEntry(string slug, string version, string title)
        {
            Slug = slug;
            Version = version;
            Title = title;
        }
    }

    /// <summary>
    /// 模组预设：把当前装的模组列表导出成一个文件，发给朋友，对方导入后
    /// 就能装出几乎一模一样的模组组合。
    ///
    /// 这是一个典型的"轻量启动器"高性价比功能：Modrinth 的安装链路已经有了，
    /// 这里只补一层"清单的读写 + 批量按 slug 安装"。
    ///
    /// 文件格式：JSON（人可读、可手改、便于进版本库）。
    /// 示例：
    /// {
    ///   "Format": 1,
    ///   "MinecraftVersion": "1.20.1",
    ///   "Loader": "fabric",
    ///   "Mods": [ { "Slug": "sodium", "Version": "0.5.8", "Title": "钠 Sodium" } ]
    /// }
    /// </summary>
    public sealed class ModPreset
    {
        public const int CurrentFormat = 1;
        /// <summary>预设文件扩展名。</summary>
        public const string FileExtension = ".rllmods.json";

        public int Format = CurrentFormat;
        /// <summary>这套预设对应的 Minecraft 版本（导入时用于挑选兼容版本）。</summary>
        public string MinecraftVersion = "";
        /// <summary>加载器类型，当前只支持 fabric。</summary>
        public string Loader = "fabric";
        public List<ModPresetEntry> Mods = new List<ModPresetEntry>();

        public ModPreset()
        {
        }

        /// <summary>
        /// 从 mods 目录里已安装的模组生成预设。
        /// 只收录能读出 fabric.mod.json 的模组（读不出 id 的不进清单）。
        /// </summary>
        public static ModPreset FromModsDirectory(string modsDirectory, string minecraftVersion)
        {
            ModPreset preset = new ModPreset();
            preset.MinecraftVersion = minecraftVersion ?? "";
            string[] files = InstancePaths.ListModFiles(modsDirectory);
            for (int i = 0; i < files.Length; i++)
            {
                string id = ModDuplicates.ReadModId(files[i]);
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                string version = "";
                string title = "";
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
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                json = reader.ReadToEnd();
                            }
                            Dictionary<string, object> root = JsonUtil.ParseObject(json);
                            version = JsonUtil.GetString(root, "version") ?? "";
                            title = JsonUtil.GetString(root, "name") ?? "";
                        }
                    }
                }
                catch
                {
                }
                preset.Mods.Add(new ModPresetEntry(id, version, title));
            }
            return preset;
        }

        /// <summary>
        /// 解析预设文件内容。返回 null 表示格式无法识别。
        /// </summary>
        public static ModPreset Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                Dictionary<string, object> root = JsonUtil.ParseObject(json);
                ModPreset preset = new ModPreset();
                preset.Format = JsonUtil.GetInt(root, "Format", CurrentFormat);
                preset.MinecraftVersion = JsonUtil.GetString(root, "MinecraftVersion", "");
                preset.Loader = JsonUtil.GetString(root, "Loader", "fabric");
                System.Collections.IList mods = JsonUtil.GetArray(root, "Mods");
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        IDictionary<string, object> item = JsonUtil.AsObject(mods[i]);
                        if (item == null)
                        {
                            continue;
                        }
                        string slug = JsonUtil.GetString(item, "Slug");
                        if (string.IsNullOrEmpty(slug))
                        {
                            continue;
                        }
                        preset.Mods.Add(new ModPresetEntry(
                            slug,
                            JsonUtil.GetString(item, "Version", ""),
                            JsonUtil.GetString(item, "Title", "")));
                    }
                }
                return preset;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 校验预设是否可用。返回空串表示通过；否则返回原因。
        /// </summary>
        public string Validate()
        {
            if (Format <= 0 || Format > CurrentFormat)
            {
                return "预设格式版本不受支持（" + Format + "）。";
            }
            if (!string.IsNullOrEmpty(Loader) &&
                !string.Equals(Loader, "fabric", StringComparison.OrdinalIgnoreCase))
            {
                return "预设指定的加载器是 " + Loader + "，本启动器目前只支持 fabric。";
            }
            if (Mods.Count == 0)
            {
                return "预设里没有模组。";
            }
            if (Mods.Count > 200)
            {
                return "预设里的模组数量过多（超过 200 个）。";
            }
            for (int i = 0; i < Mods.Count; i++)
            {
                string slug = Mods[i].Slug;
                if (string.IsNullOrEmpty(slug) || slug.Length > 64)
                {
                    return "预设里有无效的模组标识。";
                }
                for (int j = 0; j < slug.Length; j++)
                {
                    char c = slug[j];
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                              (c >= '0' && c <= '9') || c == '-' || c == '_';
                    if (!ok)
                    {
                        return "预设里有无效的模组标识：" + slug;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 序列化成可读的 JSON。
        /// </summary>
        public string ToJson()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\r\n");
            builder.Append("  \"Format\": ").Append(Format).Append(",\r\n");
            builder.Append("  \"MinecraftVersion\": \"").Append(Escape(MinecraftVersion)).Append("\",\r\n");
            builder.Append("  \"Loader\": \"").Append(Escape(Loader)).Append("\",\r\n");
            builder.Append("  \"Mods\": [\r\n");
            for (int i = 0; i < Mods.Count; i++)
            {
                ModPresetEntry entry = Mods[i];
                builder.Append("    { \"Slug\": \"").Append(Escape(entry.Slug)).Append("\"");
                if (!string.IsNullOrEmpty(entry.Version))
                {
                    builder.Append(", \"Version\": \"").Append(Escape(entry.Version)).Append("\"");
                }
                if (!string.IsNullOrEmpty(entry.Title))
                {
                    builder.Append(", \"Title\": \"").Append(Escape(entry.Title)).Append("\"");
                }
                builder.Append(" }");
                if (i < Mods.Count - 1)
                {
                    builder.Append(",");
                }
                builder.Append("\r\n");
            }
            builder.Append("  ]\r\n");
            builder.Append("}\r\n");
            return builder.ToString();
        }

        /// <summary>
        /// 导出到文件。返回写入的路径。
        /// </summary>
        public string ExportTo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }
            string problem = Validate();
            if (problem.Length > 0)
            {
                throw new InvalidOperationException(problem);
            }
            PathUtil.WriteAllTextAtomic(path, ToJson());
            return path;
        }

        /// <summary>
        /// 从文件读入。文件不存在或格式错误时抛出异常。
        /// </summary>
        public static ModPreset LoadFrom(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("预设文件不存在。", path);
            }
            FileInfo info = new FileInfo(path);
            if (info.Length > 4L * 1024L * 1024L)
            {
                throw new InvalidDataException("预设文件过大（超过 4MB）。");
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            ModPreset preset = Parse(json);
            if (preset == null)
            {
                throw new InvalidDataException("无法解析预设文件（不是合法的 JSON 对象）。");
            }
            string problem = preset.Validate();
            if (problem.Length > 0)
            {
                throw new InvalidDataException(problem);
            }
            return preset;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
