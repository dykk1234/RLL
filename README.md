# RLL（Rouzhi Lite Launcher）

轻量、安全的 Minecraft 启动器，使用 C# 5 + WinForms + .NET Framework 4.8 编写，单文件 exe，不需要安装 .NET SDK。采用左侧导航 + 卡片式布局，支持浅色 / 深色主题。

## 功能

- Mojang 官方版本清单（正式版 / 快照），原版下载与安装：客户端 JAR、库、资源索引、资源对象、日志配置
- Fabric 最新稳定加载器安装（需先安装对应原版）
- **Fabric Mod 下载**：通过 Modrinth 官方 API 搜索与安装，自动匹配 Minecraft 版本、解析必需前置、逐文件校验 SHA1
  - 内置常用模组：钠 Sodium、玉 Jade、苹果皮 AppleSkin、锂 Lithium、钠扩展、铟 Indium、虹彩 Iris、模组菜单
  - 支持「一键安装钠 / 玉 / 苹果皮」与关键字搜索，并识别旧版本提示「可更新 → x.y.z」
- **账号系统**：账号页统一管理离线账号与第三方认证账号，启动页下拉选择当前账号
  - 离线账号：任意数量，按 `OfflinePlayer:<name>` 生成本地 UUID，不保存密码或令牌
  - 第三方认证账号（联机）：通过 authlib-injector 接入 yggdrasil 认证服务器（如 LittleSkin / Blessing Skin / 自建服务），可进入 `online-mode=true` 的服务器。密码只在登录时于内存中存在，磁盘只保存加密后的会话令牌与档案 UUID
  - 微软正版登录（测试版，可选）：在设置页填入自己的 Azure 应用 `client_id` 后，可用设备码登录微软账号（该账号需拥有 Minecraft: Java 版），令牌加密存储，可进入官方在线服务器
- **光影包管理**：「光影」页列出当前实例光影目录，支持启用 / 禁用 / 删除 / 导入本地 zip；内置 8 个 Modrinth 推荐光影（Complementary / BSL / Sildur's Vibrant / Photon / MakeUp / Solas / Bliss 等），一键查询并下载最新发布版（SHA1 校验，走统一下载中心）
- **下载任务中心**：版本安装、Fabric 安装、模组安装、Java 下载、模组更新都以任务形式出现在「下载任务」页，进度、来源一目了然，可直接取消。支持并发数、历史上限、断点续传与暂停 / 重试
- **模组进阶**：安装本地 Mod JAR（读出模组 id / 版本 / 必需前置，只装单个 jar，不解压、不执行，同名文件绝不覆盖）；模组预设导入 / 导出（一份 JSON 发给朋友，对方自动挑兼容版本批量安装）；模组冲突检查（提示同项目已存在、缺少必需前置，仅提示不阻止）；批量更新与回滚（带备份、篡改备份拒绝恢复）
- **版本隔离**：每个版本的存档 / 配置 / 模组独立（`versions\<版本>\instance\`），开关式启停，不移动既有数据
- **下载源镜像**：官方源不可用时切到 BmclAPI 或自定义 HTTPS 镜像；只换下载地址，SHA1 仍按官方清单校验
- **异地联机（EasyTier）**：无需公网服务器 / 注册，双方加入同一网络即可用虚拟 IP 直连；核心从官方 Releases 自动下载并做 SHA256 校验，启动器退出时自动停止。独立「联机」页支持公共节点与自定义节点测速
- **Java 自动检测**：按 Minecraft 版本选择合适 Java，标注每个候选的来源可信度；检测不到时可一键下载 Adoptium Temurin JRE（SHA256 校验）
- **超长命令行自动降级为 Java @argfile**（Java 9+）：大量模组 / 长 classpath 不再受 Windows 32767 字符命令行上限影响，参数文件在启动后自动清理
- **崩溃日志分析**：日志页一键分析 `crash-reports` 或 `logs/latest.log`，按已知错误模式归纳出「问题 + 证据 + 建议」（缺少模组前置、Java 版本过低、内存 / 显存不足、库文件缺失、显卡驱动、会话失效等）；游戏异常退出后自动分析并弹出结论
- **日志自动轮转与清理**：单文件超过 64 MB 自动轮转，最多保留 3 份，启动时清理 30 天前的旧日志；游戏日志与操作记录分开归档
- **下载不会因为「慢」被误杀**：只有连续 45 秒收不到数据才判定卡死并中止，慢但持续传输的连接可以下完
- **存储管理**：设置页统计启动器日志、崩溃报告、@argfile 残留、natives 缓存、联机核心、版本与资源六类占用；一键清理只删「可再生」内容，版本 JAR / 资源 / 存档绝不触碰
- 内存、JVM 参数、自定义窗口分辨率；强调色五选一、界面字体缩放 90%–130%
- 启动游戏、实时日志、下载进度（自绘进度条）、速度与剩余时间、随时取消；启动页显示最近一次运行状态（退出码、时长、时间）
- 默认游戏目录为程序所在目录下的 `.minecraft`，整个文件夹可直接搬运；程序目录存在 `portable.txt` 时进入可移植模式，数据写入 `data\`
- 深色主题一键切换；导航栏 10 个页面各带矢量图标，程序与窗口均带品牌图标

暂不支持：Forge / NeoForge / Quilt、整合包导入、启动器自动更新、遥测。

## 构建

要求：Windows + .NET Framework 4.x（系统自带 `csc.exe`）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`dist\rll.exe`。项目使用系统 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，语言版本固定为 C# 5，因此源码不使用字符串插值、`?.`、`nameof`、元组、模式匹配等新语法。

发布用的 exe 未做代码签名，下载后请核对发布包内的 `rll.exe.sha256`（发布目录另有 `SHA256SUMS.txt` 汇总全部产物），或核对发布页给出的 SHA256；启动器本身不包含自动更新，也不会从远程拉取可执行代码。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\pack.ps1
```

`pack.ps1` 会先重新编译，再产出 `dist\` 下的运行包、源码包与 `SHA256SUMS.txt`。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1            # 离线单元测试，不需要 Java
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 --network  # 联网：访问 Mojang / Fabric / Modrinth 并实际下载
powershell -NoProfile -ExecutionPolicy Bypass -File .\uitest.ps1          # 界面冒烟：真实构造窗体、逐页切换、校验控件与布局
powershell -NoProfile -ExecutionPolicy Bypass -File .\e2e.ps1             # 端到端：下载 1.20.1 原版资源（数百 MB）并安装 Fabric
```

测试目录：`%LOCALAPPDATA%\Temp\opencode\LiteLauncherE2E`；测试产物输出到 `build\test\`，`dist\` 只保留发布产物。

### 没装 Java 时一键下载

检测不到 Java 时，启动器直接提供两个选择，而不是让用户自己去官网找：

- **自动下载**：从 Eclipse Adoptium（Temurin）官方获取 **JRE**（约 47 MB，而不是 205 MB 的完整 JDK），校验 **SHA256** 后解压到 `%APPDATA%\rll\java\<主版本>`
- **手动浏览**：仍支持选自己的 `javaw.exe`

需要的 Java 版本按当前选中的游戏版本推断（需要 16 → 下 17，需要 30 → 下 25 等）。下载的文件与 Adoptium API 返回的 SHA256 逐字节比对，不符即删除并报错。

「下载」页与「启动」页都有入口；查询、下载、校验、解压四个阶段都有可见的进度提示；查询使用短超时（20 秒 × 2 次）；失败时按原因给出可执行的建议（网络 / 代理 / 磁盘 / 校验 / 版本过旧），而不是只丢一句技术性异常；其它任务进行中时按钮会正确置灰。

## 使用

1. 打开「下载」页，点击「刷新版本清单」。
2. 选择一个正式版，点击「安装原版」；或点击「安装 Fabric」。
3. 在「启动」页选择账号（或先在「账号」页添加离线账号 / 第三方认证账号），选择版本和 Java，设置内存。
4. 点击「启动游戏」。
5. 需要模组时打开「模组」页：一键安装常用模组，或按关键字搜索 Modrinth。

首次安装需要网络。资源文件较多时下载时间较长，进度条与日志会显示当前状态，可随时点击「取消」。

### 异地联机（双方都需安装本启动器）

1. 打开「联机」页，勾选「启用异地联机」，填写**网络名**与**网络密钥**（双方必须完全一致）。
2. 点击「下载核心」（首次约 32 MB，会校验 SHA256）；状态行显示「核心已就绪」。
3. 开服方：启动游戏并在游戏内「对局域网开放」；联机页「虚拟 IP」会显示本机地址。
4. 加入方：同样加入该网络，在游戏「多人游戏 → 直接连接」里填开服方的 `虚拟IP:端口`。
5. 「复制 IP」可直接把虚拟地址复制到剪贴板。

网络名只允许字母、数字、`-`、`_`（1–64 位）。密钥以明文保存在配置文件中，请只与可信的人共享；关闭启动器时 EasyTier 核心会被自动结束。

### 下载源镜像

官方源较慢或不可达时，把「下载源」切到 `BmclAPI 镜像`；也可填自定义 HTTPS 镜像。镜像只替换下载地址，SHA1 仍按官方清单校验，因此不会降低安全性。Modrinth 的模组下载不受镜像影响。

### 版本隔离

勾选「版本隔离」后，每个版本的存档 / 配置 / 模组位于 `<游戏目录>\versions\<版本ID>\instance\`，彼此互不可见。**既有存档不会自动迁移**：如需沿用，请手动把旧的 `saves` 复制到对应实例目录。关闭开关即回到共享布局，原数据不做任何改动。

安装位置始终跟随「启动」页当前选中的版本：模组目录、光影包目录、游戏的 `--gameDir` 与进程工作目录都由同一个版本 ID 决定。模组页顶部的「当前管理目录」徽章会写出该版本 ID，切换启动版本或勾选隔离都会立即刷新；模组页下拉框只决定搜索时匹配的 MC 游戏版本，不再决定文件落到哪里。

启动器**不会**自动把共享 `<游戏目录>\mods` 里的模组复制给每个版本实例（那会把为其它 MC 版本准备的模组广播给所有版本）。如需沿用旧模组，到模组页点「从共享目录导入」：只作用于当前选中的版本，只复制、不覆盖、不删除。

启动参数层另有一层硬保障：无论版本 JSON 是否写了 `--gameDir`，启动器都会显式把游戏目录指向该版本的实例目录，并让进程工作目录与之一致，避免部分模组按 `user.dir` 读回共享 mods。

## 数据与配置

- 默认配置：`%APPDATA%\rll\config.json`（首次启动会自动迁移旧版 `%APPDATA%\LiteLauncher\config.json` 的设置）
- 默认游戏目录：程序所在目录下的 `.minecraft`
- 日志：`%APPDATA%\rll\logs\launcher-yyyyMMdd.log`，日志目录与文件的 ACL 收紧为「仅当前用户 / SYSTEM / Administrators」（同 `config.json`），因为日志里可能出现会话令牌与异地联机密钥；密钥本身在日志中以 `****` 脱敏显示
- Mod 目录：`<游戏目录>\mods`（启用版本隔离时为 `versions\<版本>\instance\mods`）
- 可移植模式：在 `rll.exe` 同目录放置 `portable.txt`，配置、日志和默认游戏目录都放到 `data\`

配置中只保存游戏目录、Java 路径、内存、JVM 参数、窗口大小、账号列表、认证服务器预设、注入器路径、上次用户名和上次版本；不保存任何账号密码。

## 界面结构

左侧固定导航栏（启动 / 账号 / 下载 / 下载任务 / 模组 / 光影 / 联机 / 设置 / 日志 / 关于），右侧为对应页面。卡片带柔和投影与主题色顶栏，主按钮渐变填充、悬停提亮，导航选中态为横向渐变，状态栏进度条与目录徽章视觉统一。顶部为页面标题与说明，页内可显示可关闭的信息条；底部状态栏显示当前操作、自绘进度条与固定尺寸的取消按钮。

## 代码结构

```text
src/
  Core/
    路径与配置    AppPaths  PathUtil  LauncherConfig  JsonUtil  WriteErrorDiagnostics
    日志与诊断    Log  CrashAnalyzer  SpaceManager
    网络与下载    HttpUtil  DownloadManager  DownloadModels  DownloadRateTracker
                  DownloadStallWatchdog  DownloadMirrors
    版本与安装    VersionModels  VersionManifestService  RuleEvaluator  VersionResolver
                  MinecraftInstaller  FabricInstaller  NativeExtractor  InstancePaths
    Java          JavaFinder  JavaInstaller
    启动          GameLauncher  CommandLine  JvmArgumentPolicy
    模组          ModrinthService  ModManager  ModIndex  ModUpdate  ModPreset
                  ModDependencyScanner  ModDuplicates  LocalModInstaller  RecommendedMods
    光影          ShaderPackManager
    账号          AuthModels  AuthService  YggdrasilClient  AuthlibInjectorService
                  AuthServerRegistry  BuiltinAuthServerPresets  MicrosoftAuthService
                  CredentialStore
    联机          EasyTierService  NodeMonitor
  MainForm.cs              主窗口（侧边导航 + 卡片页面）
  MainForm.TasksPage.cs    下载任务页
  MainForm.ShadersPage.cs  光影页
  MainForm.Microsoft.cs    微软登录入口
  MainForm.RuntimeCard.cs  启动页运行状态卡片
  MicrosoftLoginDialog.cs  设备码登录对话框
  UpdatePreviewDialog.cs   模组更新预览对话框
  GuidePanel.cs            新手引导
  PageIndex.cs             页面索引
  UiTheme.cs               浅色 / 深色配色、圆角卡片、导航按钮、进度条、提示条、空状态
  UiFactory.cs             页面骨架与通用部件构建
  NavGlyphs.cs             导航栏矢量图标
  Program.cs               TLS、异常处理、DPI、入口
  app.manifest             asInvoker、DPI、长路径
  app.ico                  程序图标
tests/
  TestHarness.cs           单元测试与网络测试
  UiSmoke.cs               界面冒烟测试
  IntegrationTest.cs       真实下载 / 安装端到端测试
build.ps1 / test.ps1 / uitest.ps1 / e2e.ps1 / pack.ps1
```

项目无第三方运行时依赖。

## 已知限制

- 未内置 Java，需要系统已安装 Java；启动器会自动检测并选择合适版本。已在 Java 8 / 21 / 25 上验证检测逻辑，1.20.1 与 26.3 的安装与启动均已实测
- 微软登录为测试版，需要自行配置 Azure 应用 `client_id`；不支持其它形式的微软账号登录
- 离线账号进入服务器时，服务器必须开启 `online-mode=false`；需要进入正版模式服务器时，请在「账号」页添加对应认证服务器的第三方认证账号（联机），并确保该服务器接受 authlib-injector 认证
- 第三方认证账号的会话令牌经 DPAPI 加密后落盘（仅 Windows；非 Windows 降级为本地 Base64 存储并在日志提示，此时请勿把配置文件复制给他人）；任何平台都不保存密码
- 不支持 Forge / NeoForge / Quilt 与整合包导入；Mod 仅支持 Modrinth 上提供 Fabric 版本的资源
- Mod 安装按 Modrinth 元数据解析必需前置，不处理可选前置与模组间的版本冲突；同时安装多个模组时请注意重复文件
- Mod「可更新」提示按文件名包含项目 slug 的宽松匹配，仅用于界面提示，不会自动删除或覆盖旧文件
- 未实现启动器自动更新；Fabric 与 Modrinth 接口不可用时只能等待或稍后重试
- 断点续传仅对带 SHA1 校验的下载生效（Mojang 库 / 资源）；authlib-injector 与 EasyTier 核心下载仍为全量重下
- 公共节点测速用 TCP 握手耗时近似延迟；`udp://` 节点仅代表主机可达
- 下载任务中心里，版本安装 / 模组安装 / Java 下载以「汇总任务」形式各占一行（进度由安装流程回填）；汇总任务不占并发槽、不写盘、不可暂停 / 续传，取消会中止对应安装
- 模组更新默认只自动勾选「明确兼容且无冲突」项；禁用模组、依赖缺失、ModId 重复等会被标黄并默认不勾选，需用户手动确认
- 强调色与界面缩放即改即存，控件构建时固化，**重启完全生效**
- 命令行超过 30000 字符且 Java 9+ 时自动改用 `@argfile`（参数文件在游戏退出后清理）；Java 8 及以下无法使用该特性，超长时会给出明确错误提示。参数文件路径带引号，因此 Windows 用户名里有空格也不影响启动
- 游戏进程输出的编码取决于 Java 和系统区域设置，个别日志中的中文可能显示为乱码；启动器自身日志使用 UTF-8
- 首次安装下载量较大，网络不稳定时可能出现元数据请求超时；启动器会重试 3 次，每次元数据请求最多等待 60 秒
- exe 未做代码签名；每次构建的时间戳与模块 ID 不同，同一份源码无法产出字节相同的二进制

## 安全说明

详见 [SECURITY.md](SECURITY.md)。核心约束：

- 只允许 HTTPS 和固定官方域名（Mojang / Fabric / Modrinth / GitHub Release 用于下载注入器），重定向逐跳校验；第三方认证服务器域名只有在用户添加预设或登录成功后才加入运行时白名单
- 所有远程下载必须带 SHA1 并通过校验；缺失 SHA1 时只允许从官方域名读取 `.sha1`；authlib-injector 官方 JAR 不提供哈希，自动下载仅限官方地址且限制 32 MB，用户可改用本地已核验文件
- 远程 JSON 只作为纯数据解析，不按类型名反序列化
- 不提供远程 UI、远程命令、shell 执行或自定义启动命令
- 不写注册表；不保存任何账号密码（第三方认证密码只在登录请求体内存中短暂存在）
- 启动参数统一转义，`UseShellExecute=false`，不经过 `cmd.exe` / PowerShell
- JVM 参数按参数名精确匹配拦截（不误伤合法属性值），用户输入、Fabric profile、版本 JSON 三类来源统一策略
- 不注册全局证书校验回调，不提供 SSL 绕过开关；TLS 按运行时能力启用 1.2 + 1.3
