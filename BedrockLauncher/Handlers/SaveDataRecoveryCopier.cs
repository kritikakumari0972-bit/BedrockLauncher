using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BedrockLauncher.Handlers
{
    public sealed class SaveDataRecoveryCopyResult
    {
        public int FileCount { get; internal set; }
        public int DirectoryCount { get; internal set; }
        public long BytesCopied { get; internal set; }
        public string ManifestSha256 { get; internal set; }
    }

    /// <summary>
    /// Copies external Minecraft save data into a new installation using a staging directory.
    /// The destination only becomes visible after the complete copy and its SHA-256 integrity checks pass.
    /// Source data is never moved or deleted by this class.
    /// </summary>
    public static class SaveDataRecoveryCopier
    {
        private sealed class SourceFile
        {
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public long Length { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public string Sha256 { get; set; }
        }

        public static SaveDataRecoveryCopyResult CopyDirectoryTransactional(
            string sourceDirectory,
            string destinationDirectory,
            Action<long, long> progress = null)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source directory is required.", nameof(sourceDirectory));
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));

            string source = Path.GetFullPath(sourceDirectory);
            string destination = Path.GetFullPath(destinationDirectory);

            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("The save-data source directory does not exist.");

            DirectoryInfo sourceInfo = new DirectoryInfo(source);
            if (sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("The save-data source is a link, not a real directory.");

            if (PathsOverlap(source, destination))
                throw new InvalidOperationException("The save-data source and recovery destination cannot overlap.");

            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("The recovery destination already exists; existing data will not be overwritten.");

            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException("The recovery destination must have a parent directory.");

            Directory.CreateDirectory(parent);
            string staging = destination + ".recovery-staging-" + Guid.NewGuid().ToString("N");

            try
            {
                List<string> directories = new List<string>();
                List<SourceFile> files = CaptureSourceTree(source, directories);
                long totalBytes = files.Aggregate(0L, (total, file) => checked(total + file.Length));
                long copiedBytes = 0;

                Directory.CreateDirectory(staging);

                foreach (string relativeDirectory in directories)
                {
                    string targetDirectory = Path.Combine(staging, relativeDirectory);
                    Directory.CreateDirectory(targetDirectory);
                }

                foreach (SourceFile file in files)
                {
                    string target = Path.Combine(staging, file.RelativePath);
                    string targetParent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetParent))
                        Directory.CreateDirectory(targetParent);

                    // Keep the source open without write/delete sharing while copying. Hash the exact bytes
                    // read from the source, then independently hash the staged file before accepting it.
                    file.Sha256 = CopyFileAndComputeSha256(file, target);

                    var sourceInfoAfterCopy = new FileInfo(file.FullPath);
                    sourceInfoAfterCopy.Refresh();
                    var targetInfo = new FileInfo(target);

                    if (!targetInfo.Exists || targetInfo.Length != file.Length ||
                        sourceInfoAfterCopy.Length != file.Length ||
                        sourceInfoAfterCopy.LastWriteTimeUtc != file.LastWriteTimeUtc ||
                        !string.Equals(file.Sha256, ComputeSha256(target), StringComparison.Ordinal))
                    {
                        throw new IOException(
                            "A save-data file changed or failed SHA-256 verification: " + file.RelativePath);
                    }

                    copiedBytes = checked(copiedBytes + targetInfo.Length);
                    progress?.Invoke(copiedBytes, totalBytes);
                }

                VerifySourceUnchanged(source, directories, files);
                VerifyStagedFiles(staging, files);

                // Finish all hashing and result construction before promotion. After Directory.Move succeeds,
                // returning the prepared result must not require more I/O that could leave an orphan installation.
                string manifestSha256 = ComputeManifestSha256(files);
                var result = new SaveDataRecoveryCopyResult
                {
                    FileCount = files.Count,
                    DirectoryCount = directories.Count,
                    BytesCopied = copiedBytes,
                    ManifestSha256 = manifestSha256
                };

                // Staging is a sibling of destination, so this promotion is a same-volume directory rename.
                Directory.Move(staging, destination);
                return result;
            }
            catch
            {
                try
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, recursive: true);
                }
                catch
                {
                    // Keep the original copy failure as the reported exception. A leftover staging folder is
                    // never treated as a completed recovery installation.
                }

                throw;
            }
        }

        private static List<SourceFile> CaptureSourceTree(string source, List<string> directories)
        {
            var files = new List<SourceFile>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(source));

            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    // Reparse points can escape the source tree; do not follow them or copy their targets.
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    string relativePath = Path.GetRelativePath(source, entry.FullName);
                    if (relativePath == "." || Path.IsPathRooted(relativePath) ||
                        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
                    {
                        throw new InvalidDataException("A save-data entry resolved outside the selected source folder.");
                    }

                    if (entry is DirectoryInfo subDirectory)
                    {
                        directories.Add(relativePath);
                        pending.Push(subDirectory);
                    }
                    else if (entry is FileInfo file)
                    {
                        files.Add(new SourceFile
                        {
                            FullPath = file.FullName,
                            RelativePath = relativePath,
                            Length = file.Length,
                            LastWriteTimeUtc = file.LastWriteTimeUtc
                        });
                    }
                }
            }

            return files;
        }

        private static void VerifySourceUnchanged(
            string source,
            List<string> expectedDirectories,
            List<SourceFile> expectedFiles)
        {
            var actualDirectories = new List<string>();
            List<SourceFile> actualFiles = CaptureSourceTree(source, actualDirectories);

            var expectedDirectorySet = new HashSet<string>(
                expectedDirectories, StringComparer.OrdinalIgnoreCase);
            var actualDirectorySet = new HashSet<string>(
                actualDirectories, StringComparer.OrdinalIgnoreCase);

            if (!expectedDirectorySet.SetEquals(actualDirectorySet))
                throw new IOException("The source save-data folder structure changed during recovery.");

            var expected = expectedFiles.ToDictionary(
                file => file.RelativePath, file => file, StringComparer.OrdinalIgnoreCase);
            var actual = actualFiles.ToDictionary(
                file => file.RelativePath, file => file, StringComparer.OrdinalIgnoreCase);

            if (expected.Count != actual.Count)
                throw new IOException("The source save-data file list changed during recovery.");

            foreach (KeyValuePair<string, SourceFile> pair in expected)
            {
                if (!actual.TryGetValue(pair.Key, out SourceFile current) ||
                    current.Length != pair.Value.Length ||
                    current.LastWriteTimeUtc != pair.Value.LastWriteTimeUtc)
                {
                    throw new IOException(
                        "A save-data file changed while recovery was copying it: " + pair.Key);
                }
            }
        }

        private static void VerifyStagedFiles(string staging, List<SourceFile> expectedFiles)
        {
            var expected = expectedFiles.ToDictionary(
                file => file.RelativePath,
                file => file,
                StringComparer.OrdinalIgnoreCase);

            var actual = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(staging));

            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidDataException("The recovery staging folder unexpectedly contains a link.");

                    if (entry is DirectoryInfo subDirectory)
                    {
                        pending.Push(subDirectory);
                    }
                    else if (entry is FileInfo file)
                    {
                        string relativePath = Path.GetRelativePath(staging, file.FullName);
                        actual.Add(relativePath, file);
                    }
                }
            }

            if (expected.Count != actual.Count)
                throw new IOException("The staged save-data copy did not pass file-count verification.");

            foreach (KeyValuePair<string, SourceFile> pair in expected)
            {
                if (!actual.TryGetValue(pair.Key, out FileInfo stagedFile) ||
                    stagedFile.Length != pair.Value.Length ||
                    !string.Equals(
                        pair.Value.Sha256,
                        ComputeSha256(stagedFile.FullName),
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        "The staged save-data file failed SHA-256 verification: " + pair.Key);
                }
            }
        }

        private static string CopyFileAndComputeSha256(SourceFile file, string destination)
        {
            using var source = new FileStream(
                file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            using var target = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            if (source.Length != file.Length)
                throw new IOException("A save-data file changed before it could be copied: " + file.RelativePath);

            byte[] buffer = new byte[1024 * 1024];
            int read;
            long total = 0;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                target.Write(buffer, 0, read);
                total = checked(total + read);
            }

            target.Flush(flushToDisk: true);
            if (total != file.Length)
                throw new IOException("A save-data file was truncated while being copied: " + file.RelativePath);

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static string ComputeSha256(string path)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static string ComputeManifestSha256(List<SourceFile> files)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (SourceFile file in files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                byte[] entry = Encoding.UTF8.GetBytes(
                    file.RelativePath.Replace(Path.DirectorySeparatorChar, '/') + "\0" +
                    file.Sha256 + "\n");
                hash.AppendData(entry);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static bool PathsOverlap(string first, string second)
        {
            return IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);
        }

        private static bool IsSameOrDescendant(string candidate, string parent)
        {
            candidate = TrimEndingSeparators(Path.GetFullPath(candidate));
            parent = TrimEndingSeparators(Path.GetFullPath(parent));

            if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
                return true;

            return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimEndingSeparators(string path)
        {
            string root = Path.GetPathRoot(path);
            while (path.Length > root.Length &&
                   (path[path.Length - 1] == Path.DirectorySeparatorChar ||
                    path[path.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }
    }
}
