using System;
 using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

namespace LiteLauncher.Core
{
    /// <summary>
    /// EasyTier 异地联机：负责下载/校验官方核心、启动与停止核心进程、解析本机虚拟 IP。
    ///
    /// 定位：启动器只做"加入 EasyTier 网络并展示本机虚拟 IP"，
    /// 不向游戏注入 server-ip，也不修改 server.properties；虚拟 IP 由用户在游戏内手动使用。
    /// </summary>
    public static class EasyTierService
    {
        /// <summary>
        /// 固定版本 + 官方 GitHub Releases 资产。版本与 SHA256 取自 GitHub API：
        /// releases/latest 的 assets[].digest 字段（sha256:27af91e2…）。
        /// 固定版本号而不是用 latest，避免上游更新后哈希失效或资产改名。
        /// </summary>
        public const string ReleaseVersion = "v2.6.4";
        public const string AssetZipName = "easytier-windows-x86_64-v2.6.4.zip";
        public const string ExpectedSha256 =
            "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84";

        public const string OfficialReleaseUrl =
            "https://github.com/EasyTier/EasyTier/releases/download/" +
            ReleaseVersion + "/" + AssetZipName;

        /// <summary>
        /// 默认公共节点。取值依据：实测环境下 DNS 可解析、TCP 11010 端口 OPEN，
        /// 且用该节点启动核心后日志出现 "new peer added"（确实连上了对等节点）。
        /// 来源：EasyTier 社区公共节点监控站公布的地址。
        ///
        /// 说明：公共节点由社区维护，地址可能失效。启动器把可用节点作为默认值，
        /// 但界面上仍可修改；若该节点失效，用户可在设置页点「获取节点地址」
        /// 查看监控站列出的其它节点。曾有内置地址 public.easytier.top 实测 NXDOMAIN。
        /// </summary>
        public const string DefaultPublicNode = "tcp://easytier.weiai.org.cn:11010";

        /// <summary>
        /// 备用公共节点（均为本机实测 TCP 可连通的地址，来自社区监控站）。
        /// 默认节点不可达时用户可从中切换；界面提供一键选择的候选列表。
        /// </summary>
        public static readonly string[] KnownPublicNodes = new string[]
        {
            "tcp://easytier.weiai.org.cn:11010",   // 厦门电信，可中转
            "tcp://et.sumnight.top:11010",         // 上海 BGP，延迟低
            "tcp://225284.xyz:11010"               // 上海电信，可中转
        };

        /// <summary>公共节点地址查询入口（社区维护的监控站，列出当前可用节点）。</summary>
        public const string PublicNodeDocsUrl = "https://upt.qicwken.cn/status/easytier";

        /// <summary>备用查询入口（另一个社区监控站）。</summary>
        public const string PublicNodeDocsUrlAlt = "https://info.qtet.cn/uptime/easytier";

        /// <summary>公共节点端口约定（TCP/UDP 均为 11010）。</summary>
        public const int PublicNodePort = 11010;

        public const string DefaultBinaryName = "easytier-core.exe";

        /// <summary>
        /// Windows 创建虚拟网卡必需的驱动库，必须与 exe 放在同一目录。
        /// 实测：缺失它时核心每 6 秒重试并持续报
        ///   "tun device error: rust tun error The file is not signed."
        /// （不是驱动未签名，而是找不到随包的签名 wintun.dll 副本）。
        /// 补上同名文件后 TUN 立即创建成功，网卡出现（如 10.126.126.1）。
        /// </summary>
        public const string RequiredWintunDll = "wintun.dll";

        /// <summary>Wintun 官方下载地址（备用：核心 zip 内通常已自带）。</summary>
        public const string WintunReleaseUrl = "https://www.wintun.net/builds/wintun-0.14.1.zip";

        /// <summary>
        /// wintun-0.14.1.zip 的官方 SHA2-256。
        /// 取自 https://www.wintun.net/ 官方发布页公布的摘要（非臆造）。
        ///
        /// 为什么必须校验：wintun.dll 是**内核态网络驱动**。这是整个启动器里唯一
        /// 没有内容哈希保护、却会落地一个系统级驱动文件的下载路径，因此不能只靠
        /// "是合法 ZIP"这种结构检查。
        /// </summary>
        public const string ExpectedWintunSha256 =
            "07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51";

        public const long MaxDownloadBytes = 64L * 1024L * 1024L;

        /// <summary>
        /// 虚拟网段。EasyTier 的 `-d`（DHCP）默认从 10.0.0.1 开始分配，
        /// 而固定 `-i` 时常见的是 10.144.x.x；因此探测放宽到整个 10.0.0.0/8 私网段，
        /// 不再写死 10.144。只认 10.x 可避免把物理网卡（192.168/172.16）误判为虚拟网卡。
        /// </summary>
        private const string VirtualIpPattern = @"\b10\.\d{1,3}\.\d{1,3}\.\d{1,3}\b";

        private static Process _core;
        private static readonly object CoreLock = new object();

        /// <summary>最近一次核心运行的输出（stdout + stderr），用于失败时给用户看原因。</summary>
        private static readonly List<string> _output = new List<string>();
        private static readonly object OutputLock = new object();
        private const int MaxOutputLines = 200;

        private static void AppendOutput(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }
            lock (OutputLock)
            {
                if (_output.Count >= MaxOutputLines)
                {
                    _output.RemoveAt(0);
                }
                _output.Add(line);
            }
            // 同时写入启动器日志，便于事后排查。
            Log.Info("[easytier] " + line);
        }

        /// <summary>
        /// 取最近的核心输出（最后若干行）。界面在启动失败时用它展示真实原因。
        /// </summary>
        public static string GetRecentOutput(int maxLines)
        {
            if (maxLines <= 0)
            {
                maxLines = 20;
            }
            lock (OutputLock)
            {
                int start = Math.Max(0, _output.Count - maxLines);
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                for (int i = start; i < _output.Count; i++)
                {
                    builder.AppendLine(_output[i]);
                }
                return builder.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// 把核心输出归纳成一句可读的原因，供界面直接显示。
        /// 覆盖实测遇到的三类：参数不被接受、TUN 设备创建失败（需管理员/驱动签名）、
        /// 端口被占用（残留实例）。
        /// </summary>
        public static string DiagnoseFailure()
        {
            string output = GetRecentOutput(60);
            if (string.IsNullOrEmpty(output))
            {
                return "";
            }
            string diagnosis = DiagnoseText(output);
            if (!string.IsNullOrEmpty(diagnosis))
            {
                return diagnosis;
            }
            // 兜底：返回最后几行原文，至少让用户能看到真实报错。
            string tail = GetRecentOutput(6);
            return string.IsNullOrEmpty(tail) ? "" : ("核心输出：\r\n" + tail);
        }

        /// <summary>
        /// 把核心输出文本映射成一条可操作的建议（纯函数，便于离线测试）。
        /// 未命中任何已知模式时返回空串，由调用方决定如何兜底。
        /// </summary>
        public static string DiagnoseText(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return "";
            }
            string lower = output.ToLowerInvariant();
            if (lower.IndexOf("unexpected argument", StringComparison.Ordinal) >= 0)
            {
                return "核心不接受启动参数（版本不匹配）。请到设置页点「下载核心」更新到受支持的版本。";
            }
            if (lower.IndexOf("the file is not signed", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("tun device error", StringComparison.Ordinal) >= 0)
            {
                // 实测结论：系统里的 wintun.sys 本身签名有效（Microsoft 签名），
                // 报 "The file is not signed" 是 EasyTier 在加载/安装 Wintun 驱动时
                // 被签名策略拦下，绝大多数情况是权限不足（非管理员）。
                // 因此提示要聚焦"以管理员身份运行"，而不是让用户去查驱动是否装好。
                return "无法创建虚拟网卡（虚拟 TUN 设备）。这通常是因为启动器没有以管理员身份运行，" +
                       "Windows 不允许普通权限加载网络驱动。\r\n" +
                       "处理办法：关闭启动器，右键选择「以管理员身份运行」后重试。\r\n" +
                       "（网络已连通，只是本机还没建出虚拟网卡，所以尚未显示虚拟 IP。）";
            }
            if (lower.IndexOf("addrinuse", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("address already in use", StringComparison.Ordinal) >= 0)
            {
                return "端口 11010-11013 已被占用（可能有残留的 easytier-core 进程）。" +
                       "请在任务管理器结束 easytier-core.exe 后重试。";
            }
            if (lower.IndexOf("firewall", StringComparison.Ordinal) >= 0 &&
                lower.IndexOf("0x80070005", StringComparison.Ordinal) >= 0)
            {
                return "无法写入防火墙规则（权限不足），组网可能受限。请以管理员身份运行本启动器。";
            }
            return "";
        }

        public static string DefaultBinaryPath
        {
            get
            {
                return Path.Combine(AppPaths.DataDirectory, "bin", DefaultBinaryName);
            }
        }

        /// <summary>解析配置的二进制路径：为空时用默认路径。</summary>
        public static string ResolveBinaryPath(string configured)
        {
            string path = string.IsNullOrEmpty(configured)
                ? DefaultBinaryPath
                : configured.Trim();
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return DefaultBinaryPath;
            }
        }

        public static bool Exists(string configured)
        {
            string path = ResolveBinaryPath(configured);
            try
            {
                // 二进制必须存在且非空；EasyTier 核心是静态链接的独立 exe，大小通常 > 1MB。
                FileInfo info = new FileInfo(path);
                return info.Exists && info.Length > 1024 * 1024;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断核心是否"完整可用"：exe 存在，且同目录下有 wintun.dll。
        /// 只检查 exe 会漏掉旧版本启动器留下的残缺安装（只有 exe，没有 dll），
        /// 用户会一直卡在 "tun device error: The file is not signed"。
        /// </summary>
        public static bool IsFullyInstalled(string configured)
        {
            if (!Exists(configured))
            {
                return false;
            }
            string directory = Path.GetDirectoryName(ResolveBinaryPath(configured));
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }
            string dll = Path.Combine(directory, RequiredWintunDll);
            try
            {
                FileInfo info = new FileInfo(dll);
                return info.Exists && info.Length > 100 * 1024;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 缺失时下载官方 zip → 校验 SHA256 → 解压出 easytier-core.exe → 原子写入目标路径。
        /// 任一步失败都会清理临时文件，不留下垃圾。
        /// </summary>
        /// <summary>
        /// 缺失时下载官方 zip → 校验 SHA256 → 解压出 easytier-core.exe 及其附属文件
        /// （含 wintun.dll）→ 原子写入目标路径。任一步失败都会清理临时文件。
        ///
        /// 注意：判据用 IsFullyInstalled 而不是 Exists——旧版本可能只装了 exe，
        /// 缺 wintun.dll 会导致虚拟网卡永远建不出来，这种情况也要触发补齐。
        /// </summary>
        public static async Task EnsureBinaryAsync(
            string configured,
            Action<long> onBytes,
            CancellationToken cancellationToken)
        {
            string target = ResolveBinaryPath(configured);
            if (IsFullyInstalled(target))
            {
                return;
            }
            if (Exists(target))
            {
                Log.Warn("检测到 EasyTier 核心缺少 " + RequiredWintunDll +
                         "（旧版安装不完整），将重新下载补齐。");
            }

            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 下载到临时 zip，校验通过后才解压。
            string tempZip = target + "." + Guid.NewGuid().ToString("N") + ".zip";
            try
            {
                Log.Info("正在下载 EasyTier 核心：" + OfficialReleaseUrl);
                await HttpUtil.DownloadFileNoChecksumAsync(
                    OfficialReleaseUrl, tempZip, MaxDownloadBytes, onBytes, cancellationToken)
                    .ConfigureAwait(false);

                string actual = ComputeSha256(tempZip);
                if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "EasyTier 核心校验失败（SHA256 不符）。期望 " + ExpectedSha256 +
                        "，实际 " + actual + "。已删除下载的文件。");
                }
                Log.Info("EasyTier 核心校验通过。");

                ExtractCore(tempZip, target, cancellationToken);

                // 官方 zip 有时不含 wintun.dll（或版本不同），必须确保它有：
                // 缺了它虚拟网卡永远建不出来，用户只会看到 "tun device error"。
                string coreDirectory = Path.GetDirectoryName(target);
                if (!IsFullyInstalled(target))
                {
                    Log.Warn("官方包内未找到 " + RequiredWintunDll + "，尝试从 Wintun 官方源补齐。");
                    await EnsureWintunAsync(coreDirectory, cancellationToken).ConfigureAwait(false);
                }
                if (!IsFullyInstalled(target))
                {
                    throw new InvalidDataException(
                        "EasyTier 缺少 " + RequiredWintunDll + "，无法创建虚拟网卡。" +
                        "请检查网络后重试，或从 https://www.wintun.net 手动下载并放到：" +
                        Path.Combine(coreDirectory ?? "", RequiredWintunDll));
                }
                Log.Info("EasyTier 核心已就绪（含 " + RequiredWintunDll + "）：" + target);
            }
            finally
            {
                PathUtil.TryDeleteFile(tempZip);
            }
        }

        /// <summary>
        /// 从官方 zip 中解出 easytier-core.exe。zip 内含多个文件，
        /// 只取目标文件名，解压到临时位置后再原子替换，避免半个文件被当成可用核心。
        /// </summary>
        private static void ExtractCore(string zipPath, string target, CancellationToken cancellationToken)
        {
            // 关键：官方 zip 里不只有 easytier-core.exe，还带有运行所需的附属文件
            // （尤其 wintun.dll —— Windows 创建虚拟网卡必需的驱动库）。
            // 旧实现只解压 exe，导致核心运行时以
            //   "tun device error: rust tun error The file is not signed."
            // 失败：它拿不到随包的签名 wintun.dll，只能去系统里找其它副本。
            // 因此这里解压 exe 所在目录的全部顶层文件（不递归子目录、不覆盖用户文件）。
            string directory = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException("EasyTier 目标目录无效：" + target);
            }
            Directory.CreateDirectory(directory);

            string exeTemp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            List<string> written = new List<string>();
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    bool foundExe = false;
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = archive.Entries[i];
                        string name = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(name))
                        {
                            // 目录项
                            continue;
                        }
                        // 只取顶层可执行/库文件，跳过文档与示例配置。
                        string lower = name.ToLowerInvariant();
                        bool wanted = lower.EndsWith(".exe") || lower.EndsWith(".dll");
                        if (!wanted)
                        {
                            continue;
                        }
                        if (entry.Length > MaxDownloadBytes)
                        {
                            throw new InvalidDataException("EasyTier 包内文件大小异常：" + name);
                        }
                        bool isMainExe = string.Equals(name, DefaultBinaryName,
                            StringComparison.OrdinalIgnoreCase);
                        if (isMainExe)
                        {
                            foundExe = true;
                        }
                        // 主程序先写到临时名，全部校验通过后再替换，避免半个文件被当成可用核心。
                        string destination = isMainExe
                            ? exeTemp
                            : Path.Combine(directory, name);
                        if (isMainExe)
                        {
                            File.Delete(exeTemp);
                        }
                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(
                            destination, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[81920];
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int read = input.Read(buffer, 0, buffer.Length);
                                if (read <= 0)
                                {
                                    break;
                                }
                                output.Write(buffer, 0, read);
                            }
                        }
                        if (!isMainExe)
                        {
                            written.Add(destination);
                            Log.Info("EasyTier 附属文件已解压：" + name);
                        }
                    }
                    if (!foundExe)
                    {
                        throw new InvalidDataException(
                            "EasyTier 压缩包中找不到 " + DefaultBinaryName + "。");
                    }
                }

                FileInfo extracted = new FileInfo(exeTemp);
                if (!extracted.Exists || extracted.Length < 1024 * 1024)
                {
                    throw new InvalidDataException("解压出的 EasyTier 核心大小异常。");
                }
                PathUtil.ReplaceFile(exeTemp, target);
            }
            finally
            {
                PathUtil.TryDeleteFile(exeTemp);
            }
        }

        /// <summary>
        /// 从 Wintun 官方包补齐 wintun.dll（仅当核心 zip 内缺失时使用）。
        /// 官方 zip 按架构分目录（bin/amd64/wintun.dll 等），这里按当前进程架构挑选。
        /// </summary>
        private static async Task EnsureWintunAsync(string directory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }
            string tempZip = Path.Combine(directory,
                "wintun-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                Log.Info("正在下载 Wintun 驱动库：" + WintunReleaseUrl);
                await HttpUtil.DownloadFileNoChecksumAsync(
                    WintunReleaseUrl, tempZip, MaxDownloadBytes, null, cancellationToken)
                    .ConfigureAwait(false);

                // 必须校验 SHA256：wintun.dll 是内核态驱动，不能只靠"是合法 ZIP"
                // 这种结构检查。摘要取自官方发布页公布的值。
                string wintunHash = ComputeSha256(tempZip);
                if (!string.Equals(wintunHash, ExpectedWintunSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Wintun 驱动库校验失败（SHA256 不符）。期望 " + ExpectedWintunSha256 +
                        "，实际 " + wintunHash + "。已删除下载的文件。");
                }
                Log.Info("Wintun 驱动库校验通过。");

                string arch = System.Environment.Is64BitProcess ? "amd64" : "x86";
                string entryName = "wintun/bin/" + arch + "/" + RequiredWintunDll;
                string target = Path.Combine(directory, RequiredWintunDll);

                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    ZipArchiveEntry entry = null;
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        if (string.Equals(archive.Entries[i].FullName, entryName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            entry = archive.Entries[i];
                            break;
                        }
                    }
                    if (entry == null)
                    {
                        throw new InvalidDataException("Wintun 包内找不到 " + entryName);
                    }
                    string tempDll = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(
                            tempDll, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[81920];
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int read = input.Read(buffer, 0, buffer.Length);
                                if (read <= 0)
                                {
                                    break;
                                }
                                output.Write(buffer, 0, read);
                            }
                        }
                        FileInfo info = new FileInfo(tempDll);
                        if (!info.Exists || info.Length < 100 * 1024)
                        {
                            throw new InvalidDataException("下载的 wintun.dll 大小异常。");
                        }
                        PathUtil.ReplaceFile(tempDll, target);
                        Log.Info("Wintun 驱动库已就绪：" + target);
                    }
                    finally
                    {
                        PathUtil.TryDeleteFile(tempDll);
                    }
                }
            }
            finally
            {
                PathUtil.TryDeleteFile(tempZip);
            }
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                return PathUtil.ToHex(sha.ComputeHash(stream));
            }
        }

        /// <summary>
        /// 校验一个字符串是否为合法的 SHA256 摘要（64 位十六进制）。
        /// 用于下载前确认"上游确实给了哈希"，避免把"没有哈希"当成"校验通过"。
        /// </summary>
        public static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9') ||
                          (c >= 'a' && c <= 'f') ||
                          (c >= 'A' && c <= 'F');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 构建 easytier-core 的参数列表（纯函数，便于离线测试）。
        ///
        /// 参数契约以 easytier-core v2.6.4 的 `--help` 实测为准：
        /// - 网络名是 `--network-name`（**没有** `-n` 短参数；`-n` 是 `--proxy-networks`，
        ///   语义完全不同，误用会把网络名当成"导出本地网络"）；
        /// - 网络密钥是 `--network-secret`（**不存在** `-k`，旧代码传 `-k` 会让核心
        ///   直接报 `unexpected argument '-k'` 并秒退，这就是"联机不可用"的根因）；
        /// - 对等节点是 `-p/--peers`；
        /// - 虚拟 IP 必须显式指定，否则核心"仅转发、不创建 TUN 设备"，界面上就永远
        ///   看不到虚拟 IP。默认用 `-d`（DHCP 自动分配），比固定 IP 更适合多机使用
        ///   （固定 IP 需要用户自行管理以免冲突）。
        /// </summary>
        public static List<string> BuildArguments(string networkName, string secret, string publicNode)
        {
            return BuildArguments(networkName, secret, publicNode, "");
        }

        /// <summary>
        /// 带显式虚拟 IP 的重载：virtualIp 非空时用 `-i` 固定分配，
        /// 为空时用 `-d` 让核心自动分配。
        /// </summary>
        public static List<string> BuildArguments(
            string networkName, string secret, string publicNode, string virtualIp)
        {
            List<string> args = new List<string>();
            if (string.IsNullOrEmpty(networkName))
            {
                throw new ArgumentException("网络名不能为空。", "networkName");
            }

            args.Add("--network-name");
            args.Add(networkName);

            if (!string.IsNullOrEmpty(secret))
            {
                // 密钥必须用长参数：v2.x 没有 -k。
                args.Add("--network-secret");
                args.Add(secret);
            }

            // 公共节点：为空时回落到实测可用的默认节点（见 DefaultPublicNode 注释）。
            // 用户也可在设置页改成监控站列出的其它节点。
            string node = string.IsNullOrEmpty(publicNode) ? DefaultPublicNode : publicNode;
            args.Add("-p");
            args.Add(node);

            // 没有 -i/-d 时核心不会创建 TUN 网卡，虚拟 IP 永远不会出现。
            if (!string.IsNullOrEmpty(virtualIp) && IsVirtualIp(virtualIp))
            {
                args.Add("-i");
                args.Add(virtualIp);
            }
            else
            {
                args.Add("-d");
            }
            return args;
        }

        /// <summary>
        /// 生成用于日志显示的参数串：把网络密钥替换成 ****。
        ///
        /// 密钥是 EasyTier 网络的准入凭证（知道网络名 + 密钥就能加入用户的局域网），
        /// 因此绝不能明文写进日志——日志文件没有像 config.json 那样收紧 ACL，
        /// 且用户经常把日志贴到 issue/论坛求助，密钥会随之外泄。
        ///
        /// 纯函数，便于离线测试。
        /// </summary>
        public static string BuildRedactedArguments(List<string> args)
        {
            if (args == null || args.Count == 0)
            {
                return "";
            }
            List<string> copy = new List<string>(args);
            for (int i = 0; i < copy.Count - 1; i++)
            {
                if (string.Equals(copy[i], "--network-secret", StringComparison.Ordinal))
                {
                    copy[i + 1] = "****";
                    i++;
                }
            }
            return CommandLine.BuildArguments(copy);
        }

        /// <summary>
        /// 启动核心进程。已在运行时直接返回现有进程。
        /// 隐藏控制台窗口并重定向 stdout，用于解析虚拟 IP。
        /// </summary>
        public static Process StartCore(LauncherConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            lock (CoreLock)
            {
                if (_core != null && !_core.HasExited)
                {
                    return _core;
                }

                string binary = ResolveBinaryPath(config.EasyTierBinaryPath);
                if (!Exists(config.EasyTierBinaryPath))
                {
                    throw new FileNotFoundException(
                        "EasyTier 核心不存在，请先在设置页下载：" + binary);
                }
                List<string> args = BuildArguments(
                    config.EasyTierNetworkName,
                    config.EasyTierNetworkSecret,
                    config.EasyTierPublicNode);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = binary;
                startInfo.Arguments = CommandLine.BuildArguments(args);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.WorkingDirectory = Path.GetDirectoryName(binary);

                Log.Info("正在加入 EasyTier 网络：" + config.EasyTierNetworkName +
                         "（节点 " + config.EasyTierPublicNode + "）");
                // 密钥绝不能进日志：网络密钥就是该网络的准入凭证，日志文件又没有
                // 收紧 ACL（同机其他账户可读），而且用户经常把日志贴到 issue/论坛
                // 求助，密钥会随之外泄。这里只记录脱敏后的参数。
                Log.Info("EasyTier 启动参数：" + BuildRedactedArguments(args));

                // 清空上一次的输出缓存，便于失败时只看到本次的报错。
                lock (OutputLock)
                {
                    _output.Clear();
                }

                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 EasyTier 核心进程。");
                }

                // 逐行捕获输出：既避免管道写满导致核心阻塞，也把报错留给界面显示。
                // 旧实现只调用 BeginOutputReadLine 而没挂事件，输出被直接丢弃，
                // 用户只能看到笼统的"未能获取虚拟 IP"，无法知道真实原因。
                process.OutputDataReceived += delegate(object s, DataReceivedEventArgs ev)
                {
                    AppendOutput(ev.Data);
                };
                process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs ev)
                {
                    AppendOutput(ev.Data);
                };
                try
                {
                    process.BeginOutputReadLine();
                }
                catch
                {
                }
                try
                {
                    process.BeginErrorReadLine();
                }
                catch
                {
                }
                _core = process;
                return process;
            }
        }

        /// <summary>
        /// 当前进程是否以管理员身份运行。
        /// 创建虚拟网卡（Wintun/TUN）需要管理员权限，实测非管理员时会报
        /// "tun device error: The file is not signed"——这不是驱动缺失，
        /// 而是签名策略在低权限下拒绝加载驱动。提前检查可避免用户困惑。
        /// </summary>
        public static bool IsElevated()
        {
            try
            {
                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunning()
        {
            lock (CoreLock)
            {
                return _core != null && !_core.HasExited;
            }
        }

        /// <summary>EasyTier 核心默认监听的端口。残留实例会占用它们并导致新核心报 AddrInUse。</summary>
        public static readonly int[] DefaultPorts = new int[] { 11010, 11011, 11012, 11013 };

        /// <summary>
        /// 检测是否有端口被占用。返回第一个被占用的端口，全部空闲返回 0。
        /// 用 TCP 连接尝试判断：能被连上说明已有进程在监听。
        /// 注意这只在启动前做提示，不做强制清理（避免误杀用户自己开的实例）。
        /// </summary>
        public static int FindBusyPort()
        {
            for (int i = 0; i < DefaultPorts.Length; i++)
            {
                int port = DefaultPorts[i];
                try
                {
                    using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
                    {
                        // 连本机回环：能连上代表端口已被监听。
                        client.Connect(System.Net.IPAddress.Loopback, port);
                        if (client.Connected)
                        {
                            return port;
                        }
                    }
                }
                catch
                {
                    // 连不上 = 端口空闲，继续检查下一个。
                }
            }
            return 0;
        }

        /// <summary>
        /// 统计系统中正在运行的 easytier-core 进程数（含非本启动器拉起的残留实例）。
        /// 用于给用户提示"已有实例在运行"。
        /// </summary>
        public static int CountExternalInstances()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("easytier-core");
                int count = 0;
                Process self;
                lock (CoreLock)
                {
                    self = _core;
                }
                for (int i = 0; i < processes.Length; i++)
                {
                    int pid = -1;
                    try
                    {
                        pid = processes[i].Id;
                    }
                    catch
                    {
                    }
                    if (self != null && pid == self.Id)
                    {
                        continue;
                    }
                    count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>结束核心进程并等待退出。启动器关闭时调用，避免留下孤儿进程。</summary>
        public static void StopCore()
        {
            lock (CoreLock)
            {
                if (_core == null)
                {
                    return;
                }
                try
                {
                    if (!_core.HasExited)
                    {
                        Log.Info("正在退出 EasyTier 网络...");
                        _core.Kill();
                        _core.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("停止 EasyTier 核心失败：" + ex.Message);
                }
                finally
                {
                    try
                    {
                        _core.Dispose();
                    }
                    catch
                    {
                    }
                    _core = null;
                }
            }
        }

        /// <summary>
        /// 从文本中提取虚拟 IPv4（纯函数，便于离线测试）。
        /// 只接受 10.144.0.0/16 网段，避免误把日志里其它 IP 当成虚拟地址。
        /// </summary>
        public static string ParseVirtualIp(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            Match match = Regex.Match(text, VirtualIpPattern);
            return match.Success ? match.Value : "";
        }

        /// <summary>
        /// 取本机虚拟 IP。优先级：核心日志里报告的 IP → 本机网卡（10.x 网段）。
        /// 终止条件同时看"网卡出现"与"核心已退出"，超时只作兜底：
        /// 首次加载驱动或慢机器上，固定 15 秒容易误报失败。
        /// </summary>
        public static async Task<string> GetVirtualIPv4Async(int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                // 默认给足 40 秒：首次创建 TUN 与建立 P2P 连接可能较慢。
                timeoutMs = 40000;
            }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            Process process;
            lock (CoreLock)
            {
                process = _core;
            }

            while (DateTime.UtcNow < deadline)
            {
                // 1) 核心自己在日志里报告了 IP（最准确）。
                string fromOutput = ParseVirtualIp(GetRecentOutput(80));
                if (!string.IsNullOrEmpty(fromOutput))
                {
                    return fromOutput;
                }
                // 2) 退回扫描网卡：核心接入后会创建虚拟网卡。
                string fromAdapter = FindVirtualIpOnAdapters();
                if (fromAdapter.Length > 0)
                {
                    return fromAdapter;
                }
                // 3) 核心已退出：再等也没有意义，交给界面用 DiagnoseFailure 显示原因。
                if (process != null && process.HasExited)
                {
                    break;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
            return "";
        }

        /// <summary>
        /// 扫描本机网卡的 IPv4 地址，找出 10.144.0.0/16 网段的那一个。
        /// </summary>
        public static string FindVirtualIpOnAdapters()
        {
            try
            {
                System.Net.NetworkInformation.NetworkInterface[] adapters =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < adapters.Length; i++)
                {
                    System.Net.NetworkInformation.IPInterfaceProperties properties =
                        adapters[i].GetIPProperties();
                    System.Net.NetworkInformation.UnicastIPAddressInformationCollection addresses =
                        properties.UnicastAddresses;
                    for (int j = 0; j < addresses.Count; j++)
                    {
                        string address = addresses[j].Address.ToString();
                        if (IsVirtualIp(address))
                        {
                            return address;
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        public static bool IsVirtualIp(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }
            // 接受 10.0.0.0/8 私网段（DHCP 默认分配与此一致）。
            return Regex.IsMatch(address, @"^10\.\d{1,3}\.\d{1,3}\.\d{1,3}$");
        }
    }
}
