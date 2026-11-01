using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 下载源镜像映射。目标：官方源不可用时切换到镜像，覆盖
    /// 版本清单、版本 JSON、库与资源、Fabric 安装相关下载。
    ///
    /// 设计取舍：
    /// - 只做"官方 URL → 镜像 URL"的纯字符串映射，**不改变任何校验逻辑**：
    ///   镜像只替换地址，SHA1 校验仍然针对官方清单里的哈希，因此镜像投毒会被发现。
    /// - Modrinth API 不做镜像替换（它的响应包含签名链路与版本语义，镜像易过期）。
    /// - 自定义镜像必须是 HTTPS 站点，保存时加入运行时白名单（与认证服务器同机制）。
    /// </summary>
    public static class DownloadMirrors
    {
        public const string Official = "official";
        public const string BmclApi = "bmclapi";

        /// <summary>BMCLAPI 镜像根地址。国内用户常用，覆盖 Mojang 与 Fabric 的常用资源。</summary>
        public const string BmclApiHost = "bmclapi2.bangbang93.com";
        public const string BmclApiBase = "https://bmclapi2.bangbang93.com";

        /// <summary>
        /// 官方主机 → BMCLAPI 路径前缀的映射。
        /// 例：https://piston-meta.mojang.com/... → https://bmclapi2.bangbang93.com/...
        /// </summary>
        private static readonly Dictionary<string, string> HostMap = CreateHostMap();

        private static Dictionary<string, string> CreateHostMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            // 版本清单与版本 JSON
            map["piston-meta.mojang.com"] = BmclApiBase;
            map["launchermeta.mojang.com"] = BmclApiBase;
            // 客户端 JAR 与资源对象
            map["piston-data.mojang.com"] = BmclApiBase;
            map["launcher.mojang.com"] = BmclApiBase;
            map["resources.download.minecraft.net"] = BmclApiBase + "/resources";
            // 库文件
            map["libraries.minecraft.net"] = BmclApiBase + "/maven";
            // Fabric 相关
            map["meta.fabricmc.net"] = BmclApiBase + "/fabric-meta";
            map["maven.fabricmc.net"] = BmclApiBase + "/maven";
            return map;
        }

        /// <summary>
        /// 判断一个字符串是否为受支持的自定义镜像地址（HTTPS + 合法主机）。
        /// 用于配置校验与设置页保存时的即时校验。
        /// </summary>
        public static bool IsValidCustomMirror(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (uri.UserInfo.Length > 0)
            {
                return false;
            }
            if (uri.HostNameType != UriHostNameType.Dns)
            {
                return false;
            }
            if (uri.Host.EndsWith(".", StringComparison.Ordinal))
            {
                return false;
            }
            return uri.Port == 443;
        }

        /// <summary>
        /// 取镜像地址对应的主机名（用于加入白名单）。非法地址返回空串。
        /// </summary>
        public static string GetMirrorHost(string mirror)
        {
            if (string.Equals(mirror, BmclApi, StringComparison.OrdinalIgnoreCase))
            {
                return BmclApiHost;
            }
            Uri uri;
            if (IsValidCustomMirror(mirror) && Uri.TryCreate(mirror.Trim(), UriKind.Absolute, out uri))
            {
                return uri.Host;
            }
            return "";
        }

        /// <summary>
        /// 把官方 URL 映射到镜像 URL。mirror 为 official 时原样返回。
        /// 非官方主机、或不在映射表内的主机保持原样（例如 Modrinth 的 cdn 域名）。
        /// 这是纯函数：不做网络请求，便于离线测试与推理。
        /// </summary>
        public static string Map(string url, string mirror)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            if (string.IsNullOrEmpty(mirror) ||
                string.Equals(mirror, Official, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            Uri uri;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    return url;
                }
            }
            catch
            {
                return url;
            }

            // Modrinth 不做镜像：保留原地址。
            if (string.Equals(uri.Host, "api.modrinth.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            string prefix;
            if (string.Equals(mirror, BmclApi, StringComparison.OrdinalIgnoreCase))
            {
                if (!HostMap.TryGetValue(uri.Host, out prefix))
                {
                    return url;
                }
            }
            else
            {
                // 自定义镜像：只接管映射表内已知的官方主机，其余保持官方地址，
                // 避免把未知主机也转发到用户填写的第三方站点。
                if (!HostMap.TryGetValue(uri.Host, out prefix))
                {
                    return url;
                }
                prefix = mirror.TrimEnd('/');
            }

            string mapped = prefix + uri.PathAndQuery;
            // 映射结果必须是合法 HTTPS，否则放弃映射（宁可用官方源）。
            if (!IsValidCustomMirror(mapped))
            {
                return url;
            }
            return mapped;
        }

        public static bool IsMirrorEnabled(string mirror)
        {
            return !string.IsNullOrEmpty(mirror) &&
                   !string.Equals(mirror, Official, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetDisplayName(string mirror)
        {
            if (string.IsNullOrEmpty(mirror) ||
                string.Equals(mirror, Official, StringComparison.OrdinalIgnoreCase))
            {
                return "官方源";
            }
            if (string.Equals(mirror, BmclApi, StringComparison.OrdinalIgnoreCase))
            {
                return "BmclAPI 镜像";
            }
            return "自定义镜像（" + GetMirrorHost(mirror) + "）";
        }
    }
}
