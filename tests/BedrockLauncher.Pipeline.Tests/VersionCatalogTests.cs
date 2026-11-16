using System;
using System.Collections.Generic;
using System.Linq;
using BedrockLauncher.UpdateProcessor.Classes;
using BedrockLauncher.UpdateProcessor.Databases;
using BedrockLauncher.UpdateProcessor.Enums;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class VersionCatalogTests
    {
        private const string GdkPackage =
            "Microsoft.MinecraftUWP_1.26.4005.0_x64__8wekyb3d8bbwe";

        [Fact]
        public void ParsedEntriesReceiveTheSourceOfTheirDatabase()
        {
            var db = new VersionJsonDb();
            db.ParseJson(
                """
                [
                  ["1.21.114.1", "3738c248-0603-4560-abf4-fdd66e7cd852", 0, "x64", 0]
                ]
                """,
                null,
                VersionCatalogSource.CommunityCatalogue);

            VersionInfoJson entry = Assert.Single(db.list);

            Assert.Equal(VersionCatalogSource.CommunityCatalogue, entry.catalogSource);
            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);
            Assert.Equal(VersionAvailability.Catalogued, result.Availability);
            Assert.Equal(VersionCatalogSource.CommunityCatalogue, result.Source);
            Assert.True(result.CanAttemptDownload);
            Assert.Contains("must be resolved", result.Reason);
        }

        [Fact]
        public void GdkEntryIsDownloadSourceListedOnlyWhenExactPackageUrlExists()
        {
            var entry = new VersionInfoJson(
                "1.26.40.5",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                "x64",
                PackageType.GDK,
                GdkPackage,
                new[] { "https://assets1.xboxlive.com/packages/" + GdkPackage + ".msixvc" },
                VersionCatalogSource.GdkLinksManifest);

            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);

            Assert.Equal(VersionAvailability.DownloadSourceListed, result.Availability);
            Assert.Equal(VersionCatalogSource.GdkLinksManifest, result.Source);
            Assert.True(result.CanAttemptDownload);
            Assert.Contains("must still be attempted", result.Reason);
        }

        [Fact]
        public void GdkEntryWithoutExactPackageIdentityIsUnavailable()
        {
            var entry = new VersionInfoJson(
                "1.26.40.5",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                "x64",
                PackageType.GDK,
                null,
                new[] { "https://assets1.xboxlive.com/packages/" + GdkPackage + ".msixvc" });

            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);

            Assert.Equal(VersionAvailability.Unavailable, result.Availability);
            Assert.False(result.CanAttemptDownload);
            Assert.Contains("exact required package", result.Reason);
        }

        [Fact]
        public void GdkEntryWithWrongPackageVersionIsUnavailable()
        {
            const string wrongPackage =
                "Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe";

            var entry = new VersionInfoJson(
                "1.26.40.5",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                "x64",
                PackageType.GDK,
                wrongPackage,
                new[] { "https://assets1.xboxlive.com/packages/" + wrongPackage + ".msixvc" });

            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);

            Assert.Equal(VersionAvailability.Unavailable, result.Availability);
            Assert.Contains("does not match", result.Reason);
        }

        [Fact]
        public void GdkEntryWithOnlyForeignPackageUrlsIsUnavailable()
        {
            const string otherPackage =
                "Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe";

            var entry = new VersionInfoJson(
                "1.26.40.5",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                "x64",
                PackageType.GDK,
                GdkPackage,
                new[] { "https://assets1.xboxlive.com/packages/" + otherPackage + ".msixvc" });

            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);

            Assert.Equal(VersionAvailability.Unavailable, result.Availability);
            Assert.False(result.CanAttemptDownload);
        }

        [Fact]
        public void PackageArchitectureOutsideTheSupportedSetIsMarkedUnsupported()
        {
            var entry = new VersionInfoJson(
                "1.21.114.1",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                "arm64",
                PackageType.UWP);

            VersionCatalogValidationResult result =
                VersionCatalogValidator.Validate(entry, new[] { "x64", "x86" });

            Assert.Equal(VersionAvailability.UnsupportedArchitecture, result.Availability);
            Assert.False(result.CanAttemptDownload);
        }

        [Fact]
        public void UnknownArchitectureIsNotPresentedAsInstallable()
        {
            var entry = new VersionInfoJson(
                "1.21.114.1",
                Guid.NewGuid().ToString(),
                VersionType.Release,
                VersionDbFallbackArchitecture(),
                PackageType.UWP);

            VersionCatalogValidationResult result = VersionCatalogValidator.Validate(entry);

            Assert.Equal(VersionAvailability.Unavailable, result.Availability);
            Assert.False(result.CanAttemptDownload);
        }

        [Fact]
        public void DuplicateRowsWithSameUpdateIdentityAndPackageTypeAreCollapsed()
        {
            const string uuid = "3738c248-0603-4560-abf4-fdd66e7cd852";
            var db = new VersionJsonDb();
            db.ParseJson(
                $"""
                [
                  ["1.21.114.1", "{uuid}", 0, "x64", 0],
                  ["1.21.114.1", "{uuid}", 0, "x64", 0]
                ]
                """);

            Assert.Single(db.list);
        }

        [Fact]
        public void UwpAndGdkEntriesRemainSeparateForTheSameMinecraftVersion()
        {
            const string gdkUuid = "dff4c2a1-0dc9-4fc4-bd90-19087eb7f246";
            var db = new VersionJsonDb();
            db.ParseJson(
                $"""
                [
                  ["1.26.40.5", "3738c248-0603-4560-abf4-fdd66e7cd852", 0, "x64", 0],
                  ["1.26.40.5", "{gdkUuid}", 0, "x64", 1, "{GdkPackage}",
                    ["https://assets1.xboxlive.com/packages/{GdkPackage}.msixvc"]]
                ]
                """);

            Assert.Equal(2, db.list.Count);
            Assert.Contains(db.list, x => x.packageType == PackageType.UWP);
            Assert.Contains(db.list, x => x.packageType == PackageType.GDK);
        }

        private static string VersionDbFallbackArchitecture() => "???";
    }
}
