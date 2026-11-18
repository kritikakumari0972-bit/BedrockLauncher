using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BedrockLauncher.Handlers
{
    /// <summary>
    /// Detects accidental or later changes to a cached MSIXVC file.
    /// The receipt is generated locally; it is not a trusted publisher signature or authenticity proof.
    /// Windows/Gaming Services still validates the package when it is deployed.
    /// </summary>
    public static class GdkPackageCacheIntegrity
    {
        private const string ReceiptHeader = "BEDROCKLAUNCHER-GDK-CACHE-SHA256-V1";

        public static string GetReceiptPath(string packagePath) =>
            packagePath + ".bedrocklauncher.sha256";

        /// <summary>
        /// Verifies a prior local receipt or creates one for a newly downloaded/legacy cache file.
        /// A missing receipt is fingerprinted once. A malformed or mismatching existing receipt fails closed.
        /// </summary>
        public static bool VerifyOrCreate(
            string packagePath,
            string requiredPackageFullName,
            out bool receiptCreated,
            out string reason)
        {
            receiptCreated = false;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("A package path is required.", nameof(packagePath));
            if (string.IsNullOrWhiteSpace(requiredPackageFullName))
                throw new ArgumentException("The exact required package identity is required.", nameof(requiredPackageFullName));

            if (!File.Exists(packagePath))
            {
                reason = "The cached package file does not exist.";
                return false;
            }

            FileAttributes attributes = File.GetAttributes(packagePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                reason = "The cached package path is a reparse point; cache verification refuses to follow it.";
                return false;
            }

            string receiptPath = GetReceiptPath(packagePath);
            bool hasReceipt = File.Exists(receiptPath);
            string[] receipt = null;

            if (hasReceipt)
            {
                try
                {
                    receipt = File.ReadAllLines(receiptPath, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    reason = "The local cache integrity receipt cannot be read: " + ex.Message;
                    return false;
                }

                if (receipt.Length != 4 ||
                    !string.Equals(receipt[0], ReceiptHeader, StringComparison.Ordinal) ||
                    !string.Equals(receipt[1], requiredPackageFullName, StringComparison.Ordinal) ||
                    !long.TryParse(receipt[2], NumberStyles.None, CultureInfo.InvariantCulture, out long recordedLength) ||
                    recordedLength < 1 ||
                    receipt[3].Length != 64)
                {
                    reason = "The local cache integrity receipt is malformed or belongs to a different package identity.";
                    return false;
                }

                long currentLength = new FileInfo(packagePath).Length;
                if (currentLength != recordedLength)
                {
                    reason = $"The cached package length changed ({currentLength} bytes; receipt records {recordedLength}).";
                    return false;
                }
            }

            string actualHash;
            long actualLength;
            using (var stream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            {
                actualLength = stream.Length;
                using (SHA256 sha256 = SHA256.Create())
                    actualHash = Convert.ToHexString(sha256.ComputeHash(stream));
            }

            if (actualLength < 1)
            {
                reason = "The cached package is empty.";
                return false;
            }

            if (hasReceipt)
            {
                if (!string.Equals(receipt[3], actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "The cached package SHA-256 does not match its local integrity receipt.";
                    return false;
                }

                return true;
            }

            WriteReceiptAtomically(receiptPath, requiredPackageFullName, actualLength, actualHash);
            receiptCreated = true;
            return true;
        }

        public static void DeleteReceipt(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                return;

            string receiptPath = GetReceiptPath(packagePath);
            if (File.Exists(receiptPath))
                File.Delete(receiptPath);
        }

        private static void WriteReceiptAtomically(
            string receiptPath,
            string packageIdentity,
            long length,
            string sha256)
        {
            string temporaryPath = receiptPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string contents = string.Join("\n",
                ReceiptHeader,
                packageIdentity,
                length.ToString(CultureInfo.InvariantCulture),
                sha256) + "\n";

            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
                File.Move(temporaryPath, receiptPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
