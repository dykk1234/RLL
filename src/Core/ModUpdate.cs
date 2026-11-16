using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>模组更新计划中的一个候选（一个已索引 ModId 对应一个目标版本）。</summary>
    public sealed class ModUpdateCandidate
    {
        public ModIndexEntry Entry;
        /// <summary>Modrinth 目标版本；null 表示无兼容新版本。</summary>
        public ModVersion Target;
        public ModVersionFile TargetFile;
        public bool HasUpdate;
        /// <summary>明确兼容且无冲突 → 可安全自动更新（其余需用户确认）。</summary>
        public bool AutoSafe;
        public List<string> Blockers = new List<string>();
        /// <summary>跳过原因（ModId 在 Modrinth 无项目/无兼容版本等）。</summary>
        public string SkipReason;

        public bool Skipped
        {
            get { return !string.IsNullOrEmpty(SkipReason); }
        }
    }

    /// <summary>一次「检查更新」的计划。</summary>
    public sealed class ModUpdatePlan
    {
        public List<ModUpdateCandidate> Candidates = new List<ModUpdateCandidate>();

        public int AutoSafeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Candidates.Count; i++)
                {
                    if (Candidates[i].AutoSafe)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public int UpdateCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Candidates.Count; i++)
                {
                    if (Candidates[i].HasUpdate)
                    {
                        count++;
                    }
                }
                return count;
            }
        }
    }

    /// <summary>单个模组更新的失败信息。</summary>
    public sealed class ModUpdateFailure
    {
        public string ModId;
        public string Reason;

        public ModUpdateFailure(string modId, string reason)
        {
            ModId = modId ?? "";
            Reason = reason ?? "";
        }
    }

    /// <summary>批量更新的结果（成功/跳过/失败分组）。</summary>
    public sealed class ModUpdateResult
    {
        public List<string> Updated = new List<string>();
        public List<string> AlreadyLatest = new List<string>();
        public List<ModUpdateFailure> Failed = new List<ModUpdateFailure>();
        public List<string> Skipped = new List<string>();

        public string BuildReport()
        {
            return "更新完成：成功 " + Updated.Count + " 个" +
                (AlreadyLatest.Count > 0 ? "，已最新 " + AlreadyLatest.Count + " 个" : "") +
                (Skipped.Count > 0 ? "，跳过 " + Skipped.Count + " 个" : "") +
                (Failed.Count > 0 ? "，失败 " + Failed.Count + " 个" : "") + "。";
        }
    }

    /// <summary>一个回滚备份条目。</summary>
    public sealed class ModBackupEntry
    {
        public string InstanceId;
        public string ModId;
        /// <summary>备份标识（目录内文件名主部）。</summary>
        public string BackupId;
        public string Version;
        public string FilePath;
        /// <summary>备份文件 SHA-1（恢复前必须核验）。</summary>
        public string Sha1;
        public DateTime TimeUtc;
    }

    /// <summary>
    /// 1.6.0（F-1603）：模组批量更新与回滚。
    ///
    /// 事务规则（PRD 4.3.1）：
    /// - 以单模组为事务边界：下载临时文件 → 校验（由 DownloadManager/HttpUtil 完成）→
    ///   备份旧文件 → 原子替换 → 失效索引 → 写入备份历史；任何步骤失败都恢复旧文件；
    /// - 批量更新不做跨模组全局回滚，但逐项报告、每项保持原子性；
    /// - 默认只自动更新"明确兼容且无冲突"的条目（AutoSafe），存在不确定项要求用户确认；
    /// - 回滚前必须校验备份文件哈希与备份时记录一致。
    ///
    /// 检查口径（F-1602/F-1603 联动）：
    /// - 按 ModId 精确匹配 Modrinth 项目；ModIndex 提供统一的本实例已装口径；
    /// - 版本比较为字符串相等（Modrinth 版本号并非统一语义化）；版本号为空视为"有更新"。
    /// </summary>
    public static class ModUpdateService
    {
        public const int MaxBackupCountDefault = 3;

        /// <summary>
        /// 检查更新：为索引中可识别的模组逐个查询 Modrinth。
        /// 只查 fabric 加载器版本（与 ModrinthService.GetVersionsAsync 同口径）。
        /// </summary>
        public static async Task<ModUpdatePlan> CheckUpdatesAsync(
            ModIndexSnapshot snapshot,
            string gameVersion,
            CancellationToken cancellationToken)
        {
            ModUpdatePlan plan = new ModUpdatePlan();
            if (snapshot == null)
            {
                return plan;
            }
            ModrinthService service = new ModrinthService();
            for (int i = 0; i < snapshot.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModIndexEntry entry = snapshot.Entries[i];
                ModUpdateCandidate candidate = new ModUpdateCandidate();
                candidate.Entry = entry;
                if (!entry.IsIdentifiable)
                {
                    candidate.SkipReason = "元数据缺失/损坏，无法识别 ModId";
                    plan.Candidates.Add(candidate);
                    continue;
                }
                List<ModVersion> versions = null;
                try
                {
                    versions = await service.GetVersionsAsync(
                        entry.ModId,
                        gameVersion,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    candidate.SkipReason = "查询 Modrinth 失败：" + ex.Message;
                    plan.Candidates.Add(candidate);
                    continue;
                }
                if (versions == null || versions.Count == 0)
                {
                    candidate.SkipReason = "Modrinth 无该项目或无兼容版本";
                    plan.Candidates.Add(candidate);
                    continue;
                }
                ModVersion target = ModrinthService.SelectBestVersion(versions, gameVersion);
                if (target == null)
                {
                    candidate.SkipReason = "无兼容 Minecraft " + gameVersion + " 的 Fabric 版本";
                    plan.Candidates.Add(candidate);
                    continue;
                }
                ModVersionFile file = ModrinthService.SelectPrimaryFile(target);
                if (file == null)
                {
                    candidate.SkipReason = "目标版本缺少可安装的 JAR 文件";
                    plan.Candidates.Add(candidate);
                    continue;
                }
                candidate.Target = target;
                candidate.TargetFile = file;
                candidate.HasUpdate = string.IsNullOrEmpty(entry.Version) ||
                    !string.Equals(entry.Version, target.VersionNumber, StringComparison.Ordinal);
                if (!candidate.HasUpdate)
                {
                    plan.Candidates.Add(candidate);
                    continue;
                }
                CheckAutoSafe(snapshot, entry, target, candidate.Blockers);
                candidate.AutoSafe = candidate.Blockers.Count == 0;
                plan.Candidates.Add(candidate);
            }
            return plan;
        }

        /// <summary>
        /// 执行更新（单模组事务）。只更新 selected 中的 ModId。
        /// </summary>
        public static async Task<ModUpdateResult> ApplyAsync(
            string instanceId,
            string modsDirectory,
            ModUpdatePlan plan,
            List<string> selectedModIds,
            DownloadManager downloadManager,
            int backupCount,
            CancellationToken cancellationToken)
        {
            ModUpdateResult result = new ModUpdateResult();
            if (plan == null || plan.Candidates.Count == 0)
            {
                return result;
            }
            if (backupCount <= 0)
            {
                backupCount = MaxBackupCountDefault;
            }
            string fullModsDir = Path.GetFullPath(modsDirectory);
            string tempRoot = AppPaths.DownloadTempDirectory;

            for (int i = 0; i < plan.Candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModUpdateCandidate candidate = plan.Candidates[i];
                if (candidate.Skipped)
                {
                    result.Skipped.Add(candidate.Entry.ModId);
                    continue;
                }
                if (!candidate.HasUpdate)
                {
                    result.AlreadyLatest.Add(candidate.Entry.ModId);
                    continue;
                }
                if (!IsSelected(selectedModIds, candidate.Entry.ModId))
                {
                    continue;
                }
                string modId = candidate.Entry.ModId;
                try
                {
                    await ApplyOneAsync(
                        instanceId,
                        fullModsDir,
                        candidate,
                        downloadManager,
                        tempRoot,
                        backupCount,
                        cancellationToken).ConfigureAwait(false);
                    result.Updated.Add(modId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed.Add(new ModUpdateFailure(modId, ex.Message));
                    Log.Warn("模组更新失败：" + modId + " —— " + ex.Message);
                }
            }
            return result;
        }

        /// <summary>列出某实例某模组的可回滚备份（按时间倒序）。</summary>
        public static List<ModBackupEntry> ListBackups(string instanceId, string modId)
        {
            List<ModBackupEntry> result = new List<ModBackupEntry>();
            string directory = AppPaths.ModUpdateBackupDirectory(instanceId, modId);
            if (!Directory.Exists(directory))
            {
                return result;
            }
            string[] jars = Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly);
            Array.Sort(jars, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < jars.Length; i++)
            {
                string jar = jars[i];
                string sidecar = jar + ".sha1";
                if (!File.Exists(sidecar))
                {
                    continue;
                }
                ModBackupEntry entry = new ModBackupEntry();
                entry.InstanceId = instanceId;
                entry.ModId = modId;
                entry.FilePath = jar;
                entry.BackupId = Path.GetFileNameWithoutExtension(jar);
                try
                {
                    entry.Sha1 = File.ReadAllText(sidecar).Trim();
                }
                catch
                {
                    entry.Sha1 = "";
                }
                try
                {
                    entry.TimeUtc = File.GetLastWriteTimeUtc(jar);
                }
                catch
                {
                    entry.TimeUtc = DateTime.MinValue;
                }
                result.Add(entry);
            }
            result.Sort(delegate(ModBackupEntry a, ModBackupEntry b)
            {
                return b.TimeUtc.CompareTo(a.TimeUtc);
            });
            return result;
        }

        /// <summary>
        /// 回滚到指定备份。恢复前校验备份哈希；恢复当前文件前先把当前版本
        /// 重新入备份（保持回滚链），随后原子替换并失效索引。
        /// </summary>
        public static void Rollback(
            string instanceId,
            string modsDirectory,
            string modId,
            string backupId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string backupDir = AppPaths.ModUpdateBackupDirectory(instanceId, modId);
            string backupJar = Path.Combine(backupDir, backupId + ".jar");
            string backupSidecar = backupJar + ".sha1";
            if (!File.Exists(backupJar) || !File.Exists(backupSidecar))
            {
                throw new FileNotFoundException("备份文件不存在：" + backupId);
            }
            string recorded = File.ReadAllText(backupSidecar).Trim();
            string actual = PathUtil.GetSha1(backupJar);
            if (!string.Equals(recorded, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "备份文件哈希与记录不一致，已拒绝回滚（备份可能损坏）。");
            }

            // 定位当前目标 jar：优先启用的同名 ModId 文件。
            string currentJar = "";
            List<ModIndexEntry> entries = ModIndexService.GetByModId(
                ModIndexService.Scan(instanceId, modsDirectory, false), modId);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Enabled)
                {
                    currentJar = entries[i].FilePath;
                    break;
                }
            }
            if (string.IsNullOrEmpty(currentJar) || !File.Exists(currentJar))
            {
                throw new FileNotFoundException("未找到要回滚的模组文件：" + modId);
            }

            // 1) 把当前版本备份为新条目（保持回滚链）。
            string newBackupId = CreateBackup(instanceId, modId, currentJar, backupCountFor(backupDir));
            // 2) 复制备份到临时区后原子替换（不直接消费备份文件）。
            string tempCopy = Path.Combine(
                AppPaths.DownloadTempDirectory,
                "rollback-" + Guid.NewGuid().ToString("N") + ".jar");
            Directory.CreateDirectory(Path.GetDirectoryName(tempCopy));
            try
            {
                File.Copy(backupJar, tempCopy, true);
                PathUtil.ReplaceFile(tempCopy, currentJar);
            }
            finally
            {
                PathUtil.TryDeleteFile(tempCopy);
            }
            ModIndexService.InvalidateFile(instanceId, currentJar);
            Log.Info("模组回滚完成：" + modId + " → " + newBackupId);
        }

        // ---------- 内部 ----------

        private static bool IsSelected(List<string> selected, string modId)
        {
            if (selected == null)
            {
                return false;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                if (string.Equals(selected[i], modId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CheckAutoSafe(
            ModIndexSnapshot snapshot,
            ModIndexEntry entry,
            ModVersion target,
            List<string> blockers)
        {
            if (target.Loaders != null && target.Loaders.Count > 0)
            {
                bool hasFabric = false;
                for (int i = 0; i < target.Loaders.Count; i++)
                {
                    if (string.Equals(target.Loaders[i], "fabric", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFabric = true;
                        break;
                    }
                }
                if (!hasFabric)
                {
                    blockers.Add("目标版本不支持 Fabric 加载器");
                }
            }
            for (int i = 0; i < entry.Dependencies.Count; i++)
            {
                ModIndexDependency dep = entry.Dependencies[i];
                if (!string.Equals(dep.Kind, "required", StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(dep.Id, "minecraft", StringComparison.Ordinal) ||
                    string.Equals(dep.Id, "fabric", StringComparison.Ordinal) ||
                    string.Equals(dep.Id, "java", StringComparison.Ordinal))
                {
                    continue;
                }
                List<ModIndexEntry> installed = ModIndexService.GetByModId(snapshot, dep.Id);
                bool presentEnabled = false;
                for (int j = 0; j < installed.Count; j++)
                {
                    if (installed[j].Enabled)
                    {
                        presentEnabled = true;
                        break;
                    }
                }
                if (!presentEnabled)
                {
                    blockers.Add("缺少必需前置 " + dep.Id + "（或已被禁用）");
                }
            }
            List<ModIndexEntry> duplicates = ModIndexService.GetByModId(snapshot, entry.ModId);
            if (duplicates.Count > 1)
            {
                blockers.Add("实例存在多个同名 ModId（" + duplicates.Count + " 个），需先清理重复");
            }
            for (int i = 0; i < entry.Dependencies.Count; i++)
            {
                ModIndexDependency dep = entry.Dependencies[i];
                if (!string.Equals(dep.Kind, "conflicts", StringComparison.Ordinal))
                {
                    continue;
                }
                if (ModIndexService.GetByModId(snapshot, dep.Id).Count > 0)
                {
                    blockers.Add("与已安装模组冲突：" + dep.Id);
                }
            }
        }

        private static async Task ApplyOneAsync(
            string instanceId,
            string fullModsDir,
            ModUpdateCandidate candidate,
            DownloadManager downloadManager,
            string tempRoot,
            int backupCount,
            CancellationToken cancellationToken)
        {
            ModIndexEntry entry = candidate.Entry;
            string oldPath = entry.FilePath;
            if (!PathUtil.IsSubPath(fullModsDir, oldPath))
            {
                throw new InvalidDataException("模组文件不在实例 mods 目录内，拒绝更新：" + oldPath);
            }

            DownloadRequest request = new DownloadRequest();
            request.TaskName = "更新模组 " + entry.DisplayName + " → " + candidate.Target.VersionNumber;
            request.Url = candidate.TargetFile.Url;
            request.HashAlgorithm = "sha1";
            request.ExpectedHash = candidate.TargetFile.Sha1;
            request.ExpectedSize = candidate.TargetFile.Size;
            request.Source = "modupdate";
            request.Priority = 1;
            request.AllowResume = true;
            Directory.CreateDirectory(tempRoot);
            request.TargetPath = Path.Combine(
                tempRoot,
                "update-" + Guid.NewGuid().ToString("N") + ".jar");

            DownloadTask task = await downloadManager.RunTrackedAsync(
                request, cancellationToken).ConfigureAwait(false);
            if (task.State != DownloadTaskState.Completed)
            {
                throw new InvalidDataException(
                    "下载失败（" + task.ErrorCode + "）：" + task.ErrorMessage);
            }
            if (!File.Exists(request.TargetPath))
            {
                throw new InvalidDataException("下载完成但目标文件缺失。");
            }

            string backupId = "";
            if (File.Exists(oldPath))
            {
                backupId = CreateBackup(instanceId, entry.ModId, oldPath, backupCount);
            }

            bool replaced = false;
            try
            {
                PathUtil.ReplaceFile(request.TargetPath, oldPath);
                replaced = true;
            }
            finally
            {
                PathUtil.TryDeleteFile(request.TargetPath);
                if (!replaced && !string.IsNullOrEmpty(backupId))
                {
                    // 替换失败时尝试恢复旧文件（备份仍在，可安全回滚）。
                    string backupJar = Path.Combine(
                        AppPaths.ModUpdateBackupDirectory(instanceId, entry.ModId),
                        backupId + ".jar");
                    if (File.Exists(backupJar) && !File.Exists(oldPath))
                    {
                        File.Copy(backupJar, oldPath, true);
                    }
                }
                ModIndexService.InvalidateFile(instanceId, oldPath);
            }
        }

        private static string CreateBackup(string instanceId, string modId, string sourceJar, int backupCount)
        {
            string directory = AppPaths.ModUpdateBackupDirectory(instanceId, modId);
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string backupId = "bk-" + stamp;
            string backupJar = Path.Combine(directory, backupId + ".jar");
            File.Copy(sourceJar, backupJar, false);
            File.WriteAllText(backupJar + ".sha1", PathUtil.GetSha1(sourceJar));
            TrimBackups(directory, backupCount);
            return backupId;
        }

        private static void TrimBackups(string directory, int backupCount)
        {
            string[] jars = Directory.GetFiles(directory, "bk-*.jar", SearchOption.TopDirectoryOnly);
            if (jars.Length <= backupCount)
            {
                return;
            }
            Array.Sort(jars, StringComparer.OrdinalIgnoreCase);
            int remove = jars.Length - backupCount;
            for (int i = 0; i < remove; i++)
            {
                PathUtil.TryDeleteFile(jars[i]);
                PathUtil.TryDeleteFile(jars[i] + ".sha1");
            }
        }

        private static int backupCountFor(string directory)
        {
            return MaxBackupCountDefault;
        }
    }
}
