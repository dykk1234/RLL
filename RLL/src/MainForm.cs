using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// RLL 主窗口：左侧导航 + 卡片式内容页。
    /// 所有网络操作都走 Core 中带域名白名单与 SHA1 校验的实现。
    /// </summary>
    public sealed class MainForm : Form
    {
        private const string BrandTitle = "RLL - Rouzhi Lite Launcher";

        // 推荐清单的数据与排序放在 Core（RecommendedMods），便于离线测试断言。
        // 这里只做一层缓存引用，避免每次访问都重建列表。
        private static readonly List<RecommendedMod> ModCatalog = RecommendedMods.GetAll();

        private readonly LauncherConfig _config;
        private readonly List<JavaInfo> _javaList = new List<JavaInfo>();
        private readonly List<NavButton> _navButtons = new List<NavButton>();
        private readonly List<Control> _pages = new List<Control>();
        private readonly List<NoticeBar> _notices = new List<NoticeBar>();
        private VersionManifest _manifest;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _modsCts;
        private bool _busy;
        private bool _updatingUi;
        private bool _modsPageLoaded;
        private Process _gameProcess;
        /// <summary>
        /// 本次启动使用的 Java @argfile 路径。命令行过长时 Java 9+ 走 @argfile，
        /// 但参数文件只能在游戏进程退出后删除：Process.Start 只是创建进程，
        /// Java 进程此刻还没打开该文件，立即删除会导致"找不到参数文件"。
        /// </summary>
        private string _pendingArgFile;

        // 启动页（账号选择）
        private ComboBox _accountCombo;
        private Button _manageAccountsButton;
        private Label _accountStatusLabel;
        /// <summary>启动页新手引导面板（可关闭；步骤完成后自动隐藏）。</summary>
        private GuidePanel _launchGuide;
        /// <summary>用户手动关闭过引导，本次运行内不再自动弹出。</summary>
        private bool _guideDismissed;
        // 账号页
        private ListView _accountList;
        private ComboBox _authServerCombo;
        private TextBox _authServerCustomBox;
        private TextBox _authUserBox;
        private TextBox _authPassBox;
        private Button _authLoginButton;
        private Button _authRegisterButton;
        private Button _authValidateButton;
        private Button _setActiveAccountButton;
        private Button _deleteAccountButton;
        private Button _addOfflineButton;
        private Button _injectorButton;
        private Label _injectorStatusLabel;
        private Label _accountPageStatusLabel;
        private ComboBox _versionCombo;
        private ComboBox _javaCombo;
        private Label _javaRequirementLabel;
        private NumericUpDown _minMemoryBox;
        private NumericUpDown _maxMemoryBox;
        private TextBox _jvmBox;
        private CheckBox _resolutionCheck;
        private NumericUpDown _widthBox;
        private NumericUpDown _heightBox;
        private Button _startButton;
        private Button _detectJavaButton;
        private Button _browseJavaButton;
        private Button _downloadJavaButton;
        private Button _openGameDirButton;
        private Button _saveLaunchButton;

        // 下载页
        private ListView _remoteList;
        private ListView _installedList;
        private CheckBox _snapshotCheck;
        private Label _manifestStatusLabel;
        private Button _refreshManifestButton;
        private Button _installVanillaButton;
        private Button _installFabricButton;
        private Button _refreshInstalledButton;
        private Button _refreshModListButton;
        private Button _openVersionsButton;
        private SplitContainer _versionSplit;

        // 模组页
        private ComboBox _modVersionCombo;
        private ListView _modList;
        private ListView _modSearchList;
        private TextBox _modSearchBox;
        private Button _modSearchButton;
        private Button _installModButton;
        private Button _installSearchModButton;
        private Button _installCommonModsButton;
        private Button _refreshModsButton;
        private Button _openModsButton;
        /// <summary>安装本地 Mod JAR（用户从别处拿到的模组文件）。</summary>
        private Button _installLocalModButton;
        /// <summary>导出模组预设（把当前模组清单分享给别人）。</summary>
        private Button _exportPresetButton;
        /// <summary>导入模组预设（按清单批量安装）。</summary>
        private Button _importPresetButton;
        private Label _modStatusLabel;
        // 已安装模组管理（禁用 / 启用 / 删除）
        private ListView _installedModList;
        private Button _toggleModButton;
        private Button _deleteModButton;

        // 设置页
        private TextBox _gameDirBox;
        private NumericUpDown _threadsBox;
        private CheckBox _settingsSnapshotCheck;
        private CheckBox _darkThemeCheck;
        private Button _saveSettingsButton;
        private Button _openDataButton;
        private Button _openLogButton;

        // 设置页：下载源与版本隔离（1.3.0）
        private ComboBox _mirrorCombo;
        private TextBox _mirrorCustomBox;
        private Label _mirrorHintLabel;
        private CheckBox _isolateCheck;

        // 设置页：异地联机（EasyTier，1.3.0）
        private CheckBox _easyTierEnabledCheck;
        private TextBox _easyTierNameBox;
        private TextBox _easyTierSecretBox;
        private TextBox _easyTierNodeBox;
        private ComboBox _easyTierNodePresetCombo;
        private Button _easyTierNodeDocsButton;
        private TextBox _easyTierBinaryBox;
        private Button _easyTierDownloadButton;
        private Button _easyTierLogButton;
        private Button _easyTierCopyIpButton;
        private Label _easyTierStatusLabel;
        private Label _easyTierIpLabel;
        private TableLayoutPanel _easyTierConfigPanel;

        // 日志 / 关于页
        private TextBox _logBox;
        private Button _clearLogButton;
        private Button _analyzeCrashButton;
        private EmptyState _logEmptyState;
        private TextBox _aboutBox;

        // 状态栏
        private FlatProgressBar _progressBar;
        private Label _statusLabel;
        private Button _cancelButton;

        /// <summary>
        /// 是否跳过 Shown 时的自动刷新。由 internal 构造函数重载注入，仅供界面冒烟测试。
        /// </summary>
        private readonly bool _skipAutoStart;

        public MainForm()
            : this(false)
        {
        }

        /// <summary>
        /// skipAutoStart 跳过 Shown 时的自动刷新（联网获取清单、检测 Java）。
        /// 仅供界面冒烟测试使用，避免生产类里留下仅为测试存在的可写字段。
        /// </summary>
        internal MainForm(bool skipAutoStart)
        {
            _skipAutoStart = skipAutoStart;
            _config = LauncherConfig.Load();
            // 主题必须在 BuildUi 之前确定：UiTheme 的色值在构建控件时被读取。
            UiTheme.SetDark(_config.DarkTheme);
            // 把已保存的认证服务器主机加入 HttpUtil 白名单（用户已明确信任）。
            AuthService.ApplyConfigToHttp(_config);
            BuildUi();
            ApplyConfig();
            Log.Message += OnLogMessage;
            Shown += MainFormShown;
            FormClosing += MainFormClosing;
            UpdateActionButtons();
        }

        // ============================ 界面构建 ============================

        /// <summary>
        /// 取程序图标：exe 内嵌的图标在运行时由 CLR 暴露为程序集图标，
        /// 这里直接复用，避免在源码目录里再放一份 ico（也避免路径依赖）。
        /// </summary>
        internal static Icon LoadApplicationIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = BrandTitle + " " + AppPaths.LauncherVersion;
            Icon = LoadApplicationIcon();
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 740);
            MinimumSize = new Size(980, 660);
            Font = UiTheme.FontRegular(9F);
            BackColor = UiTheme.Background;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 3;
            root.RowCount = 2;
            root.BackColor = UiTheme.Background;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

            root.Controls.Add(BuildSidebar(), 0, 0);
            root.SetRowSpan(root.GetControlFromPosition(0, 0), 2);

            Panel separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = UiTheme.Border;
            root.Controls.Add(separator, 1, 0);
            root.SetRowSpan(separator, 2);

            Panel contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.BackColor = UiTheme.Background;
            contentHost.Padding = new Padding(18, 8, 18, 6);
            BuildLaunchPage(contentHost);
            BuildAccountPage(contentHost);
            BuildDownloadPage(contentHost);
            BuildModsPage(contentHost);
            BuildSettingsPage(contentHost);
            BuildLogPage(contentHost);
            BuildAboutPage(contentHost);
            root.Controls.Add(contentHost, 2, 0);

            root.Controls.Add(BuildStatusBar(), 2, 1);

            Controls.Add(root);
            ShowPage(0);
            ResumeLayout(false);
            PerformLayout();
        }

        private Panel BuildSidebar()
        {
            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Fill;
            sidebar.BackColor = UiTheme.SidebarBackground;

            Label logo = new Label();
            logo.Text = "RLL";
            logo.Font = UiTheme.FontBold(24F);
            logo.ForeColor = UiTheme.Accent;
            logo.AutoSize = true;
            logo.Location = new Point(22, 18);
            logo.BackColor = Color.Transparent;

            Label subtitle = new Label();
            subtitle.Text = "Rouzhi Lite Launcher";
            subtitle.Font = UiTheme.FontRegular(8F);
            subtitle.ForeColor = UiTheme.TextSecondary;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(24, 60);
            subtitle.BackColor = Color.Transparent;

            Label versionLabel = new Label();
            versionLabel.Text = "版本 " + AppPaths.LauncherVersion;
            versionLabel.Font = UiTheme.FontRegular(8.5F);
            versionLabel.ForeColor = UiTheme.TextSecondary;
            versionLabel.AutoSize = true;
            versionLabel.BackColor = Color.Transparent;

            string[] titles = new string[] { "启动", "账号", "下载", "模组", "设置", "日志", "关于" };
            FlowLayoutPanel nav = new FlowLayoutPanel();
            nav.FlowDirection = FlowDirection.TopDown;
            nav.WrapContents = false;
            nav.AutoSize = true;
            nav.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            nav.Location = new Point(4, 96);
            nav.BackColor = Color.Transparent;
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                NavButton button = new NavButton();
                button.Text = titles[i];
                button.Glyph = NavGlyphs.FromIndex(i);
                button.Width = 186;
                button.Margin = new Padding(0, 0, 0, 4);
                button.Click += delegate { ShowPage(index); };
                _navButtons.Add(button);
                nav.Controls.Add(button);
            }

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 52;
            bottom.BackColor = Color.Transparent;
            versionLabel.Location = new Point(22, 16);
            bottom.Controls.Add(versionLabel);

            sidebar.Controls.Add(bottom);
            sidebar.Controls.Add(nav);
            sidebar.Controls.Add(logo);
            sidebar.Controls.Add(subtitle);
            return sidebar;
        }

        private Panel BuildStatusBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = UiTheme.Background;
            bar.Padding = new Padding(6, 2, 6, 6);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 1;
            layout.BackColor = UiTheme.Background;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.ForeColor = UiTheme.TextSecondary;
            _statusLabel.Text = "就绪";
            layout.Controls.Add(_statusLabel, 0, 0);

            _progressBar = new FlatProgressBar();
            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Margin = new Padding(10, 14, 12, 14);
            layout.Controls.Add(_progressBar, 1, 0);

            // 关键修复：取消按钮必须用固定尺寸。CreateSecondaryButton 会开启 AutoSize，
            // 放进 TableLayoutPanel 后会按 PreferredSize 把整行撑到约 100px
            // （状态栏只预留 46px），按钮下半部分被裁掉，表现为"显示异常"。
            _cancelButton = UiTheme.CreateFixedButton("取消", 70, 28);
            _cancelButton.ForeColor = UiTheme.Danger;
            _cancelButton.Enabled = false;
            _cancelButton.Anchor = AnchorStyles.Right;
            _cancelButton.Click += CancelButtonClick;
            layout.Controls.Add(_cancelButton, 2, 0);

            bar.Controls.Add(layout);
            return bar;
        }

        private TableLayoutPanel CreatePage(string title, string caption)
        {
            // 页面骨架由 UiFactory 组装；这里只负责登记提示条，便于后续在对应页上显示消息。
            NoticeBar notice;
            TableLayoutPanel page = UiFactory.CreatePage(title, caption, out notice);
            _notices.Add(notice);
            return page;
        }

        private CardPanel CreateListCard(string title, ListView list)
        {
            return UiFactory.CreateListCard(title, list);
        }

        private void BuildLaunchPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "启动游戏",
                "选好账号、版本和 Java，点「启动游戏」即可。第一次使用请照着下面的步骤走一遍。");

            CardPanel card = UiTheme.CreateCard();
            // AutoSize 让卡片随表单内容变高；Dock=Fill 会固定高度并把启动按钮挤出卡片。
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.MinimumSize = new Size(0, 420);
            card.Margin = new Padding(0);

            TableLayoutPanel grid = new TableLayoutPanel();
            // 不做 Percent 行：表单高度随内容而定，剩余空间交给卡片本身。
            // 用 Percent 会把启动按钮推到网格底部之外（实测按钮被挤出卡片 350px）。
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = UiTheme.CardBackground;
            grid.ColumnCount = 2;
            grid.RowCount = 8;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 8; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            // 第 0 行是新手引导（可关闭，完成后自动隐藏），下面才是启动表单。
            _launchGuide = new GuidePanel();
            _launchGuide.Dock = DockStyle.Fill;
            _launchGuide.Margin = new Padding(0, 0, 0, 10);
            _launchGuide.ActionClicked += LaunchGuideActionClick;
            grid.Controls.Add(_launchGuide, 0, 0);
            grid.SetColumnSpan(_launchGuide, 2);
            grid.Controls.Add(UiTheme.CreateFieldLabel("账号"), 0, 1);
            TableLayoutPanel accountPanel = new TableLayoutPanel();
            accountPanel.Dock = DockStyle.Fill;
            accountPanel.ColumnCount = 3;
            accountPanel.RowCount = 1;
            accountPanel.BackColor = UiTheme.CardBackground;
            accountPanel.Margin = new Padding(0, 0, 0, 6);
            accountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            accountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _accountCombo = new ComboBox();
            _accountCombo.Dock = DockStyle.Fill;
            _accountCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _accountCombo.Margin = new Padding(0, 4, 6, 3);
            UiTheme.StyleCombo(_accountCombo);
            _accountCombo.SelectedIndexChanged += AccountComboChanged;
            accountPanel.Controls.Add(_accountCombo, 0, 0);
            _manageAccountsButton = UiTheme.CreateSecondaryButton("管理账号");
            _manageAccountsButton.Margin = new Padding(0, 3, 6, 3);
            _manageAccountsButton.Click += delegate { ShowPage(1); };
            accountPanel.Controls.Add(_manageAccountsButton, 1, 0);
            _accountStatusLabel = UiTheme.CreateFieldLabel("");
            _accountStatusLabel.AutoEllipsis = true;
            _accountStatusLabel.Margin = new Padding(0, 7, 0, 3);
            _accountStatusLabel.MaximumSize = new Size(190, 0);
            accountPanel.Controls.Add(_accountStatusLabel, 2, 0);
            grid.Controls.Add(accountPanel, 1, 1);

            grid.Controls.Add(UiTheme.CreateFieldLabel("已安装版本"), 0, 2);
            _versionCombo = new ComboBox();
            _versionCombo.Dock = DockStyle.Fill;
            _versionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _versionCombo.Margin = new Padding(0, 4, 3, 8);
            UiTheme.StyleCombo(_versionCombo);
            _versionCombo.SelectedIndexChanged += VersionComboSelectedIndexChanged;
            grid.Controls.Add(_versionCombo, 1, 2);

            _javaRequirementLabel = UiTheme.CreateFieldLabel("Java");
            grid.Controls.Add(_javaRequirementLabel, 0, 3);
            TableLayoutPanel javaPanel = new TableLayoutPanel();
            javaPanel.Dock = DockStyle.Fill;
            javaPanel.ColumnCount = 3;
            javaPanel.RowCount = 1;
            javaPanel.BackColor = UiTheme.CardBackground;
            javaPanel.Margin = new Padding(0, 0, 0, 6);
            javaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            javaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            javaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            javaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _javaCombo = new ComboBox();
            _javaCombo.Dock = DockStyle.Fill;
            _javaCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _javaCombo.Margin = new Padding(0, 4, 6, 3);
            UiTheme.StyleCombo(_javaCombo);
            javaPanel.Controls.Add(_javaCombo, 0, 0);
            _detectJavaButton = UiTheme.CreateSecondaryButton("检测 Java");
            _detectJavaButton.Margin = new Padding(0, 3, 6, 3);
            _detectJavaButton.Click += DetectJavaClick;
            javaPanel.Controls.Add(_detectJavaButton, 1, 0);
            // 没装 Java 时不必让用户自己去官网找：直接一键下载官方 JRE。
            _downloadJavaButton = UiTheme.CreateSecondaryButton("下载 Java");
            _downloadJavaButton.Margin = new Padding(0, 3, 6, 3);
            _downloadJavaButton.Click += DownloadJavaClick;
            javaPanel.Controls.Add(_downloadJavaButton, 2, 0);
            _browseJavaButton = UiTheme.CreateSecondaryButton("浏览...");
            _browseJavaButton.Margin = new Padding(0, 3, 0, 3);
            _browseJavaButton.Click += BrowseJavaClick;
            javaPanel.Controls.Add(_browseJavaButton, 3, 0);
            grid.Controls.Add(javaPanel, 1, 3);

            grid.Controls.Add(UiTheme.CreateFieldLabel("内存（MB）"), 0, 4);
            FlowLayoutPanel memoryPanel = new FlowLayoutPanel();
            memoryPanel.Dock = DockStyle.Fill;
            memoryPanel.AutoSize = true;
            memoryPanel.WrapContents = false;
            memoryPanel.BackColor = UiTheme.CardBackground;
            memoryPanel.Margin = new Padding(0, 2, 0, 6);
            Label minLabel = UiTheme.CreateFieldLabel("最小");
            minLabel.Margin = new Padding(0, 7, 6, 3);
            memoryPanel.Controls.Add(minLabel);
            _minMemoryBox = CreateNumeric(512, 65536, 512, 96);
            memoryPanel.Controls.Add(_minMemoryBox);
            Label maxLabel = UiTheme.CreateFieldLabel("最大");
            maxLabel.Margin = new Padding(16, 7, 6, 3);
            memoryPanel.Controls.Add(maxLabel);
            _maxMemoryBox = CreateNumeric(512, 65536, 512, 96);
            memoryPanel.Controls.Add(_maxMemoryBox);
            grid.Controls.Add(memoryPanel, 1, 4);

            grid.Controls.Add(UiTheme.CreateFieldLabel("窗口分辨率"), 0, 5);
            FlowLayoutPanel resolutionPanel = new FlowLayoutPanel();
            resolutionPanel.Dock = DockStyle.Fill;
            resolutionPanel.AutoSize = true;
            resolutionPanel.WrapContents = false;
            resolutionPanel.BackColor = UiTheme.CardBackground;
            resolutionPanel.Margin = new Padding(0, 2, 0, 6);
            _resolutionCheck = new CheckBox();
            _resolutionCheck.Text = "自定义";
            _resolutionCheck.AutoSize = true;
            _resolutionCheck.ForeColor = UiTheme.TextPrimary;
            _resolutionCheck.Margin = new Padding(0, 8, 12, 3);
            _resolutionCheck.CheckedChanged += ResolutionCheckChanged;
            resolutionPanel.Controls.Add(_resolutionCheck);
            _widthBox = CreateNumeric(320, 7680, 1, 82);
            resolutionPanel.Controls.Add(_widthBox);
            Label timesLabel = UiTheme.CreateFieldLabel("×");
            timesLabel.Margin = new Padding(6, 7, 6, 3);
            resolutionPanel.Controls.Add(timesLabel);
            _heightBox = CreateNumeric(240, 4320, 1, 82);
            resolutionPanel.Controls.Add(_heightBox);
            grid.Controls.Add(resolutionPanel, 1, 5);

            grid.Controls.Add(UiTheme.CreateFieldLabel("JVM 参数"), 0, 6);
            _jvmBox = new TextBox();
            _jvmBox.Dock = DockStyle.Fill;
            _jvmBox.Multiline = true;
            _jvmBox.ScrollBars = ScrollBars.Vertical;
            _jvmBox.Height = 78;
            _jvmBox.Margin = new Padding(0, 4, 3, 6);
            UiTheme.StyleTextBox(_jvmBox);
            grid.Controls.Add(_jvmBox, 1, 6);

            FlowLayoutPanel actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.AutoSize = true;
            actionPanel.WrapContents = false;
            actionPanel.BackColor = UiTheme.CardBackground;
            actionPanel.Margin = new Padding(0, 8, 0, 0);
            _startButton = UiTheme.CreatePrimaryButton("启动游戏");
            _startButton.Width = 168;
            _startButton.Height = 44;
            _startButton.Font = UiTheme.FontBold(11F);
            _startButton.Margin = new Padding(0, 3, 10, 3);
            _startButton.Click += StartGameClick;
            actionPanel.Controls.Add(_startButton);
            _openGameDirButton = UiTheme.CreateSecondaryButton("打开游戏目录");
            _openGameDirButton.Height = 36;
            _openGameDirButton.Margin = new Padding(0, 7, 10, 3);
            _openGameDirButton.Click += OpenGameDirClick;
            actionPanel.Controls.Add(_openGameDirButton);
            _saveLaunchButton = UiTheme.CreateSecondaryButton("保存设置");
            _saveLaunchButton.Height = 36;
            _saveLaunchButton.Margin = new Padding(0, 7, 3, 3);
            _saveLaunchButton.Click += SaveSettingsClick;
            actionPanel.Controls.Add(_saveLaunchButton);
            grid.Controls.Add(actionPanel, 1, 7);

            card.Controls.Add(grid);

            // 启动页表单加上新手引导后总高度可能超过一屏，因此与设置页一样
            // 放进可滚动容器：内容变高时可滚动，而不是把按钮挤出可视区。
            Panel launchScroll = new Panel();
            launchScroll.Dock = DockStyle.Fill;
            launchScroll.AutoScroll = true;
            launchScroll.BackColor = UiTheme.Background;
            launchScroll.Margin = new Padding(0);
            TableLayoutPanel launchContent = new TableLayoutPanel();
            launchContent.Location = new Point(0, 0);
            launchContent.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            launchContent.AutoSize = true;
            launchContent.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            launchContent.BackColor = UiTheme.Background;
            launchContent.ColumnCount = 1;
            launchContent.RowCount = 1;
            launchContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            launchContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            launchContent.Padding = new Padding(0, 0, 0, 12);
            launchContent.Margin = new Padding(0);
            card.Dock = DockStyle.Top;
            launchContent.Controls.Add(card, 0, 0);
            launchScroll.Controls.Add(launchContent);
            page.Controls.Add(launchScroll, 0, 2);

            Label note = UiTheme.CreateCaptionLabel(
                "提示：首次使用请先在「下载」页安装原版或 Fabric；在「账号」页可添加离线账号或第三方认证账号（联机）。游戏目录默认为本程序所在目录下的 .minecraft。");
            note.Margin = new Padding(4, 8, 0, 2);
            page.Controls.Add(note, 0, 3);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        private void BuildAccountPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "账号",
                "单机或和朋友联机，用离线账号就行；要进需要正版验证的服务器，才需要在这里登录第三方认证账号。登录密码不会保存到你的电脑上。");

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.BackColor = UiTheme.Background;
            content.ColumnCount = 1;
            content.RowCount = 4;
            // 顺序：登录卡片在最上面（用户最常做的事），其后是账号列表与操作。
            // 旧布局把"账号列表"放在 Percent 100% 的首行，会把登录区挤到窗口之外
            // （实测登录按钮 Y=796，窗口高 760），用户只看到离线账号入口，
            // 以为"没有第三方登录"。
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 账号页内容（登录 + 列表 + 注入器）在较小窗口下会超过一屏，
            // 放进可滚动容器，保证底部控件（登录/注入器）始终可访问。
            Panel accountScroll = new Panel();
            accountScroll.Dock = DockStyle.Fill;
            accountScroll.AutoScroll = true;
            accountScroll.BackColor = UiTheme.Background;
            accountScroll.Margin = new Padding(0);
            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            content.Padding = new Padding(0, 0, 0, 8);
            accountScroll.Controls.Add(content);
            page.Controls.Add(accountScroll, 0, 2);

            // ---------- 账号列表 ----------
            CardPanel listCard = UiTheme.CreateCard();
            // 在 AutoSize 的滚动内容里，Dock=Fill 的卡片会塌成 0 高，
            // 必须给固定最小高度，让账号列表有稳定的可点区域。
            listCard.Dock = DockStyle.Fill;
            listCard.MinimumSize = new Size(0, 210);
            listCard.Margin = new Padding(0, 0, 0, 10);
            TableLayoutPanel listLayout = new TableLayoutPanel();
            listLayout.Dock = DockStyle.Fill;
            listLayout.BackColor = UiTheme.CardBackground;
            listLayout.ColumnCount = 1;
            listLayout.RowCount = 2;
            listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            listLayout.Controls.Add(UiTheme.CreateSectionLabel("已保存的账号"), 0, 0);
            _accountList = new ListView();
            _accountList.Dock = DockStyle.Fill;
            _accountList.View = View.Details;
            _accountList.FullRowSelect = true;
            _accountList.HideSelection = false;
            _accountList.MultiSelect = false;
            _accountList.Columns.Add("名称", 170);
            _accountList.Columns.Add("类型", 110);
            _accountList.Columns.Add("来源 / 服务器", 250);
            _accountList.Columns.Add("状态", 140);
            _accountList.DoubleClick += delegate { SetActiveAccountClick(null, EventArgs.Empty); };
            listLayout.Controls.Add(_accountList, 0, 1);
            listCard.Controls.Add(listLayout);
            content.Controls.Add(listCard, 0, 1);

            // ---------- 列表操作 ----------
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.AutoSize = true;
            actions.WrapContents = false;
            actions.BackColor = UiTheme.Background;
            actions.Margin = new Padding(0, 0, 0, 10);
            _setActiveAccountButton = UiTheme.CreateSecondaryButton("设为当前账号");
            _setActiveAccountButton.Height = 34;
            _setActiveAccountButton.Margin = new Padding(0, 3, 8, 3);
            _setActiveAccountButton.Click += SetActiveAccountClick;
            actions.Controls.Add(_setActiveAccountButton);
            _authValidateButton = UiTheme.CreateSecondaryButton("校验令牌");
            _authValidateButton.Height = 34;
            _authValidateButton.Margin = new Padding(0, 3, 8, 3);
            _authValidateButton.Click += ValidateTokenClick;
            actions.Controls.Add(_authValidateButton);
            _deleteAccountButton = UiTheme.CreateSecondaryButton("删除账号");
            _deleteAccountButton.Height = 34;
            _deleteAccountButton.ForeColor = UiTheme.Danger;
            _deleteAccountButton.Margin = new Padding(0, 3, 8, 3);
            _deleteAccountButton.Click += DeleteAccountClick;
            actions.Controls.Add(_deleteAccountButton);
            _addOfflineButton = UiTheme.CreateSecondaryButton("添加离线账号");
            _addOfflineButton.Height = 34;
            _addOfflineButton.Margin = new Padding(0, 3, 8, 3);
            _addOfflineButton.Click += AddOfflineAccountClick;
            actions.Controls.Add(_addOfflineButton);
            content.Controls.Add(actions, 0, 2);

            // ---------- 第三方认证登录 ----------
            CardPanel loginCard = UiTheme.CreateCard();
            // 登录卡片必须给定高度：Dock=Fill 在 AutoSize 的滚动容器里会被压成
            // 剩余高度（实测仅 60px），而它内含 4 行控件需要约 160px，
            // 结果"账号/密码"整行连同登录、注册按钮被裁掉——用户只看到
            // "认证服务器"下拉，以为没有登录入口。
            loginCard.Dock = DockStyle.Top;
            loginCard.Height = 190;
            loginCard.MinimumSize = new Size(0, 190);
            loginCard.Margin = new Padding(0, 0, 0, 10);
            TableLayoutPanel loginLayout = new TableLayoutPanel();
            loginLayout.Dock = DockStyle.Fill;
            loginLayout.BackColor = UiTheme.CardBackground;
            loginLayout.ColumnCount = 4;
            loginLayout.RowCount = 4;
            loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; i++)
            {
                loginLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            loginLayout.Controls.Add(UiTheme.CreateSectionLabel("添加第三方认证账号（联机）"), 0, 0);
            loginLayout.SetColumnSpan(loginLayout.GetControlFromPosition(0, 0), 4);

            loginLayout.Controls.Add(UiTheme.CreateFieldLabel("认证服务器"), 0, 1);
            _authServerCombo = new ComboBox();
            _authServerCombo.Dock = DockStyle.Fill;
            _authServerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _authServerCombo.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleCombo(_authServerCombo);
            loginLayout.Controls.Add(_authServerCombo, 1, 1);
            loginLayout.Controls.Add(UiTheme.CreateFieldLabel("或自定义地址"), 0, 2);
            _authServerCustomBox = new TextBox();
            _authServerCustomBox.Dock = DockStyle.Fill;
            _authServerCustomBox.Margin = new Padding(0, 4, 8, 3);
            _authServerCustomBox.Text = "https://";
            UiTheme.StyleTextBox(_authServerCustomBox);
            loginLayout.Controls.Add(_authServerCustomBox, 1, 2);
            _accountPageStatusLabel = UiTheme.CreateFieldLabel("");
            _accountPageStatusLabel.AutoEllipsis = true;
            _accountPageStatusLabel.Margin = new Padding(0, 7, 0, 3);
            loginLayout.Controls.Add(_accountPageStatusLabel, 3, 2);

            loginLayout.Controls.Add(UiTheme.CreateFieldLabel("账号 / 密码"), 0, 3);
            FlowLayoutPanel credentialPanel = new FlowLayoutPanel();
            credentialPanel.Dock = DockStyle.Fill;
            credentialPanel.AutoSize = true;
            credentialPanel.WrapContents = false;
            credentialPanel.BackColor = UiTheme.CardBackground;
            _authUserBox = new TextBox();
            _authUserBox.Width = 160;
            _authUserBox.Margin = new Padding(0, 4, 8, 3);
            _authUserBox.MaxLength = 64;
            UiTheme.StyleTextBox(_authUserBox);
            credentialPanel.Controls.Add(_authUserBox);
            _authPassBox = new TextBox();
            _authPassBox.Width = 160;
            _authPassBox.Margin = new Padding(0, 4, 8, 3);
            _authPassBox.UseSystemPasswordChar = true;
            _authPassBox.MaxLength = 128;
            UiTheme.StyleTextBox(_authPassBox);
            credentialPanel.Controls.Add(_authPassBox);
            _authLoginButton = UiTheme.CreatePrimaryButton("登录");
            _authLoginButton.Height = 34;
            _authLoginButton.Margin = new Padding(0, 3, 8, 3);
            _authLoginButton.Click += LoginAuthClick;
            credentialPanel.Controls.Add(_authLoginButton);
            // 启动器不代替用户注册（注册需要邮箱验证等网页流程），
            // 但必须给出入口：之前只提供"登录"，没有账号的用户无从下手。
            _authRegisterButton = UiTheme.CreateSecondaryButton("注册账号");
            _authRegisterButton.Height = 34;
            _authRegisterButton.Margin = new Padding(0, 3, 3, 3);
            _authRegisterButton.Click += OpenRegisterPageClick;
            credentialPanel.Controls.Add(_authRegisterButton);
            loginLayout.Controls.Add(credentialPanel, 1, 3);
            loginLayout.SetColumnSpan(credentialPanel, 3);
            loginCard.Controls.Add(loginLayout);
            content.Controls.Add(loginCard, 0, 0);

            // ---------- authlib-injector ----------
            FlowLayoutPanel injectorPanel = new FlowLayoutPanel();
            injectorPanel.Dock = DockStyle.Fill;
            injectorPanel.AutoSize = true;
            injectorPanel.WrapContents = false;
            injectorPanel.BackColor = UiTheme.Background;
            _injectorStatusLabel = UiTheme.CreateFieldLabel("");
            _injectorStatusLabel.AutoEllipsis = true;
            _injectorStatusLabel.Margin = new Padding(0, 7, 10, 3);
            injectorPanel.Controls.Add(_injectorStatusLabel);
            _injectorButton = UiTheme.CreateSecondaryButton("下载注入器");
            _injectorButton.Height = 32;
            _injectorButton.Margin = new Padding(0, 3, 8, 3);
            _injectorButton.Click += DownloadInjectorClick;
            injectorPanel.Controls.Add(_injectorButton);
            Button browseInjectorButton = UiTheme.CreateSecondaryButton("选择本地 JAR...");
            browseInjectorButton.Height = 32;
            browseInjectorButton.Margin = new Padding(0, 3, 3, 3);
            browseInjectorButton.Click += BrowseInjectorClick;
            injectorPanel.Controls.Add(browseInjectorButton);
            content.Controls.Add(injectorPanel, 0, 3);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        // ============================ 账号页逻辑 ============================

        private void RefreshAccountList()
        {
            if (_accountList == null || _accountList.IsDisposed)
            {
                return;
            }
            _accountList.BeginUpdate();
            try
            {
                _accountList.Items.Clear();
                List<Account> accounts = AuthService.GetAccounts(_config);
                for (int i = 0; i < accounts.Count; i++)
                {
                    Account account = accounts[i];
                    ListViewItem item = new ListViewItem(account.DisplayName ?? account.UserName);
                    item.SubItems.Add(account.Type == AccountType.Authlib ? "第三方认证" : "离线");
                    if (account.Type == AccountType.Authlib)
                    {
                        item.SubItems.Add(account.AuthServerUrl ?? "");
                    }
                    else
                    {
                        item.SubItems.Add("本机离线");
                    }
                    if (string.Equals(account.Id, _config.SelectedAccountId, StringComparison.Ordinal))
                    {
                        item.SubItems.Add("当前使用");
                        item.Font = UiTheme.FontBold(9F);
                    }
                    else
                    {
                        item.SubItems.Add("");
                    }
                    item.Tag = account;
                    _accountList.Items.Add(item);
                }
            }
            finally
            {
                _accountList.EndUpdate();
            }
            UpdateInjectorStatus();
        }

        private void UpdateInjectorStatus()
        {
            if (_injectorStatusLabel == null || _injectorStatusLabel.IsDisposed)
            {
                return;
            }
            string path = AuthlibInjectorService.ResolveInjectorPath(_config.AuthlibInjectorPath);
            if (AuthlibInjectorService.Exists(_config.AuthlibInjectorPath))
            {
                _injectorStatusLabel.Text = "注入器已就绪：" + path;
                _injectorStatusLabel.ForeColor = UiTheme.Success;
            }
            else
            {
                _injectorStatusLabel.Text = "注入器缺失：" + path + "（登录第三方账号并启动时自动下载，也可手动获取）";
                _injectorStatusLabel.ForeColor = UiTheme.Warning;
            }
        }

        private Account GetSelectedAccountFromList()
        {
            if (_accountList == null || _accountList.SelectedItems.Count == 0)
            {
                return null;
            }
            return _accountList.SelectedItems[0].Tag as Account;
        }

        private void SetActiveAccountClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            Account account = GetSelectedAccountFromList();
            if (account == null)
            {
                MessageBox.Show("请先在上方列表中选择一个账号。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _config.SelectedAccountId = account.Id;
            _config.LastUsername = account.Type == AccountType.Authlib
                ? account.DisplayName
                : account.UserName;
            SaveConfigSafely();
            RefreshAccountList();
            RefreshAccountCombo();
            SetStatus("当前账号已切换为：" + account.GetDisplay());
        }

        private void DeleteAccountClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            Account account = GetSelectedAccountFromList();
            if (account == null)
            {
                MessageBox.Show("请先在上方列表中选择一个账号。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult confirm = MessageBox.Show(
                "确定删除账号：" + account.GetDisplay() + "？\r\n（第三方账号的会话令牌将一并删除）",
                BrandTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
            AuthService.DeleteAccount(_config, account.Id);
            SaveConfigSafely();
            RefreshAccountList();
            RefreshAccountCombo();
            SetStatus("账号已删除");
        }

        private void AddOfflineAccountClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            PromptDialog dialog = new PromptDialog(
                "添加离线账号", "输入 3-16 位字母、数字或下划线的用户名：", "Player");
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            string username = (dialog.Value ?? "").Trim();
            if (!CommandLine.IsValidUsername(username))
            {
                MessageBox.Show("离线用户名必须是 3-16 位字母、数字或下划线。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Account account = AuthService.CreateOffline(username);
            AuthService.AddOrUpdateAccount(_config, account);
            _config.SelectedAccountId = account.Id;
            _config.LastUsername = username;
            SaveConfigSafely();
            RefreshAccountList();
            RefreshAccountCombo();
            SetStatus("离线账号已添加：" + username);
        }

        /// <summary>
        /// 打开认证服务器的注册页面。
        /// 注册涉及邮箱验证、人机校验与站点条款，不适合（也无法）由启动器代为完成；
        /// 启动器的职责是把用户送到正确的地方，注册完回来即可直接登录。
        /// </summary>
        private void OpenRegisterPageClick(object sender, EventArgs e)
        {
            string serverUrl = GetSelectedAuthServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show(
                    "请先选择或填写认证服务器地址。\r\n\r\n" +
                    "内置预设已包含 LittleSkin，选中后会跳到它的注册页。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string registerUrl = AuthServerRegistry.GetRegisterUrl(serverUrl);
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                // 用系统默认浏览器打开：绝对路径调用，避免 PATH 劫持。
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                startInfo.Arguments = CommandLine.QuoteArgument(registerUrl);
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
                SetStatus("已在浏览器打开注册页：" + AuthValidation.GetHost(serverUrl));
                ShowNoticeOnPage(1, NoticeBar.NoticeLevel.Info,
                    "已打开注册页面。注册完成后再回到这里，用同一组账号密码点「登录」即可。");
            }
            catch (Exception ex)
            {
                Log.Error("打开注册页失败：" + registerUrl, ex);
                ShowCopyableText("请手动访问注册页", registerUrl +
                    "\r\n\r\n（自动打开浏览器失败：" + ex.Message + "）");
            }
        }

        /// <summary>取当前选择的认证服务器地址（自定义优先，其次下拉预设）。</summary>
        private string GetSelectedAuthServerUrl()
        {
            string custom = (_authServerCustomBox.Text ?? "").Trim();
            if (custom.Length > 0 && custom != "https://")
            {
                return AuthValidation.NormalizeAuthServerUrl(custom);
            }
            AuthServerPreset preset = _authServerCombo.SelectedItem as AuthServerPreset;
            return preset == null ? "" : preset.Url;
        }

        private async void LoginAuthClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            string serverUrl = (_authServerCustomBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(serverUrl) || serverUrl == "https://")
            {
                AuthServerPreset preset = _authServerCombo.SelectedItem as AuthServerPreset;
                if (preset != null)
                {
                    serverUrl = preset.Url;
                }
            }
            string username = (_authUserBox.Text ?? "").Trim();
            string password = _authPassBox.Text ?? "";
            if (string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show("请选择或输入认证服务器地址（https://...）。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(username) || password.Length == 0)
            {
                MessageBox.Show("请输入认证服务器的账号与密码。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!AuthValidation.IsValidAuthServerUrl(AuthValidation.NormalizeAuthServerUrl(serverUrl)))
            {
                MessageBox.Show("认证服务器地址无效：必须是 https:// 开头的合法地址（443 端口、域名）。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CancellationToken token = BeginOperation("正在登录认证服务器...");
            Account account = null;
            try
            {
                SetStatus("正在登录认证服务器...");
                // 记录实际使用的地址与账号名（不记录密码），便于用户反馈问题时定位。
                Log.Info("第三方登录请求：服务器 " + serverUrl + "，账号 " + username);
                account = await AuthService.LoginAuthAsync(
                    _config, serverUrl, username, password, token);
                token.ThrowIfCancellationRequested();
                if (IsDisposed || Disposing)
                {
                    return;
                }
                account.LastUsedUnix = System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerSecond;
                AuthService.AddOrUpdateAccount(_config, account);
                _config.SelectedAccountId = account.Id;
                _config.LastUsername = account.DisplayName;
                SaveConfigSafely();
                // 记住该服务器为预设，方便下次直接选择。
                SaveAuthServerPreset(serverUrl);
                _authPassBox.Text = "";
                RefreshAccountList();
                RefreshAccountCombo();
                SetStatus("登录成功：" + account.GetDisplay());
                _accountPageStatusLabel.Text = "登录成功：" + account.GetDisplay();
                _accountPageStatusLabel.ForeColor = UiTheme.Success;
            }
            catch (OperationCanceledException)
            {
                SetStatus("登录已取消");
            }
            catch (Exception ex)
            {
                Log.Error("第三方登录失败", ex);
                SetStatus("登录失败");
                _accountPageStatusLabel.Text = "登录失败：" + ex.Message;
                _accountPageStatusLabel.ForeColor = UiTheme.Danger;
                MessageBox.Show("登录失败：\r\n" + ex.Message, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void ValidateTokenClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            Account account = GetSelectedAccountFromList();
            if (account == null)
            {
                MessageBox.Show("请先在上方列表中选择一个账号。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (account.Type != AccountType.Authlib)
            {
                MessageBox.Show("离线账号没有会话令牌需要校验。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CancellationToken token = BeginOperation("正在校验会话令牌...");
            try
            {
                // 校验请求同样要过白名单：账号主机可能来自上次运行保存的配置，
                // 本次启动还没加入运行时白名单，否则会报"不允许的请求地址"。
                HttpUtil.AddAllowedHost(AuthValidation.GetHost(account.AuthServerUrl));
                bool valid = await YggdrasilClient.ValidateAsync(
                    account.AuthServerUrl, account.AccessToken, token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                if (valid)
                {
                    SetStatus("令牌有效：" + account.GetDisplay());
                    MessageBox.Show("会话令牌仍然有效，无需重新登录。", BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus("令牌已失效，请重新登录：" + account.GetDisplay());
                    MessageBox.Show(
                        "会话令牌已失效。\r\n请在认证服务器地址与账号密码处重新登录该账号。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("校验已取消");
            }
            catch (Exception ex)
            {
                Log.Error("校验令牌失败", ex);
                MessageBox.Show("校验令牌失败：\r\n" + ex.Message, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void DownloadInjectorClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (AuthlibInjectorService.Exists(_config.AuthlibInjectorPath))
            {
                MessageBox.Show("注入器已存在。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CancellationToken token = BeginOperation("正在下载 authlib-injector...");
            try
            {
                await AuthlibInjectorService.EnsureInjectorAsync(
                    _config.AuthlibInjectorPath, null, token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                UpdateInjectorStatus();
                SetStatus("authlib-injector 下载完成");
            }
            catch (OperationCanceledException)
            {
                SetStatus("下载已取消");
            }
            catch (Exception ex)
            {
                Log.Error("下载 authlib-injector 失败", ex);
                MessageBox.Show("下载 authlib-injector 失败：\r\n" + ex.Message +
                    "\r\n\r\n也可以点击「选择本地 JAR...」使用已核验的文件。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
        }

        private void BrowseInjectorClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 authlib-injector JAR";
                dialog.Filter = "JAR 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        FileInfo info = new FileInfo(dialog.FileName);
                        if (!info.Exists || info.Length <= 0 ||
                            info.Length > AuthlibInjectorService.MaxInjectorBytes)
                        {
                            MessageBox.Show("所选文件无效或超过大小上限（32MB）。", BrandTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    catch
                    {
                        return;
                    }
                    _config.AuthlibInjectorPath = dialog.FileName;
                    SaveConfigSafely();
                    UpdateInjectorStatus();
                    SetStatus("已选择注入器：" + dialog.FileName);
                }
            }
        }

        private void SaveAuthServerPreset(string serverUrl)
        {
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (!AuthValidation.IsValidAuthServerUrl(normalized))
            {
                return;
            }
            if (_config.AuthServers == null)
            {
                _config.AuthServers = new List<AuthServerPreset>();
            }
            for (int i = 0; i < _config.AuthServers.Count; i++)
            {
                if (string.Equals(_config.AuthServers[i].Url, normalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            _config.AuthServers.Add(new AuthServerPreset(
                AuthValidation.GetHost(normalized), normalized));
            SaveConfigSafely();
            RefreshAuthServerCombo();
        }

        private void RefreshAuthServerCombo()
        {
            if (_authServerCombo == null || _authServerCombo.IsDisposed)
            {
                return;
            }
            string previous = "";
            AuthServerPreset previousPreset = _authServerCombo.SelectedItem as AuthServerPreset;
            if (previousPreset != null)
            {
                previous = previousPreset.Url;
            }

            _authServerCombo.Items.Clear();
            if (_config.AuthServers == null)
            {
                _config.AuthServers = new List<AuthServerPreset>();
            }
            // 去重集合：内置预设始终保留，用户保存的预设排在其后。
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 内置便捷入口：必须始终显示，不能只在"配置里没有预设"时才出现。
            // 否则用户一旦保存过任意预设（甚至只是在别处登录过一次），
            // LittleSkin 入口就消失，新用户会以为"启动器不支持该站点"。
            // 这些条目不写入配置，是否信任由用户登录时决定。
            // 地址统一取自 BuiltinAuthServerPresets，避免与白名单放行处各写一份。
            List<AuthServerPreset> builtin = BuiltinAuthServerPresets.GetAll();
            for (int i = 0; i < builtin.Count; i++)
            {
                if (seen.Add(builtin[i].Url))
                {
                    _authServerCombo.Items.Add(builtin[i]);
                }
            }
            for (int i = 0; i < _config.AuthServers.Count; i++)
            {
                AuthServerPreset preset = _config.AuthServers[i];
                if (preset != null && seen.Add(preset.Url))
                {
                    _authServerCombo.Items.Add(preset);
                }
            }

            // 尽量恢复用户之前的选择，否则默认选第一个（内置 LittleSkin）。
            int restore = -1;
            if (!string.IsNullOrEmpty(previous))
            {
                for (int i = 0; i < _authServerCombo.Items.Count; i++)
                {
                    AuthServerPreset item = _authServerCombo.Items[i] as AuthServerPreset;
                    if (item != null &&
                        string.Equals(item.Url, previous, StringComparison.OrdinalIgnoreCase))
                    {
                        restore = i;
                        break;
                    }
                }
            }
            if (_authServerCombo.Items.Count > 0)
            {
                _authServerCombo.SelectedIndex = restore >= 0 ? restore : 0;
            }
        }

        private void SaveConfigSafely()
        {
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("保存配置失败：" + ex.Message);
            }
        }

        private void BuildDownloadPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "下载与安装",
                "先装原版游戏，再装 Fabric（想用模组就装 Fabric）。下载的文件会逐个校验，损坏会自动重下。不知道选哪个？先装最新的正式版原版即可。");

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = UiTheme.Background;
            body.ColumnCount = 1;
            body.RowCount = 2;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = false;
            toolbar.BackColor = UiTheme.Background;
            toolbar.Margin = new Padding(0, 0, 0, 8);
            _refreshManifestButton = UiTheme.CreateSecondaryButton("刷新版本清单");
            _refreshManifestButton.Click += RefreshManifestClick;
            toolbar.Controls.Add(_refreshManifestButton);
            _snapshotCheck = new CheckBox();
            _snapshotCheck.Text = "显示快照";
            _snapshotCheck.AutoSize = true;
            _snapshotCheck.ForeColor = UiTheme.TextPrimary;
            _snapshotCheck.Margin = new Padding(4, 9, 12, 3);
            _snapshotCheck.CheckedChanged += SnapshotCheckChanged;
            toolbar.Controls.Add(_snapshotCheck);
            _manifestStatusLabel = UiTheme.CreateCaptionLabel("尚未获取版本清单");
            _manifestStatusLabel.Margin = new Padding(4, 11, 3, 3);
            toolbar.Controls.Add(_manifestStatusLabel);
            body.Controls.Add(toolbar, 0, 0);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.Panel1.Padding = new Padding(0, 0, 6, 0);
            split.Panel2.Padding = new Padding(6, 0, 0, 0);
            _versionSplit = split;

            _remoteList = new ListView();
            _remoteList.Dock = DockStyle.Fill;
            _remoteList.View = View.Details;
            _remoteList.FullRowSelect = true;
            _remoteList.MultiSelect = false;
            _remoteList.HideSelection = false;
            _remoteList.GridLines = false;
            _remoteList.BorderStyle = BorderStyle.None;
            _remoteList.Font = UiTheme.FontRegular(9F);
            _remoteList.Columns.Add("版本", 190);
            _remoteList.Columns.Add("类型", 70);
            _remoteList.Columns.Add("发布时间", 120);
            _remoteList.SelectedIndexChanged += RemoteListSelectedIndexChanged;
            split.Panel1.Controls.Add(CreateListCard("Mojang 可安装版本", _remoteList));

            _installedList = new ListView();
            _installedList.Dock = DockStyle.Fill;
            _installedList.View = View.Details;
            _installedList.FullRowSelect = true;
            _installedList.MultiSelect = false;
            _installedList.HideSelection = false;
            _installedList.GridLines = false;
            _installedList.BorderStyle = BorderStyle.None;
            _installedList.Font = UiTheme.FontRegular(9F);
            _installedList.Columns.Add("已安装版本", 260);
            _installedList.DoubleClick += InstalledListDoubleClick;
            split.Panel2.Controls.Add(CreateListCard("本地已安装版本", _installedList));

            body.Controls.Add(split, 0, 1);
            page.Controls.Add(body, 0, 2);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.WrapContents = false;
            footer.BackColor = UiTheme.Background;
            footer.Margin = new Padding(0, 10, 0, 0);
            _installVanillaButton = UiTheme.CreatePrimaryButton("安装原版");
            _installVanillaButton.Width = 128;
            _installVanillaButton.Click += InstallVanillaClick;
            footer.Controls.Add(_installVanillaButton);
            _installFabricButton = UiTheme.CreatePrimaryButton("安装 Fabric");
            _installFabricButton.Width = 150;
            _installFabricButton.Margin = new Padding(0, 3, 10, 3);
            _installFabricButton.Click += InstallFabricClick;
            footer.Controls.Add(_installFabricButton);
            _refreshInstalledButton = UiTheme.CreateSecondaryButton("刷新已安装");
            _refreshInstalledButton.Margin = new Padding(0, 6, 10, 3);
            _refreshInstalledButton.Click += RefreshInstalledClick;
            footer.Controls.Add(_refreshInstalledButton);
            _openVersionsButton = UiTheme.CreateSecondaryButton("打开 versions 目录");
            _openVersionsButton.Margin = new Padding(0, 6, 3, 3);
            _openVersionsButton.Click += OpenVersionsClick;
            footer.Controls.Add(_openVersionsButton);
            page.Controls.Add(footer, 0, 3);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        private void BuildModsPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "模组下载",
                "选一个已安装的 Fabric 版本，就能装模组。常用模组可一键安装，前置会自动补齐。必须使用 Fabric 版本，纯原版不能装模组。想用离线账号联机，请安装第一个「更高级联机设置」——它给「对局域网开放」加上「正版验证：禁用」开关。");

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = UiTheme.Background;
            body.ColumnCount = 1;
            body.RowCount = 3;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel versionRow = new FlowLayoutPanel();
            versionRow.Dock = DockStyle.Fill;
            versionRow.AutoSize = true;
            versionRow.WrapContents = false;
            versionRow.BackColor = UiTheme.Background;
            versionRow.Margin = new Padding(0, 0, 0, 8);
            Label versionCaption = UiTheme.CreateCaptionLabel("目标 Fabric 版本");
            versionCaption.Margin = new Padding(0, 8, 8, 3);
            versionRow.Controls.Add(versionCaption);
            _modVersionCombo = new ComboBox();
            _modVersionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _modVersionCombo.Width = 330;
            _modVersionCombo.Margin = new Padding(0, 4, 10, 3);
            UiTheme.StyleCombo(_modVersionCombo);
            _modVersionCombo.SelectedIndexChanged += ModVersionComboChanged;
            versionRow.Controls.Add(_modVersionCombo);
            _refreshModsButton = UiTheme.CreateSecondaryButton("刷新状态");
            _refreshModsButton.Margin = new Padding(0, 4, 10, 3);
            _refreshModsButton.Click += RefreshModsClick;
            versionRow.Controls.Add(_refreshModsButton);
            _modStatusLabel = UiTheme.CreateCaptionLabel("");
            _modStatusLabel.Margin = new Padding(2, 9, 3, 3);
            versionRow.Controls.Add(_modStatusLabel);
            body.Controls.Add(versionRow, 0, 0);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.Panel1.Padding = new Padding(0, 0, 6, 0);
            split.Panel2.Padding = new Padding(6, 0, 0, 0);

            // 左侧改成上下两段：上=可安装的推荐模组，下=已安装模组（可禁用/启用/删除）。
            // 合并成一个列表会让"能装的"和"已装的"混在一起，用户分不清该点哪个。
            TableLayoutPanel leftPane = new TableLayoutPanel();
            leftPane.Dock = DockStyle.Fill;
            leftPane.BackColor = UiTheme.Background;
            leftPane.ColumnCount = 1;
            leftPane.RowCount = 2;
            leftPane.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            leftPane.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            leftPane.Margin = new Padding(0);

            _modList = new ListView();
            _modList.Dock = DockStyle.Fill;
            _modList.View = View.Details;
            _modList.FullRowSelect = true;
            _modList.MultiSelect = false;
            _modList.HideSelection = false;
            _modList.GridLines = false;
            _modList.BorderStyle = BorderStyle.None;
            _modList.Font = UiTheme.FontRegular(9F);
            _modList.Columns.Add("模组", 190);
            _modList.Columns.Add("说明", 220);
            _modList.Columns.Add("状态", 190);
            _modList.SelectedIndexChanged += ModListSelectedIndexChanged;
            _modList.DoubleClick += InstallSelectedModClick;
            for (int i = 0; i < ModCatalog.Count; i++)
            {
                ListViewItem item = new ListViewItem(ModCatalog[i].Title);
                item.SubItems.Add(ModCatalog[i].Description);
                item.SubItems.Add("—");
                item.Tag = i;
                _modList.Items.Add(item);
            }
            split.Panel1.Controls.Add(leftPane);

            // ---------- 已安装模组管理 ----------
            CardPanel manageCard = UiTheme.CreateCard();
            manageCard.Dock = DockStyle.Fill;
            manageCard.Margin = new Padding(0, 8, 0, 0);
            TableLayoutPanel manageLayout = new TableLayoutPanel();
            manageLayout.Dock = DockStyle.Fill;
            manageLayout.BackColor = UiTheme.CardBackground;
            manageLayout.ColumnCount = 1;
            manageLayout.RowCount = 3;
            manageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            manageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            manageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            manageLayout.Controls.Add(UiTheme.CreateSectionLabel("已安装模组"), 0, 0);

            _installedModList = new ListView();
            _installedModList.Dock = DockStyle.Fill;
            _installedModList.View = View.Details;
            _installedModList.FullRowSelect = true;
            _installedModList.MultiSelect = true;
            _installedModList.HideSelection = false;
            _installedModList.GridLines = false;
            _installedModList.BorderStyle = BorderStyle.None;
            _installedModList.Font = UiTheme.FontRegular(9F);
            _installedModList.Columns.Add("模组", 170);
            _installedModList.Columns.Add("版本", 80);
            _installedModList.Columns.Add("状态", 80);
            _installedModList.SelectedIndexChanged += InstalledModSelectedChanged;
            manageLayout.Controls.Add(_installedModList, 0, 1);

            FlowLayoutPanel manageButtons = new FlowLayoutPanel();
            manageButtons.Dock = DockStyle.Fill;
            manageButtons.AutoSize = true;
            manageButtons.WrapContents = false;
            manageButtons.BackColor = UiTheme.CardBackground;
            manageButtons.Margin = new Padding(0, 6, 0, 0);
            _toggleModButton = UiTheme.CreateSecondaryButton("禁用 / 启用");
            _toggleModButton.Width = 110;
            _toggleModButton.Margin = new Padding(0, 3, 8, 3);
            _toggleModButton.Click += ToggleModClick;
            manageButtons.Controls.Add(_toggleModButton);
            _deleteModButton = UiTheme.CreateSecondaryButton("删除");
            _deleteModButton.Width = 80;
            _deleteModButton.Margin = new Padding(0, 3, 8, 3);
            _deleteModButton.Click += DeleteModClick;
            manageButtons.Controls.Add(_deleteModButton);
            _refreshModListButton = UiTheme.CreateSecondaryButton("刷新");
            _refreshModListButton.Width = 70;
            _refreshModListButton.Margin = new Padding(0, 3, 3, 3);
            _refreshModListButton.Click += RefreshInstalledModsClick;
            manageButtons.Controls.Add(_refreshModListButton);
            manageLayout.Controls.Add(manageButtons, 0, 2);
            manageCard.Controls.Add(manageLayout);
            leftPane.Controls.Add(CreateListCard("常用模组（可按需安装）", _modList), 0, 0);
            leftPane.Controls.Add(manageCard, 0, 1);

            CardPanel searchCard = UiTheme.CreateCard();
            searchCard.Dock = DockStyle.Fill;
            searchCard.Margin = new Padding(0);
            TableLayoutPanel searchLayout = new TableLayoutPanel();
            searchLayout.Dock = DockStyle.Fill;
            searchLayout.BackColor = UiTheme.CardBackground;
            searchLayout.ColumnCount = 1;
            searchLayout.RowCount = 3;
            searchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            searchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            searchLayout.Controls.Add(UiTheme.CreateSectionLabel("搜索 Modrinth"), 0, 0);

            TableLayoutPanel searchRow = new TableLayoutPanel();
            searchRow.Dock = DockStyle.Fill;
            searchRow.ColumnCount = 2;
            searchRow.RowCount = 1;
            searchRow.AutoSize = true;
            searchRow.BackColor = UiTheme.CardBackground;
            searchRow.Margin = new Padding(0, 0, 0, 8);
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _modSearchBox = new TextBox();
            _modSearchBox.Dock = DockStyle.Fill;
            _modSearchBox.MaxLength = 64;
            _modSearchBox.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleTextBox(_modSearchBox);
            searchRow.Controls.Add(_modSearchBox, 0, 0);
            _modSearchButton = UiTheme.CreateSecondaryButton("搜索");
            _modSearchButton.Margin = new Padding(0, 3, 0, 3);
            _modSearchButton.Click += SearchModsClick;
            searchRow.Controls.Add(_modSearchButton, 1, 0);
            searchLayout.Controls.Add(searchRow, 0, 1);

            _modSearchList = new ListView();
            _modSearchList.Dock = DockStyle.Fill;
            _modSearchList.View = View.Details;
            _modSearchList.FullRowSelect = true;
            _modSearchList.MultiSelect = false;
            _modSearchList.HideSelection = false;
            _modSearchList.GridLines = false;
            _modSearchList.BorderStyle = BorderStyle.None;
            _modSearchList.Font = UiTheme.FontRegular(9F);
            _modSearchList.Columns.Add("名称", 160);
            _modSearchList.Columns.Add("简介", 240);
            _modSearchList.Columns.Add("下载量", 80);
            searchLayout.Controls.Add(_modSearchList, 0, 2);
            searchCard.Controls.Add(searchLayout);
            split.Panel2.Controls.Add(searchCard);

            body.Controls.Add(split, 0, 1);
            page.Controls.Add(body, 0, 2);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.WrapContents = false;
            footer.BackColor = UiTheme.Background;
            footer.Margin = new Padding(0, 10, 0, 0);
            _installModButton = UiTheme.CreatePrimaryButton("安装选中模组");
            _installModButton.Width = 150;
            _installModButton.Click += InstallSelectedModClick;
            footer.Controls.Add(_installModButton);
            _installSearchModButton = UiTheme.CreatePrimaryButton("安装搜索结果");
            _installSearchModButton.Width = 150;
            _installSearchModButton.Margin = new Padding(0, 3, 10, 3);
            _installSearchModButton.Click += InstallSearchModClick;
            footer.Controls.Add(_installSearchModButton);
            _installCommonModsButton = UiTheme.CreateSecondaryButton("一键安装钠 / 玉 / 苹果皮");
            _installCommonModsButton.Margin = new Padding(0, 6, 10, 3);
            _installCommonModsButton.Click += InstallCommonModsClick;
            footer.Controls.Add(_installCommonModsButton);
            _openModsButton = UiTheme.CreateSecondaryButton("打开 mods 目录");
            _openModsButton.Margin = new Padding(0, 6, 10, 3);
            _openModsButton.Click += OpenModsClick;
            footer.Controls.Add(_openModsButton);
            _installLocalModButton = UiTheme.CreateSecondaryButton("安装本地 Mod JAR");
            _installLocalModButton.Margin = new Padding(0, 6, 10, 3);
            _installLocalModButton.Click += InstallLocalModClick;
            footer.Controls.Add(_installLocalModButton);
            _exportPresetButton = UiTheme.CreateSecondaryButton("导出模组预设");
            _exportPresetButton.Margin = new Padding(0, 6, 10, 3);
            _exportPresetButton.Click += ExportPresetClick;
            footer.Controls.Add(_exportPresetButton);
            _importPresetButton = UiTheme.CreateSecondaryButton("导入模组预设");
            _importPresetButton.Margin = new Padding(0, 6, 3, 3);
            _importPresetButton.Click += ImportPresetClick;
            footer.Controls.Add(_importPresetButton);
            page.Controls.Add(footer, 0, 3);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        private void BuildSettingsPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "设置",
                "一般不用改这里的设置。游戏目录默认就在本程序旁边，整个文件夹可以拷到别的电脑继续用。");

            CardPanel card = UiTheme.CreateCard();
            // 随内容自适应高度，避免固定高度把下方控件裁掉。
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.MinimumSize = new Size(0, 380);
            card.Margin = new Padding(0);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = UiTheme.CardBackground;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 8; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            grid.Controls.Add(UiTheme.CreateFieldLabel("游戏目录"), 0, 0);
            TableLayoutPanel dirPanel = new TableLayoutPanel();
            dirPanel.Dock = DockStyle.Fill;
            dirPanel.ColumnCount = 2;
            dirPanel.RowCount = 1;
            dirPanel.BackColor = UiTheme.CardBackground;
            dirPanel.Margin = new Padding(0, 0, 0, 6);
            dirPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dirPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _gameDirBox = new TextBox();
            _gameDirBox.Dock = DockStyle.Fill;
            _gameDirBox.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleTextBox(_gameDirBox);
            dirPanel.Controls.Add(_gameDirBox, 0, 0);
            Button browseDirButton = UiTheme.CreateSecondaryButton("浏览...");
            browseDirButton.Margin = new Padding(0, 3, 0, 3);
            browseDirButton.Click += BrowseGameDirClick;
            dirPanel.Controls.Add(browseDirButton, 1, 0);
            grid.Controls.Add(dirPanel, 1, 0);

            grid.Controls.Add(UiTheme.CreateFieldLabel("下载线程"), 0, 1);
            FlowLayoutPanel threadPanel = new FlowLayoutPanel();
            threadPanel.Dock = DockStyle.Fill;
            threadPanel.AutoSize = true;
            threadPanel.WrapContents = false;
            threadPanel.BackColor = UiTheme.CardBackground;
            threadPanel.Margin = new Padding(0, 2, 0, 6);
            _threadsBox = CreateNumeric(1, 16, 1, 80);
            threadPanel.Controls.Add(_threadsBox);
            Label threadHint = UiTheme.CreateCaptionLabel("建议 4-8；过高可能被服务器限速。");
            threadHint.Margin = new Padding(8, 9, 3, 3);
            threadPanel.Controls.Add(threadHint);
            grid.Controls.Add(threadPanel, 1, 1);

            grid.Controls.Add(UiTheme.CreateFieldLabel("版本列表"), 0, 2);
            FlowLayoutPanel optionPanel = new FlowLayoutPanel();
            optionPanel.Dock = DockStyle.Fill;
            optionPanel.AutoSize = true;
            optionPanel.WrapContents = false;
            optionPanel.BackColor = UiTheme.CardBackground;
            optionPanel.Margin = new Padding(0, 2, 0, 6);
            _settingsSnapshotCheck = new CheckBox();
            _settingsSnapshotCheck.Text = "在下载页显示快照版本";
            _settingsSnapshotCheck.AutoSize = true;
            _settingsSnapshotCheck.ForeColor = UiTheme.TextPrimary;
            _settingsSnapshotCheck.Margin = new Padding(0, 8, 18, 8);
            _settingsSnapshotCheck.CheckedChanged += SettingsSnapshotCheckChanged;
            optionPanel.Controls.Add(_settingsSnapshotCheck);
            _darkThemeCheck = new CheckBox();
            _darkThemeCheck.Text = "深色主题";
            _darkThemeCheck.AutoSize = true;
            _darkThemeCheck.ForeColor = UiTheme.TextPrimary;
            _darkThemeCheck.Margin = new Padding(0, 8, 3, 8);
            _darkThemeCheck.CheckedChanged += DarkThemeCheckChanged;
            optionPanel.Controls.Add(_darkThemeCheck);
            grid.Controls.Add(optionPanel, 1, 2);

            // ---------- 下载源 ----------
            grid.Controls.Add(UiTheme.CreateFieldLabel("下载源"), 0, 3);
            TableLayoutPanel mirrorPanel = new TableLayoutPanel();
            mirrorPanel.Dock = DockStyle.Fill;
            mirrorPanel.ColumnCount = 3;
            mirrorPanel.RowCount = 1;
            mirrorPanel.BackColor = UiTheme.CardBackground;
            mirrorPanel.Margin = new Padding(0, 0, 0, 6);
            mirrorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mirrorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mirrorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mirrorCombo = new ComboBox();
            _mirrorCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _mirrorCombo.Width = 170;
            _mirrorCombo.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleCombo(_mirrorCombo);
            _mirrorCombo.Items.Add("官方源");
            _mirrorCombo.Items.Add("BmclAPI 镜像");
            _mirrorCombo.Items.Add("自定义镜像…");
            _mirrorCombo.SelectedIndexChanged += MirrorComboChanged;
            mirrorPanel.Controls.Add(_mirrorCombo, 0, 0);
            _mirrorCustomBox = new TextBox();
            _mirrorCustomBox.Width = 260;
            _mirrorCustomBox.Margin = new Padding(0, 4, 8, 3);
            _mirrorCustomBox.Text = "https://";
            UiTheme.StyleTextBox(_mirrorCustomBox);
            _mirrorCustomBox.Visible = false;
            mirrorPanel.Controls.Add(_mirrorCustomBox, 1, 0);
            _mirrorHintLabel = UiTheme.CreateCaptionLabel("");
            _mirrorHintLabel.Margin = new Padding(0, 9, 3, 3);
            mirrorPanel.Controls.Add(_mirrorHintLabel, 2, 0);
            grid.Controls.Add(mirrorPanel, 1, 3);

            // ---------- 版本隔离 ----------
            grid.Controls.Add(UiTheme.CreateFieldLabel("版本隔离"), 0, 4);
            FlowLayoutPanel isolatePanel = new FlowLayoutPanel();
            isolatePanel.Dock = DockStyle.Fill;
            isolatePanel.AutoSize = true;
            isolatePanel.WrapContents = false;
            isolatePanel.BackColor = UiTheme.CardBackground;
            isolatePanel.Margin = new Padding(0, 2, 0, 6);
            _isolateCheck = new CheckBox();
            _isolateCheck.Text = "每个版本独立存档 / 配置 / 模组";
            _isolateCheck.AutoSize = true;
            _isolateCheck.ForeColor = UiTheme.TextPrimary;
            _isolateCheck.Margin = new Padding(0, 8, 10, 8);
            isolatePanel.Controls.Add(_isolateCheck);
            Label isolateHint = UiTheme.CreateCaptionLabel(
                "目录 versions\\<版本>\\instance；既有存档需手动迁移");
            isolateHint.Margin = new Padding(0, 11, 3, 3);
            isolatePanel.Controls.Add(isolateHint);
            grid.Controls.Add(isolatePanel, 1, 4);

            FlowLayoutPanel savePanel = new FlowLayoutPanel();
            savePanel.Dock = DockStyle.Fill;
            savePanel.AutoSize = true;
            savePanel.WrapContents = false;
            savePanel.BackColor = UiTheme.CardBackground;
            savePanel.Margin = new Padding(0, 4, 0, 8);
            _saveSettingsButton = UiTheme.CreatePrimaryButton("保存设置");
            _saveSettingsButton.Width = 130;
            _saveSettingsButton.Click += SaveSettingsClick;
            savePanel.Controls.Add(_saveSettingsButton);
            grid.Controls.Add(savePanel, 1, 5);

            grid.Controls.Add(UiTheme.CreateFieldLabel("数据目录"), 0, 6);
            Label dataLabel = new Label();
            dataLabel.Text = AppPaths.DataDirectory;
            dataLabel.AutoSize = true;
            dataLabel.Dock = DockStyle.Top;
            dataLabel.ForeColor = UiTheme.TextSecondary;
            dataLabel.BackColor = Color.Transparent;
            dataLabel.Margin = new Padding(0, 6, 3, 3);
            grid.Controls.Add(dataLabel, 1, 6);

            FlowLayoutPanel dataButtons = new FlowLayoutPanel();
            dataButtons.Dock = DockStyle.Fill;
            dataButtons.AutoSize = true;
            dataButtons.WrapContents = false;
            dataButtons.BackColor = UiTheme.CardBackground;
            dataButtons.Margin = new Padding(0, 2, 0, 8);
            _openDataButton = UiTheme.CreateSecondaryButton("打开数据目录");
            _openDataButton.Click += OpenDataClick;
            dataButtons.Controls.Add(_openDataButton);
            _openLogButton = UiTheme.CreateSecondaryButton("打开日志目录");
            _openLogButton.Click += OpenLogClick;
            dataButtons.Controls.Add(_openLogButton);
            grid.Controls.Add(dataButtons, 1, 7);

            // 必须 AutoSize：Dock=Fill 的标签不参与 TableLayoutPanel 的行高计算，
            // 多行文本会被所在 AutoSize 行裁掉（表现为"安全提示被截断"）。
            // 另外 WinForms 的多行 Label 自测高度会比实际排版略小（实测 96 vs 需要 102），
            // 因此改用 TextBox（只读、无边框）以获得可靠的多行排版与自动高度。
            TextBox securityNote = new TextBox();
            securityNote.Multiline = true;
            securityNote.ReadOnly = true;
            securityNote.BorderStyle = BorderStyle.None;
            securityNote.BackColor = UiTheme.CardBackground;
            securityNote.ForeColor = UiTheme.TextSecondary;
            securityNote.Font = UiTheme.FontRegular(8.5F);
            securityNote.ScrollBars = ScrollBars.None;
            securityNote.TabStop = false;
            securityNote.Text = "安全说明\r\n" +
                "· 仅允许 HTTPS 与官方域名列表（Mojang / Fabric / Modrinth），重定向逐跳校验。\r\n" +
                "· 下载文件校验 SHA1，ZIP 解压防路径穿越。\r\n" +
                "· 不调用 cmd.exe / PowerShell，不写注册表。\r\n" +
                "· 登录密码绝不落盘，只保存会话令牌。\r\n" +
                "· 不注册全局证书校验回调，不提供 SSL 绕过开关。";
            securityNote.Margin = new Padding(0, 12, 3, 3);
            grid.Controls.Add(securityNote, 0, 8);
            grid.SetColumnSpan(securityNote, 2);
            // 高度按行数计算，并留一行余量避免最后一字被裁。
            securityNote.Height = Math.Max(76,
                (securityNote.Lines.Length + 1) * securityNote.Font.Height);

            card.Controls.Add(grid);

            // 设置页内容（下载源、版本隔离、异地联机）会超过一屏高度，
            // 因此把两张卡片放进可滚动的竖向容器，避免控件被挤出可视区。
            TableLayoutPanel stack = new TableLayoutPanel();
            stack.Dock = DockStyle.Fill;
            stack.BackColor = UiTheme.Background;
            stack.ColumnCount = 1;
            stack.RowCount = 1;
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            stack.Margin = new Padding(0);

            Panel scrollHost = new Panel();
            scrollHost.Dock = DockStyle.Fill;
            scrollHost.AutoScroll = true;
            scrollHost.BackColor = UiTheme.Background;
            scrollHost.Margin = new Padding(0);
            // AutoScroll 容器里的内容层：固定左上角 + 锚定左右即可撑满宽度，
            // 高度由 AutoSize 随两张卡片累加。内容超过视口时容器自动出现滚动条。
            TableLayoutPanel scrolledContent = new TableLayoutPanel();
            scrolledContent.Location = new Point(0, 0);
            scrolledContent.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            scrolledContent.AutoSize = true;
            scrolledContent.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            scrolledContent.BackColor = UiTheme.Background;
            scrolledContent.ColumnCount = 1;
            scrolledContent.RowCount = 2;
            scrolledContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scrolledContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scrolledContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scrolledContent.Margin = new Padding(0);

            // 底部留白：AutoScroll 的 DisplayRectangle 在子控件展开后可能滞后一帧，
            // 加上内边距可避免最后一行文字被滚动边界切掉。
            scrolledContent.Padding = new Padding(0, 0, 0, 12);

            card.Dock = DockStyle.Top;
            card.Margin = new Padding(0);
            scrolledContent.Controls.Add(card, 0, 0);
            CardPanel easyTierCard = BuildEasyTierCard();
            easyTierCard.Margin = new Padding(0, 10, 0, 0);
            scrolledContent.Controls.Add(easyTierCard, 0, 1);
            scrollHost.Controls.Add(scrolledContent);

            stack.Controls.Add(scrollHost, 0, 0);
            page.Controls.Add(stack, 0, 2);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        /// <summary>
        /// 异地联机（EasyTier）卡片：开关 + 网络名/密钥/节点 + 核心下载 + 虚拟 IP。
        /// 启用后启动游戏时自动加入网络；虚拟 IP 由用户在游戏内手动使用。
        /// </summary>
        private CardPanel BuildEasyTierCard()
        {
            CardPanel card = UiTheme.CreateCard();
            // 卡片随内容自适应高度：内部是纵向堆叠的表单，固定高度必然裁掉控件。
            // Dock 留空（由父 TableLayoutPanel 的 AutoSize 行决定位置与宽度）。
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.MinimumSize = new Size(0, 110);
            card.Margin = new Padding(0, 10, 0, 0);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.BackColor = UiTheme.CardBackground;
            layout.ColumnCount = 2;
            layout.RowCount = 8;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label section = UiTheme.CreateSectionLabel("异地联机（EasyTier）");
            layout.Controls.Add(section, 0, 0);
            layout.SetColumnSpan(section, 2);

            Label intro = UiTheme.CreateCaptionLabel(
                "无需公网服务器：双方加入同一网络后，在游戏内用对方的虚拟 IP 直连。" +
                "启动器只负责组网与显示 IP，不修改服务器配置。");
            // 说明文字必须随窗口宽度自动换行：固定 MaximumSize 会在窄窗口下溢出。
            intro.AutoSize = true;
            intro.MaximumSize = new Size(0, 0);
            intro.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(intro, 0, 1);
            layout.SetColumnSpan(intro, 2);

            _easyTierEnabledCheck = new CheckBox();
            _easyTierEnabledCheck.Text = "启用异地联机（启动游戏时自动加入网络）";
            _easyTierEnabledCheck.AutoSize = true;
            _easyTierEnabledCheck.ForeColor = UiTheme.TextPrimary;
            _easyTierEnabledCheck.Margin = new Padding(0, 4, 0, 6);
            _easyTierEnabledCheck.CheckedChanged += EasyTierEnabledChanged;
            layout.Controls.Add(_easyTierEnabledCheck, 0, 2);
            layout.SetColumnSpan(_easyTierEnabledCheck, 2);

            // 网络配置区：未启用时折叠。
            _easyTierConfigPanel = new TableLayoutPanel();
            _easyTierConfigPanel.Dock = DockStyle.Fill;
            _easyTierConfigPanel.AutoSize = true;
            _easyTierConfigPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _easyTierConfigPanel.BackColor = UiTheme.CardBackground;
            _easyTierConfigPanel.ColumnCount = 2;
            _easyTierConfigPanel.RowCount = 7;
            _easyTierConfigPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _easyTierConfigPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _easyTierConfigPanel.Margin = new Padding(0);
            _easyTierConfigPanel.Visible = false;

            _easyTierConfigPanel.Controls.Add(UiTheme.CreateFieldLabel("网络名"), 0, 0);
            _easyTierNameBox = new TextBox();
            _easyTierNameBox.Dock = DockStyle.Fill;
            _easyTierNameBox.MaxLength = 64;
            _easyTierNameBox.Margin = new Padding(0, 4, 8, 6);
            UiTheme.StyleTextBox(_easyTierNameBox);
            _easyTierConfigPanel.Controls.Add(_easyTierNameBox, 1, 0);

            _easyTierConfigPanel.Controls.Add(UiTheme.CreateFieldLabel("网络密钥"), 0, 1);
            _easyTierSecretBox = new TextBox();
            _easyTierSecretBox.Dock = DockStyle.Fill;
            _easyTierSecretBox.MaxLength = 128;
            _easyTierSecretBox.UseSystemPasswordChar = true;
            _easyTierSecretBox.Margin = new Padding(0, 4, 8, 6);
            UiTheme.StyleTextBox(_easyTierSecretBox);
            _easyTierConfigPanel.Controls.Add(_easyTierSecretBox, 1, 1);

            // 公共节点：下拉预设（本机实测可连通的节点）+ 可手动输入自定义地址。
            // 旧方案只给一个失效的固定地址，用户无从选择；这里同时提供
            // 一键选用可用节点和"查看全部节点"入口。
            TableLayoutPanel nodePanel = new TableLayoutPanel();
            nodePanel.Dock = DockStyle.Fill;
            nodePanel.ColumnCount = 3;
            nodePanel.RowCount = 1;
            nodePanel.BackColor = UiTheme.CardBackground;
            nodePanel.Margin = new Padding(0, 0, 0, 0);
            nodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _easyTierNodePresetCombo = new ComboBox();
            _easyTierNodePresetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _easyTierNodePresetCombo.Width = 210;
            _easyTierNodePresetCombo.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleCombo(_easyTierNodePresetCombo);
            for (int i = 0; i < EasyTierService.KnownPublicNodes.Length; i++)
            {
                _easyTierNodePresetCombo.Items.Add(EasyTierService.KnownPublicNodes[i]);
            }
            _easyTierNodePresetCombo.SelectedIndexChanged += EasyTierNodePresetChanged;
            nodePanel.Controls.Add(_easyTierNodePresetCombo, 0, 0);
            _easyTierNodeBox = new TextBox();
            _easyTierNodeBox.Dock = DockStyle.Fill;
            _easyTierNodeBox.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleTextBox(_easyTierNodeBox);
            nodePanel.Controls.Add(_easyTierNodeBox, 1, 0);
            _easyTierNodeDocsButton = UiTheme.CreateSecondaryButton("查看全部节点");
            _easyTierNodeDocsButton.Margin = new Padding(0, 3, 0, 3);
            _easyTierNodeDocsButton.Click += EasyTierNodeDocsClick;
            nodePanel.Controls.Add(_easyTierNodeDocsButton, 2, 0);
            _easyTierConfigPanel.Controls.Add(nodePanel, 1, 2);

            Label nodeHint = UiTheme.CreateCaptionLabel(
                "从左侧下拉选择可用节点，或直接填写地址（格式 tcp://节点地址:11010）。" +
                "公共节点由社区维护、地址可能失效，点「查看全部节点」可查看监控站给出的最新可用地址。");
            // AutoSize=false + Dock=Fill 会把高度锁在单行（实测仅 23px），
            // 多行说明被截成"…地址可能失"。改为 AutoSize 让高度随换行增长。
            nodeHint.AutoSize = true;
            nodeHint.MaximumSize = new Size(0, 0);
            nodeHint.Margin = new Padding(0, 0, 8, 6);
            _easyTierConfigPanel.Controls.Add(nodeHint, 1, 3);

            _easyTierConfigPanel.Controls.Add(UiTheme.CreateFieldLabel("核心程序"), 0, 4);
            // 用 TableLayoutPanel 而不是 FlowLayoutPanel：后者不会给输入框
            // 留出弹性宽度，固定宽度的 TextBox 会顶着两个按钮横向溢出。
            TableLayoutPanel binaryPanel = new TableLayoutPanel();
            binaryPanel.Dock = DockStyle.Fill;
            binaryPanel.ColumnCount = 4;
            binaryPanel.RowCount = 1;
            binaryPanel.BackColor = UiTheme.CardBackground;
            binaryPanel.Margin = new Padding(0, 0, 0, 6);
            binaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            binaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            binaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            binaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _easyTierBinaryBox = new TextBox();
            _easyTierBinaryBox.Dock = DockStyle.Fill;
            _easyTierBinaryBox.Margin = new Padding(0, 4, 8, 3);
            UiTheme.StyleTextBox(_easyTierBinaryBox);
            binaryPanel.Controls.Add(_easyTierBinaryBox, 0, 0);
            Button browseBinary = UiTheme.CreateSecondaryButton("浏览…");
            browseBinary.Margin = new Padding(0, 3, 8, 3);
            browseBinary.Click += BrowseEasyTierBinaryClick;
            binaryPanel.Controls.Add(browseBinary, 1, 0);
            _easyTierDownloadButton = UiTheme.CreateSecondaryButton("下载核心");
            _easyTierDownloadButton.Margin = new Padding(0, 3, 8, 3);
            _easyTierDownloadButton.Click += EasyTierDownloadClick;
            binaryPanel.Controls.Add(_easyTierDownloadButton, 2, 0);
            // 失败时用户能直接看到核心的原始报错，不必去翻日志文件。
            _easyTierLogButton = UiTheme.CreateSecondaryButton("查看核心日志");
            _easyTierLogButton.Margin = new Padding(0, 3, 3, 3);
            _easyTierLogButton.Click += EasyTierLogClick;
            binaryPanel.Controls.Add(_easyTierLogButton, 3, 0);
            _easyTierConfigPanel.Controls.Add(binaryPanel, 1, 4);

            _easyTierConfigPanel.Controls.Add(UiTheme.CreateFieldLabel("状态"), 0, 5);
            _easyTierStatusLabel = UiTheme.CreateCaptionLabel("");
            _easyTierStatusLabel.Dock = DockStyle.Fill;
            _easyTierStatusLabel.Margin = new Padding(0, 6, 3, 6);
            _easyTierConfigPanel.Controls.Add(_easyTierStatusLabel, 1, 5);

            _easyTierConfigPanel.Controls.Add(UiTheme.CreateFieldLabel("虚拟 IP"), 0, 6);
            TableLayoutPanel ipPanel = new TableLayoutPanel();
            ipPanel.Dock = DockStyle.Fill;
            ipPanel.ColumnCount = 2;
            ipPanel.RowCount = 1;
            ipPanel.BackColor = UiTheme.CardBackground;
            ipPanel.Margin = new Padding(0, 0, 0, 4);
            ipPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            ipPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _easyTierIpLabel = UiTheme.CreateCaptionLabel("尚未加入网络");
            _easyTierIpLabel.Tag = "";
            _easyTierIpLabel.Dock = DockStyle.Fill;
            _easyTierIpLabel.Margin = new Padding(0, 8, 12, 3);
            ipPanel.Controls.Add(_easyTierIpLabel, 0, 0);
            _easyTierCopyIpButton = UiTheme.CreateSecondaryButton("复制 IP");
            _easyTierCopyIpButton.Margin = new Padding(0, 3, 3, 3);
            _easyTierCopyIpButton.Click += EasyTierCopyIpClick;
            ipPanel.Controls.Add(_easyTierCopyIpButton, 1, 0);
            _easyTierConfigPanel.Controls.Add(ipPanel, 1, 6);

            layout.Controls.Add(_easyTierConfigPanel, 0, 3);
            layout.SetColumnSpan(_easyTierConfigPanel, 2);

            Label trust = UiTheme.CreateCaptionLabel(
                "安全提示：网络密钥以明文保存在配置文件中，它是该网络的准入凭证；" +
                "组网为 P2P 直连，请只与你信任的人共享网络名与密钥。");
            trust.AutoSize = true;
            trust.MaximumSize = new Size(0, 0);
            trust.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(trust, 0, 4);
            layout.SetColumnSpan(trust, 2);

            card.Controls.Add(layout);
            return card;
        }

        private void BuildLogPage(Panel host)
        {
            TableLayoutPanel page = CreatePage("运行日志", "游戏或启动器出问题时，点「分析崩溃日志」能自动找出原因并给出处理建议。");

            CardPanel card = UiTheme.CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Both;
            _logBox.Dock = DockStyle.Fill;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.BackColor = UiTheme.CardBackground;
            _logBox.ForeColor = UiTheme.TextPrimary;
            _logBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _logBox.WordWrap = false;

            // 日志为空时不再呈现一片空白：给出说明与"打开日志目录"入口。
            _logEmptyState = new EmptyState();
            _logEmptyState.Dock = DockStyle.Fill;
            _logEmptyState.ShowState(
                "暂无运行日志",
                "启动器运行后，操作记录会实时显示在这里。",
                "打开日志目录");
            _logEmptyState.ActionClicked += OpenLogClick;

            // 先加日志框、再加空状态：后添加的控件位于更上层，
            // 这样空状态能覆盖在空白的日志框之上；日志一有内容就隐藏它。
            card.Controls.Add(_logBox);
            card.Controls.Add(_logEmptyState);
            _logEmptyState.BringToFront();
            page.Controls.Add(card, 0, 2);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.WrapContents = false;
            footer.BackColor = UiTheme.Background;
            footer.Margin = new Padding(0, 10, 0, 0);
            Button openLog = UiTheme.CreateSecondaryButton("打开日志目录");
            openLog.Click += OpenLogClick;
            footer.Controls.Add(openLog);
            _analyzeCrashButton = UiTheme.CreatePrimaryButton("分析崩溃日志");
            _analyzeCrashButton.Width = 140;
            _analyzeCrashButton.Margin = new Padding(0, 3, 8, 3);
            _analyzeCrashButton.Click += AnalyzeCrashClick;
            footer.Controls.Add(_analyzeCrashButton);
            _clearLogButton = UiTheme.CreateSecondaryButton("清空显示");
            _clearLogButton.Click += ClearLogClick;
            footer.Controls.Add(_clearLogButton);
            page.Controls.Add(footer, 0, 3);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        private void BuildAboutPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "关于 RLL",
                "RLL（Rouzhi Lite Launcher）是轻量、安全的 Minecraft 启动器：单文件、无第三方依赖、无遥测。");

            CardPanel card = UiTheme.CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            _aboutBox = new TextBox();
            _aboutBox.Multiline = true;
            _aboutBox.ReadOnly = true;
            _aboutBox.ScrollBars = ScrollBars.Vertical;
            _aboutBox.Dock = DockStyle.Fill;
            _aboutBox.BorderStyle = BorderStyle.None;
            _aboutBox.BackColor = UiTheme.CardBackground;
            _aboutBox.Font = UiTheme.FontRegular(9.5F);
            _aboutBox.Text = "RLL - Rouzhi Lite Launcher " + AppPaths.LauncherVersion + "\r\n" +
                "轻量、安全的 Minecraft 启动器（Windows / .NET Framework 4.8）\r\n\r\n" +
                "怎么开始用\r\n" +
                "1. 「下载」页选一个版本，点「安装原版」（想用模组就装 Fabric）\r\n" +
                "2. 「启动」页点「检测 Java」，选中刚装好的版本\r\n" +
                "3. 点「启动游戏」。需要调内存就在「启动」页改「最大内存」\r\n\r\n" +
                "已支持\r\n" +
                "· Mojang 版本清单与原版下载安装（客户端、库、资源、日志配置）\r\n" +
                "· Fabric 加载器安装（安装后可安装 Fabric 模组）\r\n" +
                "· 模组下载：常用模组一键安装、关键字搜索、必需前置自动解析、SHA1 校验\r\n" +
                "· 账号：离线账号 + 第三方认证账号（可进正版模式服务器）\r\n" +
                "· 异地联机（EasyTier）：不用公网服务器，双方填同一网络名即可直连\r\n" +
                "· 下载源镜像：官方源慢或不通时可切换 BmclAPI 或自定义镜像\r\n" +
                "· 版本隔离：每个版本独立存档 / 配置 / 模组，互不影响\r\n" +
                "· 崩溃日志分析：自动归纳问题原因并给出处理建议\r\n" +
                "· Java 自动检测与手动选择、内存 / JVM 参数 / 窗口分辨率\r\n" +
                "· 深色主题、实时日志、下载进度与随时取消\r\n" +
                "· 默认游戏目录为程序所在目录下的 .minecraft，整个文件夹可直接搬运\r\n\r\n" +
                "暂不支持\r\n" +
                "· 微软官方正版登录、Forge / NeoForge / Quilt、整合包导入\r\n" +
                "· 自动下载 Java、自动更新、遥测（不会收集任何使用数据）\r\n\r\n" +
                "安全设计\r\n" +
                "· 仅 HTTPS，严格域名白名单（Mojang / Fabric / Modrinth），重定向逐跳校验\r\n" +
                "· 所有下载文件校验哈希；ZIP 解压防路径穿越\r\n" +
                "· 远程 JSON 只作为纯数据解析，不按类型名反序列化\r\n" +
                "· 不提供远程 UI、远程命令或自定义 shell 执行\r\n" +
                "· 不写注册表；登录密码绝不落盘（只保存会话令牌）\r\n" +
                "· 启动参数统一转义，进程 UseShellExecute=false\r\n" +
                "· 不注册全局证书校验回调，不提供 SSL 绕过开关\r\n\r\n" +
                "已知限制\r\n" +
                "· 首次安装需要网络；资源文件较多时下载时间较长\r\n" +
                "· 未内置 Java，需要系统已安装 Java 8 / 17 / 21 等\r\n" +
                "· 模组仅支持 Modrinth 上提供 Fabric 版本的资源\r\n" +
                "· 异地联机的公共节点稳定性取决于网络环境\r\n\r\n" +
                "配置文件：" + AppPaths.ConfigFile + "\r\n" +
                "日志目录：" + AppPaths.LogDirectory + "\r\n" +
                "可移植模式：在 exe 同目录放置 portable.txt 后，数据保存到程序目录下的 data\\。";
            card.Controls.Add(_aboutBox);
            page.Controls.Add(card, 0, 2);

            _pages.Add(page);
            host.Controls.Add(page);
        }

        private static NumericUpDown CreateNumeric(int min, int max, int increment, int width)
        {
            NumericUpDown numeric = new NumericUpDown();
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.Increment = increment;
            numeric.Width = width;
            numeric.Font = UiTheme.FontRegular(9.5F);
            numeric.Margin = new Padding(0, 5, 0, 3);
            return numeric;
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= _pages.Count)
            {
                return;
            }
            for (int i = 0; i < _pages.Count; i++)
            {
                bool visible = i == index;
                // 必须无条件赋值。Control.Visible 的读取结果是有效可见性：
                // 父窗口尚未显示时恒为 false，而其内部标志仍为 true，
                // 用条件赋值会漏掉隐藏动作，窗口显示后所有页面会同时可见。
                _pages[i].Visible = visible;
                if (i < _navButtons.Count)
                {
                    _navButtons[i].Selected = visible;
                }
            }
            if (index == 1)
            {
                RefreshAccountList();
            }
            if (index == 3)
            {
                // 每次进入模组页都刷新已安装列表：用户可能在别处改过 mods 目录。
                RefreshInstalledMods();
                if (!_modsPageLoaded)
                {
                    _modsPageLoaded = true;
                    QueueModStatusRefresh();
                }
            }
        }

        // ============================ 配置与启动 ============================

        private void ApplyConfig()
        {
            _updatingUi = true;
            RefreshAccountCombo();
            _minMemoryBox.Value = ClampDecimal(_config.MinMemoryMB, _minMemoryBox.Minimum, _minMemoryBox.Maximum);
            _maxMemoryBox.Value = ClampDecimal(_config.MaxMemoryMB, _maxMemoryBox.Minimum, _maxMemoryBox.Maximum);
            _jvmBox.Text = _config.JvmArgs;
            _resolutionCheck.Checked = _config.CustomResolution;
            _widthBox.Value = ClampDecimal(_config.WindowWidth, _widthBox.Minimum, _widthBox.Maximum);
            _heightBox.Value = ClampDecimal(_config.WindowHeight, _heightBox.Minimum, _heightBox.Maximum);
            _gameDirBox.Text = _config.GameDir;
            _threadsBox.Value = ClampDecimal(_config.DownloadThreads, _threadsBox.Minimum, _threadsBox.Maximum);
            _snapshotCheck.Checked = _config.ShowSnapshots;
            _settingsSnapshotCheck.Checked = _config.ShowSnapshots;
            _darkThemeCheck.Checked = _config.DarkTheme;

            // 下载源：把配置值映射回下拉框三态（官方 / BmclAPI / 自定义）。
            if (string.Equals(_config.DownloadMirror, DownloadMirrors.BmclApi,
                StringComparison.OrdinalIgnoreCase))
            {
                _mirrorCombo.SelectedIndex = 1;
            }
            else if (DownloadMirrors.IsMirrorEnabled(_config.DownloadMirror))
            {
                _mirrorCombo.SelectedIndex = 2;
                _mirrorCustomBox.Text = _config.DownloadMirror;
            }
            else
            {
                _mirrorCombo.SelectedIndex = 0;
            }
            _mirrorCustomBox.Visible = _mirrorCombo.SelectedIndex == 2;
            UpdateMirrorHint();
            HttpUtil.SetMirror(_config.DownloadMirror);

            _isolateCheck.Checked = _config.IsolateVersions;

            // 异地联机
            _easyTierEnabledCheck.Checked = _config.EasyTierEnabled;
            _easyTierNameBox.Text = _config.EasyTierNetworkName;
            _easyTierSecretBox.Text = _config.EasyTierNetworkSecret;
            _easyTierNodeBox.Text = _config.EasyTierPublicNode;
            // 让预设下拉反映当前值：已是预设之一则选中它，否则保持"自定义"状态。
            if (_easyTierNodePresetCombo != null)
            {
                bool matched = false;
                for (int i = 0; i < _easyTierNodePresetCombo.Items.Count; i++)
                {
                    if (string.Equals(_easyTierNodePresetCombo.Items[i] as string,
                        _config.EasyTierPublicNode, StringComparison.OrdinalIgnoreCase))
                    {
                        _easyTierNodePresetCombo.SelectedIndex = i;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    _easyTierNodePresetCombo.SelectedIndex = -1;
                }
            }
            // 显示实际生效的核心路径：配置为空时用默认路径。
            // 旧实现直接填空字符串，输入框看起来是空的，而状态行却显示
            // "核心已就绪：<默认路径>"，用户无法判断到底装的哪个文件。
            _easyTierBinaryBox.Text = EasyTierService.ResolveBinaryPath(_config.EasyTierBinaryPath);
            if (_easyTierConfigPanel != null)
            {
                _easyTierConfigPanel.Visible = _config.EasyTierEnabled;
            }
            UpdateEasyTierStatus();

            RefreshAuthServerCombo();
            UpdateResolutionEnabled();
            _updatingUi = false;
        }

        // ============================ 账号 ============================

        /// <summary>
        /// 用配置中的账号列表刷新启动页账号下拉框，并保证至少存在一个账号
        /// （无账号时自动创建 LastUsername 对应的离线账号，并立即持久化）。
        /// </summary>
        private void RefreshAccountCombo()
        {
            if (_accountCombo == null || _accountCombo.IsDisposed)
            {
                return;
            }
            bool wasUpdating = _updatingUi;
            _updatingUi = true;
            try
            {
                // 界面刷新需要"至少有一个可选账号"，因此这里用显式初始化（会写入配置），
                // 而不是纯查询的 GetSelectedAccount。
                Account selected = AuthService.EnsureDefaultAccount(_config);
                List<Account> accounts = AuthService.GetAccounts(_config);
                _accountCombo.Items.Clear();
                for (int i = 0; i < accounts.Count; i++)
                {
                    _accountCombo.Items.Add(accounts[i]);
                }
                int selectedIndex = -1;
                for (int i = 0; i < accounts.Count; i++)
                {
                    if (string.Equals(accounts[i].Id, selected.Id, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                _accountCombo.SelectedIndex = selectedIndex;
                UpdateAccountStatus();
            }
            finally
            {
                _updatingUi = wasUpdating;
            }
        }

        private void UpdateAccountStatus()
        {
            if (_accountStatusLabel == null || _accountStatusLabel.IsDisposed)
            {
                return;
            }
            Account account = GetSelectedAccountFromCombo();
            if (account == null)
            {
                _accountStatusLabel.Text = "";
                return;
            }
            if (account.Type == AccountType.Authlib)
            {
                _accountStatusLabel.Text = "可进入正版服务器";
                _accountStatusLabel.ForeColor = UiTheme.Success;
            }
            else
            {
                _accountStatusLabel.Text = "离线模式";
                _accountStatusLabel.ForeColor = UiTheme.TextSecondary;
            }
        }

        private Account GetSelectedAccountFromCombo()
        {
            if (_accountCombo == null || _accountCombo.SelectedIndex < 0)
            {
                return null;
            }
            return _accountCombo.SelectedItem as Account;
        }

        private void AccountComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            Account account = GetSelectedAccountFromCombo();
            if (account != null)
            {
                _config.SelectedAccountId = account.Id;
                _config.LastUsername = account.Type == AccountType.Authlib
                    ? account.DisplayName
                    : account.UserName;
                UpdateAccountStatus();
            }
        }

        private static decimal ClampDecimal(int value, decimal min, decimal max)
        {
            decimal result = value;
            if (result < min)
            {
                result = min;
            }
            if (result > max)
            {
                result = max;
            }
            return result;
        }

        private async void MainFormShown(object sender, EventArgs e)
        {
            if (_skipAutoStart)
            {
                return;
            }
            try
            {
                if (_versionSplit != null && _versionSplit.Width > 600)
                {
                    _versionSplit.SplitterDistance = (int)(_versionSplit.Width * 0.56);
                }
            }
            catch
            {
            }
            RefreshInstalledVersions(_config.LastVersion);
            await DetectJavaAsync(false);
            await RefreshManifestAsync();
        }

        private async Task RefreshManifestAsync()
        {
            if (_busy)
            {
                return;
            }
            _refreshManifestButton.Enabled = false;
            SetStatus("正在获取 Mojang 版本清单...");
            try
            {
                VersionManifestService service = new VersionManifestService();
                VersionManifest manifest = await service.GetManifestAsync(CancellationToken.None);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _manifest = manifest;
                PopulateRemoteList();
                _manifestStatusLabel.Text = "共 " + manifest.Versions.Count + " 个版本";
                Log.Info("版本清单已更新，共 " + manifest.Versions.Count + " 个版本。");
                SetStatus("版本清单已更新");
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                Log.Error("获取版本清单失败", ex);
                _manifestStatusLabel.Text = "获取版本清单失败";
                SetStatus("获取版本清单失败，请检查网络后重试");
            }
            finally
            {
                if (!IsDisposed && !Disposing && _refreshManifestButton != null)
                {
                    _refreshManifestButton.Enabled = !_busy;
                }
            }
        }

        private void PopulateRemoteList()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (_manifest == null || _remoteList == null)
            {
                return;
            }
            string selectedId = "";
            if (_remoteList.SelectedItems.Count > 0)
            {
                VersionSummary selected = _remoteList.SelectedItems[0].Tag as VersionSummary;
                if (selected != null)
                {
                    selectedId = selected.Id;
                }
            }
            bool showSnapshots = _snapshotCheck.Checked;
            _remoteList.BeginUpdate();
            try
            {
                _remoteList.Items.Clear();
                for (int i = 0; i < _manifest.Versions.Count; i++)
                {
                    VersionSummary summary = _manifest.Versions[i];
                    if (!showSnapshots && !string.Equals(summary.Type, "release", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    ListViewItem item = new ListViewItem(summary.Id);
                    item.SubItems.Add(VersionManifestService.GetTypeName(summary.Type));
                    item.SubItems.Add(summary.ReleaseTimeUtc == DateTimeOffset.MinValue
                        ? summary.ReleaseTime
                        : summary.ReleaseTimeUtc.ToLocalTime().ToString("yyyy-MM-dd"));
                    item.Tag = summary;
                    _remoteList.Items.Add(item);
                    if (string.Equals(summary.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                        item.Focused = true;
                    }
                }
            }
            finally
            {
                _remoteList.EndUpdate();
            }
            UpdateActionButtons();
        }

        private void RefreshInstalledVersions(string selectId)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            string gameDir = GetGameDirFromUi();
            List<string> versions = VersionResolver.GetInstalledVersions(gameDir);
            _updatingUi = true;
            try
            {
                _installedList.BeginUpdate();
                _installedList.Items.Clear();
                for (int i = 0; i < versions.Count; i++)
                {
                    ListViewItem item = new ListViewItem(versions[i]);
                    item.Tag = versions[i];
                    _installedList.Items.Add(item);
                }
                _installedList.EndUpdate();

                _versionCombo.Items.Clear();
                for (int i = 0; i < versions.Count; i++)
                {
                    _versionCombo.Items.Add(versions[i]);
                }

                string previousMod = _modVersionCombo.SelectedItem as string;
                List<string> fabricVersions = new List<string>();
                for (int i = 0; i < versions.Count; i++)
                {
                    try
                    {
                        EffectiveVersion effective = VersionResolver.LoadInstalledVersion(gameDir, versions[i]);
                        if (VersionResolver.IsFabricVersion(effective))
                        {
                            fabricVersions.Add(versions[i]);
                        }
                    }
                    catch
                    {
                        // 单个版本 JSON 损坏时跳过该条目，其余版本仍可使用。
                    }
                }
                _modVersionCombo.Items.Clear();
                for (int i = 0; i < fabricVersions.Count; i++)
                {
                    _modVersionCombo.Items.Add(fabricVersions[i]);
                }
                if (!string.IsNullOrEmpty(previousMod) && fabricVersions.Contains(previousMod))
                {
                    _modVersionCombo.SelectedItem = previousMod;
                }
                else if (fabricVersions.Count > 0)
                {
                    _modVersionCombo.SelectedIndex = 0;
                }
                else
                {
                    _modStatusLabel.Text = "尚未安装 Fabric 版本，请先在「下载」页安装。";
                }

                string target = selectId;
                if (string.IsNullOrEmpty(target) || !versions.Contains(target))
                {
                    target = _config.LastVersion;
                }
                if (string.IsNullOrEmpty(target) || !versions.Contains(target))
                {
                    target = versions.Count > 0 ? versions[0] : "";
                }
                if (!string.IsNullOrEmpty(target))
                {
                    _versionCombo.SelectedItem = target;
                    for (int i = 0; i < _installedList.Items.Count; i++)
                    {
                        if (string.Equals(_installedList.Items[i].Text, target, StringComparison.OrdinalIgnoreCase))
                        {
                            _installedList.Items[i].Selected = true;
                            _installedList.Items[i].Focused = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _updatingUi = false;
            }
            UpdateJavaSelectionForVersion();
            UpdateActionButtons();
            if (!_updatingUi && _modVersionCombo.Items.Count > 0 && _modsPageLoaded)
            {
                QueueModStatusRefresh();
            }
        }

        private void VersionComboSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            UpdateJavaSelectionForVersion();
            UpdateActionButtons();
        }

        private void ModVersionComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            UpdateActionButtons();
            // 换版本后模组目录也变了，已安装列表必须跟着刷新，否则会显示上一个版本的模组。
            RefreshInstalledMods();
            if (_modsPageLoaded)
            {
                QueueModStatusRefresh();
            }
        }

        private void UpdateJavaSelectionForVersion()
        {
            if (_updatingUi)
            {
                return;
            }
            string versionId = _versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(versionId))
            {
                _javaRequirementLabel.Text = "Java";
                return;
            }
            int required = 8;
            try
            {
                EffectiveVersion version = VersionResolver.LoadInstalledVersion(GetGameDirFromUi(), versionId);
                required = version.JavaVersionMajor > 0 ? version.JavaVersionMajor : 8;
            }
            catch
            {
                // 版本 JSON 损坏时仍允许界面显示，启动时会给出明确错误。
            }
            _javaRequirementLabel.Text = "Java（需 " + required + "）";
            JavaInfo selected = GetSelectedJava();
            if (selected == null || (selected.Major > 0 && selected.Major < required))
            {
                JavaInfo best = JavaFinder.ChooseBest(_javaList, required);
                if (best != null)
                {
                    SelectJavaInCombo(best);
                }
            }
        }

        private JavaInfo GetSelectedJava()
        {
            return _javaCombo.SelectedItem as JavaInfo;
        }

        private void SelectJavaInCombo(JavaInfo java)
        {
            if (java == null)
            {
                return;
            }
            for (int i = 0; i < _javaCombo.Items.Count; i++)
            {
                JavaInfo item = _javaCombo.Items[i] as JavaInfo;
                if (item != null &&
                    string.Equals(item.LaunchPath, java.LaunchPath, StringComparison.OrdinalIgnoreCase))
                {
                    _javaCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private async Task DetectJavaAsync(bool showMessage)
        {
            _detectJavaButton.Enabled = false;
            SetStatus("正在检测 Java...");
            try
            {
                List<JavaInfo> found = await Task.Run(delegate { return JavaFinder.FindAll(); });
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _javaList.Clear();
                _javaList.AddRange(found);
                _updatingUi = true;
                try
                {
                    _javaCombo.Items.Clear();
                    for (int i = 0; i < _javaList.Count; i++)
                    {
                        _javaCombo.Items.Add(_javaList[i]);
                    }
                }
                finally
                {
                    _updatingUi = false;
                }

                JavaInfo preferred = FindJavaByPath(_config.JavaPath);
                if (preferred == null)
                {
                    preferred = GetSelectedJava();
                }
                if (preferred == null && _javaList.Count > 0)
                {
                    preferred = _javaList[0];
                }
                if (preferred != null)
                {
                    SelectJavaInCombo(preferred);
                }
                UpdateJavaSelectionForVersion();
                Log.Info("Java 检测完成，共找到 " + _javaList.Count + " 个。");
                if (_javaList.Count == 0)
                {
                    SetStatus("未检测到 Java，可以点「下载 Java」自动安装");
                    if (showMessage)
                    {
                        ShowJavaMissingDialog();
                    }
                }
                else
                {
                    SetStatus("已检测到 " + _javaList.Count + " 个 Java");
                    if (showMessage)
                    {
                        MessageBox.Show(
                            "已检测到 " + _javaList.Count + " 个 Java。",
                            BrandTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("检测 Java 失败", ex);
                SetStatus("检测 Java 失败");
                if (showMessage)
                {
                    MessageBox.Show("检测 Java 失败：" + ex.Message, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing && _detectJavaButton != null)
                {
                    _detectJavaButton.Enabled = !_busy;
                }
            }
        }

        private JavaInfo FindJavaByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            for (int i = 0; i < _javaList.Count; i++)
            {
                JavaInfo java = _javaList[i];
                if (string.Equals(java.LaunchPath, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(java.ProbePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return java;
                }
            }
            return null;
        }

        private async void DetectJavaClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            await DetectJavaAsync(true);
        }

        /// <summary>
        /// 没装 Java 时的引导：直接提供「自动下载」入口，而不是让用户自己去官网找。
        /// 需要的版本按所选 Minecraft 版本推断；下载的是 JRE（约 47MB）。
        /// </summary>
        private void ShowJavaMissingDialog()
        {
            int required = GetRequiredJavaMajorFromSelectedVersion();
            int download = JavaInstaller.MapToDownloadable(required);
            string versionHint = required > 0
                ? "当前选中的游戏版本建议使用 Java " + required + "，将下载 Java " + download + "。"
                : "一般 1.20.1 用 Java 17、1.21 及更新用 Java 21、更老的版本用 Java 8。";

            if (MessageBox.Show(
                "没有检测到 Java。\r\n\r\n" +
                "Minecraft 必须靠 Java 运行，没装 Java 就启动不了游戏。\r\n\r\n" +
                versionHint + "\r\n" +
                "下载来源：Eclipse Adoptium（Temurin）官方，约 47MB，只下载运行时（JRE）。\r\n\r\n" +
                "要现在自动下载并安装吗？\r\n" +
                "（也可以点「浏览...」手动选择已装好的 javaw.exe）",
                BrandTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                DownloadJavaAsync(required);
            }
        }

        /// <summary>从启动页选中的版本推断需要的 Java 主版本；无法判断时返回 0。</summary>
        private int GetRequiredJavaMajorFromSelectedVersion()
        {
            try
            {
                string versionId = "";
                if (_versionCombo != null && _versionCombo.SelectedItem != null)
                {
                    versionId = (_versionCombo.SelectedItem as string) ?? "";
                }
                if (string.IsNullOrEmpty(versionId))
                {
                    return 0;
                }
                // 复用与 Java 下拉框一致的判定来源（版本的 version JSON）。
                EffectiveVersion version = VersionResolver.LoadInstalledVersion(
                    GetGameDirFromUi(), versionId);
                return version.JavaVersionMajor > 0 ? version.JavaVersionMajor : 8;
            }
            catch
            {
                return 0;
            }
        }

        private void DownloadJavaClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            int required = GetRequiredJavaMajorFromSelectedVersion();
            DownloadJavaAsync(required);
        }

        /// <summary>设置进度百分比（切到确定模式）。回调可能来自后台线程，需切回 UI 线程。</summary>
        private void SetProgressPercent(int percent)
        {
            if (IsDisposed || Disposing || _progressBar == null)
            {
                return;
            }
            if (_progressBar.InvokeRequired)
            {
                try
                {
                    _progressBar.BeginInvoke((MethodInvoker)delegate { SetProgressPercent(percent); });
                }
                catch
                {
                }
                return;
            }
            if (percent < 0) { percent = 0; }
            if (percent > 100) { percent = 100; }
            _progressBar.Marquee = false;
            _progressBar.Value = percent;
        }

        /// <summary>下载并安装 JRE，装好后自动加入 Java 列表并选中。</summary>
        private async void DownloadJavaAsync(int requiredMajor)
        {
            if (_busy)
            {
                MessageBox.Show("当前有其它任务在进行，请等它结束后再试。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CancellationToken token = BeginOperation("正在查询 Java 下载信息...");
            try
            {
                JavaDownload download = await JavaInstaller.QueryAsync(requiredMajor, token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                if (download == null)
                {
                    SetStatus("查询 Java 下载信息失败");
                    MessageBox.Show(
                        "没能获取 Java 下载信息（可能是网络问题）。\r\n\r\n" +
                        "可以手动访问 https://adoptium.net 下载安装，装完回来点「检测 Java」。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MessageBox.Show(
                    "将下载 Java " + download.MajorVersion + "（" + download.ReleaseName + "）\r\n" +
                    "大小约 " + (download.Size / 1024 / 1024) + " MB。\r\n\r\n" +
                    "安装位置：" + Path.Combine(AppPaths.DataDirectory, "java") + "\r\n\r\n" +
                    "开始下载吗？（可点「取消」中止）",
                    BrandTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                SetStatus("正在下载 Java " + download.MajorVersion + "...");
                _progressBar.Marquee = false;
                Action<long> onBytes = delegate(long received)
                {
                    if (download.Size > 0)
                    {
                        int percent = (int)(received * 100L / download.Size);
                        if (percent > 100) { percent = 100; }
                        SetProgressPercent(percent);
                        SetStatus("正在下载 Java：" + (received / 1024 / 1024) + " / " +
                                  (download.Size / 1024 / 1024) + " MB");
                    }
                };
                string javaDir = await JavaInstaller.InstallAsync(download, onBytes, token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _progressBar.Marquee = true;
                if (string.IsNullOrEmpty(javaDir))
                {
                    SetStatus("Java 安装失败");
                    MessageBox.Show("Java 安装失败，详细原因见日志。", BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 装好后重新检测，让新 Java 出现在下拉框并自动选中。
                SetStatus("Java 已安装，正在重新检测...");
                await DetectJavaAsync(false);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                JavaInfo installed = null;
                for (int i = 0; i < _javaList.Count; i++)
                {
                    string candidate = _javaList[i].ProbePath;
                    if (candidate != null && candidate.IndexOf(javaDir,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        installed = _javaList[i];
                        break;
                    }
                }
                if (installed != null)
                {
                    SelectJavaInCombo(installed);
                    _config.JavaPath = installed.LaunchPath;
                    SaveConfigSafely();
                    _javaRequirementLabel.Text = "Java（已选用 " + installed.Major + "）";
                    SetStatus("Java " + installed.Major + " 安装完成");
                }
                else
                {
                    SetStatus("Java 已下载到 " + javaDir + "，但未能自动识别");
                    MessageBox.Show(
                        "Java 已下载到：\r\n" + javaDir + "\r\n\r\n" +
                        "但启动器没能自动识别它。请点「浏览...」手动选择该目录下的 javaw.exe。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("Java 下载已取消");
            }
            catch (Exception ex)
            {
                Log.Error("下载 Java 失败", ex);
                SetStatus("Java 下载失败");
                MessageBox.Show("下载 Java 失败：\r\n" + ex.Message +
                    "\r\n\r\n也可以手动访问 https://adoptium.net 下载安装。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void BrowseJavaClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 javaw.exe 或 java.exe";
                dialog.Filter = "Java 可执行文件 (javaw.exe;java.exe)|javaw.exe;java.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _browseJavaButton.Enabled = false;
                SetStatus("正在验证 Java...");
                try
                {
                    string path = dialog.FileName;
                    JavaInfo info = await Task.Run(delegate { return JavaFinder.Probe(path); });
                    if (IsDisposed || Disposing)
                    {
                        return;
                    }
                    if (info == null)
                    {
                        MessageBox.Show("无法运行该 Java，请确认文件有效。", BrandTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    bool replaced = false;
                    for (int i = 0; i < _javaList.Count; i++)
                    {
                        if (string.Equals(_javaList[i].LaunchPath, info.LaunchPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _javaList[i] = info;
                            replaced = true;
                            break;
                        }
                    }
                    if (!replaced)
                    {
                        _javaList.Add(info);
                    }
                    _updatingUi = true;
                    try
                    {
                        _javaCombo.Items.Clear();
                        for (int i = 0; i < _javaList.Count; i++)
                        {
                            _javaCombo.Items.Add(_javaList[i]);
                        }
                    }
                    finally
                    {
                        _updatingUi = false;
                    }
                    SelectJavaInCombo(info);
                    _config.JavaPath = info.LaunchPath;
                    Log.Info("已选择 Java：" + info.Display);
                    SetStatus("已选择 " + info.Display);
                }
                catch (Exception ex)
                {
                    Log.Error("验证 Java 失败", ex);
                    MessageBox.Show("验证 Java 失败：" + ex.Message, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (!IsDisposed && !Disposing && _browseJavaButton != null)
                    {
                        _browseJavaButton.Enabled = !_busy;
                    }
                }
            }
        }

        private void ResolutionCheckChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            UpdateResolutionEnabled();
        }

        private void UpdateResolutionEnabled()
        {
            _widthBox.Enabled = _resolutionCheck.Checked;
            _heightBox.Enabled = _resolutionCheck.Checked;
        }

        private async void StartGameClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (_gameProcess != null && !_gameProcess.HasExited)
            {
                MessageBox.Show("游戏已经在运行。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string versionId = _versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(versionId))
            {
                MessageBox.Show(
                    "还没有可用的游戏版本，所以无法启动。\r\n\r\n" +
                    "怎么处理：\r\n" +
                    "1. 到左侧「下载」页，点「刷新版本清单」\r\n" +
                    "2. 在左侧列表选一个版本（不确定就选最上面的正式版）\r\n" +
                    "3. 点「安装原版」；想用模组就改点「安装 Fabric」\r\n" +
                    "4. 装完会自动回到本页并选好版本\r\n\r\n" +
                    "首次安装需要联网，下载可能需要几分钟。",
                    BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPage(2);
                return;
            }
            Account account = GetSelectedAccountFromCombo();
            if (account == null)
            {
                MessageBox.Show("尚未选择账号，请先在「账号」页添加或选择一个账号。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(1);
                return;
            }
            string username;
            if (account.Type == AccountType.Authlib)
            {
                username = account.DisplayName;
            }
            else
            {
                username = account.UserName;
                if (!CommandLine.IsValidUsername(username))
                {
                    MessageBox.Show("离线用户名必须是 3-16 位字母、数字或下划线，请在账号页修改。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ShowPage(1);
                    return;
                }
            }
            JavaInfo java = GetSelectedJava();
            if (java == null)
            {
                MessageBox.Show("尚未选择 Java，请点击“检测 Java”或手动浏览。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveConfigFromUi();
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("保存配置失败：" + ex.Message);
            }

            LaunchOptions options = new LaunchOptions();
            options.GameDir = GetGameDirFromUi();
            options.VersionId = versionId;
            options.Username = username;
            options.Account = account;
            options.AuthlibInjectorPath = _config.AuthlibInjectorPath;
            options.JavaPath = java.LaunchPath;
            options.MinMemoryMB = (int)_minMemoryBox.Value;
            options.MaxMemoryMB = (int)_maxMemoryBox.Value;
            options.JvmArgs = _jvmBox.Text;
            options.CustomResolution = _resolutionCheck.Checked;
            options.WindowWidth = (int)_widthBox.Value;
            options.WindowHeight = (int)_heightBox.Value;
            options.IsolateVersions = _config.IsolateVersions;

            // 版本隔离：首次启用某版本时创建实例目录（只建目录，不移动既有数据）。
            if (options.IsolateVersions)
            {
                try
                {
                    InstancePaths.EnsureInstanceDirectory(options.GameDir, versionId);
                    // 之前装在共享 <GameDir>\mods 里的模组，隔离后游戏读不到，
                    // 表现为"开了隔离就加载不到 mod"。这里把它们复制进实例目录，
                    // 只复制不移动，已存在的同名文件不覆盖。
                    int seeded = InstancePaths.SeedInstanceMods(options.GameDir, versionId);
                    if (seeded > 0)
                    {
                        SetStatus("已同步 " + seeded + " 个模组到版本实例目录");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("创建版本实例目录失败：" + ex.Message);
                }
            }
            else
            {
                // 关闭隔离的反向补救：隔离期间新装的模组落在 instance\mods 里，
                // 关掉隔离后游戏改读共享 <GameDir>\mods，那些模组就"消失"了。
                // 这里把它们补回共享目录（只复制、不覆盖、不移动）。
                try
                {
                    List<string> instances = InstancePaths.ListInstanceVersions(options.GameDir);
                    int restored = 0;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        restored += InstancePaths.SeedSharedModsFromInstance(
                            options.GameDir, instances[i]);
                    }
                    if (restored > 0)
                    {
                        SetStatus("已从版本实例目录补回 " + restored + " 个模组");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("从实例目录补回模组失败：" + ex.Message);
                }
            }

            // 重复模组检测：同一个 mod id 出现两份会让 Fabric 直接加载失败，
            // 用户只会看到"模组不生效"，很难自己发现是重复文件。
            // 这里在启动前提示并给出具体文件名，不自动删除（删用户文件风险太大）。
            try
            {
                string scanDir = InstancePaths.GetModsDirectory(
                    options.GameDir, versionId, options.IsolateVersions);
                List<DuplicateModGroup> duplicates = ModDuplicates.Find(scanDir);
                if (duplicates.Count > 0)
                {
                    StringBuilder warning = new StringBuilder();
                    warning.Append("发现 ").Append(duplicates.Count)
                        .Append(" 个模组存在重复文件，游戏会因此加载失败：\r\n");
                    for (int i = 0; i < duplicates.Count; i++)
                    {
                        DuplicateModGroup group = duplicates[i];
                        warning.Append("\r\n· ").Append(group.ModId).Append("\r\n");
                        string keep = ModDuplicates.PickKeepFile(group.Files);
                        for (int f = 0; f < group.Files.Count; f++)
                        {
                            string name = System.IO.Path.GetFileName(group.Files[f]);
                            warning.Append("    ").Append(name);
                            if (string.Equals(group.Files[f], keep,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                warning.Append("（建议保留）");
                            }
                            else
                            {
                                warning.Append("（建议删除）");
                            }
                            warning.Append("\r\n");
                        }
                    }
                    warning.Append("\r\n请删掉标记为「建议删除」的文件后重试。目录：")
                        .Append(scanDir);
                    Log.Warn("启动前检测到重复模组：" + duplicates.Count + " 组");
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        "检测到重复模组（同一模组装了两份），游戏将无法加载。启动前会列出要删除的具体文件。");
                    MessageBox.Show(warning.ToString(), BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("重复模组检测失败：" + ex.Message);
            }

            LaunchPlan plan = null;
            CancellationToken token = BeginOperation("正在准备启动 " + versionId + "...");
            _progressBar.Marquee = true;
            try
            {
                // 第三方认证账号需要 authlib-injector；缺失时自动下载（可被取消）。
                if (account.Type == AccountType.Authlib &&
                    !AuthlibInjectorService.Exists(_config.AuthlibInjectorPath))
                {
                    SetStatus("正在准备 authlib-injector（第三方认证注入器）...");
                    await AuthlibInjectorService.EnsureInjectorAsync(
                        _config.AuthlibInjectorPath, null, token);
                    token.ThrowIfCancellationRequested();
                    if (IsDisposed || Disposing)
                    {
                        return;
                    }
                }
                plan = await Task.Run(delegate { return GameLauncher.Prepare(options, token); });
                token.ThrowIfCancellationRequested();
                if (IsDisposed || Disposing)
                {
                    return;
                }

                // 异地联机：核心缺失时自动下载，然后加入网络并等待虚拟 IP。
                // 组网失败只提示、不阻止游戏启动——单机仍然可以玩。
                if (_config.EasyTierEnabled)
                {
                    await PrepareEasyTierAsync(token);
                }

                Process process = GameLauncher.Start(plan);
                _gameProcess = process;
                process.Exited += GameProcessExited;
                if (process.HasExited)
                {
                    GameProcessExited(process, EventArgs.Empty);
                }
                else
                {
                    SetStatus("游戏已启动：" + versionId + "（PID " + process.Id + "）");
                    Log.Info("游戏进程已启动，PID " + process.Id + "。");
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warn("启动准备已取消。");
                SetStatus("启动已取消");
            }
            catch (Exception ex)
            {
                Log.Error("启动游戏失败", ex);
                SetStatus("启动游戏失败");
                MessageBox.Show(
                    "启动游戏失败：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // @argfile 参数文件不能在启动后立即删除：Process.Start 只是创建进程，
                // Java 尚未打开该文件，立刻删会造成"找不到参数文件"的偶发启动失败。
                // 真正的清理推迟到游戏进程退出（GameProcessExited）；
                // 若启动失败则在这里清理（游戏没起来，不会再有人读它）。
                if (plan != null && !string.IsNullOrEmpty(plan.ArgFilePath))
                {
                    if (_gameProcess == null || _gameProcess.HasExited)
                    {
                        PathUtil.TryDeleteFile(plan.ArgFilePath);
                    }
                    else
                    {
                        _pendingArgFile = plan.ArgFilePath;
                    }
                }
                EndOperation();
            }
        }

        private void GameProcessExited(object sender, EventArgs e)
        {
            Process process = sender as Process;
            int exitCode = -1;
            if (process != null)
            {
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                }
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    Log.Info("游戏进程已退出，退出码 " + exitCode + "。");
                    // 游戏异常退出时自动分析崩溃日志：用户不必再自己去「日志」页点按钮。
                    // 退出码非 0 或日志里存在新的 crash-report 时才提示，避免正常退出也弹窗。
                    if (exitCode != 0)
                    {
                        TryAutoAnalyzeCrash();
                    }
                    SetStatus("游戏已退出，退出码 " + exitCode);
                    // @argfile 在游戏退出后才能安全删除（Java 可能还没读完）。
                    if (!string.IsNullOrEmpty(_pendingArgFile))
                    {
                        PathUtil.TryDeleteFile(_pendingArgFile);
                        _pendingArgFile = "";
                    }
                    // Process 实现 IDisposable：不释放会累积句柄（用户反复开关启动器时明显）。
                    if (process != null)
                    {
                        try
                        {
                            process.Dispose();
                        }
                        catch
                        {
                        }
                    }
                    _gameProcess = null;
                    UpdateActionButtons();
                }));
            }
            catch
            {
                // 窗口关闭过程中不再更新界面。
            }
        }

        private void CancelButtonClick(object sender, EventArgs e)
        {
            if (_cts == null)
            {
                return;
            }
            try
            {
                _cts.Cancel();
                Log.Warn("用户请求取消当前操作。");
                SetStatus("正在取消...");
            }
            catch
            {
            }
        }

        private CancellationToken BeginOperation(string status)
        {
            _cts = new CancellationTokenSource();
            _busy = true;
            _progressBar.Marquee = false;
            _progressBar.Value = 0;
            _cancelButton.Enabled = true;
            SetStatus(status);
            UpdateActionButtons();
            return _cts.Token;
        }

        private void EndOperation()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (_cts != null)
            {
                _cts.Dispose();
                _cts = null;
            }
            _busy = false;
            _progressBar.Marquee = false;
            _progressBar.Value = 0;
            _cancelButton.Enabled = false;
            UpdateActionButtons();
        }

        private Progress<InstallProgress> CreateProgress()
        {
            return new Progress<InstallProgress>(delegate(InstallProgress progress)
            {
                if (progress == null)
                {
                    return;
                }
                if (IsDisposed || Disposing)
                {
                    return;
                }
                int value = progress.Percent;
                if (value < 0)
                {
                    value = 0;
                }
                if (value > 100)
                {
                    value = 100;
                }
                _progressBar.Marquee = false;
                _progressBar.Value = value;
                SetStatus(progress.Status);
            });
        }

        private async void InstallVanillaClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            VersionSummary version = GetSelectedRemoteVersion();
            if (version == null)
            {
                MessageBox.Show("请先在左侧选择一个 Mojang 版本。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(
                "将下载并安装原版 " + version.Id + " 到：\r\n" + GetGameDirFromUi() +
                "\r\n\r\n是否继续？",
                BrandTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            CancellationToken token = BeginOperation("准备安装原版 " + version.Id + "...");
            try
            {
                MinecraftInstaller installer = CreateInstaller();
                await installer.InstallVanillaAsync(version, CreateProgress(), token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                Log.Info("原版安装完成：" + version.Id);
                RefreshInstalledVersions(version.Id);
                SetStatus("原版安装完成：" + version.Id);
                // 安装成功后自动切到启动页并选中刚装的版本，省去用户手动来回切换。
                ShowPage(0);
                ShowNotice(NoticeBar.NoticeLevel.Success,
                    "原版 " + version.Id + " 安装完成，已选中该版本，可以直接启动。");
            }
            catch (OperationCanceledException)
            {
                Log.Warn("安装已取消：" + version.Id);
                SetStatus("安装已取消");
            }
            catch (Exception ex)
            {
                Log.Error("安装原版失败：" + version.Id, ex);
                SetStatus("安装原版失败");
                MessageBox.Show(
                    "安装原版失败：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void InstallFabricClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            VersionSummary version = GetSelectedRemoteVersion();
            if (version == null)
            {
                MessageBox.Show("请先在左侧选择一个 Mojang 版本，Fabric 会安装在该版本之上。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(
                "将安装 Fabric 最新稳定加载器到原版 " + version.Id + "。\r\n" +
                "如果原版尚未安装，会先下载原版。\r\n\r\n是否继续？",
                BrandTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            CancellationToken token = BeginOperation("准备安装 Fabric...");
            try
            {
                MinecraftInstaller installer = CreateInstaller();
                FabricInstaller fabric = new FabricInstaller(installer, new VersionManifestService());
                string installedId = await fabric.InstallAsync(version.Id, CreateProgress(), token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                Log.Info("Fabric 安装完成：" + installedId);
                RefreshInstalledVersions(installedId);
                SetStatus("Fabric 安装完成：" + installedId);
                ShowPage(0);
                ShowNotice(NoticeBar.NoticeLevel.Success,
                    "Fabric 安装完成（" + installedId + "），已选中该版本。可到「模组」页安装模组。");
            }
            catch (OperationCanceledException)
            {
                Log.Warn("Fabric 安装已取消。");
                SetStatus("安装已取消");
            }
            catch (Exception ex)
            {
                Log.Error("安装 Fabric 失败", ex);
                SetStatus("安装 Fabric 失败");
                MessageBox.Show(
                    "安装 Fabric 失败：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 启动游戏前准备异地联机：确保核心就绪 → 加入网络 → 等待虚拟 IP。
        /// 任一步失败只记录日志与页面提示，不阻断游戏启动。
        /// </summary>
        private async Task PrepareEasyTierAsync(CancellationToken token)
        {
            try
            {
                if (!EasyTierService.Exists(_config.EasyTierBinaryPath))
                {
                    SetStatus("正在下载 EasyTier 核心...");
                    await EasyTierService.EnsureBinaryAsync(
                        _config.EasyTierBinaryPath, null, token);
                    token.ThrowIfCancellationRequested();
                }
                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (string.IsNullOrEmpty(_config.EasyTierNetworkName))
                {
                    Log.Warn("异地联机已启用但未填写网络名，跳过组网。");
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        "异地联机已启用但未填写网络名，请在设置页填写后再启动。");
                    return;
                }
                // 公共节点必填：没有它核心无法发现任何对等节点。
                // 不再提供内置地址（社区节点会变，内置值实测已失效）。
                if (string.IsNullOrEmpty(_config.EasyTierPublicNode))
                {
                    Log.Warn("异地联机已启用但未填写公共节点，跳过组网。");
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        "异地联机需要公共节点地址：请在设置页「公共节点」填入 " +
                        "tcp://节点地址:11010，并点「获取节点地址」查看官方文档给出的当前可用地址。");
                    return;
                }

                SetStatus("正在加入 EasyTier 网络...");

                // 权限预检：创建虚拟网卡需要管理员权限。实测非管理员时核心会报
                // "tun device error: The file is not signed"（不是驱动缺失，
                // 而是低权限下签名策略拒绝加载驱动）。提前告知，避免用户以为功能坏了。
                if (!EasyTierService.IsElevated())
                {
                    Log.Warn("当前非管理员权限，虚拟网卡可能无法创建。");
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        "当前不是以管理员身份运行，无法创建虚拟网卡，因此拿不到虚拟 IP。" +
                        "请关闭启动器后右键选择「以管理员身份运行」再试（游戏本身仍会正常启动）。");
                }

                // 启动前检查端口占用：残留实例会让新核心报 AddrInUse，
                // 用户只看到"未能获取 IP"却不知道原因。
                int busyPort = EasyTierService.FindBusyPort();
                if (busyPort > 0)
                {
                    int external = EasyTierService.CountExternalInstances();
                    string hint = "端口 " + busyPort + " 已被占用";
                    if (external > 0)
                    {
                        hint += "，检测到 " + external + " 个 easytier-core 进程在运行";
                    }
                    Log.Warn(hint + "，异地联机可能失败。");
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        hint + "。请在任务管理器结束 easytier-core.exe 后重试（游戏仍会正常启动）。");
                }

                EasyTierService.StartCore(_config);

                // 等待虚拟 IP：拿到就提示用户，拿不到也只提示不阻塞。
                SetStatus("正在获取虚拟 IP...");
                string ip = await EasyTierService.GetVirtualIPv4Async(0);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                if (string.IsNullOrEmpty(ip))
                {
                    // 失败时把核心的真实原因透出，而不是笼统的"未能获取 IP"。
                    string reason = EasyTierService.DiagnoseFailure();
                    Log.Warn("未能获取虚拟 IP。诊断：" + (reason.Length == 0 ? "无核心输出" : reason));
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                        string.IsNullOrEmpty(reason)
                            ? "未能获取虚拟 IP：核心没有输出可供诊断。可在设置页点「查看核心日志」，或换一个公共节点。"
                            : "异地联机失败：" + reason + "（游戏仍会正常启动）");
                }
                else
                {
                    Log.Info("EasyTier 虚拟 IP：" + ip);
                    SetEasyTierIp(ip);
                    // 关键提醒：组网成功 ≠ 能进所有服务器。
                    // 对方服务器若开了 online-mode（正版验证），离线账号会被拒，
                    // 游戏里表现为"无效会话"。用户常误以为组网失败，这里提前说明。
                    string onlineModeHint = "";
                    Account current = AuthService.GetSelectedAccount(_config);
                    if (current == null || current.Type != AccountType.Authlib)
                    {
                        // 离线账号最常见的坑：开房间时正版验证没关。
                        // 原版「对局域网开放」没有这个开关，装了 mcwifipnp（更高级联机设置）
                        // 才会出现「正版验证」选项，选「禁用」即可允许离线玩家加入。
                        // 按实际是否装有该模组给出不同提示，避免让用户找不存在的选项。
                        string modsDir = GetModsDirectoryFromUi();
                        bool hasLanMod = ModDependencyScanner.IsModInstalled(modsDir, "mcwifipnp");
                        if (hasLanMod)
                        {
                            onlineModeHint = "\r\n提示：两边都用离线账号时，开房间的一方要在" +
                                "「对局域网开放」界面把「正版验证」设为「禁用」" +
                                "（或「禁用 + 修复UUID」）。否则加入方会被提示「无效会话」。" +
                                "这不是联机失败，是房间在拒绝离线账号。";
                        }
                        else
                        {
                            onlineModeHint = "\r\n提示：两边都用离线账号时，若加入方被提示「无效会话」，" +
                                "说明开房间的一方在按正版校验。原版「对局域网开放」没有关闭选项，" +
                                "可安装「更高级联机设置」（mcwifipnp）后把「正版验证」设为「禁用」。";
                        }
                    }
                    ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Info,
                        "已加入网络，本机虚拟 IP：" + ip + "。在游戏内用对方虚拟 IP 直连。" +
                        onlineModeHint);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error("准备异地联机失败", ex);
                ShowNoticeOnPage(0, NoticeBar.NoticeLevel.Warning,
                    "异地联机未成功：" + ex.Message + "。游戏仍会正常启动。");
            }
        }

        /// <summary>把虚拟 IP 显示在设置页，并记录到 Tag 供「复制 IP」使用。</summary>
        private void SetEasyTierIp(string ip)
        {
            if (_easyTierIpLabel == null || _easyTierIpLabel.IsDisposed)
            {
                return;
            }
            _easyTierIpLabel.Tag = ip ?? "";
            _easyTierIpLabel.Text = string.IsNullOrEmpty(ip) ? "尚未加入网络" : ip;
            _easyTierIpLabel.ForeColor = string.IsNullOrEmpty(ip)
                ? UiTheme.TextSecondary
                : UiTheme.Success;
            UpdateEasyTierStatus();
        }

        private MinecraftInstaller CreateInstaller()
        {
            return new MinecraftInstaller(
                GetGameDirFromUi(),
                (int)_threadsBox.Value,
                new VersionManifestService());
        }

        private VersionSummary GetSelectedRemoteVersion()
        {
            if (_remoteList == null || _remoteList.SelectedItems.Count == 0)
            {
                return null;
            }
            return _remoteList.SelectedItems[0].Tag as VersionSummary;
        }

        private string GetGameDirFromUi()
        {
            return PathUtil.NormalizeDirectory(_gameDirBox.Text, AppPaths.DefaultGameDirectory);
        }

        private void SaveConfigFromUi()
        {
            _config.GameDir = GetGameDirFromUi();
            _config.JavaPath = GetSelectedJava() != null ? GetSelectedJava().LaunchPath : "";
            _config.MinMemoryMB = (int)_minMemoryBox.Value;
            _config.MaxMemoryMB = (int)_maxMemoryBox.Value;
            _config.JvmArgs = _jvmBox.Text;
            _config.DownloadThreads = (int)_threadsBox.Value;
            _config.LastVersion = (_versionCombo.SelectedItem as string) ?? "";
            Account currentAccount = GetSelectedAccountFromCombo();
            if (currentAccount != null)
            {
                _config.SelectedAccountId = currentAccount.Id;
                _config.LastUsername = currentAccount.Type == AccountType.Authlib
                    ? currentAccount.DisplayName
                    : currentAccount.UserName;
            }
            _config.ShowSnapshots = _snapshotCheck.Checked;
            _config.CustomResolution = _resolutionCheck.Checked;
            _config.WindowWidth = (int)_widthBox.Value;
            _config.WindowHeight = (int)_heightBox.Value;

            // 1.3.0 新增项
            _config.DownloadMirror = GetMirrorFromUi();
            _config.IsolateVersions = _isolateCheck.Checked;
            _config.EasyTierEnabled = _easyTierEnabledCheck.Checked;
            _config.EasyTierNetworkName = (_easyTierNameBox.Text ?? "").Trim();
            _config.EasyTierNetworkSecret = (_easyTierSecretBox.Text ?? "").Trim();
            _config.EasyTierPublicNode = (_easyTierNodeBox.Text ?? "").Trim();
            _config.EasyTierBinaryPath = (_easyTierBinaryBox.Text ?? "").Trim();
        }

        private void SaveSettingsClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            string gameDir = GetGameDirFromUi();
            try
            {
                Directory.CreateDirectory(gameDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建游戏目录：\r\n" + ex.Message, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _gameDirBox.Text = gameDir;
            _snapshotCheck.Checked = _settingsSnapshotCheck.Checked;

            // 下载源：自定义地址先校验，非法则不保存并提示。
            if (_mirrorCombo.SelectedIndex == 2)
            {
                string custom = (_mirrorCustomBox.Text ?? "").Trim();
                if (!DownloadMirrors.IsValidCustomMirror(custom))
                {
                    ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Error,
                        "自定义镜像地址必须是 https:// 地址（443 端口、合法域名）。");
                    MessageBox.Show(
                        "自定义镜像地址无效，请填写 https:// 开头的合法地址（443 端口）。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 异地联机：启用时必须填网络名。
            if (_easyTierEnabledCheck.Checked &&
                (_easyTierNameBox.Text ?? "").Trim().Length == 0)
            {
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Error,
                    "启用异地联机需要填写网络名（1-64 位字母、数字、- 或 _）。");
                MessageBox.Show(
                    "启用异地联机需要填写网络名（1-64 位字母、数字、- 或 _）。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _easyTierNameBox.Focus();
                return;
            }
            // 启用联机时公共节点也不能为空：缺了它核心发现不到对等节点，
            // 用户会以为功能坏了。
            if (_easyTierEnabledCheck.Checked &&
                (_easyTierNodeBox.Text ?? "").Trim().Length == 0)
            {
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Error,
                    "启用异地联机需要填写公共节点地址（如 tcp://节点地址:11010）。");
                MessageBox.Show(
                    "启用异地联机需要填写公共节点地址。\r\n\r\n" +
                    "格式：tcp://节点地址:11010\r\n" +
                    "公共节点由 EasyTier 社区维护、地址会变动，请点「获取节点地址」" +
                    "查看官方文档给出的当前地址。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _easyTierNodeBox.Focus();
                return;
            }

            SaveConfigFromUi();
            ApplyMirrorFromUi();
            try
            {
                _config.Save();
                Log.Info("设置已保存：游戏目录 " + gameDir);
                SetStatus("设置已保存");
                RefreshInstalledVersions(_config.LastVersion);
                PopulateRemoteList();
                UpdateEasyTierStatus();
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Success,
                    "设置已保存。" + (_config.EasyTierEnabled
                        ? "启动游戏时将自动加入网络 " + _config.EasyTierNetworkName + "。"
                        : ""));
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Error, "保存设置失败：" + ex.Message);
            }
        }

        private void SnapshotCheckChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _updatingUi = true;
            try
            {
                _settingsSnapshotCheck.Checked = _snapshotCheck.Checked;
            }
            finally
            {
                _updatingUi = false;
            }
            _config.ShowSnapshots = _snapshotCheck.Checked;
            PopulateRemoteList();
        }

        /// <summary>
        /// 下载源下拉：官方 / BmclAPI / 自定义。选"自定义"时展开地址输入框。
        /// 切换后立即应用到 HttpUtil，保存设置时再持久化。
        /// </summary>
        private void MirrorComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            bool custom = _mirrorCombo.SelectedIndex == 2;
            if (_mirrorCustomBox != null)
            {
                _mirrorCustomBox.Visible = custom;
            }
            UpdateMirrorHint();
        }

        private void UpdateMirrorHint()
        {
            if (_mirrorHintLabel == null || _mirrorHintLabel.IsDisposed)
            {
                return;
            }
            if (_mirrorCombo.SelectedIndex == 2)
            {
                string value = (_mirrorCustomBox.Text ?? "").Trim();
                _mirrorHintLabel.Text = DownloadMirrors.IsValidCustomMirror(value)
                    ? "将只接管 Mojang / Fabric 的下载地址"
                    : "需为 https:// 地址（443 端口）";
                _mirrorHintLabel.ForeColor = DownloadMirrors.IsValidCustomMirror(value)
                    ? UiTheme.TextSecondary
                    : UiTheme.Danger;
                return;
            }
            _mirrorHintLabel.ForeColor = UiTheme.TextSecondary;
            _mirrorHintLabel.Text = _mirrorCombo.SelectedIndex == 1
                ? "国内镜像，仅覆盖资源下载；哈希校验仍按官方清单"
                : "默认，直连官方源";
        }

        /// <summary>
        /// 读取界面上的下载源取值：official / bmclapi / 自定义 URL。
        /// 自定义项非法时回落为 official，避免把坏地址写进配置。
        /// </summary>
        private string GetMirrorFromUi()
        {
            if (_mirrorCombo.SelectedIndex == 1)
            {
                return DownloadMirrors.BmclApi;
            }
            if (_mirrorCombo.SelectedIndex == 2)
            {
                string value = (_mirrorCustomBox.Text ?? "").Trim();
                if (DownloadMirrors.IsValidCustomMirror(value))
                {
                    return value;
                }
            }
            return DownloadMirrors.Official;
        }

        private void ApplyMirrorFromUi()
        {
            HttpUtil.SetMirror(GetMirrorFromUi());
        }

        /// <summary>
        /// 异地联机开关：勾选后展开网络配置，未勾选时折叠以节省竖向空间。
        /// </summary>
        private void EasyTierEnabledChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            bool enabled = _easyTierEnabledCheck.Checked;
            if (_easyTierConfigPanel != null && !_easyTierConfigPanel.IsDisposed)
            {
                _easyTierConfigPanel.Visible = enabled;
            }
            UpdateEasyTierStatus();
        }

        /// <summary>刷新异地联机的状态行：核心是否就绪、当前虚拟 IP。</summary>
        private void UpdateEasyTierStatus()
        {
            if (_easyTierStatusLabel == null || _easyTierStatusLabel.IsDisposed)
            {
                return;
            }
            bool enabled = _easyTierEnabledCheck != null && _easyTierEnabledCheck.Checked;
            bool ready = EasyTierService.Exists(_config.EasyTierBinaryPath);
            bool running = EasyTierService.IsRunning();
            string binary = EasyTierService.ResolveBinaryPath(_config.EasyTierBinaryPath);

            if (!enabled)
            {
                _easyTierStatusLabel.Text = ready ? "核心已就绪（未启用）" : "未启用";
                _easyTierStatusLabel.ForeColor = UiTheme.TextSecondary;
            }
            else if (!ready)
            {
                _easyTierStatusLabel.Text = "核心未下载，启动游戏时会自动下载";
                _easyTierStatusLabel.ForeColor = UiTheme.Warning;
            }
            else if (running)
            {
                _easyTierStatusLabel.Text = "已加入网络（核心运行中）";
                _easyTierStatusLabel.ForeColor = UiTheme.Success;
            }
            else
            {
                _easyTierStatusLabel.Text = "核心已就绪：" + binary;
                _easyTierStatusLabel.ForeColor = UiTheme.TextSecondary;
            }
        }

        /// <summary>从预设下拉选中节点时，同步到输入框。</summary>
        private void EasyTierNodePresetChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            string selected = _easyTierNodePresetCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(selected) && _easyTierNodeBox != null)
            {
                _easyTierNodeBox.Text = selected;
            }
        }

        /// <summary>
        /// 打开 EasyTier 官方文档，供用户获取当前有效的公共节点地址。
        /// 公共节点由社区维护、地址会变动，启动器不再内置固定值。
        /// </summary>
        private void EasyTierNodeDocsClick(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                startInfo.Arguments = CommandLine.QuoteArgument(EasyTierService.PublicNodeDocsUrl);
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Info,
                    "已打开 EasyTier 官方文档。在文档的「公共服务器」一节可以找到当前的公共节点地址。");
            }
            catch (Exception ex)
            {
                ShowCopyableText("请手动访问官方文档", EasyTierService.PublicNodeDocsUrl +
                    "\r\n\r\n（自动打开浏览器失败：" + ex.Message + "）");
            }
        }

        /// <summary>
        /// 显示核心进程的最近输出与诊断结论。失败时用户能直接看到原因，
        /// 不必去翻日志文件，也不必靠猜。
        /// </summary>
        private void EasyTierLogClick(object sender, EventArgs e)
        {
            string output = EasyTierService.GetRecentOutput(40);
            string diagnosis = EasyTierService.DiagnoseFailure();
            if (string.IsNullOrEmpty(output) && string.IsNullOrEmpty(diagnosis))
            {
                MessageBox.Show(
                    "还没有核心运行输出。\r\n\r\n启用异地联机并启动游戏后，这里会显示核心的日志。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrEmpty(diagnosis))
            {
                builder.Append("诊断结论：\r\n").Append(diagnosis).Append("\r\n\r\n");
            }
            builder.Append("最近的核心输出：\r\n");
            builder.Append(string.IsNullOrEmpty(output) ? "(无)" : output);
            builder.Append("\r\n\r\n启动器日志目录：").Append(AppPaths.LogDirectory);

            string text = builder.ToString();
            if (text.Length > 2000)
            {
                text = text.Substring(text.Length - 2000);
                text = "（仅显示最后 2000 字符）\r\n" + text;
            }
            // 提供复制入口：让用户能把日志贴给他人求助。
            ShowCopyableText("EasyTier 核心日志", text);
        }

        /// <summary>显示一段可复制的文本（只读多行框 + 复制按钮）。</summary>
        private void ShowCopyableText(string title, string text)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(720, 460);
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.Font = UiTheme.FontRegular(9F);
                dialog.BackColor = UiTheme.Background;
                dialog.Icon = Icon;

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Dock = DockStyle.Fill;
                box.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
                box.BackColor = Color.White;
                box.Text = text;

                Panel footer = new Panel();
                footer.Dock = DockStyle.Bottom;
                footer.Height = 46;
                footer.BackColor = UiTheme.Background;
                Button copy = UiTheme.CreateFixedButton("复制到剪贴板", 130, 30);
                copy.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                copy.Location = new Point(0, 8);
                copy.Click += delegate
                {
                    try
                    {
                        Clipboard.SetText(text);
                        SetStatus("日志已复制到剪贴板");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("复制失败：" + ex.Message, BrandTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                Button close = UiTheme.CreateFixedButton("关闭", 80, 30);
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Location = new Point(footer.Width - 90, 8);
                close.Click += delegate { dialog.Close(); };
                footer.Controls.Add(copy);
                footer.Controls.Add(close);
                footer.Resize += delegate
                {
                    close.Location = new Point(footer.Width - 90, 8);
                };

                dialog.Controls.Add(box);
                dialog.Controls.Add(footer);
                dialog.ShowDialog(this);
            }
        }

        private async void EasyTierDownloadClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            CancellationToken token = BeginOperation("正在下载 EasyTier 核心...");
            try
            {
                await EasyTierService.EnsureBinaryAsync(
                    _config.EasyTierBinaryPath, null, token);
                SetStatus("EasyTier 核心已下载并通过 SHA256 校验");
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Success, "EasyTier 核心已就绪。");
                UpdateEasyTierStatus();
            }
            catch (OperationCanceledException)
            {
                SetStatus("EasyTier 核心下载已取消");
            }
            catch (Exception ex)
            {
                Log.Error("下载 EasyTier 核心失败", ex);
                SetStatus("EasyTier 核心下载失败");
                ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Error,
                    "下载 EasyTier 核心失败：" + ex.Message);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>选择本地 easytier-core.exe（高信任用户可自行核验后使用）。</summary>
        private void BrowseEasyTierBinaryClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 easytier-core.exe";
                dialog.Filter = "EasyTier 核心 (easytier-core.exe)|easytier-core.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _easyTierBinaryBox.Text = dialog.FileName;
                UpdateEasyTierStatus();
            }
        }

        /// <summary>
        /// 把当前虚拟 IP 复制到剪贴板，方便直接粘进游戏多人游戏地址框。
        /// </summary>
        private void EasyTierCopyIpClick(object sender, EventArgs e)
        {
            try
            {
                string ip = (_easyTierIpLabel == null) ? "" : (_easyTierIpLabel.Tag as string);
                if (string.IsNullOrEmpty(ip))
                {
                    ip = EasyTierService.FindVirtualIpOnAdapters();
                }
                if (string.IsNullOrEmpty(ip))
                {
                    ShowNoticeOnPage(4, NoticeBar.NoticeLevel.Info,
                        "尚未获取到虚拟 IP，请先启动游戏或加入网络。");
                    return;
                }
                Clipboard.SetText(ip);
                SetStatus("已复制虚拟 IP：" + ip);
            }
            catch (Exception ex)
            {
                SetStatus("复制失败：" + ex.Message);
            }
        }

        private void SettingsSnapshotCheckChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }            _updatingUi = true;
            try
            {
                _snapshotCheck.Checked = _settingsSnapshotCheck.Checked;
            }
            finally
            {
                _updatingUi = false;
            }
            _config.ShowSnapshots = _settingsSnapshotCheck.Checked;
            PopulateRemoteList();
        }

        /// <summary>
        /// 切换深色主题。UiTheme 的色值都是静态属性，切换后必须重建界面才能生效。
        /// 重建会保留当前选中的页面与已加载的版本信息。
        /// </summary>
        private void DarkThemeCheckChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            bool dark = _darkThemeCheck.Checked;
            _config.DarkTheme = dark;
            UiTheme.SetDark(dark);
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("保存主题设置失败：" + ex.Message);
            }
            ApplyTheme();
        }

        /// <summary>
        /// 重建整个界面以应用当前主题，并恢复页面与状态。
        /// </summary>
        private void ApplyTheme()
        {
            int selectedPage = 0;
            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages[i].Visible)
                {
                    selectedPage = i;
                }
            }
            string selectedVersion = _versionCombo.SelectedItem as string;
            string selectedRemote = "";
            if (_remoteList.SelectedItems.Count > 0)
            {
                VersionSummary summary = _remoteList.SelectedItems[0].Tag as VersionSummary;
                if (summary != null)
                {
                    selectedRemote = summary.Id;
                }
            }

            SuspendLayout();
            try
            {
                UpdateActionButtons();
                Controls.Clear();
                _pages.Clear();
                _navButtons.Clear();
                BuildUi();
                ApplyConfig();
                _updatingUi = true;
                try
                {
                    _darkThemeCheck.Checked = _config.DarkTheme;
                }
                finally
                {
                    _updatingUi = false;
                }
                if (!string.IsNullOrEmpty(selectedVersion) &&
                    _versionCombo.Items.Contains(selectedVersion))
                {
                    _versionCombo.SelectedItem = selectedVersion;
                }
                PopulateRemoteList();
                ShowPage(selectedPage);
            }
            finally
            {
                ResumeLayout(true);
            }
            SetStatus("已切换为" + (UiTheme.IsDark ? "深色" : "浅色") + "主题");
        }

        private void RemoteListSelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateActionButtons();
        }

        private void RefreshInstalledClick(object sender, EventArgs e)
        {
            RefreshInstalledVersions(_versionCombo.SelectedItem as string);
            SetStatus("已刷新本地版本列表");
        }

        private void InstalledListDoubleClick(object sender, EventArgs e)
        {
            if (_installedList.SelectedItems.Count == 0)
            {
                return;
            }
            string id = _installedList.SelectedItems[0].Text;
            _versionCombo.SelectedItem = id;
            ShowPage(0);
        }

        private void BrowseGameDirClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 Minecraft 游戏目录（.minecraft）";
                dialog.SelectedPath = GetGameDirFromUi();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _gameDirBox.Text = Path.GetFullPath(dialog.SelectedPath);
                    RefreshInstalledVersions("");
                }
            }
        }

        private void OpenGameDirClick(object sender, EventArgs e)
        {
            OpenFolder(GetGameDirFromUi());
        }

        private void OpenVersionsClick(object sender, EventArgs e)
        {
            OpenFolder(Path.Combine(GetGameDirFromUi(), "versions"));
        }

        private void OpenModsClick(object sender, EventArgs e)
        {
            OpenFolder(GetModsDirectoryFromUi());
        }

        /// <summary>
        /// 刷新「已安装模组」列表。
        /// 直接按目录内容列出（不联网），因此打开模组页就能看到实际装了哪些。
        /// </summary>
        private void RefreshInstalledMods()
        {
            if (_installedModList == null || _installedModList.IsDisposed)
            {
                return;
            }
            _installedModList.BeginUpdate();
            try
            {
                _installedModList.Items.Clear();
                List<InstalledMod> mods = ModManager.List(GetModsDirectoryFromUi());
                for (int i = 0; i < mods.Count; i++)
                {
                    InstalledMod mod = mods[i];
                    ListViewItem item = new ListViewItem(mod.DisplayName);
                    item.SubItems.Add(string.IsNullOrEmpty(mod.Version) ? "—" : mod.Version);
                    item.SubItems.Add(mod.Enabled ? "已启用" : "已禁用");
                    // 禁用的条目用灰色，让用户一眼看出哪些不会生效。
                    if (!mod.Enabled)
                    {
                        item.ForeColor = UiTheme.TextMuted;
                    }
                    item.Tag = mod;
                    _installedModList.Items.Add(item);
                }
                if (mods.Count == 0)
                {
                    _modStatusLabel.Text = "当前版本目录里还没有模组：" +
                        GetModsDirectoryFromUi();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("列出已安装模组失败：" + ex.Message);
            }
            finally
            {
                _installedModList.EndUpdate();
            }
            UpdateActionButtons();
        }

        private void RefreshInstalledModsClick(object sender, EventArgs e)
        {
            RefreshInstalledMods();
            SetStatus("模组列表已刷新");
        }

        private void InstalledModSelectedChanged(object sender, EventArgs e)
        {
            UpdateActionButtons();
        }

        /// <summary>禁用/启用选中的模组。禁用的实现是把扩展名改成 .jar.disabled。</summary>
        private void ToggleModClick(object sender, EventArgs e)
        {
            if (_installedModList == null || _installedModList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在「已安装模组」中选中要操作的条目。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int toggled = 0;
            StringBuilder errors = new StringBuilder();
            for (int i = 0; i < _installedModList.SelectedItems.Count; i++)
            {
                InstalledMod mod = _installedModList.SelectedItems[i].Tag as InstalledMod;
                if (mod == null)
                {
                    continue;
                }
                try
                {
                    if (mod.Enabled)
                    {
                        ModManager.Disable(mod.FilePath);
                    }
                    else
                    {
                        ModManager.Enable(mod.FilePath);
                    }
                    toggled++;
                }
                catch (Exception ex)
                {
                    errors.Append("· ").Append(mod.DisplayName).Append("：")
                        .Append(ex.Message).Append("\r\n");
                    Log.Warn("切换模组状态失败：" + mod.FilePath + "，" + ex.Message);
                }
            }
            RefreshInstalledMods();
            if (errors.Length > 0)
            {
                MessageBox.Show("部分模组操作失败：\r\n\r\n" + errors, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus("已切换 " + toggled + " 个模组的状态（禁用后需重启游戏生效）");
            }
        }

        /// <summary>删除选中的模组文件。删除需要二次确认，避免误触丢文件。</summary>
        private void DeleteModClick(object sender, EventArgs e)
        {
            if (_installedModList == null || _installedModList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在「已安装模组」中选中要删除的条目。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < _installedModList.SelectedItems.Count; i++)
            {
                InstalledMod mod = _installedModList.SelectedItems[i].Tag as InstalledMod;
                if (mod != null)
                {
                    names.Append("· ").Append(System.IO.Path.GetFileName(mod.FilePath)).Append("\r\n");
                }
            }
            if (MessageBox.Show(
                "将永久删除以下文件（不进入回收站）：\r\n\r\n" + names +
                "\r\n如果只是暂时不想用，建议点「禁用 / 启用」而不是删除。\r\n\r\n确定删除吗？",
                BrandTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            int deleted = 0;
            StringBuilder errors = new StringBuilder();
            string modsDirectory = GetModsDirectoryFromUi();
            for (int i = 0; i < _installedModList.SelectedItems.Count; i++)
            {
                InstalledMod mod = _installedModList.SelectedItems[i].Tag as InstalledMod;
                if (mod == null)
                {
                    continue;
                }
                try
                {
                    ModManager.Delete(modsDirectory, mod.FilePath);
                    deleted++;
                }
                catch (Exception ex)
                {
                    errors.Append("· ").Append(mod.DisplayName).Append("：")
                        .Append(ex.Message).Append("\r\n");
                    Log.Warn("删除模组失败：" + mod.FilePath + "，" + ex.Message);
                }
            }
            RefreshInstalledMods();
            if (errors.Length > 0)
            {
                MessageBox.Show("部分模组删除失败：\r\n\r\n" + errors, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus("已删除 " + deleted + " 个模组文件");
            }
        }

        private void OpenDataClick(object sender, EventArgs e)
        {
            OpenFolder(AppPaths.DataDirectory);
        }

        private void OpenLogClick(object sender, EventArgs e)
        {
            OpenFolder(AppPaths.LogDirectory);
        }

        /// <summary>
        /// 分析游戏目录下的崩溃报告与最新日志，把已知错误模式归纳成
        /// "问题 + 证据 + 建议"，结果直接显示在日志页，也写入启动器日志备查。
        /// </summary>
        private void AnalyzeCrashClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            try
            {
                SetStatus("正在分析崩溃日志...");
                string gameDir = GetGameDirFromUi();
                CrashReport report = CrashAnalyzer.Analyze(gameDir);
                string text = CrashAnalyzer.Format(report);
                Log.Info("崩溃日志分析完成，来源：" + (report.SourceFile ?? "无") +
                         "，结论数：" + report.Findings.Count);

                _logBox.Clear();
                _logBox.AppendText(text);
                if (_logEmptyState != null && !_logEmptyState.IsDisposed)
                {
                    _logEmptyState.HideState();
                }

                if (report.SourceFile == null)
                {
                    SetStatus("未找到崩溃报告或游戏日志");
                    ShowNotice(NoticeBar.NoticeLevel.Info,
                        "没有找到崩溃报告或 latest.log，请先启动一次游戏。");
                }
                else if (report.Findings.Count == 0)
                {
                    SetStatus("未发现已知错误模式");
                    ShowNotice(NoticeBar.NoticeLevel.Success,
                        "未在日志中发现已知错误模式，可以点「打开日志目录」查看完整内容。");
                }
                else
                {
                    SetStatus("发现 " + report.Findings.Count + " 个可定位的问题");
                    ShowNotice(NoticeBar.NoticeLevel.Warning,
                        "发现 " + report.Findings.Count + " 个可定位的问题，详细建议已写入上方日志区。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("分析崩溃日志失败", ex);
                SetStatus("分析崩溃日志失败");
                ShowNotice(NoticeBar.NoticeLevel.Error, "分析崩溃日志失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 游戏异常退出后自动分析崩溃日志并弹窗提示。
        ///
        /// 用户在最需要帮助的时候（游戏崩了）往往不知道该去哪找日志——
        /// 以前必须自己切到「日志」页点「分析崩溃日志」。现在退出即自动分析。
        ///
        /// 只在退出码非 0 时触发，且分析不出结论时不打扰用户。
        /// </summary>
        private void TryAutoAnalyzeCrash()
        {
            try
            {
                string gameDir = GetGameDirFromUi();
                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    return;
                }
                CrashReport report = CrashAnalyzer.Analyze(gameDir);
                if (report.SourceFile == null || report.Findings.Count == 0)
                {
                    // 没有崩溃报告，或没有匹配到已知错误模式：安静地记录到日志即可。
                    if (report.SourceFile != null)
                    {
                        Log.Info("游戏异常退出，未在日志中匹配到已知错误模式。");
                    }
                    return;
                }

                string text = CrashAnalyzer.Format(report);
                Log.Info("游戏异常退出，已自动分析崩溃日志：\\r\\n" + text);

                // 同时写到日志页，用户随时可以回来看完整结论。
                if (_logBox != null && !_logBox.IsDisposed)
                {
                    _logBox.AppendText(text + Environment.NewLine);
                    if (_logEmptyState != null && !_logEmptyState.IsDisposed)
                    {
                        _logEmptyState.HideState();
                    }
                }

                // 弹窗只给要点，完整建议留在日志页（弹窗太长反而看不清）。
                StringBuilder brief = new StringBuilder();
                brief.Append("游戏异常退出，已自动分析崩溃日志。\\r\\n\\r\\n");
                brief.Append("发现 ").Append(report.Findings.Count).Append(" 个可定位的问题：\\r\\n");
                for (int i = 0; i < report.Findings.Count && i < 5; i++)
                {
                    brief.Append("\\r\\n· ").Append(report.Findings[i].Title)
                           .Append("（").Append(report.Findings[i].Severity).Append("）\\r\\n");
                    brief.Append("  ").Append(report.Findings[i].Advice).Append("\\r\\n");
                }
                if (report.Findings.Count > 5)
                {
                    brief.Append("\\r\\n…… 其余 ").Append(report.Findings.Count - 5)
                           .Append(" 个问题见「日志」页。");
                }
                brief.Append("\\r\\n\\r\\n完整建议已写入启动器的「日志」页。");

                MessageBox.Show(brief.ToString(), BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                // 自动分析失败不能影响游戏退出流程。
                Log.Warn("自动分析崩溃日志失败：" + ex.Message);
            }
        }

        private void ClearLogClick(object sender, EventArgs e)
        {
            if (_logBox != null && !_logBox.IsDisposed)
            {
                _logBox.Clear();
            }
            if (_logEmptyState != null && !_logEmptyState.IsDisposed)
            {
                _logEmptyState.ShowState(
                    "暂无运行日志",
                    "启动器运行后，操作记录会实时显示在这里。",
                    "打开日志目录");
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                startInfo.Arguments = CommandLine.QuoteArgument(path);
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log.Error("打开目录失败：" + path, ex);
                MessageBox.Show("打开目录失败：" + ex.Message, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void RefreshManifestClick(object sender, EventArgs e)
        {
            await RefreshManifestAsync();
        }

        // ============================ 模组下载 ============================

        private string GetSelectedFabricVersionId()
        {
            return _modVersionCombo.SelectedItem as string;
        }

        /// <summary>
        /// 取模组页应使用的 mods 目录。
        ///
        /// 关键：必须与"游戏实际读取的目录"一致。开启版本隔离后游戏的
        /// --gameDir 是实例目录，只读 versions\&lt;版本&gt;\instance\mods；
        /// 若这里仍返回 &lt;GameDir&gt;\mods，模组会下载成功却完全不生效。
        /// </summary>
        private string GetModsDirectoryFromUi()
        {
            return InstancePaths.GetModsDirectory(
                GetGameDirFromUi(),
                GetSelectedFabricVersionId(),
                _config.IsolateVersions);
        }

        /// <summary>
        /// 在 mods 目录中查找某个模组项目的旧版本 JAR。
        /// 采用"slug 出现在文件名里"的宽松匹配，仅用于界面提示"可更新"，
        /// 不会据此删除任何文件；实际覆盖只发生在按精确文件名下载时。
        /// </summary>
        private static string FindOutdatedModFile(string modsDirectory, string slug)
        {
            if (string.IsNullOrEmpty(modsDirectory) || string.IsNullOrEmpty(slug) ||
                !Directory.Exists(modsDirectory))
            {
                return null;
            }
            string[] jars;
            try
            {
                // 同时包含被禁用的模组：用户禁用旧版 sodium、保留新版时，
                // 只扫 .jar 会看不到旧版仍然在位，"可更新"提示就不准。
                jars = InstancePaths.ListModFiles(modsDirectory);
            }
            catch
            {
                return null;
            }
            for (int i = 0; i < jars.Length; i++)
            {
                string name = Path.GetFileName(jars[i]);
                if (name.IndexOf(slug, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return name;
                }
            }
            return null;
        }

        private string GetFabricGameVersion(string fabricVersionId)
        {
            if (string.IsNullOrEmpty(fabricVersionId))
            {
                return "";
            }
            try
            {
                EffectiveVersion version = VersionResolver.LoadInstalledVersion(
                    GetGameDirFromUi(), fabricVersionId);
                return version != null ? (version.GameVersionId ?? "") : "";
            }
            catch (Exception ex)
            {
                Log.Warn("读取 Fabric 版本信息失败：" + fabricVersionId + "，" + ex.Message);
                return "";
            }
        }

        private async void RefreshModsClick(object sender, EventArgs e)
        {
            await RefreshModStatusAsync();
        }

        /// <summary>
        /// 把模组状态刷新延迟到当前事件处理结束之后，避免在构造或下拉框事件里重入网络请求。
        /// </summary>
        private void QueueModStatusRefresh()
        {
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                // 有意不等待：状态刷新失败只会写入日志，不应阻塞界面。
                Task ignored = RefreshModStatusAsync();
            }));
        }

        private async Task RefreshModStatusAsync()
        {
            if (IsDisposed || Disposing || _busy)
            {
                return;
            }
            if (_modsCts != null)
            {
                try
                {
                    _modsCts.Cancel();
                }
                catch
                {
                }
                _modsCts.Dispose();
                _modsCts = null;
            }
            _modsCts = new CancellationTokenSource();
            CancellationToken token = _modsCts.Token;

            string fabricVersionId = GetSelectedFabricVersionId();
            string gameVersion = GetFabricGameVersion(fabricVersionId);
            string modsDirectory = GetModsDirectoryFromUi();
            if (string.IsNullOrEmpty(gameVersion))
            {
                for (int i = 0; i < _modList.Items.Count; i++)
                {
                    _modList.Items[i].SubItems[2].Text = "需要 Fabric 版本";
                }
                _modStatusLabel.Text = "尚未选择可用的 Fabric 版本，请先在「下载」页安装 Fabric。";
                return;
            }

            _modStatusLabel.Text = "正在查询兼容 " + gameVersion + " 的版本...";
            _refreshModsButton.Enabled = false;
            ModrinthService service = new ModrinthService();
            int installedCount = 0;
            try
            {
                for (int i = 0; i < ModCatalog.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string status;
                    try
                    {
                        ModVersion best = await service.GetBestVersionAsync(
                            ModCatalog[i].Slug, gameVersion, token);
                        if (best == null)
                        {
                            status = "无兼容 " + gameVersion + " 的版本";
                        }
                        else
                        {
                            ModVersionFile file = ModrinthService.SelectPrimaryFile(best);
                            if (file == null)
                            {
                                status = "该版本缺少 JAR 文件";
                            }
                            else if (File.Exists(Path.Combine(modsDirectory, file.FileName)))
                            {
                                status = "已安装 " + best.VersionNumber;
                                installedCount++;
                            }
                            else
                            {
                                // 当前版本未安装，但目录里可能已有该模组的旧版本 —— 提示可更新。
                                string outdated = FindOutdatedModFile(modsDirectory, ModCatalog[i].Slug);
                                if (outdated != null)
                                {
                                    status = "可更新 → " + best.VersionNumber;
                                }
                                else
                                {
                                    status = "可下载 " + best.VersionNumber;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        status = "查询失败";
                        Log.Warn("查询 Mod 状态失败：" + ModCatalog[i].Slug + "，" + ex.Message);
                    }
                    if (IsDisposed || Disposing)
                    {
                        return;
                    }
                    if (i < _modList.Items.Count)
                    {
                        _modList.Items[i].SubItems[2].Text = status;
                    }
                }
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _modStatusLabel.Text = "Minecraft " + gameVersion + "：已安装 " + installedCount + " / " +
                                       ModCatalog.Count + " 个常用模组";
                SetStatus("模组状态已刷新");
                // 联机模组的主动提醒：装没装直接关系到"离线账号能不能联机"。
                // 用本地检测（读 jar 内的 fabric.mod.json），不再发网络请求。
                if (!ModDependencyScanner.IsModInstalled(modsDirectory, "mcwifipnp"))
                {
                    ShowNoticeOnPage(3, NoticeBar.NoticeLevel.Warning,
                        "还没装「更高级联机设置」(mcwifipnp)。原版「对局域网开放」没有" +
                        "「正版验证」开关，离线账号联机时加入方会报「无效会话」。" +
                        "在左侧列表选中第一个模组点「安装选中模组」即可。");
                }
                else
                {
                    ShowNoticeOnPage(3, NoticeBar.NoticeLevel.Info,
                        "「更高级联机设置」已安装。开房间时在「对局域网开放」界面把" +
                        "「正版验证」设为「禁用」，离线账号就能正常加入。");
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!IsDisposed && !Disposing && _refreshModsButton != null)
                {
                    _refreshModsButton.Enabled = !_busy;
                }
                UpdateActionButtons();
            }
        }

        private void ModListSelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateActionButtons();
        }

        private async void InstallSelectedModClick(object sender, EventArgs e)
        {
            if (_modList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在列表中选择一个模组。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            object tag = _modList.SelectedItems[0].Tag;
            if (!(tag is int))
            {
                return;
            }
            int index = (int)tag;
            if (index < 0 || index >= ModCatalog.Count)
            {
                return;
            }
            await InstallModAsync(ModCatalog[index].Slug, ModCatalog[index].Title);
        }

        private async void InstallSearchModClick(object sender, EventArgs e)
        {
            if (_modSearchList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在搜索结果中选择一个模组。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ModProject project = _modSearchList.SelectedItems[0].Tag as ModProject;
            if (project == null)
            {
                return;
            }
            string slug = string.IsNullOrEmpty(project.Slug) ? project.Id : project.Slug;
            string title = string.IsNullOrEmpty(project.Title) ? slug : project.Title;
            await InstallModAsync(slug, title);
        }

        /// <summary>
        /// 安装本地 Mod JAR：用户从别处（整合包、朋友分享、手动下载）拿到的模组文件，
        /// 原本无处安放。现在可以直接选一个本地 .jar 装进 mods 目录。
        ///
        /// 只安装**单个** jar，不解压、不执行、不递归目录——安全边界比整合包导入严格得多。
        /// </summary>
        private void InstallLocalModClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择本地 Mod JAR";
                dialog.Filter = "模组 JAR (*.jar)|*.jar|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string jarPath = dialog.FileName;
                string problem = LocalModInstaller.ValidateJarPath(jarPath);
                if (problem.Length > 0)
                {
                    MessageBox.Show("无法安装：" + problem, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 读元数据（只读 jar 内的小文件，不加载类、不执行代码）。
                LocalModInfo info = LocalModInstaller.ReadInfo(jarPath);
                string modsDirectory = GetModsDirectoryFromUi();

                StringBuilder message = new StringBuilder();
                message.Append("将安装这个本地模组：\r\n\r\n");
                message.Append("文件：").Append(info.FileName).Append("\r\n");
                message.Append("识别：").Append(info.Describe()).Append("\r\n");
                if (info.Depends.Count > 0)
                {
                    List<string> installed = CollectInstalledModIds(modsDirectory);
                    List<string> missing =
                        LocalModInstaller.FindMissingDependencies(info, installed);
                    if (missing.Count > 0)
                    {
                        message.Append("\r\n缺少必需前置（这些不会自动安装，需要手动处理）：\r\n");
                        for (int i = 0; i < missing.Count; i++)
                        {
                            message.Append("  · ").Append(missing[i]).Append("\r\n");
                        }
                    }
                    else if (info.Depends.Count > 0)
                    {
                        message.Append("\r\n必需前置：").Append(info.Depends.Count)
                            .Append(" 个，均已安装。\r\n");
                    }
                }
                message.Append("\r\n安装位置：").Append(modsDirectory).Append("\r\n");

                if (MessageBox.Show(message.ToString(), BrandTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    string installedName = LocalModInstaller.Install(modsDirectory, jarPath);
                    MessageBox.Show(
                        "已安装：" + installedName + "\r\n\r\n" +
                        "如果游戏提示缺少前置，请把前置模组也用「安装本地 Mod JAR」装上，" +
                        "或从模组页搜索安装。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshInstalledMods();
                }
                catch (Exception ex)
                {
                    Log.Error("安装本地 Mod 失败", ex);
                    MessageBox.Show("安装失败：" + ex.Message, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// 导出模组预设：把当前 mods 目录里的模组清单写成 JSON 文件，
        /// 发给朋友，对方导入就能装出几乎一样的组合。
        /// </summary>
        private void ExportPresetClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            string modsDirectory = GetModsDirectoryFromUi();
            if (!Directory.Exists(modsDirectory))
            {
                MessageBox.Show("mods 目录还不存在，没有可导出的模组。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ModPreset preset = ModPreset.FromModsDirectory(
                modsDirectory, GetFabricGameVersion(GetSelectedFabricVersionId()));
            if (preset.Mods.Count == 0)
            {
                MessageBox.Show(
                    "mods 目录里没有可导出的模组。\r\n\r\n" +
                    "只有能读出 fabric.mod.json（也就是 Fabric 模组）的文件才会被收录。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出模组预设";
                dialog.Filter = "模组预设 (*" + ModPreset.FileExtension + ")|*" +
                                ModPreset.FileExtension + "|所有文件 (*.*)|*.*";
                dialog.FileName = "minecraft-mods" + ModPreset.FileExtension;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    preset.ExportTo(dialog.FileName);
                    MessageBox.Show(
                        "已导出 " + preset.Mods.Count + " 个模组到：\r\n" + dialog.FileName +
                        "\r\n\r\n把这个文件发给朋友，他用「导入模组预设」就能装出一样的组合。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log.Error("导出模组预设失败", ex);
                    MessageBox.Show("导出失败：" + ex.Message, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// 导入模组预设：按清单批量安装。
        /// 单个模组失败不中断整批——预设里常有一两个不兼容的，用户更希望"能装的都装上"。
        /// </summary>
        private async void ImportPresetClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            string fabricVersionId = GetSelectedFabricVersionId();
            if (string.IsNullOrEmpty(fabricVersionId))
            {
                MessageBox.Show(
                    "请先在「下载」页安装 Fabric 版本，再导入模组预设。\r\n\r\n" +
                    "预设需要按 Minecraft 版本挑选兼容的模组版本。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string gameVersion = GetFabricGameVersion(fabricVersionId);
            if (string.IsNullOrEmpty(gameVersion))
            {
                MessageBox.Show("无法读取该 Fabric 版本对应的 Minecraft 版本，请重新安装 Fabric。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入模组预设";
                dialog.Filter = "模组预设 (*" + ModPreset.FileExtension + ")|*" +
                                ModPreset.FileExtension + "|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    ModPreset preset = ModPreset.LoadFrom(dialog.FileName);
                    string problem = preset.Validate();
                    if (problem.Length > 0)
                    {
                        MessageBox.Show("预设无法使用：" + problem, BrandTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    StringBuilder summary = new StringBuilder();
                    summary.Append("预设包含 ").Append(preset.Mods.Count).Append(" 个模组");
                    if (!string.IsNullOrEmpty(preset.MinecraftVersion))
                    {
                        summary.Append("，对应 Minecraft ").Append(preset.MinecraftVersion);
                    }
                    summary.Append("。\r\n\r\n");
                    summary.Append("将按当前选中的版本（").Append(gameVersion)
                        .Append("）挑选每个模组的兼容版本并安装。\r\n");
                    summary.Append("没有兼容版本的模组会被跳过，其余照常安装。\r\n\r\n");
                    summary.Append("开始安装？");

                    if (MessageBox.Show(summary.ToString(), BrandTitle,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return;
                    }

                    List<string> slugs = new List<string>();
                    for (int i = 0; i < preset.Mods.Count; i++)
                    {
                        slugs.Add(preset.Mods[i].Slug);
                    }

                    CancellationToken token = BeginOperation("正在按预设安装模组...");
                    try
                    {
                        ModrinthService service = new ModrinthService();
                        ModBatchInstallResult result = await service.InstallManyAsync(
                            GetGameDirFromUi(),
                            gameVersion,
                            slugs,
                            CreateProgress(),
                            token);
                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        StringBuilder done = new StringBuilder();
                        done.Append("导入完成。\r\n\r\n");
                        done.Append("成功安装 ").Append(result.Installed.Count).Append(" 个模组");
                        if (result.AlreadyFiles.Count > 0)
                        {
                            done.Append("（其中 ").Append(result.AlreadyFiles.Count)
                                .Append(" 个之前就装好了）");
                        }
                        done.Append("。\r\n");
                        if (result.Failed.Count > 0)
                        {
                            done.Append("\r\n以下 ").Append(result.Failed.Count)
                                .Append(" 个没有装上：\r\n");
                            for (int i = 0; i < result.Failed.Count; i++)
                            {
                                done.Append("  · ").Append(result.Failed[i].Slug)
                                    .Append(" —— ").Append(result.Failed[i].Reason)
                                    .Append("\r\n");
                            }
                        }
                        MessageBox.Show(done.ToString(), BrandTitle,
                            MessageBoxButtons.OK,
                            result.Failed.Count > 0
                                ? MessageBoxIcon.Warning
                                : MessageBoxIcon.Information);
                        RefreshInstalledMods();
                    }
                    catch (OperationCanceledException)
                    {
                        SetStatus("已取消按预设安装模组");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("按预设安装模组失败", ex);
                        MessageBox.Show("导入失败：" + ex.Message, BrandTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        EndOperation();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("导入模组预设失败", ex);
                    MessageBox.Show("导入失败：" + ex.Message, BrandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// 收集已安装模组的 mod id（读 jar 内的 fabric.mod.json，不执行任何代码）。
        /// </summary>
        private static List<string> CollectInstalledModIds(string modsDirectory)
        {
            List<string> ids = new List<string>();
            string[] files = InstancePaths.ListModFiles(modsDirectory);
            for (int i = 0; i < files.Length; i++)
            {
                string id = ModDuplicates.ReadModId(files[i]);
                if (!string.IsNullOrEmpty(id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        private async Task InstallModAsync(string slug, string displayName)
        {
            if (_busy)
            {
                return;
            }
            string fabricVersionId = GetSelectedFabricVersionId();
            if (string.IsNullOrEmpty(fabricVersionId))
            {
                MessageBox.Show(
                    "请先在「下载」页安装 Fabric 版本，再安装模组。\r\n\r\n" +
                    "选择已安装的 Fabric 版本后，启动器会自动匹配对应的 Minecraft 版本。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            string gameVersion = GetFabricGameVersion(fabricVersionId);
            if (string.IsNullOrEmpty(gameVersion))
            {
                MessageBox.Show("无法读取该 Fabric 版本对应的 Minecraft 版本，请重新安装 Fabric。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 一致性提醒：模组装到哪个版本由"模组页选中的 Fabric 版本"决定，
            // 而启动游戏用的是"启动页选中的版本"。两者不一致时模组装了也不会生效，
            // 用户很难自己发现，因此在提示里明确对比。
            string launchVersionId = "";
            if (_versionCombo != null && _versionCombo.SelectedItem != null)
            {
                // 版本下拉框里存的是版本名字符串
                launchVersionId = (_versionCombo.SelectedItem as string) ?? "";
            }
            bool versionMismatch = !string.IsNullOrEmpty(launchVersionId) &&
                !string.Equals(launchVersionId, fabricVersionId, StringComparison.OrdinalIgnoreCase);
            // 安装前检查冲突与缺失依赖：只提示、不强制阻止，保留原有宽松安装行为。
            string modsDirectory = GetModsDirectoryFromUi();
            List<ModCheckItem> checks = ModDependencyScanner.Scan(
                modsDirectory, null, slug, null, null);
            string mismatchNote = versionMismatch
                ? "\r\n\r\n⚠ 注意：模组会装到「" + fabricVersionId + "」，" +
                  "但你当前在启动页选中的是「" + launchVersionId + "」。" +
                  "启动后者时读不到这些模组。若要用模组，请把启动页的版本也切到「" +
                  fabricVersionId + "」。"
                : "";
            if (checks.Count > 0)
            {
                string detail = ModDependencyScanner.Format(checks);
                if (MessageBox.Show(
                    detail + "\r\n\r\n目标：" + displayName + "（Minecraft " + gameVersion +
                    " / Fabric）\r\n安装到：" + modsDirectory + mismatchNote,
                    BrandTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
            else if (MessageBox.Show(
                "将安装 " + displayName + "（Minecraft " + gameVersion + " / Fabric）到：\r\n" +
                modsDirectory +
                "\r\n\r\n必需前置模组会自动一并安装。" + mismatchNote +
                "\r\n是否继续？",
                BrandTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            CancellationToken token = BeginOperation("准备下载 Mod：" + displayName + "...");
            try
            {
                ModrinthService service = new ModrinthService();
                ModInstallResult result = await service.InstallAsync(
                    GetGameDirFromUi(), gameVersion, slug, CreateProgress(), token);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                Log.Info("Mod 安装完成：" + displayName + " → " +
                         string.Join(", ", result.Files.ToArray()));
                SetStatus("Mod 安装完成：" + displayName);
            }
            catch (OperationCanceledException)
            {
                Log.Warn("Mod 安装已取消：" + displayName);
                SetStatus("Mod 安装已取消");
            }
            catch (Exception ex)
            {
                Log.Error("Mod 安装失败：" + displayName, ex);
                SetStatus("Mod 安装失败");
                MessageBox.Show(
                    "安装 Mod 失败：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                EndOperation();
            }
            await RefreshModStatusAsync();
        }

        private async void InstallCommonModsClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            string fabricVersionId = GetSelectedFabricVersionId();
            if (string.IsNullOrEmpty(fabricVersionId))
            {
                MessageBox.Show(
                    "请先在「下载」页安装 Fabric 版本，再安装模组。",
                    BrandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            string gameVersion = GetFabricGameVersion(fabricVersionId);
            if (string.IsNullOrEmpty(gameVersion))
            {
                MessageBox.Show("无法读取该 Fabric 版本对应的 Minecraft 版本，请重新安装 Fabric。",
                    BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<RecommendedMod> common = new List<RecommendedMod>();
            for (int i = 0; i < ModCatalog.Count; i++)
            {
                if (ModCatalog[i].Common)
                {
                    common.Add(ModCatalog[i]);
                }
            }
            List<string> names = new List<string>();
            for (int i = 0; i < common.Count; i++)
            {
                names.Add(common[i].Title);
            }
            if (MessageBox.Show(
                "将一键安装：" + string.Join("、", names.ToArray()) +
                "\r\n目标版本：Minecraft " + gameVersion + " / Fabric" +
                "\r\n\r\n是否继续？",
                BrandTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            CancellationToken token = BeginOperation("准备安装常用 Mod...");
            List<string> failures = new List<string>();
            try
            {
                ModrinthService service = new ModrinthService();
                string gameDir = GetGameDirFromUi();
                for (int i = 0; i < common.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    SetStatus("安装常用 Mod " + (i + 1) + "/" + common.Count + "：" + common[i].Title);
                    try
                    {
                        ModInstallResult result = await service.InstallAsync(
                            gameDir, gameVersion, common[i].Slug, CreateProgress(), token);
                        Log.Info("Mod 安装完成：" + common[i].Title + " → " +
                                 string.Join(", ", result.Files.ToArray()));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Mod 安装失败：" + common[i].Title, ex);
                        failures.Add(common[i].Title + "：" + ex.Message);
                    }
                }
                if (IsDisposed || Disposing)
                {
                    return;
                }
                if (failures.Count == 0)
                {
                    SetStatus("常用 Mod 安装完成");
                }
                else
                {
                    SetStatus("部分常用 Mod 安装失败，详见日志");
                    MessageBox.Show(
                        "以下模组安装失败：\r\n\r\n" + string.Join("\r\n", failures.ToArray()) +
                        "\r\n\r\n详细信息已写入日志。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                // 装完立即刷新已安装列表，让用户看到结果并能接着禁用/删除。
                RefreshInstalledMods();
            }
            catch (OperationCanceledException)
            {
                Log.Warn("常用 Mod 安装已取消。");
                SetStatus("安装已取消");
            }
            finally
            {
                EndOperation();
            }
            await RefreshModStatusAsync();
        }

        private async void SearchModsClick(object sender, EventArgs e)
        {
            await SearchModsAsync();
        }

        private async Task SearchModsAsync()
        {
            if (_busy)
            {
                return;
            }
            string query = (_modSearchBox.Text ?? "").Trim();
            if (query.Length == 0)
            {
                MessageBox.Show("请输入要搜索的模组名称。", BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _modSearchBox.Focus();
                return;
            }
            if (query.Length > 64)
            {
                query = query.Substring(0, 64);
            }
            string gameVersion = GetFabricGameVersion(GetSelectedFabricVersionId());
            _modSearchButton.Enabled = false;
            SetStatus("正在搜索 Mod：" + query);
            try
            {
                ModrinthService service = new ModrinthService();
                List<ModProject> projects = await service.SearchAsync(
                    query, gameVersion, CancellationToken.None);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _modSearchList.BeginUpdate();
                try
                {
                    _modSearchList.Items.Clear();
                    for (int i = 0; i < projects.Count; i++)
                    {
                        ModProject project = projects[i];
                        string title = string.IsNullOrEmpty(project.Title)
                            ? (project.Slug ?? project.Id)
                            : project.Title;
                        ListViewItem item = new ListViewItem(title == null ? "" : title);
                        item.SubItems.Add(project.Description == null ? "" : project.Description);
                        item.SubItems.Add(project.Downloads.ToString());
                        item.Tag = project;
                        _modSearchList.Items.Add(item);
                    }
                }
                finally
                {
                    _modSearchList.EndUpdate();
                }
                SetStatus("搜索完成，共 " + projects.Count + " 个结果");
                if (projects.Count == 0)
                {
                    MessageBox.Show(
                        "没有找到兼容的 Fabric 模组。\r\n可以换一个关键字，或先安装 Fabric 版本以按游戏版本过滤。",
                        BrandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error("搜索 Mod 失败", ex);
                SetStatus("搜索 Mod 失败");
                MessageBox.Show("搜索 Mod 失败：" + ex.Message, BrandTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing && _modSearchButton != null)
                {
                    _modSearchButton.Enabled = !_busy;
                }
            }
        }

        // ============================ 状态与收尾 ============================

        /// <summary>
        /// 刷新启动页的新手引导：按"有版本 / 有 Java / 有账号"三项判断当前进度，
        /// 只把第一个未完成的步骤标为"当前"，其余为未开始或已完成。
        /// 三项都完成后自动隐藏，避免日常使用时一直占地方。
        /// </summary>
        private void UpdateLaunchGuide()
        {
            if (_launchGuide == null || _launchGuide.IsDisposed)
            {
                return;
            }
            if (_guideDismissed)
            {
                _launchGuide.HideGuide();
                return;
            }

            bool hasVersion = _versionCombo != null && _versionCombo.Items.Count > 0;
            bool hasJava = _javaCombo != null && _javaCombo.Items.Count > 0;
            bool hasAccount = _accountCombo != null && _accountCombo.Items.Count > 0;

            // 全部就绪：不再显示引导。
            if (hasVersion && hasJava && hasAccount)
            {
                _launchGuide.HideGuide();
                return;
            }

            List<GuidePanel.Step> steps = new List<GuidePanel.Step>();
            GuidePanel.Step stepVersion = new GuidePanel.Step(
                "安装一个游戏版本",
                hasVersion ? "已安装版本，可在上方下拉框中选中" : "到「下载」页选一个版本，点「安装原版」或「安装 Fabric」");
            stepVersion.State = hasVersion ? 2 : 1;
            steps.Add(stepVersion);

            GuidePanel.Step stepJava = new GuidePanel.Step(
                "选择 Java",
                hasJava ? "已检测到 Java，可保持默认" : "点「检测 Java」自动查找；找不到就手动浏览 javaw.exe");
            // 前一步没完成时，本步显示为未开始。
            stepJava.State = hasJava ? 2 : (hasVersion ? 1 : 0);
            steps.Add(stepJava);

            GuidePanel.Step stepAccount = new GuidePanel.Step(
                "准备账号",
                hasAccount ? "已有可用账号" : "默认已有离线账号；要进正版服务器请到「账号」页登录");
            stepAccount.State = hasAccount ? 2 : (hasVersion && hasJava ? 1 : 0);
            steps.Add(stepAccount);

            // 主按钮指向当前最该做的动作。
            string actionText;
            if (!hasVersion)
            {
                actionText = "去下载版本";
            }
            else if (!hasJava)
            {
                actionText = "检测 Java";
            }
            else
            {
                actionText = "去管理账号";
            }

            _launchGuide.SetContent("第一次使用？按这三步就能玩上", steps, actionText);
            _launchGuide.Height = _launchGuide.PreferredHeight;
        }

        /// <summary>引导面板的主按钮：跳到当前最需完成的步骤。</summary>
        private void LaunchGuideActionClick(object sender, EventArgs e)
        {
            bool hasVersion = _versionCombo != null && _versionCombo.Items.Count > 0;
            bool hasJava = _javaCombo != null && _javaCombo.Items.Count > 0;
            if (!hasVersion)
            {
                ShowPage(2);   // 下载页
            }
            else if (!hasJava)
            {
                DetectJavaClick(null, EventArgs.Empty);
            }
            else
            {
                ShowPage(1);   // 账号页
            }
        }

        private void UpdateActionButtons()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            // 版本/Java/账号任一变都会走到这里，顺带刷新新手引导的步骤状态。
            UpdateLaunchGuide();
            bool idle = !_busy;
            if (_startButton != null)
            {
                _startButton.Enabled = idle && (_gameProcess == null || _gameProcess.HasExited);
            }
            if (_refreshManifestButton != null)
            {
                _refreshManifestButton.Enabled = idle;
            }
            bool hasRemote = GetSelectedRemoteVersion() != null;
            if (_installVanillaButton != null)
            {
                _installVanillaButton.Enabled = idle && hasRemote;
            }
            if (_installFabricButton != null)
            {
                _installFabricButton.Enabled = idle && hasRemote;
            }
            if (_refreshInstalledButton != null)
            {
                _refreshInstalledButton.Enabled = idle;
            }
            if (_refreshModListButton != null)
            {
                _refreshModListButton.Enabled = idle;
            }
            // 本地 Mod 安装不需要 Fabric 版本（只是把 jar 放进 mods 目录），
            // 因此只受"是否有其它任务在跑"约束。
            if (_installLocalModButton != null)
            {
                _installLocalModButton.Enabled = idle;
            }
            // 预设导出同样不需要 Fabric 版本；导入需要（要按 MC 版本挑兼容版本）。
            if (_exportPresetButton != null)
            {
                _exportPresetButton.Enabled = idle;
            }
            // 禁用/删除只对选中的行生效，没选中时置灰，避免点了没反应。
            bool hasInstalledSelection = _installedModList != null &&
                !_installedModList.IsDisposed && _installedModList.SelectedItems.Count > 0;
            if (_toggleModButton != null)
            {
                _toggleModButton.Enabled = idle && hasInstalledSelection;
            }
            if (_deleteModButton != null)
            {
                _deleteModButton.Enabled = idle && hasInstalledSelection;
            }
            if (_detectJavaButton != null)
            {
                _detectJavaButton.Enabled = idle;
            }
            if (_browseJavaButton != null)
            {
                _browseJavaButton.Enabled = idle;
            }
            if (_saveSettingsButton != null)
            {
                _saveSettingsButton.Enabled = idle;
            }
            if (_saveLaunchButton != null)
            {
                _saveLaunchButton.Enabled = idle;
            }
            if (_cancelButton != null)
            {
                _cancelButton.Enabled = _busy && _cts != null;
            }

            bool hasFabricVersion = !string.IsNullOrEmpty(GetSelectedFabricVersionId());
            if (_importPresetButton != null)
            {
                _importPresetButton.Enabled = idle && hasFabricVersion;
            }
            if (_importPresetButton != null)
            {
                _importPresetButton.Enabled = idle && hasFabricVersion;
            }
            if (_installModButton != null)
            {
                _installModButton.Enabled = idle && hasFabricVersion &&
                                            (_modList == null || _modList.SelectedItems.Count > 0);
            }
            if (_installSearchModButton != null)
            {
                _installSearchModButton.Enabled = idle && hasFabricVersion &&
                                                  (_modSearchList == null || _modSearchList.SelectedItems.Count > 0);
            }
            if (_installCommonModsButton != null)
            {
                _installCommonModsButton.Enabled = idle && hasFabricVersion;
            }
            if (_refreshModsButton != null)
            {
                _refreshModsButton.Enabled = idle && hasFabricVersion;
            }
            if (_openModsButton != null)
            {
                _openModsButton.Enabled = idle;
            }
            if (_modSearchButton != null)
            {
                _modSearchButton.Enabled = idle;
            }
        }

        private void SetStatus(string status)
        {
            if (_statusLabel == null || _statusLabel.IsDisposed)
            {
                return;
            }
            _statusLabel.Text = status ?? "";
        }

        /// <summary>
        /// 在当前可见页面顶部显示一条提示。相比弹窗，它不打断操作，
        /// 内容留在页面上可供阅读与复制。
        /// </summary>
        private void ShowNotice(NoticeBar.NoticeLevel level, string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            int current = 0;
            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages[i].Visible)
                {
                    current = i;
                    break;
                }
            }
            if (current < _notices.Count)
            {
                _notices[current].ShowNotice(level, message);
            }
        }

        /// <summary>
        /// 在指定页面上显示提示（页面可能尚未切换过去）。
        /// </summary>
        private void ShowNoticeOnPage(int pageIndex, NoticeBar.NoticeLevel level, string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (pageIndex >= 0 && pageIndex < _notices.Count)
            {
                _notices[pageIndex].ShowNotice(level, message);
            }
        }

        private void OnLogMessage(string line)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(OnLogMessage), line);
                }
                catch
                {
                }
                return;
            }
            if (_logBox == null || _logBox.IsDisposed)
            {
                return;
            }
            if (_logBox.TextLength > 400000)
            {
                _logBox.Clear();
            }
            _logBox.AppendText(line + Environment.NewLine);
            // 有内容后隐藏空状态占位。
            if (_logEmptyState != null && !_logEmptyState.IsDisposed && _logEmptyState.Visible)
            {
                _logEmptyState.HideState();
            }
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            // 退出时停止 EasyTier 核心，避免留下孤儿进程（组网进程不应在启动器关闭后继续）。
            try
            {
                EasyTierService.StopCore();
            }
            catch (Exception ex)
            {
                Log.Warn("退出时停止 EasyTier 失败：" + ex.Message);
            }
            if (_busy)
            {
                DialogResult result = MessageBox.Show(
                    "当前有下载或启动准备正在进行，确定要退出吗？",
                    BrandTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                    }
                    catch
                    {
                    }
                }
            }
            if (_modsCts != null)
            {
                try
                {
                    _modsCts.Cancel();
                    _modsCts.Dispose();
                }
                catch
                {
                }
                _modsCts = null;
            }
            SaveConfigFromUi();
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("退出时保存配置失败：" + ex.Message);
            }
            if (_gameProcess != null && !_gameProcess.HasExited)
            {
                Log.Info("启动器关闭，游戏进程继续运行。");
            }
            // 游戏还在跑时不能删 @argfile（Java 可能还没读完）；
            // 游戏已退出但事件没来得及处理的残留在这里兜底清理。
            if (_gameProcess == null || _gameProcess.HasExited)
            {
                if (!string.IsNullOrEmpty(_pendingArgFile))
                {
                    PathUtil.TryDeleteFile(_pendingArgFile);
                    _pendingArgFile = "";
                }
            }
        }
    }

    /// <summary>
    /// 简单的单行输入对话框（用于添加离线账号等场景）。
    /// </summary>
    internal sealed class PromptDialog : Form
    {
        private readonly TextBox _valueBox;

        public string Value
        {
            get { return _valueBox.Text; }
        }

        public PromptDialog(string title, string message, string defaultValue)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 132);
            Font = UiTheme.FontRegular(9F);
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.TextPrimary;

            Label prompt = new Label();
            prompt.AutoSize = false;
            prompt.Dock = DockStyle.Top;
            prompt.Height = 34;
            prompt.Padding = new Padding(12, 12, 12, 0);
            prompt.Text = message;
            prompt.ForeColor = UiTheme.TextPrimary;
            Controls.Add(prompt);

            _valueBox = new TextBox();
            _valueBox.Location = new Point(12, 52);
            _valueBox.Width = 376;
            _valueBox.MaxLength = 64;
            UiTheme.StyleTextBox(_valueBox);
            _valueBox.Text = defaultValue ?? "";
            _valueBox.SelectAll();
            Controls.Add(_valueBox);

            Button okButton = UiTheme.CreatePrimaryButton("确定");
            okButton.Location = new Point(216, 92);
            okButton.Size = new Size(80, 30);
            okButton.DialogResult = DialogResult.OK;
            Controls.Add(okButton);

            Button cancelButton = UiTheme.CreateSecondaryButton("取消");
            cancelButton.Location = new Point(304, 92);
            cancelButton.Size = new Size(80, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _valueBox.Focus();
        }
    }
}
