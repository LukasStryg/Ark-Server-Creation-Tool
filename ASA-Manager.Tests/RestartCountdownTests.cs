using System;
using System.Linq;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RestartCountdownTests
    {
        [Fact]
        public void Builds_descending_steps_with_gaps_to_next_warning()
        {
            var steps = RestartCountdown.Steps(new[] { 5, 1 });

            Assert.Equal(2, steps.Count);
            Assert.Equal("Server restarting in 5 minutes", steps[0].Message);
            Assert.Equal(TimeSpan.FromMinutes(4), steps[0].WaitAfter); // 5 -> 1 is a 4-minute gap
            Assert.Equal("Server restarting in 1 minute", steps[1].Message);
            Assert.Equal(TimeSpan.FromMinutes(1), steps[1].WaitAfter); // last warning waits its own minutes
        }

        [Fact]
        public void Sorts_dedups_and_drops_non_positive()
        {
            var steps = RestartCountdown.Steps(new[] { 1, 5, 5, 0, -2, 10 });
            Assert.Equal(3, steps.Count);
            Assert.Equal("Server restarting in 10 minutes", steps[0].Message);
            Assert.Equal("Server restarting in 5 minutes", steps[1].Message);
            Assert.Equal("Server restarting in 1 minute", steps[2].Message);
        }

        [Fact]
        public void Empty_input_yields_no_steps()
        {
            Assert.Empty(RestartCountdown.Steps(Array.Empty<int>()));
        }
    }
}
