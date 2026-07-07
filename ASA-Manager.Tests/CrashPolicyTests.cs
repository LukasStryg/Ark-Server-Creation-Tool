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

        // --- LooksCrashed: the startup-grace guard that stops a freshly (re)started server from
        //     being mistaken for a crash before it is observable as running (its launcher hands off
        //     to the real ASA process, so IsRunning is briefly false right after a restart). ---

        private const int Grace = 60;

        [Fact]
        public void Within_startup_grace_a_not_running_server_is_not_a_crash()
        {
            // Regression: right after a restart the process is launched but not yet observable
            // (IsRunning == false). Inside the grace window it must NOT be reported as a crash.
            bool crashed = CrashPolicy.LooksCrashed(
                shouldBeRunning: true, operationInProgress: false, autoRestartPaused: false,
                isRunning: false, now: Now.AddSeconds(30), startedAt: Now, startupGraceSeconds: Grace);
            Assert.False(crashed);
        }

        [Fact]
        public void After_startup_grace_a_not_running_server_is_a_crash()
        {
            bool crashed = CrashPolicy.LooksCrashed(
                true, false, false, isRunning: false,
                now: Now.AddSeconds(Grace + 1), startedAt: Now, startupGraceSeconds: Grace);
            Assert.True(crashed);
        }

        [Fact]
        public void At_the_grace_boundary_a_not_running_server_is_a_crash()
        {
            // Grace is an exclusive upper bound: at exactly startedAt + grace the window is over.
            bool crashed = CrashPolicy.LooksCrashed(
                true, false, false, isRunning: false,
                now: Now.AddSeconds(Grace), startedAt: Now, startupGraceSeconds: Grace);
            Assert.True(crashed);
        }

        [Fact]
        public void An_observably_running_server_is_never_a_crash()
        {
            bool crashed = CrashPolicy.LooksCrashed(
                true, false, false, isRunning: true,
                now: Now.AddSeconds(Grace + 100), startedAt: Now, startupGraceSeconds: Grace);
            Assert.False(crashed);
        }

        [Fact]
        public void An_operation_in_progress_is_not_a_crash()
        {
            bool crashed = CrashPolicy.LooksCrashed(
                true, operationInProgress: true, autoRestartPaused: false, isRunning: false,
                now: Now.AddSeconds(Grace + 1), startedAt: Now, startupGraceSeconds: Grace);
            Assert.False(crashed);
        }

        [Fact]
        public void A_paused_server_is_not_a_crash()
        {
            bool crashed = CrashPolicy.LooksCrashed(
                true, false, autoRestartPaused: true, isRunning: false,
                now: Now.AddSeconds(Grace + 1), startedAt: Now, startupGraceSeconds: Grace);
            Assert.False(crashed);
        }

        [Fact]
        public void A_server_not_meant_to_run_is_not_a_crash()
        {
            bool crashed = CrashPolicy.LooksCrashed(
                shouldBeRunning: false, operationInProgress: false, autoRestartPaused: false,
                isRunning: false, now: Now.AddSeconds(Grace + 1), startedAt: Now, startupGraceSeconds: Grace);
            Assert.False(crashed);
        }

        [Fact]
        public void With_no_start_timestamp_a_not_running_server_is_a_crash()
        {
            // No recorded start (e.g. a server already up when the app launched): with no grace
            // window to honor, a server that should be running but isn't is a genuine crash.
            bool crashed = CrashPolicy.LooksCrashed(
                true, false, false, isRunning: false,
                now: Now, startedAt: null, startupGraceSeconds: Grace);
            Assert.True(crashed);
        }
    }
}
