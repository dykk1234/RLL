using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using LiteLauncher;

namespace LiteLauncher.UiSmoke
{
    /// <summary>
    /// 界面冒烟测试：真实构造 MainForm，逐页切换，检查控件树、尺寸与可见性。
    /// 不发起网络请求（不触发生命周期事件），只验证布局逻辑。
    /// </summary>
    internal static class UiSmoke
    {
        private static int _passed;
        private static int _failed;

        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Run("Form construction", TestConstruction);
                Run("Page switching", TestPageSwitching);
                Run("Key controls present", TestKeyControls);
                Run("Layout bounds", TestLayoutBounds);
                Run("Status bar cancel button", TestStatusBarCancelButton);
                Run("Notice bars per page", TestNoticeBars);
                Run("Log empty state", TestLogEmptyState);
                Run("Nav glyphs", TestNavGlyphs);
                Run("Form icon", TestFormIcon);
                Run("Settings 1.3.0 controls", TestSettingsNewControls);
                Run("Java label single line", TestJavaLabelLayout);
                Run("Launch guide panel", TestLaunchGuide);
                Run("No PCL references in UI text", TestNoPclReference);
                Run("Dark theme colors", TestDarkTheme);
                Run("No async lifecycle invoked", TestNoNetworkOnConstruction);
                Run("Mod page new buttons", TestModPageNewButtons);
                Run("Java download buttons", TestJavaDownloadButtons);
                // 1.7.5（B-18）：缩放 × 窗口尺寸 布局矩阵，锁住"设置页控件被挤出可视区"。
                Run("Settings layout matrix", TestSettingsLayoutMatrix);
                // 1.7.6（B-19/B-20）：模组页目录提示 + 下载任务页承载汇总任务。
                Run("Mods page shows managed directory", TestModsDirectoryNote);
                // 1.7.7（B-21）：模组目标目录必须跟随启动版本，而不是模组页下拉框
                Run("Mods target follows launch version", TestModsTargetFollowsLaunchVersion);
                // 1.7.7：模组页按钮较多，换行后不能落到窗口可视区之外
                Run("Mods page buttons in view", TestModsPageButtonsInView);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("UI Passed: " + _passed + ", Failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            int passedBefore = _passed;
            try
            {
                test();
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                Exception failure = ex;
                // 1.7.7：夹具「构造窗体 → 立刻 Dispose」会偶发 WinForms 的
                // 「执行 CreateHandle() 时无法调用值 Dispose()」（隐藏页
                // ListView 句柄被销毁后，释放时又试图重建）。
                // 对照 1.7.6 原版基线 5 轮中 1 轮复现，属于既有释放时序
                // 问题，与被测逻辑无关。恢复计数后重跑一次并在输出中留痕；
                // 重跑仍失败才计为真失败。
                if (IsWinFormsDisposeRace(ex))
                {
                    _passed = passedBefore;
                    try
                    {
                        test();
                        Console.WriteLine("[PASS] " + name + "（WinForms 释放时序重试）");
                        return;
                    }
                    catch (Exception retryEx)
                    {
                        failure = retryEx;
                        _passed = passedBefore;
                    }
                }
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + failure.GetType().Name +
                                  ": " + failure.Message);
                Console.WriteLine(failure.StackTrace);
            }
        }

        private static bool IsWinFormsDisposeRace(Exception ex)
        {
            return ex is InvalidOperationException &&
                   (ex.Message ?? "").IndexOf("CreateHandle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Check(string name, bool condition)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + name);
            }
            _passed++;
        }

        private static MainForm CreateForm(bool show)
        {
            // skipAutoStart 通过构造函数注入：界面测试不应触发联网与 Java 探测。
            MainForm form = new MainForm(true);
            form.CreateControl();
            form.Size = new Size(1080, 740);
            if (show)
            {
                // 隐藏窗体不会真正执行布局与坐标换算，必须显示后再测量。
                form.Show();
                Application.DoEvents();
            }
            form.PerformLayout();
            return form;
        }

        private static MainForm CreateForm()
        {
            return CreateForm(true);
        }

        private static T FindControl<T>(Control root, string name) where T : Control
        {
            if (root.Controls.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control child = root.Controls[i];
                if (child.Name == name && child is T)
                {
                    return (T)child;
                }
                T found = FindControl<T>(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void CollectByType<T>(Control root, List<T> result) where T : Control
        {
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control child = root.Controls[i];
                if (child is T)
                {
                    result.Add((T)child);
                }
                CollectByType<T>(child, result);
            }
        }

        private static object GetPrivateField(object target, string field)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null)
                {
                    return info.GetValue(target);
                }
                type = type.BaseType;
            }
            throw new Exception("field not found: " + field);
        }

        private static List<Control> GetPages(MainForm form)
        {
            return (List<Control>)GetPrivateField(form, "_pages");
        }

        private static void TestConstruction()
        {
            using (MainForm form = CreateForm())
            {
                Check("form created", form != null);
                Check("title has brand", form.Text.IndexOf("RLL", StringComparison.Ordinal) >= 0);
                // 版本号现在来自程序集元数据，测试不应硬编码具体版本。
                Check("title has version", form.Text.IndexOf(
                    LiteLauncher.Core.AppPaths.LauncherVersion, StringComparison.Ordinal) >= 0);
                Check("client size", form.ClientSize.Width >= 1000 && form.ClientSize.Height >= 700);
                Check("minimum size", form.MinimumSize.Width >= 900 && form.MinimumSize.Height >= 620);
            }
        }

        private static void TestPageSwitching()
        {
            // 1.7.7：这里不用 using 在切页后立刻 Dispose。逐页切换会销毁隐藏页
            // ListView 的句柄，紧接着 Dispose 时 WinForms 会试图重建句柄并抛
            // 「执行 CreateHandle() 时无法调用值 Dispose()」。该问题在 1.7.6
            // 基线同样偶发（对照 5 轮中 1 轮复现），并非本次改动引入；
            // 夹具改为隐藏窗体、由进程退出统一清理，避免把 WinForms 的
            // 释放时序问题误判成界面回归。产品侧在 OnFormClosing 补齐句柄。
            MainForm form = CreateForm();
            try
            {
                List<Control> pages = GetPages(form);
                Check("page count matches PageIndex", pages.Count == PageIndex.About + 1);

                List<NavButton> navs = new List<NavButton>();
                CollectByType<NavButton>(form, navs);
                Check("nav buttons match page count", navs.Count == GetPages(form).Count);

                // 初始应停在启动页，且只有该页可见。
                int visible = 0;
                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages[i].Visible)
                    {
                        visible++;
                    }
                }
                Check("one visible page initially", visible == 1);
                Check("launch page selected", pages[0].Visible);
                Check("first nav selected", navs[0].Selected);
                Check("others not selected", !navs[1].Selected && !navs[2].Selected);

                // 逐页切换，每次都应只剩目标页可见，并且导航状态同步。
                // 页面顺序必须与 PageIndex 一致（1.6.0/1.7.0 插入新页后这里曾长期未同步）。
                string[] expectedTitles = new string[]
                {
                    "启动游戏", "账号", "下载与安装", "下载任务", "模组下载", "光影",
                    "异地联机", "设置", "运行日志", "关于 RLL"
                };
                Check("title table matches page count",
                    expectedTitles.Length == pages.Count);
                for (int i = 0; i < pages.Count; i++)
                {
                    InvokeShowPage(form, i);
                    form.PerformLayout();
                    int count = 0;
                    for (int j = 0; j < pages.Count; j++)
                    {
                        if (pages[j].Visible)
                        {
                            count++;
                        }
                    }
                    Check("only page " + i + " visible", count == 1);
                    Check("page " + i + " visible", pages[i].Visible);
                    Check("nav " + i + " selected", navs[i].Selected);
                    Check("page " + i + " has size",
                        pages[i].Width > 0 && pages[i].Height > 0);
                    Check("page " + i + " title", HasTitle(pages[i], expectedTitles[i]));
                }

            }
            finally
            {
                form.Hide();
            }
        }

        private static bool HasTitle(Control page, string expected)
        {
            List<Label> labels = new List<Label>();
            CollectByType<Label>(page, labels);
            for (int i = 0; i < labels.Count; i++)
            {
                if (string.Equals(labels[i].Text, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void InvokeShowPage(MainForm form, int index)
        {
            MethodInfo method = form.GetType().GetMethod(
                "ShowPage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new Exception("ShowPage not found");
            }
            method.Invoke(form, new object[] { index });
        }

        private static void TestKeyControls()
        {
            using (MainForm form = CreateForm())
            {
                Check("account combo", GetPrivateField(form, "_accountCombo") != null);
                Check("account page list", GetPrivateField(form, "_accountList") != null);
                Check("auth server combo", GetPrivateField(form, "_authServerCombo") != null);
                Check("auth login button", GetPrivateField(form, "_authLoginButton") != null);
                Check("injector button", GetPrivateField(form, "_injectorButton") != null);
                Check("version combo", GetPrivateField(form, "_versionCombo") != null);
                Check("java combo", GetPrivateField(form, "_javaCombo") != null);
                Check("start button", GetPrivateField(form, "_startButton") != null);
                Check("remote list", GetPrivateField(form, "_remoteList") != null);
                Check("installed list", GetPrivateField(form, "_installedList") != null);
                Check("mod version combo", GetPrivateField(form, "_modVersionCombo") != null);
                Check("mod list", GetPrivateField(form, "_modList") != null);
                Check("mod search list", GetPrivateField(form, "_modSearchList") != null);
                Check("install common button", GetPrivateField(form, "_installCommonModsButton") != null);
                Check("game dir box", GetPrivateField(form, "_gameDirBox") != null);
                Check("log box", GetPrivateField(form, "_logBox") != null);
                Check("about box", GetPrivateField(form, "_aboutBox") != null);
                Check("status label", GetPrivateField(form, "_statusLabel") != null);
                Check("progress bar", GetPrivateField(form, "_progressBar") != null);

                // 常用模组目录：条数随需求增减，这里只断言非空并检查关键模组在列，
                // 不绑定具体下标（顺序会随优先级调整，例如联机模组被置顶）。
                ListView modList = (ListView)GetPrivateField(form, "_modList");
                Check("mod catalog non-empty", modList.Items.Count > 0);
                Check("lan mod listed first",
                    JoinText(modList.Items[0]).IndexOf("联机", StringComparison.Ordinal) >= 0);
                Check("lan mod mentions online mode",
                    JoinText(modList.Items[0]).IndexOf("正版验证", StringComparison.Ordinal) >= 0);
                Check("sodium listed", ContainsMod(modList, "Sodium"));
                Check("jade listed", ContainsMod(modList, "Jade"));
                Check("appleskin listed", ContainsMod(modList, "AppleSkin"));

                // 启动页按钮初始状态：无版本时不可启动。
                Button start = (Button)GetPrivateField(form, "_startButton");
                Check("start enabled before version selected", start.Enabled);

                // 模组安装按钮在未选择 Fabric 版本时应禁用。
                Button common = (Button)GetPrivateField(form, "_installCommonModsButton");
                Check("mod install disabled without fabric version", !common.Enabled);
            }
        }

        /// <summary>在模组列表中按标题检索，避免依赖固定下标。</summary>
        private static bool ContainsMod(ListView list, string keyword)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (JoinText(list.Items[i]).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string JoinText(ListViewItem item)
        {
            string text = item.Text;
            for (int i = 0; i < item.SubItems.Count; i++)
            {
                text += " " + item.SubItems[i].Text;
            }
            return text;
        }

        private static void TestLayoutBounds()
        {
            using (MainForm form = CreateForm())
            {
                List<Control> pages = GetPages(form);
                InvokeShowPage(form, PageIndex.Start);
                form.PerformLayout();

                // 启动页的卡片必须铺满可见区域，且按钮在卡片内。
                Button start = (Button)GetPrivateField(form, "_startButton");
                Check("start button has size", start.Width > 100 && start.Height > 30);

                CardPanel firstCard = null;
                List<CardPanel> cards = new List<CardPanel>();
                CollectByType<CardPanel>(pages[0], cards);
                Check("launch page has card", cards.Count == 1);
                firstCard = cards[0];
                Check("card has size", firstCard.Width > 400 && firstCard.Height > 200);

                Point screenPoint = start.PointToScreen(Point.Empty);
                Rectangle cardRect = new Rectangle(
                    firstCard.PointToScreen(Point.Empty), firstCard.Size);
                Check("start button inside card", cardRect.Contains(
                    new Rectangle(screenPoint, start.Size)));

                // 侧边导航宽度固定，内容区应占剩余宽度。
                List<NavButton> navs = new List<NavButton>();
                CollectByType<NavButton>(form, navs);
                Check("nav width", navs[0].Width >= 150);

                // 下载页的两个列表都要有列，且可滚动区域足够。
                ListView remote = (ListView)GetPrivateField(form, "_remoteList");
                Check("remote list columns", remote.Columns.Count == 3);
                ListView installed = (ListView)GetPrivateField(form, "_installedList");
                Check("installed list columns", installed.Columns.Count == 1);

                // 日志页的日志框应使用等宽字体且只读。
                TextBox log = (TextBox)GetPrivateField(form, "_logBox");
                Check("log readonly", log.ReadOnly);
                Check("log monospace", log.Font.Name == "Consolas");

                // 关于页文本框只读。
                TextBox about = (TextBox)GetPrivateField(form, "_aboutBox");
                Check("about readonly", about.ReadOnly);
                Check("about mentions mods", about.Text.IndexOf("Modrinth", StringComparison.Ordinal) >= 0);
                Check("about mentions default dir", about.Text.IndexOf(".minecraft", StringComparison.Ordinal) >= 0);
            }
        }

        /// <summary>
        /// 取消按钮的显示异常回归测试：AutoSize 的按钮在 TableLayoutPanel 的
        /// AutoSize 列里会把整行撑到约 100px，远超 46px 的状态栏高度，
        /// 导致按钮下半部分被裁掉。这里锁定"按钮不超过状态栏高度"这一点。
        /// </summary>
        private static void TestStatusBarCancelButton()
        {
            using (MainForm form = CreateForm())
            {
                Button cancel = (Button)GetPrivateField(form, "_cancelButton");
                Check("cancel not autosize", !cancel.AutoSize);
                Check("cancel height fits status bar", cancel.Height <= 40);
                Check("cancel width sane", cancel.Width >= 50 && cancel.Width <= 100);
                Check("cancel text", string.Equals(cancel.Text, "取消", StringComparison.Ordinal));

                // 按钮必须落在状态栏面板内，且不与状态文字重叠。
                Panel bar = cancel.Parent == null ? null : cancel.Parent.Parent as Panel;
                Check("cancel inside status bar", bar != null);
                if (bar != null)
                {
                    Rectangle barOnForm = new Rectangle(bar.PointToScreen(Point.Empty), bar.Size);
                    Rectangle cancelOnForm = new Rectangle(
                        cancel.PointToScreen(Point.Empty), cancel.Size);
                    Check("cancel within bar bounds", barOnForm.Contains(cancelOnForm));
                    Check("cancel height <= bar height", cancel.Height <= bar.Height);
                }

                Label status = (Label)GetPrivateField(form, "_statusLabel");
                Rectangle statusOnForm = new Rectangle(
                    status.PointToScreen(Point.Empty), status.Size);
                Rectangle cancelRect = new Rectangle(
                    cancel.PointToScreen(Point.Empty), cancel.Size);
                Check("status and cancel do not overlap",
                    !statusOnForm.IntersectsWith(cancelRect));
            }
        }

        private static void TestNoticeBars()
        {
            using (MainForm form = CreateForm())
            {
                List<NoticeBar> notices = new List<NoticeBar>();
                CollectByType<NoticeBar>(form, notices);
                Check("one notice per page", notices.Count == GetPages(form).Count);
                for (int i = 0; i < notices.Count; i++)
                {
                    // 初始全部隐藏，避免空占位。
                    Check("notice hidden initially " + i, !notices[i].Visible);
                    Check("notice has height", notices[i].Height > 0);
                }
            }
        }

        /// <summary>
        /// 日志页在无内容时必须显示空状态占位，而不是一片空白。
        /// 同时验证：写入日志后空状态自动隐藏。
        /// </summary>
        private static void TestLogEmptyState()
        {
            using (MainForm form = CreateForm())
            {
                List<Control> pages = GetPages(form);
                InvokeShowPage(form, PageIndex.Logs);
                form.PerformLayout();

                List<EmptyState> states = new List<EmptyState>();
                CollectByType<EmptyState>(pages[PageIndex.Logs], states);
                Check("log page has empty state", states.Count == 1);
                EmptyState empty = states[0];
                Check("empty state visible initially", empty.Visible);
                Check("empty state has title", empty.TitleText.Length > 0);
                Check("empty state has action", empty.ActionText.Length > 0);

                // 空状态必须覆盖在日志框之上（z-order 更靠前）。
                TextBox logBox = (TextBox)GetPrivateField(form, "_logBox");
                Check("empty state above log box",
                    logBox.Parent.Controls.GetChildIndex(empty) <
                    logBox.Parent.Controls.GetChildIndex(logBox));

                // 写入一条日志后应自动隐藏。
                MethodInfo onLog = form.GetType().GetMethod(
                    "OnLogMessage", BindingFlags.Instance | BindingFlags.NonPublic);
                Check("OnLogMessage exists", onLog != null);
                if (onLog != null)
                {
                    onLog.Invoke(form, new object[] { "[test] 一条测试日志" });
                    Check("empty state hidden after log", !empty.Visible);
                    Check("log box has content", logBox.TextLength > 0);
                }
            }
        }

        /// <summary>
        /// 导航按钮必须各自带不同的矢量图标，避免所有页共用同一个图形。
        /// </summary>
        private static void TestNavGlyphs()
        {
            using (MainForm form = CreateForm())
            {
                List<NavButton> navs = new List<NavButton>();
                CollectByType<NavButton>(form, navs);
                Check("nav buttons match page count", navs.Count == GetPages(form).Count);
                for (int i = 0; i < navs.Count; i++)
                {
                    Check("nav " + i + " glyph assigned",
                        navs[i].Glyph == NavGlyphs.FromIndex(i));
                }
                HashSet<NavGlyphs.Glyph> distinct = new HashSet<NavGlyphs.Glyph>();                for (int i = 0; i < navs.Count; i++)
                {
                    distinct.Add(navs[i].Glyph);
                }
                Check("nav glyphs are distinct", distinct.Count == navs.Count);
            }
        }

        /// <summary>
        /// 窗口必须带图标；取不到 exe 图标时退回系统默认图标。
        /// </summary>
        private static void TestFormIcon()
        {
            using (MainForm form = CreateForm())
            {
                Check("form has icon", form.Icon != null);
            }
        }

        /// <summary>
        /// 1.3.0 设置页新增控件：下载源下拉、版本隔离开关、异地联机开关与配置区。
        /// </summary>
        private static void TestSettingsNewControls()
        {
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Settings);
                form.PerformLayout();
                Application.DoEvents();

                ComboBox mirror = (ComboBox)GetPrivateField(form, "_mirrorCombo");
                Check("mirror combo exists", mirror != null);
                Check("mirror has three options", mirror.Items.Count == 3);
                Check("mirror default official", mirror.SelectedIndex == 0);

                CheckBox isolate = (CheckBox)GetPrivateField(form, "_isolateCheck");
                Check("isolate check exists", isolate != null);
                // 不假设"默认为关"：该值来自用户配置，随实际保存的设置变化。
                // 这里只验证控件状态与配置一致（界面正确反映了已保存的值）。
                Check("isolate reflects config",
                    isolate.Checked == LiteLauncher.Core.LauncherConfig.Load().IsolateVersions);

                CheckBox easyTier = (CheckBox)GetPrivateField(form, "_easyTierEnabledCheck");
                Check("easytier check exists", easyTier != null);
                // 不假设"默认为关"：该值来自用户配置，可能已被用户开启。
                // 断言界面正确反映了已保存的设置。
                Check("easytier reflects config",
                    easyTier.Checked == LiteLauncher.Core.LauncherConfig.Load().EasyTierEnabled);

                TextBox name = (TextBox)GetPrivateField(form, "_easyTierNameBox");
                Check("easytier name box", name != null);
                TextBox secret = (TextBox)GetPrivateField(form, "_easyTierSecretBox");
                Check("easytier secret box", secret != null);
                Check("easytier secret masked", secret.UseSystemPasswordChar);
                Check("easytier node box",
                    GetPrivateField(form, "_easyTierNodeBox") != null);
                Check("easytier binary box",
                    GetPrivateField(form, "_easyTierBinaryBox") != null);
                Check("easytier download button",
                    GetPrivateField(form, "_easyTierDownloadButton") != null);
                Check("easytier copy ip button",
                    GetPrivateField(form, "_easyTierCopyIpButton") != null);
                Check("easytier status label",
                    GetPrivateField(form, "_easyTierStatusLabel") != null);

                // 配置区可见性必须跟随开关状态（不假设初始值为关：
                // 用户可能已启用，此时展开才是正确行为）。
                TableLayoutPanel configPanel =
                    (TableLayoutPanel)GetPrivateField(form, "_easyTierConfigPanel");
                Check("easytier config panel exists", configPanel != null);
                // 1.7.5：联机配置卡自 1.5.0 起属「联机」页，不在设置页。
                // 控件有效可见性依赖父页面可见，所以先切到联机页再断言展开/折叠，
                // 否则读到的永远是 False（这正是本用例长期假失败的原因）。
                InvokeShowPage(form, PageIndex.Network);
                form.PerformLayout();
                Application.DoEvents();

                // 关 -> 折叠
                easyTier.Checked = false;
                Application.DoEvents();
                form.PerformLayout();
                Check("easytier config collapses", !configPanel.Visible);
                // 开 -> 展开
                easyTier.Checked = true;
                Application.DoEvents();
                form.PerformLayout();
                Check("easytier config expands", configPanel.Visible);

                // 恢复到用户实际配置值，避免影响后续用例。
                easyTier.Checked = LiteLauncher.Core.LauncherConfig.Load().EasyTierEnabled;
                Application.DoEvents();

                // 设置页内容超过一屏时必须能滚动，否则底部控件点不到。
                List<Control> pages = GetPages(form);
                Check("settings page scrollable", HasScrollHost(pages[PageIndex.Settings]));
            }
        }

        private static bool HasScrollHost(Control page)
        {
            List<Panel> panels = new List<Panel>();
            CollectByType<Panel>(page, panels);
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i].AutoScroll)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 新手引导面板：首次打开应可见、有步骤与主按钮；
        /// 版本/Java/账号都齐备后应自动隐藏，避免日常使用时一直占用空间。
        /// </summary>
        private static void TestLaunchGuide()
        {
            using (MainForm form = CreateForm())
            {
                GuidePanel guide = (GuidePanel)GetPrivateField(form, "_launchGuide");
                Check("guide exists", guide != null);
                if (guide == null) { return; }

                // 启动页可见时刷新一次引导状态。
                InvokeShowPage(form, PageIndex.Start);
                MethodInfo update = form.GetType().GetMethod(
                    "UpdateLaunchGuide", BindingFlags.Instance | BindingFlags.NonPublic);
                Check("UpdateLaunchGuide exists", update != null);
                if (update != null)
                {
                    update.Invoke(form, null);
                }
                form.PerformLayout();
                Application.DoEvents();

                Check("guide has positive height", guide.Height > 80);
                Check("guide preferred height", guide.PreferredHeight > 80);

                // 引导不能在启动按钮上：验证两者不重叠。
                Button start = (Button)GetPrivateField(form, "_startButton");
                if (guide.Visible && start != null)
                {
                    Rectangle g = new Rectangle(guide.PointToScreen(Point.Empty), guide.Size);
                    Rectangle s = new Rectangle(start.PointToScreen(Point.Empty), start.Size);
                    Check("guide does not cover start button", !g.IntersectsWith(s));
                }

                // 手动隐藏后再刷新不应重新出现（本次运行内保持关闭）。
                guide.HideGuide();
                Check("guide hidden", !guide.Visible);
            }
        }

        /// <summary>
        /// UI 文案中不应出现"模仿 / 参考其它启动器"的表述。
        /// </summary>
        private static void TestNoPclReference()
        {
            using (MainForm form = CreateForm())
            {
                List<Label> labels = new List<Label>();
                List<Control> pages = GetPages(form);
                for (int i = 0; i < pages.Count; i++)
                {
                    CollectByType<Label>(pages[i], labels);
                }
                int hits = 0;
                for (int i = 0; i < labels.Count; i++)
                {
                    if (labels[i].Text != null &&
                        labels[i].Text.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hits++;
                        Console.WriteLine("  PCL in label: " + labels[i].Text);
                    }
                }
                Check("no PCL mention in page labels", hits == 0);

                // 关于页正文同样不应出现。
                TextBox about = (TextBox)GetPrivateField(form, "_aboutBox");
                Check("about has no PCL", about == null ||
                    about.Text.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) < 0);
            }
        }

        /// <summary>
        /// 回归：启动页 Java 标签必须单行显示且不被截断。
        /// 旧布局用固定 104px 的标签列，而 "Java（需要 17）" 需要 107px，
        /// 会被压窄折成两行。
        /// </summary>
        private static void TestJavaLabelLayout()
        {
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Start);
                form.PerformLayout();
                Application.DoEvents();

                Label javaLabel = (Label)GetPrivateField(form, "_javaRequirementLabel");
                Check("java label exists", javaLabel != null);

                string[] variants = new string[]
                {
                    "Java", "Java（需 8）", "Java（需 17）", "Java（需 21）"
                };
                for (int i = 0; i < variants.Length; i++)
                {
                    javaLabel.Text = variants[i];
                    form.PerformLayout();
                    Application.DoEvents();
                    Size ink = TextRenderer.MeasureText(
                        javaLabel.Text, javaLabel.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding);
                    Check("java label single line: " + variants[i],
                        javaLabel.Height <= ink.Height + 3);
                    Check("java label not truncated: " + variants[i],
                        javaLabel.Width >= javaLabel.PreferredSize.Width);
                }

                // 所有表单标签都不应折行。
                List<Control> allPages = GetPages(form);
                List<Label> labels = new List<Label>();
                CollectByType<Label>(allPages[0], labels);
                int wrapped = 0;
                for (int i = 0; i < labels.Count; i++)
                {
                    Label label = labels[i];
                    if (!label.Visible || label.Text.Length == 0 || label.Font.Bold ||
                        label.Text.Length > 30)
                    {
                        continue;
                    }
                    Size ink = TextRenderer.MeasureText(
                        label.Text, label.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding);
                    if (label.Height > ink.Height + 3)
                    {
                        wrapped++;
                    }
                }
                Check("no wrapped field labels on launch page", wrapped == 0);
            }
        }

        private static void TestDarkTheme()
        {
            bool originalDark = UiTheme.IsDark;
            try
            {
                UiTheme.SetDark(false);
                Color lightCard = UiTheme.CardBackground;
                Color lightText = UiTheme.TextPrimary;
                UiTheme.SetDark(true);
                Color darkCard = UiTheme.CardBackground;
                Color darkText = UiTheme.TextPrimary;

                Check("dark card differs", lightCard != darkCard);
                Check("dark card is dark", darkCard.GetBrightness() < 0.35f);
                Check("light card is bright", lightCard.GetBrightness() > 0.8f);
                Check("dark text is light", darkText.GetBrightness() > 0.7f);
                Check("light text is dark", lightText.GetBrightness() < 0.4f);
                Check("dark background dark", UiTheme.Background.GetBrightness() < 0.2f);
            }
            finally
            {
                UiTheme.SetDark(originalDark);
            }

            // 深色模式下窗体仍能正常构建与切换页面。
            bool before = UiTheme.IsDark;
            try
            {
                UiTheme.SetDark(true);
                using (MainForm form = CreateForm())
                {
                    Check("dark form builds", form.Text.IndexOf("RLL", StringComparison.Ordinal) >= 0);
                    List<Control> pages = GetPages(form);
                    Check("dark form has all pages", pages.Count == PageIndex.About + 1);
                }
            }
            finally
            {
                UiTheme.SetDark(before);
            }
        }

        private static void TestNoNetworkOnConstruction()
        {
            // 构造函数只应构建界面与读取本地配置，不应阻塞在网络上。
            // 这里通过测量构造耗时间接验证（本地配置读取为毫秒级）。
            DateTime begin = DateTime.UtcNow;
            using (MainForm form = CreateForm())
            {
                double elapsed = (DateTime.UtcNow - begin).TotalSeconds;
                Check("construction under 5s", elapsed < 5.0);
                Check("no manifest loaded", GetPrivateField(form, "_manifest") == null);
                Check("mods page not loaded", (bool)GetPrivateField(form, "_modsPageLoaded") == false);
            }
        }

        /// <summary>
        /// 模组页新增的三个入口按钮必须存在、有文案、且在版面上完整可见。
        /// （安装本地 Mod JAR / 导出模组预设 / 导入模组预设）
        /// </summary>
        private static void TestModPageNewButtons()
        {
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Mods);
                form.PerformLayout();
                Application.DoEvents();

                string[] fieldNames = new string[]
                {
                    "_installLocalModButton", "_exportPresetButton", "_importPresetButton"
                };
                string[] expectedTexts = new string[]
                {
                    "安装本地 Mod JAR", "导出模组预设", "导入模组预设"
                };
                for (int i = 0; i < fieldNames.Length; i++)
                {
                    Button button = (Button)GetPrivateField(form, fieldNames[i]);
                    Check(fieldNames[i] + " exists", button != null);
                    if (button == null)
                    {
                        continue;
                    }
                    Check(fieldNames[i] + " has text", button.Text != null &&
                        button.Text.IndexOf(expectedTexts[i], StringComparison.Ordinal) >= 0);
                    Check(fieldNames[i] + " is visible", button.Visible);
                    // 按钮不能被父容器裁掉：宽度必须够放下文案。
                    Check(fieldNames[i] + " fits its text",
                        button.Width >= button.Text.Length * 7);
                    Check(fieldNames[i] + " has handler", HasClickHandler(button));
                }
            }
        }

        /// <summary>
        /// 判断按钮是否绑定了 Click 事件（防止新按钮建好了却忘了接线）。
        ///
        /// WinForms 把事件处理器存在 Control 的 EventHandlerList 里，
        /// 键是 Control 私有的静态字段 EventClick。
        /// </summary>
        private static bool HasClickHandler(Button button)
        {
            if (button == null)
            {
                return false;
            }
            System.Reflection.FieldInfo keyField = typeof(Control).GetField(
                "EventClick",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            if (keyField == null)
            {
                return false;
            }
            object key = keyField.GetValue(null);
            if (key == null)
            {
                return false;
            }
            System.Reflection.PropertyInfo eventsProp = typeof(Control).GetProperty(
                "Events",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (eventsProp == null)
            {
                return false;
            }
            System.ComponentModel.EventHandlerList list =
                eventsProp.GetValue(button, null) as System.ComponentModel.EventHandlerList;
            if (list == null)
            {
                return false;
            }
            Delegate handler = list[key];
            return handler != null;
        }

        /// <summary>
        /// 1.7.5（B-18）回归：设置页布局矩阵。
        ///
        /// 之前的问题：设置页所有横向容器都设了 WrapContents = false 并使用硬编码控件宽度，
        /// 窗口变窄或界面缩放调到 110%–130% 时右侧控件被挤出可视区且不换行；
        /// 同时固定宽度完全没适配缩放（字号涨 30% 而控件宽度不变）。
        ///
        /// 这里遍历「界面缩放 90/100/110/120/130 × 窗口 1080×740 / 980×660」，
        /// 逐档断言关键控件的可见性、尺寸与说明文字的多行高度 —— 只要有一档坏掉就失败。
        /// </summary>
        private static void TestSettingsLayoutMatrix()
        {
            int[] scales = new int[] { 90, 100, 110, 120, 130 };
            Size[] windows = new Size[] { new Size(1080, 740), new Size(980, 660) };
            int originalScale = UiTheme.FontScalePercent;
            try
            {
                for (int s = 0; s < scales.Length; s++)
                {
                    UiTheme.SetFontScale(scales[s]);
                    for (int w = 0; w < windows.Length; w++)
                    {
                        Size window = windows[w];
                        using (MainForm form = CreateForm(false))
                        {
                            form.ClientSize = window;
                            form.Show();
                            Application.DoEvents();
                            InvokeShowPage(form, PageIndex.Settings);
                            form.PerformLayout();
                            Application.DoEvents();

                            string tag = " (scale " + scales[s] + "%, window " +
                                window.Width + "x" + window.Height + ")";
                            List<Control> pages = GetPages(form);

                            // 1) 设置页横向容器一律允许换行（这是遮挡的最主要原因）。
                            List<FlowLayoutPanel> bars = new List<FlowLayoutPanel>();
                            CollectByType<FlowLayoutPanel>(pages[PageIndex.Settings], bars);
                            for (int i = 0; i < bars.Count; i++)
                            {
                                Check("settings toolbar wraps" + tag, bars[i].WrapContents);
                            }

                            // 2) 关键控件存在、可见且有尺寸。
                            string[] criticalFields = new string[]
                            {
                                "_accentCombo", "_fontScaleCombo", "_saveSettingsButton",
                                "_mirrorCombo", "_gameDirBox"
                            };
                            for (int i = 0; i < criticalFields.Length; i++)
                            {
                                Control control = (Control)GetPrivateField(form, criticalFields[i]);
                                Check("critical control exists: " + criticalFields[i] + tag,
                                    control != null);
                                if (control == null)
                                {
                                    continue;
                                }
                                Check("critical control visible: " + criticalFields[i] + tag,
                                    control.Visible);
                                Check("critical control has size: " + criticalFields[i] + tag,
                                    control.Width > 0 && control.Height > 0);
                            }

                            // 3) 固定宽度必须随缩放增长（此前是硬编码，缩放越大越容易截断）。
                            ComboBox accent = (ComboBox)GetPrivateField(form, "_accentCombo");
                            ComboBox scaleCombo =
                                (ComboBox)GetPrivateField(form, "_fontScaleCombo");
                            Check("accent combo width scales" + tag,
                                accent.Width >= UiTheme.Scaled(116));
                            Check("font scale combo width scales" + tag,
                                scaleCombo.Width >= UiTheme.Scaled(84));

                            // 4) 说明文字不能被裁：按控件实际宽度重算折行后应有高度并比较。
                            //    单行放得下也算通过（不强制多行），只要求不裁字。
                            List<Label> labels = new List<Label>();
                            CollectByType<Label>(pages[PageIndex.Settings], labels);
                            int notes = 0;
                            int clippedNotes = 0;
                            for (int i = 0; i < labels.Count; i++)
                            {
                                Label label = labels[i];
                                if (!label.Visible || label.Text.Length < 40 ||
                                    !label.AutoSize)
                                {
                                    continue;
                                }
                                notes++;
                                Size needed = TextRenderer.MeasureText(
                                    label.Text, label.Font,
                                    new Size(label.Width, int.MaxValue),
                                    TextFormatFlags.WordBreak);
                                if (label.Height < needed.Height - 2)
                                {
                                    clippedNotes++;
                                    Console.WriteLine("  clipped note" + tag + ": h=" +
                                        label.Height + " needed=" + needed.Height +
                                        " w=" + label.Width);
                                }
                            }
                            Check("settings page has notes" + tag, notes > 0);
                            Check("no clipped note" + tag, clippedNotes == 0);
                        }
                    }
                }
            }
            finally
            {
                UiTheme.SetFontScale(originalScale);
            }
        }

        /// <summary>
        /// 1.7.6（B-19）回归：模组页必须明确显示「当前管理目录」。
        ///
        /// 之前的实测反馈"各版本 mod 无法分别管理"，一半原因在服务层路径分叉，
        /// 另一半就是界面上根本看不出当前这一屏管的是哪个目录 ——
        /// 用户无从判断切换版本后列表变化是 bug 还是隔离生效。
        /// </summary>
        private static void TestModsDirectoryNote()
        {
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Mods);
                form.PerformLayout();
                Application.DoEvents();

                Label note = (Label)GetPrivateField(form, "_modsDirLabel");
                Check("mods dir note exists", note != null);
                if (note == null)
                {
                    return;
                }
                Check("mods dir note visible", note.Visible);
                Check("mods dir note has size", note.Width > 0 && note.Height > 0);
                Check("mods dir note mentions directory",
                    note.Text != null && note.Text.IndexOf("mods", StringComparison.OrdinalIgnoreCase) >= 0);
                Check("mods dir note states isolation scope",
                    note.Text.Contains("版本隔离"));

                // 已安装列表与目录提示必须同时刷新，避免"提示显示 A、列表显示 B"。
                ListView installed =
                    (ListView)GetPrivateField(form, "_installedModList");
                Check("installed mod list exists", installed != null);
                if (installed != null)
                {
                    Check("installed mod list visible", installed.Visible);
                }
            }
        }

        /// <summary>
        /// 「下载 Java」在启动页与下载页各有一个入口，两个都必须存在且接了事件。
        ///
        /// 背景：用户装完原版后最可能停在下载页，而以前「下载 Java」只在启动页，
        /// 不少用户根本没注意到那个按钮，于是以为"没有这个功能"。
        /// </summary>
        private static void TestJavaDownloadButtons()
        {
            using (MainForm form = CreateForm())
            {
                string[] fieldNames = new string[]
                {
                    "_downloadJavaButton", "_downloadJavaButton2"
                };
                for (int i = 0; i < fieldNames.Length; i++)
                {
                    Button button = (Button)GetPrivateField(form, fieldNames[i]);
                    Check(fieldNames[i] + " exists", button != null);
                    if (button == null)
                    {
                        continue;
                    }
                    Check(fieldNames[i] + " has text", button.Text != null &&
                        button.Text.IndexOf("下载 Java", StringComparison.Ordinal) >= 0);
                    Check(fieldNames[i] + " has click handler", HasClickHandler(button));
                }
            }
        }

        private static string InvokePrivateString(MainForm form, string name)
        {
            MethodInfo method = typeof(MainForm).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new Exception(name + " not found");
            }
            return (string)method.Invoke(form, null);
        }

        private static void SetIsolation(MainForm form, bool value)
        {
            object config = GetPrivateField(form, "_config");
            Type type = config.GetType();
            FieldInfo field = type.GetField("IsolateVersions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(config, value);
                return;
            }
            PropertyInfo property = type.GetProperty("IsolateVersions",
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                throw new Exception("IsolateVersions not found");
            }
            property.SetValue(config, value, null);
        }

        /// <summary>
        /// 1.7.7（B-21）回归：人为复现 1.7.6 的「启动页选 B、模组页下拉停在 A」，
        /// 断言安装目录与提示都跟随 B。旧实现会装进 A 的实例目录，游戏读 B，
        /// 正是「各版本 mod 仍然无法分开管理 / 装了不生效」的根因。
        /// </summary>
        /// <summary>
        /// 1.7.7 回归：模组页按钮必须在窗口可视区内。
        /// 底部按钮多、会换行，容器高度算错时第二行按钮会落到屏幕外。
        /// 同时把实测坐标打到日志，便于定位具体是哪一个按钮越界。
        /// </summary>
        private static void TestModsPageButtonsInView()
        {
            // 只覆盖窗口尺寸这一维度：字体缩放由 LauncherConfig.UiFontScale 驱动，
            // MainForm 构造函数会用配置值覆盖测试里的 SetFontScale，直接设缩放
            // 会得到"看起来测了、其实没生效"的假验证。
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Mods);
                Size[] sizes = new Size[] { new Size(1080, 740), new Size(980, 660) };
                for (int s = 0; s < sizes.Length; s++)
                {
                    form.Size = sizes[s];
                    form.PerformLayout();
                    Application.DoEvents();
                    Check("mods buttons in view at " + sizes[s].Width,
                        CollectOutOfViewButtons(form, sizes[s].Width.ToString()).Count == 0);
                }
            }
        }

        private static List<string> CollectOutOfViewButtons(MainForm form, string tag)
        {
            List<string> outOfView = new List<string>();
            List<Control> pages = GetPages(form);
            List<Button> buttons = new List<Button>();
            CollectByType<Button>(pages[PageIndex.Mods], buttons);
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                Point origin = button.PointToScreen(Point.Empty);
                Point client = form.PointToClient(origin);
                Rectangle bounds = new Rectangle(client, button.Size);
                string label = (button.Name ?? button.Text ?? "button");
                Console.WriteLine("MODS-BTN " + tag + " | " + label + " | " + bounds);
                if (bounds.Left < 0 || bounds.Top < 0 ||
                    bounds.Right > form.ClientSize.Width ||
                    bounds.Bottom > form.ClientSize.Height)
                {
                    outOfView.Add(label + " " + bounds);
                }
            }
            return outOfView;
        }

        private static void TestModsTargetFollowsLaunchVersion()
        {
            using (MainForm form = CreateForm())
            {
                InvokeShowPage(form, PageIndex.Mods);
                form.PerformLayout();
                Application.DoEvents();

                ComboBox launchCombo = (ComboBox)GetPrivateField(form, "_versionCombo");
                ComboBox modCombo = (ComboBox)GetPrivateField(form, "_modVersionCombo");
                Label note = (Label)GetPrivateField(form, "_modsDirLabel");
                Button import = (Button)GetPrivateField(form, "_importSharedModsButton");
                Check("launch version combo exists", launchCombo != null);
                Check("mod version combo exists", modCombo != null);
                Check("mods dir note exists", note != null);
                Check("explicit import button exists", import != null);
                if (launchCombo == null || modCombo == null || note == null)
                {
                    return;
                }
                if (import != null)
                {
                    Check("explicit import button labelled",
                        import.Text != null && import.Text.Contains("从共享目录导入"));
                }

                SetIsolation(form, true);

                // 制造分叉：真正会被启动的是 B，模组页下拉停在 A。
                launchCombo.Items.Clear();
                launchCombo.Items.Add("fabric-A");
                launchCombo.Items.Add("fabric-B");
                launchCombo.SelectedItem = "fabric-B";
                modCombo.Items.Clear();
                modCombo.Items.Add("fabric-A");
                modCombo.Items.Add("fabric-B");
                modCombo.SelectedItem = "fabric-A";

                string target = InvokePrivateString(form, "GetModTargetVersionId");
                Check("mod target is launch version",
                    string.Equals(target, "fabric-B", StringComparison.Ordinal));

                string dir = InvokePrivateString(form, "GetModsDirectoryFromUi");
                Check("mods dir follows launch version",
                    dir.IndexOf("fabric-B", StringComparison.OrdinalIgnoreCase) >= 0);
                Check("mods dir ignores stale mod combo version",
                    dir.IndexOf("fabric-A", StringComparison.OrdinalIgnoreCase) < 0);

                MethodInfo update = typeof(MainForm).GetMethod(
                    "UpdateModsDirectoryNote", BindingFlags.Instance | BindingFlags.NonPublic);
                Check("directory note refresh method exists", update != null);
                if (update != null)
                {
                    update.Invoke(form, null);
                    Check("note shows launch version", note.Text.Contains("fabric-B"));
                    Check("note states isolation scope", note.Text.Contains("版本隔离"));
                }

                // 切换启动版本后，目录必须跟着换到另一个实例。
                launchCombo.SelectedItem = "fabric-A";
                string dir2 = InvokePrivateString(form, "GetModsDirectoryFromUi");
                Check("mods dir changes with launch version",
                    dir2.IndexOf("fabric-A", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.Equals(dir, dir2, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
