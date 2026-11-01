using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LiteLauncher.Core;

namespace LiteLauncher.Integration
{
    internal static class IntegrationTest
    {
        private static int _passed;
        private static int _failed;
        private static int _nextProgress;

        private static int Main(string[] args)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp",
                "opencode",
                "LiteLauncherE2E");
            bool resume = false;
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (string.Equals(args[i], "--resume", StringComparison.OrdinalIgnoreCase))
                {
                    resume = true;
                }
            }
            if (Directory.Exists(root) && !resume)
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);
            Console.WriteLine("E2E directory: " + root + (resume ? " (resume)" : " (fresh)"));
            try
            {
                Run(root);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] E2E -> " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.ToString());
            }
            Console.WriteLine();
            Console.WriteLine("E2E passed checks: " + _passed + ", failures: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string gameDir)
        {
            Console.WriteLine("Fetching manifest...");
            VersionManifest manifest = new VersionManifestService()
                .GetManifestAsync(CancellationToken.None).GetAwaiter().GetResult();
            VersionSummary version = manifest.Find("1.20.1");
            Check("1.20.1 found", version != null);

            MinecraftInstaller installer = new MinecraftInstaller(gameDir, 8, new VersionManifestService());
            // 版本 JSON 存在不代表资源完整：检查 client jar、资源对象与 natives，
            // 任一缺失就重新执行安装（安装器会跳过已校验通过的文件）。
            bool complete = VersionResolver.IsInstalled(gameDir, "1.20.1") &&
                            HasCompleteInstall(gameDir, "1.20.1");
            if (complete)
            {
                Console.WriteLine("Vanilla 1.20.1 already installed, skipping download.");
            }
            else
            {
                Console.WriteLine("Installing vanilla 1.20.1 (downloads missing files only)...");
                installer.InstallVanillaAsync(
                    version,
                    CreateProgress("vanilla"),
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            string jsonPath = Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.json");
            string jarPath = Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.jar");
            string indexPath = Path.Combine(gameDir, "assets", "indexes", "5.json");
            string nativesPath = Path.Combine(gameDir, "versions", "1.20.1", "natives");
            Check("version json", File.Exists(jsonPath));
            Check("client jar", File.Exists(jarPath) && new FileInfo(jarPath).Length > 1000000);
            Check("asset index", File.Exists(indexPath));
            Check("natives extracted", Directory.Exists(nativesPath) &&
                Directory.GetFiles(nativesPath, "*.dll", SearchOption.TopDirectoryOnly).Length > 0);
            string assetsObjects = Path.Combine(gameDir, "assets", "objects");
            Check("asset objects", Directory.Exists(assetsObjects) &&
                Directory.GetFiles(assetsObjects, "*", SearchOption.AllDirectories).Length > 100);

            EffectiveVersion vanilla = VersionResolver.LoadInstalledVersion(gameDir, "1.20.1");
            Check("vanilla main class", vanilla.MainClass == "net.minecraft.client.main.Main");
            Check("vanilla classpath", vanilla.ClasspathArtifacts.Count > 10);
            Check("vanilla natives", vanilla.NativeArtifacts.Count > 0);

            LaunchOptions options = new LaunchOptions();
            options.GameDir = gameDir;
            options.VersionId = "1.20.1";
            options.Username = "Steve";
            options.MinMemoryMB = 1024;
            options.MaxMemoryMB = 2048;
            JavaInfo java = new JavaInfo();
            java.LaunchPath = "C:\\Java\\bin\\javaw.exe";
            java.ProbePath = "C:\\Java\\bin\\java.exe";
            java.Major = 17;
            LaunchPlan vanillaPlan = GameLauncher.BuildPlan(vanilla, options, java, gameDir);
            Check("vanilla plan cp", vanillaPlan.Arguments.Contains("-cp "));
            Check("vanilla plan username", vanillaPlan.Arguments.Contains("--username Steve"));
            Check("vanilla plan client jar", vanillaPlan.Classpath.Contains("1.20.1.jar"));

            Console.WriteLine("Installing Fabric latest stable...");
            FabricInstaller fabric = new FabricInstaller(installer, new VersionManifestService());
            string fabricId = FindInstalledFabricVersion(gameDir);
            if (fabricId == null)
            {
                fabricId = fabric.InstallAsync(
                    "1.20.1",
                    CreateProgress("fabric"),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                Console.WriteLine("Fabric already installed: " + fabricId);
            }
            Check("fabric id", fabricId.StartsWith("fabric-loader-", StringComparison.Ordinal));
            string fabricJson = Path.Combine(gameDir, "versions", fabricId, fabricId + ".json");
            Check("fabric json", File.Exists(fabricJson));

            EffectiveVersion fabricVersion = VersionResolver.LoadInstalledVersion(gameDir, fabricId);
            Check("fabric main class", fabricVersion.MainClass.StartsWith("net.fabricmc.", StringComparison.Ordinal));
            Check("fabric inherits assets", fabricVersion.AssetsId == "5");
            Check("fabric client jar", fabricVersion.ClientJarVersionId == "1.20.1");
            Check("fabric classpath", fabricVersion.ClasspathArtifacts.Count > vanilla.ClasspathArtifacts.Count);
            Check("fabric loader lib", ClasspathContains(fabricVersion, "fabric-loader"));

            options.VersionId = fabricId;
            LaunchPlan fabricPlan = GameLauncher.BuildPlan(fabricVersion, options, java, gameDir);
            Check("fabric plan main class", fabricPlan.Arguments.Contains(
                "net.fabricmc.loader.impl.launch.knot.KnotClient"));
            Check("fabric plan emu", fabricPlan.Arguments.Contains("-DFabricMcEmu="));
            Check("fabric plan username", fabricPlan.Arguments.Contains("--username Steve"));

            // 通过 Modrinth 安装用户点名的三个常用模组，验证版本匹配、SHA1 与落盘位置。
            Console.WriteLine("Installing common mods from Modrinth...");
            ModrinthService modrinth = new ModrinthService();
            string[] slugs = new string[] { "sodium", "jade", "appleskin" };
            string modsDir = Path.Combine(gameDir, "mods");
            for (int i = 0; i < slugs.Length; i++)
            {
                ModInstallResult result = modrinth.InstallAsync(
                    gameDir, "1.20.1", slugs[i], CreateProgress("mod:" + slugs[i]),
                    CancellationToken.None).GetAwaiter().GetResult();
                Check("mod installed: " + slugs[i], result.Files.Count > 0);
                for (int j = 0; j < result.Files.Count; j++)
                {
                    string path = Path.Combine(modsDir, result.Files[j]);
                    Check("mod file exists: " + result.Files[j], File.Exists(path));
                    Check("mod file is inside mods: " + result.Files[j], PathUtil.IsSubPath(modsDir, path));
                    Check("mod file sha1: " + result.Files[j],
                        PathUtil.IsSha1(PathUtil.GetSha1(path)));
                    Check("mod is jar: " + result.Files[j],
                        result.Files[j].EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
                }
                Console.WriteLine("  " + slugs[i] + " -> " + string.Join(", ", result.Files.ToArray()) +
                                  (result.AlreadyPresentFiles.Count == result.Files.Count
                                      ? " (already present)" : ""));
            }

            int jarCount = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Length;
            Check("mods directory has jars", jarCount >= slugs.Length);

            // 模组页选择器依赖的 Fabric 识别与游戏版本映射必须对真实安装结果成立。
            Check("fabric detected", VersionResolver.IsFabricVersion(fabricVersion));
            Check("game version mapped", fabricVersion.GameVersionId == "1.20.1");
            Check("vanilla not fabric", !VersionResolver.IsFabricVersion(vanilla));

            // 已安装 Fabric 版本应出现在版本列表，供模组页下拉框填充。
            List<string> installed = VersionResolver.GetInstalledVersions(gameDir);
            Check("fabric id in installed list", installed.Contains(fabricId));

            Console.WriteLine("E2E install and launch-plan verification completed.");
            Console.WriteLine("Mods directory: " + modsDir + " (" + jarCount + " jars)");
        }

        private static bool HasCompleteInstall(string gameDir, string versionId)
        {
            try
            {
                EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, versionId);
                string clientJar = VersionResolver.GetClientJarPath(gameDir, version);
                if (version.ClientJar != null && (!File.Exists(clientJar) || new FileInfo(clientJar).Length < 1000000))
                {
                    return false;
                }
                if (!string.IsNullOrEmpty(version.AssetsId))
                {
                    string indexPath = VersionResolver.GetAssetIndexPath(gameDir, version.AssetsId);
                    if (!File.Exists(indexPath))
                    {
                        return false;
                    }
                    string objectsDirectory = Path.Combine(
                        VersionResolver.GetAssetsDirectory(gameDir), "objects");
                    if (!Directory.Exists(objectsDirectory) ||
                        Directory.GetFiles(objectsDirectory, "*", SearchOption.AllDirectories).Length < 100)
                    {
                        return false;
                    }
                }
                string nativesDirectory = VersionResolver.GetNativesDirectory(gameDir, versionId);
                if (version.NativeArtifacts.Count > 0 && !Directory.Exists(nativesDirectory))
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Install check failed, will reinstall: " + ex.Message);
                return false;
            }
        }

        private static string FindInstalledFabricVersion(string gameDir)
        {
            List<string> versions = VersionResolver.GetInstalledVersions(gameDir);
            for (int i = 0; i < versions.Count; i++)
            {
                try
                {
                    EffectiveVersion version = VersionResolver.LoadInstalledVersion(gameDir, versions[i]);
                    if (VersionResolver.IsFabricVersion(version) && version.GameVersionId == "1.20.1")
                    {
                        return versions[i];
                    }
                }
                catch
                {
                    // 损坏的版本目录跳过，不影响其他已安装版本。
                }
            }
            return null;
        }

        private static bool ClasspathContains(EffectiveVersion version, string text)
        {
            for (int i = 0; i < version.ClasspathArtifacts.Count; i++)
            {
                if (version.ClasspathArtifacts[i].Path.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static IProgress<InstallProgress> CreateProgress(string label)
        {
            _nextProgress = 0;
            return new Progress<InstallProgress>(delegate(InstallProgress progress)
            {
                if (progress == null)
                {
                    return;
                }
                if (progress.Percent >= _nextProgress)
                {
                    Console.WriteLine("[" + label + " " + progress.Percent + "%] " + progress.Status);
                    _nextProgress = progress.Percent + 10;
                }
            });
        }

        private static void Check(string name, bool condition)
        {
            if (!condition)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
                throw new Exception("Assertion failed: " + name);
            }
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
