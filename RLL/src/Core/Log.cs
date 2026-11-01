using System;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    public static class Log
    {
        private static readonly object SyncRoot = new object();
        private static string _logDirectory;
        private static string _logFile;

        public static event Action<string> Message;

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
                        _logFile = Path.Combine(
                            _logDirectory,
                            "launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                        // 文件已存在时（当天第二次启动）再单独收紧一次，
                        // 因为老文件是在目录收紧之前创建的。
                        PathUtil.RestrictFileToCurrentUser(_logFile);
                    }
                    catch
                    {
                        _logFile = null;
                    }
                }
            }
            Info("RLL " + AppPaths.LauncherVersion + " 启动");
        }

        public static void Info(string message)
        {
            Write("信息", message);
        }

        public static void Warn(string message)
        {
            Write("警告", message);
        }

        public static void Error(string message)
        {
            Write("错误", message);
        }

        public static void Error(string message, Exception exception)
        {
            if (exception == null)
            {
                Error(message);
                return;
            }
            Error(message + "：" + exception.GetType().Name + ": " + exception.Message);
            WriteFileOnly(exception.ToString());
        }

        public static void Debug(string message)
        {
            Write("调试", message);
        }

        private static void Write(string level, string message)
        {
            string line = string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                DateTime.Now,
                level,
                message ?? "");
            lock (SyncRoot)
            {
                if (!string.IsNullOrEmpty(_logFile))
                {
                    try
                    {
                        File.AppendAllText(_logFile, line + Environment.NewLine, new UTF8Encoding(false));
                    }
                    catch
                    {
                        // 日志写盘失败不影响启动器主流程。
                    }
                }
            }
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

        private static void WriteFileOnly(string message)
        {
            lock (SyncRoot)
            {
                if (string.IsNullOrEmpty(_logFile))
                {
                    return;
                }
                try
                {
                    File.AppendAllText(_logFile, message + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }
    }
}
