using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>
    /// Windows 命令行拼接/解析。启动器从不经过 cmd.exe，所有参数都由这里转义后
    /// 直接传给 CreateProcess。
    /// </summary>
    public static class CommandLine
    {
        /// <summary>
        /// 命令行长度安全阈值。Windows CreateProcess 上限约 32767 字符，
        /// 留出余量（Java 可执行文件路径、工作目录等也会占用额度）。
        /// 超过阈值且 Java 主版本 &gt;= 9 时改用 @argfile（Java 9+ 支持）。
        /// </summary>
        public const int MaxCommandLineLength = 30000;

        /// <summary>
        /// 硬上限：任何情况下都不允许超过（含 Java 8，它不支持 @argfile）。
        /// </summary>
        public const int HardCommandLineLimit = 32700;
        public static string QuoteArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }
            if (argument.Length == 0)
            {
                return "\"\"";
            }
            bool needQuote = false;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (char.IsWhiteSpace(c) || c == '"')
                {
                    needQuote = true;
                    break;
                }
            }
            if (!needQuote)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder(argument.Length + 8);
            builder.Append('"');
            int backslashes = 0;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                }
                else
                {
                    if (backslashes > 0)
                    {
                        builder.Append('\\', backslashes);
                        backslashes = 0;
                    }
                    builder.Append(c);
                }
            }
            if (backslashes > 0)
            {
                builder.Append('\\', backslashes * 2);
            }
            builder.Append('"');
            return builder.ToString();
        }

        public static string BuildArguments(IEnumerable<string> arguments)
        {
            if (arguments == null)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(QuoteArgument(argument));
            }
            return builder.ToString();
        }

        /// <summary>
        /// 按 CommandLineToArgvW 的常见规则拆分参数，用于解析用户填写的 JVM 参数和
        /// 旧版 minecraftArguments。不会调用任何 shell。
        /// </summary>
        public static List<string> SplitArguments(string commandLine)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(commandLine))
            {
                return result;
            }

            int i = 0;
            int length = commandLine.Length;
            while (i < length)
            {
                while (i < length && char.IsWhiteSpace(commandLine[i]))
                {
                    i++;
                }
                if (i >= length)
                {
                    break;
                }

                StringBuilder argument = new StringBuilder();
                bool inQuotes = false;
                while (i < length)
                {
                    char c = commandLine[i];
                    if (!inQuotes && char.IsWhiteSpace(c))
                    {
                        break;
                    }

                    if (c == '\\')
                    {
                        int start = i;
                        while (i < length && commandLine[i] == '\\')
                        {
                            i++;
                        }
                        int count = i - start;
                        if (i < length && commandLine[i] == '"')
                        {
                            argument.Append('\\', count / 2);
                            if ((count % 2) == 1)
                            {
                                argument.Append('"');
                                i++;
                            }
                            else
                            {
                                inQuotes = !inQuotes;
                                i++;
                            }
                        }
                        else
                        {
                            argument.Append('\\', count);
                        }
                    }
                    else if (c == '"')
                    {
                        inQuotes = !inQuotes;
                        i++;
                    }
                    else
                    {
                        argument.Append(c);
                        i++;
                    }
                }
                result.Add(argument.ToString());
            }
            return result;
        }

        public static bool IsValidUsername(string username)
        {
            if (string.IsNullOrEmpty(username) || username.Length < 3 || username.Length > 16)
            {
                return false;
            }
            for (int i = 0; i < username.Length; i++)
            {
                char c = username[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '_';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        public static string ComputeOfflineUuid(string username)
        {
            if (username == null)
            {
                throw new ArgumentNullException("username");
            }
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
                // RFC 4122 version 3 (MD5 name-based) 与 IETF variant。
                hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                string hex = PathUtil.ToHex(hash);
                return hex.Substring(0, 8) + "-" +
                       hex.Substring(8, 4) + "-" +
                       hex.Substring(12, 4) + "-" +
                       hex.Substring(16, 4) + "-" +
                       hex.Substring(20, 12);
            }
        }

        public static string ComputeOfflineUuidCompact(string username)
        {
            return ComputeOfflineUuid(username).Replace("-", "");
        }

        // ============================ Java @argfile ============================

        /// <summary>
        /// 是否需要使用 @argfile：命令行过长且 Java 主版本 >= 9（Java 8 及更早
        /// 不支持 @argfile，只能走常规命令行并承担长度风险）。
        /// </summary>
        public static bool ShouldUseArgFile(int commandLineLength, int javaMajor)
        {
            return commandLineLength > MaxCommandLineLength && javaMajor >= 9;
        }

        /// <summary>
        /// 按 Java @file 语法编码单个参数。Java 的 @file 解析与 shell 类似：
        /// 空白分隔参数、双引号分组、反斜杠转义下一字符。因此这里把
        /// 反斜杠与双引号全部转义，含空白时再加引号包裹。
        /// </summary>
        public static string EncodeArgFileLine(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }
            if (argument.Length == 0)
            {
                return "\"\"";
            }
            bool needQuote = false;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (char.IsWhiteSpace(c) || c == '"' || c == '\\')
                {
                    needQuote = true;
                    break;
                }
            }
            StringBuilder builder = new StringBuilder(argument.Length + 4);
            if (needQuote)
            {
                builder.Append('"');
            }
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == '\\' || c == '"')
                {
                    builder.Append('\\');
                }
                builder.Append(c);
            }
            if (needQuote)
            {
                builder.Append('"');
            }
            return builder.ToString();
        }

        /// <summary>
        /// 把参数列表写入 @argfile 文件（每个参数一行），返回文件完整路径。
        /// 文件名使用随机 GUID，避免与并发启动冲突。
        /// </summary>
        public static string WriteArgFile(
            IEnumerable<string> arguments,
            string directory,
            out string content)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException("arguments");
            }
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentNullException("directory");
            }
            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\r\n");
                }
                builder.Append(EncodeArgFileLine(argument ?? ""));
            }
            content = builder.ToString();
            string path = Path.Combine(
                directory,
                "rll-args-" + Guid.NewGuid().ToString("N") + ".txt");
            // Java 9–17 读取 @argfile 时使用平台默认编码（而非 UTF-8），
            // JDK 18+ 才默认 UTF-8。若按 UTF-8 写入，含中文的游戏路径/用户名
            // 会在旧版 Java 上变成乱码，进而找不到版本文件或登录名错误。
            // 因此这里按系统 ANSI 代码页写入，与 Java 的读取方式保持一致。
            PathUtil.WriteAllBytesAtomic(path, GetArgFileEncoding().GetBytes(content));
            return path;
        }

        /// <summary>
        /// @argfile 应使用的编码：取系统 ANSI 代码页（Windows 中文环境为 GBK）。
        /// JDK 18 起默认为 UTF-8，但无法在写文件时得知目标 Java 版本，
        /// 因此统一按平台编码写入——这也是 JDK 18 仍兼容的路径（它会回退到平台编码读取无 BOM 文件）。
        /// </summary>
        internal static Encoding GetArgFileEncoding()
        {
            try
            {
                return Encoding.Default;
            }
            catch
            {
                return new UTF8Encoding(false);
            }
        }

        /// <summary>
        /// 测试用：按 Java @file 语法解码一行，用于 EncodeArgFileLine 的往返校验。
        /// </summary>
        public static string DecodeArgFileLine(string line)
        {
            if (line == null)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder(line.Length);
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    builder.Append(line[i + 1]);
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }
    }
}
