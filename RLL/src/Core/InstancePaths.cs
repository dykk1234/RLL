using System;
using System.Collections.Generic;
using System.IO;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 版本隔离的目录约定。
    ///
    /// 开启隔离后，每个版本使用 <c>&lt;GameDir&gt;\versions\&lt;版本ID&gt;\instance\</c> 作为
    /// 游戏工作目录；其结构与官方 .minecraft 一致（saves / config / mods / logs ...）。
    /// 共享的 libraries 与 assets 不动，避免重复占用空间。
    ///
    /// 关闭隔离时行为完全回到旧布局，既有数据不受影响（不做任何迁移或删除）。
    /// </summary>
    public static class InstancePaths
    {
        public const string InstanceFolderName = "instance";

        /// <summary>
        /// 取某个版本的实例目录。版本名会先做安全校验：
        /// 实例目录位于版本目录内，因此版本名必须是合法的单段名称。
        /// </summary>
        public static string GetInstanceDirectory(string gameDir, string versionId)
        {
            if (string.IsNullOrEmpty(gameDir))
            {
                throw new ArgumentNullException("gameDir");
            }
            PathUtil.ValidateVersionId(versionId);
            string versionDirectory = VersionResolver.GetVersionDirectory(
                Path.GetFullPath(gameDir), versionId);
            return Path.Combine(versionDirectory, InstanceFolderName);
        }

        /// <summary>
        /// 确保实例目录存在。首次启用某版本的隔离时调用；
        /// 只在缺失时创建子目录，不移动、不删除任何既有文件。
        /// </summary>
        public static string EnsureInstanceDirectory(string gameDir, string versionId)
        {
            string instance = GetInstanceDirectory(gameDir, versionId);
            Directory.CreateDirectory(instance);
            // 游戏首次启动会自己创建这些子目录；这里预先建立常用的几个，
            // 让用户能直观看到"这个版本有独立存档"。
            string[] subdirectories = new string[]
            {
                "saves", "config", "mods", "logs", "resourcepacks", "screenshots"
            };
            for (int i = 0; i < subdirectories.Length; i++)
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(instance, subdirectories[i]));
                }
                catch
                {
                    // 单个子目录创建失败不应阻止启动。
                }
            }
            return instance;
        }

        /// <summary>
        /// 判断某版本是否已有实例目录（用于界面提示"已有独立数据"）。
        /// </summary>
        public static bool Exists(string gameDir, string versionId)
        {
            try
            {
                return Directory.Exists(GetInstanceDirectory(gameDir, versionId));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取游戏实际读取 mods 的目录。
        ///
        /// 这是版本隔离最容易踩的坑：开启隔离后游戏的 --gameDir 指向实例目录，
        /// 因此它只读 instance\mods；若启动器仍把模组装到 &lt;GameDir&gt;\mods，
        /// 文件虽然下载成功，游戏却完全看不到（表现为"模组不生效"）。
        ///
        /// 统一在这里解析，所有涉及 mods 的路径（安装、扫描"可更新"、打开目录）
        /// 都必须走它，避免两处逻辑再次分叉。
        /// </summary>
        public static string GetModsDirectory(string gameDir, string versionId, bool isolateVersions)
        {
            if (string.IsNullOrEmpty(gameDir))
            {
                throw new ArgumentNullException("gameDir");
            }
            if (!isolateVersions || string.IsNullOrEmpty(versionId))
            {
                return Path.Combine(Path.GetFullPath(gameDir), "mods");
            }
            try
            {
                return Path.Combine(GetInstanceDirectory(gameDir, versionId), "mods");
            }
            catch (ArgumentException)
            {
                // 版本名非法时退回共享目录，避免因隔离开关而完全无法操作模组。
                return Path.Combine(Path.GetFullPath(gameDir), "mods");
            }
        }

        /// <summary>
        /// 启用版本隔离时，把共享 mods 目录里的模组补进实例目录。
        ///
        /// 为什么需要这一步：用户通常先在共享 &lt;GameDir&gt;\mods 里装好模组，
        /// 之后才打开版本隔离开关。隔离后游戏只读 instance\mods（新建时为空），
        /// 结果模组全部"消失"，表现为"开了隔离就加载不到 mod"。
        ///
        /// 行为约定（用户数据安全优先）：
        /// - 只【复制】同名文件，从不移动或删除共享目录里的原文件；
        /// - 实例目录已存在同名文件时跳过，不覆盖（避免冲掉用户手动放过的新版本）；
        /// - 单个文件失败只记录日志，不影响其它文件与启动流程。
        ///
        /// 返回实际复制过去的文件个数。
        /// </summary>
        public static int SeedInstanceMods(string gameDir, string versionId)
        {
            if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(versionId))
            {
                return 0;
            }
            string sharedMods;
            string instanceMods;
            try
            {
                sharedMods = Path.Combine(Path.GetFullPath(gameDir), "mods");
                instanceMods = Path.Combine(GetInstanceDirectory(gameDir, versionId), "mods");
            }
            catch (ArgumentException)
            {
                return 0;
            }
            if (!Directory.Exists(sharedMods))
            {
                return 0;
            }
            try
            {
                Directory.CreateDirectory(instanceMods);
            }
            catch
            {
                return 0;
            }

            int copied = 0;
            string[] files;
            try
            {
                files = ListModFiles(sharedMods);
            }
            catch
            {
                return 0;
            }
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                string destination = Path.Combine(instanceMods, name);
                try
                {
                    if (File.Exists(destination))
                    {
                        // 已存在就跳过：可能用户已经手动放入了更新的版本。
                        continue;
                    }
                    File.Copy(files[i], destination, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    Log.Warn("复制模组到实例目录失败：" + name + "，" + ex.Message);
                }
            }
            if (copied > 0)
            {
                Log.Info("已把 " + copied + " 个模组从共享目录复制到实例目录（版本 " + versionId + "）。");
            }
            return copied;
        }

        /// <summary>
        /// 列出目录下的模组文件，**同时包含 .jar 与 .jar.disabled**。
        ///
        /// 为什么必须包含被禁用的：启用状态是模组状态的一部分。同步时若丢掉禁用状态，
        /// 就会出现"用户在实例里禁用了某个模组 → 关闭隔离 → 共享目录里它又变回启用"
        /// 的状态不同步（与 1.4.0 修复的"模组消失"属于同一类问题）。
        ///
        /// 放在 Core 层供所有需要"摸清 mods 目录里到底有什么"的地方复用，
        /// 避免各处各自用 glob 通配符推断而出现口径不一致。
        /// </summary>
        public static string[] ListModFiles(string modsDirectory)
        {
            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                return new string[0];
            }
            List<string> found = new List<string>();
            found.AddRange(Directory.GetFiles(
                modsDirectory, "*.jar", SearchOption.TopDirectoryOnly));
            found.AddRange(Directory.GetFiles(
                modsDirectory, "*.jar" + ModManager.DisabledSuffix, SearchOption.TopDirectoryOnly));
            string[] result = found.ToArray();
            Array.Sort(result, StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// 关闭版本隔离时的反向补救：把实例目录里的模组补回共享 mods 目录。
        ///
        /// 场景：用户在隔离状态下又装了新模组（装在 instance\mods），之后关掉隔离，
        /// 游戏回到读 &lt;GameDir&gt;\mods —— 那些新模组就"消失"了。
        /// 实测确认这是真实缺口：隔离中装的 mod-c 在关闭隔离后游戏完全看不到。
        ///
        /// 约束与正向同步一致：只复制、不移动、已存在不覆盖、单文件失败不影响其它。
        /// 因为是"补回共享目录"，多个版本的实例都可能有模组，这里按版本逐个调用。
        /// 返回实际复制过去的文件个数。
        /// </summary>
        public static int SeedSharedModsFromInstance(string gameDir, string versionId)
        {
            if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(versionId))
            {
                return 0;
            }
            string sharedMods;
            string instanceMods;
            try
            {
                sharedMods = Path.Combine(Path.GetFullPath(gameDir), "mods");
                instanceMods = Path.Combine(GetInstanceDirectory(gameDir, versionId), "mods");
            }
            catch (ArgumentException)
            {
                return 0;
            }
            if (!Directory.Exists(instanceMods))
            {
                return 0;
            }
            try
            {
                Directory.CreateDirectory(sharedMods);
            }
            catch
            {
                return 0;
            }

            int copied = 0;
            string[] files;
            try
            {
                files = ListModFiles(instanceMods);
            }
            catch
            {
                return 0;
            }
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                string destination = Path.Combine(sharedMods, name);
                try
                {
                    if (File.Exists(destination))
                    {
                        continue;
                    }
                    File.Copy(files[i], destination, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    Log.Warn("复制实例模组回共享目录失败：" + name + "，" + ex.Message);
                }
            }
            if (copied > 0)
            {
                Log.Info("关闭隔离时把 " + copied + " 个模组从版本 " + versionId +
                         " 的实例目录复制回共享 mods 目录。");
            }
            return copied;
        }

        /// <summary>
        /// 列出所有已有实例目录的版本名（用于关闭隔离时逐个补回共享 mods）。
        /// 只探测 versions\&lt;版本&gt;\instance 结构，不解析 JSON，代价很小。
        /// </summary>
        public static List<string> ListInstanceVersions(string gameDir)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(gameDir))
            {
                return result;
            }
            string versionsRoot;
            try
            {
                versionsRoot = Path.Combine(Path.GetFullPath(gameDir), "versions");
            }
            catch
            {
                return result;
            }
            if (!Directory.Exists(versionsRoot))
            {
                return result;
            }
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(versionsRoot);
            }
            catch
            {
                return result;
            }
            for (int i = 0; i < directories.Length; i++)
            {
                try
                {
                    string instance = Path.Combine(directories[i], InstanceFolderName);
                    if (Directory.Exists(instance))
                    {
                        result.Add(Path.GetFileName(directories[i]));
                    }
                }
                catch
                {
                }
            }
            return result;
        }
    }
}
