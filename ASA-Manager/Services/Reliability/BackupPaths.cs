using System;
using System.IO;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure helpers for backup destination paths and timestamp folder names.</summary>
    public static class BackupPaths
    {
        public static string Timestamp(DateTime now) => now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static string SnapshotFolder(string backupRoot, string label, string timestamp)
            => Path.Combine(backupRoot, label, timestamp);
    }
}
