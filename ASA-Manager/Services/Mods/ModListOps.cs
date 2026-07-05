using System.Collections.Generic;
using System.Linq;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool.Services.Mods
{
    /// <summary>Copy/merge operations for mod lists, preserving order and de-duplicating by ProjectId.</summary>
    public static class ModListOps
    {
        public static List<ModEntry> Replace(IEnumerable<ModEntry> source, bool includeDisabled)
            => source.Where(m => includeDisabled || m.Enabled)
                     .Select(m => new ModEntry(m.ProjectId, m.Enabled))
                     .ToList();

        public static List<ModEntry> Merge(IList<ModEntry> target, IEnumerable<ModEntry> source, bool includeDisabled)
        {
            var result = target.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
            var have = new HashSet<ulong>(result.Select(m => m.ProjectId));
            foreach (var m in source.Where(m => includeDisabled || m.Enabled))
            {
                if (have.Add(m.ProjectId)) result.Add(new ModEntry(m.ProjectId, m.Enabled));
            }
            return result;
        }
    }
}
