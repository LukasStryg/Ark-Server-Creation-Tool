using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Servers;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>In-app heartbeat: fires due scheduled tasks and detects/handles crashes.</summary>
    public class ReliabilityCoordinator
    {
        private sealed class State
        {
            public bool ShouldBeRunning;
            public bool OperationInProgress;
            public bool AutoRestartPaused;
            public readonly List<DateTime> CrashTimes = new();
        }

        private readonly ASCTGlobalConfig _config;
        private readonly BackupService _backups;
        private readonly Dictionary<int, State> _state = new();
        private DispatcherTimer? _timer;
        private bool _ticking;

        public ReliabilityCoordinator(ASCTGlobalConfig config, BackupService backups)
        {
            _config = config;
            _backups = backups;
        }

        public void Start()
        {
            foreach (var s in _config.Servers)
                _state[s.ID] = new State { ShouldBeRunning = s.IsRunning };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += async (_, __) => await TickAsync();
            _timer.Start();
        }

        private State StateFor(int id) => _state.TryGetValue(id, out var st) ? st : (_state[id] = new State());

        public void NotifyStarted(int serverId) { var s = StateFor(serverId); s.ShouldBeRunning = true; s.AutoRestartPaused = false; }
        public void NotifyStopping(int serverId) => StateFor(serverId).OperationInProgress = true;
        public void NotifyStopped(int serverId) { var s = StateFor(serverId); s.ShouldBeRunning = false; s.OperationInProgress = false; }
        public void MarkOperation(int serverId, bool inProgress) => StateFor(serverId).OperationInProgress = inProgress;

        /// <summary>Starts the given servers sequentially, spaced by the configured stagger delay, honoring the exclude flag.</summary>
        public async Task StartStaggeredAsync(IEnumerable<ASCTServerConfig> servers, Action? onEach = null)
        {
            var toStart = StaggeredStarter.SelectServersToStart(servers).Where(s => !s.IsRunning).ToList();
            for (int i = 0; i < toStart.Count; i++)
            {
                var s = toStart[i];
                s.TransientStatus = "Starting…";
                s.ProcessManager.Start();
                NotifyStarted(s.ID);
                await ServerControl.SnapshotAfterStartAsync(s);
                s.TransientStatus = null;
                onEach?.Invoke();
                if (i < toStart.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, (int)_config.AutoStartStaggerTime)));
            }
        }

        private async Task TickAsync()
        {
            if (_ticking) return;   // never overlap ticks
            _ticking = true;
            try
            {
                var now = DateTime.Now;
                RunDueSchedules(now);
                await DetectCrashesAsync(now);
            }
            catch (Exception ex) { ReliabilityLog.Append($"Tick error: {ex.Message}"); }
            finally { _ticking = false; }
        }

        private void RunDueSchedules(DateTime now)
        {
            foreach (var task in _config.ScheduledTasks.Where(t => t.Enabled).ToList())
            {
                if (!ScheduleEvaluator.IsDue(task, now)) continue;
                task.LastRun = now;            // stamp before running so it can't re-fire while it runs
                try { _config.Save(); } catch { }
                // Fire-and-forget: a long restart countdown or a large backup must not block the tick,
                // or crash detection and other schedules would freeze until it finishes.
                _ = RunTaskSafelyAsync(task, now);
            }
        }

        private async Task RunTaskSafelyAsync(ScheduledTask task, DateTime now)
        {
            try { await RunTaskAsync(task, now); }
            catch (Exception ex) { ReliabilityLog.Append($"Scheduled {task.Type} failed: {ex.Message}"); }
        }

        private async Task RunTaskAsync(ScheduledTask task, DateTime now)
        {
            var (servers, label) = ResolveTarget(task);
            if (servers.Count == 0) { ReliabilityLog.Append($"Scheduled {task.Type}: no matching servers ({label})"); return; }

            if (task.Type == ScheduledTaskType.Backup)
            {
                await _backups.BackupTargetAsync(task.TargetKind, servers, now);
                return;
            }

            // Restart: only running servers; stagger the whole set.
            var running = servers.Where(s => s.IsRunning).ToList();
            var steps = RestartCountdown.Steps(_config.RestartWarningMinutes);
            foreach (var server in running)
            {
                MarkOperation(server.ID, true);
                try
                {
                    await ServerControl.For(server).RestartWithCountdownAsync(
                        steps, TimeSpan.FromSeconds(60), (d, ct) => Task.Delay(d, ct));
                    NotifyStarted(server.ID);
                    ReliabilityLog.Append($"Scheduled restart done: {server.Name}");
                }
                finally { MarkOperation(server.ID, false); }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, (int)_config.AutoStartStaggerTime)));
            }
        }

        private (List<ASCTServerConfig> servers, string label) ResolveTarget(ScheduledTask task) => task.TargetKind switch
        {
            ScheduleTargetKind.All => (_config.Servers.ToList(), "All"),
            ScheduleTargetKind.Cluster => (_config.Servers.Where(s => s.ClusterKey == task.TargetClusterKey).ToList(),
                                           string.IsNullOrEmpty(task.TargetClusterKey) ? "cluster" : task.TargetClusterKey!),
            _ => ResolveSingle(task),
        };

        private (List<ASCTServerConfig>, string) ResolveSingle(ScheduledTask task)
        {
            var s = _config.Servers.FirstOrDefault(x => x.ID == task.TargetServerId);
            return s == null ? (new List<ASCTServerConfig>(), "server")
                             : (new List<ASCTServerConfig> { s }, s.Name);
        }

        private async Task DetectCrashesAsync(DateTime now)
        {
            if (!_config.CrashAutoRestartEnabled) return;

            foreach (var server in _config.Servers)
            {
                var st = StateFor(server.ID);
                bool crashed = st.ShouldBeRunning && !st.OperationInProgress && !st.AutoRestartPaused && !server.IsRunning;
                if (!crashed) continue;

                st.CrashTimes.Add(now);
                var decision = CrashPolicy.Decide(st.CrashTimes, now, _config.CrashThresholdCount, _config.CrashWindowMinutes);
                if (decision == CrashDecision.GiveUp)
                {
                    st.AutoRestartPaused = true;
                    server.TransientStatus = "Crashed — auto-restart paused";
                    ReliabilityLog.Append($"Crash give-up: {server.Name} (paused)");
                    continue;
                }

                ReliabilityLog.Append($"Crash detected: {server.Name}; auto-restarting");
                MarkOperation(server.ID, true);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _config.CrashRestartBackoffSeconds)));
                    server.ProcessManager.Start();
                }
                finally { MarkOperation(server.ID, false); }
            }
        }
    }
}
