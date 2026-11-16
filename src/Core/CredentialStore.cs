using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>一个被保护的值：算法 + 格式版本 + Base64 数据。</summary>
    public sealed class ProtectedValue
    {
        /// <summary>"dpapi"（Windows CurrentUser）或 "plain"（非 Windows 降级，仅 Base64 混淆 + 显式标记）。</summary>
        public string Algorithm;
        /// <summary>密文格式版本（当前 1）。</summary>
        public int Version;
        /// <summary>Base64 编码的保护数据。</summary>
        public string DataBase64;

        public ProtectedValue()
        {
            Version = 1;
        }

        public static ProtectedValue FromJson(IDictionary<string, object> obj)
        {
            ProtectedValue value = new ProtectedValue();
            if (obj == null)
            {
                return null;
            }
            value.Algorithm = JsonUtil.GetString(obj, "Algorithm");
            value.Version = JsonUtil.GetInt(obj, "Version", 1);
            value.DataBase64 = JsonUtil.GetString(obj, "Data");
            if (string.IsNullOrEmpty(value.DataBase64))
            {
                return null;
            }
            return value;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["Algorithm"] = Algorithm ?? "";
            item["Version"] = Version;
            item["Data"] = DataBase64 ?? "";
            return item;
        }
    }

    /// <summary>
    /// 1.6.0（F-1604）：凭证保护存储。
    ///
    /// - Windows：使用 DPAPI（DataProtectionScope.CurrentUser）加密第三方会话令牌与
    ///   EasyTier 网络密钥；密文绑定当前 Windows 用户，本机配置泄露无法直接复用。
    /// - 非 Windows（开发/沙箱）：无 DPAPI，降级为 "plain"（Base64 + 显式算法标记），
    ///   配置仍以结构化形式存储并在 UI/日志中遮蔽；README 如实声明该降级。
    /// - 解密失败不静默覆盖密文：调用方必须提示重新认证 / 重新输入密钥。
    /// - 日志、异常、下载任务与导出文件一律不得记录明文凭证（配合 Log.Sanitize）。
    /// </summary>
    public static class CredentialStore
    {
        public const string AlgorithmDpapi = "dpapi";
        public const string AlgorithmPlain = "plain";
        public const int Version = 1;

        /// <summary>是否运行在 Windows（可调用 DPAPI）。</summary>
        public static bool IsWindows
        {
            get
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT;
            }
        }

        /// <summary>
        /// 保护明文。purpose 参与熵，区分"令牌"与"网络密钥"等用途。
        /// 返回 null 表示输入为空（调用方应跳过，不产生密文）。
        /// </summary>
        public static ProtectedValue Protect(string plaintext, string purpose)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                return null;
            }
            ProtectedValue value = new ProtectedValue();
            value.Algorithm = IsWindows ? AlgorithmDpapi : AlgorithmPlain;
            value.Version = Version;
            byte[] data = Encoding.UTF8.GetBytes(plaintext);
            if (IsWindows)
            {
                byte[] entropy = Encoding.UTF8.GetBytes("rll-cred-" + (purpose ?? ""));
                byte[] protectedData;
                try
                {
                    protectedData = System.Security.Cryptography.ProtectedData.Protect(
                        data,
                        entropy,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                }
                catch (PlatformNotSupportedException)
                {
                    value.Algorithm = AlgorithmPlain;
                    protectedData = data;
                }
                value.DataBase64 = Convert.ToBase64String(protectedData);
            }
            else
            {
                value.DataBase64 = Convert.ToBase64String(data);
            }
            return value;
        }

        /// <summary>
        /// 解除保护。解密失败抛异常（不返回空串、不覆盖密文），
        /// 由调用方提示重新认证或重新输入密钥。
        /// </summary>
        public static string Unprotect(ProtectedValue value, string purpose)
        {
            if (value == null || string.IsNullOrEmpty(value.DataBase64))
            {
                return "";
            }
            byte[] data = Convert.FromBase64String(value.DataBase64);
            if (string.Equals(value.Algorithm, AlgorithmDpapi, StringComparison.Ordinal))
            {
                if (!IsWindows)
                {
                    // 在非 Windows 环境读 Windows 密文：无法解密，如实失败。
                    throw new InvalidDataException(
                        "凭证由 Windows DPAPI 加密，当前环境无法解密；请在本机重新登录或重新输入密钥。");
                }
                byte[] entropy = Encoding.UTF8.GetBytes("rll-cred-" + (purpose ?? ""));
                byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(
                    data,
                    entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            if (string.Equals(value.Algorithm, AlgorithmPlain, StringComparison.Ordinal))
            {
                // 降级算法：Base64 原样返回（配置泄露时仍需配合日志遮蔽与文件 ACL）。
                return Encoding.UTF8.GetString(data);
            }
            throw new InvalidDataException("未知的凭证保护算法：" + value.Algorithm);
        }
    }
}
