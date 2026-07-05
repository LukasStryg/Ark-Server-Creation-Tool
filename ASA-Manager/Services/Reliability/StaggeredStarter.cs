using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure selection of which servers a bulk-start operation should launch, in order.</summary>
    public static class StaggeredStarter
    {
        // Pure: filters on ExcludeFromBulkStart only. Skipping already-running servers needs
        // ASCTServerConfig.IsRunning (which requires the loaded config singleton), so the caller
        // does that at runtime — see ReliabilityCoordinator.StartStaggeredAsync.
        public static IReadOnlyList<ASCTServerConfig> SelectServersToStart(IEnumerable<ASCTServerConfig> servers)
            => servers.Where(s => !s.ExcludeFromBulkStart).ToList();
    }
}
