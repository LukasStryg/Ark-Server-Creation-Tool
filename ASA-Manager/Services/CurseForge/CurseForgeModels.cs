using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ARKServerCreationTool.Services.CurseForge
{
    public class CfListResponse<T> { [JsonProperty("data")] public List<T> Data { get; set; } = new(); }
    public class CfSingleResponse<T> { [JsonProperty("data")] public T? Data { get; set; } }

    public class CfMod
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("gameId")] public long GameId { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("summary")] public string Summary { get; set; } = string.Empty;
        [JsonProperty("downloadCount")] public long DownloadCount { get; set; }
        [JsonProperty("authors")] public List<CfAuthor> Authors { get; set; } = new();
        [JsonProperty("logo")] public CfLogo? Logo { get; set; }
        [JsonProperty("latestFiles")] public List<CfFile> LatestFiles { get; set; } = new();
        [JsonProperty("dateModified")] public DateTimeOffset DateModified { get; set; }
    }

    public class CfAuthor { [JsonProperty("name")] public string Name { get; set; } = string.Empty; }

    public class CfLogo
    {
        [JsonProperty("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
        [JsonProperty("url")] public string? Url { get; set; }
    }

    public class CfFile
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("fileDate")] public DateTimeOffset FileDate { get; set; }
        [JsonProperty("fileLength")] public long FileLength { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
    }
}
