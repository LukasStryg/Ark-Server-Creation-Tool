using ARKServerCreationTool;
using ARKServerCreationTool.Models;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RconLaunchArgsTests
    {
        private static ASCTServerConfig Server()
        {
            var s = new ASCTServerConfig(0, 7777);
            s.RconEnabled = true;
            s.RconPort = 27020;
            s.ServerAdminPassword = "secretpw";
            return s;
        }

        [Fact]
        public void MapQueryOptions_puts_admin_password_last()
        {
            var s = Server();
            string opts = s.MapQueryOptions;
            Assert.StartsWith("?", opts);
            Assert.Contains("RCONEnabled=True", opts);
            Assert.Contains("RCONPort=27020", opts);
            Assert.EndsWith("?ServerAdminPassword=secretpw", opts);
        }

        [Fact]
        public void MapQueryOptions_includes_multihome_before_password()
        {
            var s = Server();
            s.UseMultihome = true;
            s.IPAddress = "10.0.0.5";
            string opts = s.MapQueryOptions;
            Assert.Contains("MultiHome=10.0.0.5", opts);
            Assert.EndsWith("?ServerAdminPassword=secretpw", opts);
        }

        [Fact]
        public void LaunchArguments_contains_rcon_and_mods()
        {
            var s = Server();
            s.Mods.Add(new ModEntry(111));
            string args = s.LaunchArguments;
            Assert.Contains("?RCONEnabled=True", args);
            Assert.Contains("\"-mods=111\"", args);
            // ServerAdminPassword must sit in the quoted map token, i.e. before the dash -port arg
            int pwIdx = args.IndexOf("ServerAdminPassword=secretpw");
            int portIdx = args.IndexOf("\"-port=");
            Assert.True(pwIdx > 0 && pwIdx < portIdx, "admin password must sit in the map ?-chain before dash args");
        }

        [Fact]
        public void MapQueryOptions_empty_when_rcon_disabled_and_no_multihome()
        {
            var s = Server();
            s.RconEnabled = false;
            Assert.Equal(string.Empty, s.MapQueryOptions);
        }

        [Fact]
        public void GenerateAdminPassword_is_long_and_alphanumeric()
        {
            string pw = ASCTServerConfig.GenerateAdminPassword();
            Assert.True(pw.Length >= 16);
            Assert.All(pw, c => Assert.True(char.IsLetterOrDigit(c)));
        }
    }
}
