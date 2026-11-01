using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    public static class VersionResolver
    {
        private const int MaxInheritDepth = 10;
        private const long MaxVersionJsonBytes = 16L * 1024L * 1024L;

        public static VersionJson ParseVersionJson(string json, string sourcePath)
        {
            Dictionary<string, object> root = JsonUtil.ParseObject(json);
            VersionJson version = new VersionJson();
            version.Id = JsonUtil.GetString(root, "id");
            version.Type = JsonUtil.GetString(root, "type");
            version.MainClass = JsonUtil.GetString(root, "mainClass");
            version.Assets = JsonUtil.GetString(root, "assets");
            version.InheritsFrom = JsonUtil.GetString(root, "inheritsFrom");
            version.Jar = JsonUtil.GetString(root, "jar");
            version.MinecraftArguments = JsonUtil.GetString(root, "minecraftArguments");
            version.SourcePath = sourcePath;

            if (!string.IsNullOrEmpty(version.InheritsFrom))
            {
                PathUtil.ValidateVersionId(version.InheritsFrom);
            }
            if (!string.IsNullOrEmpty(version.Jar))
            {
                PathUtil.ValidateVersionId(version.Jar);
            }

            IDictionary<string, object> javaVersion = JsonUtil.GetObject(root, "javaVersion");
            if (javaVersion != null)
            {
                version.JavaVersionMajor = JsonUtil.GetInt(javaVersion, "majorVersion", 0);
            }

            IDictionary<string, object> downloads = JsonUtil.GetObject(root, "downloads");
            if (downloads != null)
            {
                version.Client = ParseDownload(JsonUtil.GetObject(downloads, "client"));
            }
            version.AssetIndex = ParseDownload(JsonUtil.GetObject(root, "assetIndex"));

            IDictionary<string, object> logging = JsonUtil.GetObject(root, "logging");
            if (logging != null)
            {
                IDictionary<string, object> client = JsonUtil.GetObject(logging, "client");
                if (client != null)
                {
                    LoggingInfo loggingInfo = new LoggingInfo();
                    loggingInfo.Argument = JsonUtil.GetString(client, "argument");
                    IDictionary<string, object> fileObject = JsonUtil.GetObject(client, "file");
                    loggingInfo.File = ParseDownload(fileObject);
                    if (loggingInfo.File != null && string.IsNullOrEmpty(loggingInfo.File.Path))
                    {
                        loggingInfo.File.Path = JsonUtil.GetString(fileObject, "id");
                    }
                    version.Logging = loggingInfo;
                }
            }

            IList libraries = JsonUtil.GetArray(root, "libraries");
            if (libraries != null)
            {
                for (int i = 0; i < libraries.Count; i++)
                {
                    IDictionary<string, object> item = JsonUtil.AsObject(libraries[i]);
                    if (item == null)
                    {
                        continue;
                    }
                    version.Libraries.Add(ParseLibrary(item));
                }
            }

            IDictionary<string, object> arguments = JsonUtil.GetObject(root, "arguments");
            if (arguments != null)
            {
                version.GameArguments = ParseArguments(JsonUtil.GetArray(arguments, "game"));
                version.JvmArguments = ParseArguments(JsonUtil.GetArray(arguments, "jvm"));
            }
            return version;
        }

        public static EffectiveVersion LoadInstalledVersion(string gameDir, string versionId)
        {
            PathUtil.ValidateVersionId(versionId);
            HashSet<string> stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VersionJson raw = LoadRaw(gameDir, versionId, stack, 0);
            return Resolve(raw);
        }

        public static bool IsFabricVersion(EffectiveVersion version)
        {
            if (version == null)
            {
                return false;
            }
            return (version.MainClass ?? "").IndexOf(
                "fabric", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsInstalled(string gameDir, string versionId)
        {
            if (!PathUtil.IsValidVersionId(versionId))
            {
                return false;
            }
            try
            {
                return File.Exists(GetVersionJsonPath(gameDir, versionId));
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetInstalledVersions(string gameDir)
        {
            List<string> result = new List<string>();
            string versionsDirectory = Path.Combine(gameDir, "versions");
            if (!Directory.Exists(versionsDirectory))
            {
                return result;
            }
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(versionsDirectory);
            }
            catch
            {
                return result;
            }
            for (int i = 0; i < directories.Length; i++)
            {
                string id = Path.GetFileName(directories[i]);
                if (!PathUtil.IsValidVersionId(id))
                {
                    continue;
                }
                string jsonPath = Path.Combine(directories[i], id + ".json");
                if (File.Exists(jsonPath))
                {
                    result.Add(id);
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static string GetVersionsDirectory(string gameDir)
        {
            return Path.Combine(gameDir, "versions");
        }

        public static string GetVersionDirectory(string gameDir, string versionId)
        {
            PathUtil.ValidateVersionId(versionId);
            return PathUtil.SafeCombine(GetVersionsDirectory(gameDir), versionId);
        }

        public static string GetVersionJsonPath(string gameDir, string versionId)
        {
            PathUtil.ValidateVersionId(versionId);
            return PathUtil.SafeCombine(
                GetVersionDirectory(gameDir, versionId),
                versionId + ".json");
        }

        public static string GetNativesDirectory(string gameDir, string versionId)
        {
            return PathUtil.SafeCombine(GetVersionDirectory(gameDir, versionId), "natives");
        }

        public static string GetNativesTargetDirectory(string gameDir, EffectiveVersion version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            string nativesDirectory = GetNativesDirectory(gameDir, version.Id);
            if (string.IsNullOrEmpty(version.NativesRelativePath))
            {
                return nativesDirectory;
            }
            return PathUtil.SafeCombine(nativesDirectory, version.NativesRelativePath);
        }

        /// <summary>
        /// 创建 natives 根目录、java.library.path 指向的子目录，以及 JNA/LWJGL/Netty
        /// 需要的工作目录。26.2+ 的版本 JSON 会引用这些子目录，但游戏本身只创建其中一部分。
        /// </summary>
        public static void EnsureNativesLayout(string nativesDirectory, EffectiveVersion version)
        {
            if (string.IsNullOrEmpty(nativesDirectory))
            {
                throw new ArgumentNullException("nativesDirectory");
            }
            Directory.CreateDirectory(nativesDirectory);
            if (version == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(version.NativesRelativePath))
            {
                Directory.CreateDirectory(
                    PathUtil.SafeCombine(nativesDirectory, version.NativesRelativePath));
            }
            for (int i = 0; i < version.NativesWorkDirectories.Count; i++)
            {
                string relative = version.NativesWorkDirectories[i];
                if (!string.IsNullOrEmpty(relative))
                {
                    Directory.CreateDirectory(PathUtil.SafeCombine(nativesDirectory, relative));
                }
            }
        }

        private static string DetectNativesRelativePath(List<ArgumentEntry> arguments)
        {
            const string property = "-Djava.library.path=";
            for (int i = 0; i < arguments.Count; i++)
            {
                ArgumentEntry entry = arguments[i];
                if (entry == null || !RuleEvaluator.IsAllowed(entry.Rules, null))
                {
                    continue;
                }
                for (int j = 0; j < entry.Values.Count; j++)
                {
                    string value = entry.Values[j];
                    if (value == null ||
                        !value.StartsWith(property, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string relative;
                    if (TryGetNativesSubPath(value.Substring(property.Length), out relative))
                    {
                        return relative;
                    }
                }
            }
            return "";
        }

        private static List<string> DetectNativesWorkDirectories(List<ArgumentEntry> arguments)
        {
            string[] properties = new string[]
            {
                "-Djna.tmpdir=",
                "-Dorg.lwjgl.system.SharedLibraryExtractPath=",
                "-Dio.netty.native.workdir="
            };
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arguments.Count; i++)
            {
                ArgumentEntry entry = arguments[i];
                if (entry == null || !RuleEvaluator.IsAllowed(entry.Rules, null))
                {
                    continue;
                }
                for (int j = 0; j < entry.Values.Count; j++)
                {
                    string value = entry.Values[j];
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }
                    for (int k = 0; k < properties.Length; k++)
                    {
                        if (!value.StartsWith(properties[k], StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string relative;
                        if (TryGetNativesSubPath(value.Substring(properties[k].Length), out relative) &&
                            !string.IsNullOrEmpty(relative) &&
                            seen.Add(relative))
                        {
                            result.Add(relative);
                        }
                        break;
                    }
                }
            }
            return result;
        }

        private static bool TryGetNativesSubPath(string value, out string relative)
        {
            const string placeholder = "${natives_directory}";
            relative = "";
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string normalized = value.Replace('\\', '/');
            if (!normalized.StartsWith(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string suffix = normalized.Substring(placeholder.Length);
            if (suffix.Length == 0)
            {
                return true;
            }
            if (suffix[0] != '/')
            {
                return false;
            }
            suffix = suffix.Trim('/');
            if (suffix.Length == 0)
            {
                return true;
            }
            if (!PathUtil.IsSafeRelativePath(suffix))
            {
                throw new InvalidDataException("版本 JSON 中的 natives 子目录无效：" + value);
            }
            relative = suffix;
            return true;
        }

        public static string GetClientJarPath(string gameDir, EffectiveVersion version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            string jarId = string.IsNullOrEmpty(version.ClientJarVersionId)
                ? version.Id
                : version.ClientJarVersionId;
            return PathUtil.SafeCombine(
                GetVersionDirectory(gameDir, jarId),
                jarId + ".jar");
        }

        public static string GetAssetsDirectory(string gameDir)
        {
            return Path.Combine(gameDir, "assets");
        }

        public static string GetLibrariesDirectory(string gameDir)
        {
            return Path.Combine(gameDir, "libraries");
        }

        public static string GetAssetIndexPath(string gameDir, string assetsId)
        {
            if (string.IsNullOrEmpty(assetsId))
            {
                throw new ArgumentException("资源索引名称为空。", "assetsId");
            }
            return PathUtil.SafeCombine(
                Path.Combine(GetAssetsDirectory(gameDir), "indexes"),
                assetsId + ".json");
        }

        public static bool ReadAssetIndexVirtual(string gameDir, string assetsId)
        {
            if (string.IsNullOrEmpty(assetsId))
            {
                return false;
            }
            string path = GetAssetIndexPath(gameDir, assetsId);
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                Dictionary<string, object> root = JsonUtil.ParseObject(File.ReadAllText(path, Encoding.UTF8));
                return JsonUtil.GetBool(root, "virtual", false);
            }
            catch
            {
                return false;
            }
        }

        private static VersionJson LoadRaw(
            string gameDir,
            string versionId,
            HashSet<string> stack,
            int depth)
        {
            if (depth > MaxInheritDepth)
            {
                throw new InvalidDataException("版本继承层级过深：" + versionId);
            }
            PathUtil.ValidateVersionId(versionId);
            if (!stack.Add(versionId))
            {
                throw new InvalidDataException("检测到版本继承循环：" + versionId);
            }

            string path = GetVersionJsonPath(gameDir, versionId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "未找到版本 " + versionId + " 的 JSON 文件。请先安装该版本。",
                    path);
            }
            FileInfo info = new FileInfo(path);
            if (info.Length > MaxVersionJsonBytes)
            {
                throw new InvalidDataException("版本 JSON 过大：" + path);
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            VersionJson raw = ParseVersionJson(json, path);
            if (!string.IsNullOrEmpty(raw.Id) &&
                !string.Equals(raw.Id, versionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("版本 JSON 的 id 与目录名不一致：" + versionId);
            }
            raw.Id = versionId;
            if (!string.IsNullOrEmpty(raw.InheritsFrom))
            {
                raw.Parent = LoadRaw(gameDir, raw.InheritsFrom, stack, depth + 1);
            }
            return raw;
        }

        private static EffectiveVersion Resolve(VersionJson raw)
        {
            EffectiveVersion parent = raw.Parent != null ? Resolve(raw.Parent) : null;
            EffectiveVersion version = new EffectiveVersion();
            version.Id = raw.Id;
            version.GameVersionId = parent != null && !string.IsNullOrEmpty(parent.GameVersionId)
                ? parent.GameVersionId
                : FirstNonEmpty(raw.InheritsFrom, raw.Id, "");
            version.Type = FirstNonEmpty(raw.Type, parent != null ? parent.Type : null, "release");
            version.MainClass = FirstNonEmpty(
                raw.MainClass,
                parent != null ? parent.MainClass : null,
                "");
            if (!IsValidClassName(version.MainClass))
            {
                throw new InvalidDataException("版本 " + raw.Id + " 的 mainClass 无效。");
            }

            if (raw.Client != null)
            {
                version.ClientJar = raw.Client;
                version.ClientJarVersionId = raw.Id;
            }
            else if (parent != null)
            {
                version.ClientJar = parent.ClientJar;
                version.ClientJarVersionId = parent.ClientJarVersionId;
            }
            else
            {
                version.ClientJar = null;
                version.ClientJarVersionId = raw.Id;
            }
            if (!string.IsNullOrEmpty(raw.Jar))
            {
                version.ClientJarVersionId = raw.Jar;
            }
            PathUtil.ValidateVersionId(version.ClientJarVersionId);

            version.AssetIndex = raw.AssetIndex != null
                ? raw.AssetIndex
                : (parent != null ? parent.AssetIndex : null);
            version.AssetsId = FirstNonEmpty(
                raw.Assets,
                parent != null ? parent.AssetsId : null,
                "");

            if (raw.Logging != null)
            {
                version.LoggingArgument = raw.Logging.Argument;
                version.LoggingFile = raw.Logging.File;
            }
            else if (parent != null)
            {
                version.LoggingArgument = parent.LoggingArgument;
                version.LoggingFile = parent.LoggingFile;
            }

            version.JavaVersionMajor = raw.JavaVersionMajor > 0
                ? raw.JavaVersionMajor
                : (parent != null ? parent.JavaVersionMajor : 0);

            version.GameArguments.AddRange(raw.GameArguments);
            version.JvmArguments.AddRange(raw.JvmArguments);
            if (parent != null)
            {
                version.GameArguments.AddRange(parent.GameArguments);
                version.JvmArguments.AddRange(parent.JvmArguments);
            }

            version.LegacyMinecraftArguments = FirstNonEmpty(
                raw.MinecraftArguments,
                parent != null ? parent.LegacyMinecraftArguments : null,
                "");

            version.NativesRelativePath = DetectNativesRelativePath(version.JvmArguments);
            version.NativesWorkDirectories = DetectNativesWorkDirectories(version.JvmArguments);

            List<NativeArtifact> childNatives;
            List<DownloadInfo> childClasspath = ProcessLibraries(
                raw.Libraries,
                !string.IsNullOrEmpty(version.NativesRelativePath),
                out childNatives);
            AddUniqueClasspath(version.ClasspathArtifacts, childClasspath);
            AddUniqueNatives(version.NativeArtifacts, childNatives);
            if (parent != null)
            {
                AddUniqueClasspath(version.ClasspathArtifacts, parent.ClasspathArtifacts);
                AddUniqueNatives(version.NativeArtifacts, parent.NativeArtifacts);
                version.Warnings.AddRange(parent.Warnings);
            }
            return version;
        }

        private static List<DownloadInfo> ProcessLibraries(
            List<LibraryInfo> libraries,
            bool useNativeClasspath,
            out List<NativeArtifact> natives)
        {
            List<DownloadInfo> classpath = new List<DownloadInfo>();
            natives = new List<NativeArtifact>();
            HashSet<string> classpathPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> nativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, NativeCandidate> bestNatives =
                new Dictionary<string, NativeCandidate>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, NativeCandidate> bestClasspathNatives =
                new Dictionary<string, NativeCandidate>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < libraries.Count; i++)
            {
                LibraryInfo library = libraries[i];
                if (!RuleEvaluator.IsAllowed(library.Rules, null))
                {
                    continue;
                }

                bool nativeOnly = IsNativeOnly(library);
                // 26.2+ 把平台 native JAR 作为普通 classifier 依赖写入 downloads.artifact，
                // 并且 java.library.path 指向 natives/java。此时 native JAR 必须留在 classpath：
                // LWJGL 会从 classpath 提取 DLL，jtracy 也通过 ClassLoader.getResourceAsStream 读取 DLL。
                bool nativeOnClasspath = useNativeClasspath &&
                                         nativeOnly &&
                                         library.Artifact != null &&
                                         library.Natives.Count == 0;
                bool hasOnlyNativeClassifiers = library.Artifact == null &&
                                                library.Natives.Count > 0 &&
                                                library.Classifiers.Count > 0;

                if (!nativeOnly && !hasOnlyNativeClassifiers)
                {
                    DownloadInfo artifact = BuildDownload(library, null, library.Artifact);
                    if (artifact == null)
                    {
                        throw new InvalidDataException("库缺少下载地址：" + library.Name);
                    }
                    AddUniqueDownload(classpath, classpathPaths, artifact);
                }

                if (library.Natives.Count > 0)
                {
                    string classifier = SelectNativesClassifier(library.Natives);
                    if (!string.IsNullOrEmpty(classifier))
                    {
                        DownloadInfo classifierDownload = null;
                        if (library.Classifiers.ContainsKey(classifier))
                        {
                            classifierDownload = BuildDownload(
                                library,
                                classifier,
                                library.Classifiers[classifier]);
                        }
                        else
                        {
                            classifierDownload = BuildDownload(library, classifier, null);
                        }
                        if (classifierDownload != null)
                        {
                            AddUniqueNative(
                                natives,
                                nativePaths,
                                new NativeArtifact
                                {
                                    Name = library.Name,
                                    Classifier = classifier,
                                    Download = classifierDownload
                                });
                        }
                    }
                }

                if (nativeOnly)
                {
                    DownloadInfo nativeDownload = BuildDownload(library, library.Classifier, library.Artifact);
                    if (nativeDownload != null)
                    {
                        int rank = GetNativeArchRank(library.Classifier);
                        Dictionary<string, NativeCandidate> candidates = nativeOnClasspath
                            ? bestClasspathNatives
                            : bestNatives;
                        NativeCandidate existing;
                        if (!candidates.TryGetValue(library.BaseName, out existing) || rank > existing.Rank)
                        {
                            NativeCandidate candidate = new NativeCandidate();
                            candidate.Download = nativeDownload;
                            candidate.Rank = rank;
                            candidate.Name = library.Name;
                            candidate.Classifier = library.Classifier;
                            candidates[library.BaseName] = candidate;
                        }
                    }
                }
            }

            // 新布局：每个库只选择与当前架构匹配的 native JAR，并作为 classpath 条目保留。
            foreach (NativeCandidate candidate in bestClasspathNatives.Values)
            {
                if (candidate.Rank <= 0)
                {
                    continue;
                }
                AddUniqueDownload(classpath, classpathPaths, candidate.Download);
            }

            foreach (NativeCandidate candidate in bestNatives.Values)
            {
                if (candidate.Rank <= 0)
                {
                    continue;
                }
                AddUniqueNative(
                    natives,
                    nativePaths,
                    new NativeArtifact
                    {
                        Name = candidate.Name,
                        Classifier = candidate.Classifier,
                        Download = candidate.Download
                    });
            }
            return classpath;
        }

        private static bool IsNativeOnly(LibraryInfo library)
        {
            return !string.IsNullOrEmpty(library.Classifier) &&
                   library.Classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase);
        }

        private static string SelectNativesClassifier(Dictionary<string, string> natives)
        {
            string template;
            if (!natives.TryGetValue("windows", out template) || string.IsNullOrEmpty(template))
            {
                return null;
            }
            string arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            return template.Replace("${arch}", arch);
        }

        private static int GetNativeArchRank(string classifier)
        {
            if (string.IsNullOrEmpty(classifier))
            {
                return 0;
            }
            string value = classifier.ToLowerInvariant();
            bool arm64 = value.IndexOf("arm64", StringComparison.Ordinal) >= 0 ||
                         value.IndexOf("aarch64", StringComparison.Ordinal) >= 0;
            bool x86 = value.IndexOf("x86", StringComparison.Ordinal) >= 0 &&
                       value.IndexOf("x86_64", StringComparison.Ordinal) < 0 &&
                       value.IndexOf("amd64", StringComparison.Ordinal) < 0;
            bool windows = value.IndexOf("windows", StringComparison.Ordinal) >= 0;
            string current = RuleEvaluator.CurrentOsArch;

            if (string.Equals(current, "arm64", StringComparison.OrdinalIgnoreCase))
            {
                if (arm64)
                {
                    return 4;
                }
                if (windows && !x86)
                {
                    return 1;
                }
                return 0;
            }
            if (string.Equals(current, "x86", StringComparison.OrdinalIgnoreCase))
            {
                if (x86)
                {
                    return 4;
                }
                if (windows && !arm64)
                {
                    return 1;
                }
                return 0;
            }
            if (windows && !arm64 && !x86)
            {
                return 3;
            }
            if (windows)
            {
                return 0;
            }
            return 2;
        }

        private static DownloadInfo BuildDownload(
            LibraryInfo library,
            string classifier,
            DownloadInfo source)
        {
            string path = source != null ? source.Path : null;
            if (string.IsNullOrEmpty(path))
            {
                path = BuildMavenPath(library, classifier);
            }
            string url = source != null ? source.Url : null;
            if (string.IsNullOrEmpty(url))
            {
                url = CombineUrl(library.UrlBase, path);
            }
            else if (url.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                url = CombineUrl(library.UrlBase, url);
            }
            string sha1 = source != null ? source.Sha1 : null;
            if (string.IsNullOrEmpty(sha1))
            {
                sha1 = library.DirectSha1;
            }
            long size = source != null ? source.Size : 0;
            if (size <= 0)
            {
                size = library.DirectSize;
            }
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }
            if (!HttpUtil.IsAllowedUrl(url))
            {
                throw new InvalidDataException("库下载地址不在允许列表：" + url);
            }
            if (!string.IsNullOrEmpty(sha1) && !PathUtil.IsSha1(sha1))
            {
                throw new InvalidDataException("库的 SHA1 格式无效：" + library.Name);
            }
            if (!PathUtil.IsSafeRelativePath(path))
            {
                throw new InvalidDataException("库路径无效：" + library.Name);
            }
            return new DownloadInfo(path, url, sha1, size);
        }

        private static string BuildMavenPath(LibraryInfo library, string classifier)
        {
            string actualClassifier = classifier;
            if (string.IsNullOrEmpty(actualClassifier))
            {
                actualClassifier = library.Classifier;
            }
            string fileName = library.ArtifactId + "-" + library.Version;
            if (!string.IsNullOrEmpty(actualClassifier))
            {
                fileName += "-" + actualClassifier;
            }
            fileName += "." + library.Extension;
            return library.GroupId.Replace('.', '/') + "/" +
                   library.ArtifactId + "/" +
                   library.Version + "/" +
                   fileName;
        }

        private static string CombineUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return baseUrl;
            }
            if (relative.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                return relative;
            }
            if (string.IsNullOrEmpty(baseUrl))
            {
                return relative;
            }
            return baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
        }

        private static void AddUniqueDownload(
            List<DownloadInfo> list,
            HashSet<string> paths,
            DownloadInfo download)
        {
            if (download == null || string.IsNullOrEmpty(download.Path))
            {
                return;
            }
            if (paths.Add(download.Path))
            {
                list.Add(download);
            }
        }

        private static void AddUniqueNative(
            List<NativeArtifact> list,
            HashSet<string> paths,
            NativeArtifact artifact)
        {
            if (artifact == null || artifact.Download == null ||
                string.IsNullOrEmpty(artifact.Download.Path))
            {
                return;
            }
            if (paths.Add(artifact.Download.Path))
            {
                list.Add(artifact);
            }
        }

        private static void AddUniqueClasspath(List<DownloadInfo> target, List<DownloadInfo> source)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < target.Count; i++)
            {
                seen.Add(target[i].Path);
            }
            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrEmpty(source[i].Path) && seen.Add(source[i].Path))
                {
                    target.Add(source[i]);
                }
            }
        }

        private static void AddUniqueNatives(List<NativeArtifact> target, List<NativeArtifact> source)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < target.Count; i++)
            {
                seen.Add(target[i].Download.Path);
            }
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Download != null &&
                    !string.IsNullOrEmpty(source[i].Download.Path) &&
                    seen.Add(source[i].Download.Path))
                {
                    target.Add(source[i]);
                }
            }
        }

        private static LibraryInfo ParseLibrary(IDictionary<string, object> item)
        {
            string name = JsonUtil.GetString(item, "name");
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException("库条目缺少 name。");
            }
            LibraryInfo library = ParseMavenName(name);
            library.UrlBase = JsonUtil.GetString(item, "url");
            library.DirectSha1 = JsonUtil.GetString(item, "sha1");
            library.DirectSize = JsonUtil.GetLong(item, "size", 0);
            if (!string.IsNullOrEmpty(library.DirectSha1) && !PathUtil.IsSha1(library.DirectSha1))
            {
                throw new InvalidDataException("库的 SHA1 格式无效：" + name);
            }
            library.Rules = ParseRules(JsonUtil.GetArray(item, "rules"));

            IDictionary<string, object> natives = JsonUtil.GetObject(item, "natives");
            if (natives != null)
            {
                foreach (KeyValuePair<string, object> pair in natives)
                {
                    string classifier = pair.Value as string;
                    if (!string.IsNullOrEmpty(classifier))
                    {
                        library.Natives[pair.Key] = classifier;
                    }
                }
            }

            IDictionary<string, object> downloads = JsonUtil.GetObject(item, "downloads");
            if (downloads != null)
            {
                library.Artifact = ParseDownload(JsonUtil.GetObject(downloads, "artifact"));
                IDictionary<string, object> classifiers = JsonUtil.GetObject(downloads, "classifiers");
                if (classifiers != null)
                {
                    foreach (KeyValuePair<string, object> pair in classifiers)
                    {
                        IDictionary<string, object> value = JsonUtil.AsObject(pair.Value);
                        DownloadInfo download = ParseDownload(value);
                        if (download != null)
                        {
                            library.Classifiers[pair.Key] = download;
                        }
                    }
                }
            }
            return library;
        }

        private static LibraryInfo ParseMavenName(string name)
        {
            string raw = name.Trim();
            string extension = "jar";
            int at = raw.IndexOf('@');
            if (at >= 0)
            {
                extension = raw.Substring(at + 1);
                raw = raw.Substring(0, at);
            }
            string[] parts = raw.Split(':');
            if (parts.Length < 3 || parts.Length > 4)
            {
                throw new InvalidDataException("无效的 Maven 名称：" + name);
            }
            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length == 4 ? parts[3] : null;
            if (!IsSafeMavenPart(group) || !IsSafeMavenPart(artifact) ||
                !IsSafeMavenPart(version) || (classifier != null && !IsSafeMavenPart(classifier)) ||
                !IsSafeMavenPart(extension))
            {
                throw new InvalidDataException("Maven 名称包含非法路径字符：" + name);
            }
            LibraryInfo library = new LibraryInfo();
            library.Name = name;
            library.GroupId = group;
            library.ArtifactId = artifact;
            library.Version = version;
            library.Classifier = classifier;
            library.Extension = extension;
            library.BaseName = group + ":" + artifact + ":" + version;
            return library;
        }

        private static bool IsSafeMavenPart(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == "..")
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '.' || c == '-' || c == '_' || c == '+';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        private static List<Rule> ParseRules(IList array)
        {
            List<Rule> rules = new List<Rule>();
            if (array == null)
            {
                return rules;
            }
            for (int i = 0; i < array.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(array[i]);
                if (item == null)
                {
                    continue;
                }
                Rule rule = new Rule();
                rule.Action = JsonUtil.GetString(item, "action", "allow");
                IDictionary<string, object> os = JsonUtil.GetObject(item, "os");
                if (os != null)
                {
                    rule.OsName = JsonUtil.GetString(os, "name");
                    rule.OsArch = JsonUtil.GetString(os, "arch");
                    rule.OsVersion = JsonUtil.GetString(os, "version");
                }
                IDictionary<string, object> features = JsonUtil.GetObject(item, "features");
                if (features != null)
                {
                    foreach (KeyValuePair<string, object> pair in features)
                    {
                        bool value = false;
                        if (pair.Value is bool)
                        {
                            value = (bool)pair.Value;
                        }
                        else
                        {
                            bool.TryParse(Convert.ToString(pair.Value, CultureInfo.InvariantCulture), out value);
                        }
                        rule.Features[pair.Key] = value;
                    }
                }
                rules.Add(rule);
            }
            return rules;
        }

        private static List<ArgumentEntry> ParseArguments(IList array)
        {
            List<ArgumentEntry> result = new List<ArgumentEntry>();
            if (array == null)
            {
                return result;
            }
            for (int i = 0; i < array.Count; i++)
            {
                object item = array[i];
                string text = item as string;
                if (text != null)
                {
                    ArgumentEntry stringEntry = new ArgumentEntry();
                    stringEntry.Values.Add(text);
                    result.Add(stringEntry);
                    continue;
                }
                IDictionary<string, object> obj = JsonUtil.AsObject(item);
                if (obj == null)
                {
                    continue;
                }
                ArgumentEntry entry = new ArgumentEntry();
                entry.Rules = ParseRules(JsonUtil.GetArray(obj, "rules"));
                object value;
                if (obj.TryGetValue("value", out value))
                {
                    string valueText = value as string;
                    if (valueText != null)
                    {
                        entry.Values.Add(valueText);
                    }
                    else
                    {
                        IList valueList = JsonUtil.AsList(value);
                        if (valueList != null)
                        {
                            for (int j = 0; j < valueList.Count; j++)
                            {
                                if (valueList[j] != null)
                                {
                                    entry.Values.Add(Convert.ToString(
                                        valueList[j],
                                        CultureInfo.InvariantCulture));
                                }
                            }
                        }
                    }
                }
                result.Add(entry);
            }
            return result;
        }

        private static DownloadInfo ParseDownload(IDictionary<string, object> item)
        {
            if (item == null)
            {
                return null;
            }
            DownloadInfo download = new DownloadInfo();
            download.Path = JsonUtil.GetString(item, "path");
            download.Url = JsonUtil.GetString(item, "url");
            download.Sha1 = JsonUtil.GetString(item, "sha1");
            download.Size = JsonUtil.GetLong(item, "size", 0);
            if (!string.IsNullOrEmpty(download.Sha1) && !PathUtil.IsSha1(download.Sha1))
            {
                throw new InvalidDataException("下载项 SHA1 格式无效：" + download.Url);
            }
            return download;
        }

        private static string FirstNonEmpty(string first, string second, string fallback)
        {
            if (!string.IsNullOrEmpty(first))
            {
                return first;
            }
            if (!string.IsNullOrEmpty(second))
            {
                return second;
            }
            return fallback;
        }

        private static bool IsValidClassName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool first = i == 0;
                bool ok;
                if (first)
                {
                    ok = (c >= 'a' && c <= 'z') ||
                         (c >= 'A' && c <= 'Z') ||
                         c == '_' || c == '$';
                }
                else
                {
                    ok = (c >= 'a' && c <= 'z') ||
                         (c >= 'A' && c <= 'Z') ||
                         (c >= '0' && c <= '9') ||
                         c == '_' || c == '$' || c == '.';
                }
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class NativeCandidate
        {
            public DownloadInfo Download;
            public int Rank;
            public string Name;
            public string Classifier;
        }
    }
}
