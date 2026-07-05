using System.Collections.Generic;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Mods;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ModListOpsTests
    {
        private static List<ModEntry> L(params (ulong id, bool en)[] items)
        {
            var l = new List<ModEntry>();
            foreach (var (id, en) in items) l.Add(new ModEntry(id, en));
            return l;
        }

        [Fact]
        public void Replace_copies_order_and_can_drop_disabled()
        {
            var source = L((111, true), (222, false), (333, true));

            var all = ModListOps.Replace(source, includeDisabled: true);
            Assert.Equal(new ulong[] { 111, 222, 333 }, all.ConvertAll(m => m.ProjectId));

            var enabledOnly = ModListOps.Replace(source, includeDisabled: false);
            Assert.Equal(new ulong[] { 111, 333 }, enabledOnly.ConvertAll(m => m.ProjectId));
        }

        [Fact]
        public void Merge_appends_new_preserving_target_order_and_dedupes()
        {
            var target = L((111, true), (222, true));
            var source = L((222, true), (333, true));
            var merged = ModListOps.Merge(target, source, includeDisabled: true);
            Assert.Equal(new ulong[] { 111, 222, 333 }, merged.ConvertAll(m => m.ProjectId));
        }
    }
}
