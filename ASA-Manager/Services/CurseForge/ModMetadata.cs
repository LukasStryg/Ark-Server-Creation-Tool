using System;

namespace ARKServerCreationTool.Services.CurseForge
{
    /// <summary>Resolved, cacheable display data for one mod (keyed by Project ID).</summary>
    public class ModMetadata
    {
        public ulong ProjectId { get; set; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? ThumbnailUrl { get; set; }
        public long? LatestFileId { get; set; }
        public DateTimeOffset? LatestFileDate { get; set; }
        public long? FileLength { get; set; }
        public DateTimeOffset LastCheckedUtc { get; set; }
    }
}
