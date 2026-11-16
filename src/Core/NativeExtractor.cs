using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace LiteLauncher.Core
{
    public static class NativeExtractor
    {
        private const long MaxEntryBytes = 256L * 1024L * 1024L;
        private const long MaxTotalBytes = 1024L * 1024L * 1024L;

        public static void Extract(
            string librariesDirectory,
            string nativesDirectory,
            List<NativeArtifact> artifacts,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            Extract(
                librariesDirectory,
                nativesDirectory,
                "",
                artifacts,
                progress,
                cancellationToken);
        }

        public static void Extract(
            string librariesDirectory,
            string nativesDirectory,
            string relativeDirectory,
            List<NativeArtifact> artifacts,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(nativesDirectory))
            {
                throw new ArgumentNullException("nativesDirectory");
            }
            if (Directory.Exists(nativesDirectory))
            {
                Directory.Delete(nativesDirectory, true);
            }
            Directory.CreateDirectory(nativesDirectory);

            string targetDirectory = nativesDirectory;
            if (!string.IsNullOrEmpty(relativeDirectory))
            {
                targetDirectory = PathUtil.SafeCombine(nativesDirectory, relativeDirectory);
            }
            Directory.CreateDirectory(targetDirectory);

            if (artifacts == null || artifacts.Count == 0)
            {
                return;
            }

            long totalBytes = 0;
            HashSet<string> extractedNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < artifacts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NativeArtifact artifact = artifacts[i];
                if (artifact == null || artifact.Download == null)
                {
                    continue;
                }
                string jarPath = PathUtil.SafeCombine(librariesDirectory, artifact.Download.Path);
                if (!File.Exists(jarPath))
                {
                    throw new FileNotFoundException("本地库文件不存在：" + jarPath, jarPath);
                }
                if (progress != null)
                {
                    progress.Report(new InstallProgress(
                        90 + (int)((i * 9.0) / artifacts.Count),
                        "解压本地库 " + (i + 1) + "/" + artifacts.Count));
                }

                using (ZipArchive archive = ZipFile.OpenRead(jarPath))
                {
                    for (int j = 0; j < archive.Entries.Count; j++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = archive.Entries[j];
                        string entryName = entry.FullName;
                        if (string.IsNullOrEmpty(entryName) ||
                            entryName.EndsWith("/", StringComparison.Ordinal) ||
                            entryName.EndsWith("\\", StringComparison.Ordinal) ||
                            entryName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) ||
                            entryName.StartsWith("META-INF\\", StringComparison.OrdinalIgnoreCase) ||
                            entryName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        // 只解压真正的本地库，忽略 .sha1、.git、module-info.class 等伴随文件。
                        if (!PathUtil.IsSafeRelativePath(entryName))
                        {
                            throw new InvalidDataException("本地库条目路径无效：" + entryName);
                        }
                        string fileName = Path.GetFileName(entryName);
                        if (string.IsNullOrEmpty(fileName) || !IsNativeBinary(fileName))
                        {
                            continue;
                        }
                        if (!extractedNames.Add(fileName))
                        {
                            throw new InvalidDataException("本地库包含重名文件：" + fileName);
                        }

                        long entryLength = entry.Length;
                        if (entryLength < 0 || entryLength > MaxEntryBytes)
                        {
                            throw new InvalidDataException("本地库条目过大：" + entryName);
                        }
                        totalBytes += entryLength;
                        if (totalBytes > MaxTotalBytes)
                        {
                            throw new InvalidDataException("本地库解压总量过大。");
                        }

                        // 旧版把 -Djava.library.path 指向 natives 根目录，因此扁平化到根目录；
                        // 26.2+ 指向 natives/java，调用方会传入相对目录。
                        string destination = PathUtil.SafeCombine(targetDirectory, fileName);
                        using (Stream source = entry.Open())
                        using (FileStream target = new FileStream(
                            destination,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            false))
                        {
                            CopyStream(source, target, cancellationToken);
                        }
                    }
                }
            }
        }

        private static bool IsNativeBinary(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }
            return string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".so", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".dylib", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jnilib", StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyStream(Stream source, Stream target, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                target.Write(buffer, 0, read);
            }
        }
    }
}
