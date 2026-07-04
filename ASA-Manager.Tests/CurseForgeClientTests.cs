using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.CurseForge;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class CurseForgeClientTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _json;
            public HttpRequestMessage? LastRequest;
            public string? LastBody;
            public StubHandler(string json) { _json = json; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                LastRequest = request;
                if (request.Content != null) LastBody = await request.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
                };
            }
        }

        private const string BatchJson = @"{""data"":[
            {""id"":111,""name"":""Alpha"",""summary"":""a"",""downloadCount"":10,
             ""authors"":[{""name"":""Ann""}],
             ""logo"":{""thumbnailUrl"":""http://x/thumb.png"",""url"":""http://x/full.png""},
             ""latestFiles"":[{""id"":5001,""fileDate"":""2026-06-01T00:00:00Z"",""fileLength"":100,""displayName"":""v1""}],
             ""dateModified"":""2026-06-01T00:00:00Z""}
        ]}";

        [Fact]
        public async Task GetModsAsync_parses_batch_response()
        {
            var handler = new StubHandler(BatchJson);
            var client = new CurseForgeClient(new HttpClient(handler), "test-key");
            var mods = await client.GetModsAsync(new ulong[] { 111 });
            Assert.Single(mods);
            Assert.Equal(111, mods[0].Id);
            Assert.Equal("Alpha", mods[0].Name);
            Assert.Equal("Ann", mods[0].Authors[0].Name);
            Assert.Equal("http://x/thumb.png", mods[0].Logo!.ThumbnailUrl);
            Assert.Equal(5001, mods[0].LatestFiles[0].Id);
        }

        [Fact]
        public async Task GetModsAsync_sends_api_key_and_modIds_body()
        {
            var handler = new StubHandler(BatchJson);
            var client = new CurseForgeClient(new HttpClient(handler), "test-key");
            await client.GetModsAsync(new ulong[] { 111, 222 });
            Assert.True(handler.LastRequest!.Headers.Contains("x-api-key"));
            Assert.Contains("\"modIds\"", handler.LastBody);
            Assert.Contains("111", handler.LastBody);
            Assert.Contains("222", handler.LastBody);
        }

        [Fact]
        public void HasKey_false_when_null_or_blank()
        {
            Assert.False(new CurseForgeClient(new HttpClient(), null).HasKey);
            Assert.False(new CurseForgeClient(new HttpClient(), "  ").HasKey);
            Assert.True(new CurseForgeClient(new HttpClient(), "k").HasKey);
        }

        [Fact]
        public void NewestFile_picks_latest_by_date()
        {
            var mod = new CfMod
            {
                LatestFiles = new List<CfFile>
                {
                    new CfFile { Id = 1, FileDate = DateTimeOffset.Parse("2026-05-01T00:00:00Z") },
                    new CfFile { Id = 2, FileDate = DateTimeOffset.Parse("2026-06-01T00:00:00Z") },
                }
            };
            Assert.Equal(2, ModUpdateChecker.NewestFile(mod)!.Id);
        }

        [Fact]
        public void HasNewerFile_true_when_snapshot_older_or_missing()
        {
            var mod = new CfMod { LatestFiles = new List<CfFile> { new CfFile { Id = 2, FileDate = DateTimeOffset.Parse("2026-06-01T00:00:00Z") } } };
            Assert.True(ModUpdateChecker.HasNewerFile(mod, null));
            Assert.True(ModUpdateChecker.HasNewerFile(mod, 1));
            Assert.False(ModUpdateChecker.HasNewerFile(mod, 2));
        }

        private sealed class StatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _code;
            public StatusHandler(HttpStatusCode code) { _code = code; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent("{\"data\":[]}") });
        }

        [Fact]
        public async Task ValidateKeyAsync_ok_on_200()
        {
            var client = new CurseForgeClient(new HttpClient(new StatusHandler(HttpStatusCode.OK)), "k");
            var (ok, _) = await client.ValidateKeyAsync();
            Assert.True(ok);
        }

        [Fact]
        public async Task ValidateKeyAsync_fails_on_403()
        {
            var client = new CurseForgeClient(new HttpClient(new StatusHandler(HttpStatusCode.Forbidden)), "k");
            var (ok, _) = await client.ValidateKeyAsync();
            Assert.False(ok);
        }

        [Fact]
        public async Task ValidateKeyAsync_fails_without_key()
        {
            var client = new CurseForgeClient(new HttpClient(new StatusHandler(HttpStatusCode.OK)), null);
            var (ok, _) = await client.ValidateKeyAsync();
            Assert.False(ok);
        }
    }
}
