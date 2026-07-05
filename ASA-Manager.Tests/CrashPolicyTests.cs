using System;
using System.Collections.Generic;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class CrashPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 7, 5, 12, 0, 0);

        [Fact]
        public void Restarts_when_first_crash()
        {
            var decision = CrashPolicy.Decide(new List<DateTime> { Now }, Now, thresholdCount: 3, windowMinutes: 5);
            Assert.Equal(CrashDecision.Restart, decision);
        }

        [Fact]
        public void Restarts_on_second_crash_in_window()
        {
            var crashes = new List<DateTime> { Now.AddMinutes(-2), Now };
            Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide(crashes, Now, 3, 5));
        }

        [Fact]
        public void Gives_up_on_third_crash_in_window()
        {
            var crashes = new List<DateTime> { Now.AddMinutes(-3), Now.AddMinutes(-1), Now };
            Assert.Equal(CrashDecision.GiveUp, CrashPolicy.Decide(crashes, Now, 3, 5));
        }

        [Fact]
        public void Ignores_crashes_older_than_window()
        {
            // two old crashes fell out of the 5-minute window; only the current one counts
            var crashes = new List<DateTime> { Now.AddMinutes(-30), Now.AddMinutes(-20), Now };
            Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide(crashes, Now, 3, 5));
        }
    }
}
