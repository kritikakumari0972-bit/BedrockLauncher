using BedrockLauncher.Handlers;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class UwpPackageRegistrationValidatorTests
    {
        private const string Family = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        private const string Version = "1.26.4005.0";
        private const string Path = @"C:\BedrockLauncher\versions\1.26.4005.0";

        [Fact]
        public void AcceptsOnlyExactFamilyVersionAndLauncherFolder()
        {
            Assert.True(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Version, Version));
        }

        [Fact]
        public void RejectsStoreRegistrationEvenWhenFamilyAndVersionMatch()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, @"C:\Program Files\WindowsApps\Minecraft", Version, Version));
        }

        [Fact]
        public void RejectsAnotherPackageFamily()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", Path, Path, Version, Version));
        }

        [Fact]
        public void RejectsDifferentRegisteredPackageVersion()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Version, "1.26.5203.0"));
        }

        [Fact]
        public void RejectsMissingOrInvalidPackageVersion()
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path, Path, Version, "1.26.4005"));
        }

        [Fact]
        public void NormalizesPathCaseAndTrailingSeparators()
        {
            Assert.True(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, Path + "\\",
                @"c:\bedrocklauncher\VERSIONS\1.26.4005.0\", Version, Version));
        }

        [Theory]
        [InlineData(null, Path)]
        [InlineData("", Path)]
        [InlineData(Path, null)]
        [InlineData(Path, "")]
        public void RejectsMissingInstallDirectory(string expectedPath, string installedPath)
        {
            Assert.False(UwpPackageRegistrationValidator.MatchesExactRegistration(
                Family, Family, expectedPath, installedPath, Version, Version));
        }
    }
}
