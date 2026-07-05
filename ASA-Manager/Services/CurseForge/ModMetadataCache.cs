using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ARKServerCreationTool.Services.CurseForge
{
    /// <summary>Shared, disk-persisted cache of resolved CurseForge metadata keyed by Project ID.</summary>
    public class ModMetadataCache
    {
        [JsonIgnore] public const string FileName = "ModMetadataCache.json";

        [JsonProperty] private Dictionary<ulong, ModMetadata> _byId = new();

        public bool TryGet(ulong id, out ModMetadata meta)
        {
            if (_byId.TryGetValue(id, out var m)) { meta = m; return true; }
            meta = new ModMetadata { ProjectId = id };
            return false;
        }

        public bool IsStale(ulong id, TimeSpan ttl, DateTimeOffset nowUtc)
        {
            if (!_byId.TryGetValue(id, out var m)) return true;
            return nowUtc - m.LastCheckedUtc > ttl;
        }

        public void Upsert(ModMetadata meta) => _byId[meta.ProjectId] = meta;

        /// <summary>Batch-resolve the given ids via CurseForge and update the cache. No-op if the client has no key.</summary>
        public async Task RefreshAsync(IEnumerable<ulong> ids, CurseForgeClient client, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!client.HasKey) return;
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return;

            var mods = await client.GetModsAsync(idList, ct);
            foreach (var mod in mods)
            {
                var newest = ModUpdateChecker.NewestFile(mod);
                Upsert(new ModMetadata
                {
                    ProjectId = (ulong)mod.Id,
                    Name = mod.Name,
                    Author = mod.Authors.FirstOrDefault()?.Name,
                    ThumbnailUrl = mod.Logo?.ThumbnailUrl,
                    LatestFileId = newest?.Id,
                    LatestFileDate = newest?.FileDate,
                    FileLength = newest?.FileLength,
                    LastCheckedUtc = nowUtc
                });
            }
        }

        public void Save()
        {
            string tmp = FileName + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            File.Move(tmp, FileName, overwrite: true); // atomic replace so a mid-write crash can't leave a truncated cache
        }

        public static ModMetadataCache Load()
        {
            try
            {
                if (!File.Exists(FileName)) return new ModMetadataCache();
                return JsonConvert.DeserializeObject<ModMetadataCache>(File.ReadAllText(FileName)) ?? new ModMetadataCache();
            }
            catch
            {
                return new ModMetadataCache(); // a corrupt/locked cache must never crash the app
            }
        }
    }
}
