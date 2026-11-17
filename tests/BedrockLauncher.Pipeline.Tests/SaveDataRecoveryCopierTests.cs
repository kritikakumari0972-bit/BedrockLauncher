using System;
using System.IO;
using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class SaveDataRecoveryCopierTests
    {
        [Fact]
        public void CopiesWorldFilesAndEmptyDirectoriesBeforePromotingDestination()
        {
            string root = CreateRoot();
            try
            {
                string source = Path.Combine(root, "source");
                string destination = Path.Combine(root, "install", "packageData");
                Directory.CreateDirectory(Path.Combine(source, "minecraftWorlds", "world-one"));
                Directory.CreateDirectory(Path.Combine(source, "resource_packs", "empty"));
                File.WriteAllText(Path.Combine(source, "level.dat"), "world-meta");
                File.WriteAllText(Path.Combine(source, "minecraftWorlds", "world-one", "levelname.txt"), "Test world");

                SaveDataRecoveryCopyResult result = SaveDataRecoveryCopier.CopyDirectoryTransactional(
                    source, destination);

                Assert.True(Directory.Exists(destination));
                Assert.Equal(2, result.FileCount);
                Assert.True(result.DirectoryCount >= 2);
                Assert.Equal(20, result.BytesCopied);
                Assert.Equal("world-meta", File.ReadAllText(Path.Combine(destination, "level.dat")));
                Assert.Equal("Test world",
                    File.ReadAllText(Path.Combine(destination, "minecraftWorlds", "world-one", "levelname.txt")));
                Assert.True(Directory.Exists(Path.Combine(destination, "resource_packs", "empty")));
                Assert.True(Directory.Exists(source));
                Assert.Equal("world-meta", File.ReadAllText(Path.Combine(source, "level.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void FailedCopyDoesNotPromotePartialRecoveryInstallation()
        {
            string root = CreateRoot();
            try
            {
                string source = Path.Combine(root, "source");
                string destination = Path.Combine(root, "install", "packageData");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "one.dat"), "first");
                File.WriteAllText(Path.Combine(source, "two.dat"), "second");

                Assert.Throws<OperationCanceledException>(() =>
                    SaveDataRecoveryCopier.CopyDirectoryTransactional(
                        source,
                        destination,
                        (copied, total) => throw new OperationCanceledException("cancel test")));

                Assert.False(Directory.Exists(destination));
                Assert.Empty(Directory.GetDirectories(
                    Path.GetDirectoryName(destination),
                    Path.GetFileName(destination) + ".recovery-staging-*"));
                Assert.Equal("first", File.ReadAllText(Path.Combine(source, "one.dat")));
                Assert.Equal("second", File.ReadAllText(Path.Combine(source, "two.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void ExistingDestinationIsNeverOverwritten()
        {
            string root = CreateRoot();
            try
            {
                string source = Path.Combine(root, "source");
                string destination = Path.Combine(root, "recovery");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(source, "new.dat"), "new");
                File.WriteAllText(Path.Combine(destination, "old.dat"), "keep");

                Assert.Throws<IOException>(() =>
                    SaveDataRecoveryCopier.CopyDirectoryTransactional(source, destination));

                Assert.False(File.Exists(Path.Combine(destination, "new.dat")));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "old.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void RejectsSourceAndDestinationThatOverlap()
        {
            string root = CreateRoot();
            try
            {
                string source = Path.Combine(root, "source");
                string destination = Path.Combine(source, "nested-recovery");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "level.dat"), "keep");

                Assert.Throws<InvalidOperationException>(() =>
                    SaveDataRecoveryCopier.CopyDirectoryTransactional(source, destination));

                Assert.False(Directory.Exists(destination));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "level.dat")));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void RejectsMissingSourceWithoutCreatingDestination()
        {
            string root = CreateRoot();
            try
            {
                string source = Path.Combine(root, "missing");
                string destination = Path.Combine(root, "recovery");

                Assert.Throws<DirectoryNotFoundException>(() =>
                    SaveDataRecoveryCopier.CopyDirectoryTransactional(source, destination));

                Assert.False(Directory.Exists(destination));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "BedrockLauncher-RecoveryTests-" + Guid.NewGuid().ToString("N"));
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
