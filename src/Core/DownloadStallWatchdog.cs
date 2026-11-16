using System;
using System.Diagnostics;
using System.Threading;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 下载停滞检测（watchdog）。
    ///
    /// ## 为什么不用固定的总时长超时
    ///
    /// 旧实现给整个下载设了 15 分钟硬超时（HttpClient.Timeout）。
    /// 但这个超时覆盖的是**整个操作**（含响应体读取），于是：
    ///
    /// | 速度 | 50 MB | 200 MB | 500 MB |
    /// | --- | --- | --- | --- |
    /// | 0.6 MB/s | 1.4 分 ok | 5.6 分 ok | 13.9 分 ok |
    /// | 0.2 MB/s | 4.2 分 ok | **16.7 分 FAIL** | **41.7 分 FAIL** |
    /// | 0.05 MB/s | **16.7 分 FAIL** | **66.7 分 FAIL** | **166.7 分 FAIL** |
    ///
    /// 也就是说：**网络只是慢，下载却被中途掐断**，用户看到"下载失败"，
    /// 而实际连接是通的。重试又从 0 开始——死循环。
    ///
    /// 固定总时长无法区分「慢」和「死」：
    /// 一个 47 MB 的文件在 0.05 MB/s 的网络下需要 15 分钟以上，
    /// 但它**一直在动**，没有理由中断它。
    ///
    /// ## 判定标准
    ///
    /// 只有**连续 N 秒没有收到任何字节**才判定为卡死并中止。
    /// 慢但持续有数据的连接不会被误杀；真正卡死（服务器不再响应、
    /// 连接半开）的连接会在 N 秒内被及时发现。
    ///
    /// 纯逻辑，无 IO，便于离线测试。
    /// </summary>
    public sealed class DownloadStallWatchdog
    {
        private readonly int _stallTimeoutSeconds;
        private readonly Stopwatch _clock = new Stopwatch();
        private DateTime _lastActivityUtc;
        private volatile bool _stalled;

        /// <summary>
        /// 构造一个看门狗。
        /// </summary>
        /// <param name="stallTimeoutSeconds">
        /// 连续多少秒没有收到数据就判定为卡死。建议 30-60 秒：
        /// 低于 20 秒会误杀极慢的连接，高于 120 秒则卡死的连接要等太久才发现。
        /// </param>
        public DownloadStallWatchdog(int stallTimeoutSeconds)
        {
            if (stallTimeoutSeconds <= 0)
            {
                stallTimeoutSeconds = 30;
            }
            _stallTimeoutSeconds = stallTimeoutSeconds;
            _clock.Start();
            _lastActivityUtc = DateTime.UtcNow;
        }

        /// <summary>连续无数据多少秒后判定为卡死。</summary>
        public int StallTimeoutSeconds
        {
            get { return _stallTimeoutSeconds; }
        }

        /// <summary>是否已判定为卡死。</summary>
        public bool IsStalled
        {
            get { return _stalled; }
        }

        /// <summary>距离上次收到数据过了多少秒。</summary>
        public double SecondsSinceActivity
        {
            get { return (DateTime.UtcNow - _lastActivityUtc).TotalSeconds; }
        }

        /// <summary>
        /// 收到数据时调用，刷新"最后活动时间"。
        /// </summary>
        public void Touch()
        {
            _lastActivityUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// 检查是否已经卡死。
        /// </summary>
        public bool CheckStalled()
        {
            if (SecondsSinceActivity >= _stallTimeoutSeconds)
            {
                _stalled = true;
            }
            return _stalled;
        }

        /// <summary>
        /// 起一个后台监测线程：卡死时取消给定的令牌。
        ///
        /// 下载正常结束（调用方取消 source）时监测线程随之退出。
        /// </summary>
        public static DownloadStallWatchdog StartWatchdog(
            int stallTimeoutSeconds,
            CancellationTokenSource source,
            out CancellationToken token)
        {
            DownloadStallWatchdog watchdog = new DownloadStallWatchdog(stallTimeoutSeconds);
            // C# 5 不允许在 lambda 里捕获 out 参数，所以先复制到局部变量。
            CancellationToken monitorToken = source.Token;
            token = monitorToken;
            // CancellationToken 没有 WaitOne，用一个事件来做「可等待的取消信号」。
            ManualResetEvent stopSignal = new ManualResetEvent(false);
            // 令牌被取消时立刻唤醒监测线程，避免它白等 1 秒才退出。
            CancellationTokenRegistration registration = monitorToken.Register(
                delegate { try { stopSignal.Set(); } catch { } });
            Thread thread = new Thread(delegate()
            {
                while (!monitorToken.IsCancellationRequested)
                {
                    try
                    {
                        // 等待 1 秒或收到取消信号，谁先来算谁。
                        if (stopSignal.WaitOne(1000))
                        {
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                    if (watchdog.CheckStalled())
                    {
                        try
                        {
                            source.Cancel();
                        }
                        catch
                        {
                        }
                        break;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "RLL Download Stall Watchdog";
            thread.Start();
            // 下载正常结束（调用方取消 source）时，监测线程靠这个事件退出。
            // 事件本身交给 GC 即可（它是纯等待句柄，没有非托管资源需要显式释放）。
            return watchdog;
        }
    }
}