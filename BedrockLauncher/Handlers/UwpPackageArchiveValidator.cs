using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BedrockLauncher.Handlers
{
    /// <summary>Validated package identity read from a UWP AppxManifest.xml.</summary>
    public sealed class UwpPackageManifestIdentity
    {
        public string Name { get; }
        public Version Version { get; }
        public string Architecture { get; }

        internal UwpPackageManifestIdentity(string name, Version version, string architecture)
        {
            Name = name;
            Version = version;
            Architecture = architecture;
        }
    }

    /// <summary>
    /// Validates the identity in a UWP package manifest before a launcher version directory is replaced.
    /// This is independent of Windows package registration, so the identity rules can be regression-tested.
    /// </summary>
    public static class UwpPackageArchiveValidator
    {
        public static UwpPackageManifestIdentity Validate(
            string manifestXml,
            string expectedIdentityName,
            string expectedArchitecture)
        {
            if (string.IsNullOrWhiteSpace(manifestXml))
                throw new InvalidDataException("The package manifest is empty.");

            if (string.IsNullOrWhiteSpace(expectedIdentityName))
                throw new ArgumentException("The expected package identity name is required.", nameof(expectedIdentityName));

            if (string.IsNullOrWhiteSpace(expectedArchitecture) ||
                expectedArchitecture == "???")
            {
                throw new InvalidDataException("The selected Minecraft version has no validated package architecture.");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(manifestXml, LoadOptions.None);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is ArgumentException)
            {
                throw new InvalidDataException("The package manifest is not valid XML.", ex);
            }

            XElement[] identities = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Identity")
                .ToArray();

            if (identities.Length != 1)
                throw new InvalidDataException("The package manifest must contain exactly one Identity element.");

            XElement identity = identities[0];
            string name = (string)identity.Attribute("Name");
            string versionText = (string)identity.Attribute("Version");
            string architecture = (string)identity.Attribute("ProcessorArchitecture");

            if (!string.Equals(name, expectedIdentityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The package manifest identifies '{name ?? "(missing)"}', but the selected Minecraft version requires '{expectedIdentityName}'.");
            }

            if (!Version.TryParse(versionText, out Version packageVersion) ||
                packageVersion.Build < 0 ||
                packageVersion.Revision < 0)
            {
                throw new InvalidDataException(
                    $"The package manifest version '{versionText ?? "(missing)"}' is not a four-part package version.");
            }

            if (!string.Equals(architecture, expectedArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The package manifest architecture is '{architecture ?? "(missing)"}', but this catalogue entry requires '{expectedArchitecture}'.");
            }

            return new UwpPackageManifestIdentity(name, packageVersion, architecture);
        }
    }
}
