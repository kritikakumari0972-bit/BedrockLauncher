using BedrockLauncher.UpdateProcessor.Enums;

namespace BedrockLauncher.UpdateProcessor.Classes
{
    public sealed class VersionCatalogValidationResult
    {
        public VersionAvailability Availability { get; }
        public VersionCatalogSource Source { get; }
        public string Reason { get; }

        public bool CanAttemptDownload =>
            Availability == VersionAvailability.Catalogued ||
            Availability == VersionAvailability.DownloadSourceListed;

        public VersionCatalogValidationResult(
            VersionAvailability availability,
            VersionCatalogSource source,
            string reason)
        {
            Availability = availability;
            Source = source;
            Reason = reason ?? string.Empty;
        }
    }
}
