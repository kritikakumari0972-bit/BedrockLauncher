using System;
using System.IO;
using System.Linq;
using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class GdkPackageCacheIntegrityTests
    {
        [Fact]
        public void CreatesLocalReceiptAndReusesItForTheExactPackage()
        {
            string root = CreateTempDirectory();
            string package = Path.Combine(root, "Minecraft.msixvc");
            byte[] bytes = Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();

            try
            {
                File.WriteAllBytes(package, bytes);

                Assert.True(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package,
                    "Microsoft.MinecraftUWP_1.26.4005.0_x64__8wekyb3d8bbwe",
                    out bool created,
                    out string reason));
                Assert.True(created);
                Assert.Empty(reason);

                string receipt = GdkPackageCacheIntegrity.GetReceiptPath(package);
                Assert.True(File.Exists(receipt));
                string[] receiptLines = File.ReadAllLines(receipt);
                Assert.Equal("BEDROCKLAUNCHER-GDK-CACHE-SHA256-V1", receiptLines[0]);
                Assert.Equal(bytes.Length.ToString(), receiptLines[2]);
                Assert.Matches("^[A-F0-9]{64}$", receiptLines[3]);

                Assert.True(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package,
                    "Microsoft.MinecraftUWP_1.26.4005.0_x64__8wekyb3d8bbwe",
                    out created,
                    out reason));
                Assert.False(created);
                Assert.Empty(reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void DetectsSameLengthPackageTampering()
        {
            string root = CreateTempDirectory();
            string package = Path.Combine(root, "Minecraft.msixvc");

            try
            {
                File.WriteAllBytes(package, new byte[] { 1, 2, 3, 4, 5 });
                Assert.True(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package, "exact-package", out _, out _));

                // Same length, different bytes: a length-only check would miss this.
                File.WriteAllBytes(package, new byte[] { 5, 4, 3, 2, 1 });

                Assert.False(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package, "exact-package", out _, out string reason));
                Assert.Contains("SHA-256", reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void RejectsReceiptBoundToAnotherPackageIdentity()
        {
            string root = CreateTempDirectory();
            string package = Path.Combine(root, "Minecraft.msixvc");

            try
            {
                File.WriteAllBytes(package, new byte[] { 1, 2, 3 });
                Assert.True(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package, "package-one", out _, out _));

                Assert.False(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package, "package-two", out _, out string reason));
                Assert.Contains("different package identity", reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void RefusesToFingerprintEmptyPackage()
        {
            string root = CreateTempDirectory();
            string package = Path.Combine(root, "Minecraft.msixvc");

            try
            {
                File.WriteAllBytes(package, Array.Empty<byte>());

                Assert.False(GdkPackageCacheIntegrity.VerifyOrCreate(
                    package, "exact-package", out bool created, out string reason));
                Assert.False(created);
                Assert.Contains("empty", reason);
                Assert.False(File.Exists(GdkPackageCacheIntegrity.GetReceiptPath(package)));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "BedrockLauncher-GdkCacheTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
