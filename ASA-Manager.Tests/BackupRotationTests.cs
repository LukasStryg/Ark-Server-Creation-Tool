using System.Linq;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class BackupRotationTests
    {
        // Folder names are sortable timestamps: yyyy-MM-dd_HH-mm-ss
        private static readonly string[] Folders =
        {
            "2026-07-05_01-00-00", "2026-07-05_02-00-00", "2026-07-05_03-00-00", "2026-07-05_04-00-00",
        };

        [Fact]
        public void Deletes_oldest_beyond_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 2);
            Assert.Equal(new[] { "2026-07-05_01-00-00", "2026-07-05_02-00-00" }, toDelete.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Deletes_none_when_under_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 10);
            Assert.Empty(toDelete);
        }

        [Fact]
        public void Deletes_none_at_exact_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 4);
            Assert.Empty(toDelete);
        }
    }
}
