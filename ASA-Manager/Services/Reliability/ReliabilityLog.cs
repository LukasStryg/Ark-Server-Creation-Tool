using System;
using System.IO;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Best-effort append-only log for scheduled actions, backups, and crash events.</summary>
    public static class ReliabilityLog
    {
        private static readonly object _lock = new();
        public const string FileName = "ReliabilityLog.txt";

        public static void Append(string message)
        {
            try
            {
                lock (_lock)
                    File.AppendAllText(FileName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
