namespace ARKServerCreationTool
{
    /// <summary>App-wide shared services: one HttpClient + one metadata cache, and a CurseForge client factory.</summary>
    public static class AppServices
    {
        private static readonly System.Net.Http.HttpClient http = new();

        public static Services.CurseForge.ModMetadataCache MetadataCache { get; } = Services.CurseForge.ModMetadataCache.Load();

        public static Services.CurseForge.CurseForgeClient CurseForge()
            => new Services.CurseForge.CurseForgeClient(http, ASCTGlobalConfig.Instance.CurseForgeApiKey);
    }
}
