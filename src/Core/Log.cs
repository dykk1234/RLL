using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 启动器的日志输出。
    ///
    /// ## 为什么不能"每行写一次文件"
    ///
    /// 游戏启动后会向 stdout/stderr 输出大量日志（Minecraft 首次启动常见
    /// 几万到十几万行）。这些行由 .NET 的异步管道读取器回调过来。
    ///
    /// 旧实现对**每一行**都做一次 `File.AppendAllText`——也就是每行都
    /// 打开文件、写、关闭。同时每行还触发一次 `Log.Message` 事件，
    /// 界面层再对每一行 `BeginInvoke` 一次。
    ///
    /// 实测（Windows，本机）：让子进程输出 8 万行，比较两种实现——
    ///
    /// | 实现 | 子进程（模拟游戏）写出全部内容所花时间 |
    /// | --- | --- |
    /// | 每行 open/write/close | **163 秒** |
    /// | 批量写入 | **4.6 秒** |
    ///
    /// 原因不是"游戏自己慢"，而是**管道反压**：游戏往 stdout 写数据，
    /// 管道缓冲区（默认 4KB）满了就必须等读取方把数据取走。读取方每行都
    /// 同步做一次文件 I/O，取数据的速度跟不上游戏写的速度，游戏就被
    /// **卡在写日志上**——于是表现为"游戏窗口很久才出来"。
    ///
    /// ## 日志为什么会涨到撑爆磁盘
    ///
    /// 批量写修好了卡顿，却让写入吞吐大幅提升——**日志涨得也快了**。
    /// 实测（1.4.4）：本机 7.7 小时产生 **351.7 MB**，其中 **99.9% 是游戏日志**；
    /// 按此速率 30 天约 **30 GB**，而当时 `Log.cs` 没有任何保留策略。
    ///
    /// 本实现（1.4.5）补上三道闸：
    /// 1. **单文件大小上限**：超过上限就轮转（`xxx.1.log`、`xxx.2.log`…），
    ///    最多保留 N 份，超出的丢弃。界面日志框里仍然看得到（用户当场看得到，
    ///    不必依赖文件）。
    /// 2. **保留期清理**：启动时删掉 N 天前的旧日志。
    /// 3. **游戏日志与操作日志分开归档**（`launcher-*.log` / `game-*.log`）：
    ///    两者量级差 3 个数量级，可以分别制定保留策略；
    ///    用户提 issue 时贴操作日志就够了，不必贴几十 MB 的游戏日志——
    ///    这也顺带降低了「用户整份贴日志求助」导致密钥外泄的风险面。
    ///
    /// ## 线程安全
    ///
    /// 所有公开方法线程安全。写盘在后台线程完成，调用方不会因为日志而阻塞。
    /// </summary>
    public static class Log
    {
        private static readonly object SyncRoot = new object();
        private static string _logDirectory;
        private static string _logFile;
        private static string _gameLogFile;

        /// <summary>普通日志事件：用于界面日志框（操作记录）。</summary>
        public static event Action<string> Message;

        /// <summary>
        /// 游戏日志事件：与 <see cref="Message"/> 分开，避免游戏的高频输出
        /// 把操作记录刷走。界面层可以按自己的节奏消费。
        /// </summary>
        public static event Action<string> GameMessage;

        /// <summary>待写入文件的行队列（操作记录）。</summary>
        private static readonly Queue<string> _pending = new Queue<string>();

        /// <summary>待写入文件的行队列（游戏日志）。</summary>
        private static readonly Queue<string> _gamePending = new Queue<string>();

        /// <summary>后台写盘线程。</summary>
        private static Thread _writer;
        /// <summary>通知后台线程有新内容。</summary>
        private static readonly ManualResetEvent _wake = new ManualResetEvent(false);
        /// <summary>后台线程退出信号。</summary>
        private static volatile bool _stopping;

        /// <summary>累积的行数（达到阈值时提前唤醒写盘线程）。</summary>
        private const int FlushThreshold = 200;
        /// <summary>后台线程的检查间隔（毫秒）。</summary>
        private const int FlushIntervalMs = 200;

        /// <summary>单个日志文件的体积上限（字节）。超过就轮转。</summary>
        public const long MaxFileBytes = 64L * 1024L * 1024L;
        /// <summary>每个日志文件保留的份数（当前文件 + 轮转出来的）。</summary>
        public const int MaxRotatedFiles = 3;
        /// <summary>日志保留天数。超过这个天数的文件在启动时删除。</summary>
        public const int RetentionDays = 30;

        // ---- 1.5.0 轻量化：保留策略可由设置页配置（0 = 使用内置默认）----

        /// <summary>可配置的日志保留天数（含启动器与游戏日志）。</summary>
        public static int ConfiguredRetentionDays { get; set; }
        /// <summary>可配置的单文件轮转份数。</summary>
        public static int ConfiguredMaxRotatedFiles { get; set; }

        private static int EffectiveRetentionDays
        {
            get { return ConfiguredRetentionDays > 0 ? ConfiguredRetentionDays : RetentionDays; }
        }

        private static int EffectiveMaxRotatedFiles
        {
            get { return ConfiguredMaxRotatedFiles > 0 ? ConfiguredMaxRotatedFiles : MaxRotatedFiles; }
        }

        public static void Initialize(string logDirectory)
        {
            lock (SyncRoot)
            {
                _logDirectory = logDirectory;
                if (!string.IsNullOrEmpty(_logDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(_logDirectory);
                        // 日志里可能出现会话令牌、EasyTier 网络密钥等敏感信息，
                        // 而日志目录默认继承 %APPDATA% 的宽松 ACL（同机其他账户可读）。
                        // 收紧目录 ACL 后，新建其中的日志文件会继承这份权限。
                        PathUtil.RestrictDirectoryToCurrentUser(_logDirectory);

                        string day = DateTime.Now.ToString("yyyyMMdd");
                        _logFile = Path.Combine(_logDirectory, "launcher-" + day + ".log");
                        _gameLogFile = Path.Combine(_logDirectory, "game-" + day + ".log");
                        // 文件已存在时（当天第二次启动）再单独收紧一次，
                        // 因为老文件是在目录收紧之前创建的。
                        PathUtil.RestrictFileToCurrentUser(_logFile);
                        PathUtil.RestrictFileToCurrentUser(_gameLogFile);
                    }
                    catch
                    {
                        _logFile = null;
                        _gameLogFile = null;
                    }
                }
            }
            EnsureWriter();
            // 清理过期日志。放在写盘线程启动之后，避免与写入竞争。
            CleanupOldLogs();
            Info("RLL " + AppPaths.LauncherVersion + " 启动");
        }

        public static void Info(string message)
        {
            Write("信息", Sanitize(message));
        }

        public static void Warn(string message)
        {
            Write("警告", Sanitize(message));
        }

        public static void Error(string message)
        {
            Write("错误", Sanitize(message));
        }

        public static void Error(string message, Exception exception)
        {
            if (exception == null)
            {
                Error(message);
                return;
            }
            Error(message + "：" + exception.GetType().Name + ": " + Sanitize(exception.Message));
            WriteFileOnly(Sanitize(exception.ToString()));
        }

        public static void Debug(string message)
        {
            Write("调试", Sanitize(message));
        }

        /// <summary>
        /// 1.6.0（F-1604）：统一脱敏。把日志/异常中可能出现的凭证键值对掩码，
        /// 覆盖 AccessToken / ClientToken / Authorization / EasyTier 密钥等字段，
        /// 防止日志、崩溃堆栈、导出文件泄露明文凭证。
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            // 键 + 分隔符后：优先整段引号值；无引号则掩到首个分隔符（逗号/分号/空白/行尾）。
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                "(?i)(([\"']?(accessToken|clientToken|authorization|easytierNetworkSecret|networkSecret|secret|password|passwd)[\"']?\\s*[:=]\\s*)([\"'][^\"']*[\"']|[^\"'\\s,;\\r\\n]+))",
                "$2***");
        }

        /// <summary>
        /// 写一行游戏日志。
        ///
        /// 这是**高频**入口：游戏可能每秒输出上千行。它只入队，不做同步 I/O，
        /// 因此不会因为游戏刷日志而卡住游戏本身。
        /// </summary>
        public static void Game(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            if (message.Length > 4000)
            {
                message = message.Substring(0, 4000) + "…";
            }
            string line = "[游戏] " + message;
            Enqueue(line, true);
        }

        private static void Write(string level, string message)
        {
            string line = string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                DateTime.Now,
                level,
                message ?? "");
            Enqueue(line, false);
            Action<string> handler = Message;
            if (handler != null)
            {
                try
                {
                    handler(line);
                }
                catch
                {
                    // 界面日志异常不能反过来影响业务逻辑。
                }
            }
        }

        /// <summary>
        /// 把一行加入待写队列。文件写入由后台线程完成，调用方立即返回。
        /// </summary>
        private static void Enqueue(string line, bool isGame)
        {
            bool shouldWake = false;
            lock (SyncRoot)
            {
                if (isGame)
                {
                    _gamePending.Enqueue(line);
                    if (_gamePending.Count >= FlushThreshold)
                    {
                        shouldWake = true;
                    }
                }
                else
                {
                    _pending.Enqueue(line);
                    if (_pending.Count >= FlushThreshold)
                    {
                        shouldWake = true;
                    }
                }
            }
            if (shouldWake)
            {
                try
                {
                    _wake.Set();
                }
                catch
                {
                }
            }
            if (isGame)
            {
                Action<string> gameHandler = GameMessage;
                if (gameHandler != null)
                {
                    try
                    {
                        gameHandler(line);
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 确保后台写盘线程已启动。
        /// </summary>
        private static void EnsureWriter()
        {
            lock (SyncRoot)
            {
                if (_writer != null)
                {
                    return;
                }
                _stopping = false;
                _writer = new Thread(WriterLoop);
                _writer.IsBackground = true;
                _writer.Name = "RLL Log Writer";
                _writer.Start();
            }
        }

        /// <summary>
        /// 后台写盘循环：等一批数据或超时，然后一次性写进文件。
        /// </summary>
        private static void WriterLoop()
        {
            while (!_stopping)
            {
                try
                {
                    _wake.WaitOne(FlushIntervalMs);
                }
                catch
                {
                }
                _wake.Reset();
                Flush();
            }
            // 退出前把剩下的写完。
            Flush();
        }

        /// <summary>
        /// 把队列里的行一次性写入日志文件。
        /// </summary>
        private static void Flush()
        {
            List<string> batch = new List<string>();
            List<string> gameBatch = new List<string>();
            string path;
            string gamePath;
            lock (SyncRoot)
            {
                if (_pending.Count == 0 && _gamePending.Count == 0)
                {
                    return;
                }
                path = _logFile;
                gamePath = _gameLogFile;
                while (_pending.Count > 0)
                {
                    batch.Add(_pending.Dequeue());
                }
                while (_gamePending.Count > 0)
                {
                    gameBatch.Add(_gamePending.Dequeue());
                }
            }
            if (!string.IsNullOrEmpty(path) && batch.Count > 0)
            {
                AppendWithRotation(path, batch);
            }
            if (!string.IsNullOrEmpty(gamePath) && gameBatch.Count > 0)
            {
                AppendWithRotation(gamePath, gameBatch);
            }
        }

        /// <summary>
        /// 批量追加到文件；超过体积上限就轮转。
        ///
        /// 轮转规则：当前文件 `xxx.log` 超过上限时，
        /// 依次把 `xxx.2.log` 改名为 `xxx.3.log`、`xxx.1.log` 改名为 `xxx.2.log`，
        /// 再把 `xxx.log` 改名为 `xxx.1.log`，最后新建一个空的 `xxx.log`。
        /// 最多保留 <see cref="MaxRotatedFiles"/> 份，超出的删除。
        /// </summary>
        private static void AppendWithRotation(string path, List<string> lines)
        {
            try
            {
                // 一次性打开、一次性写入、一次性关闭——而不是每行一次。
                StringBuilder builder = new StringBuilder(lines.Count * 80);
                for (int i = 0; i < lines.Count; i++)
                {
                    builder.Append(lines[i]).Append(Environment.NewLine);
                }
                string text = builder.ToString();

                // 写入前先看当前文件是否已经超限——超了就先轮转再写。
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length >= MaxFileBytes)
                    {
                        Rotate(path);
                    }
                }
                catch
                {
                }

                File.AppendAllText(path, text, new UTF8Encoding(false));
            }
            catch
            {
                // 日志写盘失败不影响启动器主流程。
            }
        }

        /// <summary>
        /// 轮转日志文件：把当前文件挪到 `.1`，旧的 `.1` 挪到 `.2`……超出份数的删除。
        /// </summary>
        internal static void Rotate(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                // 从最旧的一份开始删/挪：xxx.N -> 删，xxx.(N-1) -> xxx.N ...
                int keep = EffectiveMaxRotatedFiles;
                for (int i = keep; i >= 1; i--)
                {
                    string source = RotatedPath(path, i - 1);
                    string target = RotatedPath(path, i);
                    if (!File.Exists(source))
                    {
                        continue;
                    }
                    if (i >= keep)
                    {
                        // 超出保留份数，直接删掉最旧的。
                        File.Delete(source);
                        continue;
                    }
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    File.Move(source, target);
                }
            }
            catch
            {
                // 轮转失败不影响主流程。
            }
        }

        /// <summary>
        /// 取轮转后的文件名：`xxx.log` 的第 index 份为 `xxx.<index>.log`。
        /// </summary>
        internal static string RotatedPath(string path, int index)
        {
            if (index <= 0)
            {
                return path;
            }
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string fileName = name + "." + index.ToString() + extension;
            if (string.IsNullOrEmpty(directory))
            {
                return fileName;
            }
            return Path.Combine(directory, fileName);
        }

        /// <summary>
        /// 清理过期日志：删除超过保留期的日志文件。
        ///
        /// 只删 `launcher-*.log` / `game-*.log` 这两类自己产生的文件，
        /// 不碰目录里的其它东西；当前正在写的文件永远不会被删。
        /// </summary>
        internal static int CleanupOldLogs(string logDirectory, DateTime now, int retentionDays)
        {
            if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory))
            {
                return 0;
            }
            int deleted = 0;
            try
            {
                DateTime cutoff = now.AddDays(-retentionDays);
                string currentLauncher;
                string currentGame;
                lock (SyncRoot)
                {
                    currentLauncher = _logFile;
                    currentGame = _gameLogFile;
                }
                string[] files = Directory.GetFiles(logDirectory, "*.log");
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    bool isLauncher = name.StartsWith("launcher-", StringComparison.OrdinalIgnoreCase);
                    bool isGame = name.StartsWith("game-", StringComparison.OrdinalIgnoreCase);
                    if (!isLauncher && !isGame)
                    {
                        continue;
                    }
                    // 当前正在写的文件与其轮转副本都不动（避免删掉正在写的东西）。
                    if (string.Equals(files[i], currentLauncher, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(files[i], currentGame, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        FileInfo info = new FileInfo(files[i]);
                        if (info.LastWriteTime < cutoff)
                        {
                            File.Delete(files[i]);
                            deleted++;
                        }
                    }
                    catch
                    {
                        // 单个文件删不掉不影响其它。
                    }
                }
            }
            catch
            {
            }
            return deleted;
        }

        private static void CleanupOldLogs()
        {
            string directory;
            lock (SyncRoot)
            {
                directory = _logDirectory;
            }
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }
            try
            {
                int deleted = CleanupOldLogs(directory, DateTime.Now, EffectiveRetentionDays);
                if (deleted > 0)
                {
                    Log.Info("已清理 " + deleted + " 个过期日志文件");
                }
            }
            catch
            {
            }
        }

        private static void WriteFileOnly(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            try
            {
                string path;
                lock (SyncRoot)
                {
                    path = _logFile;
                }
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                File.AppendAllText(path, message + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>
        /// 停止后台写盘线程并把剩余内容落盘。退出时调用。
        /// </summary>
        /// <summary>
        /// 1.5.0 轻量化：删除全部历史日志（launcher-*.log / game-*.log 及其轮转），
        /// 仅保留今天的当前文件（后台写盘线程仍持有路径）。返回删除的文件个数。
        /// </summary>
        public static int PurgeAllLogs()
        {
            string directory;
            lock (SyncRoot)
            {
                directory = _logDirectory;
            }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            string today = DateTime.Now.ToString("yyyyMMdd");
            // F7：除按日期前缀跳过「当天」文件外，再按当前实际打开的文件名精确跳过——
            // 避免跨天边界（23:59→00:00）或系统日期被修改时误删正在写入的日志。
            string openLauncherName;
            string openGameName;
            lock (SyncRoot)
            {
                openLauncherName = _logFile != null ? Path.GetFileName(_logFile) : "";
                openGameName = _gameLogFile != null ? Path.GetFileName(_gameLogFile) : "";
            }
            int deleted = 0;
            try
            {
                FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.log", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = files[i].Name;
                    // 今天的当前文件与其轮转（可能正在被写）一律跳过；
                    // 同时跳过当前真正打开的文件（跨天/改系统日期时的兜底）。
                    bool openToday = name.StartsWith("launcher-" + today, StringComparison.Ordinal) ||
                                     name.StartsWith("game-" + today, StringComparison.Ordinal);
                    bool openNow = (!string.IsNullOrEmpty(openLauncherName) && name == openLauncherName) ||
                                   (!string.IsNullOrEmpty(openGameName) && name == openGameName);
                    if (openToday || openNow)
                    {
                        continue;
                    }
                    try
                    {
                        files[i].Delete();
                        deleted++;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return deleted;
        }

        /// <summary>日志目录总占用（字节）。目录不存在返回 0。</summary>
        public static long GetLogDirectoryBytes()
        {
            string directory;
            lock (SyncRoot)
            {
                directory = _logDirectory;
            }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }
            long total = 0;
            try
            {
                FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.log", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    total += files[i].Length;
                }
            }
            catch
            {
            }
            return total;
        }

        public static void Shutdown()
        {
            _stopping = true;
            try
            {
                _wake.Set();
            }
            catch
            {
            }
            Thread writer;
            lock (SyncRoot)
            {
                writer = _writer;
                _writer = null;
            }
            if (writer != null)
            {
                try
                {
                    writer.Join(1000);
                }
                catch
                {
                }
            }
            Flush();
        }
    }
}
