using System;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure "is this scheduled task due to run now?" logic. Caller stamps LastRun after firing.</summary>
    public static class ScheduleEvaluator
    {
        public static bool IsDue(ScheduledTask task, DateTime now)
        {
            switch (task.Mode)
            {
                case ScheduleMode.DailyAtTime:
                    if (now.TimeOfDay < task.DailyTime) return false;
                    return task.LastRun == null || task.LastRun.Value.Date < now.Date;

                case ScheduleMode.EveryNHours:
                    if (task.LastRun == null) return true;
                    return now - task.LastRun.Value >= TimeSpan.FromHours(Math.Max(1, task.IntervalHours));

                default:
                    return false;
            }
        }
    }
}
