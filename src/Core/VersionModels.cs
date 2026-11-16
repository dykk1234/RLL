using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    public sealed class DownloadInfo
    {
        public string Path;
        public string Url;
        public string Sha1;
        public long Size;

        public DownloadInfo()
        {
        }

        public DownloadInfo(string path, string url, string sha1, long size)
        {
            Path = path;
            Url = url;
            Sha1 = sha1;
            Size = size;
        }
    }

    public sealed class Rule
    {
        public string Action = "allow";
        public string OsName;
        public string OsArch;
        public string OsVersion;
        public Dictionary<string, bool> Features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ArgumentEntry
    {
        public List<Rule> Rules = new List<Rule>();
        public List<string> Values = new List<string>();
    }

    public sealed class LibraryInfo
    {
        public string Name;
        public string GroupId;
        public string ArtifactId;
        public string Version;
        public string Classifier;
        public string Extension = "jar";
        public string BaseName;
        public string UrlBase;
        public string DirectSha1;
        public long DirectSize;
        public List<Rule> Rules = new List<Rule>();
        public Dictionary<string, string> Natives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public DownloadInfo Artifact;
        public Dictionary<string, DownloadInfo> Classifiers =
            new Dictionary<string, DownloadInfo>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class LoggingInfo
    {
        public string Argument;
        public DownloadInfo File;
    }

    public sealed class VersionJson
    {
        public string Id;
        public string Type;
        public string MainClass;
        public string Assets;
        public string InheritsFrom;
        public string Jar;
        public string MinecraftArguments;
        public string SourcePath;
        public int JavaVersionMajor;
        public DownloadInfo Client;
        public DownloadInfo AssetIndex;
        public LoggingInfo Logging;
        public List<LibraryInfo> Libraries = new List<LibraryInfo>();
        public List<ArgumentEntry> GameArguments = new List<ArgumentEntry>();
        public List<ArgumentEntry> JvmArguments = new List<ArgumentEntry>();
        public VersionJson Parent;
    }

    public sealed class NativeArtifact
    {
        public string Name;
        public string Classifier;
        public DownloadInfo Download;
    }

    public sealed class EffectiveVersion
    {
        public string Id;
        public string Type;
        public string MainClass;
        public string AssetsId;
        // 实际 Minecraft 版本（Fabric 子版本会继承原版，例如 1.20.1）。
        public string GameVersionId;
        public string ClientJarVersionId;
        public string LoggingArgument;
        public int JavaVersionMajor;
        public DownloadInfo ClientJar;
        public DownloadInfo AssetIndex;
        public DownloadInfo LoggingFile;
        public List<DownloadInfo> ClasspathArtifacts = new List<DownloadInfo>();
        public List<NativeArtifact> NativeArtifacts = new List<NativeArtifact>();
        // 26.2+ 的 java.library.path 指向 natives 下的子目录（如 natives/java）。
        // 旧版本为空，表示 natives 根目录。
        public string NativesRelativePath = "";
        // jna.tmpdir、LWJGL 提取目录、Netty 工作目录等需要预先存在的子目录。
        public List<string> NativesWorkDirectories = new List<string>();
        public List<ArgumentEntry> GameArguments = new List<ArgumentEntry>();
        public List<ArgumentEntry> JvmArguments = new List<ArgumentEntry>();
        public string LegacyMinecraftArguments;
        public List<string> Warnings = new List<string>();
    }

    public sealed class InstallProgress
    {
        public int Percent;
        public string Status;

        public InstallProgress()
        {
        }

        public InstallProgress(int percent, string status)
        {
            Percent = percent;
            Status = status;
        }
    }

    public sealed class VersionSummary
    {
        public string Id;
        public string Type;
        public string Url;
        public string Sha1;
        public long Size;
        public string ReleaseTime;
        public DateTimeOffset ReleaseTimeUtc;
    }

    public sealed class VersionManifest
    {
        public string LatestRelease;
        public string LatestSnapshot;
        public List<VersionSummary> Versions = new List<VersionSummary>();

        public VersionSummary Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < Versions.Count; i++)
            {
                if (string.Equals(Versions[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return Versions[i];
                }
            }
            return null;
        }
    }

    public sealed class LaunchFeatures
    {
        public bool IsDemoUser;
        public bool HasCustomResolution;
        public bool HasQuickPlaysSupport;

        public Dictionary<string, bool> ToDictionary()
        {
            Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            values["is_demo_user"] = IsDemoUser;
            values["has_custom_resolution"] = HasCustomResolution;
            values["has_quick_plays_support"] = HasQuickPlaysSupport;
            values["is_quick_play_singleplayer"] = false;
            values["is_quick_play_multiplayer"] = false;
            values["is_quick_play_realms"] = false;
            return values;
        }
    }
}
