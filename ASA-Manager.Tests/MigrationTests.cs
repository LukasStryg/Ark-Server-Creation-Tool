using ARKServerCreationTool;
using Newtonsoft.Json;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class MigrationTests
    {
        [Fact]
        public void Legacy_modIDs_array_migrates_into_Mods()
        {
            // Old config shape: mods stored as a JSON array under "modIDs".
            string json = "{\"ID\":0,\"Name\":\"s\",\"GameDirectory\":\"d\",\"GamePort\":7777,\"modIDs\":[111,222]}";
            var s = JsonConvert.DeserializeObject<ASCTServerConfig>(json)!;
            Assert.Equal(2, s.Mods.Count);
            Assert.Equal(111ul, s.Mods[0].ProjectId);
            Assert.Equal(222ul, s.Mods[1].ProjectId);
            Assert.True(s.Mods[0].Enabled);
        }

        [Fact]
        public void Migrated_config_does_not_re_serialize_modIDs()
        {
            string json = "{\"ID\":0,\"Name\":\"s\",\"GameDirectory\":\"d\",\"GamePort\":7777,\"modIDs\":[111]}";
            var s = JsonConvert.DeserializeObject<ASCTServerConfig>(json)!;
            string reserialized = JsonConvert.SerializeObject(s);
            Assert.DoesNotContain("modIDs", reserialized);
            Assert.Contains("\"Mods\"", reserialized);
        }
    }
}
