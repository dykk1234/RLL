using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>一个可下载的 Java 运行时。</summary>
    public sealed class JavaDownload
    {
        public int MajorVersion;
        public string ReleaseName;
        public string Url;
        public long Size;
        public string Sha256;
    }

    /// <summary>
    /// 从 Eclipse Adoptium（Temurin）官方 API 获取并安装 Java 运行时。
    ///
    /// 为什么用 JRE 而不是 JDK：JRE 约 47MB，JDK 约 205MB。
    /// 用户只是要运行游戏，JRE 已经足够，没必要下载整个开发套件。
    ///
    /// 安全约定：
    /// - 下载地址来自官方 API 的 JSON，且域名必须在内置白名单内
    ///   （github.com / release-assets.githubusercontent.com 已在白名单）；
    /// - 校验 SHA256（API 返回 checksum），不符直接丢弃；
    /// - 解压到独立目录，不覆盖用户已有的任何 Java。
    /// </summary>
    public static class JavaInstaller
    {
        /// <summary>Adoptium API 主机。必须加入白名单才能访问。</summary>
        public const string ApiHost = "api.adoptium.net";

        /// <summary>把某个 Minecraft 版本需要的 Java 主版本号映射成下载用的主版本。</summary>
        /// <remarks>
        /// 官方只提供 LTS 与少量版本；老版本游戏要 Java 8，1.17+ 要 17，1.20.5+/1.21 要 21。
        /// 这里就近取可下载的版本，避免用户面对"找不到 Java 8 下载"的情况。
        /// </remarks>
        public static int MapToDownloadable(int required)
        {
            if (required <= 8)
            {
                return 8;
            }
            if (required <= 11)
            {
                return 11;
            }
            if (required <= 17)
            {
                return 17;
            }
            if (required <= 21)
            {
                return 21;
            }
            return 25;
        }

        /// <summary>
        /// 查询指定主版本的 JRE 下载信息（Windows x64）。失败返回 null。
        /// </summary>
        public static async Task<JavaDownload> QueryAsync(int majorVersion, CancellationToken token)
        {
            int release = MapToDownloadable(majorVersion);
            string url = "https://" + ApiHost + "/v3/assets/latest/" + release +
                         "/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
            try
            {
                string json = await HttpUtil.GetStringAsync(url, token).ConfigureAwait(false);
                System.Collections.IList list = JsonUtil.AsList(JsonUtil.Parse(json));
                if (list == null || list.Count == 0)
                {
                    Log.Warn("Adoptium 未返回 Java " + release + " 的下载信息。");
                    return null;
                }
                IDictionary<string, object> first = JsonUtil.AsObject(list[0]);
                if (first == null)
                {
                    return null;
                }
                JavaDownload download = new JavaDownload();
                download.MajorVersion = release;
                download.ReleaseName = JsonUtil.GetString(first, "release_name") ?? "";
                IDictionary<string, object> binary = JsonUtil.GetObject(first, "binary");
                if (binary == null)
                {
                    return null;
                }
                IDictionary<string, object> package = JsonUtil.GetObject(binary, "package");
                if (package == null)
                {
                    return null;
                }
                download.Url = JsonUtil.GetString(package, "link") ?? "";
                download.Size = JsonUtil.GetLong(package, "size", 0);
                download.Sha256 = JsonUtil.GetString(package, "checksum") ?? "";
                if (string.IsNullOrEmpty(download.Url))
                {
                    return null;
                }
                return download;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("查询 Java 下载信息失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 下载并解压 JRE，返回解压出的 java.exe 所在目录（失败返回空串）。
        /// 安装到 <c>&lt;DataDirectory&gt;\java\&lt;主版本&gt;</c>，与启动器其它数据同处。
        /// 已存在同名 java.exe 时直接复用，不重复下载。
        /// </summary>
        public static async Task<string> InstallAsync(
            JavaDownload download,
            Action<long> onBytes,
            CancellationToken token)
        {
            if (download == null)
            {
                throw new ArgumentNullException("download");
            }
            string root = Path.Combine(AppPaths.DataDirectory, "java",
                download.MajorVersion.ToString());
            string existing = FindJavaExecutable(root);
            if (!string.IsNullOrEmpty(existing))
            {
                return Path.GetDirectoryName(existing);
            }

            string tempZip = Path.Combine(AppPaths.DataDirectory, "java",
                "jre-" + download.MajorVersion + "-" + Guid.NewGuid().ToString("N") + ".zip");
            Directory.CreateDirectory(Path.Combine(AppPaths.DataDirectory, "java"));
            try
            {
                Log.Info("正在下载 Java " + download.MajorVersion + "（" +
                         download.ReleaseName + "）：" + download.Url);
                // 上限设 400MB：JRE 约 47MB，留足余量但不允许无限制。
                await HttpUtil.DownloadFileNoChecksumAsync(
                    download.Url, tempZip, 400L * 1024L * 1024L, onBytes, token)
                    .ConfigureAwait(false);

                // SHA256 必须存在且校验通过才允许解压。
                // 旧实现是"有则校验"，API 少给一个字段就整段跳过——那样一个未经校验的
                // 压缩包会直接进入解压路径，与下面的路径穿越缺陷叠加。
                // Adoptium API 正常情况下一定返回 checksum，因此"缺失即拒绝"不会
                // 影响正常用户，只会挡住校验被绕过的攻击路径。
                if (!EasyTierService.IsSha256(download.Sha256))
                {
                    throw new InvalidDataException(
                        "Java 下载信息缺少有效的 SHA256，已拒绝安装。" +
                        "可手动访问 https://adoptium.net 下载安装。");
                }
                string actual = EasyTierService.ComputeSha256(tempZip);
                if (!string.Equals(actual, download.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Java 下载校验失败（SHA256 不符）。已删除下载的文件。");
                }
                Log.Info("Java 下载校验通过。");

                token.ThrowIfCancellationRequested();
                // 解压到临时目录再整体移动，避免半个目录被当成可用 Java。
                string tempDir = root + ".tmp-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(tempDir);
                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        for (int i = 0; i < archive.Entries.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            ZipArchiveEntry entry = archive.Entries[i];
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                continue;   // 目录项由文件解压时自动创建
                            }
                            // 条目名完全来自远程 ZIP，必须先做路径穿越校验再落盘。
                            // 旧实现直接 Path.Combine(tempDir, entry.FullName)，含 ".." 的
                            // 条目名会把文件写到解压目录之外（zip-slip），且
                            // Directory.CreateDirectory 还会主动创建越界的中间目录。
                            // 这里与 NativeExtractor / ModrinthService 保持同一套
                            // SafeCombine 校验，保证写入的文件一定在临时目录内。
                            if (!PathUtil.IsSafeRelativePath(entry.FullName))
                            {
                                throw new InvalidDataException(
                                    "Java 压缩包条目路径无效：" + entry.FullName);
                            }
                            string target = PathUtil.SafeCombine(tempDir, entry.FullName);
                            string parent = Path.GetDirectoryName(target);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }
                            using (Stream input = entry.Open())
                            using (FileStream output = new FileStream(target,
                                FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                byte[] buffer = new byte[81920];
                                while (true)
                                {
                                    token.ThrowIfCancellationRequested();
                                    int read = input.Read(buffer, 0, buffer.Length);
                                    if (read <= 0)
                                    {
                                        break;
                                    }
                                    output.Write(buffer, 0, read);
                                }
                            }
                        }
                    }
                    string javaExe = FindJavaExecutable(tempDir);
                    if (string.IsNullOrEmpty(javaExe))
                    {
                        throw new InvalidDataException("解压后的 Java 目录里找不到 javaw.exe/java.exe。");
                    }
                    if (Directory.Exists(root))
                    {
                        try
                        {
                            Directory.Delete(root, true);
                        }
                        catch
                        {
                        }
                    }
                    Directory.Move(tempDir, root);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch
                    {
                    }
                }

                string installed = FindJavaExecutable(root);
                if (string.IsNullOrEmpty(installed))
                {
                    throw new InvalidDataException("Java 安装失败：目标目录里没有可执行文件。");
                }
                Log.Info("Java 已安装：" + installed);
                return Path.GetDirectoryName(installed);
            }
            finally
            {
                PathUtil.TryDeleteFile(tempZip);
            }
        }

        /// <summary>
        /// 在目录中查找 java.exe / javaw.exe（优先 javaw 以免弹控制台窗口）。
        /// 找不到返回空串。
        /// </summary>
        public static string FindJavaExecutable(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return "";
            }
            // 官方包里是 &lt;jdk&gt;\bin\javaw.exe 结构，用两层搜索兜住。
            string[] names = new string[] { "javaw.exe", "java.exe" };
            for (int n = 0; n < names.Length; n++)
            {
                string[] found;
                try
                {
                    found = Directory.GetFiles(directory, names[n], SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }
                if (found.Length > 0)
                {
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    return found[0];
                }
            }
            return "";
        }
    }
}
