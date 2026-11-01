using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>一条崩溃分析结论。</summary>
    public sealed class CrashFinding
    {
        /// <summary>简短标题，如"缺少模组前置"。</summary>
        public string Title;
        /// <summary>从日志中摘出的证据行。</summary>
        public string Evidence;
        /// <summary>可操作的修复建议。</summary>
        public string Advice;
        /// <summary>严重度：Error / Warning / Info。</summary>
        public string Severity;
    }

    public sealed class CrashReport
    {
        public List<CrashFinding> Findings = new List<CrashFinding>();
        public string SourceFile;
        public bool HasCrashReport;
        public bool HasGameLog;
    }

    /// <summary>
    /// 崩溃日志分析：读取 crash-reports 与 latest.log，按已知模式归纳原因并给出建议。
    /// 纯文本匹配，不执行任何内容；只在本地读取游戏目录下的日志文件。
    /// </summary>
    public static class CrashAnalyzer
    {
        private const int MaxLines = 4000;
        private const long MaxFileBytes = 8L * 1024L * 1024L;

        private sealed class Pattern
        {
            public string Title;
            public string Advice;
            public string Severity;
            public string[] Needles;
            public string[] Stop;

            public Pattern(string title, string advice, string severity,
                string[] needles, string[] stop)
            {
                Title = title;
                Advice = advice;
                Severity = severity;
                Needles = needles;
                Stop = stop;
            }
        }

        /// <summary>
        /// 已知的崩溃/错误模式。每个模式给出"症状 → 建议"，尽量让用户不必读完整堆栈。
        /// </summary>
        private static readonly Pattern[] Patterns = new Pattern[]
        {
            new Pattern(
                "缺少模组前置",
                "有模组声明了依赖但未满足。请到「模组」页安装对应前置（常见为 Fabric API），或移除提示中的模组。",
                "Error",
                new string[] { "Missing or unsupported mandatory dependencies",
                               "requires any version of", "requires version",
                               "Incompatible mods found", "Mod resolution failed" },
                new string[0]),

            new Pattern(
                "模组版本与游戏版本不匹配",
                "模组声明的 Minecraft 版本与实际不一致。请安装与该版本匹配的模组文件，避免手动混放。",
                "Error",
                new string[] { "mods.toml", "requires minecraft", "Unsupported class version",
                               "NoClassDefFoundError" },
                new string[0]),

            new Pattern(
                "Java 版本过低",
                "游戏要求更高版本的 Java。请在「启动」页选择更高版本的 Java（1.20.1 需要 17，较新版本通常需要 21 或更高）。",
                "Error",
                new string[] { "UnsupportedClassVersionError", "requires Java",
                               "java.lang.ClassFormatError" },
                new string[0]),

            new Pattern(
                "显存或内存不足",
                "分配的内存不足或显卡驱动异常。可在「启动」页调高最大内存，或降低渲染距离/关闭光影。",
                "Error",
                new string[] { "OutOfMemoryError", "GL_OUT_OF_MEMORY", "EXCEPTION_ACCESS_VIOLATION",
                               "OpenGL error" },
                new string[0]),

            new Pattern(
                "本地库缺失或加载失败",
                "游戏文件不完整。请在「下载」页重新安装该版本（安装器会补齐缺失文件）。",
                "Error",
                new string[] { "Failed to locate library", "UnsatisfiedLinkError",
                               "No such file or directory", "Could not find or load main class" },
                new string[0]),

            new Pattern(
                "显卡驱动问题",
                "显卡驱动过旧或不兼容。请更新显卡驱动；笔记本电脑请确认游戏使用独立显卡运行。",
                "Error",
                new string[] { "GLFW error", "Failed to create window", "Pixel format not accelerated",
                               "Your video card does not support" },
                new string[0]),

            new Pattern(
                "登录或会话失效",
                "账号会话不可用。常见两种情况：\r\n" +
                "① 离线账号加入了开启正版验证的房间——这是最可能的原因。\r\n" +
                "   单人存档开房间（「对局域网开放」）时：原版没有正版验证开关，" +
                "装了「更高级联机设置」（mcwifipnp）才有，把「正版验证」选「禁用」" +
                "（或「禁用 + 修复UUID」）。\r\n" +
                "   独立服务端开服时：把 server.properties 的 online-mode 改成 false 并重启。\r\n" +
                "② 认证账号的会话令牌过期——到「账号」页点「校验令牌」，失效则重新登录。\r\n" +
                "（异地联机组网成功只代表网络通了，不代表能通过服务器的正版验证。）",
                "Warning",
                new string[] { "Failed to verify authentication", "Invalid session",
                               "AuthenticationException", "Invalid token",
                               "MinecraftClientException" },
                new string[0]),

            new Pattern(
                "模组之间冲突",
                "两个模组修改了同一处内容。请移除其中一个，或换用兼容版本。",
                "Error",
                new string[] { "Mixin apply failed", "mixin", "conflicting", "Duplicate mod" },
                new string[0]),

            new Pattern(
                "资源包或数据包损坏",
                "资源文件解析失败。请删掉最近添加的资源包，或重新安装版本以恢复资源文件。",
                "Warning",
                new string[] { "Resource pack", "Failed to load", "pack.mcmeta" },
                new string[0]),

            new Pattern(
                "命令行过长",
                "启动参数超过 Windows 限制。本启动器对 Java 9+ 会自动改用 @argfile；若仍失败请减少模组数量。",
                "Error",
                new string[] { "The command line is too long", "Command line too long",
                               "输入行太长" },
                new string[0])
        };

        /// <summary>
        /// 分析指定游戏目录。优先读最新 crash-report，没有则退回 latest.log。
        /// 目录不存在或没有日志时返回空结论（Findings 为空，标志位为 false）。
        /// </summary>
        public static CrashReport Analyze(string gameDir)
        {
            CrashReport report = new CrashReport();
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                return report;
            }

            string text = "";
            string crashFile = FindLatestFile(Path.Combine(gameDir, "crash-reports"), "*.txt");
            if (crashFile != null)
            {
                report.SourceFile = crashFile;
                report.HasCrashReport = true;
                text = ReadCapped(crashFile);
            }
            if (string.IsNullOrEmpty(text))
            {
                string logFile = Path.Combine(gameDir, "logs", "latest.log");
                if (File.Exists(logFile))
                {
                    report.SourceFile = logFile;
                    report.HasGameLog = true;
                    text = ReadCapped(logFile);
                }
            }
            if (string.IsNullOrEmpty(text))
            {
                return report;
            }

            for (int i = 0; i < Patterns.Length; i++)
            {
                Pattern pattern = Patterns[i];
                string evidence = FindEvidence(text, pattern.Needles);
                if (evidence == null)
                {
                    continue;
                }
                // 命中"排除项"时跳过，避免把正常信息误报成故障。
                if (pattern.Stop.Length > 0 && FindEvidence(text, pattern.Stop) != null)
                {
                    continue;
                }
                CrashFinding finding = new CrashFinding();
                finding.Title = pattern.Title;
                finding.Advice = pattern.Advice;
                finding.Severity = pattern.Severity;
                finding.Evidence = evidence;
                report.Findings.Add(finding);
            }
            return report;
        }

        /// <summary>
        /// 把结论渲染成可直接显示的多行文本。
        /// </summary>
        public static string Format(CrashReport report)
        {
            if (report == null)
            {
                return "";
            }
            if (report.Findings.Count == 0)
            {
                return report.SourceFile == null
                    ? "没有找到崩溃报告或游戏日志。启动一次游戏后再回来查看。"
                    : "未在日志中发现已知的错误模式。日志来源：" + report.SourceFile;
            }
            StringBuilder builder = new StringBuilder();
            builder.Append("日志来源：").Append(report.SourceFile).Append("\r\n");
            builder.Append("发现 ").Append(report.Findings.Count).Append(" 个可定位的问题：\r\n");
            for (int i = 0; i < report.Findings.Count; i++)
            {
                CrashFinding finding = report.Findings[i];
                builder.Append("\r\n[").Append(i + 1).Append("] ").Append(finding.Title)
                       .Append("（").Append(finding.Severity).Append("）\r\n");
                builder.Append("    证据：").Append(finding.Evidence).Append("\r\n");
                builder.Append("    建议：").Append(finding.Advice).Append("\r\n");
            }
            return builder.ToString();
        }

        private static string FindLatestFile(string directory, string filter)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }
            try
            {
                string[] files = Directory.GetFiles(directory, filter, SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                {
                    return null;
                }
                string latest = files[0];
                DateTime latestTime = File.GetLastWriteTimeUtc(latest);
                for (int i = 1; i < files.Length; i++)
                {
                    DateTime time = File.GetLastWriteTimeUtc(files[i]);
                    if (time > latestTime)
                    {
                        latestTime = time;
                        latest = files[i];
                    }
                }
                return latest;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadCapped(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes)
                {
                    return "";
                }
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, false))
                {
                    StringBuilder builder = new StringBuilder();
                    int lines = 0;
                    while (!reader.EndOfStream && lines < MaxLines)
                    {
                        builder.AppendLine(reader.ReadLine());
                        lines++;
                    }
                    return builder.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private static string FindEvidence(string text, string[] needles)
        {
            if (string.IsNullOrEmpty(text) || needles == null)
            {
                return null;
            }
            string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                for (int j = 0; j < needles.Length; j++)
                {
                    if (lines[i].IndexOf(needles[j], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string evidence = lines[i].Trim();
                        if (evidence.Length > 160)
                        {
                            evidence = evidence.Substring(0, 160) + "…";
                        }
                        return evidence;
                    }
                }
            }
            return null;
        }
    }
}
