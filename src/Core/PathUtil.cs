using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LiteLauncher.Core
{
    public static class PathUtil
    {
        private static readonly char[] DirectorySeparators = new char[]
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        };

        public static bool IsValidVersionId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128)
            {
                return false;
            }
            if (id == "." || id == ".." || id.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }
            if (id.EndsWith(".", StringComparison.Ordinal) || id.EndsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '.' || c == '_' || c == '-' || c == '+';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        public static void ValidateVersionId(string id)
        {
            if (!IsValidVersionId(id))
            {
                throw new ArgumentException("版本名称不安全：" + (id ?? "(null)"), "id");
            }
        }

        public static bool IsSha1(string value)
        {
            if (value == null || value.Length != 40)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9') ||
                          (c >= 'a' && c <= 'f') ||
                          (c >= 'A' && c <= 'F');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsSubPath(string root, string candidate)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate))
            {
                return false;
            }
            string fullRoot = Path.GetFullPath(root);
            string fullCandidate = Path.GetFullPath(candidate);
            if (string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }
            return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 远程 JSON 中的相对路径只允许普通文件路径。拒绝盘符、ADS、类路径分隔符
        /// （;）、Windows 非法字符以及以点/空格结尾的段，避免路径混淆。
        /// </summary>
        public static bool IsSafeRelativePath(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return false;
            }
            for (int i = 0; i < relative.Length; i++)
            {
                char c = relative[i];
                if (c < 32 || c == ':' || c == ';' || c == '"' ||
                    c == '<' || c == '>' || c == '|' || c == '*' || c == '?')
                {
                    return false;
                }
            }
            string[] segments = relative.Split(
                new char[] { '/', '\\' },
                StringSplitOptions.None);
            if (segments.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    return false;
                }
                if (segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 将远程/元数据中的相对路径拼到可信根目录下，并阻止路径穿越。
        /// </summary>
        public static string SafeCombine(string root, string relative)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }
            if (relative == null)
            {
                throw new ArgumentNullException("relative");
            }
            if (!IsSafeRelativePath(relative))
            {
                throw new InvalidDataException("路径为空或包含非法字符：" + relative);
            }
            string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
            {
                throw new InvalidDataException("不允许绝对路径：" + relative);
            }
            string fullRoot = Path.GetFullPath(root);
            string combined = Path.GetFullPath(Path.Combine(fullRoot, normalized));
            if (!IsSubPath(fullRoot, combined))
            {
                throw new InvalidDataException("路径越界：" + relative);
            }
            return combined;
        }

        public static string GetSha1(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        /// <summary>1.6.0（F-1603）：把任意字符串清理成安全的单段文件名（用于备份目录等）。</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unknown";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j] || c == '/' || c == '\\' || c == ':')
                    {
                        bad = true;
                        break;
                    }
                }
                builder.Append(bad ? '_' : c);
            }
            string result = builder.ToString().Trim();
            if (result.Length == 0 || result == "." || result == "..")
            {
                return "unknown";
            }
            return result;
        }

        /// <summary>1.6.0（F-1602）：计算文件 SHA-256（ModIndex 用，识别内容变化）。</summary>
        public static string GetSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (System.Security.Cryptography.SHA256 sha256 =
                System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        public static string ToHex(byte[] bytes)
        {
            if (bytes == null)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }

        public static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                ReplaceFile(temp, path);
            }
            finally
            {
                TryDeleteFile(temp);
            }
        }

        public static void WriteAllTextAtomic(string path, string text)
        {
            WriteAllBytesAtomic(path, new UTF8Encoding(false).GetBytes(text ?? ""));
        }

        public static void ReplaceFile(string temp, string target)
        {
            if (File.Exists(target))
            {
                try
                {
                    File.Replace(temp, target, null);
                    return;
                }
                catch (IOException)
                {
                    // 某些文件系统不支持 File.Replace，退回到删除后移动。
                }
                catch (UnauthorizedAccessException)
                {
                    // 交给下面的移动操作给出更明确的错误。
                }
                File.Delete(target);
            }
            File.Move(temp, target);
        }

        /// <summary>
        /// 收紧文件权限，只允许当前用户与 SYSTEM / Administrators 访问。
        /// 用于保存含会话令牌的配置文件。实现上使用 FileSecurity（不写注册表），
        /// 任意步骤失败都只记录警告而不抛出——权限收紧失败不应导致配置无法保存。
        /// </summary>
        public static void RestrictFileToCurrentUser(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                // 用 FileInfo 实例式 ACL API（net48 原生实例方法 / net6 扩展方法均可编译），
                // 不用 File.GetAccessControl(path) 静态重载——后者在 net6 已被移除。
                FileInfo fileInfo = new FileInfo(path);
                FileSecurity security = fileInfo.GetAccessControl();
                ApplyCurrentUserOnly(security);
                fileInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                Log.Warn("收紧配置文件权限失败（不影响保存）：" + ex.Message);
            }
        }

        /// <summary>
        /// 收紧目录权限，只允许当前用户与 SYSTEM / Administrators 访问。
        /// 用于日志目录——日志里可能出现会话令牌与 EasyTier 网络密钥，
        /// 而目录默认继承 %APPDATA% 的宽松 ACL（同机其他账户可读）。
        /// 目录本身收紧后，新建其中的日志文件也会继承这份 ACL。
        /// </summary>
        public static void RestrictDirectoryToCurrentUser(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                // 同上：用 DirectoryInfo 实例式 ACL API，保证 net48 / net6 都可编译。
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                DirectorySecurity security = dirInfo.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                }
                AddWellKnownDirectoryRule(security,
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
                AddWellKnownDirectoryRule(security,
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                dirInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                Log.Warn("收紧目录权限失败（不影响使用）：" + ex.Message);
            }
        }

        /// <summary>
        /// 给文件 ACL 去掉继承、只留当前用户与系统/管理员。
        /// </summary>
        private static void ApplyCurrentUserOnly(FileSecurity security)
        {
            security.SetAccessRuleProtection(true, false);
            AddCurrentUserRule(security);
            AddWellKnownRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            AddWellKnownRule(security,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        }

        private static void AddCurrentUserRule(FileSecurity security)
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
            {
                return;
            }
            FileSystemAccessRule full = new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow);
            security.AddAccessRule(full);
        }

        private static void AddWellKnownDirectoryRule(
            DirectorySecurity security, SecurityIdentifier sid)
        {
            if (sid == null)
            {
                return;
            }
            FileSystemAccessRule rule = new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            security.AddAccessRule(rule);
        }

        private static void AddWellKnownRule(FileSecurity security, SecurityIdentifier sid)
        {
            if (sid == null)
            {
                return;
            }
            FileSystemAccessRule rule = new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow);
            security.AddAccessRule(rule);
        }

        public static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败不应覆盖真正的异常。
            }
        }

        public static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // 清理失败不应覆盖真正的异常。
            }
        }

        public static string NormalizeDirectory(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path))
            {
                return fallback;
            }
            try
            {
                string full = Path.GetFullPath(path.Trim());
                if (full.Length == 0)
                {
                    return fallback;
                }
                return full;
            }
            catch
            {
                return fallback;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "未知";
            }
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }
            if (unit == 0)
            {
                return ((long)value).ToString() + " " + units[unit];
            }
            return value.ToString("0.##") + " " + units[unit];
        }

        /// <summary>
        /// 1.7.3：把剩余秒数格式化为「N 秒 / N 分 N 秒 / N 小时 N 分」。
        /// 下载大文件时需要让用户直接看出还要等多久；放在 Core 便于离线测试。
        /// </summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
            {
                return "—";
            }
            if (seconds < 60)
            {
                return seconds + " 秒";
            }
            int minutes = seconds / 60;
            if (minutes < 60)
            {
                return minutes + " 分 " + (seconds % 60) + " 秒";
            }
            return (minutes / 60) + " 小时 " + (minutes % 60) + " 分";
        }

        /// <summary>
        /// 1.7.3：下载进度百分比文案。总量未知时退回已下载字节数，
        /// 不能显示误导性的 0% 或 100%。
        /// </summary>
        public static string FormatProgress(long received, long total)
        {
            if (total <= 0)
            {
                return received > 0 ? FormatBytes(received) : "—";
            }
            double percent = received * 100.0 / total;
            if (percent < 0)
            {
                percent = 0;
            }
            if (percent > 100.0)
            {
                percent = 100.0;
            }
            return percent.ToString("F1") + "%";
        }

        public static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        public static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            return path.TrimEnd(DirectorySeparators);
        }
    }
}
