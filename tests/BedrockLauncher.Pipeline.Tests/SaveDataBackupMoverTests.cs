using System;
using System.IO;
using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class SaveDataBackupMoverTests
    {
        [Fact]
        public void MovesWorldDataOutsidePackageTreeAndPreservesFiles()
        {
            string root = CreateRoot();
            try
            {
                string packageData = Path.Combine(root, "Packages", "MinecraftFamily");
                string source = Path.Combine(packageData, "LocalState", "games", "com.mojang");
                string backupRoot = Path.Combine(root, "BedrockLauncher", "SaveDataBackups");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "level.dat"), "world-data");
                Directory.CreateDirectory(Path.Combine(source, "minecraftWorlds", "world-one"));
                File.WriteAllText(
                    Path.Combine(source, "minecraftWorlds", "world-one", "levelname.txt"),
                    "Test world");

                string backup = SaveDataBackupMover.MoveDirectoryToBackup(
                    source,
                    packageData,
                    backupRoot,
                    "MinecraftFamily");

                Assert.False(Directory.Exists(source));
                Assert.NotNull(backup);
                Assert.StartsWith(Path.GetFullPath(backupRoot), Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase);
                Assert.Equal("world-data", File.ReadAllText(Path.Combine(backup, "level.dat")));
                Assert.Equal(
                    "Test world",
                    File.ReadAllText(Path.Combine(backup, "minecraftWorlds", "world-one", "levelname.txt")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void RejectsBackupInsidePackageTreeWithoutMovingWorlds()
        {
            string root = CreateRoot();
            try
            {
                string packageData = Path.Combine(root, "Packages", "MinecraftFamily");
                string source = Path.Combine(packageData, "LocalState", "games", "com.mojang");
                string backupInsidePackage = Path.Combine(packageData, "launcher-backups");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "level.dat"), "keep-me");

                Assert.Throws<InvalidOperationException>(() =>
                    SaveDataBackupMover.MoveDirectoryToBackup(
                        source,
                        packageData,
                        backupInsidePackage,
                        "MinecraftFamily"));

                Assert.True(Directory.Exists(source));
                Assert.Equal("keep-me", File.ReadAllText(Path.Combine(source, "level.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void BackupCreationFailureLeavesOriginalWorldDataInPlace()
        {
            string root = CreateRoot();
            try
            {
                string packageData = Path.Combine(root, "Packages", "MinecraftFamily");
                string source = Path.Combine(packageData, "LocalState", "games", "com.mojang");
                string backupRootFile = Path.Combine(root, "backup-root-is-a-file");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "level.dat"), "keep-me");
                File.WriteAllText(backupRootFile, "not-a-directory");

                Assert.Throws<IOException>(() =>
                    SaveDataBackupMover.MoveDirectoryToBackup(
                        source,
                        packageData,
                        backupRootFile,
                        "MinecraftFamily"));

                Assert.True(Directory.Exists(source));
                Assert.Equal("keep-me", File.ReadAllText(Path.Combine(source, "level.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void EmptySaveDataDirectoryDoesNotCreateAnUnnecessaryBackup()
        {
            string root = CreateRoot();
            try
            {
                string packageData = Path.Combine(root, "Packages", "MinecraftFamily");
                string source = Path.Combine(packageData, "LocalState", "games", "com.mojang");
                string backupRoot = Path.Combine(root, "BedrockLauncher", "SaveDataBackups");
                Directory.CreateDirectory(source);

                string backup = SaveDataBackupMover.MoveDirectoryToBackup(
                    source,
                    packageData,
                    backupRoot,
                    "MinecraftFamily");

                Assert.Null(backup);
                Assert.True(Directory.Exists(source));
                Assert.False(Directory.Exists(backupRoot));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "BedrockLauncher-SaveDataTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteRoot(string root)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
