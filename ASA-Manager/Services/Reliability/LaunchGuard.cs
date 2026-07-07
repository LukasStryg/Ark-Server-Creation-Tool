using System;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>
    /// Pure guard against launching a second server process while a just-launched one is still
    /// booting and not yet observable as running. ASA's ArkAscendedServer.exe is a launcher that
    /// hands off to the real server process, so a running check can read false for a few seconds
    /// after Start(); without this guard a restart during that window would spawn a duplicate
    /// process and the two would fight for the same ports.
    /// </summary>
    public static class LaunchGuard
    {
        public static bool ShouldLaunch(bool observedRunning, DateTime now, DateTime? lastLaunchedAt, int cooldownSeconds)
        {
            if (observedRunning) return false;   // already up — never double-launch
            if (lastLaunchedAt is DateTime t && now < t + TimeSpan.FromSeconds(Math.Max(0, cooldownSeconds)))
                return false;                    // launched within the cooldown; likely still booting
            return true;
        }
    }
}
