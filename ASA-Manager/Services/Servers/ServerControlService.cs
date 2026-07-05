using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Reliability;

namespace ARKServerCreationTool.Services.Servers
{
    public enum StopResult { AlreadyStopped, GracefulStop, ForcedStop, Failed }

    /// <summary>Orchestrates graceful stop/restart: SaveWorld -> DoExit via RCON, with a force-stop fallback.</summary>
    public class ServerControlService
    {
        private readonly Func<IRconClient> _rconFactory;
        private readonly IServerProcessController _process;
        private readonly string _adminPassword;

        public ServerControlService(Func<IRconClient> rconFactory, IServerProcessController process, string adminPassword)
        {
            _rconFactory = rconFactory;
            _process = process;
            _adminPassword = adminPassword;
        }

        public async Task<StopResult> GracefulStopAsync(TimeSpan timeout, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (!_process.IsRunning) return StopResult.AlreadyStopped;

            bool rconOk = false;
            try
            {
                using var rcon = _rconFactory();
                rconOk = await rcon.ConnectAndAuthenticateAsync(_adminPassword, ct);
                if (rconOk)
                {
                    progress?.Report("Saving world...");
                    await rcon.ExecuteAsync("SaveWorld", ct);
                    progress?.Report("Shutting down...");
                    await rcon.ExecuteAsync("DoExit", ct);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"RCON failed: {ex.Message}");
                rconOk = false;
            }

            if (rconOk && await WaitForStopAsync(timeout, ct))
                return StopResult.GracefulStop;

            progress?.Report(rconOk ? "Graceful stop timed out; forcing stop." : "RCON unavailable; forcing stop.");
            _process.ForceStop();
            return _process.IsRunning ? StopResult.Failed : StopResult.ForcedStop;
        }

        public async Task<bool> RestartToApplyAsync(TimeSpan stopTimeout, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var stop = await GracefulStopAsync(stopTimeout, progress, ct);
            if (stop == StopResult.Failed) return false;
            progress?.Report("Starting server (mods re-download on boot)...");
            return _process.Start();
        }

        /// <summary>Opens a short-lived RCON connection and broadcasts a center-screen message. Best-effort.</summary>
        public async Task BroadcastAsync(string message, CancellationToken ct = default)
        {
            try
            {
                using var rcon = _rconFactory();
                if (await rcon.ConnectAndAuthenticateAsync(_adminPassword, ct))
                    await rcon.ExecuteAsync($"Broadcast {message}", ct);
            }
            catch { /* best-effort warning; a failed broadcast must not abort the restart */ }
        }

        /// <summary>Broadcasts each countdown step (waiting between them via the injected delay), then restarts.</summary>
        public async Task<bool> RestartWithCountdownAsync(IReadOnlyList<RestartStep> steps, TimeSpan stopTimeout,
            Func<TimeSpan, CancellationToken, Task> delay, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            foreach (var step in steps)
            {
                progress?.Report(step.Message);
                await BroadcastAsync(step.Message, ct);
                await delay(step.WaitAfter, ct);
            }
            return await RestartToApplyAsync(stopTimeout, progress, ct);
        }

        private async Task<bool> WaitForStopAsync(TimeSpan timeout, CancellationToken ct)
        {
            var step = TimeSpan.FromMilliseconds(100);
            var waited = TimeSpan.Zero;
            while (waited < timeout)
            {
                if (!_process.IsRunning) return true;
                await Task.Delay(step, ct);
                waited += step;
            }
            return !_process.IsRunning;
        }
    }
}
