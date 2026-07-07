using System;
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    public enum CrashDecision { Restart, GiveUp }

    /// <summary>Pure throttle: give up once crashes within the sliding window reach the threshold.</summary>
    public static class CrashPolicy
    {
        public static CrashDecision Decide(IReadOnlyList<DateTime> crashesInclNow, DateTime now,
                                           int thresholdCount, int windowMinutes)
        {
            var cutoff = now - TimeSpan.FromMinutes(Math.Max(1, windowMinutes));
            int inWindow = crashesInclNow.Count(t => t >= cutoff);
            return inWindow >= Math.Max(1, thresholdCount) ? CrashDecision.GiveUp : CrashDecision.Restart;
        }

        /// <summary>
        /// Pure crash predicate. True only when a server that should be up isn't observably running,
        /// no operation is mid-flight, auto-restart isn't paused, AND it is past the startup grace
        /// window since it was last (re)started. The grace window keeps a freshly (re)started ASA
        /// server — whose launcher briefly hands off to the real process, so it reads as not-running
        /// for a few seconds — from being mistaken for a crash on the next heartbeat tick.
        /// A null <paramref name="startedAt"/> means no start was recorded (no grace to honor).
        /// </summary>
        public static bool LooksCrashed(bool shouldBeRunning, bool operationInProgress, bool autoRestartPaused,
                                        bool isRunning, DateTime now, DateTime? startedAt, int startupGraceSeconds)
        {
            if (!shouldBeRunning || operationInProgress || autoRestartPaused || isRunning)
                return false;
            if (startedAt is DateTime started && now < started + TimeSpan.FromSeconds(Math.Max(0, startupGraceSeconds)))
                return false;   // still inside the startup grace window
            return true;
        }
    }
}
