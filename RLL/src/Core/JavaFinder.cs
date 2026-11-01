using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    public sealed class JavaInfo
    {
        public string LaunchPath;
        public string ProbePath;
        public int Major;
        public string VersionText;
        public string Display;
        /// <summary>
        /// 候选来源（JAVA_HOME / PATH / 安装目录 / Minecraft 自带运行时）。
        /// PATH 中的目录可能被普通用户写入，显示来源才能让用户判断是否可信。
        /// </summary>
        public string Source;
        /// <summary>
        /// 来源目录是否可能被当前用户写入。为 true 时界面会给出提示。
        /// </summary>
        public bool FromUntrustedLocation;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Display) ? (LaunchPath ?? "") : Display;
        }
    }

    public static class JavaFinder
    {
        public static List<JavaInfo> FindAll()
        {
            List<JavaInfo> result = new List<JavaInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 顺序体现可信度优先级：JAVA_HOME 与固定安装目录需要管理员权限才能写入，
            // PATH 中的目录普通用户即可写入，因此放到最后，并在结果里标注来源。
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                AddCandidate(result, seen, Path.Combine(javaHome, "bin", "javaw.exe"), "JAVA_HOME");
                AddCandidate(result, seen, Path.Combine(javaHome, "bin", "java.exe"), "JAVA_HOME");
            }

            List<string> roots = GetJavaRoots();
            for (int i = 0; i < roots.Count; i++)
            {
                AddFromRoot(result, seen, roots[i]);
            }

            string commonJavapath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Common Files", "Oracle", "Java", "javapath", "javaw.exe");
            AddCandidate(result, seen, commonJavapath, "安装目录");
            string commonJavapathX86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Common Files", "Oracle", "Java", "javapath", "javaw.exe");
            AddCandidate(result, seen, commonJavapathX86, "安装目录");

            string minecraftRuntime = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft", "runtime");
            AddFromRuntime(result, seen, minecraftRuntime);

            // PATH 放最后：这里的目录可能被当前用户写入，属于"可信度较低"的来源。
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                string[] parts = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string directory = parts[i].Trim().Trim('"');
                    if (directory.Length == 0)
                    {
                        continue;
                    }
                    AddCandidate(result, seen, Path.Combine(directory, "javaw.exe"), "PATH");
                    AddCandidate(result, seen, Path.Combine(directory, "java.exe"), "PATH");
                }
            }

            // 来源可信的排前面，其次才看 Java 主版本。
            result.Sort(delegate(JavaInfo left, JavaInfo right)
            {
                if (left.FromUntrustedLocation != right.FromUntrustedLocation)
                {
                    return left.FromUntrustedLocation ? 1 : -1;
                }
                int major = right.Major.CompareTo(left.Major);
                if (major != 0)
                {
                    return major;
                }
                return string.Compare(left.LaunchPath, right.LaunchPath, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public static JavaInfo Probe(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string launchPath;
            try
            {
                launchPath = Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }
            if (!File.Exists(launchPath))
            {
                return null;
            }

            string probePath = launchPath;
            if (string.Equals(Path.GetFileName(launchPath), "javaw.exe", StringComparison.OrdinalIgnoreCase))
            {
                string javaExe = Path.Combine(Path.GetDirectoryName(launchPath), "java.exe");
                if (File.Exists(javaExe))
                {
                    probePath = javaExe;
                }
                else
                {
                    JavaInfo unknown = new JavaInfo();
                    unknown.LaunchPath = launchPath;
                    unknown.ProbePath = launchPath;
                    unknown.Major = 0;
                    unknown.VersionText = "未知（缺少 java.exe）";
                    unknown.Display = "Java 未知 - " + launchPath;
                    return unknown;
                }
            }

            string output = "";
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = probePath;
                startInfo.Arguments = "-version";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.WorkingDirectory = Path.GetDirectoryName(probePath);

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        return null;
                    }
                    try
                    {
                        Task.WaitAll(new Task[] { errorTask, outputTask }, 2000);
                    }
                    catch
                    {
                    }
                    if (errorTask.Status == TaskStatus.RanToCompletion)
                    {
                        output += errorTask.Result;
                    }
                    if (outputTask.Status == TaskStatus.RanToCompletion)
                    {
                        output += "\r\n" + outputTask.Result;
                    }
                }
            }
            catch
            {
                return null;
            }

            string versionText = "";
            Match match = Regex.Match(
                output,
                "version\\s+\"([^\"]+)\"",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
            if (match.Success)
            {
                versionText = match.Groups[1].Value.Trim();
            }
            int major = ParseMajor(versionText);
            JavaInfo info = new JavaInfo();
            info.LaunchPath = launchPath;
            info.ProbePath = probePath;
            info.Major = major;
            info.VersionText = string.IsNullOrEmpty(versionText) ? "未知" : versionText;
            info.Display = (major > 0 ? "Java " + major : "Java 未知") +
                           " - " + launchPath;
            return info;
        }

        public static JavaInfo ChooseBest(List<JavaInfo> candidates, int requiredMajor)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            JavaInfo bestCompatible = null;
            JavaInfo highest = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                JavaInfo java = candidates[i];
                if (java == null || java.Major <= 0)
                {
                    continue;
                }
                if (highest == null || java.Major > highest.Major)
                {
                    highest = java;
                }
                if (requiredMajor > 0 && java.Major >= requiredMajor)
                {
                    if (bestCompatible == null ||
                        java.Major < bestCompatible.Major ||
                        (java.Major == bestCompatible.Major &&
                         string.Compare(java.VersionText, bestCompatible.VersionText,
                             StringComparison.OrdinalIgnoreCase) > 0))
                    {
                        bestCompatible = java;
                    }
                }
            }
            if (bestCompatible != null)
            {
                return bestCompatible;
            }
            return highest;
        }

        public static int ParseMajor(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return 0;
            }
            string text = version.Trim();
            if (text.StartsWith("1.", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }
            int end = 0;
            while (end < text.Length && text[end] >= '0' && text[end] <= '9')
            {
                end++;
            }
            if (end == 0)
            {
                return 0;
            }
            int major;
            if (int.TryParse(text.Substring(0, end), out major))
            {
                return major;
            }
            return 0;
        }

        private static List<string> GetJavaRoots()
        {
            List<string> roots = new List<string>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localPrograms = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs");
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AddRoot(roots, Path.Combine(programFiles, "Java"));
            AddRoot(roots, Path.Combine(programFiles, "Eclipse Adoptium"));
            AddRoot(roots, Path.Combine(programFiles, "Eclipse Foundation"));
            AddRoot(roots, Path.Combine(programFiles, "Microsoft"));
            AddRoot(roots, Path.Combine(programFiles, "BellSoft"));
            AddRoot(roots, Path.Combine(programFiles, "Zulu"));
            AddRoot(roots, Path.Combine(programFiles, "Amazon Corretto"));
            AddRoot(roots, Path.Combine(programFiles, "AdoptOpenJDK"));
            AddRoot(roots, Path.Combine(programFiles, "Semeru"));
            AddRoot(roots, Path.Combine(programFiles, "RedHat"));
            AddRoot(roots, Path.Combine(programFiles, "IBM"));
            AddRoot(roots, Path.Combine(programFilesX86, "Java"));
            AddRoot(roots, Path.Combine(programFilesX86, "Eclipse Adoptium"));
            AddRoot(roots, Path.Combine(localPrograms, "Eclipse Adoptium"));
            AddRoot(roots, Path.Combine(localPrograms, "Microsoft"));
            AddRoot(roots, Path.Combine(localPrograms, "Java"));
            AddRoot(roots, Path.Combine(userProfile, ".jdks"));
            return roots;
        }

        private static void AddRoot(List<string> roots, string root)
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                roots.Add(root);
            }
        }

        private static void AddFromRoot(
            List<JavaInfo> result,
            HashSet<string> seen,
            string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }
            AddCandidate(result, seen, Path.Combine(root, "bin", "javaw.exe"));
            AddCandidate(result, seen, Path.Combine(root, "bin", "java.exe"));
            string[] children;
            try
            {
                children = Directory.GetDirectories(root);
            }
            catch
            {
                return;
            }
            for (int i = 0; i < children.Length; i++)
            {
                AddCandidate(result, seen, Path.Combine(children[i], "bin", "javaw.exe"));
                AddCandidate(result, seen, Path.Combine(children[i], "bin", "java.exe"));
            }
        }

        private static void AddFromRuntime(
            List<JavaInfo> result,
            HashSet<string> seen,
            string runtimeRoot)
        {
            if (!Directory.Exists(runtimeRoot))
            {
                return;
            }
            string[] level1;
            try
            {
                level1 = Directory.GetDirectories(runtimeRoot);
            }
            catch
            {
                return;
            }
            for (int i = 0; i < level1.Length; i++)
            {
                AddFromRoot(result, seen, level1[i]);
                string[] level2;
                try
                {
                    level2 = Directory.GetDirectories(level1[i]);
                }
                catch
                {
                    continue;
                }
                for (int j = 0; j < level2.Length; j++)
                {
                    AddFromRoot(result, seen, level2[j]);
                }
            }
        }

        private static void AddCandidate(
            List<JavaInfo> result,
            HashSet<string> seen,
            string path)
        {
            AddCandidate(result, seen, path, "安装目录");
        }

        private static void AddCandidate(
            List<JavaInfo> result,
            HashSet<string> seen,
            string path,
            string source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!seen.Add(full))
            {
                return;
            }
            JavaInfo info = Probe(full);
            if (info != null)
            {
                info.Source = source;
                info.FromUntrustedLocation = IsUserWritableLocation(full);
                // 让下拉框直接显示来源，用户能看出这个 java.exe 从哪来。
                info.Display = (info.Display ?? full) + "  [" + source + "]";
                result.Add(info);
            }
        }

        /// <summary>
        /// 判断路径是否位于普通用户可写的位置（用户目录、临时目录）。
        /// 这类位置被发现并执行的 java.exe 可能是被投放的伪程序，
        /// 界面会据此给出提示，而不是静默使用。
        /// </summary>
        public static bool IsUserWritableLocation(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string full;
            string root;
            try
            {
                full = Path.GetFullPath(path);
                root = Path.GetFullPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile));
            }
            catch
            {
                return false;
            }
            if (!string.IsNullOrEmpty(root) && full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string temp = "";
            try
            {
                temp = Path.GetFullPath(Path.GetTempPath());
            }
            catch
            {
            }
            return !string.IsNullOrEmpty(temp) &&
                   full.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
