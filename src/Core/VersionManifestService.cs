using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    public sealed class VersionManifestService
    {
        public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public async Task<VersionManifest> GetManifestAsync(CancellationToken cancellationToken)
        {
            string json = await HttpUtil.GetStringAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.ParseObject(json);
            VersionManifest manifest = new VersionManifest();

            IDictionary<string, object> latest = JsonUtil.GetObject(root, "latest");
            if (latest != null)
            {
                manifest.LatestRelease = JsonUtil.GetString(latest, "release");
                manifest.LatestSnapshot = JsonUtil.GetString(latest, "snapshot");
            }

            IList versions = JsonUtil.GetArray(root, "versions");
            if (versions == null)
            {
                throw new FormatException("版本清单缺少 versions 数组。");
            }
            int skipped = 0;
            for (int i = 0; i < versions.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(versions[i]);
                if (item == null)
                {
                    continue;
                }
                string id = JsonUtil.GetString(item, "id");
                string type = JsonUtil.GetString(item, "type");
                string url = JsonUtil.GetString(item, "url");
                string sha1 = JsonUtil.GetString(item, "sha1");
                long size = JsonUtil.GetLong(item, "size", 0);
                if (!PathUtil.IsValidVersionId(id) || !HttpUtil.IsAllowedUrl(url) || !PathUtil.IsSha1(sha1))
                {
                    skipped++;
                    continue;
                }
                VersionSummary summary = new VersionSummary();
                summary.Id = id;
                summary.Type = type;
                summary.Url = url;
                summary.Sha1 = sha1.ToLowerInvariant();
                summary.Size = size;
                summary.ReleaseTime = JsonUtil.GetString(item, "releaseTime");
                DateTimeOffset releaseTime;
                if (DateTimeOffset.TryParse(
                    summary.ReleaseTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out releaseTime))
                {
                    summary.ReleaseTimeUtc = releaseTime;
                }
                manifest.Versions.Add(summary);
            }

            if (manifest.Versions.Count == 0)
            {
                throw new FormatException("版本清单为空。");
            }
            if (skipped > 0)
            {
                Log.Warn("版本清单中有 " + skipped + " 个条目因名称或下载地址不合规被忽略。");
            }
            return manifest;
        }

        public static string GetTypeName(string type)
        {
            if (string.Equals(type, "release", StringComparison.OrdinalIgnoreCase))
            {
                return "正式版";
            }
            if (string.Equals(type, "snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return "快照";
            }
            if (string.Equals(type, "old_beta", StringComparison.OrdinalIgnoreCase))
            {
                return "旧测试版";
            }
            if (string.Equals(type, "old_alpha", StringComparison.OrdinalIgnoreCase))
            {
                return "旧预览版";
            }
            return string.IsNullOrEmpty(type) ? "未知" : type;
        }
    }
}
