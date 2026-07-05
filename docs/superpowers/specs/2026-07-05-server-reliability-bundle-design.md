# Server Reliability Bundle — Design

**Date:** 2026-07-05
**Status:** Approved (brainstorming) → pending implementation plan
**Builds on:** Phase 1 (mod-management-overhaul) — the RCON layer (`ServerControl`, `RconClient`,
`ServerControlService`), `GameProcessManager`, and the `TransientStatus` / `StatusText` display fields.

## 1. Problem / Motivation

The tool can start, stop, and gracefully stop servers, but everything is **manual and reactive**:

- **No scheduled restarts** — ASA servers leak memory over uptime (RAM creep → lag → crashes);
  keeping them healthy means remembering to restart them by hand.
- **No backups** — the live `.ark` save is the *only* copy. Corruption (bad shutdown) or a bad change
  (griefing, a mod wiping a base, an admin mistake) is unrecoverable; there is no history to roll back to.
- **No crash recovery** — an unattended crash (common right after a game patch) leaves the server down
  until a human notices.
- **"Start All" cold-boots every server simultaneously** — a CPU/RAM spike on a shared host — and cannot
  skip a server you keep in the list but don't want running.
- **`AutoStartStaggerTime` is dead code** — the setting exists (default 30 s) but nothing reads it.

This bundle adds a **server-lifecycle / reliability layer** on top of the Phase 1 RCON foundation.

## 2. Scope

**In scope:**

1. Staggered bulk-start + per-server "exclude from bulk start".
2. In-app **scheduler** driving scheduled **restarts** (countdown → save → exit → relaunch) and scheduled **backups**.
3. Automated + manual **backups** (server / cluster / all) with **rotation**.
4. **Crash detection + throttled auto-restart**.

**Explicitly deferred (each its own later spec):**

- Guided in-app **restore** from a backup (for now: restore by copying a backup folder back — documented).
- Backup **compression** (zip) and incremental/differential backups.
- **Cron** expressions / day-of-week / monthly scheduling patterns.
- External **notifications** (Discord / email / webhook) on crash or give-up.
- **Hang detection** (process alive but frozen) — MVP detects process *exit* only.
- **OS-level** (Windows Task Scheduler) scheduling — the coordinator is in-app.

## 3. Architecture

**One in-app `ReliabilityCoordinator`, one heartbeat.** Created in `App.Application_Startup` after the
Server List window shows; lives for the app's lifetime. A `DispatcherTimer` ticks every **~15 s**. Each tick:

1. Evaluate every enabled `ScheduledTask`; run those that are due; stamp `LastRun`; save config.
2. Check each server for a crash and act per the crash policy.

*Alternative considered and rejected:* per-concern timers + OS `Process.Exited` events for instant crash
detection. ASA needs no sub-second crash detection (~15 s latency is negligible), and events are more
moving parts to wire and test. The single tick wins on simplicity and testability.

**Runtime model — in-app.** Reliability logic runs only while ASCT is open. The servers are separate
processes that keep running regardless; they are simply *unmanaged* (no scheduled actions, no crash
recovery) until ASCT is reopened. **Requirement:** keep ASCT running on the host for these features to act.

**Per-server runtime state** (in-memory; re-derived at app start; **not** persisted):

- `ShouldBeRunning` — set true when we start a server, false when we intentionally stop it. Initialized to
  the server's current `IsRunning` at app start.
- `OperationInProgress` — a guard set during graceful stop / scheduled restart / backup-save so those
  intentional actions are never mistaken for crashes.
- `CrashTimes` — a sliding list of recent crash timestamps for that server.
- `AutoRestartPaused` — set when the crash policy gives up on a server.

A **crash** is defined as `ShouldBeRunning && !OperationInProgress && !IsRunning`.

**Isolation / testability** (same pattern as Phase 1: pure logic is unit-tested; a thin I/O shell is
manually verified on Windows because WPF/process/file work can't run on the macOS dev box):

- **Pure units (unit-tested):** `ScheduleEvaluator`, `BackupRotation`, `CrashPolicy`, `StaggeredStarter`.
- **Shell (delegates to existing services):** `ReliabilityCoordinator` (timer + orchestration),
  `BackupService` (save-then-copy). These call the Phase 1 `ServerControl` / `GameProcessManager`.

## 4. Feature behavior

### 4.1 Staggered bulk-start + per-server exclude

- New per-server flag `ExcludeFromBulkStart` (default `false`) — a checkbox in the server config window.
- `StaggeredStarter.SelectServersToStart(servers)` drops excluded and already-running servers and preserves
  the list order.
- All three bulk-start paths — **Start All** (`ServerList`), **cluster start** (`ServerList` cluster prompt
  and `ServerWindow` "Start Cluster"), and **launch-time auto-start** (`App`) — start servers **sequentially**
  with `AutoStartStaggerTime` between each (async; reuses the "Starting…" `TransientStatus`).
- Launch-auto-start condition becomes `StartAutomatically && !ExcludeFromBulkStart` (exclude always wins).
- A server's **own** Start button ignores `ExcludeFromBulkStart` — the flag governs only *bulk* operations,
  so a dormant server is still manually startable.

### 4.2 Scheduler + scheduled restarts

- A persisted `List<ScheduledTask>`. `ScheduleEvaluator.IsDue(task, now)`:
  - `DailyAtTime`: due if `now.TimeOfDay >= DailyTime` **and** (`LastRun == null` or `LastRun.Date < now.Date`).
  - `EveryNHours`: due if `LastRun == null` or `now - LastRun >= IntervalHours` hours.
  - The coordinator sets `LastRun = now` immediately after firing (prevents double-fire within a tick window).
- When a **Restart** task fires, resolve the target's currently-running servers, then:
  - Broadcast at each `RestartWarningMinutes` lead time (default 15/10/5/1): `Broadcast "Server restarting in X minutes"`.
  - At T-0: `SaveWorld → DoExit` (graceful, via `ServerControl`), then relaunch. Cluster/All relaunch
    **staggered** (reuses `StaggeredStarter` + `AutoStartStaggerTime`).
  - `OperationInProgress` is set for the whole countdown+restart so the crash monitor ignores the intended downtime.
  - **Stopped targets are skipped** — a scheduled restart acts on running servers only (recovery is the
    crash monitor's job, not the scheduler's).

### 4.3 Backups

- `BackupService.BackupAsync(target)`:
  1. For each server in the target: if running, `SaveWorld` (best-effort, short RCON timeout; set `OperationInProgress`).
  2. Copy each server's `ShooterGame/Saved/SavedArks` → `BackupRoot/<label>/<timestamp>/<serverName>/`, where
     `<label>` is the server name (Server target), the cluster key (Cluster), or `"All"` (All), and `<timestamp>`
     is a sortable stamp such as `2026-07-05_04-00-00`.
  3. For **Cluster** / **All** targets, copy the shared cluster dir (`GlobalClusterDir`) **once** into
     `BackupRoot/<label>/<timestamp>/_cluster/`. A **single-server** backup copies only that server's saves
     (not the cluster dir).
  4. Apply rotation: `BackupRotation.ToDelete(existingTimestampFolders, BackupKeepCount)` → delete the oldest
     beyond the keep-count. Rotation runs only after a *successful* backup.
- The file copy runs off the UI thread. `BackupRoot` is configurable (recommend a separate physical drive so
  the copy doesn't contend with the live server's disk). ARK writes saves atomically, so a copy can't catch a
  half-written file.
- **Manual Backup Now** lives in the **server window** — backs up *this server*, or the *whole cluster* when
  the existing "entire cluster" checkbox is ticked (same pattern as Start/Stop). **Backup All** lives in the
  **Server List**. The Server List has no single-server backup (per-server actions belong in the server window).

### 4.4 Crash detection + throttled auto-restart

- Global toggle `CrashAutoRestartEnabled` (default `true`).
- Each tick, for every server currently classified as crashed:
  `CrashPolicy.Decide(recentCrashes, now, CrashThresholdCount, CrashWindowMinutes)` →
  - Prune crash times older than the window; if the remaining count (including this crash) reaches
    `CrashThresholdCount` (`count >= threshold`, default 3) → **GiveUp**: set `AutoRestartPaused`, status
    "Crashed — auto-restart paused", write to the reliability log. With the defaults, the 1st and 2nd crashes
    within 5 minutes each auto-restart and the 3rd pauses the server.
  - Else → **Restart**: after `CrashRestartBackoffSeconds`, relaunch the server (keeping `ShouldBeRunning = true`).
- **No per-server crash flag is needed:** `ShouldBeRunning` already encodes intent — a manually-stopped or
  dormant server is never `ShouldBeRunning`, so it is never auto-restarted.
- A server that runs stably past the window naturally resets (old crashes fall out of the sliding window).
- `AutoRestartPaused` clears when the user next starts that server manually.

## 5. Data model & config

**`ASCTServerConfig`** — add:

- `bool ExcludeFromBulkStart { get; set; } = false;`

**`ASCTGlobalConfig`** — add:

- `List<ScheduledTask> ScheduledTasks = new();`
- `string BackupRoot` (default `Path.Combine(Directory.GetCurrentDirectory(), "Backups")`)
- `int BackupKeepCount = 24;`
- `int[] RestartWarningMinutes = { 15, 10, 5, 1 };`
- `bool CrashAutoRestartEnabled = true;`
- `int CrashThresholdCount = 3;`
- `int CrashWindowMinutes = 5;`
- `int CrashRestartBackoffSeconds = 10;`
- `AutoStartStaggerTime` (already present, 30 s) — now read + exposed in the global config UI.

**`ScheduledTask`** (new, JSON-persisted):

```csharp
public class ScheduledTask
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public ScheduledTaskType Type { get; set; }        // Restart | Backup
    public ScheduleTargetKind TargetKind { get; set; } // Server | Cluster | All
    public int? TargetServerId { get; set; }           // when TargetKind == Server
    public string? TargetClusterKey { get; set; }      // when TargetKind == Cluster
    public ScheduleMode Mode { get; set; }             // DailyAtTime | EveryNHours
    public TimeSpan DailyTime { get; set; }            // when Mode == DailyAtTime
    public int IntervalHours { get; set; }             // when Mode == EveryNHours
    public DateTime? LastRun { get; set; }
}

public enum ScheduledTaskType { Restart, Backup }
public enum ScheduleTargetKind { Server, Cluster, All }
public enum ScheduleMode { DailyAtTime, EveryNHours }
```

## 6. Pure unit interfaces

```csharp
public static class ScheduleEvaluator
{
    public static bool IsDue(ScheduledTask task, DateTime now);
}

public static class BackupRotation
{
    // folderNames are timestamped (sortable) backup dir names for one target.
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> folderNames, int keepCount);
}

public enum CrashDecision { Restart, GiveUp }

public static class CrashPolicy
{
    // recentCrashes = that server's prior crash timestamps (this crash appended by caller or included as `now`).
    public static CrashDecision Decide(IReadOnlyList<DateTime> crashesInclNow, DateTime now,
                                       int thresholdCount, int windowMinutes);
}

public static class StaggeredStarter
{
    // Excludes ExcludeFromBulkStart and already-running servers; preserves input order.
    public static IReadOnlyList<ASCTServerConfig> SelectServersToStart(IEnumerable<ASCTServerConfig> servers);
}
```

## 7. UI surface

- **Server config window:** "Exclude from Start All / auto-start" checkbox (`ExcludeFromBulkStart`).
- **Server window:** **Backup Now** button (this server, or the whole cluster if "entire cluster" is ticked),
  alongside the existing controls.
- **Server List:** **Scheduled Tasks…** button (a dialog to add/edit/remove tasks — target dropdown, type,
  timing) and **Backup All**. The status column reuses `StatusText` to show *Restarting… / Backing up… /
  Crashed — auto-restart paused*.
- **Global config window:** stagger delay (`AutoStartStaggerTime`), backup root + keep-count, restart warning
  times, and the crash settings (enable toggle, threshold, window, backoff).
- **Reliability log:** a lightweight append-only text log (e.g. `ReliabilityLog.txt`) recording scheduled
  actions, backup results, and crash/restart/give-up events.

## 8. Error handling

- **Backup copy failure:** caught, logged, surfaced in status; the app does not crash; rotation runs only on
  a successful backup.
- **RCON save/broadcast failure during a scheduled restart:** proceed to the graceful stop, which already has
  a force-kill fallback in `ServerControlService`.
- **Scheduled task whose target no longer exists** (deleted server/cluster): skip and log; do not throw.
- **Tick robustness:** each task's execution is wrapped so one failure cannot kill the coordinator loop.
- **`LastRun` after firing:** advanced in memory immediately even if the subsequent `config.Save()` fails, so a
  transient save error cannot cause a double-fire.

## 9. Testing

- **xUnit (pure, runs on macOS + CI):**
  - `ScheduleEvaluator`: daily before/after the time, already-ran-today, across midnight; interval never-run,
    within-interval, past-interval.
  - `BackupRotation`: fewer-than / more-than / exactly keep-count.
  - `CrashPolicy`: under threshold → Restart; over threshold within window → GiveUp; stale crashes outside the
    window pruned → Restart.
  - `StaggeredStarter`: excludes flagged + already-running; preserves order.
- **Manual on Windows:** staggered Start All (observe spacing) + exclude skips the flagged server; a 2-minute
  scheduled restart (observe broadcasts → save → relaunch); Backup Now + Backup All + rotation deleting old
  snapshots; kill a server process → auto-restart; kill it repeatedly → give-up + paused status.

## 10. Performance

- **Idle tick:** negligible — a few date comparisons and a running-state check per server every 15 s.
- **Backup copy:** disk-I/O-bound while running; runs async/off-peak; separate-drive recommended; rotation caps
  disk usage; atomic saves prevent torn copies.
- **Restarts:** same cost as a manual restart; cluster/all relaunch is staggered to avoid simultaneous cold-boots.

## 11. Out of scope / future

Guided in-app restore; backup compression + incremental backups; cron / day-of-week scheduling; external
notifications on crash; hang detection; OS-level scheduling. Each is a candidate for its own later bundle.
