using System;
using System.IO;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class BackupPathsTests
    {
        [Fact]
        public void Timestamp_is_sortable()
        {
            var ts = BackupPaths.Timestamp(new DateTime(2026, 7, 5, 4, 3, 2));
            Assert.Equal("2026-07-05_04-03-02", ts);
        }

        [Fact]
        public void Snapshot_folder_combines_root_label_timestamp()
        {
            var folder = BackupPaths.SnapshotFolder(Path.Combine("C:", "Backups"), "west", "2026-07-05_04-03-02");
            Assert.Equal(Path.Combine("C:", "Backups", "west", "2026-07-05_04-03-02"), folder);
        }
    }
}
