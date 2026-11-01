using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    public sealed class FabricInstaller
    {
        private readonly MinecraftInstaller _installer;
        private readonly VersionManifestService _manifestService;

        public FabricInstaller(MinecraftInstaller installer, VersionManifestService manifestService)
        {
            if (installer == null)
            {
                throw new ArgumentNullException("installer");
            }
            _installer = installer;
            _manifestService = manifestService ?? new VersionManifestService();
        }

        public async Task<string> InstallAsync(
            string gameVersionId,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            PathUtil.ValidateVersionId(gameVersionId);
            Report(progress, 0, "获取版本清单");
            VersionManifest manifest = await _manifestService.GetManifestAsync(cancellationToken)
                .ConfigureAwait(false);
            VersionSummary gameVersion = manifest.Find(gameVersionId);
            if (gameVersion == null)
            {
                throw new InvalidDataException("版本清单中找不到 " + gameVersionId);
            }

            if (!VersionResolver.IsInstalled(_installer.GameDir, gameVersionId))
            {
                MappedProgress vanillaProgress = new MappedProgress(progress, 0, 45, "安装原版：");
                await _installer.InstallVanillaAsync(
                    gameVersion,
                    vanillaProgress,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Report(progress, 45, "原版 " + gameVersionId + " 已安装");
            }

            string loaderVersion = await GetLatestStableLoaderAsync(
                gameVersionId,
                progress,
                cancellationToken).ConfigureAwait(false);
            Report(progress, 52, "下载 Fabric 配置");
            string profileUrl = string.Format(
                "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json",
                Uri.EscapeDataString(gameVersionId),
                Uri.EscapeDataString(loaderVersion));
            string profileJson = await HttpUtil.GetStringAsync(profileUrl, cancellationToken)
                .ConfigureAwait(false);
            VersionJson profile = VersionResolver.ParseVersionJson(profileJson, profileUrl);
            ValidateProfile(profile, gameVersionId);

            string profilePath = VersionResolver.GetVersionJsonPath(_installer.GameDir, profile.Id);
            PathUtil.WriteAllTextAtomic(profilePath, profileJson);
            Report(progress, 58, "Fabric 配置已保存，开始安装库文件");

            MappedProgress fabricProgress = new MappedProgress(progress, 55, 100, "安装 Fabric：");
            await _installer.InstallResolvedAsync(
                profile.Id,
                fabricProgress,
                cancellationToken).ConfigureAwait(false);
            return profile.Id;
        }

        private static async Task<string> GetLatestStableLoaderAsync(
            string gameVersionId,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            Report(progress, 46, "查询 Fabric 加载器");
            string url = "https://meta.fabricmc.net/v2/versions/loader/" +
                         Uri.EscapeDataString(gameVersionId);
            string json;
            try
            {
                json = await HttpUtil.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                if (IsUnsupportedVersionError(ex))
                {
                    throw new InvalidOperationException(
                        "该 Minecraft 版本不受 Fabric 支持：" + gameVersionId +
                        "。请选择 Fabric 已支持的版本（通常为 1.14 或更高版本）。", ex);
                }
                throw;
            }
            IList list = JsonUtil.AsList(JsonUtil.Parse(json));
            if (list == null || list.Count == 0)
            {
                throw new InvalidDataException("Fabric 没有返回可用的加载器版本。");
            }

            string firstValid = null;
            for (int i = 0; i < list.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(list[i]);
                if (item == null)
                {
                    continue;
                }
                IDictionary<string, object> loader = JsonUtil.GetObject(item, "loader");
                if (loader == null)
                {
                    continue;
                }
                string version = JsonUtil.GetString(loader, "version");
                if (!PathUtil.IsValidVersionId(version))
                {
                    continue;
                }
                if (firstValid == null)
                {
                    firstValid = version;
                }
                if (JsonUtil.GetBool(loader, "stable", false))
                {
                    return version;
                }
            }
            if (!string.IsNullOrEmpty(firstValid))
            {
                Log.Warn("Fabric 没有标记为 stable 的加载器，使用最新可用版本 " + firstValid);
                return firstValid;
            }
            throw new InvalidDataException("Fabric 加载器版本格式无效。");
        }

        private static void ValidateProfile(VersionJson profile, string gameVersionId)
        {
            if (profile == null)
            {
                throw new InvalidDataException("Fabric 配置为空。");
            }
            if (!PathUtil.IsValidVersionId(profile.Id) ||
                !profile.Id.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Fabric 配置的版本名无效。");
            }
            if (!string.Equals(profile.InheritsFrom, gameVersionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Fabric 配置继承的版本与所选版本不一致。");
            }
            if (string.IsNullOrEmpty(profile.MainClass) ||
                !profile.MainClass.StartsWith("net.fabricmc.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Fabric 配置的 mainClass 不在允许的命名空间内。");
            }
            if (profile.Libraries.Count == 0)
            {
                throw new InvalidDataException("Fabric 配置没有库文件。");
            }
            for (int i = 0; i < profile.Libraries.Count; i++)
            {
                LibraryInfo library = profile.Libraries[i];
                if (!string.IsNullOrEmpty(library.UrlBase) && !HttpUtil.IsAllowedUrl(library.UrlBase))
                {
                    throw new InvalidDataException("Fabric 库地址不在允许列表：" + library.UrlBase);
                }
                if (library.Artifact != null &&
                    !string.IsNullOrEmpty(library.Artifact.Url) &&
                    !HttpUtil.IsAllowedUrl(library.Artifact.Url))
                {
                    throw new InvalidDataException("Fabric 库地址不在允许列表：" + library.Artifact.Url);
                }
            }

            ValidateJvmArguments(profile.JvmArguments);
        }

        private static void ValidateJvmArguments(List<ArgumentEntry> arguments)
        {
            if (arguments == null)
            {
                return;
            }
            // 与用户自定义参数使用同一套策略（参数名精确匹配）。
            // 旧实现是各自维护子串黑名单，且漏掉了 -XX:OnError 系列，
            // 上游被攻破时可借此在崩溃时执行命令。
            for (int i = 0; i < arguments.Count; i++)
            {
                for (int j = 0; j < arguments[i].Values.Count; j++)
                {
                    string value = arguments[i].Values[j];
                    if (JvmArgumentPolicy.IsBanned(value))
                    {
                        throw new InvalidDataException(
                            "Fabric 配置包含不允许的 JVM 参数：" + value);
                    }
                }
            }
        }

        private static bool IsUnsupportedVersionError(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                string message = current.Message ?? "";
                if (message.IndexOf("HTTP 400", StringComparison.Ordinal) >= 0 ||
                    message.IndexOf("HTTP 404", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string status)
        {
            if (progress != null)
            {
                progress.Report(new InstallProgress(percent, status));
            }
        }

        private sealed class MappedProgress : IProgress<InstallProgress>
        {
            private readonly IProgress<InstallProgress> _inner;
            private readonly int _start;
            private readonly int _end;
            private readonly string _prefix;

            public MappedProgress(
                IProgress<InstallProgress> inner,
                int start,
                int end,
                string prefix)
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
