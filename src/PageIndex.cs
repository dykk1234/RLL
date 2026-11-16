namespace LiteLauncher
{
    /// <summary>
    /// 导航页面索引常量（1.5.0 修复 F6 起集中管理，插入页面不再漏改）。
    /// 1.6.0（F-1601）：新增「下载任务」页（索引 3），模组/联机/设置/日志/关于顺延。
    /// 索引约定：启动0 / 账号1 / 下载2 / 下载任务3 / 模组4 / 光影5 / 联机6 / 设置7 / 日志8 / 关于9。
    /// </summary>
    public static class PageIndex
    {
        public const int Start = 0;
        public const int Account = 1;
        public const int Download = 2;
        /// <summary>1.6.0（F-1601）：下载任务中心页。</summary>
        public const int Tasks = 3;
        public const int Mods = 4;
        /// <summary>1.7.0（F-1701）：光影包管理页。</summary>
        public const int Shaders = 5;
        public const int Network = 6;
        public const int Settings = 7;
        public const int Logs = 8;
        public const int About = 9;
    }
}
