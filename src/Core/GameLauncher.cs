using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace LiteLauncher.Core
{
    public sealed class LaunchOptions
    {
        public string GameDir;
        public string VersionId;
        public string Username;
        public string JavaPath;
        public int MinMemoryMB;
        public int MaxMemoryMB;
        public string JvmArgs;
        public bool CustomResolution;
        public int WindowWidth;
        public int WindowHeight;
        /// <summary>
        /// 使用的账号。null = 按离线账号处理（本地 UUID）。
        /// 非 null 且 Type=Authlib 时走第三方认证：注入 -javaagent、
        /// 使用服务器返回的 UUID/会话令牌，可进入正版模式服务器。
        /// </summary>
        public Account Account;
        /// <summary>authlib-injector JAR 路径（认证账号必填）。</summary>
        public string AuthlibInjectorPath;
        /// <summary>
        /// 版本隔离：为 true 时把游戏的 --gameDir 指向该版本的独立实例目录，
        /// 使不同版本的存档/配置/模组互不可见。assets 与 libraries 仍共享。
        /// </summary>
        public bool IsolateVersions;

        public LaunchOptions()
        {
            GameDir = AppPaths.DefaultGameDirectory;
            VersionId = "";
            Username = "Player";
            JavaPath = "";
            MinMemoryMB = 1024;
            MaxMemoryMB = 4096;
            JvmArgs = "";
            CustomResolution = false;
            WindowWidth = 854;
            WindowHeight = 480;
            Account = null;
            AuthlibInjectorPath = "";
        }
    }

    public sealed class LaunchPlan
    {
        public string JavaPath;
        public string Arguments;
        public string WorkingDirectory;
        public string VersionId;
        public string MainClass;
        public string Classpath;
        public string NativesDirectory;
        /// <summary>使用 @argfile 时生成的参数文件路径；启动后由界面层清理。</summary>
        public string ArgFilePath;
    }

    public static class GameLauncher
    {
        /// <summary>
        /// 解析版本、选择 Java、检查本地文件并生成完整启动计划。
        /// 该方法不启动任何进程，便于测试和界面层先展示错误。
        /// </summary>
        public static LaunchPlan Prepare(LaunchOptions options, CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (string.IsNullOrEmpty(options.GameDir))
            {
                throw new ArgumentException("游戏目录为空。", "options");
            }
            PathUtil.ValidateVersionId(options.VersionId);
            // 1.7.5（B-06）：微软账号同样需要前置校验（令牌/UUID 完整）；
            // 但它不需要 authlib-injector，因此校验分两级。
            bool authAccount = options.Account != null &&
                (options.Account.Type == AccountType.Authlib ||
                 options.Account.Type == AccountType.Microsoft);
            if (authAccount)
            {
                if (!AuthValidation.IsValidAuthDisplayName(options.Username))
                {
                    throw new ArgumentException("认证账号的显示名无效。", "options");
                }
                if (options.Account.Type == AccountType.Authlib)
                {
                    ValidateAuthSetup(options);
                }
                else
                {
                    ValidateMicrosoftSetup(options);
                }
            }
            else if (!CommandLine.IsValidUsername(options.Username))
            {
                throw new ArgumentException("离线用户名必须是 3-16 位字母、数字或下划线。", "options");
            }

            string gameDir = Path.GetFullPath(options.GameDir);
            ValidateGameDirectory(gameDir);
            if (!Directory.Exists(gameDir))
            {
                Directory.CreateDirectory(gameDir);
            }
            cancellationToken.ThrowIfCancellationRequested();

            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, options.VersionId);
            cancellationToken.ThrowIfCancellationRequested();

            JavaInfo java = SelectJava(options, version, cancellationToken);
            if (java == null)
            {
                throw new InvalidOperationException(
                    "没有找到可用的 Java。请安装 Java " +
                    (version.JavaVersionMajor > 0 ? version.JavaVersionMajor.ToString() : "8") +
                    " 或更高版本，然后在启动页手动选择 javaw.exe。");
            }
            int required = version.JavaVersionMajor > 0 ? version.JavaVersionMajor : 8;
            if (java.Major > 0 && java.Major < required)
            {
                throw new InvalidOperationException(
                    "所选 Java " + java.Major + " 低于该版本要求的 Java " + required +
                    "，请更换 Java 后重试。");
            }

            VerifyLocalFiles(gameDir, version, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNatives(gameDir, version, cancellationToken);

            // 1.7.7（B-21）：隔离时游戏的工作目录是 versions\<版本>\instance。
            // 该目录必须在实际启动前就存在，否则 GameLauncher.Start 会因为
            // WorkingDirectory 不存在而直接失败（界面层创建过的实例目录可能因
            // 用户换了游戏目录或清理过磁盘而缺失，这里再兜一次）。
            if (options.IsolateVersions)
            {
                InstancePaths.EnsureInstanceDirectory(gameDir, version.Id);
            }

            LaunchPlan plan = BuildPlan(version, options, java, gameDir);
            Log.Info("启动计划已生成：版本 " + version.Id + "，Java " +
                     (java.Major > 0 ? java.Major.ToString() : "未知") +
                     "，主类 " + version.MainClass);
            return plan;
        }

        /// <summary>
        /// 纯参数构建：不做磁盘检查，测试可以直接构造 EffectiveVersion 调用。
        /// </summary>
        public static LaunchPlan BuildPlan(
            EffectiveVersion version,
            LaunchOptions options,
            JavaInfo java,
            string gameDir)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (java == null || string.IsNullOrEmpty(java.LaunchPath))
            {
                throw new ArgumentException("Java 路径为空。", "java");
            }
            PathUtil.ValidateVersionId(version.Id);
            if (string.IsNullOrEmpty(version.MainClass))
            {
                throw new InvalidDataException("版本缺少 mainClass。");
            }

            string fullGameDir = Path.GetFullPath(gameDir);
            ValidateGameDirectory(fullGameDir);
            string librariesDirectory = VersionResolver.GetLibrariesDirectory(fullGameDir);
            string assetsDirectory = VersionResolver.GetAssetsDirectory(fullGameDir);
            string nativesDirectory = VersionResolver.GetNativesDirectory(fullGameDir, version.Id);
            string assetsId = version.AssetsId ?? "";

            string clientJar = VersionResolver.GetClientJarPath(fullGameDir, version);
            List<string> classpathEntries = new List<string>();
            classpathEntries.Add(clientJar);
            for (int i = 0; i < version.ClasspathArtifacts.Count; i++)
            {
                DownloadInfo library = version.ClasspathArtifacts[i];
                if (library == null || string.IsNullOrEmpty(library.Path))
                {
                    continue;
                }
                string path = PathUtil.SafeCombine(librariesDirectory, library.Path);
                if (!ContainsIgnoreCase(classpathEntries, path))
                {
                    classpathEntries.Add(path);
                }
            }
            string classpath = string.Join(";", classpathEntries.ToArray());

            string gameAssetsDirectory = assetsDirectory;
            if (VersionResolver.ReadAssetIndexVirtual(fullGameDir, assetsId))
            {
                gameAssetsDirectory = PathUtil.SafeCombine(
                    assetsDirectory,
                    "virtual/" + assetsId);
            }

            // 1.7.7（B-21）：本次启动的「唯一游戏目录」。
            // 开启隔离时 = versions\<版本>\instance；关闭时 = <游戏目录>。
            // 它同时决定 ${game_directory}、 --gameDir、进程工作目录与 mods 位置，
            // 四处必须同源 —— 此前只把占位符换成实例目录，plan.WorkingDirectory
            // 却写死共享目录，且版本 JSON 缺 --gameDir 时根本不会传该参数，
            // 游戏于是按进程工作目录读 <游戏目录>\mods，隔离形同虚设。
            string launchGameDir = fullGameDir;
            if (options.IsolateVersions)
            {
                launchGameDir = InstancePaths.GetInstanceDirectory(fullGameDir, version.Id);
            }

            Dictionary<string, string> values = BuildPlaceholderValues(
                version,
                options,
                classpath,
                nativesDirectory,
                assetsDirectory,
                gameAssetsDirectory,
                librariesDirectory,
                launchGameDir);
            Dictionary<string, bool> features = BuildFeatures(options);

            List<string> arguments = new List<string>();
            int minMemory = Clamp(options.MinMemoryMB, 512, 65536);
            int maxMemory = Clamp(options.MaxMemoryMB, minMemory, 65536);
            arguments.Add("-Xms" + minMemory + "M");
            arguments.Add("-Xmx" + maxMemory + "M");
            // 对旧版本中可能存在的 Log4j 漏洞做启动级缓解，不影响新版本。
            arguments.Add("-Dlog4j2.formatMsgNoLookups=true");

            // 第三方认证（联机）：注入 authlib-injector，使游戏通过认证服务器
            // 完成 hasJoined 校验，从而进入该服务器的正版模式服务器。
            if (options.Account != null && options.Account.Type == AccountType.Authlib)
            {
                string injectorPath = AuthlibInjectorService.ResolveInjectorPath(options.AuthlibInjectorPath);
                if (!File.Exists(injectorPath))
                {
                    throw new FileNotFoundException(
                        "缺少 authlib-injector，请先在账号页下载或选择注入器。", injectorPath);
                }
                arguments.Add(AuthlibInjectorService.BuildJavaAgentArgument(
                    injectorPath, options.Account.AuthServerUrl));
            }

            bool hasLibraryPath = false;
            // 版本 JSON 里若带 "-cp ${classpath}"（Mojang 1.13+ 的标准写法），
            // 旧实现只是"记录"它并因此【跳过】追加自己拼装的 classpath，等于把
            // classpath 的决定权交给了远端 JSON——一旦 JSON 里写的是字面路径而不是
            // ${classpath} 占位符，启动器拼好的完整 classpath 就会被整个丢弃。
            // 现在改为：丢弃 JSON 里的 -cp/-classpath 及其值，始终由启动器追加
            // 自己拼装的完整 classpath（见下方 arguments.Add("-cp") 处）。
            bool skipNextClasspathValue = false;
            for (int i = 0; i < version.JvmArguments.Count; i++)
            {
                ArgumentEntry entry = version.JvmArguments[i];
                if (entry == null || !RuleEvaluator.IsAllowed(entry.Rules, features))
                {
                    continue;
                }
                for (int j = 0; j < entry.Values.Count; j++)
                {
                    string value = ReplacePlaceholders(entry.Values[j], values);
                    if (value == null || value.Length == 0)
                    {
                        continue;
                    }
                    // 丢弃 JSON 提供的 classpath 参数（及其值），由启动器统一提供。
                    if (skipNextClasspathValue)
                    {
                        skipNextClasspathValue = false;
                        continue;
                    }
                    if (string.Equals(value, "-cp", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "-classpath", StringComparison.OrdinalIgnoreCase))
                    {
                        skipNextClasspathValue = true;
                        continue;
                    }
                    // 版本 JSON 只做关键级校验：-javaagent / -Xbootclasspath /
                    // -XX:OnError 会直接加载或执行任意本地代码，必须拒绝。
                    if (JvmArgumentPolicy.IsCritical(value))
                    {
                        throw new InvalidDataException(
                            "版本配置包含不允许的 JVM 参数：" + value);
                    }
                    if (value.StartsWith("-Djava.library.path=", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLibraryPath = true;
                    }
                    arguments.Add(value);
                }
            }

            if (!string.IsNullOrEmpty(version.LoggingArgument) && version.LoggingFile != null)
            {
                string loggingPath = PathUtil.SafeCombine(
                    Path.Combine(assetsDirectory, "log_configs"),
                    version.LoggingFile.Path);
                string loggingArgument = ReplacePlaceholders(version.LoggingArgument, values);
                loggingArgument = loggingArgument.Replace("${path}", loggingPath);
                arguments.Add(loggingArgument);
            }

            List<string> userArguments = CommandLine.SplitArguments(options.JvmArgs);
            for (int i = 0; i < userArguments.Count; i++)
            {
                string value = userArguments[i];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }
                // 用户自定义参数走最严格的等级：-cp/-classpath 也在黑名单里，
                // 因此这里不需要再单独处理 classpath（IsBanned 会先抛异常）。
                if (JvmArgumentPolicy.IsBanned(value))
                {
                    throw new InvalidOperationException(
                        "出于安全考虑，自定义 JVM 参数中不允许使用：" + value);
                }
                if (value.StartsWith("-Djava.library.path=", StringComparison.OrdinalIgnoreCase))
                {
                    hasLibraryPath = true;
                }
                arguments.Add(value);
            }

            if (!hasLibraryPath)
            {
                arguments.Add("-Djava.library.path=" + nativesDirectory);
            }
            // 始终由启动器追加自己拼装的完整 classpath。
            // 旧实现在版本 JSON 里出现 -cp 时会跳过这里（把 classpath 交给远端 JSON），
            // 现在无论 JSON 写没写 -cp，都以启动器的拼装结果为准。
            arguments.Add("-cp");
            arguments.Add(classpath);
            arguments.Add(version.MainClass);

            // 记录游戏参数的起点：后面只在这一段里校正 --gameDir，
            // 不会误伤 JVM 参数（例如 -Djava.library.path）。
            int gameArgumentsStart = arguments.Count;

            bool usedLegacyArguments = false;
            if (version.GameArguments.Count > 0)
            {
                for (int i = 0; i < version.GameArguments.Count; i++)
                {
                    ArgumentEntry entry = version.GameArguments[i];
                    if (entry == null || !RuleEvaluator.IsAllowed(entry.Rules, features))
                    {
                        continue;
                    }
                    for (int j = 0; j < entry.Values.Count; j++)
                    {
                        arguments.Add(ReplacePlaceholders(entry.Values[j], values));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(version.LegacyMinecraftArguments))
            {
                usedLegacyArguments = true;
                List<string> legacy = CommandLine.SplitArguments(version.LegacyMinecraftArguments);
                for (int i = 0; i < legacy.Count; i++)
                {
                    arguments.Add(ReplacePlaceholders(legacy[i], values));
                }
            }
            else
            {
                throw new InvalidDataException("版本没有可用的游戏参数。");
            }

            if (options.CustomResolution && usedLegacyArguments)
            {
                arguments.Add("--width");
                arguments.Add(Clamp(options.WindowWidth, 320, 7680).ToString());
                arguments.Add("--height");
                arguments.Add(Clamp(options.WindowHeight, 240, 4320).ToString());
            }

            // 1.7.7（B-21）：隔离的硬保障。版本 JSON 是否写 --gameDir 属于外部数据，
            // 不能当作隔离生效的前提；Fabric profile、裁剪过的自定义版本或旧版
            // minecraftArguments 都可能没有这一项。这里无论上游怎么写，都强制把
            // 游戏目录指向 launchGameDir，让「装到哪 / 读哪里」永远一致。
            ForceGameDirectoryArgument(arguments, gameArgumentsStart, launchGameDir);

            LaunchPlan plan = new LaunchPlan();
            plan.JavaPath = string.IsNullOrEmpty(java.ProbePath) ? java.LaunchPath : java.ProbePath;
            string commandLine = CommandLine.BuildArguments(arguments);
            plan.Arguments = commandLine;
            if (CommandLine.ShouldUseArgFile(commandLine.Length, java.Major))
            {
                string content;
                string argFile = CommandLine.WriteArgFile(arguments, AppPaths.DataDirectory, java.Major, out content);
                plan.ArgFilePath = argFile;
                // 必须加引号：参数文件路径位于 %APPDATA%\rll 下，而 %APPDATA% 含
                // Windows 用户名。用户名里只要有空格（"John Smith"），未加引号的
                // "@C:\...\John Smith\...\rll-args-x.txt" 会被 Windows 按
                // CommandLineToArgvW 规则拆成多个 token，Java 拿到的是被截断的
                // 路径，表现为"找不到参数文件"、游戏带着空参数启动。
                plan.Arguments = "@" + CommandLine.QuoteArgument(argFile);
                Log.Info("命令行过长（" + commandLine.Length +
                         " 字符），已改用 Java @argfile：" + argFile);
            }
            else if (commandLine.Length > CommandLine.HardCommandLineLimit)
            {
                throw new InvalidOperationException(
                    "命令行过长（" + commandLine.Length +
                    " 字符），且所选 Java " + (java.Major > 0 ? java.Major.ToString() : "未知") +
                    " 不支持 @argfile（需要 Java 9+）。请减少模组数量或更换较新的 Java。");
            }
            // 工作目录必须与 --gameDir 一致：部分模组 / 加载器会读 user.dir
            // 下的 ./mods，而不是 --gameDir 下的 mods。隔离时若仍用共享目录，
            // 这些模组依旧会被所有版本共用。
            plan.WorkingDirectory = launchGameDir;
            plan.VersionId = version.Id;
            plan.MainClass = version.MainClass;
            plan.Classpath = classpath;
            plan.NativesDirectory = nativesDirectory;
            return plan;
        }

        public static Process Start(LaunchPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            if (string.IsNullOrEmpty(plan.JavaPath) || !File.Exists(plan.JavaPath))
            {
                throw new FileNotFoundException("Java 不存在：" + plan.JavaPath, plan.JavaPath);
            }
            if (string.IsNullOrEmpty(plan.WorkingDirectory) || !Directory.Exists(plan.WorkingDirectory))
            {
                throw new DirectoryNotFoundException("游戏目录不存在：" + plan.WorkingDirectory);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = plan.JavaPath;
            startInfo.Arguments = plan.Arguments;
            startInfo.WorkingDirectory = plan.WorkingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            // 不继承可能注入 JVM 参数的环境变量。
            startInfo.EnvironmentVariables.Remove("JAVA_TOOL_OPTIONS");
            startInfo.EnvironmentVariables.Remove("_JAVA_OPTIONS");
            startInfo.EnvironmentVariables.Remove("JDK_JAVA_OPTIONS");

            Process process = new Process();
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    WriteGameLog(e.Data);
                }
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    WriteGameLog(e.Data);
                }
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动 Java 进程。");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        public static void Kill(Process process)
        {
            if (process == null)
            {
                return;
            }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // 用户可能已经关闭游戏。
            }
        }

        private static JavaInfo SelectJava(
            LaunchOptions options,
            EffectiveVersion version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(options.JavaPath))
            {
                JavaInfo selected = JavaFinder.Probe(options.JavaPath);
                if (selected != null)
                {
                    return selected;
                }
                throw new InvalidOperationException("无法使用指定的 Java：" + options.JavaPath);
            }

            int required = version.JavaVersionMajor > 0 ? version.JavaVersionMajor : 8;
            List<JavaInfo> candidates = JavaFinder.FindAll();
            cancellationToken.ThrowIfCancellationRequested();
            return JavaFinder.ChooseBest(candidates, required);
        }

        /// <summary>
        /// 认证账号启动前置校验：会话令牌、UUID、认证服务器地址必须完整有效，
        /// 注入器 JAR 必须存在（缺失时由界面层在 Prepare 前下载）。
        /// </summary>
        private static void ValidateAuthSetup(LaunchOptions options)
        {
            Account account = options.Account;
            if (account == null || string.IsNullOrEmpty(account.AccessToken) ||
                string.IsNullOrEmpty(account.Uuid) || string.IsNullOrEmpty(account.DisplayName))
            {
                throw new InvalidOperationException(
                    "认证账号缺少会话数据，请到账号页重新登录。");
            }
            if (!AuthValidation.IsValidUuid(account.Uuid))
            {
                throw new InvalidOperationException("认证账号的 UUID 无效，请重新登录。");
            }
            // 统一成带连字符的标准形式再写入启动参数：
            // 有的服务器返回 32 位无连字符 UUID，直接传给游戏会导致校验失败。
            account.Uuid = AuthValidation.NormalizeUuid(account.Uuid);
            if (!AuthValidation.IsValidAuthServerUrl(account.AuthServerUrl))
            {
                throw new InvalidOperationException("认证服务器地址无效，请重新配置。");
            }
            string injectorPath = AuthlibInjectorService.ResolveInjectorPath(options.AuthlibInjectorPath);
            if (!File.Exists(injectorPath))
            {
                throw new FileNotFoundException(
                    "缺少 authlib-injector，请先在账号页下载或选择注入器。", injectorPath);
            }
        }

        /// <summary>
        /// 1.7.5（B-06）：微软正版账号的启动前置校验。
        /// 与 Authlib 不同，它不需要 authlib-injector（不注入 -javaagent），
        /// 但必须持有 Minecraft 令牌与带连字符的正版 UUID，否则进不了正版服务器。
        /// 令牌过期由界面层在启动前用 refresh_token 刷新（见 TryRefreshMicrosoftAsync）。
        /// </summary>
        private static void ValidateMicrosoftSetup(LaunchOptions options)
        {
            Account account = options.Account;
            if (account == null || string.IsNullOrEmpty(account.AccessToken) ||
                string.IsNullOrEmpty(account.Uuid))
            {
                throw new InvalidOperationException(
                    "微软账号缺少会话数据（令牌或 UUID 为空），请到账号页重新登录微软账号。");
            }
            if (!AuthValidation.IsValidUuid(account.Uuid))
            {
                throw new InvalidOperationException("微软账号的 UUID 无效，请重新登录。");
            }
            // 统一成带连字符的标准形式再写入启动参数（api.minecraftservices 返回
            // 无连字符 UUID，游戏侧校验要求带连字符形式）。
            account.Uuid = AuthValidation.NormalizeUuid(account.Uuid);
        }

        private static void VerifyLocalFiles(
            string gameDir,
            EffectiveVersion version,
            CancellationToken cancellationToken)
        {
            string clientJar = VersionResolver.GetClientJarPath(gameDir, version);
            if (!File.Exists(clientJar))
            {
                throw new FileNotFoundException(
                    "客户端 JAR 不存在，请先安装版本 " + version.Id + "：" + clientJar,
                    clientJar);
            }
            if (version.AssetIndex != null && !string.IsNullOrEmpty(version.AssetsId))
            {
                string indexPath = VersionResolver.GetAssetIndexPath(gameDir, version.AssetsId);
                if (!File.Exists(indexPath))
                {
                    throw new FileNotFoundException(
                        "资源索引不存在，请先安装版本 " + version.Id + "：" + indexPath,
                        indexPath);
                }
            }

            string librariesDirectory = VersionResolver.GetLibrariesDirectory(gameDir);
            for (int i = 0; i < version.ClasspathArtifacts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadInfo library = version.ClasspathArtifacts[i];
                if (library == null || string.IsNullOrEmpty(library.Path))
                {
                    continue;
                }
                string path = PathUtil.SafeCombine(librariesDirectory, library.Path);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "库文件缺失，请重新安装版本 " + version.Id + "：" + path,
                        path);
                }
            }
            for (int i = 0; i < version.NativeArtifacts.Count; i++)
            {
                DownloadInfo nativeLibrary = version.NativeArtifacts[i].Download;
                if (nativeLibrary == null || string.IsNullOrEmpty(nativeLibrary.Path))
                {
                    continue;
                }
                string path = PathUtil.SafeCombine(librariesDirectory, nativeLibrary.Path);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "本地库缺失，请重新安装版本 " + version.Id + "：" + path,
                        path);
                }
            }
        }

        private static void EnsureNatives(
            string gameDir,
            EffectiveVersion version,
            CancellationToken cancellationToken)
        {
            string nativesDirectory = VersionResolver.GetNativesDirectory(gameDir, version.Id);
            string targetDirectory = VersionResolver.GetNativesTargetDirectory(gameDir, version);
            bool needsExtract = !Directory.Exists(targetDirectory);
            if (!needsExtract)
            {
                try
                {
                    // natives 会被扁平化到目标目录；只检查目标目录 DLL，
                    // 这样旧版本残留的根目录/子目录布局也会触发重新解压。
                    needsExtract = Directory.GetFiles(
                        targetDirectory,
                        "*.dll",
                        SearchOption.TopDirectoryOnly).Length == 0;
                }
                catch
                {
                    needsExtract = true;
                }
            }
            if (version.NativeArtifacts.Count == 0)
            {
                VersionResolver.EnsureNativesLayout(nativesDirectory, version);
                return;
            }
            if (!needsExtract)
            {
                VersionResolver.EnsureNativesLayout(nativesDirectory, version);
                return;
            }

            Log.Info("正在解压本地库到 " + targetDirectory);
            NativeExtractor.Extract(
                VersionResolver.GetLibrariesDirectory(gameDir),
                nativesDirectory,
                version.NativesRelativePath,
                version.NativeArtifacts,
                null,
                cancellationToken);
            VersionResolver.EnsureNativesLayout(nativesDirectory, version);
        }

        /// <summary>
        /// 1.7.7（B-21）：把游戏参数段里的 --gameDir 强制校正为 launchGameDir；
        /// 上游若根本没写这一项，就补上。
        ///
        /// 兼容三种写法：标准的两段式（--gameDir &lt;值&gt;）、老 Beta 的
        /// --workDir、以及合写成一个 token 的 --gameDir=&lt;值&gt;。
        /// 只在游戏参数段（startIndex 之后）查找，避免误改 JVM 参数。
        /// </summary>
        private static void ForceGameDirectoryArgument(
            List<string> arguments,
            int startIndex,
            string gameDirectory)
        {
            for (int i = startIndex; i < arguments.Count; i++)
            {
                string token = arguments[i] ?? "";
                if (token.StartsWith("--gameDir=", StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith("--workDir=", StringComparison.OrdinalIgnoreCase))
                {
                    int equals = token.IndexOf('=');
                    arguments[i] = token.Substring(0, equals + 1) + gameDirectory;
                    return;
                }
                if (!string.Equals(token, "--gameDir", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(token, "--workDir", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (i + 1 < arguments.Count)
                {
                    arguments[i + 1] = gameDirectory;
                }
                else
                {
                    arguments.Add(gameDirectory);
                }
                return;
            }
            // 上游没写 --gameDir：补一段，游戏就不会退回进程工作目录。
            arguments.Add("--gameDir");
            arguments.Add(gameDirectory);
        }

        private static Dictionary<string, string> BuildPlaceholderValues(
            EffectiveVersion version,
            LaunchOptions options,
            string classpath,
            string nativesDirectory,
            string assetsDirectory,
            string gameAssetsDirectory,
            string librariesDirectory,
            string launchGameDir)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            string versionType = string.IsNullOrEmpty(version.Type) ? "release" : version.Type;

            string playerName;
            string uuid;
            string accessToken;
            string userType;
            if (options.Account != null &&
                (options.Account.Type == AccountType.Authlib ||
                 options.Account.Type == AccountType.Microsoft))
            {
                // 第三方认证 / 微软正版：使用服务端分配的档案（UUID 带连字符）与会话令牌，
                // userType=mojang 与正版行为一致。
                //
                // 1.7.5 修复（B-06）：此前分支只判断 Authlib，微软账号（AccountType.Microsoft）
                // 落进 else 的离线分支 —— 拿 accessToken="0"、userType="legacy"、
                // 本地 v3 UUID，加入任何正版服务器必然验证失败。1.7.0 宣称的"微软正版登录"
                // 实际是坏功能。现在两类账号共用同一条认证分支（authlib-injector 只对
                // Authlib 注入，见 :215 的条件）。
                playerName = options.Account.Type == AccountType.Authlib
                    ? options.Account.DisplayName
                    : (string.IsNullOrEmpty(options.Account.UserName)
                        ? options.Account.DisplayName : options.Account.UserName);
                // 1.7.5：UUID 一律转成带连字符形式。api.minecraftservices 与部分认证服务器
                // 返回的是 32 位无连字符 UUID，直接传给游戏会导致正版校验失败；
                // 这里做一次兜底规范化，不依赖调用方是否走过 Prepare。
                uuid = AuthValidation.NormalizeUuid(options.Account.Uuid);
                accessToken = options.Account.AccessToken;
                userType = "mojang";
            }
            else
            {
                // 离线：本地 UUID v3 + 空令牌。
                playerName = options.Username;
                uuid = CommandLine.ComputeOfflineUuidCompact(options.Username);
                accessToken = "0";
                userType = "legacy";
            }

            values["auth_player_name"] = playerName;
            values["version_name"] = options.VersionId;
            // 版本隔离：游戏工作目录改指该版本的独立实例目录，存档/配置/模组互不可见。
            // assets、libraries、natives 仍使用共享目录，因此不会重复下载。
            // 1.7.7（B-21）：值由调用方统一计算后传入，避免这里按未归一化的
            // options.GameDir 再算一遍，与 plan.WorkingDirectory 出现第二条口径。
            values["game_directory"] = launchGameDir;
            values["assets_root"] = assetsDirectory;
            values["game_assets"] = gameAssetsDirectory;
            values["assets_index_name"] = version.AssetsId ?? "";
            values["auth_uuid"] = uuid;
            values["uuid"] = uuid;
            values["auth_access_token"] = accessToken;
            values["accessToken"] = accessToken;
            values["auth_session"] = accessToken;
            values["user_type"] = userType;
            values["version_type"] = versionType;
            values["user_properties"] = "{}";
            values["clientid"] = "";
            values["auth_xuid"] = "";
            values["launcher_name"] = "RLL";
            values["launcher_version"] = AppPaths.LauncherVersion;
            values["classpath"] = classpath;
            values["natives_directory"] = nativesDirectory;
            values["library_directory"] = librariesDirectory;
            values["classpath_separator"] = ";";
            values["resolution_width"] = Clamp(options.WindowWidth, 320, 7680).ToString();
            values["resolution_height"] = Clamp(options.WindowHeight, 240, 4320).ToString();
            values["quickPlayPath"] = "";
            values["quickPlaySingleplayer"] = "";
            values["quickPlayMultiplayer"] = "";
            values["quickPlayRealms"] = "";
            return values;
        }

        private static Dictionary<string, bool> BuildFeatures(LaunchOptions options)
        {
            LaunchFeatures features = new LaunchFeatures();
            features.IsDemoUser = false;
            features.HasCustomResolution = options.CustomResolution;
            features.HasQuickPlaysSupport = false;
            return features.ToDictionary();
        }

        private static string ReplacePlaceholders(string value, Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            string result = value;
            foreach (KeyValuePair<string, string> pair in values)
            {
                result = result.Replace("${" + pair.Key + "}", pair.Value ?? "");
            }
            return result;
        }

        private static bool IsBannedUserJvmArgument(string value)
        {
            // 统一走 JvmArgumentPolicy（参数名精确匹配），不再各自维护子串黑名单。
            return JvmArgumentPolicy.IsBanned(value);
        }

        private static void ValidateGameDirectory(string gameDir)
        {
            // 分号是 Windows classpath 分隔符；如果游戏目录包含分号，
            // 生成的 classpath 会被拆成额外条目，因此直接拒绝。
            if (gameDir.IndexOf(';') >= 0)
            {
                throw new ArgumentException("游戏目录不能包含分号（;）。", "gameDir");
            }
        }

        private static bool ContainsIgnoreCase(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static void WriteGameLog(string line)
        {
            if (line.Length > 4000)
            {
                line = line.Substring(0, 4000) + "…";
            }
            // 走 Log.Game：只入队、由后台线程批量写盘。
            // 这里绝不能做同步文件 I/O——游戏刷日志时管道会被反压，
            // 游戏卡在写 stdout 上，表现为"游戏窗口很久才出来"。
            Log.Game(line);
        }
    }
}
