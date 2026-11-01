using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    public sealed class ModProject
    {
        public string Id;
        public string Slug;
        public string Title;
        public string Description;
        public string IconUrl;
        public string ClientSide;
        public long Downloads;
        public List<string> Categories = new List<string>();
    }

    public sealed class ModVersionFile
    {
        public string FileName;
        public string Url;
        public string Sha1;
        public string Sha512;
        public long Size;
        public bool Primary;
    }

    public sealed class ModDependency
    {
        public string VersionId;
        public string ProjectId;
        public string DependencyType;
    }

    public sealed class ModVersion
    {
        public string Id;
        public string ProjectId;
        public string Name;
        public string VersionNumber;
        public string VersionType;
        public List<string> GameVersions = new List<string>();
        public List<string> Loaders = new List<string>();
        public List<ModVersionFile> Files = new List<ModVersionFile>();
        public List<ModDependency> Dependencies = new List<ModDependency>();
    }

    public sealed class ModInstallResult
    {
        public List<string> Files = new List<string>();
        public List<string> NewFiles = new List<string>();
        public List<string> AlreadyPresentFiles = new List<string>();
    }

    /// <summary>批量安装时单个模组的失败信息。</summary>
    public sealed class ModBatchFailure
    {
        /// <summary>失败的模组标识。</summary>
        public string Slug;
        /// <summary>失败原因（给用户看的简短描述）。</summary>
        public string Reason;

        public ModBatchFailure()
        {
        }

        public ModBatchFailure(string slug, string reason)
        {
            Slug = slug;
            Reason = reason ?? "";
        }
    }

    /// <summary>
    /// 批量安装（预设导入）的结果。
    /// </summary>
    public sealed class ModBatchInstallResult
    {
        /// <summary>成功安装的模组标识。</summary>
        public List<string> Installed = new List<string>();
        /// <summary>安装失败��模组与原因。</summary>
        public List<ModBatchFailure> Failed = new List<ModBatchFailure>();
        /// <summary>实际写入的文件名。</summary>
        public List<string> Files = new List<string>();
        /// <summary>本次新写入的文件名。</summary>
        public List<string> NewFiles = new List<string>();
        /// <summary>已存在（内容一致）的文件名。</summary>
        public List<string> AlreadyFiles = new List<string>();
    }

    /// <summary>
    /// 通过 Modrinth 官方 API 查询和安装 Fabric Mod。
    /// 只接受 HTTPS 白名单域名，并校验 Modrinth 元数据中的 SHA1。
    /// </summary>
    public sealed class ModrinthService
    {
        private const string ApiBase = "https://api.modrinth.com/v2";
        private const int MaxDependencyDepth = 4;
        private const int MaxModFiles = 24;

        public async Task<List<ModProject>> SearchAsync(
            string query,
            string gameVersion,
            CancellationToken cancellationToken)
        {
            string url = ApiBase + "/search?limit=20&query=" +
                         Uri.EscapeDataString(query ?? "") +
                         "&facets=" + Uri.EscapeDataString(BuildFacets(gameVersion));
            string json = await HttpUtil.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.ParseObject(json);
            IList hits = JsonUtil.GetArray(root, "hits");
            List<ModProject> result = new List<ModProject>();
            if (hits != null)
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    IDictionary<string, object> item = JsonUtil.AsObject(hits[i]);
                    if (item == null)
                    {
                        continue;
                    }
                    ModProject project = ParseProject(item);
                    if (!string.IsNullOrEmpty(project.Slug) || !string.IsNullOrEmpty(project.Id))
                    {
                        result.Add(project);
                    }
                }
            }
            return result;
        }

        public async Task<ModProject> GetProjectAsync(
            string idOrSlug,
            CancellationToken cancellationToken)
        {
            ValidateIdentifier(idOrSlug);
            string url = ApiBase + "/project/" + Uri.EscapeDataString(idOrSlug);
            string json = await HttpUtil.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return ParseProject(JsonUtil.ParseObject(json));
        }

        public async Task<List<ModVersion>> GetVersionsAsync(
            string idOrSlug,
            string gameVersion,
            CancellationToken cancellationToken)
        {
            ValidateIdentifier(idOrSlug);
            if (!string.IsNullOrEmpty(gameVersion))
            {
                ValidateGameVersion(gameVersion);
            }
            string url = ApiBase + "/project/" + Uri.EscapeDataString(idOrSlug) +
                         "/version?loaders=" + Uri.EscapeDataString("[\"fabric\"]");
            if (!string.IsNullOrEmpty(gameVersion))
            {
                url += "&game_versions=" +
                       Uri.EscapeDataString("[\"" + gameVersion + "\"]");
            }
            string json = await HttpUtil.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            IList items = JsonUtil.Parse(json) as IList;
            List<ModVersion> result = new List<ModVersion>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    IDictionary<string, object> item = JsonUtil.AsObject(items[i]);
                    if (item != null)
                    {
                        result.Add(ParseVersion(item));
                    }
                }
            }
            return result;
        }

        public async Task<ModVersion> GetVersionByIdAsync(
            string versionId,
            CancellationToken cancellationToken)
        {
            ValidateIdentifier(versionId);
            string url = ApiBase + "/version/" + Uri.EscapeDataString(versionId);
            string json = await HttpUtil.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return ParseVersion(JsonUtil.ParseObject(json));
        }

        public async Task<ModVersion> GetBestVersionAsync(
            string idOrSlug,
            string gameVersion,
            CancellationToken cancellationToken)
        {
            List<ModVersion> versions = await GetVersionsAsync(
                idOrSlug, gameVersion, cancellationToken).ConfigureAwait(false);
            return SelectBestVersion(versions, gameVersion);
        }

        public static ModVersion SelectBestVersion(List<ModVersion> versions, string gameVersion)
        {
            if (versions == null)
            {
                return null;
            }
            ModVersion fallback = null;
            for (int i = 0; i < versions.Count; i++)
            {
                ModVersion version = versions[i];
                if (!Matches(version, gameVersion))
                {
                    continue;
                }
                if (string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }
                if (fallback == null)
                {
                    fallback = version;
                }
            }
            return fallback;
        }

        public static bool Matches(ModVersion version, string gameVersion)
        {
            if (version == null || string.IsNullOrEmpty(gameVersion))
            {
                return false;
            }
            bool loaderOk = false;
            for (int i = 0; i < version.Loaders.Count; i++)
            {
                if (string.Equals(version.Loaders[i], "fabric", StringComparison.OrdinalIgnoreCase))
                {
                    loaderOk = true;
                    break;
                }
            }
            if (!loaderOk)
            {
                return false;
            }
            for (int i = 0; i < version.GameVersions.Count; i++)
            {
                if (string.Equals(version.GameVersions[i], gameVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static ModVersionFile SelectPrimaryFile(ModVersion version)
        {
            if (version == null || version.Files == null)
            {
                return null;
            }
            ModVersionFile fallback = null;
            for (int i = 0; i < version.Files.Count; i++)
            {
                ModVersionFile file = version.Files[i];
                if (file == null || !IsSafeModFileName(file.FileName))
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = file;
                }
                if (file.Primary)
                {
                    return file;
                }
            }
            return fallback;
        }

        public static bool IsSafeModFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length > 200)
            {
                return false;
            }
            if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (fileName.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }
            if (fileName.EndsWith(".", StringComparison.Ordinal) ||
                fileName.EndsWith(" ", StringComparison.Ordinal) ||
                fileName.StartsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                return false;
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < fileName.Length; i++)
            {
                if (Array.IndexOf(invalid, fileName[i]) >= 0)
                {
                    return false;
                }
            }
            return true;
        }

        public async Task<ModInstallResult> InstallAsync(
            string gameDir,
            string gameVersion,
            string idOrSlug,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(gameDir))
            {
                throw new ArgumentNullException("gameDir");
            }
            ValidateGameVersion(gameVersion);
            ValidateIdentifier(idOrSlug);

            Report(progress, 1, "解析 Mod 版本");
            ModVersion root = await GetBestVersionAsync(
                idOrSlug, gameVersion, cancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                throw new InvalidOperationException(
                    "该 Mod 没有兼容 Minecraft " + gameVersion + " 的 Fabric 版本。");
            }

            List<ModVersion> versions = new List<ModVersion>();
            HashSet<string> visitedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await CollectDependenciesAsync(
                root,
                gameVersion,
                versions,
                visitedVersions,
                visitedProjects,
                0,
                cancellationToken).ConfigureAwait(false);

            string modsDirectory = Path.Combine(Path.GetFullPath(gameDir), "mods");
            Directory.CreateDirectory(modsDirectory);

            ModInstallResult result = new ModInstallResult();
            for (int i = 0; i < versions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModVersion version = versions[i];
                ModVersionFile file = SelectPrimaryFile(version);
                if (file == null)
                {
                    throw new InvalidDataException(
                        "Mod 版本没有可安装的 JAR 文件：" + version.Name);
                }
                ValidateModFile(file);
                string targetPath = PathUtil.SafeCombine(modsDirectory, file.FileName);
                bool existed = false;
                if (File.Exists(targetPath))
                {
                    try
                    {
                        existed = string.Equals(
                            PathUtil.GetSha1(targetPath),
                            file.Sha1,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        existed = false;
                    }
                }

                int percent = 8 + (int)((long)i * 90L / Math.Max(1, versions.Count));
                Report(progress, percent,
                    "下载 Mod " + (i + 1) + "/" + versions.Count + "：" + file.FileName);
                await HttpUtil.DownloadFileAsync(
                    file.Url,
                    targetPath,
                    file.Sha1,
                    file.Size,
                    null,
                    cancellationToken).ConfigureAwait(false);

                result.Files.Add(file.FileName);
                if (existed)
                {
                    result.AlreadyPresentFiles.Add(file.FileName);
                }
                else
                {
                    result.NewFiles.Add(file.FileName);
                }
            }
            Report(progress, 100, "Mod 安装完成，共 " + result.Files.Count + " 个文件");
            return result;
        }

        /// <summary>
        /// 批量安装：按预设清单依次安装多个模组。
        ///
        /// 与单次 InstallAsync 的区别：
        /// - **单个模组失败不中断整批**——预设里常有一两个模组当前版本不兼容，
        ///   用户更希望"能装的都装上"，而不是整个导入失败；
        /// - 失败项收集起来返回，界面可以明确告诉用户哪几个没装上、为什么。
        ///
        /// 依赖仍由每个 InstallAsync 自行解析（自动补齐前置）。
        /// </summary>
        public async Task<ModBatchInstallResult> InstallManyAsync(
            string gameDir,
            string gameVersion,
            List<string> idOrSlugs,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            ModBatchInstallResult result = new ModBatchInstallResult();
            if (idOrSlugs == null || idOrSlugs.Count == 0)
            {
                return result;
            }
            int total = idOrSlugs.Count;
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string slug = idOrSlugs[i];
                if (string.IsNullOrEmpty(slug))
                {
                    continue;
                }
                // 进度区间按批次均分，让用户看到"装到第几个了"。
                int start = 5 + (int)((long)i * 90L / total);
                int end = 5 + (int)((long)(i + 1) * 90L / total);
                try
                {
                    ModInstallResult single = await InstallAsync(
                        gameDir,
                        gameVersion,
                        slug,
                        new MappedProgress(progress, start, end, slug + " "),
                        cancellationToken).ConfigureAwait(false);
                    result.Installed.Add(slug);
                    result.Files.AddRange(single.Files);
                    result.NewFiles.AddRange(single.NewFiles);
                    result.AlreadyFiles.AddRange(single.AlreadyPresentFiles);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单个失败不影响整批：记录下来，最后统一提示。
                    result.Failed.Add(new ModBatchFailure(slug, ex.Message));
                    Log.Warn("预设批量安装：" + slug + " 安装失败 —— " + ex.Message);
                }
            }
            Report(progress, 100,
                "批量安装完成：成功 " + result.Installed.Count + " 个，失败 " +
                result.Failed.Count + " 个");
            return result;
        }

        private async Task CollectDependenciesAsync(
            ModVersion version,
            string gameVersion,
            List<ModVersion> result,
            HashSet<string> visitedVersions,
            HashSet<string> visitedProjects,
            int depth,
            CancellationToken cancellationToken)
        {
            if (version == null || string.IsNullOrEmpty(version.Id))
            {
                return;
            }
            if (!visitedVersions.Add(version.Id))
            {
                return;
            }
            if (result.Count >= MaxModFiles)
            {
                throw new InvalidOperationException("Mod 依赖数量超过安全上限。");
            }
            result.Add(version);
            if (!string.IsNullOrEmpty(version.ProjectId))
            {
                visitedProjects.Add(version.ProjectId);
            }
            if (depth >= MaxDependencyDepth)
            {
                return;
            }

            for (int i = 0; i < version.Dependencies.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModDependency dependency = version.Dependencies[i];
                if (dependency == null ||
                    !string.Equals(dependency.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ModVersion dependencyVersion = null;
                if (!string.IsNullOrEmpty(dependency.VersionId))
                {
                    try
                    {
                        dependencyVersion = await GetVersionByIdAsync(
                            dependency.VersionId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                        {
                            throw;
                        }
                        Log.Warn("读取 Mod 依赖版本失败：" + dependency.VersionId + "，" + ex.Message);
                        dependencyVersion = null;
                    }
                    if (dependencyVersion != null && !Matches(dependencyVersion, gameVersion))
                    {
                        dependencyVersion = null;
                    }
                }
                if (dependencyVersion == null && !string.IsNullOrEmpty(dependency.ProjectId))
                {
                    dependencyVersion = await GetBestVersionAsync(
                        dependency.ProjectId, gameVersion, cancellationToken).ConfigureAwait(false);
                }
                if (dependencyVersion == null)
                {
                    if (!string.IsNullOrEmpty(dependency.ProjectId))
                    {
                        throw new InvalidOperationException(
                            "缺少兼容 Minecraft " + gameVersion + " 的必需前置 Mod：" +
                            dependency.ProjectId);
                    }
                    continue;
                }
                if (visitedVersions.Contains(dependencyVersion.Id) ||
                    (!string.IsNullOrEmpty(dependencyVersion.ProjectId) &&
                     visitedProjects.Contains(dependencyVersion.ProjectId)))
                {
                    continue;
                }
                await CollectDependenciesAsync(
                    dependencyVersion,
                    gameVersion,
                    result,
                    visitedVersions,
                    visitedProjects,
                    depth + 1,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ValidateModFile(ModVersionFile file)
        {
            if (!IsSafeModFileName(file.FileName))
            {
                throw new InvalidDataException("Mod 文件名不安全：" + file.FileName);
            }
            if (!HttpUtil.IsAllowedUrl(file.Url))
            {
                throw new InvalidDataException("Mod 下载地址不在允许列表：" + file.Url);
            }
            if (!PathUtil.IsSha1(file.Sha1))
            {
                throw new InvalidDataException("Mod 文件缺少有效 SHA1：" + file.FileName);
            }
            if (file.Size < 0)
            {
                throw new InvalidDataException("Mod 文件大小无效：" + file.FileName);
            }
        }

        private static void ValidateIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
            {
                throw new ArgumentException("Mod 标识无效。", "value");
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '-' || c == '_';
                if (!ok)
                {
                    throw new ArgumentException("Mod 标识包含非法字符。", "value");
                }
            }
        }

        private static void ValidateGameVersion(string gameVersion)
        {
            if (!PathUtil.IsValidVersionId(gameVersion))
            {
                throw new ArgumentException("Minecraft 版本无效。", "gameVersion");
            }
        }

        private static string BuildFacets(string gameVersion)
        {
            if (!string.IsNullOrEmpty(gameVersion))
            {
                ValidateGameVersion(gameVersion);
            }
            // Modrinth 的 facets 语义：外层数组表示 AND，内层数组表示 OR。
            // 因此每个条件单独成组，否则 project_type 与 fabric 会变成"或"关系。
            string facets = "[[\"project_type:mod\"],[\"categories:fabric\"]";
            if (!string.IsNullOrEmpty(gameVersion))
            {
                facets += ",[\"versions:" + gameVersion + "\"]";
            }
            return facets + "]";
        }

        private static ModProject ParseProject(IDictionary<string, object> root)
        {
            ModProject project = new ModProject();
            project.Id = JsonUtil.GetString(root, "project_id");
            if (string.IsNullOrEmpty(project.Id))
            {
                project.Id = JsonUtil.GetString(root, "id");
            }
            project.Slug = JsonUtil.GetString(root, "slug");
            project.Title = JsonUtil.GetString(root, "title");
            project.Description = JsonUtil.GetString(root, "description");
            project.IconUrl = JsonUtil.GetString(root, "icon_url");
            project.ClientSide = JsonUtil.GetString(root, "client_side");
            project.Downloads = JsonUtil.GetLong(root, "downloads", 0);
            project.Categories = ParseStringList(JsonUtil.GetArray(root, "categories"));
            return project;
        }

        private static ModVersion ParseVersion(IDictionary<string, object> root)
        {
            ModVersion version = new ModVersion();
            version.Id = JsonUtil.GetString(root, "id");
            version.ProjectId = JsonUtil.GetString(root, "project_id");
            version.Name = JsonUtil.GetString(root, "name");
            version.VersionNumber = JsonUtil.GetString(root, "version_number");
            version.VersionType = JsonUtil.GetString(root, "version_type");
            version.GameVersions = ParseStringList(JsonUtil.GetArray(root, "game_versions"));
            version.Loaders = ParseStringList(JsonUtil.GetArray(root, "loaders"));

            IList files = JsonUtil.GetArray(root, "files");
            if (files != null)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    IDictionary<string, object> fileObject = JsonUtil.AsObject(files[i]);
                    if (fileObject == null)
                    {
                        continue;
                    }
                    ModVersionFile file = new ModVersionFile();
                    file.FileName = JsonUtil.GetString(fileObject, "filename");
                    file.Url = JsonUtil.GetString(fileObject, "url");
                    file.Size = JsonUtil.GetLong(fileObject, "size", 0);
                    file.Primary = JsonUtil.GetBool(fileObject, "primary", false);
                    IDictionary<string, object> hashes = JsonUtil.GetObject(fileObject, "hashes");
                    if (hashes != null)
                    {
                        file.Sha1 = JsonUtil.GetString(hashes, "sha1");
                        file.Sha512 = JsonUtil.GetString(hashes, "sha512");
                    }
                    version.Files.Add(file);
                }
            }

            IList dependencies = JsonUtil.GetArray(root, "dependencies");
            if (dependencies != null)
            {
                for (int i = 0; i < dependencies.Count; i++)
                {
                    IDictionary<string, object> dependencyObject = JsonUtil.AsObject(dependencies[i]);
                    if (dependencyObject == null)
                    {
                        continue;
                    }
                    ModDependency dependency = new ModDependency();
                    dependency.VersionId = JsonUtil.GetString(dependencyObject, "version_id");
                    dependency.ProjectId = JsonUtil.GetString(dependencyObject, "project_id");
                    dependency.DependencyType = JsonUtil.GetString(dependencyObject, "dependency_type");
                    version.Dependencies.Add(dependency);
                }
            }
            return version;
        }

        private static List<string> ParseStringList(IList list)
        {
            List<string> result = new List<string>();
            if (list == null)
            {
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                string value = list[i] as string;
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }
            return result;
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string status)
        {
            if (progress != null)
            {
                progress.Report(new InstallProgress(percent, status));
            }
        }

        /// <summary>
        /// 把内层进度映射到外层的一段区间，并给状态加前缀。
        /// 批量安装时用它把"每个模组自己的 0-100%"映射成整批的 5-95%。
        /// </summary>
        private sealed class MappedProgress : IProgress<InstallProgress>
        {
            private readonly IProgress<InstallProgress> _inner;
            private readonly int _start;
            private readonly int _end;
            private readonly string _prefix;

            public MappedProgress(
                IProgress<InstallProgress> inner, int start, int end, string prefix)
            {
                _inner = inner;
                _start = start;
                _end = end;
                _prefix = prefix ?? "";
            }

            public void Report(InstallProgress value)
            {
                if (_inner == null || value == null)
                {
                    return;
                }
                int percent = _start + (value.Percent * (_end - _start)) / 100;
                if (percent < _start)
                {
                    percent = _start;
                }
                if (percent > _end)
                {
                    percent = _end;
                }
                _inner.Report(new InstallProgress(percent, _prefix + value.Status));
            }
        }
    }
}
