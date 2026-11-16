using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 认证服务器的注册页地址登记。
    ///
    /// 背景：启动器只提供"登录"，而新用户没有账号。注册本身包含邮箱验证、
    /// 人机校验与站点条款，无法由启动器代为完成（也不应代填凭据），
    /// 因此提供"把你送到正确页面"的入口。
    ///
    /// 地址来源优先级：
    /// 1) 服务器 yggdrasil 元数据里的 meta.links.register（最权威，随站点自适应）；
    /// 2) 内置已知站点的固定地址；
    /// 3) 兜底用 API 根地址去掉 /api/yggdrasil 后的站点首页。
    /// </summary>
    public static class AuthServerRegistry
    {
        /// <summary>已知站点：API 根地址（规范化后）→ 注册页地址。</summary>
        private static readonly Dictionary<string, string> KnownRegisters =
            CreateKnownRegisters();

        private static Dictionary<string, string> CreateKnownRegisters()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            // LittleSkin 的注册页；其元数据也提供了同一地址，这里作为离线兜底。
            map["https://littleskin.cn/api/yggdrasil"] = "https://littleskin.cn/auth/register";
            return map;
        }

        /// <summary>
        /// 取注册页地址。serverUrl 是认证服务器 API 根地址
        /// （例如 https://littleskin.cn/api/yggdrasil）。
        /// </summary>
        public static string GetRegisterUrl(string serverUrl)
        {
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                return "";
            }

            string known;
            if (KnownRegisters.TryGetValue(normalized, out known))
            {
                return known;
            }

            // 兜底：去掉 /api/yggdrasil（或 /api/... ）后拼 /auth/register。
            // 多数 Blessing Skin 系站点使用该路径。
            string site = GetSiteRoot(normalized);
            return string.IsNullOrEmpty(site) ? "" : site + "/auth/register";
        }

        /// <summary>
        /// 从 API 根地址推站点根（去掉 /api/ 之后的部分）。
        /// 例：https://littleskin.cn/api/yggdrasil → https://littleskin.cn
        /// </summary>
        public static string GetSiteRoot(string serverUrl)
        {
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                return "";
            }
            int apiIndex = normalized.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (apiIndex > 0)
            {
                return normalized.Substring(0, apiIndex);
            }
            Uri uri;
            if (Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
            return "";
        }

        /// <summary>
        /// 判断地址是否看起来像"还没填完"（例如仅输入了 https://）。
        /// 供界面给出更准确的提示。
        /// </summary>
        public static bool IsPlaceholder(string serverUrl)
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                return true;
            }
            string trimmed = serverUrl.Trim();
            return trimmed.Length == 0 ||
                   string.Equals(trimmed, "https://", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "http://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
