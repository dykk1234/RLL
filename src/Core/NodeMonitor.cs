using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>单个节点的探测结果。</summary>
    public sealed class NodeProbeResult
    {
        /// <summary>原始节点地址（如 tcp://host:port）。</summary>
        public string Node;
        /// <summary>延迟毫秒；失败为 -1。</summary>
        public int LatencyMs = -1;
        /// <summary>是否连通。</summary>
        public bool Success;
        public string Error;
    }

    /// <summary>
    /// 1.5.0 联机稳定性：公共节点延迟探测。
    /// 用 TCP Connect 耗时作为延迟估计（EasyTier 公共节点以 tcp 为主；
    /// udp:// 节点同样按 TCP 探测，仅作"主机是否可达"的参考，结果界面会注明）。
    /// 纯客户端行为，不发送任何业务数据。
    /// </summary>
    public static class NodeMonitor
    {
        public const int DefaultTimeoutMs = 3000;

        /// <summary>
        /// 解析节点地址：支持 tcp://host:port、udp://host:port、host:port、
        /// 方括号 IPv6（[2001:db8::1] 或 [2001:db8::1]:11010）与无端口 IPv6 字面量（2001:db8::1）。
        /// 省略端口时使用 EasyTier 公共节点默认端口 11010。
        /// </summary>
        public static bool TryParseNode(string node, out string host, out int port)
        {
            host = "";
            port = EasyTierService.PublicNodePort;
            if (string.IsNullOrEmpty(node))
            {
                return false;
            }
            string value = node.Trim();
            int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0)
            {
                value = value.Substring(schemeEnd + 3);
            }
            // 去掉路径部分（有的地址会带 /path）。
            int slash = value.IndexOf('/');
            if (slash >= 0)
            {
                value = value.Substring(0, slash);
            }
            if (value.Length == 0)
            {
                return false;
            }
            if (value[0] == '[')
            {
                // 方括号 IPv6 字面量：[2001:db8::1] 或 [2001:db8::1]:11010。
                int close = value.IndexOf(']');
                if (close <= 1)
                {
                    return false;
                }
                host = value.Substring(1, close - 1);
                string rest = value.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (rest[0] != ':')
                    {
                        return false;
                    }
                    string portText = rest.Substring(1).Trim();
                    int parsed;
                    if (!int.TryParse(portText, out parsed) || parsed <= 0 || parsed > 65535)
                    {
                        return false;
                    }
                    port = parsed;
                }
                return IsValidHost(host);
            }
            int colon = value.LastIndexOf(':');
            if (colon >= 0)
            {
                string head = value.Substring(0, colon);
                string portText = value.Substring(colon + 1).Trim();
                int parsed;
                bool numericPort = int.TryParse(portText, out parsed);
                if (!head.Contains(":") && numericPort && parsed > 0 && parsed <= 65535)
                {
                    // 规范的 host:port（IPv4 / 主机名）。
                    host = head.Trim();
                    port = parsed;
                }
                else if (head.Contains(":"))
                {
                    // 无端口 IPv6 字面量（如 2001:db8::1）：整串作为主机，端口用默认。
                    host = value.Trim();
                }
                else
                {
                    // 单个冒号但端口段非法（"host:" / "host:abc"）→ 无效。
                    return false;
                }
            }
            else
            {
                host = value.Trim();
            }
            return IsValidHost(host);
        }

        /// <summary>主机名校验：允许字母数字、点、连字符与冒号（IPv6 分隔符）。</summary>
        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > 253)
            {
                return false;
            }
            for (int i = 0; i < host.Length; i++)
            {
                char c = host[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '.' || c == '-' || c == ':';
                // '[' ']' 已在方括号分支拆走；IPv6 zone id（fe80::1%12）暂不支持。
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>同步探测单个节点。超时或连接失败都算不通。</summary>
        public static NodeProbeResult Probe(string node, int timeoutMs)
        {
            NodeProbeResult result = new NodeProbeResult();
            result.Node = node;
            string host;
            int port;
            if (!TryParseNode(node, out host, out port))
            {
                result.Error = "地址无法解析";
                return result;
            }
            Stopwatch watch = Stopwatch.StartNew();
            TcpClient client = new TcpClient();
            try
            {
                IAsyncResult async = client.BeginConnect(host, port, null, null);
                if (!async.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    result.Error = "连接超时";
                    return result;
                }
                client.EndConnect(async);
                watch.Stop();
                result.LatencyMs = (int)watch.ElapsedMilliseconds;
                result.Success = true;
                return result;
            }
            catch (Exception exception)
            {
                result.Error = exception.Message;
                if (result.Error != null && result.Error.Length > 80)
                {
                    result.Error = result.Error.Substring(0, 80);
                }
                return result;
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        /// <summary>并发探测上限：内置 + 自定义节点较多时避免一次性占满线程池。</summary>
        public const int MaxProbeConcurrency = 4;

        /// <summary>并发探测一批节点（去空、去重）。失败项也返回，便于界面标注。</summary>
        public static async Task<List<NodeProbeResult>> ProbeAllAsync(
            IEnumerable<string> nodes, int timeoutMs, CancellationToken token)
        {
            List<string> list = new List<string>();
            if (nodes != null)
            {
                foreach (string node in nodes)
                {
                    if (string.IsNullOrEmpty(node))
                    {
                        continue;
                    }
                    if (!list.Contains(node))
                    {
                        list.Add(node);
                    }
                }
            }
            SemaphoreSlim gate = new SemaphoreSlim(MaxProbeConcurrency, MaxProbeConcurrency);
            List<Task<NodeProbeResult>> tasks = new List<Task<NodeProbeResult>>();
            for (int i = 0; i < list.Count; i++)
            {
                string node = list[i];
                tasks.Add(Task.Run(async delegate
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        return Probe(node, timeoutMs);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }
            try
            {
                NodeProbeResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                return new List<NodeProbeResult>(results);
            }
            finally
            {
                gate.Dispose();
            }
        }
    }
}
