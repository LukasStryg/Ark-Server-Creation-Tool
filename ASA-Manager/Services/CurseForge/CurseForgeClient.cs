using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ARKServerCreationTool.Services.CurseForge
{
    /// <summary>Thin client for the CurseForge for Studios REST API (api.curseforge.com/v1).</summary>
    public class CurseForgeClient
    {
        public const long AsaGameId = 83374; // ARK: Survival Ascended (verify at runtime via GET /v1/games/83374)
        private const string BaseUrl = "https://api.curseforge.com";

        private readonly HttpClient _http;
        private readonly string? _apiKey;

        public CurseForgeClient(HttpClient http, string? apiKey)
        {
            _http = http;
            _apiKey = apiKey;
        }

        public bool HasKey => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>Batch-resolves mods by Project ID via POST /v1/mods. Unknown ids are omitted; match by returned Id.</summary>
        public async Task<IReadOnlyList<CfMod>> GetModsAsync(IEnumerable<ulong> ids, CancellationToken ct = default)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return new List<CfMod>();

            var body = JsonConvert.SerializeObject(new { modIds = idList });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/mods")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", _apiKey ?? string.Empty);
            req.Headers.Add("Accept", "application/json");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonConvert.DeserializeObject<CfListResponse<CfMod>>(json);
            return parsed?.Data ?? new List<CfMod>();
        }

        /// <summary>Checks whether the configured API key is accepted by CurseForge (GET /v1/games).</summary>
        public async Task<(bool ok, string message)> ValidateKeyAsync(CancellationToken ct = default)
        {
            if (!HasKey) return (false, "No API key set.");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/games");
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("Accept", "application/json");
            try
            {
                using var resp = await _http.SendAsync(req, ct);
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return (false, $"Key rejected by CurseForge ({(int)resp.StatusCode}). Double-check the key.");
                if (!resp.IsSuccessStatusCode)
                    return (false, $"Unexpected response from CurseForge: {(int)resp.StatusCode}.");
                return (true, "API key is valid.");
            }
            catch (System.Exception ex) { return (false, $"Could not reach CurseForge: {ex.Message}"); }
        }
    }
}
