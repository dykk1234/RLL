using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 1.6.0（F-1601）：统一下载任务的请求体。
    ///
    /// 所有需要"可观察、可取消、可恢复"的下载统一提交为 DownloadRequest，
    /// 由 DownloadManager 编排传输与校验，业务模块不再各自拼装下载循环。
    /// 请求本身不含任何账号密码 / 访问令牌 / 联机密钥（见 F-1604 安全边界）。
    /// </summary>
    public sealed class DownloadRequest
    {
        /// <summary>用户可读的任务名，如 "Mod sodium 1.20.1"。</summary>
        public string TaskName;
        /// <summary>下载地址（必须为 HTTPS 且通过 HttpUtil 白名单）。</summary>
        public string Url;
        /// <summary>最终落盘路径（目标文件）。</summary>
        public string TargetPath;
        /// <summary>哈希算法："sha1"（有校验）或 "none"（无校验，仅限白名单豁免资产）。</summary>
        public string HashAlgorithm;
        /// <summary>期望哈希（sha1 时必填，缺失即拒绝）。</summary>
        public string ExpectedHash;
        /// <summary>期望大小；大于 0 时校验。</summary>
        public long ExpectedSize;
        /// <summary>资源类型/来源标识，如 "modrinth" / "minecraft" / "easytier"。</summary>
        public string Source;
        /// <summary>优先级：0 普通，1 前台（只影响队列顺序，不绕过安全校验）。</summary>
        public int Priority;
        /// <summary>是否允许断点续传（仅带哈希校验的下载允许）。</summary>
        public bool AllowResume;

        public DownloadRequest()
        {
            HashAlgorithm = "sha1";
            Priority = 0;
            AllowResume = true;
            ExpectedSize = 0;
        }

        public string GetDedupKey()
        {
            return (Url ?? "") + "|" + (ExpectedHash ?? "") + "|" + (TargetPath ?? "");
        }
    }

    /// <summary>下载任务状态机：Queued → Running → Verifying → Completed；异常 Failed；用户终止 Canceled；中断可恢复 WaitingRetry。</summary>
    public enum DownloadTaskState
    {
        Queued,
        Running,
        Verifying,
        Completed,
        Failed,
        Canceled,
        WaitingRetry
    }

    /// <summary>一个下载任务的快照（供 UI 展示与持久化）。</summary>
    public sealed class DownloadTask
    {
        public string TaskId;
        public string TaskName;
        public string Url;
        public string TargetPath;
        public string HashAlgorithm;
        public string ExpectedHash;
        public long ExpectedSize;
        public string Source;
        public int Priority;
        public bool AllowResume;

        public DownloadTaskState State;
        public long BytesReceived;
        public long TotalBytes;
        public double SpeedBytesPerSec;
        public int EtaSeconds;
        public string ErrorCode;
        public string ErrorMessage;
        public DateTime CreatedAt;
        public DateTime StartedAt;
        public DateTime CompletedAt;

        public bool IsTerminal
        {
            get
            {
                return State == DownloadTaskState.Completed ||
                       State == DownloadTaskState.Failed ||
                       State == DownloadTaskState.Canceled;
            }
        }

        /// <summary>
        /// 1.7.6（B-20）：汇总型任务标记。
        ///
        /// 背景：游戏安装与模组下载此前完全绕开 DownloadManager（各自用 HttpUtil 拼下载循环），
        /// 于是「下载任务」页看不到它们、也无法取消。但一次版本安装包含几百到上千个小文件，
        /// 若逐个文件建任务会把任务页刷爆（用户反而什么都看不清），也会冲掉历史记录。
        ///
        /// 因此引入"汇总任务"：一次安装 / 一个模组 = 任务页一行，
        /// 由业务模块自行并行下载文件，并把整体进度回填到这一行。
        /// 汇总任务不占并发槽、不持久化（它是运行期视图，不是可续传的文件下载）。
        /// </summary>
        public bool IsJob;
        /// <summary>汇总任务的完成百分比（0–100）；仅 IsJob 时有意义。</summary>
        public int Percent;
        /// <summary>用户取消汇总任务时的回调（仅内存态）。由发起方设置，用于中止其下载循环。</summary>
        public Action CancelCallback;

        /// <summary>运行中的取消源（仅内存态，不持久化）。由 DownloadManager 维护。</summary>
        public CancellationTokenSource ActiveToken;
        /// <summary>用户取消（仅内存态）：运行中任务最终落为 Canceled。</summary>
        public bool UserCancelRequested;
        /// <summary>用户暂停（仅内存态）：运行中任务最终落为 WaitingRetry（分片保留）。</summary>
        public bool UserPauseRequested;

        public string GetDedupKeyIfAny()
        {
            return (Url ?? "") + "|" + (ExpectedHash ?? "") + "|" + (TargetPath ?? "");
        }

        public DownloadRequest ToRequest()
        {
            DownloadRequest request = new DownloadRequest();
            request.TaskName = TaskName;
            request.Url = Url;
            request.TargetPath = TargetPath;
            request.HashAlgorithm = HashAlgorithm;
            request.ExpectedHash = ExpectedHash;
            request.ExpectedSize = ExpectedSize;
            request.Source = Source;
            request.Priority = Priority;
            request.AllowResume = AllowResume;
            return request;
        }
    }

    /// <summary>任务变更事件（节流后触发；UI 侧建议用定时器轮询快照，避免跨线程更新控件）。</summary>
    public sealed class TaskChangedEventArgs
    {
        public DownloadTask Task;
        public DownloadTaskState OldState;
        public DownloadTaskState NewState;
    }
}
