namespace BedrockLauncher.UpdateProcessor.Enums
{
    /// <summary>
    /// Identifies the catalogue from which a version entry was loaded.
    /// This is runtime metadata; the source databases remain separate and retain their existing JSON formats.
    /// </summary>
    public enum VersionCatalogSource
    {
        Unknown = 0,
        CommunityCatalogue = 1,
        MicrosoftStore = 2,
        GdkLinksManifest = 3,
        PersistedGdkCatalogue = 4
    }
}
