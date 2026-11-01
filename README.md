# RLL（Rouzhi Lite Launcher）

轻量、安全的 Minecraft 启动器，使用 C# 5 + WinForms + .NET Framework 4.8 编写，单文件 exe，不需要安装 .NET SDK。采用左侧导航 + 卡片式布局，支持浅色 / 深色主题。
<p align="center">
  <img src="https://img.shields.io/github/license/dykk1234/RLL" alt="License GPL-3.0">
  <img src="https://img.shields.io/github/v/release/dykk1234/RLL" alt="Release">
  <img src="https://img.shields.io/github/stars/dykk1234/RLL" alt="Stars">
  <img src="https://img.shields.io/github/languages/top/dykk1234/RLL" alt="Language">
</p>

## 功能

- Mojang 官方版本清单（正式版 / 快照）
- 原版下载与安装：客户端 JAR、库、资源索引、资源对象、日志配置
- Fabric 最新稳定加载器安装（先确保对应原版已安装）
- **Fabric Mod 下载**：通过 Modrinth 官方 API 搜索与安装，自动匹配 Minecraft 版本、解析必需前置、逐文件校验 SHA1
  - 内置常用模组：钠 Sodium、玉 Jade、苹果皮 AppleSkin、锂 Lithium、钠扩展、铟 Indium、虹彩 Iris、模组菜单
  - 支持「一键安装钠 / 玉 / 苹果皮」与关键字搜索
  - 识别旧版本并提示「可更新 → x.y.z」
- 离线登录：按 `OfflinePlayer:<name>` 生成标准 UUID，不保存密码或令牌
- **账号系统**（新增于 1.2.1）：账号页统一管理离线账号与第三方认证账号；启动页下拉选择当前账号。
  - 离线账号：任意数量，按 `OfflinePlayer:<name>` 生成本地 UUID，不保存密码或令牌。
  - 第三方认证账号（联机）：通过 authlib-injector 接入 yggdrasil 认证服务器（如
    LittleSkin / Blessing Skin / 自建服务），可进入对应服务器的正版模式（online-mode=true）服务器。
    密码只在登录时于内存中存在，磁盘只保存会话令牌与档案 UUID；可随时校验令牌、重新登录或删除。
- **authlib-injector 注入器**：账号页一键下载官方 1.2.5 或选择本地已核验 JAR；第三方账号启动时自动注入 `-javaagent` 参数。
- Java 自动检测、手动选择、按 Minecraft 版本选择合适 Java，并标注每个候选的来源可信度
- 内存、JVM 参数、自定义窗口分辨率
- **超长命令行自动降级为 Java @argfile**（Java 9+）：大量模组/长 classpath 不再受 Windows
  32767 字符命令行上限影响，参数文件在启动后自动清理
- 启动游戏、实时日志、下载进度（自绘进度条）、随时取消
- **崩溃日志分析**（新增于 1.2.2）：日志页一键分析 `crash-reports` 或 `logs/latest.log`，
  按 10 类已知错误模式归纳出「问题 + 证据 + 建议」（缺少模组前置、Java 版本过低、
  内存/显存不足、库文件缺失、显卡驱动、会话失效等），不必自己读完整堆栈
- **默认游戏目录为程序所在目录下的 `.minecraft`**，整个文件夹可直接搬运
- 安装成功后自动切换到启动页并选中新装的版本
- 页面内信息条提示（可关闭），不再只依赖弹窗
- 深色主题：设置页一键切换；导航栏 7 个页面各带矢量图标，程序与窗口均带品牌图标
- **异地联机（EasyTier，1.3.0）**：无需公网服务器/注册，双方加入同一网络即可在游戏内
  用对方虚拟 IP 直连；核心从官方 Releases 自动下载并做 SHA256 校验，启动器退出时自动停止
- **下载源镜像（1.3.0）**：官方源不可用时可切到 BmclAPI 或自定义 HTTPS 镜像；
  只换下载地址，SHA1 校验仍按官方清单，镜像被篡改会被拦截
- **版本隔离（1.3.0）**：每个版本独立存档/配置/模组（`versions\<版本>\instance\`），
  开关式启停，不移动既有数据
- **模组冲突检查（1.3.0）**：安装前提示"同项目已存在""缺少必需前置"，仅提示不阻止
- **安装本地 Mod JAR（1.4.3）**：从整合包、朋友分享或手动下载拿到的 `.jar`
  也能直接装进 mods 目录——启动器会读出它的模组 id、版本与必需前置，
  并提示哪些前置还缺。只安装单个 jar，不解压、不执行，**同名文件绝不覆盖**
- **模组预设导入 / 导出（1.4.3）**：把当前模组清单导出成一个 JSON 文件发给朋友，
  对方导入后自动挑兼容版本批量安装。单个模组装不上不影响其余
- **游戏崩溃自动分析（1.4.3）**：游戏异常退出后自动分析崩溃日志并弹出结论，
  不用再自己切到「日志」页找按钮
- **下载速度与剩余时间（1.4.3）**：下载时显示实时速度（MB/s）与预计剩余时间，
  几百 MB 的首次安装不再是"干等"
- 可移植模式：程序目录存在 `portable.txt` 时，数据写入程序目录下的 `data\`

暂不支持：微软正版登录、Forge/NeoForge/Quilt、整合包导入、启动器自动更新、遥测。

> 「没装 Java 时一键下载」已在 1.4.1 实现（见下文「没装 Java 时一键下载」）；
> 「多版本隔离」与「下载源镜像」分别在 1.3.0 实现。

## 构建

要求：Windows + .NET Framework 4.x（系统自带 `csc.exe`）。

```powershell
cd LiteLauncher
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`dist\rll.exe`。

发布用的 exe 未做代码签名，下载后请核对发布包内的 `SHA256SUMS.txt` 或发布页提供的 SHA256；启动器本身不包含自动更新，也不会从远程拉取可执行代码。

项目使用系统 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，语言版本固定为 C# 5，因此源码不使用字符串插值、`?.`、`nameof`、元组、模式匹配等新语法。

## 测试

单元测试（离线，不需要 Java）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

网络测试（访问 Mojang / Fabric / Modrinth 官方接口，实际下载库文件与 Mod）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 --network
```

界面冒烟测试（真实构造窗体、逐页切换、校验控件与布局）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uitest.ps1
```

端到端安装测试（会下载 1.20.1 的原版资源，约数百 MB，并安装 Fabric 与常用 Mod）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\e2e.ps1
```

测试目录：`%LOCALAPPDATA%\Temp\opencode\LiteLauncherE2E`。

测试产物输出到 `build\test\`，`dist\` 只保留发布产物。

### 没装 Java 时一键下载（1.4.1 新增）

检测不到 Java 时，启动器会直接提供两个选择，而不是只让用户自己去官网找：

- **自动下载**：从 Eclipse Adoptium（Temurin）官方获取 **JRE**（约 47MB，而不是 205MB 的完整 JDK），
  校验 **SHA256** 后解压到 `%APPDATA%\rll\java\<主版本>`；
- **手动浏览**：仍支持选自己的 `javaw.exe`。

需要的 Java 版本按当前选中的游戏版本推断（需要 16 → 下 17，需要 30 → 下 25 等）。
下载的文件与 Adoptium API 返回的 SHA256 逐字节比对，不符即删除并报错。

### 本次验证结果（1.4.3）

- 构建：`build.ps1` 直调系统 `csc.exe`，产出单文件 `rll.exe`（约 335 KB，含内嵌图标）。
- 离线单元测试：**698 项通过，0 失败**（新增 42 项：本地 Mod 安装与安全边界、
  模组预设往返与校验、下载速度与 ETA 估算）。
- 界面冒烟测试：**190 项通过，0 失败**（新增模组页新按钮的存在性、文案、
  可见性、尺寸与事件接线检查）。
- 真实启动：`dist\rll.exe` 启动后窗口标题 `RLL - Rouzhi Lite Launcher 1.4.3`，进程响应正常。
- 分发包一致性：`SHA256SUMS.txt` 与三个产物哈希一致；zip 内 `rll.exe` 与 `dist\rll.exe` 字节相同；
  source zip 内文件与工作区逐文件 SHA256 一致，且解压后可独立编译。
- 配置安全：`config.json` 保存后 ACL 为"仅当前用户 / SYSTEM / Administrators"且不继承上级目录；
  日志目录与日志文件同样收紧；测试断言配置中不存在任何密码字段或明文密码。

> 历史版本（1.2.0 / 1.2.2）的验证数据见 CHANGELOG 对应条目。

测试脚本重跑方法：`test.ps1`（离线单元）、`test.ps1 --network`（联网）、`uitest.ps1`（界面）、
`e2e.ps1`（端到端安装，需下载数百 MB）。

26.2+ 的版本 JSON 把 native JAR 作为普通 classifier 依赖，并把 `java.library.path` 指向 `natives/java`。启动器会按当前架构选择 native JAR 放入 classpath（LWJGL 从 classpath 提取 DLL，jtracy 也通过 classpath 读取 DLL），同时创建 `natives/java`、`natives/jna`、`natives/lwjgl`、`natives/netty`；1.20.1 等旧布局仍解压到 `natives` 根目录。Fabric 对不支持的版本（如 1.12.2）返回 HTTP 400 时，界面会提示“该 Minecraft 版本不受 Fabric 支持”，而不是原始网络错误。

Fabric 元数据与 Modrinth 接口偶发超时，启动器会重试；如果网络持续不可用，请稍后再试，本版本不会自动切换到非官方镜像。

## 使用

1. 打开「下载」页，点击「刷新版本清单」。
2. 选择一个正式版，点击「安装原版」；或点击「安装 Fabric」。
3. 在「启动」页选择账号（或先在「账号」页添加离线账号 / 第三方认证账号），选择版本和 Java，设置内存。
4. 点击「启动游戏」。
5. 需要模组时打开「模组」页：顶部选择目标 Fabric 版本，即可一键安装常用模组，或用关键字搜索 Modrinth。

首次安装需要网络。资源文件较多时下载时间较长，进度条和日志会显示当前状态，可随时点击「取消」。

### 异地联机（双方都需安装本启动器）

1. 打开「设置」页，勾选「启用异地联机」，填写**网络名**与**网络密钥**（双方必须完全一致）。
2. 点击「下载核心」（首次约 32 MB，会校验 SHA256）；状态行显示「核心已就绪」。
3. 开服方：启动游戏并在游戏内「对局域网开放」；设置页「虚拟 IP」会显示本机地址。
4. 加入方：同样加入该网络，在游戏「多人游戏 → 直接连接」里填开服方的 `虚拟IP:端口`。
5. 「复制 IP」可直接把虚拟地址复制到剪贴板。

网络名只允许字母、数字、`-`、`_`（1-64 位）。密钥以明文保存在配置文件中，
请只与可信的人共享；关闭启动器时 EasyTier 核心会被自动结束。

### 下载源镜像

官方源较慢或不可达时，在设置页把「下载源」切到 `BmclAPI 镜像`；也可填自定义 HTTPS 镜像。
镜像只替换下载地址，SHA1 仍按官方清单校验，因此不会降低安全性。
Modrinth 的模组下载不受镜像影响。

### 版本隔离

勾选「版本隔离」后，每个版本的存档/配置/模组位于
`<游戏目录>\versions\<版本ID>\instance\`，彼此互不可见。
**既有存档不会自动迁移**：如需沿用，请手动把旧的 `saves` 复制到对应实例目录。
关闭开关即回到共享布局，原数据不做任何改动。

## 数据与配置

- 默认配置：`%APPDATA%\rll\config.json`（首次启动会自动迁移旧版 `%APPDATA%\LiteLauncher\config.json` 的设置）
- 默认游戏目录：程序所在目录下的 `.minecraft`
- 日志：`%APPDATA%\rll\logs\launcher-yyyyMMdd.log`
  - 日志目录与文件的 ACL 已收紧为「仅当前用户 / SYSTEM / Administrators」（同 `config.json`），
    因为日志里可能出现会话令牌与异地联机密钥；密钥本身在日志中以 `****` 脱敏显示。
- Mod 目录：`<游戏目录>\mods`
- 可移植模式：在 `rll.exe` 同目录放置 `portable.txt`，配置、日志和默认游戏目录都放到 `data\`

配置中只保存游戏目录、Java 路径、内存、JVM 参数、窗口大小、账号列表（离线用户名 / 第三方认证的会话令牌与档案 UUID）、认证服务器预设、注入器路径、上次用户名和上次版本；不保存任何账号密码。

## 界面结构

左侧固定导航栏（启动 / 账号 / 下载 / 模组 / 设置 / 日志 / 关于），右侧为对应页面，页面内使用圆角卡片分区；
顶部为页面标题与说明，页内可显示可关闭的信息条；底部状态栏显示当前操作、自绘进度条与固定尺寸的取消按钮。

## 代码结构

```text
src/
  Core/
    AppPaths.cs              路径与便携模式、版本号（读程序集元数据）
    PathUtil.cs              路径穿越防护、SHA1、原子写入
    Log.cs                   文件日志 + 界面日志事件
    JsonUtil.cs              只读 JSON 数据解析（32 MB 上限）
    LauncherConfig.cs        配置加载/校验/保存、旧配置一次性迁移
    CommandLine.cs           Windows 参数转义、离线 UUID
    HttpUtil.cs              HTTPS allowlist、重定向校验、SHA1 下载
    JvmArgumentPolicy.cs     JVM 参数精确匹配黑名单（三类来源统一）
    VersionModels.cs         版本/库/参数数据模型
    VersionManifestService.cs Mojang 版本清单
    RuleEvaluator.cs         os/arch/features 规则
    VersionResolver.cs       inheritsFrom 合并、库与 native 选择、Fabric 识别
    NativeExtractor.cs       安全 ZIP 解压
    MinecraftInstaller.cs    原版安装与资源整理
    FabricInstaller.cs       Fabric loader/profile 安装
    ModrinthService.cs       Modrinth 搜索、版本匹配、依赖解析、Mod 安装
    JavaFinder.cs            Java 检测、来源可信度标注
    AuthModels.cs            账号模型、认证服务器校验、账号 ID 规则（1.2.1 新增）
    YggdrasilClient.cs       yggdrasil 认证协议客户端：登录/校验/注销（1.2.1 新增）
    AuthlibInjectorService.cs 注入器获取与 -javaagent 参数（1.2.1 新增）
    AuthService.cs           账号增删改查、白名单同步（1.2.1 新增）
    CrashAnalyzer.cs         崩溃日志分析：模式匹配与建议生成（1.2.2 新增）
    GameLauncher.cs          启动计划、参数替换、账号注入、@argfile、进程启动
  MainForm.cs                主窗口（侧边导航 + 卡片页面）
  UiTheme.cs                 浅色/深色配色、圆角卡片、导航按钮、进度条、提示条、空状态
  UiFactory.cs               页面骨架与通用部件构建
  NavGlyphs.cs               导航栏矢量图标（1.2.2 新增）
  Program.cs                 TLS、异常处理、DPI、入口
  app.manifest               asInvoker、DPI、长路径
  app.ico                    程序图标（由 tools/IconGen.cs 生成）
tools/
  IconGen.cs                 图标生成器：代码绘制，无第三方素材
tests/
  TestHarness.cs             单元测试与网络测试
  IntegrationTest.cs         真实下载/安装端到端测试
  UiSmoke.cs                 界面冒烟测试
build.ps1 / test.ps1 / uitest.ps1 / e2e.ps1 / pack.ps1
node_modules/ 不存在         无第三方运行时依赖
```

## 已知限制

- 未内置 Java，需要系统已安装 Java；启动器会自动检测并选择合适版本。本机已用 Java 8 / 21 / 25 验证检测逻辑；1.20.1 原版与 Fabric 使用 Java 21 验证，26.3 原版与 Fabric 使用 Java 25 验证。
- 离线账号进入服务器时，服务器必须开启 `online-mode=false`；需要进入正版模式服务器时，请在「账号」页添加对应认证服务器的第三方认证账号（联机），并确保该服务器接受 authlib-injector 认证。
- 第三方认证账号的会话令牌保存在本机配置文件中（明文存储）；不保存密码。认证服务器由用户主动添加/登录后才被信任（加入域名白名单）。
- 不支持微软官方账号（Microsoft OAuth）登录。
- 不支持 Forge/NeoForge/Quilt 与整合包导入；Mod 仅支持 Modrinth 上提供 Fabric 版本的资源。
- Mod 安装按 Modrinth 元数据解析必需前置，不处理可选前置与模组间的版本冲突；同时安装多个模组时请注意重复文件。
- Mod「可更新」提示按文件名包含项目 slug 的宽松匹配，仅用于界面提示，不会自动删除或覆盖旧文件。
- 未实现启动器自动更新与下载断点续传；Fabric 与 Modrinth 接口不可用时只能等待或稍后重试。
- 命令行超过 30000 字符且 Java 9+ 时自动改用 `@argfile`（参数文件在游戏退出后清理）；Java 8 及以下无法使用该特性，超长时会给出明确错误提示。
  - 参数文件路径带引号，因此 Windows 用户名里有空格也不影响启动。
- 游戏进程输出的编码取决于 Java 和系统区域设置，个别日志中的中文可能显示为乱码；启动器自身日志使用 UTF-8。
- 首次安装下载量较大，网络不稳定时可能出现元数据请求超时；启动器会重试 3 次，每次元数据请求最多等待 60 秒。
- exe 未做代码签名；每次构建的时间戳与模块 ID 不同，同一份源码无法产出字节相同的二进制。

## 安全说明

详见 [SECURITY.md](SECURITY.md)。核心约束：

- 只允许 HTTPS 和固定官方域名（Mojang / Fabric / Modrinth / GitHub Release 用于下载注入器），重定向逐跳校验；第三方认证服务器域名只有在用户添加预设或登录成功后才加入运行时白名单。
- 所有远程下载必须带 SHA1 并通过校验；缺失 SHA1 时只允许从官方域名读取 `.sha1`；authlib-injector 官方 JAR 不提供哈希，自动下载仅限官方地址且限制 32MB，用户可改用本地已核验文件。
- 远程 JSON 只作为纯数据解析，不按类型名反序列化。
- 不提供远程 UI、远程命令、shell 执行或自定义启动命令。
- 不写注册表；不保存任何账号密码（第三方认证密码只在登录请求体内存中短暂存在）。
- 启动参数统一转义，`UseShellExecute=false`，不经过 `cmd.exe` / PowerShell。
- JVM 参数按参数名精确匹配拦截（不误伤合法属性值），用户输入、Fabric profile、版本 JSON 三类来源统一策略。
- 不注册全局证书校验回调，不提供 SSL 绕过开关；TLS 按运行时能力启用 1.2 + 1.3。
