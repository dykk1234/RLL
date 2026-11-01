using System;
using System.Collections.Generic;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 账号类型。Offline = 离线账号（本地 UUID，不经过任何认证服务器）；
    /// Authlib = 通过 authlib-injector 接入第三方 yggdrasil 认证服务器（如
    /// LittleSkin / Blessing Skin / 自建服务），可进入这些服务器的正版模式服务器。
    /// </summary>
    public enum AccountType
    {
        Offline = 0,
        Authlib = 1
    }

    /// <summary>
    /// 一个已保存的账号。注意：只保存会话令牌（AccessToken），
    /// 永远不保存密码；密码只在登录期间存在于内存中。
    /// </summary>
    public sealed class Account
    {
        /// <summary>
        /// 稳定 ID，格式：offline|名字 或 auth|认证服务器主机|显示名。
        /// </summary>
        public string Id { get; set; }
        public int TypeCode { get; set; }
        /// <summary>离线账号的用户名 / 认证账号的登录邮箱。</summary>
        public string UserName { get; set; }
        /// <summary>游戏内显示名。离线账号与用户名相同。</summary>
        public string DisplayName { get; set; }
        /// <summary>yggdrasil selectedProfile 的 UUID（带连字符）。离线账号为空。</summary>
        public string Uuid { get; set; }
        /// <summary>会话令牌。仅认证账号有效；离线账号固定为空。</summary>
        public string AccessToken { get; set; }
        public string ClientToken { get; set; }
        /// <summary>认证服务器 API 根地址，如 https://littleskin.cn/api/yggdrasil</summary>
        public string AuthServerUrl { get; set; }
        /// <summary>最近使用时间（Unix 秒），用于排序。</summary>
        public long LastUsedUnix { get; set; }

        public AccountType Type
        {
            get { return (AccountType)TypeCode; }
        }

        public string GetDisplay()
        {
            if (Type == AccountType.Authlib)
            {
                string server = AuthServerUrl ?? "";
                string host = server;
                try
                {
                    Uri uri = new Uri(server);
                    host = uri.Host;
                }
                catch
                {
                }
                return (string.IsNullOrEmpty(DisplayName) ? UserName : DisplayName) +
                       "（" + host + "）";
            }
            return string.IsNullOrEmpty(DisplayName) ? UserName : DisplayName;
        }

        /// <summary>供 ComboBox 显示。</summary>
        public override string ToString()
        {
            return GetDisplay();
        }
    }

    /// <summary>
    /// 认证服务器预设。内置预设只是社区常用地址，是否正确以服务器方公告为准；
    /// 用户始终可以输入自定义地址。
    /// </summary>
    public sealed class AuthServerPreset
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public AuthServerPreset()
        {
        }

        public AuthServerPreset(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }

    /// <summary>
    /// 认证服务器地址与账号字段的校验规则。全部为纯函数，便于离线测试。
    /// </summary>
    public static class AuthValidation
    {
        /// <summary>
        /// 校验认证服务器 API 根地址：必须 HTTPS、443 端口、DNS 主机名、
        /// 无 userinfo、无结尾点。允许带路径（如 /api/yggdrasil）。
        /// </summary>
        public static bool IsValidAuthServerUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
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
            if (uri.Port != 443)
            {
                return false;
            }
            return true;
        }

        /// <summary>去除首尾空白与结尾斜杠，返回规范形式。</summary>
        /// <summary>
        /// 把用户输入的地址规范化为 yggdrasil API 根地址。
        ///
        /// 关键：用户常只填站点首页（如 https://littleskin.cn），而认证接口实际位于
        /// /api/yggdrasil 下。实测：
        ///   POST https://littleskin.cn/authserver/authenticate            → 404
        ///   POST https://littleskin.cn/api/yggdrasil/authserver/authenticate → 400（路由存在）
        /// 因此当输入只是"站点根"（路径为空或仅 /）时，自动补 /api/yggdrasil，
        /// 避免用户因为少写一段路径而一直登录失败。
        /// 若用户已填写了带路径的地址（如 /api/yggdrasil 或其它自定义 API 根），则保持原样。
        /// </summary>
        public static string NormalizeAuthServerUrl(string url)
        {
            if (url == null)
            {
                return "";
            }
            string result = url.Trim();
            while (result.EndsWith("/", StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 1);
            }
            if (result.Length == 0)
            {
                return result;
            }

            Uri uri;
            if (Uri.TryCreate(result, UriKind.Absolute, out uri))
            {
                // 路径为空（或只有 /）说明用户填的是站点首页，补上标准 API 前缀。
                string path = uri.AbsolutePath;
                if (string.IsNullOrEmpty(path) || path == "/")
                {
                    return uri.GetLeftPart(UriPartial.Authority) + "/api/yggdrasil";
                }
            }
            return result;
        }

        public static string GetHost(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "";
            }
            try
            {
                Uri uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// yggdrasil selectedProfile.id 的格式校验。
        ///
        /// 标准规定是 8-4-4-4-12 带连字符形式，但实测有些服务器（例如 LittleSkin 的
        /// Yggdrasil Connect 实现）返回的是 32 位无连字符形式。旧实现只接受带连字符，
        /// 会把这类正常响应判成"UUID 格式无效"，导致登录永远失败。
        ///
        /// 因此这里同时接受两种形式；取值时用 NormalizeUuid 统一成带连字符的标准形式，
        /// 保证写入启动参数与配置里的值始终一致。
        /// </summary>
        public static bool IsValidUuid(string uuid)
        {
            return !string.IsNullOrEmpty(NormalizeUuid(uuid));
        }

        /// <summary>
        /// 把 UUID 规范化成 8-4-4-4-12 的小写十六进制形式。
        ///
        /// 只接受两种合法形态：36 位带连字符（连字符必须在固定位置）、
        /// 或 32 位纯十六进制。畸形写法（如尾部多一个连字符、连字符位置错乱）
        /// 一律返回空串——不能"看到一堆十六进制就拼成 UUID"，
        /// 否则会把非法数据当成有效标识传给游戏。
        /// </summary>
        public static string NormalizeUuid(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
            {
                return "";
            }
            string trimmed = uuid.Trim();
            if (trimmed.Length == 36)
            {
                if (trimmed[8] != '-' || trimmed[13] != '-' ||
                    trimmed[18] != '-' || trimmed[23] != '-')
                {
                    return "";
                }
                StringBuilder dashed = new StringBuilder(32);
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (i == 8 || i == 13 || i == 18 || i == 23)
                    {
                        continue;
                    }
                    char c = trimmed[i];
                    if (!IsHexDigit(c))
                    {
                        return "";
                    }
                    dashed.Append(char.ToLowerInvariant(c));
                }
                return FormatUuid(dashed.ToString());
            }
            if (trimmed.Length == 32)
            {
                StringBuilder plain = new StringBuilder(32);
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (!IsHexDigit(trimmed[i]))
                    {
                        return "";
                    }
                    plain.Append(char.ToLowerInvariant(trimmed[i]));
                }
                return FormatUuid(plain.ToString());
            }
            return "";
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>把 32 位小写十六进制串格式化成 8-4-4-4-12。</summary>
        private static string FormatUuid(string hex)
        {
            if (hex.Length != 32)
            {
                return "";
            }
            return hex.Substring(0, 8) + "-" +
                   hex.Substring(8, 4) + "-" +
                   hex.Substring(12, 4) + "-" +
                   hex.Substring(16, 4) + "-" +
                   hex.Substring(20, 12);
        }

        /// <summary>认证账号的显示名：1-16 位，不含空白与控制字符、冒号。</summary>
        public static bool IsValidAuthDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 1 || name.Length > 16)
            {
                return false;
            }
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == ':')
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsValidClientToken(string token)
        {
            return !string.IsNullOrEmpty(token) && token.Length <= 64;
        }
    }

    /// <summary>
    /// 账号 ID 的稳定生成规则：不同来源（离线/认证服务器）互不冲突，
    /// 且用户名中不含 '|' 与 ':'，可安全拼接。
    /// </summary>
    public static class AccountIds
    {
        public static string Offline(string username)
        {
            return "offline|" + (username ?? "");
        }

        public static string Auth(string serverUrl, string displayName)
        {
            return "auth|" + AuthValidation.GetHost(serverUrl) + "|" + (displayName ?? "");
        }

        public static List<Account> SortByLastUsed(List<Account> accounts)
        {
            List<Account> sorted = new List<Account>(accounts);
            sorted.Sort(delegate(Account left, Account right)
            {
                long leftTime = left == null ? 0 : left.LastUsedUnix;
                long rightTime = right == null ? 0 : right.LastUsedUnix;
                int result = rightTime.CompareTo(leftTime);
                if (result != 0)
                {
                    return result;
                }
                string leftName = left == null ? "" : (left.GetDisplay() ?? "");
                string rightName = right == null ? "" : (right.GetDisplay() ?? "");
                return string.Compare(leftName, rightName, StringComparison.Ordinal);
            });
            return sorted;
        }
    }
}
