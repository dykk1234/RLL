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
            bool authAccount = options.Account != null && options.Account.Type == AccountType.Authlib;
            if (authAccount)
            {
                if (!AuthValidation.IsValidAuthDisplayName(options.Username))
                {
                    throw new ArgumentException("认证账号的显示名无效。", "options");
                }
                ValidateAuthSetup(options);
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

            Dictionary<string, string> values = BuildPlaceholderValues(
                version,
                options,
                classpath,
                nativesDirectory,
                assetsDirectory,
                gameAssetsDirectory,
                librariesDirectory);
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

            LaunchPlan plan = new LaunchPlan();
            plan.JavaPath = string.IsNullOrEmpty(java.ProbePath) ? java.LaunchPath : java.ProbePath;
            string commandLine = CommandLine.BuildArguments(arguments);
            plan.Arguments = commandLine;
            if (CommandLine.ShouldUseArgFile(commandLine.Length, java.Major))
            {
                string content;
                string argFile = CommandLine.WriteArgFile(arguments, AppPaths.DataDirectory, out content);
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
            plan.WorkingDirectory = fullGameDir;
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

        private static Dictionary<string, string> BuildPlaceholderValues(
            EffectiveVersion version,
            LaunchOptions options,
            string classpath,
            string nativesDirectory,
            string assetsDirectory,
            string gameAssetsDirectory,
            string librariesDirectory)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            string versionType = string.IsNullOrEmpty(version.Type) ? "release" : version.Type;

            string playerName;
            string uuid;
            string accessToken;
            string userType;
            if (options.Account != null && options.Account.Type == AccountType.Authlib)
            {
                // 第三方认证：使用服务器分配的档案（UUID 带连字符）与会话令牌，
                // userType=mojang 与正版行为一致（authlib-injector 负责 hasJoined）。
                playerName = options.Account.DisplayName;
                uuid = options.Account.Uuid;
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
            string launchGameDir = options.IsolateVersions
                ? InstancePaths.GetInstanceDirectory(options.GameDir, options.VersionId)
                : Path.GetFullPath(options.GameDir);
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
            Log.Info("[游戏] " + line);
        }
    }
}
