# 安全设计

RLL（Rouzhi Lite Launcher）的目标是提供可靠的 Minecraft 启动体验，同时从设计上避免远程配置执行、无签名更新、命令注入、SSL 绕过和凭据弱存储等问题。

## 威胁模型

本启动器主要防范：

1. 远程元数据/下载内容被篡改，导致下载恶意文件或路径穿越。
2. 恶意版本 JSON、Fabric profile 或资源索引诱导启动器执行命令、加载任意本地文件或写出游戏目录。
3. 网络中间人攻击或重定向到非官方域名。
4. 启动器被要求以管理员权限运行、写注册表或通过 shell 拼接命令。
5. 启动器自身保存账号密码/令牌，导致凭据泄露。

不在首版威胁模型内：本机已被完全控制、用户主动执行恶意程序、Minecraft 本身或用户安装的 Mod 的漏洞。

### 信任假设（明确写出，便于评估边界）

启动器的防护建立在以下"必须信任"的前提上。若其中任何一条不成立，相应防护也会失效：

1. **信任 Mojang 清单与版本 JSON 的 SHA1**：版本清单自身通过 HTTPS 获取，其内容是后续所有下载的信任根。
   清单若被篡改，后续校验都会为攻击者的文件"背书"。
2. **信任 Fabric meta 的 TLS 通道**：Fabric profile 没有固定 SHA1，只能依赖 HTTPS 与域名白名单。
   上游被攻破时可下发恶意 profile；启动器能做的是拒绝其中的危险 JVM 参数与不在白名单的库地址。
3. **信任 Modrinth 元数据**：Mod 的 SHA1 来自 Modrinth 响应，只能保证"下载到的文件与元数据一致"，
   不能保证"元数据描述的就是安全代码"。Mod 属于第三方代码。
4. **信任本机 Java 运行时本身可信**：启动器校验候选路径与来源，但不校验 `java.exe` 的签名或哈希。
5. **信任发布渠道**：exe 未做代码签名，用户应从官方渠道下载并核对 SHA256。

## 网络与下载

- 只允许 `https`，且只允许以下主机（精确匹配）：
  - `piston-meta.mojang.com`
  - `launchermeta.mojang.com`
  - `piston-data.mojang.com`
  - `launcher.mojang.com`
  - `libraries.minecraft.net`
  - `resources.download.minecraft.net`
  - `meta.fabricmc.net`
  - `maven.fabricmc.net`
  - `repo1.maven.org`
  - `repo.maven.apache.org`
  - `meta.quiltmc.org`
  - `maven.quiltmc.org`
  - `api.modrinth.com`
  - `cdn.modrinth.com`
  - `github.com`（仅用于下载两个固定版本的官方发布件：authlib-injector 与 EasyTier 核心。
    该 URL 会 302 跳转到 GitHub 的资产 CDN，因此下一跳主机也必须在白名单内）
  - `release-assets.githubusercontent.com`（GitHub Release 资产的实际分发主机；
    2024 年后新资产走此域名，缺少它会导致"重定向到不允许的地址"）
  - `objects.githubusercontent.com`（旧资产的兼容域名，保留以支持历史版本）
  - `www.wintun.net`（EasyTier 官方包缺失 `wintun.dll` 时的备用来源；
    下载的 zip 校验官方公布的 SHA2-256，见下文「异地联机」）
  - `api.adoptium.net`（用户没装 Java 时查询 Temurin JRE 下载信息；
    下载文件本身由 `github.com` 分发）
- 第三方认证服务器（yggdrasil）主机**默认不在白名单**：只有用户在账号页添加认证服务器预设、
  或登录成功后，对应主机才会加入运行时白名单（仅内存，不落盘）；重启后白名单回到
  内置清单 + 配置中已保存的预设/认证账号主机（登录成功的账号主机存于账号记录中）。
- 拒绝 `http`、非 443 端口、URL 中的 userinfo、IP 主机、末尾带点的域名。
- 禁止自动重定向；每一跳都重新校验 allowlist，最多 5 跳。
- 不注册 `ServerCertificateValidationCallback`，不提供“忽略 SSL 错误”选项。
- `HttpUtil` 与 `Program.Main` 共用同一套 TLS 探测逻辑：按运行时能力启用 TLS 1.2 + 1.3，
  不支持时回退到仅 TLS 1.2；静态构造不再无条件重置为 1.2（1.2.1 修复）。
- 除 authlib-injector 自动下载外，每个下载项必须有 40 位 SHA1；下载到临时文件后校验
  SHA1 和大小，再原子替换目标文件。authlib-injector 官方不提供哈希，自动下载仅允许
  固定版本号的官方地址、限制 32 MB、原子替换；高信任用户可用「选择本地 JAR...」使用
  自己核验过的文件（信任边界见下文「第三方认证（联机）」）。
- **Java 运行时下载（Adoptium/Temurin）必须带 SHA256**：API 未返回 `checksum` 字段即
  拒绝安装，不做「有则校验、无则跳过」的降级——否则未经校验的压缩包会直接进入解压路径。
- **Wintun 驱动库下载必须带 SHA2-256**：摘要硬编码为官方发布页公布的值
  （`07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51`）。
  该文件是内核态网络驱动，不能只靠「是合法 ZIP」的结构检查。
- 远程 JSON 中的 `sha1`、`size`、`path`、`url` 都经过格式和 allowlist 校验。
- 元数据响应限制 16 MB，单个下载限制 8 GB。

## 路径与 ZIP

- 版本名只允许字母、数字、`.`、`_`、`-`、`+`，禁止 `.`/`..`、路径分隔符和首尾点。
- 所有远程相对路径都经过 `PathUtil.SafeCombine`：
  - 拒绝绝对路径、盘符、ADS（`:`）、类路径分隔符（`;`）；
  - 拒绝 `..`、`.`、空段、双斜杠、Windows 非法字符和以点/空格结尾的段；
  - 拼接后再次检查是否仍在可信根目录内。
- Maven 坐标只允许字母、数字、`.`、`-`、`_`、`+`，防止通过 `;` 注入 classpath。
- ZIP 解压只接受普通文件条目，跳过 `META-INF/` 和 `__MACOSX/`；每个条目和总解压量有上限。
- **所有 ZIP 解压路径都做路径穿越校验**：条目名先过 `PathUtil.IsSafeRelativePath`
  （拒绝 `..`、绝对路径、盘符、ADS、`;` 等），再经 `PathUtil.SafeCombine` 拼到可信根目录，
  拼接后再次确认仍在根目录内。原生库、JRE、Mod 文件三条解压链路统一使用这一套。
  （1.4.1 修复：Java JRE 解压曾直接 `Path.Combine`，含 `..` 的条目名可写到解压目录之外。）
- 原生库只解压 `.dll` / `.so` / `.dylib` / `.jnilib`；旧布局（`java.library.path=${natives_directory}`）扁平化到 `natives` 根目录，26.2+ 新布局解压到 `natives/java` 等子目录；重名文件直接拒绝。
- 26.2+ 的 native JAR 作为普通 classifier 依赖留在 classpath，仍经过 HTTPS 域名白名单和 SHA1 校验；启动器只按当前架构选择对应 native JAR。
- 原生库解压前删除旧 `natives` 目录；解压目标再次经过 `SafeCombine`。

## 远程 JSON

- 使用 `JavaScriptSerializer.DeserializeObject` 解析为 `Dictionary` / `IList` 等纯数据结构。
- 不使用 `TypeNameHandling`、`BinaryFormatter`、`XmlSerializer` 按类型名实例化远程对象。
- 版本 `mainClass` 只允许合法 Java 类名字符；Fabric profile 额外要求以 `net.fabricmc.` 开头。
- Fabric profile 必须继承用户选择的原版版本；库 URL 必须在 allowlist。
- JVM 参数按 `JvmArgumentPolicy` 精确匹配参数名（不是子串包含），分两级：
  - **关键级**（对所有来源生效）：`-javaagent`、`-agentlib`、`-agentpath`、
    `-Xbootclasspath`（含 `/a`、`/p`）、`-Djava.system.class.loader`、
    `-Dsun.boot.class.path`、`-cp`/`-classpath`（用户与 Fabric 来源）、`-jar`、
    `-XX:OnError`、`-XX:OnOutOfMemoryError`、`-Djdk.java.options`、`-Djava.tool.options`。
  - **扩展级**（对用户输入与 Fabric profile 生效）：`--add-opens`、`--add-exports`、
    `--add-reads`、`--patch-module`、`--module-path`、`--upgrade-module-path`、`-p`、`-Dbootclasspath`。
  - 版本 JSON 只做关键级校验：Mojang 清单经 SHA1 校验，且未来版本可能合法使用模块参数；
    JSON 里的 `-cp` 被记录而非拒绝，因为启动器随后会用自己拼装的完整 classpath 覆盖它。
- 版本 `inheritsFrom` 限制深度 10，并检测循环继承。

## Mod 下载（Modrinth）

- 只访问 `api.modrinth.com` 与 `cdn.modrinth.com`，同样受 HTTPS、端口、重定向逐跳校验约束。
- Mod 项目标识（slug / project id / version id）只允许字母、数字、`-`、`_`，其他字符直接拒绝，避免拼接进 URL 或路径。
- 目标 Minecraft 版本必须是合法版本名，且在查询时作为参数转义，不允许注入 facets 语法。
- 安装前重新校验：目标游戏版本属于 Fabric；Mod 元数据声明支持 `fabric` 且包含该游戏版本；只从与所选 Fabric 版本映射的真实 Minecraft 版本查询。
- 每个待下载文件必须同时满足：
  - 文件名为单段 `.jar`（拒绝子目录、`..`、隐藏文件、非法字符、非 `.jar`、超长名）；
  - 下载地址在 allowlist 内；
  - 带有效 40 位 SHA1，并校验大小；
  - 拼接目标路径后再用 `SafeCombine` 确认仍在 `<游戏目录>\mods` 内。
- 依赖解析只跟随 `required` 前置，最大深度 4、最大文件数 24，防止依赖链被用来放大下载或无限递归。
- 已存在且 SHA1 一致的文件不计为新增，避免重复覆盖。
- Mod JAR 属于第三方代码，启动器只做传输与落盘校验，不解析、不执行其中内容；模组本身的安全性由 Modrinth 项目与其作者负责。

## 本地 Mod 安装（1.4.3）

用户从别处拿到的 `.jar` 也可以直接装进 mods 目录。安全边界刻意收得很紧，
因为它比「整合包导入」少涉多很多：

- **只接受单个本地 `.jar` 文件**：不解压、不执行、不递归目录、不处理 `.mrpack`；
- 文件名必须是**单段合法名**（拒绝子目录、`..`、隐藏文件、非法字符、超长名、非 `.jar`）；
- 目标路径经 `PathUtil.SafeCombine` 确认仍在 mods 目录内；
- 只**读取** jar 内的 `fabric.mod.json`（不加载类、不执行代码），读取有 256 KB 上限；
- **同名文件绝不覆盖**：已存在时自动加序号后缀，用户原文件不受影响；
- 先复制到临时文件再原子改名，中途失败不会留下半个 jar；
- 前置依赖只做**提示**，不自动安装——避免"装一个 jar 就顺带拉一堆代码"。

与整合包导入的关键差别：这里**完全不涉及** `overrides/` 目录、`server.properties`、
`ops.json`、`banned-*.json` 等配置文件，因此不存在「整合包注入启动命令/权限」这一类风险。

## 模组预设（1.4.3）

- 预设是**纯数据**（JSON），只含 mod id / 版本 / 显示名，不含可执行内容；
- 导入时只拿 `Slug` 去 Modrinth 官方 API 查询，标识字符集限定为字母、数字、`-`、`_`；
- 预设里**没有** jar 文件，也不从预设引入任何本地路径，因此不构成新的信任边界；
- 模组文件本身仍走 Modrinth 那条完整校验链路（HTTPS + 白名单 + SHA1 + 原子替换）。

## 启动过程

- 启动器不调用 `cmd.exe`、PowerShell 或任何命令解释器。
- `ProcessStartInfo.UseShellExecute = false`，直接启动 `java.exe` / `javaw.exe`。
- 所有参数由 `CommandLine.QuoteArgument` 按 Windows `CommandLineToArgvW` 规则转义，再交给 `CreateProcess`。
- 不继承 `JAVA_TOOL_OPTIONS`、`_JAVA_OPTIONS`、`JDK_JAVA_OPTIONS`，避免环境变量注入 JVM 参数。
- 三类来源（用户自定义、Fabric profile、版本 JSON）在启动前都经过 `JvmArgumentPolicy` 的统一校验，
  不再各自维护一套黑名单。
- Java 探测按来源可信度排序：`JAVA_HOME`、固定安装目录、Minecraft 自带运行时优先，
  `%PATH%` 中的目录放到最后；下拉框标注每个候选的来源，位于用户目录或临时目录的候选
  会标记为可能被篡改。探测会以 `-version` 执行候选程序（5 秒超时），这是识别 Java 的必要代价，
  因此来源提示必须可见。
- 默认加入 `-Dlog4j2.formatMsgNoLookups=true`，对旧版本的 Log4j 漏洞做启动级缓解。
- 离线账号 UUID 使用标准 `MD5("OfflinePlayer:" + name)`，设置 RFC 4122 version 3 / IETF variant；不生成、不保存访问令牌。
- 游戏进程输出通过管道写入本地日志，不经过 shell。
- TLS 按运行时能力启用 1.2 + 1.3；不支持 TLS 1.3 的运行时回退到仅 1.2，仍然不注册证书校验回调。
- 命令行超过 30000 字符且 Java 9+ 时，把参数逐行写入 `%APPDATA%\rll\rll-args-<guid>.txt`
  并以 `@文件` 方式交给 Java（参数转义按 Java @file 语法：反斜杠与引号转义、空白引号分组）；
  参数文件在启动完成后立即删除。Java 8 及以下不支持 @argfile，超长时给出明确错误而非截断。
  - **参数文件路径必须加引号**：路径位于 `%APPDATA%\rll` 下，而 `%APPDATA%` 含 Windows
    用户名。用户名带空格时未加引号的 `@C:\...\John Smith\...\rll-args-x.txt` 会被 Windows
    按 `CommandLineToArgvW` 规则拆成多个 token，Java 拿到截断路径 →「找不到参数文件」。
  - **参数文件在游戏进程退出后才删除**：`Process.Start` 只是创建进程，Java 尚未打开该文件，
    立即删除会造成偶发启动失败。

## 第三方认证（联机）

第三方认证功能基于 authlib-injector 标准协议实现，信任边界如下：

- **认证服务器主机默认不被信任**：`auth.example.com` 这类主机只有在用户添加认证服务器预设，
  或登录成功后，才进入运行时白名单（`HttpUtil.AllowedHosts`，仅内存）。所有 yggdrasil 请求
  （`/authserver/authenticate`、`/validate`、`/invalidate`）仍强制 HTTPS + 443 + DNS 主机 +
  重定向逐跳校验。
- **密码只在内存与请求体中存在**：登录请求发出后即被丢弃，绝不写入配置、日志或任何文件；
  配置中保存的是服务器返回的会话令牌（AccessToken）与档案 UUID——这是离线启动器接入正版
  模式服务器的必要数据（明文 JSON 存储）。
- **authlib-injector 属于第三方可执行代码**：官方不提供发布件哈希，因此自动下载只允许
  固定版本号（1.2.5）的官方 GitHub Release 地址、HTTPS、32 MB 上限、原子替换，无法做
  SHA1 校验。更高信任的用户应下载后自行核验，并用「选择本地 JAR...」指定文件。
- **联机能力等价性**：本功能让游戏通过 authlib-injector 完成 `hasJoined` 校验，从而进入
  该认证服务器开设的正版模式（`online-mode=true`）服务器——这是
  所提供的能力，不等于微软官方正版登录。
- **会话失效**：令牌可能因服务器侧失效而过期，账号页提供「校验令牌」；失效后重新登录即可，
  不会自动静默重登。
- **Java 注入参数白名单冲突说明**：authlib-injector 的 `-javaagent` 由启动器本机代码在
  `BuildPlan` 中拼接，来源是用户选择/保存的账号与注入器路径，不来自任何远程 JSON，
  因此不受远程参数注入面影响；用户自定义 JVM 参数中的 `-javaagent` 仍被 `JvmArgumentPolicy` 拒绝。

## 异地联机（EasyTier，1.3.0）

用 EasyTier 组建 P2P 虚拟局域网，让异地玩家像局域网一样联机。信任边界如下：

- **二进制信任**：只从官方 GitHub Releases 下载，**固定版本 `v2.6.4` + SHA256 硬校验**
  （`27af91e2…5f525f84`，取值来源为 GitHub API 的 `assets[].digest`，非臆造）。
  哈希不符即删除下载文件并报错。启动器只把核心作为**子进程**运行，不加载为库；
  用户也可用「浏览…」指定本地已核验的 exe。
- **网络密钥明文存储**：`EasyTierNetworkSecret` 保存在 `config.json` 中。它等于该网络的
  准入凭证（知道网络名 + 密钥的人即可加入），因此配置文件的 ACL 已收紧为
  "仅当前用户 / SYSTEM / Administrators"（见下文「本地数据」），且设置页有明确提示。
- **密钥不写入日志**：`easytier-core` 的启动参数里含 `--network-secret`，启动器**不把完整参数
  写进日志**，只记录脱敏后的版本（密钥位置显示为 `****`）。日志文件本身同样收紧 ACL
  （仅当前用户 / SYSTEM / Administrators），因为日志里可能出现会话令牌等敏感信息。
- **进程命令行暴露（上游接口限制，无法完全消除）**：`easytier-core` 只接受命令行参数接收密钥，
  因此同机任意进程都可通过 WMI / `Get-CimInstance Win32_Process` 读取到
  `easytier-core.exe --network-secret <值>`。这是上游核心的接口设计，启动器无法规避。
  缓解手段是收紧配置目录 ACL，并避免在启动器自身的日志里再次记录密钥。
- **P2P 直连语义**：组网后虚拟网段内成员可互相直连访问，不经过启动器或任何中继授权。
  请只与你信任的人共享网络名与密钥。
- **不越界**：启动器只负责"加入网络 + 展示本机虚拟 IP"，**不修改 `server.properties`、
  不向游戏注入 `server-ip`、不内置服务端**。虚拟 IP 由用户在游戏内手动输入。
- **公共节点**：默认使用官方文档维护的 `tcp://public.easytier.top:11010`；可自行替换。
  该地址由 EasyTier 核心进程直连，不经过 `HttpUtil`，因此不受启动器 HTTP 白名单约束
  （它不是 HTTP 请求，而是核心自己的组网协议流量）。
- **进程生命周期**：启动器退出时调用 `StopCore()` 结束核心，避免留下孤儿进程。

## 下载源镜像（1.3.0）

- 新增 `DownloadMirrors`，可切换到 BmclAPI 或自定义镜像。**镜像只替换 URL，不参与校验**：
  下载完成后仍按官方清单里的 SHA1 比对，因此镜像返回被篡改的文件会被校验拦截。
- 自定义镜像必须是 HTTPS + 443 + 合法域名，非法值在保存时拒绝并回落官方源；
  其主机在保存后加入运行时白名单（与认证服务器同机制）。
- 只映射已知的官方主机（Mojang / Fabric）；Modrinth API 与 CDN 不映射，
  未知主机保持官方地址，避免把任意流量转发到第三方站点。
- 镜像可能滞后或不完整，仅作官方源不可用时的备用通道。

## 版本隔离（1.3.0）

- 开启后每个版本的 `--gameDir` 指向 `versions\<版本>\instance\`，存档/配置/模组互相隔离；
  `assets` 与 `libraries` 仍共享，不产生重复下载。
- 仅创建目录，**不移动或删除任何既有数据**；关闭开关即回到旧布局，原数据保持不变。
- 版本名经 `ValidateVersionId` 校验（拒绝 `..`、路径分隔符等），实例目录无法被穿越到版本目录之外。

## 本地数据

- 默认配置：`%APPDATA%\rll\config.json`（首次启动会自动迁移旧版 `%APPDATA%\LiteLauncher` 的设置）
- 默认日志：`%APPDATA%\rll\logs\`
  - 日志 ACL 已收紧为「仅当前用户 / SYSTEM / Administrators」且不继承上级目录
    （与 `config.json` 一致），因为日志中可能出现会话令牌与网络密钥。
- 默认游戏目录：程序所在目录下的 `.minecraft`
- Mod 目录：`<游戏目录>\mods`，只写入通过 SHA1 校验、文件名为单段 `.jar` 的文件
- authlib-injector 默认路径：`%APPDATA%\rll\authlib-injector.jar`（可改）
- 可移植模式：程序目录存在 `portable.txt` 时使用 `data\`
- 不写注册表、不创建服务、不设置开机启动、不安装驱动。
- 配置只保存游戏目录、Java 路径、内存、JVM 参数、窗口大小、账号列表（离线用户名 /
  第三方认证的会话令牌与档案 UUID）、认证服务器预设、注入器路径、上次用户名和上次版本。
- 不保存任何账号密码、微软账号 OAuth token、refresh token、Cookie 或设备码。
- 日志可能包含本地路径、用户名和游戏输出；日志仅保存在本机，不自动上传。

## 常见风险与处理方式

| 风险 | 处理方式 |
| --- | --- |
| 远程主页 XAML 可写 `LaunchAdvanceRun` 导致命令执行 | 无远程 UI、无 XAML、无远程命令字段 |
| 更新包无签名 | 首版不做自动更新，避免无签名更新链 |
| 整合包可注入启动命令/JVM 参数 | 首版不导入整合包；JVM 参数有禁止列表 |
| SSL 校验可关闭 | 无全局回调、无绕过开关；TLS 按能力启用 1.2 + 1.3 |
| EasyTier 仅校验大小 | 所有下载校验 SHA1 + 大小；authlib-injector 无哈希，仅官方地址 + 32MB 上限 |
| 可逆凭据存储 | 不保存密码；第三方认证仅保存会话令牌（明文 JSON，文件权限已收紧） |
| 默认遥测 | 无遥测、无统计上报 |
| 硬编码第三方 API Key | 不内置任何 API Key |
| 第三方认证联机（authlib-injector） | 已实现：认证服务器主机需用户显式添加才入白名单；密码不落盘 |

## 报告安全问题

如果发现安全问题，请不要在公开 issue 中粘贴可利用细节、账号信息或完整日志。请提供：

- 受影响版本（`rll.exe` 文件版本）
- 复现步骤
- 预期行为与实际行为
- 相关日志片段（先删除用户名、路径等个人信息）

在修复发布前，请给维护者合理的处理时间。
