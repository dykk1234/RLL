using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 1.6.0（F-1601）：统一下载管理中心。
    ///
    /// 职责边界（遵循 PRD「在底层下载能力之上加 DownloadManager，不重写传输层」）：
    /// - 传输与哈希校验仍由 HttpUtil.DownloadFileAsync / DownloadFileNoChecksumAsync 承担；
    /// - 本类负责：任务身份、状态机、并发上限、活动任务去重、取消/重试/暂停（可续传）、
    ///   元数据持久化与启动恢复、历史上限、进度节流与事件通知；
    /// - 持久化文件只保存任务元数据，绝不保存账号密码 / 令牌 / 联机密钥。
    ///
    /// 并发与安全：并发上限只影响队列调度，不绕过 HttpUtil 的 HTTPS 白名单与哈希校验；
    /// 任务恢复时重新走 HttpUtil 的出站校验（镜像映射、allowlist、重定向逐跳）。
    /// </summary>
    public sealed class DownloadManager
    {
        public const int DefaultMaxConcurrency = 3;
        public const int DefaultHistoryLimit = 100;

        // 稳定错误码（F-1601 错误模型：Code + 用户消息）。
        public const string ErrorCanceled = "CANCELLED";
        public const string ErrorHashMismatch = "HASH_MISMATCH";
        public const string ErrorStalled = "DOWNLOAD_STALLED";
        public const string ErrorDownload = "DOWNLOAD_ERROR";
        public const string ErrorInvalidRequest = "INVALID_REQUEST";
        /// <summary>无哈希下载的默认大小上限（与 HttpUtil 一致：8 GB）。</summary>
        public const long DefaultNoChecksumMaxBytes = 8L * 1024L * 1024L * 1024L;

        private readonly object _sync = new object();
        private readonly List<DownloadTask> _tasks = new List<DownloadTask>();
        private readonly SemaphoreSlim _slots;
        private readonly string _stateFile;
        private readonly Func<DownloadRequest, Action<long>, CancellationToken, Task> _executor;
        private readonly Stopwatch _lastEvent = Stopwatch.StartNew();
        private long _nextId;

        /// <summary>全局并发上限（1–8，来自 LauncherConfig.DownloadMaxConcurrency）。</summary>
        public int MaxConcurrency;
        /// <summary>历史任务保留条数（默认 100）。</summary>
        public int HistoryLimit;

        /// <summary>任务状态变更事件（内部节流：高频进度不逐个发事件）。</summary>
        public event Action<TaskChangedEventArgs> TaskChanged;

        public DownloadManager()
            : this(AppPaths.DownloadTaskStateFile, null)
        {
        }

        /// <summary>注入自定义执行器用于测试；stateFile 为持久化路径。</summary>
        public DownloadManager(string stateFile, Func<DownloadRequest, Action<long>, CancellationToken, Task> executor)
        {
            _stateFile = stateFile ?? "";
            _executor = executor ?? DefaultExecuteAsync;
            MaxConcurrency = DefaultMaxConcurrency;
            HistoryLimit = DefaultHistoryLimit;
            _slots = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
            RestoreFromFile();
        }

        public static string DescribeState(DownloadTaskState state)
        {
            switch (state)
            {
                case DownloadTaskState.Queued:
                    return "排队中";
                case DownloadTaskState.Running:
                    return "下载中";
                case DownloadTaskState.Verifying:
                    return "校验中";
                case DownloadTaskState.Completed:
                    return "已完成";
                case DownloadTaskState.Failed:
                    return "失败";
                case DownloadTaskState.Canceled:
                    return "已取消";
                case DownloadTaskState.WaitingRetry:
                    return "等待重试";
                default:
                    return "未知";
            }
        }

        public List<DownloadTask> GetTasks()
        {
            lock (_sync)
            {
                List<DownloadTask> copy = new List<DownloadTask>();
                for (int i = 0; i < _tasks.Count; i++)
                {
                    copy.Add(CloneTask(_tasks[i]));
                }
                return copy;
            }
        }

        /// <summary>
        /// 提交一个下载请求，返回 TaskId。
        /// 去重：同一 URL + 期望哈希 + 目标路径的**活动**任务合并，不产生重复写入；
        /// 不同目标路径（不同实例）不合并。
        /// </summary>
        public string Enqueue(DownloadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            if (string.IsNullOrEmpty(request.Url) || string.IsNullOrEmpty(request.TargetPath))
            {
                throw new InvalidDataException("下载请求缺少 URL 或目标路径。");
            }
            bool needHash = string.Equals(request.HashAlgorithm, "sha1", StringComparison.OrdinalIgnoreCase);
            if (needHash && !PathUtil.IsSha1(request.ExpectedHash))
            {
                throw new InvalidDataException("下载请求缺少有效的 SHA1（" + request.TaskName + "）。");
            }

            string dedup = request.GetDedupKey();
            string taskId = "";
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    DownloadTask existing = _tasks[i];
                    // 1.7.6（B-20）：汇总任务的 URL/哈希/目标路径都是空的，去重键退化成
                    // "||"，会与其它汇总任务互相误判为重复。因此直接跳过它们。
                    if (existing.IsJob)
                    {
                        continue;
                    }
                    if (!existing.IsTerminal &&
                        string.Equals(existing.GetDedupKeyIfAny(), dedup, StringComparison.Ordinal))
                    {
                        return existing.TaskId;
                    }
                }
                DownloadTask task = new DownloadTask();
                task.TaskId = NewTaskId();
                task.TaskName = string.IsNullOrEmpty(request.TaskName) ? request.Url : request.TaskName;
                task.Url = request.Url;
                task.TargetPath = request.TargetPath;
                task.HashAlgorithm = request.HashAlgorithm;
                task.ExpectedHash = request.ExpectedHash;
                task.ExpectedSize = request.ExpectedSize;
                task.Source = request.Source;
                task.Priority = request.Priority;
                task.AllowResume = request.AllowResume;
                task.State = DownloadTaskState.Queued;
                task.CreatedAt = DateTime.Now;
                _tasks.Add(task);
                TrimHistoryLocked();
                PersistLocked();
                taskId = task.TaskId;
            }
            Pump();
            return taskId;
        }

        public DownloadTask GetTask(string taskId)
        {
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (string.Equals(_tasks[i].TaskId, taskId, StringComparison.Ordinal))
                    {
                        return CloneTask(_tasks[i]);
                    }
                }
            }
            return null;
        }

        // ---------- 1.7.6（B-20）：汇总型任务 ----------

        /// <summary>
        /// 登记一个"汇总任务"（一次版本安装 / 一个模组安装 = 任务页一行）。
        ///
        /// 与 Enqueue 的区别：
        /// - 不占用并发槽（真正的并发由发起方自己的下载循环控制）；
        /// - 不持久化（安装是运行期过程，重启后没有"半次安装"可恢复）；
        /// - 状态直接进 Running，由发起方回填百分比与最终结果。
        ///
        /// 这样既能让任务页与状态栏进度行看到游戏 / 模组下载，又不会让上千个
        /// 小文件挤满任务列表。
        /// </summary>
        public DownloadTask BeginJob(string taskName, string source)
        {
            DownloadTask task = new DownloadTask();
            task.IsJob = true;
            task.TaskName = string.IsNullOrEmpty(taskName) ? "下载任务" : taskName;
            task.Source = source ?? "";
            task.HashAlgorithm = "";
            task.State = DownloadTaskState.Running;
            task.Percent = 0;
            lock (_sync)
            {
                task.TaskId = NewTaskId();
                task.CreatedAt = DateTime.Now;
                task.StartedAt = task.CreatedAt;
                _tasks.Add(task);
                TrimHistoryLocked();
            }
            RaiseLocked(task, DownloadTaskState.Queued, DownloadTaskState.Running);
            return CloneTask(task);
        }

        /// <summary>回填汇总任务的整体进度（0–100）。</summary>
        public void UpdateJob(string taskId, int percent)
        {
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (string.Equals(_tasks[i].TaskId, taskId, StringComparison.Ordinal))
                    {
                        if (!_tasks[i].IsJob || _tasks[i].IsTerminal)
                        {
                            return;
                        }
                        int value = percent;
                        if (value < 0)
                        {
                            value = 0;
                        }
                        if (value > 100)
                        {
                            value = 100;
                        }
                        _tasks[i].Percent = value;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 注册汇总任务的取消回调（发起方用它中止自己的下载循环）。
        /// 任务中心的「取消」按钮点击后由 CancelInternal 调用它。
        /// </summary>
        public void SetJobCancelCallback(string taskId, Action callback)
        {
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (string.Equals(_tasks[i].TaskId, taskId, StringComparison.Ordinal))
                    {
                        _tasks[i].CancelCallback = callback;
                        return;
                    }
                }
            }
        }

        /// <summary>结束汇总任务：state 应为 Completed / Failed / Canceled。</summary>
        public void EndJob(string taskId, DownloadTaskState state, string errorCode, string errorMessage)
        {
            DownloadTask found = null;
            DownloadTaskState old;
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (string.Equals(_tasks[i].TaskId, taskId, StringComparison.Ordinal))
                    {
                        found = _tasks[i];
                        break;
                    }
                }
                if (found == null || !found.IsJob || found.IsTerminal)
                {
                    return;
                }
                old = found.State;
                found.State = state;
                found.CompletedAt = DateTime.Now;
                found.SpeedBytesPerSec = 0;
                found.EtaSeconds = 0;
                if (state == DownloadTaskState.Completed)
                {
                    found.Percent = 100;
                    found.BytesReceived = 0;
                    found.TotalBytes = 0;
                }
                if (!string.IsNullOrEmpty(errorCode))
                {
                    found.ErrorCode = errorCode;
                }
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    found.ErrorMessage = errorMessage;
                }
            }
            RaiseLocked(found, old, state);
        }

        /// <summary>取消任务。运行中的任务通过令牌中止；已完成/已取消不重复操作。</summary>
        public void Cancel(string taskId)
        {
            CancelInternal(taskId, false);
        }

        /// <summary>
        /// 暂停：仅对支持续传的任务有效。中止当前传输但保留分片，
        /// 状态置为 WaitingRetry；重试时由 HttpUtil 的 Range 续传。
        /// 不支持续传的任务返回 false（UI 据此禁用暂停按钮）。
        /// </summary>
        public bool Pause(string taskId)
        {
            return CancelInternal(taskId, true);
        }

        /// <summary>重试：把失败 / 已取消 / 等待重试的任务重新入队。</summary>
        public void Retry(string taskId)
        {
            DownloadRequest request = null;
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    DownloadTask task = _tasks[i];
                    if (string.Equals(task.TaskId, taskId, StringComparison.Ordinal))
                    {
                        if (task.State == DownloadTaskState.Queued ||
                            task.State == DownloadTaskState.Running ||
                            task.State == DownloadTaskState.Verifying)
                        {
                            return;
                        }
                        // 1.7.6（B-20）：汇总任务没有可续传的文件请求，重试无从恢复；
                        // 直接忽略（用户在对应页面重新发起即可），避免把它塞回队列后
                        // 被 Pump 当成普通下载用空 URL 执行。
                        if (task.IsJob)
                        {
                            return;
                        }
                        request = task.ToRequest();
                        task.BytesReceived = 0;
                        task.TotalBytes = 0;
                        task.SpeedBytesPerSec = 0;
                        task.EtaSeconds = 0;
                        task.ErrorCode = "";
                        task.ErrorMessage = "";
                        task.UserCancelRequested = false;
                        task.UserPauseRequested = false;
                        task.State = DownloadTaskState.Queued;
                        task.CreatedAt = DateTime.Now;
                        PersistLocked();
                        RaiseLocked(task, DownloadTaskState.WaitingRetry, DownloadTaskState.Queued);
                        break;
                    }
                }
            }
            if (request != null)
            {
                Pump();
            }
        }

        /// <summary>清除已完成/失败/已取消的历史任务（保留活动任务）。</summary>
        public void ClearHistory()
        {
            lock (_sync)
            {
                for (int i = _tasks.Count - 1; i >= 0; i--)
                {
                    if (_tasks[i].IsTerminal)
                    {
                        _tasks.RemoveAt(i);
                    }
                }
                PersistLocked();
            }
        }

        /// <summary>把在途任务元数据写盘（退出时调用；内部已按状态变更节流）。</summary>
        public void SaveState()
        {
            lock (_sync)
            {
                PersistLocked();
            }
        }

        /// <summary>
        /// 1.6.0（F-1603）：以"入队并等待终态"的方式执行下载，供事务性流程（模组更新）使用。
        /// 任务仍在任务中心可见、可取消；返回终态任务快照，调用方据此判断是否继续事务。
        /// </summary>
        public Task<DownloadTask> RunTrackedAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(delegate
            {
                string taskId = Enqueue(request);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DownloadTask task = GetTask(taskId);
                    if (task == null)
                    {
                        throw new InvalidOperationException("下载任务不存在：" + taskId);
                    }
                    if (task.IsTerminal)
                    {
                        return task;
                    }
                    Thread.Sleep(100);
                }
            }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // ---------- 内部 ----------

        private static async Task DefaultExecuteAsync(
            DownloadRequest request,
            Action<long> onBytes,
            CancellationToken cancellationToken)
        {
            bool needHash = string.Equals(request.HashAlgorithm, "sha1", StringComparison.OrdinalIgnoreCase);
            if (needHash)
            {
                await HttpUtil.DownloadFileAsync(
                    request.Url,
                    request.TargetPath,
                    request.ExpectedHash,
                    request.ExpectedSize,
                    onBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await HttpUtil.DownloadFileNoChecksumAsync(
                    request.Url,
                    request.TargetPath,
                    request.ExpectedSize > 0 ? request.ExpectedSize : DefaultNoChecksumMaxBytes,
                    onBytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private bool CancelInternal(string taskId, bool pause)
        {
            CancellationTokenSource sourceToCancel = null;
            Action jobCancel = null;
            DownloadTask jobCancelDirect = null;
            DownloadTask found = null;
            lock (_sync)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    DownloadTask task = _tasks[i];
                    if (string.Equals(task.TaskId, taskId, StringComparison.Ordinal))
                    {
                        found = task;
                        if (task.State == DownloadTaskState.Queued)
                        {
                            task.State = pause ? DownloadTaskState.WaitingRetry : DownloadTaskState.Canceled;
                            task.CompletedAt = DateTime.Now;
                            PersistLocked();
                            RaiseLocked(task, DownloadTaskState.Queued, task.State);
                            return true;
                        }
                        if (task.State == DownloadTaskState.Running)
                        {
                            if (task.IsJob)
                            {
                                // 1.7.6（B-20）：汇总任务没有传输令牌，取消要靠发起方注册的
                                // 回调（它会中止自己的下载循环并把任务收尾成 Canceled）。
                                // 汇总任务整体不可续传，因此"暂停"直接拒绝（返回 false，
                                // UI 据此把暂停按钮置灰），只有取消是有意义的。
                                if (pause)
                                {
                                    return false;
                                }
                                task.UserCancelRequested = true;
                                jobCancel = task.CancelCallback;
                                if (jobCancel == null)
                                {
                                    // 没有回调（例如发起方已提前退出）：直接收尾，
                                    // 否则任务会永远停在 Running 状态。
                                    found = null;
                                    jobCancelDirect = task;
                                }
                                break; // 退出锁后再触发回调，避免在锁内跑外部代码
                            }
                            if (pause && !task.AllowResume)
                            {
                                return false;
                            }
                            if (pause)
                            {
                                task.UserPauseRequested = true;
                            }
                            else
                            {
                                task.UserCancelRequested = true;
                            }
                            sourceToCancel = TakeTokenLocked(task.TaskId);
                            break; // 退出锁后执行取消；状态由异步线程收尾
                        }
                        if (task.State == DownloadTaskState.WaitingRetry)
                        {
                            task.State = pause ? DownloadTaskState.WaitingRetry : DownloadTaskState.Canceled;
                            task.CompletedAt = DateTime.Now;
                            PersistLocked();
                            RaiseLocked(task, DownloadTaskState.WaitingRetry, task.State);
                            return true;
                        }
                        return false;
                    }
                }
            }
            if (sourceToCancel != null)
            {
                try
                {
                    sourceToCancel.Cancel();
                }
                catch
                {
                }
            }
            if (jobCancel != null)
            {
                try
                {
                    jobCancel();
                }
                catch (Exception ex)
                {
                    Log.Warn("取消汇总任务失败：" + ex.Message);
                }
            }
            if (jobCancelDirect != null)
            {
                // 没有注册回调的汇总任务：直接收尾，避免永远停在 Running。
                EndJob(jobCancelDirect.TaskId, DownloadTaskState.Canceled,
                    ErrorCanceled, "下载已由用户取消。");
            }
            return found != null || jobCancel != null || jobCancelDirect != null;
        }

        private void Pump()
        {
            lock (_sync)
            {
                // 从队列里按优先级挑选可启动的任务（未达到并发上限的）。
                for (int pass = 0; pass < 2; pass++)
                {
                    int desiredPriority = pass == 0 ? 1 : 0;
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        DownloadTask task = _tasks[i];
                        // 1.7.6（B-20）：汇总任务由发起方驱动，绝不能进入 Pump ——
                        // 否则会被当成普通下载按空 URL 执行，还会白占一个并发槽。
                        if (task.IsJob)
                        {
                            continue;
                        }
                        if (task.State != DownloadTaskState.Queued)
                        {
                            continue;
                        }
                        if (task.Priority != desiredPriority)
                        {
                            continue;
                        }
                        if (!_slots.Wait(0))
                        {
                            return; // 并发已满；等已完成任务的信号再继续
                        }
                        // 先挂取消令牌再置 Running：消除"Running 但令牌未挂"的取消空窗。
                        CancellationTokenSource startedCts = new CancellationTokenSource();
                        _tasks[i].ActiveToken = startedCts;
                        _tasks[i].State = DownloadTaskState.Running;
                        _tasks[i].StartedAt = DateTime.Now;
                        RaiseLocked(task, DownloadTaskState.Queued, DownloadTaskState.Running);
                        ThreadPool.QueueUserWorkItem(RunTaskWorker, task);
                    }
                }
            }
        }

        private void RunTaskWorker(object state)
        {
            DownloadTask task = (DownloadTask)state;
            CancellationTokenSource cts = null;
            try
            {
                lock (_sync)
                {
                    // 若任务在排队到启动之间被取消/暂停，直接收尾。
                    if (task.State == DownloadTaskState.Canceled ||
                        task.State == DownloadTaskState.WaitingRetry)
                    {
                        return;
                    }
                    cts = task.ActiveToken;
                    if (cts == null)
                    {
                        cts = new CancellationTokenSource();
                    }
                    // 启动兜底：若启动前已被标记取消/暂停，先让令牌生效，
                    // 由统一的取消收尾路径处理状态，避免漏取消。
                    if (task.UserCancelRequested || task.UserPauseRequested)
                    {
                        try
                        {
                            cts.Cancel();
                        }
                        catch
                        {
                        }
                    }
                }

                DateTime started = DateTime.Now;
                long previousBytes = 0;
                Stopwatch speedWatch = Stopwatch.StartNew();
                DownloadRequest request = task.ToRequest();
                Task run = _executor(request, delegate(long cumulative)
                {
                    long nowTicks = speedWatch.ElapsedTicks;
                    double seconds = nowTicks / (double)Stopwatch.Frequency;
                    lock (_sync)
                    {
                        task.BytesReceived = cumulative;
                        task.TotalBytes = Math.Max(task.TotalBytes, cumulative);
                        if (seconds > 0.5)
                        {
                            task.SpeedBytesPerSec = (cumulative - previousBytes) / seconds;
                            previousBytes = cumulative;
                            if (task.SpeedBytesPerSec > 0 && task.TotalBytes > 0)
                            {
                                long remaining = task.TotalBytes - cumulative;
                                task.EtaSeconds = (int)Math.Ceiling(remaining / task.SpeedBytesPerSec);
                            }
                            speedWatch.Restart();
                        }
                    }
                }, cts.Token);
                run.GetAwaiter().GetResult();

                // 传输完成后进入校验阶段（HttpUtil 内部已校验；这里只改状态标记）。
                SetState(task, DownloadTaskState.Verifying);

                SetState(task, DownloadTaskState.Completed);
                lock (_sync)
                {
                    task.CompletedAt = DateTime.Now;
                    task.SpeedBytesPerSec = 0;
                    task.EtaSeconds = 0;
                }
            }
            catch (OperationCanceledException)
            {
                bool userPause = task.UserPauseRequested;
                bool userCancel = task.UserCancelRequested;
                if (userCancel)
                {
                    SetState(task, DownloadTaskState.Canceled);
                    SetError(task, ErrorCanceled, "下载已由用户取消。");
                }
                else if (userPause)
                {
                    SetState(task, DownloadTaskState.WaitingRetry);
                    SetError(task, ErrorCanceled, "下载已暂停；支持续传的分片已保留。");
                }
                else
                {
                    SetState(task, DownloadTaskState.WaitingRetry);
                    SetError(task, ErrorCanceled, "下载中断；支持续传的分片已保留，可重试续传。");
                }
            }
            catch (Exception ex)
            {
                SetState(task, DownloadTaskState.Failed);
                SetError(task, ClassifyError(ex), ex.Message);
            }
            finally
            {
                lock (_sync)
                {
                    if (cts != null)
                    {
                        try
                        {
                            cts.Dispose();
                        }
                        catch
                        {
                        }
                    }
                    // 释放并发槽，让排队中的任务继续。
                    try
                    {
                        _slots.Release();
                    }
                    catch
                    {
                    }
                    PersistLocked();
                }
                Pump();
            }
        }

        private CancellationTokenSource TakeTokenLocked(string taskId)
        {
            // 令牌自 Pump 起由管理器持有，直到任务结束（finally 释放），
            // 因此 Running 期间取消/暂停总能拿到令牌，不存在空窗。
            for (int i = 0; i < _tasks.Count; i++)
            {
                DownloadTask task = _tasks[i];
                if (string.Equals(task.TaskId, taskId, StringComparison.Ordinal))
                {
                    return task.ActiveToken;
                }
            }
            return null;
        }

        private void SetState(DownloadTask task, DownloadTaskState newState)
        {
            DownloadTaskState oldState;
            lock (_sync)
            {
                oldState = task.State;
                if (oldState == newState)
                {
                    return;
                }
                task.State = newState;
            }
            RaiseLocked(task, oldState, newState);
        }

        private void SetError(DownloadTask task, string code, string message)
        {
            lock (_sync)
            {
                task.ErrorCode = code;
                task.ErrorMessage = message ?? "";
                task.CompletedAt = DateTime.Now;
            }
        }

        private static string ClassifyError(Exception ex)
        {
            string text = (ex == null) ? "" : (ex.Message ?? "");
            if (text.IndexOf("SHA1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("哈希", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("校验", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ErrorHashMismatch;
            }
            if (text.IndexOf("停滞", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("stall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ErrorStalled;
            }
            return ErrorDownload;
        }

        private void RaiseLocked(DownloadTask task, DownloadTaskState oldState, DownloadTaskState newState)
        {
            Action<TaskChangedEventArgs> handler = TaskChanged;
            if (handler == null)
            {
                return;
            }
            // 事件节流：运行中任务的进度不逐个触发，只报状态迁移。
            if (oldState == newState)
            {
                return;
            }
            try
            {
                TaskChangedEventArgs args = new TaskChangedEventArgs();
                args.Task = CloneTask(task);
                args.OldState = oldState;
                args.NewState = newState;
                handler(args);
            }
            catch
            {
            }
        }

        private void TrimHistoryLocked()
        {
            if (HistoryLimit <= 0)
            {
                HistoryLimit = DefaultHistoryLimit;
            }
            int terminal = 0;
            for (int i = 0; i < _tasks.Count; i++)
            {
                if (_tasks[i].IsTerminal)
                {
                    terminal++;
                }
            }
            if (terminal <= HistoryLimit)
            {
                return;
            }
            int toRemove = terminal - HistoryLimit;
            for (int i = 0; i < _tasks.Count && toRemove > 0; i++)
            {
                if (_tasks[i].IsTerminal)
                {
                    _tasks.RemoveAt(i);
                    i--;
                    toRemove--;
                }
            }
        }

        // ---------- 持久化 ----------

        private void PersistLocked()
        {
            if (string.IsNullOrEmpty(_stateFile))
            {
                return;
            }
            try
            {
                List<object> list = new List<object>();
                for (int i = 0; i < _tasks.Count; i++)
                {
                    DownloadTask t = _tasks[i];
                    // 1.7.6（B-20）：汇总任务是运行期视图，不写盘 —— 它没有可续传的
                    // 文件请求，写盘后重启会被恢复成永久的"等待重试"空记录。
                    if (t.IsJob)
                    {
                        continue;
                    }
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["TaskId"] = t.TaskId;
                    item["TaskName"] = t.TaskName;
                    item["Url"] = t.Url;
                    item["TargetPath"] = t.TargetPath;
                    item["HashAlgorithm"] = t.HashAlgorithm;
                    item["ExpectedHash"] = t.ExpectedHash;
                    item["ExpectedSize"] = t.ExpectedSize;
                    item["Source"] = t.Source;
                    item["Priority"] = t.Priority;
                    item["AllowResume"] = t.AllowResume;
                    item["State"] = (int)t.State;
                    item["BytesReceived"] = t.BytesReceived;
                    item["TotalBytes"] = t.TotalBytes;
                    item["ErrorCode"] = t.ErrorCode;
                    item["ErrorMessage"] = t.ErrorMessage;
                    item["CreatedAt"] = t.CreatedAt.ToString("o");
                    list.Add(item);
                }
                string json = JsonUtil.Serialize(list);
                string directory = Path.GetDirectoryName(_stateFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                PathUtil.WriteAllTextAtomic(_stateFile, json);
            }
            catch
            {
                // 持久化失败不阻塞下载流程（元数据丢失可接受，目标文件仍在）。
            }
        }

        private void RestoreFromFile()
        {
            if (string.IsNullOrEmpty(_stateFile) || !File.Exists(_stateFile))
            {
                return;
            }
            try
            {
                object parsed = JsonUtil.Parse(File.ReadAllText(_stateFile));
                IList items = parsed as IList;
                if (items == null)
                {
                    return;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    IDictionary<string, object> item = JsonUtil.AsObject(items[i]);
                    if (item == null)
                    {
                        continue;
                    }
                    DownloadTask t = new DownloadTask();
                    t.TaskId = JsonUtil.GetString(item, "TaskId");
                    if (string.IsNullOrEmpty(t.TaskId))
                    {
                        continue;
                    }
                    t.TaskName = JsonUtil.GetString(item, "TaskName");
                    t.Url = JsonUtil.GetString(item, "Url");
                    t.TargetPath = JsonUtil.GetString(item, "TargetPath");
                    t.HashAlgorithm = JsonUtil.GetString(item, "HashAlgorithm");
                    t.ExpectedHash = JsonUtil.GetString(item, "ExpectedHash");
                    t.ExpectedSize = JsonUtil.GetLong(item, "ExpectedSize", 0);
                    t.Source = JsonUtil.GetString(item, "Source");
                    t.Priority = JsonUtil.GetInt(item, "Priority", 0);
                    t.AllowResume = JsonUtil.GetBool(item, "AllowResume", true);
                    int state = JsonUtil.GetInt(item, "State", (int)DownloadTaskState.WaitingRetry);
                    // 启动恢复：未完成的任务一律回到"等待重试"，由用户确认后再继续；
                    // 重新走 HttpUtil 出站校验（白名单/镜像/重定向），不允许静默恢复跳过校验。
                    if (state != (int)DownloadTaskState.Completed &&
                        state != (int)DownloadTaskState.Failed &&
                        state != (int)DownloadTaskState.Canceled)
                    {
                        t.State = DownloadTaskState.WaitingRetry;
                    }
                    else
                    {
                        t.State = (DownloadTaskState)state;
                    }
                    t.UserCancelRequested = false;
                    t.UserPauseRequested = false;
                    t.BytesReceived = JsonUtil.GetLong(item, "BytesReceived", 0);
                    t.TotalBytes = JsonUtil.GetLong(item, "TotalBytes", 0);
                    t.ErrorCode = JsonUtil.GetString(item, "ErrorCode");
                    t.ErrorMessage = JsonUtil.GetString(item, "ErrorMessage");
                    string created = JsonUtil.GetString(item, "CreatedAt");
                    DateTime parsedDate;
                    if (DateTime.TryParse(created, out parsedDate))
                    {
                        t.CreatedAt = parsedDate;
                    }
                    else
                    {
                        t.CreatedAt = DateTime.Now;
                    }
                    _tasks.Add(t);
                }
            }
            catch
            {
                _tasks.Clear();
            }
        }

        private string NewTaskId()
        {
            _nextId++;
            return "dl-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + _nextId;
        }

        private static DownloadTask CloneTask(DownloadTask source)
        {
            DownloadTask copy = new DownloadTask();
            copy.TaskId = source.TaskId;
            copy.TaskName = source.TaskName;
            copy.Url = source.Url;
            copy.TargetPath = source.TargetPath;
            copy.HashAlgorithm = source.HashAlgorithm;
            copy.ExpectedHash = source.ExpectedHash;
            copy.ExpectedSize = source.ExpectedSize;
            copy.Source = source.Source;
            copy.Priority = source.Priority;
            copy.AllowResume = source.AllowResume;
            // 1.7.6（B-20）：汇总任务字段必须一起快照。否则任务页拿到的副本没有
            // IsJob / Percent，会按"普通文件任务"渲染成永远 0% 的进度。
            copy.IsJob = source.IsJob;
            copy.Percent = source.Percent;
            copy.State = source.State;
            copy.BytesReceived = source.BytesReceived;
            copy.TotalBytes = source.TotalBytes;
            copy.SpeedBytesPerSec = source.SpeedBytesPerSec;
            copy.EtaSeconds = source.EtaSeconds;
            copy.ErrorCode = source.ErrorCode;
            copy.ErrorMessage = source.ErrorMessage;
            copy.CreatedAt = source.CreatedAt;
            copy.StartedAt = source.StartedAt;
            copy.CompletedAt = source.CompletedAt;
            return copy;
        }
    }
}
