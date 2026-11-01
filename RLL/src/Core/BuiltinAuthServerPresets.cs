using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 内置认证服务器预设（不写入用户配置，仅作为界面上的便捷入口）。
    ///
    /// 集中定义的原因：界面用它填充下拉框、AuthService 用它预先放行主机白名单。
    /// 若两处各写一份地址，改一处漏一处就会出现"下拉里有但登录报不允许的地址"。
    /// </summary>
    public static class BuiltinAuthServerPresets
    {
        public const string LittleSkinName = "LittleSkin（社区）";
        public const string LittleSkinUrl = "https://littleskin.cn/api/yggdrasil";

        /// <summary>
        /// 取全部内置预设。按显示顺序返回。
        /// </summary>
        public static List<AuthServerPreset> GetAll()
        {
            List<AuthServerPreset> presets = new List<AuthServerPreset>();
            presets.Add(new AuthServerPreset(LittleSkinName, LittleSkinUrl));
            return presets;
        }
    }
}
