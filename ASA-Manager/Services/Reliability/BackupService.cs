using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Servers;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Save-then-copy backups of server saves (+ shared cluster dir) with rotation.</summary>
    public class BackupService
    {
        private readonly ASCTGlobalConfig _config;
        public BackupService(ASCTGlobalConfig config) => _config = config;

        public async Task BackupTargetAsync(ScheduleTargetKind kind, IReadOnlyList<ASCTServerConfig> targetServers,
            string label, DateTime now, IProgress<string>? progress = null)
        {
            string timestamp = BackupPaths.Timestamp(now);
            bool includeClusterDir = kind != ScheduleTargetKind.Server;

            foreach (var server in targetServers)
                await BackupServerAsync(server, label, timestamp, progress);

            if (includeClusterDir)
                CopyDirectory(_config.GlobalClusterDir,
                    Path.Combine(BackupPaths.SnapshotFolder(_config.BackupRoot, label, timestamp), "_cluster"));

            ApplyRotation(label);
            ReliabilityLog.Append($"Backup complete: {label} ({timestamp})");
        }

        private async Task BackupServerAsync(ASCTServerConfig server, string label, string timestamp, IProgress<string>? progress)
        {
            progress?.Report($"Backing up {server.Name}...");
            if (server.IsRunning)
            {
                try { await ServerControl.For(server).BroadcastAsync("Backing up world..."); } catch { }
                try
                {
                    using var rcon = new Rcon.RconClient("127.0.0.1", server.RconPort);
                    if (await rcon.ConnectAndAuthenticateAsync(server.ServerAdminPassword))
                        await rcon.ExecuteAsync("SaveWorld");
                }
                catch { /* best-effort save; still copy whatever is on disk */ }
            }

            string savedArks = Path.Combine(server.GameDirectory, @"ShooterGame\Saved\SavedArks");
            string dest = Path.Combine(BackupPaths.SnapshotFolder(_config.BackupRoot, label, timestamp), server.Name);
            await Task.Run(() => CopyDirectory(savedArks, dest));
        }

        private void ApplyRotation(string label)
        {
            string labelDir = Path.Combine(_config.BackupRoot, label);
            if (!Directory.Exists(labelDir)) return;
            var folders = Directory.GetDirectories(labelDir).Select(Path.GetFileName)!.Where(n => n != null).Cast<string>();
            foreach (var stale in BackupRotation.ToDelete(folders, _config.BackupKeepCount))
            {
                try { Directory.Delete(Path.Combine(labelDir, stale), recursive: true); } catch { }
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }
}
