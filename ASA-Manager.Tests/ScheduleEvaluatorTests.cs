using System;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ScheduleEvaluatorTests
    {
        private static ScheduledTask Daily(TimeSpan at, DateTime? lastRun) => new ScheduledTask
        { Mode = ScheduleMode.DailyAtTime, DailyTime = at, LastRun = lastRun };

        private static ScheduledTask Interval(int hours, DateTime? lastRun) => new ScheduledTask
        { Mode = ScheduleMode.EveryNHours, IntervalHours = hours, LastRun = lastRun };

        [Fact]
        public void Daily_is_due_after_time_when_not_run_today()
        {
            var now = new DateTime(2026, 7, 5, 5, 1, 0);
            Assert.True(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), lastRun: null), now));
        }

        [Fact]
        public void Daily_is_not_due_before_time()
        {
            var now = new DateTime(2026, 7, 5, 4, 59, 0);
            Assert.False(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), lastRun: null), now));
        }

        [Fact]
        public void Daily_is_not_due_when_already_run_today()
        {
            var now = new DateTime(2026, 7, 5, 6, 0, 0);
            var ranAt = new DateTime(2026, 7, 5, 5, 0, 30);
            Assert.False(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), ranAt), now));
        }

        [Fact]
        public void Daily_is_due_again_next_day()
        {
            var now = new DateTime(2026, 7, 6, 5, 0, 30);
            var ranYesterday = new DateTime(2026, 7, 5, 5, 0, 30);
            Assert.True(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), ranYesterday), now));
        }

        [Fact]
        public void Interval_is_due_when_never_run()
        {
            Assert.True(ScheduleEvaluator.IsDue(Interval(6, lastRun: null), new DateTime(2026, 7, 5, 0, 0, 0)));
        }

        [Fact]
        public void Interval_is_not_due_within_window()
        {
            var now = new DateTime(2026, 7, 5, 3, 0, 0);
            var ranAt = new DateTime(2026, 7, 5, 0, 0, 0);
            Assert.False(ScheduleEvaluator.IsDue(Interval(6, ranAt), now));
        }

        [Fact]
        public void Interval_is_due_past_window()
        {
            var now = new DateTime(2026, 7, 5, 6, 1, 0);
            var ranAt = new DateTime(2026, 7, 5, 0, 0, 0);
            Assert.True(ScheduleEvaluator.IsDue(Interval(6, ranAt), now));
        }
    }
}
