using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RestartOutcomeTests
    {
        [Fact]
        public void A_successful_start_is_marked_started_and_logged_done()
        {
            var outcome = RestartOutcome.From("Island", started: true);
            Assert.True(outcome.MarkStarted);
            Assert.Equal("Scheduled restart done: Island", outcome.LogLine);
        }

        [Fact]
        public void A_failed_start_is_not_marked_started()
        {
            var outcome = RestartOutcome.From("Island", started: false);
            Assert.False(outcome.MarkStarted);
        }

        [Fact]
        public void A_failed_start_is_logged_as_a_failure_not_done()
        {
            // The bug: a failed restart used to log the same "done" line as a success and mark the
            // server started, silently masking the failure. It must read as a failure instead.
            var outcome = RestartOutcome.From("Island", started: false);
            Assert.DoesNotContain("done", outcome.LogLine);
            Assert.Contains("failed", outcome.LogLine);
            Assert.Contains("Island", outcome.LogLine);
        }
    }
}
