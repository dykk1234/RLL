using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>模组元数据状态：Valid=可读 / Missing=无 fabric.mod.json / Invalid=元数据损坏或超限。</summary>
    public enum ModMetadataStatus
    {
        Valid,
        Missing,
        Invalid
    }

    /// <summary>索引中的一个依赖声明。</summary>
    public sealed class ModIndexDependency
    {
        /// <summary>依赖的 ModId（或 minecraft / fabric 等平台依赖）。</summary>
        public string Id;
        /// <summary>依赖种类：required（depends）/ optional（recommends）/ suggests / conflicts。</summary>
        public string Kind;
        /// <summary>版本范围原文（如 ">=1.20"，可能为空）。</summary>
        public string VersionRange;

        public ModIndexDependency()
        {
        }

        public ModIndexDependency(string id, string kind, string versionRange)
        {
            Id = id ?? "";
            Kind = kind ?? "required";
            VersionRange = versionRange ?? "";
        }
    }

    /// <summary>模组索引中的一个条目（每个 mods 目录下的 jar 一条）。</summary>
    public sealed class ModIndexEntry
    {
        public string InstanceId;
        public string FilePath;
        public string FileName;
        /// <summary>模组 id（优先来自 fabric.mod.json；缺失时保留文件名，不猜测）。</summary>
        public string ModId;
        public string DisplayName;
        public string Version;
        /// <summary>加载器（当前仅索引 Fabric 实例，取值 "fabric"）。</summary>
        public string Loader;
        /// <summary>目标 Minecraft 版本范围（来自 depends.minecraft，原文，可能为空）。</summary>
        public string GameVersionRange;
        public List<ModIndexDependency> Dependencies = new List<ModIndexDependency>();
        /// <summary>文件 SHA-256（用于识别内容变化；按需计算）。</summary>
        public string FileHash;
        /// <summary>是否启用（识别 .jar 与 .jar.disabled 后缀）。</summary>
        public bool Enabled;
        public ModMetadataStatus MetadataStatus;
        public long Size;
        public DateTime LastWriteTimeUtc;

        public bool IsIdentifiable
        {
            get { return MetadataStatus == ModMetadataStatus.Valid; }
        }
    }

    /// <summary>一次扫描的快照（实例隔离）。</summary>
    public sealed class ModIndexSnapshot
    {
        public string InstanceId;
        public List<ModIndexEntry> Entries = new List<ModIndexEntry>();
        public DateTime ScanTime;
        /// <summary>本次未能解析的文件名（损坏/超限），不会让整个扫描失败。</summary>
        public List<string> UnreadableFiles = new List<string>();
    }

    /// <summary>
    /// 1.6.0（F-1602）：ModIndex 统一模组索引。
    ///
    /// 单一事实来源：ModManager / ModDuplicates / ModDependencyScanner / 更新提示
    /// 应统一读取本索引，避免各模块对"模组 id / 启用状态 / 依赖"口径不一致。
    ///
    /// 缓存策略（PRD 4.2.1）：
    /// - 只在实例模组页打开时扫描，不在启动阶段无条件扫全部实例；
    /// - 缓存键 = 规范化路径 + 文件长度 + 最后修改时间；命中则不重复解析 JAR；
    /// - 禁用模组仍出现在索引中（Enabled=false），依赖/冲突计算按启用状态区分；
    /// - 损坏 JAR / 缺 fabric.mod.json 作为"未识别文件"呈现，不中断扫描。
    /// </summary>
    public static class ModIndexService
    {
        /// <summary>fabric.mod.json 最大读取字节数（防超大元数据）。</summary>
        public const int MaxMetadataBytes = 256 * 1024;

        private sealed class CachedFile
        {
            public string Signature;
            public ModIndexEntry Entry;
        }

        private sealed class CacheState
        {
            public string InstanceId;
            public string ModsDirectory;
            public Dictionary<string, CachedFile> Files = new Dictionary<string, CachedFile>();
        }

        private static readonly Dictionary<string, CacheState> _cache =
            new Dictionary<string, CacheState>(StringComparer.Ordinal);

        /// <summary>是否启用缓存（LauncherConfig.ModIndexCacheEnabled）。</summary>
        public static bool CacheEnabled = true;

        /// <summary>
        /// 扫描（或命中缓存返回）某实例的模组索引快照。
        /// instanceId 用于缓存隔离；modsDirectory 为实际 mods 目录。
        /// </summary>
        public static ModIndexSnapshot Scan(string instanceId, string modsDirectory, bool force)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = "default";
            }
            string fullDir = string.IsNullOrEmpty(modsDirectory)
                ? ""
                : Path.GetFullPath(modsDirectory);

            CacheState state;
            if (!_cache.TryGetValue(instanceId, out state) ||
                !string.Equals(state.ModsDirectory, fullDir, StringComparison.OrdinalIgnoreCase))
            {
                state = new CacheState();
                state.InstanceId = instanceId;
                state.ModsDirectory = fullDir;
                _cache[instanceId] = state;
            }

            List<string> files = new List<string>();
            if (!string.IsNullOrEmpty(fullDir) && Directory.Exists(fullDir))
            {
                try
                {
                    string[] listed = InstancePaths.ListModFiles(fullDir);
                    for (int i = 0; i < listed.Length; i++)
                    {
                        files.Add(listed[i]);
                    }
                }
                catch
                {
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            ModIndexSnapshot snapshot = new ModIndexSnapshot();
            snapshot.InstanceId = instanceId;
            snapshot.ScanTime = DateTime.Now;

            Dictionary<string, CachedFile> next = new Dictionary<string, CachedFile>(StringComparer.Ordinal);
            for (int i = 0; i < files.Count; i++)
            {
                string filePath = files[i];
                FileInfo info = null;
                try
                {
                    info = new FileInfo(filePath);
                }
                catch
                {
                    info = null;
                }
                if (info == null || !info.Exists)
                {
                    continue;
                }
                string signature = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                string key = filePath;

                CachedFile cached = null;
                // force=true 时强制重扫：忽略签名命中，重建该文件的解析结果。
                if (!force && CacheEnabled && state.Files.TryGetValue(key, out cached))
                {
                    if (string.Equals(cached.Signature, signature, StringComparison.Ordinal))
                    {
                        next[key] = cached;
                        snapshot.Entries.Add(cached.Entry);
                        continue; // 未变化：不重复解析 JAR
                    }
                }

                ModIndexEntry entry = BuildEntry(instanceId, filePath, info);
                CachedFile fresh = new CachedFile();
                fresh.Signature = signature;
                fresh.Entry = entry;
                next[key] = fresh;
                snapshot.Entries.Add(entry);
            }

            // 文件系统变化后使对应条目失效：只保留本次仍存在的文件缓存。
            state.Files = next;
            foreach (KeyValuePair<string, CachedFile> pair in state.Files)
            {
                if (pair.Value.Entry.MetadataStatus == ModMetadataStatus.Invalid)
                {
                    snapshot.UnreadableFiles.Add(pair.Value.Entry.FileName);
                }
            }
            return snapshot;
        }

        /// <summary>使某实例的索引缓存全部失效（文件操作后调用）。</summary>
        public static void Invalidate(string instanceId)
        {
            if (instanceId == null)
            {
                return;
            }
            _cache.Remove(instanceId);
        }

        /// <summary>文件操作（安装/删除/更新）后让目标文件的缓存失效。</summary>
        public static void InvalidateFile(string instanceId, string filePath)
        {
            CacheState state;
            if (instanceId == null || !_cache.TryGetValue(instanceId, out state))
            {
                return;
            }
            state.Files.Remove(filePath);
        }

        /// <summary>按 ModId 取条目（含禁用项；未识别条目 ModId 为文件名）。</summary>
        public static List<ModIndexEntry> GetByModId(ModIndexSnapshot snapshot, string modId)
        {
            List<ModIndexEntry> result = new List<ModIndexEntry>();
            if (snapshot == null || string.IsNullOrEmpty(modId))
            {
                return result;
            }
            for (int i = 0; i < snapshot.Entries.Count; i++)
            {
                if (string.Equals(snapshot.Entries[i].ModId, modId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(snapshot.Entries[i]);
                }
            }
            return result;
        }

        private static ModIndexEntry BuildEntry(string instanceId, string filePath, FileInfo info)
        {
            ModIndexEntry entry = new ModIndexEntry();
            entry.InstanceId = instanceId;
            entry.FilePath = filePath;
            entry.FileName = Path.GetFileName(filePath);
            entry.Enabled = !entry.FileName.EndsWith(ModManager.DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            entry.Loader = "fabric";
            entry.Size = info.Length;
            entry.LastWriteTimeUtc = info.LastWriteTimeUtc;

            string modId = "";
            string version = "";
            string displayName = "";
            try
            {
                using (System.IO.Compression.ZipArchive archive =
                    System.IO.Compression.ZipFile.OpenRead(filePath))
                {
                    System.IO.Compression.ZipArchiveEntry meta =
                        archive.GetEntry("fabric.mod.json");
                    if (meta == null)
                    {
                        entry.MetadataStatus = ModMetadataStatus.Missing;
                    }
                    else if (meta.Length > MaxMetadataBytes)
                    {
                        entry.MetadataStatus = ModMetadataStatus.Invalid;
                    }
                    else
                    {
                        string json;
                        using (Stream stream = meta.Open())
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            json = reader.ReadToEnd();
                        }
                        Dictionary<string, object> root = JsonUtil.ParseObject(json);
                        modId = JsonUtil.GetString(root, "id") ?? "";
                        version = JsonUtil.GetString(root, "version") ?? "";
                        displayName = JsonUtil.GetString(root, "name") ?? "";
                        entry.GameVersionRange = ReadRange(root, "depends", "minecraft");
                        AddDependencies(entry, root, "depends", "required");
                        AddDependencies(entry, root, "recommends", "optional");
                        AddDependencies(entry, root, "suggests", "suggests");
                        AddDependencies(entry, root, "breaks", "conflicts");
                        AddDependencies(entry, root, "conflicts", "conflicts");
                        entry.MetadataStatus = ModMetadataStatus.Valid;
                    }
                }
            }
            catch
            {
                entry.MetadataStatus = ModMetadataStatus.Invalid;
            }

            entry.ModId = string.IsNullOrEmpty(modId) ? entry.FileName : modId;
            entry.Version = version;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = entry.FileName;
            }
            entry.DisplayName = displayName;
            return entry;
        }

        private static string ReadRange(
            Dictionary<string, object> root,
            string sectionName,
            string targetId)
        {
            IDictionary<string, object> section = JsonUtil.AsObject(
                JsonUtil.GetObject(root, sectionName));
            if (section == null)
            {
                return "";
            }
            object value;
            if (section.TryGetValue(targetId, out value))
            {
                if (value is string)
                {
                    return (string)value;
                }
                if (value is IList)
                {
                    IList list = (IList)value;
                    if (list.Count > 0)
                    {
                        object first = list[0];
                        return first == null ? "" : first.ToString();
                    }
                }
            }
            return "";
        }

        private static void AddDependencies(
            ModIndexEntry entry,
            Dictionary<string, object> root,
            string sectionName,
            string kind)
        {
            IDictionary<string, object> section = JsonUtil.AsObject(
                JsonUtil.GetObject(root, sectionName));
            if (section == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> pair in section)
            {
                string range = "";
                if (pair.Value is string)
                {
                    range = (string)pair.Value;
                }
                else if (pair.Value is IList)
                {
                    IList list = (IList)pair.Value;
                    if (list.Count > 0)
                    {
                        object first = list[0];
                        range = first == null ? "" : first.ToString();
                    }
                }
                entry.Dependencies.Add(new ModIndexDependency(
                    pair.Key, kind, range));
            }
        }
    }
}
