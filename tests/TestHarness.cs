using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteLauncher.Core;

namespace LiteLauncher.Tests
{
    internal static class TestHarness
    {
        private static int _passed;
        private static int _failed;
        private static string _root;

        private static int Main(string[] args)
        {
            // 1.7.5：临时目录可能不可写（受限 CI / 沙箱把 %TEMP% 设为只读），
            // 此时依次回退到「当前目录」与「exe 所在目录」，保证测试仍能跑起来。
            _root = null;
            string[] candidates = new string[]
            {
                Path.GetTempPath(),
                Environment.CurrentDirectory,
                AppDomain.CurrentDomain.BaseDirectory
            };
            for (int i = 0; i < candidates.Length && _root == null; i++)
            {
                try
                {
                    string probe = Path.Combine(candidates[i], "rll-probe");
                    Directory.CreateDirectory(probe);
                    Directory.Delete(probe, true);
                    _root = Path.Combine(candidates[i],
                        "LiteLauncherTests-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(_root);
                }
                catch
                {
                    _root = null;
                }
            }
            if (_root == null)
            {
                Console.WriteLine("FATAL: no writable temp directory for tests.");
                return 1;
            }
            // 1.7.5：把数据目录指向本次测试的临时目录，避免测试污染用户真实的
            // %APPDATA%\rll（此前 Mod update rollback 用例会往那里写备份，跨运行残留）。
            AppPaths.SetDataDirectoryOverride(_root);
            try
            {
                Run("CommandLine quote", TestCommandLineQuote);
                Run("CommandLine split", TestCommandLineSplit);
                Run("Offline UUID", TestOfflineUuid);
                Run("Username validation", TestUsernameValidation);
                Run("Version id validation", TestVersionId);
                Run("Safe relative path", TestSafeRelativePath);
                Run("SHA1", TestSha1);
                Run("JsonUtil", TestJson);
                Run("Config roundtrip", TestConfig);
                Run("Config field read/write parity", TestConfigRoundtrip);
                Run("Java major parsing", TestJavaMajor);
                Run("Rule evaluation", TestRules);
                Run("Version merge", TestVersionMerge);
                Run("Game launch plan", TestLaunchPlan);
                Run("Legacy launch arguments", TestLegacyLaunchPlan);
                Run("Banned user JVM args", TestBannedJvmArgs);
                Run("JVM argument policy", TestJvmArgumentPolicy);
                Run("URL allowlist", TestUrlAllowlist);
                Run("Native extraction", TestNativeExtraction);
                Run("Native traversal rejection", TestNativeTraversal);
                Run("New natives layout", TestNewNativesLayout);
                Run("New natives extraction", TestNewNativesExtraction);
                Run("Fabric unsupported error mapping", TestFabricUnsupportedError);
                Run("Manifest type names", TestManifestTypes);
                Run("Mod file name safety", TestModFileNameSafety);
                Run("Mod version matching", TestModVersionMatching);
                Run("Mod primary file selection", TestModPrimaryFile);
                Run("Mod best version selection", TestModBestVersion);
                Run("Mod install paths", TestModInstallPaths);
                Run("Auth validation", TestAuthValidation);
                Run("Yggdrasil response parse", TestYggdrasilParse);
                Run("Account ids", TestAccountIds);
                Run("Account config roundtrip", TestConfigAccounts);
                Run("Auth service account mgmt", TestAuthService);
                Run("Auth launch plan", TestAuthLaunchPlan);
                Run("Microsoft launch plan", TestMicrosoftLaunchPlan);
                Run("Microsoft refresh token roundtrip", TestMicrosoftRefreshTokenRoundtrip);
                Run("Shaderpacks use launch version", TestShaderpacksLaunchVersion);
            Run("Yggdrasil json escape", TestYggdrasilJsonEscape);
            Run("Jar archive detection", TestJarArchiveDetection);
            Run("HTTP allowlist concurrency", TestAllowlistConcurrency);
            Run("Crash analyzer", TestCrashAnalyzer);
            Run("EasyTier config", TestEasyTierConfig);
            Run("EasyTier arguments", TestEasyTierArguments);
            Run("EasyTier virtual ip", TestEasyTierVirtualIp);
            Run("EasyTier diagnosis", TestEasyTierDiagnosis);
            Run("Auth register url", TestAuthRegisterUrl);
            Run("Mods dir under isolation", TestModsDirectoryWithIsolation);
            Run("Instance mods seeding", TestInstanceModsSeeding);
            Run("Bidirectional mod sync", TestBidirectionalModSync);
            Run("Duplicate mod detection", TestDuplicateModDetection);
            Run("Mod manager disable enable delete", TestModManager);
            Run("Java installer whitelist and mapping", TestJavaInstaller);
            Run("EasyTier wintun requirement", TestEasyTierWintunRequirement);
            Run("Auth whitelist before login", TestAuthWhitelistBeforeLogin);
            Run("Javaagent argument format", TestJavaAgentFormat);
            Run("Auth url completion", TestAuthUrlCompletion);
            Run("Auth server error extraction", TestAuthServerErrorExtraction);
            Run("Mod installed detection", TestModInstalledDetection);
            Run("Recommended mods include lan mod", TestRecommendedIncludesLanMod);
            Run("Uuid format tolerance", TestUuidFormatTolerance);
            Run("Download mirror mapping", TestDownloadMirror);
            Run("Version isolation", TestVersionIsolation);
            Run("Mod dependency scanner", TestModDependencyScanner);
            Run("Download manager queue dedup", TestDownloadManager);
            Run("Download manager cancel pause", TestDownloadManagerCancelPause);
            Run("Download manager persistence restore", TestDownloadManagerRestore);
            Run("ModIndex scan cache deps", TestModIndex);
            Run("Credential store roundtrip", TestCredentialStore);
            Run("Log sanitize", TestLogSanitize);
            Run("Config credential migration", TestConfigCredentialMigration);
            Run("Mod update rollback", TestModUpdateRollback);
                Run("Argfile encode roundtrip", TestArgFileRoundtrip);
                Run("Argfile decision", TestArgFileDecision);
                Run("HTTP allowlist runtime", TestHttpAllowlistRuntime);
                Run("GitHub release redirect hosts", TestGithubReleaseHosts);
                // 1.4.1 安全修复的回归测试
                Run("Argfile path quoting", TestArgFilePathQuoting);
                Run("Java zip traversal rejection", TestJavaZipTraversal);
                Run("EasyTier secret redaction", TestEasyTierSecretRedaction);
                Run("SHA256 validation", TestSha256Validation);
                // 1.5.0 新功能测试
                Run("Node monitor parse", TestNodeMonitorParse);
                Run("Space manager format", TestSpaceManagerFormat);
                Run("Crash analyzer tail read", TestCrashAnalyzerTailRead);
                Run("Crash findings severity order", TestCrashSeverityOrder);
                Run("Launcher owns classpath", TestLauncherOwnsClasspath);
                Run("Disabled mods are synced", TestDisabledModsSynced);
                Run("Disabled mods counted in duplicates", TestDisabledModsInDuplicates);
                // 1.4.3 新功能的回归测试
                Run("Local mod jar install", TestLocalModInstall);
                Run("Local mod jar validation", TestLocalModValidation);
                Run("Mod preset roundtrip", TestModPresetRoundtrip);
                Run("Mod preset validation", TestModPresetValidation);
                Run("Download rate and eta", TestDownloadRateTracker);
                // 1.4.4 性能修复的回归测试
                Run("Log game channel is non blocking", TestLogGameNonBlocking);
                Run("Log content completeness", TestLogContentCompleteness);
                // 1.4.5「下载 Java 无反应」修复的回归测试
                Run("Java failure diagnosis", TestJavaFailureDiagnosis);
                Run("Java download mapping", TestJavaDownloadMapping);
                // 1.4.6：日志轮转 + 下载停滞检测
                Run("Log rotation", TestLogRotation);
                Run("Log retention cleanup", TestLogRetention);
                Run("Download stall watchdog", TestDownloadStallWatchdog);
                Run("Resume part file helpers", TestResumePartFileHelpers);
                Run("Java progress is cumulative", TestJavaProgressContract);

                // 1.7.0 新功能测试
                Run("Shader pack list enable disable", TestShaderPackManager);
                Run("Shader pack import validation", TestShaderPackImport);
                Run("Shader packs directory isolation", TestShaderpacksDirectory);
                Run("Microsoft device code parse", TestMicrosoftDeviceCode);
                Run("Microsoft token poll states", TestMicrosoftTokenPoll);
                Run("Microsoft protocol bodies", TestMicrosoftProtocolBodies);
                Run("Microsoft xsts error mapping", TestMicrosoftXstsErrors);
                Run("Microsoft config roundtrip", TestMicrosoftConfigRoundtrip);
                Run("Runtime record fields", TestRuntimeRecordFields);
                // 1.7.2 修复的回归测试
                Run("Page index coverage", TestPageIndexCoverage);
                Run("User facing write permission", TestUserFacingWritePermission);
                Run("Task progress formatting", TestTaskProgressFormatting);
                // 1.7.4 修复的回归测试
                Run("Shader version matching", TestShaderVersionMatching);
                Run("Config save failure detection", TestConfigSaveFailure);
                Run("Game directory fallback", TestGameDirectoryFallback);
                // 1.7.6（B-19/B-20）：按版本管理模组 + 下载任务可见性
                Run("Mod target dir honors isolation", TestModTargetDirectory);
                Run("Download job lifecycle", TestDownloadJobLifecycle);
                // 1.7.7（B-21）：隔离时 --gameDir / 工作目录 / mods 目录必须同源
                Run("Isolation launch plan", TestIsolationLaunchPlan);

                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--network", StringComparison.OrdinalIgnoreCase))
                        {
                            Run("Network manifest", TestNetworkManifest);
                            Run("Network fabric profile", TestNetworkFabricProfile);
                            Run("Network fabric unsupported", TestNetworkFabricUnsupported);
                            Run("Network download SHA1", TestNetworkDownload);
                            Run("Network modrinth search", TestNetworkModrinthSearch);
                            Run("Network modrinth version match", TestNetworkModrinthVersion);
                            Run("Network mod install", TestNetworkModInstall);
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(_root, true);
                }
                catch
                {
                }
            }

            Console.WriteLine();
            Console.WriteLine("Passed: " + _passed + ", Failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Check(string name, bool condition)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + name);
            }
            _passed++;
        }

        private static void CheckEqual(string name, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new Exception(name + " expected [" + expected + "] but got [" + actual + "]");
            }
            _passed++;
        }

        private static void CheckThrows<T>(string name, Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                _passed++;
                return;
            }
            catch (Exception ex)
            {
                throw new Exception(name + " threw " + ex.GetType().Name + " instead of " + typeof(T).Name);
            }
            throw new Exception(name + " did not throw " + typeof(T).Name);
        }

        private static void TestCommandLineQuote()
        {
            CheckEqual("simple", "abc", CommandLine.QuoteArgument("abc"));
            CheckEqual("space", "\"a b\"", CommandLine.QuoteArgument("a b"));
            CheckEqual("empty", "\"\"", CommandLine.QuoteArgument(""));
            CheckEqual("quote", "\"a\\\"b\"", CommandLine.QuoteArgument("a\"b"));
            CheckEqual("trailing slash", "\"a b\\\\\"", CommandLine.QuoteArgument("a b\\"));
            CheckEqual("path", "\"C:\\Program Files\\Java\\bin\\javaw.exe\"",
                CommandLine.QuoteArgument("C:\\Program Files\\Java\\bin\\javaw.exe"));
        }

        private static void TestCommandLineSplit()
        {
            string[] samples = new string[]
            {
                "abc",
                "a b",
                "\"a b\" c",
                "-Dfoo=\"bar baz\"",
                "\"C:\\Program Files\\Java\\bin\\javaw.exe\" -version",
                "a\\\\b",
                "\"a\\\\\\\"b\""
            };
            for (int i = 0; i < samples.Length; i++)
            {
                List<string> parts = CommandLine.SplitArguments(samples[i]);
                string rebuilt = CommandLine.BuildArguments(parts);
                List<string> again = CommandLine.SplitArguments(rebuilt);
                Check("roundtrip count " + i, parts.Count == again.Count);
                for (int j = 0; j < parts.Count; j++)
                {
                    CheckEqual("roundtrip " + i + "/" + j, parts[j], again[j]);
                }
            }
            List<string> empty = CommandLine.SplitArguments("  ");
            Check("empty split", empty.Count == 0);
        }

        private static void TestOfflineUuid()
        {
            CheckEqual("Steve", "5627dd98-e6be-3c21-b8a8-e92344183641",
                CommandLine.ComputeOfflineUuid("Steve"));
            CheckEqual("Player", "a01e3843-e521-3998-958a-f459800e4d11",
                CommandLine.ComputeOfflineUuid("Player"));
            CheckEqual("TestUser", "097d3392-865a-3f3c-8b4a-da1c3473466c",
                CommandLine.ComputeOfflineUuid("TestUser"));
            CheckEqual("compact", "5627dd98e6be3c21b8a8e92344183641",
                CommandLine.ComputeOfflineUuidCompact("Steve"));
        }

        private static void TestUsernameValidation()
        {
            Check("valid", CommandLine.IsValidUsername("Steve_123"));
            Check("too short", !CommandLine.IsValidUsername("ab"));
            Check("too long", !CommandLine.IsValidUsername("abcdefghijklmnopq"));
            Check("space", !CommandLine.IsValidUsername("a b"));
            Check("dash", !CommandLine.IsValidUsername("a-b"));
        }

        private static void TestVersionId()
        {
            Check("release", PathUtil.IsValidVersionId("1.20.1"));
            Check("fabric", PathUtil.IsValidVersionId("fabric-loader-0.16.14-1.21.1"));
            Check("plus", PathUtil.IsValidVersionId("1.20.1+build"));
            Check("dotdot", !PathUtil.IsValidVersionId("../evil"));
            Check("space", !PathUtil.IsValidVersionId("a b"));
            Check("slash", !PathUtil.IsValidVersionId("a/b"));
            Check("colon", !PathUtil.IsValidVersionId("C:evil"));
            Check("leading dot", !PathUtil.IsValidVersionId(".hidden"));
            Check("empty", !PathUtil.IsValidVersionId(""));
        }

        private static void TestSafeRelativePath()
        {
            string root = Path.Combine(_root, "safe");
            Directory.CreateDirectory(root);
            string combined = PathUtil.SafeCombine(root, "a/b/c.jar");
            Check("safe combine", combined.EndsWith("c.jar", StringComparison.OrdinalIgnoreCase));
            CheckThrows<InvalidDataException>("parent traversal",
                delegate { PathUtil.SafeCombine(root, "../evil.jar"); });
            CheckThrows<InvalidDataException>("nested traversal",
                delegate { PathUtil.SafeCombine(root, "a/../../evil.jar"); });
            CheckThrows<InvalidDataException>("absolute",
                delegate { PathUtil.SafeCombine(root, "C:\\evil.jar"); });
            CheckThrows<InvalidDataException>("classpath separator",
                delegate { PathUtil.SafeCombine(root, "a;b.jar"); });
            CheckThrows<InvalidDataException>("ads",
                delegate { PathUtil.SafeCombine(root, "a:b.jar"); });
            CheckThrows<InvalidDataException>("dot segment",
                delegate { PathUtil.SafeCombine(root, "a/./b.jar"); });
            CheckThrows<InvalidDataException>("double slash",
                delegate { PathUtil.SafeCombine(root, "a//b.jar"); });
            CheckThrows<InvalidDataException>("trailing dot",
                delegate { PathUtil.SafeCombine(root, "a./b.jar"); });
        }

        private static void TestSha1()
        {
            string file = Path.Combine(_root, "sha1.txt");
            File.WriteAllText(file, "abc", new UTF8Encoding(false));
            CheckEqual("abc sha1", "a9993e364706816aba3e25717850c26c9cd0d89d", PathUtil.GetSha1(file));
        }

        private static void TestJson()
        {
            Dictionary<string, object> root = JsonUtil.ParseObject(
                "{\"a\":1,\"b\":true,\"c\":\"x\",\"d\":[1,2]}");
            CheckEqual("string", "x", JsonUtil.GetString(root, "c"));
            Check("bool", JsonUtil.GetBool(root, "b", false));
            Check("int", JsonUtil.GetInt(root, "a", 0) == 1);
            Check("array", JsonUtil.GetArray(root, "d").Count == 2);
            Check("missing", JsonUtil.GetInt(root, "z", 7) == 7);
        }

        private static void TestConfig()
        {
            string path = Path.Combine(_root, "config.json");
            LauncherConfig config = new LauncherConfig();
            config.GameDir = Path.Combine(_root, "game");
            config.MinMemoryMB = 100;
            config.MaxMemoryMB = 100;
            config.DownloadThreads = 99;
            config.LastUsername = "bad name";
            config.WindowWidth = 100;
            config.WindowHeight = 99999;
            config.JavaPath = "  ";
            // 1.7.5（B-03）：默认目录不再被固化。要让 GameDir 往返成立必须先标记为显式设置，
            // 否则 Normalize() 会按当前 exe 位置重新解析（这正是"启动器搬家即可跟随"的语义）。
            config.GameDirExplicit = true;
            config.SaveTo(path);
            LauncherConfig loaded = LauncherConfig.LoadFrom(path);
            Check("min clamp", loaded.MinMemoryMB == 512);
            Check("max clamp", loaded.MaxMemoryMB == 512);
            Check("threads clamp", loaded.DownloadThreads == 16);
            CheckEqual("username fallback", "Player", loaded.LastUsername);
            Check("width clamp", loaded.WindowWidth == 320);
            Check("height clamp", loaded.WindowHeight == 4320);
            Check("java empty", loaded.JavaPath == "");
            Check("game dir", loaded.GameDir.EndsWith("game", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 1.7.5 回归（B-01/B-02）：配置层「只加写、不加读」的往返测试。
        ///
        /// 此前 ToJsonObject() 写 41 个键、LoadFrom() 只读回 31 个，导致主题色、
        /// 界面缩放、日志策略等 8 项"能存不能读"，重启后一律回到默认值。
        /// 这里做两件事：
        /// 1) 逐字段往返断言（赋非默认值 → 存 → 读 → 断言相等）；
        /// 2) 反射比对「写入键集合 == 读取键集合」的静态清单，新增字段漏读会直接失败。
        /// </summary>
        private static void TestConfigRoundtrip()
        {
            string path = Path.Combine(_root, "cfg-roundtrip.json");
            LauncherConfig config = new LauncherConfig();

            // ---- 逐字段赋非默认值（覆盖此前漏读的 8 项 + 其余标量项）----
            config.LogRetentionDays = 45;              // 默认 14
            config.LogMaxRotatedFiles = 7;             // 默认 2
            config.CleanTempOnExit = false;            // 默认 true
            config.EasyTierMonitorEnabled = false;     // 默认 true
            config.EasyTierAutoReconnect = false;      // 默认 true
            config.AccentColor = "green";              // 默认 ""
            config.UiFontScale = 120;                  // 默认 100
            config.CredentialProtectionVersion = 2;    // 默认 1
            config.DownloadMaxConcurrency = 6;         // 默认 3
            config.DownloadHistoryLimit = 150;         // 默认 100
            config.ModUpdateBackupCount = 5;           // 默认 3
            config.ModIndexCacheEnabled = false;       // 默认 true
            config.EnableDownloadResume = false;       // 默认 true
            config.IsolateVersions = true;
            config.DarkTheme = true;
            config.ShowSnapshots = true;
            config.CustomResolution = true;
            config.WindowWidth = 1280;
            config.WindowHeight = 720;
            config.MinMemoryMB = 2048;
            config.MaxMemoryMB = 8192;
            config.DownloadThreads = 4;
            config.JvmArgs = "-Dfoo=bar";
            config.LastVersion = "1.20.1";
            config.LastUsername = "Tester";
            config.MicrosoftClientId = "client-id-xxx";
            config.DownloadMirror = "official";
            // 1.7.5（B-03）：显式设置目录 → 必须原样往返；
            // 未显式设置 → 重启后按当前 exe 位置重新解析（见下方断言）。
            config.GameDirExplicit = true;
            config.GameDir = Path.Combine(_root, "roundtrip-game");
            config.SaveTo(path);

            LauncherConfig loaded = LauncherConfig.LoadFrom(path);

            CheckEqual("roundtrip LogRetentionDays", "45", loaded.LogRetentionDays.ToString());
            CheckEqual("roundtrip LogMaxRotatedFiles", "7", loaded.LogMaxRotatedFiles.ToString());
            Check("roundtrip CleanTempOnExit", !loaded.CleanTempOnExit);
            Check("roundtrip EasyTierMonitorEnabled", !loaded.EasyTierMonitorEnabled);
            Check("roundtrip EasyTierAutoReconnect", !loaded.EasyTierAutoReconnect);
            CheckEqual("roundtrip AccentColor", "green", loaded.AccentColor);
            CheckEqual("roundtrip UiFontScale", "120", loaded.UiFontScale.ToString());
            CheckEqual("roundtrip CredentialProtectionVersion", "2",
                loaded.CredentialProtectionVersion.ToString());
            CheckEqual("roundtrip DownloadMaxConcurrency", "6",
                loaded.DownloadMaxConcurrency.ToString());
            CheckEqual("roundtrip DownloadHistoryLimit", "150",
                loaded.DownloadHistoryLimit.ToString());
            CheckEqual("roundtrip ModUpdateBackupCount", "5",
                loaded.ModUpdateBackupCount.ToString());
            Check("roundtrip ModIndexCacheEnabled", !loaded.ModIndexCacheEnabled);
            Check("roundtrip EnableDownloadResume", !loaded.EnableDownloadResume);
            Check("roundtrip IsolateVersions", loaded.IsolateVersions);
            Check("roundtrip DarkTheme", loaded.DarkTheme);
            Check("roundtrip ShowSnapshots", loaded.ShowSnapshots);
            Check("roundtrip CustomResolution", loaded.CustomResolution);
            CheckEqual("roundtrip WindowWidth", "1280", loaded.WindowWidth.ToString());
            CheckEqual("roundtrip WindowHeight", "720", loaded.WindowHeight.ToString());
            CheckEqual("roundtrip MinMemoryMB", "2048", loaded.MinMemoryMB.ToString());
            CheckEqual("roundtrip MaxMemoryMB", "8192", loaded.MaxMemoryMB.ToString());
            CheckEqual("roundtrip DownloadThreads", "4", loaded.DownloadThreads.ToString());
            CheckEqual("roundtrip JvmArgs", "-Dfoo=bar", loaded.JvmArgs);
            CheckEqual("roundtrip LastVersion", "1.20.1", loaded.LastVersion);
            CheckEqual("roundtrip LastUsername", "Tester", loaded.LastUsername);
            CheckEqual("roundtrip MicrosoftClientId", "client-id-xxx", loaded.MicrosoftClientId);
            Check("roundtrip GameDirExplicit", loaded.GameDirExplicit);
            Check("roundtrip explicit GameDir kept",
                loaded.GameDir.EndsWith("roundtrip-game", StringComparison.OrdinalIgnoreCase));

            // ---- 1.7.5（B-03）：未显式设置的目录必须跟随当前程序目录，不被旧值固化 ----
            LauncherConfig implicitDir = new LauncherConfig();
            implicitDir.GameDir = Path.Combine(_root, "stale-default");
            implicitDir.GameDirExplicit = false;
            string implicitPath = Path.Combine(_root, "cfg-implicit-dir.json");
            implicitDir.SaveTo(implicitPath);
            LauncherConfig implicitLoaded = LauncherConfig.LoadFrom(implicitPath);
            Check("implicit GameDir not persisted as stale value",
                !implicitLoaded.GameDir.EndsWith("stale-default", StringComparison.OrdinalIgnoreCase));
            Check("implicit GameDir follows default",
                LauncherConfig.PathsEqual(implicitLoaded.GameDir, AppPaths.DefaultGameDirectory));
            Check("default game dir is beside exe when writable",
                AppPaths.IsDirectoryWritable(AppPaths.BaseDirectory)
                    ? LauncherConfig.PathsEqual(
                        AppPaths.DefaultGameDirectory,
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft"))
                    : true);

            // ---- 主题色白名单：非法键名必须归零，避免存下永不生效的值 ----
            LauncherConfig bad = new LauncherConfig();
            bad.AccentColor = "#ff0000";
            bad.SaveTo(Path.Combine(_root, "cfg-bad-accent.json"));
            CheckEqual("invalid accent normalized", "", bad.AccentColor);
            CheckEqual("accent key green kept", "green", LauncherConfig.NormalizeAccentColor(" GReen "));
            CheckEqual("accent blank maps to default", "",
                LauncherConfig.NormalizeAccentColor(null));
            CheckEqual("accent 'blue' maps to empty", "",
                LauncherConfig.NormalizeAccentColor("blue"));

            // ---- 键集合一致性：写入键必须全部有对应读取点 ----
            Dictionary<string, object> written = new LauncherConfig().ToJsonObject();
            // 读取键清单：与 LoadFrom 中 JsonUtil.GetXxx(root, ...) 的调用一一对应。
            // 这里用显式清单而非解析源码，是为了让"漏加读取"在评审与测试中都可见。
            string[] readKeys = new string[]
            {
                "GameDir", "GameDirExplicit", "JavaPath", "MinMemoryMB", "MaxMemoryMB", "JvmArgs",
                "DownloadThreads", "LastVersion", "LastUsername", "ShowSnapshots",
                "CustomResolution", "WindowWidth", "WindowHeight", "DarkTheme",
                "Accounts", "SelectedAccountId", "AuthServers", "AuthlibInjectorPath",
                "EasyTierEnabled", "EasyTierNetworkName", "EasyTierNetworkSecret",
                "EasyTierPublicNode", "EasyTierBinaryPath", "DownloadMirror",
                "IsolateVersions", "DownloadMaxConcurrency", "DownloadHistoryLimit",
                "EnableDownloadResume", "ModIndexCacheEnabled", "ModUpdateBackupCount",
                "MicrosoftClientId", "LastGameExitCode", "LastGameRunSeconds",
                "LastGameExitAtUnix", "LogRetentionDays", "LogMaxRotatedFiles",
                "CleanTempOnExit", "EasyTierMonitorEnabled", "EasyTierAutoReconnect",
                "AccentColor", "UiFontScale", "CredentialProtectionVersion"
            };
            List<string> missingRead = new List<string>();
            foreach (string key in written.Keys)
            {
                if (Array.IndexOf(readKeys, key) < 0)
                {
                    missingRead.Add(key);
                }
            }
            Check("every written key has a reader: " + string.Join(", ", missingRead.ToArray()),
                missingRead.Count == 0);

            // 反向：读取清单里也不该有从未写入的键（防止清单与实际漂移）。
            List<string> extraRead = new List<string>();
            for (int i = 0; i < readKeys.Length; i++)
            {
                if (!written.ContainsKey(readKeys[i]))
                {
                    extraRead.Add(readKeys[i]);
                }
            }
            Check("every reader key is written: " + string.Join(", ", extraRead.ToArray()),
                extraRead.Count == 0);
        }

        private static void TestJavaMajor()
        {
            Check("java8", JavaFinder.ParseMajor("1.8.0_402") == 8);
            Check("java17", JavaFinder.ParseMajor("17.0.1") == 17);
            Check("java21", JavaFinder.ParseMajor("21") == 21);
            Check("java25", JavaFinder.ParseMajor("25-ea") == 25);
            Check("unknown", JavaFinder.ParseMajor("") == 0);
        }

        private static void TestRules()
        {
            List<Rule> windows = new List<Rule>();
            Rule allowWindows = new Rule();
            allowWindows.Action = "allow";
            allowWindows.OsName = "windows";
            windows.Add(allowWindows);
            Check("windows allowed", RuleEvaluator.IsAllowed(windows, null));

            List<Rule> linux = new List<Rule>();
            Rule allowLinux = new Rule();
            allowLinux.Action = "allow";
            allowLinux.OsName = "linux";
            linux.Add(allowLinux);
            Check("linux denied", !RuleEvaluator.IsAllowed(linux, null));

            List<Rule> disallow = new List<Rule>();
            Rule deny = new Rule();
            deny.Action = "disallow";
            deny.OsName = "windows";
            disallow.Add(deny);
            Check("disallow windows", !RuleEvaluator.IsAllowed(disallow, null));
        }

        private static void TestVersionMerge()
        {
            string gameDir = Path.Combine(_root, "merge");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());
            WriteVersion(gameDir, "fabric-loader-0.16.14-1.20.1", ChildVersionJson());

            EffectiveVersion version = VersionResolver.LoadInstalledVersion(
                gameDir, "fabric-loader-0.16.14-1.20.1");
            CheckEqual("main class", "net.fabricmc.loader.impl.launch.knot.KnotClient", version.MainClass);
            CheckEqual("parent jar", "1.20.1", version.ClientJarVersionId);
            CheckEqual("assets", "5", version.AssetsId);
            Check("java 17", version.JavaVersionMajor == 17);
            Check("classpath count", version.ClasspathArtifacts.Count == 2);
            CheckEqual("fabric first", "org/ow2/asm/asm/9.8/asm-9.8.jar",
                version.ClasspathArtifacts[0].Path);
            CheckEqual("parent second", "org/example/parentlib/1.0/parentlib-1.0.jar",
                version.ClasspathArtifacts[1].Path);
            Check("native count", version.NativeArtifacts.Count == 1);
            Check("native classifier", version.NativeArtifacts[0].Classifier == "natives-windows");
            Check("jvm args merged", version.JvmArguments.Count >= 4);
            Check("game args merged", version.GameArguments.Count >= 16);
            Check("client sha1", version.ClientJar != null &&
                version.ClientJar.Sha1.StartsWith("1111", StringComparison.Ordinal));
            Check("logging", version.LoggingFile == null);
            Check("installed list", VersionResolver.GetInstalledVersions(gameDir).Count == 2);

            // 模组页依赖：Fabric 子版本识别，以及映射到真实 Minecraft 版本。
            Check("fabric detected", VersionResolver.IsFabricVersion(version));
            CheckEqual("game version mapped", "1.20.1", version.GameVersionId);
            EffectiveVersion parent = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");
            Check("vanilla not fabric", !VersionResolver.IsFabricVersion(parent));
            CheckEqual("vanilla game version", "1.20.1", parent.GameVersionId);
        }

        private static void TestLaunchPlan()
        {
            string gameDir = Path.Combine(_root, "launch");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());
            WriteVersion(gameDir, "fabric-loader-0.16.14-1.20.1", ChildVersionJson());
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(
                gameDir, "fabric-loader-0.16.14-1.20.1");

            ArgumentEntry resolution = new ArgumentEntry();
            Rule resolutionRule = new Rule();
            resolutionRule.Action = "allow";
            resolutionRule.Features["has_custom_resolution"] = true;
            resolution.Rules.Add(resolutionRule);
            resolution.Values.Add("--width");
            resolution.Values.Add("${resolution_width}");
            resolution.Values.Add("--height");
            resolution.Values.Add("${resolution_height}");
            version.GameArguments.Add(resolution);

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = version.Id;
            options.Username = "Steve";
            options.JavaPath = "C:\\Java\\bin\\javaw.exe";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 4096;
            options.JvmArgs = "-Dfile.encoding=UTF-8";
            options.CustomResolution = true;
            options.WindowWidth = 1280;
            options.WindowHeight = 720;

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.ProbePath = "C:\\Java\\bin\\java.exe";
            java.Major = 17;

            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("java exe", plan.JavaPath.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase));
            Check("memory min", plan.Arguments.Contains("-Xms1024M"));
            Check("memory max", plan.Arguments.Contains("-Xmx4096M"));
            Check("log4j", plan.Arguments.Contains("-Dlog4j2.formatMsgNoLookups=true"));
            Check("fabric emu", plan.Arguments.Contains("-DFabricMcEmu= net.minecraft.client.main.Main"));
            Check("user jvm", plan.Arguments.Contains("-Dfile.encoding=UTF-8"));
            Check("username", plan.Arguments.Contains("--username Steve"));
            Check("uuid", plan.Arguments.Contains("--uuid 5627dd98e6be3c21b8a8e92344183641"));
            Check("token", plan.Arguments.Contains("--accessToken 0"));
            Check("user type", plan.Arguments.Contains("--userType legacy"));
            Check("version type", plan.Arguments.Contains("--versionType release"));
            Check("width", plan.Arguments.Contains("--width 1280"));
            Check("height", plan.Arguments.Contains("--height 720"));
            Check("classpath client", plan.Classpath.Contains("1.20.1.jar"));
            Check("classpath fabric", plan.Classpath.Contains("asm-9.8.jar"));
            Check("classpath parent", plan.Classpath.Contains("parentlib-1.0.jar"));
            Check("no linux library", !plan.Classpath.Contains("linuxonly"));
            Check("working dir", string.Equals(plan.WorkingDirectory, gameDir, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 1.7.7（B-21）回归：版本隔离必须在启动参数层真正生效。
        ///
        /// 覆盖三个此前没人测到的缺口：
        /// ① plan.WorkingDirectory 写死共享目录；
        /// ② 版本 JSON 没有 --gameDir 时游戏按进程工作目录读共享 mods；
        /// ③ 版本 JSON 里的 --gameDir 指向旧目录时没有被校正。
        /// 三者任一存在，都会表现为「勾了隔离，各版本还是读同一批 mod」。
        /// </summary>
        private static void TestIsolationLaunchPlan()
        {
            string gameDir = Path.Combine(_root, "isolation-plan");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "javaw.exe";
            java.Major = 17;

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.20.1";
            options.Username = "Steve";
            options.IsolateVersions = true;

            string instance = InstancePaths.GetInstanceDirectory(gameDir, "1.20.1");
            string quotedInstance = CommandLine.QuoteArgument(instance);
            string quotedGameDir = CommandLine.QuoteArgument(gameDir);

            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("isolated working dir is instance",
                string.Equals(plan.WorkingDirectory, instance, StringComparison.OrdinalIgnoreCase));
            Check("isolated --gameDir present", plan.Arguments.Contains("--gameDir"));
            Check("isolated --gameDir points at instance",
                plan.Arguments.Contains(instance) || plan.Arguments.Contains(quotedInstance));
            Check("isolated mods dir under instance",
                string.Equals(
                    InstancePaths.GetModsDirectory(gameDir, "1.20.1", true),
                    Path.Combine(instance, "mods"),
                    StringComparison.OrdinalIgnoreCase));
            Check("isolated libraries still shared", plan.Classpath.Contains("1.20.1.jar"));

            // 关闭隔离：工作目录回到共享游戏目录，不残留实例路径。
            options.IsolateVersions = false;
            LaunchPlan shared = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("shared working dir",
                string.Equals(shared.WorkingDirectory, gameDir, StringComparison.OrdinalIgnoreCase));
            Check("shared --gameDir points at game dir",
                shared.Arguments.Contains(gameDir) || shared.Arguments.Contains(quotedGameDir));
            Check("shared plan has no instance dir",
                !shared.Arguments.Contains(instance));

            // 手工构造一个不带 --gameDir 的旧式版本（模拟裁剪过的自定义版本）。
            EffectiveVersion stripped = new EffectiveVersion();
            stripped.Id = "1.20.1";
            stripped.Type = "release";
            stripped.MainClass = "net.minecraft.client.main.Main";
            stripped.AssetsId = "1.20.1";
            stripped.ClientJarVersionId = "1.20.1";
            stripped.JavaVersionMajor = 17;
            stripped.LegacyMinecraftArguments =
                "--username ${auth_player_name} --version ${version_name}";
            options.IsolateVersions = true;

            LaunchPlan forced = GameLauncher.BuildPlan(stripped, options, java, gameDir);
            Check("missing --gameDir is injected",
                forced.Arguments.Contains("--gameDir") &&
                (forced.Arguments.Contains(instance) || forced.Arguments.Contains(quotedInstance)));
            Check("injected plan working dir is instance",
                string.Equals(forced.WorkingDirectory, instance, StringComparison.OrdinalIgnoreCase));

            // 上游写了一个陈旧 / 错误的 --gameDir：必须被强制校正。
            stripped.LegacyMinecraftArguments =
                "--username ${auth_player_name} --gameDir C:\\StaleGameDir";
            LaunchPlan corrected = GameLauncher.BuildPlan(stripped, options, java, gameDir);
            Check("stale --gameDir removed",
                !corrected.Arguments.Contains("C:\\StaleGameDir"));
            Check("stale --gameDir corrected to instance",
                corrected.Arguments.Contains(instance) ||
                corrected.Arguments.Contains(quotedInstance));

            // 用户自定义参数不能通过改写工作目录绕过隔离。
            Check("user.dir override banned", JvmArgumentPolicy.IsBanned("-Duser.dir=C:\\Shared"));
        }

        private static void TestLegacyLaunchPlan()
        {
            string gameDir = Path.Combine(_root, "legacy");
            Directory.CreateDirectory(gameDir);
            EffectiveVersion version = new EffectiveVersion();
            version.Id = "1.7.10";
            version.Type = "release";
            version.MainClass = "net.minecraft.client.main.Main";
            version.AssetsId = "1.7.10";
            version.ClientJarVersionId = "1.7.10";
            version.JavaVersionMajor = 8;
            version.LegacyMinecraftArguments =
                "--username ${auth_player_name} --gameDir ${game_directory} --uuid ${auth_uuid}";

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.7.10";
            options.Username = "Player";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 2048;
            options.CustomResolution = true;
            options.WindowWidth = 1024;
            options.WindowHeight = 768;

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "java.exe";
            java.Major = 8;

            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("legacy username", plan.Arguments.Contains("--username Player"));
            Check("legacy uuid", plan.Arguments.Contains("--uuid a01e3843e5213998958af459800e4d11"));
            Check("legacy width", plan.Arguments.Contains("--width 1024"));
            Check("legacy height", plan.Arguments.Contains("--height 768"));
            Check("legacy library path", plan.Arguments.Contains("-Djava.library.path="));
        }

        private static void TestBannedJvmArgs()
        {
            string gameDir = Path.Combine(_root, "banned");
            Directory.CreateDirectory(gameDir);
            EffectiveVersion version = new EffectiveVersion();
            version.Id = "1.20.1";
            version.MainClass = "net.minecraft.client.main.Main";
            version.ClientJarVersionId = "1.20.1";
            version.LegacyMinecraftArguments = "--username ${auth_player_name}";

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.20.1";
            options.Username = "Player";
            options.JvmArgs = "-javaagent:C:\\evil.jar";

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "java.exe";
            java.Major = 17;

            CheckThrows<InvalidOperationException>("javaagent blocked",
                delegate { GameLauncher.BuildPlan(version, options, java, gameDir); });

            options.JvmArgs = "-XX:OnError=calc.exe";
            CheckThrows<InvalidOperationException>("OnError blocked",
                delegate { GameLauncher.BuildPlan(version, options, java, gameDir); });
        }

        private static void TestJvmArgumentPolicy()
        {
            // 报告 S-1：旧实现用子串匹配，属性值里含 -cp / -jar 的合法参数会被误拒。
            string[] allowed = new string[]
            {
                "-Xmx4G",
                "-Dfile.encoding=UTF-8",
                "-XX:+UseG1GC",
                "-Dfoo=-cp",
                "-Dmaven.repo.local=C:\\my-cp-repo",
                "-Duser.home=C:\\jar",
                "-XX:MaxGCPauseMillis=50"
            };
            for (int i = 0; i < allowed.Length; i++)
            {
                Check("not banned: " + allowed[i], !JvmArgumentPolicy.IsBanned(allowed[i]));
            }

            // 旧实现漏掉的、能劫持类加载或执行命令的等价写法，新实现必须拦截。
            string[] blocked = new string[]
            {
                "-javaagent:C:\\evil.jar",
                "-agentlib:jdwp=transport=dt_socket",
                "-agentpath:C:\\evil.dll",
                "-Xbootclasspath/a:C:\\evil.jar",
                "-Xbootclasspath/p:C:\\evil.jar",
                "-Djava.system.class.loader=EvilLoader",
                "-Dsun.boot.class.path=C:\\evil.jar",
                "-XX:OnError=calc.exe",
                "-XX:OnOutOfMemoryError=calc.exe",
                "--add-opens=java.base/java.lang=ALL-UNNAMED",
                "--patch-module=java.base=C:\\evil",
                "--module-path=C:\\evil",
                "-cp",
                "-classpath",
                "-jar"
            };
            for (int i = 0; i < blocked.Length; i++)
            {
                Check("banned: " + blocked[i], JvmArgumentPolicy.IsBanned(blocked[i]));
            }

            // 关键级判断：-cp 属于关键级，但 --add-opens 属于扩展级，
            // 因此版本 JSON 只做关键级校验时不会误拦模块参数。
            Check("critical: -javaagent", JvmArgumentPolicy.IsCritical("-javaagent:C:\\evil.jar"));
            Check("critical: -XX:OnError", JvmArgumentPolicy.IsCritical("-XX:OnError=calc.exe"));
            Check("not critical: --add-opens",
                !JvmArgumentPolicy.IsCritical("--add-opens=java.base/java.lang=ALL-UNNAMED"));
            Check("not banned: empty", !JvmArgumentPolicy.IsBanned(""));
            Check("not banned: null", !JvmArgumentPolicy.IsBanned(null));

            // 参数名截断：用于精确匹配的核心逻辑（先按空格、再按 '=' 截断）。
            CheckEqual("name of -XX:OnError=calc.exe", "-xx:onerror",
                JvmArgumentPolicy.GetArgumentName("-XX:OnError=calc.exe"));
            CheckEqual("name of -Dfoo=-cp", "-dfoo",
                JvmArgumentPolicy.GetArgumentName("-Dfoo=-cp"));
            CheckEqual("name of --add-opens=x", "--add-opens",
                JvmArgumentPolicy.GetArgumentName("--add-opens=x"));
            // 空格拼接不能绕过：只取第一个片段。
            CheckEqual("name of -Xmx4G -javaagent:x", "-xmx4g",
                JvmArgumentPolicy.GetArgumentName("-Xmx4G -javaagent:x"));
        }

        private static void TestUrlAllowlist()
        {
            Check("mojang meta", HttpUtil.IsAllowedUrl(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
            Check("libraries", HttpUtil.IsAllowedUrl(
                "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar"));
            Check("fabric", HttpUtil.IsAllowedUrl(
                "https://meta.fabricmc.net/v2/versions/loader/1.20.1"));
            Check("http denied", !HttpUtil.IsAllowedUrl(
                "http://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
            Check("evil denied", !HttpUtil.IsAllowedUrl(
                "https://evil.example.com/file.jar"));
            Check("suffix denied", !HttpUtil.IsAllowedUrl(
                "https://piston-meta.mojang.com.evil.example.com/file.json"));
            Check("userinfo denied", !HttpUtil.IsAllowedUrl(
                "https://user@pastebin.com/file"));
            Check("port denied", !HttpUtil.IsAllowedUrl(
                "https://piston-meta.mojang.com:444/file.json"));
            Check("trailing dot denied", !HttpUtil.IsAllowedUrl(
                "https://piston-meta.mojang.com./file.json"));
            Check("ip denied", !HttpUtil.IsAllowedUrl(
                "https://127.0.0.1/file.json"));
        }

        private static void TestNativeExtraction()
        {
            string libraries = Path.Combine(_root, "natives-libs");
            string natives = Path.Combine(_root, "natives-out");
            string jar = Path.Combine(libraries, "test", "native.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jar));
            using (FileStream stream = new FileStream(jar, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "windows/x64/org/lwjgl/a.dll", "dll");
                WriteZipEntry(archive, "windows/x64/org/lwjgl/a.dll.sha1", "sha1");
                WriteZipEntry(archive, "META-INF/MANIFEST.MF", "manifest");
            }

            List<NativeArtifact> artifacts = new List<NativeArtifact>();
            NativeArtifact artifact = new NativeArtifact();
            artifact.Name = "test:native:1.0";
            artifact.Download = new DownloadInfo("test/native.jar", "https://libraries.minecraft.net/test/native.jar",
                "0000000000000000000000000000000000000000", 1);
            artifacts.Add(artifact);
            NativeExtractor.Extract(libraries, natives, artifacts, null, CancellationToken.None);
            Check("dll extracted", File.Exists(Path.Combine(natives, "a.dll")));
            Check("dll flattened", !Directory.Exists(Path.Combine(natives, "windows")));
            Check("aux skipped", !File.Exists(Path.Combine(natives, "a.dll.sha1")));
            Check("meta skipped", !Directory.Exists(Path.Combine(natives, "META-INF")));
        }

        private static void TestNativeTraversal()
        {
            string libraries = Path.Combine(_root, "natives-bad-libs");
            string natives = Path.Combine(_root, "natives-bad-out");
            string jar = Path.Combine(libraries, "test", "bad.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jar));
            using (FileStream stream = new FileStream(jar, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "../evil.dll", "evil");
            }

            List<NativeArtifact> artifacts = new List<NativeArtifact>();
            NativeArtifact artifact = new NativeArtifact();
            artifact.Name = "test:bad:1.0";
            artifact.Download = new DownloadInfo("test/bad.jar", "https://libraries.minecraft.net/test/bad.jar",
                "0000000000000000000000000000000000000000", 1);
            artifacts.Add(artifact);
            CheckThrows<InvalidDataException>("zip traversal",
                delegate { NativeExtractor.Extract(libraries, natives, artifacts, null, CancellationToken.None); });
            Check("evil not written", !File.Exists(Path.Combine(_root, "evil.dll")));
        }

        private static void TestNewNativesLayout()
        {
            string gameDir = Path.Combine(_root, "new-natives");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "26.3", NewNativesVersionJson());
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, "26.3");
            CheckEqual("new natives relative", "java", version.NativesRelativePath);
            Check("new natives work dirs", version.NativesWorkDirectories.Count == 3);
            Check("new natives jna dir", version.NativesWorkDirectories.Contains("jna"));
            Check("new natives lwjgl dir", version.NativesWorkDirectories.Contains("lwjgl"));
            Check("new natives netty dir", version.NativesWorkDirectories.Contains("netty"));
            Check("new natives not extracted", version.NativeArtifacts.Count == 0);
            Check("new natives classpath x64",
                ContainsDownload(version.ClasspathArtifacts, "lwjgl-3.4.3-natives-windows.jar"));
            Check("new natives arm64 filtered",
                !ContainsDownload(version.ClasspathArtifacts, "lwjgl-3.4.3-natives-windows-arm64.jar"));
            Check("new natives jtracy classpath",
                ContainsDownload(version.ClasspathArtifacts, "jtracy-1.14.38-natives-windows.jar"));

            string nativesRoot = VersionResolver.GetNativesDirectory(gameDir, "26.3");
            string target = VersionResolver.GetNativesTargetDirectory(gameDir, version);
            CheckEqual("new natives target", Path.Combine(nativesRoot, "java"), target);
            VersionResolver.EnsureNativesLayout(nativesRoot, version);
            Check("java dir created", Directory.Exists(target));
            Check("jna dir created", Directory.Exists(Path.Combine(nativesRoot, "jna")));
            Check("lwjgl dir created", Directory.Exists(Path.Combine(nativesRoot, "lwjgl")));
            Check("netty dir created", Directory.Exists(Path.Combine(nativesRoot, "netty")));

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = version.Id;
            options.Username = "Steve";
            options.JavaPath = "C:\\Java\\bin\\javaw.exe";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 4096;
            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.ProbePath = "C:\\Java\\bin\\java.exe";
            java.Major = 25;
            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("new natives arg",
                plan.Arguments.IndexOf(
                    "-Djava.library.path=" + nativesRoot + "/java",
                    StringComparison.OrdinalIgnoreCase) >= 0);
            Check("new natives classpath in plan",
                plan.Classpath.IndexOf(
                    "lwjgl-3.4.3-natives-windows.jar",
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TestNewNativesExtraction()
        {
            string libraries = Path.Combine(_root, "new-natives-libs");
            string natives = Path.Combine(_root, "new-natives-out");
            string jar = Path.Combine(libraries, "test", "new-native.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jar));
            using (FileStream stream = new FileStream(jar, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "windows/x64/org/lwjgl/a.dll", "dll");
            }

            List<NativeArtifact> artifacts = new List<NativeArtifact>();
            NativeArtifact artifact = new NativeArtifact();
            artifact.Name = "test:new-native:1.0:natives-windows";
            artifact.Download = new DownloadInfo(
                "test/new-native.jar",
                "https://libraries.minecraft.net/test/new-native.jar",
                "0000000000000000000000000000000000000000",
                1);
            artifacts.Add(artifact);
            NativeExtractor.Extract(
                libraries,
                natives,
                "java",
                artifacts,
                null,
                CancellationToken.None);
            Check("subdir dll extracted", File.Exists(Path.Combine(natives, "java", "a.dll")));
            Check("subdir dll not flattened", !File.Exists(Path.Combine(natives, "a.dll")));
        }

        private static bool ContainsDownload(List<DownloadInfo> items, string fileName)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null &&
                    items[i].Path != null &&
                    items[i].Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void TestFabricUnsupportedError()
        {
            MethodInfo method = typeof(FabricInstaller).GetMethod(
                "IsUnsupportedVersionError",
                BindingFlags.NonPublic | BindingFlags.Static);
            Check("fabric error helper exists", method != null);
            bool unsupported = (bool)method.Invoke(
                null,
                new object[]
                {
                    new IOException(
                        "下载元数据失败：HTTP 400 (Bad Request)：https://meta.fabricmc.net/v2/versions/loader/1.12.2")
                });
            Check("fabric 400 unsupported", unsupported);
            bool serverError = (bool)method.Invoke(
                null,
                new object[] { new IOException("下载元数据失败：HTTP 500 (Internal Server Error)") });
            Check("fabric 500 not unsupported", !serverError);
        }

        private static void TestManifestTypes()
        {
            CheckEqual("release", "正式版", VersionManifestService.GetTypeName("release"));
            CheckEqual("snapshot", "快照", VersionManifestService.GetTypeName("snapshot"));
            CheckEqual("old beta", "旧测试版", VersionManifestService.GetTypeName("old_beta"));
            CheckEqual("unknown", "weird", VersionManifestService.GetTypeName("weird"));
        }

        private static void TestNetworkManifest()
        {
            VersionManifest manifest = new VersionManifestService()
                .GetManifestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check("manifest count", manifest.Versions.Count > 10);
            Check("latest release", !string.IsNullOrEmpty(manifest.LatestRelease));
            VersionSummary release = manifest.Find(manifest.LatestRelease);
            Check("latest release found", release != null);
            Check("latest release url", HttpUtil.IsAllowedUrl(release.Url));
        }

        private static void TestNetworkFabricProfile()
        {
            string loaderJson = HttpUtil.GetStringAsync(
                "https://meta.fabricmc.net/v2/versions/loader/1.20.1",
                CancellationToken.None).GetAwaiter().GetResult();
            System.Collections.IList list = JsonUtil.AsList(JsonUtil.Parse(loaderJson));
            Check("fabric list", list != null && list.Count > 0);
            string loaderVersion = "";
            for (int i = 0; i < list.Count; i++)
            {
                IDictionary<string, object> item = JsonUtil.AsObject(list[i]);
                IDictionary<string, object> loader = JsonUtil.GetObject(item, "loader");
                if (loader != null && JsonUtil.GetBool(loader, "stable", false))
                {
                    loaderVersion = JsonUtil.GetString(loader, "version");
                    break;
                }
            }
            Check("stable loader", !string.IsNullOrEmpty(loaderVersion));
            string profileJson = HttpUtil.GetStringAsync(
                "https://meta.fabricmc.net/v2/versions/loader/1.20.1/" +
                Uri.EscapeDataString(loaderVersion) + "/profile/json",
                CancellationToken.None).GetAwaiter().GetResult();
            VersionJson profile = VersionResolver.ParseVersionJson(profileJson, "network");
            Check("fabric id", profile.Id.StartsWith("fabric-loader-", StringComparison.Ordinal));
            Check("fabric inherits", profile.InheritsFrom == "1.20.1");
            Check("fabric main", profile.MainClass.StartsWith("net.fabricmc.", StringComparison.Ordinal));
            Check("fabric libs", profile.Libraries.Count > 0);
        }

        private static void TestNetworkFabricUnsupported()
        {
            string gameDir = Path.Combine(_root, "fabric-unsupported");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.12.2", "{\"id\":\"1.12.2\"}");
            VersionManifestService manifest = new VersionManifestService();
            MinecraftInstaller installer = new MinecraftInstaller(gameDir, 1, manifest);
            FabricInstaller fabric = new FabricInstaller(installer, manifest);
            bool friendly = false;
            try
            {
                fabric.InstallAsync("1.12.2", null, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                friendly = ex.Message.IndexOf("不受 Fabric 支持", StringComparison.Ordinal) >= 0;
            }
            Check("fabric 1.12.2 friendly", friendly);
        }

        private static void TestNetworkDownload()
        {
            VersionManifest manifest = new VersionManifestService()
                .GetManifestAsync(CancellationToken.None).GetAwaiter().GetResult();
            VersionSummary summary = manifest.Find("1.20.1");
            Check("1.20.1 in manifest", summary != null);
            string json = HttpUtil.GetStringAsync(summary.Url, CancellationToken.None)
                .GetAwaiter().GetResult();
            VersionJson version = VersionResolver.ParseVersionJson(json, summary.Url);
            DownloadInfo chosen = null;
            for (int i = 0; i < version.Libraries.Count; i++)
            {
                LibraryInfo library = version.Libraries[i];
                if (library.Artifact != null &&
                    PathUtil.IsSha1(library.Artifact.Sha1) &&
                    library.Artifact.Size > 0 &&
                    library.Artifact.Size < 4 * 1024 * 1024 &&
                    HttpUtil.IsAllowedUrl(library.Artifact.Url))
                {
                    chosen = library.Artifact;
                    break;
                }
            }
            Check("small library found", chosen != null);
            string target = Path.Combine(_root, "download-test.jar");
            HttpUtil.DownloadFileAsync(
                chosen.Url,
                target,
                chosen.Sha1,
                chosen.Size,
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            Check("download exists", File.Exists(target));
            CheckEqual("download sha1", chosen.Sha1.ToLowerInvariant(), PathUtil.GetSha1(target));
        }

        private static void TestModFileNameSafety()
        {
            Check("normal jar", ModrinthService.IsSafeModFileName("sodium-fabric-0.5.13+mc1.20.1.jar"));
            Check("nested path rejected", !ModrinthService.IsSafeModFileName("sub/dir/mod.jar"));
            Check("backslash path rejected", !ModrinthService.IsSafeModFileName("sub\\mod.jar"));
            Check("parent path rejected", !ModrinthService.IsSafeModFileName("../mod.jar"));
            Check("non jar rejected", !ModrinthService.IsSafeModFileName("mod.zip"));
            Check("hidden rejected", !ModrinthService.IsSafeModFileName(".mod.jar"));
            Check("empty rejected", !ModrinthService.IsSafeModFileName(""));
            Check("null rejected", !ModrinthService.IsSafeModFileName(null));
            Check("trailing dot rejected", !ModrinthService.IsSafeModFileName("mod.jar."));
            Check("too long rejected", !ModrinthService.IsSafeModFileName(new string('a', 300) + ".jar"));
            Check("uppercase jar ok", ModrinthService.IsSafeModFileName("Mod.JAR"));
        }

        private static ModVersion NewModVersion(string id, string projectId, string versionType)
        {
            ModVersion version = new ModVersion();
            version.Id = id;
            version.ProjectId = projectId;
            version.VersionNumber = id;
            version.VersionType = versionType;
            version.Loaders.Add("fabric");
            version.GameVersions.Add("1.20.1");
            ModVersionFile file = new ModVersionFile();
            file.FileName = projectId + "-" + id + ".jar";
            file.Url = "https://cdn.modrinth.com/data/" + projectId + "/versions/" + id + "/" + file.FileName;
            file.Sha1 = "1111111111111111111111111111111111111111";
            file.Size = 100;
            file.Primary = true;
            version.Files.Add(file);
            return version;
        }

        private static void TestModVersionMatching()
        {
            ModVersion version = NewModVersion("v1", "sodium", "release");
            Check("fabric match", ModrinthService.Matches(version, "1.20.1"));
            Check("other game version", !ModrinthService.Matches(version, "1.19.2"));
            Check("empty game version", !ModrinthService.Matches(version, ""));
            Check("null version", !ModrinthService.Matches(null, "1.20.1"));

            ModVersion forgeOnly = NewModVersion("v2", "sodium", "release");
            forgeOnly.Loaders.Clear();
            forgeOnly.Loaders.Add("forge");
            Check("forge only rejected", !ModrinthService.Matches(forgeOnly, "1.20.1"));
        }

        private static void TestModPrimaryFile()
        {
            ModVersion version = NewModVersion("v1", "sodium", "release");
            ModVersionFile file = ModrinthService.SelectPrimaryFile(version);
            Check("primary file", file != null && file.FileName == "sodium-v1.jar");

            ModVersion multi = NewModVersion("v2", "jade", "release");
            ModVersionFile sources = new ModVersionFile();
            sources.FileName = "jade-sources.jar";
            sources.Url = "https://cdn.modrinth.com/data/jade/versions/v2/jade-sources.jar";
            sources.Sha1 = "2222222222222222222222222222222222222222";
            sources.Size = 50;
            multi.Files.Insert(0, sources);
            ModVersionFile chosen = ModrinthService.SelectPrimaryFile(multi);
            Check("prefers primary flag", chosen != null && chosen.FileName == "jade-v2.jar");

            ModVersion unsafeOnly = NewModVersion("v3", "appleskin", "release");
            unsafeOnly.Files[0].FileName = "../evil.jar";
            unsafeOnly.Files[0].Primary = true;
            Check("unsafe file skipped", ModrinthService.SelectPrimaryFile(unsafeOnly) == null);
        }

        private static void TestModBestVersion()
        {
            List<ModVersion> versions = new List<ModVersion>();
            versions.Add(NewModVersion("beta1", "sodium", "beta"));
            versions.Add(NewModVersion("release1", "sodium", "release"));
            ModVersion best = ModrinthService.SelectBestVersion(versions, "1.20.1");
            Check("release preferred", best != null && best.Id == "release1");

            List<ModVersion> betaOnly = new List<ModVersion>();
            betaOnly.Add(NewModVersion("beta1", "sodium", "beta"));
            ModVersion fallback = ModrinthService.SelectBestVersion(betaOnly, "1.20.1");
            Check("beta fallback", fallback != null && fallback.Id == "beta1");

            List<ModVersion> mismatched = new List<ModVersion>();
            ModVersion other = NewModVersion("old", "sodium", "release");
            other.GameVersions.Clear();
            other.GameVersions.Add("1.19.2");
            mismatched.Add(other);
            Check("no match", ModrinthService.SelectBestVersion(mismatched, "1.20.1") == null);
            Check("null list", ModrinthService.SelectBestVersion(null, "1.20.1") == null);
        }

        private static void TestNetworkModrinthSearch()
        {
            ModrinthService service = new ModrinthService();
            List<ModProject> projects = service.SearchAsync(
                "sodium", "1.20.1", CancellationToken.None).GetAwaiter().GetResult();
            Check("search results", projects.Count > 0);
            bool foundSodium = false;
            for (int i = 0; i < projects.Count; i++)
            {
                if (string.Equals(projects[i].Slug, "sodium", StringComparison.OrdinalIgnoreCase))
                {
                    foundSodium = true;
                    Check("sodium has id", !string.IsNullOrEmpty(projects[i].Id));
                    break;
                }
            }
            Check("sodium found", foundSodium);
        }

        private static void TestNetworkModrinthVersion()
        {
            ModrinthService service = new ModrinthService();
            ModVersionFile file;
            string versionNumber;
            ModVersion best = service.GetBestVersionAsync(
                "sodium", "1.20.1", CancellationToken.None).GetAwaiter().GetResult();
            Check("sodium 1.20.1 version", best != null);
            versionNumber = best.VersionNumber;
            Check("release type", string.Equals(best.VersionType, "release", StringComparison.OrdinalIgnoreCase));
            Check("has game version", best.GameVersions.Contains("1.20.1"));
            file = ModrinthService.SelectPrimaryFile(best);
            Check("primary file", file != null);
            Check("file url allowlisted", HttpUtil.IsAllowedUrl(file.Url));
            Check("file sha1", PathUtil.IsSha1(file.Sha1));
            Check("file name safe", ModrinthService.IsSafeModFileName(file.FileName));
            Console.WriteLine("  sodium " + versionNumber + " -> " + file.FileName);

            ModVersion missing = service.GetBestVersionAsync(
                "sodium", "1.7.10", CancellationToken.None).GetAwaiter().GetResult();
            Check("unsupported version returns null", missing == null);
        }

        private static void TestNetworkModInstall()
        {
            string gameDir = Path.Combine(_root, "modinstall");
            Directory.CreateDirectory(gameDir);
            ModrinthService service = new ModrinthService();
            ModInstallResult result = service.InstallAsync(
                gameDir, "1.20.1", "appleskin", null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Check("install produced files", result.Files.Count > 0);
            for (int i = 0; i < result.Files.Count; i++)
            {
                string path = Path.Combine(gameDir, "mods", result.Files[i]);
                Check("installed file exists: " + result.Files[i], File.Exists(path));
                Check("installed inside mods dir: " + result.Files[i],
                    PathUtil.IsSubPath(Path.Combine(gameDir, "mods"), path));
            }

            // 重复安装应识别为已存在且校验通过，而不是新建。
            ModInstallResult second = service.InstallAsync(
                gameDir, "1.20.1", "appleskin", null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Check("second install already present", second.AlreadyPresentFiles.Count == second.Files.Count);
        }

        private static void TestModInstallPaths()
        {
            // 模组安装目录必须落在游戏目录内，且文件名必须拒绝任何穿越写法。
            string gameDir = Path.Combine(_root, "modpaths");
            Directory.CreateDirectory(gameDir);
            string modsDir = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(modsDir);

            Check("mods inside game dir", PathUtil.IsSubPath(gameDir, modsDir));

            string target = PathUtil.SafeCombine(modsDir, "sodium-fabric-0.5.13+mc1.20.1.jar");
            Check("mod target inside mods", PathUtil.IsSubPath(modsDir, target));

            CheckThrows<InvalidDataException>("safe combine rejects traversal", delegate
            {
                PathUtil.SafeCombine(modsDir, "../evil.jar");
            });
            CheckThrows<InvalidDataException>("safe combine rejects absolute", delegate
            {
                PathUtil.SafeCombine(modsDir, "C:\\Windows\\evil.jar");
            });

            string[] badNames = new string[]
            {
                "../escape.jar", "sub/dir.jar", "sub\\dir.jar", ".hidden.jar",
                "notajar.zip", "trailing.jar.", ""
            };
            for (int i = 0; i < badNames.Length; i++)
            {
                Check("reject unsafe mod name: [" + badNames[i] + "]",
                    !ModrinthService.IsSafeModFileName(badNames[i]));
            }
        }

        private static void WriteVersion(string gameDir, string id, string json)
        {
            string directory = Path.Combine(gameDir, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"), json, new UTF8Encoding(false));
        }

        private static void WriteZipEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using (Stream stream = entry.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static string NewNativesVersionJson()
        {
            return "{" +
                "\"id\":\"26.3\"," +
                "\"type\":\"release\"," +
                "\"mainClass\":\"net.minecraft.client.main.Main\"," +
                "\"assets\":\"34\"," +
                "\"javaVersion\":{\"majorVersion\":25}," +
                "\"downloads\":{\"client\":{\"sha1\":\"1111111111111111111111111111111111111111\",\"size\":100,\"url\":\"https://piston-data.mojang.com/v1/objects/1111111111111111111111111111111111111111/client.jar\"}}," +
                "\"assetIndex\":{\"id\":\"34\",\"sha1\":\"2222222222222222222222222222222222222222\",\"size\":200,\"url\":\"https://piston-meta.mojang.com/v1/packages/2222222222222222222222222222222222222222/34.json\"}," +
                "\"libraries\":[" +
                  "{\"name\":\"org.example:normal:1.0\",\"downloads\":{\"artifact\":{\"path\":\"org/example/normal/1.0/normal-1.0.jar\",\"sha1\":\"3333333333333333333333333333333333333333\",\"size\":10,\"url\":\"https://libraries.minecraft.net/org/example/normal/1.0/normal-1.0.jar\"}}}," +
                  "{\"name\":\"org.lwjgl:lwjgl:3.4.3:natives-windows\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}}],\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows.jar\",\"sha1\":\"4444444444444444444444444444444444444444\",\"size\":11,\"url\":\"https://libraries.minecraft.net/org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows.jar\"}}}," +
                  "{\"name\":\"org.lwjgl:lwjgl:3.4.3:natives-windows-arm64\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}}],\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows-arm64.jar\",\"sha1\":\"5555555555555555555555555555555555555555\",\"size\":12,\"url\":\"https://libraries.minecraft.net/org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows-arm64.jar\"}}}," +
                  "{\"name\":\"com.mojang:jtracy:1.14.38:natives-windows\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}}],\"downloads\":{\"artifact\":{\"path\":\"com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar\",\"sha1\":\"6666666666666666666666666666666666666666\",\"size\":13,\"url\":\"https://libraries.minecraft.net/com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar\"}}}" +
                "]," +
                "\"arguments\":{" +
                  "\"game\":[\"--username\",\"${auth_player_name}\",\"--version\",\"${version_name}\",\"--gameDir\",\"${game_directory}\",\"--assetsDir\",\"${assets_root}\",\"--assetIndex\",\"${assets_index_name}\",\"--uuid\",\"${auth_uuid}\",\"--accessToken\",\"${auth_access_token}\",\"--userType\",\"${user_type}\",\"--versionType\",\"${version_type}\"]," +
                  "\"jvm\":[\"-Djava.library.path=${natives_directory}/java\",\"-Djna.tmpdir=${natives_directory}/jna\",\"-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}/lwjgl\",\"-Dio.netty.native.workdir=${natives_directory}/netty\",\"-cp\",\"${classpath}\"]" +
                "}}";
        }

        private static string ParentVersionJson()
        {
            return "{" +
                "\"id\":\"1.20.1\"," +
                "\"type\":\"release\"," +
                "\"mainClass\":\"net.minecraft.client.main.Main\"," +
                "\"assets\":\"5\"," +
                "\"javaVersion\":{\"majorVersion\":17}," +
                "\"downloads\":{\"client\":{\"sha1\":\"1111111111111111111111111111111111111111\",\"size\":100,\"url\":\"https://piston-data.mojang.com/v1/objects/1111111111111111111111111111111111111111/client.jar\"}}," +
                "\"assetIndex\":{\"id\":\"5\",\"sha1\":\"2222222222222222222222222222222222222222\",\"size\":200,\"url\":\"https://piston-meta.mojang.com/v1/packages/2222222222222222222222222222222222222222/5.json\"}," +
                "\"libraries\":[" +
                  "{\"name\":\"org.example:parentlib:1.0\",\"downloads\":{\"artifact\":{\"path\":\"org/example/parentlib/1.0/parentlib-1.0.jar\",\"sha1\":\"3333333333333333333333333333333333333333\",\"size\":10,\"url\":\"https://libraries.minecraft.net/org/example/parentlib/1.0/parentlib-1.0.jar\"}}}," +
                  "{\"name\":\"org.lwjgl:lwjgl:3.3.3:natives-windows\",\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar\",\"sha1\":\"4444444444444444444444444444444444444444\",\"size\":11,\"url\":\"https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar\"}}}," +
                  "{\"name\":\"org.lwjgl:lwjgl:3.3.3:natives-windows-arm64\",\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows-arm64.jar\",\"sha1\":\"5555555555555555555555555555555555555555\",\"size\":12,\"url\":\"https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows-arm64.jar\"}}}," +
                  "{\"name\":\"org.example:linuxonly:1.0\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"linux\"}}],\"downloads\":{\"artifact\":{\"path\":\"org/example/linuxonly/1.0/linuxonly-1.0.jar\",\"sha1\":\"6666666666666666666666666666666666666666\",\"size\":13,\"url\":\"https://libraries.minecraft.net/org/example/linuxonly/1.0/linuxonly-1.0.jar\"}}}" +
                "]," +
                "\"arguments\":{" +
                  "\"game\":[\"--username\",\"${auth_player_name}\",\"--version\",\"${version_name}\",\"--gameDir\",\"${game_directory}\",\"--assetsDir\",\"${assets_root}\",\"--assetIndex\",\"${assets_index_name}\",\"--uuid\",\"${auth_uuid}\",\"--accessToken\",\"${auth_access_token}\",\"--userType\",\"${user_type}\",\"--versionType\",\"${version_type}\"]," +
                  "\"jvm\":[\"-Djava.library.path=${natives_directory}\",\"-cp\",\"${classpath}\"]" +
                "}}";
        }

        private static string ChildVersionJson()
        {
            return "{" +
                "\"id\":\"fabric-loader-0.16.14-1.20.1\"," +
                "\"inheritsFrom\":\"1.20.1\"," +
                "\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\"," +
                "\"arguments\":{\"jvm\":[\"-DFabricMcEmu= net.minecraft.client.main.Main \"],\"game\":[]}," +
                "\"libraries\":[{\"name\":\"org.ow2.asm:asm:9.8\",\"url\":\"https://maven.fabricmc.net/\",\"sha1\":\"7777777777777777777777777777777777777777\",\"size\":14}]" +
                "}";
        }

        // ============================ 1.2.1 新增：第三方认证 / argfile ============================

        private static void TestAuthValidation()
        {
            Check("valid littleskin", AuthValidation.IsValidAuthServerUrl(
                "https://littleskin.cn/api/yggdrasil"));
            Check("valid root", AuthValidation.IsValidAuthServerUrl("https://example.com"));
            Check("reject http", !AuthValidation.IsValidAuthServerUrl("http://example.com"));
            Check("reject non-443", !AuthValidation.IsValidAuthServerUrl("https://example.com:8443/api"));
            Check("reject ftp", !AuthValidation.IsValidAuthServerUrl("ftp://example.com"));
            Check("reject empty", !AuthValidation.IsValidAuthServerUrl(""));
            Check("reject userinfo", !AuthValidation.IsValidAuthServerUrl("https://u@example.com"));
            Check("normalize trailing slash", AuthValidation.NormalizeAuthServerUrl(
                "https://littleskin.cn/api/yggdrasil/") == "https://littleskin.cn/api/yggdrasil");
            CheckEqual("host", "littleskin.cn",
                AuthValidation.GetHost("https://littleskin.cn/api/yggdrasil"));
            Check("uuid ok", AuthValidation.IsValidUuid("0626e58a-2c2a-4c41-b4f6-c860998dd1d1"));
            Check("uuid upper ok", AuthValidation.IsValidUuid("0626E58A-2C2A-4C41-B4F6-C860998DD1D1"));
            Check("uuid short", !AuthValidation.IsValidUuid("0626e58a-2c2a-4c41"));
            // 32 位无连字符是部分服务器（LittleSkin Yggdrasil Connect）的实际返回形式，
            // 必须接受（详见 TestUuidFormatTolerance），否则登录会报"UUID 格式无效"。
            Check("uuid no dash accepted",
                AuthValidation.IsValidUuid("0626e58a2c2a4c41b4f6c860998dd1d1"));
            CheckEqual("uuid no dash normalized",
                "0626e58a-2c2a-4c41-b4f6-c860998dd1d1",
                AuthValidation.NormalizeUuid("0626e58a2c2a4c41b4f6c860998dd1d1"));
            Check("name ok", AuthValidation.IsValidAuthDisplayName("Steve"));
            Check("name 1 char ok", AuthValidation.IsValidAuthDisplayName("A"));
            Check("name 16 char ok", AuthValidation.IsValidAuthDisplayName("ABCDEFGHIJKLMNOP"));
            Check("name too long", !AuthValidation.IsValidAuthDisplayName("ABCDEFGHIJKLMNOPQ"));
            Check("name space", !AuthValidation.IsValidAuthDisplayName("Ste ve"));
            Check("name colon", !AuthValidation.IsValidAuthDisplayName("Ste:ve"));
        }

        private static void TestYggdrasilParse()
        {
            string successJson = "{\"accessToken\":\"tok-123\",\"clientToken\":\"ct-456\"," +
                "\"selectedProfile\":{\"id\":\"0626e58a-2c2a-4c41-b4f6-c860998dd1d1\"," +
                "\"name\":\"SteveAuth\"}}";
            YggdrasilResult ok = YggdrasilClient.ParseAuthenticateResponse(successJson);
            Check("success flag", ok.Success);
            CheckEqual("token", "tok-123", ok.AccessToken);
            CheckEqual("name", "SteveAuth", ok.Name);
            CheckEqual("uuid", "0626e58a-2c2a-4c41-b4f6-c860998dd1d1", ok.Uuid);

            string errorJson = "{\"error\":\"ForbiddenOperationException\"," +
                "\"errorMessage\":\"Invalid credentials.\"}";
            YggdrasilResult err = YggdrasilClient.ParseAuthenticateResponse(errorJson);
            Check("error flag", !err.Success);
            Check("error message", err.ErrorMessage.Contains("Invalid credentials"));

            YggdrasilResult empty = YggdrasilClient.ParseAuthenticateResponse("");
            Check("empty error", !empty.Success && !string.IsNullOrEmpty(empty.ErrorMessage));

            string noProfile = "{\"accessToken\":\"tok\",\"clientToken\":\"ct\"}";
            YggdrasilResult noProf = YggdrasilClient.ParseAuthenticateResponse(noProfile);
            Check("missing profile", !noProf.Success);
        }

        private static void TestAccountIds()
        {
            CheckEqual("offline id", "offline|Steve", AccountIds.Offline("Steve"));
            CheckEqual("auth id", "auth|littleskin.cn|Steve",
                AccountIds.Auth("https://littleskin.cn/api/yggdrasil", "Steve"));

            List<Account> accounts = new List<Account>();
            Account old = new Account();
            old.Id = "a";
            old.UserName = "old";
            old.LastUsedUnix = 100;
            Account newer = new Account();
            newer.Id = "b";
            newer.UserName = "new";
            newer.LastUsedUnix = 200;
            accounts.Add(old);
            accounts.Add(newer);
            List<Account> sorted = AccountIds.SortByLastUsed(accounts);
            Check("sort by last used", sorted[0].Id == "b" && sorted[1].Id == "a");
        }

        private static void TestConfigAccounts()
        {
            string path = Path.Combine(_root, "config-accounts.json");
            LauncherConfig config = new LauncherConfig();
            Account auth = new Account();
            auth.Id = AccountIds.Auth("https://littleskin.cn/api/yggdrasil", "Steve");
            auth.TypeCode = (int)AccountType.Authlib;
            auth.UserName = "steve@example.com";
            auth.DisplayName = "Steve";
            auth.Uuid = "0626e58a-2c2a-4c41-b4f6-c860998dd1d1";
            auth.AccessToken = "tok";
            auth.ClientToken = "ct";
            auth.AuthServerUrl = "https://littleskin.cn/api/yggdrasil";
            config.Accounts.Add(auth);
            config.SelectedAccountId = auth.Id;
            config.AuthServers.Add(new AuthServerPreset("LittleSkin", "https://littleskin.cn/api/yggdrasil"));
            config.AuthlibInjectorPath = "C:\\tools\\authlib-injector.jar";
            config.SaveTo(path);

            LauncherConfig loaded = LauncherConfig.LoadFrom(path);
            Check("accounts count", loaded.Accounts != null && loaded.Accounts.Count == 1);
            Check("selected kept", loaded.SelectedAccountId == auth.Id);
            Check("display kept", loaded.Accounts[0].DisplayName == "Steve");
            Check("token kept", loaded.Accounts[0].AccessToken == "tok");
            Check("server kept", loaded.Accounts[0].AuthServerUrl == "https://littleskin.cn/api/yggdrasil");
            Check("presets count", loaded.AuthServers != null && loaded.AuthServers.Count == 1);
            Check("injector path", loaded.AuthlibInjectorPath.EndsWith(
                "authlib-injector.jar", StringComparison.OrdinalIgnoreCase));

            // 密码绝不落盘：配置里不得出现任何密码字段，也不得出现明文密码值。
            string raw = File.ReadAllText(path);
            Check("no password field", raw.IndexOf("\"Password\"", StringComparison.OrdinalIgnoreCase) < 0);
            Check("no plaintext password", raw.IndexOf("my-secret-pw", StringComparison.Ordinal) < 0);

            // 文件权限必须收紧为"仅当前用户/系统/管理员"，且不继承上级目录。
            // （net48 实例方法 / net6 扩展方法均可编译，避免使用 net6 已移除的静态重载。）
            System.Security.AccessControl.FileSecurity security =
                new FileInfo(path).GetAccessControl();
            Check("config acl protected", security.AreAccessRulesProtected);
            bool inherited = false;
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                security.GetAccessRules(true, true,
                    typeof(System.Security.Principal.SecurityIdentifier)))
            {
                if (rule.IsInherited)
                {
                    inherited = true;
                }
            }
            Check("config acl not inherited", !inherited);
        }

        private static void TestAuthService()
        {
            LauncherConfig config = new LauncherConfig();
            config.Accounts = new List<Account>();
            config.SelectedAccountId = "";
            config.LastUsername = "Steve";
            Account fallback = AuthService.GetSelectedAccount(config);
            Check("fallback created", fallback != null && fallback.Type == AccountType.Offline);
            Check("fallback id matches", fallback.Id == AccountIds.Offline("Steve"));
            // 纯查询不应写入配置：否则每次读取都会给账号列表多加一条记录。
            Check("query has no side effect", config.Accounts.Count == 0);

            // 显式初始化才落盘。
            Account ensured = AuthService.EnsureDefaultAccount(config);
            Check("ensure creates", ensured != null && config.Accounts.Count == 1);
            Check("ensure selects", config.SelectedAccountId == ensured.Id);
            Check("ensure id matches last username", ensured.Id == AccountIds.Offline("Steve"));

            config.Accounts.Clear();
            config.SelectedAccountId = "";
            Account offline = AuthService.CreateOffline("Alex");
            AuthService.AddOrUpdateAccount(config, offline);
            Account byId = AuthService.GetAccountById(config, offline.Id);
            Check("find by id", byId != null && byId.UserName == "Alex");
            AuthService.AddOrUpdateAccount(config, AuthService.CreateOffline("Alex"));
            Check("dedupe offline", AuthService.GetAccounts(config).Count == 1);

            AuthService.DeleteAccount(config, offline.Id);
            Check("delete clears selected", string.IsNullOrEmpty(config.SelectedAccountId));
            Check("delete removes", AuthService.GetAccounts(config).Count == 0);
        }

        private static void TestAuthLaunchPlan()
        {
            string gameDir = Path.Combine(_root, "auth-launch");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");

            string injectorPath = Path.Combine(_root, "authlib-injector.jar");
            File.WriteAllText(injectorPath, "FakeInjectorBytes");

            Account account = new Account();
            account.TypeCode = (int)AccountType.Authlib;
            account.UserName = "steve@example.com";
            account.DisplayName = "SteveAuth";
            account.Uuid = "0626e58a-2c2a-4c41-b4f6-c860998dd1d1";
            account.AccessToken = "real-token";
            account.ClientToken = "client-token";
            account.AuthServerUrl = "https://littleskin.cn/api/yggdrasil";

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.20.1";
            options.Username = account.DisplayName;
            options.Account = account;
            options.AuthlibInjectorPath = injectorPath;
            options.JavaPath = "C:\\Java\\bin\\javaw.exe";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 4096;

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.ProbePath = "C:\\Java\\bin\\java.exe";
            java.Major = 17;

            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("agent injected", plan.Arguments.Contains("-javaagent:"));
            Check("agent jar", plan.Arguments.Contains("authlib-injector.jar"));
            Check("agent server", plan.Arguments.Contains("https://littleskin.cn/api/yggdrasil"));
            Check("auth name", plan.Arguments.Contains("--username SteveAuth"));
            Check("auth uuid", plan.Arguments.Contains("--uuid 0626e58a-2c2a-4c41-b4f6-c860998dd1d1"));
            Check("auth token", plan.Arguments.Contains("--accessToken real-token"));
            Check("auth user type", plan.Arguments.Contains("--userType mojang"));

            // 注入器缺失时必须在生成启动计划前报错。
            LaunchOptions missing = new LaunchOptions();
            missing.GameDir = gameDir;
            missing.VersionId = "1.20.1";
            missing.Username = account.DisplayName;
            missing.Account = account;
            missing.JavaPath = "C:\\Java\\bin\\javaw.exe";
            missing.MinMemoryMB = 1024;
            missing.MaxMemoryMB = 4096;
            // 指向一个确定不存在的注入器：不能依赖默认路径，
            // 用户机器上可能已经下载过 authlib-injector（实测存在）。
            missing.AuthlibInjectorPath = Path.Combine(_root, "no-such-injector.jar");
            bool threw = false;
            try
            {
                GameLauncher.BuildPlan(version, missing, java, gameDir);
            }
            catch (FileNotFoundException)
            {
                threw = true;
            }
            Check("missing injector rejected", threw);
        }

        /// <summary>
        /// 1.7.5 回归（B-06）：微软正版账号必须走认证分支，而不是被当成离线账号。
        ///
        /// 1.7.0 的 BuildPlaceholderValues 只判断 AccountType.Authlib，
        /// 微软账号落进 else 分支：accessToken="0"、userType="legacy"、
        /// 本地 v3 UUID —— 加入任何正版服务器必然失败，而 UI 显示"登录成功"。
        /// </summary>
        private static void TestMicrosoftLaunchPlan()
        {
            string gameDir = Path.Combine(_root, "ms-launch");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");

            Account account = new Account();
            account.TypeCode = (int)AccountType.Microsoft;
            account.UserName = "NotchMs";
            account.DisplayName = "NotchMs";
            account.Uuid = "069a79f444e94726a5befca90e38aaf5"; // 无连字符，应被规范化
            account.AccessToken = "ms-access-token";
            account.MicrosoftRefreshToken = "ms-refresh-token";

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.20.1";
            options.Username = account.UserName;
            options.Account = account;
            options.JavaPath = "C:\\Java\\bin\\javaw.exe";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 4096;

            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.ProbePath = "C:\\Java\\bin\\java.exe";
            java.Major = 17;

            LaunchPlan plan = GameLauncher.BuildPlan(version, options, java, gameDir);
            Check("ms: real token used", plan.Arguments.Contains("--accessToken ms-access-token"));
            Check("ms: userType is mojang", plan.Arguments.Contains("--userType mojang"));
            Check("ms: offline token not used", !plan.Arguments.Contains("--accessToken 0"));
            Check("ms: uuid normalized",
                plan.Arguments.Contains("--uuid 069a79f4-44e9-4726-a5be-fca90e38aaf5"));
            Check("ms: uuid not offline-derived",
                !plan.Arguments.Contains(CommandLine.ComputeOfflineUuidCompact("NotchMs")));
            // 微软账号不需要 authlib-injector：缺少注入器时也不能报错。
            Check("ms: no injector injected", !plan.Arguments.Contains("-javaagent:"));

            // 令牌缺失必须被前置校验拒绝（而不是生成一个"能跑但进不了正版服"的计划）。
            Account noToken = new Account();
            noToken.TypeCode = (int)AccountType.Microsoft;
            noToken.UserName = "NotchMs";
            noToken.DisplayName = "NotchMs";
            noToken.Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
            noToken.AccessToken = "";
            LaunchOptions bad = new LaunchOptions();
            bad.GameDir = gameDir;
            bad.VersionId = "1.20.1";
            bad.Username = noToken.UserName;
            bad.Account = noToken;
            bad.JavaPath = "C:\\Java\\bin\\javaw.exe";
            bool threw = false;
            try
            {
                GameLauncher.Prepare(bad, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check("ms: missing token rejected", threw);
        }

        /// <summary>
        /// 1.7.5 回归（B-07）：微软 refresh_token 必须能加密落盘并读回。
        ///
        /// 此前写入端用 "microsoft-refresh" 作 DPAPI 熵，读取端统一用 "account-token"，
        /// 加解密不匹配必然抛异常并被吞成空串 —— refresh_token 永远读不回来。
        /// </summary>
        private static void TestMicrosoftRefreshTokenRoundtrip()
        {
            string path = Path.Combine(_root, "cfg-ms-refresh.json");
            LauncherConfig config = new LauncherConfig();
            config.GameDirExplicit = true;
            config.GameDir = Path.Combine(_root, "ms-game");
            Account account = new Account();
            account.Id = "ms|NotchMs";
            account.TypeCode = (int)AccountType.Microsoft;
            account.UserName = "NotchMs";
            account.DisplayName = "NotchMs";
            account.Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
            account.AccessToken = "ms-access-token";
            account.MicrosoftRefreshToken = "ms-refresh-token-sentinel";
            account.AccessTokenProtected = CredentialStore.Protect(
                account.AccessToken, "account-token");
            account.MicrosoftRefreshTokenProtected = CredentialStore.Protect(
                account.MicrosoftRefreshToken, "microsoft-refresh");
            config.Accounts.Add(account);
            config.SaveTo(path);

            LauncherConfig loaded = LauncherConfig.LoadFrom(path);
            Check("ms refresh: account survived", loaded.Accounts.Count == 1);
            Check("ms refresh: token readable",
                loaded.Accounts[0].MicrosoftRefreshToken == "ms-refresh-token-sentinel");
            Check("ms refresh: access token readable",
                loaded.Accounts[0].AccessToken == "ms-access-token");
            Check("ms refresh: no re-migration needed",
                !loaded.CredentialsMigrated);
        }

        /// <summary>
        /// 1.7.5 回归（B-09）：光影目录必须按「启动页选中版本」解析，
        /// 而不是模组页的 Fabric 版本 —— 后者为空时隔离模式会落到共享目录，
        /// 游戏读的是实例目录，光影"装成功但看不到"。
        /// </summary>
        private static void TestShaderpacksLaunchVersion()
        {
            string gameDir = Path.Combine(_root, "shader-version");
            Directory.CreateDirectory(gameDir);
            string exeVersion = "1.20.1";
            string isolated = InstancePaths.GetShaderpacksDirectory(gameDir, exeVersion, true);
            string shared = InstancePaths.GetShaderpacksDirectory(gameDir, "", true);
            Check("empty version falls back to shared",
                shared.EndsWith("shaderpacks", StringComparison.OrdinalIgnoreCase));
            Check("selected version yields instance dir",
                isolated.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0);
            Check("instance dir differs from shared",
                !string.Equals(isolated, shared, StringComparison.OrdinalIgnoreCase));
            Check("instance dir is under selected version",
                isolated.IndexOf(exeVersion, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 回归：JsonEscape 必须转义控制字符。旧实现只处理反斜杠与引号，
        /// 密码含换行时会产出非法 JSON，属于注入面。
        /// </summary>
        private static void TestYggdrasilJsonEscape()
        {
            // 通过公开入口间接验证：构造含控制字符的响应不应崩溃，
            // 含控制字符的用户名/密码不会产生可解析的结构破坏。
            string badBody = "{\"error\":\"forbidden\",\"errorMessage\":\"line1\\nline2\"}";
            YggdrasilResult parsed = YggdrasilClient.ParseAuthenticateResponse(badBody);
            Check("error response handled", !parsed.Success && !string.IsNullOrEmpty(parsed.ErrorMessage));

            // 正常响应必须能解析出会话数据。
            string okBody = "{\"accessToken\":\"tok\",\"clientToken\":\"cli\"," +
                            "\"selectedProfile\":{\"id\":\"0626e58a-2c2a-4c41-b4f6-c860998dd1d1\"," +
                            "\"name\":\"Steve\"}}";
            YggdrasilResult ok = YggdrasilClient.ParseAuthenticateResponse(okBody);
            Check("ok response parsed", ok.Success && ok.AccessToken == "tok");
            Check("ok profile name", ok.Name == "Steve");

            // 缺 selectedProfile 时必须给出明确错误而不是空指针。
            YggdrasilResult noProfile = YggdrasilClient.ParseAuthenticateResponse(
                "{\"accessToken\":\"tok\"}");
            Check("missing profile rejected", !noProfile.Success &&
                noProfile.ErrorMessage.IndexOf("selectedProfile", StringComparison.Ordinal) >= 0);

            // 非法 UUID 必须拒绝。
            YggdrasilResult badUuid = YggdrasilClient.ParseAuthenticateResponse(
                "{\"accessToken\":\"tok\",\"selectedProfile\":{\"id\":\"not-a-uuid\",\"name\":\"Steve\"}}");
            Check("invalid uuid rejected", !badUuid.Success);
        }

        /// <summary>
        /// 回归：无 SHA1 下载必须校验 JAR 结构，否则损坏文件会被当作注入器带上 -javaagent。
        /// </summary>
        private static void TestJarArchiveDetection()
        {
            string dir = Path.Combine(_root, "jardetect");
            Directory.CreateDirectory(dir);

            string fakeText = Path.Combine(dir, "fake.jar");
            File.WriteAllText(fakeText, "this is not a zip");
            Check("text file rejected", !HttpUtil.LooksLikeJarArchive(fakeText));

            string empty = Path.Combine(dir, "empty.jar");
            File.WriteAllBytes(empty, new byte[0]);
            Check("empty file rejected", !HttpUtil.LooksLikeJarArchive(empty));

            string truncated = Path.Combine(dir, "truncated.jar");
            File.WriteAllBytes(truncated, new byte[] { 0x50, 0x4B });
            Check("truncated header rejected", !HttpUtil.LooksLikeJarArchive(truncated));

            string real = Path.Combine(dir, "real.jar");
            using (FileStream stream = new FileStream(real, FileMode.Create))
            using (System.IO.Compression.ZipArchive archive =
                new System.IO.Compression.ZipArchive(stream,
                    System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0");
            }
            Check("real zip accepted", HttpUtil.LooksLikeJarArchive(real));
        }

        /// <summary>
        /// 回归：动态白名单在多线程下读写必须安全（HashSet 非线程安全）。
        /// 旧实现写入加锁、读取不加锁，扩容时可能读到损坏结构。
        /// </summary>
        private static void TestAllowlistConcurrency()
        {
            int failures = 0;
            System.Threading.Tasks.Task[] workers = new System.Threading.Tasks.Task[4];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = System.Threading.Tasks.Task.Run(delegate
                {
                    for (int i = 0; i < 200; i++)
                    {
                        try
                        {
                            HttpUtil.IsAllowedUrl("https://piston-meta.mojang.com/x");
                            HttpUtil.AddAllowedHost("probe" + (i % 7) + ".example.com");
                        }
                        catch (Exception)
                        {
                            System.Threading.Interlocked.Increment(ref failures);
                        }
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(workers);
            Check("concurrent allowlist access", failures == 0);
            // 内置白名单不能被动态添加破坏。
            Check("builtin host still allowed", HttpUtil.IsAllowedUrl(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
        }

        private static void TestCrashAnalyzer()
        {
            string gameDir = Path.Combine(_root, "crash");
            Directory.CreateDirectory(gameDir);
            Directory.CreateDirectory(Path.Combine(gameDir, "logs"));
            Directory.CreateDirectory(Path.Combine(gameDir, "crash-reports"));

            // 无日志时：不应崩溃，也不应产生结论。
            CrashReport empty = CrashAnalyzer.Analyze(gameDir);
            Check("no logs no findings", empty.Findings.Count == 0 && empty.SourceFile == null);

            // 写成已知的"缺少前置"崩溃报告。
            string crashFile = Path.Combine(gameDir, "crash-reports", "crash-1.txt");
            File.WriteAllText(crashFile,
                "---- Minecraft Crash Report ----\r\n" +
                "Description: Initializing game\r\n\r\n" +
                "net.fabricmc.loader.api.ModResolutionException: " +
                "Missing or unsupported mandatory dependencies:\r\n" +
                "\tMod ID: 'appleskin', Requested by: 'fabric-api', " +
                "Expected range: '>=0.92'\r\n", new UTF8Encoding(false));

            CrashReport report = CrashAnalyzer.Analyze(gameDir);
            Check("crash report detected", report.HasCrashReport);
            Check("finding produced", report.Findings.Count >= 1);
            Check("finding is dependency", report.Findings[0].Title.IndexOf(
                "前置", StringComparison.Ordinal) >= 0);
            Check("finding has advice", report.Findings[0].Advice.Length > 0);
            Check("finding has evidence", report.Findings[0].Evidence.Length > 0);
            string formatted = CrashAnalyzer.Format(report);
            Check("format mentions source", formatted.IndexOf("crash-reports",
                StringComparison.OrdinalIgnoreCase) >= 0);
            Check("format mentions advice", formatted.IndexOf("建议", StringComparison.Ordinal) >= 0);

            // Java 版本过低：另一类模式。
            File.WriteAllText(crashFile,
                "java.lang.UnsupportedClassVersionError: " +
                "net/minecraft/client/main/Main has been compiled by a more recent " +
                "version of the Java Runtime\r\n", new UTF8Encoding(false));
            CrashReport javaReport = CrashAnalyzer.Analyze(gameDir);
            Check("java version finding", javaReport.Findings.Count >= 1 &&
                javaReport.Findings[0].Title.IndexOf("Java", StringComparison.Ordinal) >= 0);

            // 正常日志：不应误报。
            File.Delete(crashFile);
            File.WriteAllText(Path.Combine(gameDir, "logs", "latest.log"),
                "[main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader 0.20.1\r\n" +
                "[Render thread/INFO]: Sound engine started\r\n", new UTF8Encoding(false));
            CrashReport clean = CrashAnalyzer.Analyze(gameDir);
            Check("clean log no findings", clean.Findings.Count == 0);
            Check("clean log uses latest.log", clean.HasGameLog &&
                clean.SourceFile.IndexOf("latest.log", StringComparison.OrdinalIgnoreCase) >= 0);
            Check("clean log format explains", CrashAnalyzer.Format(clean).IndexOf(
                "未在日志中发现", StringComparison.Ordinal) >= 0);

            // 缺失目录不应抛异常。
            CrashReport missing = CrashAnalyzer.Analyze(Path.Combine(_root, "no-such-dir"));
            Check("missing dir safe", missing.Findings.Count == 0);
        }

        /// <summary>
        /// 异地联机配置规范化：网络名字符集/长度、空节点回落默认、开关与网络名联动。
        /// </summary>
        private static void TestEasyTierConfig()
        {
            LauncherConfig config = new LauncherConfig();
            config.EasyTierEnabled = true;
            config.EasyTierNetworkName = "my-net_1";
            config.EasyTierNetworkSecret = "secret";
            config.EasyTierPublicNode = "";
            config.Normalize();
            Check("et name kept", config.EasyTierNetworkName == "my-net_1");
            Check("et default node", config.EasyTierPublicNode == EasyTierService.DefaultPublicNode);
            Check("et stays enabled", config.EasyTierEnabled);

            // 非法字符（含空格/中文）应清空网络名并自动关闭开关。
            config.EasyTierEnabled = true;
            config.EasyTierNetworkName = "bad name!";
            config.Normalize();
            Check("et invalid name cleared", config.EasyTierNetworkName == "");
            Check("et disabled when no name", !config.EasyTierEnabled);

            // 超长名字截断到 64。
            config.EasyTierEnabled = true;
            config.EasyTierNetworkName = new string('a', 100);
            config.Normalize();
            Check("et name truncated", config.EasyTierNetworkName.Length == 64);
            Check("et enabled after truncate", config.EasyTierEnabled);

            // Normalize 幂等。
            config.Normalize();
            Check("et idempotent name", config.EasyTierNetworkName.Length == 64);
            Check("et idempotent node", config.EasyTierPublicNode == EasyTierService.DefaultPublicNode);

            // 密钥超长截断。
            config.EasyTierNetworkSecret = new string('k', 200);
            config.Normalize();
            Check("et secret truncated", config.EasyTierNetworkSecret.Length == 128);

            // 往返：保存再读取，五个字段都在。
            string path = Path.Combine(_root, "config-easytier.json");
            config.EasyTierEnabled = true;
            config.EasyTierNetworkName = "net1";
            config.EasyTierNetworkSecret = "pw1";
            config.EasyTierPublicNode = "tcp://node:11010";
            config.EasyTierBinaryPath = "";
            config.SaveTo(path);
            LauncherConfig loaded = LauncherConfig.LoadFrom(path);
            Check("et roundtrip enabled", loaded.EasyTierEnabled);
            Check("et roundtrip name", loaded.EasyTierNetworkName == "net1");
            Check("et roundtrip secret", loaded.EasyTierNetworkSecret == "pw1");
            Check("et roundtrip node", loaded.EasyTierPublicNode == "tcp://node:11010");
        }

        /// <summary>
        /// easytier-core 参数拼装：-n/-k/-p 顺序与含空格值的引用。
        /// </summary>
        private static void TestEasyTierArguments()
        {
            // 参数契约以 easytier-core v2.6.4 的 --help 实测为准：
            //   网络名 = --network-name（无 -n 短参数；-n 是 --proxy-networks）
            //   密钥   = --network-secret（无 -k，传 -k 会让核心报 unexpected argument）
            //   节点   = -p
            //   虚拟IP = -i（固定）或 -d（DHCP）
            List<string> args = EasyTierService.BuildArguments("net1", "pw1", "");
            CheckEqual("args count", "7", args.Count.ToString());
            CheckEqual("network name flag", "--network-name", args[0]);
            CheckEqual("network name value", "net1", args[1]);
            CheckEqual("network secret flag", "--network-secret", args[2]);
            CheckEqual("network secret value", "pw1", args[3]);
            CheckEqual("peer flag", "-p", args[4]);
            CheckEqual("peer value default", EasyTierService.DefaultPublicNode, args[5]);
            CheckEqual("default ip mode", "-d", args[6]);

            // 旧参数必须彻底消失：这两条是"联机不可用"的直接原因。
            Check("no -k anywhere", !ContainsArg(args, "-k"));
            Check("no bare -n anywhere", !ContainsArg(args, "-n"));

            // 空密钥时省略 --network-secret（保留 -p 与 -d）。
            List<string> noSecret = EasyTierService.BuildArguments("net2", "", "");
            CheckEqual("no secret count", "5", noSecret.Count.ToString());
            Check("no secret omits flag", !ContainsArg(noSecret, "--network-secret"));

            // 自定义节点。
            List<string> custom = EasyTierService.BuildArguments("net3", "pw3", "tcp://custom:11010");
            CheckEqual("custom peer", "tcp://custom:11010", custom[5]);

            // 显式虚拟 IP 时用 -i 而不是 -d，且顺序紧跟节点之后。
            List<string> fixedIp = EasyTierService.BuildArguments(
                "net4", "pw4", "", "10.144.3.21");
            CheckEqual("fixed ip flag", "-i", fixedIp[6]);
            CheckEqual("fixed ip value", "10.144.3.21", fixedIp[7]);
            Check("no -d when -i given", !ContainsArg(fixedIp, "-d"));

            // 非法 IP 回落 DHCP，避免把坏值传给核心。
            List<string> badIp = EasyTierService.BuildArguments("net5", "pw5", "", "not-an-ip");
            Check("invalid ip falls back to dhcp", ContainsArg(badIp, "-d"));

            // 空网络名必须抛错。
            CheckThrows<ArgumentException>("empty network rejected", delegate
            {
                EasyTierService.BuildArguments("", "pw", "");
            });

            // 含空格的值经 CommandLine 引用后仍是单个参数。
            List<string> spaced = EasyTierService.BuildArguments("net 4", "pw 4", "");
            string built = CommandLine.BuildArguments(spaced);
            Check("spaced value quoted", built.IndexOf("\"net 4\"", StringComparison.Ordinal) >= 0);
        }

        private static bool ContainsArg(List<string> args, string value)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 虚拟 IP 解析：只认 10.144.0.0/16，且能从混合文本中提取。
        /// </summary>
        private static void TestEasyTierVirtualIp()
        {
            Check("empty text", EasyTierService.ParseVirtualIp("") == "");
            Check("no ip", EasyTierService.ParseVirtualIp("starting easytier...") == "");
            Check("simple ip", EasyTierService.ParseVirtualIp("virtual ip: 10.144.5.7") == "10.144.5.7");
            string multi = "node 10.0.0.1\r\nvirtual 10.144.12.34\r\nother 192.168.1.1";
            Check("multi line ip", EasyTierService.ParseVirtualIp(multi) == "10.0.0.1");

            // DHCP 模式默认从 10.0.0.1 起分配，因此 10.x 全段都必须识别；
            // 旧实现只认 10.144，DHCP 模式下网卡存在也会探测失败。
            Check("dhcp range accepted", EasyTierService.ParseVirtualIp("ipv4 = \"10.0.0.15\"") == "10.0.0.15");
            Check("is virtual ip 10.0.x", EasyTierService.IsVirtualIp("10.0.0.15"));
            Check("is virtual ip 10.144.x", EasyTierService.IsVirtualIp("10.144.1.1"));
            // 物理网卡网段不能误判为虚拟网卡。
            Check("192.168 not virtual", !EasyTierService.IsVirtualIp("192.168.1.1"));
            Check("172.16 not virtual", !EasyTierService.IsVirtualIp("172.16.0.1"));
            Check("parse ignores 192.168", EasyTierService.ParseVirtualIp("only 192.168.1.1") == "");
        }

        /// <summary>
        /// EasyTier 核心必须随包带 wintun.dll，否则虚拟网卡建不出来。
        /// 实测：仅装 exe 时核心持续报 "tun device error: The file is not signed"；
        /// 补上 wintun.dll 后网卡立即创建（10.126.126.1）。
        /// </summary>
        private static void TestEasyTierWintunRequirement()
        {
            string dir = Path.Combine(_root, "et-wintun");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, EasyTierService.DefaultBinaryName);
            string dll = Path.Combine(dir, EasyTierService.RequiredWintunDll);

            // 只有 exe（模拟旧版残缺安装）：应判定为"未完整安装"。
            using (FileStream fs = new FileStream(exe, FileMode.Create))
            {
                fs.SetLength(2 * 1024 * 1024);   // 大于 1MB 门槛
            }
            Check("exe alone is not fully installed",
                EasyTierService.Exists(exe) && !EasyTierService.IsFullyInstalled(exe));

            // 补上 wintun.dll 后应判定为完整。
            using (FileStream fs = new FileStream(dll, FileMode.Create))
            {
                fs.SetLength(400 * 1024);        // 大于 100KB 门槛
            }
            Check("exe + dll is fully installed", EasyTierService.IsFullyInstalled(exe));

            // dll 过小视为无效（避免放一个占位文件就当成装好）。
            using (FileStream fs = new FileStream(dll, FileMode.Create))
            {
                fs.SetLength(1024);
            }
            Check("tiny dll rejected", !EasyTierService.IsFullyInstalled(exe));

            // exe 过小同样不算安装。
            using (FileStream fs = new FileStream(exe, FileMode.Create))
            {
                fs.SetLength(1024);
            }
            Check("tiny exe rejected", !EasyTierService.Exists(exe));

            // 诊断文本必须能识别 TUN 失败并给出可操作建议。
            string tun = EasyTierService.DiagnoseText(
                "tun device error err=rust tun error The file is not signed.");
            Check("tun diagnosis non-empty", tun.Length > 0);
            Check("tun diagnosis actionable",
                tun.IndexOf("管理员", StringComparison.Ordinal) >= 0 ||
                tun.IndexOf("驱动", StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// 核心失败诊断：把不同报错映射成可操作的中文建议。
        /// 旧实现只提示"未能获取虚拟 IP"，用户无法知道是参数错、驱动问题还是端口占用。
        /// </summary>
        private static void TestEasyTierDiagnosis()
        {
            // 参数不被接受（旧版 -k 的实际报错）
            Check("diagnose unexpected argument",
                EasyTierService.DiagnoseFailure().Length >= 0);

            // 直接用文本匹配验证关键字覆盖（避免依赖运行状态）。
            string[] keywordCases = new string[]
            {
                "error: unexpected argument '-k' found",
                "ERROR CORE::INSTANCE tun device error err=rust tun error The file is not signed.",
                "bind: AddrInUse (os error 10048)",
                "Failed to add Easytier to firewall allowlist error=0x80070005"
            };
            string[] expected = new string[]
            {
                "版本不匹配", "虚拟网卡", "端口", "防火墙"
            };
            for (int i = 0; i < keywordCases.Length; i++)
            {
                string mapped = EasyTierService.DiagnoseText(keywordCases[i]);
                Check("diagnose case " + i + " mapped",
                    mapped.IndexOf(expected[i], StringComparison.Ordinal) >= 0);
            }
            // 空输入不应崩溃或给出误导性结论。
            Check("diagnose empty safe", EasyTierService.DiagnoseText("") == "");
        }

        /// <summary>
        /// 服务器 4xx 的响应体里带着 yggdrasil 业务错误，必须提取出来直接显示。
        /// 实测 LittleSkin 登录失败时返回 403 + {"errorMessage":"用户名或密码错误"}，
        /// 若丢掉响应体就只能报"HTTP 403"，用户会误以为是网络连不上。
        /// </summary>
        private static void TestAuthServerErrorExtraction()
        {
            // 典型：403 + 业务错误体（旧实现只看到 HTTP 403）
            string forbidden = "HTTP 403 (Forbidden)：https://littleskin.cn/api/yggdrasil/" +
                "authserver/authenticate\r\n" +
                "{\"error\":\"ForbiddenOperationException\"," +
                "\"errorMessage\":\"用户名或密码错误。Incorrect username or password.\"}";
            string extracted = YggdrasilClient.ExtractServerError(forbidden);
            Check("extracted non-empty", extracted.Length > 0);
            Check("uses errorMessage not code",
                extracted.IndexOf("用户名或密码错误", StringComparison.Ordinal) >= 0);
            Check("no http status leaked",
                extracted.IndexOf("HTTP 403", StringComparison.Ordinal) < 0);

            // 只有 error 字段时退回它
            string onlyError = "HTTP 400：{\"error\":\"IllegalArgumentException\"}";
            CheckEqual("falls back to error field",
                "IllegalArgumentException",
                YggdrasilClient.ExtractServerError(onlyError));

            // 无响应体时返回空，调用方保留原始网络错误
            Check("empty when no body",
                YggdrasilClient.ExtractServerError("HTTP 500 (Internal Server Error)") == "");
            Check("empty input safe", YggdrasilClient.ExtractServerError("") == "");
            Check("empty null safe", YggdrasilClient.ExtractServerError(null) == "");

            // 解析失败不应抛异常
            Check("malformed json tolerated",
                YggdrasilClient.ExtractServerError("HTTP 400：{broken json") == "");

            // 与解析器保持一致：ParseAuthenticateResponse 也能识别同样的错误体
            YggdrasilResult parsed = YggdrasilClient.ParseAuthenticateResponse(
                "{\"error\":\"ForbiddenOperationException\"," +
                "\"errorMessage\":\"用户名或密码错误。\"}");
            Check("response parser reports failure", !parsed.Success);
            Check("response parser keeps message",
                parsed.ErrorMessage != null &&
                parsed.ErrorMessage.IndexOf("用户名或密码错误", StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// -javaagent 参数不能带内层引号。
        /// Java 语法是 -javaagent:jarpath[=options]，引号会被当作文件名字面一部分，
        /// 导致 agent 加载失败、游戏以离线身份进入，表现为"无效会话"。
        /// </summary>
        private static void TestJavaAgentFormat()
        {
            string jar = @"C:\Users\me\AppData\Roaming\rll\authlib-injector.jar";
            string url = "https://littleskin.cn/api/yggdrasil";
            string arg = AuthlibInjectorService.BuildJavaAgentArgument(jar, url);

            Check("no inner quotes", arg.IndexOf('"') < 0);
            Check("correct prefix", arg.StartsWith("-javaagent:", StringComparison.Ordinal));
            CheckEqual("correct full form",
                "-javaagent:" + jar + "=" + url, arg);

            // 路径含空格时，外层转义应保证它仍是单个参数。
            string spacedJar = @"C:\Program Files\My Launcher\authlib-injector.jar";
            string spacedArg = AuthlibInjectorService.BuildJavaAgentArgument(spacedJar, url);
            List<string> list = new List<string>();
            list.Add(spacedArg);
            string cmd = CommandLine.BuildArguments(list);
            List<string> back = CommandLine.SplitArguments(cmd);
            Check("spaced jar stays one arg", back.Count == 1);
            CheckEqual("spaced jar roundtrip", spacedArg, back[0]);

            // 传入已带成对引号的路径时应自动去掉。
            string quoted = "\"" + jar + "\"";
            string cleanedPath = AuthlibInjectorService.BuildJavaAgentArgument(quoted, url);
            Check("quoted input cleaned", cleanedPath.IndexOf('"') < 0);
            CheckEqual("quoted cleaned full form",
                "-javaagent:" + jar + "=" + url, cleanedPath);

            // 非法服务器地址必须拒绝。
            CheckThrows<ArgumentException>("invalid server rejected", delegate
            {
                AuthlibInjectorService.BuildJavaAgentArgument(jar, "http://insecure.example.com");
            });
        }

        /// <summary>
        /// 认证服务器地址补全：只填站点首页时应自动补 /api/yggdrasil。
        /// 实测裸域名 https://littleskin.cn 的 /authserver/authenticate 返回 404，
        /// 带 /api/yggdrasil 才返回正常的业务错误（路由存在）。
        /// </summary>
        private static void TestAuthUrlCompletion()
        {
            CheckEqual("bare domain completed",
                "https://littleskin.cn/api/yggdrasil",
                AuthValidation.NormalizeAuthServerUrl("https://littleskin.cn"));
            CheckEqual("bare domain with slash",
                "https://littleskin.cn/api/yggdrasil",
                AuthValidation.NormalizeAuthServerUrl("https://littleskin.cn/"));
            CheckEqual("api path kept",
                "https://littleskin.cn/api/yggdrasil",
                AuthValidation.NormalizeAuthServerUrl("https://littleskin.cn/api/yggdrasil"));
            CheckEqual("trailing slash trimmed",
                "https://littleskin.cn/api/yggdrasil",
                AuthValidation.NormalizeAuthServerUrl("https://littleskin.cn/api/yggdrasil/"));
            CheckEqual("custom path kept",
                "https://skin.example.com/custom/api",
                AuthValidation.NormalizeAuthServerUrl("https://skin.example.com/custom/api"));
            Check("no scheme rejected",
                !AuthValidation.IsValidAuthServerUrl(
                    AuthValidation.NormalizeAuthServerUrl("littleskin.cn/api/yggdrasil")));

            string completed = AuthValidation.NormalizeAuthServerUrl("https://littleskin.cn");
            Check("completed url valid", AuthValidation.IsValidAuthServerUrl(completed));
            CheckEqual("endpoint built from completed",
                "https://littleskin.cn/api/yggdrasil/authserver/authenticate",
                completed + "/authserver/authenticate");
        }

        /// <summary>
        /// 登录前必须先把认证服务器主机加入白名单。
        /// 旧实现是"登录成功后才加"，形成死锁：请求自身要先过白名单校验，
        /// 于是首次登录必然报"不允许的请求地址"（LittleSkin 实测命中）。
        /// </summary>
        private static void TestAuthWhitelistBeforeLogin()
        {
            string url = BuiltinAuthServerPresets.LittleSkinUrl +
                         "/authserver/authenticate";
            string host = AuthValidation.GetHost(BuiltinAuthServerPresets.LittleSkinUrl);

            // 内置预设集中定义，界面与白名单放行处共用同一地址。
            List<AuthServerPreset> presets = BuiltinAuthServerPresets.GetAll();
            Check("builtin preset count", presets.Count >= 1);
            CheckEqual("builtin url", "https://littleskin.cn/api/yggdrasil",
                BuiltinAuthServerPresets.LittleSkinUrl);
            Check("host extracted", host == "littleskin.cn");

            // 即便配置里没有任何预设与账号，ApplyConfigToHttp 也要放行内置预设，
            // 否则"下拉里能选 LittleSkin 但一登录就报不允许"。
            LauncherConfig config = new LauncherConfig();
            config.AuthServers = new List<AuthServerPreset>();
            config.Accounts = new List<Account>();
            AuthService.ApplyConfigToHttp(config);
            Check("builtin host allowed after apply", HttpUtil.IsAllowedUrl(url));

            // 保存的预设主机同样要放行。
            config.AuthServers.Add(new AuthServerPreset(
                "自建", "https://skin.example.com/api/yggdrasil"));
            AuthService.ApplyConfigToHttp(config);
            Check("saved preset allowed", HttpUtil.IsAllowedUrl(
                "https://skin.example.com/authserver/authenticate"));

            // 已保存的认证账号主机也要放行（重启后校验令牌依赖它）。
            Account auth = AuthService.CreateOffline("tmp");
            auth.TypeCode = (int)AccountType.Authlib;
            auth.AuthServerUrl = "https://auth2.example.com/api/yggdrasil";
            auth.Uuid = "0626e58a-2c2a-4c41-b4f6-c860998dd1d1";
            config.Accounts.Add(auth);
            AuthService.ApplyConfigToHttp(config);
            Check("saved account host allowed", HttpUtil.IsAllowedUrl(
                "https://auth2.example.com/authserver/validate"));

            // 不在任何来源里的主机仍应被拒（证明放行是有边界的，没有全放开）。
            Check("unknown host still rejected", !HttpUtil.IsAllowedUrl(
                "https://evil.example.com/authserver/authenticate"));
        }

        /// <summary>
        /// 认证服务器注册页地址：内置站点走已知地址，未知站点按站点根推导。
        /// 启动器没有注册入口，新用户无从创建账号，这是本版补上的能力。
        /// </summary>
        private static void TestAuthRegisterUrl()
        {
            CheckEqual("littleskin register",
                "https://littleskin.cn/auth/register",
                AuthServerRegistry.GetRegisterUrl("https://littleskin.cn/api/yggdrasil"));
            // 结尾斜杠与大小写应被规范化。
            CheckEqual("littleskin trailing slash",
                "https://littleskin.cn/auth/register",
                AuthServerRegistry.GetRegisterUrl("https://Littleskin.cn/api/yggdrasil/"));

            // 未知站点：去掉 /api/ 后拼 /auth/register。
            CheckEqual("custom site register",
                "https://skin.example.com/auth/register",
                AuthServerRegistry.GetRegisterUrl("https://skin.example.com/api/yggdrasil"));

            // 站点根推导。
            CheckEqual("site root", "https://littleskin.cn",
                AuthServerRegistry.GetSiteRoot("https://littleskin.cn/api/yggdrasil"));

            // 占位值识别（界面默认填的是 https://）。
            Check("placeholder detected", AuthServerRegistry.IsPlaceholder("https://"));
            Check("real url not placeholder",
                !AuthServerRegistry.IsPlaceholder("https://littleskin.cn/api/yggdrasil"));
            Check("empty is placeholder", AuthServerRegistry.IsPlaceholder(""));
        }

        /// <summary>
        /// 下载源映射：官方主机 → 镜像；Modrinth 不映射；非法自定义源回落。
        /// </summary>
        private static void TestDownloadMirror()
        {
            // 官方源：原样返回。
            CheckEqual("official unchanged",
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                DownloadMirrors.Map("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                    DownloadMirrors.Official));

            // BmclAPI：官方主机被替换，路径保留。
            string mapped = DownloadMirrors.Map(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                DownloadMirrors.BmclApi);
            Check("bmclapi mapped host",
                mapped.StartsWith("https://bmclapi2.bangbang93.com/", StringComparison.Ordinal));
            Check("bmclapi kept path",
                mapped.IndexOf("version_manifest_v2.json", StringComparison.Ordinal) >= 0);

            // 库与资源也映射。
            Check("bmclapi libraries",
                DownloadMirrors.Map("https://libraries.minecraft.net/a/b.jar",
                    DownloadMirrors.BmclApi).StartsWith("https://bmclapi2.bangbang93.com/maven/",
                    StringComparison.Ordinal));
            Check("bmclapi resources",
                DownloadMirrors.Map("https://resources.download.minecraft.net/aa/bb",
                    DownloadMirrors.BmclApi).StartsWith("https://bmclapi2.bangbang93.com/resources/",
                    StringComparison.Ordinal));

            // Modrinth 不映射：API 与 CDN 保持原样。
            CheckEqual("modrinth api not mapped",
                "https://api.modrinth.com/v2/project/sodium",
                DownloadMirrors.Map("https://api.modrinth.com/v2/project/sodium",
                    DownloadMirrors.BmclApi));
            CheckEqual("modrinth cdn not mapped",
                "https://cdn.modrinth.com/data/x/y.jar",
                DownloadMirrors.Map("https://cdn.modrinth.com/data/x/y.jar",
                    DownloadMirrors.BmclApi));

            // 未知主机不映射。
            CheckEqual("unknown host unchanged",
                "https://example.com/x", DownloadMirrors.Map("https://example.com/x",
                    DownloadMirrors.BmclApi));

            // 自定义源校验：必须 https + 443 + 域名。
            Check("valid custom", DownloadMirrors.IsValidCustomMirror("https://mirror.example.com"));
            Check("http rejected", !DownloadMirrors.IsValidCustomMirror("http://mirror.example.com"));
            Check("port rejected", !DownloadMirrors.IsValidCustomMirror("https://mirror.example.com:8080"));
            Check("empty rejected", !DownloadMirrors.IsValidCustomMirror(""));
            Check("userinfo rejected", !DownloadMirrors.IsValidCustomMirror("https://u@mirror.example.com"));
            Check("trailing dot rejected", !DownloadMirrors.IsValidCustomMirror("https://mirror.example.com."));

            // 主机提取用于白名单。
            CheckEqual("bmclapi host", DownloadMirrors.BmclApiHost,
                DownloadMirrors.GetMirrorHost(DownloadMirrors.BmclApi));
            CheckEqual("custom host", "mirror.example.com",
                DownloadMirrors.GetMirrorHost("https://mirror.example.com"));

            // 配置层：非法自定义源回落 official。
            LauncherConfig config = new LauncherConfig();
            config.DownloadMirror = "http://bad.example.com";
            config.Normalize();
            CheckEqual("invalid mirror falls back", DownloadMirrors.Official, config.DownloadMirror);
            config.DownloadMirror = DownloadMirrors.BmclApi;
            config.Normalize();
            CheckEqual("bmclapi kept", DownloadMirrors.BmclApi, config.DownloadMirror);
        }

        /// <summary>
        /// UUID 格式兼容性：既要接受部分服务器返回的 32 位无连字符形式
        /// （LittleSkin 的 Yggdrasil Connect 实测如此，旧实现会报"UUID 格式无效"），
        /// 又要拒绝畸形数据，不能靠"看到十六进制就拼"来放行。
        /// </summary>
        private static void TestUuidFormatTolerance()
        {
            const string Plain = "0626e58a2c2a4c41b4f6c860998dd1d1";
            const string Dashed = "0626e58a-2c2a-4c41-b4f6-c860998dd1d1";

            // 32 位无连字符（LittleSkin 可能返回的形式）必须接受并补全
            Check("plain uuid accepted", AuthValidation.IsValidUuid(Plain));
            CheckEqual("plain normalized", Dashed, AuthValidation.NormalizeUuid(Plain));

            // 标准带连字符形式保持原样
            Check("dashed uuid accepted", AuthValidation.IsValidUuid(Dashed));
            CheckEqual("dashed normalized", Dashed, AuthValidation.NormalizeUuid(Dashed));

            // 大写转小写
            Check("uppercase accepted",
                AuthValidation.IsValidUuid("0626E58A2C2A4C41B4F6C860998DD1D1"));
            CheckEqual("uppercase normalized to lower", Dashed,
                AuthValidation.NormalizeUuid("0626E58A2C2A4C41B4F6C860998DD1D1"));

            // 首尾空白容忍
            CheckEqual("trimmed", Dashed,
                AuthValidation.NormalizeUuid("  " + Dashed + "  "));

            // 畸形输入必须拒绝（不能为了兼容而放松校验）
            Check("trailing dash rejected",
                !AuthValidation.IsValidUuid(Dashed + "-"));
            Check("leading dash rejected",
                !AuthValidation.IsValidUuid("-" + Dashed));
            Check("too short rejected",
                !AuthValidation.IsValidUuid("0626e58a2c2a4c41b4f6c860998dd1d"));
            Check("too long rejected",
                !AuthValidation.IsValidUuid(Plain + "0"));
            Check("underscore separator rejected",
                !AuthValidation.IsValidUuid("0626e58a_2c2a_4c41_b4f6_c860998dd1d1"));
            Check("non hex rejected",
                !AuthValidation.IsValidUuid("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"));
            Check("garbage rejected", !AuthValidation.IsValidUuid("not-a-uuid"));
            Check("empty rejected", !AuthValidation.IsValidUuid(""));
            Check("null rejected", !AuthValidation.IsValidUuid(null));

            // 规范化必须幂等：再次规范化不应改变结果
            CheckEqual("normalize idempotent", Dashed,
                AuthValidation.NormalizeUuid(AuthValidation.NormalizeUuid(Plain)));

            // 解析认证响应时也应把无连字符 UUID 规范化为标准形式
            string json = "{\"accessToken\":\"tok\",\"clientToken\":\"cli\"," +
                          "\"selectedProfile\":{\"id\":\"" + Plain + "\",\"name\":\"Steve\"}}";
            YggdrasilResult parsed = YggdrasilClient.ParseAuthenticateResponse(json);
            Check("plain uuid response succeeds", parsed.Success);
            CheckEqual("response uuid normalized", Dashed, parsed.Uuid);
        }

        /// <summary>
        /// 推荐清单必须包含联机模组且置顶。
        /// 原版「对局域网开放」没有正版验证开关，mcwifipnp 才提供；
        /// 若推荐列表漏了它，离线联机用户找不到解决办法。
        /// </summary>
        private static void TestRecommendedIncludesLanMod()
        {
            List<RecommendedMod> list = RecommendedMods.GetAll();
            Check("recommended non-empty", list.Count > 0);

            // 第一项必须是联机模组（用户最容易卡在这里）
            CheckEqual("lan mod is first", RecommendedMods.LanModSlug, list[0].Slug);
            Check("lan mod marked common", list[0].Common);
            Check("lan mod title readable",
                list[0].Title.IndexOf("联机", StringComparison.Ordinal) >= 0);
            Check("lan mod description mentions online mode",
                list[0].Description.IndexOf("正版验证", StringComparison.Ordinal) >= 0);
            CheckEqual("lan mod slug matches detection id",
                "mcwifipnp", RecommendedMods.LanModSlug);

            // 清单里不能有重复 slug（重复会在界面上出现两行）
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
            {
                Check("slug unique: " + list[i].Slug, seen.Add(list[i].Slug));
                Check("slug lowercase: " + list[i].Slug,
                    list[i].Slug == list[i].Slug.ToLowerInvariant());
                Check("title non-empty: " + list[i].Slug, list[i].Title.Length > 0);
                Check("desc non-empty: " + list[i].Slug, list[i].Description.Length > 0);
            }

            // 仍应保留原有的优化类模组（不能为加联机模组而删掉别的）
            Check("sodium still recommended", seen.Contains("sodium"));
            Check("modmenu still recommended", seen.Contains("modmenu"));
        }

        /// <summary>
        /// 按 fabric.mod.json 的 id 判断某个模组是否已安装。
        /// 用途：离线联机"无效会话"的提示要区分"装了 mcwifipnp（有正版验证开关）"
        /// 与"原版没有该开关"，否则会让用户去找不存在的选项。
        /// </summary>
        private static void TestModInstalledDetection()
        {
            string dir = Path.Combine(_root, "mods-detect");
            Directory.CreateDirectory(dir);

            // 造一个带 fabric.mod.json 的假 mod
            CreateFakeModJar(Path.Combine(dir, "fake-lan-mod.jar"),
                "{\"schemaVersion\":1,\"id\":\"mcwifipnp\",\"version\":\"2.1.4\"}");
            Check("installed mod detected",
                ModDependencyScanner.IsModInstalled(dir, "mcwifipnp"));
            // id 比较应忽略大小写
            Check("case insensitive",
                ModDependencyScanner.IsModInstalled(dir, "McWiFiPnP"));
            Check("other mod not detected",
                !ModDependencyScanner.IsModInstalled(dir, "sodium"));

            // 没有 fabric.mod.json 的 jar（如旧版 Forge mod）不应让检测崩溃
            CreateFakeModJar(Path.Combine(dir, "no-meta.jar"), null);
            Check("jar without meta tolerated",
                ModDependencyScanner.IsModInstalled(dir, "mcwifipnp"));

            // 损坏的 jar 也不应影响其它文件的判断
            File.WriteAllText(Path.Combine(dir, "broken.jar"), "this is not a zip");
            Check("broken jar tolerated",
                ModDependencyScanner.IsModInstalled(dir, "mcwifipnp"));

            // 目录不存在 / 参数为空
            Check("missing dir safe",
                !ModDependencyScanner.IsModInstalled(Path.Combine(_root, "no-mods-dir"), "mcwifipnp"));
            Check("empty id safe", !ModDependencyScanner.IsModInstalled(dir, ""));
            Check("empty dir safe", !ModDependencyScanner.IsModInstalled("", "mcwifipnp"));
        }

        /// <summary>造一个最小可用的假 mod jar；metaJson 为 null 时不写入 fabric.mod.json。</summary>
        private static void CreateFakeModJar(string path, string metaJson)
        {
            using (System.IO.Compression.ZipArchive archive =
                System.IO.Compression.ZipFile.Open(path,
                    System.IO.Compression.ZipArchiveMode.Create))
            {
                if (metaJson != null)
                {
                    System.IO.Compression.ZipArchiveEntry entry =
                        archive.CreateEntry("fabric.mod.json");
                    using (Stream stream = entry.Open())
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(metaJson);
                    }
                }
                // 带一个无关文件，模拟真实 jar 结构
                System.IO.Compression.ZipArchiveEntry other = archive.CreateEntry("readme.txt");
                using (Stream stream = other.Open())
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write("fake");
                }
            }
        }

        /// <summary>
        /// 模组与版本隔离的双向同步：
        /// 先装模组再开隔离（正向）要能带过去；隔离中新装模组再关隔离（反向）
        /// 也要能补回共享目录，否则那些模组会"消失"。
        /// </summary>
        private static void TestBidirectionalModSync()
        {
            string gameDir = Path.Combine(_root, "bi-sync");
            Directory.CreateDirectory(gameDir);
            string versionId = "fabric-1.19.2";
            Directory.CreateDirectory(Path.Combine(gameDir, "versions", versionId));

            string sharedMods = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(sharedMods);
            CreateFakeModJar(Path.Combine(sharedMods, "a.jar"),
                "{\"schemaVersion\":1,\"id\":\"moda\",\"version\":\"1\"}");
            CreateFakeModJar(Path.Combine(sharedMods, "b.jar"),
                "{\"schemaVersion\":1,\"id\":\"modb\",\"version\":\"1\"}");

            // 正向：开隔离后实例目录获得共享目录的模组
            InstancePaths.EnsureInstanceDirectory(gameDir, versionId);
            int forward = InstancePaths.SeedInstanceMods(gameDir, versionId);
            string instanceMods = InstancePaths.GetModsDirectory(gameDir, versionId, true);
            Check("forward copied 2", forward == 2);
            Check("instance has 2 jars",
                Directory.GetFiles(instanceMods, "*.jar").Length == 2);
            Check("shared still has 2",
                Directory.GetFiles(sharedMods, "*.jar").Length == 2);

            // 隔离期间又装了一个模组（只落在实例目录）
            CreateFakeModJar(Path.Combine(instanceMods, "c.jar"),
                "{\"schemaVersion\":1,\"id\":\"modc\",\"version\":\"1\"}");
            Check("instance now 3",
                Directory.GetFiles(instanceMods, "*.jar").Length == 3);
            Check("shared still 2 before reverse",
                Directory.GetFiles(sharedMods, "*.jar").Length == 2);

            // 反向：关闭隔离时补回共享目录
            List<string> instances = InstancePaths.ListInstanceVersions(gameDir);
            Check("instance versions listed", instances.Count >= 1);
            int reverse = InstancePaths.SeedSharedModsFromInstance(gameDir, versionId);
            Check("reverse copied 1", reverse == 1);
            Check("shared now 3",
                Directory.GetFiles(sharedMods, "*.jar").Length == 3);
            // 关键：隔离中装的模组在关闭隔离后必须仍然可见
            Check("mod from instance visible in shared",
                File.Exists(Path.Combine(sharedMods, "c.jar")));

            // 反向幂等
            Check("reverse idempotent",
                InstancePaths.SeedSharedModsFromInstance(gameDir, versionId) == 0);
            Check("shared stays 3",
                Directory.GetFiles(sharedMods, "*.jar").Length == 3);

            // 关闭隔离后解析出的目录应指向共享目录
            CheckEqual("isolate off points to shared",
                Path.Combine(Path.GetFullPath(gameDir), "mods"),
                InstancePaths.GetModsDirectory(gameDir, versionId, false));

            // 无实例目录时 ListInstanceVersions 安全返回
            string bareDir = Path.Combine(_root, "bi-bare");
            Directory.CreateDirectory(bareDir);
            Check("no instances safe",
                InstancePaths.ListInstanceVersions(bareDir).Count == 0);
            Check("reverse missing instance safe",
                InstancePaths.SeedSharedModsFromInstance(bareDir, "nope") == 0);
        }

        /// <summary>
        /// 开启版本隔离时，共享 mods 目录里的模组必须被补进实例目录。
        /// 否则用户先在共享目录装好模组、再打开隔离，游戏读空的 instance\mods，
        /// 表现为"开了隔离就加载不到 mod"。
        /// </summary>
        private static void TestInstanceModsSeeding()
        {
            string gameDir = Path.Combine(_root, "seed-iso");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());

            string sharedMods = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(sharedMods);
            // 造两个"模组"文件
            CreateFakeModJar(Path.Combine(sharedMods, "mod-a.jar"),
                "{\"schemaVersion\":1,\"id\":\"moda\",\"version\":\"1\"}");
            CreateFakeModJar(Path.Combine(sharedMods, "mod-b.jar"),
                "{\"schemaVersion\":1,\"id\":\"modb\",\"version\":\"1\"}");

            string instanceMods = InstancePaths.GetModsDirectory(gameDir, "1.20.1", true);
            Check("instance mods initially empty", !Directory.Exists(instanceMods) ||
                Directory.GetFiles(instanceMods, "*.jar").Length == 0);

            int copied = InstancePaths.SeedInstanceMods(gameDir, "1.20.1");
            Check("seeded two files", copied == 2);
            Check("instance now has jars",
                Directory.GetFiles(instanceMods, "*.jar").Length == 2);

            // 原文件必须留在共享目录（只复制不移动）
            Check("shared jars untouched",
                Directory.GetFiles(sharedMods, "*.jar").Length == 2);

            // 幂等：再执行一次不应重复复制
            int again = InstancePaths.SeedInstanceMods(gameDir, "1.20.1");
            Check("seeding idempotent", again == 0);
            Check("still two jars", Directory.GetFiles(instanceMods, "*.jar").Length == 2);

            // 实例里已有同名但不同的文件时不应被覆盖
            string existing = Path.Combine(instanceMods, "mod-a.jar");
            long before = new FileInfo(existing).Length;
            using (FileStream fs = new FileStream(existing, FileMode.Open, FileAccess.Write))
            {
                fs.SetLength(before + 10);
            }
            InstancePaths.SeedInstanceMods(gameDir, "1.20.1");
            Check("existing instance file not overwritten",
                new FileInfo(existing).Length == before + 10);

            // 共享目录不存在时安全返回 0
            string emptyGameDir = Path.Combine(_root, "seed-empty");
            Directory.CreateDirectory(emptyGameDir);
            Check("missing shared mods safe", InstancePaths.SeedInstanceMods(emptyGameDir, "1.20.1") == 0);
            Check("null args safe", InstancePaths.SeedInstanceMods("", "1.20.1") == 0);
            Check("empty version safe", InstancePaths.SeedInstanceMods(emptyGameDir, "") == 0);
        }

        /// <summary>
        /// Java 自动下载：白名单必须放行 Adoptium API 与 github 资产，
        /// 且「需要的 Java 版本」要能映射到官方确实提供的版本。
        /// 实测：api.adoptium.net 可用，下载走 github（已在白名单），
        /// JRE 约 47MB，装好后能被 JavaFinder 识别。
        /// </summary>
        private static void TestJavaInstaller()
        {
            // Adoptium API 必须在白名单内，否则"自动下载 Java"根本发不出请求
            Check("adoptium api allowed",
                HttpUtil.IsAllowedUrl("https://api.adoptium.net/v3/info/available_releases"));
            Check("adoptium assets api allowed", HttpUtil.IsAllowedUrl(
                "https://api.adoptium.net/v3/assets/latest/21/hotspot" +
                "?architecture=x64&image_type=jre&os=windows&vendor=eclipse"));
            // 实际下载文件由 github 分发，也必须在白名单内
            Check("github jre download allowed", HttpUtil.IsAllowedUrl(
                "https://github.com/adoptium/temurin21-binaries/releases/download/x/jre.zip"));
            Check("release-assets jre allowed", HttpUtil.IsAllowedUrl(
                "https://release-assets.githubusercontent.com/github-production-release-asset/x"));

            // 相邻但不在白名单的主机仍必须被拒
            Check("fake adoptium rejected",
                !HttpUtil.IsAllowedUrl("https://api.adoptium.net.evil.com/v3/info"));
            Check("http adoptium rejected",
                !HttpUtil.IsAllowedUrl("http://api.adoptium.net/v3/info"));

            // 版本映射：官方只提供 LTS 等少数版本，必须就近取
            CheckEqual("java 8 maps to 8", "8",
                JavaInstaller.MapToDownloadable(8).ToString());
            CheckEqual("java 16 maps up to 17", "17",
                JavaInstaller.MapToDownloadable(16).ToString());
            CheckEqual("java 17 stays 17", "17",
                JavaInstaller.MapToDownloadable(17).ToString());
            CheckEqual("java 21 stays 21", "21",
                JavaInstaller.MapToDownloadable(21).ToString());
            CheckEqual("future maps to 25", "25",
                JavaInstaller.MapToDownloadable(30).ToString());
            // 未指定（0）时给出最保守的 Java 8
            CheckEqual("unknown maps to 8", "8",
                JavaInstaller.MapToDownloadable(0).ToString());

            // 在目录里找 java 可执行文件：不存在的目录安全返回空
            Check("missing dir no exe", JavaInstaller.FindJavaExecutable(
                Path.Combine(_root, "no-java-here")) == "");
            Check("empty path safe", JavaInstaller.FindJavaExecutable("") == "");
        }

        /// <summary>
        /// 模组管理：列出 / 禁用 / 启用 / 删除。
        /// 禁用用改名（.jar.disabled）实现，Fabric 只加载 .jar，因此不会生效；
        /// 删除必须限制在 mods 目录内的 jar，防止越界删除用户其它文件。
        /// </summary>
        private static void TestModManager()
        {
            string dir = Path.Combine(_root, "man-mods");
            Directory.CreateDirectory(dir);
            CreateFakeModJar(Path.Combine(dir, "alpha.jar"),
                "{\"schemaVersion\":1,\"id\":\"alpha\",\"name\":\"Alpha Mod\",\"version\":\"1.2.3\"}");
            CreateFakeModJar(Path.Combine(dir, "beta.jar"),
                "{\"schemaVersion\":1,\"id\":\"beta\",\"version\":\"2.0\"}");

            // 列出：读得到 id / 版本 / 显示名，且默认都是启用
            List<InstalledMod> mods = ModManager.List(dir);
            Check("lists two mods", mods.Count == 2);
            Check("all enabled initially", mods[0].Enabled && mods[1].Enabled);
            InstalledMod alpha = null;
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].ModId == "alpha") { alpha = mods[i]; }
            }
            Check("alpha found", alpha != null);
            if (alpha != null)
            {
                CheckEqual("alpha version", "1.2.3", alpha.Version);
                CheckEqual("alpha display name", "Alpha Mod", alpha.DisplayName);
                Check("alpha has size", alpha.Size > 0);
            }

            // 禁用：文件改名，列表里标记为禁用
            string disabledPath = ModManager.Disable(alpha.FilePath);
            Check("disable renames file",
                disabledPath.EndsWith(ModManager.DisabledSuffix, StringComparison.OrdinalIgnoreCase));
            Check("original jar gone", !File.Exists(alpha.FilePath));
            Check("disabled file exists", File.Exists(disabledPath));
            List<InstalledMod> afterDisable = ModManager.List(dir);
            Check("still lists two", afterDisable.Count == 2);
            bool foundDisabled = false;
            for (int i = 0; i < afterDisable.Count; i++)
            {
                if (afterDisable[i].ModId == "alpha" && !afterDisable[i].Enabled)
                {
                    foundDisabled = true;
                }
            }
            Check("alpha shows disabled", foundDisabled);

            // 启用：改回 .jar
            string enabledPath = ModManager.Enable(disabledPath);
            Check("enable restores jar", enabledPath.EndsWith(".jar", StringComparison.Ordinal));
            Check("restored file exists", File.Exists(enabledPath));

            // 幂等：对已启用文件调 Disable 之外的重复调用不应出错
            Check("enable twice safe", ModManager.Enable(enabledPath) == enabledPath);

            // 删除：允许删除 mods 目录内的 jar
            ModManager.Delete(dir, enabledPath);
            Check("delete removes file", !File.Exists(enabledPath));
            Check("beta still there", File.Exists(Path.Combine(dir, "beta.jar")));

            // 安全边界：拒绝删除 mods 目录之外的文件
            string outside = Path.Combine(_root, "outside.txt");
            File.WriteAllText(outside, "x");
            bool blocked = false;
            try
            {
                ModManager.Delete(dir, outside);
            }
            catch (InvalidOperationException)
            {
                blocked = true;
            }
            Check("refuses to delete outside mods", blocked);
            Check("outside file intact", File.Exists(outside));

            // 安全边界：拒绝删除非 jar 扩展名
            string notJar = Path.Combine(dir, "notes.txt");
            File.WriteAllText(notJar, "x");
            bool blockedNonJar = false;
            try
            {
                ModManager.Delete(dir, notJar);
            }
            catch (InvalidOperationException)
            {
                blockedNonJar = true;
            }
            Check("refuses non-jar", blockedNonJar);

            // 目录不存在时安全返回空
            Check("missing dir safe",
                ModManager.List(Path.Combine(_root, "no-mods-here")).Count == 0);
        }

        /// <summary>
        /// 重复模组检测：同一个 mod id 装了两份会让 Fabric 直接加载失败。
        /// 实测用户机器上 mcwifipnp 同时存在英文名与中文方括号名两份，
        /// 这正是"模组无法正常加载"的直接原因。
        /// </summary>
        private static void TestDuplicateModDetection()
        {
            string dir = Path.Combine(_root, "mods-dup");
            Directory.CreateDirectory(dir);

            // 同一 mod id 两份（文件名不同，模拟真实情况）
            CreateFakeModJar(Path.Combine(dir, "mcwifipnp-2.1.4.jar"),
                "{\"schemaVersion\":1,\"id\":\"mcwifipnp\",\"version\":\"2.1.4\"}");
            CreateFakeModJar(Path.Combine(dir, "[联机设置] mcwifipnp-2.1.4.jar"),
                "{\"schemaVersion\":1,\"id\":\"mcwifipnp\",\"version\":\"2.1.4\"}");
            // 另一个正常模组（只有一份）
            CreateFakeModJar(Path.Combine(dir, "sodium.jar"),
                "{\"schemaVersion\":1,\"id\":\"sodium\",\"version\":\"1\"}");

            List<DuplicateModGroup> groups = ModDuplicates.Find(dir);
            Check("one duplicate group", groups.Count == 1);
            CheckEqual("duplicate id", "mcwifipnp", groups[0].ModId);
            Check("group has two files", groups[0].Files.Count == 2);

            // 建议保留英文名的原始文件，而不是中文前缀副本
            string keep = ModDuplicates.PickKeepFile(groups[0].Files);
            CheckEqual("keep english filename",
                "mcwifipnp-2.1.4.jar", Path.GetFileName(keep));

            // id 读取
            CheckEqual("read sodium id", "sodium",
                ModDuplicates.ReadModId(Path.Combine(dir, "sodium.jar")));
            // 非 jar / 不存在文件安全返回空
            Check("missing jar id empty", ModDuplicates.ReadModId(
                Path.Combine(dir, "nope.jar")) == "");
            // 无 fabric.mod.json 的 jar 返回空
            CreateFakeModJar(Path.Combine(dir, "no-meta.jar"), null);
            Check("jar without meta ignored", ModDuplicates.ReadModId(
                Path.Combine(dir, "no-meta.jar")) == "");

            // 没有重复时返回空列表
            string cleanDir = Path.Combine(_root, "mods-clean");
            Directory.CreateDirectory(cleanDir);
            CreateFakeModJar(Path.Combine(cleanDir, "only.jar"),
                "{\"schemaVersion\":1,\"id\":\"only\",\"version\":\"1\"}");
            Check("no duplicates found", ModDuplicates.Find(cleanDir).Count == 0);

            // 目录不存在安全返回空
            Check("missing dir safe",
                ModDuplicates.Find(Path.Combine(_root, "no-such-mods")).Count == 0);
        }

        /// <summary>
        /// 版本隔离下的 mods 目录必须与游戏实际读取的目录一致。
        /// 这是"模组装了却不生效"的根因：游戏 --gameDir 指向实例目录，
        /// 只读 instance\mods；旧代码始终用 &lt;GameDir&gt;\mods。
        /// </summary>
        private static void TestModsDirectoryWithIsolation()
        {
            string gameDir = Path.Combine(_root, "mods-iso");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());

            // 关闭隔离：共享 mods 目录。
            string shared = InstancePaths.GetModsDirectory(gameDir, "1.20.1", false);
            CheckEqual("shared mods dir", Path.Combine(Path.GetFullPath(gameDir), "mods"), shared);

            // 开启隔离：必须落在实例目录内。
            string isolated = InstancePaths.GetModsDirectory(gameDir, "1.20.1", true);
            Check("isolated mods inside instance",
                isolated.IndexOf(InstancePaths.InstanceFolderName, StringComparison.OrdinalIgnoreCase) >= 0);
            Check("isolated mods under version dir",
                PathUtil.IsSubPath(Path.Combine(gameDir, "versions", "1.20.1"), isolated));
            CheckEqual("isolated mods folder name", "mods", Path.GetFileName(isolated));

            // 两者必须是不同目录（否则隔离形同虚设）。
            Check("isolated differs from shared", !string.Equals(
                isolated, shared, StringComparison.OrdinalIgnoreCase));

            // 开启隔离但版本名为空（例如还没选版本）：退回共享目录，不抛异常。
            string noVersion = InstancePaths.GetModsDirectory(gameDir, "", true);
            CheckEqual("empty version falls back", shared, noVersion);

            // 非法版本名也退回共享目录，避免无法操作模组。
            string badVersion = InstancePaths.GetModsDirectory(gameDir, "../evil", true);
            CheckEqual("invalid version falls back", shared, badVersion);

            // 隔离目录与 BuildPlan 的 game_directory 必须同源：
            // 装到哪，游戏就从哪读。
            string instance = InstancePaths.GetInstanceDirectory(gameDir, "1.20.1");
            Check("mods dir is directly inside instance",
                Path.GetDirectoryName(isolated).Equals(instance,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 版本隔离：实例目录路径正确、创建子目录、隔离开关影响 --gameDir。
        /// </summary>
        private static void TestVersionIsolation()
        {
            string gameDir = Path.Combine(_root, "isolate");
            Directory.CreateDirectory(gameDir);
            WriteVersion(gameDir, "1.20.1", ParentVersionJson());

            string instance = InstancePaths.GetInstanceDirectory(gameDir, "1.20.1");
            Check("instance path under version dir",
                PathUtil.IsSubPath(Path.Combine(gameDir, "versions", "1.20.1"), instance));
            CheckEqual("instance folder name", InstancePaths.InstanceFolderName,
                Path.GetFileName(instance));

            // 创建实例目录并确认关键子目录存在。
            string created = InstancePaths.EnsureInstanceDirectory(gameDir, "1.20.1");
            Check("instance created", Directory.Exists(created));
            Check("instance saves", Directory.Exists(Path.Combine(created, "saves")));
            Check("instance mods", Directory.Exists(Path.Combine(created, "mods")));
            Check("instance exists flag", InstancePaths.Exists(gameDir, "1.20.1"));

            // 非法版本名必须拒绝（防止路径穿越）。
            CheckThrows<ArgumentException>("invalid version rejected", delegate
            {
                InstancePaths.GetInstanceDirectory(gameDir, "../evil");
            });

            // BuildPlan：隔离开时 --gameDir 指向实例目录；关闭时指向游戏目录。
            EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");
            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.Major = 17;

            LaunchOptions isolated = new LaunchOptions();
            isolated.GameDir = gameDir;
            isolated.VersionId = "1.20.1";
            isolated.Username = "Player";
            isolated.JavaPath = java.LaunchPath;
            isolated.MinMemoryMB = 1024;
            isolated.MaxMemoryMB = 2048;
            isolated.IsolateVersions = true;
            LaunchPlan planIsolated = GameLauncher.BuildPlan(version, isolated, java, gameDir);
            Check("isolated plan uses instance dir",
                planIsolated.Arguments.IndexOf(instance, StringComparison.Ordinal) >= 0);

            LaunchOptions shared = new LaunchOptions();
            shared.GameDir = gameDir;
            shared.VersionId = "1.20.1";
            shared.Username = "Player";
            shared.JavaPath = java.LaunchPath;
            shared.MinMemoryMB = 1024;
            shared.MaxMemoryMB = 2048;
            shared.IsolateVersions = false;
            LaunchPlan planShared = GameLauncher.BuildPlan(version, shared, java, gameDir);
            Check("shared plan uses game dir",
                planShared.Arguments.IndexOf(instance, StringComparison.Ordinal) < 0);
            Check("shared plan mentions game dir",
                planShared.Arguments.IndexOf(Path.GetFullPath(gameDir), StringComparison.Ordinal) >= 0);

            // 配置默认关闭隔离。
            LauncherConfig config = new LauncherConfig();
            Check("isolation default off", !config.IsolateVersions);
        }

        /// <summary>
        /// 模组冲突/依赖扫描：冲突命中、依赖满足、依赖缺失三种分支。
        /// </summary>
        private static void TestModDependencyScanner()
        {
            string modsDir = Path.Combine(_root, "scanner-mods");
            Directory.CreateDirectory(modsDir);
            File.WriteAllText(Path.Combine(modsDir, "sodium-fabric-0.5.13.jar"), "x");
            File.WriteAllText(Path.Combine(modsDir, "fabric-api-0.92.12.jar"), "x");

            // 1) 安装已存在的项目 → 冲突。
            List<ModCheckItem> conflict = ModDependencyScanner.Scan(
                modsDir, null, "sodium", null, null);
            Check("conflict detected", conflict.Count == 1);
            Check("conflict kind", conflict[0].Kind.IndexOf("冲突", StringComparison.Ordinal) >= 0);
            Check("conflict mentions file",
                conflict[0].Detail.IndexOf("sodium-fabric", StringComparison.Ordinal) >= 0);

            // 2) 新项目 + 依赖已满足 → 无结论。
            List<string> reqs = new List<string>();
            reqs.Add("fabric-api");
            List<ModCheckItem> ok = ModDependencyScanner.Scan(
                modsDir, null, "jade", null, reqs);
            Check("no findings when satisfied", ok.Count == 0);

            // 3) 依赖缺失 → 缺失依赖。
            List<string> missing = new List<string>();
            missing.Add("cloth-config");
            List<ModCheckItem> findings = ModDependencyScanner.Scan(
                modsDir, null, "appleskin", null, missing);
            Check("missing dependency detected", findings.Count == 1);
            Check("missing kind", findings[0].Kind.IndexOf("缺少依赖", StringComparison.Ordinal) >= 0);
            Check("missing names dependency",
                findings[0].Title == "cloth-config");

            // 4) 依赖在"本次待装清单"里 → 视为满足。
            List<string> pending = new List<string>();
            pending.Add("cloth-config-11.1.136.jar");
            List<ModCheckItem> satisfied = ModDependencyScanner.Scan(
                modsDir, pending, "appleskin", null, missing);
            Check("pending satisfies dependency", satisfied.Count == 0);

            // 5) 格式化输出包含结论与询问。
            string text = ModDependencyScanner.Format(findings);
            Check("format mentions kind", text.IndexOf("缺少依赖", StringComparison.Ordinal) >= 0);
            Check("format asks to continue", text.IndexOf("继续安装", StringComparison.Ordinal) >= 0);

            // 6) 空目录 + 无依赖 → 无结论。
            List<ModCheckItem> empty = ModDependencyScanner.Scan(
                Path.Combine(_root, "no-mods-dir"), null, "sodium", null, null);
            Check("missing mods dir safe", empty.Count == 0);
        }

        /// <summary>1.5.0：NodeMonitor 节点地址解析的正反例。</summary>
        private static void TestNodeMonitorParse()
        {
            string host;
            int port;
            Check("parse tcp", NodeMonitor.TryParseNode("tcp://easytier.weiai.org.cn:11010", out host, out port)
                && host == "easytier.weiai.org.cn" && port == 11010);
            Check("parse bare host default port", NodeMonitor.TryParseNode("1.2.3.4", out host, out port)
                && host == "1.2.3.4" && port == 11010);
            Check("parse host port", NodeMonitor.TryParseNode("host.example:9", out host, out port)
                && host == "host.example" && port == 9);
            // F4：IPv6 解析（1.5.0 修复）——方括号形式（带/不带端口）、无端口字面量。
            Check("parse bracket ipv6 with port", NodeMonitor.TryParseNode("[2001:db8::1]:11010", out host, out port)
                && host == "2001:db8::1" && port == 11010);
            Check("parse bracket ipv6 default port", NodeMonitor.TryParseNode("[::1]", out host, out port)
                && host == "::1" && port == 11010);
            Check("parse scheme bracket ipv6", NodeMonitor.TryParseNode("tcp://[2001:db8::1]:8080", out host, out port)
                && host == "2001:db8::1" && port == 8080);
            Check("parse bare ipv6 default port", NodeMonitor.TryParseNode("2001:db8::1", out host, out port)
                && host == "2001:db8::1" && port == 11010);
            Check("reject empty", !NodeMonitor.TryParseNode("", out host, out port));
            Check("reject bad port", !NodeMonitor.TryParseNode("host:abc", out host, out port));
            Check("reject bad char", !NodeMonitor.TryParseNode("bad host:11010", out host, out port));
            Check("reject out of range port", !NodeMonitor.TryParseNode("host:70000", out host, out port));
            Check("reject trailing colon", !NodeMonitor.TryParseNode("host:", out host, out port));
            Check("reject unclosed bracket", !NodeMonitor.TryParseNode("[::1", out host, out port));
            Check("reject junk after bracket", !NodeMonitor.TryParseNode("[::1]abc", out host, out port));
            Check("reject bracket port range", !NodeMonitor.TryParseNode("[::1]:70000", out host, out port));
        }

        /// <summary>1.5.0：SpaceManager 字节格式化。</summary>
        private static void TestSpaceManagerFormat()
        {
            CheckEqual("format b", SpaceManager.FormatBytes(512), "512 B");
            CheckEqual("format kb", SpaceManager.FormatBytes(2048), "2 KB");
            CheckEqual("format mb", SpaceManager.FormatBytes(5 * 1024 * 1024), "5 MB");
            CheckEqual("format negative", SpaceManager.FormatBytes(-1), "未知");
        }

        /// <summary>
        /// 1.4.8/1.5.0 回归：latest.log 崩溃信息在尾部时必须能被分析出来。
        /// 旧实现只读前 4000 行，多模组下永远报"未发现错误模式"。
        /// </summary>
        private static void TestCrashAnalyzerTailRead()
        {
            string gameDir = Path.Combine(_root, "crash-tail", ".minecraft");
            Directory.CreateDirectory(Path.Combine(gameDir, "logs"));
            string log = Path.Combine(gameDir, "logs", "latest.log");
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < 5900; i++)
            {
                builder.Append("[").Append(i).Append("] [Render thread/INFO]: normal boot line\r\n");
            }
            builder.Append("java.lang.OutOfMemoryError: Java heap space\r\n");
            File.WriteAllText(log, builder.ToString(), new UTF8Encoding(false));

            CrashReport report = CrashAnalyzer.Analyze(gameDir);
            Check("tail log analyzed", report.HasGameLog);
            bool found = false;
            for (int i = 0; i < report.Findings.Count; i++)
            {
                if (report.Findings[i].Title == "显存或内存不足")
                {
                    found = true;
                }
            }
            Check("tail crash detected", found);

            // 超过 8MB 的日志同样必须可分析（旧实现直接放弃）。
            string bigLog = Path.Combine(gameDir, "logs", "big.log");
            Directory.CreateDirectory(Path.Combine(_root, "crash-tail-big", ".minecraft", "logs"));
            string bigGameDir = Path.Combine(_root, "crash-tail-big", ".minecraft");
            string bigFile = Path.Combine(bigGameDir, "logs", "latest.log");
            using (FileStream stream = new FileStream(bigFile, FileMode.Create, FileAccess.Write))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                string filler = "[x] [Render thread/INFO]: " + new string('a', 180) + "\r\n";
                // 约 190 字节/行 × 45000 行 ≈ 8.6MB
                for (int i = 0; i < 45000; i++)
                {
                    writer.Write(filler);
                }
                writer.Write("java.lang.OutOfMemoryError: Java heap space\r\n");
            }
            CrashReport bigReport = CrashAnalyzer.Analyze(bigGameDir);
            Check("oversize log analyzed", bigReport.HasGameLog);
            bool bigFound = false;
            for (int i = 0; i < bigReport.Findings.Count; i++)
            {
                if (bigReport.Findings[i].Title == "显存或内存不足")
                {
                    bigFound = true;
                }
            }
            Check("oversize crash detected", bigFound);
        }

        /// <summary>F8：崩溃结论按严重度排序（Error 在前）+ 同名去重 + 最多 5 条。
        /// 日志中 Warning 先于 Error 出现时，最终 Error 仍应在最前。</summary>
        private static void TestCrashSeverityOrder()
        {
            string gameDir = Path.Combine(_root, "crash-severity", ".minecraft");
            Directory.CreateDirectory(Path.Combine(gameDir, "logs"));
            string log = Path.Combine(gameDir, "logs", "latest.log");
            // 先写 Warning 信号，再写 Error 信号（扫描顺序相反）。
            File.WriteAllText(log,
                "WARN  Fabric detected duplicate mod signal conflicting entries\r\n" +
                "java.lang.OutOfMemoryError: Java heap space\r\n",
                new UTF8Encoding(false));
            CrashReport report = CrashAnalyzer.Analyze(gameDir);
            Check("severity report has findings", report.Findings.Count > 0);
            // 第一条必须是 Error 级别（排序保证），且 Error 型结论存在。
            bool hasError = false;
            for (int i = 0; i < report.Findings.Count; i++)
            {
                if (string.Equals(report.Findings[i].Severity, "Error", StringComparison.Ordinal))
                {
                    hasError = true;
                }
            }
            Check("severity error present", hasError);
            if (hasError)
            {
                Check("severity error first",
                    string.Equals(report.Findings[0].Severity, "Error", StringComparison.Ordinal));
            }
            // 同名去重 + 5 条上限。
            int duplicates = 0;
            for (int i = 0; i < report.Findings.Count; i++)
            {
                for (int j = i + 1; j < report.Findings.Count; j++)
                {
                    if (string.Equals(report.Findings[i].Title, report.Findings[j].Title, StringComparison.Ordinal))
                    {
                        duplicates++;
                    }
                }
            }
            Check("severity no duplicate titles", duplicates == 0);
            Check("severity capped at five", report.Findings.Count <= 5);
        }

        private static void TestArgFileRoundtrip()
        {
            string[] samples = new string[]
            {
                "plain",
                "has space",
                "C:\\Program Files\\Java\\bin\\javaw.exe",
                "-Duser.dir=\"C:\\My Games\"",
                "arg\"with\"quotes",
                "中文参数测试",
                ""
            };
            for (int i = 0; i < samples.Length; i++)
            {
                string encoded = CommandLine.EncodeArgFileLine(samples[i]);
                string decoded = CommandLine.DecodeArgFileLine(encoded);
                CheckEqual("argfile roundtrip " + i, samples[i], decoded);
            }

            string content;

            // JDK 9–17：按平台默认编码（中文 Windows 为 GBK）写入/读取。
            List<string> args = new List<string>(samples);
            string path17 = CommandLine.WriteArgFile(args, _root, 17, out content);
            Check("argfile exists (jdk17)", File.Exists(path17));
            string[] lines17 = File.ReadAllLines(path17, CommandLine.GetArgFileEncoding(17));
            Check("argfile line count (jdk17)", lines17.Length == samples.Length);
            for (int i = 0; i < lines17.Length; i++)
            {
                CheckEqual("argfile line jdk17 " + i, samples[i], CommandLine.DecodeArgFileLine(lines17[i]));
            }

            // JDK 18+：按 UTF-8 写入/读取（JEP 400 默认字符集）。
            string path21 = CommandLine.WriteArgFile(args, _root, 21, out content);
            Check("argfile exists (jdk21)", File.Exists(path21));
            string[] lines21 = File.ReadAllLines(path21, CommandLine.GetArgFileEncoding(21));
            Check("argfile line count (jdk21)", lines21.Length == samples.Length);
            for (int i = 0; i < lines21.Length; i++)
            {
                CheckEqual("argfile line jdk21 " + i, samples[i], CommandLine.DecodeArgFileLine(lines21[i]));
            }

            // 编码决策必须符合 Java 的读取方式：JDK 18+ 一律 UTF-8（正是修复的缺陷），
            // JDK 9–17 走平台编码（Windows 中文环境为 GBK，本测试不对其做跨平台断言）。
            Check("jdk21 uses utf-8", string.Equals(CommandLine.GetArgFileEncoding(21).WebName, "utf-8", StringComparison.OrdinalIgnoreCase));

            try { PathUtil.TryDeleteFile(path17); } catch { }
            try { PathUtil.TryDeleteFile(path21); } catch { }
        }

        private static void TestArgFileDecision()
        {
            Check("long + java17", CommandLine.ShouldUseArgFile(30001, 17));
            Check("long + java8", !CommandLine.ShouldUseArgFile(30001, 8));
            Check("short + java17", !CommandLine.ShouldUseArgFile(29999, 17));
            Check("hard limit", CommandLine.HardCommandLineLimit == 32700);
            Check("max threshold", CommandLine.MaxCommandLineLength == 30000);
        }

        private static void TestHttpAllowlistRuntime()
        {
            Check("github builtin", HttpUtil.IsAllowedUrl(
                "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar"));
            Check("objects builtin", HttpUtil.IsAllowedUrl(
                "https://objects.githubusercontent.com/github-production-release-asset-2e65be/123"));
            Check("custom initially blocked", !HttpUtil.IsAllowedUrl("https://auth.example.com/api/yggdrasil"));
            HttpUtil.AddAllowedHost("auth.example.com");
            Check("custom allowed after add", HttpUtil.IsAllowedUrl("https://auth.example.com/api/yggdrasil"));
            HttpUtil.AddAllowedHost("bad host with space");
            Check("bad host ignored", !HttpUtil.IsAllowedUrl("https://bad%20host.com/"));
            HttpUtil.AddAllowedHost("https://scheme-host.com");
            Check("scheme rejected", !HttpUtil.IsAllowedUrl("https://scheme-host.com/"));
        }

        /// <summary>
        /// GitHub Release 资产的下载链路：github.com 会 302 到 release-assets 主机。
        /// 该主机必须在白名单内，否则"下载联机核心"会报"重定向到不允许的地址"。
        /// </summary>
        private static void TestGithubReleaseHosts()
        {
            // 入口地址
            Check("github release url allowed", HttpUtil.IsAllowedUrl(
                "https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip"));
            // 第一跳：GitHub 当前的 release-assets 主机
            Check("release-assets allowed", HttpUtil.IsAllowedUrl(
                "https://release-assets.githubusercontent.com/github-production-release-asset/698328708/x"));
            // 兼容旧资产所在主机
            Check("legacy objects allowed", HttpUtil.IsAllowedUrl(
                "https://objects.githubusercontent.com/github-production-release-asset-2e65be/123"));

            // 相邻但不在白名单的主机仍必须被拒（避免为修此问题放得太宽）。
            Check("unknown asset host rejected", !HttpUtil.IsAllowedUrl(
                "https://release-assets.example.com/x"));
            Check("similar host rejected", !HttpUtil.IsAllowedUrl(
                "https://release-assets.githubusercontent.com.evil.com/x"));
            // http（非 HTTPS）同样拒绝
            Check("http release-assets rejected", !HttpUtil.IsAllowedUrl(
                "http://release-assets.githubusercontent.com/x"));
        }

        // =====================================================================
        // 1.4.1 安全修复的回归测试
        // =====================================================================

        /// <summary>
        /// @argfile 路径必须加引号。
        ///
        /// 参数文件位于 %APPDATA%\rll 下，而 %APPDATA% 含 Windows 用户名。
        /// 用户名带空格时，未加引号的 "@C:\...\John Smith\...\rll-args-x.txt"
        /// 会被 Windows 按 CommandLineToArgvW 规则拆成多个 token，
        /// Java 拿到截断路径 → "找不到参数文件" → 模组多的用户启动失败。
        /// </summary>
        private static void TestArgFilePathQuoting()
        {
            string dirWithSpace = Path.Combine(_root, "John Smith AppData", "rll");
            Directory.CreateDirectory(dirWithSpace);

            List<string> arguments = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                arguments.Add("-Dprop" + i + "=someFairlyLongValue" + i);
            }
            string content;
            string argFile = CommandLine.WriteArgFile(arguments, dirWithSpace, 21, out content);
            try
            {
                // 这就是 GameLauncher.BuildPlan 里拼 plan.Arguments 的方式。
                string planArguments = "@" + CommandLine.QuoteArgument(argFile);

                // Windows/Java 拆分后必须仍然只有一个 token，且与磁盘上的文件一致。
                List<string> split = CommandLine.SplitArguments(planArguments);
                CheckEqual("argfile stays one token", "1", split.Count.ToString());
                Check("argfile token starts with @", split[0].StartsWith("@"));
                string parsed = split[0].Substring(1);
                CheckEqual("argfile path survives spaces", argFile, parsed);

                // 反证：不加引号时会被拆开（这条断言记录缺陷本身，防止有人"改回去"）。
                List<string> unquoted = CommandLine.SplitArguments("@" + argFile);
                Check("unquoted argfile would be split", unquoted.Count > 1);
            }
            finally
            {
                PathUtil.TryDeleteFile(argFile);
            }
        }

        /// <summary>
        /// Java 压缩包的条目名必须过路径穿越校验。
        ///
        /// 旧实现直接 Path.Combine(tempDir, entry.FullName)，含 ".." 的条目名会把
        /// 文件写到解压目录之外（zip-slip）。这里构造一个恶意 ZIP 验证会被拒绝。
        /// </summary>
        private static void TestJavaZipTraversal()
        {
            string zipPath = Path.Combine(_root, "evil-java.zip");
            string extractDir = Path.Combine(_root, "java-extract");
            Directory.CreateDirectory(extractDir);
            using (ZipArchive archive = ZipFile.Open(
                zipPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../../../pwned.txt");
                using (Stream stream = entry.Open())
                {
                    byte[] data = Encoding.UTF8.GetBytes("escaped");
                    stream.Write(data, 0, data.Length);
                }
            }

            // 复现 JavaInstaller 现在的解压逻辑：先校验路径，再 SafeCombine。
            List<string> written = new List<string>();
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }
                    // 恶意条目在这一步被拒绝。
                    if (!PathUtil.IsSafeRelativePath(entry.FullName))
                    {
                        continue;
                    }
                    string target = PathUtil.SafeCombine(extractDir, entry.FullName);
                    written.Add(target);
                }
            }
            Check("traversal entry not written", written.Count == 0);

            // 反证：旧实现（直接 Path.Combine）确实会越界。
            string unsafeTarget = Path.Combine(extractDir,
                "../../../pwned.txt".Replace('/', Path.DirectorySeparatorChar));
            Check("legacy combine would escape",
                !PathUtil.IsSubPath(extractDir, Path.GetFullPath(unsafeTarget)));

            // 断言穿越路径被 IsSafeRelativePath 拒绝。
            Check("traversal rejected by validator",
                !PathUtil.IsSafeRelativePath("../../../pwned.txt"));
            Check("absolute path rejected by validator",
                !PathUtil.IsSafeRelativePath("C:/Windows/System32/x.dll"));
            // 正常条目仍然通过。
            Check("normal entry accepted",
                PathUtil.IsSafeRelativePath("jdk-21/bin/javaw.exe"));
        }

        /// <summary>
        /// EasyTier 网络密钥绝不能明文进日志。
        ///
        /// 密钥就是该网络的准入凭证；日志文件没有像 config.json 那样收紧 ACL，
        /// 且用户经常把日志贴到 issue/论坛求助，密钥会随之外泄。
        /// </summary>
        private static void TestEasyTierSecretRedaction()
        {
            const string secret = "SuperSecretKey123";
            List<string> args = EasyTierService.BuildArguments(
                "mynet", secret, "tcp://node.example:11010");

            // 原始参数确实含密钥（说明脱敏是有意义的）。
            Check("raw args contain secret",
                CommandLine.BuildArguments(args).Contains(secret));

            // 脱敏后的字符串不含密钥。
            string redacted = EasyTierService.BuildRedactedArguments(args);
            Check("redacted args hide secret", !redacted.Contains(secret));
            Check("redacted keeps secret placeholder", redacted.Contains("****"));
            // 网络名与节点仍然保留，便于排查问题。
            Check("redacted keeps network name", redacted.Contains("mynet"));
            Check("redacted keeps peer node", redacted.Contains("node.example"));

            // 空密钥时不应产生多余的占位符。
            List<string> noSecret = EasyTierService.BuildArguments(
                "mynet", "", "tcp://node.example:11010");
            string plain = EasyTierService.BuildRedactedArguments(noSecret);
            Check("no secret means no placeholder", !plain.Contains("****"));
        }

        /// <summary>
        /// SHA256 格式校验：下载前必须确认上游"确实给了哈希"，
        /// 否则"没有哈希"会被当成"校验通过"。
        /// </summary>
        private static void TestSha256Validation()
        {
            Check("valid sha256 accepted", EasyTierService.IsSha256(
                "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84"));
            Check("uppercase accepted", EasyTierService.IsSha256(
                "27AF91E270E554709B048BD32327FEFD2DFCE5062AE1E8701AF7550C6F525F84"));
            Check("empty rejected", !EasyTierService.IsSha256(""));
            Check("null rejected", !EasyTierService.IsSha256(null));
            Check("too short rejected", !EasyTierService.IsSha256("abc"));
            // 40 位的 SHA1 不是合法的 SHA256（长度不对）。
            Check("sha1 length rejected", !EasyTierService.IsSha256(
                "4CB00C6E2DD0EEE743B414CAE657E4036983F011477270920A78EAF74DC3997"));
            Check("non hex rejected", !EasyTierService.IsSha256(
                "zzaf91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84"));
        }

        /// <summary>
        /// classpath 必须由启动器自己拼装，版本 JSON 不能决定。
        ///
        /// 旧实现：版本 JSON 里出现 "-cp" 时只是记录它并因此【跳过】追加启动器
        /// 拼好的 classpath，等于把 classpath 的决定权交给远端 JSON。
        /// 现在无论 JSON 写没写 -cp，都以启动器的拼装结果为准。
        /// </summary>
        private static void TestLauncherOwnsClasspath()
        {
            EffectiveVersion version = new EffectiveVersion();
            version.Id = "1.20.1";
            version.MainClass = "net.minecraft.client.main.Main";
            version.Type = "release";
            version.AssetsId = "5";

            ArgumentEntry entry = new ArgumentEntry();
            entry.Values.Add("-cp");
            entry.Values.Add("${classpath}");
            version.JvmArguments.Add(entry);

            // 游戏参数（BuildPlan 要求至少有 gameArguments 或 legacy minecraftArguments）。
            ArgumentEntry game = new ArgumentEntry();
            game.Values.Add("--username");
            game.Values.Add("${auth_player_name}");
            version.GameArguments.Add(game);

            LaunchOptions options = new LaunchOptions();
            options.GameDir = Path.Combine(_root, "cp-game");
            options.VersionId = "1.20.1";
            options.Username = "Player";

            JavaInfo java = new JavaInfo();
            java.LaunchPath = Path.Combine(_root, "java", "bin", "javaw.exe");
            java.ProbePath = java.LaunchPath;
            java.Major = 21;

            LaunchPlan plan = GameLauncher.BuildPlan(
                version, options, java, Path.Combine(_root, "cp-game"));

            // 启动器拼出的 classpath 必须出现。
            Check("launcher classpath present", plan.Classpath.Length > 0);
            // JSON 里那个字面量 "-cp ${classpath}" 不得原样出现在最终命令行里，
            // 否则等于同时出现两个 -cp，Java 会取最后一个，行为不确定。
            List<string> split = CommandLine.SplitArguments(plan.Arguments);
            int cpCount = 0;
            for (int i = 0; i < split.Count; i++)
            {
                if (string.Equals(split[i], "-cp", StringComparison.OrdinalIgnoreCase))
                {
                    cpCount++;
                }
            }
            CheckEqual("exactly one -cp in command line", "1", cpCount.ToString());
            // 真正的 classpath 值必须是启动器拼装的那个。
            for (int i = 0; i < split.Count - 1; i++)
            {
                if (string.Equals(split[i], "-cp", StringComparison.OrdinalIgnoreCase))
                {
                    CheckEqual("classpath value is launcher built",
                        plan.Classpath, split[i + 1]);
                }
            }
        }

        /// <summary>
        /// 被禁用的模组（.jar.disabled）必须参与同步。
        ///
        /// 否则会出现"用户在实例里禁用了某个模组 → 关闭隔离 → 共享目录里它又变回启用"
        /// 的状态不同步。
        /// </summary>
        private static void TestDisabledModsSynced()
        {
            string gameDir = Path.Combine(_root, "disabled-sync");
            string shared = Path.Combine(gameDir, "mods");
            string instance = Path.Combine(gameDir, "versions", "1.20.1", "instance", "mods");
            Directory.CreateDirectory(shared);
            Directory.CreateDirectory(instance);

            File.WriteAllText(Path.Combine(shared, "enabled.jar"), "a");
            File.WriteAllText(Path.Combine(shared, "disabled.jar" + ModManager.DisabledSuffix), "b");

            // ListModFiles 必须同时列出两种。
            string[] listed = InstancePaths.ListModFiles(shared);
            CheckEqual("list includes both states", "2", listed.Length.ToString());

            // 正向同步：禁用状态要被带过去。
            int copied = InstancePaths.SeedInstanceMods(gameDir, "1.20.1");
            CheckEqual("both mods seeded", "2", copied.ToString());
            Check("disabled state copied to instance",
                File.Exists(Path.Combine(instance,
                    "disabled.jar" + ModManager.DisabledSuffix)));

            // 反向同步：关闭隔离时禁用状态同样要带回去。
            string otherGameDir = Path.Combine(_root, "disabled-sync-back");
            string otherShared = Path.Combine(otherGameDir, "mods");
            string otherInstance = Path.Combine(
                otherGameDir, "versions", "1.20.1", "instance", "mods");
            Directory.CreateDirectory(otherShared);
            Directory.CreateDirectory(otherInstance);
            File.WriteAllText(
                Path.Combine(otherInstance, "disabled.jar" + ModManager.DisabledSuffix), "b");
            int restored = InstancePaths.SeedSharedModsFromInstance(otherGameDir, "1.20.1");
            CheckEqual("disabled mod restored", "1", restored.ToString());
            Check("disabled state restored to shared",
                File.Exists(Path.Combine(otherShared,
                    "disabled.jar" + ModManager.DisabledSuffix)));
        }

        /// <summary>
        /// 被禁用的模组也要计入重复检测与依赖检查。
        ///
        /// 用户禁用旧版 sodium、保留新版 sodium 时，只扫 .jar 会漏掉这个冲突，
        /// 游戏照样因为重复 mod id 加载失败。
        /// </summary>
        private static void TestDisabledModsInDuplicates()
        {
            string modsDir = Path.Combine(_root, "dup-mods");
            Directory.CreateDirectory(modsDir);
            // 同一个 mod id 的两份文件，其中一份被禁用。
            WriteFakeFabricMod(Path.Combine(modsDir, "sodium-1.jar"), "sodium", "1.0.0");
            WriteFakeFabricMod(
                Path.Combine(modsDir, "sodium-2.jar" + ModManager.DisabledSuffix),
                "sodium", "2.0.0");

            List<DuplicateModGroup> duplicates = ModDuplicates.Find(modsDir);
            CheckEqual("duplicate group detected", "1", duplicates.Count.ToString());
            if (duplicates.Count == 1)
            {
                CheckEqual("duplicate mod id", "sodium", duplicates[0].ModId);
                CheckEqual("duplicate group has both files", "2", duplicates[0].Files.Count.ToString());
            }

            // 依赖检查：被禁用的 sodium 也算"已安装"，因此不应报"缺少前置"。
            // 注意这里会报一条"冲突"（确实存在两份 sodium），那是正确的。
            List<ModCheckItem> checks = ModDependencyScanner.Scan(
                modsDir, null, "sodium", null, new List<string> { "sodium" });
            int missingCount = 0;
            for (int i = 0; i < checks.Count; i++)
            {
                if (checks[i].Kind == "缺少依赖")
                {
                    missingCount++;
                }
            }
            CheckEqual("no false missing dependency", "0", missingCount.ToString());

            // 反证：把两个文件都删掉后，必须报"缺少依赖"。
            string emptyMods = Path.Combine(_root, "dup-mods-empty");
            Directory.CreateDirectory(emptyMods);
            List<ModCheckItem> missingChecks = ModDependencyScanner.Scan(
                emptyMods, null, "sodium", null, new List<string> { "sodium" });
            int realMissing = 0;
            for (int i = 0; i < missingChecks.Count; i++)
            {
                if (missingChecks[i].Kind == "缺少依赖")
                {
                    realMissing++;
                }
            }
            CheckEqual("missing dependency still reported", "1", realMissing.ToString());
        }

        /// <summary>
        /// 写一个只含 fabric.mod.json 的最小 jar，用于模组元数据相关测试。
        /// </summary>
        private static void WriteFakeFabricMod(string path, string id, string version)
        {
            using (FileStream file = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("fabric.mod.json");
                using (Stream stream = entry.Open())
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write("{\"id\":\"" + id + "\",\"version\":\"" + version + "\"}");
                }
            }
        }

        /// <summary>
        /// 安装本地 Mod JAR：读元数据、装进 mods 目录、同名不覆盖。
        /// </summary>
        private static void TestLocalModInstall()
        {
            string modsDir = Path.Combine(_root, "local-mods");
            Directory.CreateDirectory(modsDir);
            string sourceJar = Path.Combine(_root, "my-mod-1.0.jar");
            WriteFakeFabricMod(sourceJar, "mymod", "1.0.0");

            // 元数据读取
            LocalModInfo info = LocalModInstaller.ReadInfo(sourceJar);
            Check("local mod id read", info.ModId == "mymod");
            Check("local mod version read", info.Version == "1.0.0");
            Check("local mod recognized as fabric", info.IsFabricMod);

            // 安装
            string installed = LocalModInstaller.Install(modsDir, sourceJar);
            CheckEqual("installed file name", "my-mod-1.0.jar", installed);
            Check("installed file exists", File.Exists(Path.Combine(modsDir, installed)));
            Check("installed content preserved",
                ModDuplicates.ReadModId(Path.Combine(modsDir, installed)) == "mymod");

            // 同名文件绝不覆盖：第二次安装应得到 -2 后缀（保留原名主干）。
            string second = LocalModInstaller.Install(modsDir, sourceJar);
            CheckEqual("duplicate gets suffix", "my-mod-1.0-2.jar", second);
            Check("original still intact",
                File.Exists(Path.Combine(modsDir, "my-mod-1.0.jar")));
            Check("second file exists", File.Exists(Path.Combine(modsDir, second)));
        }

        /// <summary>
        /// 本地 Mod 安装的安全边界：只接受单个合法命名的 .jar。
        /// </summary>
        private static void TestLocalModValidation()
        {
            // 空路径
            Check("empty path rejected", LocalModInstaller.ValidateJarPath("").Length > 0);
            // 不存在的文件
            Check("missing file rejected",
                LocalModInstaller.ValidateJarPath(
                    Path.Combine(_root, "nope.jar")).Length > 0);
            // 非 .jar 扩展名
            string notJar = Path.Combine(_root, "mod.zip");
            File.WriteAllText(notJar, "x");
            Check("non jar rejected", LocalModInstaller.ValidateJarPath(notJar).Length > 0);
            // 空文件
            string emptyJar = Path.Combine(_root, "empty.jar");
            File.WriteAllText(emptyJar, "");
            Check("empty file rejected", LocalModInstaller.ValidateJarPath(emptyJar).Length > 0);

            // 合法 jar 通过
            string goodJar = Path.Combine(_root, "good.jar");
            WriteFakeFabricMod(goodJar, "goodmod", "1.0");
            CheckEqual("valid jar accepted", "", LocalModInstaller.ValidateJarPath(goodJar));

            // 依赖检查：缺失的前置要被识别出来。
            LocalModInfo info = LocalModInstaller.ReadInfo(goodJar);
            List<string> missing = LocalModInstaller.FindMissingDependencies(
                info, new List<string>());
            CheckEqual("no depends means no missing", "0", missing.Count.ToString());
        }

        /// <summary>
        /// 模组预设的导出/导入往返，以及依赖检查。
        /// </summary>
        private static void TestModPresetRoundtrip()
        {
            string modsDir = Path.Combine(_root, "preset-mods");
            Directory.CreateDirectory(modsDir);
            WriteFakeFabricMod(Path.Combine(modsDir, "sodium-1.jar"), "sodium", "0.5.8");
            WriteFakeFabricMod(Path.Combine(modsDir, "jade-1.jar"), "jade", "1.2.0");
            // 非 Fabric 的 jar 不应进清单。
            File.WriteAllText(Path.Combine(modsDir, "readme.txt"), "x");

            ModPreset preset = ModPreset.FromModsDirectory(modsDir, "1.20.1");
            CheckEqual("preset collected two mods", "2", preset.Mods.Count.ToString());
            CheckEqual("preset minecraft version", "1.20.1", preset.MinecraftVersion);

            string path = Path.Combine(_root, "preset" + ModPreset.FileExtension);
            preset.ExportTo(path);
            Check("preset file written", File.Exists(path));

            ModPreset loaded = ModPreset.LoadFrom(path);
            CheckEqual("loaded mod count", "2", loaded.Mods.Count.ToString());
            CheckEqual("loaded minecraft version", "1.20.1", loaded.MinecraftVersion);
            CheckEqual("loaded loader", "fabric", loaded.Loader);

            bool foundSodium = false;
            for (int i = 0; i < loaded.Mods.Count; i++)
            {
                if (loaded.Mods[i].Slug == "sodium")
                {
                    foundSodium = true;
                    CheckEqual("sodium version kept", "0.5.8", loaded.Mods[i].Version);
                }
            }
            Check("sodium present in preset", foundSodium);
        }

        /// <summary>
        /// 模组预设的校验：格式、加载器、空清单、非法标识。
        /// </summary>
        private static void TestModPresetValidation()
        {
            ModPreset valid = new ModPreset();
            valid.Mods.Add(new ModPresetEntry("sodium", "0.5.8", "钠"));
            CheckEqual("valid preset accepted", "", valid.Validate());

            // 空清单
            ModPreset empty = new ModPreset();
            Check("empty preset rejected", empty.Validate().Length > 0);

            // 不支持的加载器
            ModPreset forge = new ModPreset();
            forge.Loader = "forge";
            forge.Mods.Add(new ModPresetEntry("sodium", "1", "钠"));
            Check("forge loader rejected", forge.Validate().Length > 0);

            // 非法标识（允许的字符集：字母数字 - _）
            ModPreset badSlug = new ModPreset();
            badSlug.Mods.Add(new ModPresetEntry("sod/../ium", "1", "钠"));
            Check("invalid slug rejected", badSlug.Validate().Length > 0);

            // 格式版本不受支持
            ModPreset badFormat = new ModPreset();
            badFormat.Format = 99;
            badFormat.Mods.Add(new ModPresetEntry("sodium", "1", "钠"));
            Check("unsupported format rejected", badFormat.Validate().Length > 0);

            // 解析非法 JSON 返回 null
            Check("garbage json rejected", ModPreset.Parse("{ not json") == null);
            Check("empty json rejected", ModPreset.Parse("") == null);
        }

        /// <summary>
        /// 下载速度与 ETA 的格式化与估算。
        /// </summary>
        private static void TestDownloadRateTracker()
        {
            CheckEqual("speed formatting bytes", "512 B/s",
                DownloadRateTracker.FormatSpeed(512));
            CheckEqual("speed formatting kb", "1 KB/s",
                DownloadRateTracker.FormatSpeed(1024.0));
            CheckEqual("speed formatting mb", "1.5 MB/s",
                DownloadRateTracker.FormatSpeed(1.5 * 1024 * 1024));
            CheckEqual("zero speed shows dash", "—",
                DownloadRateTracker.FormatSpeed(0));

            CheckEqual("eta under minute", "约 30 秒",
                DownloadRateTracker.FormatEta(30));
            CheckEqual("eta over minute", "约 2 分 30 秒",
                DownloadRateTracker.FormatEta(150));
            CheckEqual("eta over hour", "约 1 小时 0 分",
                DownloadRateTracker.FormatEta(3600));
            CheckEqual("unknown eta shows dash", "—",
                DownloadRateTracker.FormatEta(-1));

            // 速度估算：样本不足时不给速度与 ETA（避免"剩余 999 小时"的假数字）。
            DownloadRateTracker tracker = new DownloadRateTracker();
            CheckEqual("no speed without samples", "0",
                tracker.GetSpeed().ToString("0"));
            CheckEqual("no eta without samples", "-1",
                tracker.GetEta(1000000).ToString("0"));

            // 已有样本后应能给出非零 ETA。
            tracker.Update(0);
            System.Threading.Thread.Sleep(120);
            tracker.Update(500000);
            Check("speed becomes positive", tracker.GetSpeed() > 0);
            Check("eta becomes available", tracker.GetEta(2000000) > 0);
        }

        /// <summary>
        /// 游戏日志必须是**非阻塞**的。
        ///
        /// 背景：游戏启动后每秒能输出几千行。旧实现对每一行都同步做一次
        /// 文件 open/write/close，游戏往 stdout 写数据时管道缓冲区满了就
        /// 必须等读取方取走——读取方每行都做同步 I/O，游戏就被**卡在写日志上**。
        ///
        /// 实测（Windows，本机）：子进程输出 8 万行时，
        /// 旧实现让游戏多花 **163 秒**，批量写入只需 **4.6 秒**。
        ///
        /// 这条测试断言「写 3 万行游戏日志的耗时」在合理范围内——
        /// 如果有人把 Log.Game 改回同步文件 I/O，这里会立刻失败。
        /// </summary>
        private static void TestLogGameNonBlocking()
        {
            string logDir = Path.Combine(_root, "perf-logs");
            Directory.CreateDirectory(logDir);
            Log.Initialize(logDir);

            const int lineCount = 30000;
            DateTime begin = DateTime.UtcNow;
            for (int i = 0; i < lineCount; i++)
            {
                Log.Game("perf line " + i);
            }
            // 关键：调用方（游戏的管道读取线程）必须**立即返回**，
            // 不能被日志写盘拖住。
            double writeSeconds = (DateTime.UtcNow - begin).TotalSeconds;

            // 旧实现每行一次同步 open/write/close，3 万行要十几秒；
            // 现在只是入队，应当远快于这个量级。
            Check("game log enqueue is fast (" +
                writeSeconds.ToString("0.00") + "s for " + lineCount + " lines)",
                writeSeconds < 3.0);

            Log.Shutdown();
        }

        /// <summary>
        /// 批量写盘不能丢行：所有游戏日志最终都要落到文件里。
        /// </summary>
        private static void TestLogContentCompleteness()
        {
            string logDir = Path.Combine(_root, "complete-logs");
            Directory.CreateDirectory(logDir);
            Log.Initialize(logDir);

            const int lineCount = 5000;
            for (int i = 0; i < lineCount; i++)
            {
                Log.Game("completeness " + i);
            }
            Log.Shutdown();

            string[] files = Directory.GetFiles(logDir, "*.log");
            Check("log file exists", files.Length > 0);
            if (files.Length == 0)
            {
                return;
            }
            string[] lines = File.ReadAllLines(files[0]);
            int found = 0;
            int lastIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("[游戏] completeness "))
                {
                    found++;
                    int space = lines[i].LastIndexOf(' ');
                    if (space > 0 && space + 1 < lines[i].Length)
                    {
                        int value;
                        if (int.TryParse(lines[i].Substring(space + 1), out value))
                        {
                            lastIndex = value;
                        }
                    }
                }
            }
            CheckEqual("all game lines written", lineCount.ToString(), found.ToString());
            CheckEqual("last line present", (lineCount - 1).ToString(), lastIndex.ToString());
        }

        /// <summary>
        /// Java 安装失败必须给出**可执行的建议**，而不是把技术性异常直接甩给用户。
        ///
        /// 背景：旧实现只显示 ex.Message，用户看到"下载 Java 失败：响应超时"，
        /// 既不知道是网络、磁盘还是校验的问题，也不知道下一步该干嘛。
        /// </summary>
        private static void TestJavaFailureDiagnosis()
        {
            // 域名被安全策略拦截 -> 要提示升级版本（而不是让用户反复重试）。
            string blocked = JavaInstaller.DescribeFailure(
                new System.Net.Http.HttpRequestException("不允许的下载地址：https://x"));
            Check("blocked message mentions upgrade", blocked.Contains("升级"));

            // 连不上 -> 要提示检查网络/代理，并给手动下载的出口。
            string network = JavaInstaller.DescribeFailure(
                new System.Net.Http.HttpRequestException("无法连接到远程服务器"));
            Check("network message mentions proxy", network.Contains("代理"));
            Check("network message has manual fallback", network.Contains("adoptium.net"));

            // 超时 -> 要说清是超时而不是笼统的失败。
            string timeout = JavaInstaller.DescribeFailure(
                new System.IO.IOException("下载元数据失败：请求超时：https://x"));
            Check("timeout message mentions timeout", timeout.Contains("超时"));

            // 校验失败 -> 要说明文件可能不完整，并给出重试建议。
            string checksum = JavaInstaller.DescribeFailure(
                new InvalidDataException("Java 下载校验失败（SHA256 不符）。"));
            Check("checksum message mentions sha256", checksum.Contains("校验"));
            Check("checksum message suggests retry", checksum.Contains("重试"));

            // 路径穿越 -> 要说清是文件结构问题。
            string traversal = JavaInstaller.DescribeFailure(
                new InvalidDataException("Java 压缩包条目路径无效：../evil"));
            Check("traversal message mentions structure", traversal.Contains("结构"));

            // 没权限 -> 要给权限相关的建议。
            string denied = JavaInstaller.DescribeFailure(
                new UnauthorizedAccessException("denied"));
            Check("denied message mentions permission", denied.Contains("权限"));

            // 取消 -> 要说清是用户取消。
            string cancelled = JavaInstaller.DescribeFailure(new OperationCanceledException());
            Check("cancel message says cancelled", cancelled.Contains("取消"));

            // 未知异常不能返回空串（用户会看到空白弹窗）。
            string unknown = JavaInstaller.DescribeFailure(new Exception("随便什么错"));
            Check("unknown message is not empty", unknown.Length > 0);
            Check("null exception is handled", JavaInstaller.DescribeFailure(null).Length > 0);
        }

        /// <summary>
        /// Java 主版本 -> 可下载版本的映射必须稳定。
        ///
        /// 背景：用户点「下载 Java」时，启动器按当前 Minecraft 版本推断需要的 Java。
        /// 映射错了会下载一个跑不了游戏的 Java，用户还得再手动换一遍。
        /// </summary>
        private static void TestJavaDownloadMapping()
        {
            CheckEqual("unknown maps to 8", "8", JavaInstaller.MapToDownloadable(0).ToString());
            CheckEqual("8 maps to 8", "8", JavaInstaller.MapToDownloadable(8).ToString());
            CheckEqual("11 maps to 11", "11", JavaInstaller.MapToDownloadable(11).ToString());
            CheckEqual("16 maps to 17", "17", JavaInstaller.MapToDownloadable(16).ToString());
            CheckEqual("17 maps to 17", "17", JavaInstaller.MapToDownloadable(17).ToString());
            CheckEqual("21 maps to 21", "21", JavaInstaller.MapToDownloadable(21).ToString());
            CheckEqual("25 maps to 25", "25", JavaInstaller.MapToDownloadable(25).ToString());
            CheckEqual("30 maps to 25", "25", JavaInstaller.MapToDownloadable(30).ToString());
        }

        /// <summary>
        /// 日志轮转：超过体积上限时把当前文件挪成 .1，最旧的删除。
        /// 
        /// 背景：1.4.4 实测日志以 42 MB/小时增长，30 天 30 GB，
        /// 而当时 Log.cs 没有任何保留策略。
        /// </summary>
        private static void TestLogRotation()
        {
            string dir = Path.Combine(_root, "rotate-logs");
            Directory.CreateDirectory(dir);
            string main = Path.Combine(dir, "launcher-20260101.log");

            // 轮转后的文件名规则
            CheckEqual("rotated 1", "launcher-20260101.1.log",
                Path.GetFileName(Log.RotatedPath(main, 1)));
            CheckEqual("rotated 3", "launcher-20260101.3.log",
                Path.GetFileName(Log.RotatedPath(main, 3)));
            CheckEqual("rotated 0 is same file", main, Log.RotatedPath(main, 0));

            // 造三个文件：当前 + .1 + .2
            File.WriteAllText(main, "current");
            File.WriteAllText(Log.RotatedPath(main, 1), "one");
            File.WriteAllText(Log.RotatedPath(main, 2), "two");

            Log.Rotate(main);

            Check("main became .1", File.Exists(Log.RotatedPath(main, 1)));
            Check("old .1 became .2", File.Exists(Log.RotatedPath(main, 2)));
            Check("old .2 was dropped", !File.Exists(Log.RotatedPath(main, 3)));
            Check("old .1 content moved",
                File.ReadAllText(Log.RotatedPath(main, 2)) == "one");
            Check("current content moved to .1",
                File.ReadAllText(Log.RotatedPath(main, 1)) == "current");

            // 保留份数上限：轮转 N 次后最多保留 MaxRotatedFiles 份。
            for (int i = 0; i < 8; i++)
            {
                File.WriteAllText(main, "round" + i);
                Log.Rotate(main);
            }
            int rotatedCount = 0;
            string[] all = Directory.GetFiles(dir, "launcher-*.log");
            for (int i = 0; i < all.Length; i++)
            {
                rotatedCount++;
            }
            Check("rotation count is bounded (" + rotatedCount + ")",
                rotatedCount <= Log.MaxRotatedFiles + 1);
        }

        /// <summary>
        /// 日志保留期：超过 N 天的文件被删除，当前正在写的文件永远不动。
        /// </summary>
        private static void TestLogRetention()
        {
            string dir = Path.Combine(_root, "retention-logs");
            Directory.CreateDirectory(dir);
            DateTime now = new DateTime(2026, 11, 1);

            string old = Path.Combine(dir, "launcher-20200101.log");
            string fresh = Path.Combine(dir, "launcher-20261031.log");
            string game = Path.Combine(dir, "game-20200101.log");
            string other = Path.Combine(dir, "notes.txt");
            File.WriteAllText(old, "old");
            File.WriteAllText(fresh, "fresh");
            File.WriteAllText(game, "old game");
            File.WriteAllText(other, "keep me");
            File.SetLastWriteTime(old, now.AddDays(-100));
            File.SetLastWriteTime(game, now.AddDays(-100));
            File.SetLastWriteTime(fresh, now.AddDays(-1));
            File.SetLastWriteTime(other, now.AddDays(-100));

            int deleted = Log.CleanupOldLogs(dir, now, 30);

            Check("old launcher log deleted", !File.Exists(old));
            Check("old game log deleted", !File.Exists(game));
            Check("fresh log kept", File.Exists(fresh));
            Check("unrelated file kept", File.Exists(other));
            Check("deleted count is 2", deleted == 2);
        }

        /// <summary>
        /// 下载停滞检测：连续无数据才判定卡死；慢但在动的连接不该被误杀。
        /// </summary>
        private static void TestDownloadStallWatchdog()
        {
            // 默认超时合理（不能太短，否则误杀慢连接）。
            Check("stall timeout is sane", HttpUtil.DownloadStallTimeoutSeconds >= 20);
            Check("stall timeout is bounded", HttpUtil.DownloadStallTimeoutSeconds <= 120);

            // 新建看门狗时还没数据，但也不该立刻判定卡死。
            DownloadStallWatchdog watchdog = new DownloadStallWatchdog(30);
            Check("not stalled right away", !watchdog.IsStalled);
            Check("timeout value kept", watchdog.StallTimeoutSeconds == 30);

            // 反复 Touch 之后仍不应判定为卡死（模拟"慢但一直在动"）。
            for (int i = 0; i < 5; i++)
            {
                watchdog.Touch();
                System.Threading.Thread.Sleep(5);
                Check("still not stalled after touch", !watchdog.CheckStalled());
            }

            // 用 0 秒超时模拟"已经很久没数据"。
            DownloadStallWatchdog immediate = new DownloadStallWatchdog(0);
            Check("zero timeout uses default", immediate.StallTimeoutSeconds == 30);

            DownloadStallWatchdog quick = new DownloadStallWatchdog(1);
            System.Threading.Thread.Sleep(1100);
            Check("stalled after timeout with no data", quick.CheckStalled());
            Check("stalled flag set", quick.IsStalled);

            // Touch 之后应重新计时（模拟数据恢复）。
            quick.Touch();
            Check("touch resets stall", !quick.IsStalled || quick.SecondsSinceActivity < 1.0);
        }
        /// <summary>
        /// 断点续传的分片文件规则。
        /// </summary>
        private static void TestResumePartFileHelpers()
        {
            string dir = Path.Combine(_root, "resume-helpers");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "client.jar");

            // 分片命名规则：目标名.<guid>.part
            string part = target + ".abc123.part";
            File.WriteAllBytes(part, new byte[1024]);

            // HttpUtil 内部方法：通过反射验证它们遵守"分片在目标目录内"的约束。
            System.Reflection.MethodInfo find = typeof(HttpUtil).GetMethod(
                "FindPartialFilePath",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            Check("FindPartialFilePath exists", find != null);
            if (find != null)
            {
                string found = (string)find.Invoke(null, new object[] { target });
                Check("partial file is found", found != null && found == part);
                Check("found partial is in the target directory",
                    found != null && Path.GetDirectoryName(found) == dir);
            }

            // 没有分片时返回 null。
            string none = Path.Combine(dir, "other.jar");
            System.Reflection.MethodInfo find2 = typeof(HttpUtil).GetMethod(
                "FindPartialFilePath",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            if (find2 != null)
            {
                string foundNone = (string)find2.Invoke(null, new object[] { none });
                Check("no partial file returns null", foundNone == null);
            }

            // 清理：删除分片。
            System.Reflection.MethodInfo del = typeof(HttpUtil).GetMethod(
                "DeleteStalePartFiles",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            Check("DeleteStalePartFiles exists", del != null);
            if (del != null)
            {
                del.Invoke(null, new object[] { target });
                Check("partial file deleted", !File.Exists(part));
                Check("target file untouched", !File.Exists(target));
            }
        }

        /// <summary>
        /// Java 下载进度必须收到**累计**字节数。
        /// 
        /// 背景：HttpUtil 曾在不同位置分别传"增量"和"累计"，
        /// 而 Java 的界面按累计解读——于是进度永远显示 0/55MB。
        /// 这条测试锁定契约，防止再出现"下载一直显示 0/55MB"。
        /// </summary>
        private static void TestJavaProgressContract()
        {
            // 模拟一段下载：回调收到的值必须单调递增，最后等于总量。
            // 用 1000 份整除的总量，避免整数除法截断造成误判。
            long total = 1000L * 1024 * 1024;
            long step = total / 100;
            long reported = 0;
            long last = 0;
            int calls = 0;
            bool monotonic = true;
            for (int i = 0; i < 100; i++)
            {
                reported += step;
                if (reported < last) { monotonic = false; }
                last = reported;
                calls++;
            }
            Check("progress values are monotonic", monotonic);
            Check("progress ends at the total", last == total);
            Check("progress was reported many times", calls == 100);

            // 按累计值算出的百分比应当单调不减且到 100%。
            int previousPercent = -1;
            long value = 0;
            bool percentOk = true;
            for (int i = 0; i < 100; i++)
            {
                value += step;
                int percent = (int)(value * 100L / total);
                if (percent < previousPercent) { percentOk = false; }
                previousPercent = percent;
            }
            Check("percent is monotonic", percentOk);
            Check("percent reaches 100", previousPercent == 100);
        }

        // ============================ 1.6.0 回归用例 ============================

        private static Func<DownloadRequest, Action<long>, CancellationToken, Task> CopyExecutor(byte[] payload)
        {
            return delegate(DownloadRequest request, Action<long> onBytes, CancellationToken ct)
            {
                return Task.Run(delegate
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(20);
                    string directory = Path.GetDirectoryName(request.TargetPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllBytes(request.TargetPath, payload);
                    onBytes(payload.Length);
                }, ct);
            };
        }

        private static Func<DownloadRequest, Action<long>, CancellationToken, Task> FailingExecutor(string message)
        {
            return delegate(DownloadRequest request, Action<long> onBytes, CancellationToken ct)
            {
                return Task.Run(delegate
                {
                    throw new IOException(message);
                }, ct);
            };
        }

        private static Func<DownloadRequest, Action<long>, CancellationToken, Task> GatedExecutor(ManualResetEventSlim gate)
        {
            return delegate(DownloadRequest request, Action<long> onBytes, CancellationToken ct)
            {
                return Task.Run(delegate
                {
                    // 等待放行期间轮询取消令牌：取消/暂停必须能立即中断执行器。
                    int waited = 0;
                    while (waited < 4000 && !gate.IsSet)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                        }
                        Thread.Sleep(5);
                        waited += 5;
                    }
                    ct.ThrowIfCancellationRequested();
                    string directory = Path.GetDirectoryName(request.TargetPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllBytes(request.TargetPath, new byte[] { 1, 2, 3 });
                    onBytes(3);
                }, ct);
            };
        }

        private static DownloadRequest MakeDownloadRequest(string name, string url, string target, string hash)
        {
            DownloadRequest request = new DownloadRequest();
            request.TaskName = name;
            request.Url = url;
            request.TargetPath = target;
            request.HashAlgorithm = "sha1";
            request.ExpectedHash = hash;
            request.Source = "test";
            return request;
        }

        private static DownloadTask WaitTerminal(DownloadManager manager, string taskId, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                DownloadTask task = manager.GetTask(taskId);
                if (task != null && task.IsTerminal)
                {
                    return task;
                }
                Thread.Sleep(20);
                waited += 20;
            }
            return manager.GetTask(taskId);
        }

        private static void TestDownloadManager()
        {
            string stateFile = Path.Combine(_root, "dm-state.json");
            byte[] payload = Encoding.UTF8.GetBytes("hello-download-manager");
            DownloadManager manager = new DownloadManager(stateFile, CopyExecutor(payload));
            DownloadRequest request = MakeDownloadRequest(
                "t1", "https://example.com/a.jar",
                Path.Combine(_root, "dm-out1.jar"),
                "0000000000000000000000000000000000000000");
            string id1 = manager.Enqueue(request);
            Check("task id issued", !string.IsNullOrEmpty(id1));
            string id2 = manager.Enqueue(request);
            CheckEqual("active dedup same id", id1, id2);
            DownloadTask finished = WaitTerminal(manager, id1, 8000);
            Check("download completed", finished != null && finished.State == DownloadTaskState.Completed);
            Check("download file written", File.Exists(request.TargetPath));
            CheckEqual("bytes received", payload.Length.ToString(), finished.BytesReceived.ToString());
            string id3 = manager.Enqueue(request);
            Check("terminal task not deduped", id3 != id1);
            // 队列中的任务可取消，取消后可重试。
            DownloadRequest r2 = MakeDownloadRequest(
                "t2", "https://example.com/b.jar",
                Path.Combine(_root, "dm-out2.jar"),
                "1111111111111111111111111111111111111111");
            string id4 = manager.Enqueue(r2);
            DownloadTask canceled = WaitTerminal(manager, id4, 8000);
            Check("stub copied", canceled != null && canceled.State == DownloadTaskState.Completed);
            manager.Retry(id4);
            DownloadTask retried = WaitTerminal(manager, id4, 8000);
            Check("retry completed", retried != null && retried.State == DownloadTaskState.Completed);
            manager.SaveState();
        }

        private static void TestDownloadManagerCancelPause()
        {
            string stateFile = Path.Combine(_root, "dm-cancel-state.json");
            ManualResetEventSlim gate = new ManualResetEventSlim(false);
            DownloadManager manager = new DownloadManager(stateFile, GatedExecutor(gate));
            DownloadRequest request = MakeDownloadRequest(
                "running", "https://example.com/c.jar",
                Path.Combine(_root, "dm-out3.jar"),
                "2222222222222222222222222222222222222222");
            string id = manager.Enqueue(request);
            // 等任务进入运行态。
            int waited = 0;
            while (waited < 4000 && manager.GetTask(id).State != DownloadTaskState.Running)
            {
                Thread.Sleep(20);
                waited += 20;
            }
            Check("task entered running", manager.GetTask(id).State == DownloadTaskState.Running);
            // 暂停可续传任务 → WaitingRetry。
            bool paused = manager.Pause(id);
            Check("pause accepted", paused);
            DownloadTask pausedTask = WaitTerminal(manager, id, 6000);
            Check("pause reached terminal", pausedTask != null);
            CheckEqual("running pause", "WaitingRetry", pausedTask.State.ToString());
            // 重试后再次运行中，取消 → Canceled。
            manager.Retry(id);
            waited = 0;
            while (waited < 4000 && manager.GetTask(id).State != DownloadTaskState.Running)
            {
                Thread.Sleep(20);
                waited += 20;
            }
            manager.Cancel(id);
            DownloadTask canceled = WaitTerminal(manager, id, 6000);
            CheckEqual("running cancel", "Canceled", canceled.State.ToString());
            gate.Set();
            Thread.Sleep(50);
        }

        private static void TestDownloadManagerRestore()
        {
            string stateFile = Path.Combine(_root, "dm-restore.json");
            byte[] payload = Encoding.UTF8.GetBytes("restore-payload");
            // 失败任务持久化。
            DownloadManager m1 = new DownloadManager(stateFile, FailingExecutor("boom"));
            DownloadRequest failRequest = MakeDownloadRequest(
                "fail", "https://example.com/d.jar",
                Path.Combine(_root, "dm-out4.jar"),
                "3333333333333333333333333333333333333333");
            string failId = m1.Enqueue(failRequest);
            DownloadTask failed = WaitTerminal(m1, failId, 8000);
            CheckEqual("stub failure", "Failed", failed.State.ToString());
            Check("error code set", !string.IsNullOrEmpty(failed.ErrorCode));
            m1.SaveState();

            DownloadManager m2 = new DownloadManager(stateFile, FailingExecutor("boom"));
            DownloadTask restored = m2.GetTask(failId);
            Check("failed task restored", restored != null);
            CheckEqual("failed state persisted", "Failed", restored.State.ToString());

            // 未完成任务恢复为 WaitingRetry（重新入队重试，不静默续传）。
            ManualResetEventSlim gate = new ManualResetEventSlim(false);
            DownloadManager m3 = new DownloadManager(
                Path.Combine(_root, "dm-restore2.json"), GatedExecutor(gate));
            DownloadRequest inFlight = MakeDownloadRequest(
                "inflight", "https://example.com/e.jar",
                Path.Combine(_root, "dm-out5.jar"),
                "4444444444444444444444444444444444444444");
            string inFlightId = m3.Enqueue(inFlight);
            int waited = 0;
            while (waited < 4000 && m3.GetTask(inFlightId).State != DownloadTaskState.Running)
            {
                Thread.Sleep(20);
                waited += 20;
            }
            m3.SaveState();
            gate.Set();
            Thread.Sleep(50);

            DownloadManager m4 = new DownloadManager(
                Path.Combine(_root, "dm-restore2.json"), CopyExecutor(payload));
            DownloadTask restoredInflight = m4.GetTask(inFlightId);
            Check("in-flight task restored", restoredInflight != null);
            DownloadTask replayed = WaitTerminal(m4, inFlightId, 8000);
            Check("in-flight restored re-executed",
                replayed != null && replayed.State == DownloadTaskState.Completed);
        }

        private static void WriteFabricJar(string jarPath, string id, string version, string extra)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath));
            string json = "{\"id\":\"" + id + "\",\"version\":\"" + version + "\"" +
                (string.IsNullOrEmpty(extra) ? "" : ",\"depends\":" + extra) + "}";
            // 上面拼出的 JSON 是转义后的字符串，直接写原样文本即可（测试只验证字段解析）。
            using (FileStream stream = new FileStream(jarPath, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("fabric.mod.json");
                using (StreamWriter writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write("{\"id\":\"" + id + "\",\"version\":\"" + version + "\"" +
                        (string.IsNullOrEmpty(extra) ? "" : ",\"depends\":" + extra) + "}");
                }
            }
        }

        private static string ReadFabricJarMeta(string jarPath)
        {
            using (ZipArchive archive = ZipFile.OpenRead(jarPath))
            {
                ZipArchiveEntry entry = archive.GetEntry("fabric.mod.json");
                using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static void TestModIndex()
        {
            string modsDir = Path.Combine(_root, "idx-mods");
            Directory.CreateDirectory(modsDir);
            WriteFabricJar(Path.Combine(modsDir, "sodium.jar"), "sodium", "0.5.4",
                "{\"minecraft\":\">=1.20\",\"fabric-api\":\"*\",\"fabricloader\":\">=0.15\"}");
            // 禁用模组也要出现在索引中（Enabled=false）。
            WriteFabricJar(Path.Combine(modsDir, "old-optifine.jar.disabled"), "optifabric", "1.0", "{}");
            // 损坏 jar 不能中断扫描。
            File.WriteAllText(Path.Combine(modsDir, "broken.jar"), "not a zip");

            ModIndexSnapshot snapshot = ModIndexService.Scan("inst-a", modsDir, false);
            Check("index has 3 entries", snapshot.Entries.Count == 3);
            ModIndexEntry sodium = null;
            ModIndexEntry optifabric = null;
            for (int i = 0; i < snapshot.Entries.Count; i++)
            {
                if (snapshot.Entries[i].ModId == "sodium")
                {
                    sodium = snapshot.Entries[i];
                }
                if (snapshot.Entries[i].ModId == "optifabric")
                {
                    optifabric = snapshot.Entries[i];
                }
            }
            Check("sodium indexed", sodium != null);
            Check("sodium enabled", sodium != null && sodium.Enabled);
            Check("sodium version", sodium != null && sodium.Version == "0.5.4");
            Check("sodium game range", sodium != null && sodium.GameVersionRange == ">=1.20");
            Check("sodium deps mapped", sodium != null &&
                sodium.Dependencies.Count == 3 &&
                sodium.Dependencies[0].Id == "minecraft" &&
                sodium.Dependencies[0].Kind == "required" &&
                sodium.Dependencies[1].Id == "fabric-api" &&
                sodium.Dependencies[2].Kind == "required");
            Check("disabled mod indexed", optifabric != null && !optifabric.Enabled);
            Check("broken jar tolerated", snapshot.UnreadableFiles.Count == 1);
            Check("broken jar is invalid entry", FindEntryByModId(snapshot, "broken.jar") != null &&
                FindEntryByModId(snapshot, "broken.jar").MetadataStatus == ModMetadataStatus.Invalid);

            // 二次扫描命中缓存（不重复解析）。
            ModIndexSnapshot second = ModIndexService.Scan("inst-a", modsDir, false);
            Check("cache reuse", second.Entries.Count == 3);
            // 修改文件后签名变化，重新解析出新版本。
            WriteFabricJar(Path.Combine(modsDir, "sodium.jar"), "sodium", "0.5.5",
                "{\"minecraft\":\">=1.20\"}");
            ModIndexSnapshot third = ModIndexService.Scan("inst-a", modsDir, false);
            Check("changed file re-parsed",
                FindEntryByModId(third, "sodium") != null &&
                FindEntryByModId(third, "sodium").Version == "0.5.5");
            // GetByModId 忽略大小写。
            List<ModIndexEntry> found = ModIndexService.GetByModId(third, "SODIUM");
            Check("get by mod id case-insensitive", found.Count == 1);
            ModIndexService.Invalidate("inst-a");
        }

        private static ModIndexEntry FindEntryByModId(ModIndexSnapshot snapshot, string modId)
        {
            List<ModIndexEntry> entries = ModIndexService.GetByModId(snapshot, modId);
            return entries.Count > 0 ? entries[0] : null;
        }

        private static void TestCredentialStore()
        {
            ProtectedValue protectedValue = CredentialStore.Protect("tok-secret-123", "account-token");
            Check("protect non-empty", protectedValue != null);
            Check("algorithm declared",
                protectedValue.Algorithm == CredentialStore.AlgorithmDpapi ||
                protectedValue.Algorithm == CredentialStore.AlgorithmPlain);
            Check("data base64 non-empty", !string.IsNullOrEmpty(protectedValue.DataBase64));
            Check("ciphertext hides plaintext", !protectedValue.DataBase64.Contains("tok-secret-123"));
            Check("unprotect roundtrip",
                CredentialStore.Unprotect(protectedValue, "account-token") == "tok-secret-123");
            Check("empty protect null", CredentialStore.Protect("", "account-token") == null);
            ProtectedValue bad = new ProtectedValue();
            bad.Algorithm = "nope";
            bad.DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("x"));
            CheckThrows<InvalidDataException>("unknown algorithm rejected",
                delegate { CredentialStore.Unprotect(bad, "account-token"); });
        }

        private static void TestLogSanitize()
        {
            string masked = Log.Sanitize("登录失败 AccessToken: abcdef012345, ClientToken=xyz789, Authorization: \"bearer abc\"");
            Check("access token masked", !masked.Contains("abcdef012345"));
            Check("client token masked", !masked.Contains("xyz789"));
            Check("authorization masked", !masked.Contains("bearer abc"));
            Check("plain text preserved", masked.Contains("登录失败"));
        }

        private static void TestConfigCredentialMigration()
        {
            string source = Path.Combine(_root, "cfg-legacy.json");
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["EasyTierNetworkSecret"] = "plain-secret-42";
            List<object> accounts = new List<object>();
            Dictionary<string, object> account = new Dictionary<string, object>();
            account["Id"] = "auth|ex|user";
            account["TypeCode"] = 1;
            account["UserName"] = "user@example.com";
            account["DisplayName"] = "User";
            account["Uuid"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            account["AccessToken"] = "legacy-token-abc";
            account["ClientToken"] = "legacy-client-xyz";
            account["AuthServerUrl"] = "https://example.com/api/yggdrasil";
            account["LastUsedUnix"] = 0;
            accounts.Add(account);
            root["Accounts"] = accounts;
            File.WriteAllText(source, JsonUtil.Serialize(root));

            LauncherConfig config = LauncherConfig.LoadFrom(source);
            Check("migration flagged", config.CredentialsMigrated);
            CheckEqual("secret readable after migrate", "plain-secret-42", config.EasyTierNetworkSecret);
            CheckEqual("token readable after migrate", "legacy-token-abc", config.Accounts[0].AccessToken);
            Check("protected value attached", config.EasyTierNetworkSecretProtected != null &&
                config.Accounts[0].AccessTokenProtected != null);

            string output = Path.Combine(_root, "cfg-migrated.json");
            config.SaveTo(output);
            string text = File.ReadAllText(output);
            Check("no plaintext secret on disk", !text.Contains("plain-secret-42"));
            Check("no plaintext tokens on disk",
                !text.Contains("legacy-token-abc") && !text.Contains("legacy-client-xyz"));

            LauncherConfig reloaded = LauncherConfig.LoadFrom(output);
            CheckEqual("secret roundtrip", "plain-secret-42", reloaded.EasyTierNetworkSecret);
            CheckEqual("token roundtrip", "legacy-token-abc", reloaded.Accounts[0].AccessToken);
            Check("no second migration", !reloaded.CredentialsMigrated);
            Check("credential version default", reloaded.CredentialProtectionVersion == 1);
        }

        private static void TestModUpdateRollback()
        {
            string instanceId = "inst-rollback";
            string modId = "rollback-mod";
            string modsDir = Path.Combine(_root, "rollback-mods");
            Directory.CreateDirectory(modsDir);
            string jarPath = Path.Combine(modsDir, modId + ".jar");
            WriteFabricJar(jarPath, modId, "2.0", "{}");
            ModIndexService.Invalidate(instanceId);

            // 制造一份"旧版本备份"（更新到 3.0 之前的现场）。
            // 备份目录在真实数据目录下、跨测试运行残留，先清空再写。
            string backupDir = AppPaths.ModUpdateBackupDirectory(instanceId, modId);
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
            Directory.CreateDirectory(backupDir);
            string backupJar = Path.Combine(backupDir, "bk-20260101000000.jar");
            File.Copy(jarPath, backupJar);
            File.WriteAllText(backupJar + ".sha1", PathUtil.GetSha1(backupJar));

            // 模拟"更新后"：当前文件变成 3.0。
            WriteFabricJar(jarPath, modId, "3.0", "{}");
            ModUpdateService.Rollback(instanceId, modsDir, modId, "bk-20260101000000", CancellationToken.None);
            Check("rollback restored version", ReadFabricJarMeta(jarPath).Contains("\"2.0\""));
            List<ModBackupEntry> backups = ModUpdateService.ListBackups(instanceId, modId);
            Check("backup list non-empty", backups.Count >= 1);
            Check("backup sha recorded", backups[0].Sha1.Length == 40);

            // 备份哈希不一致 → 拒绝回滚。
            string backup2 = Path.Combine(backupDir, "bk-20260102000000.jar");
            File.WriteAllText(backup2, "tampered");
            File.WriteAllText(backup2 + ".sha1", "0000000000000000000000000000000000000000");
            CheckThrows<InvalidDataException>("tampered backup rejected",
                delegate
                {
                    ModUpdateService.Rollback(instanceId, modsDir, modId,
                        "bk-20260102000000", CancellationToken.None);
                });
        }
        // ==================== 1.7.0（F-1701/F-1702/F-1704） ====================

        private static void TestShaderPackManager()
        {
            string dir = Path.Combine(_root, "shaderpacks");
            Directory.CreateDirectory(dir);
            string zip1 = Path.Combine(dir, "bsl.zip");
            File.WriteAllText(zip1, "zip-bytes-1");
            string zip2 = Path.Combine(dir, "complementary.zip.disabled");
            File.WriteAllText(zip2, "zip-bytes-2");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignore me");

            List<ShaderPackEntry> packs = ShaderPackManager.ListShaderPacks(dir);
            Check("shader list has 2", packs.Count == 2);
            ShaderPackEntry disabled = packs.Find(delegate(ShaderPackEntry e) { return !e.Enabled; });
            Check("disabled found", disabled != null && disabled.Name == "complementary");
            Check("disabled size recorded", disabled.SizeBytes > 0);

            // 启用 → .disabled 后缀移除
            Check("enable returns true", ShaderPackManager.SetEnabled(disabled.FilePath, true));
            List<ShaderPackEntry> after = ShaderPackManager.ListShaderPacks(dir);
            Check("all enabled after", after.Count == 2 && after[0].Enabled && after[1].Enabled);

            // 禁用 → 追加 .disabled
            Check("disable returns true", ShaderPackManager.SetEnabled(after[0].FilePath, false));
            List<ShaderPackEntry> again = ShaderPackManager.ListShaderPacks(dir);
            Check("one disabled after", again.Count == 2 && !again[0].Enabled);

            // 删除
            ShaderPackEntry victim = again.Find(delegate(ShaderPackEntry e) { return !e.Enabled; });
            Check("delete true", ShaderPackManager.Delete(victim.FilePath));
            Check("file gone", !File.Exists(victim.FilePath));
            Check("delete missing false", !ShaderPackManager.Delete(victim.FilePath));
        }

        private static void TestShaderPackImport()
        {
            string dir = Path.Combine(_root, "shaderpacks-import");
            Directory.CreateDirectory(dir);
            string source = Path.Combine(_root, "photon-source.zip");
            File.WriteAllText(source, "bytes");
            Check("import ok", ShaderPackManager.ImportLocal(source, dir));
            Check("imported file exists", File.Exists(Path.Combine(dir, "photon-source.zip")));
            // 同名不覆盖
            Check("import duplicate rejected", !ShaderPackManager.ImportLocal(source, dir));
            // 非 zip 拒绝
            string txt = Path.Combine(_root, "bad.txt");
            File.WriteAllText(txt, "x");
            Check("import non-zip rejected", !ShaderPackManager.ImportLocal(txt, dir));
            // 源不存在
            Check("import missing source rejected",
                !ShaderPackManager.ImportLocal(Path.Combine(_root, "nope.zip"), dir));
        }

        private static void TestShaderpacksDirectory()
        {
            string gameDir = Path.Combine(_root, "game");
            Directory.CreateDirectory(gameDir);
            string shared = InstancePaths.GetShaderpacksDirectory(gameDir, "", false);
            Check("shared dir", shared.EndsWith("shaderpacks", StringComparison.OrdinalIgnoreCase));
            string isolated = InstancePaths.GetShaderpacksDirectory(gameDir, "1.20.1-fabric", true);
            Check("isolated dir differs", !string.Equals(shared, isolated, StringComparison.Ordinal));
        }

        private static void TestMicrosoftDeviceCode()
        {
            string json = "{\"device_code\":\"dc123\",\"user_code\":\"ABC-DEF\"," +
                "\"verification_uri\":\"https://microsoft.com/devicelogin\"," +
                "\"interval\":5,\"expires_in\":900}";
            MicrosoftAuthService.DeviceCodeResult r = MicrosoftAuthService.ParseDeviceCode(json);
            Check("device code parsed", r.DeviceCode == "dc123");
            Check("user code parsed", r.UserCode == "ABC-DEF");
            Check("interval parsed", r.IntervalSeconds == 5);
            Check("uri parsed", r.VerificationUri == "https://microsoft.com/devicelogin");
            CheckThrows<InvalidDataException>("empty device code rejected",
                delegate { MicrosoftAuthService.ParseDeviceCode("{\"user_code\":\"X\"}"); });
        }

        private static void TestMicrosoftTokenPoll()
        {
            string pending = "{\"error\":\"authorization_pending\"}";
            MicrosoftAuthService.TokenPollResult p = MicrosoftAuthService.ParseTokenResponse(403, pending);
            Check("pending state", p.Pending && !p.Complete);

            string complete = "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":86400}";
            MicrosoftAuthService.TokenPollResult c = MicrosoftAuthService.ParseTokenResponse(200, complete);
            Check("complete state", c.Complete && c.AccessToken == "at" && c.RefreshToken == "rt");

            string denied = "{\"error\":\"access_denied\",\"error_description\":\"no thanks\"}";
            MicrosoftAuthService.TokenPollResult e = MicrosoftAuthService.ParseTokenResponse(403, denied);
            Check("denied state", !e.Complete && !e.Pending && e.ErrorCode == "access_denied");
            Check("denied message", e.ErrorDescription.Contains("no thanks"));
        }

        private static void TestMicrosoftProtocolBodies()
        {
            string xbl = MicrosoftAuthService.BuildXblBody("oat");
            Check("xbl body has rps ticket", xbl.Contains("\"RpsTicket\":\"d=oat\""));
            Check("xbl body has site", xbl.Contains("user.auth.xboxlive.com"));
            string xsts = MicrosoftAuthService.BuildXstsBody("xblt");
            Check("xsts body has token", xsts.Contains("\"UserTokens\""));
            Check("xsts body has retail", xsts.Contains("RETAIL"));
            Check("xsts relying party", xsts.Contains(MicrosoftAuthService.XstsRelyingParty));
            string mc = MicrosoftAuthService.BuildMcLoginBody("uhs1", "xstst");
            Check("mc body prefix", mc.Contains("XBL3.0 x=uhs1;xstst"));
            string deviceBody = MicrosoftAuthService.BuildDeviceCodeBody("cid");
            Check("device body has scope", deviceBody.Contains("XboxLive.signin"));

            // XSTS 响应解析出 uhs
            string xstsJson = "{\"Token\":\"t\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"u42\"}]}}";
            string uhs;
            string token = MicrosoftAuthService.ParseXstsToken(xstsJson, out uhs);
            Check("xsts token parsed", token == "t");
            Check("xsts uhs parsed", uhs == "u42");
        }

        private static void TestMicrosoftXstsErrors()
        {
            Check("no minecraft error", MicrosoftAuthService.MapXstsError(
                "{\"XErr\":2148916233}").Contains("没有 Minecraft"));
            Check("family error", MicrosoftAuthService.MapXstsError(
                "{\"XErr\":2148916236}").Contains("家庭限制"));
            Check("bad credential error", MicrosoftAuthService.MapXstsError(
                "{\"XErr\":2148916225}").Contains("凭据无效"));
            Check("unknown xerr", MicrosoftAuthService.MapXstsError(
                "{\"XErr\":999999}").Contains("999999"));
            Check("empty maps", MicrosoftAuthService.MapXstsError(null).Length > 0);
        }

        private static void TestMicrosoftConfigRoundtrip()
        {
            LauncherConfig config = new LauncherConfig();
            config.MicrosoftClientId = "01234567-abcd-ef00-1234-56789abcdef0";
            Account account = new Account();
            account.Id = "ms|Steve";
            account.TypeCode = (int)AccountType.Microsoft;
            account.UserName = "Steve";
            account.DisplayName = "Steve";
            account.Uuid = "11111111-2222-3333-4444-555555555555";
            account.AccessToken = "mcat";
            account.MicrosoftRefreshToken = "mrt";
            account.MicrosoftRefreshTokenProtected = CredentialStore.Protect("mrt", "microsoft-refresh");
            config.Accounts = new List<Account>();
            config.Accounts.Add(account);

            string path = Path.Combine(_root, "cfg-ms.json");
            File.WriteAllText(path, JsonUtil.Serialize(config.ToJsonObject()));
            string json = File.ReadAllText(path);
            Check("client id written", json.Contains("01234567-abcd-ef00-1234-56789abcdef0"));
            Check("type written", json.Contains("\"TypeCode\":2"));

            LauncherConfig restored = LauncherConfig.LoadFrom(path);
            Check("client id roundtrip",
                restored.MicrosoftClientId == "01234567-abcd-ef00-1234-56789abcdef0");
            Check("microsoft account kept", restored.Accounts.Count == 1);
            Check("account type roundtrip",
                restored.Accounts[0].TypeCode == (int)AccountType.Microsoft);
            Check("display roundtrip", restored.Accounts[0].GetDisplay().Contains("微软"));
        }

        private static void TestRuntimeRecordFields()
        {
            LauncherConfig config = new LauncherConfig();
            config.LastGameExitCode = -1;
            string path = Path.Combine(_root, "cfg-run.json");
            File.WriteAllText(path, JsonUtil.Serialize(config.ToJsonObject()));
            string json = File.ReadAllText(path);
            Check("exit code written", json.Contains("\"LastGameExitCode\":-1"));
            config.LastGameExitCode = 0;
            config.LastGameRunSeconds = 754;
            config.LastGameExitAtUnix = 1234567890;
            File.WriteAllText(path, JsonUtil.Serialize(config.ToJsonObject()));
            LauncherConfig restored = LauncherConfig.LoadFrom(path);
            Check("exit code roundtrip", restored.LastGameExitCode == 0);
            Check("run seconds roundtrip", restored.LastGameRunSeconds == 754);
            Check("exit at roundtrip", restored.LastGameExitAtUnix == 1234567890);
        }

        /// <summary>
        /// 1.7.2 回归：导航页索引必须从 0 开始连续递增，且与 BuildUi 的构建顺序一致。
        /// PageIndex 定义在 src\PageIndex.cs（不在 Core，测试程序不编译它），
        /// 因此这里校验"约定本身"：10 个页面、索引 0..9 连续，防止再插入页面时漏改。
        /// </summary>
        private static void TestPageIndexCoverage()
        {
            // 页面数量与 PageIndex.About 常量必须同步：0=启动 ... 9=关于。
            const int expectedPageCount = 10;
            Check("page count is 10", expectedPageCount == 10);

            // 索引必须能覆盖 0..9 且无空洞（用数组模拟导航标题表长度）。
            string[] navTitles = new string[]
            {
                "启动", "账号", "下载", "下载任务", "模组",
                "光影", "联机", "设置", "日志", "关于"
            };
            Check("nav titles match pages", navTitles.Length == expectedPageCount);
            for (int i = 0; i < navTitles.Length; i++)
            {
                Check("nav title not empty " + i, navTitles[i].Length > 0);
            }
            Check("shaders is index 5", navTitles[5] == "光影");
            Check("tasks is index 3", navTitles[3] == "下载任务");
        }

        /// <summary>
        /// 1.7.3 回归：下载进度的显示文案。
        /// 关键约束：总量未知时不能显示误导性的 0% / 100%，只能显示已下载字节数。
        /// </summary>
        private static void TestTaskProgressFormatting()
        {
            // 总量未知：未收到任何数据 -> 破折号；已有数据 -> 字节数
            Check("unknown total no bytes", PathUtil.FormatProgress(0, 0) == "—");
            Check("unknown total with bytes",
                PathUtil.FormatProgress(1234567, 0) != "—");
            Check("unknown total shows bytes",
                PathUtil.FormatProgress(1234567, 0).Contains("MB") ||
                PathUtil.FormatProgress(1234567, 0).Contains("KB"));

            // 正常进度
            Check("half is 50", PathUtil.FormatProgress(500, 1000) == "50.0%");
            Check("complete is 100", PathUtil.FormatProgress(1000, 1000) == "100.0%");
            Check("zero is 0", PathUtil.FormatProgress(0, 1000) == "0.0%");

            // 超出（服务端 Content-Length 与实际不符）必须钳制，不能出现 150%
            Check("overflow clamped", PathUtil.FormatProgress(1500, 1000) == "100.0%");
            // 负数不能产生负百分比
            Check("negative received clamped", PathUtil.FormatProgress(-10, 1000) == "0.0%");

            // 时间格式化
            Check("eta zero", PathUtil.FormatDuration(0) == "—");
            Check("eta negative", PathUtil.FormatDuration(-5) == "—");
            Check("eta seconds", PathUtil.FormatDuration(45) == "45 秒");
            Check("eta minute", PathUtil.FormatDuration(90) == "1 分 30 秒");
            Check("eta hour", PathUtil.FormatDuration(3700) == "1 小时 1 分");
        }

        /// <summary>
        /// 1.7.6（B-19）：模组安装目录必须按"目标版本"解析。
        ///
        /// 旧行为：界面用「模组页 Fabric 下拉框」解析目录，但服务层自己拼
        /// &lt;gameDir&gt;\mods —— 隔离模式下两者不一致，模组装到了游戏不读的位置，
        /// 表现就是"每个版本的模组无法分开管理"。
        /// 现在 InstallToModsDirectoryAsync 接受显式目录，落盘位置由调用方决定。
        /// </summary>
        private static void TestModTargetDirectory()
        {
            string gameDir = Path.Combine(_root, "mod-target");
            Directory.CreateDirectory(gameDir);

            // 隔离开：实例目录内。
            string isolated = InstancePaths.GetModsDirectory(gameDir, "fabric-1.20.1", true);
            // 隔离关：共享目录。
            string shared = InstancePaths.GetModsDirectory(gameDir, "fabric-1.20.1", false);
            Check("two versions get distinct mods dirs",
                !string.Equals(isolated, shared, StringComparison.OrdinalIgnoreCase));

            // 两个不同版本在隔离模式下各自的 mods 目录必须互不相同 ——
            // 这是"分别管理各版本 mod"的核心保证。
            string other = InstancePaths.GetModsDirectory(gameDir, "fabric-1.19.2", true);
            Check("different versions isolated",
                !string.Equals(isolated, other, StringComparison.OrdinalIgnoreCase));

            // 目标目录由调用方显式传给服务层：模拟一次落盘检查。
            Directory.CreateDirectory(isolated);
            string jar = PathUtil.SafeCombine(isolated, "demo.jar");
            File.WriteAllText(jar, "demo");
            Check("jar lands in isolated dir", File.Exists(jar));
            Check("jar not in shared dir",
                !File.Exists(Path.Combine(shared, "demo.jar")));
            Check("isolated jar resolves inside instance",
                PathUtil.IsSubPath(Path.GetDirectoryName(isolated), jar));

            // 服务层签名必须同时提供旧入口（兼容）与新入口（显式目录）。
            Check("legacy install api exists",
                typeof(ModrinthService).GetMethod("InstallAsync") != null);
            Check("explicit dir install api exists",
                typeof(ModrinthService).GetMethod("InstallToModsDirectoryAsync") != null);
            Check("explicit dir batch api exists",
                typeof(ModrinthService).GetMethod("InstallManyToModsDirectoryAsync") != null);
        }

        /// <summary>
        /// 1.7.6（B-20）：汇总任务生命周期。
        ///
        /// 游戏安装与模组下载此前完全不进 DownloadManager，于是「下载任务」页看不到它们。
        /// 现在用 BeginJob / UpdateJob / EndJob 把它们登记为一行汇总任务。
        /// </summary>
        private static void TestDownloadJobLifecycle()
        {
            string stateFile = Path.Combine(_root, "job-state.json");
            DownloadManager manager = new DownloadManager(stateFile, CopyExecutor(new byte[] { 1 }));

            DownloadTask job = manager.BeginJob("安装原版 1.20.1", "minecraft");
            Check("job id issued", job != null && !string.IsNullOrEmpty(job.TaskId));
            Check("job flagged", job.IsJob);
            Check("job running on creation", job.State == DownloadTaskState.Running);

            // 汇总任务必须出现在任务列表里（这正是"任务页能看到游戏下载"的依据）。
            List<DownloadTask> listed = manager.GetTasks();
            DownloadTask found = null;
            for (int i = 0; i < listed.Count; i++)
            {
                if (string.Equals(listed[i].TaskId, job.TaskId, StringComparison.Ordinal))
                {
                    found = listed[i];
                    break;
                }
            }
            Check("job listed", found != null && found.IsJob);

            // 进度回填必须被快照带出来（CloneTask 若漏拷 IsJob/Percent 会永远 0%）。
            manager.UpdateJob(job.TaskId, 40);
            DownloadTask mid = manager.GetTask(job.TaskId);
            Check("job percent updated", mid.Percent == 40);
            Check("job percent clamped low", ClampTest(manager, job.TaskId, -5) == 0);
            Check("job percent clamped high", ClampTest(manager, job.TaskId, 250) == 100);

            // 不占并发槽：并发上限设为 1 时，仍能同时登记多个汇总任务。
            manager.MaxConcurrency = 1;
            DownloadTask second = manager.BeginJob("安装模组 sodium", "modrinth");
            Check("second job allowed at concurrency 1", second != null);
            Check("job count is 2", manager.GetTasks().Count >= 2);

            // 汇总任务不持久化：它没有可续传文件请求，写盘会让重启后出现空记录。
            manager.SaveState();
            Check("job state file exists", File.Exists(stateFile));
            if (File.Exists(stateFile))
            {
                string text = File.ReadAllText(stateFile);
                // 汇总任务不应出现在磁盘上（空列表序列化为 []，长度只有 2）。
                Check("jobs not persisted", !text.Contains(job.TaskId));
                Check("job name not persisted", !text.Contains("安装原版 1.20.1"));
            }

            // 结束：完成 -> 100%，失败 / 取消 -> 终态。
            manager.EndJob(job.TaskId, DownloadTaskState.Completed, "", "");
            DownloadTask done = manager.GetTask(job.TaskId);
            Check("job completed", done.State == DownloadTaskState.Completed);
            Check("completed job shows 100", done.Percent == 100);
            Check("completed job is terminal", done.IsTerminal);

            // 重复 EndJob 必须幂等（发起方 finally 里可能重复调用）。
            manager.EndJob(job.TaskId, DownloadTaskState.Failed, "X", "again");
            DownloadTask again = manager.GetTask(job.TaskId);
            Check("end job idempotent", again.State == DownloadTaskState.Completed);

            // 汇总任务不可"重试"（没有文件请求可恢复），Retry 必须直接忽略。
            manager.Retry(job.TaskId);
            Check("job retry ignored", manager.GetTask(job.TaskId).State == DownloadTaskState.Completed);

            // 汇总任务不可"暂停"（只有取消有意义）。
            Check("job pause rejected", !manager.Pause(second.TaskId));

            // 取消：通过回调通知发起方，并把任务收尾为 Canceled。
            bool cancelled = false;
            manager.SetJobCancelCallback(second.TaskId, delegate { cancelled = true; });
            manager.Cancel(second.TaskId);
            Check("job cancel callback fired", cancelled);
            manager.EndJob(second.TaskId, DownloadTaskState.Canceled,
                DownloadManager.ErrorCanceled, "用户取消");
            Check("job canceled", manager.GetTask(second.TaskId).State == DownloadTaskState.Canceled);

            // 清空历史必须把终态汇总任务一并清掉。
            manager.ClearHistory();
            Check("history cleared", manager.GetTasks().Count == 0);
        }

        private static int ClampTest(DownloadManager manager, string taskId, int percent)
        {
            manager.UpdateJob(taskId, percent);
            return manager.GetTask(taskId).Percent;
        }

        /// <summary>1.7.4 回归：光影包版本匹配。
        ///
        /// 光影包在 Modrinth 上的 loader 是 iris / optifine / canvas 等，几乎不会是 "fabric"。
        /// 此前光影查询复用了模组的「必须 fabric」匹配，导致结果恒为空——
        /// 用户看到的就是"找不到可下载的最新版本光影包"。
        /// </summary>
        private static void TestShaderVersionMatching()
        {
            ModVersion iris = new ModVersion();
            iris.Id = "iris";
            iris.VersionType = "release";
            iris.GameVersions.Add("1.20.1");
            iris.Loaders.Add("iris");

            // 模组模式（requireFabric=true）必须判为不匹配：原有语义不能回退。
            Check("iris is not a fabric mod",
                !ModrinthService.Matches(iris, "1.20.1", true));
            // 光影模式（requireFabric=false）必须匹配。
            Check("iris matches as shader",
                ModrinthService.Matches(iris, "1.20.1", false));
            Check("shader selection finds iris",
                ModrinthService.SelectBestVersion(
                    new List<ModVersion> { iris }, "1.20.1", false) == iris);
            Check("mod selection rejects iris",
                ModrinthService.SelectBestVersion(
                    new List<ModVersion> { iris }, "1.20.1", true) == null);

            // 未标注 game_versions 的光影视为不限版本。
            ModVersion unrestricted = new ModVersion();
            unrestricted.Id = "unrestricted";
            unrestricted.VersionType = "release";
            unrestricted.Loaders.Add("optifine");
            Check("unrestricted shader matches any version",
                ModrinthService.Matches(unrestricted, "1.20.1", false));
            Check("unrestricted shader matches empty version",
                ModrinthService.Matches(unrestricted, "", false));

            // release 优先于 beta。
            ModVersion beta = new ModVersion();
            beta.Id = "beta";
            beta.VersionType = "beta";
            beta.GameVersions.Add("1.20.1");
            beta.Loaders.Add("iris");
            Check("release preferred over beta",
                ModrinthService.SelectBestVersion(
                    new List<ModVersion> { beta, iris }, "1.20.1", false) == iris);
        }

        /// <summary>
        /// 1.7.4 回归：配置保存失败必须能被调用方感知。
        /// 此前 Save() 失败时主题色/缩放调用点只记日志，用户表现为"改了设置、重启又还原"。
        /// </summary>
        private static void TestConfigSaveFailure()
        {
            LauncherConfig config = new LauncherConfig();
            config.AccentColor = "green";

            string okPath = Path.Combine(_root, "cfg-save-ok.json");
            config.SaveTo(okPath);
            Check("save to writable path succeeds", File.Exists(okPath));

            // TrySave 绝不能抛异常，只能返回 null 或失败原因字符串。
            string result = config.TrySave();
            Check("TrySave never throws",
                result == null || result.Length > 0);

            // 可写性探测：可写目录必须判为可写。
            Check("writable probe detects writable dir",
                AppPaths.IsDirectoryWritable(_root));
            Check("writable probe rejects empty", !AppPaths.IsDirectoryWritable(""));
        }

        /// <summary>1.7.4 回归：默认游戏目录必须落在可写位置（不可写时自动回退）。</summary>
        private static void TestGameDirectoryFallback()
        {
            string path = AppPaths.DefaultGameDirectory;
            Check("default game dir non-empty", !string.IsNullOrEmpty(path));
            Check("default game dir is writable target",
                AppPaths.IsDirectoryWritable(Path.GetDirectoryName(path)) ||
                AppPaths.IsDirectoryWritable(path));
        }

        /// <summary>
        /// 1.7.2 回归：写入权限异常必须被翻译成可照做的提示，而不是原始 Windows 错误。
        /// 场景：启动器放在 Program Files / 只读目录，用户看到"访问被拒绝"却不知怎么办。
        /// </summary>
        private static void TestUserFacingWritePermission()
        {
            string message = WriteErrorDiagnostics.Describe(
                new UnauthorizedAccessException("Access to the path is denied."),
                "启动游戏", @"C:\Program Files\RLL");
            Check("mentions permission", message.Contains("没有写入权限"));
            Check("shows current game dir", message.Contains(@"C:\Program Files\RLL"));
            Check("gives actionable steps", message.Contains("以管理员身份运行"));
            Check("offers settings fix", message.Contains("设置"));
            Check("is write permission error",
                WriteErrorDiagnostics.IsWritePermissionError(
                    new UnauthorizedAccessException("denied")));

            // 目录不存在
            string missing = WriteErrorDiagnostics.Describe(
                new DirectoryNotFoundException("x"), "启动游戏", @"D:\gone");
            Check("missing dir handled", missing.Contains("目录不存在"));

            // 未知异常仍要给出动作名与原始原因，不能吞掉信息。
            string other = WriteErrorDiagnostics.Describe(
                new InvalidOperationException("某个业务错误"), "启动游戏", @"D:\game");
            Check("other keeps action", other.Contains("启动游戏"));
            Check("other keeps reason", other.Contains("某个业务错误"));
            Check("other is not permission",
                !WriteErrorDiagnostics.IsWritePermissionError(
                    new InvalidOperationException("x")));

            // 包装异常需追到最内层真实原因。
            Exception wrapped = new Exception("外层包装",
                new UnauthorizedAccessException("denied"));
            Check("unwraps inner permission",
                WriteErrorDiagnostics.Describe(wrapped, "启动游戏", @"D:\game")
                    .Contains("没有写入权限"));
            Check("root cause resolved",
                WriteErrorDiagnostics.GetRootCause(wrapped) is UnauthorizedAccessException);

            // null 必须安全（不能抛）。
            Check("null safe", WriteErrorDiagnostics.Describe(null, "启动游戏", @"D:\g").Length > 0);
            Check("null not permission",
                !WriteErrorDiagnostics.IsWritePermissionError((Exception)null));
            // 1.7.5（B-04）：TrySave 返回的是文本，文本版判定必须同样可靠。
            Check("text permission detected",
                WriteErrorDiagnostics.IsWritePermissionError(
                    "对路径“D:\\g\\config.json”的访问被拒绝。"));
            Check("text permission not over-eager",
                !WriteErrorDiagnostics.IsWritePermissionError("磁盘空间不足"));
        }

    }
}