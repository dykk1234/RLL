using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    /// <summary>常用模组推荐项（模组页左侧列表的数据源）。</summary>
    public sealed class RecommendedMod
    {
        public readonly string Slug;
        public readonly string Title;
        public readonly string Description;
        /// <summary>true 表示多数玩家都会装，界面上优先展示。</summary>
        public readonly bool Common;

        public RecommendedMod(string slug, string title, string description, bool common)
        {
            Slug = slug;
            Title = title;
            Description = description;
            Common = common;
        }
    }

    /// <summary>
    /// 常用模组推荐清单。
    ///
    /// 放在 Core 层而不是界面里，是为了能被离线测试直接断言
    /// （例如"联机模组必须排在推荐列表里且置顶"），避免界面与数据脱节。
    ///
    /// 排序约定：与"能不能玩"直接相关的排前面。
    /// 目前第一位是 mcwifipnp —— 原版「对局域网开放」没有正版验证开关，
    /// 离线账号联机时加入方会报「无效会话」，这个模组才提供该开关。
    /// </summary>
    public static class RecommendedMods
    {
        /// <summary>联机必备模组的 Modrinth slug，供其它逻辑判断是否已安装。</summary>
        public const string LanModSlug = "mcwifipnp";

        public static List<RecommendedMod> GetAll()
        {
            List<RecommendedMod> list = new List<RecommendedMod>();
            list.Add(new RecommendedMod(
                LanModSlug,
                "更高级联机设置 LAN World Plug-n-Play",
                "联机必备：给「对局域网开放」加正版验证开关、端口自定义与 UPnP",
                true));
            list.Add(new RecommendedMod("sodium", "钠 Sodium",
                "渲染优化，显著提升帧率", true));
            list.Add(new RecommendedMod("jade", "玉 Jade",
                "显示准星所指方块与实体信息", true));
            list.Add(new RecommendedMod("appleskin", "苹果皮 AppleSkin",
                "显示饱和度与食物数值", true));
            list.Add(new RecommendedMod("lithium", "锂 Lithium",
                "通用逻辑与区块性能优化", false));
            list.Add(new RecommendedMod("sodium-extra", "钠：扩展 Sodium Extra",
                "为钠补充更多画面选项", false));
            list.Add(new RecommendedMod("indium", "铟 Indium",
                "补充 Fabric 渲染 API 兼容层", false));
            list.Add(new RecommendedMod("iris", "虹彩 Iris",
                "光影支持（需要钠）", false));
            list.Add(new RecommendedMod("modmenu", "模组菜单 Mod Menu",
                "在游戏内管理已安装模组", false));
            return list;
        }
    }
}
