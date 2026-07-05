using System.Collections.Generic;
using System.Linq;
using ARKServerCreationTool;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class StaggeredStarterTests
    {
        // SelectServersToStart is pure — it filters only on the ExcludeFromBulkStart flag.
        private static ASCTServerConfig Srv(int id, bool exclude = false)
            => new ASCTServerConfig(id, (ushort)(7777 + id)) { ExcludeFromBulkStart = exclude };

        [Fact]
        public void Excludes_flagged_servers_and_preserves_order()
        {
            var a = Srv(0);
            var b = Srv(1, exclude: true);
            var c = Srv(2);

            var result = StaggeredStarter.SelectServersToStart(new[] { a, b, c });

            Assert.Equal(new[] { 0, 2 }, result.Select(s => s.ID).ToArray());
        }

        [Fact]
        public void Empty_input_yields_empty()
        {
            var result = StaggeredStarter.SelectServersToStart(new List<ASCTServerConfig>());
            Assert.Empty(result);
        }
    }
}
