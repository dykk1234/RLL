using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 1.7.0（F-1701）：光影包管理。
    /// 只处理 shaderpacks 目录下的 .zip 包：列出、启用/禁用（.zip.disabled 后缀）、
    /// 删除、本地导入校验；不读取 ZIP 内部内容、不执行任何文件。
    /// 远程下载复用 DownloadManager（HTTPS 白名单 + SHA1 校验），
    /// 推荐清单来自 Modrinth 项目，兼容性以项目页信息为准。
    /// </summary>
    public sealed class ShaderPackEntry
    {
        public string FilePath;
        /// <summary>包名（文件名去掉扩展名）。</summary>
        public string Name;
        /// <summary>是否启用（未禁用 = 文件名不以 .disabled 结尾）。</summary>
        public bool Enabled;
        /// <summary>字节大小。</summary>
        public long SizeBytes;
    }

    /// <summary>内置推荐光影（Modrinth 项目，slug 真实存在；下载前会实时查询版本与哈希）。</summary>
    public sealed class RecommendedShaderPack
    {
        public string Slug;
        public string Name;
        public string Author;
        public string Description;
        /// <summary>是否以 Iris 为主要兼容加载器（其余通常兼容 OptiFine）。</summary>
        public bool IrisBased;
    }

    public static class ShaderPackManager
    {
        public const string DisabledSuffix = ".disabled";
        /// <summary>只接受 zip 包；shaderpacks 目录内其他文件一律忽略。</summary>
        private static readonly string[] ZipExtensions = new string[] { ".zip" };

        public static List<ShaderPackEntry> ListShaderPacks(string shaderpacksDirectory)
        {
            List<ShaderPackEntry> result = new List<ShaderPackEntry>();
            if (string.IsNullOrEmpty(shaderpacksDirectory) || !Directory.Exists(shaderpacksDirectory))
            {
                return result;
            }
            string[] files;
            try
            {
                files = Directory.GetFiles(shaderpacksDirectory);
            }
            catch (Exception ex)
            {
                Log.Warn("读取光影目录失败：" + ex.Message);
                return result;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string fileName = Path.GetFileName(path);
                bool disabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                string baseName = disabled
                    ? fileName.Substring(0, fileName.Length - DisabledSuffix.Length)
                    : fileName;
                if (!ZipExtensions.AnySuffix(baseName))
                {
                    continue;
                }
                ShaderPackEntry entry = new ShaderPackEntry();
                entry.FilePath = path;
                entry.Name = Path.GetFileNameWithoutExtension(baseName);
                entry.Enabled = !disabled;
                try
                {
                    FileInfo info = new FileInfo(path);
                    entry.SizeBytes = info.Exists ? info.Length : 0;
                }
                catch
                {
                    entry.SizeBytes = 0;
                }
                result.Add(entry);
            }
            return result;
        }

        /// <summary>启用/禁用：改后缀 .zip ↔ .zip.disabled。返回操作是否成功。</summary>
        public static bool SetEnabled(string filePath, bool enabled)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }
            bool currentlyDisabled = filePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (enabled == !currentlyDisabled)
            {
                return true;
            }
            string target = enabled
                ? filePath.Substring(0, filePath.Length - DisabledSuffix.Length)
                : filePath + DisabledSuffix;
            try
            {
                File.Move(filePath, target);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("切换光影启用状态失败：" + ex.Message);
                return false;
            }
        }

        public static bool Delete(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }
            try
            {
                PathUtil.TryDeleteFile(filePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除光影失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 本地导入：校验文件名与扩展名（只接受 .zip），拒绝路径穿越与保留设备名。
        /// 目标已存在同名文件时返回 false（避免静默覆盖用户文件）。
        /// </summary>
        public static bool ImportLocal(string sourcePath, string shaderpacksDirectory)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(shaderpacksDirectory))
            {
                return false;
            }
            if (!File.Exists(sourcePath))
            {
                return false;
            }
            string fileName = Path.GetFileName(sourcePath);
            if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string safeName = PathUtil.SanitizeFileName(fileName);
            string target = Path.Combine(shaderpacksDirectory, safeName);
            string fullDir = Path.GetFullPath(shaderpacksDirectory);
            if (!Path.GetFullPath(target).StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (File.Exists(target))
            {
                return false;
            }
            try
            {
                Directory.CreateDirectory(shaderpacksDirectory);
                File.Copy(sourcePath, target);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("导入光影失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>内置推荐清单：Modrinth 项目 slug 真实存在，实际版本/哈希以查询为准。</summary>
        public static List<RecommendedShaderPack> GetRecommended()
        {
            List<RecommendedShaderPack> result = new List<RecommendedShaderPack>();
            Add(result, "complementary-reimagined", "Complementary Reimagined", "EminGT",
                "写实向光影，色彩自然、性能均衡，Iris 主推。", true);
            Add(result, "complementary-unbound", "Complementary Unbound", "EminGT",
                "高度可定制，画质上限更高，适合高性能设备。", true);
            Add(result, "bsl-shaders", "BSL Shaders", "capttus",
                "经典全能光影，场景自定义选项丰富，兼容 OptiFine。", false);
            Add(result, "sildurs-vibrant-shaders", "Sildur's Vibrant Shaders", "Sildur",
                "性能分级齐全（低/中/高/极限），上手门槛低。", false);
            Add(result, "photon-shader", "Photon Shader", "sixthsurge",
                "体积光照表现突出，画风明亮通透。", true);
            Add(result, "makeup-ultra-fast-shaders", "MakeUp Ultra Fast", "Eldeston",
                "超高性能向，中低端设备也能流畅开光影。", true);
            Add(result, "solas-shader", "Solas Shader", "TAA",
                "色彩柔和、侧重氛围与性能平衡。", true);
            Add(result, "bliss-shader", "Bliss Shader", "Alphadin",
                "轻量且画面干净，适合偏好原版色调的玩家。", true);
            return result;
        }

        private static void Add(List<RecommendedShaderPack> list, string slug, string name,
            string author, string description, bool iris)
        {
            RecommendedShaderPack item = new RecommendedShaderPack();
            item.Slug = slug;
            item.Name = name;
            item.Author = author;
            item.Description = description;
            item.IrisBased = iris;
            list.Add(item);
        }
    }

    /// <summary>供 ListShaderPacks 使用的扩展名辅助（避免新增 Linq 依赖）。</summary>
    internal static class ShaderPackManagerExtensions
    {
        public static bool AnySuffix(this string[] suffixes, string value)
        {
            if (value == null)
            {
                return false;
            }
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (value.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
