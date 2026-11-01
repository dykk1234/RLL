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

                AppPaths.EnsureDirectories();
                Log.Initialize(AppPaths.LogDirectory);

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
