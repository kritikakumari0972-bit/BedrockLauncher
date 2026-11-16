namespace BedrockLauncher.UpdateProcessor.Enums
{
    /// <summary>
    /// Describes what the launcher can safely claim about a catalogue entry.
    /// Catalogued does not mean the Microsoft Store has resolved a package yet.
    /// DownloadSourceListed means an exact GDK package resource is catalogued, not that the CDN has been contacted.
    /// </summary>
    public enum VersionAvailability
    {
        Unknown = 0,
        Catalogued = 1,
        DownloadSourceListed = 2,
        Unavailable = 3,
        UnsupportedArchitecture = 4
    }
}
