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
            DateTime now, IProgress<string>? progress = null)
        {
            string timestamp = BackupPaths.Timestamp(now);

            // Each server backs up into its OWN folder (BackupRoot/<serverName>/<timestamp>/), so a single
            // backup and a Backup All build the same per-server history — no duplicate "All" tree.
            foreach (var server in targetServers)
                await BackupServerAsync(server, timestamp, progress);

            // The shared cluster dir gets its own history, copied once for cluster / all backups.
            if (kind != ScheduleTargetKind.Server)
                await BackupClusterDirAsync(timestamp, progress);

            ReliabilityLog.Append($"Backup complete: {kind} ({timestamp})");
        }

        private async Task BackupServerAsync(ASCTServerConfig server, string timestamp, IProgress<string>? progress)
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
            string dest = BackupPaths.SnapshotFolder(_config.BackupRoot, server.Name, timestamp);
            await Task.Run(() => CopyDirectory(savedArks, dest));
            ApplyRotation(server.Name);
        }

        private async Task BackupClusterDirAsync(string timestamp, IProgress<string>? progress)
        {
            if (string.IsNullOrWhiteSpace(_config.GlobalClusterDir) || !Directory.Exists(_config.GlobalClusterDir)) return;
            progress?.Report("Backing up cluster data...");
            string dest = BackupPaths.SnapshotFolder(_config.BackupRoot, "_ClusterData", timestamp);
            await Task.Run(() => CopyDirectory(_config.GlobalClusterDir, dest));
            ApplyRotation("_ClusterData");
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

        // Redundant/transient files ARK regenerates and a restore never needs: the rolling world backups
        // (.arkrbf / .arkbf — the extension varies by ASA build, kept per MaxNumOfSaveBackups), the
        // anti-corruption backup (.bak), the per-player/tribe safety copies (.profilebak / .tribebak), and
        // the mid-save scratch file (.tmp). Everything else (.ark, .arkprofile, .arktribe, .arktributetribe,
        // paintings, ...) is copied — we err toward keeping anything not known to be redundant.
        private static readonly string[] RedundantSuffixes =
            { ".arkrbf", ".arkbf", ".bak", ".profilebak", ".tribebak", ".tmp" };

        private static bool IsRedundantBackup(string file)
            => RedundantSuffixes.Any(s => file.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        private static void CopyDirectory(string source, string dest)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (IsRedundantBackup(file)) continue;
                File.Copy(file, file.Replace(source, dest), overwrite: true);
            }
        }
    }
}
