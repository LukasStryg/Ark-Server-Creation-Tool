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
    }
}
