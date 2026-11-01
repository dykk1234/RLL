using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    public sealed class MinecraftInstaller
    {
        private readonly string _gameDir;
        private readonly int _downloadThreads;
        private readonly VersionManifestService _manifestService;

        public MinecraftInstaller(
            string gameDir,
            int downloadThreads,
            VersionManifestService manifestService)
        {
            _gameDir = Path.GetFullPath(gameDir);
            _downloadThreads = Math.Max(1, Math.Min(16, downloadThreads));
            _manifestService = manifestService;
        }

        public string GameDir
        {
            get { return _gameDir; }
        }

        public async Task InstallVanillaAsync(
            VersionSummary version,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            PathUtil.ValidateVersionId(version.Id);
            if (!HttpUtil.IsAllowedUrl(version.Url) || !PathUtil.IsSha1(version.Sha1))
            {
                throw new InvalidDataException("版本清单条目无效：" + version.Id);
            }

            Report(progress, 1, "下载版本信息 " + version.Id);
            byte[] jsonBytes = await HttpUtil.GetBytesAsync(
                version.Url,
                version.Sha1,
                16 * 1024 * 1024,
                cancellationToken).ConfigureAwait(false);
            if (version.Size > 0 && jsonBytes.Length != version.Size)
            {
                throw new InvalidDataException("版本 JSON 大小与清单不符：" + version.Id);
            }
            string json = DecodeUtf8(jsonBytes);
            VersionJson parsed = VersionResolver.ParseVersionJson(json, version.Url);
            if (!string.IsNullOrEmpty(parsed.Id) &&
                !string.Equals(parsed.Id, version.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("版本 JSON 的 id 与清单不符：" + version.Id);
            }

            string jsonPath = VersionResolver.GetVersionJsonPath(_gameDir, version.Id);
            PathUtil.WriteAllBytesAtomic(jsonPath, jsonBytes);
            Report(progress, 4, "版本信息已保存");
            await InstallResolvedAsync(version.Id, progress, cancellationToken).ConfigureAwait(false);
        }

        public async Task InstallResolvedAsync(
            string versionId,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            PathUtil.ValidateVersionId(versionId);
            Report(progress, 5, "解析版本 " + versionId);
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(_gameDir, versionId);

            string librariesDirectory = VersionResolver.GetLibrariesDirectory(_gameDir);
            string assetsDirectory = VersionResolver.GetAssetsDirectory(_gameDir);
            string assetsId = version.AssetsId;

            Dictionary<string, DownloadTask> tasks =
                new Dictionary<string, DownloadTask>(StringComparer.OrdinalIgnoreCase);

            string clientJarPath = VersionResolver.GetClientJarPath(_gameDir, version);
            if (version.ClientJar != null)
            {
                AddTask(tasks, version.ClientJar, clientJarPath, "客户端 JAR");
            }
            else if (!File.Exists(clientJarPath))
            {
                throw new InvalidDataException(
                    "版本缺少客户端 JAR，且父版本未安装：" + versionId);
            }

            if (version.AssetIndex != null)
            {
                if (string.IsNullOrEmpty(assetsId))
                {
                    throw new InvalidDataException("版本缺少资源索引名称：" + versionId);
                }
                string indexPath = VersionResolver.GetAssetIndexPath(_gameDir, assetsId);
                AddTask(tasks, version.AssetIndex, indexPath, "资源索引");
            }

            for (int i = 0; i < version.ClasspathArtifacts.Count; i++)
            {
                DownloadInfo library = version.ClasspathArtifacts[i];
                string target = PathUtil.SafeCombine(librariesDirectory, library.Path);
                AddTask(tasks, library, target, "库文件");
            }
            for (int i = 0; i < version.NativeArtifacts.Count; i++)
            {
                DownloadInfo library = version.NativeArtifacts[i].Download;
                string target = PathUtil.SafeCombine(librariesDirectory, library.Path);
                AddTask(tasks, library, target, "本地库");
            }

            if (version.LoggingFile != null)
            {
                if (string.IsNullOrEmpty(version.LoggingFile.Path))
                {
                    throw new InvalidDataException("日志配置文件路径为空。");
                }
                string loggingPath = PathUtil.SafeCombine(
                    Path.Combine(assetsDirectory, "log_configs"),
                    version.LoggingFile.Path);
                AddTask(tasks, version.LoggingFile, loggingPath, "日志配置");
            }

            List<DownloadTask> taskList = new List<DownloadTask>(tasks.Values);
            for (int i = 0; i < taskList.Count; i++)
            {
                DownloadTask task = taskList[i];
                if (!PathUtil.IsSha1(task.Sha1))
                {
                    await ResolveSidecarSha1Async(task, cancellationToken).ConfigureAwait(false);
                }
            }

            Report(progress, 8, "开始下载文件");
            await DownloadManyAsync(
                taskList,
                8,
                72,
                "下载文件",
                progress,
                cancellationToken).ConfigureAwait(false);

            if (version.AssetIndex != null && !string.IsNullOrEmpty(assetsId))
            {
                string indexPath = VersionResolver.GetAssetIndexPath(_gameDir, assetsId);
                List<AssetEntry> assetEntries;
                bool virtualAssets;
                bool mapToResources;
                List<DownloadTask> assetTasks = BuildAssetTasks(
                    indexPath,
                    out assetEntries,
                    out virtualAssets,
                    out mapToResources);
                await DownloadManyAsync(
                    assetTasks,
                    72,
                    88,
                    "下载资源文件",
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (virtualAssets || mapToResources)
                {
                    Report(progress, 89, "整理旧版资源");
                    CopyLegacyAssets(
                        assetEntries,
                        assetsId,
                        virtualAssets,
                        mapToResources,
                        progress,
                        cancellationToken);
                }
            }

            Report(progress, 90, "解压本地库");
            string nativesDirectory = VersionResolver.GetNativesDirectory(_gameDir, versionId);
            NativeExtractor.Extract(
                librariesDirectory,
                nativesDirectory,
                version.NativesRelativePath,
                version.NativeArtifacts,
                progress,
                cancellationToken);
            VersionResolver.EnsureNativesLayout(nativesDirectory, version);

            Report(progress, 100, "安装完成：" + versionId);
        }

        private static void AddTask(
            Dictionary<string, DownloadTask> tasks,
            DownloadInfo download,
            string targetPath,
            string label)
        {
            if (download == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(download.Url) || !HttpUtil.IsAllowedUrl(download.Url))
            {
                throw new InvalidDataException("下载地址不在允许列表：" + download.Url);
            }
            string target = Path.GetFullPath(targetPath);
            DownloadTask existing;
            if (tasks.TryGetValue(target, out existing))
            {
                if (!string.IsNullOrEmpty(existing.Sha1) &&
                    !string.IsNullOrEmpty(download.Sha1) &&
                    !string.Equals(existing.Sha1, download.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("同一目标文件出现不同 SHA1：" + target);
                }
                if (string.IsNullOrEmpty(existing.Sha1) && !string.IsNullOrEmpty(download.Sha1))
                {
                    existing.Sha1 = download.Sha1;
                }
                return;
            }
            DownloadTask task = new DownloadTask();
            task.TargetPath = target;
            task.Url = download.Url;
            task.Sha1 = download.Sha1;
            task.Size = download.Size;
            task.Label = label;
            tasks[target] = task;
        }

        private static async Task ResolveSidecarSha1Async(
            DownloadTask task,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(task.Sha1) && !PathUtil.IsSha1(task.Sha1))
            {
                throw new InvalidDataException("SHA1 格式无效：" + task.Url);
            }
            string sidecar = await HttpUtil.GetStringAsync(
                task.Url + ".sha1",
                cancellationToken).ConfigureAwait(false);
            string[] parts = sidecar.Trim().Split(
                new char[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !PathUtil.IsSha1(parts[0]))
            {
                throw new InvalidDataException("无法获取有效的 SHA1：" + task.Url);
            }
            task.Sha1 = parts[0].ToLowerInvariant();
        }

        private async Task DownloadManyAsync(
            List<DownloadTask> tasks,
            int startPercent,
            int endPercent,
            string label,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (tasks == null || tasks.Count == 0)
            {
                Report(progress, endPercent, label + "：无需下载");
                return;
            }

            DownloadState state = new DownloadState();
            state.Tasks = tasks;
            state.TotalBytes = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                state.TotalBytes += Math.Max(1, tasks[i].Size);
            }
            state.LastPercent = -1;

            int workerCount = Math.Min(_downloadThreads, tasks.Count);
            Func<Task> worker = async () =>
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int index = Interlocked.Increment(ref state.NextIndex);
                    if (index >= state.Tasks.Count)
                    {
                        break;
                    }
                    DownloadTask task = state.Tasks[index];
                    await HttpUtil.DownloadFileAsync(
                        task.Url,
                        task.TargetPath,
                        task.Sha1,
                        task.Size,
                        delegate(long bytes)
                        {
                            Interlocked.Add(ref state.DoneBytes, bytes);
                            ReportDownloadProgress(state, label, startPercent, endPercent, progress);
                        },
                        cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref state.Completed);
                    ReportDownloadProgress(state, label, startPercent, endPercent, progress);
                }
            };

            Task[] workers = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = Task.Run(worker);
            }
            await Task.WhenAll(workers).ConfigureAwait(false);
            Report(progress, endPercent, label + "：完成");
        }

        private static void ReportDownloadProgress(
            DownloadState state,
            string label,
            int startPercent,
            int endPercent,
            IProgress<InstallProgress> progress)
        {
            long totalBytes = Interlocked.Read(ref state.TotalBytes);
            long doneBytes = Interlocked.Read(ref state.DoneBytes);
            int completed = state.Completed;
            int percent;
            if (totalBytes > 0)
            {
                double ratio = (double)doneBytes / (double)totalBytes;
                if (ratio > 1.0)
                {
                    ratio = 1.0;
                }
                percent = startPercent + (int)((endPercent - startPercent) * ratio);
            }
            else
            {
                double ratio = state.Tasks.Count == 0 ? 1.0 : (double)completed / state.Tasks.Count;
                percent = startPercent + (int)((endPercent - startPercent) * ratio);
            }
            if (percent < startPercent)
            {
                percent = startPercent;
            }
            if (percent > endPercent)
            {
                percent = endPercent;
            }
            int old = Interlocked.Exchange(ref state.LastPercent, percent);
            if (old != percent)
            {
                // 附带速度与剩余时间：首次安装要下几百 MB，只有百分比用户无法判断要等多久。
                string detail = string.Format(
                    "{0} {1}/{2}",
                    label,
                    Math.Min(completed, state.Tasks.Count),
                    state.Tasks.Count);
                double speed = state.Rate.Update(doneBytes);
                if (speed > 0)
                {
                    detail += "  " + DownloadRateTracker.FormatSpeed(speed);
                    double eta = state.Rate.GetEta(totalBytes);
                    if (eta >= 0)
                    {
                        detail += "  剩余 " + DownloadRateTracker.FormatEta(eta);
                    }
                }
                Report(progress, percent, detail);
            }
        }

        private List<DownloadTask> BuildAssetTasks(
            string indexPath,
            out List<AssetEntry> entries,
            out bool virtualAssets,
            out bool mapToResources)
        {
            entries = new List<AssetEntry>();
            virtualAssets = false;
            mapToResources = false;
            FileInfo info = new FileInfo(indexPath);
            if (!File.Exists(indexPath))
            {
                throw new FileNotFoundException("资源索引不存在：" + indexPath, indexPath);
            }
            if (info.Length > 64L * 1024L * 1024L)
            {
                throw new InvalidDataException("资源索引过大：" + indexPath);
            }
            Dictionary<string, object> root = JsonUtil.ParseObject(File.ReadAllText(indexPath, Encoding.UTF8));
            virtualAssets = JsonUtil.GetBool(root, "virtual", false);
            mapToResources = JsonUtil.GetBool(root, "map_to_resources", false);
            IDictionary<string, object> objects = JsonUtil.GetObject(root, "objects");
            if (objects == null)
            {
                throw new InvalidDataException("资源索引缺少 objects：" + indexPath);
            }

            Dictionary<string, DownloadTask> tasks =
                new Dictionary<string, DownloadTask>(StringComparer.OrdinalIgnoreCase);
            string objectsDirectory = Path.Combine(VersionResolver.GetAssetsDirectory(_gameDir), "objects");
            foreach (KeyValuePair<string, object> pair in objects)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(pair.Value);
                if (item == null)
                {
                    continue;
                }
                string hash = JsonUtil.GetString(item, "hash").ToLowerInvariant();
                long size = JsonUtil.GetLong(item, "size", 0);
                if (!PathUtil.IsSha1(hash))
                {
                    throw new InvalidDataException("资源哈希格式无效：" + pair.Key);
                }
                string relative = hash.Substring(0, 2) + "/" + hash;
                string target = PathUtil.SafeCombine(objectsDirectory, relative);
                if (!tasks.ContainsKey(target))
                {
                    DownloadTask task = new DownloadTask();
                    task.TargetPath = target;
                    task.Url = "https://resources.download.minecraft.net/" + relative;
                    task.Sha1 = hash;
                    task.Size = size;
                    task.Label = "资源文件";
                    tasks[target] = task;
                }
                AssetEntry entry = new AssetEntry();
                entry.Name = pair.Key;
                entry.Hash = hash;
                entries.Add(entry);
            }
            return new List<DownloadTask>(tasks.Values);
        }

        private void CopyLegacyAssets(
            List<AssetEntry> entries,
            string assetsId,
            bool virtualAssets,
            bool mapToResources,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            string assetsDirectory = VersionResolver.GetAssetsDirectory(_gameDir);
            string objectsDirectory = Path.Combine(assetsDirectory, "objects");
            string virtualDirectory = null;
            string resourcesDirectory = null;
            if (virtualAssets)
            {
                virtualDirectory = PathUtil.SafeCombine(
                    assetsDirectory,
                    "virtual/" + assetsId);
                Directory.CreateDirectory(virtualDirectory);
            }
            if (mapToResources)
            {
                resourcesDirectory = Path.Combine(_gameDir, "resources");
                Directory.CreateDirectory(resourcesDirectory);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssetEntry entry = entries[i];
                string source = PathUtil.SafeCombine(
                    objectsDirectory,
                    entry.Hash.Substring(0, 2) + "/" + entry.Hash);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("资源对象不存在：" + source, source);
                }
                if (virtualDirectory != null)
                {
                    string destination = PathUtil.SafeCombine(virtualDirectory, entry.Name);
                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Copy(source, destination, true);
                }
                if (resourcesDirectory != null)
                {
                    string destination = PathUtil.SafeCombine(resourcesDirectory, entry.Name);
                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Copy(source, destination, true);
                }
                if (progress != null && (i % 100) == 0)
                {
                    progress.Report(new InstallProgress(
                        89,
                        "整理旧版资源 " + (i + 1) + "/" + entries.Count));
                }
            }
        }

        private static string DecodeUtf8(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string status)
        {
            if (progress == null)
            {
                return;
            }
            if (percent < 0)
            {
                percent = 0;
            }
            if (percent > 100)
            {
                percent = 100;
            }
            progress.Report(new InstallProgress(percent, status));
        }

        private sealed class DownloadTask
        {
            public string TargetPath;
            public string Url;
            public string Sha1;
            public long Size;
            public string Label;
        }

        private sealed class DownloadState
        {
            public List<DownloadTask> Tasks;
            public long TotalBytes;
            public long DoneBytes;
            public int Completed;
            public int NextIndex = -1;
            public int LastPercent;
            /// <summary>速度与剩余时间估算器。</summary>
            public readonly DownloadRateTracker Rate = new DownloadRateTracker();
        }

        private sealed class AssetEntry
        {
            public string Name;
            public string Hash;
        }
    }
}
