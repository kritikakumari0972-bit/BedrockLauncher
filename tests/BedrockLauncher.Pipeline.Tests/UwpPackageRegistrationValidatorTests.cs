using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class UwpPackageRegistrationValidatorTests
    {
        private const string Family = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        private const string Architecture = "x64";
        private const string Version = "1.26.4005.0";
        private const string Path = @"C:\BedrockLauncher\versions\1.26.4005.0";

        [Fact]
        public void AcceptsOnlyExactFamilyArchitectureVersionAndLauncherFolder()
        {
            Assert.True(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Architecture, "X64", Version, Version));
        }

        [Fact]
        public void RejectsStoreRegistrationEvenWhenFamilyArchitectureAndVersionMatch()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, @"C:\Program Files\WindowsApps\Minecraft", Architecture, "x64", Version, Version));
        }

        [Fact]
        public void RejectsAnotherPackageFamily()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", Path, Path, Architecture, Architecture, Version, Version));
        }

        [Fact]
        public void RejectsDifferentRegisteredArchitecture()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Architecture, "arm64", Version, Version));
        }

        [Fact]
        public void RejectsMissingArchitecture()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Architecture, "", Version, Version));
        }

        [Fact]
        public void RejectsDifferentRegisteredPackageVersion()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Architecture, Architecture, Version, "1.26.5203.0"));
        }

        [Fact]
        public void RejectsMissingOrInvalidPackageVersion()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Architecture, Architecture, Version, "1.26.4005"));
        }

        [Fact]
        public void NormalizesPathCaseAndTrailingSeparators()
        {
            Assert.True(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path + "\\",
                @"c:\bedrocklauncher\VERSIONS\1.26.4005.0\", Architecture, Architecture, Version, Version));
        }

        [Theory]
        [InlineData(null, Path)]
        [InlineData("", Path)]
        [InlineData(Path, null)]
        [InlineData(Path, "")]
        public void RejectsMissingInstallDirectory(string expectedPath, string installedPath)
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, expectedPath, installedPath, Architecture, Architecture, Version, Version));
        }
    }
}
