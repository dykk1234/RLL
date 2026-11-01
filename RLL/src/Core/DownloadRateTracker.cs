using System;
using System.Diagnostics;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 下载速度与剩余时间（ETA）估算。
    ///
    /// 为什么需要它：Minecraft 首次安装要下载几百 MB 到 1-2 GB，
    /// 界面只显示一个百分比，用户完全无法判断"还要等多久""是不是卡住了"。
    ///
    /// 实现要点：
    /// - 用**滑动窗口**而不是全程平均：网络刚起步时速度样本太少，
    ///   全程平均会把瞬时速度拉平，看起来像卡死；滑动窗口能较快反映真实速度。
    /// - 速度单位用「字节/秒」，显示时换算成 MB/s 或 KB/s。
    /// - ETA = 剩余字节 / 当前速度；速度为 0 或样本不足时不给 ETA（显示"—"），
    ///   避免出现「剩余 999 小时」这种吓人的假数字。
    ///
    /// 纯逻辑，无 IO，便于离线测试。
    /// </summary>
    public sealed class DownloadRateTracker
    {
        /// <summary>滑动窗口内保留的样本数。</summary>
        private const int WindowSize = 5;

        private readonly Stopwatch _clock = new Stopwatch();
        private readonly long[] _sampleBytes = new long[WindowSize];
        private readonly double[] _sampleSeconds = new double[WindowSize];
        private int _sampleCount;
        private int _sampleIndex;
        private long _lastBytes;

        public DownloadRateTracker()
        {
            Reset();
        }

        /// <summary>
        /// 重新开始计时（一次新的下载开始时调用）。
        /// </summary>
        public void Reset()
        {
            _clock.Reset();
            _clock.Start();
            _sampleCount = 0;
            _sampleIndex = 0;
            _lastBytes = 0;
        }

        /// <summary>
        /// 记录一次进度更新，返回当前估算的字节/秒。
        /// 第一次调用只建立基线，不产生样本（否则会算出一个巨大的假速度）。
        /// </summary>
        public double Update(long totalBytes)
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            if (_sampleCount == 0)
            {
                _lastBytes = totalBytes;
                _sampleCount = 1;
                _sampleIndex = 0;
                _sampleBytes[0] = totalBytes;
                _sampleSeconds[0] = elapsed;
                return 0.0;
            }

            long delta = totalBytes - _lastBytes;
            _lastBytes = totalBytes;
            if (delta <= 0)
            {
                return GetSpeed();
            }
            if (elapsed <= _sampleSeconds[_sampleIndex])
            {
                // 两次采样间隔过短（同一毫秒内多次回调），忽略这次样本。
                return GetSpeed();
            }

            _sampleIndex = (_sampleIndex + 1) % WindowSize;
            _sampleBytes[_sampleIndex] = totalBytes;
            _sampleSeconds[_sampleIndex] = elapsed;
            if (_sampleCount < WindowSize)
            {
                _sampleCount++;
            }
            return GetSpeed();
        }

        /// <summary>
        /// 当前估算速度（字节/秒）。样本不足时返回 0。
        /// </summary>
        public double GetSpeed()
        {
            if (_sampleCount < 2)
            {
                return 0.0;
            }
            // 取窗口内最早与最新的样本，算出这一段的平均速度。
            int oldest = (_sampleIndex - _sampleCount + 1 + WindowSize) % WindowSize;
            long bytesDelta = _sampleBytes[_sampleIndex] - _sampleBytes[oldest];
            double secondsDelta = _sampleSeconds[_sampleIndex] - _sampleSeconds[oldest];
            if (bytesDelta <= 0 || secondsDelta <= 0)
            {
                return 0.0;
            }
            return bytesDelta / secondsDelta;
        }

        /// <summary>
        /// 剩余时间（秒）。速度不足或已下载完时返回 -1（表示"无法估计"）。
        /// </summary>
        public double GetEta(long totalBytes)
        {
            double speed = GetSpeed();
            if (speed <= 1.0)
            {
                return -1.0;
            }
            long remaining = totalBytes - _lastBytes;
            if (remaining <= 0)
            {
                return 0.0;
            }
            return remaining / speed;
        }

        /// <summary>
        /// 把字节/秒格式化成人类可读的速度（如 "12.4 MB/s"）。
        /// </summary>
        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return "—";
            }
            if (bytesPerSecond >= 1024.0 * 1024.0)
            {
                return (bytesPerSecond / 1024.0 / 1024.0).ToString("0.0") + " MB/s";
            }
            if (bytesPerSecond >= 1024.0)
            {
                return (bytesPerSecond / 1024.0).ToString("0") + " KB/s";
            }
            return bytesPerSecond.ToString("0") + " B/s";
        }

        /// <summary>
        /// 把剩余秒数格式化成人类可读的时长（如 "约 2 分 30 秒"）。
        /// 秒数小于 0 时返回"—"（无法估计）。
        /// </summary>
        public static string FormatEta(double seconds)
        {
            if (seconds < 0)
            {
                return "—";
            }
            if (seconds < 1.0)
            {
                return "即将完成";
            }
            if (seconds < 60.0)
            {
                return "约 " + ((int)seconds).ToString() + " 秒";
            }
            if (seconds < 3600.0)
            {
                int shortMinutes = (int)(seconds / 60.0);
                int shortRest = (int)(seconds - shortMinutes * 60.0);
                return "约 " + shortMinutes.ToString() + " 分 " + shortRest.ToString() + " 秒";
            }
            int hours = (int)(seconds / 3600.0);
            int remainingMinutes = (int)((seconds - hours * 3600.0) / 60.0);
            return "约 " + hours.ToString() + " 小时 " + remainingMinutes.ToString() + " 分";
        }
    }
}
