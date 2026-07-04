# Mod Management Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the unordered mod set with an ordered, reorderable, copy-able mod list; resolve CurseForge names/updates; and add a minimal RCON layer for graceful restarts.

**Architecture:** Fork of Ragonz/Ark-Server-Creation-Tool (C#/.NET 9/WPF, code-behind). New correctness-critical logic lives in a testable `Services/` layer and small pure helpers; WPF windows stay thin. Config stays JSON (Newtonsoft). Mods become `List<ModEntry>` on `ASCTServerConfig`; display metadata lives in a shared `ModMetadataCache`.

**Tech Stack:** C# / .NET 9 (`net9.0-windows7.0`, `Nullable` enabled), WPF, Newtonsoft.Json 13.0.4, xUnit (new test project), Source-RCON over TCP, CurseForge REST API v1.

## Global Constraints

- Target framework `net9.0-windows7.0`; `Nullable` is **enabled** — annotate reference types.
- Root namespace `ARKServerCreationTool`; use **block namespaces** (match existing files). New services go under `ARKServerCreationTool.Services.*`, models under `ARKServerCreationTool.Models`.
- **Never auto-sort/normalize mod order.** The `Mods` list order IS the load order; preserve it verbatim.
- `-mods=` value is **comma-separated, no spaces**; only `Enabled == true` mods are included; empty ⇒ no `-mods` token at all.
- In the launch command's `?`-option chain, **`ServerAdminPassword` MUST be the last `?`-option** (parse-swallow bug guard).
- CurseForge: base `https://api.curseforge.com`, all paths `/v1/...`; headers `x-api-key: <key>` and `Accept: application/json`. Missing/invalid key ⇒ HTTP 403 — degrade gracefully, never block editing.
- CurseForge batch: `POST /v1/mods` body `{"modIds":[<int>,...]}` ⇒ `{"data":[Mod,...]}`; **match results by each returned `id`, not by index** (unknown ids are omitted). ASA `gameId` is `83374` (verify via `GET /v1/games/83374` before hardcoding without fallback).
- RCON is **standard Valve Source RCON over TCP**. Packet: `[Size:int32 LE][Id:int32 LE][Type:int32 LE][Body:bytes][0x00][0x00]`; `Size = Body.Length + 10` (excludes its own 4 bytes). Types: `SERVERDATA_RESPONSE_VALUE=0`, `SERVERDATA_AUTH_RESPONSE=2`, `SERVERDATA_EXECCOMMAND=2`, `SERVERDATA_AUTH=3`. Auth success = auth-response `Id == request Id`; failure = `Id == -1`. Do **not** copy the `ASA_RCon` reference client's receive offsets (it has an off-by-one bug); parse Id at payload offset 0, Type at 4.
- GPL-3.0: the fork stays source-available.
- Commit after every task with a conventional-commit message. Run on Windows (WPF).

---

## File structure

```
ASA-Manager/
  Models/ModEntry.cs                              (Task 1, NEW)
  ASCTGlobalConfig.cs                             (Tasks 1,2 — ASCTServerConfig lives here)
  ServerConfigurationWindow.xaml(.cs)             (Tasks 1,7,8,10 — mod editor)
  CopyModsToServersWindow.xaml(.cs)               (Task 9, NEW)
  ServerWindow.xaml(.cs)                          (Task 11 — graceful stop/restart)
  ASCTConfigWindow.xaml(.cs)                      (Task 10 — CurseForge API key field)
  Services/
    Common/ListReorder.cs                         (Task 7, NEW)
    Rcon/RconPacket.cs, RconClient.cs, IRconClient.cs   (Task 3, NEW)
    CurseForge/CurseForgeModels.cs, CurseForgeClient.cs, ModUpdateChecker.cs   (Task 4, NEW)
    CurseForge/ModMetadata.cs, ModMetadataCache.cs      (Task 5, NEW)
    Servers/ServerControlService.cs, IServerProcessController.cs, GameProcessControllerAdapter.cs  (Task 6, NEW)
    Mods/ModIdParser.cs                            (Task 8, NEW)
    Mods/ModListOps.cs                             (Task 9, NEW)
ASA-Manager.Tests/                                (Task 1, NEW xUnit project)
```

---

## Task 1: Test project + `ModEntry` model + ordered `Mods` list + migration + `ModArgs`

**Files:**
- Create: `ASA-Manager.Tests/ASA-Manager.Tests.csproj`, `ASA-Manager.Tests/ModArgsTests.cs`, `ASA-Manager.Tests/MigrationTests.cs`
- Create: `ASA-Manager/Models/ModEntry.cs`
- Modify: `ASA-Manager/ASCTGlobalConfig.cs` (the `ASCTServerConfig` class: add `Mods`, replace `modIDs` field with legacy `LegacyModIDs`, add `[OnDeserialized]` migration, rewrite `ModArgs`)
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml.cs` (mechanical compile-fix: use `Mods` instead of `modIDs`, keep current plain-ListBox behavior)
- Modify: `ARK Server Creation Tool.sln` (add test project)

**Interfaces:**
- Produces: `ARKServerCreationTool.Models.ModEntry { ulong ProjectId; bool Enabled; }`; `ASCTServerConfig.Mods : List<ModEntry>`; `ASCTServerConfig.ModArgs : string` (now derived from `Mods`).

- [ ] **Step 1: Create the xUnit test project**

Run:
```bash
cd "/Users/lukas/ark-server-creation-tool"
dotnet new xunit -n "ASA-Manager.Tests" -o "ASA-Manager.Tests" -f net9.0-windows7.0
dotnet add "ASA-Manager.Tests/ASA-Manager.Tests.csproj" reference "ASA-Manager/ARK Server Creation Tool.csproj"
dotnet sln "ARK Server Creation Tool.sln" add "ASA-Manager.Tests/ASA-Manager.Tests.csproj"
```
Expected: project created, reference + solution entry added. (The test project targets `net9.0-windows7.0` because it references the WPF app assembly.)

- [ ] **Step 2: Write the failing `ModArgs` tests**

Create `ASA-Manager.Tests/ModArgsTests.cs`:
```csharp
using System.Collections.Generic;
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj"`
Expected: FAIL — `ModEntry` does not exist / `ASCTServerConfig.Mods` not found (compile error).

- [ ] **Step 4: Create the `ModEntry` model**

Create `ASA-Manager/Models/ModEntry.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ARKServerCreationTool.Models
{
    /// <summary>A single mod in a server's ordered load-order list. Order = load order.</summary>
    public class ModEntry : INotifyPropertyChanged
    {
        private bool _enabled = true;

        public ulong ProjectId { get; set; }

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        public ModEntry() { }

        public ModEntry(ulong projectId, bool enabled = true)
        {
            ProjectId = projectId;
            _enabled = enabled;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

- [ ] **Step 5: Add `Mods`, legacy field, migration, and new `ModArgs` to `ASCTServerConfig`**

In `ASA-Manager/ASCTGlobalConfig.cs`, add `using` directives at the top of the file:
```csharp
using System;
using System.Runtime.Serialization;
using ARKServerCreationTool.Models;
```
In the `ASCTServerConfig` class, **replace** the line `public HashSet<ulong> modIDs = new HashSet<ulong>();` with:
```csharp
        /// <summary>Ordered mod load-order list. The order of this list is the launch order.</summary>
        public List<ModEntry> Mods { get; set; } = new List<ModEntry>();

        /// <summary>Legacy unordered mod ids from pre-overhaul configs; migrated into <see cref="Mods"/> on load. Not written back once migrated.</summary>
        [JsonProperty("modIDs", NullValueHandling = NullValueHandling.Ignore)]
        public HashSet<ulong>? LegacyModIDs { get; set; }

        [OnDeserialized]
        internal void MigrateLegacyMods(StreamingContext context)
        {
            if (Mods.Count == 0 && LegacyModIDs != null && LegacyModIDs.Count > 0)
            {
                var seen = new HashSet<ulong>();
                foreach (var id in LegacyModIDs)
                {
                    if (seen.Add(id)) Mods.Add(new ModEntry(id));
                }
            }
            LegacyModIDs = null; // drop from future saves
        }
```
**Replace** the existing `ModArgs` property body with:
```csharp
        [JsonIgnore]
        public string ModArgs
        {
            get
            {
                var enabled = Mods.Where(m => m.Enabled).Select(m => m.ProjectId).ToList();
                if (enabled.Count == 0) return string.Empty;
                return $" \"-mods={string.Join(",", enabled)}\"";
            }
        }
```

- [ ] **Step 6: Mechanical compile-fix in `ServerConfigurationWindow.xaml.cs`**

Update the four spots that referenced `targetServer.modIDs` to use `Mods`, preserving the current plain-ListBox (ID-only) behavior for now (Task 7 replaces this UX):
- `UpdateModList()`: replace the loop body to add ids from `Mods`:
```csharp
        private void UpdateModList()
        {
            lst_modIds.Items.Clear();
            foreach (var mod in targetServer.Mods) lst_modIds.Items.Add(mod.ProjectId);
            lst_modIds.Items.Refresh();
        }
```
- `UpdateServerObject`: replace `serv.modIDs = lst_modIds.Items.Cast<ulong>().ToHashSet();` with:
```csharp
            serv.Mods = lst_modIds.Items.Cast<ulong>().Select(id => new ModEntry(id)).ToList();
```
- `btn_addMod_Click`: replace `targetServer.modIDs.Add(modID);` with:
```csharp
                if (!targetServer.Mods.Any(m => m.ProjectId == modID)) targetServer.Mods.Add(new ModEntry(modID));
```
- `btn_removeMod_Click`: replace `targetServer.modIDs.RemoveWhere(...)` with:
```csharp
            var toRemove = lst_modIds.SelectedItems.Cast<ulong>().ToHashSet();
            targetServer.Mods.RemoveAll(m => toRemove.Contains(m.ProjectId));
```
Add `using ARKServerCreationTool.Models;` to the file's usings.

- [ ] **Step 7: Write the failing migration test**

Create `ASA-Manager.Tests/MigrationTests.cs`:
```csharp
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
```

- [ ] **Step 8: Run all tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj"`
Expected: PASS (5 tests). If `ASCTServerConfig`'s constructor requires other args, the tests already pass `(0, 7777)` matching `ASCTServerConfig(int ID, ushort GamePort)`.

- [ ] **Step 9: Build the app to confirm the window compile-fix**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add "ASA-Manager/Models/ModEntry.cs" "ASA-Manager/ASCTGlobalConfig.cs" "ASA-Manager/ServerConfigurationWindow.xaml.cs" "ASA-Manager.Tests" "ARK Server Creation Tool.sln"
git commit -m "feat(mods): ordered Mods list with legacy migration and order-preserving ModArgs"
```

---

## Task 2: RCON launch options (`ServerAdminPassword` last) + password/port assignment

**Files:**
- Modify: `ASA-Manager/ASCTGlobalConfig.cs` (`ASCTServerConfig`: add `RconEnabled`, `RconPort`, `ServerAdminPassword`, `MapQueryOptions`, adjust `LaunchArguments`; add password generator. `ASCTGlobalConfig`: add `NextAvailableRconPort()` and `EnsureServerRconDefaults()`, call it after load)
- Create: `ASA-Manager.Tests/RconLaunchArgsTests.cs`

**Interfaces:**
- Consumes: `ASCTServerConfig.Mods`, `ASCTServerConfig.ModArgs` (Task 1).
- Produces: `ASCTServerConfig.RconEnabled : bool`, `.RconPort : ushort`, `.ServerAdminPassword : string`; `ASCTGlobalConfig.NextAvailableRconPort() : ushort`; `ASCTServerConfig.GenerateAdminPassword() : string` (static).

- [ ] **Step 1: Write the failing launch-args tests**

Create `ASA-Manager.Tests/RconLaunchArgsTests.cs`:
```csharp
using ARKServerCreationTool;
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
            s.Mods.Add(new ARKServerCreationTool.Models.ModEntry(111));
            string args = s.LaunchArguments;
            Assert.Contains("?RCONEnabled=True", args);
            Assert.Contains("\"-mods=111\"", args);
            // ServerAdminPassword must be inside the quoted map token, i.e. before the closing quote + dash args
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter RconLaunchArgsTests`
Expected: FAIL — `MapQueryOptions` / `RconPort` / `GenerateAdminPassword` not defined.

- [ ] **Step 3: Add RCON fields, password generator, and `MapQueryOptions` to `ASCTServerConfig`**

In `ASCTServerConfig`, add fields (near the other settings properties):
```csharp
        public bool RconEnabled { get; set; } = true;
        public ushort RconPort { get; set; }
        public string ServerAdminPassword { get; set; } = string.Empty;

        public static string GenerateAdminPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
            var sb = new System.Text.StringBuilder(20);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }
```
Add the `MapQueryOptions` property (replaces the role of `MultihomeArgs` in the map token):
```csharp
        [JsonIgnore]
        public string MapQueryOptions
        {
            get
            {
                var opts = new List<string>();
                if (UseMultihome && IPAddress != string.Empty) opts.Add($"MultiHome={IPAddress}");
                if (RconEnabled)
                {
                    opts.Add("RCONEnabled=True");
                    opts.Add($"RCONPort={RconPort}");
                }
                // ServerAdminPassword MUST be the last ?-option (parse-swallow guard).
                if (RconEnabled && !string.IsNullOrEmpty(ServerAdminPassword))
                    opts.Add($"ServerAdminPassword={ServerAdminPassword}");
                return opts.Count == 0 ? string.Empty : "?" + string.Join("?", opts);
            }
        }
```

- [ ] **Step 4: Use `MapQueryOptions` in `LaunchArguments`**

In `ASCTServerConfig.LaunchArguments`, **replace** `{MultihomeArgs}` with `{MapQueryOptions}` in the generated string:
```csharp
                    return
                        $"\"{Map}{MapQueryOptions}\" \"-port={GamePort}\" -WinLiveMaxPlayers={Slots}{ModArgs}{ClusterArgs}{CrossplayArgs}{NoBattleyeArgs}{ActiveEventArgs} -log -servergamelog"
                            .Trim();
```
(Leave the existing `MultihomeArgs` property in place — it is now unused by the generated path but harmless; do not delete to avoid touching unrelated code.)

- [ ] **Step 5: Add port assignment + defaults backfill to `ASCTGlobalConfig`**

Add to `ASCTGlobalConfig`:
```csharp
        public ushort NextAvailableRconPort()
        {
            ushort start = StartingRCONPort;
            var used = new HashSet<ushort>(Servers.Select(s => s.RconPort).Where(p => p != 0));
            for (ushort p = start; p < ushort.MaxValue; p += PortIncrement)
            {
                // avoid colliding with any assigned game or rcon port
                if (!used.Contains(p) && Servers.All(s => s.GamePort != p)) return p;
            }
            return start;
        }

        /// <summary>Backfill RCON port + admin password for servers loaded from a pre-RCON config.</summary>
        public void EnsureServerRconDefaults()
        {
            bool changed = false;
            foreach (var s in Servers)
            {
                if (s.RconPort == 0) { s.RconPort = NextAvailableRconPort(); changed = true; }
                if (string.IsNullOrEmpty(s.ServerAdminPassword)) { s.ServerAdminPassword = ASCTServerConfig.GenerateAdminPassword(); changed = true; }
            }
            if (changed) Save();
        }
```
In `ASCTGlobalConfig.LoadConfig()`, after the config is deserialized (before returning), call the backfill:
```csharp
                returnConfig = JsonConvert.DeserializeObject<ASCTGlobalConfig>(json);
                returnConfig?.EnsureServerRconDefaults();
```

- [ ] **Step 6: Assign RCON port + password when a new server is created**

In `ServerConfigurationWindow` constructor, in the `if (targetServerID == null)` (new-server) branch, after `targetServer = new ASCTServerConfig(...)`, add:
```csharp
                targetServer.RconPort = config.NextAvailableRconPort();
                targetServer.ServerAdminPassword = ASCTServerConfig.GenerateAdminPassword();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter RconLaunchArgsTests`
Expected: PASS (5 tests).

- [ ] **Step 8: Build the app**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add "ASA-Manager/ASCTGlobalConfig.cs" "ASA-Manager/ServerConfigurationWindow.xaml.cs" "ASA-Manager.Tests/RconLaunchArgsTests.cs"
git commit -m "feat(rcon): enable RCON per server via launch args with ServerAdminPassword last"
```

---

## Task 3: Source-RCON packet codec + client

**Files:**
- Create: `ASA-Manager/Services/Rcon/RconPacket.cs`, `ASA-Manager/Services/Rcon/IRconClient.cs`, `ASA-Manager/Services/Rcon/RconClient.cs`
- Create: `ASA-Manager.Tests/RconPacketTests.cs`

**Interfaces:**
- Produces:
  - `RconPacket { int Id; int Type; string Body; byte[] Encode(); static RconPacket Read(Stream); }`
  - `interface IRconClient : IDisposable { Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default); Task<string> ExecuteAsync(string command, CancellationToken ct = default); }`
  - `class RconClient : IRconClient` with `RconClient(string host, int port)`.
  - Const ints: `RconPacket.TypeResponseValue=0`, `TypeAuthResponse=2`, `TypeExecCommand=2`, `TypeAuth=3`.

- [ ] **Step 1: Write the failing packet-codec tests**

Create `ASA-Manager.Tests/RconPacketTests.cs`:
```csharp
using System.IO;
using ARKServerCreationTool.Services.Rcon;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RconPacketTests
    {
        [Fact]
        public void Encode_produces_size_equal_to_body_plus_ten()
        {
            var p = new RconPacket { Id = 1, Type = RconPacket.TypeExecCommand, Body = "SaveWorld" };
            byte[] bytes = p.Encode();
            // total on wire = size field (4) + size value; size value = body(9) + 10 = 19 => total 23
            Assert.Equal(23, bytes.Length);
            int size = System.BitConverter.ToInt32(bytes, 0);
            Assert.Equal(19, size);
        }

        [Fact]
        public void Encode_writes_id_and_type_little_endian_and_two_trailing_nulls()
        {
            var p = new RconPacket { Id = 0x0BADC0DE, Type = RconPacket.TypeAuth, Body = "pw" };
            byte[] bytes = p.Encode();
            Assert.Equal(0x0BADC0DE, System.BitConverter.ToInt32(bytes, 4)); // Id at offset 4
            Assert.Equal(RconPacket.TypeAuth, System.BitConverter.ToInt32(bytes, 8)); // Type at offset 8
            Assert.Equal(0, bytes[bytes.Length - 1]);
            Assert.Equal(0, bytes[bytes.Length - 2]);
        }

        [Fact]
        public void Read_round_trips_an_encoded_packet()
        {
            var original = new RconPacket { Id = 42, Type = RconPacket.TypeResponseValue, Body = "World Saved" };
            using var ms = new MemoryStream(original.Encode());
            var read = RconPacket.Read(ms);
            Assert.Equal(42, read.Id);
            Assert.Equal(RconPacket.TypeResponseValue, read.Type);
            Assert.Equal("World Saved", read.Body);
        }

        [Fact]
        public void Read_parses_auth_failure_id_minus_one()
        {
            var fail = new RconPacket { Id = -1, Type = RconPacket.TypeAuthResponse, Body = "" };
            using var ms = new MemoryStream(fail.Encode());
            var read = RconPacket.Read(ms);
            Assert.Equal(-1, read.Id);
            Assert.Equal(RconPacket.TypeAuthResponse, read.Type);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter RconPacketTests`
Expected: FAIL — `RconPacket` not defined.

- [ ] **Step 3: Implement `RconPacket`**

Create `ASA-Manager/Services/Rcon/RconPacket.cs`:
```csharp
using System;
using System.IO;
using System.Text;

namespace ARKServerCreationTool.Services.Rcon
{
    /// <summary>Valve Source RCON packet. Wire: [Size:i32 LE][Id:i32 LE][Type:i32 LE][Body][0x00][0x00]; Size = Body.Length + 10.</summary>
    public class RconPacket
    {
        public const int TypeResponseValue = 0;
        public const int TypeAuthResponse = 2;
        public const int TypeExecCommand = 2;
        public const int TypeAuth = 3;

        public int Id { get; set; }
        public int Type { get; set; }
        public string Body { get; set; } = string.Empty;

        public byte[] Encode()
        {
            byte[] body = Encoding.UTF8.GetBytes(Body);
            int size = body.Length + 10; // Id(4) + Type(4) + body + null + null
            using var ms = new MemoryStream(size + 4);
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            w.Write(size);   // BinaryWriter is little-endian
            w.Write(Id);
            w.Write(Type);
            w.Write(body);
            w.Write((byte)0); // body null terminator
            w.Write((byte)0); // empty-string null pad
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Reads exactly one framed packet from the stream. Throws EndOfStreamException if the stream closes mid-packet.</summary>
        public static RconPacket Read(Stream stream)
        {
            int size = ReadInt32(stream);
            byte[] payload = ReadExactly(stream, size);
            int id = BitConverter.ToInt32(payload, 0);
            int type = BitConverter.ToInt32(payload, 4);
            // body is payload[8 .. size-2] (last two bytes are the nulls)
            int bodyLen = Math.Max(0, size - 8 - 2);
            string body = Encoding.UTF8.GetString(payload, 8, bodyLen);
            return new RconPacket { Id = id, Type = type, Body = body };
        }

        private static int ReadInt32(Stream s)
        {
            byte[] b = ReadExactly(s, 4);
            return BitConverter.ToInt32(b, 0);
        }

        private static byte[] ReadExactly(Stream s, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = s.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("RCON stream closed mid-packet.");
                offset += read;
            }
            return buffer;
        }
    }
}
```
Note: `BitConverter`/`BinaryWriter` are little-endian on the x64 Windows target, matching the protocol.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter RconPacketTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Implement `IRconClient` and `RconClient` (network — not unit-tested)**

Create `ASA-Manager/Services/Rcon/IRconClient.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ARKServerCreationTool.Services.Rcon
{
    public interface IRconClient : IDisposable
    {
        Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default);
        Task<string> ExecuteAsync(string command, CancellationToken ct = default);
    }
}
```
Create `ASA-Manager/Services/Rcon/RconClient.cs`:
```csharp
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ARKServerCreationTool.Services.Rcon
{
    public class RconClient : IRconClient
    {
        private const int RequestId = 0x0BADC0DE;
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private int _nextId = 1;

        public RconClient(string host, int port) { _host = host; _port = port; }

        public async Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(_host, _port, ct);
            _stream = _tcp.GetStream();

            var auth = new RconPacket { Id = RequestId, Type = RconPacket.TypeAuth, Body = password };
            byte[] bytes = auth.Encode();
            await _stream.WriteAsync(bytes, ct);

            // Skip any leading empty RESPONSE_VALUE; the auth result is the AUTH_RESPONSE packet.
            for (int i = 0; i < 3; i++)
            {
                var reply = RconPacket.Read(_stream);
                if (reply.Type == RconPacket.TypeAuthResponse)
                    return reply.Id != -1 && reply.Id == RequestId;
            }
            return false;
        }

        public async Task<string> ExecuteAsync(string command, CancellationToken ct = default)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected.");
            int id = _nextId++;
            var pkt = new RconPacket { Id = id, Type = RconPacket.TypeExecCommand, Body = command };
            await _stream.WriteAsync(pkt.Encode(), ct);
            var reply = RconPacket.Read(_stream); // ARK returns a single framed response packet
            return reply.Body;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
        }
    }
}
```

- [ ] **Step 6: Build the app**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add "ASA-Manager/Services/Rcon"
git commit -m "feat(rcon): Source-RCON packet codec and TCP client"
```

---

## Task 4: CurseForge models + client + update-diff

**Files:**
- Create: `ASA-Manager/Services/CurseForge/CurseForgeModels.cs`, `ASA-Manager/Services/CurseForge/CurseForgeClient.cs`, `ASA-Manager/Services/CurseForge/ModUpdateChecker.cs`
- Create: `ASA-Manager.Tests/CurseForgeClientTests.cs`

**Interfaces:**
- Produces:
  - Models: `CfMod { long Id; string Name; string Summary; long DownloadCount; List<CfAuthor> Authors; CfLogo? Logo; List<CfFile> LatestFiles; DateTimeOffset DateModified; }`, `CfAuthor { string Name; }`, `CfLogo { string? ThumbnailUrl; string? Url; }`, `CfFile { long Id; DateTimeOffset FileDate; long FileLength; string DisplayName; }`.
  - `CurseForgeClient(HttpClient http, string? apiKey)` with `bool HasKey { get; }` and `Task<IReadOnlyList<CfMod>> GetModsAsync(IEnumerable<ulong> ids, CancellationToken ct = default)`.
  - `ModUpdateChecker.NewestFile(CfMod) : CfFile?`, `ModUpdateChecker.HasNewerFile(CfMod mod, long? snapshotFileId) : bool`.

- [ ] **Step 1: Write the failing client + update-diff tests**

Create `ASA-Manager.Tests/CurseForgeClientTests.cs`:
```csharp
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
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter CurseForgeClientTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement the models**

Create `ASA-Manager/Services/CurseForge/CurseForgeModels.cs`:
```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ARKServerCreationTool.Services.CurseForge
{
    public class CfListResponse<T> { [JsonProperty("data")] public List<T> Data { get; set; } = new(); }
    public class CfSingleResponse<T> { [JsonProperty("data")] public T? Data { get; set; } }

    public class CfMod
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("gameId")] public long GameId { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("summary")] public string Summary { get; set; } = string.Empty;
        [JsonProperty("downloadCount")] public long DownloadCount { get; set; }
        [JsonProperty("authors")] public List<CfAuthor> Authors { get; set; } = new();
        [JsonProperty("logo")] public CfLogo? Logo { get; set; }
        [JsonProperty("latestFiles")] public List<CfFile> LatestFiles { get; set; } = new();
        [JsonProperty("dateModified")] public DateTimeOffset DateModified { get; set; }
    }

    public class CfAuthor { [JsonProperty("name")] public string Name { get; set; } = string.Empty; }

    public class CfLogo
    {
        [JsonProperty("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
        [JsonProperty("url")] public string? Url { get; set; }
    }

    public class CfFile
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("fileDate")] public DateTimeOffset FileDate { get; set; }
        [JsonProperty("fileLength")] public long FileLength { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 4: Implement the client**

Create `ASA-Manager/Services/CurseForge/CurseForgeClient.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ARKServerCreationTool.Services.CurseForge
{
    public class CurseForgeClient
    {
        public const long AsaGameId = 83374;
        private const string BaseUrl = "https://api.curseforge.com";

        private readonly HttpClient _http;
        private readonly string? _apiKey;

        public CurseForgeClient(HttpClient http, string? apiKey)
        {
            _http = http;
            _apiKey = apiKey;
        }

        public bool HasKey => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<IReadOnlyList<CfMod>> GetModsAsync(IEnumerable<ulong> ids, CancellationToken ct = default)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return new List<CfMod>();

            var body = JsonConvert.SerializeObject(new { modIds = idList });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/mods")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("Accept", "application/json");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonConvert.DeserializeObject<CfListResponse<CfMod>>(json);
            return parsed?.Data ?? new List<CfMod>();
        }
    }
}
```

- [ ] **Step 5: Implement the update checker**

Create `ASA-Manager/Services/CurseForge/ModUpdateChecker.cs`:
```csharp
using System.Linq;

namespace ARKServerCreationTool.Services.CurseForge
{
    public static class ModUpdateChecker
    {
        /// <summary>The newest file for a mod, by fileDate (null if none).</summary>
        public static CfFile? NewestFile(CfMod mod)
            => mod.LatestFiles.OrderByDescending(f => f.FileDate).FirstOrDefault();

        /// <summary>True if the mod's newest file id differs from the recorded snapshot (or snapshot is missing).</summary>
        public static bool HasNewerFile(CfMod mod, long? snapshotFileId)
        {
            var newest = NewestFile(mod);
            if (newest == null) return false;
            return snapshotFileId == null || newest.Id != snapshotFileId.Value;
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter CurseForgeClientTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Commit**

```bash
git add "ASA-Manager/Services/CurseForge/CurseForgeModels.cs" "ASA-Manager/Services/CurseForge/CurseForgeClient.cs" "ASA-Manager/Services/CurseForge/ModUpdateChecker.cs" "ASA-Manager.Tests/CurseForgeClientTests.cs"
git commit -m "feat(curseforge): batch mod client, models, and update-diff"
```

---

## Task 5: Mod metadata cache

**Files:**
- Create: `ASA-Manager/Services/CurseForge/ModMetadata.cs`, `ASA-Manager/Services/CurseForge/ModMetadataCache.cs`
- Create: `ASA-Manager.Tests/ModMetadataCacheTests.cs`

**Interfaces:**
- Consumes: `CurseForgeClient`, `CfMod`, `ModUpdateChecker` (Task 4).
- Produces:
  - `ModMetadata { ulong ProjectId; string? Name; string? Author; long? LatestFileId; DateTimeOffset? LatestFileDate; long? FileLength; DateTimeOffset LastCheckedUtc; }`
  - `ModMetadataCache` with `bool TryGet(ulong id, out ModMetadata meta)`, `bool IsStale(ulong id, TimeSpan ttl, DateTimeOffset nowUtc)`, `void Upsert(ModMetadata meta)`, `Task RefreshAsync(IEnumerable<ulong> ids, CurseForgeClient client, DateTimeOffset nowUtc, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing cache tests**

Create `ASA-Manager.Tests/ModMetadataCacheTests.cs`:
```csharp
using System;
using ARKServerCreationTool.Services.CurseForge;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ModMetadataCacheTests
    {
        [Fact]
        public void Upsert_then_TryGet_returns_metadata()
        {
            var cache = new ModMetadataCache();
            cache.Upsert(new ModMetadata { ProjectId = 111, Name = "Alpha", LatestFileId = 5001, LastCheckedUtc = DateTimeOffset.Parse("2026-06-01T00:00:00Z") });
            Assert.True(cache.TryGet(111, out var meta));
            Assert.Equal("Alpha", meta.Name);
            Assert.Equal(5001, meta.LatestFileId);
        }

        [Fact]
        public void TryGet_false_for_unknown_id()
        {
            var cache = new ModMetadataCache();
            Assert.False(cache.TryGet(999, out _));
        }

        [Fact]
        public void IsStale_true_when_beyond_ttl_or_unknown()
        {
            var cache = new ModMetadataCache();
            var checkedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
            cache.Upsert(new ModMetadata { ProjectId = 111, LastCheckedUtc = checkedAt });
            Assert.True(cache.IsStale(999, TimeSpan.FromHours(24), checkedAt));                         // unknown => stale
            Assert.False(cache.IsStale(111, TimeSpan.FromHours(24), checkedAt.AddHours(1)));            // within ttl
            Assert.True(cache.IsStale(111, TimeSpan.FromHours(24), checkedAt.AddHours(25)));            // beyond ttl
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModMetadataCacheTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement `ModMetadata` and `ModMetadataCache`**

Create `ASA-Manager/Services/CurseForge/ModMetadata.cs`:
```csharp
using System;

namespace ARKServerCreationTool.Services.CurseForge
{
    public class ModMetadata
    {
        public ulong ProjectId { get; set; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? ThumbnailUrl { get; set; }
        public long? LatestFileId { get; set; }
        public DateTimeOffset? LatestFileDate { get; set; }
        public long? FileLength { get; set; }
        public DateTimeOffset LastCheckedUtc { get; set; }
    }
}
```
Create `ASA-Manager/Services/CurseForge/ModMetadataCache.cs`:
```csharp
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

        public void Save() => File.WriteAllText(FileName, JsonConvert.SerializeObject(this, Formatting.Indented));

        public static ModMetadataCache Load()
        {
            if (!File.Exists(FileName)) return new ModMetadataCache();
            return JsonConvert.DeserializeObject<ModMetadataCache>(File.ReadAllText(FileName)) ?? new ModMetadataCache();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModMetadataCacheTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/CurseForge/ModMetadata.cs" "ASA-Manager/Services/CurseForge/ModMetadataCache.cs" "ASA-Manager.Tests/ModMetadataCacheTests.cs"
git commit -m "feat(curseforge): shared mod metadata cache with TTL"
```

---

## Task 6: `ServerControlService` (graceful stop / restart)

**Files:**
- Create: `ASA-Manager/Services/Servers/IServerProcessController.cs`, `ASA-Manager/Services/Servers/GameProcessControllerAdapter.cs`, `ASA-Manager/Services/Servers/ServerControlService.cs`
- Create: `ASA-Manager.Tests/ServerControlServiceTests.cs`

**Interfaces:**
- Consumes: `IRconClient` (Task 3).
- Produces:
  - `interface IServerProcessController { bool IsRunning { get; } bool Start(); bool ForceStop(); }`
  - `class ServerControlService(Func<IRconClient> rconFactory, IServerProcessController process, string adminPassword)` with `Task<StopResult> GracefulStopAsync(TimeSpan timeout, IProgress<string>? progress = null, CancellationToken ct = default)` and `Task<bool> RestartToApplyAsync(TimeSpan stopTimeout, IProgress<string>? progress = null, CancellationToken ct = default)`.
  - `enum StopResult { AlreadyStopped, GracefulStop, ForcedStop, Failed }`

- [ ] **Step 1: Write the failing orchestration tests**

Create `ASA-Manager.Tests/ServerControlServiceTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Servers;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ServerControlServiceTests
    {
        private sealed class FakeRcon : IRconClient
        {
            public bool AuthOk = true;
            public bool ThrowOnConnect = false;
            public List<string> Commands = new();
            public Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default)
            {
                if (ThrowOnConnect) throw new Exception("connect failed");
                return Task.FromResult(AuthOk);
            }
            public Task<string> ExecuteAsync(string command, CancellationToken ct = default)
            {
                Commands.Add(command);
                return Task.FromResult(command == "SaveWorld" ? "World Saved" : "ok");
            }
            public void Dispose() { }
        }

        private sealed class FakeProcess : IServerProcessController
        {
            public bool Running = true;
            public int ForceStopCalls = 0;
            public bool StopsAfterDoExit = true;
            public bool IsRunning => Running;
            public bool Start() { Running = true; return true; }
            public bool ForceStop() { ForceStopCalls++; Running = false; return true; }
        }

        [Fact]
        public async Task Graceful_stop_saves_then_exits_without_force()
        {
            var rcon = new FakeRcon();
            var proc = new FakeProcess();
            // DoExit makes the process stop:
            var svc = new ServerControlService(() => rcon, proc, "pw");
            rcon.Commands.Clear();
            // simulate DoExit stopping the process by hooking through the fake: we stop when DoExit is issued
            var task = svc.GracefulStopAsync(TimeSpan.FromSeconds(2));
            proc.Running = false; // process exited after DoExit
            var result = await task;
            Assert.Equal(StopResult.GracefulStop, result);
            Assert.Contains("SaveWorld", rcon.Commands);
            Assert.Contains("DoExit", rcon.Commands);
            Assert.Equal(0, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Falls_back_to_force_stop_when_rcon_auth_fails()
        {
            var rcon = new FakeRcon { AuthOk = false };
            var proc = new FakeProcess();
            var svc = new ServerControlService(() => rcon, proc, "pw");
            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));
            Assert.Equal(StopResult.ForcedStop, result);
            Assert.Equal(1, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Falls_back_to_force_stop_when_rcon_connect_throws()
        {
            var rcon = new FakeRcon { ThrowOnConnect = true };
            var proc = new FakeProcess();
            var svc = new ServerControlService(() => rcon, proc, "pw");
            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));
            Assert.Equal(StopResult.ForcedStop, result);
            Assert.Equal(1, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Returns_already_stopped_when_not_running()
        {
            var proc = new FakeProcess { Running = false };
            var svc = new ServerControlService(() => new FakeRcon(), proc, "pw");
            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));
            Assert.Equal(StopResult.AlreadyStopped, result);
        }
    }
}
```
Note: the happy-path test sets `proc.Running = false` right after starting the async call to simulate the process exiting after `DoExit`; the service polls `IsRunning`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ServerControlServiceTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement the interface + service**

Create `ASA-Manager/Services/Servers/IServerProcessController.cs`:
```csharp
namespace ARKServerCreationTool.Services.Servers
{
    public interface IServerProcessController
    {
        bool IsRunning { get; }
        bool Start();
        bool ForceStop();
    }
}
```
Create `ASA-Manager/Services/Servers/ServerControlService.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;

namespace ARKServerCreationTool.Services.Servers
{
    public enum StopResult { AlreadyStopped, GracefulStop, ForcedStop, Failed }

    public class ServerControlService
    {
        private readonly Func<IRconClient> _rconFactory;
        private readonly IServerProcessController _process;
        private readonly string _adminPassword;

        public ServerControlService(Func<IRconClient> rconFactory, IServerProcessController process, string adminPassword)
        {
            _rconFactory = rconFactory;
            _process = process;
            _adminPassword = adminPassword;
        }

        public async Task<StopResult> GracefulStopAsync(TimeSpan timeout, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (!_process.IsRunning) return StopResult.AlreadyStopped;

            bool rconOk = false;
            try
            {
                using var rcon = _rconFactory();
                rconOk = await rcon.ConnectAndAuthenticateAsync(_adminPassword, ct);
                if (rconOk)
                {
                    progress?.Report("Saving world...");
                    await rcon.ExecuteAsync("SaveWorld", ct);
                    progress?.Report("Shutting down...");
                    await rcon.ExecuteAsync("DoExit", ct);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"RCON failed: {ex.Message}");
                rconOk = false;
            }

            if (rconOk && await WaitForStopAsync(timeout, ct))
                return StopResult.GracefulStop;

            progress?.Report(rconOk ? "Graceful stop timed out; forcing stop." : "RCON unavailable; forcing stop.");
            _process.ForceStop();
            return _process.IsRunning ? StopResult.Failed : StopResult.ForcedStop;
        }

        public async Task<bool> RestartToApplyAsync(TimeSpan stopTimeout, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var stop = await GracefulStopAsync(stopTimeout, progress, ct);
            if (stop == StopResult.Failed) return false;
            progress?.Report("Starting server (mods re-download on boot)...");
            return _process.Start();
        }

        private async Task<bool> WaitForStopAsync(TimeSpan timeout, CancellationToken ct)
        {
            var deadline = timeout;
            var step = TimeSpan.FromMilliseconds(100);
            var waited = TimeSpan.Zero;
            while (waited < deadline)
            {
                if (!_process.IsRunning) return true;
                await Task.Delay(step, ct);
                waited += step;
            }
            return !_process.IsRunning;
        }
    }
}
```
Create `ASA-Manager/Services/Servers/GameProcessControllerAdapter.cs` (real wrapper around the existing manager; not unit-tested):
```csharp
namespace ARKServerCreationTool.Services.Servers
{
    /// <summary>Adapts the existing GameProcessManager to IServerProcessController.</summary>
    public class GameProcessControllerAdapter : IServerProcessController
    {
        private readonly GameProcessManager _manager;
        public GameProcessControllerAdapter(GameProcessManager manager) { _manager = manager; }
        public bool IsRunning => _manager.IsRunning;
        public bool Start() => _manager.Start();
        // GameProcessManager.Stop() returns IsRunning (true if still running); invert to "stopped".
        public bool ForceStop() => !_manager.Stop();
    }
}
```
Add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ASA-Manager.Tests")]` to `ASA-Manager/AssemblyInfo.cs` **only if** the build reports `GameProcessManager` (internal) is inaccessible from the adapter — the adapter is in the same assembly as `GameProcessManager`, so no change is normally needed. (The adapter lives in the app assembly, not the test assembly.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ServerControlServiceTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Build the app**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add "ASA-Manager/Services/Servers" "ASA-Manager.Tests/ServerControlServiceTests.cs"
git commit -m "feat(servers): graceful stop/restart orchestration over RCON with force-stop fallback"
```

---

## Task 7: Mod editor UI — ordered list, reorder, add, remove-with-wipe

**Files:**
- Create: `ASA-Manager/Services/Common/ListReorder.cs`
- Create: `ASA-Manager.Tests/ListReorderTests.cs`
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml` (replace the mod ListBox region)
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml.cs` (bind `ObservableCollection<ModEntry>`, reorder, add, remove-with-wipe)

**Interfaces:**
- Consumes: `ModEntry`, `ASCTServerConfig.Mods` (Task 1).
- Produces: `ListReorder.MoveUp/MoveDown/Move`.

- [ ] **Step 1: Write the failing reorder tests**

Create `ASA-Manager.Tests/ListReorderTests.cs`:
```csharp
using System.Collections.Generic;
using ARKServerCreationTool.Services.Common;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ListReorderTests
    {
        [Fact]
        public void MoveUp_swaps_with_previous()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveUp(list, 2);
            Assert.Equal(new[] { 1, 3, 2 }, list);
        }

        [Fact]
        public void MoveUp_at_top_is_noop()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveUp(list, 0);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void MoveDown_swaps_with_next()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveDown(list, 0);
            Assert.Equal(new[] { 2, 1, 3 }, list);
        }

        [Fact]
        public void MoveDown_at_bottom_is_noop()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveDown(list, 2);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void Move_relocates_item_preserving_others()
        {
            var list = new List<int> { 1, 2, 3, 4 };
            ListReorder.Move(list, 0, 2);
            Assert.Equal(new[] { 2, 3, 1, 4 }, list);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ListReorderTests`
Expected: FAIL — `ListReorder` not defined.

- [ ] **Step 3: Implement `ListReorder`**

Create `ASA-Manager/Services/Common/ListReorder.cs`:
```csharp
using System.Collections.Generic;

namespace ARKServerCreationTool.Services.Common
{
    public static class ListReorder
    {
        public static void MoveUp<T>(IList<T> list, int index)
        {
            if (index <= 0 || index >= list.Count) return;
            (list[index - 1], list[index]) = (list[index], list[index - 1]);
        }

        public static void MoveDown<T>(IList<T> list, int index)
        {
            if (index < 0 || index >= list.Count - 1) return;
            (list[index + 1], list[index]) = (list[index], list[index + 1]);
        }

        public static void Move<T>(IList<T> list, int from, int to)
        {
            if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return;
            T item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ListReorderTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Replace the mod ListBox region in the XAML**

In `ASA-Manager/ServerConfigurationWindow.xaml`, replace the `<ListBox x:Name="lst_modIds" .../>` element and its neighboring add/remove controls with an itemized list that shows an enabled checkbox + label, plus Up/Down/Remove buttons. Replace the existing mod controls (the `lst_modIds` ListBox, `txt_addMod`, `btn_addMod`, `btn_removeMod`) with:
```xml
<ListBox x:Name="lst_mods" Grid.Column="1" Margin="10,359,10,129"
         SelectionMode="Extended"
         AllowDrop="True"
         PreviewMouseLeftButtonDown="lst_mods_PreviewMouseLeftButtonDown"
         MouseMove="lst_mods_MouseMove"
         Drop="lst_mods_Drop"
         SelectionChanged="lst_mods_SelectionChanged">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <CheckBox IsChecked="{Binding Enabled}" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBlock Text="{Binding ProjectId}" VerticalAlignment="Center"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
<TextBox x:Name="txt_addMod" Grid.Column="1" Margin="10,0,199,98" TextWrapping="Wrap" VerticalAlignment="Bottom" Height="26" VerticalContentAlignment="Center" FontSize="12"/>
<Button x:Name="btn_addMod" Grid.Column="1" Content="Add Mod" Margin="0,0,123,98" VerticalAlignment="Bottom" HorizontalAlignment="Right" Width="71" Height="26" Click="btn_addMod_Click"/>
<Button x:Name="btn_modUp" Grid.Column="1" Content="▲" ToolTip="Move up" Margin="0,0,95,98" VerticalAlignment="Bottom" HorizontalAlignment="Right" Width="24" Height="26" Click="btn_modUp_Click"/>
<Button x:Name="btn_modDown" Grid.Column="1" Content="▼" ToolTip="Move down" Margin="0,0,67,98" VerticalAlignment="Bottom" HorizontalAlignment="Right" Width="24" Height="26" Click="btn_modDown_Click"/>
<Button x:Name="btn_removeMod" IsEnabled="False" ToolTip="Remove selected mod" Grid.Column="1" Content="🗑️" Margin="0,0,10,98" VerticalAlignment="Bottom" HorizontalAlignment="Right" Width="52" Height="26" Click="btn_removeMod_Click"/>
```
(Keep the existing `Mods` label. Exact margins can be adjusted while running; the control names and events are what matter.)

- [ ] **Step 6: Rewrite the mod editor code-behind**

In `ASA-Manager/ServerConfigurationWindow.xaml.cs`:
- Add usings: `using System.Collections.ObjectModel;`, `using ARKServerCreationTool.Models;`, `using ARKServerCreationTool.Services.Common;`.
- Add a field and replace `UpdateModList`/add/remove/selection handlers. Remove the now-obsolete `lst_modIds_SelectionChanged` handler:
```csharp
        private ObservableCollection<ModEntry> modItems = new();

        private void UpdateModList()
        {
            modItems = new ObservableCollection<ModEntry>(targetServer.Mods.Select(m => new ModEntry(m.ProjectId, m.Enabled)));
            lst_mods.ItemsSource = modItems;
        }

        private void btn_addMod_Click(object sender, RoutedEventArgs e)
        {
            if (ulong.TryParse(txt_addMod.Text.Trim(), out ulong modID))
            {
                if (!modItems.Any(m => m.ProjectId == modID)) modItems.Add(new ModEntry(modID));
                txt_addMod.Clear();
                UpdateCommandLineBox();
            }
            else MessageBox.Show("Entered value is invalid");
        }

        private void lst_mods_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => btn_removeMod.IsEnabled = lst_mods.SelectedItems.Count > 0;

        private void btn_modUp_Click(object sender, RoutedEventArgs e)
        {
            int i = lst_mods.SelectedIndex;
            ListReorder.MoveUp(modItems, i);
            if (i > 0) lst_mods.SelectedIndex = i - 1;
            UpdateCommandLineBox();
        }

        private void btn_modDown_Click(object sender, RoutedEventArgs e)
        {
            int i = lst_mods.SelectedIndex;
            ListReorder.MoveDown(modItems, i);
            if (i >= 0 && i < modItems.Count - 1) lst_mods.SelectedIndex = i + 1;
            UpdateCommandLineBox();
        }

        private void btn_removeMod_Click(object sender, RoutedEventArgs e)
        {
            var selected = lst_mods.SelectedItems.Cast<ModEntry>().ToList();
            if (selected.Count == 0) return;
            var wipe = MessageBox.Show(
                "Also delete the cached mod files under ShooterGame\\...\\Mods\\83374\\<id> for the removed mods?\n(Fixes stale-cache crashes; files re-download on next start.)",
                "Remove mods", MessageBoxButton.YesNoCancel);
            if (wipe == MessageBoxResult.Cancel) return;
            foreach (var m in selected)
            {
                modItems.Remove(m);
                if (wipe == MessageBoxResult.Yes) TryWipeModCache(m.ProjectId);
            }
            UpdateCommandLineBox();
        }

        private void TryWipeModCache(ulong projectId)
        {
            try
            {
                string dir = Path.Combine(targetServer.GameDirectory,
                    @"ShooterGame\Binaries\Win64\ShooterGame\Mods\83374", projectId.ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) { MessageBox.Show($"Could not delete mod cache for {projectId}: {ex.Message}"); }
        }
```
- In `UpdateServerObject`, replace the line that built `serv.Mods` (from Task 1) with the ordered collection:
```csharp
            serv.Mods = modItems.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
```
- Add drag-and-drop handlers:
```csharp
        private System.Windows.Point dragStart;

        private void lst_mods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => dragStart = e.GetPosition(null);

        private void lst_mods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var diff = dragStart - e.GetPosition(null);
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (lst_mods.SelectedItem is ModEntry item)
                DragDrop.DoDragDrop(lst_mods, item, DragDropEffects.Move);
        }

        private void lst_mods_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ModEntry)) is not ModEntry dragged) return;
            int from = modItems.IndexOf(dragged);
            int to = GetDropIndex(e);
            ListReorder.Move(modItems, from, to);
            UpdateCommandLineBox();
        }

        private int GetDropIndex(DragEventArgs e)
        {
            for (int i = 0; i < lst_mods.Items.Count; i++)
            {
                if (lst_mods.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem lbi)
                {
                    var bounds = new System.Windows.Rect(lbi.TranslatePoint(new System.Windows.Point(0, 0), lst_mods), lbi.RenderSize);
                    if (e.GetPosition(lst_mods).Y < bounds.Top + bounds.Height / 2) return i;
                }
            }
            return modItems.Count - 1;
        }
```
- Delete the old `lst_modIds`-based `UpdateModList` body from Task 1 (now replaced) and remove the old `lst_modIds_SelectionChanged` method.

- [ ] **Step 7: Build and run the app to verify the editor**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`
Then launch the built exe (or run from Visual Studio), open a server's configuration, and verify: mods show with an Enabled checkbox; Add adds an id; selecting enables Remove; ▲/▼ reorder; drag-drop reorders; toggling Enabled and reordering both update the launch-args preview; Save persists order. (Use `/verify` to drive this end-to-end.)
Expected: reordering changes the `-mods=` order in the preview; disabled mods drop out of the preview.

- [ ] **Step 8: Commit**

```bash
git add "ASA-Manager/Services/Common/ListReorder.cs" "ASA-Manager.Tests/ListReorderTests.cs" "ASA-Manager/ServerConfigurationWindow.xaml" "ASA-Manager/ServerConfigurationWindow.xaml.cs"
git commit -m "feat(mods): reorderable mod editor with enable toggle and remove-with-cache-wipe"
```

---

## Task 8: Mod editor — bulk add (ID/URL) + copy/cut/paste

**Files:**
- Create: `ASA-Manager/Services/Mods/ModIdParser.cs`
- Create: `ASA-Manager.Tests/ModIdParserTests.cs`
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml` (Bulk-add + Copy/Paste buttons)
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml.cs` (handlers + a static in-process clipboard)

**Interfaces:**
- Produces: `ModIdParser.ParseMany(string) : IReadOnlyList<ulong>`, `ModIdParser.TryParseOne(string, out ulong) : bool`.

- [ ] **Step 1: Write the failing parser tests**

Create `ASA-Manager.Tests/ModIdParserTests.cs`:
```csharp
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
```
(Reflects the verified caveat: slug-only ASA URLs carry no numeric id; only URLs containing a numeric segment yield an id.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModIdParserTests`
Expected: FAIL — `ModIdParser` not defined.

- [ ] **Step 3: Implement `ModIdParser`**

Create `ASA-Manager/Services/Mods/ModIdParser.cs`:
```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ARKServerCreationTool.Services.Mods
{
    public static class ModIdParser
    {
        private static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

        /// <summary>Extracts distinct mod ids (in first-seen order) from free text: bare ids or URLs containing a numeric id.</summary>
        public static IReadOnlyList<ulong> ParseMany(string text)
        {
            var result = new List<ulong>();
            var seen = new HashSet<ulong>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var token in text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseOne(token, out ulong id) && seen.Add(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>True if the token is a bare numeric id or a URL whose LAST numeric segment is the id.</summary>
        public static bool TryParseOne(string token, out ulong id)
        {
            id = 0;
            token = token.Trim();
            if (ulong.TryParse(token, out id)) return true;

            // URL/other: only accept if it contains a numeric segment (slug-only ASA URLs will not).
            var matches = Number.Matches(token);
            if (matches.Count == 0) return false;
            return ulong.TryParse(matches[matches.Count - 1].Value, out id);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModIdParserTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Add Bulk-add + Copy/Paste buttons to the XAML**

In `ServerConfigurationWindow.xaml`, add three buttons near the mod controls (place them in a free area, e.g. below the Mods label; adjust margins while running):
```xml
<Button x:Name="btn_bulkAddMods" Grid.Column="1" Content="Bulk add…" ToolTip="Paste many mod ids / URLs" Margin="10,0,0,98" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="72" Height="26" Click="btn_bulkAddMods_Click"/>
<Button x:Name="btn_copyMods" Grid.Column="1" Content="Copy" ToolTip="Copy selected (or all) mods" Margin="86,0,0,98" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="52" Height="26" Click="btn_copyMods_Click"/>
<Button x:Name="btn_pasteMods" Grid.Column="1" Content="Paste" ToolTip="Paste copied mods" Margin="142,0,0,98" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="52" Height="26" Click="btn_pasteMods_Click"/>
```

- [ ] **Step 6: Implement the handlers**

Add to `ServerConfigurationWindow.xaml.cs` (add `using ARKServerCreationTool.Services.Mods;`):
```csharp
        // In-process copy buffer for mods (shared across open config windows).
        private static List<ModEntry> modCopyBuffer = new();

        private void btn_bulkAddMods_Click(object sender, RoutedEventArgs e)
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Paste mod ids or CurseForge URLs (comma/space/newline separated):", "Bulk add mods", "");
            if (string.IsNullOrWhiteSpace(input)) return;
            int added = 0;
            foreach (var id in ModIdParser.ParseMany(input))
            {
                if (!modItems.Any(m => m.ProjectId == id)) { modItems.Add(new ModEntry(id)); added++; }
            }
            UpdateCommandLineBox();
            MessageBox.Show($"Added {added} mod(s).");
        }

        private void btn_copyMods_Click(object sender, RoutedEventArgs e)
        {
            var source = lst_mods.SelectedItems.Count > 0 ? lst_mods.SelectedItems.Cast<ModEntry>() : modItems;
            modCopyBuffer = source.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
            MessageBox.Show($"Copied {modCopyBuffer.Count} mod(s).");
        }

        private void btn_pasteMods_Click(object sender, RoutedEventArgs e)
        {
            int added = 0;
            foreach (var m in modCopyBuffer)
            {
                if (!modItems.Any(x => x.ProjectId == m.ProjectId)) { modItems.Add(new ModEntry(m.ProjectId, m.Enabled)); added++; }
            }
            UpdateCommandLineBox();
            MessageBox.Show($"Pasted {added} new mod(s).");
        }
```
Add the reference `Microsoft.VisualBasic` (it ships with .NET; add `<Reference>` only if the build complains — normally `Microsoft.VisualBasic.Interaction` is available on `net9.0-windows`). If unavailable, replace the InputBox with a tiny modal `TextBox` dialog window instead.

- [ ] **Step 7: Build and run to verify bulk-add + copy/paste**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`, then launch, open a server config, bulk-add a few ids, Copy, open another server's config, Paste, and confirm the list updates and the preview reflects it. Use `/verify`.
Expected: pasted ids appear; duplicates skipped.

- [ ] **Step 8: Commit**

```bash
git add "ASA-Manager/Services/Mods/ModIdParser.cs" "ASA-Manager.Tests/ModIdParserTests.cs" "ASA-Manager/ServerConfigurationWindow.xaml" "ASA-Manager/ServerConfigurationWindow.xaml.cs"
git commit -m "feat(mods): bulk-add by id/URL and copy/paste between servers"
```

---

## Task 9: Copy mods to other servers / cluster

**Files:**
- Create: `ASA-Manager/Services/Mods/ModListOps.cs`
- Create: `ASA-Manager.Tests/ModListOpsTests.cs`
- Create: `ASA-Manager/CopyModsToServersWindow.xaml`, `ASA-Manager/CopyModsToServersWindow.xaml.cs`
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml` + `.cs` (a "Copy mods to…" button that opens the dialog)

**Interfaces:**
- Consumes: `ModEntry`, `ASCTServerConfig` (`Mods`, `ClusterKey`, `Name`, `ID`, `IsRunning`), `ASCTGlobalConfig.Instance.Servers`.
- Produces: `ModListOps.Replace(source, includeDisabled) : List<ModEntry>`, `ModListOps.Merge(target, source, includeDisabled) : List<ModEntry>`.

- [ ] **Step 1: Write the failing list-ops tests**

Create `ASA-Manager.Tests/ModListOpsTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModListOpsTests`
Expected: FAIL — `ModListOps` not defined.

- [ ] **Step 3: Implement `ModListOps`**

Create `ASA-Manager/Services/Mods/ModListOps.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool.Services.Mods
{
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ASA-Manager.Tests/ASA-Manager.Tests.csproj" --filter ModListOpsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Create the copy-to-servers dialog XAML**

Create `ASA-Manager/CopyModsToServersWindow.xaml`:
```xml
<Window x:Class="ARKServerCreationTool.CopyModsToServersWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Copy mods to servers" Height="440" Width="420">
    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,6">
            <TextBlock Text="Mode:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <RadioButton x:Name="rb_replace" Content="Replace" IsChecked="True" Margin="0,0,10,0"/>
            <RadioButton x:Name="rb_merge" Content="Merge (append)" Margin="0,0,10,0"/>
            <CheckBox x:Name="chk_includeDisabled" Content="Include disabled" IsChecked="True"/>
        </StackPanel>
        <ListBox x:Name="lst_targets" Grid.Row="1" SelectionMode="Multiple">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <CheckBox Content="{Binding Display}" IsChecked="{Binding Selected}"/>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <StackPanel Orientation="Horizontal" Grid.Row="2" Margin="0,6,0,0">
            <Button x:Name="btn_selectCluster" Content="Select cluster members" Click="btn_selectCluster_Click" Margin="0,0,6,0"/>
            <Button x:Name="btn_selectAll" Content="Select all" Click="btn_selectAll_Click"/>
        </StackPanel>
        <StackPanel Orientation="Horizontal" Grid.Row="3" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button x:Name="btn_apply" Content="Apply" Width="80" Click="btn_apply_Click" Margin="0,0,6,0"/>
            <Button x:Name="btn_cancel" Content="Cancel" Width="80" Click="btn_cancel_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 6: Implement the dialog code-behind**

Create `ASA-Manager/CopyModsToServersWindow.xaml.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Mods;

namespace ARKServerCreationTool
{
    public partial class CopyModsToServersWindow : Window
    {
        public class TargetRow : INotifyPropertyChanged
        {
            public int ServerId { get; init; }
            public string ClusterKey { get; init; } = string.Empty;
            public string Display { get; init; } = string.Empty;
            private bool _selected;
            public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly int sourceServerId;
        private readonly List<ModEntry> sourceMods;
        private readonly ObservableCollection<TargetRow> rows = new();

        public CopyModsToServersWindow(int sourceServerId, IEnumerable<ModEntry> sourceMods)
        {
            InitializeComponent();
            this.sourceServerId = sourceServerId;
            this.sourceMods = sourceMods.ToList();

            foreach (var s in ASCTGlobalConfig.Instance.Servers.Where(s => s.ID != sourceServerId))
            {
                string cluster = string.IsNullOrEmpty(s.ClusterKey) ? "no cluster" : s.ClusterKey;
                rows.Add(new TargetRow { ServerId = s.ID, ClusterKey = s.ClusterKey, Display = $"{s.Name}  ({cluster}){(s.IsRunning ? "  [running]" : "")}" });
            }
            lst_targets.ItemsSource = rows;
        }

        private void btn_selectCluster_Click(object sender, RoutedEventArgs e)
        {
            string srcCluster = ASCTGlobalConfig.Instance.Servers.First(s => s.ID == sourceServerId).ClusterKey;
            foreach (var r in rows) r.Selected = !string.IsNullOrEmpty(srcCluster) && r.ClusterKey == srcCluster;
        }

        private void btn_selectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in rows) r.Selected = true;
        }

        private void btn_apply_Click(object sender, RoutedEventArgs e)
        {
            var targets = rows.Where(r => r.Selected).ToList();
            if (targets.Count == 0) { MessageBox.Show("No target servers selected."); return; }
            bool includeDisabled = chk_includeDisabled.IsChecked == true;
            bool replace = rb_replace.IsChecked == true;
            bool anyRunning = false;

            foreach (var t in targets)
            {
                var server = ASCTGlobalConfig.Instance.Servers.First(s => s.ID == t.ServerId);
                server.Mods = replace
                    ? ModListOps.Replace(sourceMods, includeDisabled)
                    : ModListOps.Merge(server.Mods, sourceMods, includeDisabled);
                anyRunning |= server.IsRunning;
            }
            ASCTGlobalConfig.Instance.Save();
            MessageBox.Show(anyRunning
                ? "Mods copied. Running targets will apply the change on their next restart."
                : "Mods copied.");
            Close();
        }

        private void btn_cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
```

- [ ] **Step 7: Add the "Copy mods to…" button to the config window**

In `ServerConfigurationWindow.xaml`, add a button near the mod controls:
```xml
<Button x:Name="btn_copyToServers" Grid.Column="1" Content="Copy mods to…" Margin="200,0,0,98" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="100" Height="26" Click="btn_copyToServers_Click"/>
```
In `ServerConfigurationWindow.xaml.cs`:
```csharp
        private void btn_copyToServers_Click(object sender, RoutedEventArgs e)
        {
            if (newServer) { MessageBox.Show("Save this server first, then copy its mods to others."); return; }
            var mods = modItems.Select(m => new ModEntry(m.ProjectId, m.Enabled));
            new CopyModsToServersWindow(targetServer.ID, mods) { Owner = this }.ShowDialog();
        }
```

- [ ] **Step 8: Build and run to verify copy-to-servers**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`, then launch, open a server with a few mods, click "Copy mods to…", select targets (or "Select cluster members"), Apply, then open a target and confirm its mod list matches. Use `/verify`.
Expected: replace overwrites; merge appends without dupes; running targets show the "apply on restart" note.

- [ ] **Step 9: Commit**

```bash
git add "ASA-Manager/Services/Mods/ModListOps.cs" "ASA-Manager.Tests/ModListOpsTests.cs" "ASA-Manager/CopyModsToServersWindow.xaml" "ASA-Manager/CopyModsToServersWindow.xaml.cs" "ASA-Manager/ServerConfigurationWindow.xaml" "ASA-Manager/ServerConfigurationWindow.xaml.cs"
git commit -m "feat(mods): copy mods to other servers / whole cluster"
```

---

## Task 10: CurseForge wiring — names/thumbnails, refresh, check-for-updates, API key setting

**Files:**
- Modify: `ASA-Manager/ASCTGlobalConfig.cs` (`ASCTGlobalConfig`: add `CurseForgeApiKey`; `ASCTServerConfig`: add `RunningModVersions` snapshot dict)
- Modify: `ASA-Manager/ASCTConfigWindow.xaml` + `.cs` (add an API-key textbox)
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml` + `.cs` (show names/thumbnails, "Refresh names", "Check for updates")
- Modify: `ASA-Manager/ServerWindow.xaml.cs` (snapshot running versions after a successful start — done in Task 11's start path; here add the snapshot method)

**Interfaces:**
- Consumes: `CurseForgeClient`, `ModMetadataCache`, `ModUpdateChecker`, `ModMetadata` (Tasks 4–5).
- Produces: `ASCTGlobalConfig.CurseForgeApiKey : string`; `ASCTServerConfig.RunningModVersions : Dictionary<ulong,long>`; a shared `ModMetadataCache` accessor `App`/static; `ServerConfigurationWindow` display of names + update flags.

- [ ] **Step 1: Add settings + snapshot fields**

In `ASCTGlobalConfig`, add:
```csharp
        public string CurseForgeApiKey { get; set; } = string.Empty;
```
In `ASCTServerConfig`, add (records the newest file id per mod that the server last booted with):
```csharp
        public Dictionary<ulong, long> RunningModVersions { get; set; } = new();
```

- [ ] **Step 2: Add a shared metadata cache + CurseForge client accessor**

Create a small static accessor so windows share one cache + `HttpClient`. Add to `ASCTGlobalConfig.cs` (or a new `AppServices.cs`):
```csharp
namespace ARKServerCreationTool
{
    public static class AppServices
    {
        private static readonly System.Net.Http.HttpClient http = new();
        public static Services.CurseForge.ModMetadataCache MetadataCache { get; } = Services.CurseForge.ModMetadataCache.Load();
        public static Services.CurseForge.CurseForgeClient CurseForge()
            => new Services.CurseForge.CurseForgeClient(http, ASCTGlobalConfig.Instance.CurseForgeApiKey);
    }
}
```

- [ ] **Step 3: Add the API key field to the global config window**

In `ASCTConfigWindow.xaml`, add a labelled textbox `txt_curseforgeKey` (bind on load/save alongside the other global settings). In `ASCTConfigWindow.xaml.cs`, load `ASCTGlobalConfig.Instance.CurseForgeApiKey` into it on open and write it back on save (follow the window's existing load/save pattern for other fields).

- [ ] **Step 4: Show names/thumbnails in the mod editor**

In `ServerConfigurationWindow.xaml`, extend the `lst_mods` `DataTemplate` to bind a display name and (optional) thumbnail. Change the `TextBlock` to bind to a display string resolved from the cache. The simplest approach that avoids per-row async: resolve names into a lookup on load and expose a `DisplayName` via a small view wrapper. To keep Task 7's `ModEntry` binding, add a converter-free approach: in code-behind, after `UpdateModList()`, kick off a background refresh and then set each row's tooltip/label.

Add to `ServerConfigurationWindow.xaml.cs`:
```csharp
        private async void RefreshModNames(bool forceApi)
        {
            var ids = modItems.Select(m => m.ProjectId).ToList();
            if (ids.Count == 0) return;
            var cache = AppServices.MetadataCache;
            var client = AppServices.CurseForge();
            var now = System.DateTimeOffset.UtcNow;
            var stale = ids.Where(id => forceApi || cache.IsStale(id, System.TimeSpan.FromHours(24), now)).ToList();
            if (stale.Count > 0 && client.HasKey)
            {
                try { await cache.RefreshAsync(stale, client, now); cache.Save(); }
                catch (System.Exception ex) { lbl_modStatus.Content = $"CurseForge: {ex.Message}"; }
            }
            // Re-render list so the DataTemplate picks up resolved names via NameFor().
            lst_mods.Items.Refresh();
            lbl_modStatus.Content = client.HasKey ? "" : "Add a CurseForge API key (settings) to show mod names.";
        }

        public string NameFor(ulong projectId)
            => AppServices.MetadataCache.TryGet(projectId, out var m) && !string.IsNullOrEmpty(m.Name) ? m.Name! : $"#{projectId}";
```
Bind the row text to `NameFor` via a `RelativeSource` to the window, using a `MultiBinding`/converter, OR (simpler) keep the `ProjectId` TextBlock and add a second `TextBlock` whose `Text` is set in an item-loaded handler. Recommended simplest path: give `ModEntry` a non-persisted `[JsonIgnore] public string? DisplayName { get; set; }` and set it during `RefreshModNames` from the cache, binding the template `TextBlock` to `DisplayName` with a fallback to `ProjectId`. Add to `ModEntry`:
```csharp
        [Newtonsoft.Json.JsonIgnore] private string? _displayName;
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? $"#{ProjectId}" : _displayName!;
            set { _displayName = value; OnPropertyChanged(); }
        }
```
and in `RefreshModNames`, after cache refresh:
```csharp
            foreach (var m in modItems)
                if (AppServices.MetadataCache.TryGet(m.ProjectId, out var meta) && !string.IsNullOrEmpty(meta.Name))
                    m.DisplayName = meta.Name!;
```
Update the template `TextBlock` binding to `{Binding DisplayName}` and add a small `lbl_modStatus` label under the list. Call `RefreshModNames(false)` at the end of `UpdateModList()`.

- [ ] **Step 5: Add "Refresh names" and "Check for updates" buttons**

XAML (near the mod controls):
```xml
<Button x:Name="btn_refreshNames" Grid.Column="1" Content="Refresh names" Margin="10,0,0,68" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="90" Height="24" Click="btn_refreshNames_Click"/>
<Button x:Name="btn_checkUpdates" Grid.Column="1" Content="Check for updates" Margin="104,0,0,68" VerticalAlignment="Bottom" HorizontalAlignment="Left" Width="120" Height="24" Click="btn_checkUpdates_Click"/>
<Label x:Name="lbl_modStatus" Grid.Column="1" Margin="10,0,10,44" VerticalAlignment="Bottom" Height="22" FontSize="10"/>
```
Code-behind:
```csharp
        private void btn_refreshNames_Click(object sender, RoutedEventArgs e) => RefreshModNames(forceApi: true);

        private async void btn_checkUpdates_Click(object sender, RoutedEventArgs e)
        {
            var client = AppServices.CurseForge();
            if (!client.HasKey) { MessageBox.Show("Set a CurseForge API key in settings to check for updates."); return; }
            try
            {
                var mods = await client.GetModsAsync(modItems.Select(m => m.ProjectId));
                var snapshot = targetServer.RunningModVersions;
                var outdated = mods.Where(mod => ARKServerCreationTool.Services.CurseForge.ModUpdateChecker
                                    .HasNewerFile(mod, snapshot.TryGetValue((ulong)mod.Id, out var v) ? v : (long?)null))
                                   .Select(mod => mod.Name).ToList();
                MessageBox.Show(outdated.Count == 0
                    ? "All mods are up to date (relative to this server's last start)."
                    : "Updates available for:\n - " + string.Join("\n - ", outdated) +
                      "\n\nRestart the server (Server window → Restart to apply) to pull them.");
            }
            catch (System.Exception ex) { MessageBox.Show($"Update check failed: {ex.Message}"); }
        }
```

- [ ] **Step 6: Add the running-version snapshot helper (used by Task 11's start path)**

Add to `ASCTServerConfig`:
```csharp
        /// <summary>Records the newest known file id per mod as the "running" version (call after a successful start).</summary>
        public void SnapshotRunningModVersions(Services.CurseForge.ModMetadataCache cache)
        {
            RunningModVersions = new Dictionary<ulong, long>();
            foreach (var m in Mods)
                if (cache.TryGet(m.ProjectId, out var meta) && meta.LatestFileId.HasValue)
                    RunningModVersions[m.ProjectId] = meta.LatestFileId.Value;
        }
```

- [ ] **Step 7: Build and run to verify names + update check**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`. With a valid CurseForge key in settings, open a server config with real mod ids and confirm names appear; without a key, confirm it degrades to `#id` and shows the hint. Click "Check for updates" and confirm the dialog. Use `/verify`.
Expected: names resolve with a key; graceful `#id` fallback without one; no crash on network error.

- [ ] **Step 8: Commit**

```bash
git add "ASA-Manager/ASCTGlobalConfig.cs" "ASA-Manager/Models/ModEntry.cs" "ASA-Manager/ASCTConfigWindow.xaml" "ASA-Manager/ASCTConfigWindow.xaml.cs" "ASA-Manager/ServerConfigurationWindow.xaml" "ASA-Manager/ServerConfigurationWindow.xaml.cs"
git commit -m "feat(curseforge): resolve mod names/thumbnails and on-demand update check"
```

---

## Task 11: Graceful stop / restart wiring in `ServerWindow`

**Files:**
- Modify: `ASA-Manager/ServerWindow.xaml` (add "Graceful Stop" and "Restart to apply" buttons)
- Modify: `ASA-Manager/ServerWindow.xaml.cs` (wire `ServerControlService`; snapshot mod versions on start)

**Interfaces:**
- Consumes: `ServerControlService`, `GameProcessControllerAdapter`, `RconClient`, `IRconClient` (Tasks 3, 6); `ASCTServerConfig` RCON fields (Task 2); `SnapshotRunningModVersions`, `AppServices` (Task 10).

- [ ] **Step 1: Add buttons to `ServerWindow.xaml`**

Add near the existing `btn_start` / `btn_stop`:
```xml
<Button x:Name="btn_gracefulStop" Content="Graceful Stop" Click="btn_gracefulStop_Click" Margin="4"/>
<Button x:Name="btn_restartApply" Content="Restart (apply mods)" Click="btn_restartApply_Click" Margin="4"/>
<TextBlock x:Name="lbl_controlStatus" Margin="4" TextWrapping="Wrap"/>
```
(Place them in the window's existing layout panel next to the current buttons.)

- [ ] **Step 2: Build a `ServerControlService` for the current server**

Add to `ServerWindow.xaml.cs` (usings: `using ARKServerCreationTool.Services.Rcon;`, `using ARKServerCreationTool.Services.Servers;`, `using System.Threading.Tasks;`):
```csharp
        private ServerControlService BuildControlService()
        {
            var adapter = new GameProcessControllerAdapter(processManager);
            string host = "127.0.0.1";
            int port = targetServer.RconPort;
            string pw = targetServer.ServerAdminPassword;
            return new ServerControlService(() => new RconClient(host, port), adapter, pw);
        }

        private readonly System.Progress<string> controlProgress;
```
Initialize `controlProgress` in the constructor (after `InitializeComponent()`):
```csharp
            controlProgress = new System.Progress<string>(msg => lbl_controlStatus.Text = msg);
```

- [ ] **Step 3: Wire graceful stop**

```csharp
        private async void btn_gracefulStop_Click(object sender, RoutedEventArgs e)
        {
            btn_gracefulStop.IsEnabled = false;
            try
            {
                var result = await BuildControlService().GracefulStopAsync(System.TimeSpan.FromSeconds(60), controlProgress);
                lbl_controlStatus.Text = $"Stop result: {result}";
            }
            finally { btn_gracefulStop.IsEnabled = true; UpdateStatus(); }
        }
```

- [ ] **Step 4: Wire restart-to-apply and snapshot on start**

```csharp
        private async void btn_restartApply_Click(object sender, RoutedEventArgs e)
        {
            btn_restartApply.IsEnabled = false;
            try
            {
                // Refresh metadata so the post-start snapshot records the newest file ids.
                var client = AppServices.CurseForge();
                if (client.HasKey)
                {
                    try { await AppServices.MetadataCache.RefreshAsync(targetServer.Mods.Select(m => m.ProjectId), client, System.DateTimeOffset.UtcNow); AppServices.MetadataCache.Save(); }
                    catch (System.Exception ex) { lbl_controlStatus.Text = $"Metadata refresh failed: {ex.Message}"; }
                }
                bool ok = await BuildControlService().RestartToApplyAsync(System.TimeSpan.FromSeconds(60), controlProgress);
                if (ok)
                {
                    targetServer.SnapshotRunningModVersions(AppServices.MetadataCache);
                    config.Save();
                }
                lbl_controlStatus.Text = ok ? "Restarted; mods re-download on boot." : "Restart failed; check the server.";
            }
            finally { btn_restartApply.IsEnabled = true; UpdateStatus(); }
        }
```
Also snapshot on a normal start: in `btn_start_Click`, after `targetServer.ProcessManager.Start();` (non-cluster branch), add:
```csharp
                targetServer.SnapshotRunningModVersions(AppServices.MetadataCache);
                config.Save();
```

- [ ] **Step 5: Build and run to verify graceful stop + restart**

Run: `dotnet build "ASA-Manager/ARK Server Creation Tool.csproj"`. Start a real server, click "Graceful Stop", and confirm the status shows Saving/Shutting down and the process stops cleanly (world saved). Click "Restart (apply mods)" and confirm it stops gracefully then relaunches. If RCON does not connect (verify the `?`-option RCON settings took effect — see spec §12), confirm it falls back to force-stop with a clear message. Use `/verify`.
Expected: graceful path saves then exits; RCON-down path force-stops with a message; restart relaunches.

- [ ] **Step 6: Commit**

```bash
git add "ASA-Manager/ServerWindow.xaml" "ASA-Manager/ServerWindow.xaml.cs"
git commit -m "feat(servers): graceful stop and restart-to-apply buttons in the server window"
```

---

## Self-review

**Spec coverage** (spec §2 in-scope items → tasks):
- Ordered/reorderable/copy-able mod list → Tasks 1 (model), 7 (reorder/editor), 8 (copy/paste/bulk). ✓
- Copy mods to other servers/cluster → Task 9. ✓
- CurseForge names + on-demand update check → Tasks 4, 5, 10. ✓
- Minimal RCON + graceful stop/restart → Tasks 2 (launch options), 3 (client), 6 (orchestration), 11 (UI). ✓
- Unit tests for pure logic (arg gen, migration, RCON codec, update-diff, reorder, merge, parser) → Tasks 1–9. ✓
- Migration HashSet→List → Task 1. ✓
- Graceful degradation without API key → Tasks 4 (`HasKey`), 5 (`RefreshAsync` no-op), 10 (fallback UI). ✓
- Deferred items (watchdog, browse/search, passive/map/TC kinds, backups, config editor, QoL menu, size-cap warnings) → intentionally NOT in any task. ✓

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N" — each step has concrete code or an exact command. RCON `?`-option runtime risk is called out (spec §12) and handled by the force-stop fallback in Task 6/11, not left vague. ✓

**Type consistency:** `ModEntry(ulong, bool)`, `ASCTServerConfig.Mods`, `ModArgs`, `MapQueryOptions`, `RconPort`, `ServerAdminPassword`, `IRconClient.{ConnectAndAuthenticateAsync,ExecuteAsync}`, `RconPacket.{Encode,Read,Type*}`, `CurseForgeClient.{HasKey,GetModsAsync}`, `ModUpdateChecker.{NewestFile,HasNewerFile}`, `ModMetadataCache.{TryGet,IsStale,Upsert,RefreshAsync}`, `IServerProcessController.{IsRunning,Start,ForceStop}`, `ServerControlService.{GracefulStopAsync,RestartToApplyAsync}`, `ListReorder.{MoveUp,MoveDown,Move}`, `ModIdParser.{ParseMany,TryParseOne}`, `ModListOps.{Replace,Merge}` — names are used identically across producing and consuming tasks. ✓
