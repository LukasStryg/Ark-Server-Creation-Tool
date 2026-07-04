using ARKServerCreationTool.Services.Mods;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ModIdParserTests
    {
        [Fact]
        public void ParseMany_reads_bare_ids_various_separators()
        {
            var ids = ModIdParser.ParseMany("111, 222\n333\r\n444");
            Assert.Equal(new ulong[] { 111, 222, 333, 444 }, ids);
        }

        [Fact]
        public void ParseMany_dedupes_preserving_first_order()
        {
            var ids = ModIdParser.ParseMany("111,222,111,333");
            Assert.Equal(new ulong[] { 111, 222, 333 }, ids);
        }

        [Fact]
        public void ParseMany_extracts_numeric_id_from_a_url_when_present()
        {
            var ids = ModIdParser.ParseMany("https://www.curseforge.com/ark-survival-ascended/mods/structures-plus/files/900935");
            Assert.Equal(new ulong[] { 900935 }, ids);
        }

        [Fact]
        public void TryParseOne_true_for_bare_id_false_for_slug_only()
        {
            Assert.True(ModIdParser.TryParseOne("900935", out var id) && id == 900935);
            Assert.False(ModIdParser.TryParseOne("https://www.curseforge.com/ark-survival-ascended/mods/structures-plus", out _));
        }
    }
}
