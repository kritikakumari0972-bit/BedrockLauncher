using System;
using System.IO;
using System.Linq;

namespace BedrockLauncher.Handlers
{
    /// <summary>
    /// Moves Minecraft worlds out of the Windows package data tree before the package is removed.
    /// It deliberately uses a same-volume directory rename: it never falls back to a partial copy.
    /// </summary>
    public static class SaveDataBackupMover
    {
        public static bool HasContent(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A save-data directory is required.", nameof(directory));

            return Directory.Exists(directory) &&
                   Directory.EnumerateFileSystemEntries(directory).Any();
        }

        /// <summary>
        /// Moves a non-empty com.mojang directory to a unique backup folder. The backup must be on the
        /// same volume and outside the package data tree, otherwise Windows could delete it with the package.
        /// Returns null for an empty directory.
        /// </summary>
        public static string MoveDirectoryToBackup(
            string sourceDirectory,
            string packageDataPath,
            string backupRoot,
            string packageFamilyName)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source save-data directory is required.", nameof(sourceDirectory));
            if (string.IsNullOrWhiteSpace(packageDataPath))
                throw new ArgumentException("A package data path is required.", nameof(packageDataPath));
            if (string.IsNullOrWhiteSpace(backupRoot))
                throw new ArgumentException("A backup root is required.", nameof(backupRoot));
            if (string.IsNullOrWhiteSpace(packageFamilyName))
                throw new ArgumentException("A package family name is required.", nameof(packageFamilyName));

            string source = Path.GetFullPath(sourceDirectory);
            string packageRoot = Path.GetFullPath(packageDataPath);
            string destinationRoot = Path.GetFullPath(backupRoot);

            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("The Minecraft save-data directory no longer exists.");

            if (!HasContent(source))
                return null;

            if (IsSameOrDescendant(destinationRoot, packageRoot))
            {
                throw new InvalidOperationException(
                    "The save-data backup folder must be outside the Windows package data directory.");
            }

            string sourceVolume = Path.GetPathRoot(source);
            string destinationVolume = Path.GetPathRoot(destinationRoot);
            if (!string.Equals(sourceVolume, destinationVolume, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The save-data backup must be on the same volume as the package data so the move is atomic.");
            }

            Directory.CreateDirectory(destinationRoot);

            string safeFamilyName = string.Concat(packageFamilyName.Select(character =>
                char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-'
                    ? character
                    : '_'));
            string backupName = $"{safeFamilyName}_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}";
            string backupPath = Path.Combine(destinationRoot, backupName);

            // Directory.Move is atomic on the same volume and fails without removing the source if it cannot move.
            Directory.Move(source, backupPath);

            if (!Directory.Exists(backupPath))
            {
                throw new IOException("Windows did not retain the save-data directory at the expected backup path.");
            }

            return backupPath;
        }

        private static bool IsSameOrDescendant(string candidatePath, string parentPath)
        {
            string candidate = TrimEndingSeparators(Path.GetFullPath(candidatePath));
            string parent = TrimEndingSeparators(Path.GetFullPath(parentPath));

            if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
                return true;

            string parentPrefix = parent + Path.DirectorySeparatorChar;
            return candidate.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
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
