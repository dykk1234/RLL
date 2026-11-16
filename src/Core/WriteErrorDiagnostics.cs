using System;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 1.7.2：把底层异常翻译成用户能照做的提示。
    ///
    /// 起因：启动器默认为游戏目录选择"程序所在目录下的 .minecraft"。如果用户把
    /// 启动器放在 Program Files 之类受保护的位置，普通权限根本无法写入，安装/启动
    /// 只能拿到 "Access to the path ... is denied."，界面直接弹这句 Windows 原文，
    /// 用户并不知道"无权限"从哪来、下一步该做什么。
    ///
    /// 这里保持纯函数（不读全局状态、不碰控件），既便于离线回归测试，也让
    /// MainForm 只负责取当前游戏目录后转调。
    /// </summary>
    public static class WriteErrorDiagnostics
    {
        /// <summary>沿 InnerException 链找到最内层的真实原因（最多 4 层，防环）。</summary>
        public static Exception GetRootCause(Exception ex)
        {
            Exception root = ex;
            int guard = 0;
            while (root != null && root.InnerException != null && guard++ < 4)
            {
                root = root.InnerException;
            }
            return root;
        }

        /// <summary>
        /// 生成面向用户的错误说明。gameDir 仅用于展示当前写入位置，
        /// 便于用户直接看出是哪条路径没权限。
        /// </summary>
        public static string Describe(Exception ex, string action, string gameDir)
        {
            if (ex == null)
            {
                return (action ?? "操作") + "失败。";
            }
            Exception root = GetRootCause(ex);
            if (root == null)
            {
                return (action ?? "操作") + "失败。";
            }

            if (root is UnauthorizedAccessException)
            {
                return (action ?? "操作") + "失败：没有写入权限。\r\n\r\n" +
                    "当前游戏目录：" + (gameDir ?? "") + "\r\n" +
                    "该位置普通权限无法写入（常见于程序放在 Program Files 或系统盘受保护目录）。\r\n\r\n" +
                    "解决办法（任选其一）：\r\n" +
                    "1. 到「设置」页把「游戏目录」改到 D:\\ 等有写权限的位置；\r\n" +
                    "2. 把整个启动器文件夹移到非系统盘（如 D:\\RLL\\）；\r\n" +
                    "3. 关闭启动器，右键选择「以管理员身份运行」后重试。";
            }

            if (root is DirectoryNotFoundException)
            {
                return (action ?? "操作") + "失败：目标目录不存在，可能被移动或删除了。\r\n" +
                    "请到「设置」页重新选择「游戏目录」。";
            }

            IOException io = root as IOException;
            if (io != null)
            {
                int hr = System.Runtime.InteropServices.Marshal.GetHRForException(io) & 0xFFFF;
                if (io.Message.IndexOf("空间", StringComparison.Ordinal) >= 0 || hr == 0x70)
                {
                    return (action ?? "操作") + "失败：磁盘空间不足。请清理空间后重试。";
                }
            }

            return (action ?? "操作") + "失败：\r\n" + root.Message + "\r\n\r\n详细信息已写入日志。";
        }

        /// <summary>该异常是否属于"写入权限不足"这一类（供界面决定是否额外提示）。</summary>
        public static bool IsWritePermissionError(Exception ex)
        {
            return ex != null && GetRootCause(ex) is UnauthorizedAccessException;
        }

        /// <summary>
        /// 1.7.5（B-04）：按根因文本判断是否为写入权限问题。
        /// TrySave() 返回的是根因消息文本而非异常对象，且 .NET 的
        /// UnauthorizedAccessException 文案随系统语言变化，故用关键词兜底匹配。
        /// </summary>
        public static bool IsWritePermissionError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            return message.IndexOf("拒绝", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("权限", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("只读", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("UnauthorizedAccessException",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
