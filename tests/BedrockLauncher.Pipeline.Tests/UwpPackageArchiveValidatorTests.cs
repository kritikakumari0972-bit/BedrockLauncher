using System.IO;
using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class UwpPackageArchiveValidatorTests
    {
        private const string ValidManifest = """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Microsoft.MinecraftUWP"
                        Publisher="CN=Microsoft Corporation"
                        Version="1.21.3003.0"
                        ProcessorArchitecture="x64" />
            </Package>
            """;

        [Fact]
        public void AcceptsExactFamilyFourPartVersionAndArchitecture()
        {
            UwpPackageManifestIdentity identity = UwpPackageArchiveValidator.Validate(
                ValidManifest,
                "Microsoft.MinecraftUWP",
                "x64");

            Assert.Equal("Microsoft.MinecraftUWP", identity.Name);
            Assert.Equal(new System.Version(1, 21, 3003, 0), identity.Version);
            Assert.Equal("x64", identity.Architecture);
        }

        [Fact]
        public void RejectsPreviewPackageForReleaseVersion()
        {
            string manifest = ValidManifest.Replace(
                "Microsoft.MinecraftUWP",
                "Microsoft.MinecraftWindowsBeta");

            Assert.Throws<InvalidDataException>(() =>
                UwpPackageArchiveValidator.Validate(manifest, "Microsoft.MinecraftUWP", "x64"));
        }

        [Fact]
        public void RejectsArchitectureMismatch()
        {
            string manifest = ValidManifest.Replace("ProcessorArchitecture=\"x64\"", "ProcessorArchitecture=\"arm64\"");

            Assert.Throws<InvalidDataException>(() =>
                UwpPackageArchiveValidator.Validate(manifest, "Microsoft.MinecraftUWP", "x64"));
        }

        [Theory]
        [InlineData("1.21.3003")]
        [InlineData("not-a-version")]
        [InlineData("")]
        public void RejectsMissingOrNonFourPartPackageVersion(string version)
        {
            string manifest = ValidManifest.Replace("1.21.3003.0", version);

            Assert.Throws<InvalidDataException>(() =>
                UwpPackageArchiveValidator.Validate(manifest, "Microsoft.MinecraftUWP", "x64"));
        }

        [Fact]
        public void RejectsManifestWithoutAnIdentityElement()
        {
            Assert.Throws<InvalidDataException>(() =>
                UwpPackageArchiveValidator.Validate("<Package />", "Microsoft.MinecraftUWP", "x64"));
        }

        [Fact]
        public void RejectsUnknownExpectedArchitecture()
        {
            Assert.Throws<InvalidDataException>(() =>
                UwpPackageArchiveValidator.Validate(ValidManifest, "Microsoft.MinecraftUWP", "???"));
        }
    }
}
