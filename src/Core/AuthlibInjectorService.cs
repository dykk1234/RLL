using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// authlib-injector 注入器的获取与启动参数构建。
    /// 注入器让离线启动器接入第三方 yggdrasil 认证服务器，从而可以进入
    /// 这些服务器开设的正版模式（online-mode=true）服务器。
    ///
    /// 信任说明：官方 JAR 无上游哈希，自动下载只做 HTTPS + 白名单 +
    /// 大小上限校验；高信任用户可用「浏览…」选择本地已核验的 JAR。
    /// </summary>
    public static class AuthlibInjectorService
    {
        /// <summary>
        /// 官方发布地址（固定版本号，避免"latest 跟随"带来的意外变更）。
        /// 该 URL 会 302 到 objects.githubusercontent.com（已在白名单内）。
        /// </summary>
        public const string OfficialReleaseUrl =
            "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar";

        public const long MaxInjectorBytes = 32L * 1024L * 1024L;

        public static string DefaultInjectorPath
        {
            get { return Path.Combine(AppPaths.DataDirectory, "authlib-injector.jar"); }
        }

        /// <summary>
        /// 解析用户配置的注入器路径：为空时返回默认路径。
        /// 拒绝指向启动器数据目录之外的路径以外的任何相对路径。
        /// </summary>
        public static string ResolveInjectorPath(string configured)
        {
            string path = string.IsNullOrEmpty(configured)
                ? DefaultInjectorPath
                : configured.Trim();
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                path = DefaultInjectorPath;
            }
            return path;
        }

        public static bool Exists(string configured)
        {
            string path = ResolveInjectorPath(configured);
            try
            {
                FileInfo info = new FileInfo(path);
                return info.Exists && info.Length > 0 && info.Length <= MaxInjectorBytes;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 构建 -javaagent 启动参数：-javaagent:"路径"="认证服务器地址"。
        /// 路径含空格/引号时由 CommandLine.BuildArguments 统一转义。
        /// </summary>
        /// <summary>
        /// 构建 -javaagent 参数。
        ///
        /// 注意：这里【不能】给路径加内层引号。Java 的 -javaagent 语法是
        ///   -javaagent:jarpath[=options]
        /// 引号会被当作文件名字面的一部分，导致 agent 加载失败、游戏以离线身份进入，
        /// 表现为"无效会话"。实测旧实现生成：
        ///   -javaagent:"C:\...\authlib-injector.jar"="https://.../yggdrasil"
        /// 正确形式应为：
        ///   -javaagent:C:\...\authlib-injector.jar=https://.../yggdrasil
        /// 若路径含空格，由 CommandLine.BuildArguments 在拼整条命令行时统一加外层引号即可。
        /// </summary>
        public static string BuildJavaAgentArgument(string jarPath, string serverUrl)
        {
            if (string.IsNullOrEmpty(jarPath))
            {
                throw new ArgumentNullException("jarPath");
            }
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (!AuthValidation.IsValidAuthServerUrl(normalized))
            {
                throw new ArgumentException("认证服务器地址无效。", "serverUrl");
            }
            // 去掉可能残留的成对引号，避免调用方传进来时已带引号。
            string cleanPath = jarPath.Trim();
            if (cleanPath.Length >= 2 && cleanPath.StartsWith("\"", StringComparison.Ordinal)
                && cleanPath.EndsWith("\"", StringComparison.Ordinal))
            {
                cleanPath = cleanPath.Substring(1, cleanPath.Length - 2);
            }
            return "-javaagent:" + cleanPath + "=" + normalized;
        }

        /// <summary>
        /// 若注入器缺失则从官方地址下载。下载前会把官方地址加入白名单
        /// （GitHub 主机已在内置白名单内）。失败时抛出并保留缺失状态。
        /// </summary>
        public static async Task EnsureInjectorAsync(
            string configured,
            Action<long> onBytes,
            CancellationToken cancellationToken)
        {
            string path = ResolveInjectorPath(configured);
            if (Exists(path))
            {
                return;
            }
            Log.Info("正在下载 authlib-injector 到 " + path);
            await HttpUtil.DownloadFileNoChecksumAsync(
                OfficialReleaseUrl,
                path,
                MaxInjectorBytes,
                onBytes,
                cancellationToken).ConfigureAwait(false);
            Log.Info("authlib-injector 下载完成。");
        }
    }
}
