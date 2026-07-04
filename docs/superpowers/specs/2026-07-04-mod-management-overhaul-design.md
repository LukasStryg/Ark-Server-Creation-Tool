# ARK Server Creation Tool (Fork) — Mod Management Overhaul

**Design spec · Phase 1 · 2026-07-04**

## 1. Context & goals

We are forking [Ragonz/Ark-Server-Creation-Tool](https://github.com/Ragonz/Ark-Server-Creation-Tool)
(C# / .NET 9 / WPF, Windows-only, GPL-3.0) to expand it for a self-hosted ARK: Survival
Ascended (ASA) **cluster**. The reference project
[sanjay-m6/ARK-ASA-SERVER-MANAGER-2.0](https://github.com/sanjay-m6/ARK-ASA-SERVER-MANAGER-2.0)
(TypeScript/Rust/Tauri, MIT) is used **only as feature/UX inspiration** — it shares no code
or language with the fork, so nothing is ported; features are reimplemented from behavior and
from official ASA documentation.

The forked tool already handles the tedious ASA plumbing: game-file download (DepotDownloader,
app id `2430930`), cluster wiring via a shared `ClusterKey` + `-ClusterDirOverride`, automatic
Windows firewall rules, Amazon CA cert install, process launch, staggered auto-start, and a
launch-argument builder.

**Phase 1 goal:** overhaul mod management (the #1 pain), add CurseForge name/update awareness,
and introduce a minimal RCON layer that enables graceful restarts. Later phases (see §12) build
on this.

### The root problem being fixed

Mods are stored in `ASCTServerConfig` as an **unordered `HashSet<ulong>`**
(`ASCTGlobalConfig.cs:155`), and the launch argument is built directly from it
(`ModArgs`: `-mods={string.Join(",", modIDs)}`). Consequences:

- The mod list **cannot be reordered** — a `HashSet` has no order.
- The **actual mod load order the server runs with is undefined**, even though in ASA load
  order decides which mod's overrides win.
- Mods display as **bare numeric IDs**; the editor (`ServerConfigurationWindow`) is a plain
  multi-select `ListBox` with only *Add* / *Remove*.

## 2. Scope

**In scope (Phase 1):**

1. Ordered, reorderable, copy/paste-able mod list (data-model change + new editor).
2. Copy mods to other servers / whole cluster.
3. CurseForge integration: resolve IDs → names/author/thumbnail; on-demand "check for updates".
4. Minimal Source-RCON client + graceful stop / "restart to apply latest mods".
5. Unit tests for the pure logic (arg generation, migration, RCON codec, update-diff).

**Explicitly deferred (later phases, each its own spec):**

- Automatic update **watchdog** (background polling + auto-restart).
- In-app CurseForge **browse/search** panel.
- **Passive / map / total-conversion** mod kinds (`-passivemods`, map-as-level,
  `ActiveTotalConversion`).
- Scheduled restarts, world-save **backups**, crash **watchdog**.
- Structured **config editor** + merge-safe INI writer.
- **QoL menu**: port-conflict detection, reachability (A2S) check, mod file-integrity check,
  size-cap warnings, UI-polish bundle, banlist sync, conflict scan.

## 3. Background: how ASA mods work (load-bearing constraints)

Verified from ark.wiki.gg, CurseForge docs, and host guides. These constrain the design:

- **Source of mods:** CurseForge only. The server itself downloads/updates mods **at startup**
  from CurseForge **Project IDs** passed via `-mods=<id1>,<id2>,...` (comma-separated,
  **no spaces**). Files land under `…\ShooterGame\Binaries\Win64\ShooterGame\Mods\83374\<id>`
  (`83374` = CurseForge game id for ASA).
- **Load order matters, but the override direction is undocumented.** Order affects
  dependency/override stacking, yet Wildcard has never published whether the left- or right-most
  ID wins a conflict. → The tool **must preserve admin-specified order verbatim and never
  auto-sort/normalize it.**
- **Updates apply at startup only** — a running server never hot-swaps. "Auto-update" therefore
  means **detect-then-restart**: `stop → (optional cache wipe) → start`, and the server re-pulls
  on boot.
- **Detecting a newer version requires the CurseForge REST API** (`api.curseforge.com/v1`) with
  an `x-api-key` (apply via CurseForge for Studios). Compare stored file `id`/`fileDate` against
  the newest applicable file. Without a key, detection is unavailable (degrade gracefully; the
  manual "restart to pull latest" still works blindly).
- **Missing/unreachable mod ⇒ crash on start**, even if previously installed. Resolve every ID
  before a (re)start; if CurseForge is unreachable, do **not** restart.
- **`-automanagedmods` is a no-op on ASA** (ASE/Steam leftover). Harmless if present; not the
  update mechanism.
- **Cluster consistency:** every cluster member must load the **same mod IDs in the same order**
  or transfers corrupt (a transferred modded item/dino whose class path is missing on the
  destination drops the item / crashes the join). Update the whole cluster together. (Clients
  must also match — the tool can only warn.)
- **Size caps** (host-cited, not a hard Wildcard spec): ~20 GB total crossplay / ~50 GB PC-only.
  (Warning surface deferred to a later phase.)

## 4. Architecture overview

Keep the repo's existing **code-behind WPF** style (no MVVM framework). Introduce a thin
**service layer** so the correctness-critical logic is testable and UI stays thin.

```
ASA-Manager/
  Models/
    ModEntry.cs                 // NEW: per-server mod intent
  Services/
    CurseForge/
      CurseForgeClient.cs       // NEW: api.curseforge.com/v1 client (x-api-key)
      ModMetadataCache.cs       // NEW: shared ProjectId -> resolved metadata (+ image cache)
      CurseForgeModels.cs       // NEW: DTOs
    Rcon/
      RconClient.cs             // NEW: Source RCON (TCP) protocol client
    Servers/
      ServerControlService.cs   // NEW: graceful stop / restart orchestration
  ServerConfigurationWindow.*   // MODIFIED: new mod editor
  CopyModsToServersWindow.*     // NEW: copy-to-servers/cluster dialog
  ServerWindow.*                // MODIFIED: graceful stop + "restart to apply" + "check updates"
  ASCTGlobalConfig.cs           // MODIFIED: CurseForge API key; RCON base port (exists)
  (ASCTServerConfig in ASCTGlobalConfig.cs) // MODIFIED: Mods list, RCON fields, arg builder, migration
ASA-Manager.Tests/             // NEW: xUnit project (pure-logic tests)
```

Services take no `System.Windows` dependency where avoidable, so the test project can exercise
them without a UI. `CurseForgeClient` takes an injectable `HttpMessageHandler` for testing.

## 5. Data model & migration

Replace the unordered set with an **ordered list of intent objects**. Display data is kept
**separately** in a shared cache, so names are resolved once and shown instantly (even offline).

```csharp
// Models/ModEntry.cs — persisted inside each ASCTServerConfig, order = load order
public class ModEntry : INotifyPropertyChanged
{
    public ulong ProjectId { get; set; }        // CurseForge Project ID
    public bool  Enabled   { get; set; } = true; // included in -mods= when true
}
```

`ASCTServerConfig`:

- Add `public List<ModEntry> Mods { get; set; } = new();` (canonical, ordered).
- Keep the legacy `modIDs` field readable for **one-time migration** only.
- On deserialize (`[OnDeserialized]`): if `Mods` is empty and legacy `modIDs` is non-empty,
  populate `Mods` from `modIDs` (de-duped, `Enabled = true`); clear the legacy field on next
  save. Migration is otherwise transparent — Newtonsoft already stored the set as a JSON array.
- Rebuild `ModArgs`:
  `Mods.Where(m => m.Enabled).Select(m => m.ProjectId)` → `-mods=<ids>` in **list order**,
  comma-joined, no spaces; empty ⇒ no `-mods` token.

**Shared metadata cache** (`Services/CurseForge/ModMetadataCache`), persisted as its own JSON
+ an image cache folder, keyed by `ProjectId`:

```
Name, Author, ThumbnailPath(local), Summary,
LatestFileId, LatestFileDate, FileLength, LastCheckedUtc
```

The editor joins `Mods` (intent) with the cache (display) at render time.

## 6. Feature: mod editor (`ServerConfigurationWindow`)

Replace the `lst_modIds` `ListBox` region. Bind to an `ObservableCollection<ModEntry>`.

- **Row template:** `[✓ Enabled] [thumbnail] Name (or "#<id>" if unresolved) · #id`.
- **Reorder:** drag-and-drop (multi-select) **and** Move Up / Move Down buttons. No auto-sort.
- **Add:** textbox accepts a numeric Project ID **or** a CurseForge URL (extract the ID — see
  §11 URL risk). **Bulk-add** control: multiline paste and `.txt` import; extract IDs, validate
  (numeric / non-empty / de-dupe) before adding.
- **Remove:** removes selected entries; optional checkbox *"also delete cached mod files under
  `…\Mods\83374\<id>`"* (fixes stale-cache crashes).
- **Copy / Cut / Paste:** internal `ModEntry` clipboard, order preserved, usable within a list
  and across servers.
- **Live preview:** keep the existing generated-launch-args preview wired to the new model.

*(A text filter is out of scope for Phase 1; if added later, reordering MUST be disabled while a
filter is active, since filtered indices don't map to true list indices.)*

## 7. Feature: copy mods to servers / cluster

New `CopyModsToServersWindow` opened from the mod editor ("Copy mods to…"):

- **Targets:** checklist of every other server, with **"select all in cluster _X_"** shortcuts.
- **Mode:** *Replace* (overwrite target's `Mods`) or *Merge* (append missing, de-dupe),
  both preserving order. Toggle: *include disabled entries*.
- **Apply:** update each target `ASCTServerConfig.Mods` and `Save()`. No INI writes — launch args
  are generated from config at start. Warn that targets which are **running** apply on next
  restart.

This is the cluster-safety tool (identical mods + order across members) and beats the reference
tool's one-target-at-a-time copy.

## 8. Feature: CurseForge metadata & update awareness

`CurseForgeClient` → `api.curseforge.com/v1`, `x-api-key` from settings (fallback env
`CURSEFORGE_API_KEY`):

- **Resolve names:** `POST /v1/mods` **batch** for all Project IDs at once → fill
  `ModMetadataCache` (name, author, logo → download+cache image, `latestFiles` →
  `LatestFileId`/`LatestFileDate`, `fileLength`). 24h TTL + manual **"Refresh metadata"**.
- **Update awareness:** on each **successful server start**, snapshot the current newest
  `LatestFileId` per installed mod as that server's *running versions*. **"Check for updates"**
  compares current newest file id (from the API) vs the snapshot and flags mods with a newer
  file, then offers **"Restart to apply"** (§9). No background polling in Phase 1.
- **Graceful degradation:** missing key / offline ⇒ rows show `#id`, update-check disabled with
  an explanatory tooltip; ordering, copy, and bulk-add work regardless. Batch calls + cache +
  back-off to respect (undocumented) rate limits.

## 9. Feature: graceful restart (minimal RCON)

Today `GameProcessManager.Stop()` hard-kills the process → save-corruption risk. Introduce RCON:

- **Enable RCON per server:** assign a unique RCON port (from `ASCTGlobalConfig.StartingRCONPort`
  + increment, verified not to collide with game/query ports) and an **auto-generated admin
  password** (stored per server; editable). Inject via launch-arg `?`-options on the map token:
  `"<Map>?…?RCONEnabled=True?RCONPort=<p>?ServerAdminPassword=<pw>"` — with
  **`ServerAdminPassword` strictly last in the `?`-chain** to avoid the known parse-swallow bug
  (dash args like `-port` follow the quoted map token and are safe). No INI writing required.
- **`RconClient`:** Source RCON over TCP (auth + `SERVERDATA_EXECCOMMAND`), used to send
  `SaveWorld`, `DoExit`, `Broadcast`.
- **`ServerControlService`:**
  - **Graceful stop** = `SaveWorld` → `DoExit` → wait for process exit; on timeout, fall back to
    force-kill with a clear warning.
  - **Restart to apply latest** = optional short broadcast → graceful stop → wait → start
    (server re-pulls mods on boot) → re-snapshot running versions.
  - Before a restart, resolve all mod IDs on CurseForge; refuse to restart if any no longer
    resolve or CurseForge is unreachable (crash-on-start guard).

This RCON foundation is reused by the later reliability phase.

## 10. Error handling

- **CurseForge:** no key → features disabled gracefully, editing never blocked; network errors →
  keep cached data, non-blocking status message.
- **RCON:** connect/auth failure (server not up yet, wrong password) → clear message + fall back
  to force-kill after timeout; never hang the flow.
- **Restart guard:** warn + cancel if a mod ID no longer resolves (crash risk) or CurseForge is
  unreachable.
- **Migration:** convert legacy `modIDs` once; de-dupe preserving first occurrence.
- **Copy-to-servers:** warn on running targets; changes apply next restart.
- **Port assignment:** ensure assigned RCON port is unique across servers and doesn't collide
  with a game/query port (full conflict detection is a later QoL item).

## 11. Correctness constraints (must respect)

1. **Never auto-sort/normalize mod order.** Preserve admin order verbatim; keep it identical
   across cluster members.
2. **Cluster consistency:** same mod IDs, same order on every member; update together.
3. **`-mods=` format:** comma-separated, no spaces; order = list order; `Enabled=false` excluded.
4. **`ServerAdminPassword` must be the last `?`-option** in the launch string.
5. **Missing/unreachable mod ⇒ crash on start:** validate before every (re)start; don't restart
   during a CurseForge outage.
6. **Updates are startup-only** — no hot-swap; version change = stop → (optional wipe) → start.
7. **Version detection needs an `x-api-key`;** page-scraping is a discouraged last resort.
8. **`-automanagedmods` is not the update mechanism** — don't rely on it.
9. **INI files are not written in Phase 1** (RCON goes via command line) — no risk of clobbering
   hand-edited INI keys yet; the later config editor must merge-not-overwrite.

## 12. Open questions & risks

- **CurseForge API key approval** is an external gate (could be slow/denied). Design runs without
  it (fallback to IDs + blind "restart to pull latest"). **Recommendation:** ship manual
  "restart to pull latest" as the always-available baseline; API detection is the enhancement.
- **URL → Project ID extraction is not trivial:** ASA CurseForge URLs are slug-based; the numeric
  Project ID isn't reliably in the URL. May require an API slug→id lookup rather than pure regex —
  verify during implementation; numeric-ID and bulk-paste paths don't depend on it.
- **RCON via `?`-options must be validated at runtime** (that ASA honors `RCONEnabled`/`RCONPort`/
  `ServerAdminPassword` as command-line `?`-options). Exercise end-to-end before relying on it;
  fallback is enabling RCON via GameUserSettings.ini (needs the deferred merge-safe INI writer).
- **Game id `83374`** is inferred-stable (from the on-disk path); re-verify via `GET /v1/games`
  before hardcoding without a fallback.
- **Running-version snapshot** assumes the server pulled the newest file at start; historically
  the auto-updater can be flaky around cross-play patches — the "force clean re-download" (wipe
  `Mods\83374\<id>`) is the mitigation and is exposed via the remove-with-wipe option.

## 13. Testing strategy

The repo has no tests. Add an **xUnit** project (`ASA-Manager.Tests`, `net9.0-windows`) and TDD
the pure logic:

- **Launch-arg generation:** mod order preserved, `Enabled` filter, empty-list case,
  `ServerAdminPassword`-last invariant, RCON options present when enabled.
- **Migration:** legacy `HashSet` JSON → ordered `List<ModEntry>`, de-dupe, idempotency.
- **RCON packet codec:** encode/decode against known byte sequences; auth handshake.
- **CurseForge client:** mock `HttpMessageHandler`; JSON parsing + update-diff (newer file id
  detected; equal/older ignored; missing mod handled).
- **Copy-to-servers:** replace vs merge/de-dupe, order preserved.

UI (WPF windows) stays thin and is validated by driving the app end-to-end (`/verify`).

## 14. Project setup (fork/repo)

- Working dir `/Users/lukas/ark-server-creation-tool` is a git repo: `main` tracks the upstream
  default branch; work happens on `feature/mod-management-overhaul`. `upstream` remote =
  Ragonz/Ark-Server-Creation-Tool for syncing.
- **Open item:** create the GitHub fork and add it as `origin` when ready to push (outward-facing;
  confirm with the user first). GPL-3.0 requires the fork stay source-available.

## 15. Roadmap (phases after this one)

1. **Reliability:** scheduled restarts (countdown broadcasts) + world-save & cluster-dir backups
   + crash detection/auto-restart — all on the Phase 1 RCON layer.
2. **Config editor:** structured GameUserSettings.ini / Game.ini editing with cluster-wide
   defaults + per-map overrides and a **merge-not-overwrite** INI writer.
3. **Mod power-ups:** auto-update watchdog, in-app CurseForge browse/search, passive/map/
   total-conversion mod kinds, size-cap warnings.
4. **QoL menu:** port-conflict detection, A2S reachability check, mod integrity check, UI polish.
