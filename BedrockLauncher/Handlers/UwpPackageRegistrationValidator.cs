using System;
using System.IO;

namespace BedrockLauncher.Handlers
{
    /// <summary>
    /// Testable exact-registration predicate for UWP loose registrations. The family, four-part package
    /// version, and install directory must all match the selected launcher version.
    /// </summary>
    public static class UwpPackageRegistrationValidator
    {
        public static bool MatchesExactRegistration(
            string expectedFamily,
            string installedFamily,
            string expectedInstallDirectory,
            string installedInstallDirectory,
            string expectedPackageArchitecture,
            string installedPackageArchitecture,
            string expectedPackageVersion,
            string installedPackageVersion)
        {
            if (!PackageRegistrationMatcher.SameFamily(expectedFamily, installedFamily))
                return false;

            if (string.IsNullOrWhiteSpace(expectedPackageArchitecture) ||
                string.IsNullOrWhiteSpace(installedPackageArchitecture) ||
                !string.Equals(expectedPackageArchitecture, installedPackageArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!PackageRegistrationMatcher.SameVersion(expectedPackageVersion, installedPackageVersion))
                return false;

            return PathsEqual(expectedInstallDirectory, installedInstallDirectory);
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            try
            {
                string normalizedLeft = TrimEndingSeparators(Path.GetFullPath(left));
                string normalizedRight = TrimEndingSeparators(Path.GetFullPath(right));
                return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return false;
            }
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
