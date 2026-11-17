using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BedrockLauncher.Handlers
{
    public sealed class SaveDataRecoveryCopyResult
    {
        public int FileCount { get; internal set; }
        public int DirectoryCount { get; internal set; }
        public long BytesCopied { get; internal set; }
    }

    /// <summary>
    /// Copies external Minecraft save data into a new installation using a staging directory.
    /// The destination only becomes visible after the complete copy and its basic integrity checks pass.
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

                    // Do not overwrite anything already present in the unique staging directory.
                    File.Copy(file.FullPath, target, overwrite: false);

                    var sourceInfoAfterCopy = new FileInfo(file.FullPath);
                    sourceInfoAfterCopy.Refresh();
                    var targetInfo = new FileInfo(target);

                    if (!targetInfo.Exists || targetInfo.Length != file.Length ||
                        !sourceInfoAfterCopy.Exists ||
                        sourceInfoAfterCopy.Length != file.Length ||
                        sourceInfoAfterCopy.LastWriteTimeUtc != file.LastWriteTimeUtc)
                    {
                        throw new IOException(
                            "A save-data file changed or was copied incompletely: " + file.RelativePath);
                    }

                    copiedBytes = checked(copiedBytes + targetInfo.Length);
                    progress?.Invoke(copiedBytes, totalBytes);
                }

                VerifyStagedFiles(staging, files);

                // Staging is a sibling of destination, so this promotion is a same-volume directory rename.
                Directory.Move(staging, destination);

                return new SaveDataRecoveryCopyResult
                {
                    FileCount = files.Count,
                    DirectoryCount = directories.Count,
                    BytesCopied = copiedBytes
                };
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

        private static void VerifyStagedFiles(string staging, List<SourceFile> expectedFiles)
        {
            var expected = expectedFiles.ToDictionary(
                file => file.RelativePath,
                file => file.Length,
                StringComparer.OrdinalIgnoreCase);

            var actual = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
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
                        actual.Add(relativePath, file.Length);
                    }
                }
            }

            if (expected.Count != actual.Count ||
                expected.Any(pair => !actual.TryGetValue(pair.Key, out long length) || length != pair.Value))
            {
                throw new IOException("The staged save-data copy did not pass file-count and length verification.");
            }
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
