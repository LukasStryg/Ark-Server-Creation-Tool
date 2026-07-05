using System;
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    public readonly struct RestartStep
    {
        public RestartStep(string message, TimeSpan waitAfter) { Message = message; WaitAfter = waitAfter; }
        public string Message { get; }
        public TimeSpan WaitAfter { get; }
    }

    /// <summary>Pure countdown plan: ordered broadcast messages and the wait before the next warning / the restart.</summary>
    public static class RestartCountdown
    {
        public static IReadOnlyList<RestartStep> Steps(int[] warningMinutes)
        {
            var minutes = (warningMinutes ?? Array.Empty<int>())
                .Where(m => m > 0).Distinct().OrderByDescending(m => m).ToList();

            var steps = new List<RestartStep>(minutes.Count);
            for (int i = 0; i < minutes.Count; i++)
            {
                int m = minutes[i];
                int next = i + 1 < minutes.Count ? minutes[i + 1] : 0;   // 0 = the restart itself
                var wait = TimeSpan.FromMinutes(m - next);
                steps.Add(new RestartStep(Message(m), wait));
            }
            return steps;
        }

        private static string Message(int minutes)
            => $"Server restarting in {minutes} minute{(minutes == 1 ? "" : "s")}";
    }
}
