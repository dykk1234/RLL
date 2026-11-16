using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                // 优先 TLS 1.2 + 1.3：在支持的系统（Win11 + .NET 4.8.1）上升级到 TLS 1.3，
                // 不支持时回退到仅 TLS 1.2。不注册任何证书校验回调，也不提供 SSL 绕过开关。
                // 探测逻辑在 HttpUtil 内，HttpUtil 的静态构造与这里使用同一套结果，
                // 避免此前"Main 开启 TLS 1.3 后被 HttpUtil 静态构造重置回 1.2"的问题。
                ServicePointManager.SecurityProtocol = HttpUtil.ChooseSecurityProtocol();
                ServicePointManager.DefaultConnectionLimit = 16;

                // 1.7.5（B-05）：便携模式下 DataDirectory 就在程序目录内。程序目录不可写时
                // 这里会抛异常，被 Main 的 catch 笼统报成"启动器初始化失败"。
                // 先判断再创建，给出可照做的具体原因。
                if (!AppPaths.IsDirectoryWritable(AppPaths.BaseDirectory))
                {
                    string hint = AppPaths.IsPortable
                        ? "便携模式（检测到 portable.txt）要求程序目录可写，以便把配置与游戏数据" +
                          "存放在 data\\ 下。\r\n当前程序目录不可写：" + AppPaths.BaseDirectory +
                          "\r\n请把启动器移到可写目录（如用户目录），或删除 portable.txt 以改用 " +
                          "用户数据目录存储。"
                        : "程序目录不可写：" + AppPaths.BaseDirectory +
                          "\r\n启动器仍可运行，但无法在程序旁创建数据；配置与默认游戏目录" +
                          "将使用 " + AppPaths.DataDirectory + "。";
                    Log.Warn(hint);
                    if (AppPaths.IsPortable)
                    {
                        MessageBox.Show(hint, "RLL - Rouzhi Lite Launcher",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                AppPaths.EnsureDirectories();
                // 1.5.0：先于日志初始化读一次配置，让可配置的日志保留策略
                // 在 Initialize 内部的过期清理就生效（而非等到设置保存）。
                LauncherConfig bootConfig = LauncherConfig.Load();
                Log.ConfiguredRetentionDays = bootConfig.LogRetentionDays;
                Log.ConfiguredMaxRotatedFiles = bootConfig.LogMaxRotatedFiles;
                Log.Initialize(AppPaths.LogDirectory);

                // 1.7.5 修复（B-10）：此前检查的是已经回退过的 DefaultGameDirectory，
                // 那个值按定义就是可写的，这段检查几乎永不触发，语义也混乱。
                // 现在改为检查「程序目录」本身是否可写，并把回退原因写进日志。
                string fallbackReason = AppPaths.DefaultGameDirectoryFallbackReason;
                if (fallbackReason.Length > 0)
                {
                    Log.Warn(fallbackReason);
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Log.Error("界面线程未处理异常", e.Exception);
                    MessageBox.Show(
                        "启动器遇到未处理的错误：\r\n" + e.Exception.Message +
                        "\r\n\r\n详细信息已写入日志。",
                        "RLL - Rouzhi Lite Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception exception = e.ExceptionObject as Exception;
                    if (exception != null)
                    {
                        Log.Error("后台线程未处理异常", exception);
                    }
                    else
                    {
                        Log.Error("后台线程未处理异常：" + (e.ExceptionObject ?? "未知"));
                    }
                };

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Log.Error("启动器初始化失败", ex);
                MessageBox.Show(
                    "启动器初始化失败：\r\n" + ex.Message,
                    "RLL - Rouzhi Lite Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
