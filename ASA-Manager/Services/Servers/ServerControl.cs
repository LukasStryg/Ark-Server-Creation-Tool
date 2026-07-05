using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;

namespace ARKServerCreationTool.Services.Servers
{
    /// <summary>Factory + helpers for gracefully stopping one or many servers (single implementation used app-wide).</summary>
    public static class ServerControl
    {
        public static ServerControlService For(ASCTServerConfig s)
            => new ServerControlService(
                () => new RconClient("127.0.0.1", s.RconPort),
                new GameProcessControllerAdapter(s.ProcessManager),
                s.ServerAdminPassword);

        public static Task<StopResult> GracefulStopAsync(ASCTServerConfig s, IProgress<string>? progress = null)
            => For(s).GracefulStopAsync(TimeSpan.FromSeconds(60), progress);

        /// <summary>Gracefully stops every currently-running server in the set, in parallel.</summary>
        public static Task GracefulStopManyAsync(IEnumerable<ASCTServerConfig> servers)
            => Task.WhenAll(servers.Where(s => s.IsRunning).Select(s => GracefulStopAsync(s)));

        /// <summary>Refresh CurseForge metadata (best-effort) then record the running mod versions as the update-check baseline.</summary>
        public static async Task SnapshotAfterStartAsync(ASCTServerConfig s)
        {
            var client = AppServices.CurseForge();
            if (client.HasKey && s.Mods.Count > 0)
            {
                try
                {
                    await AppServices.MetadataCache.RefreshAsync(s.Mods.Select(m => m.ProjectId), client, System.DateTimeOffset.UtcNow);
                    AppServices.MetadataCache.Save();
                }
                catch { /* best-effort; snapshot from whatever cache we have */ }
            }
            s.SnapshotRunningModVersions(AppServices.MetadataCache);
        }
    }
}
