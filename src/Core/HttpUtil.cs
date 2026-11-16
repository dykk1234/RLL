using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 一次 HTTP 响应的结果：响应本身 + 服务端告知的总大小。
    /// 用它代替 out 参数——C# 5 不允许 async 方法带 out/ref 参数。
    /// </summary>
    internal sealed class DownloadResult
    {
        public HttpResponseMessage Response;
        /// <summary>服务端告知的总字节数（0 表示未知）。</summary>
        public long TotalSize;

        public DownloadResult(HttpResponseMessage response, long totalSize)
        {
            Response = response;
            TotalSize = totalSize;
        }
    }

    public static class HttpUtil
    {
        private static readonly HashSet<string> BaseAllowedHosts = new HashSet<string>(
            new string[]
            {
                "piston-meta.mojang.com",
                "launchermeta.mojang.com",
                "piston-data.mojang.com",
                "launcher.mojang.com",
                "libraries.minecraft.net",
                "resources.download.minecraft.net",
                "meta.fabricmc.net",
                "maven.fabricmc.net",
                "repo1.maven.org",
                "repo.maven.apache.org",
                "meta.quiltmc.org",
                "maven.quiltmc.org",
                "api.modrinth.com",
                "cdn.modrinth.com",
                // authlib-injector 与 EasyTier 的官方发布地址（GitHub Release 及其下载 CDN）。
                // 仅用于下载这两个固定版本的二进制；下载有大小上限与哈希校验，
                // 且用户可改用本地已核验文件。
                // GitHub 于 2024 年后把 Release 资产改由 release-assets 主机分发，
                // 旧域名 objects.githubusercontent.com 仍保留以兼容历史资产，两者都需放行。
                "github.com",
                "objects.githubusercontent.com",
                "release-assets.githubusercontent.com",
                // Wintun 官方站：EasyTier 创建虚拟网卡所需的 wintun.dll。
                // 仅当 EasyTier 官方包内未自带该库时才会用到。
                "www.wintun.net",
                // Eclipse Adoptium 官方 API：在用户没装 Java 时查询并下载 JRE。
                // 下载文件本身由 github.com 分发（已在白名单内）。
                "api.adoptium.net",
                // 1.7.0（F-1702）：微软正版登录（可选）。仅用于 OAuth 设备码/令牌交换、
                // Xbox/XSTS 认证与 Minecraft 档案接口；全部为官方 HTTPS 端点，
                // 且用户必须显式在设置页配置自己的 Azure 应用 client_id 才会启用。
                "login.microsoftonline.com",
                "user.auth.xboxlive.com",
                "xsts.auth.xboxlive.com",
                "api.minecraftservices.com"
            },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 当前生效白名单 = 内置清单 + 用户显式添加的认证服务器主机。
        /// 第三方认证服务器的域名必须由用户在账号页主动添加/登录后才可访问，
        /// 保持"启动器默认只信任 Mojang/Fabric/Modrinth"的既有约束。
        /// </summary>
        private static readonly HashSet<string> AllowedHosts =
            new HashSet<string>(BaseAllowedHosts, StringComparer.OrdinalIgnoreCase);

        private static readonly object AllowlistLock = new object();
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(CreateClient);
        private const int MaxRedirects = 5;
        private const int MaxMetadataBytes = 16 * 1024 * 1024;
        private const long MaxDownloadBytes = 8L * 1024L * 1024L * 1024L;
        private const int MetadataAttemptTimeoutSeconds = 60;

        /// <summary>
        /// 下载停滞判定：连接连续多少秒没有收到任何字节就认为卡死。
        ///
        /// 取 45 秒的理由：
        /// - 低于 ~20 秒会误杀极慢但仍在传输的连接（TCP 丢包重传时可能几十秒无进展）；
        /// - 高于 ~120 秒则真正卡死的连接要等太久才被发现，用户干等。
        /// 45 秒对两者都留了余量。
        /// </summary>
        public const int DownloadStallTimeoutSeconds = 45;

        /// <summary>
        /// 当前生效的下载源（official / bmclapi / 自定义 URL）。
        /// 由界面在启动与保存设置时设置；为空等同 official。
        /// 仅做 URL 映射，不改变任何哈希校验：镜像只换地址，SHA1 仍对官方清单里的值比对。
        /// </summary>
        public static string CurrentMirror = "";

        /// <summary>
        /// 统一出站入口：把官方 URL 按当前镜像映射，并确保映射后的主机已加入白名单。
        /// 映射不改变路径与查询串，因此 SHA1/SHA256 校验仍然有效。
        /// </summary>
        private static string ApplyMirror(string url)
        {
            if (string.IsNullOrEmpty(url) || !DownloadMirrors.IsMirrorEnabled(CurrentMirror))
            {
                return url;
            }
            string mapped = DownloadMirrors.Map(url, CurrentMirror);
            if (string.Equals(mapped, url, StringComparison.Ordinal))
            {
                return url;
            }
            // 镜像主机必须在白名单内，否则 IsAllowedUrl 会拒绝映射后的地址。
            string host = DownloadMirrors.GetMirrorHost(CurrentMirror);
            if (!string.IsNullOrEmpty(host))
            {
                AddAllowedHost(host);
            }
            return mapped;
        }

        /// <summary>设置当前下载源，并预先为其主机加入白名单。</summary>
        public static void SetMirror(string mirror)
        {
            CurrentMirror = DownloadMirrors.IsMirrorEnabled(mirror) ? mirror : "";
            string host = DownloadMirrors.GetMirrorHost(CurrentMirror);
            if (!string.IsNullOrEmpty(host))
            {
                AddAllowedHost(host);
            }
        }

        static HttpUtil()
        {
            // 与 Program.Main 使用同一套探测逻辑：支持时启用 TLS 1.2 + 1.3，
            // 不支持时回退到仅 TLS 1.2。此前这里无条件重置为 Tls12，
            // 会把 Main 中已开启的 TLS 1.3 覆盖掉（声明与实际行为不符）。
            ServicePointManager.SecurityProtocol = ChooseSecurityProtocol();
            ServicePointManager.DefaultConnectionLimit = 16;
        }

        /// <summary>
        /// 选择可用的 TLS 协议集合。Tls13 在部分 .NET Framework 版本与操作系统上
        /// 不被支持，直接按位或会导致赋值抛异常，因此先探测再启用。
        /// 返回探测结果，供 Program.Main 与静态构造共用同一逻辑。
        /// </summary>
        public static SecurityProtocolType ChooseSecurityProtocol()
        {
            SecurityProtocolType protocol = SecurityProtocolType.Tls12;
            try
            {
                object tls13 = Enum.Parse(typeof(SecurityProtocolType), "Tls13", false);
                SecurityProtocolType combined =
                    (SecurityProtocolType)((int)SecurityProtocolType.Tls12 | (int)tls13);
                ServicePointManager.SecurityProtocol = combined;
                protocol = combined;
            }
            catch
            {
                protocol = SecurityProtocolType.Tls12;
                try
                {
                    ServicePointManager.SecurityProtocol = protocol;
                }
                catch
                {
                }
            }
            return protocol;
        }

        /// <summary>
        /// 用户显式添加一个允许访问的认证服务器主机。只接受合法 DNS 主机名；
        /// 不会移除内置白名单。登录/启动前由 AuthService 统一调用。
        /// </summary>
        public static void AddAllowedHost(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return;
            }
            string trimmed = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (trimmed.Length == 0 || trimmed.IndexOf(':') >= 0 || trimmed.IndexOf('/') >= 0 ||
                trimmed.IndexOf(' ') >= 0)
            {
                return;
            }
            lock (AllowlistLock)
            {
                AllowedHosts.Add(trimmed);
            }
        }

        public static void AddAllowedHosts(System.Collections.Generic.IEnumerable<string> hosts)
        {
            if (hosts == null)
            {
                return;
            }
            foreach (string host in hosts)
            {
                AddAllowedHost(host);
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.UseCookies = false;
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(15);
            return client;
        }

        public static bool IsAllowedUrl(string url)
        {
            Uri uri;
            return IsAllowedUrl(url, out uri);
        }

        public static bool IsAllowedUrl(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
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
            // 读也必须加锁：HashSet 不是线程安全的。下载走多线程（DownloadManyAsync 的
            // worker 池），而认证登录会在界面线程动态添加主机；不加锁的并发读写在
            // HashSet 扩容时可能读到损坏的内部结构（历史上曾导致无限循环）。
            lock (AllowlistLock)
            {
                return AllowedHosts.Contains(uri.Host);
            }
        }

        public static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            byte[] bytes = await GetBytesWithRetryAsync(url, null, MaxMetadataBytes, cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 带**较短超时**的元数据请求。
        ///
        /// 用途：像「查询 Java 可下载版本」这种**交互式**的小查询——
        /// 用户点了按钮在等一个确认框，60 秒 x 3 次重试 = 最长 3 分钟的静默，
        /// 用户只会认为"点了没反应"。这类查询用 20 秒 x 2 次就够了：
        /// 真的网络不通时早点告诉用户，比让他干等 3 分钟好得多。
        ///
        /// 大文件下载不适用（见 DownloadFileAsync 的独立超时策略）。
        /// </summary>
        public static async Task<string> GetStringAsyncQuick(
            string url, CancellationToken cancellationToken)
        {
            byte[] bytes = await GetBytesWithRetryAsync(
                url, null, MaxMetadataBytes, cancellationToken, 20, 2)
                .ConfigureAwait(false);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        public static async Task<byte[]> GetBytesAsync(
            string url,
            string expectedSha1,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            return await GetBytesWithRetryAsync(url, expectedSha1, maxBytes, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<byte[]> GetBytesWithRetryAsync(
            string url,
            string expectedSha1,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            return await GetBytesWithRetryAsync(
                url, expectedSha1, maxBytes, cancellationToken,
                MetadataAttemptTimeoutSeconds, 3).ConfigureAwait(false);
        }

        /// <summary>
        /// 带可配置超时与重试次数的元数据请求。
        /// 交互式的小查询用短超时（见 GetStringAsyncQuick），避免用户干等。
        /// </summary>
        private static async Task<byte[]> GetBytesWithRetryAsync(
            string url,
            string expectedSha1,
            int maxBytes,
            CancellationToken cancellationToken,
            int attemptTimeoutSeconds,
            int maxAttempts)
        {
            if (maxBytes <= 0)
            {
                maxBytes = MaxMetadataBytes;
            }
            if (attemptTimeoutSeconds <= 0)
            {
                attemptTimeoutSeconds = MetadataAttemptTimeoutSeconds;
            }
            if (maxAttempts < 1)
            {
                maxAttempts = 1;
            }
            Exception lastError = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 元数据接口偶尔会无限挂起；为每次尝试设置独立超时，
                // 同时保留用户取消的语义。下载大文件仍使用 HttpClient 的全局超时。
                using (CancellationTokenSource attemptCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    attemptCts.CancelAfter(TimeSpan.FromSeconds(attemptTimeoutSeconds));
                    try
                    {
                        return await GetBytesOnceAsync(url, expectedSha1, maxBytes, attemptCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        lastError = new TimeoutException("请求超时：" + url);
                    }
                    catch (HttpRequestException ex)
                    {
                        lastError = ex;
                    }
                    catch (IOException ex)
                    {
                        lastError = ex;
                    }
                    catch (InvalidDataException ex)
                    {
                        lastError = ex;
                    }
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(500 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
            if (lastError != null)
            {
                bool timedOut = lastError is TimeoutException;
                string hint = timedOut
                    ? "（已重试 " + maxAttempts + " 次，每次最多 " + attemptTimeoutSeconds +
                      " 秒）。请检查网络连接或代理设置后重试。"
                    : "（已重试 " + maxAttempts + " 次）。请检查网络连接后重试。";
                throw new IOException("下载元数据失败：" + lastError.Message + hint, lastError);
            }
            throw new IOException("下载元数据失败：" + url);
        }

        private static async Task<byte[]> GetBytesOnceAsync(
            string url,
            string expectedSha1,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            // 出站前按当前下载源映射地址（仅换地址，哈希校验不变）。
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的下载地址：" + url);
            }

                HttpResponseMessage response = await SendGetWithRedirectsAsync(
                    uri, cancellationToken).ConfigureAwait(false);
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("元数据超过大小限制：" + url);
                    }
                    memory.Write(buffer, 0, read);
                }
                byte[] data = memory.ToArray();
                if (!string.IsNullOrEmpty(expectedSha1))
                {
                    VerifySha1(data, expectedSha1, url);
                }
                return data;
            }
        }

        /// <summary>
        /// 下载文件到本地，带 **SHA1 校验**与**断点续传**。
        ///
        /// ## onBytes 的语义（重要）
        /// 回调收到的是**累计已下载字节数**，不是增量。
        /// 旧实现在不同位置分别传"增量"和"累计"，导致 Java 下载进度永远显示 0/55MB
        /// （它按累计解读，而下载循环传的是每次 80KB 的增量）。
        /// 现在统一为累计：调用方直接用 `received / total` 算百分比即可。
        /// </summary>
        /// <param name="url">下载地址（HTTPS + 白名单）。</param>
        /// <param name="targetPath">最终落盘路径。</param>
        /// <param name="expectedSha1">期望的 SHA1（必填，缺失即拒绝）。</param>
        /// <param name="expectedSize">期望的总大小；大于 0 时会校验实际大小。</param>
        /// <param name="onBytes">进度回调，参数为**累计**已下载字节数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async Task DownloadFileAsync(
            string url,
            string targetPath,
            string expectedSha1,
            long expectedSize,
            Action<long> onBytes,
            CancellationToken cancellationToken)
        {
            await DownloadFileCoreAsync(url, targetPath, expectedSha1, expectedSize,
                onBytes, cancellationToken, true).ConfigureAwait(false);
        }
        /// <summary>
        /// 真正的下载实现：SHA1 校验 + 断点续传 + 停滞检测。
        /// </summary>
        /// <param name="onBytes">进度回调，参数为**累计**已下载字节数。</param>
        /// <param name="allowResume">是否允许断点续传（无哈希校验的下载不允许，因为无法验证完整性）。</param>
        private static async Task DownloadFileCoreAsync(
            string url,
            string targetPath,
            string expectedSha1,
            long expectedSize,
            Action<long> onBytes,
            CancellationToken cancellationToken,
            bool allowResume)
        {
            if (!PathUtil.IsSha1(expectedSha1))
            {
                throw new InvalidDataException("下载项缺少有效的 SHA1：" + url);
            }
            // 出站前按当前下载源映射地址（仅换地址，哈希校验不变）。
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的下载地址：" + url);
            }
            if (string.IsNullOrEmpty(targetPath))
            {
                throw new ArgumentNullException("targetPath");
            }

            // 目标文件已存在且哈希一致：什么都不用做，直接报告"已完成"。
            if (File.Exists(targetPath))
            {
                string existingHash = "";
                try
                {
                    existingHash = PathUtil.GetSha1(targetPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                if (string.Equals(existingHash, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    if (onBytes != null)
                    {
                        onBytes(new FileInfo(targetPath).Length);
                    }
                    return;
                }
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 断点续传：复用上一次没下完的 .part 文件。
            // 关键点：只有**带哈希校验**的下载才允许续传——否则无法确认续上的部分是对的。
            string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
            long resumeFrom = 0;
            if (allowResume)
            {
                resumeFrom = FindPartialFile(targetPath, expectedSha1);
            }
            if (resumeFrom <= 0)
            {
                // 没有可用的分片：重新开始。
                tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
                DeleteStalePartFiles(targetPath);
            }
            else
            {
                // 复用分片：把它接到临时文件后面继续写。
                tempPath = FindPartialFilePath(targetPath);
                Log.Info("断点续传：从 " + (resumeFrom / 1024 / 1024) + " MB 继续下载 " +
                    Path.GetFileName(targetPath));
            }

            CancellationTokenSource stallSource = null;
            DownloadStallWatchdog watchdog = null;
            try
            {
                long total = resumeFrom;
                stallSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                CancellationToken stallToken = stallSource.Token;
                watchdog = DownloadStallWatchdog.StartWatchdog(
                    DownloadStallTimeoutSeconds, stallSource, out stallToken);

                DownloadResult downloadResult = await SendGetWithRedirectsAsync(
                    uri, stallToken, resumeFrom).ConfigureAwait(false);
                HttpResponseMessage response = downloadResult.Response;
                long serverTotal = downloadResult.TotalSize;
                try
                {
                    int status = (int)response.StatusCode;
                    // 206 = Partial Content（续传成功）；200 = 服务端忽略了 Range，从头下。
                    bool partial = status == 206;
                    if (resumeFrom > 0 && !partial)
                    {
                        // 服务端不支持续传（或文件变了）：丢弃分片，从头开始。
                        Log.Warn("服务端不支持断点续传（返回 HTTP " + status + "），从头下载。");
                        total = 0;
                        resumeFrom = 0;
                        if (onBytes != null)
                        {
                            onBytes(0);
                        }
                    }
                    if (serverTotal > 0 && expectedSize <= 0)
                    {
                        expectedSize = serverTotal;
                    }

                    using (response)
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream file = new FileStream(
                        tempPath,
                        partial ? FileMode.Append : FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        true))
                    {
                        byte[] buffer = new byte[81920];
                        while (true)
                        {
                            int read = await stream.ReadAsync(buffer, 0, buffer.Length, stallToken)
                                .ConfigureAwait(false);
                            if (read <= 0)
                            {
                                break;
                            }
                            total += read;
                            if (total > MaxDownloadBytes)
                            {
                                throw new InvalidDataException("下载文件超过大小限制：" + url);
                            }
                            await file.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            // 停滞检测：每次收到数据就刷新"最后活动时间"。
                            // 慢但**在动**的连接不会被误杀，真正卡死的才会被 watchdog 中止。
                            watchdog.Touch();
                            if (onBytes != null)
                            {
                                onBytes(total);
                            }
                        }
                        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (expectedSize > 0 && total != expectedSize)
                    {
                        throw new InvalidDataException(
                            string.Format("文件大小不符：期望 {0} 字节，实际 {1} 字节（{2}）",
                                expectedSize, total, url));
                    }
                    string actualSha1 = PathUtil.GetSha1(tempPath);
                    if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "SHA1 校验失败：" + Path.GetFileName(targetPath) +
                            "，期望 " + expectedSha1 + "，实际 " + actualSha1);
                    }

                    PathUtil.ReplaceFile(tempPath, targetPath);
                    // 下载成功：清掉可能残留的其它分片。
                    DeleteStalePartFiles(targetPath);
                }
                finally
                {
                    response.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                // 区分两种取消：用户主动取消 vs 停滞检测判定为卡死。
                // 后者要给用户一句能看懂的话，而不是笼统的"已取消"。
                if (watchdog != null && watchdog.IsStalled &&
                    !cancellationToken.IsCancellationRequested)
                {
                    throw new IOException(
                        "下载停滞：" + Path.GetFileName(targetPath) + " 已连续 " +
                        DownloadStallTimeoutSeconds + " 秒没有收到任何数据，已中止。" +
                        "请检查网络后重试。");
                }
                throw;
            }
            finally
            {
                if (stallSource != null)
                {
                    try
                    {
                        // 结束 watchdog 线程（它监听令牌，取消即退出）。
                        stallSource.Cancel();
                        stallSource.Dispose();
                    }
                    catch
                    {
                    }
                }
                // 注意：**不删** tempPath —— 它是断点续传的依据。
                // 只有成功替换成目标文件后才会被 ReplaceFile 消费掉。
            }
        }

        /// <summary>
        /// 查找可以续传的分片文件（.part），返回它已有的字节数；没有则返回 0。
        /// </summary>
        private static long FindPartialFile(string targetPath, string expectedSha1)
        {
            string part = FindPartialFilePath(targetPath);
            if (part == null || !File.Exists(part))
            {
                return 0;
            }
            try
            {
                FileInfo info = new FileInfo(part);
                if (!info.Exists || info.Length <= 0)
                {
                    return 0;
                }
                return info.Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 找出最新的分片文件路径。命名规则是 "目标名.<guid>.part"，取最后修改的那个。
        /// </summary>
        private static string FindPartialFilePath(string targetPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return null;
                }
                string pattern = Path.GetFileName(targetPath) + ".*.part";
                string[] files = Directory.GetFiles(directory, pattern);
                if (files.Length == 0)
                {
                    return null;
                }
                Array.Sort(files, delegate(string a, string b)
                {
                    return DateTime.Compare(File.GetLastWriteTimeUtc(b),
                        File.GetLastWriteTimeUtc(a));
                });
                return files[0];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 清理残留的分片文件（下载成功或要重新开始时调用）。
        /// </summary>
        private static void DeleteStalePartFiles(string targetPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return;
                }
                string pattern = Path.GetFileName(targetPath) + ".*.part";
                string[] files = Directory.GetFiles(directory, pattern);
                for (int i = 0; i < files.Length; i++)
                {
                    PathUtil.TryDeleteFile(files[i]);
                }
            }
            catch
            {
            }
        }

        private static async Task<HttpResponseMessage> SendGetWithRedirectsAsync(
            Uri initial,
            CancellationToken cancellationToken)
        {
            DownloadResult result = await SendWithRedirectsAsync(
                HttpMethod.Get, null, initial, cancellationToken, 0).ConfigureAwait(false);
            return result.Response;
        }

        /// <summary>
        /// 带断点续传支持的 GET。
        /// </summary>
        /// <param name="resumeFrom">
        /// 已有的字节数。大于 0 时发 Range 请求；
        /// 服务端返回 206 表示续传成功，返回 200 表示它忽略了 Range（从头下）。
        /// </param>
        private static async Task<DownloadResult> SendGetWithRedirectsAsync(
            Uri initial,
            CancellationToken cancellationToken,
            long resumeFrom)
        {
            return await SendWithRedirectsAsync(
                HttpMethod.Get, null, initial, cancellationToken, resumeFrom).ConfigureAwait(false);
        }

        private static async Task<DownloadResult> SendWithRedirectsAsync(
            HttpMethod method,
            string body,
            Uri initial,
            CancellationToken cancellationToken,
            long resumeFrom,
            string contentType = "application/json",
            string bearerToken = null)
        {
            Uri current = initial;
            // 1.7.5 修复（B-08）：Bearer 令牌只允许附加在首跳请求上。
            // 此前每一轮重定向都重新附加 Authorization，跨主机重定向时会把
            // Minecraft 会话令牌发给下一跳主机（自定义认证域、CDN、镜像都可能中招）。
            // 现在：首跳带令牌；重定向目标与首跳主机不同（或协议不同）时丢弃令牌。
            bool tokenAllowed = !string.IsNullOrEmpty(bearerToken);
            Uri tokenHost = initial;
            for (int redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                if (!IsAllowedUrl(current.AbsoluteUri))
                {
                    throw new InvalidDataException("重定向到不允许的地址：" + current.AbsoluteUri);
                }
                HttpRequestMessage request = new HttpRequestMessage(method, current);
                request.Headers.UserAgent.ParseAdd(
                    "RLL/" + AppPaths.LauncherVersion + " (Rouzhi Lite Launcher)");
                request.Headers.Accept.ParseAdd("*/*");
                if (tokenAllowed)
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", bearerToken);
                }
                // 断点续传：请求从 resumeFrom 字节开始。
                if (resumeFrom > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                }
                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new StringContent(
                        body, Encoding.UTF8, contentType);
                }
                HttpResponseMessage response;
                try
                {
                    response = await Client.Value.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    request.Dispose();
                }

                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400)
                {
                    Uri location = response.Headers.Location;
                    response.Dispose();
                    if (location == null)
                    {
                        throw new HttpRequestException("服务器返回重定向但没有 Location。");
                    }
                    Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!IsAllowedUrl(next.AbsoluteUri))
                    {
                        throw new InvalidDataException("重定向到不允许的地址：" + next.AbsoluteUri);
                    }
                    // 1.7.5（B-08）：跨主机（或 https → http 降级）重定向一律不再带令牌，
                    // 与主流 HTTP 客户端的安全默认一致。
                    if (tokenAllowed &&
                        (!string.Equals(next.Host, tokenHost.Host,
                                StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(next.Scheme, tokenHost.Scheme,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        tokenAllowed = false;
                        Log.Warn("重定向到不同主机（" + next.Host +
                            "），已停止携带 Authorization 令牌。");
                    }
                    current = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    int code = (int)response.StatusCode;
                    string reason = response.ReasonPhrase;
                    // 必须先读响应体再 Dispose：yggdrasil 的 403 等业务错误把原因写在
                    // 响应体里（errorMessage）。旧实现直接 Dispose 丢掉，导致调用方只能
                    // 看到 HTTP 403，被误报成无法连接认证服务器，
                    // 而实际含义是用户名或密码错误。
                    string errorBody = "";
                    try
                    {
                        using (Stream stream = response.Content.ReadAsStreamAsync()
                            .ConfigureAwait(false).GetAwaiter().GetResult())
                        {
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                errorBody = reader.ReadToEnd();
                            }
                        }
                    }
                    catch
                    {
                    }
                    if (errorBody.Length > 4000)
                    {
                        errorBody = errorBody.Substring(0, 4000);
                    }
                    response.Dispose();
                    if (string.IsNullOrEmpty(errorBody))
                    {
                        throw new HttpRequestException(
                            string.Format("HTTP {0} ({1})：{2}", code, reason, current.AbsoluteUri));
                    }
                    throw new HttpRequestException(string.Format(
                        "HTTP {0} ({1})：{2}\r\n{3}", code, reason, current.AbsoluteUri, errorBody));
                }

                // 记录服务端告知的总大小（Content-Range 或 Content-Length）。
                long total = 0;
                if (response.Content.Headers.ContentRange != null)
                {
                    total = response.Content.Headers.ContentRange.Length ?? 0;
                }
                else if (response.Content.Headers.ContentLength.HasValue)
                {
                    total = response.Content.Headers.ContentLength.Value;
                }
                return new DownloadResult(response, total);
            }
            throw new HttpRequestException("重定向次数过多：" + initial.AbsoluteUri);
        }


        public sealed class HttpStatus
        {
            public int Code;
            public string ReasonPhrase;
            public string Body;
        }

        /// <summary>
        /// 发送 JSON POST 请求并读取响应体（无论 2xx 还是错误状态）。
        /// 认证服务器的错误响应（如 403 {"error":...}）也需要返回给调用方解析，
        /// 因此这里不把非 2xx 直接抛成异常。请求与重定向仍受白名单约束。
        /// </summary>
        public static async Task<HttpStatus> PostJsonAsync(
            string url,
            string jsonBody,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                maxBytes = MaxMetadataBytes;
            }
            // 出站前按当前下载源映射地址（仅换地址，哈希校验不变）。
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的请求地址：" + url);
            }

            DownloadResult postResult = await SendWithRedirectsAsync(
                HttpMethod.Post, jsonBody, uri, cancellationToken, 0).ConfigureAwait(false);
            using (HttpResponseMessage response = postResult.Response)
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("响应超过大小限制：" + url);
                    }
                    memory.Write(buffer, 0, read);
                }
                HttpStatus status = new HttpStatus();
                status.Code = (int)response.StatusCode;
                status.ReasonPhrase = response.ReasonPhrase;
                byte[] data = memory.ToArray();
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    status.Body = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                }
                else
                {
                    status.Body = Encoding.UTF8.GetString(data);
                }
                return status;
            }
        }

        /// <summary>
        /// 1.7.0（F-1702）：发送 application/x-www-form-urlencoded POST（微软 OAuth 设备码/令牌端点）。
        /// 与 PostJsonAsync 相同的白名单 / HTTPS / 大小限制约束；错误响应体原样返回。
        /// </summary>
        public static async Task<HttpStatus> PostFormAsync(
            string url,
            string formBody,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                maxBytes = MaxMetadataBytes;
            }
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的请求地址：" + url);
            }
            DownloadResult postResult = await SendWithRedirectsAsync(
                HttpMethod.Post, formBody, uri, cancellationToken, 0,
                "application/x-www-form-urlencoded").ConfigureAwait(false);
            using (HttpResponseMessage response = postResult.Response)
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("响应超过大小限制：" + url);
                    }
                    memory.Write(buffer, 0, read);
                }
                HttpStatus status = new HttpStatus();
                status.Code = (int)response.StatusCode;
                status.ReasonPhrase = response.ReasonPhrase;
                byte[] data = memory.ToArray();
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    status.Body = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                }
                else
                {
                    status.Body = Encoding.UTF8.GetString(data);
                }
                return status;
            }
        }

        /// <summary>
        /// 1.7.0（F-1702）：GET + Bearer 令牌（Minecraft 档案等需要授权的只读接口）。
        /// 与其它出口一致：白名单 / HTTPS / 大小限制。
        /// </summary>
        public static async Task<HttpStatus> GetStatusAsync(
            string url,
            string bearerToken,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                maxBytes = MaxMetadataBytes;
            }
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的请求地址：" + url);
            }
            DownloadResult result = await SendWithRedirectsAsync(
                HttpMethod.Get, null, uri, cancellationToken, 0,
                "application/json", bearerToken).ConfigureAwait(false);
            using (HttpResponseMessage response = result.Response)
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("响应超过大小限制：" + url);
                    }
                    memory.Write(buffer, 0, read);
                }
                HttpStatus status = new HttpStatus();
                status.Code = (int)response.StatusCode;
                status.ReasonPhrase = response.ReasonPhrase;
                byte[] data = memory.ToArray();
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    status.Body = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                }
                else
                {
                    status.Body = Encoding.UTF8.GetString(data);
                }
                return status;
            }
        }

        /// <summary>POST JSON 并返回响应文本；非 2xx 时抛出带状态码与响应摘要的异常。</summary>
        public static async Task<string> PostJsonStringAsync(
            string url,
            string jsonBody,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            HttpStatus status = await PostJsonAsync(url, jsonBody, maxBytes, cancellationToken)
                .ConfigureAwait(false);
            if (status.Code >= 200 && status.Code < 300)
            {
                return status.Body ?? "";
            }
            string snippet = status.Body ?? "";
            if (snippet.Length > 300)
            {
                snippet = snippet.Substring(0, 300);
            }
            throw new HttpRequestException(
                string.Format("HTTP {0}：{1}", status.Code,
                    string.IsNullOrEmpty(snippet) ? status.ReasonPhrase : snippet));
        }

        /// <summary>
        /// 下载无 SHA1 校验的文件（目前仅 authlib-injector JAR，其上游不提供哈希）。
        /// 仍强制：白名单地址、HTTPS、大小上限、临时文件原子替换。
        /// 调用方应优先允许用户提供本地文件；自动下载只作为便利通道。
        /// </summary>
        public static async Task DownloadFileNoChecksumAsync(
            string url,
            string targetPath,
            long maxBytes,
            Action<long> onBytes,
            CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                maxBytes = MaxDownloadBytes;
            }
            // 出站前按当前下载源映射地址（仅换地址，哈希校验不变）。
            url = ApplyMirror(url);
            Uri uri;
            if (!IsAllowedUrl(url, out uri))
            {
                throw new InvalidDataException("不允许的下载地址：" + url);
            }
            if (string.IsNullOrEmpty(targetPath))
            {
                throw new ArgumentNullException("targetPath");
            }
            if (File.Exists(targetPath))
            {
                try
                {
                    FileInfo existing = new FileInfo(targetPath);
                    // 只靠"大小在范围内"无法判断内容是否有效：半截文件、被替换的文件
                    // 都会通过。至少校验它是可解压的 JAR/ZIP 结构，避免把损坏文件
                    // 当成注入器带上 -javaagent（那会导致游戏启动直接失败）。
                    if (existing.Length > 0 && existing.Length <= maxBytes &&
                        LooksLikeJarArchive(targetPath))
                    {
                        if (onBytes != null)
                        {
                            onBytes(existing.Length);
                        }
                        return;
                    }
                    Log.Warn("现有文件不是有效的 JAR，将重新下载：" + targetPath);
                }
                catch
                {
                }
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                long total = 0;
                using (HttpResponseMessage response = await SendGetWithRedirectsAsync(uri, cancellationToken)
                    .ConfigureAwait(false))
                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream file = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true))
                {
                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }
                        total += read;
                        if (total > maxBytes)
                        {
                            throw new InvalidDataException("下载文件超过大小限制：" + url);
                        }
                        await file.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        if (onBytes != null)
                        {
                            onBytes(read);
                        }
                    }
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                if (!LooksLikeJarArchive(tempPath))
                {
                    throw new InvalidDataException(
                        "下载内容不是有效的 JAR（可能被拦截或返回了错误页面）：" + url);
                }
                PathUtil.ReplaceFile(tempPath, targetPath);
            }
            finally
            {
                PathUtil.TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// 粗略判断文件是否为 ZIP/JAR 结构：检查本地文件头 "PK\x03\x04" 或空归档头 "PK\x05\x06"。
        /// 这是对"无 SHA1 文件"的最低成本完整性检查，不能替代哈希校验，
        /// 但能挡住半截下载、文本占位文件等明显无效内容。
        /// </summary>
        internal static bool LooksLikeJarArchive(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 8))
                {
                    byte[] header = new byte[4];
                    int read = stream.Read(header, 0, 4);
                    if (read < 4)
                    {
                        return false;
                    }
                    if (header[0] != 'P' || header[1] != 'K')
                    {
                        return false;
                    }
                    // PK\x03\x04 普通条目；PK\x05\x06 空归档。
                    return (header[2] == 0x03 && header[3] == 0x04) ||
                           (header[2] == 0x05 && header[3] == 0x06);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void VerifySha1(byte[] data, string expectedSha1, string url)
        {
            if (!PathUtil.IsSha1(expectedSha1))
            {
                throw new InvalidDataException("无效的 SHA1：" + expectedSha1);
            }
            using (SHA1 sha1 = SHA1.Create())
            {
                string actual = PathUtil.ToHex(sha1.ComputeHash(data));
                if (!string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA1 校验失败：" + url);
                }
            }
        }
    }
}
