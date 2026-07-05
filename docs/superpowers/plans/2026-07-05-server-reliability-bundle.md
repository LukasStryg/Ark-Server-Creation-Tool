# Server Reliability Bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add scheduled restarts + backups (with rotation), crash detection with throttled auto-restart, and staggered bulk-start with a per-server exclude flag — all driven by one in-app coordinator over the existing RCON layer.

**Architecture:** A long-lived `ReliabilityCoordinator` (created in `App.Application_Startup`) runs a 15 s `DispatcherTimer` tick that fires due `ScheduledTask`s and detects crashes. The decision logic lives in small **pure** units (`StaggeredStarter`, `ScheduleEvaluator`, `BackupRotation`, `CrashPolicy`, `RestartCountdown`) that are unit-tested cross-platform; the I/O shell (`BackupService`, the coordinator, RCON/process calls) delegates to the Phase 1 `ServerControl` / `GameProcessManager` and is verified manually on Windows.

**Tech Stack:** C# / .NET 9 (`net9.0-windows7.0`, Nullable enabled), WPF (code-behind), Newtonsoft.Json 13.0.4, xUnit.

## Global Constraints

- Target framework `net9.0-windows7.0`; Nullable enabled — annotate reference types.
- Namespaces: models in `ARKServerCreationTool.Models`; reliability services in `ARKServerCreationTool.Services.Reliability`; tests in `ARKServerCreationTool.Tests`.
- Commits follow **Conventional Commits v1.0.0** (`feat:`, `test:`, `fix:`, `docs:`, `refactor:`).
- Build/test run on Windows only (WPF can't build on the macOS dev box). Each pure-logic task ends by asking the user to run `dotnet test "ARK Server Creation Tool.sln" -c Release`; shell/UI tasks end with an explicit manual-verification checklist.
- Work happens on branch `feature/server-reliability`. `main` is protected — land via PR.
- Reuse existing services; do **not** re-implement graceful stop or the RCON codec.
- Persisted config is `ASCTGlobalConfig.json` via Newtonsoft; new persisted fields must have safe defaults so old configs load.

---

## File Structure

**New — models:**
- `ASA-Manager/Models/ScheduledTask.cs` — `ScheduledTask` + enums `ScheduledTaskType`, `ScheduleTargetKind`, `ScheduleMode`.

**New — pure units (`ASA-Manager/Services/Reliability/`):**
- `StaggeredStarter.cs` — filter/order servers for bulk start.
- `ScheduleEvaluator.cs` — is a task due now?
- `BackupRotation.cs` — which backup folders to delete.
- `CrashPolicy.cs` — restart-or-give-up decision (+ `CrashDecision` enum).
- `RestartCountdown.cs` — broadcast messages + waits (+ `RestartStep` struct).
- `BackupPaths.cs` — pure destination-path helper.

**New — I/O shell (`ASA-Manager/Services/Reliability/`):**
- `ReliabilityLog.cs` — append-only text log.
- `BackupService.cs` — SaveWorld + copy saves/cluster dir + rotate.
- `ReliabilityCoordinator.cs` — the tick, per-server runtime state, schedule firing, crash handling.

**New — UI:**
- `ASA-Manager/ScheduledTasksWindow.xaml` (+ `.cs`) — add/edit/remove scheduled tasks.

**Modified:**
- `ASA-Manager/ASCTGlobalConfig.cs` — `ScheduledTasks` + backup/crash/warning fields; `ExcludeFromBulkStart` on `ASCTServerConfig`.
- `ASA-Manager/Services/Servers/ServerControlService.cs` — `BroadcastAsync`, `RestartWithCountdownAsync`.
- `ASA-Manager/App.xaml.cs` — create the coordinator; route launch auto-start through it (staggered).
- `ASA-Manager/ServerList.xaml(.cs)` — Start All staggered; **Scheduled Tasks…** + **Backup All** buttons.
- `ASA-Manager/ServerWindow.xaml(.cs)` — **Backup Now** button.
- `ASA-Manager/ServerConfigurationWindow.xaml(.cs)` — **Exclude from bulk start** checkbox.
- `ASA-Manager/ASCTConfigWindow.xaml(.cs)` — stagger/backup/crash settings.
- `ASA-Manager/AppServices.cs` — expose the coordinator singleton + a `BackupService` factory.

**New tests (`ASA-Manager.Tests/`):** `ScheduledTaskSerializationTests.cs`, `StaggeredStarterTests.cs`, `ScheduleEvaluatorTests.cs`, `BackupRotationTests.cs`, `CrashPolicyTests.cs`, `RestartCountdownTests.cs`, `BackupPathsTests.cs`, `RestartWithCountdownTests.cs`.

---

## Task 1: Data model — ScheduledTask, enums, config fields

**Files:**
- Create: `ASA-Manager/Models/ScheduledTask.cs`
- Modify: `ASA-Manager/ASCTGlobalConfig.cs` (add global fields; add `ExcludeFromBulkStart` to `ASCTServerConfig`)
- Test: `ASA-Manager.Tests/ScheduledTaskSerializationTests.cs`

**Interfaces:**
- Produces: `ScheduledTask { int Id; bool Enabled; ScheduledTaskType Type; ScheduleTargetKind TargetKind; int? TargetServerId; string? TargetClusterKey; ScheduleMode Mode; TimeSpan DailyTime; int IntervalHours; DateTime? LastRun }`; enums `ScheduledTaskType {Restart,Backup}`, `ScheduleTargetKind {Server,Cluster,All}`, `ScheduleMode {DailyAtTime,EveryNHours}`. New `ASCTGlobalConfig` fields: `List<ScheduledTask> ScheduledTasks`, `string BackupRoot`, `int BackupKeepCount`, `int[] RestartWarningMinutes`, `bool CrashAutoRestartEnabled`, `int CrashThresholdCount`, `int CrashWindowMinutes`, `int CrashRestartBackoffSeconds`. New `ASCTServerConfig.ExcludeFromBulkStart`.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/ScheduledTaskSerializationTests.cs`:

```csharp
using System;
using Newtonsoft.Json;
using ARKServerCreationTool.Models;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ScheduledTaskSerializationTests
    {
        [Fact]
        public void ScheduledTask_round_trips_through_json()
        {
            var task = new ScheduledTask
            {
                Id = 3,
                Enabled = true,
                Type = ScheduledTaskType.Restart,
                TargetKind = ScheduleTargetKind.Cluster,
                TargetClusterKey = "west",
                Mode = ScheduleMode.DailyAtTime,
                DailyTime = new TimeSpan(5, 0, 0),
                LastRun = null,
            };

            var json = JsonConvert.SerializeObject(task);
            var back = JsonConvert.DeserializeObject<ScheduledTask>(json)!;

            Assert.Equal(ScheduledTaskType.Restart, back.Type);
            Assert.Equal(ScheduleTargetKind.Cluster, back.TargetKind);
            Assert.Equal("west", back.TargetClusterKey);
            Assert.Equal(new TimeSpan(5, 0, 0), back.DailyTime);
            Assert.True(back.Enabled);
        }

        [Fact]
        public void New_task_defaults_are_enabled_with_no_lastrun()
        {
            var task = new ScheduledTask();
            Assert.True(task.Enabled);
            Assert.Null(task.LastRun);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `ScheduledTask` / enums do not exist (compile error).

- [ ] **Step 3: Create the model**

Create `ASA-Manager/Models/ScheduledTask.cs`:

```csharp
using System;

namespace ARKServerCreationTool.Models
{
    public enum ScheduledTaskType { Restart, Backup }
    public enum ScheduleTargetKind { Server, Cluster, All }
    public enum ScheduleMode { DailyAtTime, EveryNHours }

    /// <summary>A recurring reliability action (restart or backup) evaluated by the ReliabilityCoordinator.</summary>
    public class ScheduledTask
    {
        public int Id { get; set; }
        public bool Enabled { get; set; } = true;

        public ScheduledTaskType Type { get; set; }
        public ScheduleTargetKind TargetKind { get; set; }
        public int? TargetServerId { get; set; }        // when TargetKind == Server
        public string? TargetClusterKey { get; set; }   // when TargetKind == Cluster

        public ScheduleMode Mode { get; set; }
        public TimeSpan DailyTime { get; set; }         // when Mode == DailyAtTime
        public int IntervalHours { get; set; } = 6;     // when Mode == EveryNHours

        public DateTime? LastRun { get; set; }
    }
}
```

- [ ] **Step 4: Add the config fields**

In `ASA-Manager/ASCTGlobalConfig.cs`, add `using ARKServerCreationTool.Models;` if not present. Add these members to `ASCTGlobalConfig` (next to `CurseForgeApiKey`):

```csharp
public System.Collections.Generic.List<ScheduledTask> ScheduledTasks { get; set; } = new();

public string BackupRoot { get; set; } =
    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Backups");
public int BackupKeepCount { get; set; } = 24;

public int[] RestartWarningMinutes { get; set; } = { 15, 10, 5, 1 };

public bool CrashAutoRestartEnabled { get; set; } = true;
public int CrashThresholdCount { get; set; } = 3;
public int CrashWindowMinutes { get; set; } = 5;
public int CrashRestartBackoffSeconds { get; set; } = 10;
```

In `ASCTServerConfig` (next to `StartAutomatically`), add:

```csharp
public bool ExcludeFromBulkStart { get; set; } = false;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS (all previous 40 tests + the 2 new ones).

- [ ] **Step 6: Commit**

```bash
git add "ASA-Manager/Models/ScheduledTask.cs" "ASA-Manager/ASCTGlobalConfig.cs" "ASA-Manager.Tests/ScheduledTaskSerializationTests.cs"
git commit -m "feat(reliability): add ScheduledTask model and reliability config fields"
```

---

## Task 2: StaggeredStarter (pure)

**Files:**
- Create: `ASA-Manager/Services/Reliability/StaggeredStarter.cs`
- Test: `ASA-Manager.Tests/StaggeredStarterTests.cs`

**Interfaces:**
- Consumes: `ASCTServerConfig` (`ExcludeFromBulkStart`, `IsRunning`).
- Produces: `StaggeredStarter.SelectServersToStart(IEnumerable<ASCTServerConfig> servers) : IReadOnlyList<ASCTServerConfig>` — drops servers with `ExcludeFromBulkStart`, preserves input order. Pure: reads only the flag. (Skipping already-running servers needs `ASCTServerConfig.IsRunning`, which requires the loaded config singleton, so the shell does that at runtime — not this pure unit.)

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/StaggeredStarterTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using ARKServerCreationTool;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class StaggeredStarterTests
    {
        // SelectServersToStart is pure — it filters only on the ExcludeFromBulkStart flag.
        private static ASCTServerConfig Srv(int id, bool exclude = false)
            => new ASCTServerConfig(id, (ushort)(7777 + id)) { ExcludeFromBulkStart = exclude };

        [Fact]
        public void Excludes_flagged_servers_and_preserves_order()
        {
            var a = Srv(0);
            var b = Srv(1, exclude: true);
            var c = Srv(2);

            var result = StaggeredStarter.SelectServersToStart(new[] { a, b, c });

            Assert.Equal(new[] { 0, 2 }, result.Select(s => s.ID).ToArray());
        }

        [Fact]
        public void Empty_input_yields_empty()
        {
            var result = StaggeredStarter.SelectServersToStart(new List<ASCTServerConfig>());
            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `StaggeredStarter` does not exist.

- [ ] **Step 3: Implement**

Create `ASA-Manager/Services/Reliability/StaggeredStarter.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure selection of which servers a bulk-start operation should launch, in order.</summary>
    public static class StaggeredStarter
    {
        // Pure: filters on ExcludeFromBulkStart only. Skipping already-running servers needs
        // ASCTServerConfig.IsRunning (which requires the loaded config singleton), so the caller
        // does that at runtime — see ReliabilityCoordinator.StartStaggeredAsync.
        public static IReadOnlyList<ASCTServerConfig> SelectServersToStart(IEnumerable<ASCTServerConfig> servers)
            => servers.Where(s => !s.ExcludeFromBulkStart).ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Reliability/StaggeredStarter.cs" "ASA-Manager.Tests/StaggeredStarterTests.cs"
git commit -m "feat(reliability): add StaggeredStarter server selection"
```

---

## Task 3: ScheduleEvaluator (pure)

**Files:**
- Create: `ASA-Manager/Services/Reliability/ScheduleEvaluator.cs`
- Test: `ASA-Manager.Tests/ScheduleEvaluatorTests.cs`

**Interfaces:**
- Consumes: `ScheduledTask` (`Mode`, `DailyTime`, `IntervalHours`, `LastRun`).
- Produces: `ScheduleEvaluator.IsDue(ScheduledTask task, DateTime now) : bool`.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/ScheduleEvaluatorTests.cs`:

```csharp
using System;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ScheduleEvaluatorTests
    {
        private static ScheduledTask Daily(TimeSpan at, DateTime? lastRun) => new ScheduledTask
        { Mode = ScheduleMode.DailyAtTime, DailyTime = at, LastRun = lastRun };

        private static ScheduledTask Interval(int hours, DateTime? lastRun) => new ScheduledTask
        { Mode = ScheduleMode.EveryNHours, IntervalHours = hours, LastRun = lastRun };

        [Fact]
        public void Daily_is_due_after_time_when_not_run_today()
        {
            var now = new DateTime(2026, 7, 5, 5, 1, 0);
            Assert.True(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), lastRun: null), now));
        }

        [Fact]
        public void Daily_is_not_due_before_time()
        {
            var now = new DateTime(2026, 7, 5, 4, 59, 0);
            Assert.False(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), lastRun: null), now));
        }

        [Fact]
        public void Daily_is_not_due_when_already_run_today()
        {
            var now = new DateTime(2026, 7, 5, 6, 0, 0);
            var ranAt = new DateTime(2026, 7, 5, 5, 0, 30);
            Assert.False(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), ranAt), now));
        }

        [Fact]
        public void Daily_is_due_again_next_day()
        {
            var now = new DateTime(2026, 7, 6, 5, 0, 30);
            var ranYesterday = new DateTime(2026, 7, 5, 5, 0, 30);
            Assert.True(ScheduleEvaluator.IsDue(Daily(new TimeSpan(5, 0, 0), ranYesterday), now));
        }

        [Fact]
        public void Interval_is_due_when_never_run()
        {
            Assert.True(ScheduleEvaluator.IsDue(Interval(6, lastRun: null), new DateTime(2026, 7, 5, 0, 0, 0)));
        }

        [Fact]
        public void Interval_is_not_due_within_window()
        {
            var now = new DateTime(2026, 7, 5, 3, 0, 0);
            var ranAt = new DateTime(2026, 7, 5, 0, 0, 0);
            Assert.False(ScheduleEvaluator.IsDue(Interval(6, ranAt), now));
        }

        [Fact]
        public void Interval_is_due_past_window()
        {
            var now = new DateTime(2026, 7, 5, 6, 1, 0);
            var ranAt = new DateTime(2026, 7, 5, 0, 0, 0);
            Assert.True(ScheduleEvaluator.IsDue(Interval(6, ranAt), now));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `ScheduleEvaluator` does not exist.

- [ ] **Step 3: Implement**

Create `ASA-Manager/Services/Reliability/ScheduleEvaluator.cs`:

```csharp
using System;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure "is this scheduled task due to run now?" logic. Caller stamps LastRun after firing.</summary>
    public static class ScheduleEvaluator
    {
        public static bool IsDue(ScheduledTask task, DateTime now)
        {
            switch (task.Mode)
            {
                case ScheduleMode.DailyAtTime:
                    if (now.TimeOfDay < task.DailyTime) return false;
                    return task.LastRun == null || task.LastRun.Value.Date < now.Date;

                case ScheduleMode.EveryNHours:
                    if (task.LastRun == null) return true;
                    return now - task.LastRun.Value >= TimeSpan.FromHours(Math.Max(1, task.IntervalHours));

                default:
                    return false;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Reliability/ScheduleEvaluator.cs" "ASA-Manager.Tests/ScheduleEvaluatorTests.cs"
git commit -m "feat(reliability): add ScheduleEvaluator due-check"
```

---

## Task 4: BackupRotation (pure)

**Files:**
- Create: `ASA-Manager/Services/Reliability/BackupRotation.cs`
- Test: `ASA-Manager.Tests/BackupRotationTests.cs`

**Interfaces:**
- Produces: `BackupRotation.ToDelete(IEnumerable<string> folderNames, int keepCount) : IReadOnlyList<string>` — sorts names descending (timestamped names sort chronologically), keeps newest `keepCount`, returns the rest.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/BackupRotationTests.cs`:

```csharp
using System.Linq;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class BackupRotationTests
    {
        // Folder names are sortable timestamps: yyyy-MM-dd_HH-mm-ss
        private static readonly string[] Folders =
        {
            "2026-07-05_01-00-00", "2026-07-05_02-00-00", "2026-07-05_03-00-00", "2026-07-05_04-00-00",
        };

        [Fact]
        public void Deletes_oldest_beyond_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 2);
            Assert.Equal(new[] { "2026-07-05_01-00-00", "2026-07-05_02-00-00" }, toDelete.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Deletes_none_when_under_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 10);
            Assert.Empty(toDelete);
        }

        [Fact]
        public void Deletes_none_at_exact_keep_count()
        {
            var toDelete = BackupRotation.ToDelete(Folders, keepCount: 4);
            Assert.Empty(toDelete);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `BackupRotation` does not exist.

- [ ] **Step 3: Implement**

Create `ASA-Manager/Services/Reliability/BackupRotation.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure retention policy: given timestamp-named backup folders, return which to delete.</summary>
    public static class BackupRotation
    {
        public static IReadOnlyList<string> ToDelete(IEnumerable<string> folderNames, int keepCount)
        {
            if (keepCount < 0) keepCount = 0;
            return folderNames
                .OrderByDescending(n => n)   // newest (largest timestamp string) first
                .Skip(keepCount)
                .ToList();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Reliability/BackupRotation.cs" "ASA-Manager.Tests/BackupRotationTests.cs"
git commit -m "feat(reliability): add BackupRotation retention policy"
```

---

## Task 5: CrashPolicy (pure)

**Files:**
- Create: `ASA-Manager/Services/Reliability/CrashPolicy.cs`
- Test: `ASA-Manager.Tests/CrashPolicyTests.cs`

**Interfaces:**
- Produces: `enum CrashDecision { Restart, GiveUp }`; `CrashPolicy.Decide(IReadOnlyList<DateTime> crashesInclNow, DateTime now, int thresholdCount, int windowMinutes) : CrashDecision`. `crashesInclNow` includes the just-recorded crash; crashes older than the window are ignored; `GiveUp` when the in-window count reaches `thresholdCount`.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/CrashPolicyTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class CrashPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 7, 5, 12, 0, 0);

        [Fact]
        public void Restarts_when_first_crash()
        {
            var decision = CrashPolicy.Decide(new List<DateTime> { Now }, Now, thresholdCount: 3, windowMinutes: 5);
            Assert.Equal(CrashDecision.Restart, decision);
        }

        [Fact]
        public void Restarts_on_second_crash_in_window()
        {
            var crashes = new List<DateTime> { Now.AddMinutes(-2), Now };
            Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide(crashes, Now, 3, 5));
        }

        [Fact]
        public void Gives_up_on_third_crash_in_window()
        {
            var crashes = new List<DateTime> { Now.AddMinutes(-3), Now.AddMinutes(-1), Now };
            Assert.Equal(CrashDecision.GiveUp, CrashPolicy.Decide(crashes, Now, 3, 5));
        }

        [Fact]
        public void Ignores_crashes_older_than_window()
        {
            // two old crashes fell out of the 5-minute window; only the current one counts
            var crashes = new List<DateTime> { Now.AddMinutes(-30), Now.AddMinutes(-20), Now };
            Assert.Equal(CrashDecision.Restart, CrashPolicy.Decide(crashes, Now, 3, 5));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `CrashPolicy` does not exist.

- [ ] **Step 3: Implement**

Create `ASA-Manager/Services/Reliability/CrashPolicy.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    public enum CrashDecision { Restart, GiveUp }

    /// <summary>Pure throttle: give up once crashes within the sliding window reach the threshold.</summary>
    public static class CrashPolicy
    {
        public static CrashDecision Decide(IReadOnlyList<DateTime> crashesInclNow, DateTime now,
                                           int thresholdCount, int windowMinutes)
        {
            var cutoff = now - TimeSpan.FromMinutes(Math.Max(1, windowMinutes));
            int inWindow = crashesInclNow.Count(t => t >= cutoff);
            return inWindow >= Math.Max(1, thresholdCount) ? CrashDecision.GiveUp : CrashDecision.Restart;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Reliability/CrashPolicy.cs" "ASA-Manager.Tests/CrashPolicyTests.cs"
git commit -m "feat(reliability): add CrashPolicy throttle decision"
```

---

## Task 6: RestartCountdown (pure)

**Files:**
- Create: `ASA-Manager/Services/Reliability/RestartCountdown.cs`
- Test: `ASA-Manager.Tests/RestartCountdownTests.cs`

**Interfaces:**
- Produces: `struct RestartStep { string Message; TimeSpan WaitAfter }`; `RestartCountdown.Steps(int[] warningMinutes) : IReadOnlyList<RestartStep>`. Warning minutes are sorted descending, de-duplicated, positives only; each step's `WaitAfter` is the gap to the next warning (the last step waits its own minutes down to zero). The broadcast text is `"Server restarting in N minute(s)"`.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/RestartCountdownTests.cs`:

```csharp
using System;
using System.Linq;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RestartCountdownTests
    {
        [Fact]
        public void Builds_descending_steps_with_gaps_to_next_warning()
        {
            var steps = RestartCountdown.Steps(new[] { 5, 1 });

            Assert.Equal(2, steps.Count);
            Assert.Equal("Server restarting in 5 minutes", steps[0].Message);
            Assert.Equal(TimeSpan.FromMinutes(4), steps[0].WaitAfter); // 5 -> 1 is a 4-minute gap
            Assert.Equal("Server restarting in 1 minute", steps[1].Message);
            Assert.Equal(TimeSpan.FromMinutes(1), steps[1].WaitAfter); // last warning waits its own minutes
        }

        [Fact]
        public void Sorts_dedups_and_drops_non_positive()
        {
            var steps = RestartCountdown.Steps(new[] { 1, 5, 5, 0, -2, 10 });
            Assert.Equal(3, steps.Count);
            Assert.Equal("Server restarting in 10 minutes", steps[0].Message);
            Assert.Equal("Server restarting in 5 minutes", steps[1].Message);
            Assert.Equal("Server restarting in 1 minute", steps[2].Message);
        }

        [Fact]
        public void Empty_input_yields_no_steps()
        {
            Assert.Empty(RestartCountdown.Steps(Array.Empty<int>()));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `RestartCountdown` does not exist.

- [ ] **Step 3: Implement**

Create `ASA-Manager/Services/Reliability/RestartCountdown.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    public readonly struct RestartStep
    {
        public RestartStep(string message, TimeSpan waitAfter) { Message = message; WaitAfter = waitAfter; }
        public string Message { get; }
        public TimeSpan WaitAfter { get; }
    }

    /// <summary>Pure countdown plan: ordered broadcast messages and the wait before the next warning / the restart.</summary>
    public static class RestartCountdown
    {
        public static IReadOnlyList<RestartStep> Steps(int[] warningMinutes)
        {
            var minutes = (warningMinutes ?? Array.Empty<int>())
                .Where(m => m > 0).Distinct().OrderByDescending(m => m).ToList();

            var steps = new List<RestartStep>(minutes.Count);
            for (int i = 0; i < minutes.Count; i++)
            {
                int m = minutes[i];
                int next = i + 1 < minutes.Count ? minutes[i + 1] : 0;   // 0 = the restart itself
                var wait = TimeSpan.FromMinutes(m - next);
                steps.Add(new RestartStep(Message(m), wait));
            }
            return steps;
        }

        private static string Message(int minutes)
            => $"Server restarting in {minutes} minute{(minutes == 1 ? "" : "s")}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Reliability/RestartCountdown.cs" "ASA-Manager.Tests/RestartCountdownTests.cs"
git commit -m "feat(reliability): add RestartCountdown broadcast plan"
```

---

## Task 7: ServerControlService — Broadcast + countdown restart

**Files:**
- Modify: `ASA-Manager/Services/Servers/ServerControlService.cs`
- Test: `ASA-Manager.Tests/RestartWithCountdownTests.cs`

**Interfaces:**
- Consumes: `RestartStep` (Task 6), existing `RestartToApplyAsync`, `IRconClient`.
- Produces: `ServerControlService.BroadcastAsync(string message, CancellationToken ct = default) : Task` (opens a short-lived RCON connection, sends `Broadcast <message>`). `ServerControlService.RestartWithCountdownAsync(IReadOnlyList<RestartStep> steps, TimeSpan stopTimeout, Func<TimeSpan, CancellationToken, Task> delay, IProgress<string>? progress = null, CancellationToken ct = default) : Task<bool>` — for each step: broadcast, wait `delay(step.WaitAfter)`; then `RestartToApplyAsync`. Returns its result.

- [ ] **Step 1: Write the failing test**

Create `ASA-Manager.Tests/RestartWithCountdownTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Reliability;
using ARKServerCreationTool.Services.Servers;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RestartWithCountdownTests
    {
        private sealed class FakeRcon : IRconClient
        {
            public List<string> Commands = new();
            public Action? OnDoExit;   // in reality, DoExit terminates the server process
            public Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string> ExecuteAsync(string command, CancellationToken ct = default) { Commands.Add(command); if (command == "DoExit") OnDoExit?.Invoke(); return Task.FromResult("ok"); }
            public void Dispose() { }
        }

        private sealed class FakeProcess : IServerProcessController
        {
            public bool Running = true;
            public int StartCalls = 0;
            public bool IsRunning => Running;
            public bool Start() { StartCalls++; Running = true; return true; }
            public bool ForceStop() { Running = false; return true; }
        }

        [Fact]
        public async Task Broadcasts_each_step_then_saves_exits_and_restarts()
        {
            var rcon = new FakeRcon();
            var proc = new FakeProcess();
            rcon.OnDoExit = () => proc.Running = false;   // the server stops in response to DoExit, not before
            var svc = new ServerControlService(() => rcon, proc, "pw");
            var steps = new List<RestartStep>
            {
                new RestartStep("Server restarting in 1 minute", TimeSpan.FromMinutes(1)),
            };

            // The countdown delay is a no-op in the test; the process stays running until DoExit.
            Func<TimeSpan, CancellationToken, Task> noWait = (_, __) => Task.CompletedTask;

            var ok = await svc.RestartWithCountdownAsync(steps, TimeSpan.FromSeconds(2), noWait);

            Assert.True(ok);
            Assert.Contains("Broadcast Server restarting in 1 minute", rcon.Commands);
            Assert.Contains("SaveWorld", rcon.Commands);
            Assert.Contains("DoExit", rcon.Commands);
            Assert.Equal(1, proc.StartCalls);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `RestartWithCountdownAsync` does not exist.

- [ ] **Step 3: Implement**

In `ASA-Manager/Services/Servers/ServerControlService.cs`, add `using System.Collections.Generic;` and these two methods to the class:

```csharp
/// <summary>Opens a short-lived RCON connection and broadcasts a center-screen message. Best-effort.</summary>
public async Task BroadcastAsync(string message, CancellationToken ct = default)
{
    try
    {
        using var rcon = _rconFactory();
        if (await rcon.ConnectAndAuthenticateAsync(_adminPassword, ct))
            await rcon.ExecuteAsync($"Broadcast {message}", ct);
    }
    catch { /* best-effort warning; a failed broadcast must not abort the restart */ }
}

/// <summary>Broadcasts each countdown step (waiting between them via the injected delay), then restarts.</summary>
public async Task<bool> RestartWithCountdownAsync(IReadOnlyList<RestartStep> steps, TimeSpan stopTimeout,
    Func<TimeSpan, CancellationToken, Task> delay, IProgress<string>? progress = null, CancellationToken ct = default)
{
    foreach (var step in steps)
    {
        progress?.Report(step.Message);
        await BroadcastAsync(step.Message, ct);
        await delay(step.WaitAfter, ct);
    }
    return await RestartToApplyAsync(stopTimeout, progress, ct);
}
```

Add `using ARKServerCreationTool.Services.Reliability;` to the file's usings for `RestartStep`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "ASA-Manager/Services/Servers/ServerControlService.cs" "ASA-Manager.Tests/RestartWithCountdownTests.cs"
git commit -m "feat(reliability): add RCON broadcast and countdown restart"
```

---

## Task 8: BackupPaths (pure) + BackupService + ReliabilityLog (shell)

**Files:**
- Create: `ASA-Manager/Services/Reliability/BackupPaths.cs`, `ASA-Manager/Services/Reliability/ReliabilityLog.cs`, `ASA-Manager/Services/Reliability/BackupService.cs`
- Test: `ASA-Manager.Tests/BackupPathsTests.cs`

**Interfaces:**
- Produces:
  - `BackupPaths.Timestamp(DateTime now) : string` (`yyyy-MM-dd_HH-mm-ss`); `BackupPaths.SnapshotFolder(string backupRoot, string label, string timestamp) : string`.
  - `ReliabilityLog.Append(string message)` — appends a timestamped line to `ReliabilityLog.txt` in the working dir; best-effort.
  - `BackupService` (constructed with a `ServerControlService` factory + `ASCTGlobalConfig`): `Task BackupServerAsync(ASCTServerConfig server, string label, string timestampFolder, bool includeClusterDir, IProgress<string>? progress)`; `Task BackupTargetAsync(ScheduleTargetKind kind, IEnumerable<ASCTServerConfig> targetServers, string label, DateTime now, IProgress<string>? progress)` — copies each server's `SavedArks`, optionally the cluster dir, then applies rotation for `label`.

- [ ] **Step 1: Write the failing test (pure path helper)**

Create `ASA-Manager.Tests/BackupPathsTests.cs`:

```csharp
using System;
using System.IO;
using ARKServerCreationTool.Services.Reliability;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class BackupPathsTests
    {
        [Fact]
        public void Timestamp_is_sortable()
        {
            var ts = BackupPaths.Timestamp(new DateTime(2026, 7, 5, 4, 3, 2));
            Assert.Equal("2026-07-05_04-03-02", ts);
        }

        [Fact]
        public void Snapshot_folder_combines_root_label_timestamp()
        {
            var folder = BackupPaths.SnapshotFolder(Path.Combine("C:", "Backups"), "west", "2026-07-05_04-03-02");
            Assert.Equal(Path.Combine("C:", "Backups", "west", "2026-07-05_04-03-02"), folder);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: FAIL — `BackupPaths` does not exist.

- [ ] **Step 3: Implement `BackupPaths`**

Create `ASA-Manager/Services/Reliability/BackupPaths.cs`:

```csharp
using System;
using System.IO;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure helpers for backup destination paths and timestamp folder names.</summary>
    public static class BackupPaths
    {
        public static string Timestamp(DateTime now) => now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static string SnapshotFolder(string backupRoot, string label, string timestamp)
            => Path.Combine(backupRoot, label, timestamp);
    }
}
```

- [ ] **Step 4: Run the pure test to verify it passes**

Run: `dotnet test "ARK Server Creation Tool.sln" -c Release`
Expected: PASS.

- [ ] **Step 5: Implement `ReliabilityLog` (shell — no unit test; manual verify)**

Create `ASA-Manager/Services/Reliability/ReliabilityLog.cs`:

```csharp
using System;
using System.IO;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Best-effort append-only log for scheduled actions, backups, and crash events.</summary>
    public static class ReliabilityLog
    {
        private static readonly object _lock = new();
        public const string FileName = "ReliabilityLog.txt";

        public static void Append(string message)
        {
            try
            {
                lock (_lock)
                    File.AppendAllText(FileName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
```

- [ ] **Step 6: Implement `BackupService` (shell — copies files; manual verify on Windows)**

Create `ASA-Manager/Services/Reliability/BackupService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Servers;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Save-then-copy backups of server saves (+ shared cluster dir) with rotation.</summary>
    public class BackupService
    {
        private readonly ASCTGlobalConfig _config;
        public BackupService(ASCTGlobalConfig config) => _config = config;

        public async Task BackupTargetAsync(ScheduleTargetKind kind, IReadOnlyList<ASCTServerConfig> targetServers,
            string label, DateTime now, IProgress<string>? progress = null)
        {
            string timestamp = BackupPaths.Timestamp(now);
            bool includeClusterDir = kind != ScheduleTargetKind.Server;

            foreach (var server in targetServers)
                await BackupServerAsync(server, label, timestamp, progress);

            if (includeClusterDir)
                CopyDirectory(_config.GlobalClusterDir,
                    Path.Combine(BackupPaths.SnapshotFolder(_config.BackupRoot, label, timestamp), "_cluster"));

            ApplyRotation(label);
            ReliabilityLog.Append($"Backup complete: {label} ({timestamp})");
        }

        private async Task BackupServerAsync(ASCTServerConfig server, string label, string timestamp, IProgress<string>? progress)
        {
            progress?.Report($"Backing up {server.Name}...");
            if (server.IsRunning)
            {
                try { await ServerControl.For(server).BroadcastAsync("Backing up world..."); } catch { }
                try
                {
                    using var rcon = new Rcon.RconClient("127.0.0.1", server.RconPort);
                    if (await rcon.ConnectAndAuthenticateAsync(server.ServerAdminPassword))
                        await rcon.ExecuteAsync("SaveWorld");
                }
                catch { /* best-effort save; still copy whatever is on disk */ }
            }

            string savedArks = Path.Combine(server.GameDirectory, @"ShooterGame\Saved\SavedArks");
            string dest = Path.Combine(BackupPaths.SnapshotFolder(_config.BackupRoot, label, timestamp), server.Name);
            await Task.Run(() => CopyDirectory(savedArks, dest));
        }

        private void ApplyRotation(string label)
        {
            string labelDir = Path.Combine(_config.BackupRoot, label);
            if (!Directory.Exists(labelDir)) return;
            var folders = Directory.GetDirectories(labelDir).Select(Path.GetFileName)!.Where(n => n != null).Cast<string>();
            foreach (var stale in BackupRotation.ToDelete(folders, _config.BackupKeepCount))
            {
                try { Directory.Delete(Path.Combine(labelDir, stale), recursive: true); } catch { }
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }
}
```

Note: `ServerControl.For(server).BroadcastAsync(...)` requires Task 7. The `ServerControl.For` helper already exists.

- [ ] **Step 7: Commit**

```bash
git add "ASA-Manager/Services/Reliability/BackupPaths.cs" "ASA-Manager/Services/Reliability/ReliabilityLog.cs" "ASA-Manager/Services/Reliability/BackupService.cs" "ASA-Manager.Tests/BackupPathsTests.cs"
git commit -m "feat(reliability): add backup paths, log, and BackupService"
```

- [ ] **Step 8: Manual verification (Windows, after full wiring in later tasks)**

Deferred to Task 12's manual checklist (no UI entry point yet).

---

## Task 9: ReliabilityCoordinator (shell)

**Files:**
- Create: `ASA-Manager/Services/Reliability/ReliabilityCoordinator.cs`
- Modify: `ASA-Manager/AppServices.cs` (expose singletons)

**Interfaces:**
- Consumes: all pure units, `BackupService`, `ServerControl` / `ServerControlService`, `ASCTGlobalConfig`.
- Produces: `ReliabilityCoordinator` singleton with `Instance`, `Start()`, `NotifyStarted(int serverId)`, `NotifyStopping(int serverId)`, `NotifyStopped(int serverId)`, `MarkOperation(int serverId, bool inProgress)`; internal `TickAsync()`. `AppServices.Coordinator` and `AppServices.Backups()`.

- [ ] **Step 1: Implement the coordinator (shell; verified manually)**

Create `ASA-Manager/Services/Reliability/ReliabilityCoordinator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Servers;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>In-app heartbeat: fires due scheduled tasks and detects/handles crashes.</summary>
    public class ReliabilityCoordinator
    {
        private sealed class State
        {
            public bool ShouldBeRunning;
            public bool OperationInProgress;
            public bool AutoRestartPaused;
            public readonly List<DateTime> CrashTimes = new();
        }

        private readonly ASCTGlobalConfig _config;
        private readonly BackupService _backups;
        private readonly Dictionary<int, State> _state = new();
        private DispatcherTimer? _timer;
        private bool _ticking;

        public ReliabilityCoordinator(ASCTGlobalConfig config, BackupService backups)
        {
            _config = config;
            _backups = backups;
        }

        public void Start()
        {
            foreach (var s in _config.Servers)
                _state[s.ID] = new State { ShouldBeRunning = s.IsRunning };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += async (_, __) => await TickAsync();
            _timer.Start();
        }

        private State StateFor(int id) => _state.TryGetValue(id, out var st) ? st : (_state[id] = new State());

        public void NotifyStarted(int serverId) { var s = StateFor(serverId); s.ShouldBeRunning = true; s.AutoRestartPaused = false; }
        public void NotifyStopping(int serverId) => StateFor(serverId).OperationInProgress = true;
        public void NotifyStopped(int serverId) { var s = StateFor(serverId); s.ShouldBeRunning = false; s.OperationInProgress = false; }
        public void MarkOperation(int serverId, bool inProgress) => StateFor(serverId).OperationInProgress = inProgress;

        private async Task TickAsync()
        {
            if (_ticking) return;   // never overlap ticks
            _ticking = true;
            try
            {
                var now = DateTime.Now;
                await RunDueSchedulesAsync(now);
                await DetectCrashesAsync(now);
            }
            catch (Exception ex) { ReliabilityLog.Append($"Tick error: {ex.Message}"); }
            finally { _ticking = false; }
        }

        private async Task RunDueSchedulesAsync(DateTime now)
        {
            foreach (var task in _config.ScheduledTasks.Where(t => t.Enabled).ToList())
            {
                if (!ScheduleEvaluator.IsDue(task, now)) continue;
                task.LastRun = now;            // stamp before running so a slow run can't double-fire
                try { _config.Save(); } catch { }
                try { await RunTaskAsync(task, now); }
                catch (Exception ex) { ReliabilityLog.Append($"Scheduled {task.Type} failed: {ex.Message}"); }
            }
        }

        private async Task RunTaskAsync(ScheduledTask task, DateTime now)
        {
            var (servers, label) = ResolveTarget(task);
            if (servers.Count == 0) { ReliabilityLog.Append($"Scheduled {task.Type}: no matching servers ({label})"); return; }

            if (task.Type == ScheduledTaskType.Backup)
            {
                await _backups.BackupTargetAsync(task.TargetKind, servers, label, now);
                return;
            }

            // Restart: only running servers; stagger the whole set.
            var running = servers.Where(s => s.IsRunning).ToList();
            var steps = RestartCountdown.Steps(_config.RestartWarningMinutes);
            foreach (var server in running)
            {
                MarkOperation(server.ID, true);
                try
                {
                    await ServerControl.For(server).RestartWithCountdownAsync(
                        steps, TimeSpan.FromSeconds(60), (d, ct) => Task.Delay(d, ct));
                    NotifyStarted(server.ID);
                    ReliabilityLog.Append($"Scheduled restart done: {server.Name}");
                }
                finally { MarkOperation(server.ID, false); }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _config.AutoStartStaggerTime)));
            }
        }

        private (List<ASCTServerConfig> servers, string label) ResolveTarget(ScheduledTask task) => task.TargetKind switch
        {
            ScheduleTargetKind.All => (_config.Servers.ToList(), "All"),
            ScheduleTargetKind.Cluster => (_config.Servers.Where(s => s.ClusterKey == task.TargetClusterKey).ToList(),
                                           string.IsNullOrEmpty(task.TargetClusterKey) ? "cluster" : task.TargetClusterKey!),
            _ => ResolveSingle(task),
        };

        private (List<ASCTServerConfig>, string) ResolveSingle(ScheduledTask task)
        {
            var s = _config.Servers.FirstOrDefault(x => x.ID == task.TargetServerId);
            return s == null ? (new List<ASCTServerConfig>(), "server")
                             : (new List<ASCTServerConfig> { s }, s.Name);
        }

        private async Task DetectCrashesAsync(DateTime now)
        {
            if (!_config.CrashAutoRestartEnabled) return;

            foreach (var server in _config.Servers)
            {
                var st = StateFor(server.ID);
                bool crashed = st.ShouldBeRunning && !st.OperationInProgress && !st.AutoRestartPaused && !server.IsRunning;
                if (!crashed) continue;

                st.CrashTimes.Add(now);
                var decision = CrashPolicy.Decide(st.CrashTimes, now, _config.CrashThresholdCount, _config.CrashWindowMinutes);
                if (decision == CrashDecision.GiveUp)
                {
                    st.AutoRestartPaused = true;
                    server.TransientStatus = "Crashed — auto-restart paused";
                    ReliabilityLog.Append($"Crash give-up: {server.Name} (paused)");
                    continue;
                }

                ReliabilityLog.Append($"Crash detected: {server.Name}; auto-restarting");
                MarkOperation(server.ID, true);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _config.CrashRestartBackoffSeconds)));
                    server.ProcessManager.Start();
                }
                finally { MarkOperation(server.ID, false); }
            }
        }
    }
}
```

- [ ] **Step 2: Expose singletons in `AppServices`**

In `ASA-Manager/AppServices.cs`, add (mirroring the existing `MetadataCache` / `CurseForge()` members):

```csharp
using ARKServerCreationTool.Services.Reliability;
// ...
private static BackupService? _backups;
public static BackupService Backups() => _backups ??= new BackupService(ASCTGlobalConfig.Instance);

private static ReliabilityCoordinator? _coordinator;
public static ReliabilityCoordinator Coordinator => _coordinator ??=
    new ReliabilityCoordinator(ASCTGlobalConfig.Instance, Backups());
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build "ARK Server Creation Tool.sln" -c Release`
Expected: build succeeds (no test — the coordinator is shell, verified end-to-end in Task 12).

- [ ] **Step 4: Commit**

```bash
git add "ASA-Manager/Services/Reliability/ReliabilityCoordinator.cs" "ASA-Manager/AppServices.cs"
git commit -m "feat(reliability): add ReliabilityCoordinator tick and crash handling"
```

---

## Task 10: Staggered bulk-start wiring

**Files:**
- Modify: `ASA-Manager/ServerList.xaml.cs` (`btn_startAll_Click`, cluster-start branch in `btn_RunServer_Click`), `ASA-Manager/App.xaml.cs` (launch auto-start)

**Interfaces:**
- Consumes: `StaggeredStarter.SelectServersToStart`, `AppServices.Coordinator`, `ASCTGlobalConfig.AutoStartStaggerTime`.

- [ ] **Step 1: Add a shared staggered-start helper on the coordinator**

In `ReliabilityCoordinator`, add:

```csharp
/// <summary>Starts the given servers sequentially, spaced by the configured stagger delay, honoring the exclude flag.</summary>
public async Task StartStaggeredAsync(IEnumerable<ASCTServerConfig> servers, Action? onEach = null)
{
    var toStart = StaggeredStarter.SelectServersToStart(servers).Where(s => !s.IsRunning).ToList();
    for (int i = 0; i < toStart.Count; i++)
    {
        var s = toStart[i];
        s.TransientStatus = "Starting…";
        s.ProcessManager.Start();
        NotifyStarted(s.ID);
        await ServerControl.SnapshotAfterStartAsync(s);
        s.TransientStatus = null;
        onEach?.Invoke();
        if (i < toStart.Count - 1)
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _config.AutoStartStaggerTime)));
    }
}
```

- [ ] **Step 2: Route `btn_startAll_Click` through it**

In `ASA-Manager/ServerList.xaml.cs`, replace `btn_startAll_Click` with:

```csharp
private async void btn_startAll_Click(object sender, RoutedEventArgs e)
{
    await AppServices.Coordinator.StartStaggeredAsync(config.Servers, UpdateList);
    config.Save();
    UpdateList();
}
```

- [ ] **Step 3: Route the cluster-start branch through it**

In `btn_RunServer_Click`, replace the cluster Run branch body:

```csharp
if (runButtonStatus == RunButtonStatus.Run)
{
    await AppServices.Coordinator.StartStaggeredAsync(serversInCluster, UpdateList);
    config.Save();
}
```

(Leave the Stop branch unchanged.)

- [ ] **Step 4: Route launch auto-start through it**

In `ASA-Manager/App.xaml.cs`, make `Application_Startup` `async void`, start the coordinator, and replace the auto-start loop:

```csharp
ServerList list = new ServerList();
list.Show();

AppServices.Coordinator.Start();

if (ASCTGlobalConfig.Instance.AllowAutomaticStart)
{
    var autoStart = ASCTGlobalConfig.Instance.Servers
        .Where(s => s.StartAutomatically && !s.ExcludeFromBulkStart);
    await AppServices.Coordinator.StartStaggeredAsync(autoStart);
}
```

Add `using System.Linq;` if missing.

- [ ] **Step 5: Build**

Run: `dotnet build "ARK Server Creation Tool.sln" -c Release`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add "ASA-Manager/Services/Reliability/ReliabilityCoordinator.cs" "ASA-Manager/ServerList.xaml.cs" "ASA-Manager/App.xaml.cs"
git commit -m "feat(reliability): staggered bulk-start honoring the exclude flag"
```

- [ ] **Step 7: Manual verification (Windows)**

- Mark one server "Exclude from Start All / auto-start" (added in Task 11) or set the flag in `ASCTGlobalConfig.json`; click **Start All** → excluded server is skipped, the rest start one at a time ~stagger seconds apart (watch the "Starting…" status walk down the list).
- Set a server `StartAutomatically=true` and not excluded → it auto-starts (staggered) when ASCT launches.

---

## Task 11: UI — exclude checkbox + global settings

**Files:**
- Modify: `ASA-Manager/ServerConfigurationWindow.xaml(.cs)` (exclude checkbox), `ASA-Manager/ASCTConfigWindow.xaml(.cs)` (stagger/backup/crash settings)

- [ ] **Step 1: Add the exclude checkbox to the server config window**

In `ASA-Manager/ServerConfigurationWindow.xaml`, near the existing "Automatic Start" checkbox, add:

```xml
<CheckBox x:Name="chk_excludeBulkStart" Content="Exclude from Start All / auto-start" Margin="0,4,0,0"/>
```

In `ServerConfigurationWindow.xaml.cs`: in the load method (where `chk_automaticStart.IsChecked` is set, ~line 64) add `chk_excludeBulkStart.IsChecked = targetServer.ExcludeFromBulkStart;`. In `UpdateServerObject` (where `serv.StartAutomatically` is set, ~line 220) add `serv.ExcludeFromBulkStart = chk_excludeBulkStart.IsChecked.Value;`.

- [ ] **Step 2: Add the reliability settings to the global config window**

In `ASA-Manager/ASCTConfigWindow.xaml`, add controls (place near the existing stagger/cluster fields):

```xml
<TextBox x:Name="txt_staggerSeconds" Width="60"/>            <!-- AutoStartStaggerTime -->
<TextBox x:Name="txt_backupRoot" Width="320"/>              <!-- BackupRoot -->
<TextBox x:Name="txt_backupKeep" Width="60"/>               <!-- BackupKeepCount -->
<CheckBox x:Name="chk_crashRestart" Content="Auto-restart crashed servers"/>
<TextBox x:Name="txt_crashThreshold" Width="40"/>           <!-- CrashThresholdCount -->
<TextBox x:Name="txt_crashWindow" Width="40"/>              <!-- CrashWindowMinutes -->
```

(Label each with a `TextBlock` in the surrounding layout, matching the window's existing style.)

In `ASCTConfigWindow.xaml.cs`, in the load method set each control from `ASCTGlobalConfig.Instance` (e.g. `txt_staggerSeconds.Text = cfg.AutoStartStaggerTime.ToString();`), and in the save handler parse them back with `ushort.TryParse` / `int.TryParse`, falling back to the current value on parse failure. Example for one field:

```csharp
if (ushort.TryParse(txt_staggerSeconds.Text, out var stagger)) cfg.AutoStartStaggerTime = stagger;
cfg.BackupRoot = string.IsNullOrWhiteSpace(txt_backupRoot.Text) ? cfg.BackupRoot : txt_backupRoot.Text.Trim();
if (int.TryParse(txt_backupKeep.Text, out var keep) && keep >= 1) cfg.BackupKeepCount = keep;
cfg.CrashAutoRestartEnabled = chk_crashRestart.IsChecked == true;
if (int.TryParse(txt_crashThreshold.Text, out var thr) && thr >= 1) cfg.CrashThresholdCount = thr;
if (int.TryParse(txt_crashWindow.Text, out var win) && win >= 1) cfg.CrashWindowMinutes = win;
```

- [ ] **Step 2b: Build**

Run: `dotnet build "ARK Server Creation Tool.sln" -c Release`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "ASA-Manager/ServerConfigurationWindow.xaml" "ASA-Manager/ServerConfigurationWindow.xaml.cs" "ASA-Manager/ASCTConfigWindow.xaml" "ASA-Manager/ASCTConfigWindow.xaml.cs"
git commit -m "feat(reliability): exclude-from-bulk-start checkbox and global reliability settings"
```

- [ ] **Step 4: Manual verification (Windows)**

- Toggle "Exclude from Start All / auto-start" on a server, save, reopen config → it persists.
- Edit stagger / backup root / keep-count / crash settings, save, reopen → they persist in `ASCTGlobalConfig.json`.

---

## Task 12: UI — Scheduled Tasks window, Backup buttons, coordinator lifecycle

**Files:**
- Create: `ASA-Manager/ScheduledTasksWindow.xaml(.cs)`
- Modify: `ASA-Manager/ServerList.xaml(.cs)` (Scheduled Tasks… + Backup All buttons), `ASA-Manager/ServerWindow.xaml(.cs)` (Backup Now button)

**Interfaces:**
- Consumes: `AppServices.Coordinator`, `AppServices.Backups()`, `ASCTGlobalConfig.ScheduledTasks`, `ScheduledTask`.

- [ ] **Step 1: Scheduled Tasks window**

Create `ASA-Manager/ScheduledTasksWindow.xaml` — a `DataGrid` bound to `ASCTGlobalConfig.Instance.ScheduledTasks` with columns for Enabled (checkbox), Type (combo: Restart/Backup), Target (combo: Server/Cluster/All + an id/key field), Mode (combo: DailyAtTime/EveryNHours), Time (a `TextBox` for `HH:mm` or an interval number), plus **Add** / **Remove** / **Save** buttons. Editing writes directly to the bound `ScheduledTask` objects.

Create `ScheduledTasksWindow.xaml.cs`:

```csharp
using System.Linq;
using System.Windows;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool
{
    public partial class ScheduledTasksWindow : Window
    {
        private ASCTGlobalConfig config = ASCTGlobalConfig.Instance;

        public ScheduledTasksWindow()
        {
            InitializeComponent();
            dg_tasks.ItemsSource = config.ScheduledTasks;
        }

        private int NextId() => config.ScheduledTasks.Count == 0 ? 1 : config.ScheduledTasks.Max(t => t.Id) + 1;

        private void btn_add_Click(object sender, RoutedEventArgs e)
        {
            config.ScheduledTasks.Add(new ScheduledTask { Id = NextId(), TargetKind = ScheduleTargetKind.All,
                Type = ScheduledTaskType.Restart, Mode = ScheduleMode.DailyAtTime });
            dg_tasks.Items.Refresh();
        }

        private void btn_remove_Click(object sender, RoutedEventArgs e)
        {
            if (dg_tasks.SelectedItem is ScheduledTask t) { config.ScheduledTasks.Remove(t); dg_tasks.Items.Refresh(); }
        }

        private void btn_save_Click(object sender, RoutedEventArgs e) { config.Save(); Close(); }
    }
}
```

- [ ] **Step 2: Add the Server List buttons**

In `ASA-Manager/ServerList.xaml`, add two buttons near Start All / Stop All:

```xml
<Button x:Name="btn_scheduledTasks" Content="Scheduled Tasks…" Click="btn_scheduledTasks_Click"/>
<Button x:Name="btn_backupAll" Content="Backup All" Click="btn_backupAll_Click"/>
```

In `ASA-Manager/ServerList.xaml.cs`:

```csharp
private void btn_scheduledTasks_Click(object sender, RoutedEventArgs e)
    => new ScheduledTasksWindow { Owner = this }.ShowDialog();

private async void btn_backupAll_Click(object sender, RoutedEventArgs e)
{
    btn_backupAll.IsEnabled = false;
    try
    {
        await AppServices.Backups().BackupTargetAsync(
            Models.ScheduleTargetKind.All, config.Servers.ToList(), "All", System.DateTime.Now);
        System.Windows.MessageBox.Show("Backup All complete.");
    }
    catch (System.Exception ex) { System.Windows.MessageBox.Show($"Backup failed: {ex.Message}"); }
    finally { btn_backupAll.IsEnabled = true; }
}
```

- [ ] **Step 3: Add the Backup Now button to the server window**

In `ASA-Manager/ServerWindow.xaml`, add near the process controls:

```xml
<Button x:Name="btn_backupNow" Content="Backup Now" Click="btn_backupNow_Click"/>
```

In `ASA-Manager/ServerWindow.xaml.cs`:

```csharp
private async void btn_backupNow_Click(object sender, RoutedEventArgs e)
{
    btn_backupNow.IsEnabled = false;
    try
    {
        if (chk_entireCluster.IsChecked == true)
        {
            var members = config.Servers.Where(s => s.ClusterKey == targetServer.ClusterKey).ToList();
            string label = string.IsNullOrEmpty(targetServer.ClusterKey) ? targetServer.Name : targetServer.ClusterKey;
            await AppServices.Backups().BackupTargetAsync(Models.ScheduleTargetKind.Cluster, members, label, System.DateTime.Now);
        }
        else
        {
            await AppServices.Backups().BackupTargetAsync(Models.ScheduleTargetKind.Server,
                new System.Collections.Generic.List<ASCTServerConfig> { targetServer }, targetServer.Name, System.DateTime.Now);
        }
        System.Windows.MessageBox.Show("Backup complete.");
    }
    catch (System.Exception ex) { System.Windows.MessageBox.Show($"Backup failed: {ex.Message}"); }
    finally { btn_backupNow.IsEnabled = true; }
}
```

- [ ] **Step 4: Notify the coordinator from existing start/stop paths**

So crash detection tracks intent, add coordinator notifications to the manual per-server paths:
- `ServerWindow.btn_start_Click`: after `targetServer.ProcessManager.Start()` (and each cluster member start) call `AppServices.Coordinator.NotifyStarted(id)`.
- `ServerWindow.btn_stop_Click` and `btn_forceStop_Click`: call `AppServices.Coordinator.NotifyStopping(id)` before the stop and `NotifyStopped(id)` after.
- `ServerList.btn_RunServer_Click` / `btn_stopAll_Click`: `NotifyStopping`/`NotifyStopped` around graceful stops. (Start paths already go through `StartStaggeredAsync`, which calls `NotifyStarted`.)

- [ ] **Step 5: Build**

Run: `dotnet build "ARK Server Creation Tool.sln" -c Release`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add "ASA-Manager/ScheduledTasksWindow.xaml" "ASA-Manager/ScheduledTasksWindow.xaml.cs" "ASA-Manager/ServerList.xaml" "ASA-Manager/ServerList.xaml.cs" "ASA-Manager/ServerWindow.xaml" "ASA-Manager/ServerWindow.xaml.cs"
git commit -m "feat(reliability): scheduled-tasks window, backup buttons, coordinator notifications"
```

- [ ] **Step 7: Manual verification (Windows, end-to-end)**

- **Backup Now** (server window, cluster unchecked) → `BackupRoot/<serverName>/<timestamp>/<serverName>/` contains the SavedArks files. With "entire cluster" checked → members + `_cluster/` present.
- **Backup All** (list) → every server + `_cluster/` under `BackupRoot/All/<timestamp>/`. Run it more than `BackupKeepCount` times → oldest snapshots are deleted.
- **Scheduled restart:** add a task (All / Restart / EveryNHours=... or DailyAtTime a minute out; for a quick test temporarily set `RestartWarningMinutes` to `[1]`) → at the time, an in-game "Server restarting in 1 minute" broadcast appears, then save → exit → relaunch; `ReliabilityLog.txt` records it.
- **Scheduled backup:** add a Backup task due shortly → a snapshot appears at the interval.
- **Crash detection:** with `CrashAutoRestartEnabled` on, start a server, then kill `ArkAscendedServer.exe` in Task Manager → within ~15 s it auto-restarts and the log records it. Kill it 3 times within 5 minutes → the 3rd leaves status "Crashed — auto-restart paused" and stops restarting until you Start it manually.
- **Intent tracking:** gracefully **Stop** a server yourself → it must **not** be treated as a crash (no auto-restart).

---

## Self-Review

**Spec coverage:**
- §4.1 staggered start + exclude → Tasks 2, 10, 11. ✓
- §4.2 scheduler + scheduled restarts → Tasks 1, 3, 6, 7, 9, 12. ✓
- §4.3 backups + rotation → Tasks 4, 8, 12. ✓
- §4.4 crash detection + throttle → Tasks 5, 9, 12. ✓
- §5 data model/config → Task 1. ✓
- §6 pure interfaces → Tasks 2–6, 8. ✓
- §7 UI → Tasks 11, 12. ✓
- §8 error handling → coordinator try/catch (Task 9), best-effort log/broadcast/copy (Tasks 7, 8), `LastRun`-before-run (Task 9). ✓
- §9 testing → pure-unit tests in Tasks 1–8; manual checklists in Tasks 10–12. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The one alias slip (`ScheduleTargetKindAlias`) is called out inline with its correction (`Models.ScheduleTargetKind.Cluster`).

**Type consistency:** `BackupTargetAsync(ScheduleTargetKind, IReadOnlyList<ASCTServerConfig>, string, DateTime, IProgress<string>?)` used consistently in Tasks 8/9/12 (the `List<>` passed satisfies `IReadOnlyList<>`). `RestartWithCountdownAsync` signature matches between Tasks 7 and 9. `StaggeredStarter.SelectServersToStart` matches between Tasks 2 and 10. Coordinator `NotifyStarted/Stopping/Stopped/MarkOperation` used consistently in Tasks 9/10/12.
