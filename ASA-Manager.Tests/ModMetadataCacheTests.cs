using System;
using ARKServerCreationTool.Services.CurseForge;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ModMetadataCacheTests
    {
        [Fact]
        public void Upsert_then_TryGet_returns_metadata()
        {
            var cache = new ModMetadataCache();
            cache.Upsert(new ModMetadata { ProjectId = 111, Name = "Alpha", LatestFileId = 5001, LastCheckedUtc = DateTimeOffset.Parse("2026-06-01T00:00:00Z") });
            Assert.True(cache.TryGet(111, out var meta));
            Assert.Equal("Alpha", meta.Name);
            Assert.Equal(5001, meta.LatestFileId);
        }

        [Fact]
        public void TryGet_false_for_unknown_id()
        {
            var cache = new ModMetadataCache();
            Assert.False(cache.TryGet(999, out _));
        }

        [Fact]
        public void IsStale_true_when_beyond_ttl_or_unknown()
        {
            var cache = new ModMetadataCache();
            var checkedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
            cache.Upsert(new ModMetadata { ProjectId = 111, LastCheckedUtc = checkedAt });
            Assert.True(cache.IsStale(999, TimeSpan.FromHours(24), checkedAt));                // unknown => stale
            Assert.False(cache.IsStale(111, TimeSpan.FromHours(24), checkedAt.AddHours(1)));   // within ttl
            Assert.True(cache.IsStale(111, TimeSpan.FromHours(24), checkedAt.AddHours(25)));   // beyond ttl
        }
    }
}
