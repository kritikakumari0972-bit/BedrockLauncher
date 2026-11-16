using System;
using System.Collections.Generic;
using System.Linq;
using BedrockLauncher.UpdateProcessor.Databases;
using BedrockLauncher.UpdateProcessor.Enums;
using BedrockLauncher.UpdateProcessor.Extensions;

namespace BedrockLauncher.UpdateProcessor.Classes
{
    /// <summary>
    /// Performs local catalogue validation only. It never claims that a package is installed or that a remote
    /// download URL is reachable. The UWP Store link is resolved later by the existing Store download pipeline.
    /// </summary>
    public static class VersionCatalogValidator
    {
        private static readonly HashSet<string> KnownArchitectures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "x86",
                "x64",
                "arm",
                "arm64"
            };

        public static VersionCatalogValidationResult Validate(
            VersionInfoJson version,
            IEnumerable<string> supportedArchitectures = null)
        {
            if (!MinecraftVersion.TryParse(version.version, out _))
            {
                return Result(
                    version,
                    VersionAvailability.Unavailable,
                    "The catalogue entry does not contain a valid Minecraft version number.");
            }

            if (version.uuid == Guid.Empty)
            {
                return Result(
                    version,
                    VersionAvailability.Unavailable,
                    "The catalogue entry has no valid update identifier.");
            }

            if (!Enum.IsDefined(typeof(VersionType), version.type))
            {
                return Result(
                    version,
                    VersionAvailability.Unavailable,
                    "The catalogue entry has an unknown release channel.");
            }

            if (!Enum.IsDefined(typeof(PackageType), version.packageType))
            {
                return Result(
                    version,
                    VersionAvailability.Unavailable,
                    "The catalogue entry has an unknown Windows package type.");
            }

            string architecture = version.architecture?.Trim();
            if (string.IsNullOrWhiteSpace(architecture) ||
                architecture == VersionDbExtensions.FallbackArch ||
                !KnownArchitectures.Contains(architecture))
            {
                return Result(
                    version,
                    VersionAvailability.Unavailable,
                    "The package architecture is missing or is not recognized.");
            }

            if (supportedArchitectures != null)
            {
                var supported = new HashSet<string>(
                    supportedArchitectures.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);

                if (!supported.Contains(architecture))
                {
                    return Result(
                        version,
                        VersionAvailability.UnsupportedArchitecture,
                        $"This launcher environment has not declared support for the {architecture} package architecture.");
                }
            }

            if (version.packageType == PackageType.UWP)
            {
                // The exact Store package URL is resolved at download time by StoreNetwork.
                return Result(
                    version,
                    VersionAvailability.Catalogued,
                    "The UWP version is catalogued. Its Microsoft Store package link must be resolved before download.");
            }

            if (version.packageType == PackageType.GDK)
            {
                GdkPackageIdentity requiredPackage = version.GetRequiredGdkPackage();
                if (requiredPackage == null)
                {
                    return Result(
                        version,
                        VersionAvailability.Unavailable,
                        "The GDK entry does not identify one exact required package.");
                }

                string[] matchingUrls =
                    VersionJsonDb.FilterUrlsForIdentity(version.downloadUrls, requiredPackage);

                if (matchingUrls.Length == 0)
                {
                    return Result(
                        version,
                        VersionAvailability.Unavailable,
                        $"No download resource is catalogued for {requiredPackage.FullName}.");
                }

                return Result(
                    version,
                    VersionAvailability.DownloadSourceListed,
                    "An exact GDK package resource is catalogued. The download must still be attempted and validated.");
            }

            return Result(
                version,
                VersionAvailability.Unavailable,
                "The catalogue entry is not supported by a known package pipeline.");
        }

        private static VersionCatalogValidationResult Result(
            VersionInfoJson version,
            VersionAvailability availability,
            string reason)
        {
            return new VersionCatalogValidationResult(
                availability,
                version.catalogSource,
                reason);
        }
    }
}
