using System;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class LaunchGuardTests
    {
        private static readonly DateTime Now = new DateTime(2026, 7, 5, 12, 0, 0);
        private const int Cooldown = 30;

        [Fact]
        public void Does_not_launch_when_already_observed_running()
        {
            Assert.False(LaunchGuard.ShouldLaunch(observedRunning: true, Now, lastLaunchedAt: null, Cooldown));
        }

        [Fact]
        public void Launches_when_down_and_never_launched()
        {
            Assert.True(LaunchGuard.ShouldLaunch(observedRunning: false, Now, lastLaunchedAt: null, Cooldown));
        }

        [Fact]
        public void Does_not_relaunch_within_cooldown_of_a_recent_launch()
        {
            // Freshly launched and not yet observable (ASA launcher still handing off) — must NOT
            // spawn a second ArkAscendedServer.exe, which would fight for the same ports.
            Assert.False(LaunchGuard.ShouldLaunch(observedRunning: false, Now.AddSeconds(10), lastLaunchedAt: Now, Cooldown));
        }

        [Fact]
        public void Relaunches_after_cooldown_when_still_down()
        {
            Assert.True(LaunchGuard.ShouldLaunch(observedRunning: false, Now.AddSeconds(Cooldown + 1), lastLaunchedAt: Now, Cooldown));
        }

        [Fact]
        public void At_the_cooldown_boundary_relaunches()
        {
            // Cooldown is an exclusive upper bound: at exactly lastLaunchedAt + cooldown it is over.
            Assert.True(LaunchGuard.ShouldLaunch(observedRunning: false, Now.AddSeconds(Cooldown), lastLaunchedAt: Now, Cooldown));
        }

        [Fact]
        public void Observed_running_wins_over_an_active_cooldown()
        {
            Assert.False(LaunchGuard.ShouldLaunch(observedRunning: true, Now.AddSeconds(1), lastLaunchedAt: Now, Cooldown));
        }
    }
}
