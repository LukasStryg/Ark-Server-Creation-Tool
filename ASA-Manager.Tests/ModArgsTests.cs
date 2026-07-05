using ARKServerCreationTool;
using ARKServerCreationTool.Models;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ModArgsTests
    {
        private static ASCTServerConfig NewServer() => new ASCTServerConfig(0, 7777);

        [Fact]
        public void ModArgs_empty_when_no_mods()
        {
            var s = NewServer();
            Assert.Equal(string.Empty, s.ModArgs);
        }

        [Fact]
        public void ModArgs_preserves_list_order_no_spaces()
        {
            var s = NewServer();
            s.Mods.Add(new ModEntry(111));
            s.Mods.Add(new ModEntry(222));
            s.Mods.Add(new ModEntry(333));
            Assert.Equal(" \"-mods=111,222,333\"", s.ModArgs);
        }

        [Fact]
        public void ModArgs_excludes_disabled_mods()
        {
            var s = NewServer();
            s.Mods.Add(new ModEntry(111));
            s.Mods.Add(new ModEntry(222, enabled: false));
            s.Mods.Add(new ModEntry(333));
            Assert.Equal(" \"-mods=111,333\"", s.ModArgs);
        }
    }
}
